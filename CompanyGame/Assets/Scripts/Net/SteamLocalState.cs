using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Steam mode has no server database (AppID 480 cannot verify auth tickets), so the per-player state (position,
/// inventory, bank + stocks) lives in persistentDataPath/steam/&lt;steamid&gt;/state.json, in the shape of GET/PUT /me/state.
/// Later, with an own AppID, this file is what moves to Steam Cloud. Bank balance (personal_json) and wallet cash
/// (inside inventory_json) stay separate, exactly as on the server.
/// </summary>
public static class SteamLocalState
{
    /// <summary>Tests point this at a scratch folder.</summary>
    public static string RootOverride;

    /// <summary>One file per world: every world has its own character.</summary>
    public static string PathFor(string steamId, string world)
    {
        string root = RootOverride ?? Path.Combine(Application.persistentDataPath, "steam");
        return Path.Combine(root, Safe(steamId, "unknown"), Safe(world, "state") + ".json");
    }

    static string Safe(string name, string fallback)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = (name ?? "").Replace(c, '_');
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    public static (ApiResult, PlayerState) Get(string steamId, string world)
    {
        string path = PathFor(steamId, world);
        try
        {
            if (!File.Exists(path)) return (default, new PlayerState());
            return (default, JsonUtility.FromJson<PlayerState>(File.ReadAllText(path)) ?? new PlayerState());
        }
        catch (Exception e) { Debug.LogWarning("[SteamLocalState] read failed: " + e.Message); return (new ApiResult("read_failed"), null); }
    }

    public static ApiResult Put(string steamId, string world, PlayerState state)
    {
        string path = PathFor(steamId, world);
        try
        {
            state.updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(state));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return default;
        }
        catch (Exception e) { Debug.LogWarning("[SteamLocalState] write failed: " + e.Message); return new ApiResult("write_failed"); }
    }
}
