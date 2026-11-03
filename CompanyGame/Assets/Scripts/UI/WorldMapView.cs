using System.Collections.Generic;
using CompanyGame.World.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Live, north-up map of the current region. Reads world geometry and existing ownership only.</summary>
[DefaultExecutionOrder(-500)]
public sealed class WorldMapView : MonoBehaviour
{
    public static WorldMapView Instance { get; private set; }
    public bool IsOpen => canvas && canvas.gameObject.activeSelf;
    readonly ControlLock controls = new ControlLock();
    readonly List<Marker> markers = new List<Marker>();
    readonly List<MeshRenderer> surfaces = new List<MeshRenderer>();
    Canvas canvas;
    RectTransform viewport, playerPin;
    TMP_Text heading, detail;
    Camera mapCamera;
    RenderTexture texture;
    Material surfaceMaterial;
    PlayerMovement player;
    LandPlotManager land;
    MapParcel selected;
    Bounds bounds;
    Scene scene;
    float nextRender, nextScan, fullSize;
    bool dirty;
    int closingFrame = -1;
    public int RenderRevision { get; private set; }
    public int ParcelCount => markers.Count;
    sealed class Marker { public MapParcel parcel; public RectTransform rect; public UnityEngine.UI.Button button; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Instance = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Instance) return;
        var go = new GameObject("World Map"); DontDestroyOnLoad(go); go.AddComponent<WorldMapView>();
    }
    void Awake() { Instance = this; }

    void Update()
    {
        if (IsOpen)
        {
            if (SceneLoadManager.IsLoading || SceneManager.GetActiveScene() != scene || !player)
            { Close(); return; }
            if (GameInput.MapPressed || GameInput.CancelPressed) { Close(); return; }
            if (RectTransformUtility.RectangleContainsScreenPoint(viewport, GameInput.PointerPosition))
            {
                if (Mathf.Abs(GameInput.Scroll) > .01f) Zoom(Mathf.Pow(.82f, GameInput.Scroll));
                if (GameInput.OrbitHeld)
                {
                    Vector2 delta = GameInput.PointerDelta / canvas.scaleFactor;
                    float perPixel = mapCamera.orthographicSize * 2f / viewport.rect.height;
                    mapCamera.transform.position -= new Vector3(delta.x, 0, delta.y) * perPixel;
                    ClampCamera(); dirty = true;
                }
            }
            if (Time.unscaledTime >= nextScan) { Scan(); nextScan = Time.unscaledTime + 1f; }
            if (dirty || Time.unscaledTime >= nextRender) { RenderMap(); nextRender = Time.unscaledTime + .5f; dirty = false; }
            UpdateMarkers();
        }
        else if (GameInput.MapPressed && Time.frameCount > closingFrame) Open();
    }

    public bool Open()
    {
        player = PlayerSpawner.Player;
        if (IsOpen || !player || !player.isActiveAndEnabled || SceneLoadManager.IsLoading ||
            ChatUIManager.IsChatting || UIEventSystem.IsEditingText() || DialogueManager.OwnsInput || InputFocus.GameplayBlocked()) return false;
        scene = SceneManager.GetActiveScene();
        controls.Hold(player);
        if (!canvas) Build();
        canvas.gameObject.SetActive(true);
        land = LandPlotManager.Instance;
        if (land) land.PlotChanged += OnPlotChanged;
        Scan(); Fit();
        nextScan = Time.unscaledTime + 1f; dirty = true;
        return true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        if (land) land.PlotChanged -= OnPlotChanged;
        land = null; selected = null;
        detail.text = "번지를 누르면 부지 정보를 볼 수 있습니다";
        canvas.gameObject.SetActive(false);
        controls.Release(player, !SceneLoadManager.IsLoading);
        closingFrame = Time.frameCount;
    }
    void OnPlotChanged(string id) { dirty = true; }
    void OnDisable() { Close(); }
    void OnDestroy()
    {
        Close();
        if (texture) { texture.Release(); Destroy(texture); }
        if (surfaceMaterial) Destroy(surfaceMaterial);
        if (Instance == this) Instance = null;
    }

    void Build()
    {
        canvas = TitleUi.NewCanvas("WorldMapCanvas", 3500);
        canvas.transform.SetParent(transform, false);
        canvas.GetComponent<UnityEngine.UI.CanvasScaler>().referenceResolution = new Vector2(1280, 720);
        var dim = TitleUi.Full(canvas.transform, "Dim", UITheme.A(UITheme.Ink, .58f));
        var panel = UITheme.Panel("Map", dim, new Vector2(1150, 640), UITheme.Ivory, 10);
        heading = Label(panel, "동네 지도", 24, new Vector2(-305, 284), new Vector2(340, 38));
        heading.alignment = TextAlignmentOptions.MidlineLeft;
        var close = UITheme.Button(panel, "닫기  M / ESC", new Vector2(140, 34), false, TitleUi.Font, Close);
        ((RectTransform)close.transform).anchoredPosition = new Vector2(480, 284);
        viewport = UIBuild.Rect("MapViewport", panel, new Vector2(1102, 504));
        viewport.anchoredPosition = new Vector2(0, 6);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var image = viewport.gameObject.AddComponent<UnityEngine.UI.RawImage>(); image.raycastTarget = true;
        if (SystemInfo.graphicsUVStartsAtTop) image.uvRect = new Rect(0, 1, 1, -1);
        texture = new RenderTexture(1760, 804, 24, RenderTextureFormat.ARGB32) { name = "LiveWorldMap", antiAliasing = 2 };
        texture.Create(); image.texture = texture;
        var cameraObject = new GameObject("MapProjection"); cameraObject.transform.SetParent(transform, false);
        mapCamera = cameraObject.AddComponent<Camera>(); mapCamera.enabled = false;
        mapCamera.orthographic = true; mapCamera.aspect = 1102f / 504;
        mapCamera.transform.rotation = Quaternion.Euler(90, 0, 0);
        mapCamera.nearClipPlane = .1f;
        surfaceMaterial = new Material(Resources.Load<Shader>("WorldMapSurface")) { hideFlags = HideFlags.DontSave };
        surfaceMaterial.SetInt("_ZTest", (int)(SystemInfo.usesReversedZBuffer ? CompareFunction.GreaterEqual : CompareFunction.LessEqual));
        playerPin = UIBuild.Rect("YourPosition", viewport, new Vector2(24, 24));
        UITheme.Rounded(playerPin, UITheme.Ivory, 12, 0);
        var dot = UIBuild.Rect("Dot", playerPin, new Vector2(16, 16));
        UITheme.Rounded(dot, UITheme.Selected, 8, 0);
        Label(playerPin, "나", 12, Vector2.zero, new Vector2(20, 20)).fontStyle = FontStyles.Bold;
        Label(panel, "N ↑", 20, new Vector2(-520, 284), new Vector2(48, 34));
        detail = Label(panel, "번지를 누르면 부지 정보를 볼 수 있습니다", 15, new Vector2(-210, -264), new Vector2(660, 28));
        detail.alignment = TextAlignmentOptions.MidlineLeft;
        Label(panel, "휠 확대 · 우클릭 드래그 이동     ● 내 위치   /   초록: 내 부지 · 주황: 소유 부지", 13,
            new Vector2(-90, -295), new Vector2(900, 24)).alignment = TextAlignmentOptions.MidlineLeft;
        AddButton(panel, "−", 360, () => Zoom(1.25f));
        AddButton(panel, "+", 402, () => Zoom(.8f));
        AddButton(panel, "전체", 458, Fit, 64);
        AddButton(panel, "내 위치", 532, CenterPlayer, 76);
    }
    static TMP_Text Label(Transform parent, string value, float size, Vector2 position, Vector2 dimensions)
    {
        var t = UITheme.Text(parent, value, size, UITheme.Ink, TitleUi.Font, dimensions);
        t.rectTransform.anchoredPosition = position; return t;
    }
    static void AddButton(Transform parent, string label, float x, UnityEngine.Events.UnityAction action, float width = 36)
    {
        var b = UITheme.Button(parent, label, new Vector2(width, 34), false, TitleUi.Font, action);
        ((RectTransform)b.transform).anchoredPosition = new Vector2(x, -281);
    }

    void Scan()
    {
        surfaces.Clear();
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.gameObject.scene == scene && r.enabled && r.gameObject.activeInHierarchy && r.gameObject.layer != 5 && r.gameObject.layer != 29 &&
                r.GetComponent<MeshFilter>() && !r.GetComponentInParent<Canvas>() && r.bounds.size.magnitude < 10000)
                surfaces.Add(r);
        var parcels = FindObjectsByType<MapParcel>(FindObjectsSortMode.None);
        for (int i = markers.Count - 1; i >= 0; i--)
            if (!markers[i].parcel || markers[i].parcel.gameObject.scene != scene || !markers[i].parcel.isActiveAndEnabled)
            { Destroy(markers[i].rect.gameObject); markers.RemoveAt(i); }
        foreach (var p in parcels)
        {
            if (p.gameObject.scene != scene || markers.Exists(m => m.parcel == p)) continue;
            var b = UITheme.Button(viewport, p.number.ToString(), new Vector2(38, 23), false, TitleUi.Font, () => Select(p));
            var t = b.GetComponentInChildren<TMP_Text>(); t.fontSize = 13; t.fontStyle = FontStyles.Bold;
            markers.Add(new Marker { parcel = p, rect = (RectTransform)b.transform, button = b });
        }
        playerPin.SetAsLastSibling();
        float top = bounds.max.y;
        foreach (var r in surfaces) if (r) top = Mathf.Max(top, r.bounds.max.y);
        if (mapCamera && mapCamera.transform.position.y < top + 20)
        {
            var position = mapCamera.transform.position; position.y = top + 100;
            mapCamera.transform.position = position;
            mapCamera.farClipPlane = Mathf.Max(mapCamera.farClipPlane, top - bounds.min.y + 300);
        }
        if (markers.Count > 0) heading.text = markers[0].parcel.district + " 지도";
        else heading.text = "지역 지도";
    }

    public void Fit()
    {
        bool found = false;
        foreach (var r in surfaces)
        {
            if (!r) continue;
            // Very large decorative horizon meshes must not shrink the playable streets to a dot.
            if (r.bounds.size.x > 2500 || r.bounds.size.z > 2500) continue;
            if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
        }
        if (!found) bounds = new Bounds(player ? player.transform.position : Vector3.zero, Vector3.one * 100);
        foreach (var m in markers) bounds.Encapsulate(m.parcel.Center);
        if (markers.Count > 0)
        {
            var area = new Bounds(markers[0].parcel.Center, Vector3.zero);
            foreach (var m in markers)
                area.Encapsulate(new Bounds(m.parcel.Center, new Vector3(m.parcel.size.x, 1, m.parcel.size.y)));
            bounds = new Bounds(new Vector3(area.center.x, bounds.center.y, area.center.z),
                new Vector3(area.size.x + 40, bounds.size.y, area.size.z + 40));
        }
        fullSize = Mathf.Max(25, Mathf.Max(bounds.extents.z, bounds.extents.x / mapCamera.aspect) * 1.08f);
        mapCamera.orthographicSize = fullSize;
        mapCamera.farClipPlane = Mathf.Max(500, bounds.size.y + 300);
        mapCamera.transform.position = new Vector3(bounds.center.x, bounds.max.y + 100, bounds.center.z);
        dirty = true;
    }
    public void Zoom(float scale)
    {
        mapCamera.orthographicSize = Mathf.Clamp(mapCamera.orthographicSize * scale, Mathf.Min(12, fullSize), fullSize);
        ClampCamera(); dirty = true;
    }
    public void CenterPlayer()
    {
        if (!player) return;
        var p = player.transform.position; p.y = mapCamera.transform.position.y;
        mapCamera.transform.position = p; dirty = true;
    }
    void ClampCamera()
    {
        var p = mapCamera.transform.position;
        p.x = Mathf.Clamp(p.x, bounds.min.x, bounds.max.x); p.z = Mathf.Clamp(p.z, bounds.min.z, bounds.max.z);
        mapCamera.transform.position = p;
    }

    void RenderMap()
    {
        // Render actual meshes with a matte map palette. No world materials, lighting, layers or objects are modified.
        var buffer = new CommandBuffer { name = "Live cartographic map" };
        try
        {
            buffer.SetRenderTarget(texture);
            buffer.ClearRenderTarget(true, true, UITheme.Hex(0xBAC9BB), SystemInfo.usesReversedZBuffer ? 0f : 1f);
            buffer.SetViewProjectionMatrices(mapCamera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(mapCamera.projectionMatrix, true));
            var planes = GeometryUtility.CalculateFrustumPlanes(mapCamera);
            foreach (var r in surfaces)
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    Color color = material && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                        material && material.HasProperty("_Color") ? material.GetColor("_Color") : UITheme.Slate;
                    buffer.SetGlobalColor("_MapColor", color);
                    // DrawRenderer preserves Unity's static-batch submesh range and transform.
                    buffer.DrawRenderer(r, surfaceMaterial, i, 0);
                }
            }
            Graphics.ExecuteCommandBuffer(buffer); RenderRevision++;
        }
        finally { buffer.Release(); }
    }

    void UpdateMarkers()
    {
        foreach (var m in markers)
        {
            if (!m.parcel) continue;
            Position(m.rect, m.parcel.Center);
            string owner = land ? land.GetOwner(m.parcel.landPlotId) : null;
            var colors = m.button.colors;
            colors.normalColor = m.parcel == selected ? UITheme.Selected : string.IsNullOrEmpty(owner) ? UITheme.Ivory : owner == GameSession.LocalPlayerId ? UITheme.Green : UITheme.Selected;
            m.button.colors = colors;
        }
        if (player) Position(playerPin, player.transform.position);
        if (selected) Select(selected);
    }
    void Position(RectTransform rect, Vector3 world)
    {
        var p = mapCamera.WorldToViewportPoint(world);
        bool visible = p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1;
        rect.gameObject.SetActive(visible);
        rect.anchoredPosition = new Vector2((p.x - .5f) * viewport.rect.width, (p.y - .5f) * viewport.rect.height);
    }
    void Select(MapParcel p)
    {
        selected = p;
        string owner = land ? land.GetOwner(p.landPlotId) : null;
        string company = land ? land.GetCompanyName(p.landPlotId) : null;
        string state = string.IsNullOrEmpty(owner) ? "소유 정보 없음" : owner == GameSession.LocalPlayerId ? "내 부지" : "소유 부지";
        detail.text = p.Address + "  ·  " + state + (string.IsNullOrEmpty(company) ? "" : "  ·  " + company);
    }
}
