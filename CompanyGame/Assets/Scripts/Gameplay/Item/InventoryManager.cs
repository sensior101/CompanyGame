using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns an empty, session-long inventory across single-scene map loads.</summary>
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
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= AttachScenePlayers;
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= AttachScenePlayers;
        SceneManager.sceneLoaded += AttachScenePlayers;
        AttachScenePlayers(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void AttachScenePlayers(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var player in root.GetComponentsInChildren<PlayerMovement>(true))
            if (!player.GetComponent<PlayerInventory>()) player.gameObject.AddComponent<PlayerInventory>();
    }

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
            host.state = state;
            Destroy(this);
            return;
        }
        DontDestroyOnLoad(gameObject);
    }

    protected virtual void OnDestroy() { if (instance == this) instance = null; }
}
