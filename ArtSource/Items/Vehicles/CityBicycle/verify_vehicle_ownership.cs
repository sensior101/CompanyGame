// Unity CLI eval_file in Play Mode. Exercises existing owners; restores session state.
var p=UnityEngine.Object.FindFirstObjectByType<PlayerVehicle>();
if(!Application.isPlaying || !p || p.IsRiding)throw new Exception("Run on foot in Play Mode");
var inv=p.GetComponent<PlayerInventory>();
var registry=PropertyRegistry.Instance;
var savedOwners=registry.CaptureState();
string actor=GameSession.LocalPlayerName;
var inventoryFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var original=typeof(InventoryState).GetMethod("Copy",inventoryFlags).Invoke(inv.Inventory,null);
var motor=p.GetComponent<CharacterController>();
var cam=p.GetComponent<PlayerMovement>().viewCamera.GetComponent<PlayerCameraController>();
var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var yaw=typeof(PlayerCameraController).GetField("yaw",flags);
float oldYaw=(float)yaw.GetValue(cam);
bool oldFirstPerson=cam.firstPerson;
var pos=p.transform.position;var rot=p.transform.rotation;
var report=new System.Collections.Generic.List<string>();
Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);report.Add("PASS: "+label);};
WorldDroppedItem placed=null;
try
{
    var item=Resources.LoadAll<ItemData>("Inventory/Vehicles/CityBicycle").First();
    var state=new InventoryState();state.TryAdd(item,2,out _);
    string first=state.GetSlot(0).InstanceId,second=state.GetSlot(1).InstanceId;
    check(first!=second && first!=null,"Same-color bicycles have distinct identities");
    state.TryMoveAmount(0,3,1,out _);
    check(state.GetSlot(3).InstanceId==first,"Slot movement retains identity");
    var trade=new TradeSession(state,Array.Empty<TradeOffer>());
    trade.TryPickUp(3,out _);trade.TryCancel(out _);
    check(state.GetSlot(3).InstanceId==first,"Trade cursor cancellation retains identity");
    trade.TryPickUp(3,out _);trade.TryPlace(4,out _);
    check(state.GetSlot(4).InstanceId==first,"Trade cursor placement retains identity");
    var destination=new InventoryState();state.TryTransferTo(destination,4,1,out _);
    check(destination.GetSlot(0).InstanceId==first && state.GetSlot(4).IsEmpty,"Atomic cross-inventory transfer retains identity");
    check(!destination.TryAdd(item,1,out _,first),"Duplicate instance transfer is rejected");
    var sale=new TradeSession(state,new[]{new TradeOffer{give=new TradeItem{item=item,count=1},get=new TradeItem{cash=1500,count=1}}});
    check(sale.TryTakeOffer(0,out _)&&!sale.TryTakeOffer(0,out _)&&sale.TryCancel(out _)&&state.GetSlot(1).InstanceId==second,"Vehicle sale cancellation restores exact paid vehicle; stacking sales rejected");
    string key=destination.GetSlot(0).OwnershipKey;
    check(registry.TryRegister(key,actor)&&registry.IsVehicleLocked(key),"Registration defaults to owner-only riding");
    check(!registry.TryRegister(key,"VehicleQA_Other")&&!registry.TryAbandon(key,"VehicleQA_Other")&&!registry.TrySetVehicleLocked(key,"VehicleQA_Other",false),"Another actor cannot claim, abandon or change lock");
    check(!registry.CanRideVehicle(key,"VehicleQA_Other")&&registry.CanRideVehicle(key,actor),"Locked: only owner allowed");
    check(registry.TrySetVehicleLocked(key,actor,false)&&registry.CanRideVehicle(key,"VehicleQA_Other"),"Unlocked: other actor allowed");
    registry.TrySetVehicleLocked(key,actor,true);var saved=registry.CaptureState();registry.RestoreState(saved);
    check(registry.GetOwner(key)==actor&&registry.IsVehicleLocked(key),"Ownership and lock survive capture/restore");
    check(registry.TryAbandon(key,actor)&&!registry.IsVehicleLocked(key)&&registry.TryRegister(key,"VehicleQA_Other"),"Abandonment releases ownership for another actor");
    motor.enabled=false;p.transform.SetPositionAndRotation(new Vector3(-7,.025f,-53.2f),Quaternion.identity);motor.enabled=true;
    yaw.SetValue(cam,0f);cam.SendMessage("LateUpdate");Physics.SyncTransforms();
    int slot=Enumerable.Range(0,8).First(i=>!inv.Inventory.GetSlot(i).IsEmpty&&inv.Inventory.GetSlot(i).Item.IsVehicle);
    inv.SelectHotbar(slot);string actualKey=inv.Inventory.GetSlot(slot).OwnershipKey;
    registry.TryRegister(actualKey,actor);
    check(WorldDroppedItem.TryPlaceVehicle(inv,out var error),"Place selected bicycle: "+error);
    placed=WorldDroppedItem.FindNearestVehicle(inv);
    check(placed&&placed.VehicleOwnershipKey==actualKey&&placed.VehicleLocked,"Placement retains identity, owner and lock");
    check(!placed.TryPickUp(inv,out _),"Installed bicycle rejects direct F pickup handler");
    GameSession.LocalPlayerName="VehicleQA_Other";
    check(!placed.TryMount(inv)&&!p.IsRiding,"Other actor is rejected by actual locked mount handler");
    placed.TryDismantleVehicle("VehicleQA_Other",out _);
    check(placed.IsPlacedVehicle,"Other actor cannot dismantle owner's bicycle");
    placed.TrySetVehicleLocked(actor,false);
    check(placed.TryMount(inv)&&p.IsRiding,"Unlocked bicycle can actually mount for other actor");
    check(!placed.TryDismantleVehicle(actor,out _)&&placed.IsPlacedVehicle,"Occupied bicycle cannot be dismantled");
    cam.firstPerson=true;cam.SendMessage("LateUpdate");
    check(Mathf.Abs((float)typeof(PlayerCameraController).GetField("currentBobOffset",flags).GetValue(cam))<.00001f,"Mounted first-person head bob remains zero");
    p.Dismount(false);GameSession.LocalPlayerName=actor;
    placed.TrySetVehicleLocked(actor,true);
    check(placed.TryDismantleVehicle(actor,out _),"Dismantle converts installed vehicle to drop");
    check(!placed.IsPlacedVehicle&&placed.VehicleOwnershipKey==actualKey&&placed.VehicleLocked,"Drop retains exact ownership and lock");
    check(placed.TryPickUp(inv,out _)&&inv.Inventory.GetSlot(slot).OwnershipKey==actualKey,"Drop pickup returns same bicycle to inventory");placed=null;
    check(Enumerable.Range(0,inv.Inventory.Capacity).Count(i=>inv.Inventory.GetSlot(i).OwnershipKey==actualKey)==1,"Full lifecycle does not duplicate vehicle");
}
finally
{
    GameSession.LocalPlayerName=actor;
    p.GetComponent<PlayerInteraction>().CloseVehicleMenu();
    if(p.IsRiding)p.Dismount(false);
    if(placed){if(placed.IsPlacedVehicle)placed.TryDismantleVehicle(actor,out _);placed.TryPickUp(inv,out _);}
    typeof(InventoryState).GetMethod("ReplaceWith",inventoryFlags).Invoke(inv.Inventory,new[]{original});
    typeof(InventoryState).GetMethod("NotifyChanged",inventoryFlags).Invoke(inv.Inventory,null);registry.RestoreState(savedOwners);
    motor.enabled=false;p.transform.SetPositionAndRotation(pos,rot);motor.enabled=true;
    yaw.SetValue(cam,oldYaw);cam.firstPerson=oldFirstPerson;Physics.SyncTransforms();
}
string result=string.Join("\n",report);
System.IO.File.WriteAllText("../ArtSource/Items/Vehicles/CityBicycle/QA/OwnershipVerification.txt",result);
return result;
