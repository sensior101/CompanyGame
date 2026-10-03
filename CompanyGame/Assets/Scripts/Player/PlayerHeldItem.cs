using System.Collections.Generic;
using CompanyGame.Daldongne;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Renders the selected quick-slot item at the avatar's right hand and in first person.</summary>
[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
public sealed class PlayerHeldItem : MonoBehaviour
{
    const int HandLayer = 29;
    PlayerMovement movement;
    PlayerCombat combat;
    PlayerCameraController cameraController;
    Camera viewCamera, handCamera;
    Transform arm, visualSource, viewRig, viewArm, worldItem, firstPersonItem;
    Vector3 handPoint;
    Quaternion armRest;
    ItemData heldItem;
    float attackStarted = -10f;
    bool attackWasShot, excludedHandLayer;
    LineRenderer tracer;
    Material tracerMaterial;
    readonly List<Material> materials = new List<Material>();
    readonly List<Mesh> bakedMeshes = new List<Mesh>();

    public ItemData HeldItem => heldItem;
    public Transform WorldItemRoot => worldItem;
    public Transform FirstPersonItemRoot => firstPersonItem;
    public Transform FirstPersonArmRoot => viewArm;
    public bool IsFirstPersonVisible => viewRig && viewRig.gameObject.activeInHierarchy;
    public Vector3 WorldGripPosition => arm ? arm.TransformPoint(handPoint) : transform.position + Vector3.up;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }
    void Start()
    {
        combat = GetComponent<PlayerCombat>();
        if (combat) combat.AttackPerformed += AnimateAttack;
        EnsureCamera();
    }
    void AnimateAttack(bool shot)
    {
        attackStarted = Time.time; attackWasShot = shot;
        if (!shot || !combat) return;
        if (!tracer)
        {
            var host = new GameObject("ShotTrail", typeof(LineRenderer));
            host.transform.SetParent(transform, false);
            tracer = host.GetComponent<LineRenderer>();
            tracerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.DontSave };
            tracerMaterial.color = new Color(1f, .78f, .3f);
            tracer.sharedMaterial = tracerMaterial;
            tracer.positionCount = 2; tracer.useWorldSpace = true;
            tracer.startWidth = .014f; tracer.endWidth = .006f;
            tracer.shadowCastingMode = ShadowCastingMode.Off;
        }
        var muzzle = IsFirstPersonVisible ? firstPersonItem : worldItem;
        tracer.SetPosition(0, muzzle ? muzzle.TransformPoint(new Vector3(0, .1f, .28f)) : combat.LastAttackOrigin);
        tracer.SetPosition(1, combat.LastAttackEnd);
        tracer.enabled = true;
    }

    Transform FindRightArm()
    {
        var appearance = GetComponent<DaldongnePlayerAppearance>();
        Transform source = transform;
        if (appearance)
        {
            var visual = appearance.selected == DaldongnePlayerAppearance.Variant.Female ? appearance.female : appearance.male;
            if (visual) source = visual.transform;
        }
        if (source == visualSource && arm) return arm;
        visualSource = source;
        foreach (var child in source.GetComponentsInChildren<Transform>(true))
            if (child.name == "RightArm") return child;
        var animator = source.GetComponentInChildren<Animator>();
        return animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
    }

    void EnsureCamera()
    {
        if (viewCamera || !movement || !movement.viewCamera) return;
        viewCamera = movement.viewCamera;
        cameraController = viewCamera.GetComponent<PlayerCameraController>();
        viewRig = new GameObject("FirstPersonRightHand").transform;
        viewRig.SetParent(viewCamera.transform, false);
        var host = new GameObject("FirstPersonHandCamera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        host.transform.SetParent(viewCamera.transform, false);
        handCamera = host.GetComponent<Camera>();
        handCamera.CopyFrom(viewCamera);
        handCamera.cullingMask = 1 << HandLayer;
        handCamera.nearClipPlane = .01f;
        handCamera.farClipPlane = 4f;
        handCamera.useOcclusionCulling = false;
        var overlay = host.GetComponent<UniversalAdditionalCameraData>();
        overlay.renderType = CameraRenderType.Overlay;
        overlay.renderPostProcessing = false;
        overlay.renderShadows = false;
        overlay.volumeLayerMask = 0;
        viewCamera.GetUniversalAdditionalCameraData().cameraStack.Add(handCamera);
        excludedHandLayer = (viewCamera.cullingMask & (1 << HandLayer)) != 0;
        viewCamera.cullingMask &= ~(1 << HandLayer);
        handCamera.enabled = false;
        viewRig.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        EnsureCamera();
        var inventory = InventoryManager.Instance ? InventoryManager.Instance.State : null;
        if (inventory == null || !viewRig) return;
        Transform currentArm = FindRightArm();
        bool newArm = arm != currentArm;
        bool changedArm = newArm || (currentArm && !viewArm);
        if (changedArm)
        {
            arm = currentArm;
            if (arm) { if (newArm) armRest = arm.localRotation; BuildArm(); }
        }
        var stack = inventory.GetSlot(inventory.SelectedHotbarIndex);
        ItemData selected = stack != null && !stack.IsEmpty ? stack.Item : null;
        if (heldItem != selected || changedArm)
        {
            heldItem = selected;
            RebuildItems();
        }

        bool firstPerson = cameraController && cameraController.firstPerson;
        bool showHands = firstPerson && !InputFocus.InventoryOpen() && !InputFocus.ChatOpen() &&
            !SceneLoadManager.IsLoading && movement.isActiveAndEnabled;
        viewRig.gameObject.SetActive(showHands && arm);
        handCamera.enabled = showHands && arm;
        handCamera.fieldOfView = viewCamera.fieldOfView;
        handCamera.aspect = viewCamera.aspect;
        handCamera.rect = viewCamera.rect;
        float duration = attackWasShot ? .19f : .32f;
        if (tracer) tracer.enabled = attackWasShot && Time.time - attackStarted < .07f && !InputFocus.InventoryOpen();
        float phase = Mathf.Clamp01((Time.time - attackStarted) / duration);
        float swing = Mathf.Sin(phase * Mathf.PI);
        float recoil = attackWasShot ? swing : 0f;
        float punch = attackWasShot ? 0f : swing;
        if (arm && !firstPerson)
        {
            // The avatar motion writes its walking pose first; only the held/attacking arm is overlaid.
            if (heldItem || punch > .001f)
                arm.localRotation = armRest * Quaternion.Euler(-65f * (heldItem ? 1f : punch) - 22f * punch + 8f * recoil, 0f, -8f);
        }
        if (worldItem)
        {
            worldItem.gameObject.SetActive(!firstPerson);
            worldItem.position = WorldGripPosition + transform.up * .055f;
            worldItem.rotation = transform.rotation;
        }
        if (viewArm)
        {
            viewArm.localPosition = new Vector3(.25f - .06f * punch, -.23f + .025f * punch, .16f + .15f * punch - .055f * recoil);
            viewArm.localRotation = Quaternion.Euler(-76f - 8f * punch + 10f * recoil, -8f, -8f);
            if (firstPersonItem)
            {
                firstPersonItem.position = viewArm.TransformPoint(handPoint) + viewCamera.transform.up * .055f;
                firstPersonItem.rotation = viewCamera.transform.rotation * Quaternion.Euler(-8f * recoil, -10f, -8f);
            }
        }
    }

    void BuildArm()
    {
        Remove(ref viewArm);
        viewArm = CopyRenderers(arm, viewRig, HandLayer);
        viewArm.name = "CurrentAvatarRightArm";
        viewArm.localScale = arm.lossyScale;
        Transform hand = arm.Find("Hand");
        if (hand) handPoint = arm.InverseTransformPoint(hand.position);
        else
        {
            var mesh = arm.GetComponent<MeshFilter>();
            var bounds = mesh && mesh.sharedMesh ? mesh.sharedMesh.bounds : new Bounds(new Vector3(0f, -.25f, 0f), new Vector3(.18f, .5f, .18f));
            handPoint = new Vector3(bounds.center.x, bounds.min.y + .055f, bounds.center.z);
        }
    }

    void RebuildItems()
    {
        Remove(ref worldItem); Remove(ref firstPersonItem);
        foreach (var material in materials) if (material) Destroy(material);
        materials.Clear();
        if (!heldItem || !arm) return;
        worldItem = new GameObject("RightHandHeldItem").transform;
        worldItem.SetParent(transform, false);
        firstPersonItem = new GameObject("FirstPersonHeldItem").transform;
        firstPersonItem.SetParent(viewRig, false);
        BuildItem(worldItem, 0);
        BuildItem(firstPersonItem, HandLayer);
    }

    void BuildItem(Transform parent, int layer)
    {
        Transform model;
        if (heldItem.heldPrefab)
            model = CopyRenderers(heldItem.heldPrefab.transform, parent, layer);
        else
        {
            model = new GameObject("HeldModel").transform;
            model.SetParent(parent, false);
            if (heldItem.IsWeapon && heldItem.weaponKind == WeaponKind.Firearm) BuildPistol(model, layer);
            else if (heldItem.icon) BuildIcon(model, layer);
            else Part(model, "Item", PrimitiveType.Cube, new Vector3(0f, .05f, 0f), new Vector3(.13f, .15f, .1f),
                Material(new Color(.64f, .72f, .67f)), layer);
        }
        model.localPosition = heldItem.heldLocalPosition;
        model.localRotation = Quaternion.Euler(heldItem.heldLocalEulerAngles);
        model.localScale = heldItem.heldLocalScale;
    }

    void BuildIcon(Transform parent, int layer)
    {
        bool coin = heldItem.IsCurrency && heldItem.CurrencyValue < 10000;
        // Silver coins are intentionally smaller than gold coins in the held 3D model.
        float width = coin
            ? (heldItem.CurrencyValue < 1000 ? .14f : .19f)
            : .30f;
        var sprite = heldItem.icon;
        float height = width * sprite.bounds.size.y / Mathf.Max(.001f, sprite.bounds.size.x);
        var edge = Material(coin ? (heldItem.CurrencyValue < 1000 ? new Color(.62f, .66f, .7f) : new Color(.85f, .56f, .12f)) : new Color(.92f, .9f, .79f));
        if (coin)
        {
            var rim = Part(parent, "CoinRim", PrimitiveType.Cylinder, new Vector3(0, .07f, 0), new Vector3(width, .005f, width), edge, layer);
            rim.localRotation = Quaternion.Euler(90f, 0, 0);
        }
        else Part(parent, "PaperEdge", PrimitiveType.Cube, new Vector3(0, .07f, 0), new Vector3(width, height, .004f), edge, layer);
        for (int side = 0; side < 2; side++)
        {
            var host = new GameObject(side == 0 ? "ItemFront" : "ItemBack", typeof(SpriteRenderer));
            host.layer = layer;
            host.transform.SetParent(parent, false);
            host.transform.localPosition = new Vector3(0, .07f, side == 0 ? -.006f : .006f);
            host.transform.localRotation = Quaternion.Euler(0, side == 0 ? 0 : 180, 0);
            host.transform.localScale = Vector3.one * (width / Mathf.Max(.001f, sprite.bounds.size.x));
            var renderer = host.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    void BuildPistol(Transform parent, int layer)
    {
        var metal = Material(new Color(.18f, .2f, .22f), .65f);
        var grip = Material(new Color(.08f, .09f, .095f));
        var sight = Material(new Color(.76f, .8f, .77f), .3f);
        var handle = Part(parent, "Grip", PrimitiveType.Cube, new Vector3(0, .015f, .015f), new Vector3(.067f, .13f, .085f), grip, layer);
        handle.localRotation = Quaternion.Euler(-14f, 0, 0);
        Part(parent, "Slide", PrimitiveType.Cube, new Vector3(0, .10f, .105f), new Vector3(.074f, .074f, .27f), metal, layer);
        var barrel = Part(parent, "Barrel", PrimitiveType.Cylinder, new Vector3(0, .09f, .251f), new Vector3(.04f, .019f, .04f), grip, layer);
        barrel.localRotation = Quaternion.Euler(90f, 0, 0);
        Part(parent, "FrontSight", PrimitiveType.Cube, new Vector3(0, .144f, .211f), new Vector3(.013f, .015f, .022f), sight, layer);
        Part(parent, "RearSight", PrimitiveType.Cube, new Vector3(0, .144f, .003f), new Vector3(.041f, .015f, .018f), grip, layer);
        Part(parent, "TriggerGuard", PrimitiveType.Cube, new Vector3(0, -.008f, .095f), new Vector3(.018f, .014f, .075f), metal, layer);
        Part(parent, "GuardFront", PrimitiveType.Cube, new Vector3(0, .025f, .129f), new Vector3(.018f, .064f, .014f), metal, layer);
        Part(parent, "Trigger", PrimitiveType.Cube, new Vector3(0, .043f, .088f), new Vector3(.014f, .035f, .018f), grip, layer);
    }

    Material Material(Color color, float metallic = 0f)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HeldItemMaterial", hideFlags = HideFlags.DontSave };
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", metallic > 0f ? .5f : .2f);
        materials.Add(material);
        return material;
    }
    static Transform Part(Transform parent, string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material, int layer)
    {
        var host = GameObject.CreatePrimitive(primitive);
        host.name = name; host.layer = layer;
        host.transform.SetParent(parent, false);
        host.transform.localPosition = position; host.transform.localScale = scale;
        var collider = host.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        var renderer = host.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return host.transform;
    }
    Transform CopyRenderers(Transform source, Transform parent, int layer)
    {
        var copy = new GameObject(source.name).transform;
        copy.gameObject.layer = layer; copy.SetParent(parent, false);
        if (source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh && source.TryGetComponent<MeshRenderer>(out var renderer))
        {
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var cloned = copy.gameObject.AddComponent<MeshRenderer>();
            cloned.sharedMaterials = renderer.sharedMaterials; cloned.shadowCastingMode = ShadowCastingMode.Off;
        }
        else if (source.TryGetComponent<SkinnedMeshRenderer>(out var skin) && skin.sharedMesh)
        {
            var mesh = new Mesh { name = "HeldItemBakedMesh", hideFlags = HideFlags.DontSave };
            skin.BakeMesh(mesh); bakedMeshes.Add(mesh);
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var cloned = copy.gameObject.AddComponent<MeshRenderer>();
            cloned.sharedMaterials = skin.sharedMaterials; cloned.shadowCastingMode = ShadowCastingMode.Off;
        }
        foreach (Transform child in source)
        {
            var cloned = CopyRenderers(child, copy, layer);
            cloned.localPosition = child.localPosition;
            cloned.localRotation = child.localRotation;
            cloned.localScale = child.localScale;
            cloned.gameObject.SetActive(child.gameObject.activeSelf);
        }
        return copy;
    }
    static void Remove(ref Transform value)
    {
        if (value) { value.gameObject.SetActive(false); Destroy(value.gameObject); }
        value = null;
    }
    void OnDisable()
    {
        if (tracer) tracer.enabled = false;
        if (viewRig) viewRig.gameObject.SetActive(false);
        if (handCamera) handCamera.enabled = false;
        if (worldItem) worldItem.gameObject.SetActive(false);
    }
    void OnDestroy()
    {
        if (combat) combat.AttackPerformed -= AnimateAttack;
        if (viewCamera)
        {
            viewCamera.GetUniversalAdditionalCameraData().cameraStack.Remove(handCamera);
            if (excludedHandLayer) viewCamera.cullingMask |= 1 << HandLayer;
        }
        if (handCamera) Destroy(handCamera.gameObject);
        if (tracerMaterial) Destroy(tracerMaterial);
        Remove(ref viewRig); Remove(ref worldItem);
        foreach (var material in materials) if (material) Destroy(material);
        foreach (var mesh in bakedMeshes) if (mesh) Destroy(mesh);
    }
}
