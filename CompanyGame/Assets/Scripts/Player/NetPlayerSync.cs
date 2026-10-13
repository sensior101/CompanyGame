using System;
using System.Collections.Generic;
using CompanyGame.Daldongne;
using UnityEngine;

/// <summary>
/// Sends the local player's position while in a room and shows the other members as
/// Resources/Net/RemotePlayer avatars (built by CompanyGame/Net/Build RemotePlayer Prefab).
/// The position message also carries the character name (NameTag above the head), the look (male/female),
/// the vehicle being ridden (its model is shown under the avatar) and an attack counter (a change = one punch).
/// Attaches itself at startup; RemoteAvatarView shows all that (shared with the sync-layer AvatarState), this moves it.
/// </summary>
public sealed class NetPlayerSync : MonoBehaviour
{
    const float SendInterval = .1f;
    const float Follow = 12f;
    const float SnapDistance = 10f;
    const string PrefabPath = "Net/RemotePlayer";

    /// <summary>(id, last position, last yaw) of a member who left the room; vehicles they drove are parked there.</summary>
    public static event Action<string, Vector3, float> RemoteLeft;

    sealed class Remote
    {
        public GameObject avatar;
        public RemoteAvatarView view;
        public string map, name = "", veh = "";
        public Vector3 target;
        public float ry, speed;
        public bool isFemale;
        public int act;
    }

    static NetPlayerSync instance;
    readonly Dictionary<string, Remote> remotes = new Dictionary<string, Remote>();

    /// <summary>Character name a room member last sent (null if none yet).</summary>
    public static string NameOf(string id) =>
        instance && id != null && instance.remotes.TryGetValue(id, out var r) && r.name.Length > 0 ? r.name : null;
    NetSession session;
    GameObject prefab;
    bool prefabMissingLogged;
    float nextSendAt;
    Vector3 lastSent;
    PlayerCombat combat;
    int attacks;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if (FindAnyObjectByType<NetPlayerSync>()) return;
        var go = new GameObject("NetPlayerSync");
        DontDestroyOnLoad(go);
        go.AddComponent<NetPlayerSync>();
    }

    void Awake() { instance = this; }

    void Update()
    {
        Bind();
        if (session == null || !session.InRoom) { if (remotes.Count > 0) Clear(); return; }
        WatchAttacks();
        SendOwnPosition();
        string map = SceneLoadManager.CurrentMap.path;
        foreach (var remote in remotes.Values) Move(remote, map);
    }

    void OnDestroy()
    {
        if (session) session.Received -= OnMessage;
        if (combat) combat.AttackPerformed -= CountAttack;
    }

    void Bind()
    {
        if (session || !NetSession.Instance) return;
        session = NetSession.Instance;
        session.Received += OnMessage;
    }

    // The player object is replaced on scene travel; follow whichever PlayerCombat is current.
    void WatchAttacks()
    {
        var player = PlayerSpawner.Player;
        var current = player ? player.GetComponent<PlayerCombat>() : null;
        if (current == combat) return;
        if (combat) combat.AttackPerformed -= CountAttack;
        combat = current;
        if (combat) combat.AttackPerformed += CountAttack;
    }

    void CountAttack(bool shot)
    {
        attacks++;
        nextSendAt = 0f; // send now, a punch is short
    }

    void SendOwnPosition()
    {
        var player = PlayerSpawner.Player;
        if (!player || Time.unscaledTime < nextSendAt) return;
        float dt = SendInterval + Time.unscaledTime - nextSendAt;
        nextSendAt = Time.unscaledTime + SendInterval;
        Vector3 pos = player.transform.position;
        float speed = Vector3.ProjectOnPlane(pos - lastSent, Vector3.up).magnitude / Mathf.Clamp(dt, .01f, 1f);
        lastSent = pos;
        var look = player.GetComponent<DaldongnePlayerAppearance>();
        var vehicle = player.GetComponent<PlayerVehicle>();
        string riding = vehicle && vehicle.IsRiding && vehicle.CurrentItem ? vehicle.CurrentItem.itemId : "";
        session.SendPos(SceneLoadManager.CurrentMap.path, pos, player.transform.eulerAngles.y, speed,
            GameSession.LocalPlayerName, look && look.selected == DaldongnePlayerAppearance.Variant.Female, riding, attacks);
    }

    void OnMessage(NetMsg m)
    {
        if (m.t == "pos" && m.from != session.MyId) OnPos(m);
        else if (m.t == "peer" && m.ev == "leave") Remove(m.id);
        else if (m.t == "host" && !session.InRoom) Clear();
    }

    void OnPos(NetMsg m)
    {
        if (!remotes.TryGetValue(m.from, out var remote))
        {
            remote = new Remote { target = new Vector3(m.x, m.y, m.z), act = m.act }; // no punch for swings made before we saw them
            remotes[m.from] = remote;
        }
        remote.map = m.map;
        remote.target = new Vector3(m.x, m.y, m.z);
        remote.ry = m.ry;
        remote.speed = m.sp;
        remote.name = RemoteAvatarView.CleanName(m.nm); // Steam rooms relay pos unchecked
        remote.isFemale = m.g == 1;
        remote.veh = m.veh ?? "";
        if (m.act != remote.act) { remote.act = m.act; if (remote.view) remote.view.Punch(); }
    }

    void Move(Remote remote, string currentMap)
    {
        bool visible = remote.map == currentMap;
        if (!visible) { if (remote.avatar) remote.avatar.SetActive(false); return; }
        if (!remote.avatar && !Spawn(remote)) return;
        remote.avatar.SetActive(true);
        var t = remote.avatar.transform;
        if ((t.position - remote.target).sqrMagnitude > SnapDistance * SnapDistance) t.position = remote.target;
        float k = 1f - Mathf.Exp(-Follow * Time.deltaTime);
        t.position = Vector3.Lerp(t.position, remote.target, k);
        t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, remote.ry, 0f), k);
        var view = remote.view;
        view.female = remote.isFemale;
        view.vehicle = remote.veh;
        view.speed = remote.speed;
        view.playerName = remote.name;
    }

    bool Spawn(Remote remote)
    {
        if (!prefab) prefab = Resources.Load<GameObject>(PrefabPath);
        if (!prefab)
        {
            if (!prefabMissingLogged) Debug.LogWarning("[Net] Resources/" + PrefabPath + " is missing. Run CompanyGame/Net/Build RemotePlayer Prefab.");
            prefabMissingLogged = true;
            return false;
        }
        remote.avatar = Instantiate(prefab, remote.target, Quaternion.Euler(0f, remote.ry, 0f));
        DontDestroyOnLoad(remote.avatar);
        remote.view = remote.avatar.AddComponent<RemoteAvatarView>();
        return true;
    }

    void Remove(string id)
    {
        if (!remotes.TryGetValue(id, out var remote)) return;
        RemoteLeft?.Invoke(id, remote.target, remote.ry);
        if (remote.avatar) Destroy(remote.avatar);
        remotes.Remove(id);
    }

    void Clear()
    {
        foreach (var remote in remotes.Values) if (remote.avatar) Destroy(remote.avatar);
        remotes.Clear();
    }
}
