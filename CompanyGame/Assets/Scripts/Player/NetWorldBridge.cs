using UnityEngine;

/// <summary>
/// Connects NetSession to the world/personal save: host snapshots, participant restore, the 5Hz clock,
/// host handover (new host saves, then acks; old host deletes its world files only after the ack)
/// and quit handling for hosts. Lives in Player because it sees both Net and Gameplay. Created automatically.
/// </summary>
public sealed class NetWorldBridge : MonoBehaviour
{
    const float ClockInterval = 0.2f, AckTimeout = 30f, AckRepeat = 10f, QueryInterval = 10f;
    const int AckRepeats = 6;

    NetSession session;
    float clockTimer;
    float ackDeadline = -1f;   // old host: time to show the failure notice (the lock stays regardless)
    bool waitingAck, tookOver, quitAfterAck, quitApproved;
    float repeatTimer;         // new host: re-sends the ack, because the old host cannot ask for it
    int repeatsLeft;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<NetWorldBridge>()) return;
        var go = new GameObject("Net World Bridge");
        DontDestroyOnLoad(go);
        go.AddComponent<NetWorldBridge>();
    }

    void OnEnable() { Application.wantsToQuit += OnWantsToQuit; }
    void OnDisable() { Application.wantsToQuit -= OnWantsToQuit; }

    void Update()
    {
        if (!session) Bind(NetSession.Instance);
        if (!session) return;
        CheckAckTimeout();
        QueryHandover();
        RepeatAck();
        if (!session.InRoom || !session.IsHost) return;
        if ((clockTimer += Time.unscaledDeltaTime) < ClockInterval) return;
        clockTimer = 0f;
        if (GameClock.Current != null)
            session.SendWorld("clock", "{\"min\":" + GameClock.Current.Now.TotalMinutes + "}");
    }

    void Bind(NetSession s)
    {
        if (!s) return;
        session = s;
        s.SnapshotProvider = () => WorldSave.ToJson(WorldSave.Capture());
        s.SnapshotReceived += OnSnapshot;
        s.WorldReceived += OnWorld;
        s.HostChanged += OnHostChanged;
        s.MessageReceived += OnMessage;
        s.OpReceived += OnOp;
        s.Received += OnNetMessage;
        s.RoomReset += OnRoomReset;
        GameSession.AccountChanged += OnAccountChanged;
        SharedWriteGate.InRoomCheck = () => session && session.InRoom;
        SharedWriteGate.NameResolver = NameOf;
        SharedWriteGate.Sender = SendOp;
        SceneLoadManager.LoadStarted += OnLoadStarted;
    }

    void OnDestroy()
    {
        if (!session) return;
        session.SnapshotReceived -= OnSnapshot;
        session.WorldReceived -= OnWorld;
        session.HostChanged -= OnHostChanged;
        session.MessageReceived -= OnMessage;
        session.OpReceived -= OnOp;
        session.Received -= OnNetMessage;
        session.RoomReset -= OnRoomReset;
        GameSession.AccountChanged -= OnAccountChanged;
        SharedWriteGate.NameResolver = null;
        SharedWriteGate.Sender = null;
        SharedWriteGate.InRoomCheck = null;
        session.SnapshotProvider = null;
        SceneLoadManager.LoadStarted -= OnLoadStarted;
    }

    // False when the op could not leave this PC; the player is told and the caller must refund / not apply.
    bool SendOp(string kind, string json)
    {
        bool sent = session && session.InRoom && session.SendOp(kind, json);
        if (!sent) Say("서버에 연결되어 있지 않아 요청을 보내지 못했습니다. 연결을 확인해 주세요.");
        else if (kind.StartsWith("land.") || kind == "company.add") Say("처리 중…");
        return sent;
    }

    string NameOf(string id)
    {
        if (!session) return null;
        if (id == session.MyId) return GameSession.LocalPlayerName;
        string character = NetPlayerSync.NameOf(id);
        if (!string.IsNullOrEmpty(character)) return character;
        foreach (var p in session.Peers) if (p.id == id) return string.IsNullOrEmpty(p.name) ? null : p.name;
        return null;
    }

    // A room is joined/created or left: server op numbers restart at 1, so the applied-op counter and
    // anything waiting on the old room's ops must restart too. A host/single world is saved first, because
    // a visitor never saves and restores its own world from the file when it leaves.
    void OnRoomReset()
    {
        if (session.InRoom && GameSession.IsAuthority && !WorldSave.HandedOver) WorldSave.SaveToDisk();
        WorldSave.OpSeq = 0;
        wasHost = false; // the host role is lost "in a room" only; joining as a visitor must not lock the own world
        if (LandPlotManager.Instance) LandPlotManager.Instance.ClearPending();
    }

    // First login on this PC: records made under the old per-PC id (GUID / "Player") become the account's.
    void OnAccountChanged(string oldId, string newId)
    {
        if (oldId == newId) return;
        if (oldId != GameSession.OfflinePlayerId) { PersonalSave.SwitchAccount(oldId, newId); return; } // another account: starts fresh
        WorldSave.MigrateOwners();
        PersonalSave.MigrateIdentity(oldId, newId);
    }

    // Memory only: WorldSave.Restore never writes disk and never touches personal values.
    // The host's own state is already authoritative, so it ignores echoes of snapshots.
    void OnSnapshot(string json)
    {
        if (GameSession.IsAuthority && session.IsHost) return;
        // A snapshot older than the ops already applied is ignored (it would undo them); newer ones skip the ops they contain.
        if (WorldSave.RestoreIfNotOlder(json, session.LastOpSeq)) tookOver = true;
        session.LastOpSeq = WorldSave.OpSeq; // NetSession resets it on joined
    }

    // Shared-world op echo (sender included): applied in server order on every peer.
    void OnOp(long seq, string fromId, string kind, string json)
    {
        bool mine = fromId == session.MyId;
        string error = WorldOps.ApplyFor(seq, fromId, mine, kind, json);
        session.LastOpSeq = WorldSave.OpSeq;
        if (!mine) return;
        string text = error ?? DoneText(kind);
        if (text != null) Say(text);
    }

    static string DoneText(string kind)
    {
        switch (kind)
        {
            case "prop.reg": return "소유권을 등록했습니다.";
            case "prop.ab": return "소유권을 포기했습니다.";
            case "land.buy": return "땅을 구매했습니다.";
            case "land.reg": return "사업자 등록을 완료했습니다.";
            case "company.add": return "회사를 설립했습니다.";
            case "sns.post": return "게시했습니다.";
            case "report.sub": return "신고를 접수했습니다.";
            default: return null;
        }
    }

    // A transfer the server refused (peer_not_found / not_host / ...): stay host, undo any lock, say so.
    void OnNetMessage(NetMsg m)
    {
        if (m == null || m.t != "err" || !session.IsHost || !(quitAfterAck || WorldSave.HandedOver)) return;
        if (m.code != "peer_not_found" && m.code != "not_host" && m.code != "not_in_room") return;
        if (WorldSave.HandedOver) WorldSave.UnlockHandover();
        waitingAck = false; ackDeadline = -1f; quitAfterAck = false;
        Say("방장 넘기기에 실패했습니다. 계속 방장으로 남습니다.");
    }

    void OnWorld(string kind, string json)
    {
        if (kind != "clock" || GameClock.Current == null) return;
        var msg = JsonUtility.FromJson<ClockMsg>(json);
        if (msg != null) GameClock.Current.SetNow(GameTime.FromTotalMinutes(msg.min));
    }

    // HostChanged fires after GameSession.Begin and after SnapshotReceived, so a new host has the old
    // host's state restored by now. It saves first, then acks (broadcast; only the old host acts on it).
    void OnHostChanged(bool iAmHost)
    {
        if (iAmHost) BecameHost();
        else if (wasHost) LoseHost();
        wasHost = iAmHost;
    }

    void BecameHost()
    {
        // Leaving the room / losing the connection never undoes a handover lock: only the server giving the room
        // back (a `host` message) or a refused transfer does. A visitor that leaves gets its own world back.
        if (session.HostFromLeave) { RestoreOwnWorld(); return; }
        if (WorldSave.HandedOver)
        {
            // Rollback: the server gave the room back before the new host confirmed, so our locked world is valid again.
            WorldSave.UnlockHandover();
            waitingAck = false; ackDeadline = -1f; quitAfterAck = false;
            WorldSave.SaveToDisk();
            return;
        }
        bool handover = tookOver; // an ack is only for a real takeover, not for creating a room
        if (tookOver) { WorldSave.BumpGeneration(); tookOver = false; }
        if (!WorldSave.SaveToDisk()) { Say("월드 저장에 실패했습니다. 디스크 공간과 권한을 확인해 주세요."); return; }
        if (!handover) return;
        SendAck();
        repeatsLeft = AckRepeats; repeatTimer = AckRepeat;
    }

    // The room's world was only mirrored here: drop it (nothing of it is written) and reload this PC's own world.
    void RestoreOwnWorld()
    {
        tookOver = false;
        if (!WorldSave.LoadFromDisk()) WorldSave.RestoreBaseline();
        session.LastOpSeq = 0;
    }

    // The world now belongs to the new host: lock our copy so it can never be loaded or hosted again.
    void LoseHost()
    {
        WorldSave.LockForHandover();
        waitingAck = true;
        ackDeadline = Time.unscaledTime + AckTimeout;
    }

    void SendAck() => session.SendMessageTo("", JsonUtility.ToJson(
        new Handover { handover = "ok", opSeq = WorldSave.OpSeq, worldId = WorldSave.WorldId, generation = WorldSave.Generation }));

    void RepeatAck()
    {
        if (repeatsLeft <= 0 || !session.IsHost) return;
        if ((repeatTimer -= Time.unscaledDeltaTime) > 0f) return;
        repeatTimer = AckRepeat; repeatsLeft--;
        SendAck();
    }

    // A locked (handed-over) world asks the current host every 10s whether it has the world saved.
    void QueryHandover()
    {
        if (!WorldSave.HandedOver || !session.InRoom || session.IsHost || string.IsNullOrEmpty(session.HostId)) return;
        if ((queryTimer -= Time.unscaledDeltaTime) > 0f) return;
        queryTimer = QueryInterval;
        if (!WorldSave.TryReadLocked(out string lockedId, out int lockedGen)) return;
        session.SendMessageTo(session.HostId, JsonUtility.ToJson(
            new Handover { handover = "query", worldId = lockedId, generation = lockedGen }));
    }

    bool wasHost;
    float queryTimer;

    void OnMessage(string fromId, string data)
    {
        var msg = JsonUtility.FromJson<Handover>(data);
        if (msg == null) return;
        if (msg.handover == "query") { AnswerQuery(fromId, msg); return; }
        if (!WorldSave.HandedOver || fromId != session.HostId || msg.handover != "ok") return;
        // Only an ack for the locked world, saved at the locked generation or newer, may delete it.
        if (!WorldSave.TryReadLocked(out string lockedId, out int lockedGen) || msg.worldId != lockedId || msg.generation < lockedGen) return;
        waitingAck = false; ackDeadline = -1f;
        WorldSave.Delete();
        Say("월드를 넘겼습니다. 이제 종료할 수 있습니다.");
        if (quitAfterAck) { quitApproved = true; Application.Quit(); }
    }

    // No ack in time: tell the user, but keep the lock and the files. A late ack still deletes them.
    // Reply ack only if this PC really holds that world, saved at the asked generation or newer.
    void AnswerQuery(string fromId, Handover q)
    {
        if (!session.IsHost || q.worldId != WorldSave.WorldId || WorldSave.Generation < q.generation) return;
        if (!System.IO.File.Exists(WorldSave.Path)) return;
        session.SendMessageTo(fromId, JsonUtility.ToJson(
            new Handover { handover = "ok", opSeq = WorldSave.OpSeq, worldId = WorldSave.WorldId, generation = WorldSave.Generation }));
    }

    void CheckAckTimeout()
    {
        if (ackDeadline < 0f || Time.unscaledTime < ackDeadline) return;
        ackDeadline = -1f;
        Say("월드 넘기기를 확인하지 못했습니다. 이 PC의 월드는 잠긴 상태로 보관되며 새 방장이 확인하면 자동으로 정리됩니다.");
        if (quitAfterAck) { quitApproved = true; Application.Quit(); }
    }

    bool OnWantsToQuit()
    {
        if (quitApproved || !session || !session.InRoom || !session.IsHost) { WorldSaveRunner.SaveNow(); return true; }
        string target = EarliestOtherPeer();
        if (target == null) { WorldSaveRunner.SaveNow(); return true; } // alone: save locally and quit
        if (!quitAfterAck)
        {
            quitAfterAck = true;
            Say("방장 상태로 종료하려면 월드를 먼저 넘겨야 합니다. 잠시만 기다려 주세요.");
            session.TransferHost(target);
        }
        return false;
    }

    string EarliestOtherPeer()
    {
        foreach (var p in session.Peers)
            if (p.id != session.MyId) return p.id;
        return null;
    }

    void OnLoadStarted() => WorldSaveRunner.SaveNow();

    // ChatUIManager lives in UI (not visible from here); its public ShowSystemMessage is reached by name.
    static void Say(string message)
    {
        foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (b && b.GetType().Name == "ChatUIManager") b.SendMessage("ShowSystemMessage", message, SendMessageOptions.DontRequireReceiver);
    }

    [System.Serializable] sealed class ClockMsg { public long min; }
    [System.Serializable] sealed class Handover { public string handover; public long opSeq; public string worldId; public int generation; }
}
