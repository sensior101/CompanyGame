using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

public static class FoodVitalsQA
{
    static Dictionary<string,bool> checks;
    static void Check(string name,bool ok){checks[name]=ok;if(!ok)throw new Exception(name);}
    static async Task Wait(int ms){EditorApplication.QueuePlayerLoopUpdate();await Task.Delay(ms);}
    static ItemData Food(string name)=>AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Art/Items/ConvenienceFood/"+name+".asset");
    public static async Task<string> Run()
    {
        checks=new Dictionary<string,bool>();
        Application.runInBackground=true;EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        var temp=new GameObject("FoodQA");var stats=temp.AddComponent<PlayerStats>();
        string error=null;
        try
        {
            string[] names={"TriangleGimbap","CupRamen","LunchBox","Water","EnergyBar","EnergyDrink","Cola","Chips","Chocolate","Jelly"};
            int[] hp={7,8,10,0,0,0,0,0,0,0},sp={0,0,0,2,7,10,0,0,0,0},sr={0,0,0,0,0,0,2,3,3,3};
            for(int i=0;i<names.Length;i++)
            {
                var food=Food(names[i]);var inv=new InventoryState();inv.TryAdd(food,2,out _);
                stats.RestoreVitals(new Vector3(50,50,50),0);
                Check(names[i]+" exact effect",stats.TryConsume(inv,0)&&stats.health==50+hp[i]&&stats.stamina==50+sp[i]&&stats.stress==50-sr[i]);
                Check(names[i]+" consumes one",inv.GetSlot(0).Count==1);
                Check(names[i]+" last item disappears",stats.TryConsume(inv,0)&&inv.GetSlot(0).IsEmpty&&!stats.TryConsume(inv,0));
                inv.TryAdd(food,1,out _);stats.RestoreVitals(new Vector3(99,99,1),0);
                Check(names[i]+" clamps bounds",stats.TryConsume(inv,0)&&stats.health<=100&&stats.stamina<=100&&stats.stress>=0);
                inv.TryAdd(food,1,out _);stats.RestoreVitals(new Vector3(100,100,0),0);
                Check(names[i]+" no waste when full",!stats.TryConsume(inv,0)&&inv.GetSlot(0).Count==1);
            }
            var money=new InventoryState();money.TryAdd(CashService.GetCurrency(1500),1,out _);
            stats.RestoreVitals(new Vector3(50,50,50),0);
            Check("Currency cannot be eaten",!stats.TryConsume(money,0)&&money.GetSlot(0).Count==1);
            Check("Damage still lowers health",stats.TakeDamage(7,null,Vector3.zero)&&stats.health==43);
            stats.RestoreVitals(new Vector3(100,100,0),0);stats.RecordTravel(23.7f,3,false);
            Check("Walking accumulates fractional effort",stats.stamina==100);
            stats.RecordTravel(.3f,3,false);Check("Eight walking seconds cost one",stats.stamina==99);
            stats.RestoreVitals(new Vector3(100,100,0),0);stats.RecordTravel(18,4.5f,true);
            Check("Four sprinting seconds cost one",stats.stamina==99);
            stats.RecordTravel(0,3,false);Check("No travel no drain",stats.stamina==99);
            stats.RecordTravel(100000,3,false);Check("Stamina never negative",stats.stamina==0);
            UnityEngine.Object.DestroyImmediate(temp);temp=null;
            await Runtime();
        }
        catch(Exception ex){error=ex.ToString();}
        finally{if(temp)UnityEngine.Object.DestroyImmediate(temp);}
        return Newtonsoft.Json.JsonConvert.SerializeObject(new{checks,passed=checks.Values.Count(v=>v),failed=checks.Values.Count(v=>!v),error},Newtonsoft.Json.Formatting.Indented);
    }
    static async Task Runtime()
    {
        var player=UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();var stats=player.GetComponent<PlayerStats>();
        var inventory=player.GetComponent<PlayerInventory>();var state=inventory.Inventory;
        var original=(InventoryState)typeof(InventoryState).GetMethod("Copy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(state,null);
        var vitals=new Vector3(stats.health,stats.stamina,stats.stress);float cost=stats.PendingStaminaCost;
        var home=player.transform.position;var spawn=player.spawn;var rotation=player.transform.rotation;
        var camera=player.viewCamera.GetComponent<PlayerCameraController>();bool cameraEnabled=camera.enabled;var cameraRotation=camera.transform.rotation;
        Keyboard keyboard=null;Mouse mouse=null;
        try
        {
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            typeof(InventoryState).GetMethod("ReplaceWith",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(state,new object[]{new InventoryState()});
            state.TryAdd(Food("Water"),3,out _);state.SelectHotbar(0);stats.RestoreVitals(new Vector3(75,50,30),0);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(40,Screen.height-40)});await Wait(200);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(40,Screen.height-40),buttons=1});await Wait(180);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(40,Screen.height-40)});await Wait(200);
            Check("Real left click consumes water",state.GetSlot(0).Count==2&&stats.stamina==52);
            var bar=inventory.UserInterface.transform.Find("QuickSlots/PlayerStatusBars/Stamina/Track/Fill") as RectTransform;
            Check("Consumption updates HUD",Mathf.Abs(bar.anchorMax.x-.52f)<.001f);
            inventory.OpenInventory();await Wait(200);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(40,Screen.height-40),buttons=1});await Wait(120);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(40,Screen.height-40)});await Wait(120);
            Check("Inventory clicks cannot eat",state.GetSlot(0).Count==2);inventory.CloseInventory();await Wait(300);
            // A long, clear aisle; short samples also verify the fractional progress.
            camera.enabled=false;camera.transform.rotation=Quaternion.identity;
            player.spawn=new Vector3(.5f,.04f,3.1f);player.ResetToSpawn();await Wait(150);
            stats.RestoreVitals(new Vector3(75,100,30),0);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));await Wait(500);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Wait(150);
            float walkCost=stats.PendingStaminaCost;Check("Actual walking drains effort",walkCost>.02f&&walkCost<.15f);
            player.ResetToSpawn();await Wait(100);stats.RestoreVitals(new Vector3(75,100,30),0);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.LeftShift));await Wait(500);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());await Wait(150);
            Check("Actual sprint drains faster",stats.PendingStaminaCost>walkCost*1.4f);
            float stopped=stats.PendingStaminaCost;await Wait(300);Check("Standing costs nothing",Mathf.Approximately(stopped,stats.PendingStaminaCost));
        }
        finally
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
            if(inventory.IsOpen)inventory.CloseInventory();
            typeof(InventoryState).GetMethod("ReplaceWith",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(state,new object[]{original});
            state.SelectHotbar(original.SelectedHotbarIndex);inventory.UserInterface.Refresh();
            stats.RestoreVitals(vitals,cost);player.spawn=home;player.ResetToSpawn();player.spawn=spawn;player.transform.rotation=rotation;
            camera.enabled=cameraEnabled;camera.transform.rotation=cameraRotation;
        }
    }
}
