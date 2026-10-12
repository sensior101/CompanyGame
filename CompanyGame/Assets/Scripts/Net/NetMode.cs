using UnityEngine;

/// <summary>Which connection the title screen uses: the Cloudflare/Node server (default) or Steam lobbies (development mode, AppID 480). Remembered per profile slot.</summary>
public static class NetMode
{
    static string Key => ProfileSlot.Key("net.mode");

    public static bool IsSteam
    {
        get => PlayerPrefs.GetString(Key, "") == "steam";
        set { PlayerPrefs.SetString(Key, value ? "steam" : "cloudflare"); PlayerPrefs.Save(); }
    }

    public static string Label => IsSteam ? "Steam (개발용)" : "Cloudflare 서버";
}
