using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Client side of the shared economy on a sync-layer server (Docs/Multiplayer_Redesign.md 12.9). Registers the Net/World
/// template (eight WorldSections). On another process's server this PC turns Client (GameSession.RemoteServer while it
/// lasts): the clock and sections from the server are applied to the managers, and leaving puts this PC's own world back.
/// The owner of an in-app server keeps Host (its managers are the server's) and only reads the names.
/// Requests: the gate (SharedWriteGate.ServerSender) sends Op events to the server; OpResult shows the same words as the
/// old room and refunds a lost land purchase. veh.* never goes to a server. A clock that passed a skipped night applies
/// the night activity once per night. While connected, this PC's world.json is neither read nor written.
/// </summary>
public sealed class WorldStateSync : MonoBehaviour
{
    static WorldStateSync instance;
    static GameObject template;
    static readonly Dictionary<string, string> names = new Dictionary<string, string>();
    static readonly Queue<string[]> pending = new Queue<string[]>(); // (kind, json) sent, waiting for OpResult in order
    static readonly byte[][] held = new byte[ServerWorld.SectionCount][];
    static NetEntity world;
    static bool remote;
    static long lastClock = -1;

    /// <summary>The last request answer shown (Korean, as in the chat), for QA.</summary>
    public static string LastMessage { get; private set; }
    /// <summary>OpResults received, and nights whose activity a received clock applied, for QA.</summary>
    public static int Results { get; private set; }
    public static int NightsApplied { get; private set; }
    /// <summary>QA only: received sections wait (the last of each) until this is false again.</summary>
    public static bool HoldSections { get; set; }

    /// <summary>The world entity on this client (null when not connected).</summary>
    public static NetEntity World => world;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        names.Clear(); pending.Clear(); Array.Clear(held, 0, held.Length);
        world = null; remote = false; lastClock = -1; HoldSections = false; LastMessage = null; Results = NightsApplied = 0;
        var fallback = SyncClient.PrefabFactory; // reset at SubsystemRegistration, before this
        SyncClient.PrefabFactory = key => key == ServerWorld.PrefabKey ? Template() : fallback?.Invoke(key);
        SharedWriteGate.ServerCheck = () => SyncClient.Instance && SyncClient.Instance.Connected && SyncClient.Instance.HasCharacter;
        SharedWriteGate.ServerSender = SendOp;
        SharedWriteGate.ServerNameResolver = NameOf;
        WorldSaveRunner.WorldOnServer = () => SyncHost.Instance || SyncClient.Instance && SyncClient.Instance.Connected;
        NightActivity.ChangeVitals = (energy, stress) =>
        {
            var stats = PlayerSpawner.Player ? PlayerSpawner.Player.GetComponent<PlayerStats>() : null;
            if (stats) stats.Add(0f, energy, stress);
        };
        SyncClient.ConnectionChanged -= OnConnection;
        SyncClient.ConnectionChanged += OnConnection;
        if (instance) return;
        var go = new GameObject("WorldStateSync");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<WorldStateSync>();
    }

    // Inactive: SyncClient activates each copy after giving it its (empty) state.
    static GameObject Template()
    {
        if (template) return template;
        template = new GameObject(ServerWorld.PrefabKey);
        template.SetActive(false);
        DontDestroyOnLoad(template);
        template.AddComponent<NetEntity>();
        for (int i = 0; i < ServerWorld.SectionCount; i++) template.AddComponent<WorldSection>().index = i;
        return template;
    }

    static void OnConnection(bool connected)
    {
        names.Clear(); pending.Clear(); Array.Clear(held, 0, held.Length);
        lastClock = -1;
        if (connected)
        {
            remote = !SyncHost.Instance; // my own in-app server computes on these managers: they stay Host
            if (remote) { GameSession.Begin(SessionMode.Client); GameSession.RemoteServer = true; }
            return;
        }
        world = null;
        if (!remote) return;
        remote = false;
        GameSession.Begin(SessionMode.Host);
        GameSession.RemoteServer = false;
        GameTime.ResetNightSkip();
        if (LandPlotManager.Instance) LandPlotManager.Instance.ClearPending();
        // The server's world was only shown here: this PC's own world comes back (memory only, nothing written).
        if (!WorldSave.LoadFromDisk()) WorldSave.RestoreBaseline();
    }

    void Update()
    {
        if (HoldSections) return;
        for (int i = 0; i < held.Length; i++)
            if (held[i] != null) { byte[] blob = held[i]; held[i] = null; Apply(i, blob); }
    }

    // ---- sections ----

    internal static void OnSection(WorldSection section)
    {
        world = section.Entity;
        if (section.index == ServerWorld.NamesSection)
        {
            try { var read = ServerWorld.ReadNames(section.Blob); names.Clear(); foreach (var pair in read) names[pair.Key] = pair.Value; }
            catch (Exception e) { Debug.LogWarning("[WorldStateSync] unreadable names: " + e.Message); }
            return;
        }
        if (GameSession.IsAuthority) return; // an in-app server's owner: the managers are the server's already
        if (HoldSections) { held[section.index] = section.Blob; return; }
        Apply(section.index, section.Blob);
    }

    static void Apply(int index, byte[] blob)
    {
        if (GameSession.IsAuthority) return;
        try
        {
            if (index == ServerWorld.ClockSection) ApplyClock(blob);
            else if (index >= ServerWorld.FirstShared) WorldSave.ApplySection(index - ServerWorld.FirstShared, ServerWorld.DecodeJson(blob));
        }
        catch (Exception e) { Debug.LogWarning("[WorldStateSync] section " + index + " unreadable: " + e.Message); }
    }

    // The screen clock follows the world clock. A received time past the end of a skipped night counts as that night.
    static void ApplyClock(byte[] blob)
    {
        ServerWorld.ReadClock(blob, out uint minutes, out int skipStart, out int skipEnd);
        GameTime.SetNightSkip(skipStart, skipEnd);
        long now = minutes, before = lastClock;
        lastClock = now;
        if (GameClock.Current != null) GameClock.Current.SetNow(GameTime.FromTotalMinutes(now));
        if (before < 0 || now <= before) return;
        const long Day = GameTime.HoursPerDay * GameTime.MinutesPerHour;
        if (skipStart < skipEnd)
            for (long day = before / Day; day <= now / Day; day++)
            {
                long end = day * Day + skipEnd * GameTime.MinutesPerHour; // where the clock lands after that night
                if (end > before && end <= now) { NightsApplied++; NightActivity.Apply(NightActivity.Selected); }
            }
        if (now / Day != before / Day) LibraryCatalog.AdvanceTo((int)(now / Day)); // this PC keeps its library (12.9)
    }

    // ---- requests ----

    static bool SendOp(string kind, string json)
    {
        if (kind.StartsWith("veh.", StringComparison.Ordinal)) return false; // a server's cars are entities (12.8)
        var section = world ? world.GetComponent<WorldSection>() : null;
        if (!section || !SyncClient.Instance || !SyncClient.Instance.Connected)
        {
            NetWorldBridge.Say("서버에 연결되어 있지 않아 요청을 보내지 못했습니다. 연결을 확인해 주세요.");
            return false;
        }
        if (System.Text.Encoding.UTF8.GetByteCount(kind) + System.Text.Encoding.UTF8.GetByteCount(json) + 4 > SyncProtocol.MaxBlob)
        {
            NetWorldBridge.Say("요청이 너무 길어 보내지 못했습니다.");
            return false;
        }
        section.SendEvent(WorldSection.OpEvent, EventTarget.Server, w => { SyncProtocol.WriteStr(w, kind); SyncProtocol.WriteStr(w, json); });
        pending.Enqueue(new[] { kind, json });
        if (kind.StartsWith("land.", StringComparison.Ordinal) || kind == "company.add") NetWorldBridge.Say("처리 중…");
        return true;
    }

    internal static void OnOpResult(BinaryReader r)
    {
        string kind = SyncProtocol.ReadStr(r), error = SyncProtocol.ReadStr(r);
        string json = null;
        if (pending.Count > 0 && pending.Peek()[0] == kind) json = pending.Dequeue()[1];
        Results++;
        string text;
        if (string.IsNullOrEmpty(error))
        {
            if (kind == "land.buy" && json != null && LandPlotManager.Instance) LandPlotManager.Instance.ConfirmPending(JsonUtility.FromJson<SharedOpArgs>(json)?.a);
            text = NetWorldBridge.DoneText(kind);
        }
        else
        {
            bool refunded = kind == "land.buy" && json != null && LandPlotManager.Instance && LandPlotManager.Instance.RefundPending(JsonUtility.FromJson<SharedOpArgs>(json)?.a);
            text = refunded ? error + " 결제 금액은 돌려드렸습니다." : error;
        }
        if (text == null) return;
        LastMessage = text;
        NetWorldBridge.Say(text);
    }

    /// <summary>The character name of <paramref name="account"/> in this server's world (null when unknown).</summary>
    public static string NameOf(string account)
    {
        if (account == GameSession.LocalPlayerId) return GameSession.LocalPlayerName;
        return names.TryGetValue(account ?? "", out var name) ? RemoteAvatarView.CleanName(name) : null;
    }
}
