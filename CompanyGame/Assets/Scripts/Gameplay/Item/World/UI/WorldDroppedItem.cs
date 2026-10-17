using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A pickable world stack. Session records survive Single-scene travel; each visual
/// and collider belongs only to its map. Pickup removes the record after an atomic transfer.
/// On a sync-layer server (12.8) drops and placed vehicles are DropState entities: the record is keyed by the entity id,
/// pickup, riding and dismantling go through the server's Claim, and this PC's own drops are hidden and never written.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldDroppedItem : MonoBehaviour
{
    sealed class DropRecord
    {
        public string id;
        public string scenePath;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public bool placedVehicle;
        public string riderId; // room member driving it (a placed vehicle only)
        public InventoryState contents = new InventoryState();
        public TMP_FontAsset font;
        public WorldDroppedItem instance;
        // Sync-layer server (12.8): the entity id (0 = this PC's own drop or an old room's car) and its state.
        public ulong netId;
        public DropState sync;
        public bool pending; // spawned here, the server has not taken it yet
        public ClaimFor claim;
        public float nextSyncAt;
    }
    enum ClaimFor { None, PickUp, Ride, Dismantle }
    static readonly Dictionary<string, DropRecord> records = new Dictionary<string, DropRecord>();
    DropRecord record;
    Transform billboard;
    Transform floatingItem, spinningModel, glowBeam;
    readonly List<Mesh> effectMeshes = new List<Mesh>();
    float floatPhase;
    RectTransform pickupPopup;
    const float PromptScalePerMeter = .0036f; // same on-screen size for every item and vehicle prompt (.009 at 2.5 m)
    TMP_Text pickupText;
    GameObject vehicleModel;
    Collider vehicleCollider;
    Mesh vehicleCollisionMesh;
    MeshFilter[] vehicleMeshParts;
    Vector3[] selectionVertices, selectionProjected;
    int[] selectionTriangles, selectionQueue;
    byte[] selectionMask;
    float[] selectionDepth;
    readonly Vector3[] selectionClip = new Vector3[4];
    int selectionX, selectionY, selectionWidth, selectionHeight;
    bool selectionValid;
    Camera selectionCamera;
    Matrix4x4 selectionViewProjection, selectionModelMatrix;
    Rect selectionCameraRect;
    const float SelectionPixelSize=2f;
    PlayerVehicle rider;
    GameObject hoverOutline;
    readonly List<KeyValuePair<Transform,Transform>> outlineParts = new List<KeyValuePair<Transform,Transform>>();

    public ItemData Item => record?.contents.GetSlot(0)?.Item;
    public int Count => record?.contents.GetSlot(0)?.Count ?? 0;
    public long CurrencyTotal => Item && Item.IsCurrency ? checked(Item.CurrencyValue * Count) : 0L;
    public string DropId => record?.id;
    public static int SessionDropCount => records.Count;
    public bool IsPlacedVehicle => record != null && record.placedVehicle;
    public bool IsOccupied => (rider && rider.IsRiding) || !string.IsNullOrEmpty(record?.riderId);
    public string VehicleOwnershipKey => record?.contents.GetSlot(0)?.OwnershipKey;
    public string VehicleOwner => PropertyRegistry.Instance ? PropertyRegistry.Instance.GetOwner(VehicleOwnershipKey) : null;
    public bool VehicleLocked => PropertyRegistry.Instance && PropertyRegistry.Instance.IsVehicleLocked(VehicleOwnershipKey);
    public bool VehicleDismantleUnlocked => PropertyRegistry.Instance && PropertyRegistry.Instance.IsVehicleDismantleUnlocked(VehicleOwnershipKey);
    public bool IsHighlighted => hoverOutline && hoverOutline.activeSelf;

    public bool CanManageVehicle(string actor) => IsPlacedVehicle && !IsOccupied && PropertyRegistry.Instance &&
        PropertyRegistry.Instance.CanManageVehicle(VehicleOwnershipKey,actor);
    public bool TrySetVehicleLocked(string actor, bool locked)
    {
        return IsPlacedVehicle && !IsOccupied && PropertyRegistry.Instance &&
            PropertyRegistry.Instance.TrySetVehicleLocked(VehicleOwnershipKey,actor,locked);
    }
    public bool TrySetVehicleDismantleUnlocked(string actor, bool unlocked) =>
        IsPlacedVehicle && !IsOccupied && PropertyRegistry.Instance &&
        PropertyRegistry.Instance.TrySetVehicleDismantleUnlocked(VehicleOwnershipKey, actor, unlocked);
    public bool TryRegisterVehicle(string actor) =>
        IsPlacedVehicle && !IsOccupied && PropertyRegistry.Instance &&
        PropertyRegistry.Instance.TryRegister(VehicleOwnershipKey, actor);
    public bool TryAbandonVehicle(string actor) =>
        IsPlacedVehicle && !IsOccupied && PropertyRegistry.Instance &&
        PropertyRegistry.Instance.TryAbandon(VehicleOwnershipKey, actor);

    // The same instance moves from installed geometry to the existing dropped-item representation.
    public bool TryDismantleVehicle(string actor, out string error)
    {
        error = null;
        if (!IsPlacedVehicle || IsOccupied) return false;
        if (!PropertyRegistry.Instance || !PropertyRegistry.Instance.CanDismantleVehicle(VehicleOwnershipKey,actor))
        { error="소유자가 해체를 허용하지 않았습니다."; return false; }
        if (record.netId != 0) return TryClaim(ClaimFor.Dismantle, out error);
        // ponytail: other members only lose the car; the dropped item stays on this PC (plain drops are not shared yet).
        if (!SendVehicleOp("veh.del", record) && SharedWriteGate.SendFailed) { error="서버에 연결되어 있지 않아 해체하지 못했습니다."; return false; }
        record.placedVehicle=false;
        foreach (var collider in GetComponents<Collider>()) { collider.enabled=false; Destroy(collider); }
        foreach (Transform child in transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        foreach (var mesh in effectMeshes) if(mesh)Destroy(mesh);
        effectMeshes.Clear();
        vehicleModel=hoverOutline=null;vehicleCollider=null;
        vehicleCollisionMesh=null;vehicleMeshParts=null;
        selectionVertices=selectionProjected=null;selectionTriangles=selectionQueue=null;
        selectionMask=null;selectionDepth=null;selectionValid=false;
        outlineParts.Clear();
        billboard=floatingItem=spinningModel=glowBeam=null;pickupPopup=null;pickupText=null;
        BuildVisual();
        return true;
    }

    public void SetVehicleHover(bool visible)
    {
        visible &= IsPlacedVehicle && !IsOccupied;
        if(visible && !hoverOutline && vehicleModel)
        {
            var material=Resources.Load<Material>("Inventory/Vehicles/VehicleHoverOutline");
            if(!material)return;
            var meshes=vehicleModel.GetComponentsInChildren<MeshFilter>();
            hoverOutline=new GameObject("VehicleHoverOutline");hoverOutline.transform.SetParent(vehicleModel.transform,false);
            foreach(var source in meshes)
            {
                if(!source.sharedMesh || !source.gameObject.activeInHierarchy)continue;
                var edge=new GameObject(source.name+"_Outline",typeof(MeshFilter),typeof(MeshRenderer));
                edge.transform.SetParent(hoverOutline.transform,false);
                edge.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
                edge.transform.localScale=source.transform.lossyScale;
                outlineParts.Add(new KeyValuePair<Transform,Transform>(source.transform,edge.transform));
                edge.GetComponent<MeshFilter>().sharedMesh=source.sharedMesh;
                var renderer=edge.GetComponent<MeshRenderer>();
                var materials=new Material[source.sharedMesh.subMeshCount];
                for(int i=0;i<materials.Length;i++)materials[i]=material;
                renderer.sharedMaterials=materials;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            }
        }
        if(visible)
            foreach(var part in outlineParts)
                if(part.Key && part.Value)
                    part.Value.SetPositionAndRotation(part.Key.position,part.Key.rotation);
        if(hoverOutline)hoverOutline.SetActive(visible);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        records.Clear();
        var book = Resources.Load<ItemData>("Inventory/Books/BlankBook");
        if (book && book.IsBook)
            foreach (var saved in BookSaveService.GetWorldDrops())
            {
                if (saved == null || saved.itemId != book.itemId || string.IsNullOrEmpty(saved.dropId)) continue;
                var drop = new DropRecord { id = saved.dropId, scenePath = saved.scenePath, position = saved.position };
                if (BookSaveService.RestoreBook(drop.contents, book, saved, out _)) records[drop.id] = drop;
            }

        SceneManager.sceneLoaded -= RestoreSceneDrops;
        SceneManager.sceneLoaded += RestoreSceneDrops;
    }

    static void RestoreSceneDrops(Scene scene, LoadSceneMode mode)
    {
        foreach (var entry in records.Values)
            if (entry.scenePath == scene.path && !entry.instance) CreateVisual(entry, scene);
    }

    public static bool TryPlaceVehicle(PlayerInventory owner, out string error)
    {
        error = null;
        var source = owner ? owner.Inventory.GetSlot(owner.Inventory.SelectedHotbarIndex) : null;
        var driver = owner ? owner.GetComponent<PlayerVehicle>() : null;
        if (!owner || !owner.CanPickUpWorldItems || owner.IsOpen || !driver || driver.IsRiding ||
            InputFocus.GameplayBlocked() || source == null || source.IsEmpty || !source.Item.IsVehicle || !source.Item.vehiclePrefab)
        { error = "지금은 탈것을 설치할 수 없습니다."; return false; }
        if (SceneLoadManager.CurrentMap.path.Contains("/Interiors/"))
        { error = "탈것은 실외에서 설치해 주세요."; return false; }
        if (!TryFindVehiclePoint(owner, source.Item, out var point, out var rotation))
        { error = "앞쪽에 탈것을 설치할 평평한 공간이 부족합니다."; return false; }
        var drop = new DropRecord { id = Guid.NewGuid().ToString("N"), scenePath = SceneLoadManager.CurrentMap.path,
            position = point, rotation = rotation, placedVehicle = true, font = owner.uiFont };
        if (!owner.Inventory.TryTransferTo(drop.contents, owner.Inventory.SelectedHotbarIndex, 1, out error)) return false;
        InventoryLedgerRules.Trust(owner.Inventory, "vehicle"); // 12.10.4: trusted until 4d-4 (the layout goes out as it is then)
        if (OnServer) return SyncCommit(drop, DropState.VehicleKind, out error) || GiveBack(drop);
        // In a room the car appears for everyone (this PC included) when the op comes back.
        if (SendVehicleOp("veh.put", drop)) return true;
        if (SharedWriteGate.SendFailed) { drop.contents.TryTransferTo(owner.Inventory, 0, 1, out _); error = "서버에 연결되어 있지 않아 설치하지 못했습니다."; return false; }
        Commit(drop, SceneLoadManager.CurrentMap);
        return true;
    }

    static bool TryFindVehiclePoint(PlayerInventory owner, ItemData item, out Vector3 point, out Quaternion rotation)
    {
        point = default; rotation = Quaternion.identity;
        var motor = owner.GetComponent<PlayerMovement>();
        var forward = Vector3.ProjectOnPlane(motor.viewCamera ? motor.viewCamera.transform.forward : owner.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .5f) forward = owner.transform.forward;
        rotation = Quaternion.LookRotation(forward);
        // The prefab pivot need not be the body centre (a car pivots at the driver's seat), so work from the measured body.
        var bounds = VehicleBounds.Measure(item.vehiclePrefab);
        float reach = bounds.extents.z + 1f, rise = -Mathf.Min(0f, bounds.min.y), top = bounds.max.y + rise;
        var pivotToBody = rotation * new Vector3(bounds.center.x, 0f, bounds.center.z);
        var target = owner.transform.position + forward * reach;
        var floors = Physics.RaycastAll(target + Vector3.up * 1.4f, Vector3.down, 2.8f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(floors, (a,b) => a.distance.CompareTo(b.distance));
        foreach (var floor in floors)
        {
            if (floor.transform.IsChildOf(owner.transform) || floor.normal.y < .94f || Mathf.Abs(floor.point.y-owner.transform.position.y) > .6f) continue;
            var candidate = floor.point + Vector3.up * .025f;
            bool blocked = false;
            foreach (float end in new[] { -(bounds.extents.z - .2f), bounds.extents.z - .2f })
            {
                if (!Physics.Raycast(candidate + forward * end + Vector3.up * .4f, Vector3.down, out var support, .8f, ~0, QueryTriggerInteraction.Ignore) ||
                    support.normal.y < .94f || Mathf.Abs(support.point.y-floor.point.y) > .12f) { blocked = true; break; }
            }
            var half = new Vector3(bounds.extents.x + .05f, Mathf.Max(.1f, (top - .14f) * .5f), bounds.extents.z + .05f);
            foreach (var hit in Physics.OverlapBox(candidate + Vector3.up * (.14f + half.y), half, rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(owner.transform) && hit.bounds.max.y > candidate.y + .15f) { blocked = true; break; }
            foreach (var hit in Physics.SphereCastAll(owner.transform.position + Vector3.up * .8f, .25f, forward, reach, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(owner.transform) && hit.normal.y < .7f) { blocked = true; break; }
            if (!blocked) { point = candidate - pivotToBody + Vector3.up * rise; return true; }
        }
        return false;
    }

    public static WorldDroppedItem FindNearestVehicle(PlayerInventory owner)
    {
        WorldDroppedItem nearest = null;
        float distance = 2.5f;
        foreach (var item in FindObjectsByType<WorldDroppedItem>())
        {
            if (!item.IsPlacedVehicle || !item.vehicleModel || item.IsOccupied || !item.IsReachableFrom(owner,distance)) continue;
            nearest = item; distance = Vector3.Distance(owner.transform.position,item.transform.position);
        }
        return nearest;
    }

    public bool TryMount(PlayerInventory owner)
    {
        if (!IsPlacedVehicle || IsOccupied || !vehicleModel || !IsReachableFrom(owner,2.5f)) return false;
        var driver = owner.GetComponent<PlayerVehicle>();
        if (!driver) return false;
        if (!PropertyRegistry.Instance || !PropertyRegistry.Instance.CanRideVehicle(VehicleOwnershipKey,GameSession.LocalPlayerName))
        { owner.SetStatus("잠긴 탈것입니다. 소유자만 탑승할 수 있습니다."); return false; }
        SetVehicleHover(false);
        if (record.netId != 0) return TryClaim(ClaimFor.Ride, out _);
        // In a room the first ride op wins; this PC gets in when its own op comes back (ApplyVehicleOp).
        if (SendVehicleOp("veh.ride", record)) return true;
        return !SharedWriteGate.SendFailed && MountLocal(driver);
    }

    bool MountLocal(PlayerVehicle driver)
    {
        if (!driver || !vehicleModel) return false;
        vehicleCollider.enabled = false;
        if (!driver.TryMount(Item, vehicleModel, ReturnVehicleToGround)) { vehicleCollider.enabled = true; return false; }
        rider = driver;
        if (pickupPopup) pickupPopup.gameObject.SetActive(false);
        return true;
    }

    // Another member drives this car: their avatar shows it (NetPlayerSync), so the parked copy hides.
    void ShowParked(bool visible)
    {
        if (vehicleModel) vehicleModel.SetActive(visible);
        if (vehicleCollider) vehicleCollider.enabled = visible;
        if (!visible && pickupPopup) pickupPopup.gameObject.SetActive(false);
        if (!visible) SetVehicleHover(false);
    }

    void ReturnVehicleToGround()
    {
        if (!vehicleModel || record == null) return;
        record.position = vehicleModel.transform.position;
        record.rotation = Quaternion.Euler(0, vehicleModel.transform.eulerAngles.y, 0);
        transform.SetPositionAndRotation(record.position,record.rotation);
        vehicleModel.transform.SetParent(transform,false);
        vehicleModel.transform.localPosition = Vector3.zero;
        vehicleModel.transform.localRotation = Quaternion.identity;
        foreach (var child in vehicleModel.GetComponentsInChildren<Transform>(true))
            if (child.name == "Kickstand") child.gameObject.SetActive(true);
        rider = null;
        if (vehicleCollider) vehicleCollider.enabled = true;
        RebuildVehicleCollider();
        if (!string.IsNullOrEmpty(record.riderId)) { SendPark(record); record.riderId = null; }
        // Server: where it stands now goes out before the car is given back (also on the way to another map).
        if (record.sync && record.sync.IsOwner)
        {
            ShareRide(true);
            SyncClient.Instance.Release(record.sync.Entity);
        }
    }

    // While I drive a server car: its place goes out at most once a second, when it moved over 1 m.
    void ShareRide(bool now)
    {
        var state = record.sync;
        if (!state || !state.IsOwner) return;
        if (!now && (Time.unscaledTime < record.nextSyncAt || (state.position - record.position).sqrMagnitude <= 1f)) return;
        record.nextSyncAt = Time.unscaledTime + 1f;
        state.position = record.position;
        state.yaw = record.rotation.eulerAngles.y;
        state.MarkDirty();
    }

    public static bool TryDropStorage(PlayerInventory owner, int index, out string error,int count=-1)
    {
        error = null;
        var source = owner ? owner.Inventory.GetSlot(index) : null;
        if (source == null || source.IsEmpty) { error = "내려놓을 아이템이 없습니다."; return false; }
        if (!Prepare(owner, out var drop, out error)) return false;
        if (!owner.Inventory.TryTransferTo(drop.contents, index, count<0?source.Count:count, out error)) return false;
        InventoryLedgerRules.Trust(owner.Inventory, "drop");
        if (OnServer) return SyncCommit(drop, DropState.ItemKind, out error) || GiveBack(drop);
        Commit(drop, SceneLoadManager.CurrentMap);
        return true;
    }

    public static bool TryDropEquipment(PlayerInventory owner, EquipmentSlot slot, out string error)
    {
        error = null;
        var source = owner ? owner.Inventory.GetEquipment(slot) : null;
        if (source == null || source.IsEmpty) { error = "내려놓을 장비가 없습니다."; return false; }
        if (!Prepare(owner, out var drop, out error)) return false;
        // Prepare the destination first. A failed removal leaves the actual equipment unchanged.
        if (!drop.contents.TryAdd(source.Item, source.Count, out error)) return false;
        if (!owner.Inventory.TryRemoveEquipment(slot, out error)) return false;
        InventoryLedgerRules.Trust(owner.Inventory, "drop");
        if (OnServer) return SyncCommit(drop, DropState.ItemKind, out error) || GiveBack(drop);
        Commit(drop, SceneLoadManager.CurrentMap);
        return true;
    }

    static bool Prepare(PlayerInventory owner, out DropRecord drop, out string error)
    {
        drop = null;
        error = null;
        if (!owner || !owner.IsOpen || SceneLoadManager.IsLoading ||
            (owner.UserInterface && owner.UserInterface.IsWithdrawalOpen))
        { error = "지금은 내려놓을 수 없습니다."; return false; }
        if (!TryFindDropPoint(owner, out var position)) { error = "주변에 아이템을 놓을 공간이 없습니다. 조금 이동한 뒤 다시 놓아 주세요."; return false; }
        drop = new DropRecord
        {
            id = Guid.NewGuid().ToString("N"),
            scenePath = SceneLoadManager.CurrentMap.path,
            position = position,
            font = owner.uiFont
        };
        return true;
    }

    static void Commit(DropRecord drop, Scene scene)
    {
        records.Add(drop.id, drop);
        BookSaveService.SaveWorldDrop(drop.id, drop.scenePath, drop.position, drop.contents.GetSlot(0));
        CreateVisual(drop, scene);
    }

    public bool TryPickUp(PlayerInventory owner, out string error)
    {
        error = null;
        if (IsPlacedVehicle) { error="설치된 탈것은 좌클릭 메뉴에서 해체한 다음 F로 주워 주세요."; return false; }
        if (IsOccupied || !owner || !owner.CanPickUpWorldItems || record == null ||
            !records.ContainsKey(record.id) || gameObject.scene != SceneLoadManager.CurrentMap)
        { error = "지금은 주울 수 없습니다."; return false; }
        if (!IsReachableFrom(owner, 4f))
        { error = "아이템에 조금 더 가까이 가 주세요."; return false; }
        int quantity = Count;
        if (quantity <= 0) { error = "이미 주운 아이템입니다."; return false; }
        // Server: the first Claim wins; the answer finishes it (SyncOwner), so nothing is picked up yet.
        if (record.netId != 0) { TryClaim(ClaimFor.PickUp, out error); return false; }
        if (!record.contents.TryTransferTo(owner.Inventory, 0, quantity, out error)) return false;
        records.Remove(record.id);
        BookSaveService.RemoveWorldDrop(record.id);
        record.instance = null;
        record = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }

    public bool IsReachableFrom(PlayerInventory owner, float maximumDistance)
    {
        if (IsOccupied || !owner || gameObject.scene != SceneLoadManager.CurrentMap ||
            Vector3.Distance(owner.transform.position, transform.position) > maximumDistance) return false;
        Vector3 origin = owner.transform.position + Vector3.up * .8f;
        Vector3 delta = transform.position + Vector3.up * .24f - origin;
        foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == owner.transform || hit.transform.IsChildOf(owner.transform) ||
                hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            return false;
        }
        return true;
    }

    static bool TryFindDropPoint(PlayerInventory owner, out Vector3 result)
    {
        result = default;
        var forward = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
        // A narrow alley or an uphill step can block the point straight ahead.
        // Check reachable floor around the player before refusing the drop.
        foreach (float angle in DropAngles)
        {
            var direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            foreach (float distance in DropDistances)
                if (TryFindDropPointInDirection(owner, direction, distance, out result)) return true;
        }
        return false;
    }

    static readonly float[] DropAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
    static readonly float[] DropDistances = { 1.4f, .8f, .45f };

    static bool TryFindDropPointInDirection(PlayerInventory owner, Vector3 forward, float reach, out Vector3 result)
    {
        result = default;
        Vector3 origin = owner.transform.position + Vector3.up * .8f;
        var blocks = Physics.SphereCastAll(origin, .16f, forward, reach, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in blocks)
        {
            if (hit.transform == owner.transform || hit.transform.IsChildOf(owner.transform)) continue;
            if (hit.normal.y > .6f) continue;
            reach = Mathf.Min(reach, hit.distance - .2f);
        }
        // Never force a minimum distance through a nearby wall.
        if (reach < .4f) return false;
        var point = owner.transform.position + forward * reach;
        var floors = Physics.RaycastAll(point + Vector3.up * 1.4f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(floors, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var floor in floors)
        {
            if (floor.transform == owner.transform || floor.transform.IsChildOf(owner.transform) || floor.normal.y < .6f) continue;
            if (Mathf.Abs(floor.point.y - owner.transform.position.y) > 1.2f) continue;
            bool blocked = false;
            foreach (var obstacle in Physics.OverlapSphere(floor.point + Vector3.up * .32f, .24f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (obstacle == floor.collider || obstacle.transform == owner.transform || obstacle.transform.IsChildOf(owner.transform)) continue;
                blocked = true;
                break;
            }
            if (blocked) continue;
            result = floor.point + Vector3.up * .065f;
            return true;
        }
        return false;
    }

    static void CreateVisual(DropRecord drop, Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || drop.contents.GetSlot(0).IsEmpty) return;
        if (drop.netId == 0 && OnServer) return; // this PC's own drops stay out of a server's world
        var host = new GameObject("DroppedItem_" + drop.id);
        host.transform.SetPositionAndRotation(drop.position,drop.rotation);
        SceneManager.MoveGameObjectToScene(host, scene);
        var component = host.AddComponent<WorldDroppedItem>();
        component.record = drop;
        drop.instance = component;
        component.BuildVisual();
        if (drop.placedVehicle && !string.IsNullOrEmpty(drop.riderId)) component.ShowParked(false);
    }

    // ---- placed vehicles in the shared world (room ops + world save) ----

    // WorldSave/WorldOps (Gameplay) and NetPlayerSync (Player) cannot see this UI-assembly class, so it plugs itself in.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InstallHooks()
    {
        WorldSave.CaptureVehicles = CaptureVehicles;
        WorldSave.RestoreVehicles = RestoreVehicles;
        WorldOps.VehicleOp = ApplyVehicleOp;
        NetPlayerSync.RemoteLeft -= ReleaseRider;
        NetPlayerSync.RemoteLeft += ReleaseRider;
        SyncClient.ConnectionChanged -= OnServerConnection;
        SyncClient.ConnectionChanged += OnServerConnection;
    }

    static PlacedVehicleSave ToSave(DropRecord drop)
    {
        var stack = drop.contents.GetSlot(0);
        var model = drop.instance ? drop.instance.vehicleModel : null;
        // A car this PC is driving is wherever the player is now.
        bool driving = model && drop.instance.rider;
        return new PlacedVehicleSave { id = drop.id, item = stack.Item ? stack.Item.itemId : null, inst = stack.InstanceId,
            scene = drop.scenePath, rider = drop.riderId,
            pos = driving ? model.transform.position : drop.position,
            ry = driving ? model.transform.eulerAngles.y : drop.rotation.eulerAngles.y };
    }

    static DropRecord FromSave(PlacedVehicleSave v)
    {
        var item = InventorySnapshot.Find(v.item);
        if (!item || !item.IsVehicle || string.IsNullOrEmpty(v.id)) return null;
        var drop = new DropRecord { id = v.id, scenePath = v.scene, position = v.pos, rotation = Quaternion.Euler(0, v.ry, 0),
            placedVehicle = true, riderId = string.IsNullOrEmpty(v.rider) ? null : v.rider, font = LocalFont() };
        return drop.contents.TryAdd(item, 1, out _, v.inst) ? drop : null;
    }

    static TMP_FontAsset LocalFont()
    {
        var inventory = PlayerSpawner.Player ? PlayerSpawner.Player.GetComponent<PlayerInventory>() : null;
        return inventory ? inventory.uiFont : null;
    }

    // True when the op went to the room; false outside a room (apply locally) or when sending failed (SharedWriteGate.SendFailed).
    static bool SendVehicleOp(string kind, DropRecord drop)
    {
        SharedWriteGate.SetSendFailed(false);
        if (!SharedWriteGate.InOldRoom) return false; // veh.* is the old room's only; a sync server's cars are entities (12.8)
        bool sent = SharedWriteGate.Sender != null && SharedWriteGate.Sender(kind, JsonUtility.ToJson(ToSave(drop)));
        SharedWriteGate.SetSendFailed(!sent);
        return sent;
    }

    // Also sent while an op is being applied (a ride this PC could not start), so it skips the gate's Applying check.
    static void SendPark(DropRecord drop)
    {
        if (SharedWriteGate.InRoomCheck != null && SharedWriteGate.InRoomCheck())
            SharedWriteGate.Sender?.Invoke("veh.park", JsonUtility.ToJson(ToSave(drop)));
    }

    static void Add(DropRecord drop)
    {
        records[drop.id] = drop;
        var scene = SceneManager.GetSceneByPath(drop.scenePath);
        if (scene.IsValid() && scene.isLoaded) CreateVisual(drop, scene);
    }

    static void Remove(DropRecord drop)
    {
        records.Remove(drop.id);
        if (drop.instance) Destroy(drop.instance.gameObject);
        drop.instance = null;
    }

    static void Park(DropRecord drop, Vector3 position, float ry)
    {
        drop.riderId = null;
        drop.position = position; drop.rotation = Quaternion.Euler(0, ry, 0);
        if (!drop.instance) return;
        drop.instance.transform.SetPositionAndRotation(position, drop.rotation);
        drop.instance.ShowParked(true);
        drop.instance.RebuildVehicleCollider();
    }

    /// <summary>veh.put / veh.ride / veh.park / veh.del from the server echo (every member, the sender included).</summary>
    public static string ApplyVehicleOp(string kind, string json, string from)
    {
        var v = JsonUtility.FromJson<PlacedVehicleSave>(json);
        if (v == null || string.IsNullOrEmpty(v.id)) return null;
        records.TryGetValue(v.id, out var drop);
        bool mine = from == GameSession.LocalPlayerId;
        switch (kind)
        {
            case "veh.put":
                if (drop != null) return null;
                drop = FromSave(v);
                if (drop == null) return "알 수 없는 탈것이라 설치하지 못했습니다.";
                drop.riderId = null;
                Add(drop);
                return null;
            case "veh.ride":
                if (drop == null || !drop.placedVehicle || !string.IsNullOrEmpty(drop.riderId)) return "이미 다른 사람이 탄 탈것입니다.";
                drop.riderId = from;
                if (!mine) { if (drop.instance) drop.instance.ShowParked(false); return null; }
                var driver = PlayerSpawner.Player ? PlayerSpawner.Player.GetComponent<PlayerVehicle>() : null;
                if (drop.instance && drop.instance.MountLocal(driver)) return null;
                SendPark(drop); // could not get in after all (moved away, loading): free it for everyone
                return "지금은 탑승할 수 없습니다.";
            case "veh.park":
                if (drop == null || drop.riderId != from) return null;
                if (mine) { drop.riderId = null; return null; } // this PC already parked it
                Park(drop, v.pos, v.ry);
                return null;
            case "veh.del":
                if (drop != null && !mine && string.IsNullOrEmpty(drop.riderId)) Remove(drop);
                return null;
            default: return null;
        }
    }

    /// <summary>A member left while driving: the car stays where they were last seen.</summary>
    public static void ReleaseRider(string riderId, Vector3 position, float ry)
    {
        foreach (var drop in records.Values) if (drop.riderId == riderId) Park(drop, position, ry);
    }

    public static List<PlacedVehicleSave> CaptureVehicles()
    {
        var list = new List<PlacedVehicleSave>();
        // A server's cars live in the server's world file, never in this PC's.
        foreach (var drop in records.Values) if (drop.placedVehicle && drop.netId == 0 && !drop.contents.GetSlot(0).IsEmpty) list.Add(ToSave(drop));
        return list;
    }

    /// <summary>
    /// Makes the placed vehicles equal to a snapshot's. Clients get one every 10s, so unchanged cars are left alone
    /// and a car this PC is driving keeps going; it is only left when the snapshot no longer has it (another world).
    /// </summary>
    public static void RestoreVehicles(List<PlacedVehicleSave> list)
    {
        var keep = new HashSet<string>();
        if (list != null)
            foreach (var v in list)
            {
                if (string.IsNullOrEmpty(v.id)) continue;
                keep.Add(v.id);
                if (!records.TryGetValue(v.id, out var drop)) { drop = FromSave(v); if (drop != null) Add(drop); continue; }
                if (drop.instance && drop.instance.rider) continue; // driving it here
                string riderId = string.IsNullOrEmpty(v.rider) || v.rider == GameSession.LocalPlayerId ? null : v.rider;
                if (riderId != null) { drop.riderId = riderId; if (drop.instance) drop.instance.ShowParked(false); }
                else if (drop.riderId != null || (drop.position - v.pos).sqrMagnitude > 1e-4f) Park(drop, v.pos, v.ry);
            }
        foreach (var drop in new List<DropRecord>(records.Values))
        {
            if (!drop.placedVehicle || drop.netId != 0 || keep.Contains(drop.id)) continue;
            if (drop.instance && drop.instance.rider && drop.instance.rider.IsRiding) drop.instance.rider.Dismount(false);
            Remove(drop);
        }
    }

    // ---- sync-layer server (12.8): DropState entities ----

    static bool OnServer => SyncClient.Instance && SyncClient.Instance.Connected;
    static string NetKey(ulong id) => "net:" + id;

    static DropRecord RecordOf(DropState state) =>
        state && state.Entity && state.Entity.IsSpawned && records.TryGetValue(NetKey(state.Entity.Id), out var drop) && drop.sync == state ? drop : null;

    static void Status(string message)
    {
        var inventory = PlayerSpawner.Player ? PlayerSpawner.Player.GetComponent<PlayerInventory>() : null;
        if (inventory) inventory.SetStatus(message);
    }

    // Shares a drop or a placed car this PC just made: Spawn, then give it to the server at once (Release).
    static bool SyncCommit(DropRecord drop, byte kind, out string error)
    {
        error = null;
        WorldStateSync.UploadBooks(drop.contents); // a book's pages reach the server before the drop that names it (12.10.3)
        byte[] packed = DropState.Pack(drop.contents);
        var go = Instantiate(DropState.Template());
        go.name = "WorldDrop (mine)";
        DontDestroyOnLoad(go);
        var state = go.GetComponent<DropState>();
        state.kind = kind; state.position = drop.position; state.yaw = drop.rotation.eulerAngles.y; state.contents = packed;
        go.SetActive(true);
        var flags = EntityFlags.PersistOnLeave | EntityFlags.Claimable | (kind == DropState.ItemKind ? EntityFlags.Expiring : EntityFlags.None);
        var entity = SyncClient.Instance.Spawn(DropState.PrefabKey, go, flags);
        if (!entity) { Destroy(go); error = "서버에 연결되어 있지 않아 내려놓지 못했습니다."; return false; }
        drop.id = NetKey(entity.Id); drop.netId = entity.Id; drop.sync = state; drop.pending = true;
        Add(drop);
        SyncClient.Instance.Release(entity);
        return true;
    }

    // The drop could not be shared: its contents go back to the bag. Always false (the caller's answer).
    static bool GiveBack(DropRecord drop)
    {
        var stack = drop.contents.GetSlot(0);
        var bag = InventoryManager.Instance ? InventoryManager.Instance.State : null;
        if (stack != null && !stack.IsEmpty && (bag == null || !drop.contents.TryTransferTo(bag, 0, stack.Count, out _)))
            Debug.LogWarning("[WorldDroppedItem] could not return " + stack.DisplayName + " to the bag");
        InventoryLedgerRules.Trust(bag, "drop");
        return false;
    }

    // Only a server-owned entity can be claimed; while my own Release is on its way it is still mine here.
    bool TryClaim(ClaimFor what, out string error)
    {
        error = "";
        var entity = record.sync ? record.sync.Entity : null;
        if (!OnServer || !entity || entity.Owner != 0 || record.claim != ClaimFor.None) { error = "잠시 후 다시 해 주세요."; return false; }
        record.claim = what;
        SyncClient.Instance.Claim(entity);
        return true;
    }

    // Removes a record of mine and its entity (a Despawn for everyone, since I own it).
    static void DestroyNet(DropRecord drop)
    {
        var go = drop.sync ? drop.sync.gameObject : null;
        drop.sync = null;
        Remove(drop);
        if (go) Destroy(go);
    }

    /// <summary>A DropState got its state from the server: the record is made (id = entity id) or moved.</summary>
    internal static void SyncState(DropState state)
    {
        var entity = state.Entity;
        if (!entity || !entity.IsSpawned) return;
        if (records.TryGetValue(NetKey(entity.Id), out var drop))
        {
            if (drop.instance && drop.instance.rider) return; // I drive it
            if (drop.riderId == null) Park(drop, state.position, state.yaw);
            else { drop.position = state.position; drop.rotation = Quaternion.Euler(0, state.yaw, 0); } // shown again when given back
            return;
        }
        drop = new DropRecord { id = NetKey(entity.Id), netId = entity.Id, sync = state, scenePath = entity.Map, position = state.position,
            rotation = Quaternion.Euler(0, state.yaw, 0), placedVehicle = state.kind == DropState.VehicleKind, font = LocalFont() };
        var stack = drop.contents.GetSlot(0);
        if (!DropState.Unpack(state.contents, drop.contents) || (stack = drop.contents.GetSlot(0)).IsEmpty || drop.placedVehicle && !stack.Item.IsVehicle)
        { Debug.LogWarning("[WorldDroppedItem] server drop " + entity.Id + " has no usable item"); return; }
        if (drop.placedVehicle && entity.Owner != 0 && !entity.IsMine) drop.riderId = Rider(entity.Owner);
        Add(drop);
    }

    static string Rider(uint connection) => "sync:" + connection;

    /// <summary>The owner changed (or a refused Claim was answered): finish my claim, hide or park a car someone drives.</summary>
    internal static void SyncOwner(DropState state)
    {
        var drop = RecordOf(state);
        if (drop == null) return;
        uint owner = state.Entity.Owner, me = SyncClient.Instance ? SyncClient.Instance.MyConnection : 0;
        var claim = drop.claim;
        drop.claim = ClaimFor.None;
        if (owner == 0) drop.pending = false; // the server has it now
        if (claim != ClaimFor.None)
        {
            if (owner != 0 && owner == me) { Claimed(drop, claim); return; }
            Status(owner == 0 ? "지금은 할 수 없습니다. 잠시 후 다시 해 주세요." : claim == ClaimFor.PickUp ? "다른 사람이 먼저 주웠습니다." : "다른 사람이 사용 중인 탈것입니다.");
        }
        if (!drop.placedVehicle) return;
        if (owner != 0 && owner != me) { drop.riderId = Rider(owner); if (drop.instance) drop.instance.ShowParked(false); }
        else if (owner == 0 && drop.riderId != null) Park(drop, state.position, state.yaw); // got out or left: where it was last seen
    }

    static void Claimed(DropRecord drop, ClaimFor claim)
    {
        var entity = drop.sync.Entity;
        switch (claim)
        {
            case ClaimFor.PickUp:
                var bag = InventoryManager.Instance ? InventoryManager.Instance.State : null;
                string error = "지금은 주울 수 없습니다.";
                if (bag == null || !drop.contents.TryTransferTo(bag, 0, drop.contents.GetSlot(0).Count, out error))
                { Status(error); SyncClient.Instance.Release(entity); return; }
                InventoryLedgerRules.Trust(bag, "pickup"); // 12.10.4: trusted until 4d-4
                // Despawned at once; the character save goes out after it, so a disconnect cannot keep both.
                DestroyNet(drop);
                Status("아이템을 주웠습니다.");
                return;
            case ClaimFor.Ride:
                var driver = PlayerSpawner.Player ? PlayerSpawner.Player.GetComponent<PlayerVehicle>() : null;
                if (drop.instance && drop.instance.MountLocal(driver)) return;
                Status("지금은 탑승할 수 없습니다.");
                SyncClient.Instance.Release(entity);
                return;
            case ClaimFor.Dismantle:
                // The same contents become an ordinary dropped item anyone can pick up; then the car goes.
                var item = new DropRecord { scenePath = drop.scenePath, position = drop.position, rotation = drop.rotation, font = drop.font };
                if (!drop.contents.TryTransferTo(item.contents, 0, 1, out error) || !SyncCommit(item, DropState.ItemKind, out error))
                {
                    if (!item.contents.GetSlot(0).IsEmpty) item.contents.TryTransferTo(drop.contents, 0, 1, out _);
                    Status(error);
                    SyncClient.Instance.Release(entity);
                    return;
                }
                DestroyNet(drop);
                return;
        }
    }

    /// <summary>The entity is gone (picked up, cleared at 06:00, off my map, or my Spawn was refused): so is the record.</summary>
    internal static void SyncGone(DropState state)
    {
        var drop = RecordOf(state);
        if (drop == null) return;
        if (drop.pending && OnServer) { GiveBack(drop); Status("서버가 받지 않아 내려놓지 못했습니다."); }
        drop.sync = null;
        Remove(drop);
    }

    // Joining: this PC's own drops hide. Leaving: every server record goes and this PC's drops show again.
    static void OnServerConnection(bool connected)
    {
        if (connected)
        {
            foreach (var drop in records.Values)
                if (drop.netId == 0 && drop.instance) { Destroy(drop.instance.gameObject); drop.instance = null; }
            return;
        }
        foreach (var drop in new List<DropRecord>(records.Values)) if (drop.netId != 0) DestroyNet(drop); // the connection is gone: no Despawn goes out
        foreach (var drop in records.Values)
        {
            if (drop.instance) continue;
            var scene = SceneManager.GetSceneByPath(drop.scenePath);
            if (scene.IsValid() && scene.isLoaded) CreateVisual(drop, scene);
        }
    }

    /// <summary>Server records (QA): count and how many of them are placed vehicles.</summary>
    public static int ServerDropCount(bool vehicles)
    {
        int n = 0;
        foreach (var drop in records.Values) if (drop.netId != 0 && drop.placedVehicle == vehicles) n++;
        return n;
    }

    /// <summary>The server entity id of this drop (0 = this PC's own), for QA.</summary>
    public ulong NetId => record?.netId ?? 0;

    void BuildVisual()
    {
        if (IsPlacedVehicle)
        {
            vehicleModel = Instantiate(Item.vehiclePrefab, transform, false);
            vehicleModel.name = "PlacedVehicleModel";
            vehicleMeshParts = vehicleModel.GetComponentsInChildren<MeshFilter>();
            vehicleCollisionMesh = new Mesh { name = "VehicleSurface", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            effectMeshes.Add(vehicleCollisionMesh);
            vehicleCollider = gameObject.AddComponent<MeshCollider>();
            RebuildVehicleCollider();
            billboard = new GameObject("VehiclePrompt").transform;
            billboard.SetParent(transform,false); billboard.localPosition = Vector3.up * (VehicleBounds.Measure(vehicleModel).max.y + .45f);
            BuildPickupPopup();
            return;
        }
        var physics = gameObject.AddComponent<Rigidbody>();
        physics.isKinematic = true;
        physics.useGravity = false;
        var collider = gameObject.AddComponent<BoxCollider>();
        collider.center = new Vector3(0, .24f, 0);
        collider.size = new Vector3(.62f, .62f, .62f);
        collider.isTrigger = true;
        billboard = new GameObject("ItemBillboard").transform;
        billboard.SetParent(transform, false);
        billboard.localPosition = Vector3.up * .98f;
        floatingItem = new GameObject("FloatingItem").transform;
        floatingItem.SetParent(billboard, false);
        floatPhase = Mathf.Repeat(record.id.GetHashCode() * .001f, Mathf.PI * 2f);
        BuildItemIcon();
        BuildDropGlow();
        BuildPickupPopup();
    }

    // Physics uses the actual surface. Screen selection below fills only enclosed holes,
    // preserving concave openings such as the space above the step-through frame.
    void RebuildVehicleCollider()
    {
        if (!vehicleCollisionMesh || !(vehicleCollider is MeshCollider meshCollider)) return;
        var parts = new List<CombineInstance>();
        foreach (var part in vehicleMeshParts)
        {
            if (!part || !part.sharedMesh || !part.gameObject.activeInHierarchy) continue;
            for (int submesh=0;submesh<part.sharedMesh.subMeshCount;submesh++)
                parts.Add(new CombineInstance { mesh=part.sharedMesh, subMeshIndex=submesh,
                    transform=transform.worldToLocalMatrix*part.transform.localToWorldMatrix });
        }
        meshCollider.sharedMesh=null;
        vehicleCollisionMesh.Clear();
        vehicleCollisionMesh.CombineMeshes(parts.ToArray(),true,true);
        meshCollider.sharedMesh=vehicleCollisionMesh;
        selectionVertices=vehicleCollisionMesh.vertices;
        selectionTriangles=vehicleCollisionMesh.triangles;
        selectionProjected=new Vector3[selectionVertices.Length];
        selectionValid=false;
    }

    public static WorldDroppedItem FindPointedVehicle(PlayerInventory owner, Camera camera, Vector2 pointer)
    {
        WorldDroppedItem nearest=null;
        float distance=30f;
        foreach(var entry in records.Values)
        {
            var item=entry.instance;
            if(!item || !item.IsPlacedVehicle || item.IsOccupied || !item.IsReachableFrom(owner,3f))continue;
            if(item.TryGetVehiclePointerHit(camera,pointer,out float hitDistance) && hitDistance<distance)
            { nearest=item;distance=hitDistance; }
        }
        if(!nearest)return null;
        // A filled wheel/frame hole still has a depth: walls in front must block selection.
        foreach(var hit in Physics.RaycastAll(camera.ScreenPointToRay(pointer),distance+.005f,~0,QueryTriggerInteraction.Ignore))
        {
            if(hit.transform.IsChildOf(owner.transform) || hit.transform.IsChildOf(nearest.transform))continue;
            if(hit.distance<distance-.005f)return null;
        }
        return nearest;
    }

    public bool TryGetVehiclePointerHit(Camera camera, Vector2 pointer, out float distance)
    {
        distance=0f;
        if(!camera || !IsPlacedVehicle || IsOccupied || !vehicleCollider || !vehicleCollider.enabled ||
            !camera.pixelRect.Contains(pointer) || selectionVertices==null)return false;
        var ray=camera.ScreenPointToRay(pointer);
        if(!vehicleCollider.bounds.IntersectRay(ray))return false;
        UpdateVehicleSelection(camera);
        int x=Mathf.FloorToInt(pointer.x/SelectionPixelSize)-selectionX,y=Mathf.FloorToInt(pointer.y/SelectionPixelSize)-selectionY;
        if(x<0 || y<0 || x>=selectionWidth || y>=selectionHeight)return false;
        int index=y*selectionWidth+x;
        if(selectionMask[index]!=1 && selectionMask[index]!=3)return false;
        var world=camera.ScreenToWorldPoint(new Vector3(pointer.x,pointer.y,selectionDepth[index]));
        distance=Vector3.Dot(world-ray.origin,ray.direction);
        return distance>=0f;
    }

    // Project the visible geometry, flood background from the border, then fill only the
    // remaining enclosed holes. No rectangle, convex hull or top-to-bottom span is accepted.
    void UpdateVehicleSelection(Camera camera)
    {
        var viewProjection=camera.projectionMatrix*camera.worldToCameraMatrix;
        var modelMatrix=transform.localToWorldMatrix;
        var rect=camera.pixelRect;
        if(selectionValid && selectionCamera==camera && selectionViewProjection==viewProjection &&
            selectionModelMatrix==modelMatrix && selectionCameraRect==rect)return;
        selectionCamera=camera;selectionViewProjection=viewProjection;selectionModelMatrix=modelMatrix;selectionCameraRect=rect;
        rect=new Rect(rect.x/SelectionPixelSize,rect.y/SelectionPixelSize,rect.width/SelectionPixelSize,rect.height/SelectionPixelSize);
        float minX=rect.xMax,minY=rect.yMax,maxX=rect.xMin,maxY=rect.yMin;
        bool crossesNear=false;
        for(int i=0;i<selectionVertices.Length;i++)
        {
            var point=ProjectVehicleVertex(camera,modelMatrix.MultiplyPoint3x4(selectionVertices[i]));
            selectionProjected[i]=point;
            if(point.z<camera.nearClipPlane){crossesNear=true;continue;}
            minX=Mathf.Min(minX,point.x);minY=Mathf.Min(minY,point.y);
            maxX=Mathf.Max(maxX,point.x);maxY=Mathf.Max(maxY,point.y);
        }
        if(crossesNear){minX=rect.xMin;minY=rect.yMin;maxX=rect.xMax;maxY=rect.yMax;}
        selectionX=Mathf.Max(Mathf.FloorToInt(rect.xMin)-2,Mathf.FloorToInt(minX)-2);
        selectionY=Mathf.Max(Mathf.FloorToInt(rect.yMin)-2,Mathf.FloorToInt(minY)-2);
        selectionWidth=Mathf.Max(1,Mathf.Min(Mathf.CeilToInt(rect.xMax)+2,Mathf.CeilToInt(maxX)+2)-selectionX+1);
        selectionHeight=Mathf.Max(1,Mathf.Min(Mathf.CeilToInt(rect.yMax)+2,Mathf.CeilToInt(maxY)+2)-selectionY+1);
        int size=selectionWidth*selectionHeight;
        if(selectionMask==null || selectionMask.Length<size)
        {
            int capacity=Mathf.NextPowerOfTwo(size);
            selectionMask=new byte[capacity];selectionDepth=new float[capacity];selectionQueue=new int[capacity];
        }
        else Array.Clear(selectionMask,0,size);
        for(int i=0;i<selectionTriangles.Length;i+=3)
        {
            int ia=selectionTriangles[i],ib=selectionTriangles[i+1],ic=selectionTriangles[i+2];
            var a=selectionProjected[ia];var b=selectionProjected[ib];var c=selectionProjected[ic];
            if(a.z>=camera.nearClipPlane && b.z>=camera.nearClipPlane && c.z>=camera.nearClipPlane)
            { RasterVehicleTriangle(a,b,c,camera.orthographic);continue; }
            int count=0;
            ClipVehicleEdge(camera,ic,ia,ref count);
            ClipVehicleEdge(camera,ia,ib,ref count);
            ClipVehicleEdge(camera,ib,ic,ref count);
            for(int j=1;j+1<count;j++)RasterVehicleTriangle(selectionClip[0],selectionClip[j],selectionClip[j+1],camera.orthographic);
        }
        int head=0,tail=0;
        for(int x=0;x<selectionWidth;x++){QueueVehicleOutside(x,ref tail);QueueVehicleOutside((selectionHeight-1)*selectionWidth+x,ref tail);}
        for(int y=1;y<selectionHeight-1;y++){QueueVehicleOutside(y*selectionWidth,ref tail);QueueVehicleOutside((y+1)*selectionWidth-1,ref tail);}
        while(head<tail)
        {
            int index=selectionQueue[head++],x=index%selectionWidth;
            if(x>0)QueueVehicleOutside(index-1,ref tail);
            if(x+1<selectionWidth)QueueVehicleOutside(index+1,ref tail);
            if(index>=selectionWidth)QueueVehicleOutside(index-selectionWidth,ref tail);
            if(index+selectionWidth<size)QueueVehicleOutside(index+selectionWidth,ref tail);
        }
        // Propagate the nearest boundary's depth only into enclosed holes, never exterior.
        head=tail=0;
        for(int i=0;i<size;i++)
            if(selectionMask[i]==1 && ((i%selectionWidth>0 && selectionMask[i-1]==0) ||
                (i%selectionWidth+1<selectionWidth && selectionMask[i+1]==0) ||
                (i>=selectionWidth && selectionMask[i-selectionWidth]==0) ||
                (i+selectionWidth<size && selectionMask[i+selectionWidth]==0)))selectionQueue[tail++]=i;
        while(head<tail)
        {
            int index=selectionQueue[head++],x=index%selectionWidth;
            if(x>0)QueueVehicleHole(index-1,index,ref tail);
            if(x+1<selectionWidth)QueueVehicleHole(index+1,index,ref tail);
            if(index>=selectionWidth)QueueVehicleHole(index-selectionWidth,index,ref tail);
            if(index+selectionWidth<size)QueueVehicleHole(index+selectionWidth,index,ref tail);
        }
        selectionValid=true;
    }

    void ClipVehicleEdge(Camera camera,int from,int to,ref int count)
    {
        var a=selectionProjected[from];var b=selectionProjected[to];
        bool insideA=a.z>=camera.nearClipPlane,insideB=b.z>=camera.nearClipPlane;
        if(insideA!=insideB)
        {
            float t=(camera.nearClipPlane-a.z)/(b.z-a.z);
            var local=Vector3.LerpUnclamped(selectionVertices[from],selectionVertices[to],t);
            selectionClip[count++]=ProjectVehicleVertex(camera,transform.TransformPoint(local));
        }
        if(insideB)selectionClip[count++]=b;
    }

    static Vector3 ProjectVehicleVertex(Camera camera,Vector3 world)
    {
        var point=camera.WorldToScreenPoint(world);
        point.x/=SelectionPixelSize;point.y/=SelectionPixelSize;
        return point;
    }

    static float VehicleEdge(Vector3 a,Vector3 b,float x,float y) => (b.x-a.x)*(y-a.y)-(b.y-a.y)*(x-a.x);
    void RasterVehicleTriangle(Vector3 a,Vector3 b,Vector3 c,bool orthographic)
    {
        float area=VehicleEdge(a,b,c.x,c.y);
        if(Mathf.Abs(area)<.00001f)return;
        float sign=area>0?1f:-1f;
        int left=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.x,Mathf.Min(b.x,c.x)))-selectionX);
        int right=Mathf.Min(selectionWidth-1,Mathf.CeilToInt(Mathf.Max(a.x,Mathf.Max(b.x,c.x)))-selectionX);
        int bottom=Mathf.Max(0,Mathf.FloorToInt(Mathf.Min(a.y,Mathf.Min(b.y,c.y)))-selectionY);
        int top=Mathf.Min(selectionHeight-1,Mathf.CeilToInt(Mathf.Max(a.y,Mathf.Max(b.y,c.y)))-selectionY);
        // Conservative half-pixel coverage keeps connected thin tubes from leaking through
        // rasterization cracks. It does not bridge the open concavity above the bicycle.
        float biasA=(Mathf.Abs(c.x-b.x)+Mathf.Abs(c.y-b.y))*.5f;
        float biasB=(Mathf.Abs(a.x-c.x)+Mathf.Abs(a.y-c.y))*.5f;
        float biasC=(Mathf.Abs(b.x-a.x)+Mathf.Abs(b.y-a.y))*.5f;
        float stepA=(b.y-c.y)*sign,stepB=(c.y-a.y)*sign,stepC=(a.y-b.y)*sign;
        float inverseA=1f/a.z,inverseB=1f/b.z,inverseC=1f/c.z;
        for(int y=bottom;y<=top;y++)
        {
            float py=selectionY+y+.5f,spanMin=selectionX+left+.5f,spanMax=selectionX+right+.5f;
            // Raster only the triangle's occupied horizontal span. Thin diagonal spokes
            // must not require scanning their entire (mostly empty) bounding rectangles.
            if(!VehicleSpan(b,c,sign,biasA,py,ref spanMin,ref spanMax) ||
                !VehicleSpan(c,a,sign,biasB,py,ref spanMin,ref spanMax) ||
                !VehicleSpan(a,b,sign,biasC,py,ref spanMin,ref spanMax))continue;
            int first=Mathf.Max(left,Mathf.CeilToInt(spanMin-selectionX-.5001f));
            int last=Mathf.Min(right,Mathf.FloorToInt(spanMax-selectionX-.4999f));
            float px=selectionX+first+.5f;
            float wa=VehicleEdge(b,c,px,py)*sign,wb=VehicleEdge(c,a,px,py)*sign,wc=VehicleEdge(a,b,px,py)*sign;
            for(int x=first;x<=last;x++,wa+=stepA,wb+=stepB,wc+=stepC)
            {
                float pa=wa>0?wa:0,pb=wb>0?wb:0,pc=wc>0?wc:0,sum=pa+pb+pc;
                float depth=orthographic?(pa*a.z+pb*b.z+pc*c.z)/sum:sum/(pa*inverseA+pb*inverseB+pc*inverseC);
                int index=y*selectionWidth+x;
                if(selectionMask[index]==0 || depth<selectionDepth[index]){selectionMask[index]=1;selectionDepth[index]=depth;}
            }
        }
    }
    static bool VehicleSpan(Vector3 a,Vector3 b,float sign,float bias,float y,ref float left,ref float right)
    {
        float slope=(a.y-b.y)*sign;
        float intercept=((b.x-a.x)*(y-a.y)+(b.y-a.y)*a.x)*sign+bias;
        if(Mathf.Abs(slope)<.000001f)return intercept>=0;
        float boundary= -intercept/slope;
        if(slope>0)left=Mathf.Max(left,boundary);else right=Mathf.Min(right,boundary);
        return left<=right;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    void QueueVehicleOutside(int index,ref int tail)
    {
        if(selectionMask[index]!=0)return;
        selectionMask[index]=2;selectionQueue[tail++]=index;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    void QueueVehicleHole(int index,int source,ref int tail)
    {
        if(selectionMask[index]!=0)return;
        selectionMask[index]=3;selectionDepth[index]=selectionDepth[source];selectionQueue[tail++]=index;
    }

    void LateUpdate()
    {
        if (IsOccupied)
        {
            if (!rider) return; // another player drives it: their avatar shows the car
            // Persist the last ridden position in the existing scene-drop record.
            record.position = rider.transform.position; record.rotation = rider.transform.rotation;
            transform.SetPositionAndRotation(record.position,record.rotation);
            ShareRide(false);
            return;
        }
        var camera = Camera.main;
        if (billboard && camera) billboard.rotation = Quaternion.LookRotation(camera.transform.forward, camera.transform.up);
        // Animate only the artwork; the pickup collider and saved drop point stay on the floor.
        if (floatingItem) floatingItem.position = transform.position + Vector3.up * (.57f + Mathf.Sin(Time.time * 1.9f + floatPhase) * .075f);
        if (spinningModel) spinningModel.localRotation = Quaternion.Euler(0f, Time.time * 32f + floatPhase * Mathf.Rad2Deg, 0f);
        if (glowBeam && camera)
        {
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude > .001f) glowBeam.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
        UpdatePickupPopup(camera);
    }

    void BuildDropGlow()
    {
        var material = Resources.Load<Material>("Effects/DroppedItemGlow");
        if (!material) return;
        CreateGlowQuad("GroundGlow", material, false);
        glowBeam = CreateGlowQuad("RisingGlow", material, true);

        var host = new GameObject("RisingGoldenSparks");
        host.transform.SetParent(transform, false);
        host.transform.localPosition = Vector3.up * -.025f;
        host.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var particles = host.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = true; main.duration = 2f; main.prewarm = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.42f, .72f);
        main.startSize = new ParticleSystem.MinMaxCurve(.045f, .085f);
        main.startColor = new Color(1f, .92f, .4f, .85f);
        main.gravityModifier = 0f; main.maxParticles = 20;
        var emission = particles.emission; emission.rateOverTime = 10f;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 4f; shape.radius = .23f;
        var color = particles.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .15f), new GradientAlphaKey(.65f, .65f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material; renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = 19;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
    }

    Transform CreateGlowQuad(string name, Material material, bool beam)
    {
        var host = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        host.transform.SetParent(transform, false);
        host.transform.localPosition = Vector3.up * -.045f;
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        mesh.vertices = beam
            ? new[] { new Vector3(-.38f, 0f, 0f), new Vector3(.38f, 0f, 0f), new Vector3(.27f, 1.1f, 0f), new Vector3(-.27f, 1.1f, 0f) }
            : new[] { new Vector3(-.48f, 0f, -.48f), new Vector3(.48f, 0f, -.48f), new Vector3(.48f, 0f, .48f), new Vector3(-.48f, 0f, .48f) };
        mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds();
        effectMeshes.Add(mesh); host.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.sortingOrder = beam ? 18 : 17;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        var properties = new MaterialPropertyBlock();
        properties.SetFloat("_Beam", beam ? 1f : 0f);
        properties.SetColor("_Tint", new Color(1f, .73f, .12f, beam ? .34f : .7f));
        renderer.SetPropertyBlock(properties);
        return host.transform;
    }

    void BuildPickupPopup()
    {
        var canvasObject = new GameObject("PickupPopup", typeof(RectTransform), typeof(Canvas));
        canvasObject.transform.SetParent(billboard, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 25;
        pickupPopup = canvasObject.GetComponent<RectTransform>();
        pickupPopup.sizeDelta = new Vector2(320f, 42f);
        pickupPopup.localScale = Vector3.one * .009f;

        var panelObject = new GameObject("Panel", typeof(RectTransform), typeof(InventoryRoundedGraphic));
        var panel = panelObject.GetComponent<RectTransform>();
        panel.SetParent(pickupPopup, false);
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;
        var graphic = panelObject.GetComponent<InventoryRoundedGraphic>();
        graphic.color = new Color(.08f, .07f, .065f, .70f);
        graphic.borderColor = new Color(1f, .96f, .88f, .42f);
        graphic.largeRadius = 18f;
        graphic.smallRadius = 9f;
        graphic.borderWidth = 1.5f;
        graphic.raycastTarget = false;

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(panel, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 3f);
        textRect.offsetMax = new Vector2(-8f, -3f);
        pickupText = textObject.GetComponent<TextMeshProUGUI>();
        if (record.font) pickupText.font = record.font;
        pickupText.fontSize = 19f;
        pickupText.color = new Color(1f, .98f, .93f, .98f);
        pickupText.alignment = TextAlignmentOptions.Center;
        pickupText.textWrappingMode = TextWrappingModes.NoWrap;
        pickupText.overflowMode = TextOverflowModes.Ellipsis;
        pickupText.raycastTarget = false;
        pickupText.text = PickupPromptText();
        pickupPopup.gameObject.SetActive(false);
    }

    void UpdatePickupPopup(Camera camera)
    {
        if (!pickupPopup || record == null || Count <= 0)
        {
            if (pickupPopup) pickupPopup.gameObject.SetActive(false);
            return;
        }
        if (camera)
        {
            var canvas = pickupPopup.GetComponent<Canvas>();
            if (canvas) canvas.worldCamera = camera;
        }
        bool visible = false;
        foreach (var player in FindObjectsByType<PlayerInventory>())
        {
            if (!player || gameObject.scene != SceneLoadManager.CurrentMap || !player.CanPickUpWorldItems) continue;
            if (!IsReachableFrom(player, 3f)) continue;
            visible = true;
            break;
        }
        if (pickupText && visible) pickupText.text = PickupPromptText();
        if (visible && camera) pickupPopup.localScale = Vector3.one * (PromptScalePerMeter * Mathf.Max(1.5f, Vector3.Distance(camera.transform.position, pickupPopup.position)));
        if (pickupPopup.gameObject.activeSelf != visible) pickupPopup.gameObject.SetActive(visible);
    }

    string PickupPromptText()
    {
        return (record?.contents.GetSlot(0)?.DisplayName ?? "아이템") + (IsPlacedVehicle ? "   [좌클릭 메뉴]" : "   [줍기 F]");
    }

    void BuildItemIcon()
    {
        if (!Item || !Item.icon) return;
        if (Item.heldPrefab && Item.icon.texture.name == "StoreFoods")
        {
            var model = Instantiate(Item.heldPrefab, floatingItem, false);
            model.name = "DroppedFoodModel";
            model.transform.localPosition = new Vector3(0f, -.1f, 0f);
            model.transform.localRotation = Quaternion.Euler(0, 18, 0);
            model.transform.localScale = Vector3.one * 1.4f;
            spinningModel = model.transform;
            return;
        }
        var iconHost = new GameObject("DroppedItemIcon", typeof(SpriteRenderer));
        iconHost.transform.SetParent(floatingItem, false);
        iconHost.transform.localPosition = Vector3.zero;
        var renderer = iconHost.GetComponent<SpriteRenderer>();
        renderer.sprite = Item.icon;
        renderer.color = Color.white;
        renderer.sortingOrder = 24;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        float width = Mathf.Max(.001f, Item.icon.bounds.size.x);
        iconHost.transform.localScale = Vector3.one * (.42f / width);
    }

    void OnDestroy()
    {
        if (rider) rider.Dismount(false);
        foreach (var mesh in effectMeshes) if (mesh) Destroy(mesh);
        if (record != null && record.instance == this) record.instance = null;
    }
}
