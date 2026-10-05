using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Shared dialogue Canvas. The bubble tracks the speaking NPC's head on screen.</summary>
public sealed class DialogueView : MonoBehaviour
{
    DialogueManager manager;
    RectTransform canvasRect, bubble, prompt, content, choicePanel, choiceContent;
    TMP_Text line;
    TMP_FontAsset font;
    Canvas canvas;
    readonly List<UnityEngine.UI.Button> buttons = new List<UnityEngine.UI.Button>();
    readonly ControlLock controls = new ControlLock();
    PlayerMovement lockedPlayer;
    int selected, openedFrame;
    bool keyboardArmed;

    void Update()
    {
        if (!manager || !manager.Current || manager.VisibleOptions.Count == 0) return;
        // The Space press that starts a conversation must never choose its first option.
        if (Time.frameCount > openedFrame && !GameInput.InteractHeld) keyboardArmed = true;
        if (GameInput.NavUpPressed) SelectRow((selected + manager.VisibleOptions.Count - 1) % manager.VisibleOptions.Count);
        if (GameInput.NavDownPressed) SelectRow((selected + 1) % manager.VisibleOptions.Count);
        if (keyboardArmed && GameInput.InteractPressed)
        {
            PlayerInventory.ConsumeSpaceThisFrame();
            manager.Select(manager.VisibleOptions[selected].id);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        DialogueManager.EnsureInstance();
        if (!DialogueManager.Instance.GetComponent<DialogueView>())
            DialogueManager.Instance.gameObject.AddComponent<DialogueView>();
    }

    void Awake()
    {
        manager = GetComponent<DialogueManager>();
        manager.CanStart = CanStart;
        manager.Changed += Refresh;
    }

    bool CanStart()
    {
        var interaction = PlayerInteraction.Local;
        return interaction && interaction.isActiveAndEnabled && !interaction.IsInteractionMenuOpen &&
            !PlayerInventory.IsAnyOpen && !PlayerInventory.SpaceConsumedThisFrame && !ChatUIManager.IsChatting &&
            !UIEventSystem.IsEditingText() && !(PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) &&
            SceneLoadManager.Traveller && SceneLoadManager.Traveller.isActiveAndEnabled;
    }

    void LateUpdate()
    {
        if (!manager) return;
        if (!canvas && (manager.Nearby || manager.Current)) Build();
        if (!canvas) return;
        prompt.gameObject.SetActive(manager.Nearby && !manager.Current && CanStart());
        if (!manager.Current) return;
        // Other overlays take focus cleanly if opened by an external system.
        if (PlayerInventory.IsAnyOpen || (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) || ChatUIManager.IsChatting)
        { manager.Close(); return; }
        var camera = Camera.main;
        if (!camera) { bubble.gameObject.SetActive(false); return; }
        Vector3 screen = camera.WorldToScreenPoint(manager.Current.BubblePosition);
        bubble.gameObject.SetActive(screen.z > 0f);
        if (screen.z <= 0f) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var point);
        float half = bubble.sizeDelta.x * .5f;
        point.x = Mathf.Clamp(point.x, canvasRect.rect.xMin + half + 12f, canvasRect.rect.xMax - half - 12f);
        point.y = Mathf.Clamp(point.y, canvasRect.rect.yMin + 16f, canvasRect.rect.yMax - bubble.sizeDelta.y - 12f);
        bubble.anchoredPosition = point;
    }

    void Refresh()
    {
        PlayerInventory.ConsumeSpaceThisFrame();
        if (lockedPlayer)
        {
            controls.Release(lockedPlayer, !SceneLoadManager.IsLoading);
            lockedPlayer = null;
        }
        if (!manager.Current)
        {
            if (bubble) bubble.gameObject.SetActive(false);
            if (choicePanel) choicePanel.gameObject.SetActive(false);
            return;
        }
        if (!canvas) Build();
        if (manager.Current.HasChoices)
        {
            lockedPlayer = SceneLoadManager.Traveller as PlayerMovement;
            if (lockedPlayer) controls.Hold(lockedPlayer);
        }
        prompt.gameObject.SetActive(false);
        line.text = manager.Current.line;
        float width = Mathf.Clamp(line.GetPreferredValues(line.text).x + 48f, 260f, 520f);
        float textHeight = line.GetPreferredValues(line.text, width - 48f, 0f).y;
        int count = manager.VisibleOptions.Count;
        float height = 48f + textHeight;
        bubble.sizeDelta = new Vector2(width, height);
        var lineLayout = line.GetComponent<UnityEngine.UI.LayoutElement>();
        lineLayout.preferredHeight = textHeight;
        for (int i = 0; i < buttons.Count; i++) buttons[i].gameObject.SetActive(false);
        for (int i = 0; i < count; i++)
        {
            if (i == buttons.Count) AddButton();
            var button = buttons[i];
            var option = manager.VisibleOptions[i];
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<TMP_Text>().text = option.label;
            button.onClick.RemoveAllListeners();
            string id = option.id;
            button.onClick.AddListener(() => manager.Select(id));
        }
        choicePanel.sizeDelta = new Vector2(320f, 50f + count * 42f);
        choicePanel.gameObject.SetActive(count > 0);
        openedFrame = Time.frameCount;
        keyboardArmed = false;
        SelectRow(0);
        bubble.gameObject.SetActive(true);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(choiceContent);
    }

    void Build()
    {
        UIEventSystem.Ensure();
        font = PlayerInteraction.Local && PlayerInteraction.Local.uiFont ? PlayerInteraction.Local.uiFont : TMP_Settings.defaultFontAsset;
        var host = new GameObject("DialogueCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        host.transform.SetParent(transform, false);
        canvas = host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 198;
        canvasRect = host.GetComponent<RectTransform>();
        var scaler = host.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f); scaler.matchWidthOrHeight = .5f;
        bubble = UIBuild.Rect("SpeechBubble", host.transform, new Vector2(400f, 130f));
        bubble.pivot = new Vector2(.5f, 0f);
        var graphic = bubble.gameObject.AddComponent<DialogueBubbleGraphic>();
        graphic.color = new Color(.98f, .97f, .93f, .90f); graphic.raycastTarget = false;
        content = UIBuild.Rect("Content", bubble, Vector2.zero); UIBuild.Stretch(content);
        content.offsetMin = new Vector2(24f, 30f); content.offsetMax = new Vector2(-24f, -18f);
        var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = 6f; layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        line = Text("Line", content, "", 21f, new Color(.10f, .12f, .14f));
        line.alignment = TextAlignmentOptions.Left; line.textWrappingMode = TextWrappingModes.Normal;
        line.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        choicePanel = UIBuild.Rect("DialogueChoices", host.transform, new Vector2(320f, 134f));
        choicePanel.anchorMin = choicePanel.anchorMax = new Vector2(.5f, 0f);
        choicePanel.pivot = new Vector2(.5f, 0f);
        choicePanel.anchoredPosition = new Vector2(200f, 160f);
        var choiceBackground = choicePanel.gameObject.AddComponent<UnityEngine.UI.Image>();
        choiceBackground.color = new Color(.075f, .12f, .17f, .94f);
        choiceContent = UIBuild.Rect("Options", choicePanel, Vector2.zero); UIBuild.Stretch(choiceContent);
        choiceContent.offsetMin = new Vector2(12f, 38f); choiceContent.offsetMax = new Vector2(-12f, -12f);
        var choiceLayout = choiceContent.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        choiceLayout.spacing = 6f; choiceLayout.childControlWidth = true; choiceLayout.childControlHeight = true;
        choiceLayout.childForceExpandHeight = false; choiceLayout.childForceExpandWidth = true;
        var choiceHint = Text("Controls", choicePanel, "↑↓ 선택 · 스페이스바 확인 · 클릭", 14f, new Color(.78f, .84f, .87f));
        choiceHint.rectTransform.anchorMin = choiceHint.rectTransform.anchorMax = new Vector2(.5f, 0f);
        choiceHint.rectTransform.anchoredPosition = new Vector2(0f, 20f);
        choiceHint.rectTransform.sizeDelta = new Vector2(304f, 24f);
        choiceHint.alignment = TextAlignmentOptions.Center;
        prompt = UIBuild.Rect("TalkPrompt", host.transform, new Vector2(278f, 48f));
        prompt.anchorMin = prompt.anchorMax = new Vector2(.5f, 0f); prompt.pivot = new Vector2(.5f, 0f);
        prompt.anchoredPosition = new Vector2(0f, 148f);
        var background = prompt.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(.075f, .12f, .17f, .92f); background.raycastTarget = false;
        var hint = Text("Label", prompt, "대화하기  (스페이스바)", 20f, new Color(.94f, .96f, .97f));
        UIBuild.Stretch(hint.rectTransform); hint.alignment = TextAlignmentOptions.Center;
        bubble.gameObject.SetActive(false); prompt.gameObject.SetActive(false); choicePanel.gameObject.SetActive(false);
    }

    void AddButton()
    {
        int index = buttons.Count;
        var rect = UIBuild.Rect("Choice" + index, choiceContent, new Vector2(0f, 36f));
        rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 36f;
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = Color.white;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var colors = button.colors;
        colors.normalColor = new Color(.80f, .85f, .86f, .8f); colors.highlightedColor = new Color(.3f, .88f, .72f);
        colors.pressedColor = new Color(.23f, .70f, .57f); colors.selectedColor = colors.normalColor;
        button.colors = colors;
        var label = Text("Label", rect, "", 18f, new Color(.075f, .12f, .17f));
        UIBuild.Stretch(label.rectTransform); label.alignment = TextAlignmentOptions.Center;
        buttons.Add(button);
        var hover = rect.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        entry.callback.AddListener(_ => SelectRow(index));
        hover.triggers.Add(entry);
    }

    void SelectRow(int index)
    {
        selected = index;
        for (int i = 0; i < buttons.Count; i++)
        {
            var colors = buttons[i].colors;
            colors.normalColor = i == selected ? new Color(.3f, .88f, .72f) : new Color(.80f, .85f, .86f, .8f);
            colors.selectedColor = colors.normalColor;
            buttons[i].colors = colors;
            // One shared mouse/keyboard selection; hover tint must not highlight an old row too.
            buttons[i].targetGraphic.color = colors.normalColor;
        }
    }

    TMP_Text Text(string name, Transform parent, string value, float size, Color color)
    {
        var rect = UIBuild.Rect(name, parent, Vector2.zero);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.text = value; text.color = color; text.raycastTarget = false;
        return text;
    }

    void OnDestroy()
    {
        if (manager) { manager.Changed -= Refresh; manager.CanStart = () => false; }
        if (lockedPlayer) controls.Release(lockedPlayer, !SceneLoadManager.IsLoading);
    }
}
