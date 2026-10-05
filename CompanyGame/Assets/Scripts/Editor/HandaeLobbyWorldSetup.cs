using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HandaeLobbyWorldSetup
{
    const string Folder = "Assets/Art/WorldDistricts/HandaeHQ/Lobby/Prefabs";
    public static void ApplyToPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("15_NPCs.prefab")) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { Configure(root.transform); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var scene = SceneManager.GetActiveScene();
        if (scene.path == HandaeLobbyBuilder.ScenePath)
        {
            BindStaffSeats(GameObject.Find("Map_HandaeHQLobby").transform);
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
    }

    public static void Configure(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string name = t.name;
            if (name.StartsWith("Lounge armchair") || name.StartsWith("Waiting sofa") || name.Contains("(toilet.fbx)") || name == "Lobby armchair")
            {
                var obj = Set(t, WorldObjectType.InteractiveFixture, FurnitureFunction.Seating);
                var box = t.GetComponent<BoxCollider>();
                if (!box && name != "Lobby armchair") continue;
                int count = name.StartsWith("Waiting sofa") ? 3 : 1;
                for (int i = 0; i < count; i++)
                {
                    string seatName = "Seat " + (i + 1);
                    var point = t.Find(seatName);
                    if (!point) { point = new GameObject(seatName).transform; point.SetParent(t, false); }
                    float x = count == 1 ? 0f : (i - 1) * box.size.x * .28f;
                    point.localPosition = name == "Lobby armchair" ? new Vector3(0f, .44f, -.07f) :
                        new Vector3(box.center.x + x, box.center.y - box.size.y * .5f + box.size.y * .43f, box.center.z + box.size.z * .08f);
                    point.localRotation = name == "Lobby armchair" ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                    var seat = point.GetComponent<Seat>(); if (!seat) seat = point.gameObject.AddComponent<Seat>();
                    seat.owner = obj; seat.radius = 1.8f;
                }
            }
            else if (name.Contains("(sink.fbx)")) Set(t, WorldObjectType.InteractiveFixture, FurnitureFunction.WaterSource);
            else if (name.StartsWith("Employee card gate") || name == "Main entrance automatic sliding doors" || name == "Elevator solid shaft infill") Set(t, WorldObjectType.InteractiveFixture, FurnitureFunction.None);
            else if (name == "Reception terminal") Set(t, WorldObjectType.InteractiveFixture, FurnitureFunction.Display);
            else if (name == "Lounge long rectangular marble table" || name == "Waiting round side table" || name == "Reception internal worktop") Set(t, WorldObjectType.InteractiveFixture, FurnitureFunction.Surface);
            else if (name.StartsWith("Planter ") && name != "Planter soil") Set(t, WorldObjectType.StaticProp, FurnitureFunction.None);
        }
    }
    public static void BindStaffSeats(Transform lobby)
    {
        var chairs = lobby.GetComponentsInChildren<Seat>(true).Where(s => s.owner && s.owner.name == "Lobby armchair").OrderBy(s => s.transform.position.x).ToArray();
        var npcs = lobby.Find("15_NPCs");
        if (!npcs) return;
        for (int i = 0; i < chairs.Length; i++)
        {
            chairs[i].assignedOccupant = npcs.Find("Reception Staff " + (i + 1));
            EditorUtility.SetDirty(chairs[i]);
            PrefabUtility.RecordPrefabInstancePropertyModifications(chairs[i]);
        }
    }
    static WorldObject Set(Transform t, WorldObjectType type, FurnitureFunction functions)
    {
        var value = t.GetComponent<WorldObject>(); if (!value) value = t.gameObject.AddComponent<WorldObject>();
        value.objectType = type; value.functions = functions; value.furnitureItem = null;
        return value;
    }
}
