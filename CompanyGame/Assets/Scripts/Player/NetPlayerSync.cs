using System;
using System.Collections.Generic;
using CompanyGame.Daldongne;
using UnityEngine;

/// <summary>
/// Sends the local player's position while in a room and shows the other members as
/// Resources/Net/RemotePlayer avatars (built by CompanyGame/Net/Build RemotePlayer Prefab).
/// The position message also carries the character name (NameTag above the head), the look (male/female),
/// the vehicle being ridden (its model is shown under the avatar) and an attack counter (a change = one punch).
/// Attaches itself at startup; DaldongneAvatarMotion animates the walk, the punch is layered on top here.
/// </summary>
[DefaultExecutionOrder(500)] // after DaldongneAvatarMotion's LateUpdate, so the punch overrides the walk arm
public sealed class NetPlayerSync : MonoBehaviour
{
    const float SendInterval = .1f;
    const float Follow = 12f;
    const float SnapDistance = 10f;
    const float PunchTime = .3f;
    const string PrefabPath = "Net/RemotePlayer";

    /// <summary>(id, last position, last yaw) of a member who left the room; vehicles they drove are parked there.</summary>
    public static event Action<string, Vector3, float> RemoteLeft;

    sealed class Remote
    {
        public GameObject avatar, car;
        public Transform male, female;
        public NameTag tag;
        public string map, name = "", veh = "", carItem = "";
        public Vector3 target;
        public float ry, speed, pedal, punchAt = -10f;
        public bool isFemale, isCar;
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

    void LateUpdate()
    {
        foreach (var remote in remotes.Values)
            if (remote.avatar && remote.avatar.activeSelf) Punch(remote);
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
        remote.name = CleanName(m.nm);
        remote.isFemale = m.g == 1;
        remote.veh = m.veh ?? "";
        if (m.act != remote.act) { remote.act = m.act; remote.punchAt = Time.time; }
    }

    // Steam rooms relay pos unchecked: keep the label plain text and short.
    static string CleanName(string name)
    {
        name = (name ?? "").Replace("<", "").Replace(">", "").Trim();
        return name.Length > 12 ? name.Substring(0, 12) : name;
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
        UpdateVehicle(remote);
        // In a car only the car is seen (same as the local driver); on foot or on a bicycle the body shows.
        if (remote.male) remote.male.gameObject.SetActive(!remote.isCar && !remote.isFemale);
        if (remote.female) remote.female.gameObject.SetActive(!remote.isCar && remote.isFemale);
        var motion = Motion(remote);
        if (motion && remote.car && !remote.isCar)
        {
            var item = InventorySnapshot.Find(remote.carItem);
            remote.pedal += remote.speed * Time.deltaTime / Mathf.Max(.1f, item ? item.wheelRadius : .35f) * .45f;
            motion.SetCycling(true, remote.pedal);
        }
        remote.tag.Text = remote.name;
    }

    void UpdateVehicle(Remote remote)
    {
        if (remote.veh == remote.carItem) return;
        if (remote.car) Destroy(remote.car);
        remote.car = null; remote.isCar = false; remote.carItem = remote.veh;
        foreach (var motion in remote.avatar.GetComponentsInChildren<DaldongneAvatarMotion>(true)) motion.SetCycling(false, 0f);
        var item = InventorySnapshot.Find(remote.veh);
        if (!item || !item.vehiclePrefab) return;
        // The local driver's pivot is the car's pivot (driver's seat), so the model sits at the avatar's origin.
        remote.car = Instantiate(item.vehiclePrefab, remote.avatar.transform, false);
        foreach (var collider in remote.car.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        foreach (var child in remote.car.GetComponentsInChildren<Transform>(true))
            if (child.name == "Kickstand") child.gameObject.SetActive(false);
        remote.isCar = item.IsCar;
    }

    DaldongneAvatarMotion Motion(Remote remote)
    {
        var visual = remote.isFemale ? remote.female : remote.male;
        return visual ? visual.GetComponentInChildren<DaldongneAvatarMotion>() : null;
    }

    // The same swing as the local third-person punch (PlayerHeldItem): right arm forward, then back.
    void Punch(Remote remote)
    {
        float elapsed = Time.time - remote.punchAt;
        if (elapsed >= PunchTime || remote.isCar) return;
        var motion = Motion(remote);
        if (!motion || !motion.rightArm) return;
        float amount = Mathf.Sin(elapsed / PunchTime * Mathf.PI);
        motion.rightArm.localRotation = Quaternion.Slerp(motion.rightArm.localRotation, motion.RightArmRest * Quaternion.Euler(-76f, 0f, -3f), amount);
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
        remote.male = remote.avatar.transform.Find("MaleVisual");
        remote.female = remote.avatar.transform.Find("FemaleVisual");
        remote.tag = NameTag.Ensure(remote.avatar, remote.name); // same billboard as the local player's tag
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
