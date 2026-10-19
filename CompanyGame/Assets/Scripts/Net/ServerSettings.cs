using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// "서버 열기" settings (Docs/Multiplayer_Redesign.md 12.3): name, players, public, password, port. One file per profile
/// slot at <see cref="FilePath"/>; the PC server process gets that path as <c>-serversettings</c>. Values are always kept
/// in range by <see cref="Clamp"/>: players 2..50 on PC, 2..10 on mobile. <see cref="world"/> picks the world under
/// <see cref="WorldsFolder"/> (12.5; empty = a new one). 혼자 하기 uses <see cref="Solo"/> instead: 1 player, never clamped.
/// The server code (12.13) comes from the world id (<see cref="CodeOf"/>), so a world keeps its code across restarts.
/// The dedicated server (12.15) also reads <see cref="admins"/>, <see cref="bans"/>, <see cref="nightSkip"/> and <see cref="trafficCars"/>
/// from it; ServerAdmin reads those four again whenever the file changes, the rest need a restart.
/// </summary>
[Serializable]
public sealed class ServerSettings
{
    public const int MinPlayers = 2, MaxPlayersPc = 50, MaxPlayersMobile = 10, NameMax = 30, PasswordMax = 32, DefaultPort = 7777;
    public const string DefaultName = "달동네 서버";
    /// <summary>The most NPC cars a server may set: the most whose traffic plan closed for every seed tried (12.15). TrafficSpawner.MaxCars is the same.</summary>
    public const int MaxTrafficCars = 10, DefaultTrafficCars = 8;
    const int RecentMax = 5;

    public string name = DefaultName;
    public int maxPlayers = 8;
    public bool isPublic;
    public string password = "";
    public int port = DefaultPort;
    /// <summary>World id (folder under <see cref="WorldsFolder"/>); empty = make a new world when the server starts.</summary>
    public string world = "";
    /// <summary>Admin accounts (12.15). The one who opened the server is an admin without being here.</summary>
    public List<string> admins = new List<string>();
    /// <summary>Banned accounts: refused at Welcome, kicked when added.</summary>
    public List<string> bans = new List<string>();
    /// <summary>Night skip hours "start-end" (e.g. "0-6"); "" keeps the world's own.</summary>
    public string nightSkip = "";
    /// <summary>NPC cars on each map with lanes, 0..<see cref="MaxTrafficCars"/> (every client builds its plan with it).</summary>
    public int trafficCars = DefaultTrafficCars;

    /// <summary>QA only: pretend to be (or not to be) a phone.</summary>
    public static bool? ForceMobile;
    public static bool IsMobile => ForceMobile ?? Application.isMobilePlatform;
    public static int PlayerLimit => IsMobile ? MaxPlayersMobile : MaxPlayersPc;

    public static string Folder => Path.Combine(ProfileSlot.DataRoot, "server");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    /// <summary>The worlds of this slot's servers: worlds/&lt;id&gt;/world.dat (서버 열기 and 혼자 하기 share them).</summary>
    public static string WorldsFolder => Path.Combine(Folder, "worlds");

    /// <summary>혼자 하기 (9장-4): my PC server, private, 1 player, any free port (bound to 127.0.0.1 by the caller).</summary>
    public static ServerSettings Solo(string world) => new ServerSettings { name = "혼자 하기", maxPlayers = 1, port = 0, world = world ?? "" };

    // ---- server code (12.13) ----

    /// <summary>Letters of a server code: capitals and digits without the easily confused 0, O, 1 and I.</summary>
    public const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 6;

    /// <summary>
    /// The 6-letter code of world <paramref name="world"/>: the same every time the world is opened.
    /// ponytail: 30 bits from a hash of the id; two open servers sharing a code is about 1 in 10^9 per pair (the second one gets no session).
    /// </summary>
    public static string CodeOf(string world)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(world ?? ""));
        var code = new StringBuilder(CodeLength);
        for (int i = 0; i < CodeLength; i++) code.Append(CodeLetters[hash[i] & 31]);
        return code.ToString();
    }

    /// <summary>What a player typed as a code, in capitals without spaces; null when it cannot be one.</summary>
    public static string NormalizeCode(string typed)
    {
        string code = (typed ?? "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
        if (code.Length != CodeLength) return null;
        foreach (char c in code) if (CodeLetters.IndexOf(c) < 0) return null;
        return code;
    }

    public ServerSettings Clamp()
    {
        name = (name ?? "").Replace("<", "").Replace(">", "").Trim();
        if (name.Length > NameMax) name = name.Substring(0, NameMax).Trim();
        if (name.Length == 0) name = DefaultName;
        password ??= "";
        if (password.Length > PasswordMax) password = password.Substring(0, PasswordMax);
        maxPlayers = Mathf.Clamp(maxPlayers, MinPlayers, PlayerLimit);
        if (port < 1024 || port > 65535) port = DefaultPort;
        world = WorldStore.IsValidId(world) ? world : "";
        admins = Clean(admins);
        bans = Clean(bans);
        nightSkip = (nightSkip ?? "").Trim();
        if (nightSkip.Length > 0 && !GameServer.TryParseNightSkip(nightSkip, out _, out _)) nightSkip = "";
        trafficCars = Mathf.Clamp(trafficCars, 0, MaxTrafficCars);
        return this;
    }

    static List<string> Clean(List<string> accounts)
    {
        var list = new List<string>();
        foreach (string a in accounts ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(a) && !list.Contains(a.Trim())) list.Add(a.Trim());
        return list;
    }

    /// <summary>The file at <paramref name="path"/> (default: this slot's), clamped; defaults when missing or unreadable.</summary>
    public static ServerSettings Load(string path = null)
    {
        try
        {
            path ??= FilePath;
            if (File.Exists(path)) return (JsonUtility.FromJson<ServerSettings>(File.ReadAllText(path)) ?? new ServerSettings()).Clamp();
        }
        catch (Exception e) { Debug.LogWarning("[ServerSettings] unreadable, using defaults: " + e.Message); }
        return new ServerSettings().Clamp();
    }

    public void Save(string path = null)
    {
        path ??= FilePath;
        Clamp();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(this, true));
    }

    // ---- recent join addresses (per slot) ----

    static string RecentKey => ProfileSlot.Key("server.recent");

    public static List<string> Recent
    {
        get
        {
            var list = new List<string>(PlayerPrefs.GetString(RecentKey, "").Split('\n'));
            list.RemoveAll(string.IsNullOrWhiteSpace);
            return list;
        }
    }

    public static void Remember(string address)
    {
        var list = Recent;
        list.Remove(address);
        list.Insert(0, address);
        if (list.Count > RecentMax) list.RemoveRange(RecentMax, list.Count - RecentMax);
        PlayerPrefs.SetString(RecentKey, string.Join("\n", list));
        PlayerPrefs.Save();
    }
}
