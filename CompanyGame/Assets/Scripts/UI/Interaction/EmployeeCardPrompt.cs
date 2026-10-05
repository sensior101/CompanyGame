using CompanyGame.World.Maps;
using TMPro;
using UnityEngine;

/// <summary>Player-owned presentation for the nearest reception card; the pickup owns F and inventory changes.</summary>
[DisallowMultipleComponent]
public sealed class EmployeeCardPrompt : MonoBehaviour
{
    static readonly Color Ink = new Color(.075f, .12f, .17f, .92f);
    static readonly Color Paper = new Color(.94f, .96f, .97f);
    static readonly Color Mint = new Color(.3f, .88f, .72f);

    GameObject root;
    EmployeeCardPickup pickup;
    TMP_Text label;
    TMP_Text keyLabel;

    void LateUpdate()
    {
        var interaction = PlayerInteraction.Local;
        if (!CanShow(interaction)) { Hide(); return; }
        var near = EmployeeCardPickup.FindNearest(transform);
        if (!near) { Hide(); return; }
        if (!root) Build();
        if (pickup != near)
        {
            if (pickup) pickup.AvailabilityChanged -= Hide;
            pickup = near;
            pickup.AvailabilityChanged += Hide;
        }
        var font = interaction.uiFont ? interaction.uiFont : TMP_Settings.defaultFontAsset;
        if (label.font != font) label.font = font;
        if (keyLabel.font != font) keyLabel.font = font;
        label.text = near.card.DisplayName + " 받기";
        root.SetActive(true);
    }

    static bool CanShow(PlayerInteraction interaction) =>
        interaction && !SceneLoadManager.IsLoading && !PlayerInventory.IsAnyOpen && !ChatUIManager.IsChatting &&
        !UIEventSystem.IsEditingText() && !interaction.IsInteractionMenuOpen && !InputFocus.GameplayBlocked() &&
        !(PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen);

    void Build()
    {
        root = new GameObject("EmployeeCardPromptCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 195;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = .5f;

        var panel = UIBuild.Rect("Panel", root.transform, new Vector2(380f, 64f));
        panel.anchorMin = panel.anchorMax = new Vector2(.5f, 0f);
        panel.pivot = new Vector2(.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 148f);
        var background = panel.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = Ink;
        background.raycastTarget = false;

        var key = UIBuild.Rect("KeyBadge", panel, new Vector2(40f, 36f));
        key.anchoredPosition = new Vector2(-154f, 0f);
        var badge = key.gameObject.AddComponent<UnityEngine.UI.Image>();
        badge.color = Mint;
        badge.raycastTarget = false;
        keyLabel = Text("Key", key, "F", 20f, Ink, Vector2.zero, new Vector2(40f, 36f));
        label = Text("Label", panel, "", 20f, Paper, new Vector2(25f, 0f), new Vector2(284f, 44f));
        label.enableAutoSizing = true;
        label.fontSizeMin = 16f;
        label.fontSizeMax = 20f;
    }

    static TMP_Text Text(string name, RectTransform parent, string value, float size, Color color, Vector2 pos, Vector2 box)
    {
        var rect = UIBuild.Rect(name, parent, box);
        rect.anchoredPosition = pos;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        var interaction = PlayerInteraction.Local;
        text.font = interaction && interaction.uiFont ? interaction.uiFont : TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    void Hide()
    {
        if (pickup) pickup.AvailabilityChanged -= Hide;
        pickup = null;
        if (root) root.SetActive(false);
    }

    void OnDisable() { Hide(); }

    void OnDestroy()
    {
        Hide();
        if (root) Destroy(root);
    }
}
