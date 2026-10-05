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

    /// <summary>The local player's interaction, for opening the trade window from dialogue or other code.</summary>
    public static PlayerInteraction Local { get; private set; }

    public bool IsDestinationMenuOpen { get; private set; }
    public bool IsTradeOpen => tradeUI;
    public bool IsInteractionMenuOpen => IsDestinationMenuOpen || IsTradeOpen;
    public StoreInteractionPoint FocusedStore { get; private set; }
    public NpcTrader FocusedTrader { get; private set; }
    public bool HasNearbyAction => !IsInteractionMenuOpen && (SeatInteraction.HasNearbySeat || FurnitureLightInteraction.HasNearbyLight || DialogueManager.HasNearbyNpc || StoreInteractionPoint.FindNearest(transform) || NpcTrader.FindNearest(transform));
    public TradeWindow TradeUI => tradeUI;
    public TransitStop FocusedStop { get; private set; }
    public bool IsMenuReady => IsDestinationMenuOpen && menuArmed;

    PlayerMovement movement;
    TransitUI ui;
    TradeWindow tradeUI;
    Func<bool> tradeStillAvailable;
    TransitStop boardingStop;
    readonly List<TransitDestination> availableDestinations = new List<TransitDestination>();
    readonly ControlLock controls = new ControlLock();
    bool restorePending;
    bool travelPending;
    string travelTargetPath;
    bool menuArmed;
    bool waitForSpaceRelease = true;
    int menuOpenedFrame;

    void Awake() { movement = GetComponent<PlayerMovement>(); Local = this; }

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
        if (IsTradeOpen)
        {
            if ((tradeStillAvailable != null && !tradeStillAvailable()) || EscapePressed()) CloseTrade();
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
        if (SeatInteraction.HasNearbySeat || FurnitureLightInteraction.HasNearbyLight || DialogueManager.HasNearbyNpc || DialogueManager.IsDialogueOpen || DialogueManager.OwnsInput ||
            !movement || !movement.isActiveAndEnabled || PlayerInventory.IsAnyOpen || PlayerInventory.SpaceConsumedThisFrame ||
            ChatUIManager.IsChatting || UIEventSystem.IsEditingText())
        {
            FocusedStop = null;
            FocusedStore = null;
            FocusedTrader = null;
            if (ui) ui.HidePrompt();
            return;
        }
        FocusedStore = StoreInteractionPoint.FindNearest(transform);
        FocusedTrader = FocusedStore ? null : NpcTrader.FindNearest(transform);
        if (FocusedStore || FocusedTrader)
        {
            FocusedStop = null;
            EnsureUI();
            ui.ShowPrompt(FocusedStore ? FocusedStore.prompt : FocusedTrader.prompt, true);
            if (SpacePressed()) { if (FocusedStore) TryUseDoor(); else TryTradeWithNearest(); }
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

    bool CanStartInteraction() =>
        Application.isPlaying && isActiveAndEnabled && !IsInteractionMenuOpen && !restorePending &&
        !SceneLoadManager.IsLoading && !PlayerInventory.IsAnyOpen && !PlayerInventory.SpaceConsumedThisFrame &&
        movement && movement.isActiveAndEnabled && !ChatUIManager.IsChatting && !UIEventSystem.IsEditingText();

    public bool TryUseDoor()
    {
        if (!CanStartInteraction()) return false;
        FocusedStore = StoreInteractionPoint.FindNearest(transform);
        if (!FocusedStore) return false;
        if (string.IsNullOrEmpty(FocusedStore.targetScenePath))
        {
            // Empty target scene: move inside the current map.
            bool moved = PlayerSpawner.TeleportInScene(FocusedStore.targetSpawnId);
            if (moved) { waitForSpaceRelease = true; if (ui) ui.HidePrompt(); }
            return moved;
        }
        EnsureUI();
        bool loaded = SceneLoadManager.TryLoadMap(FocusedStore.targetScenePath, FocusedStore.targetSpawnId, movement);
        if (loaded) { waitForSpaceRelease = true; ui.HidePrompt(); }
        return loaded;
    }

    public bool TryTradeWithNearest()
    {
        var trader = NpcTrader.FindNearest(transform);
        return trader && OpenTrade(trader.offers, () => trader && trader.IsInRange(transform));
    }

    /// <summary>
    /// Opens the shared trade window. Any NPC, dialogue or quest can call this.
    /// stillAvailable closes the window when it turns false (e.g. the player walked away).
    /// </summary>
    public bool OpenTrade(TradeOffer[] offers, Func<bool> stillAvailable = null)
    {
        if (!CanStartInteraction() || offers == null) return false;
        var inventory = InventoryManager.Instance ? InventoryManager.Instance.State : null;
        if (inventory == null) return false;
        EnsureUI();
        tradeStillAvailable = stillAvailable;
        SuspendControls(); ui.HidePrompt();
        tradeUI = TradeWindow.Create(new TradeSession(inventory, offers), uiFont, CloseTrade);
        SceneManager.MoveGameObjectToScene(tradeUI.gameObject, SceneLoadManager.CurrentMap);
        return true;
    }

    public void CloseTrade()
    {
        if (!tradeUI) return;
        tradeUI.gameObject.SetActive(false);
        Destroy(tradeUI.gameObject); tradeUI = null; tradeStillAvailable = null;
        waitForSpaceRelease = true; restorePending = true;
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
        if (tradeUI) { Destroy(tradeUI.gameObject); tradeUI = null; }
        tradeStillAvailable = null; FocusedStore = null; FocusedTrader = null;
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
