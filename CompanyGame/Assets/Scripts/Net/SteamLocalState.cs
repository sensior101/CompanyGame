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

    public static string PathFor(string steamId)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) steamId = (steamId ?? "").Replace(c, '_');
        string root = RootOverride ?? Path.Combine(Application.persistentDataPath, "steam");
        return Path.Combine(root, string.IsNullOrEmpty(steamId) ? "unknown" : steamId, "state.json");
    }

    public static (ApiResult, PlayerState) Get(string steamId)
    {
        string path = PathFor(steamId);
        try
        {
            if (!File.Exists(path)) return (default, new PlayerState());
            return (default, JsonUtility.FromJson<PlayerState>(File.ReadAllText(path)) ?? new PlayerState());
        }
        catch (Exception e) { Debug.LogWarning("[SteamLocalState] read failed: " + e.Message); return (new ApiResult("read_failed"), null); }
    }

    public static ApiResult Put(string steamId, PlayerState state)
    {
        string path = PathFor(steamId);
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
