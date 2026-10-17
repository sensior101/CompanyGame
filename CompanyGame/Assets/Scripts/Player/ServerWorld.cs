using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>
/// The game side of a sync-layer server (Docs/Multiplayer_Redesign.md 12.9): the shared economy is computed here, by the
/// same managers the game uses, for the server process and for an in-app server (whose owner plays on these very
/// managers). Runs while SyncHost runs in this process:
///   start  the fresh world is captured (WorldSave.CaptureBaseline), then the world.dat state is restored (a world without
///          one starts fresh), and the game clock is paused and set to the world clock
///   frame  the game clock follows the server's world clock minute by minute, so HourChanged/DayChanged drive prices
///          and companies; the clock section goes out on every new world minute
///   1 s    each section that changed goes out as a State of the world entity Net/World (map "*", every connection)
///   Op     a client's request (event 1) is applied with WorldOps.Apply as the account of its connection; the changed
///          sections go out first, then OpResult (event 2) to the requester. veh.* and unknown kinds are refused.
///          Personal requests (p.*, 12.10.1) and the payment of land.buy go to the <see cref="Ledger"/>, which answers
///          the requester with a Ledger (event 3) before the OpResult. Book requests (book.*, 12.10.3) go to the
///          <see cref="Books"/> (book.get answers with BookText, event 4); they change no section, so none is sent for them
///   stop   an in-app server puts this PC's own world back (world.json, else the fresh world)
/// The full state (with the book bodies, WorldSaveData.books) is saved in world.dat through GameServer.WorldStateProvider; clients get only the newest
/// <see cref="Recent"/> reports and SNS posts. A section over 60KB gzipped is not sent (logged).
/// </summary>
public sealed class ServerWorld : MonoBehaviour
{
    public const string PrefabKey = "Net/World";
    public const int SectionCount = 8, ClockSection = 0, NamesSection = 1, FirstShared = 2, Recent = 100;
    const float SendInterval = 1f;
    const int MaxStepsPerFrame = 2000;

    /// <summary>The kinds a client may ask for (12.9). Everything else, veh.* included, is refused.</summary>
    static readonly HashSet<string> Allowed = new HashSet<string>
        { "prop.reg", "prop.ab", "prop.lock", "prop.dis", "land.buy", "land.reg", "company.add", "sns.post", "sns.like", "report.sub" };

    public static ServerWorld Instance { get; private set; }

    /// <summary>The personal ledger of this server's characters (12.10.1).</summary>
    public ServerLedger Ledger { get; private set; }
    /// <summary>The book bodies of this server's world (12.10.3).</summary>
    public ServerBooks Books { get; private set; }

    SyncHost host;
    GameServer server;
    readonly string[] lastJson = new string[SectionCount];
    readonly byte[][] lastBlob = new byte[SectionCount][];
    long lastClockMinute = -1;
    float nextSendAt;

    /// <summary>The world entity's id.</summary>
    public ulong EntityId { get; private set; }
    /// <summary>Gzipped size of each section as last sent, for QA.</summary>
    public readonly int[] SectionBytes = new int[SectionCount];
    /// <summary>Kinds refused because they are not allowed (veh.*, unknown), for QA.</summary>
    public readonly List<string> Refused = new List<string>();
    /// <summary>Requests applied (allowed kinds, with or without an error), for QA.</summary>
    public int Applied { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Attach()
    {
        Instance = null;
        // Before SyncHost.FromCommandLine (AfterSceneLoad) can start a server process.
        SyncHost.Started -= OnStarted; SyncHost.Started += OnStarted;
        SyncHost.Stopped -= OnStopped; SyncHost.Stopped += OnStopped;
    }

    static void OnStarted(SyncHost host)
    {
        if (Instance) Instance.End(false);
        var go = new GameObject("ServerWorld");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ServerWorld>();
        Instance.Begin(host);
    }

    static void OnStopped(SyncHost host)
    {
        if (Instance && Instance.host == host) Instance.End(host.InApp);
    }

    void Begin(SyncHost syncHost)
    {
        host = syncHost;
        server = syncHost.Server;
        var world = server.World;
        WorldSave.CaptureBaseline(); // the fresh world, before any world is put into the managers
        var saved = Decode(world.WorldState);
        if (saved == null && world.WorldState.Length > 0) Debug.LogError("[ServerWorld] the world state of " + world.Id + " is unreadable; the shared economy starts fresh");
        WorldSave.RestoreShared(saved ?? JsonUtility.FromJson<WorldSaveData>(WorldSave.Baseline));
        GameTime.SetNightSkip(world.NightSkipStart, world.NightSkipEnd);
        if (GameClock.Current is SimpleGameClock clock)
        {
            clock.Paused = true; // the world clock drives it from now on
            clock.SetNow(GameTime.FromTotalMinutes((long)Math.Floor(world.WorldMinutes)));
        }
        NightActivity.Enabled = syncHost.InApp; // a server process has no character of its own
        server.WorldStateProvider = () =>
        {
            var state = WorldSave.CaptureShared();
            state.books = Books?.Capture();
            return Encode(WorldSave.ToJson(state));
        };
        server.ServerEvent += OnServerEvent;
        EntityId = server.SpawnServerEntity(PrefabKey, SyncProtocol.AllMaps, EntityFlags.None, new byte[SectionCount][]);
        Books = new ServerBooks(server, EntityId, saved?.books);
        Ledger = new ServerLedger(server, EntityId, Books);
        SendSections();
        Debug.Log("[ServerWorld] world " + world.Id + ": " + (saved != null ? "shared economy restored" : "new shared economy") + ", clock " + GameClock.Current?.Now
                  + ", night " + world.NightSkipStart + "-" + world.NightSkipEnd);
    }

    void End(bool restorePcWorld)
    {
        if (server != null) { server.ServerEvent -= OnServerEvent; server.WorldStateProvider = null; }
        Ledger?.End();
        Ledger = null;
        Books = null;
        server = null; host = null;
        if (GameClock.Current is SimpleGameClock clock) clock.Paused = false;
        GameTime.ResetNightSkip();
        NightActivity.Enabled = true;
        // This PC's own world comes back, as an old-room visitor's does (memory only; nothing of the server's is written).
        if (restorePcWorld && !WorldSave.LoadFromDisk()) WorldSave.RestoreBaseline();
        if (Instance == this) Instance = null;
        Destroy(gameObject);
    }

    void Update()
    {
        if (server == null) return;
        var world = server.World;
        if (GameTime.NightSkipStart != world.NightSkipStart || GameTime.NightSkipEnd != world.NightSkipEnd)
            GameTime.SetNightSkip(world.NightSkipStart, world.NightSkipEnd); // SetNightSkip or -nightskip after the start
        long target = (long)Math.Floor(world.WorldMinutes);
        if (GameClock.Current is SimpleGameClock clock)
        {
            if (clock.Now.TotalMinutes > target) clock.SetNow(GameTime.FromTotalMinutes(target));
            for (int i = 0; i < MaxStepsPerFrame && clock.Now.TotalMinutes < target; i++) clock.AdvanceMinutes(1);
        }
        if (target != lastClockMinute) SendClock(target);
        if (Time.unscaledTime >= nextSendAt) { SendSections(); Books?.DropGoneParts(); }
    }

    void SendClock(long minute)
    {
        lastClockMinute = minute;
        var world = server.World;
        Set(ClockSection, ClockBlob((uint)Math.Min(minute, uint.MaxValue), world.NightSkipStart, world.NightSkipEnd));
    }

    /// <summary>Sends every section that changed since it was last sent (also right after a request).</summary>
    public void SendSections()
    {
        if (server == null) return;
        nextSendAt = Time.unscaledTime + SendInterval;
        var all = WorldSave.CaptureShared();
        for (int i = 0; i < WorldSave.SectionNames.Length; i++)
        {
            int section = FirstShared + i;
            string json = WorldSave.SectionJson(all, i, Recent);
            if (json == lastJson[section]) continue;
            lastJson[section] = json; // also when too big: logged once per change, not every second
            Set(section, Encode(json));
        }
        Set(NamesSection, NamesBlob(server.World.Characters));
        if (lastClockMinute < 0) SendClock((long)Math.Floor(server.World.WorldMinutes));
    }

    void Set(int section, byte[] blob)
    {
        if (lastBlob[section] != null && ((ReadOnlySpan<byte>)lastBlob[section]).SequenceEqual(blob)) return;
        SectionBytes[section] = blob.Length;
        if (blob.Length > SyncProtocol.MaxServerBlob)
        {
            Debug.LogError("[ServerWorld] section " + section + " is " + blob.Length + " bytes gzipped, over " + SyncProtocol.MaxServerBlob + "; not sent");
            return;
        }
        if (server.SetServerState(EntityId, (byte)section, blob)) lastBlob[section] = blob;
    }

    void OnServerEvent(uint sender, ulong entity, byte component, byte eventId, byte[] payload)
    {
        if (entity != EntityId || eventId != WorldSection.OpEvent) return;
        string kind, json;
        try
        {
            using var r = SyncProtocol.Reader(payload);
            kind = SyncProtocol.ReadStr(r); json = SyncProtocol.ReadStr(r);
            SyncProtocol.End(r);
        }
        catch (Exception) { Debug.LogWarning("[ServerWorld] unreadable request from " + sender); return; }
        string from = server.AccountOf(sender);
        if (string.IsNullOrEmpty(from) || server.World.Find(from) == null) return; // no character, no say in the world
        string error;
        bool personal = ServerLedger.Kinds.Contains(kind), book = ServerBooks.Kinds.Contains(kind);
        if (!personal && !book && !Allowed.Contains(kind))
        {
            Refused.Add(kind);
            Debug.LogWarning("[ServerWorld] refused request '" + kind + "' from " + from);
            error = "처리할 수 없는 요청입니다.";
        }
        else
        {
            Applied++;
            try
            {
                error = personal ? Ledger.Apply(sender, from, kind, json)
                    : book ? Books.Apply(sender, from, kind, json)
                    : kind == "land.buy" ? Ledger.BuyLand(from, json)
                    : WorldOps.Apply(0, from, kind, kind == "sns.post" ? WithAuthorName(json, from) : json);
            }
            catch (Exception e) { Debug.LogWarning("[ServerWorld] request '" + kind + "' failed: " + e.Message); error = "요청을 처리하지 못했습니다."; }
        }
        if (!book) SendSections(); // the change first, then the answer (one TCP stream keeps the order)
        if (ServerLedger.Counts(kind)) Ledger.Answered(sender, from); // the requester's own book, before its answer
        server.SendServerEvent(EntityId, 0, WorldSection.OpResultEvent, sender, Bytes(w =>
        {
            SyncProtocol.WriteStr(w, kind.Length > 64 ? kind.Substring(0, 64) : kind);
            SyncProtocol.WriteStr(w, error ?? "");
        }));
    }

    // A post shows the author's character name of this world, never what the client wrote.
    string WithAuthorName(string json, string from)
    {
        var a = JsonUtility.FromJson<SharedOpArgs>(json);
        if (a == null) return json;
        a.c = server.World.Find(from)?.name ?? "";
        return JsonUtility.ToJson(a);
    }

    // ---- section codecs (shared with WorldStateSync) ----

    internal static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8);
        write(w);
        w.Flush();
        return stream.ToArray();
    }

    internal static byte[] ClockBlob(uint minutes, int skipStart, int skipEnd) =>
        Bytes(w => { w.Write(minutes); w.Write((byte)skipStart); w.Write((byte)skipEnd); });

    internal static void ReadClock(byte[] blob, out uint minutes, out int skipStart, out int skipEnd)
    {
        using var r = SyncProtocol.Reader(blob);
        minutes = r.ReadUInt32(); skipStart = r.ReadByte(); skipEnd = r.ReadByte();
    }

    // gzip(u16 count, then account and name of each character).
    internal static byte[] NamesBlob(IEnumerable<WorldStore.Character> characters)
    {
        var list = new List<WorldStore.Character>(characters);
        return Gzip(Bytes(w =>
        {
            w.Write((ushort)Math.Min(list.Count, ushort.MaxValue));
            for (int i = 0; i < list.Count && i < ushort.MaxValue; i++) { SyncProtocol.WriteStr(w, list[i].account); SyncProtocol.WriteStr(w, list[i].name); }
        }));
    }

    internal static Dictionary<string, string> ReadNames(byte[] blob)
    {
        var names = new Dictionary<string, string>();
        using var r = SyncProtocol.Reader(Gunzip(blob));
        int count = r.ReadUInt16();
        for (int i = 0; i < count; i++) names[SyncProtocol.ReadStr(r)] = SyncProtocol.ReadStr(r);
        return names;
    }

    internal static byte[] Encode(string json) => Gzip(Encoding.UTF8.GetBytes(json));
    internal static string DecodeJson(byte[] blob) => Encoding.UTF8.GetString(Gunzip(blob));

    static WorldSaveData Decode(byte[] state)
    {
        if (state == null || state.Length == 0) return null;
        try { return JsonUtility.FromJson<WorldSaveData>(DecodeJson(state)); }
        catch (Exception e) { Debug.LogWarning("[ServerWorld] world state unreadable: " + e.Message); return null; }
    }

    static byte[] Gzip(byte[] raw)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Optimal, true)) gzip.Write(raw, 0, raw.Length);
        return stream.ToArray();
    }

    static byte[] Gunzip(byte[] blob)
    {
        using var gzip = new GZipStream(new MemoryStream(blob, false), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
