using UnityEngine;

using System;
using System.Collections.Generic;
using CompanyGame.World.Maps;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerInteraction : MonoBehaviour
{
    [Header("Public transport")]
    [Tooltip("Korean UI font, e.g. NotoSansKR-Regular SDF.")]
    public TMP_FontAsset uiFont;
    [Tooltip("All six districts. The active district and disabled build scenes are excluded at runtime.")]
    public TransitDestination[] destinations = Array.Empty<TransitDestination>();

    public bool IsDestinationMenuOpen { get; private set; }
    public TransitStop FocusedStop { get; private set; }
    public bool IsMenuReady => IsDestinationMenuOpen && menuArmed;

    PlayerMovement movement;
    TransitUI ui;
    TransitStop boardingStop;
    readonly List<TransitDestination> availableDestinations = new List<TransitDestination>();
    readonly List<Behaviour> suspendedControls = new List<Behaviour>();
    bool movementWasEnabled;
    bool hasControlSnapshot;
    bool restorePending;
    bool travelPending;
    bool menuArmed;
    bool waitForSpaceRelease = true;
    bool cursorWasVisible;
    CursorLockMode previousCursorLock;
    GameObject previousSelection;
    int menuOpenedFrame;

    void Awake() { movement = GetComponent<PlayerMovement>(); }

    void Update()
    {
        if (SceneLoadManager.IsLoading)
        {
            if (ui) ui.HidePrompt();
            return;
        }
        if (travelPending)
        {
            // A successful load destroys this scene-local player. If it is still
            // present when loading finishes, the async load failed; recover the UI.
            travelPending = false;
            if (ui) ui.HideModal();
            if (OpenDestinationMenu()) ui.ShowStatus("이동하지 못했습니다. 목적지를 다시 선택해 주세요.");
            return;
        }
        if (IsDestinationMenuOpen)
        {
            // Do not let the key that opened the window submit its first button.
            if (!menuArmed && Time.frameCount > menuOpenedFrame && !SpaceHeld())
            {
                menuArmed = true;
                ui.SetInteractable(true);
                ui.FocusFirstDestination();
            }
            if (!boardingStop || !boardingStop.isActiveAndEnabled || !boardingStop.IsInRange(transform) || EscapePressed())
                CloseDestinationMenu();
            return;
        }
        if (restorePending) return;
        if (waitForSpaceRelease)
        {
            if (SpaceHeld()) return;
            waitForSpaceRelease = false;
        }
        if (!movement || !movement.isActiveAndEnabled || PlayerInventory.IsAnyOpen || PlayerInventory.SpaceConsumedThisFrame ||
            ChatUIManager.IsChatting || IsEditingText())
        {
            FocusedStop = null;
            if (ui) ui.HidePrompt();
            return;
        }
        FocusedStop = TransitStop.FindNearest(transform);
        if (!FocusedStop)
        {
            if (ui) ui.HidePrompt();
            return;
        }
        EnsureUI();
        ui.ShowPrompt(FocusedStop.Prompt);
        if (SpacePressed()) OpenDestinationMenu();
    }

    void LateUpdate()
    {
        // Restore after movement Update so closing cannot also move/reset the player.
        if (!restorePending) return;
        restorePending = false;
        RestoreControls(!SceneLoadManager.IsLoading);
    }

    public bool OpenDestinationMenu()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || IsDestinationMenuOpen || restorePending || PlayerInventory.IsAnyOpen ||
            PlayerInventory.SpaceConsumedThisFrame || SceneLoadManager.IsLoading ||
            !movement || !movement.isActiveAndEnabled || ChatUIManager.IsChatting || IsEditingText()) return false;
        FocusedStop = TransitStop.FindNearest(transform);
        if (!FocusedStop) return false;
        EnsureUI();
        boardingStop = FocusedStop;
        CollectDestinations();
        SuspendControls();
        IsDestinationMenuOpen = true;
        menuArmed = false;
        menuOpenedFrame = Time.frameCount;
        ui.ShowDestinations(boardingStop.kind, CurrentDistrictName(), availableDestinations, destination => TryTravelTo(destination.scenePath));
        return true;
    }

    public void CloseDestinationMenu()
    {
        if (!IsDestinationMenuOpen || SceneLoadManager.IsLoading) return;
        IsDestinationMenuOpen = false;
        boardingStop = null;
        waitForSpaceRelease = true;
        if (ui) ui.HideModal();
        restorePending = true;
    }

    public bool TryTravelTo(string scenePath)
    {
        if (!IsDestinationMenuOpen || !menuArmed || SceneLoadManager.IsLoading ||
            !boardingStop || !boardingStop.IsInRange(transform)) return false;
        TransitDestination destination = availableDestinations.Find(candidate => candidate.scenePath == scenePath);
        if (destination == null) return false;
        ui.SetInteractable(false);
        // SceneLoadManager validates an enabled player and then owns disabling /
        // restoring it for the asynchronous scene load.
        movement.enabled = movementWasEnabled;
        if (SceneLoadManager.TryLoadMap(destination.scenePath, boardingStop.ArrivalSpawnId, movement))
        {
            IsDestinationMenuOpen = false;
            travelPending = true;
            waitForSpaceRelease = true;
            ui.ShowStatus(destination.displayName + "(으)로 이동 중입니다…");
            RestoreControls(false);
            return true;
        }
        movement.enabled = false;
        ui.SetInteractable(true);
        ui.ShowStatus("이동을 시작하지 못했습니다. 잠시 후 다시 선택해 주세요.");
        return false;
    }

    void CollectDestinations()
    {
        availableDestinations.Clear();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var destination in destinations ?? Array.Empty<TransitDestination>())
        {
            if (destination == null || string.IsNullOrWhiteSpace(destination.scenePath)) continue;
            var path = destination.scenePath.Trim().Replace('\\', '/');
            if (path == gameObject.scene.path || !paths.Add(path) ||
                SceneUtility.GetBuildIndexByScenePath(path) < 0 || !Application.CanStreamedLevelBeLoaded(path)) continue;
            availableDestinations.Add(destination);
        }
    }

    string CurrentDistrictName()
    {
        foreach (var destination in destinations ?? Array.Empty<TransitDestination>())
            if (destination != null && destination.scenePath == gameObject.scene.path)
                return destination.displayName;
        return gameObject.scene.name;
    }

    void EnsureUI()
    {
        if (ui) return;
        ui = TransitUI.Create(uiFont, CloseDestinationMenu);
        SceneManager.MoveGameObjectToScene(ui.gameObject, gameObject.scene);
    }

    void SuspendControls()
    {
        movementWasEnabled = movement.enabled;
        cursorWasVisible = Cursor.visible;
        previousCursorLock = Cursor.lockState;
        previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        hasControlSnapshot = true;
        suspendedControls.Clear();
        // Closed chat controllers also listen for Enter/Escape. Pause them while
        // keyboard navigation belongs to the destination picker.
        foreach (var chat in FindObjectsByType<ChatUIManager>(FindObjectsSortMode.None)) Suspend(chat);
        foreach (var cameraController in FindObjectsByType<PlayerCameraController>(FindObjectsSortMode.None))
            if (cameraController.target == transform) Suspend(cameraController);
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
        IsDestinationMenuOpen = false;
        FocusedStop = null;
        boardingStop = null;
        restorePending = false;
        travelPending = false;
        waitForSpaceRelease = true;
        if (ui) { ui.HidePrompt(); ui.HideModal(); }
        RestoreControls(!SceneLoadManager.IsLoading);
    }

    void OnDestroy() { if (ui) Destroy(ui.gameObject); }

    static bool IsEditingText()
    {
        var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        return selected && (selected.GetComponentInParent<TMP_InputField>() || selected.GetComponentInParent<UnityEngine.UI.InputField>());
    }

    static bool SpacePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Space);
#else
        return false;
#endif
    }

    static bool SpaceHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.Space);
#else
        return false;
#endif
    }

    static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }
}
