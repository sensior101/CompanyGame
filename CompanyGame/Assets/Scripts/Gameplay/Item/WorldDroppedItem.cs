using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A pickable world stack. Session records survive Single-scene travel; each visual
/// and collider belongs only to its map. Pickup removes the record after an atomic transfer.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldDroppedItem : MonoBehaviour
{
    sealed class DropRecord
    {
        public string id;
        public string scenePath;
        public Vector3 position;
        public InventoryState contents = new InventoryState();
        public TMP_FontAsset font;
        public WorldDroppedItem instance;
    }
    static readonly Dictionary<string, DropRecord> records = new Dictionary<string, DropRecord>();
    DropRecord record;
    Transform billboard;
    RectTransform pickupPopup;
    TMP_Text pickupText;

    public ItemData Item => record?.contents.GetSlot(0)?.Item;
    public int Count => record?.contents.GetSlot(0)?.Count ?? 0;
    public long CurrencyTotal => Item && Item.IsCurrency ? checked(Item.CurrencyValue * Count) : 0L;
    public string DropId => record?.id;
    public static int SessionDropCount => records.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        records.Clear();
        SceneManager.sceneLoaded -= RestoreSceneDrops;
        SceneManager.sceneLoaded += RestoreSceneDrops;
    }

    static void RestoreSceneDrops(Scene scene, LoadSceneMode mode)
    {
        foreach (var entry in records.Values)
            if (entry.scenePath == scene.path && !entry.instance) CreateVisual(entry, scene);
    }

    public static bool TryDropStorage(PlayerInventory owner, int index, out string error,int count=-1)
    {
        error = null;
        var source = owner ? owner.Inventory.GetSlot(index) : null;
        if (source == null || source.IsEmpty) { error = "내려놓을 아이템이 없습니다."; return false; }
        if (!Prepare(owner, out var drop, out error)) return false;
        if (!owner.Inventory.TryTransferTo(drop.contents, index, count<0?source.Count:count, out error)) return false;
        Commit(drop, SceneLoadManager.CurrentMap);
        return true;
    }

    public static bool TryDropEquipment(PlayerInventory owner, EquipmentSlot slot, out string error)
    {
        error = null;
        var source = owner ? owner.Inventory.GetEquipment(slot) : null;
        if (source == null || source.IsEmpty) { error = "내려놓을 장비가 없습니다."; return false; }
        if (!Prepare(owner, out var drop, out error)) return false;
        // Prepare the destination first. A failed removal leaves the actual equipment unchanged.
        if (!drop.contents.TryAdd(source.Item, source.Count, out error)) return false;
        if (!owner.Inventory.TryRemoveEquipment(slot, out error)) return false;
        Commit(drop, SceneLoadManager.CurrentMap);
        return true;
    }

    static bool Prepare(PlayerInventory owner, out DropRecord drop, out string error)
    {
        drop = null;
        error = null;
        if (!owner || !owner.IsOpen || SceneLoadManager.IsLoading ||
            (owner.UserInterface && owner.UserInterface.IsWithdrawalOpen))
        { error = "지금은 내려놓을 수 없습니다."; return false; }
        if (!TryFindDropPoint(owner, out var position)) { error = "앞에 아이템을 놓을 바닥이 없습니다."; return false; }
        drop = new DropRecord
        {
            id = Guid.NewGuid().ToString("N"),
            scenePath = SceneLoadManager.CurrentMap.path,
            position = position,
            font = owner.uiFont
        };
        return true;
    }

    static void Commit(DropRecord drop, Scene scene)
    {
        records.Add(drop.id, drop);
        CreateVisual(drop, scene);
    }

    public bool TryPickUp(PlayerInventory owner, out string error)
    {
        error = null;
        if (!owner || !owner.CanPickUpWorldItems || record == null ||
            !records.ContainsKey(record.id) || gameObject.scene != SceneLoadManager.CurrentMap)
        { error = "지금은 주울 수 없습니다."; return false; }
        if (!IsReachableFrom(owner, 4f))
        { error = "아이템에 조금 더 가까이 가 주세요."; return false; }
        int quantity = Count;
        if (quantity <= 0) { error = "이미 주운 아이템입니다."; return false; }
        if (!record.contents.TryTransferTo(owner.Inventory, 0, quantity, out error)) return false;
        records.Remove(record.id);
        record.instance = null;
        record = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }

    public bool IsReachableFrom(PlayerInventory owner, float maximumDistance)
    {
        if (!owner || gameObject.scene != SceneLoadManager.CurrentMap ||
            Vector3.Distance(owner.transform.position, transform.position) > maximumDistance) return false;
        Vector3 origin = owner.transform.position + Vector3.up * .8f;
        Vector3 delta = transform.position + Vector3.up * .24f - origin;
        foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == owner.transform || hit.transform.IsChildOf(owner.transform) ||
                hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            return false;
        }
        return true;
    }

    static bool TryFindDropPoint(PlayerInventory owner, out Vector3 result)
    {
        result = default;
        var forward = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
        Vector3 origin = owner.transform.position + Vector3.up * .8f;
        float reach = 1.4f;
        var blocks = Physics.SphereCastAll(origin, .16f, forward, reach, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in blocks)
        {
            if (hit.transform == owner.transform || hit.transform.IsChildOf(owner.transform)) continue;
            if (hit.normal.y > .6f) continue;
            reach = Mathf.Min(reach, hit.distance - .2f);
        }
        // Never force a minimum distance through a nearby wall.
        if (reach < .4f) return false;
        var point = owner.transform.position + forward * reach;
        var floors = Physics.RaycastAll(point + Vector3.up * 1.4f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(floors, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var floor in floors)
        {
            if (floor.transform == owner.transform || floor.transform.IsChildOf(owner.transform) || floor.normal.y < .6f) continue;
            if (Mathf.Abs(floor.point.y - owner.transform.position.y) > 1.2f) continue;
            bool blocked = false;
            foreach (var obstacle in Physics.OverlapSphere(floor.point + Vector3.up * .32f, .24f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (obstacle == floor.collider || obstacle.transform == owner.transform || obstacle.transform.IsChildOf(owner.transform)) continue;
                blocked = true;
                break;
            }
            if (blocked) continue;
            result = floor.point + Vector3.up * .065f;
            return true;
        }
        return false;
    }

    static void CreateVisual(DropRecord drop, Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || drop.contents.GetSlot(0).IsEmpty) return;
        var host = new GameObject("DroppedItem_" + drop.id);
        host.transform.position = drop.position;
        SceneManager.MoveGameObjectToScene(host, scene);
        var component = host.AddComponent<WorldDroppedItem>();
        component.record = drop;
        drop.instance = component;
        component.BuildVisual();
    }

    void BuildVisual()
    {
        var physics = gameObject.AddComponent<Rigidbody>();
        physics.isKinematic = true;
        physics.useGravity = false;
        var collider = gameObject.AddComponent<BoxCollider>();
        collider.center = new Vector3(0, .24f, 0);
        collider.size = new Vector3(.62f, .62f, .62f);
        collider.isTrigger = true;
        billboard = new GameObject("ItemBillboard").transform;
        billboard.SetParent(transform, false);
        billboard.localPosition = Vector3.up * .33f;
        BuildItemIcon();
        BuildPickupPopup();
    }

    void LateUpdate()
    {
        var camera = Camera.main;
        if (billboard && camera) billboard.rotation = Quaternion.LookRotation(camera.transform.forward, camera.transform.up);
        UpdatePickupPopup(camera);
    }

    void BuildPickupPopup()
    {
        var canvasObject = new GameObject("PickupPopup", typeof(RectTransform), typeof(Canvas));
        canvasObject.transform.SetParent(billboard, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 25;
        pickupPopup = canvasObject.GetComponent<RectTransform>();
        pickupPopup.sizeDelta = new Vector2(240f, 42f);
        pickupPopup.localScale = Vector3.one * .009f;

        var panelObject = new GameObject("Panel", typeof(RectTransform), typeof(InventoryRoundedGraphic));
        var panel = panelObject.GetComponent<RectTransform>();
        panel.SetParent(pickupPopup, false);
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;
        var graphic = panelObject.GetComponent<InventoryRoundedGraphic>();
        graphic.color = new Color(.08f, .07f, .065f, .70f);
        graphic.borderColor = new Color(1f, .96f, .88f, .42f);
        graphic.largeRadius = 18f;
        graphic.smallRadius = 9f;
        graphic.borderWidth = 1.5f;
        graphic.raycastTarget = false;

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(panel, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 3f);
        textRect.offsetMax = new Vector2(-8f, -3f);
        pickupText = textObject.GetComponent<TextMeshProUGUI>();
        if (record.font) pickupText.font = record.font;
        pickupText.fontSize = 19f;
        pickupText.color = new Color(1f, .98f, .93f, .98f);
        pickupText.alignment = TextAlignmentOptions.Center;
        pickupText.textWrappingMode = TextWrappingModes.NoWrap;
        pickupText.overflowMode = TextOverflowModes.Ellipsis;
        pickupText.raycastTarget = false;
        pickupText.text = PickupPromptText();
        pickupPopup.gameObject.SetActive(false);
    }

    void UpdatePickupPopup(Camera camera)
    {
        if (!pickupPopup || record == null || Count <= 0)
        {
            if (pickupPopup) pickupPopup.gameObject.SetActive(false);
            return;
        }
        if (camera)
        {
            var canvas = pickupPopup.GetComponent<Canvas>();
            if (canvas) canvas.worldCamera = camera;
        }
        bool visible = false;
        foreach (var player in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None))
        {
            if (!player || gameObject.scene != SceneLoadManager.CurrentMap || !player.CanPickUpWorldItems) continue;
            if (!IsReachableFrom(player, 3f)) continue;
            visible = true;
            break;
        }
        if (pickupText && visible) pickupText.text = PickupPromptText();
        if (pickupPopup.gameObject.activeSelf != visible) pickupPopup.gameObject.SetActive(visible);
    }

    string PickupPromptText()
    {
        return (Item ? Item.DisplayName : "아이템") + "   [줍기 F]";
    }

    void BuildItemIcon()
    {
        if (!Item || !Item.icon) return;
        if (Item.heldPrefab && Item.icon.texture.name == "StoreFoods")
        {
            var model = Instantiate(Item.heldPrefab, billboard, false);
            model.name = "DroppedFoodModel";
            model.transform.localPosition = new Vector3(0f, -.1f, 0f);
            model.transform.localRotation = Quaternion.Euler(0, 18, 0);
            model.transform.localScale = Vector3.one * 1.4f;
            return;
        }
        var iconHost = new GameObject("DroppedItemIcon", typeof(SpriteRenderer));
        iconHost.transform.SetParent(billboard, false);
        iconHost.transform.localPosition = new Vector3(0f, -.28f, 0f);
        var renderer = iconHost.GetComponent<SpriteRenderer>();
        renderer.sprite = Item.icon;
        renderer.color = Color.white;
        renderer.sortingOrder = 24;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        float width = Mathf.Max(.001f, Item.icon.bounds.size.x);
        iconHost.transform.localScale = Vector3.one * (.42f / width);
    }

    void OnDestroy()
    {
        if (record != null && record.instance == this) record.instance = null;
    }
}
