using UnityEngine;

/// <summary>Owns the session inventory across single-scene map loads.</summary>
[DefaultExecutionOrder(-400)]
public class InventoryManager : MonoBehaviour
{
    static InventoryManager instance;
    InventoryState state;
    public InventoryState State => state ?? (state = new InventoryState());

    public static InventoryManager Instance
    {
        get
        {
            if (!instance && Application.isPlaying)
                instance = new GameObject("Inventory Session").AddComponent<InventoryManager>();
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    protected virtual void Awake()
    {
        if (instance && instance != this) { Destroy(this); return; }
        instance = this;
        state = new InventoryState();
        // Legacy scenes may attach InventoryManager to their systems root.
        // Keep only a dedicated inventory host across maps, never the whole map.
        if (transform.parent || GetComponents<Component>().Length > 2 || transform.childCount > 0)
        {
            instance = null;
            InventoryManager host = Instance;
            host.GrantCityBicycles();
            Destroy(this);
            return;
        }
        DontDestroyOnLoad(gameObject);
        GrantCityBicycles();
        BookSaveService.RestoreInventory(state);
        state.Changed += SaveBooks;
        LibraryCatalog.ConnectLocal(state);
    }

    // Temporary traversal kit requested for this development build. One of each colour per session.
    public void GrantCityBicycles()
    {
        var items = Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle");
        System.Array.Sort(items, (a,b) => string.CompareOrdinal(a.itemId,b.itemId));
        foreach (var item in items)
        {
            bool owned = false;
            for (int i = 0; i < State.Capacity; i++)
                if (State.GetSlot(i).Item == item) { owned = true; break; }
            if (!owned && !State.TryAdd(item,1,out string error)) Debug.LogWarning(error);
        }
    }

    void SaveBooks() => BookSaveService.SaveInventory(state);
    void OnApplicationQuit() { if (instance == this) SaveBooks(); }
    protected virtual void OnDestroy()
    {
        if (state != null) state.Changed -= SaveBooks;
        if (instance == this) instance = null;
    }
}
