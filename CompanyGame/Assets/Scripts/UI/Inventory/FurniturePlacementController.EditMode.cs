using System;
using System.Collections.Generic;
using CompanyGame.World.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

/// <summary>The editing state of the existing furniture controller; no second placement or inventory manager.</summary>
public sealed partial class FurniturePlacementController
{
    enum EditGesture { None, Orbit, Pan, Move, Wall }
    sealed class EditChange
    {
        public string furniture;
        public Vector3 before, after;
        public float beforeYaw, afterYaw;
        public PlacedWallSave wallBefore, wallAfter;
    }
    bool editing, wallTool, gridEnabled = true, candidateValid;
    PropertyZone editZone;
    Camera editCamera;
    Vector3 savedCameraPosition, cameraFocus, pointerStart, moveOffset;
    Quaternion savedCameraRotation;
    bool savedOrthographic;
    float savedOrthoSize, savedFov, cameraYaw, cameraPitch, cameraSize;
    readonly ControlLock editControls = new();
    EditGesture gesture;
    string selectedFurniture, selectedWall;
    PlacedWallSave wallCandidate;
    Vector3 moveBefore;
    float moveYawBefore;
    Vector2 movePointerDown;
    bool moveDragged;
    GameObject outline, gridRoot;
    Material outlineMaterial, gridMaterial;
    readonly List<KeyValuePair<Renderer, bool>> cutaway = new(), movingRenderers = new();
    readonly List<EditChange> undo = new(), redo = new();
    readonly List<RaycastResult> editUIHits = new();
    Canvas editCanvas;
    TMP_Text editStatus;
    string editMessage = "가구를 클릭하세요.";
    UnityEngine.UI.Button selectButton, wallButton, rotateButton, removeButton;
    TMP_Text removeLabel;
    readonly List<Canvas> hiddenCanvases = new();
    public static bool IsEditing => active && active.editing;

    void Update()
    {
        if (!GameInput.BuildPressed || UIEventSystem.IsEditingText()) return;
        if (editing) { ExitEditor(); return; }
        if (active || InputFocus.GameplayBlocked() || ChatUIManager.IsChatting || DialogueManager.OwnsInput ||
            PlayerInventory.IsAnyOpen || (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) ||
            (PauseMenu.Instance && PauseMenu.Instance.IsOpen)) return;
        EnterEditor(GetComponent<PlayerInventory>(), GetComponent<PlayerMovement>());
    }

    public bool EnterEditor(PlayerInventory inventory, PlayerMovement motor)
    {
        if (editing || active || !inventory || !motor || !motor.isActiveAndEnabled || !motor.viewCamera || SceneLoadManager.IsLoading ||
            (motor.TryGetComponent<PlayerVehicle>(out var vehicle) && vehicle.IsRiding) ||
            (motor.TryGetComponent<PlayerSeating>(out var seat) && seat.IsSeated)) return false;
        var zone = FindZone(motor.transform.position);
        if (!CanEdit(zone)) { inventory.SetStatus("편집은 오프라인의 내 소유 공간 안에서 가능합니다."); return false; }
        if ((zone.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f || zone.size.y < .5f)
        { inventory.SetStatus("이 공간은 편집 가능한 치수로 설정되지 않았습니다."); return false; }
        owner = inventory; movement = motor; editZone = zone; editCamera = motor.viewCamera;
        savedCameraPosition = editCamera.transform.position; savedCameraRotation = editCamera.transform.rotation;
        savedOrthographic = editCamera.orthographic; savedOrthoSize = editCamera.orthographicSize; savedFov = editCamera.fieldOfView;
        editControls.Hold(motor);
        editing = true; active = this; startedFrame = Time.frameCount;
        wallTool = false; gesture = EditGesture.None; undo.Clear(); redo.Clear();
        cameraFocus = zone.transform.position;
        cameraYaw = zone.transform.eulerAngles.y; cameraPitch = 90f;
        cameraSize = Mathf.Clamp(Mathf.Max(zone.size.x, zone.size.z) * .6f, 2f, 35f);
        editCamera.orthographic = true;
        blue = NewPreviewMaterial();
        var edge = Resources.Load<Material>("Inventory/Vehicles/VehicleHoverOutline");
        if (edge) outlineMaterial = new Material(edge);
        HideCeiling(); BuildGrid(); BuildEditUI(); HideGameHud(); UpdateEditCamera();
        return true;
    }

    static Material NewPreviewMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", new Color(.12f, .48f, 1f, .43f));
        mat.SetFloat("_Surface", 1f); mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }
    static Bounds ModelBounds(GameObject model)
    {
        var measured = VehicleBounds.Measure(model); var scale = model.transform.lossyScale;
        return new Bounds(Vector3.Scale(measured.center, scale), Vector3.Scale(measured.size,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
    }
    GameObject VisualCopy(GameObject model, Material material, string label)
    {
        var copy = new GameObject(label); copy.transform.localScale = model.transform.lossyScale;
        copy.transform.SetPositionAndRotation(model.transform.position, model.transform.rotation);
        CopyPreviewMeshes(model.transform, copy.transform, material); return copy;
    }

    void TickEditor()
    {
        if (startedFrame == Time.frameCount) return;
        if (!editZone || !editCamera || !movement || !CanEdit(editZone) || SceneLoadManager.IsLoading ||
            editZone.gameObject.scene != SceneLoadManager.CurrentMap || !editZone.Contains(movement.transform.position))
        { ExitEditor(); return; }
        if (!Application.isFocused && !Application.isBatchMode) { CancelGesture(); return; }
        if (GameInput.CancelPressed)
        {
            if (gesture != EditGesture.None) CancelGesture();
            else if (selectedFurniture != null || selectedWall != null) Select(null, null);
            else if (wallTool) SetWallTool(false);
            else ExitEditor();
            return;
        }
        if (UIEventSystem.IsEditingText()) return;
        if (gesture == EditGesture.None)
        {
            if (GameInput.WallToolPressed) SetWallTool(true);
            if (GameInput.SelectToolPressed) SetWallTool(false);
            if (GameInput.GridPressed) ToggleGrid();
            if (GameInput.EditorTopPressed) cameraPitch = cameraPitch > 89f ? 65f : 90f;
            if (GameInput.UndoPressed) UndoEdit();
            if (GameInput.RedoPressed) RedoEdit();
            if (GameInput.DeletePressed) DeleteSelected();
            if (GameInput.RotateLeftPressed || GameInput.RotateRightPressed) RotateSelected((GameInput.RotateRightPressed ? 1f : -1f) * (GameInput.AltHeld ? 15f : 90f));
        }
        bool overUI = PointerOverEditorUI();
        if (!overUI && gesture == EditGesture.None)
        {
            cameraSize = Mathf.Clamp(cameraSize * Mathf.Exp(-GameInput.Scroll * .12f), 1.2f, 40f);
            if (GameInput.OrbitHeld) gesture = EditGesture.Orbit;
            else if (GameInput.AttackPressed)
            {
                if (wallTool)
                {
                    if (PointOnFloor(out var point))
                    {
                        pointerStart = SnapPoint(point); gesture = EditGesture.Wall;
                        wallCandidate = new PlacedWallSave { id = Guid.NewGuid().ToString("N"), property = editZone.propertyId,
                            scene = editZone.gameObject.scene.path, start = pointerStart, height = Mathf.Min(2.6f, editZone.size.y - .04f) };
                        preview = GameObject.CreatePrimitive(PrimitiveType.Cube); preview.name = "Wall preview";
                        preview.GetComponent<Collider>().enabled = false; Destroy(preview.GetComponent<Collider>());
                        preview.GetComponent<Renderer>().sharedMaterial = blue;
                    }
                }
                else
                {
                    Pick(out var furniture, out var wall);
                    Select(furniture, wall);
                    if (furniture != null) BeginMove();
                    else if (wall == null) gesture = EditGesture.Pan;
                }
            }
        }
        if (gesture == EditGesture.Pan)
        {
            if (!GameInput.PointerHeld) gesture = EditGesture.None;
            else
            {
                var delta = GameInput.PointerDelta * (cameraSize * 2f / Mathf.Max(1, Screen.height));
                var basis = Quaternion.Euler(0, cameraYaw, 0);
                cameraFocus -= basis * new Vector3(delta.x, 0, delta.y);
                var local = editZone.transform.InverseTransformPoint(cameraFocus);
                local.x = Mathf.Clamp(local.x, -editZone.size.x / 2, editZone.size.x / 2);
                local.z = Mathf.Clamp(local.z, -editZone.size.z / 2, editZone.size.z / 2); local.y = 0;
                cameraFocus = editZone.transform.TransformPoint(local);
            }
        }
        else if (gesture == EditGesture.Orbit)
        {
            if (!GameInput.OrbitHeld) gesture = EditGesture.None;
            else { cameraYaw += GameInput.PointerDelta.x * .25f; cameraPitch = Mathf.Clamp(cameraPitch - GameInput.PointerDelta.y * .2f, 35f, 90f); }
        }
        else if (gesture == EditGesture.Move) UpdateMove(overUI);
        else if (gesture == EditGesture.Wall) UpdateWall(overUI);
        UpdateEditCamera();
        UpdateEditorChrome();
    }

    void UpdateEditCamera()
    {
        if (!editCamera) return;
        editCamera.orthographicSize = cameraSize;
        var rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0);
        editCamera.transform.SetPositionAndRotation(cameraFocus - rotation * Vector3.forward * (cameraSize * 2f + 8f), rotation);
    }
    bool PointOnFloor(out Vector3 local)
    {
        var plane = new Plane(Vector3.up, editZone.transform.position);
        var ray = editCamera.ScreenPointToRay(GameInput.PointerPosition); local = default;
        if (!plane.Raycast(ray, out float distance)) return false;
        local = editZone.transform.InverseTransformPoint(ray.GetPoint(distance)); local.y = 0; return true;
    }
    Vector3 SnapPoint(Vector3 p)
    {
        if (gridEnabled && !GameInput.AltHeld) { p.x = Mathf.Round(p.x * 10f) / 10f; p.z = Mathf.Round(p.z * 10f) / 10f; }
        return p;
    }
    void Pick(out string furniture, out string wall)
    {
        furniture = wall = null;
        var hits = Physics.RaycastAll(editCamera.ScreenPointToRay(GameInput.PointerPosition), 300f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.gameObject.scene != editZone.gameObject.scene || hit.transform.IsChildOf(movement.transform) || IsCeiling(hit.collider.bounds)) continue;
            foreach (var entry in placed)
                if (entry.property == editZone.propertyId && entry.scene == editZone.gameObject.scene.path &&
                    instances.TryGetValue(entry.id, out var model) && model && hit.transform.IsChildOf(model.transform))
                { furniture = entry.id; return; }
            var id = InteriorWallLayout.Find(hit.transform);
            var record = InteriorWallLayout.Get(id);
            if (record != null && record.property == editZone.propertyId) wall = id;
            return; // Occluded objects must not be picked through solid walls.
        }
    }
    void Select(string furniture, string wall)
    {
        selectedFurniture = furniture; selectedWall = wall;
        if (outline) Destroy(outline); outline = null;
        GameObject model = furniture != null && instances.TryGetValue(furniture, out var obj) ? obj : InteriorWallLayout.Model(wall);
        if (model && outlineMaterial) outline = VisualCopy(model, outlineMaterial, "Editor selection outline");
        editMessage = furniture != null ? "드래그해서 옮기세요." : wall != null ? "선택한 벽" : wallTool ? "바닥을 드래그해서 벽을 그리세요." : "가구를 클릭하세요.";
    }
    static bool Occupied(GameObject model)
    {
        foreach (var seat in model.GetComponentsInChildren<Seat>()) if (seat.Occupant) return true;
        return false;
    }
    void BeginMove()
    {
        if (!instances.TryGetValue(selectedFurniture, out var model) || !model || Occupied(model) || !PointOnFloor(out var p))
        { editMessage = "사용 중인 가구는 이동할 수 없습니다."; return; }
        var record = placed.Find(e => e.id == selectedFurniture);
        moveBefore = record.position; moveYawBefore = record.yaw; yaw = model.transform.eulerAngles.y;
        movePointerDown = GameInput.PointerPosition; moveDragged = false;
        moveOffset = editZone.transform.InverseTransformPoint(model.transform.position) - p; moveOffset.y = 0;
        bounds = ModelBounds(model); preview = VisualCopy(model, blue, "Furniture move preview");
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        { movingRenderers.Add(new(renderer, renderer.forceRenderingOff)); renderer.forceRenderingOff = true; }
        candidateValid = true; gesture = EditGesture.Move;
    }
    void UpdateMove(bool overUI)
    {
        if (!instances.TryGetValue(selectedFurniture, out var model) || !model) { CancelGesture(); return; }
        if (!moveDragged)
        {
            moveDragged = (GameInput.PointerPosition - movePointerDown).sqrMagnitude >= 25f;
            if (!moveDragged)
            {
                if (!GameInput.PointerHeld) { CancelGesture(); Select(selectedFurniture, null); }
                return;
            }
        }
        candidateValid = false;
        if (PointOnFloor(out var local))
        {
            local = SnapPoint(local + moveOffset); local.y = .015f - bounds.min.y;
            var position = editZone.transform.TransformPoint(local); var rotation = Quaternion.Euler(0, yaw, 0);
            preview.transform.SetPositionAndRotation(position, rotation);
            candidateValid = !overUI && FitsVolume(editZone, bounds, position, rotation, c => c.transform.IsChildOf(model.transform));
            if (outline) outline.transform.SetPositionAndRotation(position, rotation);
        }
        TintPreview(candidateValid);
        editMessage = candidateValid ? "놓으면 이동 확정 · Esc 취소" : "설치 불가: 겹침(캐릭터 포함)·바닥·구역 경계를 확인하세요.";
        if (!GameInput.PointerHeld)
        {
            if (candidateValid)
            {
                var committedLocal = editZone.transform.InverseTransformPoint(preview.transform.position);
                var localYaw = Mathf.DeltaAngle(editZone.transform.eulerAngles.y, yaw);
                if (TryMovePlacedFurniture(selectedFurniture, editZone, committedLocal, localYaw) &&
                    ((committedLocal - moveBefore).sqrMagnitude > .000001f || Mathf.Abs(Mathf.DeltaAngle(localYaw, moveYawBefore)) > .01f))
                    Push(new EditChange { furniture = selectedFurniture, before = moveBefore, after = committedLocal, beforeYaw = moveYawBefore, afterYaw = localYaw });
            }
            CancelGesture(); Select(selectedFurniture, null);
        }
    }
    public static bool TryMovePlacedFurniture(string id, PropertyZone zone, Vector3 localPosition, float localYaw)
    {
        if (!CanEdit(zone) || !float.IsFinite(localYaw) || !float.IsFinite(localPosition.x) || !float.IsFinite(localPosition.y) || !float.IsFinite(localPosition.z)) return false;
        var record = placed.Find(e => e.id == id);
        if (record == null || record.property != zone.propertyId || record.scene != zone.gameObject.scene.path ||
            !instances.TryGetValue(id, out var model) || !model || Occupied(model)) return false;
        var position = zone.transform.TransformPoint(localPosition); var rotation = Quaternion.Euler(0, zone.transform.eulerAngles.y + localYaw, 0);
        if (!FitsVolume(zone, ModelBounds(model), position, rotation, c => c.transform.IsChildOf(model.transform))) return false;
        model.transform.SetPositionAndRotation(position, rotation); record.position = localPosition; record.yaw = localYaw;
        WorldSaveRunner.RequestSave(); return true;
    }
    void RotateSelected(float amount)
    {
        var record = placed.Find(e => e.id == selectedFurniture);
        if (record == null) { editMessage = "회전할 가구를 먼저 클릭하세요."; return; }
        var old = record.yaw; var position = record.position; var target = old + amount;
        bool ok = TryMovePlacedFurniture(record.id, editZone, position, target), nudged = false;
        // A long piece against a wall has no room to turn in place: slide it to the nearest spot where it fits.
        for (int ring = 1; ring <= 12 && !ok; ring++)
            for (int i = 0; i < 8 && !ok; i++)
            {
                float angle = i * Mathf.PI / 4f;
                var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * .1f);
                ok = nudged = TryMovePlacedFurniture(record.id, editZone, position + offset, target);
            }
        if (!ok) { editMessage = Occupied(instances[record.id]) ? "사용 중인 가구는 회전할 수 없습니다." : "공간이 부족해 회전할 수 없습니다."; return; }
        Push(new EditChange { furniture = record.id, before = position, after = record.position, beforeYaw = old, afterYaw = record.yaw });
        Select(record.id, null);
        if (nudged) editMessage = "회전할 공간을 만들려고 위치를 조금 옮겼습니다.";
    }
    void UpdateWall(bool overUI)
    {
        candidateValid = false;
        if (PointOnFloor(out var p))
        {
            p = SnapPoint(p);
            if (Mathf.Abs(p.x - pointerStart.x) >= Mathf.Abs(p.z - pointerStart.z)) p.z = pointerStart.z; else p.x = pointerStart.x;
            wallCandidate.end = p;
            if (InteriorWallLayout.Shape(wallCandidate, editZone, out var position, out var rotation, out var size))
            {
                preview.SetActive(true); preview.transform.SetPositionAndRotation(position, rotation); preview.transform.localScale = size;
                candidateValid = !overUI && InteriorWallLayout.CanPlace(wallCandidate, editZone);
            }
            else preview.SetActive(false);
        }
        TintPreview(candidateValid);
        editMessage = $"길이 {Vector3.Distance(wallCandidate.start, wallCandidate.end):F1}m · 높이 {wallCandidate.height:F1}m · {(candidateValid ? "놓으면 생성" : "배치 불가")}";
        if (!GameInput.PointerHeld)
        {
            if (candidateValid && InteriorWallLayout.TryPut(wallCandidate, editZone))
                Push(new EditChange { wallAfter = InteriorWallLayout.Copy(wallCandidate) });
            CancelGesture();
        }
    }
    void TintPreview(bool valid) => blue.SetColor("_BaseColor", valid ? new Color(.12f, .48f, 1, .43f) : new Color(1, .15f, .12f, .48f));
    void CancelGesture()
    {
        if (preview) { preview.SetActive(false); Destroy(preview); } preview = null;
        foreach (var pair in movingRenderers) if (pair.Key) pair.Key.forceRenderingOff = pair.Value;
        movingRenderers.Clear(); wallCandidate = null; gesture = EditGesture.None;
        if (blue) TintPreview(true);
        if (selectedFurniture != null && instances.TryGetValue(selectedFurniture, out var model) && model && outline)
            outline.transform.SetPositionAndRotation(model.transform.position, model.transform.rotation);
    }
    void Push(EditChange change)
    {
        undo.Add(change); if (undo.Count > 50) undo.RemoveAt(0); redo.Clear();
    }
    public void UndoEdit() => History(undo, redo, false);
    public void RedoEdit() => History(redo, undo, true);
    void History(List<EditChange> source, List<EditChange> destination, bool forward)
    {
        if (!editing || gesture != EditGesture.None || source.Count == 0) return;
        var change = source[source.Count - 1]; bool ok;
        if (change.furniture != null) ok = TryMovePlacedFurniture(change.furniture, editZone, forward ? change.after : change.before, forward ? change.afterYaw : change.beforeYaw);
        else
        {
            var target = forward ? change.wallAfter : change.wallBefore;
            ok = target != null ? InteriorWallLayout.TryPut(target, editZone) : InteriorWallLayout.TryRemove((change.wallAfter ?? change.wallBefore).id, editZone);
        }
        if (!ok) { editMessage = "되돌릴 위치가 막혀 있거나 편집 권한이 없습니다."; return; }
        source.RemoveAt(source.Count - 1); destination.Add(change); Select(null, null);
        editMessage = forward ? "다시 실행했습니다." : "직전 작업을 되돌렸습니다.";
    }
    /// <summary>Returns placed furniture to the bag. Nothing changes unless every check passes; storage must be emptied first.</summary>
    public static bool TryRetrievePlacedFurniture(string id, PropertyZone zone, InventoryState bag, out string error)
    {
        error = "회수할 수 없는 가구입니다.";
        if (!CanEdit(zone) || bag == null) return false;
        var record = placed.Find(e => e.id == id);
        if (record == null || record.property != zone.propertyId || record.scene != zone.gameObject.scene.path ||
            !instances.TryGetValue(id, out var model) || !model) return false;
        if (Occupied(model)) { error = "사용 중인 가구는 회수할 수 없습니다."; return false; }
        foreach (var storage in model.GetComponentsInChildren<FridgeStorage>(true))
        {
            var contents = storage.Contents;
            for (int i = 0; contents.GetSlot(i) != null; i++)
                if (!contents.GetSlot(i).IsEmpty) { error = "안에 든 물건을 먼저 꺼내 주세요."; return false; }
        }
        var definition = InventorySnapshot.Find(record.item);
        if (!definition || !bag.TryAdd(definition, 1, out error)) return false;
        placed.Remove(record); instances.Remove(id);
        model.SetActive(false);
        if (Application.isPlaying) Destroy(model); else DestroyImmediate(model);
        // Bag and world save separately: write both now so a crash cannot duplicate or lose the item.
        WorldSaveRunner.SaveNow();
        return true;
    }
    void DeleteSelected()
    {
        if (!editing || gesture != EditGesture.None) return;
        if (selectedFurniture != null)
        {
            var id = selectedFurniture;
            if (!TryRetrievePlacedFurniture(id, editZone, owner.Inventory, out var error)) { editMessage = error; return; }
            // Retrieval is final: older history entries would point at a model that no longer exists and block undo.
            undo.RemoveAll(c => c.furniture == id); redo.RemoveAll(c => c.furniture == id);
            Select(null, null); editMessage = "가구를 인벤토리로 회수했습니다.";
            return;
        }
        var wall = InteriorWallLayout.Get(selectedWall);
        if (wall != null && InteriorWallLayout.TryRemove(wall.id, editZone))
        { Push(new EditChange { wallBefore = wall }); Select(null, null); editMessage = "벽을 철거했습니다. Ctrl+Z로 복구할 수 있습니다."; }
    }
    void SetWallTool(bool value) { CancelGesture(); wallTool = value; Select(null, null); }
    void ToggleGrid() { gridEnabled = !gridEnabled; editMessage = gridEnabled ? "격자에 맞춰 놓습니다." : "격자 없이 자유롭게 놓습니다."; }
    public void ExitEditor()
    {
        if (!editing) return;
        CancelGesture(); Select(null, null);
        foreach (var pair in cutaway) if (pair.Key) pair.Key.forceRenderingOff = pair.Value;
        cutaway.Clear();
        foreach (var canvas in hiddenCanvases) if (canvas) canvas.enabled = true;
        hiddenCanvases.Clear();
        if (editCamera)
        {
            editCamera.transform.SetPositionAndRotation(savedCameraPosition, savedCameraRotation);
            editCamera.orthographic = savedOrthographic; editCamera.orthographicSize = savedOrthoSize; editCamera.fieldOfView = savedFov;
        }
        if (editCanvas) { editCanvas.gameObject.SetActive(false); Destroy(editCanvas.gameObject); }
        if (gridRoot) Destroy(gridRoot);
        if (gridMaterial) Destroy(gridMaterial); if (outlineMaterial) Destroy(outlineMaterial); if (blue) Destroy(blue);
        editCanvas = null; gridRoot = null; blue = null; outlineMaterial = null; gridMaterial = null;
        editing = false; if (active == this) active = null; lastInputFrame = Time.frameCount;
        editControls.Release(movement, !SceneLoadManager.IsLoading);
        WorldSaveRunner.RequestSave(); undo.Clear(); redo.Clear(); editZone = null;
    }
    bool IsCeiling(Bounds b)
    {
        var p = editZone.transform.InverseTransformPoint(b.center);
        return b.min.y >= editZone.transform.position.y + editZone.size.y - .3f && b.size.y < .65f &&
            Mathf.Abs(p.x) <= editZone.size.x / 2 + b.extents.x && Mathf.Abs(p.z) <= editZone.size.z / 2 + b.extents.z;
    }
    void HideCeiling()
    {
        foreach (var renderer in FindObjectsByType<Renderer>())
            if (renderer.gameObject.scene == editZone.gameObject.scene && IsCeiling(renderer.bounds))
            { cutaway.Add(new(renderer, renderer.forceRenderingOff)); renderer.forceRenderingOff = true; }
    }
    void BuildGrid()
    {
        gridRoot = new GameObject("Interior edit grid"); gridRoot.transform.SetPositionAndRotation(editZone.transform.position, editZone.transform.rotation);
        gridMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")); gridMaterial.SetColor("_BaseColor", new Color(.28f,.55f,.65f));
        // Ten-centimetre snapping with a half-metre guide keeps large rooms readable.
        for (float x = Mathf.Ceil(-editZone.size.x); x <= editZone.size.x; x++) AddGridLine(new Vector3(x*.5f,.02f,-editZone.size.z*.5f), new Vector3(x*.5f,.02f,editZone.size.z*.5f));
        for (float z = Mathf.Ceil(-editZone.size.z); z <= editZone.size.z; z++) AddGridLine(new Vector3(-editZone.size.x*.5f,.02f,z*.5f), new Vector3(editZone.size.x*.5f,.02f,z*.5f));
        gridRoot.SetActive(false);
    }
    void AddGridLine(Vector3 a, Vector3 b)
    {
        var go = new GameObject("Guide"); go.transform.SetParent(gridRoot.transform, false); var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false; line.positionCount = 2; line.SetPosition(0,a); line.SetPosition(1,b);
        line.startWidth = line.endWidth = .006f; line.sharedMaterial = gridMaterial; line.shadowCastingMode = ShadowCastingMode.Off;
    }
    bool PointerOverEditorUI()
    {
        if (!EventSystem.current) return false;
        editUIHits.Clear(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = GameInput.PointerPosition }, editUIHits);
        foreach (var hit in editUIHits) if (hit.module is UnityEngine.UI.GraphicRaycaster) return true;
        return false;
    }
    // Stats, clock and hotbar only get in the way here, and their canvases would also swallow clicks on the room.
    void HideGameHud()
    {
        foreach (var canvas in FindObjectsByType<Canvas>())
            if (canvas != editCanvas && canvas.isRootCanvas && canvas.enabled && canvas.renderMode != RenderMode.WorldSpace)
            { canvas.enabled = false; hiddenCanvases.Add(canvas); }
    }
    static void PaintTool(UnityEngine.UI.Button button, bool on)
    {
        var colors = button.colors;
        colors.normalColor = on ? UITheme.Selected : UITheme.Sand;
        colors.highlightedColor = on ? Color.Lerp(UITheme.Selected, Color.white, .15f) : UITheme.Ivory;
        button.colors = colors;
        button.GetComponentInChildren<TMP_Text>().color = on ? Color.white : UITheme.Ink;
    }
    // Only what the current state can use is on screen.
    void UpdateEditorChrome()
    {
        if (!editStatus) return;
        editStatus.text = editMessage;
        if (gridRoot) gridRoot.SetActive(gridEnabled && (gesture == EditGesture.Move || gesture == EditGesture.Wall));
        PaintTool(selectButton, !wallTool); PaintTool(wallButton, wallTool);
        bool furniture = selectedFurniture != null, any = furniture || selectedWall != null;
        rotateButton.gameObject.SetActive(furniture);
        removeButton.gameObject.SetActive(any);
        if (any) removeLabel.text = furniture ? "회수 Del" : "철거 Del";
        editStatus.rectTransform.anchoredPosition = new Vector2(any ? -135f : 0f, 0f);
    }
    void BuildEditUI()
    {
        editCanvas = TitleUi.NewCanvas("InteriorEditorCanvas", 3600); editCanvas.transform.SetParent(transform, false);
        editCanvas.GetComponent<UnityEngine.UI.CanvasScaler>().referenceResolution = new Vector2(1280,720);
        var font = owner.uiFont ? owner.uiFont : TitleUi.Font;
        var bar = UITheme.Panel("EditToolbar", editCanvas.transform, new Vector2(460,60));
        bar.anchorMin = bar.anchorMax = new Vector2(.5f,1); bar.pivot = new Vector2(.5f,1); bar.anchoredPosition = new Vector2(0,-12);
        bar.GetComponentInChildren<InventoryRoundedGraphic>().raycastTarget = true;
        UnityEngine.UI.Button Tool(RectTransform parent, string label, float x, float width, UnityEngine.Events.UnityAction action)
        {
            var button = UITheme.Button(parent, label, new Vector2(width,42), false, font, action);
            ((RectTransform)button.transform).anchoredPosition = new Vector2(x,0);
            return button;
        }
        selectButton = Tool(bar, "선택 V", -165, 102, () => SetWallTool(false));
        wallButton = Tool(bar, "벽 W", -55, 102, () => SetWallTool(true));
        Tool(bar, "되돌리기", 55, 102, UndoEdit);
        Tool(bar, "완료 B", 165, 102, ExitEditor);
        var foot = UITheme.Panel("EditHelp", editCanvas.transform, new Vector2(760,60));
        foot.anchorMin = foot.anchorMax = new Vector2(.5f,0); foot.pivot = new Vector2(.5f,0); foot.anchoredPosition = new Vector2(0,12);
        foot.GetComponentInChildren<InventoryRoundedGraphic>().raycastTarget = true;
        editStatus = UITheme.Text(foot,"",17,UITheme.Ink,font,new Vector2(470,50));
        rotateButton = Tool(foot, "회전 Q/E", 190, 118, () => RotateSelected(90f));
        removeButton = Tool(foot, "회수 Del", 316, 118, DeleteSelected);
        removeLabel = removeButton.GetComponentInChildren<TMP_Text>();
        UpdateEditorChrome();
    }
}
