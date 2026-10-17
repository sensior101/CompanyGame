using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using CompanyGame.Daldongne;
using UnityEngine;

/// <summary>
/// The character on a sync-layer server (Docs/Multiplayer_Redesign.md 12.5). The server keeps one content blob per
/// account in its world file; this builds it (bank and stocks = PersonalSave, inventory = InventorySnapshot, map and
/// position), gzipped, and applies the one SyncClient got once the map is up. A new character starts from the baseline
/// (PersonalSave.ResetToFresh) and is saved at once. Saves every 30 seconds, before a map change, before leaving
/// (TitleFlow.ReturnToTitle, SyncHost.BeforeStop) and on quit. While <see cref="Active"/> this PC writes no personal
/// file (WorldSaveRunner.OnServer); after leaving, this PC's own money and inventory come back.
/// The server keeps the client's map, position and inventory as they come, but puts its own ledger (bank and stocks) in
/// place of the personal part (12.10.1, ServerLedger).
/// </summary>
public sealed class CharacterSync : MonoBehaviour
{
    const float SaveInterval = 30f;
    const byte ContentVersion = 1;

    static CharacterSync instance;
    bool applied;
    float nextSaveAt;

    /// <summary>True from applying the server's character until this PC's own state is back.</summary>
    public static bool Active => instance && instance.applied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        WorldSaveRunner.OnServer = () => Active;
        if (instance) return;
        var go = new GameObject("CharacterSync");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<CharacterSync>();
    }

    void Awake()
    {
        SceneLoadManager.LoadStarted += SaveNow; // the player is still in the old map: that position is worth keeping
        SyncHost.BeforeStop += SaveNow;
    }

    void OnDestroy()
    {
        SceneLoadManager.LoadStarted -= SaveNow;
        SyncHost.BeforeStop -= SaveNow;
        if (instance == this) instance = null;
    }

    // ponytail: best effort, the socket may close with the process before the frame is out; the 30s timer bounds the loss.
    // An in-app server (혼자 하기) gets it through SyncHost.BeforeStop instead.
    void OnApplicationQuit() => SaveNow();

    static bool InMap
    {
        get
        {
            var player = PlayerSpawner.Player;
            return player && player.isActiveAndEnabled && !SceneLoadManager.IsLoading && SceneLoadManager.CurrentMap.name != PlayerSpawner.TitleSceneName;
        }
    }

    void Update()
    {
        var client = SyncClient.Instance;
        bool on = client && client.Connected && client.HasCharacter;
        if (!applied && on && InMap) Apply(client);
        else if (applied && !on) LeftServer();
        else if (applied && Time.unscaledTime >= nextSaveAt) SaveNow();
        if (applied) KeepLook();
    }

    void Apply(SyncClient client)
    {
        GameSession.SetCharacter(client.CharacterName, client.CharacterGender);
        applied = true;
        if (!Read(client.CharacterContent, out var saved))
        {
            if (client.CharacterContent.Length > 0) Debug.LogWarning("[CharacterSync] the saved character is unreadable; starting from the new character state");
            PersonalSave.ResetToFresh(); // a new character never inherits this PC's or another world's money or items
            SaveNow();
            return;
        }
        if (!string.IsNullOrEmpty(saved.inventory)) InventorySnapshot.Import(InventoryManager.Instance.State, saved.inventory);
        if (!string.IsNullOrEmpty(saved.personal)) PersonalSave.Import(saved.personal);
        var player = PlayerSpawner.Player;
        if (saved.map == SceneLoadManager.CurrentMap.path)
        {
            player.spawn = saved.position;
            player.ResetToSpawn();
            player.transform.rotation = Quaternion.Euler(0f, saved.yaw, 0f);
        }
        nextSaveAt = Time.unscaledTime + SaveInterval;
    }

    void LeftServer()
    {
        applied = false;
        GameSession.ClearCharacter();
        PersonalSave.ResetToFresh();
        PersonalSave.LoadFromDisk();
    }

    /// <summary>Sends the character to the server now (no-op when not on a server).</summary>
    public static void SaveNow()
    {
        if (!instance) return;
        instance.nextSaveAt = Time.unscaledTime + SaveInterval;
        var client = SyncClient.Instance;
        if (!instance.applied || !client || !client.Connected || !PlayerSpawner.Player) return;
        client.SaveCharacter(Capture());
    }

    // The character's look wins over the prefab default (and the Alt+1/2 debug toggle), as NetPlayerSave does for rooms.
    static void KeepLook()
    {
        var player = PlayerSpawner.Player;
        var look = player ? player.GetComponent<DaldongnePlayerAppearance>() : null;
        if (!look || GameSession.CharacterGender == null) return;
        var want = GameSession.CharacterGender == "female" ? DaldongnePlayerAppearance.Variant.Female : DaldongnePlayerAppearance.Variant.Male;
        if (look.selected != want) look.Select(want);
    }

    // ---- content: gzip(u8 version, str map, f32 x y z yaw, str personal json, str inventory json) ----

    public struct Saved
    {
        public string map, personal, inventory;
        public Vector3 position;
        public float yaw;
    }

    /// <summary>The content blob of the character as it is now (also used by QA to measure it).</summary>
    public static byte[] Capture()
    {
        var player = PlayerSpawner.Player;
        return Encode(new Saved
        {
            map = SceneLoadManager.CurrentMap.path,
            position = player ? player.transform.position : Vector3.zero,
            yaw = player ? player.transform.eulerAngles.y : 0f,
            personal = PersonalSave.Export(),
            inventory = InventorySnapshot.Export(InventoryManager.Instance.State, false) // book pages stay on the server (12.10.3)
        });
    }

    /// <summary>The content blob of <paramref name="saved"/>, without any manager (the server's ledger writes it, 12.10.1).</summary>
    public static byte[] Encode(Saved saved)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, System.IO.Compression.CompressionLevel.Optimal, true))
        using (var w = new BinaryWriter(gzip, Encoding.UTF8))
        {
            w.Write(ContentVersion);
            w.Write(saved.map ?? "");
            w.Write(saved.position.x); w.Write(saved.position.y); w.Write(saved.position.z);
            w.Write(saved.yaw);
            w.Write(saved.personal ?? "");
            w.Write(saved.inventory ?? "");
        }
        return stream.ToArray();
    }

    /// <summary>Decodes a content blob (no manager is touched). False for an empty (new character) or unreadable blob.</summary>
    public static bool Read(byte[] content, out Saved saved)
    {
        saved = default;
        if (content == null || content.Length == 0) return false;
        try
        {
            using var gzip = new GZipStream(new MemoryStream(content, false), CompressionMode.Decompress);
            using var r = new BinaryReader(gzip, Encoding.UTF8);
            if (r.ReadByte() != ContentVersion) return false;
            saved.map = r.ReadString();
            saved.position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            saved.yaw = r.ReadSingle();
            saved.personal = r.ReadString();
            saved.inventory = r.ReadString();
            return true;
        }
        catch (Exception e) { Debug.LogWarning("[CharacterSync] unreadable content: " + e.Message); return false; }
    }

    /// <summary>The map the saved character was in, or null (new character): the title enters it instead of the default map.</summary>
    public static string SavedMap(byte[] content) => Read(content, out var saved) && !string.IsNullOrEmpty(saved.map) ? saved.map : null;
}
