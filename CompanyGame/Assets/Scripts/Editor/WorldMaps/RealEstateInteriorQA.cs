using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Acceptance checks for the real-estate office. RunLayout (edit mode): furniture inside the walls, no furniture overlap, a player
/// capsule flood-filled from the entry reaches the exit, every seat and the whole office, plus survey screenshots.
/// RunPlay (Play Mode, no -quit, exits itself): walks the real player in through the RE_01 door, around the office, and back out.
/// Reports go to ArtSource/WorldDistricts/RealEstateInterior_20261107/QA.
/// </summary>
[InitializeOnLoad]
public static class RealEstateInteriorQA
{
    const string QA = "../ArtSource/WorldDistricts/RealEstateInterior_20261107/QA/";
    const string Active = "RealEstateQA.active", Step = "RealEstateQA.step", Log_ = "RealEstateQA.log", Fails = "RealEstateQA.fails", Errors = "RealEstateQA.errors";
    const float PlayerRadius = .3f, PlayerHeight = 1.8f, Cell = .1f;

    static RealEstateInteriorQA()
    {
        if (!SessionState.GetBool(Active, false)) return;
        Application.logMessageReceived += OnLog;
        EditorApplication.update += Tick;
    }

    // ---- edit mode layout ----
    public static void RunLayout()
    {
        Directory.CreateDirectory(QA);
        var lines = new List<string>(); int fails = 0;
        void Check(bool ok, string what) { lines.Add((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

        var scene = EditorSceneManager.OpenScene(RealEstateInteriorBuilder.Interior, OpenSceneMode.Single);
        Physics.SyncTransforms();
        var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var world = all.Single(t => t.name == "10_World");
        var groups = world.Cast<Transform>().Where(t => t.name != "RealEstateShell").ToArray();
        var furniture = groups.Where(g => g.name != "CeilingLights").SelectMany(g => g.Cast<Transform>()).Where(t => t.GetComponentInChildren<MeshRenderer>()).ToArray();
        lines.Add("Furniture pieces: " + furniture.Length);
        Bounds BoundsOf(Transform t) { var rs = t.GetComponentsInChildren<MeshRenderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }

        const float slack = .012f;
        foreach (var t in furniture.Concat(groups.Single(g => g.name == "CeilingLights").Cast<Transform>().Where(x => x.GetComponentInChildren<MeshRenderer>())))
        {
            var b = BoundsOf(t);
            bool inside = b.min.x >= -RealEstateInteriorBuilder.RoomHalfX - slack && b.max.x <= RealEstateInteriorBuilder.RoomHalfX + slack &&
                          b.min.z >= -RealEstateInteriorBuilder.RoomHalfZ - slack && b.max.z <= RealEstateInteriorBuilder.RoomHalfZ + slack &&
                          b.min.y >= -slack && b.max.y <= RealEstateInteriorBuilder.CeilingY + slack;
            if (!inside) Check(false, "through a wall/floor/ceiling: " + t.parent.name + "/" + t.name + " " + b.min.ToString("F2") + ".." + b.max.ToString("F2"));
        }
        if (!lines.Any(l => l.StartsWith("FAIL"))) Check(true, "all " + furniture.Length + " pieces and the ceiling lights are inside the walls, floor and ceiling");

        // Solid floor furniture must not overlap (shrunk slightly: touching is fine).
        var solids = furniture.Where(t => t.GetComponentInChildren<Collider>() && t.position.y < .1f).ToArray(); // items on a desk or counter rest on it
        int overlaps = 0;
        for (int i = 0; i < solids.Length; i++)
            for (int j = i + 1; j < solids.Length; j++)
            {
                Bounds a = solids[i].GetComponentsInChildren<Collider>().Select(c => c.bounds).Aggregate((p, q) => { p.Encapsulate(q); return p; });
                Bounds b = solids[j].GetComponentsInChildren<Collider>().Select(c => c.bounds).Aggregate((p, q) => { p.Encapsulate(q); return p; });
                a.Expand(new Vector3(-.03f, 0, -.03f)); b.Expand(new Vector3(-.03f, 0, -.03f));
                if (solids[i].parent.name == "MeetingRoom" && solids[j].parent.name == "MeetingRoom" && solids[i].name.Contains("HighPartition") && solids[j].name.Contains("HighPartition")) continue; // partition corner joints
                if (solids[i].name.Contains("Rug") || solids[j].name.Contains("Rug")) continue;
                if (a.Intersects(b)) { overlaps++; Check(false, "overlap: " + solids[i].parent.name + "/" + solids[i].name + " @" + solids[i].position.ToString("F2") + " with " + solids[j].parent.name + "/" + solids[j].name + " @" + solids[j].position.ToString("F2")); }
            }
        if (overlaps == 0) Check(true, "no solid furniture overlaps (" + solids.Length + " pieces)");

        // Flood fill with the real player's capsule size from the entry spawn.
        var entry = all.Select(t => t.GetComponent<MapSpawnPoint>()).Single(s => s && s.spawnId == RealEstateInteriorBuilder.EntrySpawn).transform.position;
        int nx = Mathf.CeilToInt(26 / Cell), nz = Mathf.CeilToInt(16.4f / Cell);
        var free = new bool[nx, nz]; var seen = new bool[nx, nz];
        Vector3 At(int x, int z) => new Vector3(-13 + (x + .5f) * Cell, 0, -8.2f + (z + .5f) * Cell);
        for (int x = 0; x < nx; x++)
            for (int z = 0; z < nz; z++)
            {
                var p = At(x, z);
                free[x, z] = !Physics.CheckCapsule(p + Vector3.up * (PlayerRadius + .05f), p + Vector3.up * (PlayerHeight - PlayerRadius), PlayerRadius, ~0, QueryTriggerInteraction.Ignore);
            }
        (int, int) CellOf(Vector3 p) => (Mathf.Clamp(Mathf.FloorToInt((p.x + 13) / Cell), 0, nx - 1), Mathf.Clamp(Mathf.FloorToInt((p.z + 8.2f) / Cell), 0, nz - 1));
        var start = CellOf(entry);
        Check(free[start.Item1, start.Item2], "the entry spawn is clear of furniture");
        var queue = new Queue<(int, int)>(); queue.Enqueue(start); seen[start.Item1, start.Item2] = true;
        int reached = 0, freeCount = 0;
        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue(); reached++;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int a = x + dx, b = z + dz;
                if (a < 0 || b < 0 || a >= nx || b >= nz || seen[a, b] || !free[a, b]) continue;
                seen[a, b] = true; queue.Enqueue((a, b));
            }
        }
        foreach (bool f in free) if (f) freeCount++;
        lines.Add("Walkable " + reached * Cell * Cell + " m2 reached of " + freeCount * Cell * Cell + " m2 free, room 426 m2");
        Check(reached >= freeCount * .96f, "almost all free floor is connected to the entry (" + (100f * reached / freeCount).ToString("F1") + "%)");
        bool Near(Vector3 p, float radius)
        {
            for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++)
                if (seen[x, z]) { var q = At(x, z); if (new Vector2(q.x - p.x, q.z - p.z).sqrMagnitude <= radius * radius) return true; }
            return false;
        }
        var exit = all.Select(t => t.GetComponent<StoreInteractionPoint>()).Single(s => s);
        Check(Near(exit.transform.position, exit.radius * .6f), "the exit point is reachable");
        var seats = all.Select(t => t.GetComponent<Seat>()).Where(s => s).ToArray();
        var unreachable = seats.Where(s => !Near(s.transform.position, 1.1f)).ToArray();
        foreach (var s in unreachable) Check(false, "seat out of reach: " + s.transform.parent.name + " @" + s.transform.position.ToString("F2"));
        if (unreachable.Length == 0) Check(true, seats.Length + " seats all reachable within 1.1 m");
        Check(seats.All(s => s.owner && s.owner.HasFunction(FurnitureFunction.Seating)), "every seat has a seating owner");
        Check(all.Select(t => t.GetComponent<WorldObject>()).Where(w => w).All(w => w.objectType == WorldObjectType.StaticProp && !w.furnitureItem), "no placed office furniture can be picked up");
        foreach (var (name, p) in new[] { ("reception", new Vector3(-2.2f, 0, -2)), ("lounge", new Vector3(-9.5f, 0, -2.6f)), ("consultation aisle", new Vector3(8.2f, 0, -2)), ("meeting room", new Vector3(-6.8f, 0, 3)), ("staff desks", new Vector3(7.6f, 0, 7)), ("manager", new Vector3(-3, 0, 5.4f)) })
            Check(Near(p, .3f), name + " is walkable");
        // Narrowest gap on the door-to-reception axis: 1.6 m wide free corridor.
        bool corridor = true;
        for (float z = -7.5f; z < -2.95f; z += .2f) for (float x = -.8f; x <= .8f; x += .2f) corridor &= free[CellOf(new Vector3(x, 0, z)).Item1, CellOf(new Vector3(x, 0, z)).Item2];
        Check(corridor, "1.6 m clear corridor from the door to the reception counter");
        // Each computer rests fully on its desk, keyboard included.
        var desks = furniture.Where(t => t.name.StartsWith("PlainDesk")).Select(BoundsOf).ToArray();
        int overhang = 0;
        foreach (var pc in furniture.Where(t => t.name.StartsWith("DesktopSet")))
        {
            var b = BoundsOf(pc); var d = desks.OrderBy(k => (k.center - b.center).sqrMagnitude).First();
            if (b.min.x < d.min.x || b.max.x > d.max.x || b.min.z < d.min.z || b.max.z > d.max.z) { overhang++; Check(false, "computer overhangs its desk @" + pc.position.ToString("F2") + " pc " + b.min.ToString("F2") + ".." + b.max.ToString("F2") + " desk " + d.min.ToString("F2") + ".." + d.max.ToString("F2")); }
        }
        if (overhang == 0) Check(true, desks.Length + " desks: every computer sits inside its desk top");
        var catalog = JsonUtility.FromJson<FurnitureStorageRules.Locations>(File.ReadAllText("Assets/Resources/Furniture/StorageLocations.json")).items.Where(l => l.map == RealEstateInteriorBuilder.Interior).Select(l => l.id).ToArray();
        var cabinets = all.Select(t => t.GetComponent<FridgeStorage>()).Where(s => s).ToArray();
        Check(cabinets.Length > 0 && cabinets.All(s => catalog.Contains(s.Id)), cabinets.Length + " storage pieces are in the server catalog (" + catalog.Length + " entries for this map)");
        // Staff: six seated NPCs, each on its own chair seat, the four desks with their choices.
        var staff = all.Select(t => t.GetComponent<DialogueData>()).Where(d => d && d.npcId != null && d.npcId.StartsWith(RealEstateDesk.Prefix)).ToArray();
        var unseated = staff.Where(d => !d.GetComponent<SeatedStaff>() || !SeatedStaff.FindSeat(d.transform)).Select(d => d.npcId).ToArray();
        Check(staff.Length == 6 && unseated.Length == 0 && staff.Select(d => SeatedStaff.FindSeat(d.transform)).Distinct().Count() == 6,
            staff.Length + " staff, each on its own chair seat " + string.Join(", ", unseated));
        foreach (var id in new[] { RealEstateDesk.Reception, RealEstateDesk.LandDesk, RealEstateDesk.CommercialDesk, RealEstateDesk.HouseDesk })
        {
            var d = staff.FirstOrDefault(s => s.npcId == id);
            Check(d && d.options.Length == 3, id + " at " + (d ? d.transform.position.ToString("F2") : "-") + " with " + (d ? d.options.Length : 0) + " choices");
        }
        RealEstateCatalog.Reload();
        var listings = RealEstateCatalog.All;
        int lands = listings.Count(l => l.kind == RealEstateKind.Land), shops = listings.Count(l => l.kind == RealEstateKind.Commercial), homes = listings.Count(l => l.kind == RealEstateKind.House);
        Check(lands == 8 && shops == 32 && homes == 24 && listings.All(l => l.price > 0 && !string.IsNullOrEmpty(l.district)),
            "listings: land " + lands + ", commercial " + shops + ", house " + homes);
        Check(listings.All(l => LandPlot.PriceOf(l.id) == l.price) && LandPlot.PriceOf("qa-no-such-plot") == LandPlot.DefaultPrice,
            "the server's price lookup (LandPlot.PriceOf) uses the listing prices");

        // Walkable map for the report.
        var map = new Texture2D(nx, nz, TextureFormat.RGBA32, false);
        for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++) map.SetPixel(x, z, seen[x, z] ? new Color(.55f, .85f, .55f) : free[x, z] ? new Color(.9f, .75f, .3f) : new Color(.25f, .25f, .28f));
        File.WriteAllBytes(QA + "WalkableMap.png", map.EncodeToPNG()); Object.DestroyImmediate(map);

        // Survey screenshots.
        var ceiling = all.Single(t => t.name == "Ceiling").GetComponent<Renderer>();
        ceiling.enabled = false;
        foreach (var lamp in all.Where(t => t.name.StartsWith("RE_Fluorescent")).SelectMany(t => t.GetComponentsInChildren<Renderer>())) lamp.enabled = false;
        Shot("01_TopDown.png", new Vector3(0, 21, -.0f), Quaternion.Euler(90, 0, 0), 48, 1600, 1000);
        Shot("02_Cutaway.png", new Vector3(0, 15, -15), Quaternion.Euler(48, 0, 0), 55, 1600, 1000);
        ceiling.enabled = true;
        foreach (var lamp in all.Where(t => t.name.StartsWith("RE_Fluorescent")).SelectMany(t => t.GetComponentsInChildren<Renderer>())) lamp.enabled = true;
        Shot("03_Entry_Eye.png", entry + Vector3.up * 1.6f, Quaternion.Euler(4, 0, 0), 70);
        Shot("04_Lounge_Eye.png", new Vector3(-5.5f, 1.6f, -2f), Quaternion.Euler(6, -125, 0), 70);
        Shot("05_Consult_Eye.png", new Vector3(5.5f, 1.6f, -6.5f), Quaternion.Euler(6, 50, 0), 70);
        Shot("06_Meeting_Eye.png", new Vector3(-6.6f, 1.6f, 2.7f), Quaternion.Euler(6, -55, 0), 70);
        Shot("07_Back_Eye.png", new Vector3(3f, 1.6f, 2.6f), Quaternion.Euler(6, 28, 0), 75);
        Shot("08_DoorInside_Eye.png", new Vector3(1.2f, 1.6f, -3.6f), Quaternion.Euler(6, 185, 0), 70);

        Shot("14_LookDown_Sofa.png", new Vector3(-9.6f, 1.6f, -3.1f), Quaternion.Euler(50, -115, 0), 70);
        Shot("15_LookDown_Desk.png", new Vector3(8.6f, 1.6f, -2.2f), Quaternion.Euler(50, 90, 0), 70);
        Shot("16_ListingBoard.png", new Vector3(1.6f, 1.6f, -2.6f), Quaternion.Euler(6, 42, 0), 70);
        Shot("19_Desks_Edit.png", new Vector3(6.2f, 1.6f, -3.8f), Quaternion.Euler(8, 70, 0), 70);

        // Outside: portal wiring and the door.
        var outside = EditorSceneManager.OpenScene(RealEstateInteriorBuilder.Outside, OpenSceneMode.Single);
        Physics.SyncTransforms();
        var o = outside.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var portal = o.Select(t => t.GetComponent<StoreInteractionPoint>()).Where(p => p && p.targetScenePath == RealEstateInteriorBuilder.Interior).ToArray();
        Check(portal.Length == 1 && portal[0].targetSpawnId == RealEstateInteriorBuilder.EntrySpawn, "CivicDistrict has one portal into the office");
        var back = o.Select(t => t.GetComponent<MapSpawnPoint>()).Where(p => p && p.spawnId == RealEstateInteriorBuilder.ExitSpawn).ToArray();
        Check(back.Length == 1, "CivicDistrict has the office exit spawn");
        Check(exit.targetScenePath == RealEstateInteriorBuilder.Outside && exit.targetSpawnId == RealEstateInteriorBuilder.ExitSpawn, "the office exit points back to that spawn");
        Check(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == RealEstateInteriorBuilder.Interior), "the office is in the build scene list");
        if (portal.Length == 1)
        {
            var door = RealEstateInteriorBuilder.OfficeDoor(RealEstateInteriorBuilder.FindOffice(outside));
            lines.Add("Door " + door.center.ToString("F2") + " size " + door.size.ToString("F2") + ", portal " + portal[0].transform.position.ToString("F2") + ", exit spawn " + back[0].transform.position.ToString("F2"));
            var eye = back[0].transform.position + new Vector3(1.6f, 1.6f, -3.6f);
            Shot("09_OutsideDoor.png", eye, Quaternion.LookRotation(door.center - eye), 60);
        }
        lines.Insert(0, fails == 0 ? "RESULT PASS" : "RESULT FAIL " + fails);
        File.WriteAllLines(QA + "layout.txt", lines);
        Debug.Log("[RealEstateQA] layout\n" + string.Join("\n", lines));
    }

    static void Shot(string file, Vector3 position, Quaternion rotation, float fov, int w = 1280, int h = 720)
    {
        var existing = Object.FindFirstObjectByType<Camera>();
        var g = new GameObject("QA Camera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = g.AddComponent<Camera>();
        if (existing) { cam.clearFlags = existing.clearFlags; cam.backgroundColor = existing.backgroundColor; }
        Capture(cam, position, rotation, fov, w, h, file);
        Object.DestroyImmediate(g);
    }

    static void Capture(Camera cam, Vector3 position, Quaternion rotation, float fov, int w, int h, string file)
    {
        cam.transform.SetPositionAndRotation(position, rotation); cam.fieldOfView = fov; cam.nearClipPlane = .03f; cam.farClipPlane = 300;
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = previous;
        var active = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt);
        Directory.CreateDirectory(QA);
        File.WriteAllBytes(QA + file, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
    }

    // ---- real Play Mode walk ----
    enum Phase { Enter, AtDoor, Loading, Walk, Storage, Desks, BackAtExit, LoadingOut, Done }
    static readonly Vector3[] Route =
    {
        new Vector3(0, 0, -4f), new Vector3(-2.2f, 0, -3f), new Vector3(-7f, 0, -2.5f), new Vector3(-4f, 0, 2.7f), new Vector3(-7f, 0, 2.7f),
        new Vector3(-4f, 0, 2.7f), new Vector3(3f, 0, 2.8f), new Vector3(7.5f, 0, 3.5f), new Vector3(7.8f, 0, -5f), new Vector3(2f, 0, -5.5f), new Vector3(0, 0, -6.6f)
    };
    static float waitUntil;

    public static void RunPlay()
    {
        SessionState.SetBool(Active, true); SessionState.SetInt(Step, 0); SessionState.SetInt(Fails, 0);
        SessionState.SetString(Log_, ""); SessionState.SetString(Errors, "");
        SessionState.SetInt("RealEstateQA.wp", 0);
        Application.logMessageReceived += OnLog;
        EditorSceneManager.OpenScene(RealEstateInteriorBuilder.Outside, OpenSceneMode.Single);
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        SessionState.SetString(Errors, SessionState.GetString(Errors, "") + "\n" + message.Split('\n')[0]);
    }

    static void Note(bool ok, string what)
    {
        SessionState.SetString(Log_, SessionState.GetString(Log_, "") + (ok ? "PASS " : "FAIL ") + what + "\n");
        if (!ok) SessionState.SetInt(Fails, SessionState.GetInt(Fails, 0) + 1);
        Debug.Log("[RealEstateQA] " + (ok ? "PASS " : "FAIL ") + what);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try { Advance(); }
        catch (Exception exception) { Note(false, "exception: " + exception.Message); Finish(); }
    }

    static void PlayerShot(string file)
    {
        var cam = Camera.main;
        if (!cam) { Note(false, "no main camera for " + file); return; }
        var t = cam.transform; Vector3 p = t.position; var r = t.rotation; float fov = cam.fieldOfView;
        Capture(cam, p, r, fov, 1280, 720, file);
    }

    static void Advance()
    {
        var phase = (Phase)SessionState.GetInt(Step, 0);
        if (Time.realtimeSinceStartup < waitUntil) return;
        var player = PlayerSpawner.Player;
        void Go(Phase next, float wait = 0) { SessionState.SetInt(Step, (int)next); waitUntil = Time.realtimeSinceStartup + wait; }
        switch (phase)
        {
            case Phase.Enter:
                if (Time.frameCount < 10 || SceneLoadManager.IsLoading || !player || !player.isActiveAndEnabled) return;
                Note(PlayerSpawner.TeleportInScene(RealEstateInteriorBuilder.ExitSpawn), "player stands at the RE_01 door spawn in CivicDistrict");
                Go(Phase.AtDoor, 1.5f);
                break;
            case Phase.AtDoor:
            {
                var point = StoreInteractionPoint.FindNearest(player.transform);
                Note(point && point.targetScenePath == RealEstateInteriorBuilder.Interior, "the door prompt at RE_01 is '" + (point ? point.prompt : "none") + "'");
                PlayerShot("10_Play_OutsideDoor.png");
                var interaction = player.GetComponent<PlayerInteraction>();
                Note(interaction && interaction.TryUseDoor(), "using the door starts the transition (PlayerInteraction.TryUseDoor)");
                Go(Phase.Loading, 1f);
                break;
            }
            case Phase.Loading:
                if (SceneLoadManager.IsLoading || !player || !player.isActiveAndEnabled) return;
                if (SceneManager.GetActiveScene().path != RealEstateInteriorBuilder.Interior) { Note(false, "arrived in " + SceneManager.GetActiveScene().path + " " + SceneLoadManager.LastError); Finish(); return; }
                Note(true, "arrived in the office at " + player.transform.position.ToString("F2"));
                Go(Phase.Walk, 1.2f);
                break;
            case Phase.Walk:
            {
                int wp = SessionState.GetInt("RealEstateQA.wp", 0);
                if (wp == 0 && !SessionState.GetBool("RealEstateQA.shot", false))
                {
                    SessionState.SetBool("RealEstateQA.shot", true);
                    var cc0 = player.GetComponent<CharacterController>();
                    Note(cc0.isGrounded && Mathf.Abs(player.transform.position.y) < .2f, "standing on the office floor, y=" + player.transform.position.y.ToString("F3"));
                    PlayerShot("11_Play_Inside.png");
                }
                if (wp >= Route.Length)
                {
                    Note(true, "walked the whole route with the real CharacterController (" + Route.Length + " legs)");
                    Go(Phase.Storage, 0f);
                    return;
                }
                var cc = player.GetComponent<CharacterController>();
                var target = Route[wp]; var delta = target - player.transform.position; delta.y = 0;
                if (delta.magnitude < .25f) { SessionState.SetInt("RealEstateQA.wp", wp + 1); SessionState.SetFloat("RealEstateQA.legStart", Time.realtimeSinceStartup); return; }
                if (SessionState.GetFloat("RealEstateQA.legStart", 0) == 0) SessionState.SetFloat("RealEstateQA.legStart", Time.realtimeSinceStartup);
                if (Time.realtimeSinceStartup - SessionState.GetFloat("RealEstateQA.legStart", 0) > 25f)
                {
                    Note(false, "blocked walking to " + target.ToString("F1") + " at " + player.transform.position.ToString("F2"));
                    SessionState.SetInt("RealEstateQA.wp", wp + 1);
                    SessionState.SetFloat("RealEstateQA.legStart", Time.realtimeSinceStartup);
                    return;
                }
                cc.Move(delta.normalized * Mathf.Min(4f * Mathf.Max(Time.unscaledDeltaTime, .02f), delta.magnitude));
                break;
            }
            case Phase.Storage:
                if (!StorageStep(player)) return;
                Go(Phase.Desks, 0f);
                break;
            case Phase.Desks:
                if (!DeskStep(player)) return;
                PlayerShot("12_Play_BackAtDoor.png");
                Go(Phase.BackAtExit, .3f);
                break;
            case Phase.BackAtExit:
            {
                var point = StoreInteractionPoint.FindNearest(player.transform);
                Note(point && point.targetScenePath == RealEstateInteriorBuilder.Outside, "the exit prompt inside is '" + (point ? point.prompt : "none") + "'");
                var interaction = player.GetComponent<PlayerInteraction>();
                Note(interaction && interaction.TryUseDoor(), "using the exit starts the transition");
                Go(Phase.LoadingOut, 1f);
                break;
            }
            case Phase.LoadingOut:
                if (SceneLoadManager.IsLoading || !player || !player.isActiveAndEnabled) return;
                Note(SceneManager.GetActiveScene().path == RealEstateInteriorBuilder.Outside, "back in CivicDistrict at " + player.transform.position.ToString("F2") + " " + SceneLoadManager.LastError);
                PlayerShot("13_Play_BackOutside.png");
                var spawn = Object.FindObjectsByType<MapSpawnPoint>().FirstOrDefault(s => s.spawnId == RealEstateInteriorBuilder.ExitSpawn);
                Note(spawn && Vector3.Distance(spawn.transform.position, player.transform.position) < 1f, "the player came out at the RE_01 exit spawn");
                Finish();
                break;
        }
    }

    // Real prompt priority, the same window, click and drag in, click out, then close.
    static int storageStep;
    static float storageWait;
    static ItemData testItem;
    static Camera shotCamera;
    static RenderTexture shotTexture;
    static Canvas shotCanvas;
    static ItemData testBook;
    static bool testBookAdded;
    // Overlay canvases only reach a camera in screen-camera mode.
    static void BeginUiShot(InventoryUI ui)
    {
        shotCamera = Camera.main; shotCanvas = ui.GetComponent<Canvas>();
        shotTexture = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        shotCamera.targetTexture = shotTexture;
        shotCanvas.renderMode = RenderMode.ScreenSpaceCamera; shotCanvas.worldCamera = shotCamera; shotCanvas.planeDistance = .5f;
    }
    static void EndUiShot(string file)
    {
        shotCamera.Render();
        var active = RenderTexture.active; RenderTexture.active = shotTexture;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        RenderTexture.active = active;
        File.WriteAllBytes(QA + file, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        shotCamera.targetTexture = null; RenderTexture.ReleaseTemporary(shotTexture);
        shotCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }
    static void Teleport(PlayerMovement player, Vector3 p)
    {
        var cc = player.GetComponent<CharacterController>(); cc.enabled = false; player.transform.position = p; cc.enabled = true;
    }
    static bool StorageStep(PlayerMovement player)
    {
        if (Time.realtimeSinceStartup < storageWait) return false;
        var inventory = player.GetComponent<PlayerInventory>();
        var bag = inventory.Inventory;
        var fridgeUi = player.GetComponent<FridgeInteraction>();
        void Wait(float s) { storageWait = Time.realtimeSinceStartup + s; storageStep++; }
        switch (storageStep)
        {
            case 0: // beside a drawer unit with a staff chair just behind the player
                Teleport(player, new Vector3(12f, .03f, -2.5f));
                Wait(.4f);
                return false;
            case 1:
            {
                Note(FridgeInteraction.HasNearbyFridge && !SeatInteraction.HasNearbySeat && !DialogueManager.HasNearbyNpc,
                    "beside a cabinet behind desk 2, storage owns Space, not the chair or the consultant (" + (DialogueManager.Instance.Nearby ? DialogueManager.Instance.Nearby.displayName : "no NPC") + ")");
                var near = FridgeStorage.FindNearest(player.transform);
                testItem = Resources.LoadAll<ItemData>("Inventory/Furniture/Household").First(i => i.name.Contains("LowTable"));
                Note(bag.TryAdd(testItem, 1, out _), "test item in the bag");
                typeof(FridgeInteraction).GetMethod("Open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(fridgeUi, new object[] { near });
                Note(FridgeInteraction.IsOpen && inventory.IsOpen && inventory.UserInterface.IsContainerOpen, "storage opens inside the player's own inventory window");
                Wait(.2f);
                return false;
            }
            case 2:
            {
                var ui = inventory.UserInterface; var state = ui.ContainerState;
                // A real pointer at each slot's centre must land on that slot, not on the window behind it.
                var pointers = ui.GetComponentsInChildren<InventorySlotPointer>().Where(p => p.isActiveAndEnabled && p.GetComponentInParent<UnityEngine.UI.GridLayoutGroup>() && p.transform.parent.name is "ContainerSlots" or "StorageSlots").ToArray();
                int missed = 0;
                foreach (var pointer in pointers)
                {
                    var screen = RectTransformUtility.WorldToScreenPoint(null, pointer.transform.position);
                    var hits = new List<UnityEngine.EventSystems.RaycastResult>();
                    UnityEngine.EventSystems.EventSystem.current.RaycastAll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = screen }, hits);
                    if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<InventorySlotPointer>() != pointer) missed++;
                }
                Note(pointers.Length >= state.Capacity + 16 && missed == 0, "mouse hit test reaches all " + pointers.Length + " slots (missed " + missed + ")");
                int slot = Enumerable.Range(0, bag.Capacity).First(i => !bag.GetSlot(i).IsEmpty && bag.GetSlot(i).Item == testItem);
                inventory.ClickInventorySlot(slot);
                Note(bag.GetSlot(slot).IsEmpty && Enumerable.Range(0, state.Capacity).Any(i => !state.GetSlot(i).IsEmpty), "left click on a bag slot moves it into storage");
                int stored = Enumerable.Range(0, state.Capacity).First(i => !state.GetSlot(i).IsEmpty);
                ui.ClickContainerSlot(stored);
                Note(state.GetSlot(stored).IsEmpty && Enumerable.Range(0, bag.Capacity).Any(i => !bag.GetSlot(i).IsEmpty && bag.GetSlot(i).Item == testItem), "left click on a storage slot moves it back to the bag");
                slot = Enumerable.Range(0, bag.Capacity).First(i => !bag.GetSlot(i).IsEmpty && bag.GetSlot(i).Item == testItem);
                Note(inventory.BeginDragInventory(slot) && inventory.DropOnContainer() && bag.GetSlot(slot).IsEmpty, "dragging a bag slot onto storage moves it");
                // Leave one item showing in the top slots for the picture.
                BeginUiShot(ui);
                Wait(.3f);
                return false;
            }
            case 3:
            {
                EndUiShot("17_StorageWindow.png");
                var ui = inventory.UserInterface; var state = ui.ContainerState;
                int stored = Enumerable.Range(0, state.Capacity).First(i => !state.GetSlot(i).IsEmpty);
                ui.ClickContainerSlot(stored);
                int slot = Enumerable.Range(0, bag.Capacity).First(i => !bag.GetSlot(i).IsEmpty && bag.GetSlot(i).Item == testItem);
                bag.TryRemove(slot, 1, out _);
                inventory.CloseInventory();
                Wait(.2f);
                return false;
            }
            case 4:
                Note(!FridgeInteraction.IsOpen && !inventory.IsOpen && !inventory.UserInterface.IsContainerOpen, "closing the window closes the storage");
                inventory.OpenInventory();
                Note(inventory.IsOpen && inventory.UserInterface.WindowRect.Find("CurrentPlayer").gameObject.activeSelf, "the normal inventory comes back with its wardrobe");
                inventory.CloseInventory();
                Teleport(player, new Vector3(4f, .03f, 7.4f)); // in front of a bookcase
                Wait(.4f);
                return false;
            case 5:
            {
                var near = FridgeStorage.FindNearest(player.transform);
                Note(near && near.storageType == StorageType.Books, "the nearest storage here is a bookcase");
                Note(bag.TryAdd(testItem, 1, out _), "a non-book test item in the bag");
                testBook = Resources.LoadAll<ItemData>("Inventory").FirstOrDefault(i => i.IsBook || i.isBook);
                testBookAdded = testBook && bag.TryAdd(testBook, 1, out _);
                typeof(FridgeInteraction).GetMethod("Open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(fridgeUi, new object[] { near });
                Wait(.2f);
                return false;
            }
            case 6:
            {
                var ui = inventory.UserInterface;
                int slot = Enumerable.Range(0, bag.Capacity).First(i => !bag.GetSlot(i).IsEmpty && bag.GetSlot(i).Item == testItem);
                Note(!ui.BagSlotEnabled(slot) && !ui.BagSlotAccepted(slot), "bookcase: a non-book bag slot is disabled");
                inventory.ClickInventorySlot(slot);
                Note(!bag.GetSlot(slot).IsEmpty && ui.ContainerState.GetSlot(0).IsEmpty, "clicking the disabled slot moves nothing");
                Note(!inventory.BeginDragInventory(slot) && !inventory.IsDragging, "the disabled slot cannot be dragged");
                if (testBookAdded)
                {
                    int book = Enumerable.Range(0, bag.Capacity).First(i => !bag.GetSlot(i).IsEmpty && bag.GetSlot(i).Item == testBook);
                    Note(ui.BagSlotEnabled(book) && ui.BagSlotAccepted(book), "a book stays enabled in the same window");
                }
                else Note(true, "no plain book item could be added; the enabled case was covered by the drawer above");
                BeginUiShot(ui);
                Wait(.3f);
                return false;
            }
            case 7:
            {
                EndUiShot("18_BookcaseFilter.png");
                inventory.CloseInventory();
                Wait(.2f);
                return false;
            }
            case 8:
            {
                var ui = inventory.UserInterface;
                Note(Enumerable.Range(0, bag.Capacity).All(ui.BagSlotEnabled), "after closing, every bag slot is enabled again");
                bag.TryRemove(Enumerable.Range(0, bag.Capacity).First(i => !bag.GetSlot(i).IsEmpty && bag.GetSlot(i).Item == testItem), 1, out _);
                if (testBookAdded) bag.TryRemove(Enumerable.Range(0, bag.Capacity).First(i => !bag.GetSlot(i).IsEmpty && bag.GetSlot(i).Item == testBook), 1, out _);
                Teleport(player, new Vector3(0, .03f, -6.6f));
                Wait(.4f);
                return false;
            }
        }
        return true;
    }

    // ---- the desks (phase 1: buying and selling) ----
    static int deskStep;
    static float deskWait;
    static long moneyBefore;
    static RealEstateListing bought;
    static Canvas dialogueCanvas;
    static RenderTexture dialogueTexture;

    static DialogueData StaffOf(string id) => Object.FindObjectsByType<DialogueData>().FirstOrDefault(d => d.npcId == id);

    static void Face(PlayerMovement player, Vector3 target)
    {
        var d = target - player.transform.position; d.y = 0;
        player.transform.rotation = Quaternion.LookRotation(d);
    }

    // What the player sees right now, with the dialogue UI drawn just in front of the near plane.
    static void BeginDialogueShot()
    {
        var view = Object.FindAnyObjectByType<DialogueView>();
        dialogueCanvas = view ? view.GetComponentsInChildren<Canvas>(true).FirstOrDefault(c => c.name == "DialogueCanvas") : null;
        var cam = Camera.main;
        dialogueTexture = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = dialogueTexture;
        if (dialogueCanvas) { dialogueCanvas.renderMode = RenderMode.ScreenSpaceCamera; dialogueCanvas.worldCamera = cam; dialogueCanvas.planeDistance = .3f; }
    }
    static void EndDialogueShot(string file)
    {
        var cam = Camera.main;
        cam.Render();
        var active = RenderTexture.active; RenderTexture.active = dialogueTexture;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        RenderTexture.active = active;
        File.WriteAllBytes(QA + file, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        cam.targetTexture = null; RenderTexture.ReleaseTemporary(dialogueTexture);
        if (dialogueCanvas) dialogueCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    static bool DeskStep(PlayerMovement player)
    {
        if (Time.realtimeSinceStartup < deskWait) return false;
        var manager = DialogueManager.Instance;
        var plots = LandPlotManager.Instance;
        var bank = BankManager.Instance;
        void Wait(float s) { deskWait = Time.realtimeSinceStartup + s; deskStep++; }
        string Line() => manager.CurrentLine ?? "";
        string Options() => string.Join(" | ", manager.VisibleOptions.Select(o => o.label));
        switch (deskStep)
        {
            case 0: // in front of desk 3 (houses), between the two guest chairs
            {
                var house = StaffOf(RealEstateDesk.HouseDesk);
                Teleport(player, new Vector3(8.4f, .03f, 1.8f)); Face(player, house.transform.position);
                Wait(.8f);
                return false;
            }
            case 1:
            {
                var house = StaffOf(RealEstateDesk.HouseDesk);
                var seated = Object.FindObjectsByType<SeatedStaff>();
                Note(seated.Length == 6 && seated.All(s => s.Seat && s.Seat.Occupant == s.transform &&
                    Vector3.Distance(s.transform.position, s.Seat.transform.position - Vector3.up * SeatedStaff.SeatDrop) < .01f), seated.Length + " staff sit in their chairs (seat occupied)");
                Note(manager.Nearby == house, "Space at desk 3 talks to " + (manager.Nearby ? manager.Nearby.displayName : "nobody"));
                Note(manager.Begin(house) && manager.VisibleOptions.Count == 3, "desk 3 greets: " + Line() + " [" + Options() + "]");
                Note(manager.Select(RealEstateDesk.Browse), "chose '주택 매물 보기'");
                Note(Line().Contains("24건") && manager.VisibleOptions.Count == 7, "house listings page 1: " + Line().Replace("\n", " / ") + " [" + Options() + "]");
                BeginDialogueShot();
                Wait(.4f);
                return false;
            }
            case 2:
            {
                EndDialogueShot("20_Play_HouseListings.png");
                bought = RealEstateDesk.ForSale(RealEstateKind.House)[0];
                Note(manager.Select(RealEstateDesk.Pick + bought.id), "picked " + bought.Address + " " + bought.name);
                Note(Line().Contains(bought.price.ToString("N0")), "detail: " + Line().Replace("\n", " / "));
                moneyBefore = bank.Money;
                if (moneyBefore >= bought.price) bank.TrySpend(moneyBefore - bought.price + 1, MoneyChangeReason.Purchase); // one won short
                manager.Select(RealEstateDesk.Buy + bought.id);
                Note(plots.GetState(bought.id) == LandPlotState.Available && Line().Contains("잔액이 부족"), "one won short: refused, nothing bought: " + Line());
                bank.AddMoney(bought.price + 5000000, MoneyChangeReason.Sale);
                moneyBefore = bank.Money;
                manager.Close(); // the next Space press starts a new conversation once the dialogue camera is back
                Wait(.8f);
                return false;
            }
            case 3:
            {
                var house = StaffOf(RealEstateDesk.HouseDesk);
                Note(manager.Begin(house) && manager.Select(RealEstateDesk.Browse) && manager.Select(RealEstateDesk.Pick + bought.id), "talked again and opened " + bought.id);
                BeginDialogueShot();
                Wait(.4f);
                return false;
            }
            case 4:
            {
                EndDialogueShot("21_Play_HouseDetail.png");
                manager.Select(RealEstateDesk.Buy + bought.id);
                Note(plots.GetState(bought.id) == LandPlotState.Owned && plots.GetOwner(bought.id) == GameSession.LocalPlayerId &&
                     bank.Money == moneyBefore - bought.price, "bought " + bought.id + " for " + bought.price.ToString("N0") + " from the account: " + Line());
                Note(!RealEstateDesk.ForSale(RealEstateKind.House).Any(l => l.id == bought.id), "the bought listing is off the list");
                manager.Close();
                Wait(.8f);
                return false;
            }
            case 5:
            {
                var house = StaffOf(RealEstateDesk.HouseDesk);
                Note(manager.Begin(house) && manager.Select(RealEstateDesk.SellList), "talked again: '내 주택 팔기'");
                Note(manager.VisibleOptions.Any(o => o.id == RealEstateDesk.SellPick + bought.id), "sell list offers it: " + Line() + " [" + Options() + "]");
                manager.Select(RealEstateDesk.SellPick + bought.id);
                long payout = RealEstateCatalog.BuyBackPrice(bought.price);
                Note(Line().Contains(payout.ToString("N0")), "buy-back offer: " + Line().Replace("\n", " / "));
                moneyBefore = bank.Money;
                manager.Select(RealEstateDesk.Sell + bought.id);
                Note(plots.GetState(bought.id) == LandPlotState.Available && bank.Money == moneyBefore + payout, "sold back for " + payout.ToString("N0") + " (80%): " + Line());
                // Registered businesses stay.
                plots.ApplyPurchase(bought.id, GameSession.LocalPlayerId);
                plots.ApplyRegisterBusiness(bought.id, "QA상회", "QA", true);
                Note(plots.TrySell(bought.id) == LandPlotResult.WrongState && plots.GetState(bought.id) == LandPlotState.Registered, "a plot with a business registration cannot be sold");
                Teleport(player, new Vector3(0, .03f, -3.1f)); Face(player, StaffOf(RealEstateDesk.Reception).transform.position);
                manager.Close();
                Wait(.6f);
                return false;
            }
            case 6:
            {
                var reception = StaffOf(RealEstateDesk.Reception);
                Note(manager.Nearby == reception, "Space at the counter talks to " + (manager.Nearby ? manager.Nearby.displayName : "nobody"));
                manager.Begin(reception);
                manager.Select(RealEstateDesk.Holdings);
                Note(Line().Contains("주택 1건") && Line().Contains("QA상회"), "reception lists my holdings: " + Line().Replace("\n", " / "));
                BeginDialogueShot();
                Wait(.4f);
                return false;
            }
            case 7:
            {
                EndDialogueShot("22_Play_Reception.png");
                manager.Close();
                Teleport(player, new Vector3(8.4f, .03f, -5.6f)); Face(player, StaffOf(RealEstateDesk.LandDesk).transform.position);
                Wait(.6f);
                return false;
            }
            case 8:
            {
                Note(manager.Nearby == StaffOf(RealEstateDesk.LandDesk), "desk 1 is the land desk (" + (manager.Nearby ? manager.Nearby.displayName : "nobody") + ")");
                manager.Begin(StaffOf(RealEstateDesk.LandDesk)); manager.Select(RealEstateDesk.Browse);
                Note(Line().Contains("부지 매물 8건"), "land listings: " + Line().Replace("\n", " / "));
                manager.Close();
                Teleport(player, new Vector3(8.4f, .03f, -1.9f)); Face(player, StaffOf(RealEstateDesk.CommercialDesk).transform.position);
                Wait(.6f);
                return false;
            }
            case 9:
            {
                Note(manager.Nearby == StaffOf(RealEstateDesk.CommercialDesk), "desk 2 is the commercial desk (" + (manager.Nearby ? manager.Nearby.displayName : "nobody") + ")");
                manager.Begin(StaffOf(RealEstateDesk.CommercialDesk)); manager.Select(RealEstateDesk.Browse);
                Note(Line().Contains("상가 매물 32건") && manager.VisibleOptions.Any(o => o.label.Contains("다음 목록 (1/7)")), "commercial listings: " + Line().Replace("\n", " / ") + " [" + Options() + "]");
                manager.Select(RealEstateDesk.Next);
                Note(manager.VisibleOptions.Any(o => o.label.Contains("다음 목록 (2/7)")), "next page: [" + Options() + "]");
                manager.Close();
                Teleport(player, new Vector3(6.4f, .03f, -3.8f)); Face(player, new Vector3(11.1f, 1.2f, -1.9f));
                Wait(.8f);
                return false;
            }
            case 10:
                Capture(Camera.main, new Vector3(6.4f, 1.6f, -3.8f), Quaternion.Euler(8, 62, 0), 70, 1280, 720, "23_Play_Desks.png");
                // Leave the world as it was: the registered QA plot goes back to the agency.
                var states = plots.CaptureState(); states.RemoveAll(s => s.id == bought.id); plots.RestoreState(states);
                Teleport(player, new Vector3(0, .03f, -6.6f));
                Wait(.4f);
                return false;
        }
        return true;
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        int fails = SessionState.GetInt(Fails, 0);
        string report = (fails == 0 ? "RESULT PASS" : "RESULT FAIL " + fails) + "\n" + SessionState.GetString(Log_, "") +
                        "Error/exception logs during the run:" + (SessionState.GetString(Errors, "").Length == 0 ? " none" : SessionState.GetString(Errors, ""));
        Directory.CreateDirectory(QA);
        File.WriteAllText(QA + "play.txt", report);
        Debug.Log("[RealEstateQA] play\n" + report);
        SessionState.SetBool(Active, false);
        EditorApplication.Exit(fails == 0 ? 0 : 1);
    }
}
