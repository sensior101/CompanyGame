using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>Disposable native input/visual checks. No test items or targets are saved into scenes.</summary>
[InitializeOnLoad]
public static class PlayerHandsRuntimeQA
{
    const string KeyName = "CompanyGame.PlayerHandsRuntimeQA";
    const string Output = "../ArtSource/Inventory/QA/";
    [Serializable] public sealed class Report
    {
        public int stage, frames, lastFrame;
        public bool done, passed, restoring, background, maximized;
        public string scene, error = "";
        public double deadline;
        public float zoom;
        public Rect gameViewRect;
        public List<string> checks = new List<string>();
    }
    static Keyboard keyboard;
    static Mouse mouse;
    static Damageable target;
    static GameObject wall;
    static EditorWindow gameView;
    static PlayerHandsRuntimeQA() { EditorApplication.update += Tick; }
    static void Store(Report r) => SessionState.SetString(KeyName, JsonUtility.ToJson(r));
    public static string Status() => SessionState.GetString(KeyName, "No hand test");
    public static string Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Save scene and stop Play before disposable tests.");
        gameView = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
        var r = new Report { scene = SceneManager.GetActiveScene().path, background = PlayerSettings.runInBackground,
            maximized = gameView.maximized, gameViewRect = gameView.position, deadline = EditorApplication.timeSinceStartup + 240 };
        gameView.maximized = false;
        gameView.position = new Rect(gameView.position.x, gameView.position.y, 1280, 742);
        Store(r); Application.runInBackground = true; EditorApplication.EnterPlaymode();
        return "Hand/combat/scroll-deposit Play checks started";
    }
    static void Check(Report r, bool pass, string text)
    { if (!pass) throw new InvalidOperationException(text); r.checks.Add(text); }
    static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    static void Pointer(bool left = false, float scroll = 0)
    {
        var state = new MouseState { position = new Vector2(Screen.width * .5f, Screen.height * .5f), scroll = new Vector2(0, scroll) };
        if (left) state = state.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(mouse, state);
    }
    static void Capture(string name)
    {
        Directory.CreateDirectory(Output);
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Output + name + ".png", image.EncodeToPNG()); UnityEngine.Object.Destroy(image);
    }
    static void Aim(PlayerMovement p)
    {
        var cam = p.viewCamera;
        cam.GetComponent<PlayerCameraController>().enabled = false;
        cam.transform.position = p.transform.position + new Vector3(0, 1.3f, -.35f);
        cam.transform.rotation = Quaternion.identity;
        Physics.SyncTransforms();
    }
    static void Tick()
    {
        var json = SessionState.GetString(KeyName, ""); if (string.IsNullOrEmpty(json)) return;
        var r = JsonUtility.FromJson<Report>(json); if (r.done) return;
        try
        {
            if (r.restoring)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                EditorSceneManager.OpenScene(r.scene);
                if (!gameView) gameView = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
                gameView.maximized = false;
                gameView.position = r.gameViewRect;
                gameView.maximized = r.maximized;
                PlayerSettings.runInBackground = r.background; Application.runInBackground = r.background;
                r.done = true; Store(r); Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "player-hands-runtime.json", JsonUtility.ToJson(r, true)); return;
            }
            if (EditorApplication.timeSinceStartup > r.deadline) throw new TimeoutException("Hand tests timed out");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || SceneLoadManager.IsLoading || r.lastFrame == Time.frameCount) return;
            r.lastFrame = Time.frameCount; r.frames++; Store(r);
            if (r.frames < (r.stage == 0 || r.stage == 22 ? 40 : 15)) return;
            var p = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>(); if (!p) return;
            var inv = p.GetComponent<PlayerInventory>(); var held = p.GetComponent<PlayerHeldItem>(); var combat = p.GetComponent<PlayerCombat>();
            var camera = p.viewCamera.GetComponent<PlayerCameraController>();
            switch (r.stage)
            {
                case 0:
                    Check(r, held && combat, "Held item and combat components attach automatically");
                    keyboard = InputSystem.AddDevice<Keyboard>("Hands QA Keyboard"); keyboard.MakeCurrent();
                    mouse = InputSystem.AddDevice<Mouse>("Hands QA Mouse"); mouse.MakeCurrent();
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Disposable test floor";
                    floor.transform.position = new Vector3(0, 40, 0); floor.transform.localScale = new Vector3(24, .2f, 24);
                    p.spawn = new Vector3(0, 40.11f, 0); p.ResetToSpawn();
                    BankManager.Instance.SetMoney(100000);
                    Check(r, CashService.TryWithdraw(inv.Inventory, 1800, 5, out _), "Prepare five selected coins");
                    inv.SelectHotbar(0); camera.firstPerson = false; r.zoom = camera.distance; Pointer(); break;
                case 1:
                    Check(r, held.HeldItem && held.HeldItem.CurrencyValue == 1800 && held.WorldItemRoot && held.WorldItemRoot.gameObject.activeInHierarchy &&
                        Vector3.Distance(held.WorldItemRoot.position, held.WorldGripPosition) < .1f, "Third person selected currency sits at right hand");
                    Capture("held-currency-third-person"); Keys(Key.Tab); break;
                case 2:
                    Check(r, camera.firstPerson && held.IsFirstPersonVisible && held.FirstPersonArmRoot && held.FirstPersonItemRoot,
                        "Tab shows actual right arm and selected item in first person");
                    Check(r, held.FirstPersonArmRoot.GetComponentsInChildren<Renderer>().Any(x => x.enabled) &&
                        !held.WorldItemRoot.gameObject.activeInHierarchy, "First person arm renders without duplicate world-held item");
                    Capture("held-currency-first-person"); Keys(); break;
                case 3:
                    Keys(Key.Tab); r.stage = 25; r.frames = 0; Store(r); return;
                case 25:
                    Check(r, !camera.firstPerson, "Return to third person before checking deposit wheel zoom suppression");
                    Keys(Key.Space); r.stage = 4; r.frames = 0; Store(r); return;
                case 4:
                    Check(r, inv.Inventory.GetSlot(0).Count == 4 && CashService.BankBalance == 92800, "One Space press deposits exactly one coin; holding does not repeat");
                    Pointer(scroll: -240); break;
                case 5:
                    Check(r, inv.Inventory.GetSlot(0).Count == 2 && CashService.BankBalance == 96400, "Held Space plus two downward wheel notches deposits two coins");
                    Pointer(scroll: 120); break;
                case 6:
                    Check(r, inv.Inventory.GetSlot(0).Count == 2 && CashService.BankBalance == 96400, "Upward wheel does not deposit coins");
                    Pointer(scroll: -360); break;
                case 7:
                    Check(r, inv.Inventory.GetSlot(0).IsEmpty && CashService.BankBalance == 100000 && !held.HeldItem && !held.FirstPersonItemRoot,
                        "Scroll stops at empty stack and currency disappears from hand");
                    Check(r, Mathf.Abs(camera.distance - r.zoom) < .001f, "Deposit wheel does not change camera zoom");
                    Keys(); Pointer(); inv.OpenInventory();
                    CashService.TryWithdraw(inv.Inventory, 1000, 1, out _);
                    Check(r, WorldDroppedItem.TryDropStorage(inv, 0, out _), "Prepare a reachable dropped item"); inv.CloseInventory(); break;
                case 8: Pointer(true); break;
                case 9:
                    Check(r, WorldDroppedItem.SessionDropCount == 1 && inv.Inventory.GetSlot(0).IsEmpty, "Left click attacks without picking up a world item");
                    Pointer(); Keys(Key.F); break;
                case 10:
                    Check(r, WorldDroppedItem.SessionDropCount == 0 && inv.Inventory.GetSlot(0).Item.CurrencyValue == 1000 && held.HeldItem,
                        "F picks up nearby item and selected hand visual updates");
                    Keys(Key.Space); break;
                case 11:
                    Keys(Key.Tab);
                    var gun = ScriptableObject.CreateInstance<ItemData>(); gun.name = gun.displayName = "Temporary QA pistol";
                    gun.category = ItemCategory.Weapon; gun.weaponKind = WeaponKind.Firearm; gun.weaponDamage = 25; gun.attacksPerSecond = 20;
                    inv.Inventory.TryAdd(gun, 1, out _); inv.SelectHotbar(0);
                    var dummy = GameObject.CreatePrimitive(PrimitiveType.Cube); dummy.name = "Disposable damage target";
                    dummy.transform.position = p.transform.position + new Vector3(0, 1.3f, 5); target = dummy.AddComponent<Damageable>();
                    break;
                case 12:
                    Check(r, camera.firstPerson && held.IsFirstPersonVisible && held.HeldItem.IsWeapon &&
                        held.FirstPersonItemRoot.GetComponentsInChildren<Renderer>().Length >= 7, "Selected firearm model appears in right hand in first person");
                    Capture("held-firearm-first-person"); Aim(p); Pointer(true); break;
                case 13:
                    Check(r, combat.LastAttackWasShot && combat.LastAttackDealtDamage && target.CurrentHealth == 75, "Left click firearm deals one hit for configured damage");
                    Pointer();
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Disposable bullet blocker";
                    wall.transform.position = p.transform.position + new Vector3(0, 1.3f, 2.5f); wall.transform.localScale = new Vector3(2, 2, .3f);
                    Physics.SyncTransforms(); break;
                case 14: Pointer(true); break;
                case 15:
                    Check(r, combat.HasLastHit && combat.LastHit.collider.gameObject == wall && !combat.LastAttackDealtDamage && target.CurrentHealth == 75,
                        "Wall blocks shots before damage target");
                    Pointer(); UnityEngine.Object.Destroy(wall); inv.Inventory.TryRemove(0, 1, out _);
                    target.transform.position = p.transform.position + new Vector3(0, 1.3f, 1.2f); combat.punchesPerSecond = 20; Aim(p); break;
                case 16: Pointer(true); break;
                case 17:
                    Check(r, !combat.LastAttackWasShot && combat.LastAttackDealtDamage && target.CurrentHealth == 65, "Empty-hand left click punches nearby target");
                    Pointer(); inv.OpenInventory(); break;
                case 18: Pointer(true); break;
                case 19:
                    Check(r, target.CurrentHealth == 65 && !combat.CanAttack && !held.IsFirstPersonVisible, "Inventory UI suppresses attack and first person hand overlay");
                    Pointer(); inv.CloseInventory(); break;
                case 20:
                    CashService.TryWithdraw(inv.Inventory, 10000, 1, out _);
                    SceneManager.LoadScene("Assets/Scenes/Maps/CivicDistrict.unity");
                    r.stage = 22; r.frames = 0; Store(r); return;
                case 22:
                    Check(r, inv && held && combat && held.HeldItem && held.HeldItem.CurrencyValue == 10000,
                        "Map change restores selected held item and combat automatically");
                    r.passed = true; Finish(r); return;
            }
            r.stage++; r.frames = 0; Store(r);
        }
        catch (Exception ex) { r.error = ex.ToString(); Finish(r); }
    }
    static void Finish(Report r)
    {
        r.restoring = true; Store(r);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
