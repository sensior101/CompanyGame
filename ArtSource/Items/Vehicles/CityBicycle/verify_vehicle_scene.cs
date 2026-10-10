// Unity CLI eval_file in Play Mode. Reloads the current map through its established owner.
var p=UnityEngine.Object.FindAnyObjectByType<PlayerVehicle>();
var inv=p.GetComponent<PlayerInventory>();
if(!Application.isPlaying||p.IsRiding||inv.IsOpen)throw new Exception("Run on foot");
var motor=p.GetComponent<CharacterController>();var movement=p.GetComponent<PlayerMovement>();
var cam=movement.viewCamera.GetComponent<PlayerCameraController>();
var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var oldPos=p.transform.position;var oldRot=p.transform.rotation;
int oldSlot=inv.Inventory.SelectedHotbarIndex;
var savedOwners=PropertyRegistry.Instance.CaptureState();
bool background=Application.runInBackground;Application.runInBackground=true;
motor.enabled=false;p.transform.SetPositionAndRotation(new Vector3(-7,.025f,-53.2f),Quaternion.identity);motor.enabled=true;
var yaw=typeof(PlayerCameraController).GetField("yaw",flags);float oldYaw=(float)yaw.GetValue(cam);yaw.SetValue(cam,0f);cam.SendMessage("LateUpdate");
int index=Enumerable.Range(0,8).First(i=>!inv.Inventory.GetSlot(i).IsEmpty&&inv.Inventory.GetSlot(i).Item.IsVehicle);
inv.SelectHotbar(index);string key=inv.Inventory.GetSlot(index).OwnershipKey;
PropertyRegistry.Instance.TryRegister(key,GameSession.LocalPlayerName);
if(!WorldDroppedItem.TryPlaceVehicle(inv,out var error))throw new Exception(error);
var placed=WorldDroppedItem.FindNearestVehicle(inv);var placedPos=placed.transform.position;
if(!placed.TryMount(inv))throw new Exception("Mount failed");
string path=SceneLoadManager.CurrentMap.path;
double began=EditorApplication.timeSinceStartup;
EditorApplication.CallbackFunction callback=null;
callback=()=>{
    if(EditorApplication.timeSinceStartup-began<1 || SceneLoadManager.IsLoading)return;
    EditorApplication.update-=callback;
    WorldDroppedItem restored=null;
    string result;
    try{
        restored=UnityEngine.Object.FindObjectsByType<WorldDroppedItem>(FindObjectsSortMode.None).Single(d=>d.VehicleOwnershipKey==key);
        if(p.IsRiding||!restored.IsPlacedVehicle||!restored.VehicleLocked||restored.VehicleOwner!=GameSession.LocalPlayerName||!inv.Inventory.GetSlot(index).IsEmpty||Vector3.Distance(restored.transform.position,placedPos)>.01f)
            throw new Exception("Mounted scene reload lost vehicle state");
        result="PASS: mounted map reload dismounts; exactly one installed bicycle returns at saved position with same instance ID, owner and lock. Inventory slot remains empty until dismantled and picked up.";
    }catch(Exception ex){result="FAIL: "+ex.Message;}
    finally{
        if(restored){motor.enabled=false;p.transform.position=restored.transform.position+Vector3.right;motor.enabled=true;restored.TryDismantleVehicle(GameSession.LocalPlayerName,out _);restored.TryPickUp(inv,out _);}
        PropertyRegistry.Instance.RestoreState(savedOwners);inv.Inventory.SelectHotbar(oldSlot);
        motor.enabled=false;p.transform.SetPositionAndRotation(oldPos,oldRot);motor.enabled=true;yaw.SetValue(cam,oldYaw);Application.runInBackground=background;
    }
    System.IO.File.WriteAllText("../ArtSource/Items/Vehicles/CityBicycle/QA/SceneVerification.txt",result);
};
EditorApplication.update+=callback;
if(!SceneLoadManager.TryLoadMap(path,SceneLoadManager.DefaultSpawnId,movement)){EditorApplication.update-=callback;throw new Exception(SceneLoadManager.LastError);}
return "Queued mounted reload and identity preservation check";
