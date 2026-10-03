using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>Adds matching windows to the unused faces of scene Gosiwon instances.</summary>
    public static class TerracedBuildingDetails
    {
        const string HolderName = "Terraced Additional Gosiwon Windows";
        static readonly string[] BuildingNames =
        {
            "Gosiwon"
        };
        static readonly string[] WindowNames =
        {
            "20_SideWindows_cream", "20_SideWindows_glass"
        };

        sealed class WindowSet
        {
            public Transform building;
            public MeshFilter wall;
            public MeshFilter[] windows;
        }

        /// <summary>
        /// Modifies only scene instances. The caller is responsible for saving the scene.
        /// Shared meshes and materials stay connected to the existing Korean building assets.
        /// </summary>
        public static void Apply(GameObject root)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before adding building details.");
            if (PrefabUtility.IsPartOfPrefabAsset(root) || !root.scene.IsValid() || !root.scene.isLoaded)
                throw new InvalidOperationException("Pass the loaded map scene root, not a prefab asset.");

            var buildings = root.transform.Find("10_World/Daldongne Warm Village/10_Buildings");
            if (!buildings) throw new InvalidOperationException("The original village buildings are missing.");

            // Validate every source before replacing any previously generated decoration.
            var sets = BuildingNames.Select(name => Inspect(buildings, name)).ToArray();
            foreach (var set in sets) AddWindows(set);
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        static WindowSet Inspect(Transform buildings, string name)
        {
            var building = buildings.Find(name);
            if (!building) throw new InvalidOperationException("Missing Gosiwon instance: " + name);
            var wall = building.Find("00_Walls_mortar")?.GetComponent<MeshFilter>();
            var windows = WindowNames.Select(n => building.Find(n)?.GetComponent<MeshFilter>()).ToArray();
            if (!wall || !wall.sharedMesh || windows.Any(w => !w || !w.sharedMesh || !w.GetComponent<MeshRenderer>()))
                throw new InvalidOperationException("Missing original Gosiwon wall/window mesh: " + name);
            var scale = building.lossyScale;
            if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0)
                throw new InvalidOperationException("Gosiwon needs positive world scale: " + name);
            return new WindowSet { building = building, wall = wall, windows = windows };
        }

        // Bounds measured in world metres along the building's world right/up/forward axes.
        // Transforming mesh corners also handles pivots baked into the original mesh vertices.
        static Bounds WorldFrameBounds(Transform building, params MeshFilter[] meshes)
        {
            bool first = true;
            var result = new Bounds();
            foreach (var filter in meshes)
            {
                var bounds = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var world = filter.transform.TransformPoint(corner) - building.position;
                    var point = new Vector3(Vector3.Dot(world, building.right),
                        Vector3.Dot(world, building.up), Vector3.Dot(world, building.forward));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        static Vector3 ToWorld(Transform building, Vector3 point)
        {
            return building.position + building.right * point.x + building.up * point.y + building.forward * point.z;
        }

        static void AddWindows(WindowSet set)
        {
            var building = set.building;
            var body = WorldFrameBounds(building, set.wall);
            var source = WorldFrameBounds(building, set.windows);
            // Retain the existing gap between the +X wall and its window frames.
            float faceOffset = source.center.x - body.max.x;
            if (source.min.x < body.max.x - .03f || faceOffset <= 0)
                throw new InvalidOperationException("Original side windows are not outside the right wall: " + building.name);

            var previous = building.Find(HolderName);
            if (previous) Object.DestroyImmediate(previous.gameObject);
            var holder = new GameObject(HolderName).transform;
            holder.SetParent(building, false);

            var sourceCenter = ToWorld(building, source.center);
            var leftCenter = ToWorld(building,
                new Vector3(body.min.x - faceOffset, source.center.y, source.center.z));
            var rearCenter = ToWorld(building,
                new Vector3(body.center.x, source.center.y, body.max.z + faceOffset));
            CopyBank(set, holder, "Left windows", sourceCenter, leftCenter, 180f);
            CopyBank(set, holder, "Rear windows", sourceCenter, rearCenter, -90f);
        }

        static void CopyBank(WindowSet set, Transform holder, string name,
            Vector3 sourceCenter, Vector3 targetCenter, float angle)
        {
            var bank = new GameObject(name).transform;
            bank.SetParent(holder, false);
            bank.SetPositionAndRotation(sourceCenter, set.building.rotation);

            foreach (var source in set.windows)
            {
                var go = new GameObject(source.name) { layer = source.gameObject.layer };
                go.transform.SetParent(bank, false);
                go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                var sourceScale = source.transform.lossyScale;
                var bankScale = bank.lossyScale;
                go.transform.localScale = new Vector3(sourceScale.x / bankScale.x,
                    sourceScale.y / bankScale.y, sourceScale.z / bankScale.z);
                go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var original = source.GetComponent<MeshRenderer>();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = original.sharedMaterials;
                renderer.shadowCastingMode = original.shadowCastingMode;
                renderer.receiveShadows = original.receiveShadows;
                renderer.lightProbeUsage = original.lightProbeUsage;
                renderer.reflectionProbeUsage = original.reflectionProbeUsage;
                renderer.probeAnchor = original.probeAnchor;
                renderer.renderingLayerMask = original.renderingLayerMask;
                renderer.allowOcclusionWhenDynamic = original.allowOcclusionWhenDynamic;
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(source.gameObject));
            }

            // Rotate the complete bank about its measured window centre, never the mesh origin.
            bank.rotation = Quaternion.AngleAxis(angle, set.building.up) * bank.rotation;
            bank.position = targetCenter;
        }
    }
}
