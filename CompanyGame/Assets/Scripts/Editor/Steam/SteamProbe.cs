using UnityEditor;
using UnityEngine;

/// <summary>-executeMethod SteamProbe.Run : tries SteamAPI.Init with AppID 480 (needs the Steam client running and logged in) and logs the result.</summary>
public static class SteamProbe
{
    [MenuItem("CompanyGame/Steam/Probe Init")]
    public static void Run()
    {
        bool ok = SteamBackend.TryInit(out string error);
        Debug.Log("[SteamProbe] init=" + ok + " error=" + (error ?? "none") + " persona=" + SteamBackend.PersonaName + " steamId=" + SteamBackend.SteamIdText);
    }
}
