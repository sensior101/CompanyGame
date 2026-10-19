using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// "My PC server" (Docs/Multiplayer_Redesign.md 12.3): this game's own exe started as a windowless server process
/// (<c>-batchmode -nographics -gameserver &lt;port&gt; -serversettings &lt;path&gt; -owner &lt;my account&gt;</c>) that keeps running after the game
/// closes; the owner is an admin of it (12.15).
/// The server writes <see cref="RunningPath"/> (pid, port; the server code once its EOS session exists) when it listens; that file is how the title finds it again.
/// Stopping (12.5) writes <see cref="StopRequestPath"/>: the server saves its world and quits; after 5 seconds it is killed,
/// and only a process whose name and path are this exe's, never whatever now has that pid.
/// The editor has no exe to start (<see cref="CanStart"/> false): it uses the in-app server instead, and QA sets <see cref="ExePath"/>.
/// </summary>
public static class LocalServerProcess
{
    [Serializable]
    public sealed class RunningInfo
    {
        public int pid;
        public int port;
        public string startedAt;
        /// <summary>The world it serves (혼자 하기 must not open the same one).</summary>
        public string world;
        /// <summary>Its server code once its EOS session exists, "" before that or without EOS (12.13).</summary>
        public string code = "";
    }

    /// <summary>The server exe. Null in the editor unless QA points it at a build.</summary>
    public static string ExePath = DefaultExe();

    public static bool CanStart => !string.IsNullOrEmpty(ExePath) && File.Exists(ExePath);

    public static string RunningPath => Path.Combine(ServerSettings.Folder, "running.json");
    public static string StopRequestPath => Path.Combine(ServerSettings.Folder, StopRequestName);
    public const string StopRequestName = "stop.request";

    static string DefaultExe()
    {
#if UNITY_EDITOR
        return null;
#else
        if (Application.platform != RuntimePlatform.WindowsPlayer) return null;
        try { return Process.GetCurrentProcess().MainModule.FileName; }
        catch (Exception) { return null; }
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ExePath = DefaultExe(); }

    /// <summary>Saves the settings to this slot's file and starts the server process. Null when it could not be started.</summary>
    public static Process Start(ServerSettings settings)
    {
        if (!CanStart) return null;
        try
        {
            settings.Save();
            File.Delete(RunningPath); // a stale one would look like the new server
            File.Delete(StopRequestPath); // a stale one would stop it at once
            string log = Path.Combine(ServerSettings.Folder, "server.log");
            return Process.Start(new ProcessStartInfo(ExePath,
                $"-batchmode -nographics -gameserver {settings.port} -serversettings \"{ServerSettings.FilePath}\" -owner \"{GameSession.LocalPlayerId}\" -logFile \"{log}\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
            });
        }
        catch (Exception e) { Debug.LogWarning("[LocalServer] start failed: " + e.Message); return null; }
    }

    /// <summary>The running server of this slot, or null (no file, the process is gone, or that pid is not our exe).</summary>
    public static RunningInfo Running()
    {
        try
        {
            if (!File.Exists(RunningPath)) return null;
            var info = JsonUtility.FromJson<RunningInfo>(File.ReadAllText(RunningPath));
            if (info == null || info.pid <= 0) return null;
            using var process = Find(info.pid);
            if (process == null) { File.Delete(RunningPath); return null; } // killed or crashed: it could not clean up
            return IsOurs(process) ? info : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Asks the running server of this slot to save and quit (stop.request), kills it if it is still there after 5 seconds
    /// (only after checking the pid is our exe), then removes the running file.
    /// </summary>
    public static bool Stop()
    {
        var info = Running();
        if (info == null) return false;
        using var process = Find(info.pid);
        if (process == null || !IsOurs(process)) return false;
        try
        {
            File.WriteAllText(StopRequestPath, "");
            if (!process.WaitForExit(5000))
            {
                Debug.LogWarning("[LocalServer] the server did not stop in 5 seconds, killing it");
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch (Exception e) { Debug.LogWarning("[LocalServer] stop failed: " + e.Message); return false; }
        try { File.Delete(RunningPath); File.Delete(StopRequestPath); } catch (Exception) { /* the next Running() deletes it */ }
        return true;
    }

    static Process Find(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            if (!process.HasExited) return process;
            process.Dispose();
        }
        catch (Exception) { /* no such process */ }
        return null;
    }

    // Same file name and same full path as the exe we start. Anything unreadable counts as not ours.
    static bool IsOurs(Process process)
    {
        if (string.IsNullOrEmpty(ExePath)) return false;
        try
        {
            string want = Path.GetFullPath(ExePath);
            return string.Equals(process.ProcessName, Path.GetFileNameWithoutExtension(want), StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Path.GetFullPath(process.MainModule.FileName), want, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }
}
