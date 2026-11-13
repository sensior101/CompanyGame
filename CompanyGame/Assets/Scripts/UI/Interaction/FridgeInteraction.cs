using TMPro;
using UnityEngine;

/// <summary>
/// Space near furniture storage opens the player's own inventory window with the storage slots on top (InventoryUI.Container).
/// A left click or a drag on a bag slot stores that stack; a left click on a storage slot takes it back.
/// Space, Esc, E or the window's close button close it.
/// </summary>
[DefaultExecutionOrder(-320)]
[DisallowMultipleComponent]
public sealed class FridgeInteraction : MonoBehaviour
{
    public static bool HasNearbyFridge { get; private set; }
    public static bool IsOpen => open;

    static bool open;
    PlayerMovement movement;
    PlayerInventory player;
    FridgeStorage fridge;
    GameObject promptRoot;
    TMP_Text promptLabel;
    bool pending;
    bool networkOpen;
    InventoryState subscribedStorage;
    TMP_FontAsset font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { HasNearbyFridge = false; open = false; }

    void Awake() { movement = GetComponent<PlayerMovement>(); player = GetComponent<PlayerInventory>(); }
    void OnEnable() { WorldStateSync.Answered += Answered; }
    void Answered(string kind, string json, string error)
    {
        if (!open || (kind != "p.storage.open" && kind != "p.storage.move")) return;
        var args = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SharedOpArgs>(json);
        if (args == null || !fridge || args.a != fridge.Id) return;
        pending = false;
        if (!string.IsNullOrEmpty(error)) player.SetStatus(error);
        Refresh();
    }

    void Update()
    {
        if (open)
        {
            bool cancelled = GameInput.CancelPressed || (GameInput.InteractPressed && !PlayerInventory.SpaceConsumedThisFrame);
            if (!fridge || !player.IsOpen || !fridge.InRange(transform) || networkOpen != SharedWriteGate.OnServer || SceneLoadManager.IsLoading || cancelled)
            {
                if (cancelled) PlayerInventory.ConsumeSpaceThisFrame();
                Close();
            }
            return;
        }
        HasNearbyFridge = false;
        if (CookingInteraction.IsOpen || CookingInteraction.HasNearbyStation || !movement || !movement.isActiveAndEnabled || SceneLoadManager.IsLoading ||
            SeatInteraction.HasNearbySeat || FurnitureLightInteraction.HasNearbyLight ||
            DialogueManager.HasNearbyNpc || DialogueManager.IsDialogueOpen || DialogueManager.OwnsInput ||
            PlayerInventory.IsAnyOpen || ChatUIManager.IsChatting || UIEventSystem.IsEditingText() ||
            (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) ||
            (PlayerInteraction.Local && PlayerInteraction.Local.IsInteractionMenuOpen)) { HidePrompt(); return; }
        var nearest = FridgeStorage.FindNearest(transform);
        HasNearbyFridge = nearest;
        if (!nearest) { HidePrompt(); return; }
        if (GameInput.InteractPressed && !PlayerInventory.SpaceConsumedThisFrame)
        {
            PlayerInventory.ConsumeSpaceThisFrame();
            Open(nearest);
            return;
        }
        ShowPrompt(nearest.Label + " 열기  (스페이스바)");
    }

    void Open(FridgeStorage target)
    {
        if (!player || !player.OpenInventory()) return;
        fridge = target; open = true; HidePrompt();
        subscribedStorage = fridge.Contents;
        subscribedStorage.Changed += Refresh;
        networkOpen = SharedWriteGate.OnServer;
        player.ContainerDeposit = Deposit;
        var accepting = fridge;
        player.UserInterface.ShowContainer(fridge.Label, subscribedStorage, Withdraw, stack => accepting && accepting.Accepts(stack));
        pending = SharedWriteGate.OnServer;
        if (!fridge.RequestOpen(out string error)) { pending = false; player.SetStatus(error); }
    }

    void Close()
    {
        if (!open) return;
        if (subscribedStorage != null) subscribedStorage.Changed -= Refresh;
        subscribedStorage = null;
        fridge = null; open = false; pending = false;
        if (!player) return;
        player.ContainerDeposit = null;
        if (player.UserInterface) player.UserInterface.HideContainer();
        if (player.IsOpen) player.CloseInventory();
    }

    void Refresh()
    {
        if (open && player.UserInterface) player.UserInterface.RefreshContainer();
    }

    bool Deposit(int slot, int count) => Move(slot, count, false);
    void Withdraw(int slot)
    {
        var stack = subscribedStorage?.GetSlot(slot);
        if (stack != null && !stack.IsEmpty) Move(slot, stack.Count, true);
    }

    bool Move(int slot, int count, bool withdraw)
    {
        var bag = player ? player.Inventory : null;
        if (pending || !fridge || !fridge.InRange(transform) || bag == null) return false;
        bool ok = fridge.Transfer(bag, slot, withdraw, count, out string error);
        pending = ok && SharedWriteGate.OnServer;
        if (!ok) player.SetStatus(error);
        Refresh();
        return ok;
    }

    void ShowPrompt(string text)
    {
        if (!promptRoot)
        {
            if (!font) font = PlayerInteraction.Local ? PlayerInteraction.Local.uiFont : null;
            promptRoot = new GameObject("FridgePromptCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            promptRoot.transform.SetParent(transform, false);
            var canvas = promptRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 195;
            var scaler = promptRoot.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f); scaler.matchWidthOrHeight = .5f;
            var panel = UITheme.Panel("Panel", promptRoot.transform, new Vector2(300f, 44f));
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, 0f); panel.pivot = new Vector2(.5f, 0f); panel.anchoredPosition = new Vector2(0f, 168f);
            promptLabel = UITheme.Text(panel, "", 18f, UITheme.Ink, font, Vector2.zero);
            UIBuild.Stretch(promptLabel.rectTransform);
        }
        promptLabel.text = text; promptRoot.SetActive(true);
    }

    void HidePrompt() { if (promptRoot) promptRoot.SetActive(false); }
    void OnDisable() { WorldStateSync.Answered -= Answered; HasNearbyFridge = false; HidePrompt(); if (open) Close(); }
}
