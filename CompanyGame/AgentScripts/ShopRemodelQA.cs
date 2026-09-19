using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Project-specific verification. Does not save, close, or switch the user's scene.
public static class ShopRemodelQA
{
    const string Folder = "Assets/Art/Daldongne/WarmVillage";
    const string Output = "../ArtSource/Daldongne/ShopRemodel";
    static readonly string[] Names = { "Supermarket", "Bakery" };
    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    static Vector3 Vector(JToken p) => V((float)p[0], (float)p[1], (float)p[2]);
    static Vector3 RoutePoint(JToken p) => V((float)p[0], (float)p[2], (float)p[1]);
    static JArray SceneState() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        new JObject { ["path"] = SceneManager.GetSceneAt(i).path, ["dirty"] = SceneManager.GetSceneAt(i).isDirty }));
    static Transform LiveBuildings()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/daldongnaemap.unity");
        if (!scene.IsValid() || !scene.isLoaded) throw new Exception("Open daldongnaemap.unity before checking shops.");
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Single(t => t.name == "10_Buildings");
    }
    static Bounds BoundsOf(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new Exception("No rendered geometry: " + t.name);
        var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        return bounds;
    }
    static void CheckReferences(GameObject root, string label, List<string> errors)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                errors.Add(label + "/" + t.name + ": missing script.");
            foreach (var component in t.GetComponents<Component>().Where(c => c))
            {
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    while (property.NextVisible(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference &&
                            property.objectReferenceValue == null && !property.objectReferenceEntityIdValue.Equals(EntityId.None))
                            errors.Add(label + "/" + t.name + ": broken " + property.propertyPath);
                }
            }
        }
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            if (!f.sharedMesh || f.sharedMesh.vertexCount == 0) errors.Add(label + "/" + f.name + ": missing/empty mesh.");
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(m => !m || !m.shader || m.shader.name == "Hidden/InternalErrorShader"))
                errors.Add(label + "/" + r.name + ": missing/error material.");
        foreach (var c in root.GetComponentsInChildren<MeshCollider>(true))
            if (!c.sharedMesh) errors.Add(label + "/" + c.name + ": missing collision mesh.");
        if (!root.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger))
            errors.Add(label + ": no solid collision.");
    }
    public static object Audit()
    {
        Directory.CreateDirectory(Output);
        var beforeScenes = SceneState();
        var baseline = JObject.Parse(File.ReadAllText(Output + "/before.json"));
        var village = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/DaldongneWarmTown.prefab");
        var buildings = village.transform.Find("10_Buildings");
        var live = LiveBuildings();
        var errors = new List<string>();
        var rows = new JArray();
        if (buildings.Find("Laundry")) errors.Add("Saved village still contains Laundry.");
        if (live.Find("Laundry")) errors.Add("Live village still contains Laundry.");
        foreach (var name in Names)
        {
            string original = name == "Supermarket" ? "Laundry" : name;
            var previous = baseline["buildings"].Single(x => (string)x["name"] == original);
            string path = Folder + "/Prefabs/Buildings/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var nested = buildings.Find(name);
            var instance = live.Find(name);
            if (!prefab || !nested || !instance) { errors.Add("Missing prefab, saved instance, or live instance: " + name); continue; }
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (guid != (string)previous["guid"]) errors.Add(name + ": original prefab GUID changed.");
            if (Vector3.Distance(nested.position, Vector(previous["pivot"])) > .001f) errors.Add(name + ": saved village pivot moved.");
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nested.gameObject) != path) errors.Add(name + ": saved instance lost prefab connection.");
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.gameObject) != path) errors.Add(name + ": live instance lost prefab connection.");
            // External garden/village references use these root IDs. Child meshes may change freely.
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab, out string rootGuid, out long rootId);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab.transform, out string transformGuid, out long transformId);
            long expectedRoot = name == "Supermarket" ? 5339886773995304851L : 3440877922799875189L;
            long expectedTransform = name == "Supermarket" ? 2146594610952869075L : 5038074694675812093L;
            if (rootId != expectedRoot || transformId != expectedTransform) errors.Add(name + ": root file IDs changed; external prefab references may break.");
            CheckReferences(prefab, name, errors);
            CheckReferences(instance.gameObject, "Live/" + name, errors);
            var bounds = BoundsOf(instance);
            rows.Add(new JObject { ["name"] = name, ["path"] = path, ["guid"] = guid,
                ["rootFileId"] = rootId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["transformFileId"] = transformId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["pivot"] = JArray.FromObject(new[] { nested.position.x, nested.position.y, nested.position.z }),
                ["liveBoundsCenter"] = bounds.center.ToString("F3"), ["liveBoundsSize"] = bounds.size.ToString("F3"),
                ["renderers"] = prefab.GetComponentsInChildren<Renderer>(true).Length,
                ["triangles"] = prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).Sum(f => f.sharedMesh.triangles.Length / 3),
                ["colliders"] = prefab.GetComponentsInChildren<Collider>(true).Length });
        }
        if (EditorUtility.scriptCompilationFailed) errors.Add("Unity script compilation failed.");
        var afterScenes = SceneState();
        if (!JToken.DeepEquals(beforeScenes, afterScenes)) errors.Add("Loaded scene state changed during audit.");
        var report = new JObject { ["passed"] = errors.Count == 0, ["errors"] = JArray.FromObject(errors),
            ["compilationFailed"] = EditorUtility.scriptCompilationFailed, ["buildings"] = rows,
            ["sceneStateBefore"] = beforeScenes, ["sceneStateAfter"] = afterScenes };
        File.WriteAllText(Output + "/asset-validation.json", report.ToString());
        return report;
    }
    static Camera NewCamera(Transform parent, Scene scene)
    {
        var go = new GameObject("Temporary shop review camera") { hideFlags = HideFlags.HideAndDontSave };
        if (parent) go.transform.SetParent(parent, false);
        else SceneManager.MoveGameObjectToScene(go, scene);
        var c = go.AddComponent<Camera>(); c.scene = scene; c.enabled = false; c.orthographic = true;
        c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(.20f, .24f, .25f);
        c.nearClipPlane = .05f; c.farClipPlane = 200; c.aspect = 1.2f;
        return c;
    }
    static void Frame(Camera camera, Bounds b, bool front = false)
    {
        Vector3 direction = (front ? V(0, .035f, -1) : V(.43f, .29f, -1)).normalized;
        camera.transform.position = b.center + direction * 30;
        camera.transform.LookAt(b.center);
        float extentX = 0, extentY = 0;
        for (int i = 0; i < 8; i++)
        {
            var p = b.center + Vector3.Scale(b.extents, V((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var local = camera.transform.InverseTransformPoint(p);
            extentX = Mathf.Max(extentX, Mathf.Abs(local.x)); extentY = Mathf.Max(extentY, Mathf.Abs(local.y));
        }
        camera.orthographicSize = Mathf.Max(extentY, extentX / camera.aspect) * 1.12f;
    }
    static void Shot(Camera camera, string name)
    {
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(1200, 1000, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(1200, 1000, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1200, 1000), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
        }
        finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); }
    }
    public static object Capture()
    {
        Directory.CreateDirectory(Output);
        var beforeScenes = SceneState(); var live = LiveBuildings();
        var camera = NewCamera(null, live.gameObject.scene);
        try
        {
            foreach (var name in Names)
            {
                var instance = live.Find(name); if (!instance) throw new Exception("Missing live shop: " + name);
                Frame(camera, BoundsOf(instance)); Shot(camera, name + "_InMap");
                var preview = EditorSceneManager.NewPreviewScene();
                try
                {
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/Buildings/" + name + ".prefab"), preview);
                    var studio = new GameObject("Temporary shop studio") { hideFlags = HideFlags.HideAndDontSave };
                    SceneManager.MoveGameObjectToScene(studio, preview);
                    var key = new GameObject("Soft key").AddComponent<Light>(); key.transform.SetParent(studio.transform, false);
                    key.type = LightType.Directional; key.intensity = 1.65f; key.color = new Color(1, .95f, .86f);
                    key.transform.rotation = Quaternion.Euler(40, -35, 0); key.shadows = LightShadows.Soft;
                    var fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(studio.transform, false);
                    fill.type = LightType.Directional; fill.intensity = .6f; fill.color = new Color(.80f, .88f, 1);
                    fill.transform.rotation = Quaternion.Euler(25, 135, 0);
                    var shot = NewCamera(studio.transform, preview);
                    Frame(shot, BoundsOf(model.transform)); Shot(shot, name + "_Model");
                    Frame(shot, BoundsOf(model.transform), true); Shot(shot, name + "_Front");
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }
        }
        finally { Object.DestroyImmediate(camera.gameObject); }
        var sheet = new Texture2D(2400, 1000, TextureFormat.RGB24, false);
        try
        {
            for (int i = 0; i < Names.Length; i++)
            {
                var tile = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try { tile.LoadImage(File.ReadAllBytes(Output + "/" + Names[i] + "_Model.png")); sheet.SetPixels(i * 1200, 0, 1200, 1000, tile.GetPixels()); }
                finally { Object.DestroyImmediate(tile); }
            }
            sheet.Apply(); File.WriteAllBytes(Output + "/ShopRemodel_Overview.png", sheet.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(sheet); }
        var report = new JObject { ["images"] = 7, ["directory"] = Path.GetFullPath(Output), ["sceneStateBefore"] = beforeScenes, ["sceneStateAfter"] = SceneState() };
        File.WriteAllText(Output + "/capture-validation.json", report.ToString()); return report;
    }
    static JObject Walk(CharacterController motor, string name, Vector3 start, Vector3 end, float speed, bool reverse, float lane)
    {
        motor.enabled = false; motor.transform.position = start + Vector3.up * .06f; motor.enabled = true;
        Physics.SyncTransforms(); for (int i = 0; i < 15; i++) motor.Move(Vector3.down * .02f);
        float vertical = -2; int stalled = 0, heads = 0, moves = 0; bool fell = false;
        float length = Vector2.Distance(new Vector2(start.x, start.z), new Vector2(end.x, end.z));
        for (int i = 0; i < Mathf.CeilToInt(length / speed * 60) * 4 + 180; i++)
        {
            var remaining = end - motor.transform.position; remaining.y = 0; if (remaining.magnitude < .065f) break;
            if (motor.isGrounded && vertical < 0) vertical = -2; vertical += Physics.gravity.y / 60;
            var previous = motor.transform.position;
            var flags = motor.Move(Vector3.ClampMagnitude(remaining, speed / 60) + Vector3.up * (vertical / 60)); moves++;
            if ((flags & CollisionFlags.Above) != 0) heads++;
            var progress = motor.transform.position - previous; progress.y = 0;
            stalled = progress.magnitude < .0015f ? stalled + 1 : 0;
            fell = motor.transform.position.y < Mathf.Min(start.y, end.y) - .65f; if (stalled >= 30 || fell) break;
        }
        var error = end - motor.transform.position; float height = Mathf.Abs(error.y); error.y = 0;
        bool passed = error.magnitude < .12f && height < .24f && !fell && heads == 0;
        return new JObject { ["route"] = name, ["speed"] = speed, ["reverse"] = reverse, ["lane"] = lane,
            ["passed"] = passed, ["horizontalError"] = error.magnitude, ["heightError"] = height, ["headContacts"] = heads,
            ["moves"] = moves, ["position"] = motor.transform.position.ToString("F3"),
            ["nearby"] = passed ? null : new JArray(Physics.OverlapCapsule(motor.transform.position + V(0, .4f, 0), motor.transform.position + V(0, 1.4f, 0), .42f)
                .Where(c => c != motor).Select(c => c.name)) };
    }
    public static object Navigation()
    {
        Directory.CreateDirectory(Output);
        var beforeScenes = SceneState(); var live = LiveBuildings(); var records = new JArray();
        var temporary = new GameObject("Temporary shop access probe") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(temporary, live.gameObject.scene);
        try
        {
            var motor = temporary.AddComponent<CharacterController>();
            motor.radius = .35f; motor.height = 1.8f; motor.center = V(0, .9f, 0);
            motor.stepOffset = .23f; motor.slopeLimit = 45; motor.skinWidth = .02f; motor.minMoveDistance = 0;
            var routes = (JArray)JObject.Parse(File.ReadAllText("../ArtSource/Daldongne/warm_routes.json"))["routes"];
            foreach (var route in routes) foreach (bool reverse in new[] { false, true })
                records.Add(Walk(motor, (string)route["name"], RoutePoint(route[reverse ? "b" : "a"]), RoutePoint(route[reverse ? "a" : "b"]), 3, reverse, 0));
            var local = new[] {
                new { name = "SupermarketFront", a = V(-21, 6, 9.5f), b = V(-11.2f, 6, 9.5f) },
                new { name = "BakeryFront", a = V(13.6f, 3, -3.7f), b = V(20.4f, 3, -3.7f) }
            };
            foreach (var route in local) foreach (float lane in new[] { -.4f, 0, .4f })
                foreach (float speed in new[] { 3f, 4.5f }) foreach (bool reverse in new[] { false, true })
                {
                    var side = Vector3.Cross(Vector3.up, (route.b - route.a).normalized) * lane;
                    records.Add(Walk(motor, route.name, (reverse ? route.b : route.a) + side, (reverse ? route.a : route.b) + side, speed, reverse, lane));
                }
        }
        finally { Object.DestroyImmediate(temporary); Physics.SyncTransforms(); }
        int failures = records.Count(r => !(bool)r["passed"]);
        var report = new JObject { ["passed"] = failures == 0, ["mode"] = EditorApplication.isPlaying ? "PlayMode temporary controller" : "EditMode temporary controller",
            ["checks"] = records.Count, ["failures"] = failures, ["radius"] = .35f, ["height"] = 1.8f, ["stepOffset"] = .23f,
            ["sceneStateBefore"] = beforeScenes, ["sceneStateAfter"] = SceneState(), ["records"] = records };
        File.WriteAllText(Output + "/navigation-validation.json", report.ToString());
        return new { passed = failures == 0, checks = records.Count, failures, failed = records.Where(r => !(bool)r["passed"]).ToArray() };
    }
}
