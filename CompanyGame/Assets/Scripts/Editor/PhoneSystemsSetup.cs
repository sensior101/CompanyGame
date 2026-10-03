using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Adds the phone backend managers to the open scene as one root object. Safe to run repeatedly.</summary>
public static class PhoneSystemsSetup
{
    private const string RootName = "GameSystems";

    [MenuItem("CompanyGame/Setup/Add Phone Backend Systems To Open Scene")]
    public static void AddToOpenScene()
    {
        var root = GameObject.Find(RootName);
        bool createdRoot = root == null;
        if (createdRoot)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create " + RootName);
        }

        var added = new List<string>();
        Ensure<SimpleGameClock>(root, added);
        Ensure<CompanyManager>(root, added);
        Ensure<StockMarketManager>(root, added);
        Ensure<BankManager>(root, added);
        Ensure<SnsManager>(root, added);
        Ensure<ReportManager>(root, added);

        if (added.Count == 0)
        {
            if (createdRoot) Undo.DestroyObjectImmediate(root);
            Debug.Log("CompanyGame setup: every phone backend system is already in the open scene.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("CompanyGame setup: added " + string.Join(", ", added) + " to '" + RootName + "'. Save the scene.");

        if (FindAnyObject<PropertyManager>() == null)
        {
            Debug.LogWarning("CompanyGame setup: no PropertyManager in the open scene. Money features "
                + "(bank, stocks, shop) only work once a PropertyManager exists.");
        }
    }

    private static void Ensure<T>(GameObject root, List<string> added) where T : Component
    {
        if (FindAnyObject<T>() != null) return;

        Undo.AddComponent<T>(root);
        added.Add(typeof(T).Name);
    }

    private static T FindAnyObject<T>() where T : Object => Object.FindAnyObjectByType<T>();
}
