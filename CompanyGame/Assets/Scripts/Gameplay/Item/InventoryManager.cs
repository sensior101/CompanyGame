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
            Destroy(this);
            return;
        }
        DontDestroyOnLoad(gameObject);
        BookSaveService.RestoreInventory(state);
        state.Changed += SaveBooks;
        LibraryCatalog.ConnectLocal(state);
    }

    void SaveBooks() => BookSaveService.SaveInventory(state);
    void OnApplicationQuit() { if (instance == this) SaveBooks(); }
    protected virtual void OnDestroy()
    {
        if (state != null) state.Changed -= SaveBooks;
        if (instance == this) instance = null;
    }
}
