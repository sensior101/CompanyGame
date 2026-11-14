using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Recipe selection UI. Ingredients remain owned in the existing bag until the atomic craft succeeds.</summary>
[DefaultExecutionOrder(-321)]
[DisallowMultipleComponent]
public sealed class CookingInteraction : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public static bool HasNearbyStation { get; private set; }
    public WorldObject Station { get; private set; }
    public CookingCatalog.Selection Selection { get; private set; }=new();
    PlayerInventory player;PlayerMovement movement;InventoryState bag;
    GameObject window,prompt;TMP_Text title,status,promptText,resultName;
    RectTransform dragRoot;Image dragImage;TMP_Text dragName;InventoryGlyphGraphic flame;
    readonly List<Slot> ingredients=new();Slot output;
    InventoryUI.SelectionView inventoryView;
    List<CookingCatalog.Recipe> matches=new();int choice,dragArea=-1,dragIndex=-1;bool pending,network;
    TMP_FontAsset font;Button previousRecipe,nextRecipe;
    sealed class Slot { public Image icon;public TMP_Text label,count;public InventoryRoundedGraphic background;public InventorySlotPointer pointer; }
    public CookingCatalog.Recipe Result => matches.Count>0?matches[Mathf.Clamp(choice,0,matches.Count-1)]:null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset(){IsOpen=HasNearbyStation=false;}
    void Awake(){player=GetComponent<PlayerInventory>();movement=GetComponent<PlayerMovement>();}
    void OnEnable()=>WorldStateSync.Answered+=Answered;
    void OnDisable(){WorldStateSync.Answered-=Answered;Close();HasNearbyStation=false;if(prompt)prompt.SetActive(false);}
    void Update()
    {
        HasNearbyStation=false;
        if(IsOpen&&Station)
        {
            if(!Station.InInteractionRange(transform)||network!=SharedWriteGate.OnServer||SceneLoadManager.IsLoading||GameInput.CancelPressed||(GameInput.InteractPressed&&!PlayerInventory.SpaceConsumedThisFrame))
            {PlayerInventory.ConsumeSpaceThisFrame();Close();}
            return;
        }
        if(IsOpen){Close();return;}
        if(!movement||!movement.enabled||SceneLoadManager.IsLoading||SeatInteraction.HasNearbySeat||(PlayerSeating.Local&&PlayerSeating.Local.IsSeated)||
            FridgeInteraction.IsOpen||PlayerInventory.IsAnyOpen||ChatUIManager.IsChatting||UIEventSystem.IsEditingText()||DialogueManager.HasNearbyNpc||DialogueManager.OwnsInput||
            (PhoneManager.Instance&&PhoneManager.Instance.IsPhoneOpen)||(PlayerInteraction.Local&&PlayerInteraction.Local.IsInteractionMenuOpen))
        {if(prompt)prompt.SetActive(false);return;}
        var station=WorldObject.FindNearest(transform,FurnitureFunction.Cooking);HasNearbyStation=station;
        if(!station){if(prompt)prompt.SetActive(false);return;}
        ShowPrompt();
        if(GameInput.InteractPressed&&!PlayerInventory.SpaceConsumedThisFrame){PlayerInventory.ConsumeSpaceThisFrame();Open(station);}
    }
    public void Open(WorldObject station)
    {
        if(!station||!station.HasFunction(FurnitureFunction.Cooking)||!station.InInteractionRange(transform)||FridgeInteraction.IsOpen||PlayerInventory.IsAnyOpen||!player.UserInterface)return;
        if(IsOpen)Close();Station=station;bag=player.Inventory;if(bag==null)return;
        IsOpen=true;network=SharedWriteGate.OnServer;pending=false;choice=0;Selection=new();
        if(prompt)prompt.SetActive(false);if(!window)Build();window.SetActive(true);
        title.text="요리";bag.Changed+=BagChanged;status.text="";Refresh();
    }
    public void Close()
    {
        if(bag!=null)bag.Changed-=BagChanged;bag=null;Station=null;IsOpen=false;pending=false;Selection=new();EndSlotDrag();if(window)window.SetActive(false);
    }
    void BagChanged(){Selection=new();choice=0;Refresh();}
    void Answered(string kind,string json,string error)
    {
        if(!IsOpen||kind!="p.cook"||string.IsNullOrEmpty(json))return;
        var args=JsonUtility.FromJson<SharedOpArgs>(json);if(!Station||args.a!=Station.InteractionId)return;
        pending=false;Selection=new();Refresh();status.text=string.IsNullOrEmpty(error)?"요리를 완성했어요.":error;
    }
    int Reserved(int slot){int n=0;foreach(var c in Selection.cells)if(c!=null&&c.slot==slot)n++;return n;}
    ItemData ItemAt(int area,int index)
    {
        if(area==2)return Result?.output;
        int source=area==0?index:Selection.cells[index]?.slot??-1;var stack=bag?.GetSlot(source);
        return stack!=null&&!stack.IsEmpty?stack.Item:null;
    }
    public void BeginSlotDrag(int area,int index,Vector2 position)
    {
        if(!IsOpen||pending)return;var item=ItemAt(area,index);if(!item)return;
        if(area==0&&bag.GetSlot(index).IsSpoiled){status.text="상한 재료로는 요리할 수 없어요.";return;}
        if(area==0&&bag.GetSlot(index).Count<=Reserved(index))return;
        dragArea=area;dragIndex=index;dragImage.sprite=item.icon;dragImage.enabled=item.icon;dragName.text=item.icon?"":item.DisplayName;dragRoot.gameObject.SetActive(true);MoveSlotDrag(position);
    }
    public void MoveSlotDrag(Vector2 position)
    {if(dragArea<0||!dragRoot)return;RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)window.transform,position,null,out var local);dragRoot.anchoredPosition=local;}
    public void EndSlotDrag(){dragArea=dragIndex=-1;if(dragRoot)dragRoot.gameObject.SetActive(false);}
    public void DropSlot(int area,int index)
    {
        if(!IsOpen||pending||dragArea<0)return;
        if(dragArea==2&&area==0)TakeResult(index);
        else if(area==1&&dragArea==0&&Selection.cells[index]==null)
        {var stack=bag.GetSlot(dragIndex);if(stack!=null&&!stack.IsEmpty&&!stack.IsSpoiled&&stack.Count>Reserved(dragIndex))Selection.cells[index]=new(){slot=dragIndex,fingerprint=FurnitureStorageRules.Fingerprint(stack)};}
        else if(area==1&&dragArea==1){var cell=Selection.cells[index];Selection.cells[index]=Selection.cells[dragIndex];Selection.cells[dragIndex]=cell;}
        else if(area==0&&dragArea==1)Selection.cells[dragIndex]=null;
        choice=0;EndSlotDrag();Refresh();
    }
    public void ClickSlot(int area,int index,bool right)
    {if(!IsOpen||pending)return;if(area==2&&!right)TakeResult();else if(area==1){Selection.cells[index]=null;choice=0;Refresh();}}
    public bool TakeResult(int destination=-1)
    {
        if(pending||!Station||!Station.InInteractionRange(transform)||Result==null||bag==null)return false;
        Selection.destination=destination;var recipe=Result;bool ok;string error=null;
        if(network)
        {
            ok=SharedWriteGate.TrySend("p.cook",new SharedOpArgs{a=Station.InteractionId,b=recipe.id,c=CookingCatalog.Serialize(Selection)});pending=ok;
            if(!ok)error="서버에 연결할 수 없어요.";
        }
        else
        {
            ok=CookingCatalog.Current.TryCook(bag,recipe.id,Selection,out error);
            if(ok)FindFirstObjectByType<ChatUIManager>()?.ShowSystemMessage(recipe.output.DisplayName+" 요리를 완성했습니다.");
        }
        Selection.destination=-1;status.text=ok?(pending?"요리 중…":"요리를 완성했어요."):error;Refresh();return ok;
    }
    void Refresh()
    {
        if(!IsOpen||bag==null||!window)return;
        for(int i=0;i<9;i++)Paint(ingredients[i],ItemAt(1,i),Selection.cells[i]==null?0:1);
        inventoryView.Refresh();
        matches=CookingCatalog.Current?CookingCatalog.Current.Match(bag,Selection):new();choice=Mathf.Clamp(choice,0,Mathf.Max(0,matches.Count-1));
        Paint(output,Result?.output,Result==null?0:1);output.label.text="";resultName.text=Result==null?"완성 요리":Result.output.DisplayName;
        flame.color=pending?UITheme.Danger:Result!=null?UITheme.Selected:UITheme.Slate;
        previousRecipe.gameObject.SetActive(matches.Count>1);nextRecipe.gameObject.SetActive(matches.Count>1);

    }
    static void Paint(Slot slot,ItemData item,int count)
    {
        slot.icon.sprite=item?item.icon:null;slot.icon.enabled=item&&item.icon;slot.label.text=item?item.DisplayName:"";
        slot.label.rectTransform.anchoredPosition=slot.icon.enabled?new Vector2(0,-slot.background.rectTransform.sizeDelta.y/2+10):Vector2.zero;
        slot.label.fontSizeMax=slot.icon.enabled?10:14;slot.count.text=count>1?count.ToString():"";slot.background.color=item?UITheme.Ivory:UITheme.Sand;
    }
    TMP_Text Text(Transform parent,string value,float size,Vector2 position,Vector2 dimensions)
    {var t=UITheme.Text(parent,value,size,UITheme.Ink,font,dimensions);t.rectTransform.anchoredPosition=position;t.raycastTarget=false;return t;}
    Slot MakeSlot(Transform parent,string name,int area,int index,Vector2 position,float size)
    {
        var rect=UIBuild.Rect(name,parent,new Vector2(size,size));rect.anchoredPosition=position;
        var bg=UITheme.Rounded(rect,UITheme.Sand,8,1.5f);bg.raycastTarget=true;
        var iconRect=UIBuild.Rect("Icon",rect,new Vector2(size-18,size-24));iconRect.anchoredPosition=new Vector2(0,5);
        var icon=iconRect.gameObject.AddComponent<Image>();icon.preserveAspect=true;icon.raycastTarget=false;
        var label=Text(rect,"",10,new Vector2(0,-size/2+10),new Vector2(size-4,17));label.enableAutoSizing=true;label.fontSizeMin=8;label.fontSizeMax=10;
        var count=Text(rect,"",13,new Vector2(size/2-15,size/2-12),new Vector2(26,20));count.fontStyle=FontStyles.Bold;
        return new Slot{background=bg,icon=icon,label=label,count=count,pointer=InventorySlotPointer.AttachCooking(rect.gameObject,this,area,index)};
    }
    GameObject Canvas(string name,int order)
    {
        var root=new GameObject(name,typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.transform.SetParent(transform,false);
        var c=root.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=order;
        var scale=root.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1280,720);scale.matchWidthOrHeight=.5f;return root;
    }
    void Build()
    {
        font=player.uiFont?player.uiFont:PlayerInteraction.Local?PlayerInteraction.Local.uiFont:null;
        window=Canvas("CookingCanvas",220);
        var card=UITheme.Panel("CookingWindow",window.transform,new Vector2(400,290),UITheme.Ivory);
        card.anchoredPosition=new Vector2(0,93);
        title=Text(card,"요리",21,new Vector2(0,120),new Vector2(240,28));title.fontStyle=FontStyles.Bold;
        for(int i=0;i<9;i++)ingredients.Add(MakeSlot(card,"Ingredient_"+i,1,i,new Vector2(-158+(i%3)*62,56-(i/3)*62),58));
        Text(card,"→",28,new Vector2(19,-6),new Vector2(34,40));output=MakeSlot(card,"CookedResult",2,0,new Vector2(111,-6),78);
        resultName=Text(card,"완성 요리",14,new Vector2(111,-64),new Vector2(148,26));
        previousRecipe=UITheme.Button(card,"‹",new Vector2(24,26),false,font,()=>{choice=(choice+matches.Count-1)%matches.Count;Refresh();});((RectTransform)previousRecipe.transform).anchoredPosition=new Vector2(56,-6);
        nextRecipe=UITheme.Button(card,"›",new Vector2(24,26),false,font,()=>{choice=(choice+1)%matches.Count;Refresh();});((RectTransform)nextRecipe.transform).anchoredPosition=new Vector2(166,-6);
        var fireRect=UIBuild.Rect("CookingFire",card,new Vector2(24,30));fireRect.anchoredPosition=new Vector2(-96,-119);flame=fireRect.gameObject.AddComponent<InventoryGlyphGraphic>();flame.kind=InventoryGlyphGraphic.Glyph.Fire;flame.raycastTarget=false;
        status=Text(card,"",12,new Vector2(66,-119),new Vector2(240,30));
        var close=UITheme.Button(card,"×",new Vector2(28,28),false,font,Close);((RectTransform)close.transform).anchoredPosition=new Vector2(172,120);
        inventoryView=player.UserInterface.CreateSelectionView(window.transform,bag,stack=>!stack.IsSpoiled,_=>{},true,
            (pointer,index)=>InventorySlotPointer.AttachCooking(pointer.gameObject,this,0,index),
            index=>Mathf.Max(0,(bag?.GetSlot(index)?.Count??0)-Reserved(index)));
        inventoryView.Root.anchorMin=inventoryView.Root.anchorMax=inventoryView.Root.pivot=new Vector2(.5f,.5f);
        inventoryView.Root.anchoredPosition=new Vector2(0,-150);
        dragRoot=UIBuild.Rect("CookingDrag",window.transform,new Vector2(52,52));dragImage=dragRoot.gameObject.AddComponent<Image>();dragImage.raycastTarget=false;dragImage.preserveAspect=true;dragName=Text(dragRoot,"",12,Vector2.zero,new Vector2(64,48));dragRoot.gameObject.SetActive(false);
    }
    void ShowPrompt()
    {
        if(!prompt)
        {font=player.uiFont?player.uiFont:PlayerInteraction.Local?PlayerInteraction.Local.uiFont:null;prompt=Canvas("CookingPrompt",196);var panel=UITheme.Panel("Panel",prompt.transform,new Vector2(300,44));panel.anchorMin=panel.anchorMax=new Vector2(.5f,0);panel.anchoredPosition=new Vector2(0,168);promptText=Text(panel,"요리하기  (스페이스바)",18,Vector2.zero,new Vector2(280,40));}
        prompt.SetActive(true);
    }
}
