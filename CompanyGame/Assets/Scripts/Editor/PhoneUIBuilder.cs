using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Builds a placeholder phone UI in the open scene. Layout uses layout groups, so designers can restyle freely afterwards.</summary>
public static class PhoneUIBuilder
{
    private const string FontPath = "Assets/Art/font/NotoSansKR-Regular SDF.asset";
    private const float PhoneWidth = 520f;
    private const float PhoneHeight = 860f;
    private const float HeaderHeight = 64f;

    private static readonly Color PhoneBg = new Color(0.09f, 0.10f, 0.13f, 0.97f);
    private static readonly Color HeaderBg = new Color(0.14f, 0.16f, 0.21f, 1f);
    private static readonly Color ScreenBg = new Color(0.12f, 0.13f, 0.17f, 1f);
    private static readonly Color RowBg = new Color(0.17f, 0.19f, 0.25f, 1f);
    private static readonly Color ListBg = new Color(0f, 0f, 0f, 0.25f);
    private static readonly Color InputBg = new Color(0.07f, 0.08f, 0.11f, 1f);
    private static readonly Color ButtonBlue = new Color(0.25f, 0.42f, 0.86f, 1f);
    private static readonly Color ButtonGreen = new Color(0.20f, 0.60f, 0.42f, 1f);
    private static readonly Color ButtonPurple = new Color(0.50f, 0.36f, 0.82f, 1f);
    private static readonly Color ButtonOrange = new Color(0.90f, 0.55f, 0.22f, 1f);
    private static readonly Color ButtonPink = new Color(0.88f, 0.34f, 0.55f, 1f);
    private static readonly Color ButtonRed = new Color(0.82f, 0.30f, 0.30f, 1f);
    private static readonly Color TextMain = new Color(0.93f, 0.94f, 0.97f, 1f);
    private static readonly Color TextDim = new Color(0.62f, 0.66f, 0.74f, 1f);

    private static TMP_FontAsset font;

    [MenuItem("CompanyGame/Setup/Create Phone UI In Open Scene")]
    public static void Create()
    {
        if (Object.FindAnyObjectByType<PhoneManager>() != null)
        {
            EditorUtility.DisplayDialog("Phone UI",
                "이 씬에는 이미 PhoneManager가 있습니다. 기존 PhoneCanvas를 삭제한 뒤 다시 실행하세요.", "확인");
            return;
        }

        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) Debug.LogWarning("Phone UI: Korean font not found at " + FontPath + ". Korean text may not render.");

        EnsureEventSystem();

        var canvasGo = new GameObject("PhoneCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Phone UI");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var phoneRect = NewRect("Phone", canvasGo.transform);
        Stretch(phoneRect);
        var phone = phoneRect.gameObject.AddComponent<PhoneManager>();
        var input = phoneRect.gameObject.AddComponent<PhoneInputController>();
        Wire(input, ("phone", phone));

        var rootImage = NewPanel("PhoneRoot", phoneRect, PhoneBg);
        var rootRect = (RectTransform)rootImage.transform;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(1f, 0.5f);
        rootRect.pivot = new Vector2(1f, 0.5f);
        rootRect.anchoredPosition = new Vector2(-60f, 0f);
        rootRect.sizeDelta = new Vector2(PhoneWidth, PhoneHeight);

        BuildHeader(rootRect);

        var body = NewRect("Body", rootRect);
        Stretch(body, top: HeaderHeight);
        var appsRoot = NewRect("Apps", phoneRect);

        var apps = new List<PhoneAppBase>();
        var screens = new List<GameObject>();
        var home = BuildHome(body);

        apps.Add(BuildStock(appsRoot, body, screens));
        apps.Add(BuildBank(appsRoot, body, screens));
        apps.Add(BuildRevenue(appsRoot, body, screens));
        apps.Add(BuildShop(appsRoot, body, screens));
        apps.Add(BuildSns(appsRoot, body, screens));
        apps.Add(BuildReport(appsRoot, body, screens));

        var so = new SerializedObject(phone);
        so.FindProperty("phoneRoot").objectReferenceValue = rootRect.gameObject;
        so.FindProperty("homeScreen").objectReferenceValue = home.gameObject;
        var appsProperty = so.FindProperty("apps");
        appsProperty.arraySize = apps.Count;
        for (int i = 0; i < apps.Count; i++) appsProperty.GetArrayElementAtIndex(i).objectReferenceValue = apps[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (var screen in screens) screen.SetActive(false);

        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        Selection.activeGameObject = canvasGo;
        Debug.Log("Phone UI created under 'PhoneCanvas'. Save the scene. Open the phone with the R key in Play mode.");
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    // ---------------------------------------------------------------- frame

    private static void BuildHeader(RectTransform root)
    {
        var header = NewPanel("Header", root, HeaderBg);
        var rect = (RectTransform)header.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, HeaderHeight);

        HStack(header.gameObject, 12, 10);
        var title = NewText("Title", header.transform, "핸드폰", 28, TextMain);
        Layout(title, flexW: 1);
        var close = NewButton("CloseButton", header.transform, "닫기", ButtonRed, prefW: 90, prefH: 44, fontSize: 20);
        AddNav(close.gameObject, PhoneNavButton.NavAction.Close, null);
    }

    private static RectTransform BuildHome(RectTransform body)
    {
        var home = NewRect("HomeScreen", body);
        Stretch(home);
        var grid = home.gameObject.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(24, 24, 32, 24);
        grid.cellSize = new Vector2(216f, 150f);
        grid.spacing = new Vector2(24f, 24f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;

        AddHomeButton(home, "StockButton", "주식", ButtonRed, nameof(StockApplication));
        AddHomeButton(home, "BankButton", "은행", ButtonBlue, nameof(BankApplication));
        AddHomeButton(home, "RevenueButton", "매출", ButtonGreen, nameof(RevenueApplication));
        AddHomeButton(home, "ShopButton", "상점", ButtonOrange, nameof(ShopApplication));
        AddHomeButton(home, "SnsButton", "SNS", ButtonPink, nameof(SNSApplication));
        AddHomeButton(home, "ReportButton", "신고", ButtonPurple, nameof(ReportApplication));
        return home;
    }

    private static void AddHomeButton(RectTransform home, string name, string label, Color color, string appId)
    {
        var button = NewButton(name, home, label, color, fontSize: 32);
        AddNav(button.gameObject, PhoneNavButton.NavAction.OpenApp, appId);
    }

    private static void AddNav(GameObject go, PhoneNavButton.NavAction action, string appId)
    {
        var nav = go.AddComponent<PhoneNavButton>();
        var so = new SerializedObject(nav);
        so.FindProperty("action").enumValueIndex = (int)action;
        if (appId != null) so.FindProperty("appId").stringValue = appId;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // -------------------------------------------------------------- screens

    private static RectTransform NewScreen(string name, RectTransform body, string title, List<GameObject> screens)
    {
        var image = NewPanel(name, body, ScreenBg);
        var root = (RectTransform)image.transform;
        Stretch(root);
        VStack(image.gameObject, 14, 10);
        screens.Add(root.gameObject);

        var bar = NewRect("TopBar", root);
        HStack(bar.gameObject, 0, 10);
        Layout(bar, prefH: 52);
        var back = NewButton("BackButton", bar, "< 홈", HeaderBg, prefW: 96, prefH: 52, fontSize: 22);
        AddNav(back.gameObject, PhoneNavButton.NavAction.Home, null);
        var titleText = NewText("Title", bar, title, 28, TextMain);
        Layout(titleText, flexW: 1);
        return root;
    }

    private static T NewApp<T>(RectTransform appsRoot, GameObject screen) where T : PhoneAppBase
    {
        var go = NewRect(typeof(T).Name, appsRoot).gameObject;
        var app = go.AddComponent<T>();
        var so = new SerializedObject(app);
        so.FindProperty("screenRoot").objectReferenceValue = screen;
        so.ApplyModifiedPropertiesWithoutUndo();
        return app;
    }

    private static PhoneAppBase BuildStock(RectTransform appsRoot, RectTransform body, List<GameObject> screens)
    {
        var screen = NewScreen("StockScreen", body, "주식", screens);
        var status = NewText("StatusText", screen, "장 상태", 24, TextMain);
        Layout(status, prefH: 36);
        var summary = NewText("SummaryText", screen, "보유 주식 평가액", 22, TextDim);
        Layout(summary, prefH: 32);

        var qtyRow = NewRect("QuantityRow", screen);
        HStack(qtyRow.gameObject, 0, 10);
        Layout(qtyRow, prefH: 48);
        var qtyLabel = NewText("QuantityLabel", qtyRow, "수량", 22, TextDim);
        Layout(qtyLabel, prefW: 70);
        var qty = NewInput("QuantityInput", qtyRow, "1", TMP_InputField.ContentType.IntegerNumber, 48);
        Layout(qty, flexW: 1);

        NewScrollList("StockList", screen, 0f, 1f, out var content);
        var template = NewRowTemplate(content, "RowTemplate", 78f);
        var info = NewInfoBlock(template, 26f, 24, 20);
        NewText("Change", template, "0.0%", 22, TextDim, TextAlignmentOptions.MidlineRight).gameObject.name = "Change";
        Layout(template.Find("Change"), prefW: 90);
        var buy = NewButton("BuyButton", template, "매수", ButtonRed, prefW: 62, prefH: 46, fontSize: 20);
        var sell = NewButton("SellButton", template, "매도", ButtonBlue, prefW: 62, prefH: 46, fontSize: 20);
        template.gameObject.SetActive(false);

        var newsTitle = NewText("NewsTitle", screen, "오늘의 뉴스", 20, TextDim);
        Layout(newsTitle, prefH: 26);
        var news = NewText("NewsText", screen, string.Empty, 20, TextMain);
        Layout(news, prefH: 110);
        var message = NewText("MessageText", screen, string.Empty, 22, TextMain);
        Layout(message, prefH: 56);

        var app = NewApp<StockApplication>(appsRoot, screen.gameObject);
        var view = app.gameObject.AddComponent<StockScreenView>();
        Wire(view, ("app", app), ("statusText", status), ("summaryText", summary), ("newsText", news),
            ("messageText", message), ("quantityInput", qty), ("rowContainer", template.parent),
            ("rowTemplate", template.gameObject));
        return app;
    }

    private static PhoneAppBase BuildBank(RectTransform appsRoot, RectTransform body, List<GameObject> screens)
    {
        var screen = NewScreen("BankScreen", body, "은행", screens);
        var cash = NewText("CashText", screen, "보유 현금", 26, TextMain);
        Layout(cash, prefH: 42);
        var bank = NewText("BankText", screen, "통장 잔액", 26, TextMain);
        Layout(bank, prefH: 42);
        var amount = NewInput("AmountInput", screen, "금액 입력", TMP_InputField.ContentType.IntegerNumber, 56);

        var row = NewRect("Buttons", screen);
        HStack(row.gameObject, 0, 12);
        Layout(row, prefH: 58);
        var deposit = NewButton("DepositButton", row, "입금", ButtonBlue, prefH: 58, flexW: 1);
        var withdraw = NewButton("WithdrawButton", row, "출금", ButtonRed, prefH: 58, flexW: 1);
        var message = NewText("MessageText", screen, string.Empty, 22, TextMain);
        Layout(message, prefH: 60);

        var app = NewApp<BankApplication>(appsRoot, screen.gameObject);
        var view = app.gameObject.AddComponent<BankScreenView>();
        Wire(view, ("app", app), ("cashText", cash), ("bankText", bank), ("messageText", message),
            ("amountInput", amount), ("depositButton", deposit), ("withdrawButton", withdraw));
        return app;
    }

    private static PhoneAppBase BuildRevenue(RectTransform appsRoot, RectTransform body, List<GameObject> screens)
    {
        var screen = NewScreen("RevenueScreen", body, "매출 / 점유율", screens);
        var total = NewText("TotalText", screen, "전체 소비자", 26, TextMain);
        Layout(total, prefH: 42);

        NewScrollList("ShareList", screen, 0f, 1f, out var content);
        var list = NewText("ListText", content, string.Empty, 22, TextMain);

        var eventsTitle = NewText("EventsTitle", screen, "경제 이벤트", 20, TextDim);
        Layout(eventsTitle, prefH: 26);
        var events = NewText("EventsText", screen, string.Empty, 22, TextMain);
        Layout(events, prefH: 100);

        var app = NewApp<RevenueApplication>(appsRoot, screen.gameObject);
        var view = app.gameObject.AddComponent<RevenueScreenView>();
        Wire(view, ("app", app), ("totalText", total), ("listText", list), ("eventsText", events));
        return app;
    }

    private static PhoneAppBase BuildShop(RectTransform appsRoot, RectTransform body, List<GameObject> screens)
    {
        var screen = NewScreen("ShopScreen", body, "상점", screens);
        var cash = NewText("CashText", screen, "보유 현금", 26, TextMain);
        Layout(cash, prefH: 42);

        NewScrollList("ItemList", screen, 0f, 1f, out var content);
        var template = NewRowTemplate(content, "RowTemplate", 78f);
        NewInfoBlock(template, 26f, 24, 20);
        NewButton("BuyButton", template, "구매", ButtonBlue, prefW: 90, prefH: 46, fontSize: 20);
        template.gameObject.SetActive(false);

        var message = NewText("MessageText", screen, string.Empty, 22, TextMain);
        Layout(message, prefH: 60);

        var app = NewApp<ShopApplication>(appsRoot, screen.gameObject);
        var view = app.gameObject.AddComponent<ShopScreenView>();
        Wire(view, ("app", app), ("cashText", cash), ("messageText", message),
            ("rowContainer", template.parent), ("rowTemplate", template.gameObject));
        return app;
    }

    private static PhoneAppBase BuildSns(RectTransform appsRoot, RectTransform body, List<GameObject> screens)
    {
        var screen = NewScreen("SnsScreen", body, "SNS", screens);

        var postRow = NewRect("PostRow", screen);
        HStack(postRow.gameObject, 0, 10);
        Layout(postRow, prefH: 52);
        var input = NewInput("PostInput", postRow, "지금 무슨 생각을 하고 있나요?", TMP_InputField.ContentType.Standard, 52);
        Layout(input, flexW: 1);
        var post = NewButton("PostButton", postRow, "게시", ButtonBlue, prefW: 90, prefH: 52, fontSize: 22);
        var message = NewText("MessageText", screen, string.Empty, 20, TextDim);
        Layout(message, prefH: 30);

        NewScrollList("FeedList", screen, 0f, 1f, out var content);
        var template = NewRowTemplate(content, "RowTemplate", -1f);
        var info = NewRect("Info", template);
        VStack(info.gameObject, 0, 4);
        Layout(info, flexW: 1);
        var author = NewText("Author", info, "작성자", 20, TextDim);
        var bodyText = NewText("Body", info, "내용", 22, TextMain);
        var like = NewButton("LikeButton", template, "좋아요 0", HeaderBg, prefW: 120, prefH: 44, fontSize: 18);
        template.gameObject.SetActive(false);

        var app = NewApp<SNSApplication>(appsRoot, screen.gameObject);
        var view = app.gameObject.AddComponent<SnsScreenView>();
        Wire(view, ("app", app), ("messageText", message), ("postInput", input), ("postButton", post),
            ("rowContainer", template.parent), ("rowTemplate", template.gameObject));
        return app;
    }

    private static PhoneAppBase BuildReport(RectTransform appsRoot, RectTransform body, List<GameObject> screens)
    {
        var screen = NewScreen("ReportScreen", body, "신고", screens);
        var points = NewText("PointsText", screen, "내 벌점", 26, TextMain);
        Layout(points, prefH: 42);
        var type = NewButton("TypeButton", screen, "범죄 유형", HeaderBg, prefH: 52, fontSize: 22);
        var suspect = NewInput("SuspectInput", screen, "신고 대상 ID (예: npc_thief)", TMP_InputField.ContentType.Standard, 52);
        var evidence = NewInput("EvidenceInput", screen, "증거(스크린샷) ID", TMP_InputField.ContentType.Standard, 52);
        var value = NewInput("ValueInput", screen, "훔친 물건 금액 (절도일 때)", TMP_InputField.ContentType.IntegerNumber, 52);
        var submit = NewButton("SubmitButton", screen, "신고하기", ButtonPurple, prefH: 56, fontSize: 24);
        var message = NewText("MessageText", screen, string.Empty, 22, TextMain);
        Layout(message, prefH: 64);
        var historyTitle = NewText("HistoryTitle", screen, "내 신고 내역", 20, TextDim);
        Layout(historyTitle, prefH: 26);
        var history = NewText("HistoryText", screen, string.Empty, 20, TextMain);
        Layout(history, prefH: 130);

        var app = NewApp<ReportApplication>(appsRoot, screen.gameObject);
        var view = app.gameObject.AddComponent<ReportScreenView>();
        Wire(view, ("app", app), ("typeButton", type), ("typeLabel", type.transform.Find("Label").GetComponent<TMP_Text>()),
            ("suspectInput", suspect), ("evidenceInput", evidence), ("valueInput", value), ("submitButton", submit),
            ("messageText", message), ("pointsText", points), ("historyText", history));
        return app;
    }

    // -------------------------------------------------------- row helpers

    private static RectTransform NewRowTemplate(RectTransform content, string name, float prefH)
    {
        var row = NewPanel(name, content, RowBg);
        var hs = HStack(row.gameObject, 10, 8);
        hs.childAlignment = TextAnchor.MiddleLeft;
        if (prefH > 0f) Layout(row, prefH: prefH);
        return (RectTransform)row.transform;
    }

    private static RectTransform NewInfoBlock(RectTransform row, float nameHeight, float nameSize, float priceSize)
    {
        var info = NewRect("Info", row);
        VStack(info.gameObject, 0, 2);
        Layout(info, flexW: 1);
        var name = NewText("Name", info, "이름", nameSize, TextMain);
        Layout(name, prefH: nameHeight);
        var price = NewText("Price", info, "0원", priceSize, TextDim);
        Layout(price, prefH: priceSize + 6f);
        return info;
    }

    // ------------------------------------------------------ generic helpers

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    private static Image NewPanel(string name, Transform parent, Color color)
    {
        var rect = NewRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var rect = NewRect(name, parent);
        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Layout(Component target, float prefH = -1f, float prefW = -1f, float flexH = -1f, float flexW = -1f)
    {
        var element = target.GetComponent<LayoutElement>();
        if (element == null) element = target.gameObject.AddComponent<LayoutElement>();
        if (prefH >= 0f) element.preferredHeight = prefH;
        if (prefW >= 0f) element.preferredWidth = prefW;
        if (flexH >= 0f) element.flexibleHeight = flexH;
        if (flexW >= 0f) element.flexibleWidth = flexW;
    }

    private static VerticalLayoutGroup VStack(GameObject go, int padding, float spacing)
    {
        var group = go.AddComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(padding, padding, padding, padding);
        group.spacing = spacing;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        return group;
    }

    private static HorizontalLayoutGroup HStack(GameObject go, int padding, float spacing)
    {
        var group = go.AddComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(padding, padding, padding, padding);
        group.spacing = spacing;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        group.childAlignment = TextAnchor.MiddleLeft;
        return group;
    }

    private static Button NewButton(string name, Transform parent, string label, Color color,
        float prefW = -1f, float prefH = -1f, float flexW = -1f, float fontSize = 22f)
    {
        var image = NewPanel(name, parent, color);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var text = NewText("Label", image.transform, label, fontSize, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        if (prefW >= 0f || prefH >= 0f || flexW >= 0f) Layout(image, prefH, prefW, -1f, flexW);
        return button;
    }

    private static TMP_InputField NewInput(string name, Transform parent, string placeholder,
        TMP_InputField.ContentType contentType, float prefH)
    {
        var image = NewPanel(name, parent, InputBg);
        image.gameObject.SetActive(false);

        var area = NewRect("TextArea", image.transform);
        Stretch(area, 12f, 6f, 12f, 6f);
        area.gameObject.AddComponent<RectMask2D>();
        var placeholderText = NewText("Placeholder", area, placeholder, 20, TextDim);
        Stretch(placeholderText.rectTransform);
        placeholderText.fontStyle = FontStyles.Italic;
        var text = NewText("Text", area, string.Empty, 22, TextMain);
        Stretch(text.rectTransform);
        text.textWrappingMode = TextWrappingModes.NoWrap;

        var input = image.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = placeholderText;
        input.targetGraphic = image;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = contentType;
        image.gameObject.SetActive(true);

        Layout(image, prefH: prefH);
        return input;
    }

    private static void NewScrollList(string name, Transform parent, float prefH, float flexH, out RectTransform content)
    {
        var rootImage = NewPanel(name, parent, ListBg);
        var scroll = rootImage.gameObject.AddComponent<ScrollRect>();

        var viewport = NewRect("Viewport", rootImage.transform);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        VStack(content.gameObject, 6, 6);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        Layout(rootImage, prefH: prefH, flexH: flexH);
    }

    private static void Wire(Object target, params (string field, Object value)[] references)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in references)
        {
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("Phone UI: field '" + field + "' not found on " + target.GetType().Name);
                continue;
            }
            property.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
