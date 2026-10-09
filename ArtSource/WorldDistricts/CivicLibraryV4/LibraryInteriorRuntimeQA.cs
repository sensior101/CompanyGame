// Clone-only runtime acceptance test. Never install this in the delivered game.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.CivicLibrary
{
    public static class LibraryInteriorRuntimeQA
    {
        const string Exterior = "Assets/Scenes/Maps/CivicDistrict.unity";
        const string Interior = "Assets/Scenes/Interiors/CivicLibraryInterior.unity";
        const string Output = "LibraryInteriorRuntimeQA";
        [Serializable] public class Check { public string name, detail; public bool pass; }
        [Serializable] public class Report
        {
            public bool pass;
            public string error, persistentDataPath;
            public List<Check> checks = new List<Check>();
        }
        static Report report;
        static IEnumerator routine;
        static double deadline;
        static int previousFrame;
        static Keyboard keyboard;
        static PlayerMovement actor;
        static int actorId;

        public static void Run()
        {
            if (!Application.dataPath.Replace('\\', '/').EndsWith("/Temp/CivicLibraryV4Validation/Assets"))
                throw new InvalidOperationException("Runtime QA requires the isolated validation project.");
            if (!Application.persistentDataPath.Contains("CivicLibraryIsolatedQA"))
                throw new InvalidOperationException("Set the CLONE productName to CivicLibraryIsolatedQA before launching this process.");
            Directory.CreateDirectory(Output);
            report = new Report { persistentDataPath = Application.persistentDataPath };
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSceneManager.OpenScene(Exterior);
            deadline = EditorApplication.timeSinceStartup + 180;
            routine = Exercise();
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish("Runtime QA timed out"); return; }
            if (!EditorApplication.isPlaying || Time.frameCount == previousFrame) return;
            previousFrame = Time.frameCount;
            try { if (!routine.MoveNext()) Finish(null); }
            catch (Exception e) { Finish(e.ToString()); }
        }
        static IEnumerator Exercise()
        {
            for (int i = 0; i < 50; i++) yield return null;
            actor = PlayerSpawner.Player;
            Require("single persistent player startup", actor && Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None).Length == 1,
                actor ? actor.gameObject.scene.name : "missing");
            actorId = actor.GetInstanceID();
            InventoryManager.Instance.State.SelectHotbar(7);
            keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent(); InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView; InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually; actor.gameObject.AddComponent<LibraryQAInputDriver>();
            var benchSeat = Object.FindObjectsByType<Seat>(FindObjectsSortMode.None)
                .Where(s => s.owner && s.owner.name.IndexOf("Bench", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => s.transform.position.x).First();
            var approach = benchSeat.transform.position + benchSeat.transform.forward * 1.05f;
            approach.y = .07f;
            Teleport(approach);
            for (int i = 0; i < 8; i++) yield return null;
            Vector3 savedApproach = actor.transform.position;
            Require("bench proximity prompt", SeatInteraction.HasNearbySeat && Seat.FindNearest(actor.transform) == benchSeat,
                "SeatInteraction.HasNearbySeat=" + SeatInteraction.HasNearbySeat);
            SendKeys(Key.Space);
            yield return null; yield return null;
            SendKeys();
            for (int i = 0; i < 5; i++) yield return null;
            var seated = actor.GetComponent<PlayerSeating>();
            Require("Space seats on exterior bench", seated.IsSeated && seated.Current == benchSeat,
                "seated=" + seated.IsSeated);
            var avatar = actor.GetComponentsInChildren<CompanyGame.Daldongne.DaldongneAvatarMotion>(true).FirstOrDefault();
            Require("seated avatar pose", avatar && Mathf.Abs(Mathf.DeltaAngle(avatar.leftLeg.localEulerAngles.x, -90)) < 10, avatar ? avatar.leftLeg.localEulerAngles.ToString() : "no avatar motion");
            Capture("Bench_Seated");
            SendKeys(Key.Space);
            yield return null; yield return null;
            SendKeys();
            for (int i = 0; i < 5; i++) yield return null;
            Require("Space restores exact bench approach", !seated.IsSeated && Vector3.Distance(actor.transform.position, savedApproach) < .15f,
                "distance=" + Vector3.Distance(actor.transform.position, savedApproach));

            var occupied = new GameObject("QA occupied seat actor");
            occupied.transform.position = benchSeat.transform.position;
            benchSeat.assignedOccupant = occupied.transform;
            for (int i = 0; i < 3; i++) yield return null;
            Require("occupied bench prompt hidden", !SeatInteraction.HasNearbySeat && !Seat.FindNearest(actor.transform), "occupied seat has no interaction");
            benchSeat.assignedOccupant = null;
            Object.Destroy(occupied);

            var door = Object.FindObjectsByType<StoreInteractionPoint>(FindObjectsSortMode.None).Single(p => p.targetScenePath == Interior);
            var entry = door.transform.position + Vector3.back * .9f;
            entry.y = .81f;
            Teleport(entry);
            for (int i = 0; i < 6; i++) yield return null;
            SendKeys(Key.Space);
            yield return null; yield return null;
            SendKeys();
            int timeout = 0;
            while ((SceneLoadManager.CurrentMap.path != Interior || SceneLoadManager.IsLoading) && timeout++ < 900) yield return null;
            Require("Space enters separate library scene", SceneLoadManager.CurrentMap.path == Interior && !SceneLoadManager.IsLoading,
                SceneLoadManager.CurrentMap.path + " " + SceneLoadManager.LastError);
            Require("same player survives entry", PlayerSpawner.Player.GetInstanceID() == actorId && Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None).Length == 1,
                "player instance=" + PlayerSpawner.Player.GetInstanceID());
            for (int i = 0; i < 30; i++) yield return null;
            Capture("Interior_Arrival");
            Require("arrival remains above floor", actor.transform.position.y > -.1f && actor.transform.position.y < 1f, actor.transform.position.ToString());

            var cushion = Object.FindObjectsByType<Seat>(FindObjectsSortMode.None)
                .Where(s => s.owner && s.owner.name.Contains("Cushion"))
                .OrderBy(s => s.transform.position.y).FirstOrDefault();
            Require("reading cushions expose separate seats", cushion, cushion ? cushion.owner.name : "missing");
            var cushionApproach = cushion.transform.position + cushion.transform.forward * .85f;
            if (Physics.Raycast(cushionApproach + Vector3.up, Vector3.down, out var ground, 3)) cushionApproach.y = ground.point.y + .04f;
            Teleport(cushionApproach);
            for (int i = 0; i < 8; i++) yield return null;
            var found = Seat.FindNearest(actor.transform);
            Require("reading cushion reachable", found, found ? found.owner.name : "none");
            savedApproach = actor.transform.position;
            SendKeys(Key.Space); yield return null; yield return null; SendKeys();
            for (int i = 0; i < 5; i++) yield return null;
            Require("Space seats on reading cushion", seated.IsSeated, seated.IsSeated.ToString());
            Capture("ReadingCushion_Seated");
            SendKeys(Key.Space); yield return null; yield return null; SendKeys();
            for (int i = 0; i < 5; i++) yield return null;
            Require("reading cushion Space escape", !seated.IsSeated && Vector3.Distance(actor.transform.position, savedApproach) < .2f, actor.transform.position.ToString());

            var exit = Object.FindObjectsByType<StoreInteractionPoint>(FindObjectsSortMode.None).Single(p => p.targetScenePath == Exterior);
            Teleport(exit.transform.position + Vector3.forward * .55f);
            for (int i = 0; i < 8; i++) yield return null;
            SendKeys(Key.Space); yield return null; yield return null; SendKeys();
            timeout = 0;
            while ((SceneLoadManager.CurrentMap.path != Exterior || SceneLoadManager.IsLoading) && timeout++ < 900) yield return null;
            Require("Space returns to CivicDistrict", SceneLoadManager.CurrentMap.path == Exterior && !SceneLoadManager.IsLoading, SceneLoadManager.LastError);
            for (int i = 0; i < 15; i++) yield return null;
            Require("same player survives return", PlayerSpawner.Player.GetInstanceID() == actorId && Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None).Length == 1, "persistent instance");
            var spawn = Object.FindObjectsByType<MapSpawnPoint>(FindObjectsSortMode.None).Single(s => s.spawnId == "library_exit");
            Require("exit uses library_exit instead of unrelated respawn", Vector3.Distance(actor.transform.position, spawn.transform.position) < .2f,
                actor.transform.position + " target=" + spawn.transform.position);
            Capture("Exterior_Return");
        }
        static void Teleport(Vector3 p) { actor.spawn = p; actor.ResetToSpawn(); Physics.SyncTransforms(); }
        static Key[] pendingKeys;
        static void SendKeys(params Key[] keys) { pendingKeys = keys; }
        public static void BeforeInput() { if (pendingKeys != null) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(pendingKeys)); pendingKeys = null; } InputSystem.Update(); }
        static void Require(string name, bool pass, string detail)
        {
            report.checks.Add(new Check { name = name, pass = pass, detail = detail });
            if (!pass) throw new InvalidOperationException(name + ": " + detail);
        }
        static void Capture(string name)
        {
            var camera = Camera.main;
            var rt = RenderTexture.GetTemporary(1440, 900, 24);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            camera.targetTexture = rt;
            camera.Render(); camera.Render(); camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(1440, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), texture.EncodeToPNG());
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            RenderTexture.ReleaseTemporary(rt); Object.Destroy(texture);
        }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; InputSystem.onBeforeUpdate -= BeforeInput;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            report.error = error; report.pass = error == null && report.checks.All(c => c.pass);
            File.WriteAllText(Path.Combine(Output, "RuntimeReport.json"), JsonUtility.ToJson(report, true));
            Debug.Log("LIBRARY_RUNTIME_QA " + (report.pass ? "PASS" : "FAIL") + " " + error);
            EditorApplication.Exit(report.pass ? 0 : 1);
        }
    }
}

namespace CompanyGame.Editor.CivicLibrary { [DefaultExecutionOrder(-10000)] public sealed class LibraryQAInputDriver : MonoBehaviour { void Update() { LibraryInteriorRuntimeQA.BeforeInput(); } } }