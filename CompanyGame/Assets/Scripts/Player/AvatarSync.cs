using System.Collections.Generic;
using CompanyGame.Daldongne;
using UnityEngine;

/// <summary>
/// My avatar on a sync-layer server (Docs/Multiplayer_Redesign.md 12.7). Once the server has my character and the map is
/// up, a hidden copy of Resources/Net/RemotePlayer (kept across map loads, no renderers) is shared as entity
/// <see cref="PrefabKey"/> with FollowsOwnerMap, and follows the real player every frame: the player object itself is
/// replaced on map travel. Its NetTransform carries the position, its AvatarState the look and pose, and a PlayerCombat
/// attack becomes the Punch event. Others get the same template (NetTransform, AvatarState, RemoteAvatarView in this order,
/// so the NetBehaviour numbers match). Also keeps the Peer names: an avatar's tag shows its owner's character name.
/// </summary>
public sealed class AvatarSync : MonoBehaviour
{
    public const string PrefabKey = "Net/RemotePlayer";
    const float SpeedFollow = 20f, TeleportDistance = 2f;

    static AvatarSync instance;
    static GameObject template;
    static readonly Dictionary<uint, string> names = new Dictionary<uint, string>();

    GameObject proxy;
    AvatarState state;
    PlayerCombat combat;
    float speed;

    /// <summary>My shared avatar while on a server (null otherwise), for QA.</summary>
    public static NetEntity Mine => instance && instance.proxy ? instance.proxy.GetComponent<NetEntity>() : null;

    /// <summary>The character name the server gave connection <paramref name="connection"/> (clean, at most 12 characters), "" if none yet.</summary>
    public static string NameOf(uint connection) => names.TryGetValue(connection, out var name) ? name : "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        names.Clear();
        // SyncClient resets PrefabFactory at SubsystemRegistration, before this.
        var fallback = SyncClient.PrefabFactory;
        SyncClient.PrefabFactory = key => key == PrefabKey ? Template() : fallback?.Invoke(key);
        if (instance) return;
        var go = new GameObject("AvatarSync");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<AvatarSync>();
    }

    // Inactive: SyncClient activates each copy after giving it its state.
    static GameObject Template()
    {
        if (template) return template;
        var prefab = Resources.Load<GameObject>(PrefabKey);
        if (!prefab) { Debug.LogWarning("[AvatarSync] Resources/" + PrefabKey + " is missing. Run CompanyGame/Net/Build RemotePlayer Prefab."); return null; }
        template = Instantiate(prefab);
        template.SetActive(false);
        template.name = prefab.name;
        DontDestroyOnLoad(template);
        template.AddComponent<NetTransform>();
        template.AddComponent<AvatarState>();
        template.AddComponent<RemoteAvatarView>();
        // Keep the authored remote body for local aiming; only ServerCombat may turn an intent into player damage.
        if (!template.GetComponent<Collider>())
        {
            var body = template.AddComponent<CapsuleCollider>();
            body.center = Vector3.up * .9f; body.height = 1.8f; body.radius = .35f;
        }
        return template;
    }

    void Awake()
    {
        if (SyncClient.Instance) SyncClient.Instance.PeerChanged += OnPeer;
    }

    void OnDestroy()
    {
        if (SyncClient.Instance) SyncClient.Instance.PeerChanged -= OnPeer;
        if (combat) combat.AttackPerformed -= OnAttack;
        if (instance == this) instance = null;
    }

    static void OnPeer(bool joined, uint number, string account, string name)
    {
        if (joined) names[number] = RemoteAvatarView.CleanName(name);
        else names.Remove(number);
    }

    static bool InMap
    {
        get
        {
            var player = PlayerSpawner.Player;
            return player && player.isActiveAndEnabled && !SceneLoadManager.IsLoading && SceneLoadManager.CurrentMap.name != PlayerSpawner.TitleSceneName;
        }
    }

    void LateUpdate() // after the player moved this frame
    {
        var client = SyncClient.Instance;
        if (proxy && !proxy.GetComponent<NetEntity>().IsMine) { Destroy(proxy); proxy = null; } // disconnected: no longer shared
        if (!client || !client.Connected || !InMap) return; // keeps the last place while a map loads
        var player = PlayerSpawner.Player.transform;
        if (!proxy)
        {
            if (!client.HasCharacter || client.Map != SceneLoadManager.CurrentMap.path || !Template()) return;
            proxy = Instantiate(template, player.position, Quaternion.Euler(0f, player.eulerAngles.y, 0f));
            proxy.name = "AvatarSync (mine)";
            DestroyImmediate(proxy.GetComponent<RemoteAvatarView>());
            foreach (var collider in proxy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider); // my own punches must not hit it
            foreach (Transform child in proxy.transform) child.gameObject.SetActive(false); // the looks: nothing to render here
            proxy.SetActive(true);
            DontDestroyOnLoad(proxy);
            state = proxy.GetComponent<AvatarState>();
            speed = 0f;
            Share(player);
            client.Spawn(PrefabKey, proxy, EntityFlags.FollowsOwnerMap);
            WatchAttacks();
            return;
        }
        float dt = Time.deltaTime;
        Vector3 step = Vector3.ProjectOnPlane(player.position - proxy.transform.position, Vector3.up);
        float measured = dt > 0f && step.magnitude < TeleportDistance ? step.magnitude / dt : 0f;
        speed = Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-SpeedFollow * dt));
        proxy.transform.SetPositionAndRotation(player.position, Quaternion.Euler(0f, player.eulerAngles.y, 0f));
        Share(player);
        WatchAttacks();
    }

    void Share(Transform player)
    {
        var look = player.GetComponent<DaldongnePlayerAppearance>();
        var vehicle = player.GetComponent<PlayerVehicle>();
        var seating = player.GetComponent<PlayerSeating>();
        var hand = player.GetComponent<PlayerHeldItem>();
        state.Set(look && look.selected == DaldongnePlayerAppearance.Variant.Female,
            vehicle && vehicle.IsRiding && vehicle.CurrentItem ? vehicle.CurrentItem.itemId : "",
            speed, seating && seating.IsSeated, hand && hand.HeldItem ? hand.HeldItem.itemId : "");
    }

    // The player object is replaced on scene travel; follow whichever PlayerCombat is current.
    void WatchAttacks()
    {
        var player = PlayerSpawner.Player;
        var current = player ? player.GetComponent<PlayerCombat>() : null;
        if (current == combat) return;
        if (combat) combat.AttackPerformed -= OnAttack;
        combat = current;
        if (combat) combat.AttackPerformed += OnAttack;
    }

    void OnAttack(bool shot)
    {
        if (state && combat)
        {
            var player = PlayerSpawner.Player;
            if (player)
            {
                proxy.transform.SetPositionAndRotation(player.transform.position, Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f));
                Share(player.transform);
            }
            WorldStateSync.FlushBooks(); // an immediately moved hotbar item reaches the existing ledger before its attack intent.
            state.SendAttack(combat);
        }
    }
}
