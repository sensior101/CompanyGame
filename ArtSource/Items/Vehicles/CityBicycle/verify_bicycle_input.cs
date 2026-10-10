// Unity CLI eval_file in Play Mode. Real mouse/key edges including the five-second gesture.
var p=UnityEngine.Object.FindAnyObjectByType<PlayerVehicle>();
var inv=p.GetComponent<PlayerInventory>();
if(!Application.isPlaying||p.IsRiding||inv.IsOpen)throw new Exception("Run on foot with inventory closed");
var interaction=p.GetComponent<PlayerInteraction>();
var motor=p.GetComponent<CharacterController>();
var camera=p.GetComponent<PlayerMovement>().viewCamera.GetComponent<PlayerCameraController>();
var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
var yaw=typeof(PlayerCameraController).GetField("yaw",flags);
var pitch=typeof(PlayerCameraController).GetField("pitch",flags);float oldPitch=(float)pitch.GetValue(camera);
float oldYaw=(float)yaw.GetValue(camera);bool oldView=camera.firstPerson;
var pos=p.transform.position;var rot=p.transform.rotation;
int slot=inv.Inventory.SelectedHotbarIndex;
int index=Enumerable.Range(0,8).First(i=>!inv.Inventory.GetSlot(i).IsEmpty&&inv.Inventory.GetSlot(i).Item.IsVehicle);
string key=inv.Inventory.GetSlot(index).OwnershipKey;
var owners=PropertyRegistry.Instance.CaptureState();
PropertyRegistry.Instance.TryAbandon(key,GameSession.LocalPlayerName);
var originalSettings=UnityEngine.InputSystem.InputSystem.settings;
var testSettings=UnityEngine.Object.Instantiate(originalSettings);
testSettings.backgroundBehavior=UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
testSettings.editorInputBehaviorInPlayMode=UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
UnityEngine.InputSystem.InputSystem.settings=testSettings;
bool background=Application.runInBackground;Application.runInBackground=true;
motor.enabled=false;p.transform.SetPositionAndRotation(new Vector3(-7,.025f,-53.2f),Quaternion.identity);motor.enabled=true;
camera.firstPerson=true;yaw.SetValue(camera,0f);camera.SendMessage("LateUpdate");inv.SelectHotbar(index);
WorldDroppedItem placed=null;
Vector2 surfacePointer=Vector2.zero,gapPointer=Vector2.zero,outsidePointer=Vector2.zero;
int stage=0,frame=0;
float began=Time.unscaledTime,stageStart=Time.unscaledTime;
var report=new System.Collections.Generic.List<string>();
Action callback=null;
Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);report.Add("PASS: "+label);};
Action next=()=>{stage++;frame=0;stageStart=Time.unscaledTime;};
Action<string> finish=message=>{
    UnityEngine.InputSystem.InputSystem.onBeforeUpdate-=callback;
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,new UnityEngine.InputSystem.LowLevel.KeyboardState());
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current,new UnityEngine.InputSystem.LowLevel.MouseState());
    interaction.CloseVehicleMenu();interaction.SendMessage("LateUpdate");
    if(p.IsRiding)p.Dismount(false);
    if(placed){motor.enabled=false;p.transform.position=placed.transform.position+Vector3.right;motor.enabled=true;if(placed.IsPlacedVehicle)placed.TryDismantleVehicle(GameSession.LocalPlayerName,out _);placed.TryPickUp(inv,out _);}
    motor.enabled=false;p.transform.SetPositionAndRotation(pos,rot);motor.enabled=true;
    yaw.SetValue(camera,oldYaw);pitch.SetValue(camera,oldPitch);camera.firstPerson=oldView;inv.Inventory.SelectHotbar(slot);Physics.SyncTransforms();
    PropertyRegistry.Instance.RestoreState(owners);
    UnityEngine.InputSystem.InputSystem.settings=originalSettings;UnityEngine.Object.Destroy(testSettings);Application.runInBackground=background;
    System.IO.File.WriteAllText("../ArtSource/Items/Vehicles/CityBicycle/QA/InputVerification.txt",string.Join("\n",report)+"\n"+message);
};
callback=()=>{
    if(UnityEngine.InputSystem.LowLevel.InputState.currentUpdateType!=UnityEngine.InputSystem.LowLevel.InputUpdateType.Dynamic)return;
    try{
        frame++;float elapsed=Time.unscaledTime-stageStart;
        if(Time.unscaledTime-began>40)throw new Exception("Timeout at stage "+stage);
        bool space=false,f=false,escape=false,click=false,rightClick=false;
        Vector2 pointer=new Vector2(20,Screen.height*.8f);
        if(placed)pointer=camera.GetComponent<Camera>().WorldToScreenPoint(placed.transform.position+Vector3.up*.68f);
        if(stage>=9)pointer=surfacePointer;
        switch(stage){
        case 0: space=frame==3;if(frame==8){check(PropertyRegistry.Instance.GetOwner(key)==GameSession.LocalPlayerName,"Space tap claims held vehicle");next();}break;
        case 1: space=elapsed<.8f;if(elapsed>1f){check(PropertyRegistry.Instance.GetOwner(key)==GameSession.LocalPlayerName,"Short Space hold cancels abandonment");next();}break;
        case 2: space=elapsed<5.25f;if(elapsed>5.45f){check(PropertyRegistry.Instance.GetOwner(key)==null,"Five-second Space hold abandons ownership");next();}break;
        case 3: space=frame==3;if(frame==8){check(PropertyRegistry.Instance.GetOwner(key)==GameSession.LocalPlayerName,"Reclaim after release");next();}break;
        case 4: click=frame==3;if(frame==8){placed=WorldDroppedItem.FindNearestVehicle(inv);check(placed&&placed.VehicleOwnershipKey==key&&!p.IsRiding,"Actual left click places same registered bicycle");next();}break;
        case 5: f=frame==3;if(frame==8){check(placed&&placed.IsPlacedVehicle&&inv.Inventory.GetSlot(index).IsEmpty,"F cannot retrieve installed bicycle");next();}break;
        case 6: space=frame==3;if(frame==8){check(p.IsRiding,"Actual Space mounts");var ui=UnityEngine.Object.FindAnyObjectByType<TransitUI>();check(ui.transform.Find("VehicleRidingHints").gameObject.activeSelf&&!ui.transform.Find("BoardingPrompt").gameObject.activeSelf,"Riding hints visible and central boarding popup hidden");next();}break;
        case 7: space=frame==3;if(frame==8){check(!p.IsRiding,"Actual Space dismounts");check(!UnityEngine.Object.FindAnyObjectByType<TransitUI>().transform.Find("VehicleRidingHints").gameObject.activeSelf,"Riding hints hidden on foot");next();}break;
        case 8:
            if(frame==1){motor.enabled=false;p.transform.position=placed.transform.position+new Vector3(1.7f,0,-.75f);motor.enabled=true;var dir=placed.transform.position-p.transform.position;yaw.SetValue(camera,Mathf.Atan2(dir.x,dir.z)*Mathf.Rad2Deg);pitch.SetValue(camera,27f);camera.SendMessage("LateUpdate");Physics.SyncTransforms();}
            if(frame==4){
                var collider=placed.GetComponent<MeshCollider>();check(collider&&!collider.convex&&collider.sharedMesh.triangles.Length>0,"Physics uses actual surface; selection uses screen silhouette");
                var exactHost=new GameObject("QA old convex envelope");exactHost.transform.SetPositionAndRotation(placed.transform.position,placed.transform.rotation);
                var oldHull=exactHost.AddComponent<MeshCollider>();oldHull.convex=true;oldHull.sharedMesh=collider.sharedMesh;
                Physics.SyncTransforms();
                float bestGap=float.MaxValue;Vector2 center=camera.GetComponent<Camera>().WorldToScreenPoint(placed.transform.TransformPoint(new Vector3(0,.44f,-.08f)));
                try{
                    outsidePointer=camera.GetComponent<Camera>().WorldToScreenPoint(placed.transform.TransformPoint(new Vector3(0,.96f,-.08f)));
                    var topRay=camera.GetComponent<Camera>().ScreenPointToRay(outsidePointer);
                    check(oldHull.Raycast(topRay,out _,20f)&&!collider.Raycast(topRay,out _,20f),"Regression point above low frame was incorrectly accepted by old convex envelope");
                    check(!placed.TryGetVehiclePointerHit(camera.GetComponent<Camera>(),outsidePointer,out _),"Open air above low frame is excluded by silhouette");
                    for(int y=230;y<Screen.height-40;y+=5)
                    for(int x=80;x<Screen.width-80;x+=5){
                        var point=new Vector2(x,y);var ray=camera.GetComponent<Camera>().ScreenPointToRay(point);
                        if(!collider.bounds.IntersectRay(ray))continue;
                        if(placed.TryGetVehiclePointerHit(camera.GetComponent<Camera>(),point,out _)){
                            if(collider.Raycast(ray,out _,20f)){if(surfacePointer==Vector2.zero)surfacePointer=point;}
                            else if((point-center).sqrMagnitude<bestGap){gapPointer=point;bestGap=(point-center).sqrMagnitude;}
                        }
                    }
                }finally{oldHull.enabled=false;UnityEngine.Object.Destroy(exactHost);}
                check(surfacePointer!=Vector2.zero&&gapPointer!=Vector2.zero&&outsidePointer!=Vector2.zero,"Located surface, enclosed frame interior and open concavity above bicycle");
            }
            if(frame==8)next();break;
        case 9: pointer=gapPointer;click=frame==3;if(frame==8){check(interaction.IsVehicleMenuOpen&&placed.IsHighlighted,"Hover and left click inside open frame interior highlight bicycle and open popup");next();}break;
        case 10: pointer=outsidePointer;escape=frame==2;rightClick=frame==4;click=frame==6;if(frame==9){check(placed.IsPlacedVehicle&&!interaction.IsVehicleMenuOpen&&!placed.IsHighlighted,"Air above bicycle ignores hover, left click and right click");next();}break;
        case 11: if(frame==8){check(placed.IsHighlighted,"Pointer on actual surface shows outline");next();}break;
        case 12: click=frame==3;if(frame==8){check(interaction.IsVehicleMenuOpen,"Actual surface left click opens cursor popup");next();}break;
        case 13:
            var popup=UnityEngine.Object.FindAnyObjectByType<TransitUI>().transform.Find("VehiclePermissions");
            pointer=RectTransformUtility.WorldToScreenPoint(null,popup.Find("ToggleVehicleLock").position);
            click=frame==3;if(frame==8){check(!placed.VehicleLocked,"Actual popup button click unlocks bicycle");next();}break;
        case 14: escape=frame==3;if(frame==8){check(!interaction.IsVehicleMenuOpen&&p.GetComponent<PlayerMovement>().enabled,"Escape closes popup and restores controls");next();}break;
        case 15: click=frame==2||frame==4||frame==6;if(frame==8){check(placed.IsPlacedVehicle&&placed.Count==1&&interaction.IsVehicleMenuOpen,"Three left clicks only open permissions; no dismantle");next();}break;
        case 16: pointer=gapPointer;escape=frame==2;rightClick=frame==5;if(frame==10){check(!placed.IsPlacedVehicle&&placed.Count==1&&!interaction.IsVehicleMenuOpen,"One right click inside open frame interior dismantles bicycle");next();}break;
        case 17: f=frame==3;if(frame==8){check(inv.Inventory.GetSlot(index).OwnershipKey==key&&!placed,"Actual F picks up same vehicle with ownership intact; status="+inv.StatusMessage);placed=null;finish("PASS: all real-input cases completed");return;}break;
        }
        var keys=space?new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Space):f?new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.F):escape?new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Escape):new UnityEngine.InputSystem.LowLevel.KeyboardState();
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current,keys);
        UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current,new UnityEngine.InputSystem.LowLevel.MouseState{position=pointer,buttons=(ushort)(click?1:rightClick?2:0)});
    }catch(Exception ex){finish("FAIL stage "+stage+" frame "+frame+": "+ex);}
};
UnityEngine.InputSystem.InputSystem.onBeforeUpdate+=callback;
return "Queued enclosed silhouette hover/click, open top concavity rejection, right-click dismantle and pickup checks";
