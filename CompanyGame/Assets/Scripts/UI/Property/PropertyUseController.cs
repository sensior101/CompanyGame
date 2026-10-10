using System;
using System.Collections.Generic;
using CompanyGame.World.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Holding a deed: Space taps register it, Space held for 5 s abandons it. Also seeds zone owners,
/// logs trespassing and hands out the one-time starting deed.
/// </summary>
[DefaultExecutionOrder(-250)]
[DisallowMultipleComponent]
public sealed class PropertyUseController : MonoBehaviour
{
    enum State { Idle, Holding, WaitRelease }

    const float HoldSeconds = 5f;
    const float GaugeDelay = .25f;
    const string StartingDeedPrefix = "goshiwon_";

    State state;
    float held;
    ItemData heldDeed;
    bool startingDeedDone;
    float nextGrantTime;
    readonly List<PropertyZone> left = new List<PropertyZone>();
    readonly HashSet<PropertyZone> seeded = new HashSet<PropertyZone>();
    readonly HashSet<PropertyZone> inside = new HashSet<PropertyZone>();
    readonly List<PropertyZone> entered = new List<PropertyZone>();
    GameObject gauge;
    RectTransform fill;

    void Update()
    {
        if (SceneLoadManager.IsLoading)
        {
            inside.Clear();
            Cancel();
            return;
        }
        var registry = PropertyRegistry.Instance;
        if (!registry) return;
        SeedZones(registry);
        DetectTrespass(registry);
        GrantStartingDeed(registry);
        UpdateDeed(registry);
    }

    void SeedZones(PropertyRegistry registry)
    {
        foreach (var zone in PropertyZone.Active)
            if (seeded.Add(zone) && !string.IsNullOrEmpty(zone.seedOwner)) registry.SeedOwner(zone.propertyId, zone.seedOwner);
    }

    void DetectTrespass(PropertyRegistry registry)
    {
        var pos = transform.position;
        entered.Clear();
        foreach (var zone in PropertyZone.Active)
            if (zone.Contains(pos) && inside.Add(zone)) entered.Add(zone);
        left.Clear();
        foreach (var zone in inside)
            if (!zone || !zone.Contains(pos)) left.Add(zone);
        foreach (var zone in left) inside.Remove(zone);
        foreach (var zone in entered)
        {
            string owner = registry.GetOwner(zone.propertyId);
            if (owner != null && owner != GameSession.LocalPlayerName)
                ReportManager.Instance?.NotifyCrime(CrimeType.Trespass, GameSession.LocalPlayerName, zone.displayName);
        }
    }

    // Once per session; waits (retrying each frame) until the inventory exists.
    void GrantStartingDeed(PropertyRegistry registry)
    {
        var inventory = InventoryManager.Instance ? InventoryManager.Instance.State : null;
        if (startingDeedDone || inventory == null || Time.unscaledTime < nextGrantTime) return;
        for (int i = 0; i < inventory.Capacity; i++)
        {
            var stack = inventory.GetSlot(i);
            if (stack != null && !stack.IsEmpty && IsStartingDeed(stack.Item)) { startingDeedDone = true; return; }
        }
        startingDeedDone = true;
        var deeds = new List<ItemData>(Resources.LoadAll<ItemData>("Inventory/Deeds"));
        deeds.Sort((a, b) => string.CompareOrdinal(a.itemId, b.itemId));
        foreach (var deed in deeds)
            if (IsStartingDeed(deed) && registry.GetOwner(deed.propertyId) == null)
            {
                if (!inventory.TryAdd(deed, 1, out _)) { startingDeedDone = false; nextGrantTime = Time.unscaledTime + 1f; }
                return;
            }
    }

    static bool IsStartingDeed(ItemData item) =>
        item && item.IsDeed && item.propertyId.StartsWith(StartingDeedPrefix, StringComparison.Ordinal);

    void UpdateDeed(PropertyRegistry registry)
    {
        var inventory = InventoryManager.Instance ? InventoryManager.Instance.State : null;
        var stack = inventory?.GetSlot(inventory.SelectedHotbarIndex);
        var deed = stack != null && !stack.IsEmpty && stack.Item.IsDeed ? stack.Item : null;
        var interaction = PlayerInteraction.Local;
        bool gate = !DialogueManager.OwnsInput && !BookReader.BlocksInventoryInput && deed && interaction && !PlayerInventory.IsAnyOpen && !ChatUIManager.IsChatting &&
            !UIEventSystem.IsEditingText() && !interaction.IsInteractionMenuOpen && !interaction.HasNearbyAction &&
            !(PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) && !TransitStop.FindNearest(transform);
        if (state == State.WaitRelease) { if (!GameInput.InteractHeld) state = State.Idle; return; }
        if (!gate || (state == State.Holding && (deed != heldDeed || !GameInput.InteractHeld ||
            registry.GetOwner(deed.propertyId) != GameSession.LocalPlayerName))) { Cancel(); return; }
        if (state == State.Idle) { if (GameInput.InteractPressed) Press(registry, deed); }
        else Hold(registry, deed);
    }

    void Press(PropertyRegistry registry, ItemData deed)
    {
        string owner = registry.GetOwner(deed.propertyId);
        string me = GameSession.LocalPlayerName;
        PlayerInventory.ConsumeSpaceThisFrame();
        if (owner == me) { state = State.Holding; heldDeed = deed; held = 0f; return; }
        state = State.WaitRelease;
        if (owner != null) Say(deed.propertyName + "은(는) 이미 다른 플레이어(" + NoParse(owner) + ")가 소유하고 있습니다.");
        else if (registry.TryRegister(deed.propertyId, me)) Say(deed.propertyName + "의 소유권을 등록했습니다.");
    }

    void Hold(PropertyRegistry registry, ItemData deed)
    {
        PlayerInventory.ConsumeSpaceThisFrame();
        held += Time.unscaledDeltaTime;
        if (held >= HoldSeconds)
        {
            registry.TryAbandon(deed.propertyId, GameSession.LocalPlayerName);
            Say(deed.propertyName + "의 소유권을 포기했습니다.");
            state = State.WaitRelease;
            HideGauge();
        }
        else if (held >= GaugeDelay) ShowGauge(held / HoldSeconds);
    }

    void Cancel()
    {
        if (state == State.Holding) state = State.Idle;
        HideGauge();
    }

    // The chat popup renders TMP rich text; keep names literal.
    static string NoParse(string name) => "<noparse>" + name + "</noparse>";

    static void Say(string message)
    {
        foreach (var chat in FindObjectsByType<ChatUIManager>())
            if (chat) chat.ShowSystemMessage(message);
    }

    void ShowGauge(float amount)
    {
        if (!gauge) BuildGauge();
        fill.anchorMax = new Vector2(amount, 1f); // Image.Filled is ignored without a sprite
        gauge.SetActive(true);
    }

    void HideGauge() { if (gauge) gauge.SetActive(false); }

    void BuildGauge()
    {
        var host = new GameObject("DeedGaugeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.transform.SetParent(transform, false);
        var canvas = host.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 190;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = .5f;
        var back = UIBuild.Rect("Gauge", host.transform, new Vector2(300f, 54f));
        gauge = back.gameObject;
        back.anchorMin = back.anchorMax = new Vector2(.5f, 0f);
        back.pivot = new Vector2(.5f, 0f);
        back.anchoredPosition = new Vector2(0f, 148f);
        gauge.AddComponent<Image>().color = new Color(.075f, .12f, .17f, .92f);
        var bar = UIBuild.Rect("Bar", back, new Vector2(276f, 8f));
        bar.anchoredPosition = new Vector2(0f, -16f);
        bar.gameObject.AddComponent<Image>().color = new Color(.19f, .25f, .3f);
        var fillRect = UIBuild.Rect("Amount", bar, Vector2.zero);
        UIBuild.Stretch(fillRect);
        fill = fillRect;
        fillRect.gameObject.AddComponent<Image>().color = new Color(.3f, .88f, .72f);
        var label = UIBuild.Rect("Label", back, new Vector2(276f, 30f));
        label.anchoredPosition = new Vector2(0f, 8f);
        var text = label.gameObject.AddComponent<TextMeshProUGUI>();
        var interaction = PlayerInteraction.Local;
        text.font = interaction && interaction.uiFont ? interaction.uiFont : TMP_Settings.defaultFontAsset;
        text.text = "소유권 포기 중…";
        text.fontSize = 20f;
        text.color = new Color(.94f, .96f, .97f);
        text.alignment = TextAlignmentOptions.Center;
        foreach (var graphic in host.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
        gauge.SetActive(false);
    }

    void OnDisable() { Cancel(); inside.Clear(); }
}
