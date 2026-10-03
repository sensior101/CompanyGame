using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>Scene-local inventory input and UI; contents belong to InventoryManager.</summary>
[DefaultExecutionOrder(-300)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public sealed class PlayerInventory : MonoBehaviour
{
    public TMP_FontAsset uiFont;
    public InventoryState Inventory { get; private set; }
    public bool IsOpen { get; private set; }
    public int SelectedInventorySlot { get; private set; } = -1;
    public string StatusMessage { get; private set; } = string.Empty;
    public bool IsDragging => draggedItem;
    public ItemData DraggedItem => draggedItem;
    public int DraggedCount => draggedCount;
    public InventoryUI UserInterface => ui;
    public bool CanPickUpWorldItems => isActiveAndEnabled && !restorePending && !IsDragging &&
        !SceneLoadManager.IsLoading && !ChatUIManager.IsChatting && !IsEditingText() &&
        !(ui && ui.IsWithdrawalOpen) && !(interaction && interaction.IsInteractionMenuOpen) &&
        (IsOpen || (movement && movement.isActiveAndEnabled));
    public event Action UiChanged;
    public static bool IsAnyOpen => activeInventory && (activeInventory.IsOpen || activeInventory.restorePending);
    public static bool SpaceConsumedThisFrame => spaceConsumedFrame == Time.frameCount;
    public static bool CurrencyScrollCapturedThisFrame => currencyScrollCapturedFrame == Time.frameCount;
    public static bool HotbarScrollCapturedThisFrame => hotbarScrollCapturedFrame == Time.frameCount;

    static PlayerInventory activeInventory;
    static int spaceConsumedFrame = -1;
    static int currencyScrollCapturedFrame = -1;
    static int hotbarScrollCapturedFrame = -1;
    bool currencyDepositGesture;
    int currencyDepositSlot = -1;
    ItemData currencyDepositItem;
    float currencyScrollRemainder;
    PlayerMovement movement;
    PlayerInteraction interaction;
    InventoryUI ui;
    readonly List<Behaviour> suspendedControls = new List<Behaviour>();
    bool movementWasEnabled;
    bool hasControlSnapshot;
    bool restorePending;
    bool cursorWasVisible;
    CursorLockMode previousCursorLock;
    GameObject previousSelection;
    int closedFrame = -1;
    int suppressClickThroughFrame = -1;
    int inventoryRevision;
    int dragRevision;
    int dragIndex = -1;
    EquipmentSlot dragEquipment;
    bool dragFromEquipment;
    ItemData draggedItem;
    int draggedCount;
    int draggedSourceCount;
    bool dragRightButton;
    InventoryHandCursor handCursor;
    readonly List<RaycastResult> pointerHits = new List<RaycastResult>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        activeInventory = null;
        spaceConsumedFrame = currencyScrollCapturedFrame = hotbarScrollCapturedFrame = -1;
    }

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        interaction = GetComponent<PlayerInteraction>();
        Inventory = InventoryManager.Instance.State;
        if (!GetComponent<PlayerCombat>()) gameObject.AddComponent<PlayerCombat>();
    }

    void OnEnable()
    {
        if (Inventory != null) Inventory.Changed += HandleInventoryChanged;
        if (ui) ui.gameObject.SetActive(true);
    }

    void Start() { EnsureUI(); }

    void Update()
    {
        // Unity can omit uGUI OnEndDrag when the pointer leaves the Canvas.
        // Finish the same drag from the actual mouse-release frame so dragging
        // outside the inventory still reaches EndDragAt/DropIntoWorld.
        if (IsDragging && DragButtonReleased())
        {
            EndDragAt(PointerPosition());
            return;
        }
        // Reserve the held-key gesture even after its last coin, so later wheel
        // input cannot unexpectedly zoom the camera or board public transport.
        if (!SpaceHeld() || SpacePressed()) ResetCurrencyDepositGesture();
        else if (currencyDepositGesture) CaptureCurrencyGestureInput();
        if (SceneLoadManager.IsLoading)
        {
            if (IsOpen) CloseInventory();
            return;
        }
        if (IsOpen)
        {
            if (EscapePressed())
            {
                if (IsDragging) CancelDrag();
                else if (!ui || !ui.HandleEscape()) CloseInventory();
                return;
            }
            if (ui && ui.IsWithdrawalOpen) return;
            if (TogglePressed() && !IsEditingText()) { CloseInventory(); return; }
            if (HandleCurrencyDepositInput()) return;
            if (PickupPressed() && CanPickUpWorldItems) { TryPickUpNearest(); return; }
            return;
        }
        if (restorePending || closedFrame == Time.frameCount || !movement || !movement.isActiveAndEnabled ||
            ChatUIManager.IsChatting || IsEditingText() || (interaction && interaction.IsInteractionMenuOpen)) return;
        if (TogglePressed()) { OpenInventory(); return; }
        if (PickupPressed()) { TryPickUpNearest(); return; }
        int hotbar = HotbarPressed();
        if (hotbar >= 0) SelectHotbar(hotbar);
        if (!SpaceHeld()) HandleHotbarScrollInput();
        HandleCurrencyDepositInput();
    }

    void LateUpdate()
    {
        if (!restorePending) return;
        restorePending = false;
        RestoreControls(!SceneLoadManager.IsLoading);
        if (activeInventory == this) activeInventory = null;
    }

    public bool OpenInventory()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || IsOpen || IsAnyOpen || restorePending ||
            SceneLoadManager.IsLoading || !movement || !movement.isActiveAndEnabled || ChatUIManager.IsChatting ||
            IsEditingText() || (interaction && interaction.IsInteractionMenuOpen)) return false;
        EnsureUI();
        activeInventory = this;
        IsOpen = true;
        SelectedInventorySlot = -1;
        StatusMessage = string.Empty;
        SuspendControls();
        ui.SetOpen(true);
        if (!handCursor) handCursor = gameObject.AddComponent<InventoryHandCursor>();
        handCursor.Show();
        UiChanged?.Invoke();
        return true;
    }

    public void CloseInventory()
    {
        if (!IsOpen) return;
        CancelDrag();
        if (handCursor) handCursor.Hide();
        IsOpen = false;
        SelectedInventorySlot = -1;
        StatusMessage = string.Empty;
        closedFrame = Time.frameCount;
        if (ui) ui.SetOpen(false);
        restorePending = true;
        UiChanged?.Invoke();
    }

    public void SelectHotbar(int index)
    {
        if (IsDragging || Time.frameCount <= suppressClickThroughFrame || IsEditingText() ||
            (ui && ui.IsWithdrawalOpen) || SceneLoadManager.IsLoading || ChatUIManager.IsChatting ||
            (interaction && interaction.IsInteractionMenuOpen)) return;
        Inventory.SelectHotbar(index);
    }

    bool HandleHotbarScrollInput()
    {
        float wheel = WheelNotches();
        if (Mathf.Approximately(wheel, 0f) || IsDragging || IsEditingText() ||
            (ui && ui.IsWithdrawalOpen) || SceneLoadManager.IsLoading || ChatUIManager.IsChatting ||
            (interaction && interaction.IsInteractionMenuOpen)) return false;

        int direction = wheel < 0f ? 1 : -1; // wheel down: right, wheel up: left
        int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(wheel)));
        int next = Inventory.SelectedHotbarIndex;
        for (int i = 0; i < steps; i++)
            next = (next + direction + InventoryState.HotbarSize) % InventoryState.HotbarSize;
        SelectHotbar(next);
        hotbarScrollCapturedFrame = Time.frameCount;
        return true;
    }

    bool CanDepositCurrency() => Application.isPlaying && isActiveAndEnabled && Inventory != null && !restorePending &&
        closedFrame != Time.frameCount && !IsDragging && !SceneLoadManager.IsLoading && !ChatUIManager.IsChatting &&
        !IsEditingText() && !(ui && ui.IsWithdrawalOpen) && !(interaction && interaction.IsInteractionMenuOpen) &&
        (IsOpen || (movement && movement.isActiveAndEnabled));

    int SelectedCurrencySlot => IsOpen && SelectedInventorySlot >= 0 ? SelectedInventorySlot : Inventory.SelectedHotbarIndex;

    /// <summary>Deposit one selected currency unit by default; other stacks remain untouched.</summary>
    public bool TryDepositSelectedCurrency(int quantity = 1)
    {
        if (!CanDepositCurrency() || SpaceConsumedThisFrame) return false;
        return DepositCurrency(SelectedCurrencySlot, quantity);
    }

    bool DepositCurrency(int index, int quantity)
    {
        ItemStack stack = Inventory.GetSlot(index);
        if (stack == null || stack.IsEmpty || !stack.Item.IsCurrency) return false;
        ItemData currency = stack.Item;

        // Inventory input runs before transit input. Even a rejected deposit owns
        // this press so the same Space cannot also board public transport.
        spaceConsumedFrame = Time.frameCount;
        bool deposited = CashService.TryDeposit(Inventory, index, quantity, out string error);
        if (deposited && EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        if (deposited)
        {
            long amount = checked(currency.CurrencyValue * (long)quantity);
            ShowWalletDepositMessage(amount);
        }
        SetStatus(error);
        return deposited;
    }

    static void ShowWalletDepositMessage(long amount)
    {
        string message = amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + "원을 지갑에 도로 넣었다.";
        Debug.Log("[시스템] " + message);
        foreach (var chat in FindObjectsByType<ChatUIManager>())
            if (chat) chat.ShowPopup("시스템", message);
    }

    bool HandleCurrencyDepositInput()
    {
        // Door/clerk prompts own Space in their range, even with cash selected.
        if (!IsOpen && interaction && interaction.HasNearbyStoreAction)
        {
            ResetCurrencyDepositGesture();
            return false;
        }
        bool pressed = SpacePressed();
        if (pressed)
        {
            ResetCurrencyDepositGesture();
            if (!CanDepositCurrency() || SpaceConsumedThisFrame) return false;
            int index = SelectedCurrencySlot;
            ItemStack selected = Inventory.GetSlot(index);
            if (selected == null || selected.IsEmpty || !selected.Item.IsCurrency) return false;
            currencyDepositGesture = true;
            currencyDepositSlot = index;
            currencyDepositItem = selected.Item;
        }
        if (!currencyDepositGesture || !SpaceHeld()) return false;
        CaptureCurrencyGestureInput();
        if (!CanDepositCurrency() || SelectedCurrencySlot != currencyDepositSlot) return true;
        ItemStack stack = Inventory.GetSlot(currencyDepositSlot);
        if (stack == null || stack.IsEmpty || stack.Item != currencyDepositItem) return true;

        float wheel = WheelNotches();
        if (wheel < 0f) currencyScrollRemainder += -wheel;
        else if (wheel > 0f) currencyScrollRemainder = 0f;
        int notches = Mathf.FloorToInt(currencyScrollRemainder + .0001f);
        currencyScrollRemainder = Mathf.Max(0f, currencyScrollRemainder - notches);
        int quantity = Mathf.Min(stack.Count, notches + (pressed ? 1 : 0));
        if (quantity > 0) DepositCurrency(currencyDepositSlot, quantity);
        return true;
    }

    void CaptureCurrencyGestureInput()
    {
        spaceConsumedFrame = Time.frameCount;
        if (WheelNotches() != 0f) currencyScrollCapturedFrame = Time.frameCount;
    }

    void ResetCurrencyDepositGesture()
    {
        currencyDepositGesture = false;
        currencyDepositSlot = -1;
        currencyDepositItem = null;
        currencyScrollRemainder = 0f;
    }

    public void ClickInventorySlot(int index)
    {
        if (!CanClick() || Inventory.GetSlot(index) == null) return;
        StatusMessage = string.Empty;
        // An emptied deposit slot remains selected until the player chooses a
        // different slot. Selecting that next slot should not try moving emptiness.
        if (SelectedInventorySlot >= 0 && Inventory.GetSlot(SelectedInventorySlot).IsEmpty)
            SelectedInventorySlot = -1;
        if (SelectedInventorySlot < 0)
        {
            if (!Inventory.GetSlot(index).IsEmpty) SelectedInventorySlot = index;
        }
        else if (SelectedInventorySlot == index) SelectedInventorySlot = -1;
        else
        {
            bool moved = Inventory.TryMove(SelectedInventorySlot, index, out string error);
            StatusMessage = error ?? string.Empty;
            if (moved) SelectedInventorySlot = -1;
        }
        UiChanged?.Invoke();
    }

    public void ClickEquipmentSlot(EquipmentSlot slot)
    {
        if (!CanClick()) return;
        string error;
        bool changed = SelectedInventorySlot >= 0
            ? Inventory.TryEquip(SelectedInventorySlot, slot, out error)
            : Inventory.TryUnequip(slot, out error);
        StatusMessage = error ?? string.Empty;
        if (changed) SelectedInventorySlot = -1;
        UiChanged?.Invoke();
    }

    void HandleInventoryChanged()
    {
        inventoryRevision++;
        if (IsDragging) CancelDrag();
        if (SelectedInventorySlot >= 0 && Inventory.GetSlot(SelectedInventorySlot) == null)
            SelectedInventorySlot = -1;
        UiChanged?.Invoke();
    }

    bool CanClick() => IsOpen && !IsDragging && Time.frameCount > suppressClickThroughFrame &&
        !(ui && ui.IsWithdrawalOpen) && !SceneLoadManager.IsLoading;

    public void SetStatus(string message)
    {
        StatusMessage = message ?? string.Empty;
        UiChanged?.Invoke();
    }

    public bool BeginDragInventory(int index,bool single=false) => BeginDrag(Inventory.GetSlot(index), index, false, default,single);
    public bool BeginDragEquipment(EquipmentSlot slot,bool single=false) => BeginDrag(Inventory.GetEquipment(slot), -1, true, slot,single);

    bool BeginDrag(ItemStack stack, int index, bool fromEquipment, EquipmentSlot equipmentSlot,bool single)
    {
        if (!IsOpen || SceneLoadManager.IsLoading || (ui && ui.IsWithdrawalOpen) || stack == null || stack.IsEmpty) return false;
        CancelDrag();
        draggedItem = stack.Item;
        draggedSourceCount = stack.Count;
        draggedCount = single ? 1 : stack.Count;
        dragRightButton = single;
        dragIndex = index;
        dragFromEquipment = fromEquipment;
        dragEquipment = equipmentSlot;
        dragRevision = inventoryRevision;
        SelectedInventorySlot = index;
        StatusMessage = string.Empty;
        if (ui) ui.BeginDragVisual(stack,draggedCount);
        if (handCursor) handCursor.SetDragging(true);
        UiChanged?.Invoke();
        return true;
    }

    public void UpdateDrag(Vector2 screenPosition)
    {
        if (IsDragging && ui) ui.UpdateDragVisual(screenPosition);
    }

    bool ValidDrag()
    {
        var source = dragFromEquipment ? Inventory.GetEquipment(dragEquipment) : Inventory.GetSlot(dragIndex);
        return IsOpen && !(ui && ui.IsWithdrawalOpen) && !SceneLoadManager.IsLoading && IsDragging &&
            dragRevision == inventoryRevision && source != null && source.Item == draggedItem && source.Count == draggedSourceCount;
    }

    public bool DropOnInventory(int targetIndex)
    {
        if (!ValidDrag()) { CancelDrag(); return false; }
        int sourceIndex = dragIndex;
        bool equipped = dragFromEquipment;
        var equipmentSlot = dragEquipment;
        int amount = draggedCount;
        CancelDrag();
        string error;
        bool success = equipped ? Inventory.TryUnequipTo(equipmentSlot, targetIndex, out error)
            : Inventory.TryMoveAmount(sourceIndex, targetIndex, amount, out error);
        SetStatus(error);
        return success;
    }

    public bool DropOnEquipment(EquipmentSlot targetSlot)
    {
        if (!ValidDrag()) { CancelDrag(); return false; }
        int sourceIndex = dragIndex;
        bool equipped = dragFromEquipment;
        bool sameEquipment = equipped && dragEquipment == targetSlot;
        CancelDrag();
        if (equipped)
        {
            if (!sameEquipment) SetStatus("알맞은 소지품 칸으로 옮겨 주세요.");
            return sameEquipment;
        }
        bool success = Inventory.TryEquip(sourceIndex, targetSlot, out string error);
        SetStatus(error);
        return success;
    }

    public bool DropIntoWorld()
    {
        if (!ValidDrag()) { CancelDrag(); return false; }
        int sourceIndex = dragIndex;
        bool equipped = dragFromEquipment;
        var equipmentSlot = dragEquipment;
        int amount = draggedCount;
        CancelDrag();
        string error;
        bool success = equipped ? WorldDroppedItem.TryDropEquipment(this, equipmentSlot, out error)
            : WorldDroppedItem.TryDropStorage(this, sourceIndex, out error, amount);
        SetStatus(success ? "앞에 내려놓았습니다. 가까이에서 F 키로 주울 수 있습니다." : error);
        return success;
    }

    public void CancelDrag()
    {
        bool hadDrag = IsDragging;
        draggedItem = null;
        draggedCount = 0;
        dragIndex = -1;
        SelectedInventorySlot = -1;
        if (hadDrag) suppressClickThroughFrame = Time.frameCount + 1;
        if (ui) ui.EndDragVisual();
        if (handCursor) handCursor.SetDragging(false);
        if (hadDrag) UiChanged?.Invoke();
    }

    public void EndDragAt(Vector2 screenPosition)
    {
        if (!IsDragging) return;
        if (!Application.isFocused || float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y) ||
            screenPosition.x < 0f || screenPosition.y < 0f || screenPosition.x >= Screen.width || screenPosition.y >= Screen.height)
        {
            CancelDrag();
            return;
        }
        RaycastUI(screenPosition);
        if (pointerHits.Count > 0)
        {
            // A slot (or one of its icon/text children) accepts an inventory drop.
            var slot = pointerHits[0].gameObject.GetComponentInParent<InventorySlotPointer>();
            if (slot && slot.Owner == this)
            {
                slot.AcceptDrop();
                return;
            }

            // The inventory window has a raycastable surface so buttons and slots
            // remain reliable. That surface must not swallow a release outside
            // the window: release anywhere beyond its bounds means world drop.
            if (ui && ui.IsInsideWindow(screenPosition))
            {
                CancelDrag();
                return;
            }

            DropIntoWorld();
            return;
        }
        if (ui && ui.IsInsideWindow(screenPosition)) CancelDrag();
        else DropIntoWorld();
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused && IsDragging) CancelDrag();
    }

    void RaycastUI(Vector2 screenPosition)
    {
        pointerHits.Clear();
        if (!EventSystem.current) return;
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPosition }, pointerHits);
        // PhysicsRaycasters do not count as a UI overlay.
        pointerHits.RemoveAll(hit => !(hit.module is UnityEngine.UI.GraphicRaycaster));
    }

    /// <summary>Pick up the nearest reachable stack; F works both in gameplay and in inventory.</summary>
    public bool TryPickUpNearest()
    {
        if (!CanPickUpWorldItems) return false;
        WorldDroppedItem nearest = null;
        float bestDistance = 3f * 3f;
        foreach (var item in FindObjectsByType<WorldDroppedItem>())
        {
            if (item.gameObject.scene != SceneLoadManager.CurrentMap || item.Count <= 0) continue;
            float distance = (item.transform.position - transform.position).sqrMagnitude;
            if (distance > bestDistance || !item.IsReachableFrom(this, 3f)) continue;
            bestDistance = distance;
            nearest = item;
        }
        if (!nearest) return false;
        bool success = nearest.TryPickUp(this, out string error);
        SetStatus(success ? "아이템을 주웠습니다." : error);
        return success;
    }

    void EnsureUI()
    {
        if (ui) return;
        if (!uiFont && interaction) uiFont = interaction.uiFont;
        ui = InventoryUI.Create(this, uiFont);
        SceneManager.MoveGameObjectToScene(ui.gameObject, SceneLoadManager.CurrentMap);
    }

    void SuspendControls()
    {
        movementWasEnabled = movement.enabled;
        cursorWasVisible = Cursor.visible;
        previousCursorLock = Cursor.lockState;
        previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        hasControlSnapshot = true;
        suspendedControls.Clear();
        foreach (var chat in FindObjectsByType<ChatUIManager>()) Suspend(chat);
        foreach (var cameraController in FindObjectsByType<PlayerCameraController>())
            if (cameraController.target == transform) Suspend(cameraController);
        // Chat.OnDisable can restore the movement state it previously captured.
        // Apply our movement lock after pausing the chat controllers.
        movement.enabled = false;
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Suspend(Behaviour component)
    {
        if (!component.isActiveAndEnabled) return;
        suspendedControls.Add(component);
        component.enabled = false;
    }

    void RestoreControls(bool restoreMovement)
    {
        if (!hasControlSnapshot) return;
        hasControlSnapshot = false;
        foreach (var component in suspendedControls) if (component) component.enabled = true;
        suspendedControls.Clear();
        if (restoreMovement && movement) movement.enabled = movementWasEnabled;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = cursorWasVisible;
        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
        previousSelection = null;
    }

    void OnDisable()
    {
        ResetCurrencyDepositGesture();
        if (Inventory != null) Inventory.Changed -= HandleInventoryChanged;
        CancelDrag();
        if (handCursor) handCursor.Hide();
        IsOpen = false;
        restorePending = false;
        SelectedInventorySlot = -1;
        if (activeInventory == this) activeInventory = null;
        if (ui) { ui.SetOpen(false); ui.gameObject.SetActive(false); }
        RestoreControls(!SceneLoadManager.IsLoading);
    }

    void OnDestroy() { if (ui) Destroy(ui.gameObject); }

    static bool IsEditingText()
    {
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        return selected && (selected.GetComponentInParent<TMP_InputField>() || selected.GetComponentInParent<UnityEngine.UI.InputField>());
    }

    static bool PickupPressed()
    {
        return GameInput.PickupPressed;
    }

    static bool SpacePressed()
    {
        return GameInput.InteractPressed;
    }

    static bool SpaceHeld()
    {
        return GameInput.InteractHeld;
    }

    static float WheelNotches()
    {
        float raw = GameInput.Scroll;
        return Mathf.Approximately(raw, 0f) ? 0f : Mathf.Sign(raw);
    }

    bool DragButtonReleased()
    {
        return GameInput.ButtonReleased(dragRightButton);
    }

    static Vector2 PointerPosition()
    {
        return GameInput.PointerPosition;
    }

    static bool TogglePressed()
    {
        return GameInput.InventoryPressed;
    }

    static bool EscapePressed()
    {
        return GameInput.CancelPressed;
    }

    static int HotbarPressed()
    {
        return GameInput.NumberPressed(InventoryState.HotbarSize);
    }
}
