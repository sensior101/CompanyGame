using System.Collections.Generic;
using CompanyGame.Daldongne;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class InventoryCharacterPreview : MonoBehaviour
{
    const int PreviewLayer = 30;
    readonly Dictionary<Transform, Transform> transforms = new Dictionary<Transform, Transform>();
    readonly List<RendererPair> renderers = new List<RendererPair>();
    PlayerMovement player;
    UnityEngine.UI.RawImage image;
    GameObject stage;
    Transform portrait;
    Transform visualSource;
    Camera portraitCamera;
    RenderTexture texture;
    bool visible;
    float nextHierarchyCheck;
    int rendererCount;
    float previewYaw = -16f;
    Quaternion sourceFacing = Quaternion.identity;
    Transform previewHead;
    Transform previewHeadSource;
    float currentLookX;
    float currentLookY;
    Transform previewBody;
    Transform previewBodySource;

    sealed class RendererPair
    {
        public Renderer source;
        public Renderer copy;
    }

    public bool IsReady => texture && portraitCamera && portrait && renderers.Count > 0;
    public Camera PreviewCamera => portraitCamera;
    public RenderTexture Texture => texture;
    public float PreviewYaw => previewYaw;
    public Quaternion PreviewRotation => portrait ? portrait.localRotation : Quaternion.Euler(0f, previewYaw, 0f) * sourceFacing;
    public RectTransform PortraitRect => image ? image.rectTransform : null;

    public void Initialize(PlayerMovement owner, UnityEngine.UI.RawImage target)
    {
        player = owner;
        image = target;
        image.raycastTarget = false;
    }

    public void RotatePreview(float horizontalPixels)
    {
        // Kept as a no-op compatibility method for older callers.
    }

    public void SetVisible(bool value)
    {
        visible = value;
        if (value)
        {
            EnsureStage();
            if (stage) stage.SetActive(true);
            Synchronize(true);
        }
        if (stage) stage.SetActive(value);
        if (portraitCamera) portraitCamera.enabled = value && portrait;
    }

    void LateUpdate()
    {
        if (!visible)
            return;

        Synchronize(false);

        UpdatePreviewHeadLook();
    }

    void UpdatePreviewHeadLook()
    {
        if (!image || !portrait || !visualSource)
            return;

        EnsurePreviewBones();

        if (!previewHead)
            return;

        Vector2 mousePosition;

        if (!GameInput.HasPointer)
            return;
        
        mousePosition = GameInput.PointerPosition;

        RectTransform rect = image.rectTransform;

        Camera uiCamera = null;

        if (image.canvas &&
            image.canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            uiCamera = image.canvas.worldCamera;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect,
                mousePosition,
                uiCamera,
                out Vector2 localPoint))
        {
            return;
        }

        Rect area = rect.rect;

        float halfWidth = Mathf.Max(1f, area.width * 0.5f);
        float halfHeight = Mathf.Max(1f, area.height * 0.5f);

        float targetX = Mathf.Clamp(localPoint.x / halfWidth, -1f, 1f);
        float targetY = Mathf.Clamp(localPoint.y / halfHeight, -1f, 1f);

        currentLookX = Mathf.Lerp(
            currentLookX,
            targetX,
            Time.unscaledDeltaTime * 12f
        );

        currentLookY = Mathf.Lerp(
            currentLookY,
            targetY,
            Time.unscaledDeltaTime * 12f
        );
        Quaternion headBaseRotation = previewHead.localRotation;

        float headYaw = -currentLookX * 40f;
        float headPitch = -currentLookY * 20f;

        previewHead.localRotation =
            headBaseRotation * Quaternion.Euler(headPitch, headYaw, 0f);

        if (previewBody)
        {
            Quaternion bodyBaseRotation = previewBody.localRotation;

            float bodyYaw = -currentLookX * 10f;
            float bodyPitch = -currentLookY * 3f;

            previewBody.localRotation =
                bodyBaseRotation * Quaternion.Euler(bodyPitch, bodyYaw, 0f);
        }
    }

    void EnsurePreviewBones()
    {
        if (previewHead && previewHeadSource && previewBody && previewBodySource)
            return;

        previewHead = null;
        previewHeadSource = null;
        previewBody = null;
        previewBodySource = null;

        if (!visualSource)
            return;

        Transform[] sourceTransforms =
            visualSource.GetComponentsInChildren<Transform>(true);

        Transform sourceHead = null;
        Transform sourceBody = null;

        foreach (Transform child in sourceTransforms)
        {
            string lowerName = child.name.ToLowerInvariant();

            if (sourceHead == null &&
                (lowerName == "head" ||
                 lowerName.EndsWith(":head") ||
                 lowerName.EndsWith("_head")))
            {
                sourceHead = child;
            }

            if (sourceBody == null &&
    (lowerName == "hips" ||
     lowerName.EndsWith(":hips") ||
     lowerName.EndsWith("_hips")))
            {
                sourceBody = child;
            }
        }

        if (sourceHead &&
            transforms.TryGetValue(sourceHead, out Transform copiedHead) &&
            copiedHead)
        {
            previewHeadSource = sourceHead;
            previewHead = copiedHead;
        }

        if (sourceBody &&
            transforms.TryGetValue(sourceBody, out Transform copiedBody) &&
            copiedBody)
        {
            previewBodySource = sourceBody;
            previewBody = copiedBody;
        }
    }

    void EnsureStage()
    {
        if (stage || !player) return;
        // Outside every gameplay camera's far clip. The portrait camera also has a
        // narrow culling mask and ten-metre far plane, so it cannot see map scenery.
        stage = new GameObject("InventoryPortraitStage");
        stage.transform.position = new Vector3(30000f, -30000f, 30000f);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(stage, gameObject.scene);
        stage.layer = PreviewLayer;
        var cameraHost = new GameObject("InventoryPortraitCamera", typeof(Camera));
        cameraHost.transform.SetParent(stage.transform, false);
        cameraHost.layer = PreviewLayer;
        portraitCamera = cameraHost.GetComponent<Camera>();
        portraitCamera.tag = "Untagged";
        portraitCamera.clearFlags = CameraClearFlags.SolidColor;
        portraitCamera.backgroundColor = new Color(.075f, .085f, .095f, 0f);
        portraitCamera.orthographic = true;
        portraitCamera.nearClipPlane = .05f;
        portraitCamera.farClipPlane = 10f;
        portraitCamera.cullingMask = 1 << PreviewLayer;
        portraitCamera.allowHDR = false;
        portraitCamera.allowMSAA = true;
        portraitCamera.useOcclusionCulling = false;
        portraitCamera.depth = -20f;
        var additional = cameraHost.AddComponent<UniversalAdditionalCameraData>();
        additional.renderPostProcessing = false;
        additional.renderShadows = false;
        additional.volumeLayerMask = 0;
        additional.requiresColorOption = CameraOverrideOption.Off;
        additional.requiresDepthOption = CameraOverrideOption.Off;
        texture = new RenderTexture(384, 432, 24, RenderTextureFormat.ARGB32)
        {
            name = "InventoryCharacterPortrait",
            antiAliasing = 2,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };
        texture.Create();
        portraitCamera.targetTexture = texture;
        portraitCamera.aspect = (float)texture.width / texture.height;
        image.texture = texture;
        AddLight("PortraitKey", new Vector3(2.5f, 3f, 3.3f), new Color(1f, .91f, .79f), 6f);
        AddLight("PortraitFill", new Vector3(-2.5f, 1.8f, 1.5f), new Color(.75f, .85f, 1f), 4f);
    }

    void AddLight(string name, Vector3 position, Color color, float intensity)
    {
        var host = new GameObject(name, typeof(Light));
        host.transform.SetParent(stage.transform, false);
        host.transform.localPosition = position;
        host.layer = PreviewLayer;
        var light = host.GetComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = 8f;
        light.cullingMask = 1 << PreviewLayer;
        light.shadows = LightShadows.None;
    }

    Transform FindVisual()
    {
        if (!player) return null;
        var appearance = player.GetComponent<DaldongnePlayerAppearance>();
        if (appearance)
        {
            var chosen = appearance.selected == DaldongnePlayerAppearance.Variant.Female
                ? appearance.female : appearance.male;
            if (chosen) return chosen.transform;
        }
        var camera = player.viewCamera ? player.viewCamera.GetComponent<PlayerCameraController>() : null;
        if (camera)
        {
            if (camera.femaleVisuals && camera.femaleVisuals.activeInHierarchy) return camera.femaleVisuals.transform;
            if (camera.maleVisuals && camera.maleVisuals.activeInHierarchy) return camera.maleVisuals.transform;
        }
        return player.transform;
    }

    void Synchronize(bool force)
    {
        if (!player || !stage) return;
        var source = FindVisual();
        if (!source) return;
        bool changed = source != visualSource;
        if (force || changed || Time.unscaledTime >= nextHierarchyCheck)
        {
            nextHierarchyCheck = Time.unscaledTime + .3f;
            var sources = source.GetComponentsInChildren<Renderer>(true);
            if (force || changed || sources.Length != rendererCount) Rebuild(source, sources);
        }
        if (!portrait) return;
        foreach (var pair in transforms)
        {
            if (!pair.Key || !pair.Value || pair.Key == visualSource) continue;
            pair.Value.localPosition = pair.Key.localPosition;
            pair.Value.localRotation = pair.Key.localRotation;
            pair.Value.localScale = pair.Key.localScale;
            if (pair.Value.gameObject.activeSelf != pair.Key.gameObject.activeSelf)
                pair.Value.gameObject.SetActive(pair.Key.gameObject.activeSelf);
        }
        var playerCamera = player.viewCamera ? player.viewCamera.GetComponent<PlayerCameraController>() : null;
        bool hiddenForFirstPerson = playerCamera && playerCamera.firstPerson;
        foreach (var pair in renderers)
        {
            if (!pair.source || !pair.copy) continue;
            pair.copy.enabled = pair.source.enabled || hiddenForFirstPerson;
            var sourceMaterials = pair.source.sharedMaterials;
            var copyMaterials = pair.copy.sharedMaterials;
            bool materialsChanged = sourceMaterials.Length != copyMaterials.Length;
            for (int i = 0; !materialsChanged && i < sourceMaterials.Length; i++)
                materialsChanged = sourceMaterials[i] != copyMaterials[i];
            if (materialsChanged) pair.copy.sharedMaterials = sourceMaterials;
            if (pair.source is SkinnedMeshRenderer sourceSkin && pair.copy is SkinnedMeshRenderer copySkin && sourceSkin.sharedMesh)
            {
                if (copySkin.sharedMesh != sourceSkin.sharedMesh)
                {
                    copySkin.sharedMesh = sourceSkin.sharedMesh;
                    copySkin.localBounds = sourceSkin.localBounds;
                }
                for (int i = 0; i < sourceSkin.sharedMesh.blendShapeCount; i++)
                    copySkin.SetBlendShapeWeight(i, sourceSkin.GetBlendShapeWeight(i));
            }
            else if (pair.source.TryGetComponent<MeshFilter>(out var sourceMesh) && pair.copy.TryGetComponent<MeshFilter>(out var copyMesh))
                copyMesh.sharedMesh = sourceMesh.sharedMesh;
        }
    }

    void Rebuild(Transform source, Renderer[] sources)
    {
        if (portrait)
        {
            portrait.gameObject.SetActive(false);
            Destroy(portrait.gameObject);
        }
        transforms.Clear();
        renderers.Clear();
        visualSource = source;
        rendererCount = sources.Length;
        portrait = CopyHierarchy(source, stage.transform);
        portrait.name = "CurrentCharacterVisual";
        portrait.localPosition = Vector3.zero;
        // Face the portrait camera without changing the gameplay character's facing.
        sourceFacing = source == player.transform ? Quaternion.identity : source.localRotation;
        portrait.localRotation = Quaternion.Euler(0f, previewYaw, 0f) * sourceFacing;
        portrait.localScale = source.lossyScale;
        portrait.gameObject.SetActive(true);
        foreach (var original in sources)
        {
            if (!transforms.TryGetValue(original.transform, out var destination)) continue;
            Renderer copy;
            if (original is SkinnedMeshRenderer skinned && skinned.sharedMesh)
            {
                var skin = destination.gameObject.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = skinned.sharedMesh;
                skin.localBounds = skinned.localBounds;
                skin.updateWhenOffscreen = true;
                skin.quality = skinned.quality;
                var bones = skinned.bones;
                var copiedBones = new Transform[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] && transforms.TryGetValue(bones[i], out var bone)) copiedBones[i] = bone;
                skin.bones = copiedBones;
                if (skinned.rootBone && transforms.TryGetValue(skinned.rootBone, out var rootBone)) skin.rootBone = rootBone;
                copy = skin;
            }
            else if (original is MeshRenderer && original.TryGetComponent<MeshFilter>(out var mesh) && mesh.sharedMesh)
            {
                destination.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                copy = destination.gameObject.AddComponent<MeshRenderer>();
            }
            else continue;
            copy.sharedMaterials = original.sharedMaterials;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off;
            copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderers.Add(new RendererPair { source = original, copy = copy });
        }
        FitCamera();
        portraitCamera.enabled = visible && renderers.Count > 0;
    }

    Transform CopyHierarchy(Transform source, Transform parent)
    {
        var copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        copy.gameObject.layer = PreviewLayer;
        copy.gameObject.SetActive(source.gameObject.activeSelf);
        transforms.Add(source, copy);
        foreach (Transform child in source) CopyHierarchy(child, copy);
        return copy;
    }

    void FitCamera()
    {
        Bounds bounds = new Bounds(stage.transform.position + Vector3.up, Vector3.one);
        bool found = false;
        foreach (var pair in renderers)
        {
            if (!pair.copy.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = pair.copy.bounds; found = true; }
            else bounds.Encapsulate(pair.copy.bounds);
        }
        var localCenter = bounds.center - stage.transform.position;
        float height = Mathf.Max(.5f, bounds.size.y);
        float width = Mathf.Max(.3f, new Vector2(bounds.size.x, bounds.size.z).magnitude);
        portraitCamera.orthographicSize = Mathf.Max(height * .57f, width / portraitCamera.aspect * .57f);
        portraitCamera.transform.localPosition = localCenter + new Vector3(0f, .03f, 4.5f);
        portraitCamera.transform.LookAt(bounds.center);
    }

    void OnDisable()
    {
        if (portraitCamera) portraitCamera.enabled = false;
    }

    void OnDestroy()
    {
        if (image) image.texture = null;
        if (portraitCamera) portraitCamera.targetTexture = null;
        if (texture)
        {
            texture.Release();
            Destroy(texture);
        }
        if (stage) Destroy(stage);
    }
}
