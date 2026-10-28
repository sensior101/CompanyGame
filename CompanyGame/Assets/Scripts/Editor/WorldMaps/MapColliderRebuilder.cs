using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompanyGame.Editor.WorldMaps
{
    public enum Coverage { NotApplicable, Matched, Missing, Protrudes }

    /// <summary>One collider the rules would generate for a renderer, plus how the existing colliders compare.</summary>
    public sealed class PlannedCollider
    {
        public Renderer Source;
        public string Path;
        public ColliderKind Kind;
        public ColliderShape Shape;
        public Bounds World;
        public Mesh Mesh;
        public float Fill;
        public int UpAxis = 1;      // local axis pointing up in the world (-1 tilted)
        public float UpSign = 1f;
        public Coverage Status;
        public float Protrusion;
        public Collider Cover;
        public Bounds Local;   // box extents in the source renderer's local space (whole mesh bounds, or one split component)
    }

    /// <summary>Everything measured for one map while its scene was open (read-only).</summary>
    public sealed class MapScan
    {
        public string Name, ScenePath, Error;
        public int RendererCount, GameObjectCount;
        public List<PlannedCollider> Plans = new List<PlannedCollider>();
        public List<Collider> Existing = new List<Collider>();
        public FloorIndex Floors = new FloorIndex();
        public MapCollisionQA.Result Qa;
        public bool DirtyAfterScan;
        public List<string> Split = new List<string>();
    }

    /// <summary>Visual floor triangles cached in world space; answers "top surface below this point".</summary>
    public sealed class FloorIndex
    {
        const int MaxTriangles = 30000;
        sealed class Entry { public Bounds B; public Vector3[] V; public int[] T; }
        readonly List<Entry> entries = new List<Entry>();
        public int Skipped;

        public int Count { get { return entries.Count; } }

        public void Add(Renderer r, Mesh m)
        {
            if (!m.isReadable || m.triangles.Length / 3 > MaxTriangles) { Skipped++; return; }
            var l2w = r.transform.localToWorldMatrix;
            var v = m.vertices.Select(p => l2w.MultiplyPoint3x4(p)).ToArray();
            entries.Add(new Entry { B = r.bounds, V = v, T = m.triangles });
        }

        public bool TryTopBelow(float x, float z, float maxY, out float y)
        {
            y = float.NegativeInfinity;
            foreach (var e in entries)
            {
                if (x < e.B.min.x || x > e.B.max.x || z < e.B.min.z || z > e.B.max.z || e.B.min.y > maxY) continue;
                for (int i = 0; i < e.T.Length; i += 3)
                    if (TriY(e.V[e.T[i]], e.V[e.T[i + 1]], e.V[e.T[i + 2]], x, z, out float h) && h <= maxY && h > y) y = h;
            }
            return !float.IsNegativeInfinity(y);
        }

        static bool TriY(Vector3 a, Vector3 b, Vector3 c, float x, float z, out float y)
        {
            y = 0f;
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-8f) return false;
            float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
            float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
            float l3 = 1f - l1 - l2;
            if (l1 < 0f || l2 < 0f || l3 < 0f) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y;
            return true;
        }
    }

    /// <summary>
    /// Stage 1: Report() is a read-only dry run over all outdoor maps (opens scenes additively, never saves).
    /// Apply() regenerates the Colliders_Generated group; written but NOT run this round, and it refuses to run without -collidersApply.
    /// Run: Unity -batchmode -quit -projectPath CompanyGame -executeMethod CompanyGame.Editor.WorldMaps.MapColliderRebuilder.Report
    /// </summary>
    public static class MapColliderRebuilder
    {
        public const string GroupName = "Colliders_Generated", ManualGroupName = "Manual_Colliders";
        public const string ReportRelativePath = "_temp/team/2026-10-12_vehicles-npc-multiplayer/collision_report.md";
        public static readonly string[] Maps =
        {
            "Assets/Scenes/daldongnaemap.unity", "Assets/Scenes/Maps/BusinessDistrict.unity", "Assets/Scenes/Maps/CivicDistrict.unity",
            "Assets/Scenes/Maps/CoastalResidentialDistrict.unity", "Assets/Scenes/Maps/LuxuryResidentialDistrict.unity",
            "Assets/Scenes/Maps/NightlifeDistrict.unity", "Assets/Scenes/Maps/DaldongnePocketGarden.unity",
        };
        static readonly HashSet<string> IgnoredRoots = new HashSet<string> { "Player", "NPCs", "Map Camera", "Lighting", "SpawnPoints", "Traffic", "Citizen Navigation", "ConvenienceStore_Connections", GroupName };

        // ---------- dry run ----------

        [MenuItem("Tools/Company Game/Maps/Colliders/Report (dry run)")]
        public static void Report()
        {
            var scans = Maps.Select(ScanPath).ToList();
            string file = ReportPath();
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, MapCollisionQA.BuildReport(scans));
            Debug.Log("[MapColliderRebuilder] Dry-run report written (no scene saved): " + file);
        }

        public static string ReportPath()
        {
            string rel = Environment.GetCommandLineArgs().Contains("-collidersAfter") ? ReportRelativePath.Replace("collision_report.md", "collision_report_after.md") : ReportRelativePath;
            return Path.Combine(RepoRoot(), rel);
        }

        static string RepoRoot()
        {
            var d = new DirectoryInfo(Application.dataPath);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, ".git")) && !File.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
            return d != null ? d.FullName : Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        }

        static MapScan ScanPath(string path)
        {
            var scan = new MapScan { ScenePath = path, Name = Path.GetFileNameWithoutExtension(path) };
            var scene = SceneManager.GetSceneByPath(path);
            bool wasOpen = scene.isLoaded;
            try
            {
                if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                Scan(scene, scan);
                scan.Qa = MapCollisionQA.Measure(scan);
                scan.DirtyAfterScan = scene.isDirty && !wasOpen;
            }
            catch (Exception e) { scan.Error = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            finally { if (!wasOpen && scene.IsValid()) EditorSceneManager.CloseScene(scene, true); }
            return scan;
        }

        // ---------- classification and diff (shared by dry run and apply) ----------

        public static MapScan Scan(Scene scene, MapScan scan = null)
        {
            scan = scan ?? new MapScan { Name = scene.name, ScenePath = scene.path };
            var roots = scene.GetRootGameObjects().Where(g => !IgnoredRoots.Contains(g.name)).ToArray();
            scan.GameObjectCount = roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Count();
            var generated = scene.GetRootGameObjects().Where(g => g.name == GroupName);   // counted as existing colliders, but never as visuals
            scan.Existing = roots.Concat(generated).SelectMany(g => g.GetComponentsInChildren<Collider>(false)).Where(IsSolid).ToList();
            var rends = roots.SelectMany(g => g.GetComponentsInChildren<Renderer>(false))
                .Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && MeshOf(r) != null).ToList();
            scan.RendererCount = rends.Count;
            var plans = rends.Select(r => NewPlan(r, float.PositiveInfinity)).ToList();
            foreach (var f in plans.Where(p => p.Kind == ColliderKind.Floor || p.Kind == ColliderKind.Stairs)) scan.Floors.Add(f.Source, f.Mesh);
            foreach (var p in plans.Where(p => p.Kind == ColliderKind.Building || p.Kind == ColliderKind.Skip)) Reclassify(p, scan.Floors);
            plans = plans.SelectMany(p => Expand(p, scan)).ToList();
            foreach (var p in plans) { Finish(p); Compare(p, scan.Existing); }
            scan.Plans = plans.OrderBy(p => p.Path, StringComparer.Ordinal).ToList();
            return scan;
        }

        static bool IsSolid(Collider c)
        {
            return c.enabled && !c.isTrigger && !(c is WheelCollider) && c.gameObject.activeInHierarchy;
        }

        public static Mesh MeshOf(Renderer r)
        {
            var s = r as SkinnedMeshRenderer;
            if (s) return s.sharedMesh;
            var f = r.GetComponent<MeshFilter>();
            return f ? f.sharedMesh : null;
        }

        static PlannedCollider NewPlan(Renderer r, float groundY)
        {
            var m = MeshOf(r);
            var p = new PlannedCollider { Source = r, Mesh = m, World = r.bounds, Local = m.bounds, Path = PathOf(r.transform) };
            p.UpAxis = MapColliderRules.UpAxis(r.transform.rotation, out p.UpSign);
            p.Kind = MapColliderRules.Classify(p.Path, p.World, groundY);
            return p;
        }

        static void Reclassify(PlannedCollider p, FloorIndex floors)
        {
            float ground = p.World.min.y;
            if (floors.TryTopBelow(p.World.center.x, p.World.center.z, p.World.min.y + 0.5f, out float top)) ground = top;
            p.Kind = MapColliderRules.Classify(p.Path, p.World, ground);
        }

        // ---------- merged meshes and multi-part rails: split into connected components in memory (render meshes untouched) ----------

        const float RailCell = 1.2f, MergedCell = 3f, BigExtentRail = 2f, BigExtentMerged = 8f;

        static IEnumerable<PlannedCollider> Expand(PlannedCollider p, MapScan scan)
        {
            if (p.Kind == ColliderKind.Merged)
            {
                var byName = MapColliderRules.MergedByName(p.Source.name);
                if (byName.HasValue) { p.Kind = byName.Value; scan.Split.Add(p.Path + " -> whole mesh as " + p.Kind); return new[] { p }; }
                return SplitParts(p, scan, BigExtentMerged, MergedCell, true);
            }
            var s = p.Local.size;
            bool multiPart = (p.Kind == ColliderKind.Rail || p.Kind == ColliderKind.Glass || p.Kind == ColliderKind.Curb)
                && Mathf.Min(s.x, s.z) > 0.6f && Mathf.Max(s.x, s.z) > BigExtentRail;
            return multiPart ? SplitParts(p, scan, BigExtentRail, RailCell, false) : new[] { p };
        }

        static IEnumerable<PlannedCollider> SplitParts(PlannedCollider src, MapScan scan, float bigExtent, float cell, bool classify)
        {
            if (!src.Mesh.isReadable) return new[] { src };
            var parts = MeshSplitter.Split(src.Mesh, bigExtent, cell);
            var l2w = src.Source.transform.localToWorldMatrix;
            var result = new List<PlannedCollider>();
            foreach (var local in parts)
            {
                var world = ToWorld(local, l2w);
                var part = new PlannedCollider { Source = src.Source, Mesh = src.Mesh, Local = local, World = world, Path = src.Path, UpAxis = src.UpAxis, UpSign = src.UpSign, Kind = src.Kind };
                float ground = src.World.min.y;
                if (scan.Floors.TryTopBelow(world.center.x, world.center.z, world.min.y + 0.5f, out float top)) ground = top;
                float above = world.min.y - ground;
                if (classify) part.Kind = MapColliderRules.ClassifyPart(world.size, above);
                else if (Mathf.Max(world.size.x, world.size.y, world.size.z) < MapColliderRules.TinyExtent || above >= MapColliderRules.ElevatedHeight) part.Kind = ColliderKind.Skip;
                result.Add(part);
            }
            scan.Split.Add(src.Path + " [" + src.Kind + "] -> " + parts.Count + " parts, " + result.Count(r => r.Kind != ColliderKind.Skip) + " with collider");
            return result;
        }

        static Bounds ToWorld(Bounds local, Matrix4x4 l2w)
        {
            var b = new Bounds(l2w.MultiplyPoint3x4(local.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(l2w.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return b;
        }

        static void Finish(PlannedCollider p)
        {
            p.Fill = p.Kind == ColliderKind.Building ? FillRatio(p.Mesh) : 1f;
            p.Shape = MapColliderRules.ShapeFor(p.Kind, p.Fill);
        }

        /// <summary>|signed mesh volume| / local bounds volume; 1 when the mesh is unreadable (assume box), 0 for very large meshes (assume mesh collider).</summary>
        public static float FillRatio(Mesh m)
        {
            var bv = m.bounds.size;
            float boundsVolume = bv.x * bv.y * bv.z;
            if (!m.isReadable || boundsVolume < 1e-6f) return 1f;
            var tris = m.triangles;
            if (tris.Length > 600000) return 0f;
            var v = m.vertices;
            double vol = 0;
            for (int i = 0; i < tris.Length; i += 3)
                vol += Vector3.Dot(v[tris[i]], Vector3.Cross(v[tris[i + 1]], v[tris[i + 2]])) / 6.0;
            return Mathf.Clamp01((float)(Math.Abs(vol) / boundsVolume));
        }

        static void Compare(PlannedCollider p, List<Collider> existing)
        {
            if (p.Shape == ColliderShape.None) { p.Status = Coverage.NotApplicable; return; }
            var c = p.World.center;
            float best = float.MaxValue;
            foreach (var e in existing)
            {
                var b = e.bounds;
                b.Expand(0.3f);
                if (!b.Contains(c)) continue;
                float prot = Protrusion(p.World, e.bounds);
                if (prot < best) { best = prot; p.Cover = e; }
            }
            p.Protrusion = best == float.MaxValue ? 0f : best;
            p.Status = p.Cover == null ? Coverage.Missing : p.Protrusion > 0.3f ? Coverage.Protrudes : Coverage.Matched;
        }

        public static float Protrusion(Bounds visual, Bounds collider)
        {
            float x = Mathf.Max(visual.max.x - collider.max.x, collider.min.x - visual.min.x);
            float z = Mathf.Max(visual.max.z - collider.max.z, collider.min.z - visual.min.z);
            return Mathf.Max(0f, x, z);
        }

        public static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null && parts.Count < 12; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ---------- apply (NOT run this round) ----------

        [MenuItem("Tools/Company Game/Maps/Colliders/Apply (writes scenes!)")]
        public static void Apply()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-collidersApply"))
                throw new InvalidOperationException("Stage 2 only: pass -collidersApply (and optionally -collidersDisableLegacy) after the user approved the report.");
            int i = Array.IndexOf(args, "-collidersMaps");   // optional: -collidersMaps CivicDistrict,NightlifeDistrict
            var only = i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',') : null;
            foreach (var path in Maps.Where(m => only == null || only.Contains(Path.GetFileNameWithoutExtension(m))))
                ApplyScene(path, args.Contains("-collidersDisableLegacy"));
        }

        /// <summary>Idempotent: deletes and rebuilds Colliders_Generated; legacy colliders are only disabled (never deleted), and only where a generated collider replaces them.</summary>
        public static void ApplyScene(string path, bool disableLegacy)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == GroupName).ToArray()) { ReenableLegacy(old); UnityEngine.Object.DestroyImmediate(old); }
            var scan = Scan(scene);
            var group = new GameObject(GroupName);
            SceneManager.MoveGameObjectToScene(group, scene);
            var made = new List<Collider>();
            foreach (var plan in scan.Plans.Where(p => p.Shape != ColliderShape.None)) made.Add(Create(group.transform, plan));
            if (disableLegacy) RecordDisabled(group.transform, FixLegacy(scan, made));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MapColliderRebuilder] " + path + ": generated " + made.Count + " colliders, legacy disabled=" + disableLegacy);
        }

        static Collider Create(Transform group, PlannedCollider p)
        {
            var src = p.Source.transform;
            var go = new GameObject(p.Kind + "_" + p.Source.name);
            go.transform.SetParent(group, false);
            go.transform.SetPositionAndRotation(src.position, src.rotation);
            var scale = src.lossyScale;
            if (p.Shape == ColliderShape.Mesh)
            {
                go.transform.localScale = scale;
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = p.Mesh;
                return mc;
            }
            var box = go.AddComponent<BoxCollider>();
            var abs = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            var size = Vector3.Scale(p.Local.size, abs);
            var center = Vector3.Scale(p.Local.center, scale);
            if (p.Kind == ColliderKind.Rail || p.Kind == ColliderKind.Glass)
            {
                size = MapColliderRules.EnforceRailSize(size, p.UpAxis, p.UpSign, out Vector3 shift);
                center += shift;
            }
            else
            {
                size = MapColliderRules.Thicken(size, p.UpAxis);
                if (p.UpAxis == 1) TrimStepSlab(ref size, ref center);
            }
            box.center = center;   // the generated object has unit scale, so these are world units
            box.size = size;
            return box;
        }

        /// <summary>Flat slab just over stepOffset (0.30 m): trim the top to 0.28 so a 0.3 m step is not a coin toss.</summary>
        static void TrimStepSlab(ref Vector3 size, ref Vector3 center)
        {
            if (!MapColliderRules.IsStepTrapSlab(size)) return;
            center.y -= (size.y - MapColliderRules.StepSlabHeight) * 0.5f;
            size.y = MapColliderRules.StepSlabHeight;
        }

        /// <summary>Legacy colliders are only disabled (never deleted), and only when a generated collider replaces them; kept boundary/thin/step boxes are fixed in place.</summary>
        static List<Collider> FixLegacy(MapScan scan, List<Collider> made)
        {
            Physics.SyncTransforms();
            var madeSet = new HashSet<Collider>(made);
            var replaced = new HashSet<Renderer>(scan.Plans.Where(p => p.Shape != ColliderShape.None).Select(p => p.Source));
            var probe = made.OfType<BoxCollider>().FirstOrDefault();
            bool physicsOk = probe == null || PhysicsSees(probe);
            if (!physicsOk) Debug.LogWarning("[MapColliderRebuilder] physics does not see the new colliders; coverage test falls back to bounds");
            int off = 0, raised = 0, fixedThin = 0;
            var disabled = new List<Collider>();
            foreach (var c in scan.Existing.Where(IsLegacyStatic).ToArray())
            {
                var bc = c as BoxCollider;
                if (bc != null && MapCollisionQA.IsBoundaryBox(bc))
                {
                    if (MapCollisionQA.SurroundedByFloor(scan.Floors, bc.bounds)) { bc.enabled = false; disabled.Add(bc); off++; }
                    else if (RaiseBoundary(bc)) raised++;
                    continue;
                }
                if (IsReplaced(c, replaced) || CoveredByGenerated(c, madeSet, physicsOk)
                    || (bc != null && MapCollisionQA.IsInnerBlocker(bc, scan.Floors))) { c.enabled = false; disabled.Add(c); off++; continue; }
                if (bc != null && FixThinOrStep(bc)) fixedThin++;
            }
            Debug.Log("[MapColliderRebuilder] legacy: disabled " + off + ", boundary raised " + raised + ", thin/step fixed " + fixedThin);
            return disabled;
        }

        const string DisabledListName = "LegacyDisabled";

        /// <summary>Remember which legacy colliders we disabled (GlobalObjectId strings as child names) so a re-run can restore them first.</summary>
        static void RecordDisabled(Transform group, List<Collider> disabled)
        {
            var list = new GameObject(DisabledListName).transform;
            list.SetParent(group, false);
            foreach (var c in disabled)
            {
                var child = new GameObject(GlobalObjectId.GetGlobalObjectIdSlow(c).ToString());
                child.transform.SetParent(list, false);
            }
        }

        static void ReenableLegacy(GameObject oldGroup)
        {
            var list = oldGroup.transform.Find(DisabledListName);
            if (list == null) return;
            foreach (Transform t in list)
                if (GlobalObjectId.TryParse(t.name, out var id) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is Collider c) c.enabled = true;
        }

        static bool PhysicsSees(Collider c)
        {
            return Physics.OverlapSphere(c.bounds.center, 0.05f, ~0, QueryTriggerInteraction.Ignore).Contains(c);
        }

        static bool IsReplaced(Collider c, HashSet<Renderer> replaced)
        {
            var r = c.GetComponent<MeshRenderer>();
            return r != null && replaced.Contains(r);
        }

        /// <summary>At least half of an inner sample grid lies within 15 cm of a generated collider.</summary>
        static bool CoveredByGenerated(Collider c, HashSet<Collider> made, bool physicsOk)
        {
            int hit = 0, n = 0;
            foreach (var pt in MapCollisionQA.InnerPoints(c))
            {
                n++;
                if (physicsOk ? Physics.OverlapSphere(pt, 0.15f, ~0, QueryTriggerInteraction.Ignore).Any(made.Contains) : made.Any(g => g.bounds.Contains(pt))) hit++;
            }
            return n > 0 && hit * 2 >= n;
        }

        /// <summary>Boundary box: at least 3 m tall (bottom stays put). Returns true if changed.</summary>
        static bool RaiseBoundary(BoxCollider b)
        {
            float sy = Mathf.Abs(b.transform.lossyScale.y);
            if (sy < 1e-4f || b.bounds.size.y >= MapColliderRules.MinBoundaryHeight) return false;
            float bottom = b.center.y - b.size.y * 0.5f, h = (MapColliderRules.MinBoundaryHeight + 0.2f) / sy;
            b.size = new Vector3(b.size.x, h, b.size.z);
            b.center = new Vector3(b.center.x, bottom + h * 0.5f, b.center.z);
            return true;
        }

        /// <summary>Thin long box: thickness 0.15 m (rails also 1.1 m tall). Flat slab just over stepOffset: trimmed to 0.28 m.</summary>
        static bool FixThinOrStep(BoxCollider b)
        {
            var sc = b.transform.lossyScale;
            var abs = new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z));
            if (abs.x < 1e-4f || abs.y < 1e-4f || abs.z < 1e-4f) return false;
            var size = Vector3.Scale(b.size, abs);
            var center = Vector3.Scale(b.center, sc);
            var before = size;
            int upAxis = MapColliderRules.UpAxis(b.transform.rotation, out float upSign);
            string name = b.gameObject.name;
            if (MapColliderRules.IsRailName(name) || name.IndexOf("guard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                size = MapColliderRules.EnforceRailSize(size, upAxis, upSign, out Vector3 shift);
                center += shift;
            }
            else
            {
                size = MapColliderRules.Thicken(size, upAxis);
                if (upAxis == 1) TrimStepSlab(ref size, ref center);
            }
            if (size == before) return false;
            b.size = new Vector3(size.x / abs.x, size.y / abs.y, size.z / abs.z);
            b.center = new Vector3(center.x / sc.x, center.y / sc.y, center.z / sc.z);
            return true;
        }

        static bool IsLegacyStatic(Collider c)
        {
            if (c.GetComponentsInParent<Transform>(true).Any(t => t.name == ManualGroupName || t.name == GroupName)) return false;
            return c.GetComponents<Component>().All(k => k is Transform || k is BoxCollider || k is MeshCollider || k is MeshFilter || k is MeshRenderer);
        }
    }

    /// <summary>Welds vertices by position (1 mm), unions triangles into connected components, returns local boxes; big blobs are cut into grid cells.</summary>
    public static class MeshSplitter
    {
        static bool Big(Bounds b, float bigExtent) { return Mathf.Max(b.size.x, b.size.z) > bigExtent && Mathf.Min(b.size.x, b.size.z) > 0.6f; }

        public static List<Bounds> Split(Mesh m, float bigExtent, float cell)
        {
            var v = m.vertices;
            var t = m.triangles;
            var weld = new Dictionary<long, int>();
            var rep = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                long key = ((long)Mathf.RoundToInt(v[i].x * 1000f) * 73856093L) ^ ((long)Mathf.RoundToInt(v[i].y * 1000f) * 19349663L) ^ ((long)Mathf.RoundToInt(v[i].z * 1000f) * 83492791L);
                if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld[key] = id; }
                rep[i] = id;
            }
            var parent = Enumerable.Range(0, weld.Count).ToArray();
            for (int i = 0; i + 2 < t.Length; i += 3) { Union(parent, rep[t[i]], rep[t[i + 1]]); Union(parent, rep[t[i + 1]], rep[t[i + 2]]); }
            var root = new int[t.Length / 3];
            var bounds = new Dictionary<int, Bounds>();
            for (int i = 0; i < root.Length; i++)
            {
                root[i] = Find(parent, rep[t[i * 3]]);
                Encap(bounds, root[i], v[t[i * 3]], v[t[i * 3 + 1]], v[t[i * 3 + 2]]);
            }
            var cells = new Dictionary<long, Bounds>();
            for (int i = 0; i < root.Length; i++)
            {
                if (!Big(bounds[root[i]], bigExtent)) continue;
                var c = (v[t[i * 3]] + v[t[i * 3 + 1]] + v[t[i * 3 + 2]]) / 3f;
                long key = ((long)root[i] << 40) ^ ((long)(Mathf.FloorToInt(c.x / cell) + 100000) << 20) ^ (long)(Mathf.FloorToInt(c.z / cell) + 100000);
                if (!cells.TryGetValue(key, out var cb)) cb = new Bounds(v[t[i * 3]], Vector3.zero);
                cb.Encapsulate(v[t[i * 3]]); cb.Encapsulate(v[t[i * 3 + 1]]); cb.Encapsulate(v[t[i * 3 + 2]]);
                cells[key] = cb;
            }
            var list = bounds.Values.Where(b => !Big(b, bigExtent)).ToList();
            list.AddRange(cells.Values);
            return list;
        }

        static void Encap(Dictionary<int, Bounds> d, int r, Vector3 a, Vector3 b, Vector3 c)
        {
            if (!d.TryGetValue(r, out var bb)) bb = new Bounds(a, Vector3.zero);
            bb.Encapsulate(a); bb.Encapsulate(b); bb.Encapsulate(c);
            d[r] = bb;
        }

        static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }
        static void Union(int[] p, int a, int b) { a = Find(p, a); b = Find(p, b); if (a != b) p[a] = b; }
    }
}
