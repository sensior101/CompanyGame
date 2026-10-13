using UnityEngine;

/// <summary>
/// Per-character server save. Every world has its own character per account (name, look, inventory, personal
/// economy, position): GET/PUT /me/state?world=&lt;WorldSave.WorldId&gt;. On joining a room it restores that character
/// (a new one starts from the fresh inventory and money and is saved at once); then it PUTs every 2 min, on leaving the room
/// and on quit. On leaving, this PC's own (offline) money and inventory come back.
/// Nothing is PUT until the restore finished, so a failed GET can never overwrite the server copy.
/// The server copy wins over local books (InventorySnapshot.Import replaces the inventory).
/// In Steam mode there is no server: the same state is read from and written to persistentDataPath/steam/&lt;steamid&gt;/&lt;world&gt;.json (SteamLocalState).
/// When the server's daily budget is used up (ServerBudget) nothing is sent until the next UTC day.
/// </summary>
public sealed class NetPlayerSave : MonoBehaviour
{
    const float SaveInterval = 120f, RetryMin = 5f, RetryMax = 60f;

    /// <summary>Last failed server call ("get"/"put" plus the error code), empty while all is well.</summary>
    public static string LastError { get; private set; } = "";

    // The title screen already fetched the character to know whether to ask for a name; used once instead of a second GET.
    static PlayerState prefetched;
    static string prefetchedWorld;

    public static void Prefetch(string world, PlayerState state) { prefetchedWorld = world; prefetched = state; }

    NetSession session;
    string world; // the world whose character is loaded; saves go there even after WorldSave switched back to the own world
    bool wasInRoom;
    bool restored;
    bool busy, saving;
    float nextSaveAt, retryRestoreAt, restoreBackoff = RetryMin, saveBackoff = RetryMin;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if (FindAnyObjectByType<NetPlayerSave>()) return;
        var go = new GameObject("NetPlayerSave");
        DontDestroyOnLoad(go);
        go.AddComponent<NetPlayerSave>();
    }

    static bool Local => NetSession.Instance && NetSession.Instance.IsSteam;

    void Awake() { SceneLoadManager.LoadStarted += SaveBeforeMapChange; }
    void OnDestroy() { SceneLoadManager.LoadStarted -= SaveBeforeMapChange; }

    // The player is still in the old map here, so position and inventory are the ones worth keeping.
    void SaveBeforeMapChange() { if (restored && session && session.InRoom) Save(); }

    static PlayerData Account => PlayerData.Instance && PlayerData.Instance.IsLoggedIn ? PlayerData.Instance : null;

    void Update()
    {
        if (!session) session = NetSession.Instance;
        if (!session) return;
        bool inRoom = session.InRoom;
        if (inRoom && !wasInRoom) { restoreBackoff = RetryMin; Restore(); }
        else if (inRoom && !restored && !busy && Time.unscaledTime >= retryRestoreAt) Restore();
        else if (!inRoom && wasInRoom) LeftRoom();
        else if (inRoom && restored && Time.unscaledTime >= nextSaveAt) Save();
        if (inRoom && restored) KeepLook();
        wasInRoom = inRoom;
    }

    // Save keeps its own copy of the state (taken before any await), so the own money and inventory can come back right away.
    void LeftRoom()
    {
        if (restored) Save();
        restored = false;
        PersonalSave.ResetToFresh();
        PersonalSave.LoadFromDisk();
    }

    // The character's look wins over the prefab default (and the Alt+1/2 debug toggle) while in its world.
    static void KeepLook()
    {
        var player = PlayerSpawner.Player;
        var look = player ? player.GetComponent<CompanyGame.Daldongne.DaldongnePlayerAppearance>() : null;
        if (!look || GameSession.CharacterGender == null) return;
        var want = GameSession.CharacterGender == "female" ? CompanyGame.Daldongne.DaldongnePlayerAppearance.Variant.Female : CompanyGame.Daldongne.DaldongnePlayerAppearance.Variant.Male;
        if (look.selected != want) look.Select(want);
    }

    void OnApplicationQuit()
    {
        // ponytail: best effort, a web request started during quit may not finish. The 30s timer bounds the loss.
        if (restored) Save();
    }

    async void Restore()
    {
        var account = Account;
        if ((!account && !Local) || busy) return;
        if (!Local && ServerBudget.Blocked) { NoteBudget(); retryRestoreAt = Time.unscaledTime + RetryMax; return; }
        busy = true;
        // Joined from the title screen: wait until the map is loaded and the player is placed, so the saved position can apply.
        while (session.InRoom && (!PlayerSpawner.Player || !PlayerSpawner.Player.isActiveAndEnabled || SceneLoadManager.IsLoading))
            await Awaitable.NextFrameAsync();
        if (!session.InRoom) { busy = false; return; }
        string target = WorldSave.EnsureWorldId();
        ApiResult result; PlayerState state;
        if (prefetched != null && prefetchedWorld == target) { result = default; state = prefetched; }
        else (result, state) = Local ? SteamLocalState.Get(session.MyId, target) : await account.GetState(target);
        prefetched = null; prefetchedWorld = null;
        busy = false;
        if (!session.InRoom) return;
        if (!result.Ok || state == null)
        {
            // A failed GET must not disable saving for good: retry with backoff (a saved server copy is never overwritten before it was read).
            if (result.Error == ServerBudget.Code) NoteBudget();
            LastError = "get:" + (result.Error ?? "empty");
            Debug.LogWarning("[NetPlayerSave] GET /me/state failed (" + LastError + "), retrying in " + restoreBackoff + "s");
            retryRestoreAt = Time.unscaledTime + restoreBackoff;
            restoreBackoff = Mathf.Min(restoreBackoff * 2f, RetryMax);
            return;
        }
        LastError = "";
        world = target;
        if (!string.IsNullOrEmpty(state.name)) GameSession.SetCharacter(state.name, state.gender);
        else if (!GameSession.HasCharacter) GameSession.SetCharacter(GameSession.LocalPlayerName, "male"); // joined without the title screen
        bool fresh = !state.IsSaved || string.IsNullOrEmpty(state.name);
        if (fresh) PersonalSave.ResetToFresh(); // a new character never inherits another world's money or items
        else Apply(state);
        restored = true;
        if (fresh) Save(); // the name is kept from the start, so coming back does not ask again
        else nextSaveAt = Time.unscaledTime + SaveInterval;
    }

    // Tell the player once; the saves simply wait (the progress stays in memory and the local files).
    static void NoteBudget()
    {
        LastError = "budget";
        var net = NetSession.Instance;
        if (net && net.StatusText != ServerBudget.Message) net.StatusText = ServerBudget.Message;
    }

    static void Apply(PlayerState state)
    {
        if (!string.IsNullOrEmpty(state.inventory_json))
            InventorySnapshot.Import(InventoryManager.Instance.State, state.inventory_json);
        if (!string.IsNullOrEmpty(state.personal_json)) PersonalSave.Import(state.personal_json);
        var player = PlayerSpawner.Player;
        if (player && state.map == SceneLoadManager.CurrentMap.path)
        {
            player.spawn = new Vector3(state.x, state.y, state.z);
            player.ResetToSpawn();
            player.transform.rotation = Quaternion.Euler(0f, state.ry, 0f);
        }
    }

    async void Save()
    {
        var account = Account;
        var player = PlayerSpawner.Player;
        nextSaveAt = Time.unscaledTime + SaveInterval;
        if ((!account && !Local) || !player || saving) return;
        if (!Local && ServerBudget.Blocked) { NoteBudget(); return; } // no PUT until the next UTC day
        saving = true;
        var pos = player.transform.position;
        var state = new PlayerState
        {
            map = SceneLoadManager.CurrentMap.path,
            x = pos.x, y = pos.y, z = pos.z, ry = player.transform.eulerAngles.y,
            inventory_json = InventorySnapshot.Export(InventoryManager.Instance.State),
            personal_json = PersonalSave.Export(),
            name = GameSession.HasCharacter ? GameSession.LocalPlayerName : "",
            gender = GameSession.CharacterGender ?? ""
        };
        var result = Local ? SteamLocalState.Put(session.MyId, world, state) : await account.PutState(world, state);
        saving = false;
        if (result.Error == ServerBudget.Code) { NoteBudget(); return; }
        if (result.Ok) { saveBackoff = RetryMin; if (LastError.StartsWith("put")) LastError = ""; return; }
        LastError = "put:" + result.Error;
        Debug.LogWarning("[NetPlayerSave] PUT /me/state failed (" + LastError + "), retrying in " + saveBackoff + "s");
        nextSaveAt = Time.unscaledTime + saveBackoff;
        saveBackoff = Mathf.Min(saveBackoff * 2f, RetryMax);
    }
}
