using System.Text.RegularExpressions;
using UnityEngine;

namespace CompanyGame.Editor.WorldMaps
{
    public enum ColliderKind { Skip, Building, Floor, Stairs, Curb, Rail, Glass, Merged, Furniture }
    public enum ColliderShape { None, Box, Mesh }
    public enum Symptom { WallPassThrough, RoadFloatSink, InvisibleWall, CurbStuck, RailFall }

    /// <summary>
    /// Collision rule table (v2 section 4, v3 stage 1). Pure functions and constants only.
    /// Vehicle and character rules are implemented elsewhere (bundle A: PlayerMovement / PlayerVehicle); here they are documented and measured.
    /// </summary>
    public static class MapColliderRules
    {
        public const float CharacterRadius = 0.35f, CharacterHeight = 1.8f, StepOffset = 0.5f, SkinWidth = 0.03f, SlopeLimit = 45f;
        public const float MaxClimbableCurb = 0.45f;      // StepOffset - 0.05
        public const float MinRailHeight = 1.1f, MinRailThickness = 0.15f, MinBoundaryHeight = 3f;
        public const float ElevatedHeight = 2.3f;         // parts this far above the ground get no collider (roofs, awnings)
        public const float BoxFillRatio = 0.85f, TinyExtent = 0.3f, MergedExtent = 60f, FloorMaxThickness = 1.5f, FlatDecalHeight = 0.15f, StepSlabHeight = 0.28f;

        const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex MarkingRx = new Regex("marking|tactile|stripe|joint|crosswalk|decal", Opt);
        static readonly Regex GlassRx = new Regex("glass|window|mullion|glaz", Opt);
        static readonly Regex RailRx = new Regex("rail|fence|guard|handrail|balustrade|parapet|barrier", Opt);
        static readonly Regex CurbRx = new Regex("kerb|curb", Opt);
        static readonly Regex StairRx = new Regex("stair|step", Opt);
        static readonly Regex PropRx = new Regex("sign|awning|roof|canopy|leaf|leaves|crown|wire|lamp|neon|letter|flower|shrub|foam|cloud|water|insulator|crossbar|light", Opt);
        static readonly Regex FloorRx = new Regex("street|road|asphalt|perimeter|deck|paving|walk|plaza|sidewalk|pavement|quay|terrace|ground|land|floor|platform|cap$", Opt);
        static readonly Regex TerrainRx = new Regex("terrain|terrace|masonry|island|ground|street|road|asphalt|deck|plaza|walk|pavement", Opt);
        static readonly Regex SplitNameRx = new Regex("rail|plant|planter|edge|glass|fence|guard|barrier|furniture|prop", Opt);
        static readonly Regex NoColliderRx = new Regex("water|sea|marking|stripe|decal|crosswalk|cloud|foam", Opt);
        public static readonly Regex BoundaryNameRx = new Regex("boundary|invisible|blocker|barrier|limit|edge|bound", Opt);

        /// <summary>Classifies one renderer by name, world bounds and height above the local ground.</summary>
        public static ColliderKind Classify(string path, Bounds b, float groundY)
        {
            string name = path.Substring(path.LastIndexOf('/') + 1);
            var s = b.size;
            if (Mathf.Max(s.x, s.y, s.z) < TinyExtent) return ColliderKind.Skip;
            if (MarkingRx.IsMatch(name)) return ColliderKind.Skip;
            if (Mathf.Max(s.x, s.z) > MergedExtent && s.y > FloorMaxThickness) return ColliderKind.Merged;
            if (GlassRx.IsMatch(name)) return ColliderKind.Glass;
            if (RailRx.IsMatch(name)) return ColliderKind.Rail;
            if (CurbRx.IsMatch(name)) return ColliderKind.Curb;
            if (StairRx.IsMatch(name)) return ColliderKind.Stairs;
            if (b.min.y - groundY >= ElevatedHeight || PropRx.IsMatch(name)) return ColliderKind.Skip;
            if (s.y <= FloorMaxThickness && FloorRx.IsMatch(path.Substring(Mathf.Max(0, path.Length - 80)))) return ColliderKind.Floor;
            return ColliderKind.Building;
        }

        public static ColliderShape ShapeFor(ColliderKind kind, float fillRatio)
        {
            switch (kind)
            {
                case ColliderKind.Floor:
                case ColliderKind.Stairs: return ColliderShape.Mesh;
                case ColliderKind.Furniture:
                case ColliderKind.Curb:
                case ColliderKind.Rail:
                case ColliderKind.Glass: return ColliderShape.Box;
                case ColliderKind.Building: return fillRatio >= BoxFillRatio ? ColliderShape.Box : ColliderShape.Mesh;
                default: return ColliderShape.None;
            }
        }

        /// <summary>Merged mesh by name: a whole-mesh floor (terrain, terraces), nothing (water, markings), or null = split into components.</summary>
        public static ColliderKind? MergedByName(string name)
        {
            if (NoColliderRx.IsMatch(name)) return ColliderKind.Skip;
            if (SplitNameRx.IsMatch(name)) return null;
            return TerrainRx.IsMatch(name) ? ColliderKind.Floor : (ColliderKind?)null;
        }

        public static bool IsRailName(string name) { return RailRx.IsMatch(name) || GlassRx.IsMatch(name); }

        /// <summary>Component box (world size) -> kind. Tiny pieces and flat decals get Skip; thin tall pieces are rails.</summary>
        public static ColliderKind ClassifyPart(Vector3 s, float heightAboveGround)
        {
            if (Mathf.Max(s.x, s.y, s.z) < TinyExtent || s.y < FlatDecalHeight) return ColliderKind.Skip;
            if (heightAboveGround >= ElevatedHeight) return ColliderKind.Skip;
            bool thin = Mathf.Min(s.x, s.z) < MinRailThickness && Mathf.Max(s.x, s.z) > 0.5f;
            return thin && s.y >= 0.4f ? ColliderKind.Rail : ColliderKind.Furniture;
        }

        /// <summary>Which local axis of a box points up in the world (-1 when tilted); sign is +1/-1 along that axis. Blender-style imports often have local z up.</summary>
        public static int UpAxis(Quaternion rot, out float sign)
        {
            var axes = new[] { rot * Vector3.right, rot * Vector3.up, rot * Vector3.forward };
            for (int i = 0; i < 3; i++)
            {
                float d = Vector3.Dot(axes[i], Vector3.up);
                if (Mathf.Abs(d) > 0.99f) { sign = Mathf.Sign(d); return i; }
            }
            sign = 1f;
            return -1;
        }

        /// <summary>Any box longer than 1 m: every side except the vertical one at least 0.15 m (tunnelling).</summary>
        public static Vector3 Thicken(Vector3 size, int upAxis)
        {
            if (Mathf.Max(size.x, size.y, size.z) <= 1f) return size;
            for (int i = 0; i < 3; i++) if (i != upAxis && size[i] < MinRailThickness) size[i] = MinRailThickness;
            return size;
        }

        /// <summary>Flat slab or curb 0.26-0.32 m high gets trimmed to 0.28 (kept from when stepOffset was 0.30, so re-applying doesn't change the scenes).</summary>
        public static bool IsStepTrapSlab(Vector3 worldSize)
        {
            return worldSize.y > 0.255f && worldSize.y <= 0.32f && worldSize.y * 3f < Mathf.Max(worldSize.x, worldSize.z);
        }

        /// <summary>Rail/glass boxes: thinnest horizontal side at least 0.15 m, height at least 1.1 m (grown upward, so the centre shifts along the up axis). Sizes are in the box's own axes.</summary>
        public static Vector3 EnforceRailSize(Vector3 size, int upAxis, float upSign, out Vector3 centerShift)
        {
            centerShift = Vector3.zero;
            int a = -1, b = -1;
            for (int i = 0; i < 3; i++) if (i != upAxis) { if (a < 0) a = i; else b = i; }
            int thin = b < 0 || size[a] <= size[b] ? a : b;
            if (upAxis < 0) { for (int i = 0; i < 3; i++) size[i] = Mathf.Max(size[i], MinRailThickness); return size; }   // tilted: no axis is vertical, thicken all
            size[thin] = Mathf.Max(size[thin], MinRailThickness);
            if (upAxis >= 0 && Mathf.Max(size[a], size[b]) < MinRailThickness * 1.5f) { size[a] = Mathf.Max(size[a], MinRailThickness); size[b] = Mathf.Max(size[b], MinRailThickness); }
            if (upAxis >= 0 && size[upAxis] < MinRailHeight)
            {
                centerShift[upAxis] = upSign * (MinRailHeight - size[upAxis]) * 0.5f;
                size[upAxis] = MinRailHeight;
            }
            return size;
        }

        public static string CurbBucket(float height)
        {
            if (height <= MaxClimbableCurb) return "climbable (<= 0.25 m)";
            if (height <= 0.6f) return "trap (0.25-0.6 m, above step offset)";
            return "wall (> 0.6 m)";
        }

        public static string SymptomLabel(Symptom s)
        {
            switch (s)
            {
                case Symptom.WallPassThrough: return "1. walking through building walls";
                case Symptom.RoadFloatSink: return "2. floating/sinking on roads";
                case Symptom.InvisibleWall: return "3. blocked by invisible walls";
                case Symptom.CurbStuck: return "4. stuck on low curbs/steps";
                default: return "5. falling through railings/glass walls";
            }
        }

        /// <summary>Markdown rule table for the report header (what each kind of object must collide as).</summary>
        public static string RuleTableMarkdown()
        {
            return "| Object | Collision rule (target state) | Implemented by |\n|---|---|---|\n"
                + "| Character (walking) | CharacterController r 0.35, h 1.8, stepOffset 0.5 (was 0.23), skinWidth 0.03, slopeLimit 45 | bundle A (PlayerMovement.Awake) |\n"
                + "| Bicycle | existing CharacterController method, r 0.46 | unchanged |\n"
                + "| Car | CharacterController off while driving; body-bounds BoxCast (forward/back) + CheckBox on rotation; ground by wheel raycasts | bundle A (PlayerVehicle) |\n"
                + "| Citizen NPC | capsule r 0.3 + NavMeshAgent; Default layer + Physics.IgnoreCollision between citizens (layers postponed) | bundle E |\n"
                + "| Building wall | mesh volume / bounds volume >= 0.85: tight oriented BoxCollider per part, else static non-convex MeshCollider (visual mesh). Parts >= 2.3 m above ground, and Canopy/Sign/Awning/Roof/leaf/wire/lamp: no collider | MapColliderRebuilder |\n"
                + "| Road / sidewalk / plaza / bridge floor | non-convex MeshCollider of the visual mesh (collision height = visual height). Markings: none | MapColliderRebuilder |\n"
                + "| Stairs | MeshCollider of the visual mesh (each step is below stepOffset) | MapColliderRebuilder |\n"
                + "| Curb | height <= 0.25: box (edge softening optional); 0.25-0.6: flagged as step trap; > 0.6: ordinary box wall | MapColliderRebuilder |\n"
                + "| Railing / glass wall | thin box following visual bounds, thickness >= 0.15 m, height >= 1.1 m (even when the visual is lower) | MapColliderRebuilder |\n"
                + "| Boundary (invisible) wall | height >= 3 m, only outside the walkable area | MapColliderRebuilder / QA 5 |\n"
                + "| Layers | all Default this round (Pedestrian/TrafficVehicle postponed with TagManager/DynamicsManager) | stage 2 |\n"
                + "| Queries in new code | Physics.Raycast/Cast always QueryTriggerInteraction.Ignore; m_QueriesHitTriggers untouched | all bundles |\n"
                + "| Merged meshes | a renderer larger than 60 m wide with height > 1.5 m (district maps merge street furniture/planting/urban detail per material) cannot be classified by name: split into connected components first (stage 2) | stage 2 |\n";
        }
    }
}
