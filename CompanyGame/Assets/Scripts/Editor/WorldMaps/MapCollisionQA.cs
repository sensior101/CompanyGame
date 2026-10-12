using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>
    /// Measurements 1-5 of v2 section 4 (edit mode, read-only) and the Markdown report; measurement 6 is the separate Play Mode walk test.
    /// Edit-mode Physics.Raycast must see the opened scene's colliders; Measure() self-checks this and the report says so if it fails.
    /// </summary>
    public static class MapCollisionQA
    {
        public const float SampleTolerance = 0.15f, FloorTolerance = 0.05f;
        const int TriangleSamples = 24, TopN = 8;
        static readonly Regex BuildingHolderRx = new Regex("Building|Retail|Compan|Headquarters|HQ|OneRoom|Bakery|Props|Station|Bridge|Villa|Shop|Tower|Mall|Apartment|House|Hospital|Police|Office|Community|FireStation|Vacant|Terrain|Ground", RegexOptions.IgnoreCase);

        public struct Offender { public Symptom Symptom; public string Path, Detail; public float Score; }

        public sealed class Result
        {
            public bool PhysicsEmpty;
            public int BuildingRenderers, BuildingSamples, BuildingMissing, ApproxMeshColliders, UnreadableMeshes;
            public int BoxesChecked, BoxesHollow;
            public int FloorSamples, FloorNoCollider, FloorOff, FloorUnmatched;
            public int Curbs, CurbsClimbable, CurbsTrap, CurbsWall, StepColliders;
            public int Rails, RailsNoCollider, RailsThin, RailsLow, ThinColliders;
            public int InvisibleBoxes, BoundaryLow, InnerBlockers;
            public List<string> Merged = new List<string>();
            public List<Offender> Offenders = new List<Offender>();
        }

        [MenuItem("Tools/Company Game/Maps/Colliders/QA (same dry-run report)")]
        public static void Run() { MapColliderRebuilder.Report(); }

        // ---------- measurements 1-5 ----------

        public static Result Measure(MapScan scan)
        {
            var r = new Result();
            r.PhysicsEmpty = !PhysicsSeesColliders(scan.Existing);
            MeasureWalls(scan, r);
            MeasureHollowBoxes(scan, r);
            MeasureFloors(scan, r);
            MeasureCurbs(scan, r);
            MeasureRails(scan, r);
            MeasureInvisible(scan, r);
            foreach (var p in scan.Plans.Where(p => p.Kind == ColliderKind.Merged))
                r.Merged.Add(p.Path + " [" + Size(p.World) + "]");
            return r;
        }

        static bool PhysicsSeesColliders(List<Collider> existing)
        {
            var box = existing.FirstOrDefault(c => c is BoxCollider);
            if (box == null) return false;
            Physics.SyncTransforms();
            var b = box.bounds;
            return Physics.Raycast(new Vector3(b.center.x, b.max.y + 1f, b.center.z), Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
        }

        // 1a. building walls: visual triangle centroids vs nearest collider
        static void MeasureWalls(MapScan scan, Result r)
        {
            var cols = scan.Existing.ToArray();
            var grown = cols.Select(c => Grow(c.bounds, SampleTolerance)).ToArray();
            foreach (var p in scan.Plans)
            {
                if (p.Status == Coverage.Protrudes)
                    Add(r, Symptom.WallPassThrough, p.Path, "visual sticks out " + p.Protrusion.ToString("0.0") + " m beyond covering collider [" + Size(p.World) + "]", p.Protrusion);
                else if (p.Status == Coverage.Missing && p.Kind == ColliderKind.Building)
                    Add(r, Symptom.WallPassThrough, p.Path, "no collider under centre [" + Size(p.World) + "]", p.World.size.magnitude);
                if (p.Kind != ColliderKind.Building) continue;
                r.BuildingRenderers++;
                int total = 0, bad = 0;
                foreach (var pt in SamplePoints(p, r))
                {
                    total++;
                    if (!Covered(pt, cols, grown, r)) bad++;
                }
                r.BuildingSamples += total;
                r.BuildingMissing += bad;
                if (total >= 4 && bad * 2 >= total)
                    Add(r, Symptom.WallPassThrough, p.Path, bad + "/" + total + " surface samples > 0.15 m from any collider [" + Size(p.World) + "]", (float)bad / total * p.World.size.magnitude);
            }
        }

        static bool Covered(Vector3 pt, Collider[] cols, Bounds[] grown, Result r)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                if (!grown[i].Contains(pt)) continue;
                var mc = cols[i] as MeshCollider;
                if (mc != null && !mc.convex) { r.ApproxMeshColliders++; return true; }   // ponytail: non-convex mesh collider judged by bounds only; exact test needs a physics query
                if (((cols[i].ClosestPoint(pt)) - pt).sqrMagnitude <= SampleTolerance * SampleTolerance) return true;
            }
            return false;
        }

        static IEnumerable<Vector3> SamplePoints(PlannedCollider p, Result r)
        {
            var m = p.Mesh;
            var l2w = p.Source.transform.localToWorldMatrix;
            if (m == null || !m.isReadable)
            {
                r.UnreadableMeshes++;
                yield return p.World.center;
                yield break;
            }
            var tris = m.triangles;
            var v = m.vertices;
            int count = tris.Length / 3, stride = Mathf.Max(1, count / TriangleSamples);
            for (int t = 0; t < count; t += stride)
                yield return l2w.MultiplyPoint3x4((v[tris[t * 3]] + v[tris[t * 3 + 1]] + v[tris[t * 3 + 2]]) / 3f);
        }

        // 1b. over-coverage: solid boxes whose interior is far from every renderer (invisible walls)
        static void MeasureHollowBoxes(MapScan scan, Result r)
        {
            var visual = scan.Plans.Select(p => p.Source).Distinct().Select(rend => Grow(rend.bounds, 0.5f)).ToArray();   // whole renderers, not split parts
            foreach (var box in scan.Existing.OfType<BoxCollider>().Where(b => !InGenerated(b) && !IsBoundaryBox(b)))
            {
                r.BoxesChecked++;
                int air = 0, n = 0;
                foreach (var pt in InnerGrid(box))
                {
                    n++;
                    if (!visual.Any(b => b.Contains(pt))) air++;
                }
                if (air * 2 < n) continue;
                r.BoxesHollow++;
                var s = box.bounds.size;
                Add(r, Symptom.InvisibleWall, MapColliderRebuilder.PathOf(box.transform), air + "/" + n + " interior samples farther than 0.5 m from any renderer [" + Size(box.bounds) + "]", (float)air / n * s.x * s.y * s.z);
            }
        }

        static IEnumerable<Vector3> InnerGrid(BoxCollider box)
        {
            foreach (float x in new[] { -0.33f, 0f, 0.33f })
                foreach (float y in new[] { -0.33f, 0f, 0.33f })
                    foreach (float z in new[] { -0.33f, 0f, 0.33f })
                        yield return box.transform.TransformPoint(box.center + Vector3.Scale(box.size, new Vector3(x, y, z)));
        }

        // 2. floors: visual surface height vs the height a ray finds in the real colliders
        static void MeasureFloors(MapScan scan, Result r)
        {
            foreach (var p in scan.Plans.Where(p => p.Kind == ColliderKind.Floor))
            {
                float worst = 0f; int missing = 0, off = 0, n = 0, unmatched = 0;
                foreach (var xz in GridXZ(p.World))
                {
                    if (!scan.Floors.TryTopBelow(xz.x, xz.y, p.World.max.y + 0.01f, out float vy)) continue;
                    n++;
                    float delta;
                    var hits = Physics.RaycastAll(new Vector3(xz.x, vy + 0.5f, xz.y), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Length > 0) delta = hits.OrderByDescending(h => h.point.y).First().point.y - vy;
                    else { missing++; delta = -9.99f; }
                    if (Mathf.Abs(delta) > FloorTolerance) off++;
                    if (!hits.Any(h => Mathf.Abs(h.point.y - vy) <= FloorTolerance)) unmatched++;
                    if (Mathf.Abs(delta) > Mathf.Abs(worst)) worst = delta;
                }
                r.FloorSamples += n; r.FloorNoCollider += missing; r.FloorOff += off; r.FloorUnmatched += unmatched;
                if (unmatched > 0)
                    Add(r, Symptom.RoadFloatSink, p.Path, unmatched + "/" + n + " samples with no collider within 5 cm of the visual (top hit worst " + worst.ToString("+0.00;-0.00") + " m, " + missing + " with no collider) [" + Size(p.World) + "]", Mathf.Abs(worst) + off);
            }
        }

        static IEnumerable<Vector2> GridXZ(Bounds b)
        {
            int nx = Mathf.Clamp(Mathf.CeilToInt(b.size.x / 2f), 1, 8), nz = Mathf.Clamp(Mathf.CeilToInt(b.size.z / 2f), 1, 8);
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                    yield return new Vector2(b.min.x + b.size.x * (i + 0.5f) / nx, b.min.z + b.size.z * (k + 0.5f) / nz);
        }

        // 3. curbs and step-sized colliders
        static void MeasureCurbs(MapScan scan, Result r)
        {
            foreach (var p in scan.Plans.Where(p => p.Kind == ColliderKind.Curb))
            {
                r.Curbs++;
                float h = p.World.size.y;
                if (h <= MapColliderRules.MaxClimbableCurb) r.CurbsClimbable++;
                else if (h <= 0.6f) { r.CurbsTrap++; Add(r, Symptom.CurbStuck, p.Path, "curb " + (h * 100).ToString("0") + " cm: " + MapColliderRules.CurbBucket(h), 1f); }
                else r.CurbsWall++;
            }
            foreach (var c in scan.Existing.OfType<BoxCollider>())
            {
                var s = c.bounds.size;
                if (s.y <= MapColliderRules.StepSlabHeight + 0.005f || s.y > 0.6f || s.x < 0.3f || s.z < 0.3f) continue;
                r.StepColliders++;
                Add(r, Symptom.CurbStuck, MapColliderRebuilder.PathOf(c.transform), "collider step " + (s.y * 100).ToString("0") + " cm (CC stepOffset 23 -> 30 cm) [" + Size(c.bounds) + "]", 1f / (0.05f + Mathf.Abs(s.y - 0.3f)));
            }
        }

        // 4. rails and glass
        static void MeasureRails(MapScan scan, Result r)
        {
            foreach (var p in scan.Plans.Where(p => p.Kind == ColliderKind.Rail || p.Kind == ColliderKind.Glass))
            {
                r.Rails++;
                var grown = Grow(p.World, 0.2f);
                var cover = scan.Existing.Where(c => c.bounds.Intersects(grown)).OrderBy(c => (c.bounds.center - p.World.center).sqrMagnitude).FirstOrDefault();   // the collider closest to this rail
                if (cover == null)
                {
                    r.RailsNoCollider++;
                    Add(r, Symptom.RailFall, p.Path, p.Kind + " has no collider [" + Size(p.World) + "]", p.World.size.magnitude + 1f);
                    continue;
                }
                var cs = cover.bounds.size;
                float thick = Mathf.Min(cs.x, cs.z);
                bool thin = thick < MapColliderRules.MinRailThickness - 0.002f, low = cs.y < MapColliderRules.MinRailHeight - 0.002f;
                if (thin) r.RailsThin++;
                if (low) r.RailsLow++;
                if (thin || low) Add(r, Symptom.RailFall, p.Path, p.Kind + " collider " + (thick * 100).ToString("0") + " cm thick x " + cs.y.ToString("0.00") + " m high", 1f);
            }
            foreach (var c in scan.Existing.OfType<BoxCollider>())
            {
                var s = c.bounds.size;
                if (Mathf.Min(s.x, s.z) >= MapColliderRules.MinRailThickness - 0.002f || Mathf.Max(s.x, s.z) <= 1f) continue;
                r.ThinColliders++;
                Add(r, Symptom.RailFall, MapColliderRebuilder.PathOf(c.transform), "thin collider " + (Mathf.Min(s.x, s.z) * 100).ToString("0") + " cm x " + s.y.ToString("0.00") + " m high (tunnelling risk) [" + Size(c.bounds) + "]", 10f / Mathf.Max(0.01f, Mathf.Min(s.x, s.z)));
            }
        }

        // 5. invisible boxes: boundary height and boxes standing inside the walkable area
        static void MeasureInvisible(MapScan scan, Result r)
        {
            foreach (var c in scan.Existing)
            {
                if (InGenerated(c) || c.GetComponentInChildren<Renderer>() != null) continue;   // generated colliders follow visuals by construction
                r.InvisibleBoxes++;
                var b = c.bounds;
                string path = MapColliderRebuilder.PathOf(c.transform);
                bool named = MapColliderRules.BoundaryNameRx.IsMatch(c.gameObject.name);
                bool edge = IsEdgeShape(b);
                if ((named || edge) && b.size.y < MapColliderRules.MinBoundaryHeight)
                {
                    r.BoundaryLow++;
                    Add(r, Symptom.InvisibleWall, path, (edge ? "map-edge wall" : "boundary box") + " only " + b.size.y.ToString("0.0") + " m tall [" + Size(b) + "]", 1f + (edge ? b.size.x + b.size.z : 0f) * 0.01f);
                }
                else if (c is BoxCollider && IsInnerBlocker((BoxCollider)c, scan.Floors))
                {
                    r.InnerBlockers++;
                    Add(r, Symptom.InvisibleWall, path, "invisible box with floor on all 8 sides (standing in the walkable area) [" + Size(b) + "]", b.size.x * b.size.z);
                }
            }
        }

        public static bool InGenerated(Collider c)
        {
            for (var t = c.transform; t != null; t = t.parent) if (t.name == MapColliderRebuilder.GroupName) return true;
            return false;
        }

        static bool IsEdgeShape(Bounds b) { return Mathf.Min(b.size.x, b.size.z) < 1.5f && Mathf.Max(b.size.x, b.size.z) > 50f; }

        /// <summary>Renderer-less box that is a map-boundary wall (by name or long thin shape).</summary>
        public static bool IsBoundaryBox(BoxCollider c)
        {
            return c.GetComponentInChildren<Renderer>() == null && (MapColliderRules.BoundaryNameRx.IsMatch(c.gameObject.name) || IsEdgeShape(c.bounds));
        }

        /// <summary>Renderer-less, non-boundary box with walkable floor on all 8 sides (and not inside a building/props holder).</summary>
        public static bool IsInnerBlocker(BoxCollider c, FloorIndex floors)
        {
            if (c.GetComponentInChildren<Renderer>() != null || IsBoundaryBox(c)) return false;
            string path = MapColliderRebuilder.PathOf(c.transform);
            return !BuildingHolderRx.IsMatch(path.Substring(0, Mathf.Max(0, path.LastIndexOf('/')))) && SurroundedByFloor(floors, c.bounds);
        }

        /// <summary>27 sample points inside a collider (box: oriented grid; others: bounds grid).</summary>
        public static IEnumerable<Vector3> InnerPoints(Collider c)
        {
            var box = c as BoxCollider;
            if (box != null) return InnerGrid(box);
            var b = c.bounds;
            var pts = new List<Vector3>();
            foreach (float x in new[] { -0.33f, 0f, 0.33f })
                foreach (float y in new[] { -0.33f, 0f, 0.33f })
                    foreach (float z in new[] { -0.33f, 0f, 0.33f })
                        pts.Add(b.center + Vector3.Scale(b.size, new Vector3(x, y, z)));
            return pts;
        }

        public static bool SurroundedByFloor(FloorIndex floors, Bounds b)
        {
            float d = Mathf.Max(b.extents.x, b.extents.z) + 1.5f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                if (!floors.TryTopBelow(b.center.x + Mathf.Cos(a) * d, b.center.z + Mathf.Sin(a) * d, b.max.y, out float y) || y < b.min.y - 1.5f) return false;
            }
            return true;
        }

        static void Add(Result r, Symptom s, string path, string detail, float score)
        {
            r.Offenders.Add(new Offender { Symptom = s, Path = path, Detail = detail, Score = score });
        }

        static Bounds Grow(Bounds b, float m) { b.Expand(m * 2f); return b; }
        static string Size(Bounds b) { var s = b.size; return s.x.ToString("0.0") + " x " + s.y.ToString("0.00") + " x " + s.z.ToString("0.0") + " @ (" + b.center.x.ToString("0") + "," + b.min.y.ToString("0.0") + "," + b.center.z.ToString("0") + ")"; }

        // ---------- report ----------

        public static string BuildReport(List<MapScan> scans)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Collision report (dry run)");
            sb.AppendLine();
            sb.AppendLine("Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " by MapColliderRebuilder.Report. Scenes were opened additively and closed without saving; no scene, prefab or ProjectSettings file was changed. Colliders under Colliders_Generated are scanned like any other (so after stage 2 they count as 'existing').");
            sb.AppendLine("Measurements follow v2 section 4 items 1-5. Item 6 (Play Mode walk test) is a separate command (MapCollisionQA.RunWalkTest).");
            sb.AppendLine();
            sb.AppendLine("## Collision rule table (target state)");
            sb.AppendLine();
            sb.AppendLine(MapColliderRules.RuleTableMarkdown());
            AppendSummary(sb, scans);
            AppendSymptomMap(sb, scans);
            foreach (var s in scans) AppendMap(sb, s);
            AppendLimits(sb);
            return sb.ToString();
        }

        static void AppendSummary(StringBuilder sb, List<MapScan> scans)
        {
            sb.AppendLine("## Summary per map");
            sb.AppendLine();
            sb.AppendLine("| Map | Renderers | Colliders now | Plan Box / Mesh / none / merged | Plan matched / missing / protruding | Wall samples missing | Floor off > 5 cm (top hit) | Floor unmatched | Curbs climb / trap / wall | Step colliders (0.285-0.6 m) | Rails no collider / thin / low | Thin colliders | Invisible boxes (low edge, inner) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var s in scans.Where(s => s.Qa != null))
            {
                var q = s.Qa;
                sb.AppendLine("| " + s.Name + " | " + s.RendererCount + " | " + s.Existing.Count + " | "
                    + Count(s, ColliderShape.Box) + " / " + Count(s, ColliderShape.Mesh) + " / " + s.Plans.Count(p => p.Shape == ColliderShape.None && p.Kind != ColliderKind.Merged) + " / " + s.Plans.Count(p => p.Kind == ColliderKind.Merged) + " | "
                    + s.Plans.Count(p => p.Status == Coverage.Matched) + " / " + s.Plans.Count(p => p.Status == Coverage.Missing) + " / " + s.Plans.Count(p => p.Status == Coverage.Protrudes) + " | "
                    + Pct(q.BuildingMissing, q.BuildingSamples) + " | " + Pct(q.FloorOff, q.FloorSamples) + " | " + Pct(q.FloorUnmatched, q.FloorSamples) + " | "
                    + q.CurbsClimbable + " / " + q.CurbsTrap + " / " + q.CurbsWall + " | " + q.StepColliders + " | "
                    + q.RailsNoCollider + " / " + q.RailsThin + " / " + q.RailsLow + " (of " + q.Rails + ") | " + q.ThinColliders + " | "
                    + q.InvisibleBoxes + " (" + q.BoundaryLow + ", " + q.InnerBlockers + ") |");
            }
            foreach (var s in scans.Where(s => s.Error != null)) sb.AppendLine("| " + s.Name + " | ERROR: " + s.Error + " |");
            sb.AppendLine();
            sb.AppendLine("Acceptance targets (v2): wall samples missing < 2 %, hollow boxes < 5 %, floor off > 5 cm < 1 %, all rails >= 0.15 m thick and >= 1.1 m high, blocked walk points 0.");
            sb.AppendLine();
        }

        static int Count(MapScan s, ColliderShape shape) { return s.Plans.Count(p => p.Shape == shape); }
        static string Pct(int a, int b) { return b == 0 ? "n/a" : a + "/" + b + " (" + (100f * a / b).ToString("0.0") + "%)"; }

        static void AppendSymptomMap(StringBuilder sb, List<MapScan> scans)
        {
            sb.AppendLine("## Symptoms mapped to measurements");
            sb.AppendLine();
            sb.AppendLine("| User symptom | Measurement | Total offenders (all maps) |");
            sb.AppendLine("|---|---|---|");
            foreach (Symptom sym in Enum.GetValues(typeof(Symptom)))
                sb.AppendLine("| " + MapColliderRules.SymptomLabel(sym) + " | " + Measurement(sym) + " | " + scans.Where(s => s.Qa != null).Sum(s => s.Qa.Offenders.Count(o => o.Symptom == sym)) + " |");
            sb.AppendLine();
        }

        static string Measurement(Symptom s)
        {
            switch (s)
            {
                case Symptom.WallPassThrough: return "1a: visual surface samples far from any collider; renderers protruding beyond their covering box";
                case Symptom.RoadFloatSink: return "2: visual floor height vs collider height on a 2 m grid";
                case Symptom.InvisibleWall: return "1b: hollow boxes; 5: low boundary/edge walls and invisible boxes inside the walkable area";
                case Symptom.CurbStuck: return "3: curb heights and 23-60 cm step colliders versus stepOffset";
                default: return "4: rails/glass without collider, thinner than 15 cm or lower than 1.1 m; thin colliders";
            }
        }

        static void AppendMap(StringBuilder sb, MapScan s)
        {
            sb.AppendLine("## " + s.Name);
            sb.AppendLine();
            if (s.Error != null || s.Qa == null) { sb.AppendLine("Scan failed: " + s.Error); sb.AppendLine(); return; }
            var q = s.Qa;
            sb.AppendLine("GameObjects " + s.GameObjectCount + ", renderers " + s.RendererCount + ", solid colliders " + s.Existing.Count
                + " (Box " + s.Existing.Count(c => c is BoxCollider) + ", Mesh " + s.Existing.Count(c => c is MeshCollider) + "). Kinds: "
                + string.Join(", ", s.Plans.GroupBy(p => p.Kind).Select(g => g.Key + " " + g.Count())) + ".");
            if (q.PhysicsEmpty) sb.AppendLine("**WARNING: edit-mode Physics.Raycast did not hit the map's own colliders; floor measurement (2) is unreliable. Re-run with the scene loaded in the editor.**");
            if (s.DirtyAfterScan) sb.AppendLine("**WARNING: the scene reported dirty after the scan (it was not saved).**");
            sb.AppendLine("Samples: " + q.BuildingSamples + " wall points (" + q.ApproxMeshColliders + " judged by mesh-collider bounds only, " + q.UnreadableMeshes + " unreadable meshes), " + q.FloorSamples + " floor points, " + q.BoxesChecked + " boxes checked for hollow interior (" + q.BoxesHollow + " hollow).");
            sb.AppendLine();
            foreach (Symptom sym in Enum.GetValues(typeof(Symptom)))
            {
                var worst = q.Offenders.Where(o => o.Symptom == sym).OrderByDescending(o => o.Score).Take(TopN).ToList();
                sb.AppendLine("**" + MapColliderRules.SymptomLabel(sym) + "** (" + q.Offenders.Count(o => o.Symptom == sym) + " offenders)");
                if (worst.Count == 0) { sb.AppendLine(); sb.AppendLine("none found"); sb.AppendLine(); continue; }
                sb.AppendLine();
                sb.AppendLine("| Object path | Measured |");
                sb.AppendLine("|---|---|");
                foreach (var o in worst) sb.AppendLine("| " + Cell(o.Path) + " | " + Cell(o.Detail) + " |");
                sb.AppendLine();
            }
            if (s.Split.Count > 0)
            {
                sb.AppendLine("**Merged / multi-part meshes split in memory into connected components**: " + s.Split.Count);
                sb.AppendLine();
                foreach (var m in s.Split.Take(TopN)) sb.AppendLine("- " + Cell(m));
                sb.AppendLine();
            }
            if (q.Merged.Count > 0)
            {
                sb.AppendLine("**Merged renderers (cannot be classified by name; need splitting in stage 2)**: " + q.Merged.Count);
                sb.AppendLine();
                foreach (var m in q.Merged.Take(TopN)) sb.AppendLine("- " + Cell(m));
                sb.AppendLine();
            }
        }

        static string Cell(string s) { return s.Replace("|", "/").Replace("\n", " "); }

        static void AppendLimits(StringBuilder sb)
        {
            sb.AppendLine("## Limits of this measurement");
            sb.AppendLine();
            sb.AppendLine("- Names decide the class (rail, curb, floor...). Merged-by-material renderers (district maps) are listed but not classified.");
            sb.AppendLine("- Collider bounds are axis-aligned; rotated boxes are over-approximated. Non-convex mesh colliders are judged by bounds in measurement 1a.");
            sb.AppendLine("- Floor height uses the visual triangles of floor-class renderers; stairs count as floors for ground detection only.");
            sb.AppendLine("- Prefab-internal colliders appear only if the prefab is instantiated in the scene (they are scanned like any other object).");
            sb.AppendLine("- Whether a stretch is walkable in practice needs measurement 6 (Play Mode walk test).");
        }

        // ---------- measurement 6: Play Mode walk test ----------

        // Batch driver (same pattern as CitizenPlaySmoke): -executeMethod CompanyGame.Editor.WorldMaps.MapCollisionQA.RunWalkTestBatch -walkMaps daldongnaemap,BusinessDistrict (no -quit; exits by itself).
        const string WalkActive = "MapWalk.active", WalkIndex = "MapWalk.index", WalkMaps = "MapWalk.maps", WalkFails = "MapWalk.fails";
        static bool walkStarted;

        [InitializeOnLoadMethod]
        static void ResumeWalkBatch()
        {
            if (SessionState.GetBool(WalkActive, false)) EditorApplication.update += WalkTick;
        }

        public static void RunWalkTestBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-walkMaps");
            string list = i >= 0 && i + 1 < args.Length ? args[i + 1] : "daldongnaemap,BusinessDistrict";
            SessionState.SetString(WalkMaps, list);
            SessionState.SetInt(WalkIndex, 0);
            SessionState.SetInt(WalkFails, 0);
            SessionState.SetBool(WalkActive, true);
            EditorApplication.update += WalkTick;
        }

        static string WalkScenePath(string name)
        {
            return MapColliderRebuilder.Maps.First(m => Path.GetFileNameWithoutExtension(m) == name);
        }

        static void WalkTick()
        {
            string[] maps = SessionState.GetString(WalkMaps, "").Split(',');
            int index = SessionState.GetInt(WalkIndex, 0);
            if (index >= maps.Length) { WalkFinish(); return; }
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(WalkScenePath(maps[index]), UnityEditor.SceneManagement.OpenSceneMode.Single);
                walkStarted = false;
                EditorApplication.isPlaying = true;
                return;
            }
            if (walkStarted || Time.frameCount < 10) return;
            walkStarted = true;
            try
            {
                foreach (var g in SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == "Player" || g.name == "NPCs")) g.SetActive(false);
                Physics.SyncTransforms();
                RunWalkTest();
            }
            catch (Exception e) { Debug.LogError("[MapCollisionQA] walk test failed: " + e); SessionState.SetInt(WalkFails, SessionState.GetInt(WalkFails, 0) + 1); }
            SessionState.SetInt(WalkIndex, index + 1);
            EditorApplication.isPlaying = false;
        }

        static void WalkFinish()
        {
            EditorApplication.update -= WalkTick;
            SessionState.SetBool(WalkActive, false);
            int fails = SessionState.GetInt(WalkFails, 0);
            Debug.Log("[MapCollisionQA] walk batch done, failures " + fails);
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        /// <summary>Call in Play Mode with a map loaded. Walks straight 10 m segments on an 8 m grid with a CharacterController (new rule values) and lists blocked segments.</summary>
        [MenuItem("Tools/Company Game/Maps/Colliders/Walk test (Play Mode)")]
        public static void RunWalkTest()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode with a map loaded first.");
            var cols = UnityEngine.Object.FindObjectsByType<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
            if (cols.Length == 0) throw new InvalidOperationException("No colliders in the active scene.");
            var area = cols[0].bounds;
            foreach (var c in cols) area.Encapsulate(c.bounds);
            var probe = new GameObject("WalkTestProbe");
            var cc = probe.AddComponent<CharacterController>();
            cc.radius = MapColliderRules.CharacterRadius; cc.height = MapColliderRules.CharacterHeight; cc.center = Vector3.up * 0.9f;
            cc.stepOffset = MapColliderRules.StepOffset; cc.skinWidth = MapColliderRules.SkinWidth; cc.slopeLimit = MapColliderRules.SlopeLimit;
            var lines = new List<string>();
            int walked = 0;
            try { walked = WalkGrid(cc, area, lines); }
            finally { UnityEngine.Object.Destroy(probe); }
            WriteWalkReport(SceneManager.GetActiveScene().name, walked, lines);
        }

        static int WalkGrid(CharacterController cc, Bounds area, List<string> lines)
        {
            int walked = 0;
            for (float a = 4f; a < Mathf.Max(area.size.x, area.size.z); a += 8f)
                for (float b = 0f; b + 10f <= Mathf.Max(area.size.x, area.size.z); b += 10f)
                    foreach (var dir in new[] { Vector3.right, Vector3.forward })
                    {
                        var start = dir == Vector3.right ? new Vector3(area.min.x + b, 0f, area.min.z + a) : new Vector3(area.min.x + a, 0f, area.min.z + b);
                        if (!area.Contains(new Vector3(start.x, area.center.y, start.z)) || !FindStand(cc, area, ref start)) continue;
                        walked++;
                        string blocked = WalkSegment(cc, start, dir);
                        if (blocked != null) lines.Add("| (" + start.x.ToString("0") + ", " + start.y.ToString("0.0") + ", " + start.z.ToString("0") + ") | " + dir + " | " + Cell(blocked) + " |");
                    }
            return walked;
        }

        static bool FindStand(CharacterController cc, Bounds area, ref Vector3 start)
        {
            var hits = Physics.RaycastAll(new Vector3(start.x, area.max.y + 1f, start.z), Vector3.down, area.size.y + 2f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.point.y);
            foreach (var h in hits)
            {
                var p = h.point + Vector3.up * 0.05f;
                if (Physics.CheckCapsule(p + Vector3.up * cc.radius, p + Vector3.up * (cc.height - cc.radius), cc.radius * 0.9f, ~0, QueryTriggerInteraction.Ignore)) continue;
                start = p;
                return true;
            }
            return false;
        }

        static string WalkSegment(CharacterController cc, Vector3 start, Vector3 dir)
        {
            cc.enabled = false; cc.transform.position = start; cc.enabled = true;
            for (int i = 0; i < 20; i++) cc.Move(dir * 0.5f + Vector3.down * 0.1f);
            var moved = cc.transform.position - start; moved.y = 0f;
            if (moved.magnitude >= 5f) return null;
            return "progress " + moved.magnitude.ToString("0.0") + " of 10 m; " + DescribeBlocker(cc.transform.position, dir);
        }

        static string DescribeBlocker(Vector3 pos, Vector3 dir)
        {
            bool knee = Physics.Raycast(pos + Vector3.up * 0.3f, dir, out RaycastHit kh, 1f, ~0, QueryTriggerInteraction.Ignore);
            bool chest = Physics.Raycast(pos + Vector3.up * 1.2f, dir, out RaycastHit ch, 1f, ~0, QueryTriggerInteraction.Ignore);
            var hit = knee ? kh : ch;
            if (!knee && !chest) return "blocker not found by rays (slope or edge?)";
            string kind = knee && !chest ? "LOW OBSTACLE (curb/step trap)" : hit.collider.GetComponentInChildren<Renderer>() == null && hit.collider.GetComponentInParent<Renderer>() == null ? "INVISIBLE (no renderer)" : "wall";
            if (InGenerated(hit.collider))   // generated colliders follow a visible renderer by construction (their own object has none)
                kind = knee && !chest && !MapColliderRules.IsRailName(hit.collider.name) ? "LOW OBSTACLE (curb/step trap)" : "wall (generated from a visual)";
            return kind + ": " + MapColliderRebuilder.PathOf(hit.collider.transform);
        }

        static void WriteWalkReport(string map, int walked, List<string> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Walk test (Play Mode): " + map);
            sb.AppendLine();
            int low = lines.Count(l => l.Contains("LOW OBSTACLE")), invisible = lines.Count(l => l.Contains("INVISIBLE")), unknown = lines.Count(l => l.Contains("blocker not found"));
            sb.AppendLine("Segments walked " + walked + ", blocked " + lines.Count + " (LOW OBSTACLE " + low + ", INVISIBLE " + invisible + ", not found " + unknown + ", other = walls). Blocked includes legitimate building walls; LOW OBSTACLE and INVISIBLE rows are the suspicious ones.");
            sb.AppendLine();
            sb.AppendLine("| Start | Direction | Result |");
            sb.AppendLine("|---|---|---|");
            foreach (var l in lines.OrderBy(l => l.Contains("LOW OBSTACLE") || l.Contains("INVISIBLE") ? 0 : 1).Take(400)) sb.AppendLine(l);
            Debug.Log("[MapCollisionQA] " + map + ": walked " + walked + ", blocked " + lines.Count + ", LOW " + low + ", INVISIBLE " + invisible + ", not-found " + unknown);
            string file = Path.Combine(Path.GetDirectoryName(MapColliderRebuilder.ReportPath()), "collision_walktest_" + map + ".md");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, sb.ToString());
            Debug.Log("[MapCollisionQA] Walk test written: " + file);
        }
    }
}
