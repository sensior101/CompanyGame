using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlayEveryWare.EpicOnlineServices;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// EOS test (Docs/Multiplayer_Redesign.md 12.4) on this PC, and the bundle for a teammate's PC.
///   -executeMethod EosTestQA.WriteConfig  StreamingAssets/EOS (git-ignored, holds the client secret) from CompanyGame/eos.local.json (with -quit)
///   -executeMethod EosTestQA.RunPlay      play mode twice in a row, each time an EosProbe client: Device ID login gives a PUID (tests 1, 2 editor)
///                                         (WITHOUT -quit; the QA exits the editor)
///   -executeMethod EosTestQA.RunProcess   builds Builds/EosTest, runs a windowless -eostest server and a windowed -eostest client of the
///                                         same OS user (tests 2 build, 3, 4, 5, P2P to the same PUID recorded), then writes the bat files
///                                         and README and zips Builds/EosTest.zip for a teammate (with -quit)
///   -executeMethod EosTestQA.RunMac       builds Builds/EosTestMac (needs Mac Build Support), checks the EOS library and config in the .app,
///                                         adds the .command scripts and README, zips Builds/EosTestMac.zip (with -quit)
/// Scripts and READMEs for the bundles are in EosTestBundle~ next to this file (Unity skips folders ending in ~).
/// </summary>
[InitializeOnLoad]
public static class EosTestQA
{
    const string PlayKey = "EosTestQA.play";   // round 1 or 2 while the play mode test runs
    const string PuidKey = "EosTestQA.puid";
    const string FailKey = "EosTestQA.fail";
    const string Exe = "CompanyGame.exe";
    static int checks;

    static EosTestQA() => EditorApplication.playModeStateChanged += OnPlayState;

    [Serializable]
    sealed class LocalValues
    {
        public string ProductId, SandboxId, DeploymentId, ClientId, ClientSecret;
    }

    // ================= config =================

    /// <summary>Writes the plugin's product and Windows config with the plugin's own Config classes. Never logs the values.</summary>
    [MenuItem("CompanyGame/Net/Write EOS Config")]
    public static void WriteConfig()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../eos.local.json"));
        if (!File.Exists(path)) throw new FileNotFoundException("no EOS values: copy eos.local.example.json to eos.local.json and fill it in", path);
        var local = UnityEngine.JsonUtility.FromJson<LocalValues>(File.ReadAllText(path));

        var product = PlayEveryWare.EpicOnlineServices.Config.Get<ProductConfig>();
        string key = product.Clients.FirstOrDefault()?.Value?.EncryptionKey; // keep the key between writes (only Player/Title Data Storage use it)
        var credentials = new EOSClientCredentials(local.ClientId, local.ClientSecret, EOSClientCredentials.IsEncryptionKeyValid(key) ? key : EOSClientCredentials.GenerateEncryptionKey());
        var deployment = new Deployment { SandboxId = SandboxId.FromString(local.SandboxId), DeploymentId = Guid.Parse(local.DeploymentId) };
        product.ProductName = "CompanyGame";
        product.ProductVersion = "1";
        product.ProductId = Guid.Parse(local.ProductId);
        product.Clients.Clear();
        product.Clients.Add(credentials);
        product.Environments.Deployments.Clear();
        product.Environments.Sandboxes.Clear();
        product.Environments.AddDeployment(deployment);
        product.Write();

        var windows = PlayEveryWare.EpicOnlineServices.Config.Get<WindowsConfig>();
        windows.deployment = deployment;
        windows.clientCredentials = credentials;
        windows.isServer = false; // the server process logs in with a Device ID like a player; that is what is being tested
        windows.platformOptionsFlags = WrappedPlatformFlags.DisableOverlay | WrappedPlatformFlags.DisableSocialOverlay; // no overlay in a windowless server
        windows.Write();

        var mac = PlayEveryWare.EpicOnlineServices.Config.Get<MacOSConfig>(); // the same settings for the macOS bundle
        mac.deployment = deployment;
        mac.clientCredentials = credentials;
        mac.isServer = false;
        mac.platformOptionsFlags = windows.platformOptionsFlags;
        mac.Write();
        AssetDatabase.Refresh();
        Debug.Log("[EosTestQA] wrote EOS config to " + Path.GetDirectoryName(product.FilePath) + " (values not logged)");
    }

    // ================= play mode (tests 1, 2) =================

    public static void RunPlay()
    {
        WriteConfig();
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        SessionState.SetString(PuidKey, "");
        SessionState.SetString(FailKey, "");
        SessionState.SetInt(PlayKey, 1);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayState(PlayModeStateChange change)
    {
        int round = SessionState.GetInt(PlayKey, 0);
        if (round == 0) return;
        if (change == PlayModeStateChange.EnteredPlayMode) DrivePlay(round);
        if (change != PlayModeStateChange.EnteredEditMode) return;
        string fail = SessionState.GetString(FailKey, "");
        if (round == 1 && fail == "")
        {
            SessionState.SetInt(PlayKey, 2);
            Debug.Log("[EosTestQA] entering play mode again");
            EditorApplication.EnterPlaymode();
            return;
        }
        SessionState.SetInt(PlayKey, 0);
        if (fail == "") Debug.Log("[EosTestQA] RESULT play passed (two play mode entries, PUID " + SessionState.GetString(PuidKey, "") + ")");
        else Debug.LogError("[EosTestQA] RESULT play FAILED: " + fail);
        EditorApplication.Exit(fail == "" ? 0 : 1);
    }

    static async void DrivePlay(int round)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            var probe = EosProbe.Begin("client", null, null, 0);
            int code = await Within(probe.Finished, 120, "the probe in play mode round " + round);
            Check(code == 0 && probe.Puid != null && probe.Puid.IsValid(), "round " + round + ": Device ID login gave PUID " + Short(probe.Puid?.ToString()) + " in " + clock.ElapsedMilliseconds + "ms");
            string before = SessionState.GetString(PuidKey, "");
            if (round == 2) Check(before == probe.Puid.ToString(), "round 2: the same PUID as round 1");
            SessionState.SetString(PuidKey, probe.Puid.ToString());
            Debug.Log("[EosTestQA] round " + round + " probe lines: " + string.Join(" | ", probe.Lines));
        }
        catch (Exception e)
        {
            SessionState.SetString(FailKey, e.Message);
            Debug.LogError("[EosTestQA] " + e.Message);
        }
        EditorApplication.ExitPlaymode();
    }

    // ================= separate processes (tests 2 build, 3, 4, 5) and the bundle =================

    public static void RunProcess()
    {
        checks = 0;
        WriteConfig();
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/EosTest"));
        string qa = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/EosTestQA"));
        string exe = Path.Combine(dir, Exe);
        string scene = Array.Find(EditorBuildSettings.scenes, s => s.enabled).path; // the probe drops the title scene in batch mode
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene }, locationPathName = exe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
        });
        Check(report.summary.result == BuildResult.Succeeded, "Windows player built to " + dir + " (" + report.summary.result + ")");
        Check(File.Exists(Path.Combine(dir, "CompanyGame_Data/StreamingAssets/EOS/eos_windows_config.json")), "the EOS config is in the build");
        Directory.CreateDirectory(qa);

        Process server = null, client = null;
        try
        {
            string code = "CGQA" + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpperInvariant();
            string serverLog = Path.Combine(qa, "server_log.txt"), clientLog = Path.Combine(qa, "client_log.txt");
            File.Delete(serverLog); File.Delete(clientLog);
            server = Start(exe, $"-batchmode -nographics -eostest server -eostest-minutes 5 -eostest-code {code} -eostest-name \"QA 서버\" -eostest-max 12 -eostest-password -eostest-log \"{serverLog}\" -logFile \"{qa}/server_unity.log\"");
            string serverPuid = WaitLine(serverLog, "PUID=", server, 90);
            Check(serverPuid.Length == 32, "3 the windowless server process (pid " + server.Id + ") got PUID " + Short(serverPuid));
            Check(WaitLine(serverLog, "SESSION=", server, 60) == code, "5 the server made session " + code);

            client = Start(exe, $"-eostest client {serverPuid} -eostest-code {code} -eostest-log \"{clientLog}\" -logFile \"{qa}/client_unity.log\" -screen-fullscreen 0 -screen-width 640 -screen-height 360");
            string clientPuid = WaitLine(clientLog, "PUID=", client, 90);
            Check(clientPuid.Length == 32, "2 the windowed build (pid " + client.Id + ") got PUID " + Short(clientPuid));
            Debug.Log("[EosTestQA] 4 same OS user, two processes: PUIDs " + (clientPuid == serverPuid ? "are the SAME" : "DIFFER") + " (" + Short(serverPuid) + ", " + Short(clientPuid) + ")");
            Check(client.WaitForExit(240000), "the client finished");
            string clientText = File.ReadAllText(clientLog);
            Debug.Log("[EosTestQA] client log:\n" + clientText);
            Check(client.ExitCode == 0 && clientText.Contains("RESULT done"), "the client ran to the end (exit " + client.ExitCode + ")");
            Check(clientText.Contains("SEARCH_ATTR=Success") && clientText.Contains("SEARCH_RESULT id=" + code), "5 attribute search (bucket + KIND) found the server's session");
            Check(clientText.Contains("SEARCH_ID=Success id=" + code) && clientText.Contains("match=True"), "5 session id search found " + code);
            var mine = Lines(clientText).First(l => l.Contains("SEARCH_RESULT id=" + code));
            Check(mine.Contains("name=QA 서버") && mine.Contains("max=12") && mine.Contains("haspw=True"), "5 the found session has the name, players and password attributes: " + mine);
            Check(clientText.Contains("LOGIN_STATUS=LoggedIn"), "4 the client is still logged in at its end");
            Debug.Log("[EosTestQA] P2P to the same PUID (record only): " + string.Join(" | ", Lines(clientText).Where(l => l.Contains("P2P") || l.Contains("PING_SUMMARY") || l.Contains("PING 0")))
                + " | server saw a connection request: " + ReadShared(serverLog).Contains("P2P_REQUEST"));

            // the server's alive check runs every 30 s: one that started after the client finished shows whether the server is still logged in
            int alive = Lines(ReadShared(serverLog)).Count(l => l.Contains("ALIVE"));
            var until = DateTime.UtcNow.AddSeconds(45);
            while (Lines(ReadShared(serverLog)).Count(l => l.Contains("ALIVE")) <= alive && DateTime.UtcNow < until && !server.HasExited) Thread.Sleep(500);
            string serverText = ReadShared(serverLog);
            Debug.Log("[EosTestQA] server log:\n" + serverText);
            string last = Lines(serverText).LastOrDefault(l => l.Contains("ALIVE")) ?? "(none)";
            var changes = Lines(serverText).Where(l => l.Contains("LOGIN_STATUS_CHANGED") && !l.EndsWith("NotLoggedIn -> LoggedIn")).ToArray();
            Check(!server.HasExited && last.Contains("status=LoggedIn search=Success") && changes.Length == 0,
                "4 the server is still logged in after the client logged in and out with the same PUID: " + last + (changes.Length > 0 ? " CHANGES: " + string.Join(" | ", changes) : ""));
        }
        finally
        {
            if (client != null && !client.HasExited) client.Kill(); // only the processes this QA started
            if (server != null && !server.HasExited) server.Kill();
        }

        string zip = Bundle(dir);
        Debug.Log("[EosTestQA] RESULT process passed, checks=" + checks + " (logs: " + qa + ", bundle: " + zip + " " + new FileInfo(zip).Length / (1024 * 1024) + "MB)");
    }

    /// <summary>Scripts and README for the teammate (from EosTestBundle~ next to this file), then Builds/EosTest into Builds/EosTest.zip.
    /// The bat files run eos_test.ps1, which keeps the game process in a kill-on-close job: closing the window ends the server or client.</summary>
    static string Bundle(string dir)
    {
        CopyTemplates(dir, "eos_test.bat", "eos_test_server.bat", "eos_test.ps1", "README_팀원.txt");
        return Zip(dir, "EosTest", _ => false);
    }

    static string Templates => Path.Combine(Application.dataPath, "Scripts/Editor/Net/EosTestBundle~");

    /// <summary>Copies scripts with the line ends their OS needs (git may have changed them); .ps1 gets a BOM so Windows PowerShell 5.1 reads Korean.</summary>
    static void CopyTemplates(string dir, params string[] names)
    {
        foreach (string name in names)
        {
            string text = File.ReadAllText(Path.Combine(Templates, name)).Replace("\r\n", "\n");
            if (!name.EndsWith(".command")) text = text.Replace("\n", "\r\n");
            File.WriteAllText(Path.Combine(dir, name), text, new UTF8Encoding(name.EndsWith(".ps1")));
        }
    }

    /// <summary>Everything shippable in <paramref name="dir"/> into Builds/&lt;name&gt;.zip under the folder &lt;name&gt;/.
    /// Files for which <paramref name="executable"/> holds get Unix mode 0755 (others 0644) and the zip is marked as made on Unix, so macOS keeps the bits.</summary>
    static string Zip(string dir, string name, Func<string, bool> executable)
    {
        string zip = Path.Combine(Path.GetDirectoryName(dir), name + ".zip");
        File.Delete(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(dir.Length + 1).Replace('\\', '/');
                if (rel.Contains("DoNotShip") || rel.EndsWith("_log.txt")) continue; // Burst debug info, logs of a local run
                var entry = archive.CreateEntryFromFile(file, name + "/" + rel, System.IO.Compression.CompressionLevel.Optimal);
                entry.ExternalAttributes = (executable(rel) ? 0x81ED : 0x81A4) << 16; // S_IFREG | 0755 or 0644
            }

        // "version made by" host = 3 (Unix) in every central directory header, or unzip tools ignore the mode bits
        byte[] bytes = File.ReadAllBytes(zip);
        int end = bytes.Length - 22;
        while (end >= 0 && BitConverter.ToUInt32(bytes, end) != 0x06054b50) end--;
        int count = BitConverter.ToUInt16(bytes, end + 10), at = (int)BitConverter.ToUInt32(bytes, end + 16);
        for (int i = 0; i < count; i++)
        {
            if (BitConverter.ToUInt32(bytes, at) != 0x02014b50) throw new Exception("[EosTestQA] unexpected zip layout in " + zip);
            bytes[at + 5] = 3;
            at += 46 + BitConverter.ToUInt16(bytes, at + 28) + BitConverter.ToUInt16(bytes, at + 30) + BitConverter.ToUInt16(bytes, at + 32);
        }
        File.WriteAllBytes(zip, bytes);
        return zip;
    }

    // ================= macOS bundle (built here, run on a Mac) =================

    /// <summary>Builds Builds/EosTestMac/CompanyGame.app (Mono, Intel + Apple Silicon), checks that the EOS library and config are inside,
    /// adds the .command scripts and README, and zips Builds/EosTestMac.zip with the executable bits set (with -quit).</summary>
    public static void RunMac()
    {
        checks = 0;
        WriteConfig();
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/EosTestMac"));
        if (Directory.Exists(dir)) Directory.Delete(dir, true); // the plugin notes that building over an old .app misbehaves
        Directory.CreateDirectory(dir);
        string app = Path.Combine(dir, "CompanyGame.app");
        string scene = Array.Find(EditorBuildSettings.scenes, s => s.enabled).path;
        var activeTarget = EditorUserBuildSettings.activeBuildTarget;
        // by reflection: the type exists only with Mac Build Support, and this file must compile without it
        var archProperty = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions")?.GetProperty("architecture");
        if (archProperty == null) throw new Exception("[EosTestQA] FAIL: Mac Build Support is not installed");
        object arch = archProperty.GetValue(null);
        try
        {
            archProperty.SetValue(null, UnityEditor.Build.OSArchitecture.x64ARM64); // every EOS dylib in the plugin is universal
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene }, locationPathName = app, target = BuildTarget.StandaloneOSX, options = BuildOptions.None,
            });
            Check(report.summary.result == BuildResult.Succeeded, "macOS player built to " + app + " (" + report.summary.result + ", arch was " + arch + ")");
        }
        finally
        {
            archProperty.SetValue(null, arch);
            if (EditorUserBuildSettings.activeBuildTarget != activeTarget)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(activeTarget), activeTarget);
        }

        string[] files = Directory.GetFiles(app, "*", SearchOption.AllDirectories);
        string Find(string file) => files.FirstOrDefault(f => Path.GetFileName(f) == file);
        string exe = Path.Combine(app, "Contents/MacOS/CompanyGame");
        Check(File.Exists(exe) && Universal(exe), "the app executable is Contents/MacOS/CompanyGame, universal (x86_64 + arm64)");
        // only the EOS SDK: a macOS player binds it with DllImport; the plugin's libDynamicLibraryLoaderHelper is for editor-style dynamic loading
        string eos = Find("libEOSSDK-Mac-Shipping.dylib");
        Check(eos != null && Universal(eos), "libEOSSDK-Mac-Shipping.dylib is in the app, universal: " + eos?.Substring(app.Length + 1));
        foreach (string config in new[] { "eos_product_config.json", "eos_macos_config.json" })
            Check(Find(config) != null, config + " is in the app: " + Find(config)?.Substring(app.Length + 1));

        CopyTemplates(dir, "eos_test.command", "eos_test_server.command", "README_맥.txt");
        string zip = Zip(dir, "EosTestMac", rel => rel.EndsWith(".command") || rel.StartsWith("CompanyGame.app/Contents/MacOS/"));
        Debug.Log("[EosTestQA] RESULT mac passed, checks=" + checks + " (bundle: " + zip + " " + new FileInfo(zip).Length / (1024 * 1024) + "MB)");
    }

    /// <summary>A Mach-O fat binary holding both x86_64 and arm64.</summary>
    static bool Universal(string path)
    {
        byte[] head = new byte[64];
        using (var stream = File.OpenRead(path)) stream.Read(head, 0, head.Length);
        uint Big(int at) => (uint)(head[at] << 24 | head[at + 1] << 16 | head[at + 2] << 8 | head[at + 3]);
        if (Big(0) != 0xCAFEBABE) return false;
        var cpus = Enumerable.Range(0, (int)Big(4)).Select(i => Big(8 + i * 20)).ToArray();
        return cpus.Contains(0x01000007u) && cpus.Contains(0x0100000Cu);
    }

    // ================= helpers =================

    static Process Start(string exe, string args)
    {
        Debug.Log("[EosTestQA] start " + Path.GetFileName(exe) + " " + args);
        return Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
    }

    /// <summary>The value after <paramref name="key"/> on the first log line that has it; throws on FAILED, exit or timeout.</summary>
    static string WaitLine(string log, string key, Process process, int seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            foreach (string line in Lines(ReadShared(log)))
            {
                if (line.Contains("FAILED")) throw new Exception("[EosTestQA] FAIL: " + line + " (" + log + ")");
                int at = line.IndexOf(key, StringComparison.Ordinal);
                if (at >= 0) return line.Substring(at + key.Length).Split(' ')[0];
            }
            if (process.HasExited) throw new Exception("[EosTestQA] FAIL: process exited (" + process.ExitCode + ") before " + key + ", see " + log);
            if (DateTime.UtcNow > until) throw new Exception("[EosTestQA] FAIL: TIMEOUT waiting for " + key + " in " + log);
            Thread.Sleep(500);
        }
    }

    static string ReadShared(string path)
    {
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }
        catch (IOException) { return ""; }
    }

    static string[] Lines(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Reports show only the first 4 characters of a PUID.</summary>
    static string Short(string puid) => string.IsNullOrEmpty(puid) ? "(none)" : puid.Substring(0, Math.Min(4, puid.Length)) + "…";

    static async Task<T> Within<T>(Task<T> task, float seconds, string what)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) != task) throw new TimeoutException(what + " did not finish in " + seconds + "s");
        return task.Result;
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("[EosTestQA] FAIL: " + what);
        checks++;
        Debug.Log("[EosTestQA] ok: " + what);
    }
}
