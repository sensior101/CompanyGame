using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using CompanyGame.Daldongne;
using CompanyGame.Editor.Characters;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// QA of the characters in the server's world file (Docs/Multiplayer_Redesign.md 12.5).
///   -executeMethod CharacterQA.Run         in memory (with -quit): make → save → restart from the file → same character;
///                                          other accounts, name rules, Spawn without a character, the 48KB limit,
///                                          the clocks, two worlds, a broken world.dat read from world.prev.dat
///   -executeMethod CharacterQA.RunPlay     play mode (WITHOUT -quit; graphics for QA_SHOTS = folder, C_*.png), profile slot 4:
///                                          혼자 하기 → world → 캐릭터 만들기 (a taken name first) → game → change bank,
///                                          inventory and position → leave → 혼자 하기 again → all back; a new world starts fresh.
///                                          Also prints the content blob sizes.
///   -executeMethod CharacterQA.RunProcess  (with -quit) builds a Windows player to Builds/CharacterQA, LocalServerProcess
///                                          starts it in slot 4, a fake TCP client makes and saves a character, 끄기
///                                          (stop.request) → the last save is in world.dat; a restart serves it again
/// </summary>
[InitializeOnLoad]
public static class CharacterQA
{
    const string PlayFlag = "CharacterQA.play";
    const int Slot = 4;
    static int checks;
    static LoopbackTransport loopback;
    static GameServer server;
    static string root;

    static CharacterQA() => EditorApplication.playModeStateChanged += OnPlayState;

    /// <summary>Visual-only QA in disposable preview scenes; never opens a player, profile or inventory.</summary>
    public static void RunArticulatedPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play before character preview QA.");
        string output = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../ArtSource/Daldongne/Characters/MeshyArticulated_20261028/QA"));
        Directory.CreateDirectory(output);
        var report = new StringBuilder("Character articulated preview " + DateTime.Now.ToString("O") + "\n");
        var scene = SceneManager.GetActiveScene();
        bool dirty = scene.isDirty;
        try
        {
            PreviewArticulated(false, output, report);
            PreviewArticulated(true, output, report);
            PreviewRequire(SceneManager.GetActiveScene() == scene && scene.isDirty == dirty,
                "The user's active scene and dirty state must stay unchanged.");
            report.AppendLine("PASS: both visual prefabs, six body poses, both hands and first-person skin copies.");
            Debug.Log("[CharacterQA] articulated preview passed: " + output);
        }
        catch (Exception ex) { report.AppendLine("FAIL: " + ex); throw; }
        finally { File.WriteAllText(Path.Combine(output, "UnityPreviewReport.txt"), report.ToString()); }
    }

    static void PreviewArticulated(bool male, string output, StringBuilder report)
    {
        string sex = male ? "Male" : "Female";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(male ? ReferenceBoyImporter.VisualPath : ReferenceGirlImporter.VisualPath);
        PreviewRequire(prefab, sex + " visual prefab missing.");
        var preview = new PreviewRenderUtility();
        PlayerHeldItem held = null;
        GameObject visual = null;
        try
        {
            // Instantiate directly into the preview scene, never the user's scene.
            visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview.camera.scene);
            preview.AddSingleGO(visual);
            visual.SetActive(true);
            var motion = visual.GetComponent<DaldongneAvatarMotion>();
            PreviewRequire(motion && motion.HasHumanoidRig, sex + " needs a valid mapped Humanoid.");
            motion.ConfigureHumanoid(motion.rigAnimator);
            motion.enabled = false;
            report.AppendLine(sex + ": " + ReferenceGirlImporter.ValidateArticulated(visual));
            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                PreviewRequire(skin.bones.Length == 54 && skin.sharedMesh.bindposes.Length == 54,
                    sex + " must retain its 54-bone skin.");
                skin.updateWhenOffscreen = true;
            }
            var camera = preview.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.22f, .25f, .29f, 1f);
            camera.nearClipPlane = .01f; camera.farClipPlane = 30f;
            preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            preview.lights[0].intensity = 1.2f;
            preview.lights[1].transform.rotation = Quaternion.Euler(20f, 140f, 0f);
            preview.lights[1].intensity = .8f;
            preview.ambientColor = new Color(.55f, .55f, .55f);

            foreach (string pose in new[] { "Idle", "Walk", "Seated", "Cycling", "Fist", "Point" })
            {
                motion.SetCycling(false, 0f); motion.SetSeated(false);
                motion.SetFingerPose(false, 0f); motion.SetFingerPose(true, 0f);
                if (pose == "Walk") for (int i = 0; i < 17; i++) motion.Pose(3f, 1f / 60f);
                else if (pose == "Seated") motion.SetSeated(true);
                else if (pose == "Cycling") motion.SetCycling(true, .7f);
                else if (pose == "Fist" || pose == "Point")
                {
                    motion.SetFingerPose(false, 1f, pose == "Point");
                    motion.SetFingerPose(true, 1f, pose == "Point");
                }
                var bounds = PreviewBakedBounds(visual, sex + pose);
                camera.cullingMask = ~(1 << 29);
                PreviewFrame(camera, bounds, new Vector3(.8f, .25f, 1f), 900f / 1100f);
                PreviewSave(preview, Path.Combine(output, "Unity" + sex + pose + ".png"), 900, 1100);
                if (pose == "Fist" || pose == "Point")
                    foreach (bool right in new[] { false, true })
                    {
                        var handBounds = PreviewHandBounds(motion, right);
                        PreviewFrame(camera, handBounds, new Vector3(right ? 1f : -1f, .3f, 1f), 1f);
                        PreviewSave(preview, Path.Combine(output, "Unity" + sex + (right ? "Right" : "Left") + pose + ".png"), 800, 800);
                    }
                report.AppendLine(sex + pose + ": finite baked vertices; bounds " + bounds.size.ToString("F3"));
            }

            // Use the real private build/sync path on a disabled, disposable component.
            // Start/LateUpdate are never called, so no inventory singleton is touched.
            motion.SetSeated(false); motion.SetCycling(false, 0f);
            motion.SetFingerPose(false, 0f); motion.SetFingerPose(true, 0f);
            held = visual.AddComponent<PlayerHeldItem>(); held.enabled = false;
            var rig = EditorUtility.CreateGameObjectWithHideFlags("QA First Person", HideFlags.HideAndDontSave).transform;
            rig.SetParent(camera.transform, false);
            PreviewSet(held, "visualSource", visual.transform);
            PreviewSet(held, "avatarMotion", motion);
            PreviewSet(held, "arm", motion.rightArm);
            PreviewSet(held, "viewRig", rig);
            PreviewCall(held, "BuildArm");
            var copies = (Dictionary<Transform, Transform>)PreviewGet(held, "viewBones");
            PreviewRequire(copies.Count > 0 && held.FirstPersonArmRoot, sex + " articulated hand copy missing.");
            foreach (var source in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                PreviewRequire(copies.TryGetValue(source.transform, out var copied), "Skin renderer transform was not remapped.");
                var copy = copied.GetComponent<SkinnedMeshRenderer>();
                PreviewRequire(copy && copy.sharedMesh, "The overlay must stay skinned.");
                var a = source.sharedMesh; var b = copy.sharedMesh;
                PreviewRequire(a.vertexCount == b.vertexCount && a.bindposes.Length == b.bindposes.Length, "Skin vertex streams/bind poses changed.");
                for (int i = 0; i < a.bindposes.Length; i++) PreviewRequire(a.bindposes[i] == b.bindposes[i], "Skin bind pose changed.");
                var aw = a.GetAllBoneWeights(); var bw = b.GetAllBoneWeights();
                PreviewRequire(aw.Length == bw.Length, "Skin influence count changed.");
                for (int i = 0; i < aw.Length; i++)
                    PreviewRequire(aw[i].boneIndex == bw[i].boneIndex && Mathf.Abs(aw[i].weight - bw[i].weight) < .00001f, "Skin weight changed.");
                long triangles = 0;
                for (int i = 0; i < b.subMeshCount; i++) triangles += b.GetIndexCount(i) / 3;
                PreviewRequire(triangles > 0 && triangles < a.triangles.Length / 3, "Overlay must contain only an arm triangle subset.");
            }
            foreach (bool point in new[] { false, true })
            {
                motion.SetFingerPose(true, 1f, point);
                motion.ApplyRightArmInteraction(0f, 0f, 0f);
                PreviewCall(held, "SynchronizeSkinnedArm");
                int matched = 0;
                for (int i = 0; i < 15; i++)
                {
                    var source = motion.rigAnimator.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.RightThumbProximal + i));
                    PreviewRequire(source && copies.TryGetValue(source, out var copy) &&
                        Quaternion.Angle(source.localRotation, copy.localRotation) < .01f &&
                        Vector3.Distance(source.localPosition, copy.localPosition) < .00001f,
                        "First-person finger pose differs from source bone " + i);
                    matched++;
                }
                PreviewRequire(motion.HasRightHandGesture, "An explicit empty-hand gesture must request first-person visibility.");
                camera.orthographic = false; camera.fieldOfView = 85f; camera.aspect = 1.28f;
                camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                camera.cullingMask = 1 << 29;
                var arm = held.FirstPersonArmRoot;
                arm.localScale = (Vector3)PreviewGet(held, "viewScale");
                arm.localRotation = Quaternion.Euler(-125f, -10f, -18f);
                Vector3 grip = (Vector3)PreviewGet(held, "handPoint");
                float halfHeight = Mathf.Tan(85f * Mathf.Deg2Rad * .5f) * .65f;
                arm.localPosition = new Vector3(halfHeight * 1.28f * .68f, halfHeight * -.62f, .65f)
                    - arm.localRotation * Vector3.Scale(arm.localScale, grip);
                rig.gameObject.SetActive(true);
                PreviewBakedBounds(arm.gameObject, sex + " first-person hand");
                PreviewSave(preview, Path.Combine(output, "Unity" + sex + "FirstPerson" + (point ? "Point" : "Fist") + ".png"), 1280, 1000);
                report.AppendLine(sex + " first-person " + (point ? "point" : "fist") + ": " + matched + " finger transforms match; weights/bind poses preserved.");
            }
            motion.SetFingerPose(true, 0f);
            PreviewRequire(!motion.HasRightHandGesture, "Clearing the gesture must restore idle hand visibility policy.");
        }
        finally
        {
            // PlayerHeldItem normally releases runtime objects with Destroy. In an
            // editor preview release its owned meshes immediately before OnDestroy.
            try
            {
                if (held)
                {
                    foreach (string name in new[] { "articulatedArmMeshes", "bakedMeshes" })
                    {
                        var meshes = (List<Mesh>)PreviewGet(held, name);
                        foreach (var mesh in meshes) if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                        meshes.Clear();
                    }
                    var legacy = (Mesh)PreviewGet(held, "firstPersonArmMesh");
                    if (legacy) UnityEngine.Object.DestroyImmediate(legacy);
                    PreviewSet(held, "firstPersonArmMesh", null);
                    PreviewSet(held, "viewRig", null);
                }
            }
            finally { preview.Cleanup(); }
        }
    }

    static object PreviewGet(PlayerHeldItem held, string field) => typeof(PlayerHeldItem)
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(held);
    static void PreviewSet(PlayerHeldItem held, string field, object value) => typeof(PlayerHeldItem)
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(held, value);
    static void PreviewCall(PlayerHeldItem held, string method) => typeof(PlayerHeldItem)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(held, null);
    static void PreviewRequire(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("[Character preview] " + message); }

    static Bounds PreviewBakedBounds(GameObject visual, string label)
    {
        var result = new Bounds(); bool found = false;
        foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var baked = new Mesh();
            try
            {
                skin.BakeMesh(baked);
                PreviewRequire(baked.vertexCount > 0, label + " has no baked vertices.");
                foreach (var vertex in baked.vertices)
                {
                    var point = skin.transform.TransformPoint(vertex);
                    PreviewRequire(float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z), label + " has a non-finite baked vertex.");
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        PreviewRequire(found, label + " has no skin.");
        return result;
    }

    static Bounds PreviewHandBounds(DaldongneAvatarMotion motion, bool right)
    {
        var hand = right ? motion.rightHand : motion.leftHand;
        var bounds = new Bounds(hand.position, Vector3.one * .03f);
        int first = (int)(right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
        for (int i = 0; i < 15; i++) bounds.Encapsulate(motion.rigAnimator.GetBoneTransform((HumanBodyBones)(first + i)).position);
        bounds.Expand(.08f);
        return bounds;
    }

    static void PreviewFrame(Camera camera, Bounds bounds, Vector3 direction, float aspect)
    {
        camera.orthographic = true; camera.aspect = aspect;
        camera.transform.position = bounds.center + direction.normalized * Mathf.Max(2f, bounds.size.magnitude * 2f);
        camera.transform.LookAt(bounds.center);
        float width = .01f, height = .01f;
        for (int corner = 0; corner < 8; corner++)
        {
            var offset = Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1,
                (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
            width = Mathf.Max(width, Mathf.Abs(Vector3.Dot(camera.transform.right, offset)));
            height = Mathf.Max(height, Mathf.Abs(Vector3.Dot(camera.transform.up, offset)));
        }
        camera.orthographicSize = Mathf.Max(height, width / aspect) * 1.12f;
    }

    static void PreviewSave(PreviewRenderUtility preview, string path, int width, int height)
    {
        Texture2D image = null;
        bool begun = false;
        var previous = RenderTexture.active;
        var proxies = new List<(SkinnedMeshRenderer source, bool hidden, GameObject proxy, Mesh mesh)>();
        try
        {
            // Edit-mode GPU skinning can keep the import pose even after direct
            // bone changes. Render the same current CPU-baked deformation instead.
            foreach (var rootObject in preview.camera.scene.GetRootGameObjects())
                foreach (var skin in rootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || skin.forceRenderingOff || !skin.gameObject.activeInHierarchy ||
                        (preview.camera.cullingMask & (1 << skin.gameObject.layer)) == 0) continue;
                    var mesh = new Mesh { name = "Character QA posed skin", hideFlags = HideFlags.HideAndDontSave };
                    var proxy = EditorUtility.CreateGameObjectWithHideFlags("QA posed skin", HideFlags.HideAndDontSave);
                    proxies.Add((skin, skin.forceRenderingOff, proxy, mesh));
                    skin.BakeMesh(mesh);
                    proxy.layer = skin.gameObject.layer;
                    proxy.transform.SetParent(skin.transform, false);
                    proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = proxy.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = skin.sharedMaterials;
                    renderer.shadowCastingMode = skin.shadowCastingMode;
                    renderer.receiveShadows = skin.receiveShadows;
                    skin.forceRenderingOff = true;
                }
            preview.BeginStaticPreview(new Rect(0, 0, width, height)); begun = true;
            // Render(true) uses the current SRP and preview lighting, then Camera.Render.
            preview.Render(true, false);
            image = preview.EndStaticPreview(); begun = false;
            PreviewRequire(image, "Preview rendering produced no image.");
            File.WriteAllBytes(path, image.EncodeToPNG());
            var pixels = image.GetPixels32();
            var background = pixels[0]; int visible = 0;
            foreach (var pixel in pixels)
                if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 24) visible++;
            PreviewRequire(visible > pixels.Length / 1000, "The preview is blank: " + path);
        }
        finally
        {
            try { if (begun) { var unfinished = preview.EndStaticPreview(); if (unfinished) UnityEngine.Object.DestroyImmediate(unfinished); } }
            finally
            {
                if (image) UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.active = previous;
                foreach (var proxy in proxies)
                {
                    if (proxy.source) proxy.source.forceRenderingOff = proxy.hidden;
                    if (proxy.proxy) UnityEngine.Object.DestroyImmediate(proxy.proxy);
                    if (proxy.mesh) UnityEngine.Object.DestroyImmediate(proxy.mesh);
                }
            }
        }
    }

    // ================= in memory =================

    public static void Run()
    {
        checks = 0;
        root = Path.Combine(Path.GetTempPath(), "CharacterQA-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            Memory();
            Debug.Log("[CharacterQA] RESULT passed, checks=" + checks);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    static void Memory()
    {
        string w1 = WorldStore.NewId();
        StartServer(w1);
        var a = Join("acc-a");
        Check(!a.has && a.name == "", "a new account hears 'no character' right after Welcome");
        a.Send(SyncProtocol.Spawn(SyncProtocol.MakeId(a.number, 1), "Net/Box", "m1", EntityFlags.None, new[] { new byte[] { 1 } }));
        Step(a);
        Check(server.EntityCount == 0 && a.client.IsOpen, "Spawn without a character is ignored (the connection stays)");
        Check(Create(a, "Abc_1", "male") == CharacterResult.Ok, "make a character");
        a.Send(SyncProtocol.Spawn(SyncProtocol.MakeId(a.number, 2), "Net/Box", "m1", EntityFlags.None, new[] { new byte[] { 1 } }));
        Step(a);
        Check(server.EntityCount == 1, "with a character Spawn works");
        var twin = new Peer(loopback.Connect());
        twin.Send(SyncProtocol.Hello("acc-a", "twin", "m1"));
        Step(twin, a);
        Check(twin.reject == RejectReason.AlreadyConnected && !twin.client.IsOpen && !twin.known && server.PlayerCount == 1,
            "a second Hello of the same account → Reject(AlreadyConnected), no character sent");
        a.Send(SyncProtocol.Spawn(SyncProtocol.MakeId(a.number, 3), "Net/Box", "m1", EntityFlags.None, new[] { new byte[] { 1 } }));
        Step(a);
        Check(a.client.IsOpen && server.EntityCount == 2 && server.World.Find("acc-a").online, "the first connection of that account goes on");
        Check(Create(a, "Other", "male") == CharacterResult.AlreadyExists, "a second character for the same account → AlreadyExists");

        var b = Join("acc-b");
        Check(Create(b, "abc_1", "female") == CharacterResult.NameTaken && Create(b, "ABC_1", "male") == CharacterResult.NameTaken,
            "a name that differs only in case is taken");
        Check(Create(b, "x", "male") == CharacterResult.Invalid && Create(b, "이름<b>", "male") == CharacterResult.Invalid
              && Create(b, "가나다라마바사아자차카타파", "male") == CharacterResult.Invalid && Create(b, "Bcd", "other") == CharacterResult.Invalid
              && Create(b, "", "male") == CharacterResult.Invalid, "too short, <>, 13 characters, unknown gender, empty → Invalid");
        Check(Create(b, "Bcd", "female") == CharacterResult.Ok, "B makes its own character");

        var content = Bytes(1000, 1);
        a.Send(SyncProtocol.CharacterSave(content));
        var full = Bytes(SyncProtocol.MaxCharacter, 2);
        b.Send(SyncProtocol.CharacterSave(full));
        Step(a, b);
        Check(Same(server.World.Find("acc-a").content, content) && server.World.Find("acc-b").content.Length == SyncProtocol.MaxCharacter && b.client.IsOpen,
            "saves of 1000 bytes and exactly 48KB are kept");
        b.Send(SyncProtocol.Build((byte)ToServer.CharacterSave, w => { w.Write((uint)(SyncProtocol.MaxCharacter + 1)); w.Write(new byte[SyncProtocol.MaxCharacter + 1]); }));
        Step(a, b);
        Check(b.reject == RejectReason.Malformed && !b.client.IsOpen && a.client.IsOpen, "a save over 48KB → Reject(Malformed); the others go on");
        Check(Same(server.World.Find("acc-b").content, full), "the refused save changed nothing");
        bool threw = false;
        try { SyncProtocol.CharacterSave(new byte[SyncProtocol.MaxCharacter + 1]); } catch (InvalidDataException) { threw = true; }
        Check(threw, "the client-side builder refuses over 48KB too");

        var c = Join("acc-c");
        c.Send(SyncProtocol.CharacterSave(new byte[] { 1 }));
        Step(c);
        Check(server.World.Find("acc-c") == null && c.client.IsOpen, "a save without a character is ignored");
        c.client.Close();
        Step(a);

        // clocks: 60 real seconds = 12 game minutes (300 s per game hour)
        var chA = server.World.Find("acc-a");
        var chB = server.World.Find("acc-b");
        double world0 = server.World.WorldMinutes, a0 = chA.minutes, b0 = chB.minutes;
        server.AdvanceClock(60000);
        Step(a);
        Check(Near(chA.minutes - a0, 12) && Near(server.World.WorldMinutes - world0, 12), "60 s online: character +" + (chA.minutes - a0).ToString("F2") + ", world +" + (server.World.WorldMinutes - world0).ToString("F2") + " game minutes (12)");
        Check(chB.minutes == b0, "B was offline: its clock did not move");
        a.client.Close();
        Step();
        double aMinutes = chA.minutes, leftAt = server.World.WorldMinutes;
        Check(Near(chA.lastSeenWorldMinutes, leftAt), "leaving records the world clock (" + chA.lastSeenWorldMinutes.ToString("F2") + ")");
        server.AdvanceClock(120000);
        Step();
        Check(chA.minutes == aMinutes && Near(server.World.WorldMinutes - leftAt, 24), "120 s offline: the world +" + (server.World.WorldMinutes - leftAt).ToString("F2") + " (24), A's clock unchanged");
        var back = Join("acc-a");
        Check(back.has && back.name == "Abc_1" && back.reject == null && back.client.IsOpen, "after the first connection left, the same account gets in again with its character");
        back.client.Close();
        Step();
        aMinutes = chA.minutes;

        int saves = server.Saves;
        server.AdvanceClock(61000);
        Step();
        Check(server.Saves == saves + 1 && File.Exists(Path.Combine(root, w1, WorldStore.FileName)), "the world is saved every 60 seconds");
        double worldBefore = server.World.WorldMinutes;
        server.Stop();

        // restart: everything comes from the file
        StartServer(w1);
        var a2 = Join("acc-a");
        Check(a2.has && a2.name == "Abc_1" && a2.gender == "male" && Same(a2.content, content), "after a restart the same account gets the same character and content");
        Check(a2.minutes == (uint)aMinutes && a2.worldMinutes >= (uint)worldBefore, "the clocks came back (character " + a2.minutes + ", world " + a2.worldMinutes + ")");
        var d = Join("acc-d");
        Check(!d.has, "another account has no character");
        Check(Create(d, "BCD", "male") == CharacterResult.NameTaken, "names stay taken after the restart");
        server.Stop();

        // a second world does not mix with the first
        string w2 = WorldStore.NewId();
        StartServer(w2);
        var a3 = Join("acc-a");
        Check(!a3.has, "the same account in another world has no character");
        Check(Create(a3, "Abc_1", "female") == CharacterResult.Ok, "the same name is free in another world");
        var other = Bytes(77, 3);
        a3.Send(SyncProtocol.CharacterSave(other));
        Step(a3);
        server.Stop();
        var s1 = WorldStore.Open(root, w1);
        var s2 = WorldStore.Open(root, w2);
        Check(Same(s1.Find("acc-a").content, content) && s1.Find("acc-a").gender == "male" && s1.CharacterCount == 2
              && Same(s2.Find("acc-a").content, other) && s2.Find("acc-a").gender == "female" && s2.CharacterCount == 1, "the two world files keep their own characters");
        var list = WorldStore.List(root);
        Check(list.Count == 2 && list.Exists(s => s.id == w1 && s.characters == 2) && list.Exists(s => s.id == w2 && s.characters == 1), "the world list shows both with their character counts");

        // a broken world.dat is read from world.prev.dat
        string dat = Path.Combine(root, w1, WorldStore.FileName), prev = Path.Combine(root, w1, WorldStore.PrevName);
        Check(File.Exists(prev), "a save keeps the previous file as world.prev.dat");
        File.WriteAllBytes(dat, Bytes(500, 4));
        var fromPrev = WorldStore.Open(root, w1);
        Check(fromPrev.LoadedFromPrev && fromPrev.Find("acc-a")?.name == "Abc_1", "garbage world.dat → read from world.prev.dat");
        byte[] good = File.ReadAllBytes(prev);
        File.WriteAllBytes(dat, Sub(good, good.Length / 2));
        Check(WorldStore.Open(root, w1).LoadedFromPrev, "truncated world.dat → read from world.prev.dat");
        StartServer(w1);
        var a4 = Join("acc-a");
        Check(a4.has && Same(a4.content, content), "a server on that world serves the character from world.prev.dat");
        server.Stop();
        Check(!WorldStore.Open(root, w1).LoadedFromPrev, "its save wrote a good world.dat again");
        File.WriteAllBytes(dat, Bytes(300, 5));
        File.WriteAllBytes(prev, Bytes(300, 6));
        threw = false;
        try { WorldStore.Open(root, w1); } catch (InvalidDataException) { threw = true; }
        Check(threw && Same(File.ReadAllBytes(dat), Bytes(300, 5)), "both broken → Open refuses and leaves the files alone (no empty world over them)");
        Check(!WorldStore.List(root).Exists(s => s.id == w1), "an unreadable world is not offered in the list");
    }

    static void StartServer(string world)
    {
        loopback = new LoopbackTransport();
        server = new GameServer(loopback, 8, "QA", "", WorldStore.Open(root, world));
        server.Log += m => Debug.Log("[CharacterQA] server: " + m);
    }

    static Peer Join(string account)
    {
        var p = new Peer(loopback.Connect());
        p.Send(SyncProtocol.Hello(account, account, "m1"));
        Step(p);
        if (p.number == 0 || !p.known) throw new Exception("[CharacterQA] FAIL: " + account + " got no Welcome and Character");
        return p;
    }

    static CharacterResult Create(Peer p, string name, string gender)
    {
        p.result = null;
        p.Send(SyncProtocol.CharacterCreate(name, gender));
        Step(p);
        return p.result ?? throw new Exception("[CharacterQA] FAIL: no CharacterResult for " + name);
    }

    static void Step(params Peer[] peers)
    {
        server.Tick();
        foreach (var p in peers) p.Pump();
    }

    static bool Near(double a, double b) => Math.Abs(a - b) < .5;

    // The map tells the saves apart (12.10.4: the server keeps its own inventory).
    static byte[] Content(string map) =>
        CharacterSync.Encode(new CharacterSync.Saved { map = map, personal = PersonalSave.BaselinePersonal ?? "", inventory = "" });

    static string MapOf(byte[] content) => CharacterSync.Read(content, out var saved) ? saved.map : null;

    static byte[] Bytes(int n, int seed)
    {
        var bytes = new byte[n];
        new System.Random(seed).NextBytes(bytes);
        return bytes;
    }

    static byte[] Sub(byte[] a, int n)
    {
        var b = new byte[n];
        Array.Copy(a, b, n);
        return b;
    }

    static bool Same(byte[] a, byte[] b) => a != null && b != null && ((ReadOnlySpan<byte>)a).SequenceEqual(b);

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[CharacterQA] FAIL: " + what + Where());
        checks++;
        Debug.Log("[CharacterQA] ok: " + what);
    }

    /// <summary>A fake client that reads only what this QA needs (Welcome, Character, CharacterResult, Reject).</summary>
    sealed class Peer
    {
        public readonly IClientTransport client;
        public uint number, minutes, worldMinutes;
        public bool known, has;
        public string name = "", gender = "";
        public byte[] content;
        public CharacterResult? result;
        public RejectReason? reject;

        public Peer(IClientTransport client) { this.client = client; }

        public void Send(byte[] frame) => client.Send(frame);

        public void Pump()
        {
            while (client.TryReceive(out byte[] frame))
            {
                using var r = SyncProtocol.Reader(frame);
                switch ((ToClient)r.ReadByte())
                {
                    case ToClient.Welcome: number = r.ReadUInt32(); break;
                    case ToClient.Reject: reject = (RejectReason)r.ReadByte(); break;
                    case ToClient.CharacterResult: result = (CharacterResult)r.ReadByte(); SyncProtocol.End(r); break;
                    case ToClient.Character:
                        has = r.ReadByte() == 1; name = SyncProtocol.ReadStr(r); gender = SyncProtocol.ReadStr(r);
                        minutes = r.ReadUInt32(); worldMinutes = r.ReadUInt32(); content = SyncProtocol.ReadBlob32(r);
                        SyncProtocol.End(r);
                        known = true;
                        break;
                }
            }
        }
    }

    // ================= separate process =================

    public static void RunProcess()
    {
        checks = 0;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/CharacterQA"));
        string exe = Path.Combine(dir, "CharacterQA.exe");
        string scene = Array.Find(EditorBuildSettings.scenes, s => s.enabled).path; // one scene is enough for a server
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene }, locationPathName = exe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
        });
        Check(report.summary.result == BuildResult.Succeeded, "Windows player built to " + dir + " (" + report.summary.result + ")");

        int original = PlayerPrefs.GetInt("Profile.Slot", 1);
        Process process = null;
        try
        {
            ProfileSlot.Set(Slot);
            if (Directory.Exists(ServerSettings.Folder)) Directory.Delete(ServerSettings.Folder, true);
            LocalServerProcess.ExePath = exe;
            var settings = new ServerSettings { name = "QA 캐릭터 서버", maxPlayers = 4, port = FreePort(), world = WorldStore.NewId() };
            string log = Path.Combine(ServerSettings.Folder, "server.log");

            process = StartProcess(settings, log);
            var p = ConnectTcp(settings.port);
            p.Send(SyncProtocol.Hello("qa-proc", "P", "m1"));
            Wait(p, () => p.known, "Welcome and Character");
            Check(!p.has, "a new account in the process's new world has no character");
            p.Send(SyncProtocol.CharacterCreate("QA프로", "male"));
            Wait(p, () => p.result != null, "CharacterResult");
            Check(p.result == CharacterResult.Ok, "made a character in the server process");
            // Real content (12.10.1: the server's ledger drops what it cannot read); the map tells the saves apart.
            var last = Content("last");
            p.Send(SyncProtocol.CharacterSave(Content("first")));
            p.Send(SyncProtocol.CharacterSave(last)); // the last change before 끄기
            Thread.Sleep(300);

            var watch = Stopwatch.StartNew();
            Check(LocalServerProcess.Stop(), "끄기 (LocalServerProcess.Stop)");
            Check(process.HasExited && process.ExitCode == 0 && watch.ElapsedMilliseconds < 5000,
                "the process quit by itself in " + watch.ElapsedMilliseconds + " ms with exit code " + process.ExitCode + " (not killed)");
            Check(File.ReadAllText(log).Contains("stop requested"), "the server log says it saw stop.request");
            Check(!File.Exists(LocalServerProcess.RunningPath) && !File.Exists(LocalServerProcess.StopRequestPath), "running.json and stop.request are gone");
            var world = WorldStore.Open(ServerSettings.WorldsFolder, settings.world);
            Check(!world.LoadedFromPrev && MapOf(world.Find("qa-proc")?.content) == "last" && world.Find("qa-proc").name == "QA프로",
                "world.dat has the last save made right before 끄기");
            p.client.Close();

            process = StartProcess(settings, log);
            var again = ConnectTcp(settings.port);
            again.Send(SyncProtocol.Hello("qa-proc", "P", "m1"));
            Wait(again, () => again.known, "Welcome and Character after the restart");
            Check(again.has && again.name == "QA프로" && MapOf(again.content) == "last", "the restarted process serves the same character from world.dat");
            again.client.Close();
            Check(LocalServerProcess.Stop() && process.HasExited && process.ExitCode == 0, "끄기 again ends it cleanly");
            Debug.Log("[CharacterQA] RESULT process passed, checks=" + checks + " (server log: " + log + ")");
        }
        finally
        {
            if (process != null && !process.HasExited) process.Kill(); // only the process this QA started
            LocalServerProcess.ExePath = null;
            PlayerPrefs.SetInt("Profile.Slot", original);
            PlayerPrefs.Save();
        }
    }

    static Process StartProcess(ServerSettings settings, string log)
    {
        var process = LocalServerProcess.Start(settings);
        Check(process != null, "LocalServerProcess started the server");
        var until = DateTime.UtcNow.AddSeconds(60);
        while (LocalServerProcess.Running() == null)
        {
            if (process.HasExited) throw new Exception("[CharacterQA] FAIL: server process exited (" + process.ExitCode + "), see " + log);
            if (DateTime.UtcNow > until) throw new Exception("[CharacterQA] FAIL: TIMEOUT waiting for running.json");
            Thread.Sleep(200);
        }
        Check(LocalServerProcess.Running().world == settings.world, "running.json names the world " + settings.world);
        return process;
    }

    static Peer ConnectTcp(int port)
    {
        var client = new TcpClientTransport();
        client.Connect("127.0.0.1", port).GetAwaiter().GetResult();
        return new Peer(client);
    }

    static void Wait(Peer p, Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            p.Pump();
            if (condition()) return;
            if (DateTime.UtcNow > until) throw new Exception("[CharacterQA] FAIL: TIMEOUT waiting for " + what);
            Thread.Sleep(10);
        }
    }

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ================= play mode =================

    public static void RunPlay()
    {
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(PlayFlag, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayState(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PlayFlag, false)) return;
        SessionState.SetBool(PlayFlag, false);
        DrivePlay();
    }

    static async void DrivePlay()
    {
        int original = PlayerPrefs.GetInt("Profile.Slot", 1), code = 0;
        checks = 0;
        try { await PlayBody(); Debug.Log("[CharacterQA] RESULT play passed, checks=" + checks); }
        catch (Exception e) { Debug.LogError("[CharacterQA] RESULT play FAILED: " + e.Message + "\n" + e.StackTrace); code = 1; }
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static async Task PlayBody()
    {
        await WaitFor(() => OnTitle && !Screen.Busy, 15, "title");
        await WaitFor(() => PersonalSave.BaselinePersonal != null, 5, "the new-account baseline");
        Screen.UseCamera(Camera.main);
        // Slot 4 with its own personal state; its file is written once now, so any later change to it is a write while on the server.
        ProfileSlot.Set(Slot);
        if (Directory.Exists(ServerSettings.Folder)) Directory.Delete(ServerSettings.Folder, true);
        PersonalSave.ResetToFresh();
        PersonalSave.LoadFromDisk();
        PersonalSave.SaveToDisk();
        string personalFile = PersonalSave.Path;
        byte[] personalBefore = File.ReadAllBytes(personalFile);

        // A world where another account already took the name "Taken".
        string w1 = WorldStore.NewId();
        var seed = WorldStore.Open(ServerSettings.WorldsFolder, w1);
        seed.Create("qa-other-account", "Taken", "female", out _);
        seed.Save();

        long ownBank = BankManager.EnsureInstance().Money;
        string ownInventory = InventorySnapshot.Export(InventoryManager.Instance.State);
        long baseBank = JsonUtility.FromJson<PersonalSaveData>(PersonalSave.BaselinePersonal).bankBalance;

        Screen.Open("main");
        Check(HasButton("혼자 하기"), "the main menu has 혼자 하기");
        await Shot("C_main.png");
        await Click("혼자 하기");
        Check(Screen.Screen == "worlds" && HasButton("새 월드 만들기") && TextOf(Screen.Content).Contains("캐릭터 1명"), "혼자 하기 lists 새 월드 and the saved world (1 character)");
        await Shot("C_worlds.png");
        await Click("캐릭터 1명");
        await WaitFor(() => Screen && Screen.Screen == "name", 30, "캐릭터 만들기 for a world without my character");
        var host = SyncHost.Instance;
        Check(host && host.InApp && host.Server.MaxPlayers == 1 && host.Server.World.Id == w1, "혼자 하기 opened the in-app server for 1 player on the chosen world");
        var (fake, reject) = await FakeHello(host.Port);
        Check(reject == RejectReason.Full, "a second player is refused (Full)");
        fake.Close();
        await Shot("C_name.png");
        Inputs()[0].text = "taken";
        await Click("확인");
        await WaitFor(() => Screen && Screen.Screen == "name" && !Screen.Busy, 10, "the name screen again");
        Check(TextOf(Screen.Content).Contains("같은 이름") && Inputs()[0].text == "taken", "a name taken in this world (other case) is refused with a message, the typed name kept");
        await Shot("C_name_taken.png");
        Inputs()[0].text = "QA캐릭";
        await Click("확인");
        await WaitFor(() => InGame && CharacterSync.Active, 90, "entering the game as the new character");
        Check(GameSession.LocalPlayerName == "QA캐릭", "playing as QA캐릭");
        Check(BankManager.Instance.Money == baseBank && InventorySnapshot.Export(InventoryManager.Instance.State) == PersonalSave.BaselineInventory,
            "a new character starts with the new-account bank (" + baseBank + ") and inventory");

        Measure("new character");
        // change bank, inventory and position
        // The bank and the inventory are the server's ledger (12.10.1, 12.10.4); no request gives either on trust since
        // 12.10.6, so the in-app server's ledger is changed here and this screen the same way.
        var ledger = ServerWorld.Instance.Ledger;
        string me = GameSession.LocalPlayerId;
        long deposit = 1234567 - BankManager.Instance.Money;
        ledger.Book(me).bankBalance += deposit;
        Check(BankManager.Instance.AddMoney(deposit, MoneyChangeReason.BookRoyalty), "money of " + deposit);
        ItemData item = null;
        foreach (var i in Resources.LoadAll<ItemData>("Inventory")) if (i && !i.IsBook && InventorySnapshot.Find(i.itemId) == i) { item = i; break; }
        Check(item && ledger.Bag(me).TryAdd(item, 1, out _) && InventoryManager.Instance.State.TryAdd(item, 1, out _), "added " + (item ? item.itemId : "?") + " to the inventory");
        ledger.Store(me);
        int ledgers = WorldStateSync.Ledgers;
        await WaitFor(() => WorldStateSync.Ledgers > ledgers, 5, "the server's ledger after the layout");
        var player = PlayerSpawner.Player;
        player.spawn = player.transform.position + new Vector3(2.5f, 0f, 1.5f);
        player.ResetToSpawn();
        player.transform.rotation = Quaternion.Euler(0f, 77f, 0f);
        await Awaitable.WaitForSecondsAsync(.5f);
        Measure("after the change");
        MeasureBooks();
        string inventory = InventorySnapshot.Export(InventoryManager.Instance.State);
        Vector3 position = player.transform.position;

        TitleFlow.ReturnToTitle();
        await WaitFor(() => OnTitle && !Screen.Busy, 60, "title after leaving");
        await Awaitable.NextFrameAsync();
        Check(!SyncHost.Instance && !CharacterSync.Active, "leaving stopped the in-app server");
        var saved = WorldStore.Open(ServerSettings.WorldsFolder, w1).Find(GameSession.LocalPlayerId);
        Check(saved != null && saved.name == "QA캐릭" && CharacterSync.Read(saved.content, out var content)
              && content.personal.Contains("1234567") && content.inventory == inventory, "world.dat has QA캐릭 with the changed bank and inventory");
        Check(BankManager.Instance.Money == ownBank && InventorySnapshot.Export(InventoryManager.Instance.State) == ownInventory, "after leaving this PC's own bank and inventory are back");
        Check(Same(personalBefore, File.ReadAllBytes(personalFile)), "this PC's personal file was not written while on the server");

        await Click("혼자 하기");
        await Click("캐릭터 2명");
        await WaitFor(() => InGame && CharacterSync.Active || Screen && Screen.Screen == "name", 90, "entering the same world again");
        Check(InGame && GameSession.LocalPlayerName == "QA캐릭", "the same world again: straight in as QA캐릭 (no 캐릭터 만들기)");
        float moved = Vector3.Distance(PlayerSpawner.Player.transform.position, position);
        Check(BankManager.Instance.Money == 1234567 && InventorySnapshot.Export(InventoryManager.Instance.State) == inventory && moved < .3f,
            "bank 1234567, the inventory and the position (" + moved.ToString("F2") + " m off) are as left");
        TitleFlow.ReturnToTitle();
        await WaitFor(() => OnTitle && !Screen.Busy, 60, "title");

        await Click("혼자 하기");
        await Click("새 월드 만들기");
        Screen.FirstInput().text = "두번째 월드";
        await Click("만들기");
        await WaitFor(() => Screen && Screen.Screen == "name", 30, "캐릭터 만들기 in a new world");
        Inputs()[0].text = "QA캐릭"; // the same name is free in another world
        await Click("확인");
        await WaitFor(() => InGame && CharacterSync.Active, 90, "entering the new world");
        Check(BankManager.Instance.Money == baseBank && InventorySnapshot.Export(InventoryManager.Instance.State) == PersonalSave.BaselineInventory,
            "the new world starts from the new-account state, nothing of the other world");
        TitleFlow.ReturnToTitle();
        await WaitFor(() => OnTitle && !Screen.Busy, 60, "title");
        Check(WorldStore.List(ServerSettings.WorldsFolder).Count == 2, "two worlds in the list now");
        await Click("혼자 하기");
        Check(HasButton("두번째 월드") && HasButton(WorldName(w1)), "the list shows the new world by its name, the unnamed older one by its date");
        await Shot("C_worlds_named.png");
        await Click("뒤로");

        // 서버 열기 picks from the same worlds.
        await Click("서버 열기");
        Check(HasButton("월드: 새 월드"), "서버 열기 starts with 월드: 새 월드");
        await Click("월드:");
        Check(Screen.Screen == "worlds" && TextOf(Screen.Content).Contains("월드 선택"), "월드 (바꾸기) opens the world list");
        await Click("캐릭터 2명");
        Check(Screen.Screen == "serveropen" && HasButton("월드: " + WorldName(w1)), "the chosen world is shown on 서버 열기");
        Screen.UseCamera(Camera.main); // a new title screen since the first leave
        await Shot("C_server_world.png");
        await Click("뒤로");
    }

    static string WorldName(string id)
    {
        var s = WorldStore.List(ServerSettings.WorldsFolder).Find(w => w.id == id);
        return s.name.Length > 0 ? s.name : "월드 " + s.createdAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    // The content blob of the character as it is now: gzip size and the JSON inside.
    static void Measure(string when)
    {
        int blob = CharacterSync.Capture().Length;
        int json = Encoding.UTF8.GetByteCount(PersonalSave.Export()) + Encoding.UTF8.GetByteCount(InventorySnapshot.Export(InventoryManager.Instance.State));
        Debug.Log($"[CharacterQA] SIZE {when}: blob {blob} bytes (gzip), JSON inside {json} bytes, limit {SyncProtocol.MaxCharacter}");
    }

    // Worst case: published books full of random Hangul (hardly compresses), added until the blob passes 48KB. The bag is put back after.
    static void MeasureBooks()
    {
        var state = InventoryManager.Instance.State;
        string keep = InventorySnapshot.Export(state);
        ItemData bookItem = null;
        foreach (var i in Resources.LoadAll<ItemData>("Inventory")) if (i && i.IsBook) { bookItem = i; break; }
        if (!bookItem) { Debug.Log("[CharacterQA] SIZE books: no book item"); return; }
        var random = new System.Random(9);
        int pages = 0, books = 0, blob = CharacterSync.Capture().Length;
        while (blob <= SyncProtocol.MaxCharacter && books < 20)
        {
            var book = new BookInstanceData { title = "QA 책 " + books, authorName = "QA", isPublished = true };
            for (int p = 0; p < BookInstanceData.PageCount && blob <= SyncProtocol.MaxCharacter; p++)
            {
                var sb = new StringBuilder();
                for (int c = 0; c < 500; c++) sb.Append((char)(0xAC00 + random.Next(11172)));
                book.pages[p] = sb.ToString();
                pages++;
            }
            if (!state.TryAddBookInstance(bookItem, Guid.NewGuid().ToString("N"), book, out _)) break;
            books++;
            blob = CharacterSync.Capture().Length;
        }
        Debug.Log($"[CharacterQA] SIZE books: {books} books, {pages} pages of 500 random Hangul → blob {blob} bytes (over 48KB: {blob > SyncProtocol.MaxCharacter})");
        InventorySnapshot.Import(state, keep);
    }

    static async Task<(TcpClientTransport, RejectReason?)> FakeHello(int port)
    {
        var socket = new TcpClientTransport();
        await socket.Connect("127.0.0.1", port);
        var p = new Peer(socket);
        p.Send(SyncProtocol.Hello("qa-fake", "가짜", "QA/fake"));
        float end = Time.realtimeSinceStartup + 5f;
        while (p.reject == null && p.number == 0)
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for the answer to a fake Hello");
            await Awaitable.NextFrameAsync();
            p.Pump();
        }
        return (socket, p.reject);
    }

    // ---- helpers (as ServerOpenQA) ----

    static TitleScreen Screen => TitleScreen.Current;
    static bool OnTitle => SceneManager.GetActiveScene().name == "Title" && TitleScreen.Current != null;
    static bool InGame => SceneManager.GetActiveScene().path == TitleFlow.GameScene && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading;

    static TMP_InputField[] Inputs() => Screen.Content.GetComponentsInChildren<TMP_InputField>();

    static string Where() => Application.isPlaying && Screen ? " [screen=" + Screen.Screen + ": " + TextOf(Screen.Content).Replace('\n', '|') + "]" : "";

    static async Task WaitFor(Func<bool> condition, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what + Where());
            await Awaitable.NextFrameAsync();
        }
    }

    static async Task Click(string label)
    {
        await WaitFor(() => Screen && !Screen.Busy, 30, "screen not busy before clicking " + label);
        Check(Screen.Click(label), "clicked '" + label + "' on screen " + Screen.Screen);
        await Awaitable.NextFrameAsync();
    }

    static bool HasButton(string text)
    {
        foreach (var b in Screen.Content.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            var label = b.GetComponentInChildren<TMP_Text>();
            if (label && label.text.Contains(text)) return true;
        }
        return false;
    }

    static string TextOf(Transform t)
    {
        var sb = new StringBuilder();
        foreach (var text in t.GetComponentsInChildren<TMP_Text>()) sb.Append(text.text).Append('\n');
        return sb.ToString();
    }

    static async Task Shot(string file)
    {
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (string.IsNullOrEmpty(dir) || !Camera.main) return;
        Directory.CreateDirectory(dir);
        await Awaitable.WaitForSecondsAsync(.3f);
        await TitleShots.Snap(Camera.main, Path.Combine(dir, file));
    }
}
