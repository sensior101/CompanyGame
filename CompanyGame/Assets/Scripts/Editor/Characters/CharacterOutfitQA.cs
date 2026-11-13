using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CompanyGame.Editor.Characters
{
    /// <summary>
    /// Puts the basic outfit on MaleVisual / FemaleVisual in a preview scene, poses the real skeleton (rest, walk, sit,
    /// arms up) and checks in Unity's own skinning (BakeMesh) that the clothes stay on the body: every garment vertex
    /// within a few centimetres of the skin, and none left behind or flying off. Renders each pose.
    /// Run: -executeMethod CompanyGame.Editor.Characters.CharacterOutfitQA.Run -quit
    /// </summary>
    public static class CharacterOutfitQA
    {
        const string Out = "../ArtSource/Daldongne/Characters/Outfits_20261111/UnityQA/";

        // Each pose points limbs (bone -> its child) in a direction given in (down, forward, outward) of the avatar.
        static readonly Dictionary<string, (string bone, string child, float down, float forward, float outward)[]> Poses =
            new Dictionary<string, (string, string, float, float, float)[]>
        {
            ["Rest"] = new (string, string, float, float, float)[0],
            ["Walk"] = new[] { ("LeftArm", "LeftForeArm", .95f, .3f, .1f), ("RightArm", "RightForeArm", .95f, -.3f, .1f),
                               ("LeftUpLeg", "LeftLeg", .85f, .53f, 0f), ("RightUpLeg", "RightLeg", .91f, -.41f, 0f),
                               ("RightLeg", "RightFoot", .59f, -.81f, 0f) },
            ["Sit"] = new[] { ("LeftArm", "LeftForeArm", .97f, .1f, .2f), ("RightArm", "RightForeArm", .97f, .1f, .2f),
                              ("LeftForeArm", "LeftHand", .3f, .95f, 0f), ("RightForeArm", "RightHand", .3f, .95f, 0f),
                              ("LeftUpLeg", "LeftLeg", 0f, 1f, .05f), ("RightUpLeg", "RightLeg", 0f, 1f, .05f),
                              ("LeftLeg", "LeftFoot", 1f, 0f, 0f), ("RightLeg", "RightFoot", 1f, 0f, 0f) },
            ["ArmsUp"] = new[] { ("LeftArm", "LeftForeArm", -.8f, 0f, .6f), ("RightArm", "RightForeArm", -.8f, 0f, .6f) },
        };

        public static void Run()
        {
            Directory.CreateDirectory(Out);
            var report = new StringBuilder(); int fails = 0;
            void Check(bool ok, string what) { report.AppendLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(40, 150, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f, .55f, .55f);

            foreach (var (sex, path) in new[] { ("Male", ReferenceBoyImporter.VisualPath), ("Female", ReferenceGirlImporter.VisualPath) })
            {
                var avatar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var dress = avatar.GetComponent<AvatarOutfit>();
                Check(dress && dress.outfit, sex + ": AvatarOutfit with an outfit on " + path);
                if (!dress) continue;
                var motion = avatar.GetComponent<CompanyGame.Daldongne.DaldongneAvatarMotion>();
                if (motion) motion.enabled = false; // the QA poses the bones itself
                var animator = avatar.GetComponentInChildren<Animator>(); if (animator) animator.enabled = false;
                dress.Build();
                var body = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => !dress.Garments.Contains(s));
                Check(dress.Garments.Count == 2 && body.sharedMesh == dress.outfit.maskedBody, sex + ": top and trousers on the avatar's bones, masked body in place");
                var bones = body.bones.Concat(body.rootBone.GetComponentsInChildren<Transform>(true)).Where(b => b).GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
                var rest = bones.ToDictionary(p => p.Key, p => p.Value.localRotation);
                // The full body (not the masked one) is the skin the clothes must stay on.
                var fullMesh = AssetDatabase.LoadAllAssetsAtPath(sex == "Male" ? "Assets/Art/Daldongne/Players/MeshyMale/MaleArticulated.fbx" : "Assets/Art/Daldongne/Players/MeshyFemale/FemaleArticulated.fbx").OfType<Mesh>().First();
                var fullBody = Object.Instantiate(body.gameObject, body.transform.parent).GetComponent<SkinnedMeshRenderer>();
                var fullBones = body.bones; // same order as the body FBX mesh
                fullBody.sharedMesh = fullMesh;
                fullBody.bones = AssetDatabase.LoadAssetAtPath<GameObject>(sex == "Male" ? "Assets/Art/Daldongne/Players/MeshyMale/MaleArticulated.fbx" : "Assets/Art/Daldongne/Players/MeshyFemale/FemaleArticulated.fbx")
                    .GetComponentInChildren<SkinnedMeshRenderer>().bones.Select(b => bones[b.name]).ToArray();
                fullBody.enabled = false;

                foreach (var pose in Poses)
                {
                    foreach (var p in rest) bones[p.Key].localRotation = p.Value;
                    // Forward from the heel to the toes; outward away from the middle of the body.
                    var fwd = Vector3.ProjectOnPlane(bones["LeftToeBase"].position - bones["LeftFoot"].position, Vector3.up).normalized;
                    foreach (var (bone, child, down, forward, outward) in pose.Value)
                    {
                        var b = bones[bone]; var current = bones[child].position - b.position;
                        var side = Vector3.ProjectOnPlane(b.position - avatar.transform.position, Vector3.up); side = Vector3.ProjectOnPlane(side, fwd).normalized;
                        var want = (Vector3.down * down + fwd * forward + side * outward).normalized;
                        b.rotation = Quaternion.FromToRotation(current, want) * b.rotation;
                    }
                    var skin = Bake(fullBody);
                    var grid = new Grid(skin, .05f);
                    foreach (var garment in dress.Garments)
                    {
                        var cloth = Bake(garment);
                        var gaps = cloth.Select(grid.Nearest).OrderBy(d => d).ToArray();
                        float median = gaps[gaps.Length / 2], p99 = gaps[(int)(gaps.Length * .99f)], max = gaps[gaps.Length - 1];
                        Check(median > .003f && median < .04f && p99 < .07f, sex + " " + pose.Key + " " + garment.name + ": cloth to skin median " +
                            (median * 100).ToString("F1") + " cm, 99% " + (p99 * 100).ToString("F1") + " cm, max " + (max * 100).ToString("F1") + " cm");
                    }
                    var shown = new List<SkinnedMeshRenderer>(dress.Garments) { body };
                    var stand = Stand(shown);
                    Shot(avatar.transform, Out + sex + "_" + pose.Key + ".png");
                    foreach (var g in stand) Object.DestroyImmediate(g);
                    foreach (var r in shown) r.enabled = true;
                }
                foreach (var p in rest) bones[p.Key].localRotation = p.Value;
                avatar.transform.position += Vector3.right * 3; // keep the next one clear
                avatar.SetActive(false);
            }
            report.Insert(0, (fails == 0 ? "RESULT PASS" : "RESULT FAIL " + fails) + "\n");
            File.WriteAllText(Out + "unity_outfit_qa.txt", report.ToString());
            Debug.Log("[OutfitQA]\n" + report);
        }

        // CPU skinning with the renderer's own bones and bind poses (edit mode does not refresh GPU skinning between steps).
        static Vector3[] Bake(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh; var bones = skin.bones; var bind = mesh.bindposes;
            var m = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) m[i] = bones[i].localToWorldMatrix * bind[i];
            var vertices = mesh.vertices; var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
            var result = new Vector3[vertices.Length];
            for (int v = 0, w = 0; v < vertices.Length; v++)
            {
                Vector3 sum = Vector3.zero; float total = 0;
                for (int k = 0; k < counts[v]; k++, w++) { sum += weights[w].weight * m[weights[w].boneIndex].MultiplyPoint3x4(vertices[v]); total += weights[w].weight; }
                result[v] = total > 0 ? sum / total : skin.transform.TransformPoint(vertices[v]);
            }
            return result;
        }

        // Stand-ins with the posed shape for the camera, since the renderers keep their last skinned shape here.
        static List<GameObject> Stand(IEnumerable<SkinnedMeshRenderer> skins)
        {
            var made = new List<GameObject>();
            foreach (var skin in skins)
            {
                var mesh = Object.Instantiate(skin.sharedMesh);
                mesh.boneWeights = null; mesh.bindposes = new Matrix4x4[0];
                mesh.vertices = Bake(skin); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var go = new GameObject(skin.name + " pose"); go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = skin.sharedMaterials;
                var block = new MaterialPropertyBlock();
                if (skin.HasPropertyBlock()) { skin.GetPropertyBlock(block); r.SetPropertyBlock(block); }
                skin.enabled = false; made.Add(go);
            }
            return made;
        }

        static void Shot(Transform avatar, string file)
        {
            var go = new GameObject("QA Camera"); var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.86f, .87f, .88f);
            cam.fieldOfView = 30; cam.nearClipPlane = .05f;
            var rt = new RenderTexture(1500, 900, 24); cam.targetTexture = rt;
            var tex = new Texture2D(1500, 900, TextureFormat.RGB24, false);
            var all = new Texture2D(1500, 900, TextureFormat.RGB24, false);
            Vector3 c = avatar.position + Vector3.up * .9f;
            // front (the avatar faces +Z) and three-quarter, side by side
            var shots = new[] { Quaternion.Euler(0, 180, 0), Quaternion.Euler(0, 140, 0), Quaternion.Euler(0, 90, 0) };
            var pixels = new Color[1500 * 900];
            for (int s = 0; s < 3; s++)
            {
                go.transform.rotation = shots[s] * Quaternion.Euler(5, 0, 0);
                go.transform.position = c - go.transform.forward * 4.2f;
                cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1500, 900), 0, 0); tex.Apply();
                var src = tex.GetPixels();
                for (int y = 0; y < 900; y++) for (int x = 0; x < 500; x++) pixels[y * 1500 + s * 500 + x] = src[y * 1500 + 500 + x];
            }
            all.SetPixels(pixels); all.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(file, all.EncodeToPNG());
            Object.DestroyImmediate(tex); Object.DestroyImmediate(all); cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(go);
        }

        // Nearest-vertex distance on a uniform grid (the skin is dense enough that vertex distance stands for surface distance).
        sealed class Grid
        {
            readonly Dictionary<Vector3Int, List<Vector3>> cells = new Dictionary<Vector3Int, List<Vector3>>();
            readonly float size;
            public Grid(Vector3[] points, float cell)
            {
                size = cell;
                foreach (var p in points)
                {
                    var k = Key(p);
                    if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<Vector3>();
                    list.Add(p);
                }
            }
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.y / size), Mathf.FloorToInt(p.z / size));
            public float Nearest(Vector3 p)
            {
                var k = Key(p); float best = float.MaxValue;
                for (int r = 1; r <= 3 && best == float.MaxValue; r++)
                    for (int x = -r; x <= r; x++) for (int y = -r; y <= r; y++) for (int z = -r; z <= r; z++)
                        if (cells.TryGetValue(k + new Vector3Int(x, y, z), out var list))
                            foreach (var q in list) best = Mathf.Min(best, (q - p).sqrMagnitude);
                return best == float.MaxValue ? 1f : Mathf.Sqrt(best);
            }
        }
    }
}
