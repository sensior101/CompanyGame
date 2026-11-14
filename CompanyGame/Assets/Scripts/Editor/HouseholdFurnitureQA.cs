using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using QaPeer = SyncLayerQA.QaPeer;

/// <summary>Editor-only checks against the existing inventory, seating and server ledger. Creates no runtime service.</summary>
public static class HouseholdFurnitureQA
{
    const string Root = "Assets/Resources/Inventory/Furniture/Household";
    const string Report = "../ArtSource/Items/Furniture/HouseholdLowPoly/UnityIntegration/";
    static int checks;
    const string CookingReport="../ArtSource/Items/Furniture/HouseholdLowPoly/CookingIntegration/";
    static CookingCatalog.Selection FillRecipe(InventoryState bag,CookingCatalog.Recipe recipe,int multiplier=1)
    {
        foreach(var ingredient in recipe.ingredients)bag.TryAdd(ingredient.item,ingredient.count*multiplier,out _);
        var selection=new CookingCatalog.Selection();int cell=0;
        foreach(var ingredient in recipe.ingredients)
        {
            int slot=Enumerable.Range(0,bag.Capacity).First(i=>bag.GetSlot(i).Item==ingredient.item);
            for(int n=0;n<ingredient.count;n++)selection.cells[cell++]=new(){slot=slot,fingerprint=FurnitureStorageRules.Fingerprint(bag.GetSlot(slot))};
        }
        return selection;
    }
    static int Count(InventoryState bag,ItemData item)=>Enumerable.Range(0,bag.Capacity).Sum(i=>bag.GetSlot(i).Item==item?bag.GetSlot(i).Count:0);
    public static string RunCooking()
    {
        checks=0;var catalog=CookingCatalog.Current;Check(catalog&&catalog.recipes.Length==135,"135 existing recipes");Check(catalog.allowUnlockRecipes,"all recipes enabled");
        foreach(var recipe in catalog.recipes)
        {
            var bag=new InventoryState();var selection=FillRecipe(bag,recipe);
            Check(catalog.Match(bag,selection).Contains(recipe),recipe.id+" match");
            var wire=JsonUtility.FromJson<CookingCatalog.Selection>(CookingCatalog.Serialize(selection));
            Check(catalog.Match(bag,wire).Contains(recipe),recipe.id+" network selection round trip");
            Check(catalog.TryCook(bag,recipe.id,wire,out string error),recipe.id+" cook: "+error);
            Check(Enumerable.Range(0,bag.Capacity).Where(i=>!bag.GetSlot(i).IsEmpty).Sum(i=>bag.GetSlot(i).Count)==1&&Count(bag,recipe.output)==1,recipe.id+" exact output, ingredients consumed");
            Check(bag.GetSlot(0).IsEmpty||bag.GetSlot(0).ExpiresAt>0,recipe.id+" output expiry");
        }
        var target=catalog.recipes.First(r=>r.id=="SteamedEgg");var state=new InventoryState();var picked=FillRecipe(state,target,3);var filler=Items()[0];
        while(Enumerable.Range(0,state.Capacity).Any(i=>state.GetSlot(i).IsEmpty))state.TryAdd(filler,1,out _);
        string before=InventorySnapshot.Export(state);
        Check(!catalog.TryCook(state,target.id,picked,out _),"full bag rejects output");Check(before==InventorySnapshot.Export(state),"failed cook leaves all ingredients");
        state=new InventoryState();picked=FillRecipe(state,target,3);picked.destination=0;before=InventorySnapshot.Export(state);
        Check(!catalog.TryCook(state,target.id,picked,out _)&&before==InventorySnapshot.Export(state),"occupied destination preserves ingredients");
        picked.destination=-1;Check(!catalog.TryCook(state,"not-a-recipe",picked,out _)&&before==InventorySnapshot.Export(state),"forged recipe rejected");
        Check(catalog.TryCook(state,target.id,picked,out _),"valid cook commits");before=InventorySnapshot.Export(state);
        Check(!catalog.TryCook(state,target.id,picked,out _)&&before==InventorySnapshot.Export(state),"replayed stale selection cannot cook twice");
        state=new InventoryState();picked=FillRecipe(state,target);picked.cells[8]=picked.cells[0];before=InventorySnapshot.Export(state);
        Check(!catalog.TryCook(state,target.id,picked,out _)&&before==InventorySnapshot.Export(state),"cannot reserve unowned extra unit");
        string result=$"PASS: {checks} cooking recipe, wire format, inventory and failure checks.";File.WriteAllText(CookingReport+"RulesValidation.txt",result);return result;
    }
    public static string RunCookingServer()
    {
        checks=0;var catalog=CookingCatalog.Current;var recipe=catalog.recipes.First(r=>r.id=="SteamedEgg");
        var locations=JsonUtility.FromJson<FurnitureStorageRules.Locations>(Resources.Load<TextAsset>("Furniture/StorageLocations").text);
        var place=locations.items.First(x=>(x.functions&FurnitureFunction.Cooking)!=0);var notCook=locations.items.First(x=>x.type==StorageType.General);
        var loop=new LoopbackTransport();var server=new GameServer(loop,3);ulong world=server.SpawnServerEntity("Net/World",SyncProtocol.AllMaps,EntityFlags.None,new[]{Array.Empty<byte>()});var books=new ServerBooks(server,world,null);var ledger=new ServerLedger(server,world,books);
        try
        {
            var peer=new QaPeer(loop.Connect());const string account="cooking-qa";peer.Send(SyncProtocol.Hello(account,"요리검증",place.map));server.Tick();peer.number=peer.Take(m=>m.kind==ToClient.Welcome).number;peer.Send(SyncProtocol.CharacterCreate("요리검증","male"));server.Tick();peer.Pump();
            ulong avatar=SyncProtocol.MakeId(peer.number,1);peer.Send(SyncProtocol.Spawn(avatar,AvatarSync.PrefabKey,place.map,EntityFlags.FollowsOwnerMap,new[]{SyncLayerQA.Pos(place.position,0),Array.Empty<byte>()}));server.Tick();
            var bag=ledger.Bag(account);for(int i=0;i<bag.Capacity;i++)if(!bag.GetSlot(i).IsEmpty)bag.TryRemove(i,bag.GetSlot(i).Count,out _);
            var selection=FillRecipe(bag,recipe,3);var args=new SharedOpArgs{a=notCook.id,b=recipe.id,c=CookingCatalog.Serialize(selection)};
            string Cook()=>ledger.Apply(peer.number,account,"p.cook",JsonUtility.ToJson(args));string before=InventorySnapshot.Export(bag);
            Check(Cook()!=null&&before==InventorySnapshot.Export(bag),"server rejects non-cooking furniture");args.a=place.id;
            peer.Send(SyncProtocol.State(avatar,0,SyncLayerQA.Pos(place.position+Vector3.right*100,0)));server.Tick();Check(Cook()!=null&&before==InventorySnapshot.Export(bag),"server rejects distant cooking");
            peer.Send(SyncProtocol.State(avatar,0,SyncLayerQA.Pos(place.position,0)));server.Tick();
            Check(Cook()==null&&Count(bag,recipe.output)==1,"server produces recipe once");before=InventorySnapshot.Export(bag);Check(Cook()!=null&&before==InventorySnapshot.Export(bag),"server refuses replay");
            CharacterSync.Read(server.World.Find(account).content,out var saved);var restored=new InventoryState();InventorySnapshot.Import(restored,saved.inventory);Check(InventoryLedgerRules.SameContents(bag,restored),"cooked inventory saved by existing character ledger");
            Check(ledger.Apply(peer.number,account,"p.storage.open",JsonUtility.ToJson(new SharedOpArgs{a=place.id}))!=null,"stove cannot be used as forged storage");
        }
        finally{ledger.End();server.Stop();}
        string result=$"PASS: {checks} server cooking checks.";File.WriteAllText(CookingReport+"ServerValidation.txt",result);return result;
    }
    public static string RunCookingUI(bool leaveOpen=false)
    {
        Check(Application.isPlaying,"Play mode required");checks=0;var bag=InventoryManager.Instance.State;string before=InventorySnapshot.Export(bag);
        var player=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Player"));player.name="CookingQAPlayer";player.transform.position=new Vector3(500,0,500);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Items/Furniture/Household/Prefabs/WhiteWood/Induction.prefab");var station=UnityEngine.Object.Instantiate(prefab);station.name="CookingQAStation";station.transform.position=player.transform.position;
        var ui=player.GetComponent<CookingInteraction>();
        typeof(PlayerInventory).GetMethod("EnsureUI",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(player.GetComponent<PlayerInventory>(),null);
        try
        {
            for(int i=0;i<bag.Capacity;i++)if(!bag.GetSlot(i).IsEmpty)bag.TryRemove(i,bag.GetSlot(i).Count,out _);
            var recipe=CookingCatalog.Current.recipes.First(r=>r.id=="SteamedEgg");FillRecipe(bag,recipe,3);ui.Open(station.GetComponent<WorldObject>());
            Check(CookingInteraction.IsOpen&&InputFocus.GameplayBlocked(),"cooking opens and blocks movement");Check(!player.GetComponent<PlayerInventory>().OpenInventory(),"inventory cannot overlap");
            var pointers=ui.GetComponentsInChildren<InventorySlotPointer>().Where(p=>p.CookingOwner==ui).ToArray();Check(pointers.Count(p=>p.CookingArea==1)==9&&pointers.Count(p=>p.CookingArea==0)==16&&pointers.Count(p=>p.CookingArea==2)==1,"3x3 + output + 2x8 slots");
            var cookingRect=(RectTransform)ui.transform.Find("CookingCanvas/CookingWindow");
            var inventoryRect=(RectTransform)ui.transform.Find("CookingCanvas/InventoryItemSelection");
            Check(cookingRect&&inventoryRect&&cookingRect.parent==inventoryRect.parent,"cooking and shared inventory are separate sibling windows");
            Check(cookingRect.sizeDelta==new Vector2(400,290)&&inventoryRect.sizeDelta==new Vector2(582,176),"compact window sizes");
            Check(cookingRect.anchoredPosition.y-cookingRect.sizeDelta.y/2-(inventoryRect.anchoredPosition.y+inventoryRect.sizeDelta.y/2)==10,"10 pixel gap between windows");
            int cell=0;
            foreach(var ingredient in recipe.ingredients)
            {int source=Enumerable.Range(0,bag.Capacity).First(i=>bag.GetSlot(i).Item==ingredient.item);for(int n=0;n<ingredient.count;n++){var from=pointers.First(p=>p.CookingArea==0&&p.InventoryIndex==source);int target=cell++;var to=pointers.First(p=>p.CookingArea==1&&p.InventoryIndex==target);var e=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){pointerDrag=from.gameObject,position=new Vector2(400,400)};from.OnBeginDrag(e);to.OnDrop(e);from.OnEndDrag(e);}}
            Check(ui.Result==recipe,"real pointer drag shows result");Check(Count(bag,recipe.ingredients[0].item)==recipe.ingredients[0].count*3,"selection does not consume ingredients");
            RenderCookingUI(ui);
            var result=pointers.First(p=>p.CookingArea==2);var destination=pointers.First(p=>p.CookingArea==0&&p.InventoryIndex==15);var ev=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){pointerDrag=result.gameObject,position=new Vector2(500,400)};result.OnBeginDrag(ev);destination.OnDrop(ev);result.OnEndDrag(ev);
            Check(bag.GetSlot(15).Item==recipe.output,"result drag places dish in chosen slot");Check(ui.Result==null,"recipe selection cleared after craft");ui.Close();Check(!CookingInteraction.IsOpen,"close restores input");
        }
        finally{ui.Close();InventorySnapshot.Import(bag,before);UnityEngine.Object.Destroy(player);UnityEngine.Object.Destroy(station);}
        string report=$"PASS: {checks} Play-mode cooking UI and pointer checks; frame {Time.frameCount}.";File.WriteAllText(CookingReport+"UIValidation.txt",report);return report;
    }
    static void RenderCookingUI(CookingInteraction ui)
    {
        var canvas=ui.GetComponentsInChildren<Canvas>().First(c=>c.name=="CookingCanvas");var target=new RenderTexture(1280,720,24);var image=new Texture2D(1280,720,TextureFormat.RGB24,false);var camera=new GameObject("CookingUICapture").AddComponent<Camera>();var previous=RenderTexture.active;
        try
        {
            foreach(var t in canvas.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            camera.cullingMask=1<<31;camera.backgroundColor=new Color(.12f,.14f,.17f);camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=target;camera.nearClipPlane=.1f;camera.farClipPlane=10;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(CookingReport+"CookingUI.png",image.EncodeToPNG());
        }
        finally{canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;RenderTexture.active=previous;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(camera.gameObject);}
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void IsolatePlayTestSaves()
    {
        if(!SessionState.GetBool("HouseholdFurnitureQA",false))return;
        WorldSave.PathOverride=Path.GetFullPath(Report+"QAWorld.json");
        PersonalSave.DirOverride=Path.GetFullPath(Report+"QAPersonal");
    }
    static void Check(bool value, string label) { if (!value) throw new Exception("Household QA: " + label); checks++; }
    static ItemData[] Items() => AssetDatabase.FindAssets("t:ItemData",new[]{Root}).Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
    public static string ValidateAssets()
    {
        checks = 0; var items = Items(); Check(items.Length == 240,"240 items"); Check(items.Select(i => i.itemId).Distinct().Count()==240,"unique IDs");
        int seats = 0, storage = 0;
        foreach(var item in items)
        {
            Check(item.icon && item.furniturePrefab,item.name + " prefab/icon");
            var world = item.furniturePrefab.GetComponent<WorldObject>(); Check(world && world.furnitureItem == item,item.name + " world metadata");
            Check(item.furniturePrefab.GetComponentsInChildren<MeshFilter>().All(m => m.sharedMesh && m.sharedMesh.vertexCount > 0),item.name + " geometry");
            Check(item.furniturePrefab.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m && m.shader && m.shader.name == "Universal Render Pipeline/Lit")),item.name + " materials");
            Check(item.furniturePrefab.GetComponentsInChildren<Collider>().Length > 0,item.name + " collision");
            if(world.HasFunction(FurnitureFunction.Seating)) { var anchors = item.furniturePrefab.GetComponentsInChildren<Seat>(); Check(anchors.Length > 0 && anchors.All(s=>s.owner==world),item.name + " seats"); seats += anchors.Length; }
            if(world.HasFunction(FurnitureFunction.Storage)) { var s=item.furniturePrefab.GetComponent<FridgeStorage>();Check(s && s.storageType==item.storageType,item.name + " storage"); storage++; }
        }
        string result = $"PASS: {checks} asset checks; {items.Length} prefabs; {seats} seats; {storage} containers.";
        File.WriteAllText(Report+"AssetValidation.txt",result); return result;
    }
    public static string RunInventory()
    {
        checks=0; var all=Resources.LoadAll<ItemData>("Inventory").Concat(TradeCatalog.Items()).ToArray();
        var food=all.First(i=>FoodShelfLife.IsPerishable(i)&&FoodShelfLife.FridgeDaysOf(i)>1);
        var clothes=ScriptableObject.CreateInstance<ItemData>();clothes.hideFlags=HideFlags.HideAndDontSave;clothes.itemType=ItemType.Clothing;clothes.itemId="qa:clothing";clothes.maxStack=1;
        var furniture=Items()[0];
        var bag=new InventoryState();var box=new InventoryState();
        bag.TryAdd(food,1,out _); bag.TryAdd(clothes,1,out _); bag.TryAdd(furniture,1,out _);
        Check(!FurnitureStorageRules.Transfer(StorageType.Clothing,false,false,bag,box,0,1,null,out _),"wardrobe rejects food");
        Check(FurnitureStorageRules.Transfer(StorageType.Clothing,false,false,bag,box,1,1,null,out _),"wardrobe accepts clothes");
        Check(!FurnitureStorageRules.Transfer(StorageType.Food,true,false,bag,box,2,1,null,out _),"fridge rejects furniture");
        Check(FurnitureStorageRules.Transfer(StorageType.General,false,false,bag,box,2,1,null,out _),"drawer accepts furniture");
        Check(FurnitureStorageRules.Transfer(StorageType.General,false,false,bag,box,0,1,null,out _),"drawer accepts food");
        box=new InventoryState();bag=new InventoryState();bag.TryAdd(food,1,out _);
        int made=bag.GetSlot(0).MadeDay;
        Check(FurnitureStorageRules.Transfer(StorageType.Food,true,false,bag,box,0,1,null,out _),"fridge deposit");
        long expires=box.GetSlot(0).ExpiresAt;
        Check(expires==new GameTime(made+FoodShelfLife.FridgeDaysOf(food),GameTime.DayStartHour).TotalMinutes,"refrigerated shelf life");
        Check(FurnitureStorageRules.Transfer(StorageType.Food,true,true,bag,box,0,1,null,out _),"fridge withdraw");
        Check(bag.GetSlot(0).ExpiresAt<=expires && bag.GetSlot(0).MadeDay==made,"expiry and production date preserved");
        var full=new InventoryState();for(int i=0;i<full.Capacity;i++)full.TryAdd(furniture,1,out _);
        string a=InventorySnapshot.Export(bag),b=InventorySnapshot.Export(full);
        Check(!FurnitureStorageRules.Transfer(StorageType.General,false,false,bag,full,0,1,null,out _),"full destination refuses");
        Check(a==InventorySnapshot.Export(bag)&&b==InventorySnapshot.Export(full),"failed transfer changes neither inventory");
        box=new InventoryState(); string fingerprint=FurnitureStorageRules.Fingerprint(bag.GetSlot(0));
        Check(FurnitureStorageRules.Transfer(StorageType.General,false,false,bag,box,0,1,fingerprint,out _),"first request commits");
        Check(!FurnitureStorageRules.Transfer(StorageType.General,false,false,bag,box,0,1,fingerprint,out _),"duplicate request rejected");
        var restored=new InventoryState();Check(InventorySnapshot.Import(restored,InventorySnapshot.Export(box)),"snapshot restore");
        Check(InventorySnapshot.Export(restored)==InventorySnapshot.Export(box),"snapshot contents exact");
        var prior=FurnitureStorageRules.CaptureOffline();
        try
        {
            FurnitureStorageRules.RestoreOffline(new System.Collections.Generic.List<FurnitureStorageRules.Saved>{new(){id="qa",inventory=InventorySnapshot.Export(box)}});
            var data=new WorldSaveData{furnitureStorage=FurnitureStorageRules.CaptureOffline()};
            FurnitureStorageRules.RestoreOffline(JsonUtility.FromJson<WorldSaveData>(JsonUtility.ToJson(data)).furnitureStorage);
            Check(InventorySnapshot.Export(FurnitureStorageRules.Contents("qa",false))==InventorySnapshot.Export(box),"world save round trip");
        }
        finally{FurnitureStorageRules.RestoreOffline(prior);}
        UnityEngine.Object.DestroyImmediate(clothes);
        string result=$"PASS: {checks} inventory/expiry/save checks (synthetic clothing fixture; existing food and furniture definitions).";File.WriteAllText(Report+"InventoryValidation.txt",result);return result;
    }
    public static string RunServer()
    {
        checks=0;
        var locations=JsonUtility.FromJson<FurnitureStorageRules.Locations>(Resources.Load<TextAsset>("Furniture/StorageLocations").text);
        var place=locations.items.First(x=>x.type==StorageType.General);
        var loop=new LoopbackTransport();var server=new GameServer(loop,4);
        ulong world=server.SpawnServerEntity("Net/World",SyncProtocol.AllMaps,EntityFlags.None,new[]{Array.Empty<byte>()});
        var books=new ServerBooks(server,world,null);var ledger=new ServerLedger(server,world,books);
        try
        {
            QaPeer Join(string account,Vector3 position)
            {
                var peer=new QaPeer(loop.Connect());peer.Send(SyncProtocol.Hello(account,account,place.map));server.Tick();peer.number=peer.Take(m=>m.kind==ToClient.Welcome).number;
                peer.Send(SyncProtocol.CharacterCreate("보관검증"+ (account.EndsWith("-a") ? "가" : account.EndsWith("-b") ? "나" : "다"),"male"));server.Tick();peer.Pump();
                peer.Send(SyncProtocol.Spawn(SyncProtocol.MakeId(peer.number,1),AvatarSync.PrefabKey,place.map,EntityFlags.FollowsOwnerMap,new[]{SyncLayerQA.Pos(position,0),Array.Empty<byte>()}));server.Tick();peer.Pump();
                var bag=ledger.Bag(account);for(int i=0;i<bag.Capacity;i++)if(!bag.GetSlot(i).IsEmpty)bag.TryRemove(i,bag.GetSlot(i).Count,out _);
                return peer;
            }
            var near=Join("storage-qa-a",place.position);var other=Join("storage-qa-b",place.position);var far=Join("storage-qa-far",place.position+Vector3.right*100);
            string Op(QaPeer peer,string account,string kind,SharedOpArgs args)=>ledger.Apply(peer.number,account,kind,JsonUtility.ToJson(args));
            Check(Op(far,"storage-qa-far","p.storage.open",new(){a=place.id})!=null,"server refuses distance");
            Check(Op(near,"storage-qa-a","p.storage.open",new(){a="forged-container"})!=null,"server refuses unknown container");
            string openError=Op(near,"storage-qa-a","p.storage.open",new(){a=place.id});
            Check(openError==null,"nearby open: "+openError+"; map="+server.MapOf(near.number)+"; expected="+place.map+"; entities="+string.Join(",",server.EntityIds));
            near.Pump();var snapshot=near.Take(m=>m.kind==ToClient.Event&&m.eventId==WorldSection.StorageEvent);
            Check(snapshot!=null&&snapshot.sender==0,"server snapshot delivered over transport");
            using(var reader=SyncProtocol.Reader(snapshot.blob))
            {Check(SyncProtocol.ReadStr(reader)==place.id,"snapshot addresses requested container");var state=new InventoryState();using var stream=new System.IO.Compression.GZipStream(new MemoryStream(SyncProtocol.ReadServerBlob(reader)),System.IO.Compression.CompressionMode.Decompress);using var text=new StreamReader(stream);Check(InventorySnapshot.Import(state,text.ReadToEnd()),"snapshot decoded by client format");}
            var bag=ledger.Bag("storage-qa-a");bag.TryAdd(Items()[0],1,out _);
            var request=new SharedOpArgs{a=place.id,b=FurnitureStorageRules.Fingerprint(bag.GetSlot(0)),c="0",n=1};
            Check(Op(near,"storage-qa-a","p.storage.move",request)==null,"server deposit");
            Check(bag.GetSlot(0).IsEmpty,"server removes bag item");
            var saved=ledger.CaptureStorage();var contents=new InventoryState();InventorySnapshot.Import(contents,saved.Single(x=>x.id==place.id).inventory);
            request.b=FurnitureStorageRules.Fingerprint(contents.GetSlot(0));request.f=true;
            Check(Op(other,"storage-qa-b","p.storage.move",request)==null,"second client withdrawal");
            Check(Op(near,"storage-qa-a","p.storage.move",request)!=null,"stale concurrent withdrawal rejected");
            Check(!ledger.Bag("storage-qa-b").GetSlot(0).IsEmpty&&bag.GetSlot(0).IsEmpty,"one owner only");
            ledger.RestoreStorage(saved);var restored=ledger.CaptureStorage();Check(restored.Single(x=>x.id==place.id).inventory==saved.Single(x=>x.id==place.id).inventory,"server storage persistence");
        }
        finally{ledger.End();server.Stop();}
        string result=$"PASS: {checks} real server-ledger checks with 3 loopback clients.";File.WriteAllText(Report+"ServerValidation.txt",result);return result;
    }
    public static string BuildReviewScene()
    {
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        try
        {
            string[] styles={"WhiteWood","Eclectic","MarbleGold","ChicBlack"};
            for(int s=0;s<styles.Length;s++)
            {
                var group=new GameObject(styles[s]);var items=Items().Where(i=>AssetDatabase.GetAssetPath(i).Contains("/"+styles[s]+"/")).OrderBy(i=>i.name).ToArray();
                for(int i=0;i<items.Length;i++)
                {var go=(GameObject)PrefabUtility.InstantiatePrefab(items[i].furniturePrefab,group.transform);go.transform.localPosition=new Vector3((i%10)*3.6f,0,-(i/10)*4.5f-s*32);var storage=go.GetComponent<FridgeStorage>();if(storage)storage.storageId="household-review:"+styles[s]+":"+items[i].name;}
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="ReviewFloor";floor.transform.position=new Vector3(16,-.15f,-60);floor.transform.localScale=new Vector3(40,.3f,136);
            var spawn=new GameObject("PlayerSpawn").AddComponent<CompanyGame.World.Maps.MapSpawnPoint>();spawn.transform.position=new Vector3(0,0,3);
            var light=new GameObject("ReviewSun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(50,-30,0);
            var camera=new GameObject("ReviewCamera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(16,100,-10);camera.transform.LookAt(new Vector3(16,0,-60));camera.orthographic=true;camera.orthographicSize=72;camera.farClipPlane=300;camera.backgroundColor=new Color(.25f,.28f,.32f);camera.clearFlags=CameraClearFlags.SolidColor;
            const string path="Assets/Scenes/Templates/HouseholdFurnitureReview.unity";EditorSceneManager.SaveScene(scene,path);return path;
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(prior.IsValid()&&prior.isLoaded)SceneManager.SetActiveScene(prior);}
    }
    public static string RunSeating()
    {
        Check(Application.isPlaying,"Play mode required");checks=0;var player=new GameObject("HouseholdSeatQA");player.AddComponent<CharacterController>();player.AddComponent<PlayerMovement>();var seating=player.AddComponent<PlayerSeating>();
        try
        {
            foreach(var item in Items().Where(i=>(i.furnitureFunctions&FurnitureFunction.Seating)!=0&&i.furnitureTheme==FurnitureTheme.WhiteWood))
            {
                var fixture=UnityEngine.Object.Instantiate(item.furniturePrefab);fixture.transform.position=new Vector3(500,0,500);
                try{foreach(var seat in fixture.GetComponentsInChildren<Seat>()){player.transform.position=seat.transform.position+Vector3.forward;Check(seating.TrySit(seat),item.name+" sit");Check(seating.IsSeated&&seat.Occupant==player.transform,item.name+" occupied");Check(seating.TryStand()&&!seat.Occupant,item.name+" stand");}}
                finally{UnityEngine.Object.Destroy(fixture);}
            }
        }
        finally{UnityEngine.Object.Destroy(player);}
        string result=$"PASS: {checks} Play-mode sit/stand/occupancy checks; frame {Time.frameCount}.";File.WriteAllText(Report+"SeatingValidation.txt",result);return result;
    }
    public static string RenderStyles()
    {
        var scene=EditorSceneManager.NewPreviewScene();var previous=RenderTexture.active;
        var target=new RenderTexture(1600,1000,24);var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);
        try
        {
            string[] styles={"WhiteWood","Eclectic","MarbleGold","ChicBlack"};string[] keys={"Armchair","GamingChair","Wardrobe","Fridge","IslandTable"};
            var items=Items();
            for(int s=0;s<4;s++)for(int k=0;k<keys.Length;k++)
            {var item=items.Single(i=>i.name==keys[k]&&AssetDatabase.GetAssetPath(i).Contains("/"+styles[s]+"/"));var go=(GameObject)PrefabUtility.InstantiatePrefab(item.furniturePrefab,scene);go.transform.position=new Vector3(k*2.9f,0,-s*3.3f);}
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.position=new Vector3(6,-.15f,-5);floor.transform.localScale=new Vector3(17,.3f,16);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.3f,.32f,.35f));floor.GetComponent<Renderer>().sharedMaterial=material;
            var light=new GameObject("Light").AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,scene);light.type=LightType.Directional;light.intensity=2.5f;light.transform.rotation=Quaternion.Euler(50,-25,0);
            var fill=new GameObject("Fill").AddComponent<Light>();SceneManager.MoveGameObjectToScene(fill.gameObject,scene);fill.type=LightType.Directional;fill.intensity=.8f;fill.transform.rotation=Quaternion.Euler(35,150,0);
            var camera=new GameObject("Camera").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.scene=scene;camera.transform.position=new Vector3(15,17,16);camera.transform.LookAt(new Vector3(5.8f,.4f,-5));camera.orthographic=true;camera.orthographicSize=9;camera.farClipPlane=100;camera.backgroundColor=new Color(.19f,.21f,.24f);camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=target;
            camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(Report+"UnityStyles.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(material);return "UnityStyles.png";
        }
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);EditorSceneManager.ClosePreviewScene(scene);}
    }
    public static string RunStorageUI()
    {
        Check(Application.isPlaying,"Play mode required");checks=0;
        var original=InventorySnapshot.Export(InventoryManager.Instance.State);var storageBefore=FurnitureStorageRules.CaptureOffline();
        var player=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Player"));player.name="StorageUIQA";player.transform.position=new Vector3(500,0,500);
        var container=UnityEngine.Object.Instantiate(Items().First(i=>i.name=="Drawer3").furniturePrefab);container.transform.position=player.transform.position;container.GetComponent<FridgeStorage>().storageId="qa-ui-container";
        var ui=player.GetComponent<FridgeInteraction>();
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        try
        {
            var bag=InventoryManager.Instance.State;for(int i=0;i<bag.Capacity;i++)if(!bag.GetSlot(i).IsEmpty)bag.TryRemove(i,bag.GetSlot(i).Count,out _);
            bag.TryAdd(Items()[0],1,out _);
            typeof(FridgeInteraction).GetMethod("Open",flags).Invoke(ui,new object[]{container.GetComponent<FridgeStorage>()});
            Check(FridgeInteraction.IsOpen,"storage UI opens");Check(InputFocus.GameplayBlocked(),"gameplay blocked while open");
            Check(!player.GetComponent<PlayerInventory>().OpenInventory(),"inventory cannot overlap storage");
            player.GetComponent<PlayerInventory>().ClickInventorySlot(0);
            Check(bag.GetSlot(0).IsEmpty&&!container.GetComponent<FridgeStorage>().Contents.GetSlot(0).IsEmpty,"button deposits into shared inventory");
            player.GetComponent<PlayerInventory>().UserInterface.ClickContainerSlot(0);
            Check(!bag.GetSlot(0).IsEmpty&&container.GetComponent<FridgeStorage>().Contents.GetSlot(0).IsEmpty,"button withdraws exactly once");
            typeof(FridgeInteraction).GetMethod("Close",flags).Invoke(ui,null);Check(!FridgeInteraction.IsOpen,"storage UI closes");
        }
        finally
        {
            if(ui)typeof(FridgeInteraction).GetMethod("Close",flags).Invoke(ui,null);
            InventorySnapshot.Import(InventoryManager.Instance.State,original);FurnitureStorageRules.RestoreOffline(storageBefore);
            UnityEngine.Object.Destroy(player);UnityEngine.Object.Destroy(container);
        }
        string result=$"PASS: {checks} Play-mode UI/button/input-focus checks.";File.WriteAllText(Report+"UIValidation.txt",result);return result;
    }
}
