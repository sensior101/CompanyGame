using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompanyGame.Editor.Citizens
{
    /// <summary>
    /// Batch-mode Play Mode smoke test (no player, no network): for each scene, enter Play Mode, fire the spawner's MapReady hook,
    /// let the crowd run for a few seconds and report who spawned, who sits, who moved, and any error logs.
    /// Run: -executeMethod CompanyGame.Editor.Citizens.CitizenPlaySmoke.Run (no -quit; it exits by itself).
    /// </summary>
    [InitializeOnLoad]
    public static class CitizenPlaySmoke
    {
        const string Active = "CitizenSmoke.active", Index = "CitizenSmoke.index", Errors = "CitizenSmoke.errors", Fails = "CitizenSmoke.fails";
        const float RunSeconds = 8f;
        static readonly string[] Scenes =
        {
            "Assets/Scenes/Interiors/CivicLibraryInterior.unity",
            "Assets/Scenes/Interiors/HandaeHQLobby.unity",
            "Assets/Scenes/Maps/CivicDistrict.unity",
            "Assets/Scenes/Maps/BusinessDistrict.unity",
        };
        static float started;
        static Vector3[] before;
        static bool fired;

        static CitizenPlaySmoke()
        {
            if (!SessionState.GetBool(Active, false)) return;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            SessionState.SetBool(Active, true);
            SessionState.SetInt(Index, 0);
            SessionState.SetInt(Fails, 0);
            SessionState.SetString(Errors, "");
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            SessionState.SetString(Errors, SessionState.GetString(Errors, "") + "\n" + message.Split('\n')[0]);
        }

        static void Tick()
        {
            int index = SessionState.GetInt(Index, 0);
            if (index >= Scenes.Length) { Finish(); return; }
            if (!EditorApplication.isPlaying)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scenes[index], UnityEditor.SceneManagement.OpenSceneMode.Single);
                fired = false; started = 0f;
                EditorApplication.isPlaying = true;
                return;
            }
            if (!fired) { Fire(); return; }
            if (Time.realtimeSinceStartup - started < RunSeconds) return;
            Report(index);
            SessionState.SetInt(Index, index + 1);
            EditorApplication.isPlaying = false;
        }

        static void Fire()
        {
            if (Time.frameCount < 5) return;
            var scene = SceneManager.GetActiveScene();
            typeof(CitizenSpawner).GetMethod("OnMapReady", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { scene, "default", false });
            fired = true;
            started = Time.realtimeSinceStartup;
            before = null;
        }

        static void Report(int index)
        {
            var spawner = CitizenSpawner.Current;
            string tag = "[Smoke] " + Scenes[index] + ": ";
            if (!spawner) { Fail(tag + "no CitizenSpawner"); return; }
            var citizens = spawner.Citizens.Where(c => c).ToList();
            int seated = citizens.Count(c => c.Seated);
            int onMesh = citizens.Count(c => c.Seated || c.GetComponent<UnityEngine.AI.NavMeshAgent>().isOnNavMesh);
            Debug.Log(tag + citizens.Count + " citizens, " + seated + " seated, " + onMesh + " on the NavMesh, books " + citizens.Count(c => c.transform.Find("CitizenBook(Clone)")));
            foreach (var c in citizens)
                Debug.Log(tag + c.name + " " + c.transform.position.ToString("F1") + (c.Seated ? " seated" : "") + " dialogue='" + (c.GetComponent<DialogueData>() ? c.GetComponent<DialogueData>().line : "-") + "'");
            if (citizens.Count == 0 || onMesh != citizens.Count) Fail(tag + "citizens missing or off the NavMesh");
        }

        static void Fail(string message) { Debug.LogError(message); SessionState.SetInt(Fails, SessionState.GetInt(Fails, 0) + 1); }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            string errors = SessionState.GetString(Errors, "");
            int fails = SessionState.GetInt(Fails, 0);
            Debug.Log("[Smoke] done, report failures " + fails + ", error/exception logs:" + (errors.Length == 0 ? " none" : errors));
            SessionState.SetBool(Active, false);
            EditorApplication.Exit(fails == 0 && errors.Length == 0 ? 0 : 1);
        }
    }
}
