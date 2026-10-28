using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// NpcSyncQA's second client (Docs/Multiplayer_Redesign.md 12.14). Compiled only in the editor and in a QA build (define
/// COMPANYGAME_NPCQA), so a normal build never has it. With <c>-npcqa host:port folder</c> the built game joins that
/// server as a new character and, for each of <see cref="Maps"/>, goes there, writes "ready i" into folder/b.status and
/// records what it draws into folder/b.txt until folder/stop_i exists; "done" at the end, then it quits. The editor client
/// records a.txt with the same <see cref="Recorder"/>, so the two can be compared at the same world times.
/// </summary>
public static class NpcQaProbe
{
    public static readonly string[] Maps = { "Assets/Scenes/Maps/BusinessDistrict.unity", "Assets/Scenes/daldongnaemap.unity" };
    /// <summary>Plan positions are also written at every multiple of this many world seconds (exact comparison).</summary>
    public const double GridSeconds = 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void FromCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-npcqa");
        if (i >= 0 && i + 2 < args.Length) RunClient(args[i + 1], args[i + 2]);
    }

    static async void RunClient(string address, string folder)
    {
        string status = Path.Combine(folder, "b.status");
        int code = 0;
        try
        {
            await Until(() => TitleScreen.Current != null && !TitleScreen.Current.Busy, 60, "the title");
            Expect(await TitleFlow.JoinServer(address, "", false), "joining " + address);
            Expect(await TitleFlow.CreateServerCharacter("QA둘째", "female"), "the character");
            Expect(TitleFlow.EnterServerWorld(), "entering the world");
            using (var w = new StreamWriter(Path.Combine(folder, "b.txt")))
                for (int m = 0; m < Maps.Length; m++)
                {
                    await Travel(Maps[m]);
                    File.AppendAllText(status, "ready " + m + "\n");
                    var rec = new Recorder(w, m);
                    float end = Time.realtimeSinceStartup + 240f;
                    while (!File.Exists(Path.Combine(folder, "stop_" + m)) && Time.realtimeSinceStartup < end) { rec.Frame(); await Awaitable.NextFrameAsync(); }
                }
            File.AppendAllText(status, "done\n");
        }
        catch (Exception e)
        {
            Debug.LogError("[NpcQaProbe] " + e);
            File.AppendAllText(status, "failed " + e.Message + "\n");
            code = 1;
        }
        Application.Quit(code);
    }

    static void Expect(string error, string what) { if (error != null) throw new Exception(what + ": " + error); }

    public static async Task Until(Func<bool> done, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!done())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("TIMEOUT waiting for " + what);
            await Awaitable.NextFrameAsync();
        }
    }

    /// <summary>Goes to the map (if not there) and waits until its citizens and traffic are drawn from this server's seed.</summary>
    public static async Task Travel(string map)
    {
        await Until(() => !SceneLoadManager.IsLoading && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled, 120, "the map entered first");
        if (SceneManager.GetActiveScene().path != map && !SceneLoadManager.TryLoadMap(map, SceneLoadManager.DefaultSpawnId))
            throw new Exception("cannot travel to " + map + ": " + SceneLoadManager.LastError);
        await Until(() => SceneManager.GetActiveScene().path == map && PlayerSpawner.Player && PlayerSpawner.Player.isActiveAndEnabled && !SceneLoadManager.IsLoading, 120, map);
        await Until(() => NpcClock.Synced && NpcClock.Seed != 0 && CitizenSpawner.Current && CitizenSpawner.Current.Citizens.Count > 0
                          && (!UnityEngine.Object.FindAnyObjectByType<TrafficSpawner>() || UnityEngine.Object.FindAnyObjectByType<TrafficSpawner>().Plan != null), 30, "the crowd and traffic of " + map);
        await Awaitable.WaitForSecondsAsync(1f);
    }

    /// <summary>
    /// Writes "m map", then every 0.05 s "f clock" and a line per car ("c i x y z delay") and citizen ("p ..."), and at every
    /// grid time passed "g time" with the plan's own positions ("gc i x y z", "gp i x y z").
    /// </summary>
    public sealed class Recorder
    {
        readonly StreamWriter w;
        readonly TrafficSpawner traffic;
        double lastGrid = double.NaN;
        float nextAt;

        public Recorder(StreamWriter writer, int map)
        {
            w = writer;
            traffic = UnityEngine.Object.FindAnyObjectByType<TrafficSpawner>();
            w.WriteLine("m " + map);
        }

        public void Frame()
        {
            if (Time.realtimeSinceStartup < nextAt) return;
            nextAt = Time.realtimeSinceStartup + .05f;
            double now = NpcClock.Now;
            w.WriteLine("f " + D(now));
            int i = 0;
            if (traffic) foreach (TrafficCar car in traffic.Cars) w.WriteLine("c " + i++ + " " + V(car.transform.position) + " " + D(car.Delay));
            var citizens = CitizenSpawner.Current ? CitizenSpawner.Current.Citizens : null;
            if (citizens != null) for (i = 0; i < citizens.Count; i++) if (citizens[i]) w.WriteLine("p " + i + " " + V(citizens[i].transform.position) + " " + D(citizens[i].Delay));
            double grid = Math.Floor(now / GridSeconds) * GridSeconds;
            if (!double.IsNaN(lastGrid) && grid > lastGrid)
            {
                w.WriteLine("g " + D(grid));
                if (traffic && traffic.Plan != null)
                    for (i = 0; i < traffic.Plan.Count; i++) w.WriteLine("gc " + i + " " + V(traffic.Plan.Sample(i, grid, out _, out _, out _)));
                if (citizens != null) for (i = 0; i < citizens.Count; i++) if (citizens[i]) w.WriteLine("gp " + i + " " + V(citizens[i].PlanPosition(grid)));
            }
            lastGrid = grid;
        }

        static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => v.x.ToString("R", CultureInfo.InvariantCulture) + " " + v.y.ToString("R", CultureInfo.InvariantCulture) + " " + v.z.ToString("R", CultureInfo.InvariantCulture);
    }
}
