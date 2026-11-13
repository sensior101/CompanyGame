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

/// <summary>
/// Builds RealEstateOfficeInterior.unity from the rectangular shop shell (26 x 16.4 m) and existing furniture, and connects it
/// to the RE_01 door in CivicDistrict the same way the city hall is connected (StoreInteractionPoint + MapSpawnPoint).
/// Idempotent. Coordinates: door on the -Z wall at x=0, floor at y=0, walls at x=+-13 and z=+-8.2. A model at yaw 0 faces +Z.
/// Run: -executeMethod RealEstateInteriorBuilder.Build
/// </summary>
public static class RealEstateInteriorBuilder
{
    public const string Interior = "Assets/Scenes/Interiors/RealEstateOfficeInterior.unity";
    public const string Outside = "Assets/Scenes/Maps/CivicDistrict.unity";
    public const string EntrySpawn = "realestate_entry", ExitSpawn = "realestate_exit";
    public const string OutsidePortalName = "RealEstate Interior Portal", OutsideSpawnName = "Spawn_realestate_exit";
    public const float RoomHalfX = 13f, RoomHalfZ = 8.2f, CeilingY = 3.3f;
    const string Art = "Assets/Art/Interiors/RealEstateOffice";
    const string Household = "Assets/Art/Items/Furniture/Household/Prefabs/WhiteWood/";
    const string WhiteWoodOak = "Assets/Art/Items/Furniture/Themes/WhiteWood/Materials/WW_Oak.mat";

    enum Solid { None, Box, Trunk }
    [Serializable] class MaterialSpec { public string name; public float[] color; public float metallic, roughness, emission; }
    [Serializable] class MaterialFile { public MaterialSpec[] materials; }

    static Dictionary<string, Material> materials;
    static Material[] listings;
    static int listingIndex;

    [MenuItem("CompanyGame/Setup/Build Real Estate Office Interior")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
        AssetDatabase.Refresh();
        foreach (string folder in new[] { "Prefabs", "Materials", "Textures" })
            if (!AssetDatabase.IsValidFolder(Art + "/" + folder)) AssetDatabase.CreateFolder(Art, folder);
        materials = BuildMaterials();
        listings = BuildListings();
        var shell = BuildShellPrefab();
        BuildScene(shell);
        ConnectOutside();
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == Interior))
        {
            scenes.Add(new EditorBuildSettingsScene(Interior, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
        AssetDatabase.SaveAssets();
        RefreshServerCollision();
        FurnitureCollectionImporter.BakeStorageLocations();
        AssetDatabase.SaveAssets();
        Debug.Log(FurnitureCollectionImporter.RefreshHouseholdSeats());
        RealEstateListingBaker.Bake();
        Debug.Log("[RealEstateInterior] Built " + Interior + " and connected RE_01.");
    }

    // ---- materials ----
    static Dictionary<string, Material> BuildMaterials()
    {
        var map = new Dictionary<string, Material>();
        foreach (var spec in JsonUtility.FromJson<MaterialFile>(File.ReadAllText(Art + "/materials.json")).materials)
        {
            if (spec.name == "WhiteWood_Oak") { map[spec.name] = AssetDatabase.LoadAssetAtPath<Material>(WhiteWoodOak); continue; }
            var m = LitMaterial("RE_" + spec.name, new Color(spec.color[0], spec.color[1], spec.color[2]).gamma, spec.metallic, 1 - spec.roughness);
            if (spec.name.Contains("Glass")) MakeGlass(m);
            if (spec.name.Contains("Diffuser"))
            {
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(1f, .96f, .88f) * 1.6f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            map[spec.name] = m;
        }
        // The shell uses one body colour for floor and walls; give the office a floor that reads apart from the walls.
        map["RE_Floor"] = LitMaterial("RE_OfficeFloor", new Color(.62f, .60f, .56f), 0, .2f);
        // A matte ceiling: the room lights sit close to it and would otherwise leave glare patches.
        var ceiling = LitMaterial("RE_OfficeCeiling", new Color(.86f, .85f, .81f), 0, 0);
        ceiling.SetFloat("_SpecularHighlights", 0); ceiling.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        map["RE_Ceiling"] = ceiling;
        return map;
    }

    static Material LitMaterial(string name, Color color, float metallic, float smoothness)
    {
        string path = Art + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void MakeGlass(Material m)
    {
        var c = m.GetColor("_BaseColor"); c.a = .3f; m.SetColor("_BaseColor", c);
        m.SetFloat("_Surface", 1); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0);
        m.SetFloat("_Cull", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
    }

    // Blank poster/frame image faces become property listings; the cards carry shapes, not text.
    static Material[] BuildListings()
    {
        var accents = new[] { new Color(.16f, .47f, .29f), new Color(.2f, .36f, .58f), new Color(.72f, .42f, .16f), new Color(.45f, .22f, .22f) };
        var result = new Material[accents.Length];
        for (int i = 0; i < accents.Length; i++)
        {
            string path = Art + "/Textures/Listing_" + i + ".png";
            if (!File.Exists(path)) File.WriteAllBytes(path, ListingPng(accents[i], i));
            AssetDatabase.ImportAsset(path);
            var m = LitMaterial("RE_Listing_" + i, Color.white, 0, .2f);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            m.SetFloat("_Cull", 0); // the poster is a single plane
            result[i] = m;
        }
        return result;
    }

    static byte[] ListingPng(Color accent, int variant)
    {
        const int w = 208, h = 328;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var paper = new Color(.96f, .95f, .91f);
        var ink = new Color(.28f, .29f, .3f);
        var wall = Color.Lerp(accent, Color.white, .55f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = paper;
                if (y > h - 44) c = accent; // header band
                int cx = w / 2, baseY = 138, half = 62 + variant * 4;
                bool body = Mathf.Abs(x - cx) < half && y >= baseY && y < baseY + 74;
                bool roof = y >= baseY + 74 && y < baseY + 74 + 50 && Mathf.Abs(x - cx) < (half + 14) * (1 - (y - baseY - 74) / 50f);
                bool door = Mathf.Abs(x - cx) < 11 && y >= baseY && y < baseY + 38;
                bool window = y >= baseY + 40 && y < baseY + 62 && (Mathf.Abs(x - cx + 34) < 13 || Mathf.Abs(x - cx - 34) < 13);
                if (body) c = wall;
                if (roof) c = accent;
                if (door) c = ink;
                if (window) c = new Color(.76f, .86f, .92f);
                if (x > 22 && x < w - 22 - variant * 18 && ((y > 92 && y < 104) || (y > 66 && y < 76))) c = ink;
                if (x > 22 && x < 112 && y > 28 && y < 48) c = accent; // price tag
                tex.SetPixel(x, y, c);
            }
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        return png;
    }

    // ---- prefabs ----
    static GameObject ImportModel(string name)
    {
        string path = Art + "/Models/" + name + ".fbx";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new FileNotFoundException("Model missing", path);
        importer.importAnimation = false; importer.globalScale = 1; importer.isReadable = true;
        importer.addCollider = false; importer.preserveHierarchy = true; importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    static void Remap(GameObject root)
    {
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            r.sharedMaterials = r.sharedMaterials.Select(m => m && materials.TryGetValue(m.name, out var mapped)
                ? mapped : throw new InvalidOperationException("Unmapped material " + (m ? m.name : "null") + " on " + r.name)).ToArray();
    }

    static Bounds BoundsOf(GameObject root)
    {
        var rs = root.GetComponentsInChildren<MeshRenderer>(true);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static GameObject BuildShellPrefab()
    {
        var model = ImportModel("ShopShell");
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var wrapper = new GameObject("RealEstateShell"); SceneManager.MoveGameObjectToScene(wrapper, preview);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.transform.SetParent(wrapper.transform, false);
            Remap(instance);
            var all = instance.GetComponentsInChildren<Transform>(true);
            var door = all.Single(t => t.name == "AutomaticDoor");
            if (door.position.z > 0) instance.transform.Rotate(0, 180, 0, Space.World); // FBX handedness: resolve against the door
            Physics.SyncTransforms();
            var size = BoundsOf(instance).size;
            if (Mathf.Abs(size.x - 26.4f) > .05f || Mathf.Abs(size.z - 16.8f) > .05f || size.y < 3.3f || size.y > 3.8f)
                throw new InvalidOperationException("Unexpected shell size " + size);
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool leaf = r.transform.parent.name is "LeftLeaf" or "RightLeaf";
                if (r.name == "Floor") r.sharedMaterial = materials["RE_Floor"];
                if (r.name == "Ceiling") r.sharedMaterial = materials["RE_Ceiling"];
                // The room is roofed: let the sun through so furniture still gets a soft contact shadow on the floor.
                if (r.name == "Ceiling" || r.name.Contains("SolidPanel") || r.name.StartsWith("Door") || leaf) r.shadowCastingMode = ShadowCastingMode.Off;
                if (leaf || r.name == "SensorHousing" || r.name == "DoorRail") continue;
                r.gameObject.AddComponent<MeshCollider>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
            }
            // The leaves stay shut: leaving is through the exit point, as in the other interiors.
            var block = new GameObject("DoorwayBlocker"); block.transform.SetParent(wrapper.transform, false);
            block.transform.position = new Vector3(0, 1.3f, -8.3f);
            var box = block.AddComponent<BoxCollider>(); box.size = new Vector3(2.2f, 2.6f, .3f);
            string path = Art + "/Prefabs/RealEstateShell.prefab";
            return PrefabUtility.SaveAsPrefabAsset(wrapper, path);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    static GameObject BuildFurniturePrefab(string name, Solid solid, params Vector3[] seats)
    {
        var model = ImportModel(name);
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var wrapper = new GameObject("RE_" + name); SceneManager.MoveGameObjectToScene(wrapper, preview);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.transform.SetParent(wrapper.transform, false);
            Remap(instance);
            Physics.SyncTransforms();
            var b = BoundsOf(instance);
            if (b.size.magnitude > 8 || b.size.magnitude < .1f) throw new InvalidOperationException("Unexpected size " + name + " " + b.size);
            if (solid != Solid.None)
            {
                var box = wrapper.AddComponent<BoxCollider>();
                box.center = b.center; box.size = b.size;
                if (solid == Solid.Trunk) box.size = new Vector3(Mathf.Min(b.size.x, .55f), b.size.y, Mathf.Min(b.size.z, .55f));
            }
            if (seats.Length > 0)
            {
                var world = wrapper.AddComponent<WorldObject>();
                world.objectType = WorldObjectType.StaticProp; world.functions = FurnitureFunction.Seating;
                for (int i = 0; i < seats.Length; i++)
                {
                    var g = new GameObject("Seat_" + (i + 1)); g.transform.SetParent(wrapper.transform, false);
                    g.transform.localPosition = seats[i];
                    g.AddComponent<Seat>().owner = world;
                }
            }
            return PrefabUtility.SaveAsPrefabAsset(wrapper, Art + "/Prefabs/RE_" + name + ".prefab");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    // ---- scene ----
    static Transform Group(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }

    static GameObject Put(Transform parent, GameObject prefab, float x, float z, float yaw = 0, float y = 0)
    {
        var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        g.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0, yaw, 0));
        // Household prefabs are player-placeable; in this fixed office they are fixtures that nobody may pick up.
        var world = g.GetComponent<WorldObject>();
        if (world) { world.objectType = WorldObjectType.StaticProp; world.furnitureItem = null; PrefabUtility.RecordPrefabInstancePropertyModifications(world); }
        // The server only accepts storage it finds in the baked catalog, under an ID that does not depend on the hierarchy.
        foreach (var storage in g.GetComponentsInChildren<FridgeStorage>(true))
        {
            storage.storageId = "realestate:" + parent.name + ":" + prefab.name + ":" + x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," + z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            PrefabUtility.RecordPrefabInstancePropertyModifications(storage);
        }
        return g;
    }

    // Walls cast no shadow in this room, so anything mounted on one must not throw a shadow through it.
    static GameObject NoShadow(GameObject g)
    {
        foreach (var r in g.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        return g;
    }

    static void Listing(Transform parent, GameObject prefab, float x, float z, float yaw, float y)
    {
        var g = NoShadow(Put(parent, prefab, x, z, yaw, y));
        var m = listings[listingIndex++ % listings.Length];
        foreach (var r in g.GetComponentsInChildren<MeshRenderer>())
            if (r.sharedMaterials.Any(s => s == materials["WhiteWood_ImageWhite"]))
                r.sharedMaterials = r.sharedMaterials.Select(s => s == materials["WhiteWood_ImageWhite"] ? m : s).ToArray();
    }

    const string MaleVisual = "Assets/Art/Daldongne/Players/MaleVisual.prefab", FemaleVisual = "Assets/Art/Daldongne/Players/FemaleVisual.prefab";

    // A seated staff member in one of the office chairs. SeatedStaff puts it on the chair's seat and poses it at runtime.
    static DialogueData Staff(Transform parent, GameObject chairInstance, string visualPath, string npcId, string displayName,
        string line, DialogueOption[] options, float radius)
    {
        var seat = chairInstance.GetComponentInChildren<Seat>(true) ?? throw new InvalidOperationException("No seat on " + chairInstance.name);
        var visual = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath) ?? throw new FileNotFoundException(visualPath);
        var root = new GameObject(displayName).transform;
        root.SetParent(parent, false);
        root.SetPositionAndRotation(seat.transform.position - Vector3.up * .44f, seat.transform.rotation);
        var avatar = (GameObject)PrefabUtility.InstantiatePrefab(visual, root);
        // Office wear: a white shirt (light blue at reception) and charcoal trousers over the basic outfit.
        var dress = avatar.GetComponent<AvatarOutfit>();
        if (dress)
        {
            dress.tint = true;
            dress.top = npcId == RealEstateDesk.Reception ? new Color(.70f, .80f, .90f) : new Color(.93f, .93f, .90f);
            dress.bottom = new Color(.20f, .21f, .24f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(dress);
        }
        var head = new GameObject("SpeechAnchor").transform;
        head.SetParent(root, false); head.localPosition = Vector3.up * 1.45f;
        root.gameObject.AddComponent<SeatedStaff>(); // finds and claims this seat at runtime
        var data = root.gameObject.AddComponent<DialogueData>();
        data.npcId = npcId; data.displayName = displayName; data.line = line;
        data.head = head; data.headOffset = Vector3.zero; data.radius = radius;
        data.options = options ?? Array.Empty<DialogueOption>();
        return data;
    }

    static GameObject Household_(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Household + name + ".prefab") ?? throw new FileNotFoundException(name);

    static void BuildScene(GameObject shell)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var map = new GameObject("Map_RealEstateOfficeInterior").transform;
        var world = Group(map, "10_World");
        PrefabUtility.InstantiatePrefab(shell, world);
        listingIndex = 0;

        // Prefabs from the commercial set. Seat offsets match the household chairs (seat pivot above the cushion).
        var guest = BuildFurniturePrefab("GuestChair", Solid.Box, new Vector3(0, .55f, .02f));
        var bench = BuildFurniturePrefab("WaitingBench", Solid.Box, new Vector3(-.55f, .56f, .02f), new Vector3(.55f, .56f, .02f));
        var counter = BuildFurniturePrefab("Counter05", Solid.Box);
        var meeting = BuildFurniturePrefab("MeetingTable", Solid.Box);
        var high = BuildFurniturePrefab("HighPartition", Solid.Box);
        var low = BuildFurniturePrefab("LowPartition", Solid.Box);
        var dispenser = BuildFurniturePrefab("WaterDispenser", Solid.Box);
        var umbrella = BuildFurniturePrefab("UmbrellaStand", Solid.Box);
        var plant2 = BuildFurniturePrefab("Plant02", Solid.Trunk);
        var plant4 = BuildFurniturePrefab("Plant04", Solid.Trunk);
        var plant5 = BuildFurniturePrefab("Plant05", Solid.Trunk);
        var whiteboard = BuildFurniturePrefab("Whiteboard", Solid.None);
        var fluorescent = BuildFurniturePrefab("Fluorescent", Solid.None);
        var clock = BuildFurniturePrefab("WallClock", Solid.None);
        var frame = BuildFurniturePrefab("PictureFrame", Solid.None);
        var poster = BuildFurniturePrefab("Poster", Solid.None);
        var rug = BuildFurniturePrefab("Rug01", Solid.None);
        var mat = BuildFurniturePrefab("Rug06", Solid.None);
        var phone = BuildFurniturePrefab("DeskTelephone", Solid.None);
        // Household set (WhiteWood), same palette.
        GameObject desk = Household_("PlainDesk"), chair = Household_("OfficeChair"), computer = Household_("DesktopSet"), laptop = Household_("Laptop"),
            sofa = Household_("Sofa3"), armchair = Household_("Armchair"), lowTable = Household_("LowTable"), bookcase = Household_("Bookcase"),
            drawer = Household_("Drawer3"), display = Household_("DisplayCabinet"), bin = Household_("TrashBin"), ac = Household_("StandingAC");

        var entrance = Group(world, "Entrance");
        Put(entrance, mat, 0, -7.2f);
        Put(entrance, umbrella, -2.6f, -7.9f);
        Put(entrance, plant5, 2.8f, -7.6f);
        Put(entrance, plant4, -3.6f, -7.6f);

        var reception = Group(world, "Reception");
        Put(reception, counter, 0, -2f, 180);
        var receptionChair = Put(reception, chair, 0, -.85f, 180);
        Put(reception, phone, 1.1f, -2f, 180, 1.12f);
        Put(reception, laptop, -.4f, -2f, 0, 1.12f);
        foreach (float x in new[] { -1.6f, 0f, 1.6f }) Put(reception, low, x, .35f);

        var lounge = Group(world, "WaitingLounge");
        Put(lounge, rug, -10.2f, -4.6f, 90);
        Put(lounge, sofa, -12.4f, -4.6f, 90);
        Put(lounge, lowTable, -10.3f, -4.6f, 90);
        Put(lounge, armchair, -8.5f, -4.6f, -90);
        Put(lounge, bench, -8.7f, -7.85f);
        Put(lounge, bench, -6.1f, -7.85f);
        Put(lounge, plant5, -12.4f, -7.5f);
        Put(lounge, plant2, -12.4f, -1.4f);
        Put(lounge, dispenser, -12.72f, -2.6f, 90);

        // Listing wall either side of the door, above the benches on the lounge side.
        var listingsGroup = Group(world, "ListingWall");
        foreach (float y in new[] { 1.03f, 2.15f })
            foreach (float x in new[] { -10.2f, -9.35f, -8.5f, -7.65f, -6.8f, -5.95f, -5.1f })
                Listing(listingsGroup, poster, x, -8.19f, 0, y);
        foreach (float x in new[] { 4.2f, 5.4f, 6.6f, 7.8f })
            Listing(listingsGroup, frame, x, -8.2f, 0, 1.2f);

        // Three consultation desks along the east wall: staff on the wall side, two guests facing them.
        var consult = Group(world, "ConsultationDesks");
        var consultChairs = new List<GameObject>();
        foreach (float z in new[] { -5.6f, -1.9f, 1.8f })
        {
            Put(consult, desk, 10.1f, z, 90);
            // The set extends further on its tower side (+Z at this yaw), so it sits off-centre and the phone takes the free end.
            Put(consult, computer, 10.1f, z + .05f, 90, .78f);
            Put(consult, phone, 10.1f, z - .6f, 90, .78f);
            consultChairs.Add(Put(consult, chair, 11.1f, z, -90));
            Put(consult, guest, 9.2f, z - .45f, 90);
            Put(consult, guest, 9.2f, z + .45f, 90);
        }
        foreach (float z in new[] { -3.75f, -.05f })
            foreach (float x in new[] { 9.25f, 10.85f }) Put(consult, low, x, z);
        Put(consult, bin, 11.9f, -7.6f);

        var files = Group(world, "FileCabinets");
        foreach (float z in new[] { -6.1f, -4.9f, 1.2f, 2.4f }) Put(files, bookcase, 12.77f, z, -90);
        foreach (float z in new[] { -2.5f, -1.4f }) Put(files, drawer, 12.68f, z, -90);
        foreach (float x in new[] { 4f, 5.2f, 6.4f, 7.6f }) Put(files, bookcase, x, 7.98f, 180);
        foreach (float x in new[] { 8.8f, 9.87f }) Put(files, display, x, 7.91f, 180);
        foreach (float x in new[] { 10.95f, 12.02f }) Put(files, drawer, x, 7.88f, 180);
        Put(files, plant4, 12.3f, 4.2f);
        NoShadow(Put(files, clock, 5.4f, 8.2f, 180, 2.35f));

        var staff = Group(world, "StaffDesks");
        var staffChairs = new List<GameObject>();
        foreach (float x in new[] { 4.8f, 7.3f, 9.8f })
        {
            Put(staff, desk, x, 5f);
            Put(staff, computer, x + .12f, 5f, 0, .78f);
            staffChairs.Add(Put(staff, chair, x, 5.85f, 180));
        }
        Put(staff, bin, 6.05f, 4.9f);

        var manager = Group(world, "ManagerDesk");
        Put(manager, desk, -1f, 5.2f);
        Put(manager, computer, -.88f, 5.2f, 0, .78f);
        Put(manager, chair, -1f, 6.05f, 180);
        Put(manager, guest, -1.45f, 3.95f);
        Put(manager, guest, -.55f, 3.95f);
        foreach (float x in new[] { -2.9f, -1.7f, -.5f, .7f }) Put(manager, bookcase, x, 7.98f, 180);
        Put(manager, plant4, 2f, 7.5f);
        Put(manager, ac, -4.55f, 7.95f, 180);
        Listing(manager, frame, 2.9f, 8.2f, 180, 1.3f);

        var room = Group(world, "MeetingRoom");
        foreach (float x in new[] { -12.19f, -10.59f, -8.99f, -7.39f, -5.79f }) Put(room, high, x, 1.6f);
        foreach (float z in new[] { 4.4f, 6f, 7.39f }) Put(room, high, -5.2f, z, 90);
        Put(room, meeting, -9.3f, 4.9f);
        foreach (float x in new[] { -10.4f, -9.3f, -8.2f })
        {
            Put(room, guest, x, 3.75f);
            Put(room, guest, x, 6.05f, 180);
        }
        Put(room, phone, -8.2f, 4.9f, 180, .78f);
        NoShadow(Put(room, whiteboard, -13f, 4.9f, 90, .9f));
        Put(room, plant2, -12.4f, 7.6f);
        Listing(room, frame, -9.3f, 8.2f, 180, 1.3f);

        // Free-standing listing board in the open middle of the room, readable from both sides.
        var board = Group(world, "ListingBoard");
        foreach (float x in new[] { 4f, 5.6f }) Put(board, high, x, .9f);
        foreach (float x in new[] { 3.6f, 4.8f, 6f })
        {
            Listing(board, poster, x, .84f, 180, .9f);
            Listing(board, poster, x, .96f, 0, .9f);
        }
        Put(board, plant2, 2.6f, .9f);
        Put(board, plant2, 6.9f, .9f);

        var lighting = Group(world, "CeilingLights");
        foreach (float x in new[] { -10f, -5f, 0f, 5f, 10f })
            foreach (float z in new[] { -5f, 0f, 5f })
            {
                var lamp = NoShadow(Put(lighting, fluorescent, x, z));
                Physics.SyncTransforms();
                lamp.transform.position += Vector3.up * (CeilingY - .002f - BoundsOf(lamp).max.y);
            }
        foreach (var p in new[] { new Vector3(-8, 1.9f, -4), new Vector3(8, 1.9f, -4), new Vector3(-8, 1.9f, 4), new Vector3(8, 1.9f, 4), new Vector3(0, 2.4f, 2.6f) })
        {
            var g = new GameObject("Room Light"); g.transform.SetParent(lighting, false); g.transform.position = p;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 12; l.intensity = 1.5f;
            l.color = new Color(1f, .96f, .9f); l.shadows = LightShadows.None;
        }

        // Staff (phase 1: buying and selling, RealEstateDesk): reception, one consultant per kind at the east desks
        // (south to north: land, commercial, house), and two clerks at the back desks who only talk.
        var people = Group(map, "15_Staff");
        Staff(people, receptionChair, FemaleVisual, RealEstateDesk.Reception, "안내 직원",
            "어서 오세요, 부동산입니다. 부지는 1번, 상가는 2번, 주택은 3번 창구에서 상담해 드려요.", RealEstateDesk.ReceptionOptions(), 2.8f);
        Staff(people, consultChairs[0], MaleVisual, RealEstateDesk.LandDesk, "1번 · 부지 상담",
            "부지 상담 창구입니다. 땅을 사시거나 파시겠어요?", RealEstateDesk.ConsultantOptions(RealEstateKind.Land), 3f);
        Staff(people, consultChairs[1], FemaleVisual, RealEstateDesk.CommercialDesk, "2번 · 상가 상담",
            "상가 상담 창구입니다. 가게 자리를 찾으세요?", RealEstateDesk.ConsultantOptions(RealEstateKind.Commercial), 3f);
        Staff(people, consultChairs[2], MaleVisual, RealEstateDesk.HouseDesk, "3번 · 주택 상담",
            "주택 상담 창구입니다. 지금은 매매만 도와드리고, 월세·전세는 준비 중이에요.", RealEstateDesk.ConsultantOptions(RealEstateKind.House), 3f);
        Staff(people, staffChairs[0], FemaleVisual, RealEstateDesk.Prefix + "clerk_1", "사무 직원",
            "계약서 정리 중이에요. 상담은 앞쪽 창구에서 받으실 수 있어요.", null, 2.2f);
        Staff(people, staffChairs[2], MaleVisual, RealEstateDesk.Prefix + "clerk_2", "사무 직원",
            "새 매물 들어오면 창구에 바로 올려 드려요.", null, 2.2f);

        var gameplay = Group(map, "Gameplay");
        var entryPosition = new Vector3(0, .03f, -5.4f);
        AddSpawn(gameplay, EntrySpawn, entryPosition, 0);
        AddSpawn(gameplay, "default", entryPosition, 0);
        var exit = AddPortal(gameplay, "RealEstate Exit", new Vector3(0, .04f, -7.7f), Outside, ExitSpawn, "부동산 나가기");
        exit.radius = 1.6f;

        var cameraObject = new GameObject("Main Camera"); cameraObject.transform.SetParent(map, false);
        cameraObject.transform.SetPositionAndRotation(entryPosition + Vector3.up * 1.6f, Quaternion.identity);
        cameraObject.tag = "MainCamera";
        var cam = cameraObject.AddComponent<Camera>(); cam.nearClipPlane = .03f; cam.farClipPlane = 120;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.78f, .85f, .9f); // daylight behind the glass door
        cameraObject.AddComponent<AudioListener>();
        // Same view as the convenience store and goshiwon; the default 85 is wide enough to make furniture look far away.
        var follow = cameraObject.AddComponent<PlayerCameraController>(); follow.firstPerson = true; follow.firstPersonFOV = 75f;

        var sun = new GameObject("Interior Daylight"); sun.transform.SetParent(map, false);
        sun.transform.rotation = Quaternion.Euler(62, -25, 0);
        var light = sun.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = .55f;
        light.color = new Color(1f, .97f, .92f); light.shadows = LightShadows.Soft; light.shadowStrength = .55f;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.6f, .59f, .56f);
        RenderSettings.fog = false; RenderSettings.skybox = null;
        EditorSceneManager.SaveScene(scene, Interior);
    }

    static MapSpawnPoint AddSpawn(Transform parent, string id, Vector3 position, float yaw)
    {
        var g = new GameObject("Spawn_" + id); if (parent) g.transform.SetParent(parent, false);
        g.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        var spawn = g.AddComponent<MapSpawnPoint>(); spawn.spawnId = id;
        return spawn;
    }

    static StoreInteractionPoint AddPortal(Transform parent, string name, Vector3 position, string destination, string spawn, string prompt)
    {
        var g = new GameObject(name); if (parent) g.transform.SetParent(parent, false);
        g.transform.position = position;
        var point = g.AddComponent<StoreInteractionPoint>(); point.prompt = prompt; point.targetScenePath = destination;
        point.targetSpawnId = spawn; point.radius = 1.8f; point.heightTolerance = 1.4f;
        return point;
    }

    // ---- outside: same structure as "CityHall Interior Portal" / "Spawn_cityhall_exit" ----
    public static Bounds OfficeDoor(Transform office)
    {
        var leaves = office.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Door_Leaf_", StringComparison.Ordinal))
            .SelectMany(t => t.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        if (leaves.Length == 0) throw new InvalidOperationException("RE_01 door leaves not found.");
        var door = leaves[0].bounds;
        foreach (var r in leaves) door.Encapsulate(r.bounds);
        return door;
    }

    public static Transform FindOffice(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
        .Single(t => t.name == "RE_01" && t.parent && t.parent.name == "CivicDistrict_Community");

    static void ConnectOutside()
    {
        var scene = EditorSceneManager.OpenScene(Outside, OpenSceneMode.Single);
        var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        foreach (var old in all.Where(t => t && (t.name == OutsidePortalName || t.name == OutsideSpawnName)).ToArray())
            Object.DestroyImmediate(old.gameObject);
        all = all.Where(t => t).ToArray(); // the removed entries above are destroyed references now
        var office = FindOffice(scene);
        var door = OfficeDoor(office);
        var body = office.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        float front = Mathf.Sign(door.center.z - body.center.z);
        var doorFace = new Vector3(door.center.x, door.min.y, door.center.z + front * door.extents.z);
        Physics.SyncTransforms();
        var map = all.Single(t => t.name == "Map_CivicDistrict");
        var portalPosition = Grounded(doorFace + Vector3.forward * front * .9f, door.min.y);
        AddPortal(map, OutsidePortalName, portalPosition + Vector3.up * .03f, Interior, EntrySpawn, "부동산 들어가기");
        var spawnPosition = Grounded(doorFace + Vector3.forward * front * 1.6f, door.min.y);
        var spawn = AddSpawn(map, ExitSpawn, spawnPosition + Vector3.up * .03f, front > 0 ? 0 : 180);
        spawn.gameObject.name = OutsideSpawnName;
        Debug.Log("[RealEstateInterior] Door " + door + ", portal " + portalPosition + ", exit spawn " + spawnPosition);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static Vector3 Grounded(Vector3 p, float fallback)
    {
        var hits = Physics.RaycastAll(new Vector3(p.x, fallback + 2f, p.z), Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0) throw new InvalidOperationException("No ground in front of the RE_01 door at " + p);
        p.y = hits.Max(h => h.point.y);
        return p;
    }

    // The server loads walls per map from Resources; only this map and the Civic entry change, so rebuild just those two.
    static void RefreshServerCollision()
    {
        string manifestPath = "Assets/Resources/" + ServerCombatCollision.ResourceFolder + "/manifest.json";
        var report = JsonUtility.FromJson<CombatCollisionBuilder.Report>(File.ReadAllText(manifestPath));
        foreach (string path in new[] { Outside, Interior })
        {
            var entry = CombatCollisionBuilder.BuildMap(path);
            report.maps.RemoveAll(m => m.map == path);
            report.maps.Add(entry);
        }
        report.colliderCount = report.maps.Sum(m => m.colliders);
        File.WriteAllText(manifestPath, JsonUtility.ToJson(report, true) + "\n");
        AssetDatabase.ImportAsset(manifestPath);
    }
}
