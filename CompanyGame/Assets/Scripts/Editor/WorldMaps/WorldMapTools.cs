using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>Preserves source geometry while organizing the legacy imported village.</summary>
    public static class WorldMapTools
    {
        public const string VillagePrefab = "Assets/Art/Daldongne/WarmVillage/DaldongneWarmTown.prefab";
        const string PartsFolder = "Assets/Art/Daldongne/WarmVillage/Prefabs";
        const string ReportFolder = "Temp/WorldMapMigration";
        static readonly string[] Buildings = { "Bakery", "Cafe", "CityHall", "Convenience", "Snack", "Hamburger", "Laundry", "Gosiwon", "Home_A", "Home_B", "Home_C", "Home_D", "Station" };

        /// <summary>
        /// World bounds of the active building structure and roof, including eaves. Named site paving,
        /// landscaping and detached outdoor furnishings are excluded. Vacant plots retain their full mesh bounds.
        /// </summary>
        public static Bounds MeasureFootprint(Transform root)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy && MapColliderRebuilder.MeshOf(renderer) &&
                    (root.name.IndexOf("VacantPlot", StringComparison.OrdinalIgnoreCase) >= 0 || IsBuildingFootprintPart(root, renderer.transform))).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("No active building structure or plot geometry: " + root.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static bool IsBuildingFootprintPart(Transform root, Transform part)
        {
            // These names are present in the existing library and village assets. Never reject the generic
            // stone/concrete meshes: district models merge structural foundations and walls by material.
            bool roofStructure = Regex.IsMatch(part.name, @"(?:^|_)(?:Parapet|IvoryCornice|Cornice|Eaves?|Canopy|Fascia)(?:_|\.|$)", RegexOptions.IgnoreCase);
            for (var node = part; node && node != root; node = node.parent)
            {
                string name = node.name;
                if ((!roofStructure && name.Equals("40_Details", StringComparison.OrdinalIgnoreCase)) || name.Equals("30_Signs", StringComparison.OrdinalIgnoreCase)) return false;
                if (Regex.IsMatch(name, @"^(?:\d{2}_)?(?:Site(?:_|$)|Landscape(?:_|$)|Tree[^_]*(?:_|$)|Bench[^_]*(?:_|$)|Railings(?:_|$)|Ramp(?:_|$)|EntryStep(?:_|$)|Steps$|Sign(?:_|$)|Lettering(?:_|$))", RegexOptions.IgnoreCase)) return false;
                if (Regex.IsMatch(name, @"(?:^|_)(?:Yard|Courtyard|Garden|Planter|Plants?|Pots?|Patio|DeckBoard|Paving|Sidewalk|GroundPlane|BoundaryWall|Fence|Gate|Walkable_StairRamp)(?:_|\.|$)", RegexOptions.IgnoreCase)) return false;
                if (Regex.IsMatch(name, @"^(?:leaf(?:Light|Dark)?|foliage|grass|bark)$", RegexOptions.IgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>
        /// Sizes one explicitly named vacant plot. Only direct children of the existing building holders
        /// are accepted; map, terrain and group roots must never be passed to a sizing operation.
        /// Keeps height and world bottom centre. Business plots are deliberately left unchanged.
        /// </summary>
        public static Bounds ApplyParcelSize(Transform root, LandPlot.SizeTier tier)
        {
            float side = LandPlot.GetParcelSide(tier);
            RequireSingleSizingRoot(root, true, tier);
            var before = MeasureFootprint(root);
            if (tier == LandPlot.SizeTier.Business) return before;
            RequireNoSizingVehicles(root);
            if (Mathf.Abs(before.size.x - side) <= .001f && Mathf.Abs(before.size.z - side) <= .001f)
                return before;
            return NormalizeFootprint(root, side, side);
        }

        /// <summary>
        /// Shrinks one building only when it exceeds the tier's maximum footprint. Uses one XYZ factor,
        /// preserving all existing proportions, orientation and the world bottom centre; never enlarges it.
        /// Excluded site/furnishing branches retain their world transforms. Vehicles require separate handling.
        /// Separate scene colliders and interaction anchors remain the caller's responsibility.
        /// Business buildings and the legacy NormalizeBuildingFootprint API retain their existing behaviour.
        /// </summary>
        public static Bounds FitBuildingWithinTier(Transform root, LandPlot.SizeTier tier)
        {
            bool hasLimit = LandPlot.TryGetBuildingSide(tier, out float maximumSide);
            RequireSingleSizingRoot(root, false, tier);
            var before = MeasureFootprint(root);
            if (!hasLimit) return before;
            RequireNoSizingVehicles(root);
            float longest = Mathf.Max(before.size.x, before.size.z);
            if (!(longest > .0001f) || float.IsInfinity(longest) || float.IsNaN(longest))
                throw new InvalidOperationException("Building has no finite measurable footprint: " + root.name);
            if (longest <= maximumSide + .001f) return before;

            // A mixed details branch can contain structural cornices. Preserve only branches without any
            // structural renderer, then keep just their outermost roots so compensation is applied once.
            var excluded = root.GetComponentsInChildren<Transform>(true)
                .Where(part => part != root && !IsBuildingFootprintPart(root, part) &&
                    !part.GetComponentsInChildren<MeshRenderer>(true).Any(renderer => IsBuildingFootprintPart(root, renderer.transform)))
                .ToArray();
            var preserved = excluded.Where(part => !excluded.Any(other => other != part && part.IsChildOf(other)))
                .Select(part => (transform: part, localPosition: part.localPosition, localRotation: part.localRotation,
                    localScale: part.localScale, position: part.position, rotation: part.rotation, worldScale: part.lossyScale))
                .ToArray();
            Vector3 oldScale = root.localScale, oldPosition = root.position;
            float factor = maximumSide / longest;
            try
            {
                root.localScale = oldScale * factor;
                var after = MeasureFootprint(root);
                root.position += new Vector3(before.center.x - after.center.x, before.min.y - after.min.y,
                    before.center.z - after.center.z);
                foreach (var item in preserved)
                {
                    item.transform.localScale = item.localScale / factor;
                    item.transform.SetPositionAndRotation(item.position, item.rotation);
                }
                after = MeasureFootprint(root);
                if ((after.size - before.size * factor).sqrMagnitude > .0001f ||
                    Mathf.Abs(after.min.y - before.min.y) > .01f ||
                    Mathf.Abs(after.center.x - before.center.x) > .01f || Mathf.Abs(after.center.z - before.center.z) > .01f ||
                    after.size.x > maximumSide + .01f || after.size.z > maximumSide + .01f)
                    throw new InvalidOperationException("Building fit did not preserve proportions, anchor or maximum footprint: " + root.name);
                foreach (var item in preserved)
                {
                    if ((item.transform.position - item.position).sqrMagnitude > .0001f ||
                        (item.transform.lossyScale - item.worldScale).sqrMagnitude > .0001f ||
                        Quaternion.Angle(item.transform.rotation, item.rotation) > .01f)
                        throw new InvalidOperationException("Building fit changed an excluded site or furnishing branch: " + item.transform.name);
                }
                RecordSizingTransform(root);
                foreach (var item in preserved) RecordSizingTransform(item.transform);
                return after;
            }
            catch
            {
                root.localScale = oldScale;
                root.position = oldPosition;
                foreach (var item in preserved)
                {
                    item.transform.localPosition = item.localPosition;
                    item.transform.localRotation = item.localRotation;
                    item.transform.localScale = item.localScale;
                }
                throw;
            }
        }

        /// <summary>
        /// Sets the longer world-X/Z side to the selected tier's building dimension, enlarging or shrinking
        /// with a single XYZ factor. Preserves proportions, orientation and the world bottom centre.
        /// Excluded branches retain world size/rotation; their authored Transform pivots move relative to
        /// that bottom centre by the same factor. Child geometry within a preserved branch stays intact.
        /// Only loaded scene instances are accepted. BusinessDistrict and the Daldongne Station are unchanged.
        /// Separate colliders, scene-level dressing and interaction anchors must be updated by the caller.
        /// </summary>
        public static Bounds NormalizeBuildingToTier(Transform root, LandPlot.SizeTier tier)
        {
            bool hasTarget = LandPlot.TryGetBuildingSide(tier, out float targetSide);
            RequireSingleSizingRoot(root, false, tier);
            if (EditorUtility.IsPersistent(root) || PrefabUtility.IsPartOfPrefabAsset(root) ||
                !root.gameObject.scene.IsValid() || !root.gameObject.scene.isLoaded ||
                PrefabStageUtility.GetPrefabStage(root.gameObject) != null)
                throw new InvalidOperationException("Tier normalization accepts loaded scene instances only: " + root.name);
            var before = MeasureFootprint(root);
            if (!hasTarget || IsPreservedBuildingInstance(root)) return before;
            RequireNoSizingVehicles(root);
            float longest = Mathf.Max(before.size.x, before.size.z);
            if (!(longest > .0001f) || float.IsInfinity(longest) || float.IsNaN(longest))
                throw new InvalidOperationException("Building has no finite measurable footprint: " + root.name);
            if (Mathf.Abs(longest - targetSide) <= .001f) return before;

            // Reuse the footprint owner's exclusions. A branch that also contains a measured structural
            // renderer (including cornices inside 40_Details) must scale with the building; only its wholly
            // excluded descendant branches can be protected. Outermost selection avoids double compensation.
            var excluded = root.GetComponentsInChildren<Transform>(true)
                .Where(part => part != root && !IsBuildingFootprintPart(root, part) &&
                    !part.GetComponentsInChildren<MeshRenderer>(true).Any(renderer => IsBuildingFootprintPart(root, renderer.transform)))
                .ToArray();
            var preserved = excluded.Where(part => !excluded.Any(other => other != part && part.IsChildOf(other)))
                .Select(part => (transform: part, localPosition: part.localPosition, localRotation: part.localRotation,
                    localScale: part.localScale, position: part.position, rotation: part.rotation, worldScale: part.lossyScale))
                .ToArray();
            Vector3 oldScale = root.localScale, oldPosition = root.position;
            Quaternion oldRotation = root.rotation;
            Vector3 anchor = new Vector3(before.center.x, before.min.y, before.center.z);
            float factor = targetSide / longest;
            try
            {
                root.localScale = oldScale * factor;
                var after = MeasureFootprint(root);
                root.position += anchor - new Vector3(after.center.x, after.min.y, after.center.z);
                foreach (var item in preserved)
                {
                    item.transform.localScale = item.localScale / factor;
                    item.transform.SetPositionAndRotation(anchor + (item.position - anchor) * factor, item.rotation);
                }
                after = MeasureFootprint(root);
                if ((after.size - before.size * factor).sqrMagnitude > .0001f ||
                    Mathf.Abs(Mathf.Max(after.size.x, after.size.z) - targetSide) > .01f ||
                    Mathf.Abs(after.min.y - before.min.y) > .01f ||
                    Mathf.Abs(after.center.x - before.center.x) > .01f || Mathf.Abs(after.center.z - before.center.z) > .01f ||
                    Quaternion.Angle(root.rotation, oldRotation) > .01f)
                    throw new InvalidOperationException("Tier normalization changed proportions, anchor, orientation or target dimension: " + root.name);
                foreach (var item in preserved)
                {
                    Vector3 expected = anchor + (item.position - anchor) * factor;
                    if ((item.transform.position - expected).sqrMagnitude > .0001f ||
                        (item.transform.lossyScale - item.worldScale).sqrMagnitude > .0001f ||
                        Quaternion.Angle(item.transform.rotation, item.rotation) > .01f)
                        throw new InvalidOperationException("Tier normalization failed to preserve excluded branch size, rotation or placement: " +
                            root.name + "/" + AnimationUtility.CalculateTransformPath(item.transform, root));
                }
                RecordSizingTransform(root);
                foreach (var item in preserved) RecordSizingTransform(item.transform);
                return after;
            }
            catch
            {
                root.localScale = oldScale;
                root.SetPositionAndRotation(oldPosition, oldRotation);
                foreach (var item in preserved)
                {
                    item.transform.localPosition = item.localPosition;
                    item.transform.localRotation = item.localRotation;
                    item.transform.localScale = item.localScale;
                }
                throw;
            }
        }

        static bool IsPreservedBuildingInstance(Transform root)
        {
            if (root.gameObject.scene.name == "BusinessDistrict") return true;
            for (var node = root; node; node = node.parent)
            {
                if (node.name == "Map_BusinessDistrict" || node.name == "BusinessReferenceLayout" || node.name == "RiversideRetailDistrict") return true;
                if (root.name == "Station" && (node.name == "Map_daldongnaemap" || node.name == "Daldongne Warm Village")) return true;
            }
            return false;
        }

        static void RequireSingleSizingRoot(Transform root, bool vacant, LandPlot.SizeTier tier)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            var parent = root.parent;
            bool buildingHolder = parent && (parent.name == "Buildings" || parent.name == "10_Buildings" ||
                parent.name == "One Room Neighborhood");
            bool businessHolder = parent && parent.name == "BusinessReferenceLayout" && tier == LandPlot.SizeTier.Business;
            if ((!buildingHolder && !businessHolder) || IsSizingContainer(root.name) ||
                root.GetComponentsInChildren<Transform>(true).Any(part => part != root && IsSizingContainer(part.name)) ||
                root.GetComponentsInChildren<Terrain>(true).Length != 0)
                throw new InvalidOperationException("Sizing requires one building or vacant plot directly beneath an existing building holder: " + root.name);
            bool namedVacant = Regex.IsMatch(root.name, @"^(?:[A-Za-z][A-Za-z0-9]*_)?VacantPlot_(?:[A-Za-z]+_)?[0-9]+$", RegexOptions.IgnoreCase);
            if (vacant != namedVacant || (!vacant && root.name.IndexOf("VacantPlot", StringComparison.OrdinalIgnoreCase) >= 0))
                throw new InvalidOperationException("Use the explicit vacant-plot or building sizing method for this root: " + root.name);
        }

        static bool IsSizingContainer(string name)
        {
            return Regex.IsMatch(name, @"^(?:Map(?:_|$)|Buildings$|10_Buildings$|One Room Neighborhood$|BusinessReferenceLayout$|(?:[0-9]{2}_)?(?:World|Gameplay|Terrain|Roads|RoadsAndPaths|Vehicles|Nature|Props)$)", RegexOptions.IgnoreCase);
        }

        static void RequireNoSizingVehicles(Transform root)
        {
            if (root.GetComponentsInChildren<PlayerVehicle>(true).Length != 0 ||
                root.GetComponentsInChildren<CharacterController>(true).Length != 0 ||
                root.GetComponentsInChildren<Transform>(true).Any(part =>
                    Regex.IsMatch(part.name, @"(?:^|_)(?:Vehicles?|Cars?|Bus(?:es)?|Vans?|Bicycles?|Bikes?|TownBus|RedCar|CreamVan|YellowCar)(?:_|\.|$)", RegexOptions.IgnoreCase)))
                throw new InvalidOperationException("Remove vehicles or characters from the sizing root before changing its footprint: " + root.name);
        }

        static void RecordSizingTransform(Transform transform)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(transform)) PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
            EditorUtility.SetDirty(transform);
        }

        /// <summary>Standardizes frontage while preserving the current footprint proportions and height.</summary>
        public static Bounds NormalizeBuildingFootprint(Transform root, bool preserveFront = false)
        {
            var bounds = MeasureFootprint(root);
            return NormalizeBuildingFootprint(root, new Vector2(bounds.size.x, bounds.size.z), preserveFront);
        }

        /// <summary>
        /// Restores the source X/Z proportions before standardizing its longer side to the LandPlot frontage.
        /// The longer side is oriented along world X; depth scales proportionally and height is preserved.
        /// originalFootprint allows correcting a scene instance that was previously stretched independently on X/Z.
        /// </summary>
        public static Bounds NormalizeBuildingFootprint(Transform root, Vector2 originalFootprint, bool preserveFront = false)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            if (!(originalFootprint.x > 0f) || !(originalFootprint.y > 0f) ||
                float.IsInfinity(originalFootprint.x) || float.IsInfinity(originalFootprint.y))
                throw new ArgumentOutOfRangeException(nameof(originalFootprint));
            var before = MeasureFootprint(root);
            float depth = LandPlot.StandardBuildingSide * Mathf.Min(originalFootprint.x, originalFootprint.y) /
                Mathf.Max(originalFootprint.x, originalFootprint.y);
            // Reapplying the same source standard must not rotate an already normalized building again.
            if (Mathf.Abs(before.size.x - LandPlot.StandardBuildingSide) <= .001f && Mathf.Abs(before.size.z - depth) <= .001f)
                return before;
            Vector3 oldScale = root.localScale, oldPosition = root.position;
            Quaternion oldRotation = root.rotation;
            try
            {
                NormalizeFootprint(root, originalFootprint.x, originalFootprint.y);
                if (originalFootprint.y > originalFootprint.x)
                    root.RotateAround(MeasureFootprint(root).center, Vector3.up, 90f);
                var after = NormalizeFootprint(root, LandPlot.StandardBuildingSide, depth);
                root.position += new Vector3(before.center.x - after.center.x, before.min.y - after.min.y,
                    preserveFront ? before.min.z - after.min.z : before.center.z - after.center.z);
                after = MeasureFootprint(root);
                if (Mathf.Abs(after.size.x - LandPlot.StandardBuildingSide) > .01f || Mathf.Abs(after.size.z - depth) > .01f ||
                    Mathf.Abs(after.size.y - before.size.y) > .01f || Mathf.Abs(after.min.y - before.min.y) > .01f ||
                    Mathf.Abs(after.center.x - before.center.x) > .01f ||
                    Mathf.Abs(preserveFront ? after.min.z - before.min.z : after.center.z - before.center.z) > .01f)
                    throw new InvalidOperationException("Proportional normalization changed height, anchor or expected dimensions: " + root.name);
                if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.RecordPrefabInstancePropertyModifications(root);
                EditorUtility.SetDirty(root);
                return after;
            }
            catch
            {
                root.localScale = oldScale;
                root.SetPositionAndRotation(oldPosition, oldRotation);
                if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.RecordPrefabInstancePropertyModifications(root);
                throw;
            }
        }

        /// <summary>
        /// Sets exact world X/Z bounds while preserving height and the bottom centre.
        /// preserveFront keeps the world minimum-Z edge instead of the Z centre. The root must have
        /// upright, cardinal world axes; unsupported rotations are rejected without changing geometry.
        /// Separate colliders, decorations and interaction anchors must be moved by the caller.
        /// </summary>
        public static Bounds NormalizeFootprint(Transform root, float width, float depth, bool preserveFront = false)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            if (!(width > 0f) || float.IsInfinity(width)) throw new ArgumentOutOfRangeException(nameof(width));
            if (!(depth > 0f) || float.IsInfinity(depth)) throw new ArgumentOutOfRangeException(nameof(depth));
            var before = MeasureFootprint(root);
            if (!(before.size.x > .0001f) || !(before.size.z > .0001f))
                throw new InvalidOperationException("Mesh footprint has no measurable width or depth: " + root.name);

            var world = root.localToWorldMatrix;
            Vector3 right = world.MultiplyVector(Vector3.right).normalized;
            Vector3 up = world.MultiplyVector(Vector3.up).normalized;
            Vector3 forward = world.MultiplyVector(Vector3.forward).normalized;
            bool aligned = Mathf.Abs(right.x) > .99999f && Mathf.Abs(forward.z) > .99999f;
            bool swapped = Mathf.Abs(right.z) > .99999f && Mathf.Abs(forward.x) > .99999f;
            if (Mathf.Abs(up.y) < .99999f || (!aligned && !swapped))
                throw new InvalidOperationException("Footprint normalization requires upright cardinal world axes: " + root.name);

            Vector3 oldScale = root.localScale, oldPosition = root.position;
            try
            {
                float xFactor = width / before.size.x, zFactor = depth / before.size.z;
                root.localScale = Vector3.Scale(oldScale, swapped ? new Vector3(zFactor, 1f, xFactor) : new Vector3(xFactor, 1f, zFactor));
                var after = MeasureFootprint(root);
                root.position += new Vector3(before.center.x - after.center.x, before.min.y - after.min.y,
                    preserveFront ? before.min.z - after.min.z : before.center.z - after.center.z);
                after = MeasureFootprint(root);
                if (Mathf.Abs(after.size.x - width) > .01f || Mathf.Abs(after.size.z - depth) > .01f ||
                    Mathf.Abs(after.size.y - before.size.y) > .01f || Mathf.Abs(after.min.y - before.min.y) > .01f ||
                    Mathf.Abs(after.center.x - before.center.x) > .01f ||
                    Mathf.Abs(preserveFront ? after.min.z - before.min.z : after.center.z - before.center.z) > .01f)
                    throw new InvalidOperationException("Footprint normalization did not preserve the requested dimensions and anchor: " + root.name);
                if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.RecordPrefabInstancePropertyModifications(root);
                EditorUtility.SetDirty(root);
                return after;
            }
            catch
            {
                root.localScale = oldScale;
                root.position = oldPosition;
                throw;
            }
        }

        [MenuItem("Tools/Company Game/Maps/Organize Legacy Village")]
        public static void OrganizeVillage()
        {
            RequireClean();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(VillagePrefab);
            if (!asset) throw new InvalidOperationException("Village prefab was not found.");
            if (asset.transform.Find("10_Buildings"))
            {
                Debug.Log("The village is already organized; existing edits were preserved.");
                return;
            }
            Directory.CreateDirectory(ReportFolder + "/backups");
            foreach (var path in new[] { VillagePrefab, VillagePrefab + ".meta", SmallMapSceneBuilder.VillageScenePath, SmallMapSceneBuilder.VillageScenePath + ".meta", "ProjectSettings/EditorBuildSettings.asset" })
            {
                var backup = ReportFolder + "/backups/" + Path.GetFileName(path);
                if (!File.Exists(backup)) File.Copy(path, backup);
            }
            CaptureScene("village-before.png");
            EnsureFolder(PartsFolder + "/Buildings");
            EnsureFolder(PartsFolder + "/Trees");
            var root = PrefabUtility.LoadPrefabContents(VillagePrefab);
            try
            {
                var originals = root.transform.Cast<Transform>().ToArray();
                if (originals.Any(t => t.childCount != 0))
                    throw new InvalidOperationException("This migration expects the original flat source. Review the existing hierarchy first.");
                var before = new GeometrySnapshot(root);
                var terrain = Group(root.transform, "00_Terrain");
                var buildings = Group(root.transform, "10_Buildings");
                var nature = Group(root.transform, "20_Nature");
                var props = Group(root.transform, "30_Props");
                var colliders = Group(root.transform, "40_Colliders");
                var claimed = new HashSet<Transform>();
                foreach (var building in Buildings)
                {
                    var pieces = originals.Where(t => t.name.StartsWith(building + "_", StringComparison.Ordinal) ||
                        (building == "CityHall" && t.name.StartsWith("Building_CityHall_", StringComparison.Ordinal))).ToArray();
                    if (pieces.Length == 0) continue;
                    var holder = Group(buildings, building);
                    holder.position = GroundPivot(pieces);
                    foreach (var piece in pieces)
                    {
                        piece.SetParent(Group(holder, BuildingSection(piece.name)), true);
                        claimed.Add(piece);
                    }
                    Extract(holder, PartsFolder + "/Buildings/" + building + ".prefab");
                }
                var trees = Group(nature, "Trees");
                var treeGroups = originals.Select(t => new { transform = t, match = Regex.Match(t.name, @"^(Tree_(?:Extra_)?\d+)_(.+)$") })
                    .Where(item => item.match.Success).GroupBy(item => item.match.Groups[1].Value).OrderBy(items => items.Key);
                foreach (var tree in treeGroups)
                {
                    var pieces = tree.Select(item => item.transform).ToArray();
                    var holder = Group(trees, tree.Key);
                    var pivot = GroundPivot(pieces);
                    var trunk = tree.FirstOrDefault(item => item.match.Groups[2].Value == "trunk");
                    if (trunk != null) { pivot.x = trunk.transform.position.x; pivot.z = trunk.transform.position.z; }
                    holder.position = pivot;
                    foreach (var item in tree.OrderBy(item => item.match.Groups[2].Value, StringComparer.Ordinal))
                    {
                        item.transform.name = item.match.Groups[2].Value;
                        item.transform.SetParent(holder, true);
                        claimed.Add(item.transform);
                    }
                    Extract(holder, PartsFolder + "/Trees/" + (tree.Key.StartsWith("Tree_Extra_", StringComparison.Ordinal) ? "Tree_Extra" : "Tree_Common") + ".prefab");
                }
                foreach (var piece in originals.Where(t => !claimed.Contains(t)))
                {
                    string name = piece.name;
                    Transform parent;
                    if (name.StartsWith("Collider_", StringComparison.Ordinal)) parent = colliders;
                    else if (!piece.GetComponent<MeshFilter>()) parent = Group(props, "SourceHelpers");
                    else if (name.StartsWith("Terrain", StringComparison.Ordinal)) parent = Group(terrain, "Ground");
                    else if (Regex.IsMatch(name, @"^(Wall|WallCap)_")) parent = Group(terrain, "Walls/" + Token(name, 1));
                    else if (Regex.IsMatch(name, @"^(Rock|Water|Foam)")) parent = Group(terrain, "Coast");
                    else if (Regex.IsMatch(name, @"^(Walk_Stair|Stair|Step|Rail|Support_Stair)")) parent = Group(terrain, "Stairs/" + RouteKey(name));
                    else if (Regex.IsMatch(name, @"^(Walk|Bridge|PavingJoint|Curb|Road|CausewayGuard)")) parent = Group(terrain, "RoadsAndPaths/" + Token(name, 0));
                    else if (Regex.IsMatch(name, @"^(Garden|Planter|Soil|Shrub|Flower|WarmPot)")) parent = Group(nature, "GardensAndPlanters/" + Token(name, 0));
                    else if (Regex.IsMatch(name, @"^(TownBus|RedCar|CreamVan|YellowCar)")) parent = Group(props, "Vehicles/" + Token(name, 0));
                    else if (name.StartsWith("WarmLamp_", StringComparison.Ordinal)) parent = Group(props, "StreetLights/WarmLamp_" + Token(name, 1));
                    else if (Regex.IsMatch(name, @"^(StationBench|RestBench)")) parent = Group(props, "Benches/" + Token(name, 0));
                    else parent = Group(props, Token(name, 0));
                    piece.SetParent(parent, true);
                }
                before.AssertUnchanged(root);
                if (!PrefabUtility.SaveAsPrefabAsset(root, VillagePrefab)) throw new InvalidOperationException("Could not save organized village prefab.");
                File.WriteAllText(ReportFolder + "/geometry-validation.txt", "PASS: all original renderer, mesh, material, collider and world-transform references preserved.\n" + before.Summary + "\nBuilding prefabs: " + buildings.childCount + "\nTree instances: " + trees.childCount + "\nShared tree prefabs: 2\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            CaptureScene("village-after.png");
            Debug.Log(File.ReadAllText(ReportFolder + "/geometry-validation.txt"));
        }

        static string BuildingSection(string name)
        {
            if (Regex.IsMatch(name, "Window|Door|Awning", RegexOptions.IgnoreCase)) return "20_DoorsAndWindows";
            if (Regex.IsMatch(name, "Roof|Chimney|Gutter", RegexOptions.IgnoreCase)) return "10_Roof";
            if (Regex.IsMatch(name, "Wall|Base|Plinth|Floor|Foundation|Beam|Pillar|Column|Stair|Step|Balcony|Rail|Stucco", RegexOptions.IgnoreCase)) return "00_Structure";
            if (Regex.IsMatch(name, "Sign|Letter|Logo|Board|Banner|Flag", RegexOptions.IgnoreCase)) return "30_Signs";
            return "40_Details";
        }

        static string Token(string name, int index)
        {
            var tokens = Regex.Replace(name, @"\.\d+$", "").Split('_');
            return tokens[Math.Min(index, tokens.Length - 1)];
        }

        static string RouteKey(string name)
        {
            var match = Regex.Match(name, "BridgeNorth|BridgeSouth|CentralCrest|CentralLower|CentralMiddle|CityHall|ShopCenter|ShopWest|Subway|WestCrest|WestLower|WestMiddle|East");
            return match.Success ? match.Value : Token(name, 0);
        }

        static Transform Group(Transform parent, string path)
        {
            foreach (string name in path.Split('/'))
            {
                var next = parent.Find(name);
                if (!next)
                {
                    next = new GameObject(name).transform;
                    next.SetParent(parent, false);
                }
                parent = next;
            }
            return parent;
        }

        static Vector3 GroundPivot(Transform[] pieces)
        {
            var renderers = pieces.SelectMany(piece => piece.GetComponentsInChildren<Renderer>(true)).ToArray();
            if (renderers.Length == 0) return pieces[0].position;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        static void Extract(Transform holder, string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset)
            {
                // Save a clone so the original component identities remain intact.
                var clone = Object.Instantiate(holder.gameObject);
                clone.name = Path.GetFileNameWithoutExtension(path);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                try { asset = PrefabUtility.SaveAsPrefabAsset(clone, path); }
                finally { Object.DestroyImmediate(clone); }
                if (!asset) throw new InvalidOperationException("Could not create " + path);
            }
            PrefabUtility.ConvertToPrefabInstance(holder.gameObject, asset, new ConvertToPrefabInstanceSettings
            {
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                changeRootNameToAssetName = false,
                componentsNotMatchedBecomesOverride = true,
                gameObjectsNotMatchedBecomesOverride = true,
                recordPropertyOverridesOfMatches = true,
                logInfo = false
            }, InteractionMode.AutomatedAction);
        }

        static void EnsureFolder(string path)
        {
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void RequireClean()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your open scenes before organizing the map.");
        }

        sealed class GeometrySnapshot
        {
            readonly Dictionary<Transform, Matrix4x4> transforms;
            readonly Dictionary<MeshFilter, Mesh> meshes;
            readonly Dictionary<Renderer, Material[]> materials;
            readonly Dictionary<Collider, string> colliders;
            readonly Dictionary<Renderer, bool> enabled;
            public string Summary => $"Transforms: {transforms.Count}; mesh filters: {meshes.Count}; renderers: {materials.Count}; colliders: {colliders.Count}";
            public GeometrySnapshot(GameObject root)
            {
                transforms = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.localToWorldMatrix);
                meshes = root.GetComponentsInChildren<MeshFilter>(true).ToDictionary(m => m, m => m.sharedMesh);
                materials = root.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.sharedMaterials);
                enabled = materials.Keys.ToDictionary(r => r, r => r.enabled);
                colliders = root.GetComponentsInChildren<Collider>(true).ToDictionary(c => c, ColliderState);
            }
            public void AssertUnchanged(GameObject root)
            {
                foreach (var pair in transforms)
                {
                    if (!pair.Key) throw new InvalidOperationException("A source object was removed by the migration.");
                    var current = pair.Key.localToWorldMatrix;
                    for (int i = 0; i < 16; i++)
                        if (Mathf.Abs(current[i] - pair.Value[i]) > .0005f) throw new InvalidOperationException("World transform changed: " + pair.Key.name);
                }
                foreach (var pair in meshes)
                    if (!pair.Key || pair.Key.sharedMesh != pair.Value) throw new InvalidOperationException("Source mesh changed.");
                foreach (var pair in materials)
                    if (!pair.Key || !pair.Key.sharedMaterials.SequenceEqual(pair.Value) || pair.Key.enabled != enabled[pair.Key]) throw new InvalidOperationException("Source renderer changed.");
                foreach (var pair in colliders)
                    if (!pair.Key || ColliderState(pair.Key) != pair.Value) throw new InvalidOperationException("Source collider changed: " + pair.Key);
                if (root.GetComponentsInChildren<MeshFilter>(true).Length != meshes.Count || root.GetComponentsInChildren<Collider>(true).Length != colliders.Count)
                    throw new InvalidOperationException("Geometry count changed.");
            }
            static string ColliderState(Collider c)
            {
                var common = $"{c.GetType().Name}|{c.enabled}|{c.isTrigger}|{c.contactOffset:R}|{c.sharedMaterial?.GetEntityId()}|{c.includeLayers.value}|{c.excludeLayers.value}|{c.layerOverridePriority}";
                if (c is MeshCollider mesh) return common + $"|{mesh.sharedMesh?.GetEntityId()}|{mesh.convex}|{mesh.cookingOptions}";
                if (c is BoxCollider box) return common + "|" + box.center.ToString("R") + "|" + box.size.ToString("R");
                throw new InvalidOperationException("Add preservation validation for collider: " + c.GetType().Name);
            }
        }

        public static void CaptureScene(string fileName)
        {
            var scene = SceneManager.GetActiveScene();
            var camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            if (!camera) return;
            Directory.CreateDirectory(ReportFolder);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(1100, 900, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(1100, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1100, 900), 0, 0);
                texture.Apply();
                File.WriteAllBytes(ReportFolder + "/" + fileName, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(texture);
            }
        }

    }
}
