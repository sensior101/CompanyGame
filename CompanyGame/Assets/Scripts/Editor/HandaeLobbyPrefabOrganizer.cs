using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Stores each lobby assembly in its own prefab while retaining scene positions and references.</summary>
public static class HandaeLobbyPrefabOrganizer
{
    const string ScenePath = HandaeLobbyBuilder.ScenePath;
    const string Prefabs = "Assets/Art/WorldDistricts/HandaeHQ/Lobby/Prefabs";

    static readonly string[] GroupNames =
    {
        "01_Entrance", "02_Reception", "03_Planters", "04_LoungeArmchairs",
        "05_WaitingSofaAndTables", "06_WomensRestroom", "07_MensRestroom",
        "08_RestroomEntrance", "09_Toilets", "10_Sinks", "11_StaffOnlyRoom",
        "12_EmployeeGates", "13_Elevator", "14_Staircase"
    };

    [MenuItem("CompanyGame/Setup/Organize Handae Lobby Prefabs")]
    public static void OrganizeActiveScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before organizing the lobby.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new InvalidOperationException("Open the Handae HQ lobby scene first.");
        var lobby = GameObject.Find("Map_HandaeHQLobby");
        if (!lobby) throw new InvalidOperationException("Lobby root is missing.");
        Organize(lobby.transform);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save organized lobby scene.");
        AssetDatabase.SaveAssets();
        Debug.Log("[Handae] Lobby objects organized into category prefabs: " + Prefabs);
    }

    public static void Organize(Transform lobby)
    {
        if (lobby.Find(GroupNames[0])) return; // Already organized; do not nest prefabs on a second run.
        EnsureFolder(Prefabs);
        var categories = new Dictionary<string, Transform>();
        foreach (string groupName in GroupNames)
        {
            var group = new GameObject(groupName).transform;
            group.SetParent(lobby, false);
            categories.Add(groupName, group);
        }

        // The generated pots originally consist of three adjacent siblings.
        // Create a root for each pot before moving the entire collection.
        var details = lobby.Find("30_Details");
        int planterNumber = 0;
        if (details)
        {
            var siblings = Children(details);
            for (int i = 0; i + 2 < siblings.Count; i++)
            {
                if (siblings[i].name != "Dark stone planter" ||
                    siblings[i + 1].name != "Planter soil" ||
                    siblings[i + 2].name != "Architectural plant foliage") continue;
                var pot = siblings[i];
                var group = new GameObject("Planter " + (++planterNumber).ToString("00")).transform;
                group.gameObject.AddComponent<WorldObject>().objectType = WorldObjectType.StaticProp;
                group.SetParent(categories["03_Planters"], false);
                group.position = pot.position - Vector3.up * (pot.localScale.x * .36f);
                for (int j = 0; j < 3; j++) siblings[i + j].SetParent(group, true);
                i += 2;
            }
        }

        foreach (string rootName in new[] { "10_Architecture", "20_Furniture", "30_Details", "40_Gameplay" })
        {
            var source = lobby.Find(rootName);
            if (!source) continue;
            foreach (var item in Children(source))
            {
                string category = Classify(item.name);
                if (category != null) item.SetParent(categories[category], true);
            }
        }

        foreach (var pair in categories)
        {
            if (pair.Value.childCount == 0) { Object.DestroyImmediate(pair.Value.gameObject); continue; }
            SaveConnected(pair.Value, pair.Key + ".prefab");
        }
        foreach (string rootName in new[] { "10_Architecture", "20_Furniture", "30_Details", "40_Gameplay", "50_Lighting" })
        {
            var source = lobby.Find(rootName);
            if (source && source.childCount > 0) SaveConnected(source, rootName + ".prefab");
            else if (source) Object.DestroyImmediate(source.gameObject);
        }
        Debug.Log("[Handae] Prefab groups created; planters=" + planterNumber);
    }

    static string Classify(string name)
    {
        if (Starts(name, "Stair", "North stair", "Transparent stair", "Spawn_floor_1", "Sign 계단")) return "14_Staircase";
        if (Starts(name, "Elevator", "Sign 엘리베이터")) return "13_Elevator";
        if (Starts(name, "Gate ", "Employee card gate")) return "12_EmployeeGates";
        if (name.Contains(" toilet ")) return "09_Toilets";
        if (name.Contains(" sink ")) return "10_Sinks";
        if (Starts(name, "Women ", "Sign 여자")) return "06_WomensRestroom";
        if (Starts(name, "Men ", "Sign 남자")) return "07_MensRestroom";
        if (Starts(name, "Shared restroom")) return "08_RestroomEntrance";
        if (Starts(name, "Staff-only", "Sign 직원 외")) return "11_StaffOnlyRoom";
        if (Starts(name, "Lounge armchair")) return "04_LoungeArmchairs";
        if (Starts(name, "Waiting ", "Lounge long", "Lounge recessed")) return "05_WaitingSofaAndTables";
        if (Starts(name, "Front ", "Facade ", "Entrance ", "Main entrance", "Exterior threshold")) return "01_Entrance";
        if (Starts(name, "Central structural logo", "Logo wall", "Logo warm", "Dark vertical timber flute",
                   "Handae ascending", "Sign 한대건설", "Sign HANDAE", "U-shaped reception",
                   "Reception ", "Lobby armchair")) return "02_Reception";
        return null;
    }

    static bool Starts(string value, params string[] prefixes)
    {
        foreach (string prefix in prefixes)
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }

    static List<Transform> Children(Transform parent)
    {
        var result = new List<Transform>();
        foreach (Transform child in parent) result.Add(child);
        return result;
    }

    static void SaveConnected(Transform group, string filename)
    {
        string path = Prefabs + "/" + filename;
        bool saved;
        PrefabUtility.SaveAsPrefabAssetAndConnect(group.gameObject, path, InteractionMode.AutomatedAction, out saved);
        if (!saved) throw new IOException("Could not create prefab: " + path);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
