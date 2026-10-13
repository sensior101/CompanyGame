using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tells SyncClient the current map (SceneLoadManager.CurrentMap.path, the same key NetPlayerSync uses).
/// Lives here because Net cannot reference World.
/// </summary>
static class SyncMapBinder
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bind()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        Apply();
    }

    static void OnActiveSceneChanged(Scene from, Scene to) => Apply();

    static void Apply()
    {
        if (SyncClient.Instance) SyncClient.Instance.SetMap(SceneLoadManager.CurrentMap.path);
    }
}
