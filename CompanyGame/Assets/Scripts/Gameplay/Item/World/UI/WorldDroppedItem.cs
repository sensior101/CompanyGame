using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A pickable world stack. Session records survive Single-scene travel; each visual
/// and collider belongs only to its map. Pickup removes the record after an atomic transfer.
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
        public InventoryState contents = new InventoryState();
        public TMP_FontAsset font;
        public WorldDroppedItem instance;
    }
    static readonly Dictionary<string, DropRecord> records = new Dictionary<string, DropRecord>();
    DropRecord record;
    Transform billboard;
    Transform floatingItem, spinningModel, glowBeam;
    readonly List<Mesh> effectMeshes = new List<Mesh>();
    float floatPhase;
    RectTransform pickupPopup;
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
    public bool IsOccupied => rider && rider.IsRiding;
    public string VehicleOwnershipKey => record?.contents.GetSlot(0)?.OwnershipKey;
    public string VehicleOwner => PropertyRegistry.Instance ? PropertyRegistry.Instance.GetOwner(VehicleOwnershipKey) : null;
    public bool VehicleLocked => PropertyRegistry.Instance && PropertyRegistry.Instance.IsVehicleLocked(VehicleOwnershipKey);
    public bool IsHighlighted => hoverOutline && hoverOutline.activeSelf;

    public bool CanManageVehicle(string actor) => IsPlacedVehicle && !IsOccupied && PropertyRegistry.Instance &&
        PropertyRegistry.Instance.CanManageVehicle(VehicleOwnershipKey,actor);
    public bool TrySetVehicleLocked(string actor, bool locked)
    {
        return IsPlacedVehicle && !IsOccupied && PropertyRegistry.Instance &&
            PropertyRegistry.Instance.TrySetVehicleLocked(VehicleOwnershipKey,actor,locked);
    }

    // The same instance moves from installed geometry to the existing dropped-item representation.
    public bool TryDismantleVehicle(string actor, out string error)
    {
        error = null;
        if (!IsPlacedVehicle || IsOccupied) return false;
        if (!CanManageVehicle(actor)) { error="소유자만 탈것을 해체할 수 있습니다."; return false; }
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
        { error = "자전거는 실외에서 설치해 주세요."; return false; }
        if (!TryFindVehiclePoint(owner, out var point, out var rotation))
        { error = "앞쪽에 자전거를 설치할 평평한 공간이 부족합니다."; return false; }
        var drop = new DropRecord { id = Guid.NewGuid().ToString("N"), scenePath = SceneLoadManager.CurrentMap.path,
            position = point, rotation = rotation, placedVehicle = true, font = owner.uiFont };
        if (!owner.Inventory.TryTransferTo(drop.contents, owner.Inventory.SelectedHotbarIndex, 1, out error)) return false;
        Commit(drop, SceneLoadManager.CurrentMap);
        return true;
    }

    static bool TryFindVehiclePoint(PlayerInventory owner, out Vector3 point, out Quaternion rotation)
    {
        point = default; rotation = Quaternion.identity;
        var motor = owner.GetComponent<PlayerMovement>();
        var forward = Vector3.ProjectOnPlane(motor.viewCamera ? motor.viewCamera.transform.forward : owner.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .5f) forward = owner.transform.forward;
        rotation = Quaternion.LookRotation(forward);
        var target = owner.transform.position + forward * 1.85f;
        var floors = Physics.RaycastAll(target + Vector3.up * 1.4f, Vector3.down, 2.8f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(floors, (a,b) => a.distance.CompareTo(b.distance));
        foreach (var floor in floors)
        {
            if (floor.transform.IsChildOf(owner.transform) || floor.normal.y < .94f || Mathf.Abs(floor.point.y-owner.transform.position.y) > .6f) continue;
            var candidate = floor.point + Vector3.up * .025f;
            bool blocked = false;
            foreach (float end in new[] { -.72f, .72f })
            {
                if (!Physics.Raycast(candidate + forward * end + Vector3.up * .4f, Vector3.down, out var support, .8f, ~0, QueryTriggerInteraction.Ignore) ||
                    support.normal.y < .94f || Mathf.Abs(support.point.y-floor.point.y) > .12f) { blocked = true; break; }
            }
            foreach (var hit in Physics.OverlapBox(candidate + Vector3.up * .82f, new Vector3(.42f,.68f,1.03f), rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(owner.transform) && hit.bounds.max.y > candidate.y + .15f) { blocked = true; break; }
            foreach (var hit in Physics.SphereCastAll(owner.transform.position + Vector3.up * .8f, .25f, forward, 1.85f, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(owner.transform) && hit.normal.y < .7f) { blocked = true; break; }
            if (!blocked) { point = candidate; return true; }
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
        vehicleCollider.enabled = false;
        if (!driver.TryMount(Item, vehicleModel, ReturnVehicleToGround)) { vehicleCollider.enabled = true; return false; }
        rider = driver;
        if (pickupPopup) pickupPopup.gameObject.SetActive(false);
        return true;
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
    }

    public static bool TryDropStorage(PlayerInventory owner, int index, out string error,int count=-1)
    {
        error = null;
        var source = owner ? owner.Inventory.GetSlot(index) : null;
        if (source == null || source.IsEmpty) { error = "내려놓을 아이템이 없습니다."; return false; }
        if (!Prepare(owner, out var drop, out error)) return false;
        if (!owner.Inventory.TryTransferTo(drop.contents, index, count<0?source.Count:count, out error)) return false;
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
        if (IsPlacedVehicle) { error="설치된 탈것은 우클릭으로 해체한 다음 F로 주워 주세요."; return false; }
        if (IsOccupied || !owner || !owner.CanPickUpWorldItems || record == null ||
            !records.ContainsKey(record.id) || gameObject.scene != SceneLoadManager.CurrentMap)
        { error = "지금은 주울 수 없습니다."; return false; }
        if (!IsReachableFrom(owner, 4f))
        { error = "아이템에 조금 더 가까이 가 주세요."; return false; }
        int quantity = Count;
        if (quantity <= 0) { error = "이미 주운 아이템입니다."; return false; }
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
        var host = new GameObject("DroppedItem_" + drop.id);
        host.transform.SetPositionAndRotation(drop.position,drop.rotation);
        SceneManager.MoveGameObjectToScene(host, scene);
        var component = host.AddComponent<WorldDroppedItem>();
        component.record = drop;
        drop.instance = component;
        component.BuildVisual();
    }

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
            billboard.SetParent(transform,false); billboard.localPosition = Vector3.up * 1.5f;
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
            // Persist the last ridden position in the existing scene-drop record.
            record.position = rider.transform.position; record.rotation = rider.transform.rotation;
            transform.SetPositionAndRotation(record.position,record.rotation);
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
        pickupPopup.sizeDelta = new Vector2(IsPlacedVehicle ? 380f : 240f, 42f);
        pickupPopup.localScale = Vector3.one * (IsPlacedVehicle ? .003f : .009f);

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
        if (pickupPopup.gameObject.activeSelf != visible) pickupPopup.gameObject.SetActive(visible);
    }

    string PickupPromptText()
    {
        return (record?.contents.GetSlot(0)?.DisplayName ?? "아이템") + (IsPlacedVehicle ? (VehicleLocked ? "   [잠금 / Space 탑승]" : "   [Space 탑승]") : "   [줍기 F]");
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
