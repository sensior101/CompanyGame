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
/// Personal requests (12.10.1): a money change this client already made (stock trade, shop, fine, land payment, the
/// trusted p.bank) goes out with SharedWriteGate.SendPersonal and is counted. The server answers with its Ledger; once
/// every counted request is answered (and after the OpResult that comes with it, so a lost land purchase refunds
/// first), a ledger that differs is written over the screen managers without any money message.
/// </summary>
public sealed class WorldStateSync : MonoBehaviour
{
    static WorldStateSync instance;
    static GameObject template;
    static readonly Dictionary<string, string> names = new Dictionary<string, string>();
    static readonly Queue<string[]> pending = new Queue<string[]>(); // (kind, json) sent, waiting for OpResult in order
    static readonly byte[][] heldSections = new byte[ServerWorld.SectionCount][];
    static NetEntity world;
    static bool remote;
    static long lastClock = -1;
    static uint sentPersonal;
    static bool toldNight;
    static LedgerState held;

    sealed class LedgerState { public uint done; public long bank; public string portfolio; }

    /// <summary>The last request answer shown (Korean, as in the chat), for QA.</summary>
    public static string LastMessage { get; private set; }
    /// <summary>OpResults received, and nights whose activity a received clock applied, for QA.</summary>
    public static int Results { get; private set; }
    public static int NightsApplied { get; private set; }
    /// <summary>Ledgers received, and the times one was written over the screen managers (they differed), for QA.</summary>
    public static int Ledgers { get; private set; }
    public static int Overwrites { get; private set; }
    /// <summary>QA only: received sections wait (the last of each) until this is false again.</summary>
    public static bool HoldSections { get; set; }

    /// <summary>The world entity on this client (null when not connected).</summary>
    public static NetEntity World => world;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        names.Clear(); pending.Clear(); Array.Clear(heldSections, 0, heldSections.Length);
        world = null; remote = false; lastClock = -1; HoldSections = false; LastMessage = null; Results = NightsApplied = Ledgers = Overwrites = 0;
        sentPersonal = 0; toldNight = false; held = null;
        var fallback = SyncClient.PrefabFactory; // reset at SubsystemRegistration, before this
        SyncClient.PrefabFactory = key => key == ServerWorld.PrefabKey ? Template() : fallback?.Invoke(key);
        SharedWriteGate.ServerCheck = () => SyncClient.Instance && SyncClient.Instance.Connected && SyncClient.Instance.HasCharacter;
        SharedWriteGate.ServerSender = SendOp;
        SharedWriteGate.PersonalSender = (kind, json) => SendOp(kind, json);
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
        names.Clear(); pending.Clear(); Array.Clear(heldSections, 0, heldSections.Length);
        lastClock = -1;
        sentPersonal = 0; toldNight = false; held = null; // the server counts per connection
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
        if (!toldNight && world && SharedWriteGate.OnServer)
        {
            toldNight = true; // a new connection starts at 잠자기 on the server: tell it a pick made before
            if (NightActivity.Selected != NightActivityKind.Sleep) SharedWriteGate.SendPersonal("p.night.select", new SharedOpArgs { n = (long)NightActivity.Selected });
        }
        if (HoldSections) return;
        for (int i = 0; i < heldSections.Length; i++)
            if (heldSections[i] != null) { byte[] blob = heldSections[i]; heldSections[i] = null; Apply(i, blob); }
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
        if (HoldSections) { heldSections[section.index] = section.Blob; return; }
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
        if (ServerLedger.Counts(kind)) sentPersonal++;
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
        if (text != null)
        {
            LastMessage = text;
            NetWorldBridge.Say(text);
        }
        ApplyLedger(); // after a refund, so the ledger finds the same money
    }

    // ---- the server's ledger (12.10.1) ----

    internal static void OnLedger(BinaryReader r)
    {
        var ledger = new LedgerState { done = r.ReadUInt32(), bank = r.ReadInt64() };
        ledger.portfolio = ServerWorld.DecodeJson(SyncProtocol.ReadServerBlob(r));
        Ledgers++;
        held = ledger; // the newest wins
        if (pending.Count == 0) ApplyLedger(); // nothing to answer first (a night)
    }

    // Only while no counted request waits for its answer: then the ledger already has everything this client did.
    static void ApplyLedger()
    {
        if (held == null || held.done != sentPersonal) return;
        var ledger = held;
        held = null;
        var bank = BankManager.EnsureInstance();
        var market = StockMarketManager.Instance;
        bool money = bank && bank.Money != ledger.bank;
        bool stocks = market && JsonUtility.ToJson(market.Exchange.CapturePortfolio()) != ledger.portfolio;
        if (!money && !stocks) return;
        Overwrites++;
        Debug.Log("[WorldStateSync] the server's ledger differs (bank " + (bank ? bank.Money : 0) + " → " + ledger.bank + (stocks ? ", stocks" : "") + "): written over");
        if (money) bank.RestoreBalance(ledger.bank); // ManualAdjustment: no money message in the chat
        if (stocks) market.Exchange.RestorePortfolio(JsonUtility.FromJson<StockPortfolio>(ledger.portfolio));
    }

    /// <summary>The character name of <paramref name="account"/> in this server's world (null when unknown).</summary>
    public static string NameOf(string account)
    {
        if (account == GameSession.LocalPlayerId) return GameSession.LocalPlayerName;
        return names.TryGetValue(account ?? "", out var name) ? RemoteAvatarView.CleanName(name) : null;
    }
}
