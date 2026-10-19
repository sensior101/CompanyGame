using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 12.14 completion check: two clients at the same world time see the same cars and citizens at the same places.
/// RunProcess builds the game with the QA probe (COMPANYGAME_NPCQA) into Builds/NpcSyncQA, opens a server process, joins
/// it from the editor (client A), starts the build as client B (it joins later: a late joiner) and, on each map of
/// NpcQaProbe.Maps, records both for 15 s. Compared: the plan positions at the same grid times (to the millimetre), and
/// what each client draws at the same world time (cars within 1.0 m, citizens within 0.3 m; ones held back by someone
/// in front are left out). Then a car must stop for the editor's player standing in its way and catch up afterwards.
/// Screenshots (QA_SHOTS) of the remote client's cars and citizens.
/// RunSignalShots (offline, BusinessDistrict played directly): at a junction where a car waits, S2_walk_cars_waiting.png
/// in the pedestrians' green, then S2_yellow.png and S2_allred.png of the same approach; the lights are checked too.
/// </summary>
[InitializeOnLoad]
public static class NpcSyncQA
{
    const string ModeKey = "NpcSyncQA.mode", ExeKey = "NpcSyncQA.exe";
    const int Slot = 4;
    const float RecordSeconds = 15f, CarTolerance = 1f, CitizenTolerance = .3f;

    static int checks, failures;
    static readonly List<string> lines = new List<string>();
    static Process server, clientB;

    static NpcSyncQA() => EditorApplication.playModeStateChanged += OnPlayState;

    public static void RunProcess()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/NpcSyncQA"));
        string exe = Path.Combine(dir, "NpcSyncQA.exe");
        if (Environment.GetEnvironmentVariable("NPCQA_NOBUILD") == "1" && File.Exists(exe)) { Play(exe); return; } // reruns of the QA part only
        var scenes = Array.ConvertAll(Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes, locationPathName = exe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
            extraScriptingDefines = new[] { "COMPANYGAME_NPCQA" },
        });
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("[NpcSyncQA] RESULT FAILED: build " + report.summary.result);
            EditorApplication.Exit(1);
            return;
        }
        Play(exe);
    }

    static void Play(string exe)
    {
        SessionState.SetString(ExeKey, exe);
        SessionState.SetString(ModeKey, "process");
        EditorSceneManager.OpenScene(TitleSceneBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    public static void RunSignalShots()
    {
        SessionState.SetString(ModeKey, "signals");
        EditorSceneManager.OpenScene(TrafficMapBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayState(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode || SessionState.GetString(ModeKey, "") == "") return;
        string mode = SessionState.GetString(ModeKey, "");
        SessionState.SetString(ModeKey, "");
        if (mode == "signals") SignalShots(); else Drive(SessionState.GetString(ExeKey, ""));
    }

    // ---- signal phases on screen (offline) ----

    static async void SignalShots()
    {
        int code = 0;
        try
        {
            TrafficSpawner spawner = null;
            await NpcQaProbe.Until(() => (spawner = UnityEngine.Object.FindAnyObjectByType<TrafficSpawner>()) && spawner.Plan != null, 60, "the traffic");
            TrafficCar waiting = null;
            TrafficSignal signal = null;
            float end = Time.realtimeSinceStartup + 300f;
            while (!waiting && Time.realtimeSinceStartup < end)
            {
                int i = 0;
                foreach (TrafficCar car in spawner.Cars)
                {
                    spawner.Plan.Sample(i++, NpcClock.Now, out float speed, out int lane, out float at);
                    TrafficSignal light = spawner.Plan.Lane(lane).signal;
                    if (light && speed == 0f && spawner.Plan.StopLine(lane) - at < 6f && light.WalkAt(NpcClock.Now, out float inWalk, out _) && inWalk > .5f && inWalk < 2f)
                    { waiting = car; signal = light; break; }
                }
                await Awaitable.NextFrameAsync();
            }
            Check(waiting, "a car waits at a stop line while the pedestrians have green");
            if (!waiting) return;
            var camera = new GameObject("QA signal camera").AddComponent<Camera>();
            camera.fieldOfView = 55f;
            // From the far end of the nearest crossing of that junction: its walk box faces this way, the waiting car beside it.
            PedestrianSignal box = null;
            float nearest = float.MaxValue;
            foreach (var p in UnityEngine.Object.FindObjectsByType<PedestrianSignal>(FindObjectsSortMode.None))
            {
                if (!p.walk || !p.walk.signal || (p.walk.signal.transform.position - signal.transform.position).sqrMagnitude > 1f) continue;
                float d = (p.transform.position - waiting.Position).sqrMagnitude;
                if (d < nearest) { nearest = d; box = p; }
            }
            Check(box, "a walk box of that junction");
            Vector3 face = box.transform.forward;
            float across = Vector3.Distance(box.walk.endA.position, box.walk.endB.position);
            camera.transform.position = box.transform.position + face * (across + 5f) + Vector3.up * 6f;
            camera.transform.LookAt(Vector3.Lerp(box.transform.position + Vector3.up * 2.5f, waiting.Position, .4f));
            await Snap(camera, "S2_walk_cars_waiting.png", signal, "walk",
                () => signal.WalkAt(NpcClock.Now, out _, out _) && signal.Current == TrafficSignal.Phase.Red && PedestrianSignal.StateOf(box.walk) == 1);
            Check(PedestrianSignal.StateOf(box.walk) == 1, "walk: the walk box shows the steady green figure");
            // The yellow and the all red from an approach of that signal: in front of its lamps, where the drivers are.
            Transform lamps = signal.heads[0].yellow.transform;
            Vector3 drivers = lamps.parent.forward;
            camera.transform.position = lamps.position + drivers * 16f - Vector3.up * 2.5f + Vector3.Cross(Vector3.up, drivers) * 2f;
            camera.transform.LookAt(lamps.position - Vector3.up * 3f - drivers * 4f);
            await NpcQaProbe.Until(() => signal.Current == TrafficSignal.Phase.Yellow && signal.TimeInPhase > 1f, 90, "the yellow");
            await Snap(camera, "S2_yellow.png", signal, "yellow", () => signal.Current == TrafficSignal.Phase.Yellow);
            await NpcQaProbe.Until(() => signal.Current == TrafficSignal.Phase.Red && !signal.WalkAt(NpcClock.Now, out _, out _), 10, "the all red");
            await Snap(camera, "S2_allred.png", signal, "all red",
                () => signal.Current == TrafficSignal.Phase.Red && !signal.WalkAt(NpcClock.Now, out _, out _));
            Log("RESULT signals passed, checks=" + checks + (failures > 0 ? " FAILED " + failures : ""), failures > 0);
            if (failures > 0) code = 1;
        }
        catch (Exception e) { Log("RESULT signals FAILED: " + e.Message, true); code = 1; }
        EditorApplication.Exit(code);
    }

    // The lamps really show the phase (the emission of the head facing this approach), then the picture.
    static async Task Snap(Camera camera, string file, TrafficSignal signal, string phase, Func<bool> still)
    {
        await Awaitable.WaitForSecondsAsync(.2f);
        var head = signal.heads[0];
        var block = new MaterialPropertyBlock();
        float Lit(Renderer r) { r.GetPropertyBlock(block); return block.GetColor("_EmissionColor").maxColorComponent; }
        bool redOn = Lit(head.red) > 1f, yellowOn = Lit(head.yellow) > 1f, greenOn = Lit(head.green) > 1f;
        bool lamps = phase == "yellow" ? yellowOn && !redOn && !greenOn : redOn && !yellowOn && !greenOn;
        Check(still() && lamps, $"{phase}: the car lamps show it (red {redOn}, yellow {yellowOn}, green {greenOn})");
        string dir = Environment.GetEnvironmentVariable("QA_SHOTS");
        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); await TitleShots.Snap(camera, Path.Combine(dir, file)); }
    }

    static async void Drive(string exe)
    {
        int original = PlayerPrefs.GetInt("Profile.Slot", 1), code = 0;
        string folder = Path.Combine(Path.GetDirectoryName(exe), "qa");
        try
        {
            await Body(exe, folder);
            if (failures > 0) throw new Exception(failures + " checks failed");
            Log("RESULT process passed, checks=" + checks);
        }
        catch (Exception e) { Log("RESULT process FAILED: " + e.Message + "\n" + e.StackTrace, true); code = 1; }
        try
        {
            if (SyncClient.Instance) SyncClient.Instance.Disconnect();
            if (clientB != null && !clientB.HasExited) clientB.Kill();
            if (server != null && !server.HasExited)
            {
                File.WriteAllText(Path.Combine(folder, "server", LocalServerProcess.StopRequestName), "");
                if (!server.WaitForExit(15000)) server.Kill();
            }
        }
        catch (Exception e) { Debug.LogWarning("[NpcSyncQA] cleanup: " + e.Message); }
        PlayerPrefs.SetInt("Profile.Slot", original);
        PlayerPrefs.Save();
        EditorApplication.Exit(code);
    }

    static async Task Body(string exe, string folder)
    {
        await NpcQaProbe.Until(() => TitleScreen.Current != null && !TitleScreen.Current.Busy, 30, "the title");
        ProfileSlot.Set(Slot);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        string serverFolder = Path.Combine(folder, "server");
        Directory.CreateDirectory(serverFolder);
        int port = FreePort();
        var settings = new ServerSettings { name = "NpcSyncQA", maxPlayers = 4, port = port, world = WorldStore.NewId() };
        string settingsFile = Path.Combine(serverFolder, "settings.json");
        File.WriteAllText(settingsFile, JsonUtility.ToJson(settings));
        server = Process.Start(new ProcessStartInfo(exe,
            $"-batchmode -nographics -gameserver {port} -maxplayers 4 -bind 127.0.0.1 -serversettings \"{settingsFile}\" -logFile \"{Path.Combine(folder, "server.log")}\"")
        { UseShellExecute = false, CreateNoWindow = true });
        await NpcQaProbe.Until(() => File.Exists(Path.Combine(serverFolder, "running.json")) || server.HasExited, 60, "the server process");
        Check(!server.HasExited, "the server process runs");

        // Client A: the editor.
        Check(await TitleFlow.JoinServer("127.0.0.1:" + port, "", false) == null, "client A (the editor) joins");
        Check(await TitleFlow.CreateServerCharacter("QA첫째", "male") == null && TitleFlow.EnterServerWorld() == null, "client A's character enters");
        await NpcQaProbe.Until(() => PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading && NpcClock.Synced, 90, "client A in the world");
        uint seed = ServerWorld.SeedOf(settings.world);
        Check(NpcClock.Seed == seed && GameSession.Mode == SessionMode.Client, "client A takes the server world's seed " + seed + " from the clock section (" + NpcClock.Seed + ")");
        await Awaitable.WaitForSecondsAsync(5f); // B comes later: a late joiner

        // Client B: the build, its own profile slot (its own account).
        var start = new ProcessStartInfo(exe, $"-batchmode -nographics -profile 3 -npcqa 127.0.0.1:{port} \"{folder}\" -logFile \"{Path.Combine(folder, "b.log")}\"")
        { UseShellExecute = false, CreateNoWindow = true };
        start.EnvironmentVariables["COMPANYGAME_NOEOS"] = "1";
        clientB = Process.Start(start);
        string status = Path.Combine(folder, "b.status");
        string last = "";
        string Status()
        {
            try { if (File.Exists(status)) last = File.ReadAllText(status); }
            catch (IOException) { } // B is writing it: the last read stands
            return last;
        }
        using (var w = new StreamWriter(Path.Combine(folder, "a.txt")))
            for (int m = 0; m < NpcQaProbe.Maps.Length; m++)
            {
                await NpcQaProbe.Travel(NpcQaProbe.Maps[m]);
                await NpcQaProbe.Until(() => Status().Contains("ready " + m) || Status().Contains("failed") || clientB.HasExited, 240, "client B on " + NpcQaProbe.Maps[m]);
                Check(Status().Contains("ready " + m), "client B is on " + NpcQaProbe.Maps[m] + " (" + Status().Replace("\n", "; ") + ")");
                var rec = new NpcQaProbe.Recorder(w, m);
                float end = Time.realtimeSinceStartup + RecordSeconds;
                while (Time.realtimeSinceStartup < end) { rec.Frame(); await Awaitable.NextFrameAsync(); }
                File.WriteAllText(Path.Combine(folder, "stop_" + m), "");
                bool traffic = UnityEngine.Object.FindAnyObjectByType<TrafficSpawner>();
                if (traffic) await NpcQa.ShotNearest(true, "N_remote_traffic.png");
                await NpcQa.ShotNearest(false, m == 0 ? "N_remote_citizens_business.png" : "N_remote_citizens.png");
                if (traffic) await CarStops();
            }
        await NpcQaProbe.Until(() => Status().Contains("done") || Status().Contains("failed") || clientB.HasExited, 60, "client B to finish");
        Check(Status().Contains("done"), "client B recorded every map (" + Status().Replace("\n", "; ") + ")");
        await NpcQaProbe.Until(() => clientB.HasExited, 30, "client B to quit");
        var a = Parse(Path.Combine(folder, "a.txt"));
        var b = Parse(Path.Combine(folder, "b.txt"));
        for (int m = 0; m < NpcQaProbe.Maps.Length; m++) Compare(NpcQaProbe.Maps[m], a[m], b[m]);
    }

    // ---- a car and the player in its way (this PC's drawing) ----

    static async Task CarStops()
    {
        var spawner = UnityEngine.Object.FindAnyObjectByType<TrafficSpawner>();
        var player = PlayerSpawner.Player;
        TrafficCar car = null;
        float end = Time.realtimeSinceStartup + 30f;
        while (car == null && Time.realtimeSinceStartup < end)
        {
            foreach (var c in spawner.Cars) if (c.Speed > 4f && c.Delay == 0) { car = c; break; }
            await Awaitable.NextFrameAsync();
        }
        Check(car, "a moving car to stand in front of");
        if (!car) return;
        var motor = player.GetComponent<CharacterController>();
        Vector3 home = player.transform.position, spot = car.Position + car.Forward * 9f;
        motor.enabled = false;
        player.transform.position = spot;
        motor.enabled = true;
        Physics.SyncTransforms();
        for (int i = 0; i < 6; i++)
        {
            await Awaitable.WaitForSecondsAsync(.5f);
            Log($"NOTE car {car.key} at {car.Position}, delay {car.Delay:0.00}, speed {car.Speed:0.0}; player at {player.transform.position}");
        }
        float gap = Vector3.Distance(Flat(car.Position), Flat(spot));
        Check(car.Delay > 1 && gap > 1.5f, $"the car stopped for the player in its way (held {car.Delay:0.0} s, {gap:0.0} m away)");
        await NpcQa.ShotNearest(true, "N_car_waits.png");
        motor.enabled = false;
        player.transform.position = home; // out of the way again
        motor.enabled = true;
        Physics.SyncTransforms();
        double held = car.Delay;
        float until = Time.realtimeSinceStartup + 90f;
        while (car.Delay > 0 && Time.realtimeSinceStartup < until) await Awaitable.NextFrameAsync();
        Check(car.Delay == 0, $"afterwards it caught up with the plan (held {held:0.0} s, back on the plan in {90f - (until - Time.realtimeSinceStartup):0} s)");
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ---- comparing ----

    sealed class Frame { public double t; public readonly Dictionary<int, (Vector3 p, double d)> cars = new Dictionary<int, (Vector3, double)>(), people = new Dictionary<int, (Vector3, double)>(); }
    sealed class Run { public readonly List<Frame> frames = new List<Frame>(); public readonly Dictionary<double, Dictionary<string, Vector3>> grid = new Dictionary<double, Dictionary<string, Vector3>>(); }

    static Dictionary<int, Run> Parse(string file)
    {
        var runs = new Dictionary<int, Run>();
        Run run = null;
        Frame frame = null;
        Dictionary<string, Vector3> grid = null;
        foreach (string line in File.ReadLines(file))
        {
            string[] f = line.Split(' ');
            double N(int i) => double.Parse(f[i], CultureInfo.InvariantCulture);
            Vector3 P(int i) => new Vector3((float)N(i), (float)N(i + 1), (float)N(i + 2));
            switch (f[0])
            {
                case "m": runs[int.Parse(f[1])] = run = new Run(); break;
                case "f": run.frames.Add(frame = new Frame { t = N(1) }); break;
                case "c": frame.cars[int.Parse(f[1])] = (P(2), N(5)); break;
                case "p": frame.people[int.Parse(f[1])] = (P(2), N(5)); break;
                case "g": run.grid[N(1)] = grid = new Dictionary<string, Vector3>(); break;
                case "gc": case "gp": grid[f[0] + f[1]] = P(2); break;
            }
        }
        return runs;
    }

    static void Compare(string map, Run a, Run b)
    {
        string name = Path.GetFileNameWithoutExtension(map);
        int gridTimes = 0, gridItems = 0;
        float gridWorst = 0f;
        foreach (var pair in a.grid)
        {
            if (!b.grid.TryGetValue(pair.Key, out var other)) continue;
            gridTimes++;
            foreach (var item in pair.Value)
                if (other.TryGetValue(item.Key, out Vector3 q)) { gridItems++; gridWorst = Mathf.Max(gridWorst, Vector3.Distance(item.Value, q)); }
        }
        Check(gridTimes >= 3 && gridItems > 0, $"{name}: both clients wrote the plan at the same world times ({gridTimes} times, {gridItems} positions)");
        Check(gridWorst < .001f, $"{name}: the plans agree at the same world time (worst {gridWorst * 1000f:0.000} mm)");

        int carSamples = 0, peopleSamples = 0, heldOut = 0, carMoving = 0, peopleMoving = 0;
        float carWorst = 0f, peopleWorst = 0f;
        double carSum = 0, peopleSum = 0;
        int j = 0;
        foreach (Frame fa in a.frames)
        {
            while (j + 1 < b.frames.Count && b.frames[j + 1].t <= fa.t) j++;
            if (j + 1 >= b.frames.Count || b.frames[j].t > fa.t) continue;
            Frame b0 = b.frames[j], b1 = b.frames[j + 1];
            if (b1.t - b0.t > .2) continue;
            float k = (float)((fa.t - b0.t) / (b1.t - b0.t));
            Measure(fa.cars, b0.cars, b1.cars, k, ref carSamples, ref carMoving, ref carWorst, ref carSum, ref heldOut);
            Measure(fa.people, b0.people, b1.people, k, ref peopleSamples, ref peopleMoving, ref peopleWorst, ref peopleSum, ref heldOut);
        }
        bool hasCars = a.frames.Exists(f => f.cars.Count > 0);
        if (hasCars) Check(carSamples > 200 && carMoving > 100 && carWorst < CarTolerance, $"{name}: the cars drawn on A and B at the same world time are within {CarTolerance} m ({carSamples} samples, {carMoving} of them moving, worst {carWorst:0.000} m, mean {carSum / Math.Max(1, carSamples):0.0000} m)");
        Check(peopleSamples > 200 && peopleMoving > 50 && peopleWorst < CitizenTolerance, $"{name}: the citizens drawn on A and B at the same world time are within {CitizenTolerance} m ({peopleSamples} samples, {peopleMoving} of them walking, worst {peopleWorst:0.000} m, mean {peopleSum / Math.Max(1, peopleSamples):0.0000} m)");
        Log($"NOTE {name}: {heldOut} samples left out (held back by someone in front on A or B)");
    }

    static void Measure(Dictionary<int, (Vector3 p, double d)> a, Dictionary<int, (Vector3 p, double d)> b0, Dictionary<int, (Vector3 p, double d)> b1, float k,
        ref int samples, ref int moving, ref float worst, ref double sum, ref int heldOut)
    {
        foreach (var item in a)
        {
            if (!b0.TryGetValue(item.Key, out var x) || !b1.TryGetValue(item.Key, out var y)) continue;
            if (item.Value.d > 0 || x.d > 0 || y.d > 0) { heldOut++; continue; }
            if ((x.p - y.p).sqrMagnitude > 4f) continue; // a jump between B's frames (a seat): no fair midpoint
            float e = Vector3.Distance(item.Value.p, Vector3.Lerp(x.p, y.p, k));
            samples++; sum += e; worst = Mathf.Max(worst, e);
            if ((x.p - y.p).sqrMagnitude > 1e-6f) moving++;
        }
    }

    // ---- helpers ----

    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) failures++;
        Log((ok ? "ok   " : "FAIL ") + what, !ok);
    }

    static void Log(string text, bool error = false)
    {
        lines.Add(text);
        if (error) Debug.LogError("[NpcSyncQA] " + text); else Debug.Log("[NpcSyncQA] " + text);
    }
}
