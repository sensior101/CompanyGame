using UnityEngine;

using System;
using System.Collections.Generic;
using CompanyGame.World.Maps;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

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
    public bool IsStoreOpen => storeUI;
    public bool IsInteractionMenuOpen => IsDestinationMenuOpen || IsStoreOpen;
    public StoreInteractionPoint FocusedStore { get; private set; }
    public bool HasNearbyStoreAction => !IsInteractionMenuOpen && StoreInteractionPoint.FindNearest(transform);
    public StoreTradeUI StoreUI => storeUI;
    public TransitStop FocusedStop { get; private set; }
    public bool IsMenuReady => IsDestinationMenuOpen && menuArmed;

    PlayerMovement movement;
    TransitUI ui;
    StoreTradeUI storeUI;
    StoreInteractionPoint tradingWith;
    TransitStop boardingStop;
    readonly List<TransitDestination> availableDestinations = new List<TransitDestination>();
    readonly ControlLock controls = new ControlLock();
    bool restorePending;
    bool travelPending;
    string travelTargetPath;
    bool menuArmed;
    bool waitForSpaceRelease = true;
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
            // The player survives map loads; arriving anywhere but the target means the load failed.
            travelPending = false;
            if (SceneLoadManager.CurrentMap.path == travelTargetPath) return;
            if (ui) ui.HideModal();
            if (OpenDestinationMenu()) ui.ShowStatus("이동하지 못했습니다. 목적지를 다시 선택해 주세요.");
            return;
        }
        if (IsStoreOpen)
        {
            if (!tradingWith || !tradingWith.IsInRange(transform) || EscapePressed()) CloseStore();
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
            ChatUIManager.IsChatting || UIEventSystem.IsEditingText())
        {
            FocusedStop = null;
            FocusedStore = null;
            if (ui) ui.HidePrompt();
            return;
        }
        FocusedStore = StoreInteractionPoint.FindNearest(transform);
        if (FocusedStore)
        {
            FocusedStop = null;
            EnsureUI();
            ui.ShowPrompt(FocusedStore.prompt, true);
            if (SpacePressed()) TryUseStore();
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
        if (!Application.isPlaying || !isActiveAndEnabled || IsInteractionMenuOpen || restorePending || PlayerInventory.IsAnyOpen ||
            PlayerInventory.SpaceConsumedThisFrame || SceneLoadManager.IsLoading ||
            !movement || !movement.isActiveAndEnabled || ChatUIManager.IsChatting || UIEventSystem.IsEditingText()) return false;
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
        movement.enabled = controls.MovementWasEnabled;
        if (SceneLoadManager.TryLoadMap(destination.scenePath, boardingStop.ArrivalSpawnId, movement))
        {
            IsDestinationMenuOpen = false;
            travelPending = true;
            travelTargetPath = destination.scenePath;
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
            if (path == SceneLoadManager.CurrentMap.path || !paths.Add(path) ||
                SceneUtility.GetBuildIndexByScenePath(path) < 0 || !Application.CanStreamedLevelBeLoaded(path)) continue;
            availableDestinations.Add(destination);
        }
    }

    public bool TryUseStore()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || IsInteractionMenuOpen || restorePending ||
            SceneLoadManager.IsLoading || PlayerInventory.IsAnyOpen || PlayerInventory.SpaceConsumedThisFrame ||
            !movement || !movement.isActiveAndEnabled || ChatUIManager.IsChatting || UIEventSystem.IsEditingText()) return false;
        FocusedStore=StoreInteractionPoint.FindNearest(transform);
        if(!FocusedStore)return false;
        EnsureUI();
        if(FocusedStore.action==StoreAction.Door)
        {
            bool loaded=SceneLoadManager.TryLoadMap(FocusedStore.targetScenePath,FocusedStore.targetSpawnId,movement);
            if(loaded){waitForSpaceRelease=true;ui.HidePrompt();}
            return loaded;
        }
        var inventory=GetComponent<PlayerInventory>();
        if(!inventory || inventory.Inventory==null)return false;
        tradingWith=FocusedStore;
        SuspendControls();ui.HidePrompt();
        storeUI=StoreTradeUI.Create(new StoreTradeSession(inventory.Inventory,tradingWith.offers),uiFont,CloseStore);
        SceneManager.MoveGameObjectToScene(storeUI.gameObject,SceneLoadManager.CurrentMap);
        return true;
    }

    public void CloseStore()
    {
        if(!storeUI)return;
        storeUI.gameObject.SetActive(false);
        Destroy(storeUI.gameObject);storeUI=null;tradingWith=null;
        waitForSpaceRelease=true;restorePending=true;
    }

    string CurrentDistrictName()
    {
        foreach (var destination in destinations ?? Array.Empty<TransitDestination>())
            if (destination != null && destination.scenePath == SceneLoadManager.CurrentMap.path)
                return destination.displayName;
        return SceneLoadManager.CurrentMap.name;
    }

    void EnsureUI()
    {
        if (ui) return;
        ui = TransitUI.Create(uiFont, CloseDestinationMenu);
        SceneManager.MoveGameObjectToScene(ui.gameObject, SceneLoadManager.CurrentMap);
    }

    void SuspendControls() => controls.Hold(movement);

    void RestoreControls(bool restoreMovement) => controls.Release(movement, restoreMovement);

    void OnDisable()
    {
        IsDestinationMenuOpen = false;
        if(storeUI){Destroy(storeUI.gameObject);storeUI=null;}
        tradingWith=null;FocusedStore=null;
        FocusedStop = null;
        boardingStop = null;
        restorePending = false;
        travelPending = false;
        waitForSpaceRelease = true;
        if (ui) { ui.HidePrompt(); ui.HideModal(); }
        RestoreControls(!SceneLoadManager.IsLoading);
    }

    void OnDestroy() { if (ui) Destroy(ui.gameObject); }

    static bool SpacePressed()
    {
        return GameInput.InteractPressed;
    }

    static bool SpaceHeld()
    {
        return GameInput.InteractHeld;
    }

    static bool EscapePressed()
    {
        return GameInput.CancelPressed;
    }
}
