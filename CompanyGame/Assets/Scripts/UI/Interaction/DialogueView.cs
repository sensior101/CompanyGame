using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Shared dialogue Canvas. The bubble tracks the speaking NPC's head on screen.</summary>
[DefaultExecutionOrder(50)]
public sealed class DialogueView : MonoBehaviour
{
    DialogueManager manager;
    RectTransform canvasRect, bubble, prompt, content, choicePanel, choiceContent;
    TMP_Text line;
    TMP_Text speakerLabel;
    RectTransform speakerBadge;
    TMP_FontAsset font;
    Canvas canvas;
    readonly List<UnityEngine.UI.Button> buttons = new List<UnityEngine.UI.Button>();
    readonly ControlLock controls = new ControlLock();
    PlayerMovement lockedPlayer;
    int selected, openedFrame;
    bool keyboardArmed;
    PlayerCameraController dialogueCamera;
    Transform cameraSpeaker;
    DialogueData cameraDialogue;
    NpcTrader cameraTrader;
    bool dialogueClosed, interactionWindowWasOpen;

    void Update()
    {
        UpdateDialogueCamera();
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
        manager.Closed += ConversationClosed;
    }

    bool CanStart()
    {
        var interaction = PlayerInteraction.Local;
        return isActiveAndEnabled && !cameraSpeaker && interaction && interaction.isActiveAndEnabled && !interaction.IsInteractionMenuOpen &&
            !BookReader.BlocksInventoryInput && !(interaction.GetComponent<PlayerVehicle>()?.IsRiding ?? false) && !PlayerInventory.IsAnyOpen && !PlayerInventory.SpaceConsumedThisFrame && !ChatUIManager.IsChatting &&
            !UIEventSystem.IsEditingText() && !(PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen) &&
            SceneLoadManager.Traveller && SceneLoadManager.Traveller.isActiveAndEnabled;
    }

    void LateUpdate()
    {
        if (!manager) return;
        if (!canvas && (manager.Nearby || manager.Current)) Build();
        if (!canvas) return;
        prompt.gameObject.SetActive(manager.Nearby && (!manager.Current || manager.IsNotice) && CanStart());
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
        point.y = Mathf.Clamp(point.y, canvasRect.rect.yMin + 16f, canvasRect.rect.yMax - bubble.sizeDelta.y - 30f);
        bubble.anchoredPosition = point;
    }

    void Refresh()
    {
        if (!isActiveAndEnabled) return;
        UpdateDialogueCamera();
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
        // A cashier's greeting remains readable while the trade window opens.
        // Bubble graphics do not intercept clicks on the trade controls beneath them.
        canvas.sortingOrder = manager.HasChoices ? 198 : 310;
        if (manager.HasChoices && !InventoryItemSelector.IsOpen)
        {
            lockedPlayer = SceneLoadManager.Traveller as PlayerMovement;
            if (lockedPlayer) controls.Hold(lockedPlayer);
        }
        prompt.gameObject.SetActive(false);
        line.text = manager.CurrentLine;
        var subject = manager.Current.gameObject;
        var nameTag = subject.GetComponent<NameTag>();
        var trader = subject.GetComponent<NpcTrader>();
        speakerLabel.text = subject.GetComponent<PlayerMovement>() ? GameSession.LocalPlayerName :
            !string.IsNullOrWhiteSpace(manager.Current.displayName) ? manager.Current.displayName :
            trader && !string.IsNullOrWhiteSpace(trader.displayName) ? trader.displayName :
            nameTag ? (nameTag.nameSource != null ? nameTag.nameSource() : nameTag.Text) : "NPC";
        float nameWidth = Mathf.Max(48f, speakerLabel.GetPreferredValues(speakerLabel.text).x + 24f);
        speakerBadge.sizeDelta = new Vector2(nameWidth, 30f);
        float width = Mathf.Clamp(line.GetPreferredValues(line.text).x + 48f, 260f, 520f);
        width = Mathf.Max(width, nameWidth + 32f);
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

    // Presentation lives in the UI layer; the camera only receives a subject and a focus point.
    void UpdateDialogueCamera()
    {
        var interaction = PlayerInteraction.Local;
        var player = SceneLoadManager.Traveller as PlayerMovement;
        if (!manager || !interaction || !interaction.isActiveAndEnabled || !player || SceneLoadManager.IsLoading)
        { ReleaseDialogueCamera(SceneLoadManager.IsLoading); return; }
        if (PlayerInventory.IsAnyOpen || BookReader.BlocksInventoryInput || ChatUIManager.IsChatting ||
            (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen))
        { ReleaseDialogueCamera(); return; }

        var current = manager.Current;
        bool windowOpen = interaction.IsTradeOpen || InventoryItemSelector.IsOpen;
        // A final reply's reading timer must not keep the player in the shot.
        // An actual trade/selection window still retains its own interaction.
        if (current && manager.IsNotice && !windowOpen)
        { ReleaseDialogueCamera(); return; }
        var trader = current ? current.GetComponent<NpcTrader>() : null;
        if (current && !manager.IsNotice && (current.HasChoices || manager.HasChoices || manager.WaitingForResponse ||
                        (trader && trader.isActiveAndEnabled)))
            FocusSpeaker(player, current.transform, current, trader);
        else if (interaction.IsTradeOpen && interaction.TradeSpeaker)
        {
            trader = interaction.TradeSpeaker;
            FocusSpeaker(player, trader.transform, trader.GetComponent<DialogueData>(), trader);
        }
        else if (current && current.transform != cameraSpeaker)
        { ReleaseDialogueCamera(); return; }

        if (!cameraSpeaker || !cameraSpeaker.gameObject.activeInHierarchy ||
            cameraSpeaker.gameObject.scene != SceneLoadManager.CurrentMap ||
            (cameraDialogue && !cameraDialogue.InRange(player.transform, .6f)) ||
            (!cameraDialogue && cameraTrader && !cameraTrader.IsInRange(player.transform)))
        { ReleaseDialogueCamera(); return; }

        bool windowClosed = interactionWindowWasOpen && !windowOpen;
        interactionWindowWasOpen = windowOpen;
        if (current && current.transform == cameraSpeaker) dialogueClosed = false;
        // A choice may open its next UI asynchronously. Only an actual close (or a
        // previously opened interaction window closing) can release this session.
        if (!current && !windowOpen && (dialogueClosed || windowClosed)) ReleaseDialogueCamera();
    }

    void ConversationClosed() => dialogueClosed = true;

    void FocusSpeaker(PlayerMovement player, Transform speaker, DialogueData dialogue, NpcTrader trader)
    {
        var camera = player.viewCamera ? player.viewCamera.GetComponent<PlayerCameraController>() : null;
        if (!camera || !camera.isActiveAndEnabled) return;
        if (dialogueCamera && dialogueCamera != camera) dialogueCamera.EndDialogueCamera();
        if (cameraSpeaker != speaker) { dialogueClosed = false; interactionWindowWasOpen = false; }
        dialogueCamera = camera;
        cameraSpeaker = speaker; cameraDialogue = dialogue; cameraTrader = trader;
        camera.BeginDialogueCamera(speaker, dialogue ? dialogue.head : null, speaker.position + Vector3.up * 1.6f);
    }

    void ReleaseDialogueCamera(bool immediately = false)
    {
        if (dialogueCamera) dialogueCamera.EndDialogueCamera(immediately);
        dialogueCamera = null; cameraSpeaker = null; cameraDialogue = null; cameraTrader = null;
        dialogueClosed = false; interactionWindowWasOpen = false;
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
        graphic.color = UITheme.A(UITheme.Ivory, .93f); graphic.raycastTarget = false;
        speakerBadge = UITheme.Panel("SpeakerBadge", bubble, new Vector2(80f, 30f), UITheme.Ink, 9f, false);
        speakerBadge.anchorMin = speakerBadge.anchorMax = new Vector2(0f, 1f);
        speakerBadge.pivot = new Vector2(0f, .5f);
        speakerBadge.anchoredPosition = new Vector2(16f, 2f);
        speakerLabel = UITheme.Text(speakerBadge, "", 16f, UITheme.Ivory, font);
        speakerLabel.fontStyle = FontStyles.Bold;
        speakerLabel.richText = false;
        speakerLabel.textWrappingMode = TextWrappingModes.NoWrap;
        UIBuild.Stretch(speakerLabel.rectTransform);
        content = UIBuild.Rect("Content", bubble, Vector2.zero); UIBuild.Stretch(content);
        content.offsetMin = new Vector2(24f, 30f); content.offsetMax = new Vector2(-24f, -18f);
        var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = 6f; layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        line = Text("Line", content, "", 21f, UITheme.Ink);
        line.alignment = TextAlignmentOptions.Left; line.textWrappingMode = TextWrappingModes.Normal;
        line.richText = false;
        line.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        choicePanel = UIBuild.Rect("DialogueChoices", host.transform, new Vector2(320f, 134f));
        choicePanel.anchorMin = choicePanel.anchorMax = new Vector2(.5f, 0f);
        choicePanel.pivot = new Vector2(.5f, 0f);
        choicePanel.anchoredPosition = new Vector2(200f, 160f);
        UITheme.Rounded(choicePanel, UITheme.A(UITheme.Ivory, .93f), UITheme.Radius * 1.4f, 1.5f).raycastTarget = true;
        choiceContent = UIBuild.Rect("Options", choicePanel, Vector2.zero); UIBuild.Stretch(choiceContent);
        choiceContent.offsetMin = new Vector2(12f, 38f); choiceContent.offsetMax = new Vector2(-12f, -12f);
        var choiceLayout = choiceContent.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        choiceLayout.spacing = 6f; choiceLayout.childControlWidth = true; choiceLayout.childControlHeight = true;
        choiceLayout.childForceExpandHeight = false; choiceLayout.childForceExpandWidth = true;
        var choiceHint = Text("Controls", choicePanel, "↑↓ 선택 · 스페이스바 확인 · 클릭", 14f, UITheme.Brown);
        choiceHint.rectTransform.anchorMin = choiceHint.rectTransform.anchorMax = new Vector2(.5f, 0f);
        choiceHint.rectTransform.anchoredPosition = new Vector2(0f, 20f);
        choiceHint.rectTransform.sizeDelta = new Vector2(304f, 24f);
        choiceHint.alignment = TextAlignmentOptions.Center;
        prompt = UIBuild.Rect("TalkPrompt", host.transform, new Vector2(278f, 48f));
        prompt.anchorMin = prompt.anchorMax = new Vector2(.5f, 0f); prompt.pivot = new Vector2(.5f, 0f);
        prompt.anchoredPosition = new Vector2(0f, 148f);
        UITheme.Rounded(prompt, UITheme.A(UITheme.Ivory, .93f), UITheme.Radius, 1.5f);
        var hint = Text("Label", prompt, "대화하기  (스페이스바)", 20f, UITheme.Ink);
        UIBuild.Stretch(hint.rectTransform); hint.alignment = TextAlignmentOptions.Center;
        bubble.gameObject.SetActive(false); prompt.gameObject.SetActive(false); choicePanel.gameObject.SetActive(false);
    }

    void AddButton()
    {
        int index = buttons.Count;
        var rect = UIBuild.Rect("Choice" + index, choiceContent, new Vector2(0f, 36f));
        rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 36f;
        var image = UITheme.Rounded(rect, Color.white, UITheme.SmallRadius, 0f); image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var colors = button.colors;
        colors.normalColor = UITheme.Sand; colors.highlightedColor = UITheme.Selected;
        colors.pressedColor = UITheme.Pressed; colors.selectedColor = colors.normalColor;
        button.colors = colors;
        var label = Text("Label", rect, "", 18f, UITheme.Ink);
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
            colors.normalColor = i == selected ? UITheme.Selected : UITheme.Sand;
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

    void OnDisable()
    {
        ReleaseDialogueCamera(true);
        if (lockedPlayer) controls.Release(lockedPlayer, !SceneLoadManager.IsLoading);
        lockedPlayer = null;
    }

    void OnDestroy()
    {
        if (manager) { manager.Changed -= Refresh; manager.Closed -= ConversationClosed; manager.CanStart = () => false; }
    }
}
