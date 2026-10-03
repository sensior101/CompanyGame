using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CompanyGame.World.Maps;
using CompanyGame.Daldongne;

public static class SetupConvenienceGameplay
{
    const string Map="Assets/Scenes/daldongnaemap.unity";
    const string Interior="Assets/Scenes/Interiors/ConvenienceStoreInterior.unity";
    const string Food="Assets/Art/Items/ConvenienceFood";
    static Vector3 DoorPosition=new Vector3(-27.33f,3.62f,-5.3f);
    public static string Build()
    {
        if(EditorApplication.isPlaying)throw new Exception("Exit Play Mode first.");
        var outside=SceneManager.GetActiveScene();
        if(outside.path!=Map)throw new Exception("Open daldongnaemap first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var player=outside.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerMovement>(true)).Single(p=>p.isActiveAndEnabled);
        var camera=outside.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerCameraController>(true)).Single(c=>c.enabled);
        var offers=CreateOffers();
        var holder=outside.GetRootGameObjects().FirstOrDefault(g=>g.name=="ConvenienceStore_Connections") ?? new GameObject("ConvenienceStore_Connections");
        var door=Child(holder,"Entrance");door.transform.position=DoorPosition;
        var portal=door.GetComponent<StoreInteractionPoint>() ?? door.AddComponent<StoreInteractionPoint>();
        portal.action=StoreAction.Door;portal.prompt="들어가기";portal.radius=1.4f;portal.heightTolerance=.9f;portal.targetScenePath=Interior;portal.targetSpawnId="store_entry";
        var returnGo=Child(holder,"OutsideArrival");returnGo.transform.position=DoorPosition+Vector3.back*.85f;returnGo.transform.rotation=Quaternion.Euler(0,180,0);
        var arrival=returnGo.GetComponent<MapSpawnPoint>() ?? returnGo.AddComponent<MapSpawnPoint>();arrival.spawnId="store_exit";
        EditorSceneManager.MarkSceneDirty(outside);
        // Preserve the current scene's live edits, including those made before this task.
        EditorSceneManager.SaveScene(outside);
        var inside=EditorSceneManager.OpenScene(Interior,OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(inside);
            var links=inside.GetRootGameObjects().FirstOrDefault(g=>g.name=="ConvenienceStore_Gameplay") ?? new GameObject("ConvenienceStore_Gameplay");
            var existing=inside.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerMovement>(true)).FirstOrDefault();
            var local=existing ? existing : UnityEngine.Object.Instantiate(player.gameObject).GetComponent<PlayerMovement>();
            local.name="StorePlayer";local.transform.SetParent(null,true);SceneManager.MoveGameObjectToScene(local.gameObject,inside);
            local.transform.SetParent(links.transform,true);local.spawn=new Vector3(.4f,.06f,1.60f);local.transform.position=local.spawn;local.transform.rotation=Quaternion.identity;
            var preview=inside.GetRootGameObjects().FirstOrDefault(g=>g.name=="Interior_Preview_Camera");if(preview)preview.SetActive(false);
            var localCamera=inside.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerCameraController>(true)).FirstOrDefault();
            if(!localCamera)localCamera=UnityEngine.Object.Instantiate(camera.gameObject).GetComponent<PlayerCameraController>();
            localCamera.name="StorePlayerCamera";localCamera.transform.SetParent(null,true);SceneManager.MoveGameObjectToScene(localCamera.gameObject,inside);localCamera.transform.SetParent(links.transform,true);
            localCamera.target=local.transform;localCamera.firstPerson=true;localCamera.initialPitch=0;localCamera.firstPersonFOV=75;localCamera.distance=1.4f;localCamera.targetHeight=1.4f;localCamera.enableObstacleFade=false;
            localCamera.transform.SetPositionAndRotation(local.spawn+Vector3.up*1.6f,Quaternion.identity);
            local.viewCamera=localCamera.GetComponent<Camera>();local.viewCamera.nearClipPlane=.04f;local.viewCamera.farClipPlane=80;
            var appearance=local.GetComponent<DaldongnePlayerAppearance>();if(appearance){localCamera.femaleVisuals=appearance.female;localCamera.maleVisuals=appearance.male;}
            var spawn=Child(links,"StoreArrival");spawn.transform.position=local.spawn;var sp=spawn.GetComponent<MapSpawnPoint>() ?? spawn.AddComponent<MapSpawnPoint>();sp.spawnId="store_entry";
            var exit=Child(links,"Exit");exit.transform.position=new Vector3(0,.05f,.65f);var ep=exit.GetComponent<StoreInteractionPoint>() ?? exit.AddComponent<StoreInteractionPoint>();
            ep.action=StoreAction.Door;ep.prompt="나가기";ep.radius=1.2f;ep.targetScenePath=Map;ep.targetSpawnId="store_exit";
            var npc=Child(links,"ConvenienceClerk");npc.transform.SetPositionAndRotation(new Vector3(-3.75f,.025f,4.85f),Quaternion.Euler(0,90,0));
            if(npc.transform.childCount==0)
            {
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Daldongne/Players/MaleVisual.prefab"),inside);visual.transform.SetParent(npc.transform,false);
                var apron=GameObject.CreatePrimitive(PrimitiveType.Cube);apron.name="Staff_Green_Apron";apron.transform.SetParent(npc.transform,false);apron.transform.localPosition=new Vector3(0,1.0f,.17f);apron.transform.localScale=new Vector3(.40f,.47f,.045f);
                UnityEngine.Object.DestroyImmediate(apron.GetComponent<Collider>());apron.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Interiors/ConvenienceStore/Materials/DeepGreen.mat");
                var badge=GameObject.CreatePrimitive(PrimitiveType.Cube);badge.name="Staff_Name_Badge";badge.transform.SetParent(npc.transform,false);badge.transform.localPosition=new Vector3(-.1f,1.16f,.2f);badge.transform.localScale=new Vector3(.11f,.055f,.008f);
                UnityEngine.Object.DestroyImmediate(badge.GetComponent<Collider>());badge.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Interiors/ConvenienceStore/Materials/Ivory.mat");
            }
            var talk=Child(links,"Clerk_CustomerPosition");talk.transform.position=new Vector3(-2.02f,.04f,4.85f);
            var shop=talk.GetComponent<StoreInteractionPoint>() ?? talk.AddComponent<StoreInteractionPoint>();shop.action=StoreAction.Shopkeeper;shop.prompt="대화하기";shop.radius=1.25f;shop.heightTolerance=.8f;
            shop.offers=offers;
            EditorSceneManager.MarkSceneDirty(inside);EditorSceneManager.SaveScene(inside);
            var scenes=EditorBuildSettings.scenes.ToList();int i=scenes.FindIndex(s=>s.path==Interior);if(i<0)scenes.Add(new EditorBuildSettingsScene(Interior,true));else scenes[i].enabled=true;EditorBuildSettings.scenes=scenes.ToArray();
            return "Entrance + return portal, store player/camera, clerk and 10 offers saved; scene enabled in Build Settings.";
        }
        finally{EditorSceneManager.CloseScene(inside,true);SceneManager.SetActiveScene(outside);}
    }
    static GameObject Child(GameObject parent,string name)
    {var t=parent.transform.Find(name);if(t)return t.gameObject;var go=new GameObject(name);go.transform.SetParent(parent.transform,false);return go;}
    static StoreOffer[] CreateOffers()
    {
        var rows=new[]{
            ("TriangleGimbap","삼각김밥","food:gimbap",1500,7,0,0),
            ("CupRamen","컵라면","food:cup-ramen",2000,8,0,0),
            ("LunchBox","편의점 도시락","food:lunch-box",5000,10,0,0),
            ("Water","생수","food:water",700,0,2,0),
            ("EnergyBar","에너지바","food:energy-bar",1800,0,7,0),
            ("EnergyDrink","에너지드링크","food:energy-drink",2500,0,10,0),
            ("Cola","콜라","food:cola",1500,0,0,2),
            ("Chips","과자","food:chips",2000,0,0,3),
            ("Chocolate","초콜렛","food:chocolate",1500,0,0,3),
            ("Jelly","젤리","food:jelly",1500,0,0,3)
        };
        return rows.Select(r=>
        {
            var item=Item(r.Item1,r.Item2,r.Item3);
            item.healthRestore=r.Item5;item.staminaRestore=r.Item6;item.stressRelief=r.Item7;
            EditorUtility.SetDirty(item);AssetDatabase.SaveAssetIfDirty(item);
            return new StoreOffer{item=item,price=r.Item4};
        }).ToArray();
    }
    // Update only merchandise so later catalog edits preserve scene/player/NPC edits.
    public static string UpdateCatalog()
    {
        if(EditorApplication.isPlaying)throw new Exception("Exit Play Mode first.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var offers=CreateOffers();var previous=SceneManager.GetActiveScene();
        var scene=SceneManager.GetSceneByPath(Interior);bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(Interior,OpenSceneMode.Additive);
        try
        {
            var shop=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StoreInteractionPoint>(true)).Single(p=>p.action==StoreAction.Shopkeeper);
            Undo.RecordObject(shop,"Update convenience store catalog");shop.offers=offers;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            return string.Join("\n",offers.Select(o=>o.item.DisplayName+" = "+o.price));
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);if(previous.isLoaded)SceneManager.SetActiveScene(previous);}
    }
    static ItemData Item(string filename,string name,string id)
    {
        var texture=(TextureImporter)AssetImporter.GetAtPath(Food+"/"+filename+".png");texture.textureType=TextureImporterType.Sprite;texture.spriteImportMode=SpriteImportMode.Single;texture.filterMode=FilterMode.Point;texture.textureCompression=TextureImporterCompression.Uncompressed;texture.SaveAndReimport();
        string path=Food+"/"+filename+".asset";var data=AssetDatabase.LoadAssetAtPath<ItemData>(path);if(!data){data=ScriptableObject.CreateInstance<ItemData>();AssetDatabase.CreateAsset(data,path);}
        var photo=AssetDatabase.LoadAllAssetsAtPath(Food+"/StoreFoods.jpg").OfType<Sprite>().FirstOrDefault(s=>s.name==filename);
        data.itemId=id;data.displayName=name;data.icon=photo?photo:AssetDatabase.LoadAssetAtPath<Sprite>(Food+"/"+filename+".png");data.category=ItemCategory.General;data.maxStack=20;EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);return data;
    }
}
