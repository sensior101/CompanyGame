using UnityEngine;

/// <summary>Owns the session inventory across single-scene map loads.</summary>
[DefaultExecutionOrder(-400)]
public class InventoryManager : MonoBehaviour
{
    static InventoryManager instance;
    static bool quitting;
    InventoryState state;
    public InventoryState State => state ?? (state = new InventoryState());

    public static InventoryManager Instance
    {
        get
        {
            if (!instance && Application.isPlaying && !quitting)
                instance = new GameObject("Inventory Session").AddComponent<InventoryManager>();
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; quitting = false; }

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
    // On a sync-layer server the server gives it as the character enters (12.10.5), never this client.
    public void GrantCityBicycles()
    {
        if (InventoryLedgerRules.OnServer) return;
        InventoryLedgerRules.GrantStartVehicles(State);
    }

    void SaveBooks() => BookSaveService.SaveInventory(state);
    void OnApplicationQuit() { quitting = true; if (instance == this) SaveBooks(); }
    protected virtual void OnDestroy()
    {
        if (state != null) state.Changed -= SaveBooks;
        if (instance == this) instance = null;
    }
}
