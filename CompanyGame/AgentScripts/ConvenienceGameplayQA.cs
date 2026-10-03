using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using CompanyGame.World.Maps;

public static class ConvenienceGameplayQA
{
    const string Map="Assets/Scenes/daldongnaemap.unity";
    const string Interior="Assets/Scenes/Interiors/ConvenienceStoreInterior.unity";
    static readonly string Output=Path.GetFullPath("../ArtSource/ConvenienceInterior/DirectPurchaseQA");
    static readonly Dictionary<string,bool> checks=new Dictionary<string,bool>();
    static Keyboard keyboard;
    static Mouse mouse;
    static void Check(string name,bool ok){checks[name]=ok;if(!ok)throw new Exception("QA failed: "+name);}
    static ItemData Product(string name)=>AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Art/Items/ConvenienceFood/"+name+".asset");
    static PlayerMovement Player()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerMovement>(false)).Single();
    static async Task Pause(int ms=140){EditorApplication.QueuePlayerLoopUpdate();await Task.Delay(ms);}
    static void Key(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
    static async Task Tap(Key key){Key(key);await Pause();Key();await Pause(250);}
    static void Teleport(Vector3 p){var player=Player();player.spawn=p;player.ResetToSpawn();}
    static async Task Screenshot(string name){ScreenCapture.CaptureScreenshot(Path.Combine(Output,name+".png"));await Pause(250);}
    static StoreTradePointer Pointer(StoreTradeUI ui,StoreTradePointer.Kind kind,int index)=>ui.GetComponentsInChildren<StoreTradePointer>().Single(p=>p.kind==kind&&p.index==index);
    static Vector2 Position(StoreTradeUI ui,StoreTradePointer.Kind kind,int index)=>RectTransformUtility.WorldToScreenPoint(null,Pointer(ui,kind,index).transform.position);
    static async Task MouseAt(Vector2 p,bool held=false,bool right=false){InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=(ushort)(held?(right?2:1):0)});await Pause();}
    static async Task DragBetween(Vector2 from,Vector2 to,bool right=false)
    {await MouseAt(from);await MouseAt(from,true,right);await MouseAt(from+new Vector2(16,2),true,right);await MouseAt(to,true,right);await MouseAt(to,false,right);}
    static async Task Pick(StoreTradeUI ui,int slot)
    {var a=Position(ui,StoreTradePointer.Kind.Inventory,slot);await MouseAt(a);await MouseAt(a,true);await MouseAt(a+new Vector2(16,2),true);}
    static async Task ReleaseOn(StoreTradeUI ui,StoreTradePointer.Kind kind,int index)
    {var p=Position(ui,kind,index);await MouseAt(p,true);await MouseAt(p);}
    static async Task Place(StoreTradeUI ui,int slot)
    {var p=Position(ui,StoreTradePointer.Kind.Inventory,slot);await MouseAt(p);await MouseAt(p,true);await MouseAt(p);}
    static UnityEngine.UI.Image Ghost(StoreTradeUI ui)=>ui.transform.Find("DragGhost").GetComponent<UnityEngine.UI.Image>();
    static string Write(string name,string error=null)
    {Directory.CreateDirectory(Output);var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{checks,passed=checks.Values.Count(v=>v),failed=checks.Values.Count(v=>!v),error},Newtonsoft.Json.Formatting.Indented);File.WriteAllText(Path.Combine(Output,name),json);return json;}

    public static string Transactions()
    {
        checks.Clear();var rice=Product("TriangleGimbap");var cash=CashService.GetCurrency(1500);
        var inv=new InventoryState();inv.TryAdd(cash,3,out _);
        var session=new StoreTradeSession(inv,new[]{new StoreOffer{item=rice,price=1500}});
        for(int i=0;i<3;i++)
        {
            Check("Take product "+i,session.TryTakeOffer(0,out _) && session.CursorItem==rice && inv.GetSlot(0).Count==2-i);
            Check("Invalid offer while carrying "+i,!session.TryTakeOffer(17,out _));
            Check("Place and stack product "+i,session.TryPlace(7,out _) && inv.GetSlot(7).Count==i+1 && !session.HasCursorItem);
        }
        Check("No fourth purchase without money",!session.TryTakeOffer(0,out _) && inv.GetSlot(7).Count==3);
        Check("Invalid offer rejected",!session.TryTakeOffer(17,out _));
        inv.TryAdd(CashService.GetCurrency(500),3,out _);
        Check("Requires matching currency item",!session.TryTakeOffer(0,out _) && !session.HasCursorItem);
        inv.TryAdd(cash,1,out _);session.TryTakeOffer(0,out _);session.CancelPending();
        Check("Cancelled purchase refunds exactly one",CashService.CarriedTotal(inv)==3000 && inv.GetSlot(7).Count==3);
        session.CancelPending();Check("Cancel cannot duplicate refund",CashService.CarriedTotal(inv)==3000);
        session.TryTakeOffer(0,out _);
        Check("Cannot overwrite other item",!session.TryPlace(0,out _) && session.CursorItem==rice);session.CancelPending();
        session.TryPickUp(7,out _,true);Check("Right inventory drag takes one",inv.GetSlot(7).Count==2);session.TryPlace(8,out _);
        Check("Inventory move preserves total",inv.GetSlot(7).Count==2 && inv.GetSlot(8).Count==1);
        session.TryPickUp(7,out _);Check("Left drag holds entire stack",inv.GetSlot(7).IsEmpty && session.CursorCount==2);session.TryPlace(8,out _);
        Check("Entire stack merges",inv.GetSlot(8).Count==3 && !session.HasCursorItem);
        session.TryPickUp(8,out _);session.CancelPending();Check("Cancel restores entire stack",inv.GetSlot(8).Count==3);
        inv.TryAdd(rice,17,out _);session.TryPickUp(8,out _);Check("Twenty stack cursor",session.CursorCount==20);session.TryPlace(9,out _);
        inv.TryAdd(rice,4,out _);session.TryPickUp(2,out _);session.CancelPending();
        var direct=new InventoryState();direct.TryAdd(rice,4,out _);
        Check("Normal inventory single split",direct.TryMoveAmount(0,1,1,out _) && direct.GetSlot(0).Count==3 && direct.GetSlot(1).Count==1);
        Check("Normal inventory whole merge",direct.TryMoveAmount(0,1,3,out _) && direct.GetSlot(0).IsEmpty && direct.GetSlot(1).Count==4);
        var batchInv=new InventoryState();batchInv.TryAdd(cash,25,out _);
        var batch=new StoreTradeSession(batchInv,session.Offers);
        for(int i=0;i<20;i++)Check("Batch click "+i,batch.TryTakeOffer(0,out _) && batch.CursorCount==i+1);
        Check("Stack limit stops further payment",!batch.TryTakeOffer(0,out _) && batchInv.GetSlot(0).Count==5);
        batch.CancelPending();Check("Cancel refunds all twenty",batchInv.GetSlot(0).Count==25);
        batchInv.TryAdd(rice,19,out _);for(int i=0;i<3;i++)batch.TryTakeOffer(0,out _);
        batch.TryPlace(1,out _);Check("Partial placement leaves two on cursor",batchInv.GetSlot(1).Count==20 && batch.CursorCount==2);
        batch.CancelPending();Check("Only unplaced units refunded",batchInv.GetSlot(0).Count==24 && batchInv.GetSlot(1).Count==20);
        return Write("transactions.json");
    }

    public static async Task<string> Runtime()
    {
        if(!Application.isPlaying || SceneManager.GetActiveScene().path!=Map)throw new Exception("Start Play Mode in daldongnaemap.");
        Directory.CreateDirectory(Output);checks.Clear();bool background=Application.runInBackground;
        var originalKeyboard=Keyboard.current;var originalMouse=Mouse.current;
        try
        {
            Application.runInBackground=true;EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            keyboard=InputSystem.AddDevice<Keyboard>("CursorTradeQAKeyboard");mouse=InputSystem.AddDevice<Mouse>("CursorTradeQAMouse");
            Check("Load interior",SceneLoadManager.TryLoadMap(Interior,"store_entry",Player()));
            for(int i=0;i<100 && (SceneManager.GetActiveScene().path!=Interior||SceneLoadManager.IsLoading);i++)await Pause(100);
            await Pause(400);var inv=Player().GetComponent<PlayerInventory>().Inventory;
            Check("Fresh inventory",Enumerable.Range(0,inv.Capacity).All(i=>inv.GetSlot(i).IsEmpty));
            inv.TryAdd(CashService.GetCurrency(1500),3,out _);inv.TryAdd(CashService.GetCurrency(2000),1,out _);
            Teleport(new Vector3(-2.02f,.06f,4.85f));typeof(PlayerCameraController).GetField("yaw",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(Player().viewCamera.GetComponent<PlayerCameraController>(),-90f);
            await Pause(400);await Tap(UnityEngine.InputSystem.Key.Space);var interaction=Player().GetComponent<PlayerInteraction>();var ui=interaction.StoreUI;
            Check("Trade opens with ten foods",ui && ui.Session.Offers.Length==10);
            var pay=Pointer(ui,StoreTradePointer.Kind.Payment,0);var product=Pointer(ui,StoreTradePointer.Kind.Output,0);
            Check("Payment has no visible slot",pay.GetComponent<UnityEngine.UI.Image>().color.a==0 && pay.transform.Find("Recess").GetComponent<UnityEngine.UI.Image>().color.a==0);
            Check("Product has dark gray slot",product.transform.Find("Recess").GetComponent<UnityEngine.UI.Image>().color.r<.3f);
            await Screenshot("01_DirectPurchaseLayout");
            for(int i=0;i<3;i++)
            {
                var a=Position(ui,StoreTradePointer.Kind.Output,0);await MouseAt(a);await MouseAt(a,true);await MouseAt(a+new Vector2(16,2),true);
                Check("Product drag deducts one coin "+i,inv.GetSlot(0).Count==2-i && ui.Session.CursorItem==Product("TriangleGimbap"));
                Check("Cursor immediately carries rice "+i,Ghost(ui).sprite==Product("TriangleGimbap").icon);
                Check("Offer stays available visually "+i,product.transform.Find("Recess/Icon").GetComponent<UnityEngine.UI.Image>().sprite==Product("TriangleGimbap").icon);
                if(i==0){await MouseAt(Position(ui,StoreTradePointer.Kind.Inventory,7)+new Vector2(0,80),true);await Screenshot("02_DragProductCoinDeducted");}
                await ReleaseOn(ui,StoreTradePointer.Kind.Inventory,7);
                Check("Direct drop stacks rice "+i,!ui.IsDragging && inv.GetSlot(7).Item==Product("TriangleGimbap") && inv.GetSlot(7).Count==i+1);
            }
            Check("Three rice purchased for three coins",inv.GetSlot(0).IsEmpty && inv.GetSlot(7).Count==3);
            await Screenshot("03_ThreeRicePurchased");
            var start=Position(ui,StoreTradePointer.Kind.Output,0);await MouseAt(start);await MouseAt(start,true);await MouseAt(start+new Vector2(16,2),true);
            Check("No money blocks dragging product",!ui.IsDragging && inv.GetSlot(7).Count==3);await MouseAt(start);
            start=Position(ui,StoreTradePointer.Kind.Output,2);await MouseAt(start);await MouseAt(start,true);await MouseAt(start+new Vector2(16,2),true);
            Check("Other currency cannot buy expensive product",!ui.IsDragging && inv.GetSlot(1).Count==1);await MouseAt(start);
            start=Position(ui,StoreTradePointer.Kind.Payment,1);await MouseAt(start);await MouseAt(start,true);await MouseAt(start+new Vector2(16,2),true);
            Check("Payment preview cannot be taken",!ui.IsDragging);await MouseAt(start);
            inv.TryAdd(Product("TriangleGimbap"),1,out _);
            await Pick(ui,7);
            Check("Trade inventory left drag holds all four",ui.Session.CursorCount==4 && inv.GetSlot(7).IsEmpty && Ghost(ui).GetComponentInChildren<TMPro.TMP_Text>().text=="4");
            await ReleaseOn(ui,StoreTradePointer.Kind.Inventory,6);
            Check("Trade inventory left moves four",inv.GetSlot(6).Count==4 && !ui.IsDragging);
            await DragBetween(Position(ui,StoreTradePointer.Kind.Inventory,6),Position(ui,StoreTradePointer.Kind.Inventory,5),true);
            Check("Trade inventory right splits one",inv.GetSlot(6).Count==3 && inv.GetSlot(5).Count==1 && !ui.IsDragging);
            await Screenshot("04_TradeRightSplit");
            await DragBetween(Position(ui,StoreTradePointer.Kind.Inventory,5),Position(ui,StoreTradePointer.Kind.Inventory,6),true);
            Check("Trade inventory right merges one",inv.GetSlot(6).Count==4 && inv.GetSlot(5).IsEmpty);
            await DragBetween(Position(ui,StoreTradePointer.Kind.Inventory,6),Position(ui,StoreTradePointer.Kind.Inventory,7));
            start=Position(ui,StoreTradePointer.Kind.Output,1);await MouseAt(start);await MouseAt(start,true);await MouseAt(start+new Vector2(16,2),true);
            Check("Last payment disappears when taking ramen",inv.GetSlot(1).IsEmpty && ui.Session.CursorItem==Product("CupRamen"));
            await MouseAt(new Vector2(30,30));await Tap(UnityEngine.InputSystem.Key.Escape);
            Check("Cancelled drag refunds payment",!interaction.IsStoreOpen && inv.GetSlot(1).Count==1 && inv.GetSlot(7).Count==4);
            Check("Controls restored",Player().enabled);
            var owner=Player().GetComponent<PlayerInventory>();Check("Open normal inventory",owner.OpenInventory());await Pause(250);
            Func<int,Vector2> normalPosition=i=>RectTransformUtility.WorldToScreenPoint(null,owner.UserInterface.GetComponentsInChildren<InventorySlotPointer>().Single(p=>!p.IsEquipment&&!p.IsDropZone&&p.InventoryIndex==i&&p.name.StartsWith("StorageSlot_")).transform.position);
            await DragBetween(normalPosition(7),normalPosition(6));
            Check("Normal inventory left moves all four",inv.GetSlot(7).IsEmpty&&inv.GetSlot(6).Count==4);
            await DragBetween(normalPosition(6),normalPosition(5),true);
            Check("Normal inventory right splits one",inv.GetSlot(6).Count==3&&inv.GetSlot(5).Count==1);
            await Screenshot("05_NormalRightSplit");
            await DragBetween(normalPosition(5),normalPosition(6),true);
            Check("Normal inventory right merges one",inv.GetSlot(6).Count==4&&inv.GetSlot(5).IsEmpty);
            owner.CloseInventory();
            return Write("runtime.json");
        }
        catch(Exception e){Write("runtime.json",e.ToString());throw;}
        finally
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
            if(originalKeyboard!=null&&originalKeyboard.added)originalKeyboard.MakeCurrent();if(originalMouse!=null&&originalMouse.added)originalMouse.MakeCurrent();Application.runInBackground=background;
        }
    }
    public static Task<string> CatalogRuntime()=>Runtime();
    public static async Task<string> RepeatClickRuntime()
    {
        if(!Application.isPlaying || SceneManager.GetActiveScene().path!=Map)throw new Exception("Start Play Mode in daldongnaemap.");
        Directory.CreateDirectory(Output);checks.Clear();bool background=Application.runInBackground;
        var originalKeyboard=Keyboard.current;var originalMouse=Mouse.current;
        try
        {
            Application.runInBackground=true;EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            keyboard=InputSystem.AddDevice<Keyboard>("RepeatClickKeyboard");mouse=InputSystem.AddDevice<Mouse>("RepeatClickMouse");
            Check("Load interior",SceneLoadManager.TryLoadMap(Interior,"store_entry",Player()));
            for(int i=0;i<100 && (SceneManager.GetActiveScene().path!=Interior||SceneLoadManager.IsLoading);i++)await Pause(100);
            await Pause(400);var inv=Player().GetComponent<PlayerInventory>().Inventory;inv.TryAdd(CashService.GetCurrency(1500),4,out _);
            Teleport(new Vector3(-2.02f,.06f,4.85f));typeof(PlayerCameraController).GetField("yaw",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(Player().viewCamera.GetComponent<PlayerCameraController>(),-90f);
            await Pause(300);await Tap(UnityEngine.InputSystem.Key.Space);var interaction=Player().GetComponent<PlayerInteraction>();var ui=interaction.StoreUI;
            var p=Position(ui,StoreTradePointer.Kind.Output,0);
            for(int i=0;i<3;i++)
            {
                await MouseAt(p);await MouseAt(p,true);await MouseAt(p);
                Check("Click adds exactly one "+i,ui.Session.CursorCount==i+1 && inv.GetSlot(0).Count==3-i && inv.GetSlot(7).IsEmpty);
            }
            Check("Cursor quantity label is three",Ghost(ui).GetComponentInChildren<TMPro.TMP_Text>().text=="3");
            await MouseAt(p+new Vector2(100,0));await Screenshot("06_ThreeClicksOnCursor");
            await Place(ui,7);Check("Place entire clicked batch",inv.GetSlot(7).Count==3 && !ui.IsDragging && inv.GetSlot(0).Count==1);
            await DragBetween(p,Position(ui,StoreTradePointer.Kind.Inventory,7));
            Check("Existing drag purchase charges only once",inv.GetSlot(7).Count==4 && inv.GetSlot(0).IsEmpty && !ui.IsDragging);
            await MouseAt(p);await MouseAt(p,true);await MouseAt(p);Check("No money prevents extra clicked item",!ui.IsDragging && inv.GetSlot(7).Count==4);
            inv.TryAdd(CashService.GetCurrency(1500),3,out _);
            for(int i=0;i<2;i++){await MouseAt(p);await MouseAt(p,true);await MouseAt(p);}
            var other=Position(ui,StoreTradePointer.Kind.Output,8);await MouseAt(other);await MouseAt(other,true);await MouseAt(other);
            Check("Other product cannot mix into cursor batch",ui.Session.CursorItem==Product("TriangleGimbap") && ui.Session.CursorCount==2 && inv.GetSlot(0).Count==1);
            await Tap(UnityEngine.InputSystem.Key.Escape);
            Check("Close refunds entire unplaced batch",!interaction.IsStoreOpen && inv.GetSlot(0).Count==3 && inv.GetSlot(7).Count==4);
            return Write("repeat-click-runtime.json");
        }
        catch(Exception e){Write("repeat-click-runtime.json",e.ToString());throw;}
        finally
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
            if(originalKeyboard!=null&&originalKeyboard.added)originalKeyboard.MakeCurrent();if(originalMouse!=null&&originalMouse.added)originalMouse.MakeCurrent();Application.runInBackground=background;
        }
    }
}
