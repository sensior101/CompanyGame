using System;
using System.Collections.Generic;
using System.Linq;
using CompanyGame.Daldongne;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace CompanyGame.Editor.Citizens
{
    /// <summary>
    /// Builds Resources/Citizens/CitizenMale|CitizenFemale from the player's MaleVisual/FemaleVisual.
    /// The Meshy bodies have 212k-226k triangles, so the skinned mesh is simplified by vertex clustering
    /// to at most TriangleBudget. The 10-bone rig, bind poses, materials and DaldongneAvatarMotion are kept,
    /// so the walking pose still comes from position changes. Idempotent: the source prefabs are never touched.
    /// </summary>
    public static class CitizenPrefabBuilder
    {
        public const int TriangleBudget = 15000;
        public const string PrefabFolder = "Assets/Resources/Citizens";
        public const string MeshFolder = "Assets/Art/Citizens";
        const string MaleVisual = "Assets/Art/Daldongne/Players/MaleVisual.prefab";
        const string FemaleVisual = "Assets/Art/Daldongne/Players/FemaleVisual.prefab";

        [MenuItem("Tools/Company Game/Citizens/Build Citizen Prefabs")]
        public static void Build()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MeshFolder);
            foreach (bool female in new[] { false, true }) BuildOne(female);
            BuildBook();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Citizens] Prefabs built in " + PrefabFolder);
        }

        static void BuildOne(bool female)
        {
            string name = female ? "CitizenFemale" : "CitizenMale";
            string visual = female ? FemaleVisual : MaleVisual;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(visual);
            if (!source) throw new InvalidOperationException("Missing " + visual);
            var sourceMesh = source.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
            var mesh = SaveMesh(Simplify(sourceMesh, TriangleBudget), MeshFolder + "/" + name + "Body.asset");
            var root = PrefabUtility.LoadPrefabContents(visual);
            try
            {
                root.name = name;
                var skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                skin.sharedMesh = mesh;
                skin.updateWhenOffscreen = false;
                if (!root.GetComponent<DaldongneAvatarMotion>()) throw new InvalidOperationException(visual + " has no DaldongneAvatarMotion.");
                AddCitizenParts(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + name + ".prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[Citizens] " + name + ": " + (sourceMesh.triangles.Length / 3) + " -> " + (mesh.triangles.Length / 3) + " triangles");
        }

        // Low-poly book (cover + page block, 24 triangles) for the sitting readers: Resources/Citizens/CitizenBook.prefab.
        static void BuildBook()
        {
            var cover = MakeMaterial(MeshFolder + "/CitizenBookCover.mat", new Color(.45f, .12f, .14f));
            var pages = MakeMaterial(MeshFolder + "/CitizenBookPages.mat", new Color(.93f, .90f, .80f));
            var root = new GameObject("CitizenBook");
            try
            {
                Part(root.transform, "Cover", new Vector3(.21f, .035f, .29f), Vector3.zero, cover);
                Part(root.transform, "Pages", new Vector3(.19f, .03f, .27f), new Vector3(0f, .006f, .004f), pages);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/CitizenBook.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void Part(Transform parent, string name, Vector3 size, Vector3 position, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(parent, false);
            cube.transform.localScale = size;
            cube.transform.localPosition = position;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Material MakeMaterial(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void AddCitizenParts(GameObject root)
        {
            foreach (var old in root.GetComponents<Component>().Where(c => c is NavMeshAgent || c is Collider || c is Rigidbody || c is CitizenWalker).ToArray())
                UnityEngine.Object.DestroyImmediate(old);
            var agent = root.AddComponent<NavMeshAgent>();
            agent.agentTypeID = 0;
            agent.radius = .3f; agent.height = 1.8f; agent.baseOffset = 0f;
            agent.speed = 1.3f; agent.angularSpeed = 300f; agent.acceleration = 4f; agent.stoppingDistance = .3f;
            agent.autoBraking = true; agent.autoRepath = true; agent.autoTraverseOffMeshLink = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = 50; agent.areaMask = NavMesh.AllAreas;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 1; capsule.radius = .3f; capsule.height = 1.8f; capsule.center = new Vector3(0f, .9f, 0f);
            capsule.isTrigger = false;
            var body = root.AddComponent<Rigidbody>(); // kinematic: a collider moved by the agent without it is treated as static
            body.isKinematic = true; body.useGravity = false;
            root.AddComponent<CitizenWalker>();
        }

        // ---- mesh simplification (vertex clustering) ---------------------------------

        public static Mesh Simplify(Mesh source, int maxTriangles)
        {
            var positions = source.vertices;
            Vector3 min = source.bounds.min;
            float lo = source.bounds.size.magnitude / 4000f, hi = source.bounds.size.magnitude / 3f;
            for (int k = 0; k < 22; k++)
            {
                float mid = (lo + hi) * .5f;
                var group = Group(positions, min, mid, out _);
                if (Remap(source, group).Sum(list => list.Count) / 3 <= maxTriangles) hi = mid; else lo = mid;
            }
            var map = Group(positions, min, hi, out int count);
            return Build(source, map, count);
        }

        // Vertex -> cluster id; the cell index of a vertex packs into one long key.
        static int[] Group(Vector3[] positions, Vector3 min, float cell, out int count)
        {
            var ids = new Dictionary<long, int>();
            var map = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 q = (positions[i] - min) / cell;
                long key = (long)q.x | ((long)q.y << 21) | ((long)q.z << 42);
                if (!ids.TryGetValue(key, out int id)) { id = ids.Count; ids[key] = id; }
                map[i] = id;
            }
            count = ids.Count;
            return map;
        }

        // Per submesh index lists with collapsed and duplicate triangles removed.
        static List<List<int>> Remap(Mesh source, int[] map)
        {
            var seen = new HashSet<(int, int, int)>();
            var result = new List<List<int>>();
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var tris = source.GetTriangles(s);
                var list = new List<int>();
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = map[tris[t]], b = map[tris[t + 1]], c = map[tris[t + 2]];
                    if (a == b || b == c || a == c) continue;
                    var key = a <= b && a <= c ? (a, b, c) : b <= c ? (b, c, a) : (c, a, b);
                    if (!seen.Add(key)) continue;
                    list.Add(a); list.Add(b); list.Add(c);
                }
                result.Add(list);
            }
            return result;
        }

        static Mesh Build(Mesh source, int[] map, int count)
        {
            int bones = source.bindposes.Length;
            var positions = source.vertices; var normals = source.normals; var uvs = source.uv; var weights = source.boneWeights;
            var sumP = new Vector3[count]; var sumN = new Vector3[count]; var sumUv = new Vector2[count];
            var members = new int[count]; var boneSum = new float[count * bones];
            for (int i = 0; i < map.Length; i++)
            {
                int c = map[i];
                sumP[c] += positions[i]; sumN[c] += normals[i]; members[c]++;
                if (uvs.Length == positions.Length) sumUv[c] += uvs[i];
                var w = weights[i];
                boneSum[c * bones + w.boneIndex0] += w.weight0; boneSum[c * bones + w.boneIndex1] += w.weight1;
                boneSum[c * bones + w.boneIndex2] += w.weight2; boneSum[c * bones + w.boneIndex3] += w.weight3;
            }
            var mesh = new Mesh { name = source.name + "_Citizen" };
            var outP = new Vector3[count]; var outN = new Vector3[count]; var outUv = new Vector2[count]; var outW = new BoneWeight[count];
            for (int c = 0; c < count; c++)
            {
                outP[c] = sumP[c] / members[c];
                outN[c] = sumN[c].sqrMagnitude > 1e-8f ? sumN[c].normalized : Vector3.up;
                outUv[c] = sumUv[c] / members[c];
                outW[c] = TopFour(boneSum, c * bones, bones);
            }
            mesh.vertices = outP; mesh.normals = outN; mesh.uv = outUv; mesh.boneWeights = outW;
            mesh.bindposes = source.bindposes;
            var lists = Remap(source, map);
            mesh.subMeshCount = lists.Count;
            for (int s = 0; s < lists.Count; s++) mesh.SetTriangles(lists[s], s);
            mesh.RecalculateBounds();
            return mesh;
        }

        static BoneWeight TopFour(float[] sums, int offset, int bones)
        {
            var order = Enumerable.Range(0, bones).OrderByDescending(b => sums[offset + b]).Take(4).ToArray();
            float total = order.Sum(b => sums[offset + b]);
            if (total <= 0f) return new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            return new BoneWeight
            {
                boneIndex0 = order[0], weight0 = sums[offset + order[0]] / total,
                boneIndex1 = order[1], weight1 = sums[offset + order[1]] / total,
                boneIndex2 = order[2], weight2 = sums[offset + order[2]] / total,
                boneIndex3 = order[3], weight3 = sums[offset + order[3]] / total,
            };
        }

        // Keeps the asset (and so the prefab's reference to it) on re-runs.
        static Mesh SaveMesh(Mesh built, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!existing) { AssetDatabase.CreateAsset(built, path); return built; }
            EditorUtility.CopySerialized(built, existing);
            UnityEngine.Object.DestroyImmediate(built);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static void EnsureFolder(string path)
        {
            string parent = "Assets";
            foreach (string part in path.Substring("Assets/".Length).Split('/'))
            {
                string next = parent + "/" + part;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, part);
                parent = next;
            }
        }
    }
}
