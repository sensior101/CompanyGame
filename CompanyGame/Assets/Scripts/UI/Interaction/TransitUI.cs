using System;
using System.Collections.Generic;
using CompanyGame.World.Maps;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Scene-local transport prompt and destination picker, owned by PlayerInteraction.</summary>
public sealed class TransitUI : MonoBehaviour
{
    TMP_FontAsset font;
    GameObject prompt;
    GameObject modal;
    RectTransform cards;
    TMP_Text promptLabel;
    TMP_Text title;
    TMP_Text subtitle;
    TMP_Text status;
    UnityEngine.UI.Button close;
    RectTransform vehiclePopup;
    TMP_Text vehicleOwnerText, vehicleAccessText, vehicleLockText;
    UnityEngine.UI.Button vehicleLockButton;
    WorldDroppedItem menuVehicle;
    GameObject ridingHints;
    Action closeVehicleMenu;
    public bool IsVehicleMenuOpen => vehiclePopup && vehiclePopup.gameObject.activeSelf;
    public WorldDroppedItem MenuVehicle => menuVehicle;
    readonly List<UnityEngine.UI.Button> destinationButtons = new List<UnityEngine.UI.Button>();
    static readonly Color Ink = new Color(.075f, .12f, .17f);
    static readonly Color Paper = new Color(.94f, .96f, .97f);
    static readonly Color Mint = new Color(.3f, .88f, .72f);

    public static TransitUI Create(TMP_FontAsset font, Action onClose)
    {
        var host = new GameObject("TransitCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        var ui = host.AddComponent<TransitUI>();
        ui.font = font ? font : TMP_Settings.defaultFontAsset;
        ui.Build(onClose);
        return ui;
    }

    void Build(Action onClose)
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = .5f;

        var promptRect = Panel("BoardingPrompt", transform, new Vector2(342f, 62f), Ink);
        prompt = promptRect.gameObject;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(.5f, 0f);
        promptRect.pivot = new Vector2(.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 148f);
        var stripe = Panel("Accent", promptRect, new Vector2(5f, 42f), Mint);
        stripe.anchoredPosition = new Vector2(-164f, 0f);
        promptLabel = Label("Action", promptRect, "", 23f, Paper, new Vector2(214f, 52f));
        promptLabel.rectTransform.anchoredPosition = new Vector2(-44f, 0f);
        var cap = Panel("SpaceKeycap", promptRect, new Vector2(88f, 34f), new Color(.86f, .91f, .94f));
        cap.anchoredPosition = new Vector2(110f, 0f);
        var key = Label("Space", cap, "SPACE", 16f, Ink, new Vector2(88f, 32f));
        key.fontStyle = FontStyles.Bold;
        var capEdge = Panel("KeycapEdge", cap, new Vector2(74f, 2f), new Color(.4f, .5f, .57f));
        capEdge.anchoredPosition = new Vector2(0f, -13f);
        prompt.SetActive(false);

        var shade = Panel("TransitModal", transform, Vector2.zero, new Color(.015f, .025f, .04f, .78f));
        UIBuild.Stretch(shade);
        shade.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        modal = shade.gameObject;
        var panel = Panel("DestinationWindow", shade, new Vector2(586f, 636f), Ink);
        panel.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        var topLine = Panel("HeaderAccent", panel, new Vector2(530f, 3f), Mint);
        topLine.anchoredPosition = new Vector2(0f, 296f);
        title = Label("Title", panel, "", 30f, Paper, new Vector2(430f, 46f));
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.rectTransform.anchoredPosition = new Vector2(-48f, 257f);
        subtitle = Label("Subtitle", panel, "", 17f, new Color(.68f, .76f, .82f), new Vector2(530f, 42f));
        subtitle.alignment = TextAlignmentOptions.MidlineLeft;
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, 207f);

        var closeRect = Panel("Close", panel, new Vector2(68f, 40f), new Color(.19f, .25f, .3f));
        closeRect.anchoredPosition = new Vector2(231f, 257f);
        close = closeRect.gameObject.AddComponent<UnityEngine.UI.Button>();
        close.onClick.AddListener(() => onClose());
        closeRect.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        Label("Label", closeRect, "닫기", 17f, Paper, new Vector2(68f, 40f));

        cards = UIBuild.Rect("Destinations", panel, new Vector2(530f, 400f));
        cards.anchoredPosition = new Vector2(0f, -17f);
        var layout = cards.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        status = Label("Status", panel, "목적지를 선택하세요.   ESC  닫기", 16f,
            new Color(.72f, .79f, .83f), new Vector2(530f, 62f));
        status.rectTransform.anchoredPosition = new Vector2(0f, -270f);
        status.textWrappingMode = TextWrappingModes.Normal;
        modal.SetActive(false);
    }

    public void ShowPrompt(string label, bool compact = false)
    {
        var rect=(RectTransform)prompt.transform;
        rect.sizeDelta=compact ? new Vector2(258,52) : new Vector2(342,62);
        var accent=(RectTransform)rect.Find("Accent");accent.anchoredPosition=new Vector2(compact?-120:-164,0);
        promptLabel.rectTransform.sizeDelta=new Vector2(compact?134:214,52);
        promptLabel.rectTransform.anchoredPosition=new Vector2(-44,0);
        ((RectTransform)rect.Find("SpaceKeycap")).anchoredPosition=new Vector2(compact?72:110,0);
        promptLabel.text = label;
        prompt.SetActive(true);
    }

    public void HidePrompt() { if (prompt) prompt.SetActive(false); }
    public void HideModal() { if (modal) modal.SetActive(false); }

    public void SetRidingHints(bool visible)
    {
        if(visible && !ridingHints)
        {
            var rect=Panel("VehicleRidingHints",transform,new Vector2(190,72),new Color(.045f,.065f,.075f,.86f));
            rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.zero;
            rect.anchoredPosition=new Vector2(22,22);
            var label=Label("Controls",rect,"Shift : 가속\nSpace : 내리기",20,Paper,new Vector2(162,58));
            label.alignment=TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode=TextWrappingModes.Normal;
            ridingHints=rect.gameObject;
        }
        if(ridingHints)ridingHints.SetActive(visible);
    }

    public void ShowVehicleMenu(WorldDroppedItem vehicle, Vector2 screenPosition, Action onClose)
    {
        closeVehicleMenu=onClose;menuVehicle=vehicle;
        if(!vehiclePopup)
        {
            vehiclePopup=Panel("VehiclePermissions",transform,new Vector2(310,232),Ink);
            vehiclePopup.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
            vehiclePopup.anchorMin=vehiclePopup.anchorMax=new Vector2(.5f,.5f);vehiclePopup.pivot=new Vector2(0,1);
            // Children keep centered anchors; the popup alone follows the click.
            var heading=Label("Title",vehiclePopup,"탈것 권한 설정",23,Paper,new Vector2(244,36));
            heading.rectTransform.anchoredPosition=new Vector2(-15,88);
            vehicleOwnerText=Label("Owner",vehiclePopup,"",16,Paper,new Vector2(270,32));
            vehicleOwnerText.rectTransform.anchoredPosition=new Vector2(0,48);
            vehicleAccessText=Label("Access",vehiclePopup,"",16,new Color(.7f,.79f,.82f),new Vector2(278,38));
            vehicleAccessText.rectTransform.anchoredPosition=new Vector2(0,12);
            var toggle=Panel("ToggleVehicleLock",vehiclePopup,new Vector2(270,42),new Color(.18f,.31f,.33f));
            toggle.anchoredPosition=new Vector2(0,-36);toggle.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
            vehicleLockButton=toggle.gameObject.AddComponent<UnityEngine.UI.Button>();
            vehicleLockButton.onClick.AddListener(()=>{
                if(menuVehicle)menuVehicle.TrySetVehicleLocked(GameSession.LocalPlayerName,!menuVehicle.VehicleLocked);
                RefreshVehicleMenu();
            });
            vehicleLockText=Label("Label",toggle,"",19,Paper,new Vector2(266,40));
            var note=Label("Hint",vehiclePopup,"자전거 우클릭 : 해체 · F : 줍기",15,new Color(.7f,.79f,.82f),new Vector2(280,30));
            note.rectTransform.anchoredPosition=new Vector2(0,-87);
            var exit=Panel("Close",vehiclePopup,new Vector2(28,28),new Color(.2f,.27f,.3f));exit.anchoredPosition=new Vector2(132,88);
            exit.GetComponent<UnityEngine.UI.Image>().raycastTarget=true;
            exit.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(()=>closeVehicleMenu?.Invoke());
            Label("Label",exit,"X",20,Paper,new Vector2(28,28));
        }
        vehiclePopup.gameObject.SetActive(true);vehiclePopup.SetAsLastSibling();
        var canvasRect=(RectTransform)transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screenPosition,null,out var point);
        var bounds=canvasRect.rect;var size=vehiclePopup.sizeDelta;
        float x=point.x+12, y=point.y-12;
        if(x+size.x>bounds.xMax-8)x=point.x-size.x-12;
        if(y-size.y<bounds.yMin+8)y=point.y+size.y+12;
        vehiclePopup.anchoredPosition=new Vector2(Mathf.Clamp(x,bounds.xMin+8,bounds.xMax-size.x-8),Mathf.Clamp(y,bounds.yMin+size.y+8,bounds.yMax-8));
        RefreshVehicleMenu();
    }

    public void RefreshVehicleMenu()
    {
        if(!IsVehicleMenuOpen || !menuVehicle)return;
        string owner=menuVehicle.VehicleOwner;
        vehicleOwnerText.text="소유자 : <noparse>"+(owner??"미등록")+"</noparse>";
        vehicleAccessText.text=menuVehicle.VehicleLocked?"다른 플레이어 탑승 : 허용 안 함":"다른 플레이어 탑승 : 허용";
        vehicleLockButton.interactable=owner==GameSession.LocalPlayerName && !menuVehicle.IsOccupied;
        vehicleLockText.text=owner==null?"손에 들고 Space로 소유권 등록":owner!=GameSession.LocalPlayerName?"소유자만 권한 변경 가능":menuVehicle.VehicleLocked?"잠금 해제":"잠금";
    }
    public bool PointerInVehicleMenu(Vector2 point) => IsVehicleMenuOpen && RectTransformUtility.RectangleContainsScreenPoint(vehiclePopup,point,null);
    public void HideVehicleMenu()
    {
        if(vehiclePopup)vehiclePopup.gameObject.SetActive(false);
        menuVehicle=null;
    }

    public void ShowDestinations(TransitKind kind, string currentName,
        IReadOnlyList<TransitDestination> destinations, Action<TransitDestination> onChoose)
    {
        HidePrompt();
        foreach (Transform child in cards)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        destinationButtons.Clear();
        title.text = kind == TransitKind.Subway ? "지하철로 이동" : "버스로 이동";
        subtitle.text = "현재 위치  " + currentName;
        status.text = destinations.Count == 0 ? "현재 이동할 수 있는 지구가 없습니다." : "목적지를 선택하세요.   ESC  닫기";
        modal.SetActive(true);
        foreach (var destination in destinations)
        {
            var selectedDestination = destination;
            var row = Panel("Destination_" + destination.displayName, cards, new Vector2(530f, 70f), new Color(.15f, .21f, .27f));
            var element = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.preferredHeight = 70f;
            var button = row.gameObject.AddComponent<UnityEngine.UI.Button>();
            row.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.65f, 1f, .89f);
            colors.selectedColor = new Color(.65f, 1f, .89f);
            colors.pressedColor = new Color(.45f, .78f, .69f);
            colors.disabledColor = new Color(.55f, .6f, .64f);
            button.colors = colors;
            button.onClick.AddListener(() => onChoose(selectedDestination));
            var name = Label("Name", row, destination.displayName, 22f, Paper, new Vector2(438f, 42f));
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.rectTransform.anchoredPosition = new Vector2(-23f, 12f);
            var detail = Label("Description", row, destination.description, 14f,
                new Color(.7f, .8f, .86f), new Vector2(438f, 23f));
            detail.alignment = TextAlignmentOptions.MidlineLeft;
            detail.rectTransform.anchoredPosition = new Vector2(-23f, -17f);
            var arrow = Label("Arrow", row, ">", 24f, Mint, new Vector2(32f, 50f));
            arrow.rectTransform.anchoredPosition = new Vector2(230f, 0f);
            destinationButtons.Add(button);
        }
        WireNavigation();
        SetInteractable(false);
    }

    public void SetInteractable(bool enabled)
    {
        foreach (var button in destinationButtons) button.interactable = enabled;
        close.interactable = enabled;
    }

    public void FocusFirstDestination()
    {
        if (!EventSystem.current) return;
        EventSystem.current.SetSelectedGameObject(destinationButtons.Count > 0
            ? destinationButtons[0].gameObject : close.gameObject);
    }

    public void ShowStatus(string message) { status.text = message; }

    void WireNavigation()
    {
        for (int i = 0; i < destinationButtons.Count; i++)
        {
            var navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit };
            navigation.selectOnUp = i == 0 ? close : destinationButtons[i - 1];
            navigation.selectOnDown = i == destinationButtons.Count - 1 ? close : destinationButtons[i + 1];
            destinationButtons[i].navigation = navigation;
        }
        var closeNavigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit };
        if (destinationButtons.Count > 0)
        {
            closeNavigation.selectOnDown = destinationButtons[0];
            closeNavigation.selectOnUp = destinationButtons[destinationButtons.Count - 1];
        }
        close.navigation = closeNavigation;
    }


    static RectTransform Panel(string name, Transform parent, Vector2 size, Color color)
    {
        var rect = UIBuild.Rect(name, parent, size);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    TMP_Text Label(string name, Transform parent, string value, float size, Color color, Vector2 dimensions)
    {
        var rect = UIBuild.Rect(name, parent, dimensions);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.text = value;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }
}
