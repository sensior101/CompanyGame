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
        if (lobby.Find("15_NPCs"))
        {
            RebindStaffPoses(lobby.Find("15_NPCs"));
            HandaeLobbyWorldSetup.BindStaffSeats(lobby);
            return;
        }
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

    [MenuItem("CompanyGame/Setup/Rebind Handae Staff Avatar Poses")]
    public static void RebindStaffPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Staff pose rebinding requires Edit Mode and no compilation errors.");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            int count = RebindStaffPoses(root.transform);
            if (count != 3) throw new InvalidOperationException("Expected two reception staff and one gate staff.");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }

    public static int RebindStaffPoses(Transform group)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(MalePath);
        if (!source) throw new InvalidOperationException("Existing male avatar is missing.");
        int count = 0;
        foreach (var data in group.GetComponentsInChildren<DialogueData>(true))
        {
            bool seated = data.npcId == "handae_reception_1" || data.npcId == "handae_reception_2";
            if (!seated && data.npcId != "handae_gate_staff") continue;
            var motion = data.GetComponentInChildren<DaldongneAvatarMotion>(true);
            if (!motion || PrefabUtility.GetCorrespondingObjectFromSource(motion.gameObject) != source)
                throw new InvalidOperationException("Staff must retain its existing MaleVisual connection: " + data.npcId);
            var visual = motion.gameObject;
            var position = visual.transform.localPosition; var rotation = visual.transform.localRotation;
            var scale = visual.transform.localScale; bool active = visual.activeSelf;
            // Old rigid-bone fileIDs cannot be carried to an imported skeleton. Revert only the nested
            // visual instance; the staff root, dialogue, seat assignment and SpeechAnchor retain their IDs.
            PrefabUtility.RevertPrefabInstance(visual, InteractionMode.AutomatedAction);
            visual.transform.localPosition = position; visual.transform.localRotation = rotation;
            visual.transform.localScale = scale; visual.SetActive(active);
            float top = ApplyFixedPose(data.transform, visual, seated);
            if (data.head && data.head.parent == data.transform && data.head.name == "SpeechAnchor")
            {
                data.head.localPosition = Vector3.up * (top + .15f);
                EditorUtility.SetDirty(data.head);
                if (PrefabUtility.IsPartOfPrefabInstance(data.head)) PrefabUtility.RecordPrefabInstancePropertyModifications(data.head);
            }
            count++;
        }
        return count;
    }

    static float ApplyFixedPose(Transform root, GameObject visual, bool seated)
    {
        var motion = visual.GetComponent<DaldongneAvatarMotion>();
        if (!motion) throw new InvalidOperationException("Staff visual has no motion owner.");
        // Disable before posing: OnDisable restores the cached neutral pose when called later.
        motion.enabled = false;
        if (motion.HasHumanoidRig) motion.ConfigureHumanoid(motion.rigAnimator);
        motion.SetSeated(seated);
        foreach (var bone in visual.GetComponentsInChildren<Transform>(true))
            PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
        if (motion.rigAnimator) PrefabUtility.RecordPrefabInstancePropertyModifications(motion.rigAnimator);
        PrefabUtility.RecordPrefabInstancePropertyModifications(motion);
        float top = 0f;
        foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh);
                skin.localBounds = mesh.bounds; skin.updateWhenOffscreen = true;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = mesh.bounds.center + Vector3.Scale(mesh.bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    top = Mathf.Max(top, root.InverseTransformPoint(skin.transform.TransformPoint(point)).y);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        return top > 0f ? top : seated ? 1.3f : 1.8f;
    }

    static DialogueData Create(Transform parent, GameObject male, string name, Vector3 position, bool seated)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false); root.transform.position = position;
        root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(male, root.transform);
        float top = ApplyFixedPose(root.transform, visual, seated);
        var anchor = new GameObject("SpeechAnchor").transform;
        anchor.SetParent(root.transform, false); anchor.localPosition = Vector3.up * (top + .15f);
        var collider = root.AddComponent<CapsuleCollider>();
        collider.radius = .22f; collider.height = seated ? 1.2f : 1.7f;
        collider.center = Vector3.up * collider.height * .5f;
        var data = root.AddComponent<DialogueData>(); data.head = anchor; data.headOffset = Vector3.zero;
        return data;
    }
}
