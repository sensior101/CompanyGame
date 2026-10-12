using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Steam development mode (AppID 480) needs steam_appid.txt next to the exe, or SteamAPI.Init refuses to start.
/// Every standalone build gets it copied from the project root (created with "480" if the root copy is missing).
/// </summary>
public sealed class SteamBuildPostprocess : IPostprocessBuildWithReport
{
    public int callbackOrder => 100;

    public void OnPostprocessBuild(BuildReport report)
    {
        string exeDir = Path.GetDirectoryName(report.summary.outputPath);
        if (string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir)) return;
        string source = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "steam_appid.txt");
        if (!File.Exists(source)) File.WriteAllText(source, "480");
        File.Copy(source, Path.Combine(exeDir, "steam_appid.txt"), true);
        Debug.Log("[Steam] steam_appid.txt copied next to " + Path.GetFileName(report.summary.outputPath));
    }
}
