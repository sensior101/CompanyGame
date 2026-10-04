using System.Collections.Generic;
using CompanyGame.World.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Non-modal floor picker shown near a FloorStairs. Keys and clicks are handled here, never via EventSystem navigation.</summary>
[DisallowMultipleComponent]
public sealed class FloorStairsMenu : MonoBehaviour
{
    /// <summary>True while the menu is up or acted on Enter this frame, so chat does not open too.</summary>
    public static bool OwnsEnter => shown || enterFrame == Time.frameCount;

    static bool shown;
    static int enterFrame = -1;

    static readonly Color Ink = new Color(.075f, .12f, .17f, .92f);
    static readonly Color Paper = new Color(.94f, .96f, .97f);
    static readonly Color Mint = new Color(.3f, .88f, .72f);
    static readonly Color Idle = new Color(.19f, .25f, .3f);

    GameObject root;
    FloorStairs stairs;
    int selected;
    readonly List<int> targets = new List<int>();
    readonly List<Image> rows = new List<Image>();
    readonly List<TMP_Text> labels = new List<TMP_Text>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { shown = false; enterFrame = -1; }

    void Update()
    {
        var near = CanShow(PlayerInteraction.Local) ? FloorStairs.FindNearest(transform) : null;
        if (!near) { Hide(); return; }
        if (near != stairs || !shown) Show(near);
        if (GameInput.NavUpPressed) selected = Mathf.Max(0, selected - 1);
        if (GameInput.NavDownPressed) selected = Mathf.Min(targets.Count - 1, selected + 1);
        Refresh();
        if (GameInput.SubmitPressed) { enterFrame = Time.frameCount; Go(selected); }
    }

    static bool CanShow(PlayerInteraction interaction) =>
        interaction && !SceneLoadManager.IsLoading && !PlayerInventory.IsAnyOpen && !ChatUIManager.IsChatting &&
        !UIEventSystem.IsEditingText() && !interaction.IsInteractionMenuOpen &&
        !(PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen);

    void Go(int index)
    {
        if (index < 0 || index >= targets.Count) return;
        PlayerSpawner.TeleportInScene("floor_" + targets[index]);
    }

    void Show(FloorStairs near)
    {
        stairs = near;
        targets.Clear();
        if (near.floor < near.topFloor) targets.Add(near.floor + 1);
        if (near.floor > 1) targets.Add(near.floor - 1);
        selected = 0;
        if (!root) Build();
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].gameObject.SetActive(i < targets.Count);
            if (i < targets.Count) labels[i].text = (targets[i] > near.floor ? "윗층으로 이동" : "아래층으로 이동") + "  (" + targets[i] + "층)";
        }
        root.SetActive(true);
        shown = true;
    }

    void Hide()
    {
        shown = false;
        stairs = null;
        if (root) root.SetActive(false);
    }

    void Refresh()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            rows[i].color = i == selected ? Mint : Idle;
            labels[i].color = i == selected ? Ink : Paper;
        }
    }

    void Build()
    {
        var host = new GameObject("StairsMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        host.transform.SetParent(transform, false);
        root = host;
        var canvas = host.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 195;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = .5f;
        UIEventSystem.Ensure();
        var panel = UIBuild.Rect("Panel", host.transform, new Vector2(320f, 130f));
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, 0f);
        panel.pivot = new Vector2(.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 148f);
        panel.gameObject.AddComponent<Image>().color = Ink;
        var interaction = PlayerInteraction.Local;
        var font = interaction && interaction.uiFont ? interaction.uiFont : TMP_Settings.defaultFontAsset;
        for (int i = 0; i < 2; i++) BuildRow(panel, font, i);
        var hint = Text("Hint", panel, font, "↑↓ 선택   Enter 이동", 15f, new Color(.72f, .79f, .83f), new Vector2(0f, -53f), new Vector2(300f, 24f));
        hint.raycastTarget = false;
    }

    void BuildRow(RectTransform panel, TMP_FontAsset font, int index)
    {
        var rect = UIBuild.Rect("Row" + index, panel, new Vector2(296f, 42f));
        rect.anchoredPosition = new Vector2(0f, 34f - index * 50f);
        var image = rect.gameObject.AddComponent<Image>();
        var button = rect.gameObject.AddComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        int captured = index;
        button.onClick.AddListener(() =>
        {
            selected = captured;
            Go(captured);
            // Keep the button unselected so Enter cannot submit it a second time.
            if (UnityEngine.EventSystems.EventSystem.current) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        });
        rows.Add(image);
        var label = Text("Label", rect, font, "", 20f, Paper, Vector2.zero, new Vector2(290f, 40f));
        label.raycastTarget = false;
        labels.Add(label);
    }

    static TMP_Text Text(string name, RectTransform parent, TMP_FontAsset font, string value, float size, Color color, Vector2 pos, Vector2 box)
    {
        var rect = UIBuild.Rect(name, parent, box);
        rect.anchoredPosition = pos;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        return text;
    }

    void OnDisable() { Hide(); }
}
