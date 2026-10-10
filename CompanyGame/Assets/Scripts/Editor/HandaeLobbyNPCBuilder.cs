using System;
using System.Linq;
using CompanyGame.Daldongne;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Places the three fixed staff using the project's existing male visual prefab.</summary>
public static class HandaeLobbyNPCBuilder
{
    const string MalePath = "Assets/Art/Daldongne/Players/MaleVisual.prefab";
    const string PrefabPath = "Assets/Art/WorldDistricts/HandaeHQ/Lobby/Prefabs/15_NPCs.prefab";

    [MenuItem("CompanyGame/Setup/Place Handae Lobby NPCs")]
    public static void PlaceActiveScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != HandaeLobbyBuilder.ScenePath) throw new InvalidOperationException("Open HandaeHQLobby first.");
        Place(GameObject.Find("Map_HandaeHQLobby").transform);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    public static void Place(Transform lobby)
    {
        if (lobby.Find("15_NPCs")) { HandaeLobbyWorldSetup.BindStaffSeats(lobby); return; }
        var male = AssetDatabase.LoadAssetAtPath<GameObject>(MalePath);
        if (!male) throw new InvalidOperationException("Existing male avatar is missing.");
        var chairs = lobby.GetComponentsInChildren<Transform>().Where(t => t.name == "Lobby armchair").OrderBy(t => t.position.x).ToArray();
        var gate = lobby.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Employee card gate 1");
        if (chairs.Length != 2 || !gate) throw new InvalidOperationException("Expected two reception chairs and employee gate 1.");
        var group = new GameObject("15_NPCs").transform;
        group.SetParent(lobby, false);
        for (int i = 0; i < chairs.Length; i++)
        {
            var npc = Create(group, male, "Reception Staff " + (i + 1), chairs[i].position + Vector3.back * .07f, true);
            npc.npcId = "handae_reception_" + (i + 1);
            npc.radius = 3.5f;
            npc.line = "안녕하세요 한대건설입니다. 무엇을 도와드릴까요?";
            npc.options = new[] {
                new DialogueOption("construction_consultation", "건축 상담"),
                new DialogueOption("construction_progress", "공사 진행 상황", DialogueRequirement.ActiveConstruction),
                new DialogueOption("building_demolition", "건물철거신청", DialogueRequirement.OwnedBuilding),
                new DialogueOption("building_entry", "건물 출입")
            };
        }
        var guard = Create(group, male, "Gate Staff", gate.position + new Vector3(-1.7f, 0f, -1.5f), false);
        guard.npcId = "handae_gate_staff";
        guard.line = "출입하려면 ID카드를 소지하고 계셔야 합니다.";
        PrefabUtility.SaveAsPrefabAssetAndConnect(group.gameObject, PrefabPath, InteractionMode.AutomatedAction);
        HandaeLobbyWorldSetup.BindStaffSeats(lobby);
    }

    static DialogueData Create(Transform parent, GameObject male, string name, Vector3 position, bool seated)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false); root.transform.position = position;
        root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(male, root.transform);
        var motion = visual.GetComponent<DaldongneAvatarMotion>();
        if (motion) motion.enabled = false;
        if (seated && motion)
        {
            var hips = motion.hips.localPosition; hips.y = .54f; motion.hips.localPosition = hips;
            motion.leftLeg.localRotation = motion.rightLeg.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            motion.leftKnee.localRotation = motion.rightKnee.localRotation = Quaternion.Euler(90f, 0f, 0f);
            motion.leftArm.localRotation = motion.rightArm.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            motion.leftForearm.localRotation = motion.rightForearm.localRotation = Quaternion.Euler(-55f, 0f, 0f);
            foreach (var t in visual.GetComponentsInChildren<Transform>()) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }
        if (motion) PrefabUtility.RecordPrefabInstancePropertyModifications(motion);
        float top = 1.8f;
        foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh(); skin.BakeMesh(mesh);
            skin.localBounds = mesh.bounds; skin.updateWhenOffscreen = true;
            top = root.transform.InverseTransformPoint(skin.transform.TransformPoint(mesh.bounds.max)).y;
            UnityEngine.Object.DestroyImmediate(mesh);
            PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
        }
        var anchor = new GameObject("SpeechAnchor").transform;
        anchor.SetParent(root.transform, false); anchor.localPosition = Vector3.up * (top + .15f);
        var collider = root.AddComponent<CapsuleCollider>();
        collider.radius = .22f; collider.height = seated ? 1.2f : 1.7f;
        collider.center = Vector3.up * collider.height * .5f;
        var data = root.AddComponent<DialogueData>(); data.head = anchor; data.headOffset = Vector3.zero;
        return data;
    }
}
