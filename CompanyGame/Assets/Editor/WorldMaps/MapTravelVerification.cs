using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.Daldongne;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>
    /// Explicit editor integration check. It enters Play Mode, loads both maps,
    /// exits Play Mode and restores the original clean editor scene setup.
    /// SessionState carries the check across normal domain reloads.
    /// </summary>
    [InitializeOnLoad]
    public static class MapTravelVerification
    {
        const string SessionKey = "CompanyGame.WorldMaps.TravelVerification";
        const string ResultPath = "Temp/WorldMapTravelVerification.json";
        const string Village = "Assets/Scenes/daldongnaemap.unity";
        const string Garden = "Assets/Scenes/Maps/DaldongnePocketGarden.unity";
        enum Phase { EnteringPlay, Starting, WaitingGarden, WaitingVillage, RestoringEditor, Finished }

        [Serializable]
        sealed class SavedScene
        {
            public string path;
            public bool isLoaded;
            public bool isActive;
        }

        [Serializable]
        sealed class Run
        {
            public bool success;
            public bool completed;
            public bool editorScenesRestored;
            public string status;
            public string error;
            public List<string> checks = new List<string>();
            public SavedScene[] originalScenes;
            public Phase phase;
            public double deadline;
            public int transitionFrame;
        }

        static MapTravelVerification()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }

        [MenuItem("Tools/Company Game/Maps/Verify Map Travel (Play Mode)")]
        static void StartFromMenu() { Debug.Log(Start()); }

        /// <summary>Starts only on explicit request; does not save or discard dirty scenes.</summary>
        public static string Start()
        {
            var previous = Read();
            if (previous != null && !previous.completed) return Status();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Finish the current Play Mode/import/compile before verifying map travel.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            if (setup.Length == 0 || setup.Any(item => string.IsNullOrEmpty(item.path)))
                throw new InvalidOperationException("Save all open scenes before verifying map travel.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save all open scenes before verifying map travel.");
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(Village), "The main village scene is missing.");
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(Garden), "The pocket garden scene is missing.");
            Require(EditorBuildSettings.scenes.Any(item => item.enabled && item.path == Village), "Enable the village scene in the build scene list.");
            Require(EditorBuildSettings.scenes.Any(item => item.enabled && item.path == Garden), "Enable the pocket garden scene in the build scene list.");

            var run = new Run
            {
                originalScenes = setup.Select(item => new SavedScene
                    { path = item.path, isLoaded = item.isLoaded, isActive = item.isActive }).ToArray(),
                phase = Phase.EnteringPlay,
                status = "Entering Play Mode",
                deadline = EditorApplication.timeSinceStartup + 180
            };
            Save(run);
            try
            {
                EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Fail(run, exception); }
            return Status();
        }

        /// <summary>Returns current/final machine-readable status without starting a run.</summary>
        public static string Status()
        {
            var run = Read();
            if (run != null) return JsonUtility.ToJson(run, true);
            return File.Exists(ResultPath) ? File.ReadAllText(ResultPath) : "Map travel verification has not run.";
        }

        static Run Read()
        {
            string json = SessionState.GetString(SessionKey, string.Empty);
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Run>(json);
        }

        static void Save(Run run) { SessionState.SetString(SessionKey, JsonUtility.ToJson(run)); }

        static void Tick()
        {
            var run = Read();
            if (run == null || run.completed || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                if (run.phase == Phase.RestoringEditor)
                {
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) RestoreEditor(run);
                    return;
                }
                Require(EditorApplication.timeSinceStartup < run.deadline, "Timed out during: " + run.status);
                if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
                switch (run.phase)
                {
                    case Phase.EnteringPlay:
                        // At least two player-loop frames allow Awake/Start to finish.
                        run.phase = Phase.Starting;
                        run.transitionFrame = Time.frameCount;
                        Save(run);
                        break;
                    case Phase.Starting:
                        if (Time.frameCount <= run.transitionFrame + 2) return;
                        StartTravel(run);
                        break;
                    case Phase.WaitingGarden:
                        if (!DestinationReady(Garden, run)) return;
                        VerifyArrival(run, Garden, "default");
                        VerifyOverview(run, "garden");
                        var gardenWalker = GetPlayer(SceneManager.GetActiveScene());
                        gardenWalker.SetWalking(true);
                        Require(SceneLoadManager.TryLoadMap(Village, "station", gardenWalker), "The return transition was rejected.");
                        Next(run, Phase.WaitingVillage, "Waiting for return to village");
                        break;
                    case Phase.WaitingVillage:
                        if (!DestinationReady(Village, run)) return;
                        VerifyArrival(run, Village, "station");
                        VerifyOverview(run, "village");
                        run.success = true;
                        run.status = "Checks passed; restoring editor scenes";
                        run.phase = Phase.RestoringEditor;
                        Save(run);
                        EditorApplication.isPlaying = false;
                        break;
                }
            }
            catch (Exception exception) { Fail(run, exception); }
        }

        static void StartTravel(Run run)
        {
            var scene = SceneManager.GetActiveScene();
            Require(scene.path == Village, "Play Mode did not start in the main village scene.");
            var player = GetPlayer(scene);
            VerifySingleSceneObjects(scene);
            var appearance = player.GetComponent<DaldongnePlayerAppearance>();
            Require(appearance, "The map player is missing DaldongnePlayerAppearance.");
            appearance.Select(DaldongnePlayerAppearance.Variant.Male);
            player.SetWalking(true);
            Require(player.walking, "Walking mode could not be enabled.");

            // This intentionally emits one explanatory runtime error. It must
            // return false and leave the valid current scene/player untouched.
            Require(!SceneLoadManager.TryLoadMap("Assets/Scenes/__MissingVerificationScene.unity", "default", player),
                "A scene absent from the build list was accepted.");
            Require(!SceneLoadManager.IsLoading && SceneManager.GetActiveScene().path == Village && player.isActiveAndEnabled,
                "Rejected destination changed the source scene/player.");
            Require(!string.IsNullOrEmpty(SceneLoadManager.LastError), "The invalid scene request did not report its error.");
            run.checks.Add("Invalid scene rejected without leaving the village");

            Require(SceneLoadManager.TryLoadMap(Garden, "default", player), "The garden transition was rejected.");
            Require(!SceneLoadManager.TryLoadMap(Garden, "default", player), "A second transition was accepted while loading.");
            run.checks.Add("Duplicate transition blocked");
            Next(run, Phase.WaitingGarden, "Waiting for pocket garden");
        }

        static bool DestinationReady(string path, Run run)
        {
            if (SceneLoadManager.IsLoading || Time.frameCount <= run.transitionFrame + 2) return false;
            Require(string.IsNullOrEmpty(SceneLoadManager.LastError), "Transition error: " + SceneLoadManager.LastError);
            Require(SceneManager.GetActiveScene().path == path, "The transition ended in an unexpected scene.");
            return true;
        }

        static void VerifyArrival(Run run, string scenePath, string spawnId)
        {
            var scene = SceneManager.GetActiveScene();
            Require(scene.path == scenePath, "Unexpected arrival scene.");
            VerifySingleSceneObjects(scene);
            var player = GetPlayer(scene);
            Require(player.walking, "Walking mode was not retained at arrival.");
            Require(player.viewCamera && player.viewCamera.gameObject.scene == scene, "The player camera is not scene-local.");
            Require(player.overview && !player.overview.enabled, "The overview camera is still processing input while walking.");
            var appearance = player.GetComponent<DaldongnePlayerAppearance>();
            Require(appearance && appearance.selected == DaldongnePlayerAppearance.Variant.Male, "The selected male avatar was not retained.");
            Require(MapSpawnPoint.TryFind(scene, spawnId, out var spawn, out var error), error);
            Near(player.spawn, spawn.transform.position, "Arrival/reset spawn was not updated.");
            player.ResetToSpawn();
            Near(player.transform.position, spawn.transform.position, "ResetToSpawn did not use the destination spawn.");
            Require(player.GetComponent<CharacterController>().enabled, "ResetToSpawn left the CharacterController disabled.");
            run.checks.Add(scene.name + ": single scene/player/camera, male avatar, walking, spawn and reset preserved (" + spawnId + ")");
        }

        static void VerifyOverview(Run run, string mapName)
        {
            var player = GetPlayer(SceneManager.GetActiveScene());
            var overview = player.overview;
            Require(overview, "The map player is missing its overview controller.");
            overview.focus += Vector3.one * 5f;
            overview.zoom += 3f;
            overview.yaw += 12f;
            overview.pitch += 9f;
            player.SetWalking(false);
            Require(!player.walking && overview.enabled, "Leaving walking mode did not restore overview control.");
            Near(overview.focus, overview.homeFocus, "Overview focus did not return to this map's home.");
            Near(overview.zoom, overview.homeZoom, "Overview zoom did not return home.");
            Near(overview.yaw, overview.homeYaw, "Overview yaw did not return home.");
            Near(overview.pitch, overview.homePitch, "Overview pitch did not return home.");
            Near(player.viewCamera.orthographicSize, overview.homeZoom, "Camera projection did not return to the map overview.");
            run.checks.Add(mapName + ": leaving walking restores its own home overview");
        }

        static void VerifySingleSceneObjects(Scene scene)
        {
            Require(SceneManager.sceneCount == 1, "More than one map scene is loaded.");
            var players = Object.FindObjectsByType<PlayerMovement>()
                .Where(item => item.isActiveAndEnabled).ToArray();
            Require(players.Length == 1 && players[0].gameObject.scene == scene, "Expected exactly one active scene-local player.");
            var cameras = Object.FindObjectsByType<Camera>()
                .Where(item => item.isActiveAndEnabled).ToArray();
            Require(cameras.Length == 1 && cameras[0].gameObject.scene == scene, "Expected exactly one active scene-local camera.");
        }

        static PlayerMovement GetPlayer(Scene scene)
        {
            Require(SceneLoadManager.TryGetScenePlayer(scene, out var player, out var error), error);
            return player;
        }

        static void Next(Run run, Phase phase, string status)
        {
            run.phase = phase;
            run.status = status;
            run.transitionFrame = Time.frameCount;
            run.deadline = EditorApplication.timeSinceStartup + 180;
            Save(run);
        }

        static void PlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode) return;
            var run = Read();
            if (run == null || run.completed || run.phase == Phase.RestoringEditor) return;
            run.success = false;
            run.error = "Play Mode was stopped before verification completed.";
            run.status = "Stopped; restoring editor scenes";
            run.phase = Phase.RestoringEditor;
            Save(run);
        }

        static void Fail(Run run, Exception exception)
        {
            run.success = false;
            run.error = exception.Message;
            run.status = "Failed; restoring editor scenes";
            run.phase = Phase.RestoringEditor;
            Save(run);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        }

        static void RestoreEditor(Run run)
        {
            try
            {
                EditorSceneManager.RestoreSceneManagerSetup(run.originalScenes.Select(item => new SceneSetup
                    { path = item.path, isLoaded = item.isLoaded, isActive = item.isActive }).ToArray());
                run.editorScenesRestored = true;
            }
            catch (Exception exception)
            {
                run.success = false;
                run.error = (run.error ?? string.Empty) + " Editor restore: " + exception.Message;
            }
            run.completed = true;
            run.phase = Phase.Finished;
            run.status = run.success ? "Passed" : "Failed";
            Save(run);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(ResultPath, JsonUtility.ToJson(run, true));
            Debug.Log("Map travel verification " + run.status + ". Results: " + ResultPath);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void Near(Vector3 actual, Vector3 expected, string message)
        {
            Require((actual - expected).sqrMagnitude < .0001f, message + " Actual=" + actual + " Expected=" + expected);
        }

        static void Near(float actual, float expected, string message)
        {
            Require(Mathf.Abs(actual - expected) < .001f, message + " Actual=" + actual + " Expected=" + expected);
        }
    }
}
