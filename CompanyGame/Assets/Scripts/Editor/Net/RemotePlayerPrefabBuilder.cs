using System.IO;
using CompanyGame.Daldongne;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds Assets/Resources/Net/RemotePlayer.prefab from Resources/Player: the look and DaldongneAvatarMotion
/// stay, everything that drives or collides with the local player is removed. Safe to run again (overwrites).
/// </summary>
public static class RemotePlayerPrefabBuilder
{
    const string SourcePath = "Assets/Resources/Player.prefab";
    const string TargetPath = "Assets/Resources/Net/RemotePlayer.prefab";

    [MenuItem("CompanyGame/Net/Build RemotePlayer Prefab")]
    public static void Build()
    {
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath))
        {
            Debug.LogError("[RemotePlayer] Missing " + SourcePath);
            return;
        }
        var root = PrefabUtility.LoadPrefabContents(SourcePath);
        try
        {
            PickVisual(root);
            Strip(root);
            root.name = "RemotePlayer";
            Directory.CreateDirectory(Path.GetDirectoryName(TargetPath));
            PrefabUtility.SaveAsPrefabAsset(root, TargetPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("[RemotePlayer] Wrote " + TargetPath);
    }

    // Remote avatars all use the male look (the position message carries no appearance).
    static void PickVisual(GameObject root)
    {
        var look = root.GetComponentInChildren<DaldongnePlayerAppearance>(true);
        if (!look) return;
        if (look.male) look.male.SetActive(true);
        if (look.female && look.male) look.female.SetActive(false);
    }

    static void Strip(GameObject root)
    {
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
        // Several passes: a component cannot be removed while another one requires it.
        for (int pass = 0; pass < 4; pass++)
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component && IsLocalOnly(component)) Object.DestroyImmediate(component);
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            if (canvas) Object.DestroyImmediate(canvas.gameObject);
    }

    static bool IsLocalOnly(Component c)
    {
        if (c is DaldongneAvatarMotion) return false;
        return c is MonoBehaviour || c is CharacterController || c is Collider || c is Rigidbody ||
               c is Camera || c is AudioListener || c is AudioSource || c is Light;
    }
}
