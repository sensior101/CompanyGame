using UnityEngine;

/// <summary>
/// A game-wide system: one instance, kept across map loads. A second copy destroys itself.
/// Override OnSystemAwake for setup instead of Awake.
/// </summary>
public abstract class GameSystem<T> : MonoBehaviour where T : GameSystem<T>
{
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = (T)this;
        if (!transform.parent) DontDestroyOnLoad(gameObject);
        OnSystemAwake();
    }

    protected virtual void OnSystemAwake() { }

    protected virtual void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
