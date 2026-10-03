using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.Editor.WorldMaps;
using CompanyGame.World.Maps;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>Disposable Play-mode input, UI, money conservation and map round-trip checks.</summary>
[InitializeOnLoad]
public static class InventoryV2RuntimeQA
{
    const string KeyName = "CompanyGame.InventoryV2RuntimeQA";
    const string Output = "../ArtSource/Inventory/QA/";
    [Serializable] public sealed class Report
    {
        public int stage, frames, lastFrame;
        public bool done, passed, restoring, background;
        public string originalScene, error = "", dropId;
        public double deadline;
        public List<string> checks = new List<string>();
    }
    static Keyboard keyboard;
    static InventoryState inventory;
    static Quaternion cameraRotation;
    static Vector3 cameraPosition;
    static float originalYaw;
    static readonly EquipmentSlot[] Equipment = { EquipmentSlot.Top, EquipmentSlot.Bottom, EquipmentSlot.Socks, EquipmentSlot.Shoes, EquipmentSlot.Pet };
    static InventoryV2RuntimeQA() { EditorApplication.update += Tick; }
    static void Store(Report report) => SessionState.SetString(KeyName, JsonUtility.ToJson(report));
    public static string Status() => SessionState.GetString(KeyName, "No inventory V2 test");
    public static string Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Save scene and stop Play first.");
        var report = new Report { originalScene = SceneManager.GetActiveScene().path, background = PlayerSettings.runInBackground,
            deadline = EditorApplication.timeSinceStartup + 300 };
        EditorSceneManager.OpenScene(TerracedVillageExpansion.ScenePath);
        Store(report); Application.runInBackground = true; EditorApplication.EnterPlaymode();
        return "Inventory V2 Play checks started";
    }
    static void Check(Report report, bool pass, string description)
    { if (!pass) throw new InvalidOperationException(description); report.checks.Add(description); }
    static void Press(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    static void Capture(string name)
    {
        // Capture the completed game frame before this test step changes any UI.
        Directory.CreateDirectory(Output);
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.GetFullPath(Output + name + ".png"), texture.EncodeToPNG());
        UnityEngine.Object.Destroy(texture);
    }
    static InventorySlotPointer Storage(InventoryUI ui, int index) => ui.GetComponentsInChildren<InventorySlotPointer>()
        .First(p => !p.IsEquipment && !p.IsDropZone && p.InventoryIndex == index && p.name.StartsWith("Storage"));
    static InventorySlotPointer Gear(InventoryUI ui, EquipmentSlot slot) => ui.GetComponentsInChildren<InventorySlotPointer>()
        .Single(p => p.IsEquipment && p.Equipment == slot);
    static Vector2 Centre(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    static void Drag(InventorySlotPointer from, InventorySlotPointer to)
    {
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = Centre((RectTransform)from.transform), pointerDrag = from.gameObject };
        ExecuteEvents.Execute(from.gameObject, data, ExecuteEvents.beginDragHandler);
        data.position = Centre((RectTransform)to.transform);
        ExecuteEvents.Execute(from.gameObject, data, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(to.gameObject, data, ExecuteEvents.dropHandler);
        ExecuteEvents.Execute(from.gameObject, data, ExecuteEvents.endDragHandler);
    }
    static void CheckSquares(Report report, InventoryUI ui)
    {
        Canvas.ForceUpdateCanvases();
        var slots = ui.GetComponentsInChildren<InventorySlotPointer>().Where(p => !p.IsDropZone).ToArray();
        foreach (var slot in slots)
        {
            var rect = ((RectTransform)slot.transform).rect;
            Check(report, rect.width > 0 && Mathf.Abs(rect.width - rect.height) < .5f, "Square slot " + slot.name);
        }
    }
    static void WithdrawFields(InventoryUI ui, string value, string quantity)
    {
        var fields = ui.GetComponentsInChildren<TMP_InputField>(true);
        if (fields.Length != 2) throw new InvalidOperationException("Expected two withdrawal inputs.");
        fields[0].text = value; fields[1].text = quantity;
        fields[0].Select(); fields[0].ActivateInputField();
    }
    static void CheckWithdrawalLayout(Report report, InventoryUI ui)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var input in new[] { ui.WithdrawalAmountInput, ui.WithdrawalQuantityInput })
        {
            input.ForceLabelUpdate(); input.textComponent.ForceMeshUpdate();
            var text = input.textComponent;
            Check(report, text.textInfo.characterCount >= input.text.Length &&
                text.textInfo.characterInfo.Take(input.text.Length).All(c => c.isVisible), "Withdrawal input digits render: " + input.name);
        }
        var bank = (RectTransform)ui.transform.Find("InventoryModal/InventoryWindow/BankBalanceButton");
        var popup = (RectTransform)ui.transform.Find("InventoryModal/WithdrawalModal/WithdrawalWindow");
        var bankCorners = new Vector3[4]; var popupCorners = new Vector3[4];
        bank.GetWorldCorners(bankCorners); popup.GetWorldCorners(popupCorners);
        Check(report, popupCorners[1].y < bankCorners[0].y && bankCorners[0].y - popupCorners[1].y < 24f &&
            popupCorners[0].x >= 0 && popupCorners[2].x <= Screen.width && popupCorners[0].y >= 0,
            "Withdrawal popup fits on screen immediately below bank balance");
        Check(report, !popup.Find("WithdrawalTotal") && !popup.Find("CurrencyHint") &&
            popup.GetComponentsInChildren<TMP_InputField>().Length == 2, "Only face value and quantity inputs; no total or currency hint");
    }
    static void Tick()
    {
        string json = SessionState.GetString(KeyName, ""); if (string.IsNullOrEmpty(json)) return;
        var report = JsonUtility.FromJson<Report>(json); if (report.done) return;
        try
        {
            if (report.restoring)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                EditorSceneManager.OpenScene(report.originalScene);
                PlayerSettings.runInBackground = report.background; Application.runInBackground = report.background;
                report.done = true; Store(report); Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "inventory-v2-runtime.json", JsonUtility.ToJson(report, true)); return;
            }
            if (EditorApplication.timeSinceStartup > report.deadline) throw new TimeoutException("Inventory V2 timed out");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || SceneLoadManager.IsLoading) return;
            if (report.lastFrame == Time.frameCount) return;
            report.lastFrame = Time.frameCount; report.frames++; Store(report);
            if (report.frames < (report.stage == 0 || report.stage == 11 || report.stage == 13 ? 35 : 9)) return;
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            if (!player) return;
            var inv = player.GetComponent<PlayerInventory>(); var ui = inv.UserInterface;
            var controller = player.viewCamera.GetComponent<PlayerCameraController>();
            var travel = player.GetComponent<PlayerInteraction>();
            switch (report.stage)
            {
                case 0:
                    Check(report, ui && !inv.IsOpen && ui.HotbarSlotCount == 8 && ui.StorageSlotCount == 16 && ui.EquipmentSlotCount == 5,
                        "Eight quick slots, sixteen storage slots, four clothing and one pet");
                    Check(report, UnityEngine.Object.FindObjectsByType<MoneyUI>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0,
                        "Permanent MoneyUI is removed");
                    Check(report, CashService.BankBalance == 10000 && CashService.CarriedTotal(inv.Inventory) == 0, "Existing bank balance retained; carried cash starts empty");
                    keyboard = InputSystem.AddDevice<Keyboard>("Inventory V2 QA Keyboard"); keyboard.MakeCurrent();
                    Capture("hotbar"); Press(Key.E); break;
                case 1:
                    Check(report, inv.IsOpen && !player.enabled && !controller.enabled && Cursor.visible, "E opens inventory and suspends gameplay camera");
                    Check(report, ui.CharacterPreview.IsReady, "Current character preview is rendered");
                    Check(report, inv.GetComponent<InventoryHandCursor>().IsVisible, "Hand cursor active");
                    Check(report, Gear(ui, EquipmentSlot.Pet).transform.parent != Gear(ui, EquipmentSlot.Top).transform.parent,
                        "Pet panel separate from clothing panel");
                    Check(report, !ui.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Contains("악세서리") || t.text == "선택한 아이템"),
                        "Accessory and selected-item pane removed");
                    Check(report, !ui.GetComponentsInChildren<Transform>(true).Any(t => t.name == "DiscardDropZone" || t.name == "RotateHint" ||
                        t.name == "BagHint" || t.name == "CarriedCash" || t.name == "Status" || t.name == "ItemName"),
                        "Removed permanent slot names, footer hints, carried cash and discard button");
                    CheckSquares(report, ui); Capture("inventory-white-empty"); Press(); break;
                case 2:
                    inventory = inv.Inventory; cameraRotation = player.viewCamera.transform.rotation; cameraPosition = player.viewCamera.transform.position;
                    originalYaw = ui.CharacterPreview.PreviewYaw;
                    var portrait = ui.CharacterPreview.PortraitRect.gameObject;
                    Check(report, !portrait.GetComponent<InventoryPortraitPointer>(), "Portrait has no right-drag rotation handler");
                    PropertyManager.Instance.SetMoney(12500000); // Disposable Play-only balance for all six artwork tiers.
                    ui.OpenWithdrawal(); WithdrawFields(ui, "1800", "2");
                    report.stage = 19; report.frames = 0; Store(report); return;
                case 19:
                    Press(Key.Tab); report.stage = 15; report.frames = 0; Store(report); return;
                case 15:
                    Check(report, ui.WithdrawalQuantityInput.isFocused && EventSystem.current.currentSelectedGameObject == ui.WithdrawalQuantityInput.gameObject,
                        "Tab moves from face value input to quantity input");
                    CheckWithdrawalLayout(report, ui);
                    Capture("withdrawal"); Press(Key.Enter);
                    report.stage = 3; report.frames = 0; Store(report); return;
                case 3:
                    Check(report, Mathf.Abs(ui.CharacterPreview.PreviewYaw - originalYaw) < .01f && Quaternion.Angle(cameraRotation, player.viewCamera.transform.rotation) < .01f &&
                        Vector3.Distance(cameraPosition, player.viewCamera.transform.position) < .001f, "Right-drag leaves preview and gameplay camera fixed");
                    Check(report, CashService.BankBalance == 12496400 && CashService.CarriedTotal(inventory) == 3600 && inventory.GetSlot(0).Count == 2 &&
                        inventory.GetSlot(0).Item.icon.name == "CurrencyGold", "Enter withdraws exactly two 1800-won gold coins without repeat charge");
                    var quantityLabel = Storage(ui, 0).transform.Find("Quantity").GetComponent<TMP_Text>();
                    quantityLabel.ForceMeshUpdate();
                    Check(report, quantityLabel.text == "2" && quantityLabel.textInfo.characterCount == 1 && quantityLabel.textInfo.characterInfo[0].isVisible,
                        "Cash stack quantity is visibly rendered without Korean-font clipping");
                    var tooltipPointer = new PointerEventData(EventSystem.current) { position = Centre((RectTransform)Storage(ui, 0).transform) };
                    ExecuteEvents.Execute(Storage(ui, 0).gameObject, tooltipPointer, ExecuteEvents.pointerEnterHandler);
                    Check(report, ui.GetComponentsInChildren<TMP_Text>(true).Any(t => t.transform.parent &&
                        t.transform.parent.name == "ItemTooltip" && t.text == "1,800원"), "Hover shows the currency amount in a popup");
                    ExecuteEvents.Execute(Storage(ui, 0).gameObject, tooltipPointer, ExecuteEvents.pointerExitHandler);
                    Press(); ui.CloseWithdrawal(); break;
                case 4:
                    foreach (long value in new long[] { 100, 10000, 100000, 1000000, 10000000 })
                        Check(report, CashService.TryWithdraw(inventory, value, 1, out _), "Withdraw artwork tier " + value);
                    Check(report, CashService.BankBalance + CashService.CarriedTotal(inventory) == 12500000, "Bank plus physical cash is conserved"); break;
                case 5:
                    Capture("inventory-six-currencies");
                    Drag(Storage(ui, 0), Storage(ui, 15));
                    Check(report, inventory.GetSlot(15).Count == 2 && inventory.GetSlot(0).IsEmpty, "Pointer drag moves whole cash stack to chosen cell");
                    Drag(Storage(ui, 15), Gear(ui, EquipmentSlot.Shoes));
                    Check(report, inventory.GetSlot(15).Count == 2 && inventory.GetEquipment(EquipmentSlot.Shoes).IsEmpty && !inv.IsDragging, "Invalid equipment drop preserves money");
                    foreach (var slot in Equipment)
                    {
                        var item = ScriptableObject.CreateInstance<ItemData>(); item.name = item.displayName = "테스트 " + slot;
                        item.category = (ItemCategory)((int)slot + 1); item.hideFlags = HideFlags.DontSave;
                        inventory.TryAdd(item, 1, out _); Drag(Storage(ui, 0), Gear(ui, slot));
                        Check(report, inventory.GetEquipment(slot).Item == item, "Pointer equips " + slot);
                    }
                    Drag(Gear(ui, EquipmentSlot.Top), Storage(ui, 7));
                    Check(report, !inventory.GetSlot(7).IsEmpty && inventory.GetEquipment(EquipmentSlot.Top).IsEmpty, "Equipment drag unequips into exact storage cell");
                    Drag(Storage(ui, 7), Gear(ui, EquipmentSlot.Top));
                    Check(report, inv.BeginDragInventory(15) && inv.DropIntoWorld(), "Drag can discard from the inventory without a visible discard button");
                    Check(report, inventory.GetSlot(15).IsEmpty && WorldDroppedItem.SessionDropCount == 1, "World discard creates currency stack"); break;
                case 6:
                    Check(report, UnityEngine.Object.FindFirstObjectByType<WorldDroppedItem>().CurrencyTotal == 3600 &&
                        CashService.CarriedTotal(inventory) == 11110100, "World drop retains exact denomination and quantity");
                    Capture("inventory-equipped"); Press(Key.E); break;
                case 7:
                    Check(report, !inv.IsOpen && player.enabled && controller.enabled, "Closing inventory restores controls");
                    Capture("dropped-cash"); Press(Key.F); break;
                case 8:
                    Check(report, WorldDroppedItem.SessionDropCount == 0 && CashService.CarriedTotal(inventory) == 11113700,
                        "F picks up nearby currency exactly once");
                    Press(Key.E); break;
                case 9:
                    Check(report, inv.IsOpen, "Reopen inventory after pickup"); Press();
                    Check(report, inv.BeginDragInventory(0), "Begin cash drag"); inventory.TryMove(0, 14, out _);
                    Check(report, !inv.DropOnInventory(15) && inventory.GetSlot(14).Count == 2 && inventory.GetSlot(15).IsEmpty, "Stale drag cannot duplicate or remove changed source");
                    inventory.TryExpandWithBag(out _); CheckSquares(report, ui);
                    Check(report, ui.StorageSlotCount == 24, "Bag row expands to twenty-four square cells");
                    report.stage = 16; report.frames = 0; Store(report); return;
                case 16:
                    Capture("inventory-expanded");
                    Check(report, inv.BeginDragInventory(14) && inv.DropIntoWorld(), "Drop cash before scene round trip");
                    report.dropId = UnityEngine.Object.FindFirstObjectByType<WorldDroppedItem>().DropId;
                    inv.CloseInventory();
                    var station = UnityEngine.Object.FindObjectsByType<TransitStop>(FindObjectsSortMode.None).Single(t => t.kind == TransitKind.Subway);
                    player.spawn = station.BoardingPosition; player.ResetToSpawn(); Press();
                    report.stage = 17; report.frames = 0; Store(report); return;
                case 17:
                    Check(report, player.enabled && !PlayerInventory.IsAnyOpen, "Controls restored before transport input");
                    Press(Key.Space); report.stage = 18; report.frames = 0; Store(report); return;
                case 18:
                    Check(report, travel.IsDestinationMenuOpen, "SPACE opens transport after inventory close");
                    Press(); report.stage = 10; report.frames = 0; Store(report); return;
                case 10:
                    Check(report, travel.IsDestinationMenuOpen && travel.IsMenuReady, "Transport destination selection arms after SPACE release");
                    Press(); Check(report, travel.TryTravelTo(CityDistrictBuilder.PathFor(1)), "Travel to Civic scene"); break;
                case 11:
                    Check(report, player.gameObject.scene.path == CityDistrictBuilder.PathFor(1) && ReferenceEquals(inv.Inventory, inventory) &&
                        CashService.BankBalance == 1386300 && CashService.CarriedTotal(inventory) == 11110100, "Bank and inventory retain separate balances on map change");
                    Check(report, WorldDroppedItem.SessionDropCount == 1 && !UnityEngine.Object.FindFirstObjectByType<WorldDroppedItem>(), "Dropped cash stays in its original map");
                    Check(report, travel.OpenDestinationMenu(), "Open return transport"); break;
                case 12:
                    Check(report, travel.TryTravelTo(TerracedVillageExpansion.ScenePath), "Return to Daldongne"); break;
                case 13:
                    var dropped = UnityEngine.Object.FindFirstObjectByType<WorldDroppedItem>();
                    Check(report, dropped && dropped.DropId == report.dropId && dropped.CurrencyTotal == 3600, "Same physical cash restored on return");
                    player.spawn = dropped.transform.position + new Vector3(0, .1f, -.7f); player.ResetToSpawn(); Press(Key.F); break;
                case 14:
                    Check(report, WorldDroppedItem.SessionDropCount == 0 && CashService.CarriedTotal(inventory) + CashService.BankBalance == 12500000,
                        "Returned pickup conserves bank plus carried total");
                    Check(report, inventory.Capacity == 24 && Equipment.All(e => !inventory.GetEquipment(e).IsEmpty), "Equipment and expansion survive map round trip");
                    inv.SelectHotbar(0); Press(Key.Space);
                    report.stage = 20; report.frames = 0; Store(report); return;
                case 20:
                    Check(report, inventory.GetSlot(0).Count == 1 && CashService.BankBalance == 1388100 && CashService.CarriedTotal(inventory) == 11111900,
                        "Space deposits exactly one of the selected two 1800-won coins");
                    Check(report, !travel.IsDestinationMenuOpen && inventory.GetSlot(1).Item.CurrencyValue == 100,
                        "Deposit preserves other currency and does not open transport");
                    Press(Key.E); report.stage = 21; report.frames = 0; Store(report); return;
                case 21:
                    Check(report, inv.IsOpen && CashService.BankBalance == 1388100, "Held Space does not repeat deposit");
                    inv.ClickInventorySlot(1); ui.OpenWithdrawal(); Press(Key.Space);
                    report.stage = 22; report.frames = 0; Store(report); return;
                case 22:
                    Check(report, !inventory.GetSlot(1).IsEmpty && CashService.BankBalance == 1388100, "Space cannot deposit while withdrawal input is open");
                    ui.CloseWithdrawal(); Press(); report.stage = 23; report.frames = 0; Store(report); return;
                case 23:
                    if (inv.SelectedInventorySlot != 1) inv.ClickInventorySlot(1);
                    Press(Key.Space); report.stage = 24; report.frames = 0; Store(report); return;
                case 24:
                    Check(report, inventory.GetSlot(1).IsEmpty && CashService.BankBalance == 1388200 &&
                        CashService.BankBalance + CashService.CarriedTotal(inventory) == 12500000,
                        "Space deposits clicked inventory currency and conserves total funds");
                    Capture("inventory-deposited"); ui.OpenWithdrawal(); ui.SetWithdrawalValues("0", "3"); Press(Key.Enter);
                    report.stage = 26; report.frames = 0; Store(report); return;
                case 26:
                    Check(report, !ui.IsWithdrawalOpen && inventory.GetSlot(1).Item.IsCurrency && inventory.GetSlot(1).Item.CurrencyValue == 0 &&
                        inventory.GetSlot(1).Count == 3 && inventory.GetSlot(1).Item.icon.name == "CurrencySilver" && CashService.BankBalance == 1388200,
                        "Zero-won withdrawal creates three silver coins without changing bank balance");
                    if (inv.SelectedInventorySlot != 1) inv.ClickInventorySlot(1);
                    Press(Key.Space); report.stage = 27; report.frames = 0; Store(report); return;
                case 27:
                    Check(report, inventory.GetSlot(1).Count == 2 && CashService.BankBalance == 1388200,
                        "Space deposits one zero-won coin without adding money");
                    Press(); report.passed = true; Finish(report); return;
            }
            report.stage++; report.frames = 0; Store(report);
        }
        catch (Exception exception) { report.error = exception.ToString(); Finish(report); }
    }
    static void Finish(Report report)
    {
        report.restoring = true; Store(report);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
