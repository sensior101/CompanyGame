using System;
using System.Collections.Generic;
using CompanyGame.World.Maps;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Local floor placement, using item prefabs and the existing world save.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(20)]
public sealed partial class FurniturePlacementController : MonoBehaviour
{
    const float Reach = 6f;
    const float Clearance = .015f;
    static readonly List<PlacedFurnitureSave> placed = new();
    static readonly Dictionary<string, GameObject> instances = new();
    static FurniturePlacementController active;
    static int lastInputFrame = -1;

    public static bool OwnsInput => active || lastInputFrame == Time.frameCount;
    public static bool IsAiming => active && !active.editing;
    internal static bool IsLocalWorld => GameSession.IsAuthority && !SharedWriteGate.InRoom &&
        !(SyncClient.Instance && SyncClient.Instance.Connected);

    PlayerInventory owner;
    PlayerMovement movement;
    ItemData item;
    string instanceId;
    int slot;
    GameObject preview;
    Material blue;
    Bounds bounds;
    float yaw;
    int startedFrame;
    float wheelRemainder;
    bool capturedCursor;
    bool previousCursorVisibility;
    CursorLockMode previousCursorLock;

    public void Begin(PlayerInventory inventory, PlayerMovement motor, int index, ItemStack stack)
    {
        if (!IsLocalWorld)
        {
            inventory.SetStatus("가구 설치는 오프라인 월드에서 사용할 수 있습니다.");
            return;
        }
        if ((stack.Item.furniturePlacement & FurniturePlacement.Floor) == 0)
        {
            inventory.SetStatus("바닥에 설치할 수 있는 가구를 선택해 주세요.");
            return;
        }
        if (!motor || !motor.viewCamera || (motor.TryGetComponent<PlayerVehicle>(out var vehicle) && vehicle.IsRiding) ||
            !CanEdit(FindZone(motor.transform.position)))
        {
            inventory.SetStatus("내 소유 공간 안에서 가구를 설치해 주세요.");
            return;
        }
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader)
        {
            inventory.SetStatus("가구 미리보기를 표시할 수 없습니다.");
            return;
        }

        owner = inventory;
        movement = motor;
        slot = index;
        item = stack.Item;
        instanceId = stack.InstanceId;
        var prefab = item.furniturePrefab;
        var size = prefab.transform.localScale;
        var measured = VehicleBounds.Measure(prefab);
        bounds = new Bounds(Vector3.Scale(measured.center, size),
            Vector3.Scale(measured.size, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z))));
        yaw = 0f;
        wheelRemainder = 0f;
        blue = new Material(shader);
        blue.SetColor("_BaseColor", new Color(.12f, .48f, 1f, .43f));
        blue.SetFloat("_Surface", 1f);
        blue.SetFloat("_ZWrite", 0f);
        blue.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        blue.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        blue.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        blue.renderQueue = (int)RenderQueue.Transparent;
        preview = new GameObject("Furniture preview");
        preview.transform.localScale = size;
        CopyPreviewMeshes(prefab.transform, preview.transform, blue);
        preview.SetActive(false);
        active = this;
        startedFrame = Time.frameCount;
        inventory.CloseInventory();
        inventory.SetStatus("화면 중앙 조준 · 휠 회전 · 좌클릭 설치 · 우클릭/Esc 취소");
    }

    // Copy visual geometry only so a preview cannot register seats, storage, lights or colliders.
    static void CopyPreviewMeshes(Transform source, Transform target, Material material)
    {
        if (source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh &&
            source.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled)
        {
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var visual = target.gameObject.AddComponent<MeshRenderer>();
            var materials = new Material[renderer.sharedMaterials.Length];
            Array.Fill(materials, material);
            visual.sharedMaterials = materials;
            visual.shadowCastingMode = ShadowCastingMode.Off;
            visual.receiveShadows = false;
        }
        foreach (Transform child in source)
        {
            if (!child.gameObject.activeSelf) continue;
            var part = new GameObject(child.name).transform;
            part.SetParent(target, false);
            part.SetLocalPositionAndRotation(child.localPosition, child.localRotation);
            part.localScale = child.localScale;
            CopyPreviewMeshes(child, part, material);
        }
    }

    void LateUpdate()
    {
        if (editing) { TickEditor(); return; }
        if (active != this) return;
        if (startedFrame == Time.frameCount) return;
        if (SceneLoadManager.IsLoading || !IsLocalWorld || !movement || !movement.viewCamera ||
            ChatUIManager.IsChatting || DialogueManager.OwnsInput ||
            (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) ||
            GameInput.CancelPressed || GameInput.DismantleVehiclePressed)
        {
            Cancel();
            return;
        }
        if (!capturedCursor)
        {
            previousCursorVisibility = Cursor.visible;
            previousCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            capturedCursor = true;
        }

        var inventory = owner.Inventory;
        var stack = inventory?.GetSlot(slot);
        if (stack == null || stack.IsEmpty || stack.Item != item || stack.InstanceId != instanceId)
        {
            Cancel();
            return;
        }

        wheelRemainder += GameInput.Scroll;
        int notches = (int)wheelRemainder;
        wheelRemainder -= notches;
        yaw = Mathf.Repeat(yaw + notches * 15f, 360f);
        var rotation = Quaternion.Euler(0, yaw, 0);
        bool hasFloor = TryAimAtFloor(out var hit);
        preview.SetActive(hasFloor);
        if (!hasFloor)
        {
            if (GameInput.AttackPressed) owner.SetStatus("화면 중앙으로 설치할 바닥을 바라보세요.");
            return;
        }
        var position = hit.point + Vector3.up * (Clearance - bounds.min.y);
        preview.transform.SetPositionAndRotation(position, rotation);
        if (!GameInput.AttackPressed) return;

        var zone = FindZone(hit.point + Vector3.up * Clearance);
        if (!CanEdit(zone) || !zone.Contains(movement.transform.position) || !Fits(zone, position, rotation))
        {
            owner.SetStatus("내 공간 안의 비어 있는 평평한 바닥에 설치해 주세요. 캐릭터가 서 있는 자리에는 설치할 수 없습니다.");
            return;
        }

        var saved = new PlacedFurnitureSave
        {
            id = Guid.NewGuid().ToString("N"), item = item.itemId,
            scene = zone.gameObject.scene.path, property = zone.propertyId,
            position = zone.transform.InverseTransformPoint(position),
            yaw = Mathf.DeltaAngle(zone.transform.eulerAngles.y, yaw)
        };
        var model = Create(saved, item, zone, false);
        if (!inventory.TryRemove(slot, 1, out var error))
        {
            Destroy(model);
            owner.SetStatus(error);
            return;
        }
        placed.Add(saved);
        WorldSaveRunner.RequestSave();
        instances.Add(saved.id, model);
        model.SetActive(true);
        owner.SetStatus(item.DisplayName + " 설치 완료");
        Cancel();
    }

    bool TryAimAtFloor(out RaycastHit result)
    {
        var ray = movement.viewCamera.ViewportPointToRay(new Vector3(.5f, .5f));
        var hits = Physics.RaycastAll(ray, Reach + Vector3.Distance(ray.origin, movement.transform.position),
            ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(movement.transform) || hit.collider.gameObject.scene != SceneLoadManager.CurrentMap) continue;
            result = hit;
            return hit.normal.y >= .94f && !hit.collider.GetComponentInParent<WorldObject>() &&
                (hit.point - movement.transform.position).sqrMagnitude <= Reach * Reach;
        }
        result = default;
        return false;
    }

    bool Fits(PropertyZone zone, Vector3 position, Quaternion rotation)
    {
        var center = position + rotation * bounds.center;
        return FitsVolume(zone, bounds, position, rotation);
    }

    internal static bool FitsVolume(PropertyZone zone, Bounds bounds, Vector3 position, Quaternion rotation,
        Func<Collider, bool> ignore = null)
    {
        Physics.SyncTransforms();
        var center = position + rotation * bounds.center;
        foreach (var collider in Physics.OverlapBox(center, bounds.extents * .999f, rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            if (ignore?.Invoke(collider) ?? false) continue;
            // The player lives in the DontDestroyOnLoad scene, so the zone-scene test alone would let furniture trap them.
            if (collider.gameObject.scene == zone.gameObject.scene || collider.GetComponentInParent<PlayerMovement>()) return false;
        }
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
            var point = position + rotation * corner;
            if (!zone.Contains(point)) return false;
            if ((i & 2) != 0) continue;
            if (!Physics.Raycast(point + Vector3.up * .06f, Vector3.down, out var floor, .16f, ~0, QueryTriggerInteraction.Ignore) ||
                floor.normal.y < .94f || floor.collider.GetComponentInParent<WorldObject>()) return false;
        }
        return true;
    }

    internal static PropertyZone FindZone(Vector3 point)
    {
        foreach (var zone in PropertyZone.Active)
            if (zone.Contains(point)) return zone;
        return null;
    }

    internal static bool CanEdit(PropertyZone zone) => IsLocalWorld && zone && !string.IsNullOrEmpty(zone.propertyId) &&
        PropertyRegistry.Instance && PropertyRegistry.Instance.GetOwner(zone.propertyId) == GameSession.LocalPlayerName;

    public void Cancel()
    {
        if (editing) { ExitEditor(); return; }
        if (active == this)
        {
            active = null;
            lastInputFrame = Time.frameCount;
        }
        if (preview) Destroy(preview);
        if (blue) Destroy(blue);
        if (capturedCursor)
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisibility;
            capturedCursor = false;
        }
        preview = null;
        blue = null;
    }

    void OnDisable() => Cancel();

    static GameObject Create(PlacedFurnitureSave saved, ItemData definition, PropertyZone zone, bool enabled)
    {
        var staging = new GameObject("Furniture staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, zone.gameObject.scene);
        var model = Instantiate(definition.furniturePrefab, staging.transform);
        model.SetActive(false);
        model.transform.SetParent(null, false);
        model.transform.SetPositionAndRotation(zone.transform.TransformPoint(saved.position),
            Quaternion.Euler(0, zone.transform.eulerAngles.y + saved.yaw, 0));
        var storage = model.GetComponentsInChildren<FridgeStorage>(true);
        for (int i = 0; i < storage.Length; i++) storage[i].storageId = $"furniture:{saved.id}:{i}";
        Destroy(staging);
        model.SetActive(enabled);
        return model;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPlacement()
    {
        active = null;
        lastInputFrame = -1;
        placed.Clear();
        instances.Clear();
        SceneManager.sceneLoaded -= RestoreScene;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InstallSaveHooks()
    {
        WorldSave.CaptureFurniture = () => new List<PlacedFurnitureSave>(placed);
        WorldSave.RestoreFurniture = Restore;
        SceneManager.sceneLoaded += RestoreScene;
    }

    static void Restore(List<PlacedFurnitureSave> saved)
    {
        if (active) active.Cancel();
        foreach (var model in instances.Values)
            if (model) { model.SetActive(false); Destroy(model); }
        instances.Clear();
        placed.Clear();
        if (saved != null) placed.AddRange(saved);
        for (int i = 0; i < SceneManager.sceneCount; i++)
            RestoreScene(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
    }

    static void RestoreScene(Scene scene, LoadSceneMode mode)
    {
        if (!IsLocalWorld || !scene.isLoaded) return;
        foreach (var saved in placed)
        {
            if (saved == null || string.IsNullOrEmpty(saved.id) || saved.scene != scene.path ||
                (instances.TryGetValue(saved.id, out var existing) && existing)) continue;
            var definition = InventorySnapshot.Find(saved.item);
            if (!definition || !definition.furniturePrefab) continue;
            foreach (var zone in PropertyZone.Active)
            {
                if (zone.gameObject.scene != scene || zone.propertyId != saved.property) continue;
                instances[saved.id] = Create(saved, definition, zone, true);
                break;
            }
        }
    }
}
