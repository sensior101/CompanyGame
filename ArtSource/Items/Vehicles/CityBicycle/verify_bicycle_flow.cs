// Unity CLI eval_file, Play Mode. Uses the existing player and restores its pose.
var player = UnityEngine.Object.FindFirstObjectByType<PlayerVehicle>();
if (!Application.isPlaying || !player || player.IsRiding) throw new Exception("Run while on foot in Play Mode.");
var inventory = player.GetComponent<PlayerInventory>();
if (inventory.IsOpen) throw new Exception("Close inventory before verification.");
var movement = player.GetComponent<PlayerMovement>();
var motor = player.GetComponent<CharacterController>();
var camera = movement.viewCamera.GetComponent<PlayerCameraController>();
var held = player.GetComponent<PlayerHeldItem>();
var state = inventory.Inventory;
int selected = state.SelectedHotbarIndex;
int index = Enumerable.Range(0,8).First(i=>!state.GetSlot(i).IsEmpty && state.GetSlot(i).Item.IsVehicle);
var item = state.GetSlot(index).Item;
var originalPosition = player.transform.position;
var originalRotation = player.transform.rotation;
bool originalFirstPerson = camera.firstPerson;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var yaw = typeof(PlayerCameraController).GetField("yaw",flags);
var bob = typeof(PlayerCameraController).GetField("currentBobOffset",flags);
float originalYaw = (float)yaw.GetValue(camera);
int originalCount = Enumerable.Range(0,state.Capacity).Sum(i=>state.GetSlot(i).Item==item?state.GetSlot(i).Count:0);
WorldDroppedItem placed = null;
var report = new System.Collections.Generic.List<string>();
try
{
    motor.enabled=false; player.transform.SetPositionAndRotation(new Vector3(-7,.025f,-53.2f),Quaternion.identity); motor.enabled=true;
    camera.firstPerson=true; yaw.SetValue(camera,0f); camera.SendMessage("LateUpdate");
    inventory.SelectHotbar(index); held.SendMessage("LateUpdate");
    if(held.HeldItem!=item || !held.FirstPersonItemRoot || !held.IsFirstPersonVisible) throw new Exception("Held model absent");
    report.Add("PASS: held bicycle appears in first person");
    if(!player.GetComponent<PlayerCombat>().TryAttack()) throw new Exception("Left-click placement handler failed: "+inventory.StatusMessage);
    placed=WorldDroppedItem.FindNearestVehicle(inventory);
    if(!placed || placed.Item!=item || player.IsRiding || !state.GetSlot(index).IsEmpty) throw new Exception("Placement must move one item to the ground without mounting");
    report.Add("PASS: click places full-size model and removes one inventory item without mounting");
    held.SendMessage("LateUpdate");
    if(held.HeldItem) throw new Exception("Placed item still held");
    if(!placed.TryMount(inventory)||!player.IsRiding) throw new Exception("Mount failed");
    if(placed.TryMount(inventory)||placed.TryPickUp(inventory,out _)) throw new Exception("Occupied item duplicated or collected");
    float maxBob=0, maxEyeError=0;
    for(int i=0;i<60;i++)
    {
        var velocity=player.RideMotion(Vector3.forward,true,.025f);
        motor.Move((velocity+Vector3.down*2f)*.025f);
        camera.firstPerson=i%9!=0;
        camera.SendMessage("LateUpdate");
        maxBob=Mathf.Max(maxBob,Mathf.Abs((float)bob.GetValue(camera)));
        if(camera.firstPerson)maxEyeError=Mathf.Max(maxEyeError,Mathf.Abs(camera.transform.position.y-player.transform.position.y-camera.firstPersonEyeHeight));
    }
    if(maxBob>.00001f||maxEyeError>.00001f)throw new Exception("Mounted camera bob: "+maxBob+" eye error "+maxEyeError);
    report.Add("PASS: riding + sprint + repeated view switches; max head bob="+maxBob+", max eye-height error="+maxEyeError);
    if(!player.Dismount() || player.IsRiding || !placed || !placed.GetComponent<Collider>().enabled)throw new Exception("Dismount failed");
    camera.firstPerson=true; camera.SendMessage("LateUpdate");
    for(int i=0;i<5;i++){motor.Move(Vector3.forward*.1f);camera.SendMessage("LateUpdate");}
    if(Mathf.Abs((float)bob.GetValue(camera))<.000001f)throw new Exception("Walking head bob did not resume");
    report.Add("PASS: dismount leaves bicycle on ground; walking head bob resumes");
    if(placed.TryPickUp(inventory,out _))throw new Exception("Installed bicycle allowed direct pickup");
    placed.TryDismantleVehicle(GameSession.LocalPlayerName,out _);
    if(placed.IsPlacedVehicle || !placed.TryPickUp(inventory,out var error))throw new Exception("Dismantle / pickup failed");
    int after=Enumerable.Range(0,state.Capacity).Sum(i=>state.GetSlot(i).Item==item?state.GetSlot(i).Count:0);
    if(after!=originalCount)throw new Exception("Quantity changed across complete cycle");
    placed=null;
    report.Add("PASS: right-click dismantle handler converts to drop; pickup returns exactly one item");
}
finally
{
    if(player.IsRiding)player.Dismount(false);
    if(placed)
    {
        motor.enabled=false;player.transform.position=placed.transform.position+Vector3.right;motor.enabled=true;
        if(placed.IsPlacedVehicle)placed.TryDismantleVehicle(GameSession.LocalPlayerName,out _);
        placed.TryPickUp(inventory,out _);
    }
    motor.enabled=false;player.transform.SetPositionAndRotation(originalPosition,originalRotation);motor.enabled=true;
    camera.firstPerson=originalFirstPerson;yaw.SetValue(camera,originalYaw);camera.SendMessage("UpdatePlayerVisuals");
    state.SelectHotbar(selected);Physics.SyncTransforms();
}
string result=string.Join("\n",report);
System.IO.File.WriteAllText("../ArtSource/Items/Vehicles/CityBicycle/QA/FlowVerification.txt",result);
return result;
