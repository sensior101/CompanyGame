// Unity CLI eval_file, Play Mode: named interior/exterior points from two camera angles.
var p=UnityEngine.Object.FindAnyObjectByType<PlayerVehicle>();
if(!Application.isPlaying||!p||p.IsRiding)throw new Exception("Run on foot in Play Mode");
var inv=p.GetComponent<PlayerInventory>();var move=p.GetComponent<PlayerMovement>();var motor=p.GetComponent<CharacterController>();
var camera=move.viewCamera.GetComponent<PlayerCameraController>();var view=move.viewCamera;
var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
var yaw=typeof(PlayerCameraController).GetField("yaw",flags);var pitch=typeof(PlayerCameraController).GetField("pitch",flags);
var oldPosition=p.transform.position;var oldRotation=p.transform.rotation;var oldYaw=yaw.GetValue(camera);var oldPitch=pitch.GetValue(camera);bool oldView=camera.firstPerson;
int oldSlot=inv.Inventory.SelectedHotbarIndex,index=Enumerable.Range(0,8).First(i=>!inv.Inventory.GetSlot(i).IsEmpty&&inv.Inventory.GetSlot(i).Item.IsVehicle);
WorldDroppedItem bike=null;
var report=new System.Collections.Generic.List<string>();
Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);report.Add("PASS: "+label);};
try{
    motor.enabled=false;p.transform.SetPositionAndRotation(new Vector3(-7,.025f,-53.2f),Quaternion.identity);motor.enabled=true;
    camera.firstPerson=true;yaw.SetValue(camera,0f);camera.SendMessage("LateUpdate");inv.SelectHotbar(index);Physics.SyncTransforms();
    if(!WorldDroppedItem.TryPlaceVehicle(inv,out var error))throw new Exception(error);
    bike=WorldDroppedItem.FindNearestVehicle(inv);
    int angle=0;
    foreach(var offset in new[]{new Vector3(1.7f,0,-.75f),new Vector3(2,0,0)}){
        motor.enabled=false;p.transform.position=bike.transform.position+offset;motor.enabled=true;
        var dir=bike.transform.position-p.transform.position;yaw.SetValue(camera,Mathf.Atan2(dir.x,dir.z)*Mathf.Rad2Deg);pitch.SetValue(camera,27f);camera.SendMessage("LateUpdate");Physics.SyncTransforms();
        var top=view.WorldToScreenPoint(bike.transform.TransformPoint(new Vector3(0,.96f,-.08f)));
        var frame=view.WorldToScreenPoint(bike.transform.TransformPoint(new Vector3(0,.44f,-.08f)));
        var wheel=view.WorldToScreenPoint(bike.transform.TransformPoint(new Vector3(0,.48f,.68f)));
        var timer=System.Diagnostics.Stopwatch.StartNew();bool topHit=bike.TryGetVehiclePointerHit(view,top,out _);timer.Stop();
        check(!topHit,"Angle "+angle+": open air above the low frame is excluded");
        check(bike.TryGetVehiclePointerHit(view,frame,out _),"Angle "+angle+": enclosed frame interior is selectable");
        check(bike.TryGetVehiclePointerHit(view,wheel,out _),"Angle "+angle+": wheel interior is selectable");
        check(WorldDroppedItem.FindPointedVehicle(inv,view,frame)==bike,"Angle "+angle+": enclosed interior routes to this bicycle");
        var obstruction=GameObject.CreatePrimitive(PrimitiveType.Cube);
        try{
            var ray=view.ScreenPointToRay(frame);obstruction.transform.position=ray.GetPoint(.6f);obstruction.transform.localScale=Vector3.one*.18f;Physics.SyncTransforms();
            check(!WorldDroppedItem.FindPointedVehicle(inv,view,frame),"Angle "+angle+": foreground obstruction blocks selection");
        }finally{UnityEngine.Object.DestroyImmediate(obstruction);Physics.SyncTransforms();}
        report.Add("Mask rebuild ms: "+timer.Elapsed.TotalMilliseconds.ToString("F2"));
        int width=(int)typeof(WorldDroppedItem).GetField("selectionWidth",flags).GetValue(bike),height=(int)typeof(WorldDroppedItem).GetField("selectionHeight",flags).GetValue(bike);
        int ox=(int)typeof(WorldDroppedItem).GetField("selectionX",flags).GetValue(bike),oy=(int)typeof(WorldDroppedItem).GetField("selectionY",flags).GetValue(bike);
        var mask=(byte[])typeof(WorldDroppedItem).GetField("selectionMask",flags).GetValue(bike);
        var colors=new Color32[width*height];
        for(int i=0;i<colors.Length;i++)colors[i]=mask[i]==1?new Color32(220,228,234,255):mask[i]==3?new Color32(78,205,140,255):new Color32(24,30,38,255);
        float pixelSize=(float)typeof(WorldDroppedItem).GetField("SelectionPixelSize",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetRawConstantValue();
        Action<Vector3,Color32> mark=(point,color)=>{int cx=Mathf.FloorToInt(point.x/pixelSize)-ox,cy=Mathf.FloorToInt(point.y/pixelSize)-oy;for(int j=-3;j<=3;j++)for(int i=-3;i<=3;i++)if((Mathf.Abs(i)==3||Mathf.Abs(j)==3)&&cx+i>=0&&cx+i<width&&cy+j>=0&&cy+j<height)colors[(cy+j)*width+cx+i]=color;};
        mark(top,new Color32(255,80,80,255));mark(frame,new Color32(255,210,60,255));mark(wheel,new Color32(255,210,60,255));
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);texture.SetPixels32(colors);texture.Apply();
        System.IO.File.WriteAllBytes("../ArtSource/Items/Vehicles/CityBicycle/QA/Silhouette_"+angle+".png",texture.EncodeToPNG());UnityEngine.Object.Destroy(texture);angle++;
    }
}finally{
    if(bike){bike.TryDismantleVehicle(GameSession.LocalPlayerName,out _);bike.TryPickUp(inv,out _);}
    motor.enabled=false;p.transform.SetPositionAndRotation(oldPosition,oldRotation);motor.enabled=true;
    camera.firstPerson=oldView;yaw.SetValue(camera,oldYaw);pitch.SetValue(camera,oldPitch);inv.Inventory.SelectHotbar(oldSlot);Physics.SyncTransforms();
}
string result=string.Join("\n",report);System.IO.File.WriteAllText("../ArtSource/Items/Vehicles/CityBicycle/QA/SilhouetteVerification.txt",result);return result;
