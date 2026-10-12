using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sends the local player's position while in a room and shows the other members as
/// Resources/Net/RemotePlayer avatars (built by CompanyGame/Net/Build RemotePlayer Prefab).
/// Attaches itself at startup; remote avatars are only transform-driven, DaldongneAvatarMotion animates the walk.
/// </summary>
public sealed class NetPlayerSync : MonoBehaviour
{
    const float SendInterval = .1f;
    const float Follow = 12f;
    const float SnapDistance = 10f;
    const string PrefabPath = "Net/RemotePlayer";

    sealed class Remote
    {
        public GameObject avatar;
        public string map;
        public Vector3 target;
        public float ry;
    }

    readonly Dictionary<string, Remote> remotes = new Dictionary<string, Remote>();
    NetSession session;
    GameObject prefab;
    bool prefabMissingLogged;
    float nextSendAt;
    Vector3 lastSent;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if (FindAnyObjectByType<NetPlayerSync>()) return;
        var go = new GameObject("NetPlayerSync");
        DontDestroyOnLoad(go);
        go.AddComponent<NetPlayerSync>();
    }

    void Update()
    {
        Bind();
        if (session == null || !session.InRoom) { if (remotes.Count > 0) Clear(); return; }
        SendOwnPosition();
        string map = SceneLoadManager.CurrentMap.path;
        foreach (var remote in remotes.Values) Move(remote, map);
    }

    void OnDestroy() { if (session) session.Received -= OnMessage; }

    void Bind()
    {
        if (session || !NetSession.Instance) return;
        session = NetSession.Instance;
        session.Received += OnMessage;
    }

    void SendOwnPosition()
    {
        var player = PlayerSpawner.Player;
        if (!player || Time.unscaledTime < nextSendAt) return;
        float dt = SendInterval + Time.unscaledTime - nextSendAt;
        nextSendAt = Time.unscaledTime + SendInterval;
        Vector3 pos = player.transform.position;
        float speed = Vector3.ProjectOnPlane(pos - lastSent, Vector3.up).magnitude / Mathf.Max(dt, .01f);
        lastSent = pos;
        session.SendPos(SceneLoadManager.CurrentMap.path, pos, player.transform.eulerAngles.y, speed);
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
            remote = new Remote { target = new Vector3(m.x, m.y, m.z) };
            remotes[m.from] = remote;
        }
        remote.map = m.map;
        remote.target = new Vector3(m.x, m.y, m.z);
        remote.ry = m.ry;
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
        return true;
    }

    void Remove(string id)
    {
        if (!remotes.TryGetValue(id, out var remote)) return;
        if (remote.avatar) Destroy(remote.avatar);
        remotes.Remove(id);
    }

    void Clear()
    {
        foreach (var remote in remotes.Values) if (remote.avatar) Destroy(remote.avatar);
        remotes.Clear();
    }
}
