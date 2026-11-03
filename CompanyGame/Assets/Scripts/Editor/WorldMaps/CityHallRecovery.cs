using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Imports the surviving authored city hall backup and restores its existing scene connections.</summary>
public static class CityHallRecovery
{
    const string Art = "Assets/Art/WorldDistricts/CivicCityHallRecovered";
    public const string Interior = "Assets/Scenes/Interiors/CivicCityHallInterior.unity";
    const string Outside = "Assets/Scenes/Maps/CivicDistrict.unity";
    [Serializable] class MaterialSpec { public string name, texture; public float[] color; public float metallic, roughness, alpha; }
    [Serializable] class Materials { public MaterialSpec[] materials; }
    static Transform Node(Transform root, string name) { var matches = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray(); return matches.Length == 1 ? matches[0] : matches.Single(t => t.Find("Anchors")); }
    static string Group(Transform t, Transform root)
    {
        while (t.parent && t.parent != root) t = t.parent;
        return t.name;
    }
    static MapSpawnPoint Spawn(string name, Scene scene, Vector3 position, float yaw)
    {
        var g = new GameObject("Spawn_" + name); SceneManager.MoveGameObjectToScene(g, scene);
        g.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        var spawn = g.AddComponent<MapSpawnPoint>(); spawn.spawnId = name; return spawn;
    }
    static StoreInteractionPoint Portal(string name, Scene scene, Vector3 position, string destination, string spawn, string prompt)
    {
        var g = new GameObject(name); SceneManager.MoveGameObjectToScene(g, scene); g.transform.position = position;
        var point = g.AddComponent<StoreInteractionPoint>(); point.prompt = prompt; point.targetScenePath = destination;
        point.targetSpawnId = spawn; point.radius = 1.8f; point.heightTolerance = 1.4f; return point;
    }
    static LobbyAutomaticDoor Door(Transform host, Transform left, Transform right, bool external)
    {
        var door = host.gameObject.AddComponent<LobbyAutomaticDoor>();
        door.leftLeaf = left; door.rightLeaf = right; door.slideDistance = .94f;
        door.externallyControlled = external;
        var g = new GameObject("DoorClearance"); g.transform.SetParent(host, false);
        g.transform.position = (left.position + right.position) * .5f;
        g.transform.rotation = Quaternion.identity;
        var zone = g.AddComponent<BoxCollider>(); zone.isTrigger = true; zone.size = new Vector3(2, 2.7f, .85f);
        zone.transform.localScale = Vector3.one / zone.transform.lossyScale.x;
        door.slideDistance /= left.parent.TransformVector(Vector3.right).magnitude;
        door.obstructionZone = zone; return door;
    }
    static GameObject PrepareModel()
    {
        var path = Art + "/CivicCityHall_Interior4F.fbx";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.importAnimation = false; importer.globalScale = 1; importer.isReadable = true;
        importer.addCollider = false; importer.preserveHierarchy = true;
        importer.SaveAndReimport();
        Directory.CreateDirectory(Art + "/Materials"); AssetDatabase.Refresh();
        var specs = JsonUtility.FromJson<Materials>(File.ReadAllText(Art + "/materials.json")).materials;
        var mats = new Dictionary<string, Material>();
        foreach (var spec in specs)
        {
            string materialPath = Art + "/Materials/" + spec.name.Replace('/', '_') + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, materialPath); }
            m.SetColor("_BaseColor", new Color(spec.color[0], spec.color[1], spec.color[2], spec.alpha));
            m.SetFloat("_Metallic", spec.metallic); m.SetFloat("_Smoothness", 1 - spec.roughness);
            if (!string.IsNullOrEmpty(spec.texture)) m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/" + spec.texture));
            if (spec.alpha < .99f || spec.name.ToLowerInvariant().Contains("glass"))
            {
                var c = m.GetColor("_BaseColor"); c.a = Mathf.Min(c.a, .3f); m.SetColor("_BaseColor", c);
                m.SetFloat("_Surface", 1); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0);
                m.SetFloat("_Cull", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
            }
            mats.Add(spec.name, m); EditorUtility.SetDirty(m);
        }
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), preview);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var wrapper = new GameObject("CityHallInteriorRecovered"); SceneManager.MoveGameObjectToScene(wrapper, preview);
            model.transform.SetParent(wrapper.transform, false);
            var root = Node(model.transform, "CivicCityHall_Interior4F");
            // Resolve FBX axes against the authored entry anchor, rather than assuming import handedness.
            var entry = Node(root, "SPAWN_CityHallEntrance");
            if (entry.position.z > 0) model.transform.Rotate(0, 180, 0, Space.World);
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => mats.TryGetValue(m.name, out var mapped) ? mapped : throw new Exception("Unmapped restored material: " + m.name)).ToArray();
                string group = Group(r.transform, root);
                bool decorative = group == "Signage" || group == "Lighting" || group == "Planting" || r.name.StartsWith("GroundTileJoint") || r.name.StartsWith("FloorTileJoint");
                if (!decorative)
                {
                    var col = r.gameObject.AddComponent<MeshCollider>(); col.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                }
                if (r.name.StartsWith("COLLIDER_")) r.enabled = false;
            }
            var lift = wrapper.AddComponent<ElevatorController>();
            lift.cabin = Node(root, "ELEVATOR_Cabin");
            var cabinVolume = new GameObject("CabinPassengerVolume"); cabinVolume.transform.SetParent(lift.cabin, false);
            cabinVolume.transform.SetPositionAndRotation(lift.cabin.position + Vector3.up * 1.5f, Quaternion.identity);
            cabinVolume.transform.localScale = Vector3.one / cabinVolume.transform.lossyScale.x;
            lift.cabinArea = cabinVolume.AddComponent<BoxCollider>(); lift.cabinArea.isTrigger = true; lift.cabinArea.size = new Vector3(3.5f, 3, 3.5f);
            lift.cabinDoor = Door(lift.cabin, Node(root, "ELEVATOR_CabinDoor_Leaf_1"), Node(root, "ELEVATOR_CabinDoor_Leaf_2"), true);
            lift.floors = Enumerable.Range(1, 4).Select(i => new ElevatorController.Floor
            {
                label = i + "층", stop = Node(root, "ELEVATOR_Stop_F" + i), callPoint = Node(root, "ELEVATOR_Call_F" + i),
                landingDoor = Door(Node(root, "ELEVATOR_LandingDoor_F" + i), Node(root, "ELEVATOR_LandingDoor_F" + i + "_Leaf_1"), Node(root, "ELEVATOR_LandingDoor_F" + i + "_Leaf_2"), true)
            }).ToArray();
            foreach (string name in new[] { "DOOR_MainEntrance", "DOOR_CivilService" })
            {
                var door = Door(Node(root, name), Node(root, name + "_Leaf_01"), Node(root, name + "_Leaf_02"), false);
                door.slideDistance = (name == "DOOR_MainEntrance" ? 3.05f : 2.15f) / door.leftLeaf.parent.TransformVector(Vector3.right).magnitude;
                door.openRadius = 3.2f; door.closeRadius = 4.2f;
            }
            // Restore the original transparent lift treatment recorded after the surviving backup.
            var glass = mats.FirstOrDefault(m => m.Key.EndsWith("_civilglass")).Value;
            if (glass) foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.StartsWith("ElevatorShaft") || (r.name.StartsWith("ElevatorCabin") && !r.name.StartsWith("ElevatorCabinFloor")) || (r.name.StartsWith("ELEVATOR_") && r.name.Contains("_Leaf_")))
                    r.sharedMaterials = Enumerable.Repeat(glass, r.sharedMaterials.Length).ToArray();
            return PrefabUtility.SaveAsPrefabAsset(wrapper, Art + "/CityHallInterior.prefab");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    [MenuItem("Tools/CompanyGame/Restore city hall from surviving backup")]
    public static string Restore()
    {
        if (EditorApplication.isPlaying) throw new Exception("Edit mode required");
        if (File.Exists(Interior)) throw new Exception("Restored interior scene already exists; refusing to overwrite it.");
        var before = SceneManager.GetActiveScene();
        if (before.isDirty) throw new Exception("Save current edits before restoring the city hall.");
        var prefab = PrepareModel();
        var interior = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, interior);
        var entry = Node(instance.transform, "SPAWN_CityHallEntrance");
        Spawn("cityhall_entry", interior, entry.position + Vector3.up * .03f, 0);
        Spawn("default", interior, entry.position + Vector3.up * .03f, 0);
        Portal("CityHall Exit", interior, Node(instance.transform, "EXIT_CityHall").position, Outside, "cityhall_exit", "시청 나가기");
        for (int i = 1; i <= 4; i++)
        {
            var p = Node(instance.transform, "NAV_Stair_L_F" + i).position;
            Spawn("floor_" + i, interior, p + Vector3.up * .06f, 0);
            foreach (string side in new[] { "L", "R" })
            { var t = Node(instance.transform, "NAV_Stair_" + side + "_F" + i); var stairs = t.gameObject.AddComponent<FloorStairs>(); stairs.floor = i; stairs.topFloor = 4; }
        }
        var cameraObject = new GameObject("Main Camera"); SceneManager.MoveGameObjectToScene(cameraObject, interior);
        cameraObject.tag = "MainCamera"; var cam = cameraObject.AddComponent<Camera>(); cam.nearClipPlane = .03f; cam.farClipPlane = 250;
        cameraObject.AddComponent<AudioListener>(); var follow = cameraObject.AddComponent<PlayerCameraController>(); follow.firstPerson = true;
        var lightObject = new GameObject("Interior Daylight"); SceneManager.MoveGameObjectToScene(lightObject, interior);
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f;
        lightObject.transform.rotation = Quaternion.Euler(55, -25, 0); light.shadows = LightShadows.Soft;
        SceneManager.SetActiveScene(interior); RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.65f, .63f, .58f); RenderSettings.fog = false;
        EditorSceneManager.SaveScene(interior, Interior);
        var outside = SceneManager.GetSceneByPath(Outside);
        bool opened = !outside.IsValid() || !outside.isLoaded;
        if (opened) outside = EditorSceneManager.OpenScene(Outside, OpenSceneMode.Additive);
        if (outside.isDirty) throw new Exception("Save current civic scene edits before restoring the exterior.");
        var all = outside.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var office = all.Single(t => t.name == "CivicDistrict_DistrictOffice" && !t.GetComponent<MapParcel>() && t.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled));
        var bounds = office.GetComponentsInChildren<MeshRenderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        var exteriorPath = Art + "/CivicCityHall_Exterior.fbx";
        File.Copy(Path.GetFullPath("../ArtSource/WorldDistricts/CivicCityHall/UnityExport/CivicCityHall_Blockout.fbx"), exteriorPath, true);
        AssetDatabase.ImportAsset(exteriorPath, ImportAssetOptions.ForceSynchronousImport);
        var ext = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(exteriorPath), outside);
        ext.name = "Restored City Hall"; var rs = ext.GetComponentsInChildren<MeshRenderer>();
        foreach (var r in rs)
        {
            r.sharedMaterials = r.sharedMaterials.Select(m => { var copy = new Material(Shader.Find("Universal Render Pipeline/Lit")); copy.color = m.color; copy.name = "Exterior_" + m.name; string p = Art + "/Materials/" + copy.name + ".mat"; var old = AssetDatabase.LoadAssetAtPath<Material>(p); if (old) { Object.DestroyImmediate(copy); return old; } AssetDatabase.CreateAsset(copy, p); return copy; }).ToArray();
            var mc = r.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
        }
        var eb = rs.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        float scale = Mathf.Min(38 / eb.size.x, 38 / eb.size.z);
        ext.transform.localScale = Vector3.one * scale;
        eb = rs.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        ext.transform.position += new Vector3(bounds.center.x - eb.center.x, bounds.min.y - eb.min.y, bounds.center.z - eb.center.z);
        office.gameObject.SetActive(false);
        foreach (var t in all.Where(t => t.name.StartsWith("Building_CivicDistrict_DistrictOffice", StringComparison.Ordinal))) t.gameObject.SetActive(false);
        var doorBounds = rs.Single(r => r.name == "CH_MainEntranceGlazing").bounds;
        float frontSign = Mathf.Sign(doorBounds.center.z - rs.Single(r => r.name == "CityHall_Site_42x34").bounds.center.z);
        Vector3 entrance = new Vector3(doorBounds.center.x, doorBounds.min.y + .06f, doorBounds.center.z + frontSign * .9f);
        Portal("CityHall Interior Portal", outside, entrance, Interior, "cityhall_entry", "시청 들어가기");
        Spawn("cityhall_exit", outside, entrance + Vector3.forward * frontSign * .65f, frontSign > 0 ? 0 : 180);
        EditorSceneManager.MarkSceneDirty(outside); EditorSceneManager.SaveScene(outside);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == Interior)) scenes.Add(new EditorBuildSettingsScene(Interior, true));
        EditorBuildSettings.scenes = scenes.ToArray(); AssetDatabase.SaveAssets();
        SceneManager.SetActiveScene(before);
        EditorSceneManager.CloseScene(interior, true); if (opened) EditorSceneManager.CloseScene(outside, true);
        return "City hall restored: exterior, four-floor interior, original elevator, entry and exit";
    }

    public static void FixImportedUnits()
    {
        if (EditorApplication.isPlaying) throw new Exception("Edit mode required");
        void Fix(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.StartsWith("GroundTileJoint") || r.name.StartsWith("FloorTileJoint"))
                {
                    var c = r.GetComponent<Collider>();
                    if (c) { c.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
                }
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
                if ((box.name == "CabinPassengerVolume" || box.name == "DoorClearance") && box.transform.lossyScale.x > 10)
                {
                    var scale = box.transform.lossyScale;
                    box.transform.localScale = Vector3.Scale(box.transform.localScale, new Vector3(1 / Mathf.Abs(scale.x), 1 / Mathf.Abs(scale.y), 1 / Mathf.Abs(scale.z)));
                    PrefabUtility.RecordPrefabInstancePropertyModifications(box.transform);
                }
            foreach (var door in root.GetComponentsInChildren<LobbyAutomaticDoor>(true))
                if (door.slideDistance * door.leftLeaf.parent.TransformVector(Vector3.right).magnitude > 10)
                {
                    door.slideDistance /= door.leftLeaf.parent.TransformVector(Vector3.right).magnitude;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(door);
                }
        }
        var prefab = PrefabUtility.LoadPrefabContents(Art + "/CityHallInterior.prefab");
        try { Fix(prefab); PrefabUtility.SaveAsPrefabAsset(prefab, Art + "/CityHallInterior.prefab"); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var scene = SceneManager.GetSceneByPath(Interior);
        if (scene.IsValid() && scene.isLoaded)
        {
            foreach (var g in scene.GetRootGameObjects()) Fix(g);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
    }

    public static void FixExteriorEntrance()
    {
        if (EditorApplication.isPlaying) throw new Exception("Edit mode required");
        var scene = SceneManager.GetSceneByPath(Outside);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Outside, OpenSceneMode.Additive);
        try
        {
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var root = all.Single(t => t.name == "Restored City Hall");
            var rs = root.GetComponentsInChildren<MeshRenderer>();
            var door = rs.Single(r => r.name == "CH_MainEntranceGlazing").bounds;
            float sign = Mathf.Sign(door.center.z - rs.Single(r => r.name == "CityHall_Site_42x34").bounds.center.z);
            var entry = new Vector3(door.center.x, door.min.y + .06f, door.center.z + sign * .9f);
            var point = all.Select(t => t.GetComponent<StoreInteractionPoint>()).Single(p => p && p.targetScenePath == Interior);
            point.transform.position = entry;
            var spawn = all.Select(t => t.GetComponent<MapSpawnPoint>()).Single(p => p && p.spawnId == "cityhall_exit");
            var position = entry + Vector3.forward * sign * .65f;
            Physics.SyncTransforms();
            var floor = Physics.RaycastAll(position + Vector3.up, Vector3.down, 3, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.gameObject.scene == scene && h.normal.y > .5f).OrderBy(h => h.distance).FirstOrDefault();
            if (!floor.collider) throw new Exception("No exterior landing at restored city hall entrance");
            position.y = floor.point.y + .03f;
            spawn.transform.SetPositionAndRotation(position, Quaternion.Euler(0, sign > 0 ? 0 : 180, 0));
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    public static string TrimOldOfficeDetails()
    {
        if (EditorApplication.isPlaying) throw new Exception("Edit mode required");
        var scene = SceneManager.GetSceneByPath(Outside);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Outside, OpenSceneMode.Additive);
        try
        {
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var office = all.Single(t => t.name == "CivicDistrict_DistrictOffice" && !t.GetComponent<MapParcel>());
            var bounds = office.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            bounds.Expand(.8f);
            var parcel = all.Select(t => t.GetComponent<MapParcel>()).Single(p => p && p.parcelId == "CivicDistrict:CivicDistrict_DistrictOffice");
            bounds = new Bounds(new Vector3(parcel.Center.x, 50, parcel.Center.z), new Vector3(parcel.size.x, 110, parcel.size.y));
            var details = all.Single(t => t.name == "CivicDistrict_UrbanDetail");
            Directory.CreateDirectory(Art + "/SceneMeshes"); AssetDatabase.Refresh();
            int removed = 0, changed = 0, disabled = 0;
            foreach (var filter in details.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = filter.sharedMesh;
                if (!source || !source.isReadable) throw new Exception("Original civic detail mesh must be readable: " + filter.name);
                var vertices = source.vertices.Select(filter.transform.TransformPoint).ToArray();
                var clone = Object.Instantiate(source); int count = 0;
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    var tris = source.GetTriangles(sub); var keep = new List<int>();
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        if (bounds.Contains(vertices[tris[i]]) && bounds.Contains(vertices[tris[i + 1]]) && bounds.Contains(vertices[tris[i + 2]])) { count++; continue; }
                        keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
                    }
                    clone.SetTriangles(keep, sub, false);
                }
                if (count == 0) { Object.DestroyImmediate(clone); continue; }
                clone.RecalculateBounds(); clone.name = "Civic_CityHallTrim_" + filter.name;
                var path = Art + "/SceneMeshes/" + clone.name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing) { EditorUtility.CopySerialized(clone, existing); for (int sub = 0; sub < clone.subMeshCount; sub++) existing.SetTriangles(clone.GetTriangles(sub), sub, false); existing.RecalculateBounds(); existing.UploadMeshData(false); EditorUtility.SetDirty(existing); Object.DestroyImmediate(clone); clone = existing; }
                else AssetDatabase.CreateAsset(clone, path);
                filter.sharedMesh = clone; PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                var col = filter.GetComponent<MeshCollider>(); if (col) { col.sharedMesh = clone; PrefabUtility.RecordPrefabInstancePropertyModifications(col); }
                removed += count; changed++;
            }
            var generated = all.FirstOrDefault(t => t.name == "Colliders_Generated");
            if (generated) foreach (var c in generated.GetComponentsInChildren<Collider>())
                if (c.enabled && c.bounds.max.y > bounds.min.y + .8f && bounds.Contains(c.bounds.min) && bounds.Contains(c.bounds.max))
                { c.enabled = false; disabled++; }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            return "Old office detail triangles removed=" + removed + ", copied meshes=" + changed + ", old colliders disabled=" + disabled;
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }
}
