using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Batch-only isolated physics/save checks; never opens or writes a player's world.</summary>
public static class InteriorEditingQA
{
    static readonly List<string> checks = new();
    static void Check(bool pass, string label)
    {
        if (!pass) throw new Exception("Interior editing QA: " + label);
        checks.Add(label);
    }
    public static void Run()
    {
        if (!Application.isBatchMode || Application.isPlaying) throw new InvalidOperationException("Run only in an isolated batch Editor.");
        Directory.CreateDirectory("Temp/InteriorEditQA");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var registryField = typeof(GameSystem<PropertyRegistry>).GetField("<Instance>k__BackingField", BindingFlags.Static|BindingFlags.NonPublic);
        var oldRegistry = registryField.GetValue(null);
        var type = typeof(FurniturePlacementController);
        var placed = (List<PlacedFurnitureSave>)type.GetField("placed",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var models = (Dictionary<string,GameObject>)type.GetField("instances",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var previousPlaced = new List<PlacedFurnitureSave>(placed); var previousModels = new Dictionary<string,GameObject>(models);
        var oldCapture = WorldSave.CaptureInteriorWalls; var oldRestore = WorldSave.RestoreInteriorWalls;
        try
        {
            var registry = new GameObject("QA registry").AddComponent<PropertyRegistry>(); registryField.SetValue(null,registry);
            var zone = new GameObject("QA ownable room").AddComponent<PropertyZone>();
            zone.propertyId="qa:interior";zone.size=new Vector3(12,3.3f,12);
            typeof(PropertyZone).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(zone,null);
            registry.SeedOwner(zone.propertyId,GameSession.LocalPlayerName);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="QA floor";
            floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(12,.2f,12);
            Physics.SyncTransforms();
            var wall=new PlacedWallSave{id="qa-wall-a",scene=scene.path,property=zone.propertyId,start=new Vector3(-3,0,-2),end=new Vector3(0,0,-2)};
            Check(InteriorWallLayout.CanPlace(wall,zone),"owned floor permits wall");
            Check(InteriorWallLayout.TryPut(wall,zone),"wall creation commits geometry and record");
            Check(InteriorWallLayout.Model(wall.id).GetComponent<UnityEngine.AI.NavMeshObstacle>().carving,"wall carves navigation");
            var duplicate=InteriorWallLayout.Copy(wall);duplicate.id="duplicate";
            Check(!InteriorWallLayout.TryPut(duplicate,zone),"overlapping duplicate wall rejected");
            var joint=InteriorWallLayout.Copy(wall);joint.id="qa-wall-b";joint.start=wall.end;joint.end=new Vector3(0,0,1);
            Check(InteriorWallLayout.TryPut(joint,zone),"perpendicular shared-end wall joint accepted");
            var cross=InteriorWallLayout.Copy(wall);cross.id="cross";cross.start=new Vector3(-1,0,-4);cross.end=new Vector3(-1,0,0);
            Check(!InteriorWallLayout.CanPlace(cross,zone),"mid-wall crossing rejected");
            var outside=InteriorWallLayout.Copy(wall);outside.id="outside";outside.end=new Vector3(7,0,-2);
            Check(!InteriorWallLayout.CanPlace(outside,zone),"outside property rejected");
            var invalid=InteriorWallLayout.Copy(wall);invalid.height=float.NaN;
            Check(!InteriorWallLayout.CanPlace(invalid,zone),"non-finite geometry rejected");
            invalid=InteriorWallLayout.Copy(wall);invalid.end=invalid.start;
            Check(!InteriorWallLayout.CanPlace(invalid,zone),"zero-length wall rejected");
            invalid=InteriorWallLayout.Copy(wall);invalid.height=4;
            Check(!InteriorWallLayout.CanPlace(invalid,zone),"wall exceeding ceiling rejected");
            invalid=InteriorWallLayout.Copy(wall);invalid.id="nofloor";invalid.start=new Vector3(1,0,3);invalid.end=new Vector3(3,0,3);
            floor.GetComponent<Collider>().enabled=false;Physics.SyncTransforms();
            Check(!InteriorWallLayout.CanPlace(invalid,zone),"missing floor rejected");floor.GetComponent<Collider>().enabled=true;
            registry.TryAbandon(zone.propertyId,GameSession.LocalPlayerName);
            Check(!InteriorWallLayout.CanPlace(invalid,zone),"unowned space rejects edit");
            registry.TryRegister(zone.propertyId,GameSession.LocalPlayerName);
            var snapshot=InteriorWallLayout.Capture();snapshot[0].height=.5f;
            Check(Mathf.Abs(InteriorWallLayout.Get(wall.id).height-2.6f)<.001f,"capture owns detached wall records");
            WorldSave.CaptureInteriorWalls=InteriorWallLayout.Capture;WorldSave.RestoreInteriorWalls=InteriorWallLayout.Restore;
            var json=WorldSave.ToJson(new WorldSaveData{interiorWalls=WorldSave.CaptureInteriorWalls()});
            var data=JsonUtility.FromJson<WorldSaveData>(json);
            Check(data.interiorWalls.Count==2,"wall JSON round trip retains both segments");
            Check(InteriorWallLayout.TryRemove(wall.id,zone) && !InteriorWallLayout.Model(wall.id),"wall removal updates geometry and records");
            Check(InteriorWallLayout.TryPut(wall,zone),"deleted wall can be restored at same stable ID");
            WorldSave.RestoreInteriorWalls(data.interiorWalls);
            Check(InteriorWallLayout.Count==2 && InteriorWallLayout.Model(wall.id),"save restore reconstructs walls");
            var legacy=JsonUtility.FromJson<WorldSaveData>("{\"version\":1,\"furniture\":[]}");
            Check(legacy.interiorWalls==null,"legacy save accepts optional wall field");
            InteriorWallLayout.Restore(legacy.interiorWalls);Check(InteriorWallLayout.Count==0,"legacy restore clears previous world walls");

            placed.Clear();models.Clear();
            var model=GameObject.CreatePrimitive(PrimitiveType.Cube);model.name="QA existing furniture";
            model.transform.position=new Vector3(-2,.515f,2);
            var storage=model.AddComponent<FridgeStorage>();storage.storageId="furniture:qa-chair:0";
            var record=new PlacedFurnitureSave{id="qa-chair",item="qa-item",scene=scene.path,property=zone.propertyId,position=model.transform.position,yaw=0};
            placed.Add(record);models[record.id]=model;
            Check(FurniturePlacementController.TryMovePlacedFurniture(record.id,zone,new Vector3(2,.515f,2),90),"existing furniture moves and rotates");
            Check(models[record.id]==model && storage==model.GetComponent<FridgeStorage>() && storage.storageId=="furniture:qa-chair:0","move preserves object, ID and storage component");
            var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.transform.position=new Vector3(3,.5f,2);Physics.SyncTransforms();
            var before=JsonUtility.ToJson(record);var oldPosition=model.transform.position;
            Check(!FurniturePlacementController.TryMovePlacedFurniture(record.id,zone,new Vector3(3,.515f,2),0),"furniture collision rejected");
            Check(JsonUtility.ToJson(record)==before && model.transform.position==oldPosition,"failed move changes neither record nor transform");
            Check(!FurniturePlacementController.TryMovePlacedFurniture(record.id,zone,new Vector3(8,.515f,2),0),"furniture boundary rejection");
            Check(FurniturePlacementController.TryMovePlacedFurniture(record.id,zone,new Vector3(-2,.515f,2),0),"move undo pose accepted");
            var seat=new GameObject("QA occupied seat").AddComponent<Seat>();seat.transform.SetParent(model.transform,false);
            var occupant=new GameObject("QA seated actor");seat.assignedOccupant=occupant.transform;
            Check(!FurniturePlacementController.TryMovePlacedFurniture(record.id,zone,new Vector3(0,.515f,2),0),"occupied furniture cannot move");
            seat.assignedOccupant=null;
            zone.transform.rotation=Quaternion.Euler(0,90,0);
            Check(FurniturePlacementController.TryMovePlacedFurniture(record.id,zone,new Vector3(-2,.515f,2),30),"rotated zone local coordinates supported");
            Check(Vector3.Distance(model.transform.position,zone.transform.TransformPoint(record.position))<.001f,"zone-local save transform remains consistent");
            zone.transform.rotation=Quaternion.identity;
            // The real player sits outside the zone's scene (DontDestroyOnLoad), so test from a second scene.
            var playerScene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var playerBody=new GameObject("QA player").AddComponent<PlayerMovement>();
            SceneManager.MoveGameObjectToScene(playerBody.gameObject,playerScene);
            playerBody.transform.position=new Vector3(-2,0,-3);Physics.SyncTransforms();
            Check(!FurniturePlacementController.TryMovePlacedFurniture(record.id,zone,new Vector3(-2,.515f,-3),0),"furniture cannot be placed on the player");
            UnityEngine.Object.DestroyImmediate(playerBody.gameObject);Physics.SyncTransforms();
            Check(FurniturePlacementController.TryMovePlacedFurniture(record.id,zone,new Vector3(-2,.515f,-3),0),"same spot accepted once the player leaves");
            var dropped=AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:ItemData",new[]{"Assets/Resources/Inventory/Furniture"})[0]));
            record.item=dropped.itemId;
            var bag=new InventoryState();
            seat.assignedOccupant=occupant.transform;
            Check(!FurniturePlacementController.TryRetrievePlacedFurniture(record.id,zone,bag,out _)&&placed.Contains(record),"occupied furniture cannot be retrieved");
            seat.assignedOccupant=null;
            storage.Contents.TryAdd(dropped,1,out _);
            Check(!FurniturePlacementController.TryRetrievePlacedFurniture(record.id,zone,bag,out _)&&placed.Contains(record),"non-empty storage blocks retrieval");
            storage.Contents.TryRemove(0,1,out _);
            Check(FurniturePlacementController.TryRetrievePlacedFurniture(record.id,zone,bag,out var retrieveError),"empty furniture retrieved: "+retrieveError);
            Check(!placed.Contains(record)&&!models.ContainsKey(record.id)&&!model&&bag.GetSlot(0).Item==dropped,"retrieval moves the item to the bag and removes the model");
            File.WriteAllText("Temp/InteriorEditQA/validation.txt","PASS "+checks.Count+"\n"+string.Join("\n",checks));
            Debug.Log("INTERIOR_EDIT_QA_PASS "+checks.Count);
        }
        catch(Exception error)
        {
            File.WriteAllText("Temp/InteriorEditQA/validation.txt","FAIL\n"+error);throw;
        }
        finally
        {
            InteriorWallLayout.Restore(null);placed.Clear();placed.AddRange(previousPlaced);models.Clear();
            foreach(var pair in previousModels)models[pair.Key]=pair.Value;
            registryField.SetValue(null,oldRegistry);WorldSave.CaptureInteriorWalls=oldCapture;WorldSave.RestoreInteriorWalls=oldRestore;
        }
    }
}
