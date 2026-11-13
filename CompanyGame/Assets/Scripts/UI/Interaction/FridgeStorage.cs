using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Existing fridge component generalized to filtered furniture storage. Inventory and saving remain shared services.</summary>
[DisallowMultipleComponent]
public sealed class FridgeStorage : MonoBehaviour
{
    public const int Slots = InventoryState.BaseCapacity;
    [Min(.5f)] public float interactionRadius = 2f;
    public StorageType storageType = StorageType.Food;
    public bool refrigerated = true;
    public string storageId;
    static readonly HashSet<FridgeStorage> active = new();
    public string Id => string.IsNullOrEmpty(storageId) ? gameObject.scene.path + ":" + PathOf(transform) : storageId;
    public string Label => storageType == StorageType.Clothing ? "옷장" : refrigerated ? "냉장고" : storageType == StorageType.Books ? "책장" : "보관함";
    public string FilterLabel => storageType == StorageType.Clothing ? "의류" : storageType == StorageType.Food ? "음식·음료" : storageType == StorageType.Books ? "책" : "모든 아이템";
    public InventoryState Contents
    {
        get
        {
            bool network = SharedWriteGate.OnServer;
            bool migrate = !network && !FurnitureStorageRules.HasOffline(Id);
            var state = FurnitureStorageRules.Contents(Id, network);
            if (migrate)
            {
                string legacy = "fridge:" + gameObject.scene.name + ":" + LegacyPath(transform);
                InventorySnapshot.Import(state, PlayerPrefs.GetString(legacy, ""));
            }
            return state;
        }
    }
    public bool Accepts(ItemData item) => FurnitureStorageRules.Accepts(storageType, item);
    /// <summary>Everything a deposit would refuse before trying it, library loans included.</summary>
    public bool Accepts(ItemStack stack) => stack != null && !stack.IsEmpty && Accepts(stack.Item) && !(stack.IsUniqueBook && stack.BookData.IsLibraryLoan);
    public bool InRange(Transform player)
    {
        if (!player || !isActiveAndEnabled || gameObject.scene != SceneLoadManager.CurrentMap) return false;
        Vector3 delta = player.position - transform.position;
        return Mathf.Abs(delta.y) < 1.5f && new Vector2(delta.x, delta.z).sqrMagnitude <= interactionRadius * interactionRadius;
    }
    public bool RequestOpen(out string error)
    {
        error = null;
        if (!SharedWriteGate.OnServer) return true;
        if (SharedWriteGate.TrySend("p.storage.open", new SharedOpArgs { a = Id })) return true;
        error = "서버에 연결할 수 없어요."; return false;
    }
    public bool Transfer(InventoryState bag, int slot, bool withdraw, out string error) => Transfer(bag, slot, withdraw, 0, out error);
    /// <summary>count 0 moves the whole stack.</summary>
    public bool Transfer(InventoryState bag, int slot, bool withdraw, int count, out string error)
    {
        var from = withdraw ? Contents : bag; var stack = from?.GetSlot(slot);
        if (stack == null || stack.IsEmpty) { error = "아이템을 선택해 주세요."; return false; }
        if (count <= 0) count = stack.Count;
        if (SharedWriteGate.OnServer)
        {
            bool sent = SharedWriteGate.TrySend("p.storage.move", new SharedOpArgs { a = Id, b = FurnitureStorageRules.Fingerprint(stack), c = slot.ToString(System.Globalization.CultureInfo.InvariantCulture), n = count, f = withdraw });
            error = sent ? null : "서버에 연결할 수 없어요."; return sent;
        }
        bool ok = FurnitureStorageRules.Transfer(storageType, refrigerated, withdraw, bag, Contents, slot, count, FurnitureStorageRules.Fingerprint(stack), out error);
        if (ok) WorldSave.SaveToDisk();
        return ok;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => active.Clear();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) Attach(SceneManager.GetSceneAt(i));
    }
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);
    static void Attach(Scene scene)
    {
        if (!scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponentInParent<FridgeStorage>(true)) continue;
            var world = t.GetComponent<WorldObject>();
            bool fridge = t.name.StartsWith("Fridge", System.StringComparison.OrdinalIgnoreCase) || t.name.StartsWith("Kitchen_Fridge", System.StringComparison.OrdinalIgnoreCase);
            if (!fridge && !(world && world.HasFunction(FurnitureFunction.Storage))) continue;
            var storage = t.gameObject.AddComponent<FridgeStorage>();
            if (world && world.furnitureItem)
            {
                storage.storageType = world.furnitureItem.storageType == StorageType.None ? StorageType.General : world.furnitureItem.storageType;
                storage.refrigerated = storage.storageType == StorageType.Food;
            }
        }
    }
    void OnEnable() => active.Add(this);
    void OnDisable() => active.Remove(this);
    // Cabinets stand right behind office staff: whichever is closer owns Space, as with chairs (SeatInteraction).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void YieldDialogueToCloserStorage()
    {
        DialogueManager.EnsureInstance();
        DialogueManager.Instance.YieldTo = (player, npc) =>
        {
            var storage = FindNearest(player);
            return storage && (storage.transform.position - player.position).sqrMagnitude < (npc.transform.position - player.position).sqrMagnitude;
        };
    }

    public static FridgeStorage FindNearest(Transform player)
    {
        FridgeStorage best = null; float distance = float.MaxValue;
        foreach (var storage in active)
        {
            if (!storage || !storage.InRange(player)) continue;
            float d = (storage.transform.position - player.position).sqrMagnitude;
            if (d < distance) { best = storage; distance = d; }
        }
        return best;
    }
    static string PathOf(Transform t) => (t.parent ? PathOf(t.parent) + "/" : "") + t.name + "[" + t.GetSiblingIndex() + "]";
    static string LegacyPath(Transform t) => t.parent ? LegacyPath(t.parent) + "/" + t.name + t.GetSiblingIndex() : t.name;
}
