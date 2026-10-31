using System.Collections;
using System.Collections.Generic;
using CompanyGame.Daldongne;
using CompanyGame.World.Maps;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns the street citizens when an outdoor map is ready. Needs a NavMesh baked in the editor
/// (CitizenNavBuilder) and Resources/Citizens/CitizenMale|CitizenFemale (CitizenPrefabBuilder);
/// without them it logs once and does nothing. Lives in the map scene, so leaving the map removes it.
/// Index, name, line, look and start point are a pure function of (map, index), never of where the player is: every PC
/// builds the same crowd. Each citizen then plans its loop with NpcClock.Seed (again when the seed changes: a server's
/// seed may come after the map), so every PC of one server also sees them walk the same way (12.14). Benches are
/// shared out by position order, so no two citizens plan the same bench.
/// </summary>
public sealed class CitizenSpawner : MonoBehaviour
{
    public const int PerMap = 8;
    const float MinCitizenDistance = 3f;

    public static CitizenSpawner Current { get; private set; }
    public string MapPath { get; private set; }
    public readonly List<CitizenWalker> Citizens = new List<CitizenWalker>();
    bool spawned;
    uint plannedSeed;

    /// <summary>Outdoor maps: everything in Scenes/Maps plus the legacy village scene.</summary>
    public static bool IsOutdoorMap(string scenePath)
    {
        string path = (scenePath ?? "").Replace('\\', '/');
        return path.StartsWith("Assets/Scenes/Maps/") || path == "Assets/Scenes/daldongnaemap.unity";
    }

    /// <summary>Indoor crowd of one interior scene: readers/sitters on its Seat objects, browsers, and chatting pairs.</summary>
    public sealed class IndoorSpec
    {
        public string[] lines;
        public int sitters, browsers, chatPairs;
        public bool reading;
        public string seatFilter;      // regex on the Seat's object name
        public float seatMaxHeight;    // ground-floor seats only
        public Bounds walkArea;        // where browsers and chatters start
    }

    public static readonly Dictionary<string, IndoorSpec> Indoors = new Dictionary<string, IndoorSpec>
    {
        ["Assets/Scenes/Interiors/CivicLibraryInterior.unity"] = new IndoorSpec
        {
            lines = CitizenInfo.LibraryLines, sitters = 3, browsers = 2, chatPairs = 0, reading = true,
            seatFilter = "^SEAT_(West_|WindowChair_|CafeTable_|Sofa_Entry)", seatMaxHeight = 1f,
            walkArea = new Bounds(new Vector3(10.3f, 0f, 1.2f), new Vector3(8.5f, 6f, 9f)), // between the ground shelf rows
        },
        ["Assets/Scenes/Interiors/HandaeHQLobby.unity"] = new IndoorSpec
        {
            lines = CitizenInfo.OfficeLines, sitters = 2, browsers = 2, chatPairs = 1, reading = false,
            seatFilter = "^Seat \\d$", seatMaxHeight = 1f,
            walkArea = new Bounds(new Vector3(2f, 0f, -3.5f), new Vector3(24f, 6f, 11f)),
        },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        Current = null;
        SceneLoadManager.MapReady -= OnMapReady;
        SceneLoadManager.MapReady += OnMapReady;
    }

    // A map played directly in the editor (the offline dev path) gets no MapReady for its first scene.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void FirstScene()
    {
        if (!FindAnyObjectByType<CitizenSpawner>()) OnMapReady(SceneManager.GetActiveScene(), SceneLoadManager.DefaultSpawnId, false);
    }

    static void OnMapReady(Scene scene, string spawnId, bool arrival)
    {
        if (!IsOutdoorMap(scene.path) && !Indoors.ContainsKey(scene.path)) return;
        var host = new GameObject("Citizens (runtime)");
        SceneManager.MoveGameObjectToScene(host, scene);
        host.AddComponent<CitizenSpawner>().MapPath = scene.path;
    }

    void OnDestroy() { if (Current == this) Current = null; }

    IEnumerator Start()
    {
        Current = this;
        yield return null; // NavMeshSurface data is registered on enable; the player is placed by now
        yield return null;
        Spawn();
    }

    void Spawn()
    {
        var triangulation = NavMesh.CalculateTriangulation();
        var male = Resources.Load<GameObject>("Citizens/CitizenMale");
        var female = Resources.Load<GameObject>("Citizens/CitizenFemale");
        if (triangulation.indices.Length < 3 || !male || !female)
        {
            Debug.Log("[Citizens] " + MapPath + ": " + (triangulation.indices.Length < 3 ? "no NavMesh baked (Tools/Company Game/Citizens)" : "citizen prefabs missing") + ", no citizens.");
            Destroy(gameObject);
            return;
        }
        int mapHash = CitizenInfo.Hash(MapPath);
        var rng = new System.Random(mapHash);
        if (Indoors.TryGetValue(MapPath, out var spec)) { SpawnIndoor(spec, mapHash, rng, male, female); return; }
        var seats = FindBenches();
        var holder = new GameObject("holder"); // inactive: DialogueData and the walker must see their data before OnEnable
        holder.SetActive(false);
        Vector3? anchor = FindAnchor();
        var info = FindAnyObjectByType<CitizenNavInfo>();
        var starts = info && info.starts.Length > 0 ? new List<Vector3>(info.starts) : null;
        for (int i = 0; i < PerMap; i++)
        {
            Vector3 point = default;
            if (starts != null ? !TryStart(rng, starts, out point) : !TryPoint(rng, triangulation, anchor, out point)) continue;
            bool isFemale = ((mapHash >> 3) + i) % 2 == 0;
            Create(isFemale ? female : male, holder.transform, i, mapHash, point, rng.Next(360), Mine(seats, i));
        }
        Destroy(holder);
        IgnoreEachOther();
        PlanAll();
        Debug.Log("[Citizens] " + MapPath + ": " + Citizens.Count + " citizens, " + seats.Length + " benches, seed " + plannedSeed + ".");
    }

    void Update()
    {
        if (spawned && NpcClock.Seed != plannedSeed) PlanAll();
    }

    void PlanAll()
    {
        spawned = true;
        plannedSeed = NpcClock.Seed;
        var pieces = new System.Text.StringBuilder();
        foreach (var walker in Citizens) if (walker) { walker.Plan(plannedSeed); pieces.Append(' ').Append(walker.PlanLegs).Append('/').Append(walker.WalkSeconds.ToString("0")).Append('s'); }
        Debug.Log("[Citizens] " + MapPath + ": plans for seed " + plannedSeed + " (pieces/walking):" + pieces);
    }

    // Bench k belongs to citizen k % PerMap.
    static Seat[] Mine(Seat[] seats, int index)
    {
        var mine = new List<Seat>();
        for (int k = index; k < seats.Length; k += PerMap) mine.Add(seats[k]);
        return mine.ToArray();
    }

    void Create(GameObject prefab, Transform holder, int index, int mapHash, Vector3 point, int yaw, Seat[] seats, string[] lines = null, CitizenWalker.Role role = CitizenWalker.Role.Street)
    {
        var citizen = Instantiate(prefab, point, Quaternion.Euler(0f, yaw, 0f), holder);
        citizen.name = "Citizen_" + index;
        int seed = mapHash ^ (index * 7919);
        var dialogue = citizen.AddComponent<DialogueData>();
        dialogue.npcId = "citizen:" + System.IO.Path.GetFileNameWithoutExtension(MapPath) + ":" + index;
        dialogue.displayName = CitizenInfo.NameFor(mapHash, index);
        dialogue.line = lines == null ? CitizenInfo.LineFor(mapHash, index) : CitizenInfo.LineFor(lines, mapHash, index);
        var motion = citizen.GetComponentInChildren<DaldongneAvatarMotion>(true);
        dialogue.head = motion && motion.head ? motion.head : FindChild(citizen.transform, "Head");
        var walker = citizen.GetComponent<CitizenWalker>();
        walker.Init(index, seed, seats);
        walker.SetRole(role);
        CitizenInfo.Dress(citizen, seed);
        citizen.transform.SetParent(null, true); // becomes active here: Awake/OnEnable see the data above
        SceneManager.MoveGameObjectToScene(citizen, gameObject.scene); // DialogueData only works for objects in the current map
        Citizens.Add(walker);
    }

    // ---- interiors -------------------------------------------------------------

    void SpawnIndoor(IndoorSpec spec, int mapHash, System.Random rng, GameObject male, GameObject female)
    {
        var book = Resources.Load<GameObject>("Citizens/CitizenBook");
        var holder = new GameObject("holder");
        holder.SetActive(false);
        var starts = new List<Vector3>();
        var info = FindAnyObjectByType<CitizenNavInfo>();
        if (info) foreach (var p in info.starts) if (spec.walkArea.Contains(p)) starts.Add(p);
        int index = 0;
        foreach (var seat in PickSeats(spec, rng))
        {
            if (!NavMesh.SamplePosition(seat.transform.position, out var near, 3f, NavMesh.AllAreas)) near.position = seat.transform.position;
            var walker = Make(index++, mapHash, near.position, 0, holder.transform, male, female, spec.lines, CitizenWalker.Role.Sit);
            walker.SitAt(seat, spec.reading ? book : null);
        }
        for (int k = 0; k < spec.browsers && starts.Count > 0; k++)
            Make(index++, mapHash, TakeStart(starts, rng), rng.Next(360), holder.transform, male, female, spec.lines, CitizenWalker.Role.Browse);
        for (int k = 0; k < spec.chatPairs && starts.Count > 0; k++) MakePair(spec, ref index, mapHash, starts, rng, holder.transform, male, female);
        Destroy(holder);
        IgnoreEachOther();
        PlanAll();
        Debug.Log("[Citizens] " + MapPath + ": " + Citizens.Count + " indoor citizens (" + starts.Count + " free start points left).");
    }

    CitizenWalker Make(int index, int mapHash, Vector3 point, int yaw, Transform holder, GameObject male, GameObject female, string[] lines, CitizenWalker.Role role)
    {
        Create(((mapHash >> 3) + index) % 2 == 0 ? female : male, holder, index, mapHash, point, yaw, null, lines, role);
        return Citizens[Citizens.Count - 1];
    }

    // Two people facing each other about a metre apart.
    void MakePair(IndoorSpec spec, ref int index, int mapHash, List<Vector3> starts, System.Random rng, Transform holder, GameObject male, GameObject female)
    {
        Vector3 a = TakeStart(starts, rng);
        float x = (float)rng.NextDouble() * 2f - 1f, z = (float)rng.NextDouble() * 2f - 1f; // a direction without trigonometry
        if (x * x + z * z < .01f) x = 1f;
        Vector3 b = a + new Vector3(x, 0f, z) * (1.1f / Mathf.Sqrt(x * x + z * z));
        if (!NavMesh.SamplePosition(b, out var hit, 1f, NavMesh.AllAreas)) return;
        b = hit.position;
        Make(index++, mapHash, a, Mathf.RoundToInt(Quaternion.LookRotation(b - a).eulerAngles.y), holder, male, female, spec.lines, CitizenWalker.Role.Stand);
        Make(index++, mapHash, b, Mathf.RoundToInt(Quaternion.LookRotation(a - b).eulerAngles.y), holder, male, female, spec.lines, CitizenWalker.Role.Stand);
    }

    static Vector3 TakeStart(List<Vector3> starts, System.Random rng)
    {
        Vector3 p = starts[rng.Next(starts.Count)];
        starts.RemoveAll(o => (o - p).sqrMagnitude < 4f); // 2 m apart
        return p;
    }

    // Same order on every machine: seats sorted by name and position, then a seeded pick.
    public static List<Seat> PickSeats(IndoorSpec spec, System.Random rng)
    {
        var filter = new System.Text.RegularExpressions.Regex(spec.seatFilter);
        var all = new List<Seat>();
        foreach (var seat in FindObjectsByType<Seat>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            bool free = !seat.assignedOccupant && seat.owner && seat.owner.HasFunction(FurnitureFunction.Seating);
            if (free && seat.transform.position.y < spec.seatMaxHeight && !seat.owner.name.Contains("toilet") && filter.IsMatch(seat.name)) all.Add(seat);
        }
        all.Sort((x, y) => { int c = string.CompareOrdinal(x.name, y.name); return c != 0 ? c : x.transform.position.x.CompareTo(y.transform.position.x); });
        var picked = new List<Seat>();
        while (picked.Count < spec.sitters && all.Count > 0)
        {
            var seat = all[rng.Next(all.Count)];
            all.RemoveAll(o => (o.transform.position - seat.transform.position).sqrMagnitude < 1.5f * 1.5f); // not shoulder to shoulder
            picked.Add(seat);
        }
        return picked;
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    // In position order: the same list on every PC.
    static Seat[] FindBenches()
    {
        var found = new List<Seat>();
        foreach (var seat in FindObjectsByType<Seat>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (seat.owner) found.Add(seat);
        found.Sort((a, b) =>
        {
            Vector3 p = a.transform.position, q = b.transform.position;
            int c = p.x.CompareTo(q.x);
            return c != 0 ? c : p.z != q.z ? p.z.CompareTo(q.z) : string.CompareOrdinal(a.name, b.name);
        });
        return found.ToArray();
    }

    Vector3? FindAnchor()
    {
        if (!MapSpawnPoint.TryFind(gameObject.scene, SceneLoadManager.DefaultSpawnId, out var spawn, out _)) return null;
        return NavMesh.SamplePosition(spawn.transform.position, out var hit, 10f, NavMesh.AllAreas) ? hit.position : (Vector3?)null;
    }

    // Start point from the baked list (already limited to real walkable ground), away from the others. Not from where the
    // player is: that differs between PCs (12.14).
    bool TryStart(System.Random rng, List<Vector3> starts, out Vector3 point)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            point = starts[rng.Next(starts.Count)];
            Vector3 p = point;
            if (Citizens.Exists(o => (o.transform.position - p).sqrMagnitude < MinCitizenDistance * MinCitizenDistance)) continue;
            return true;
        }
        point = default;
        return false;
    }

    // Random NavMesh point away from the other citizens and walkable from the spawn point (rooftop islands and other
    // disconnected patches are rejected).
    bool TryPoint(System.Random rng, NavMeshTriangulation mesh, Vector3? anchor, out Vector3 point)
    {
        var path = new NavMeshPath();
        for (int attempt = 0; attempt < 40; attempt++)
        {
            int t = rng.Next(mesh.indices.Length / 3) * 3;
            Vector3 a = mesh.vertices[mesh.indices[t]], b = mesh.vertices[mesh.indices[t + 1]], c = mesh.vertices[mesh.indices[t + 2]];
            if (Vector3.Cross(b - a, c - a).magnitude < 2f) continue; // twice the area: favors larger triangles
            float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            if (!NavMesh.SamplePosition(a + (b - a) * u + (c - a) * v, out var hit, 1.5f, NavMesh.AllAreas)) continue;
            Vector3 p = point = hit.position;
            if (Citizens.Exists(o => (o.transform.position - p).sqrMagnitude < MinCitizenDistance * MinCitizenDistance)) continue;
            if (anchor.HasValue && !(NavMesh.CalculatePath(anchor.Value, point, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)) continue;
            return true;
        }
        point = default;
        return false;
    }

    // No new layers this round, so citizens pass through each other by pairwise ignore (they do not avoid each other, 12.14).
    void IgnoreEachOther()
    {
        for (int a = 0; a < Citizens.Count; a++)
        for (int b = a + 1; b < Citizens.Count; b++)
        {
            var first = Citizens[a].GetComponent<Collider>();
            var second = Citizens[b].GetComponent<Collider>();
            if (first && second) Physics.IgnoreCollision(first, second);
        }
    }
}
