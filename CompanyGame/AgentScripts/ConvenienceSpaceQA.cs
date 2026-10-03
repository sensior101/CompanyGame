using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class ConvenienceSpaceQA
{
    public static async Task<string> Run()
    {
        if(!EditorApplication.isPlaying)throw new Exception("Play Mode required");
        var player=UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        var controller=player.GetComponent<CharacterController>();
        var inventory=player.GetComponent<PlayerInventory>();
        var stats=player.GetComponent<PlayerStats>();
        Application.runInBackground=true;
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        EditorApplication.QueuePlayerLoopUpdate();
        var row=inventory.UserInterface.transform.Find("QuickSlots/PlayerStatusBars");
        var checks=new Dictionary<string,bool>();
        var distances=new Dictionary<string,float>();
        var failures=new List<string>();
        var home=player.transform.position;var rotation=player.transform.rotation;
        bool enabled=player.enabled;
        float health=stats.health, stamina=stats.stamina, stress=stats.stress;
        GameObject probe=null;
        try
        {
            checks["Three icon-only bars"]=row&&row.childCount==3&&row.GetComponentsInChildren<TMPro.TMP_Text>(true).Length==0;
            stats.health=64;stats.stamina=43;stats.stress=27;
            await Task.Delay(200);
            string[] names={"Health","Stamina","Stress"};float[] ratios={.64f,.43f,.27f};
            for(int i=0;i<3;i++)checks[names[i]+" tracks player value"]=Mathf.Abs(((RectTransform)row.Find(names[i]+"/Track/Fill")).anchorMax.x-ratios[i])<.001f;
            checks["Bars do not block mouse"]=row.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g=>!g.raycastTarget);
            var corners=new Vector3[4];((RectTransform)row).GetWorldCorners(corners);
            checks["HUD inside screen"]=corners.All(p=>p.x>=0&&p.x<=Screen.width&&p.y>=0&&p.y<=Screen.height);
            var prompt=UnityEngine.Object.FindAnyObjectByType<TransitUI>().transform.Find("BoardingPrompt") as RectTransform;
            var promptCorners=new Vector3[4];prompt.GetWorldCorners(promptCorners);
            checks["Prompt clears all status bars"]=promptCorners[0].y>corners[1].y;
            stats.health=health;stats.stamina=stamina;stats.stress=stress;
            var npc=GameObject.Find("ConvenienceClerk");
            var rs=npc.transform.Find("MaleVisual").GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
            checks["Clerk stature 1.8m"]=Mathf.Abs(b.size.y-1.8f)<.02f;
            checks["Clerk feet at floor"]=Mathf.Abs(b.min.y-.016f)<.02f;
            player.enabled=false;
            var boxes=UnityEngine.Object.FindObjectsByType<BoxCollider>().Where(c=>c.enabled&&!c.isTrigger).ToArray();
            probe=new GameObject("Route_Clearance_Probe");var capsule=probe.AddComponent<CapsuleCollider>();
            capsule.radius=controller.radius+.03f;capsule.height=controller.height;capsule.center=controller.center;
            probe.transform.position=Vector3.down*100f;
            const float step=.10f;const int nx=83,nz=94;
            Func<int,Vector3> point=id=>new Vector3(-4.1f+(id%nx)*step,.03f,.3f+(id/nx)*step);
            Func<Vector3,int> cell=p=>Mathf.Clamp(Mathf.RoundToInt((p.x+4.1f)/step),0,nx-1)+nx*Mathf.Clamp(Mathf.RoundToInt((p.z-.3f)/step),0,nz-1);
            Func<Vector3,bool> free=p=>!boxes.Any(box=>Physics.ComputePenetration(capsule,p,Quaternion.identity,box,box.transform.position,box.transform.rotation,out var dir,out var depth)&&depth>.002f);
            Physics.SyncTransforms();
            var clear=new bool[nx*nz];for(int i=0;i<clear.Length;i++)clear[i]=free(point(i));
            int start=cell(home);var prev=Enumerable.Repeat(-1,clear.Length).ToArray();var q=new Queue<int>();prev[start]=start;q.Enqueue(start);
            while(q.Count>0)
            {
                int id=q.Dequeue(),x=id%nx,z=id/nx;
                foreach(var d in new[]{new Vector2Int(1,0),new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(0,-1)})
                {
                    int xx=x+d.x,zz=z+d.y;if(xx<0||xx>=nx||zz<0||zz>=nz)continue;
                    int n=xx+zz*nx;if(!clear[n]||prev[n]!=-1||!free(Vector3.Lerp(point(id),point(n),.5f)))continue;
                    prev[n]=id;q.Enqueue(n);
                }
            }
            var targets=new Dictionary<string,Vector3>{
                {"Checkout",new Vector3(-1.56f,0,4.85f)},
                {"Staff front access",new Vector3(-3.50f,0,2.7f)},
                {"Staff rear access",new Vector3(-3.50f,0,6.5f)},
                {"Left aisle rear",new Vector3(-1.65f,0,8.1f)},
                {"Central aisle",new Vector3(.5f,0,6.1f)},
                {"Rear cross aisle left",new Vector3(.1f,0,8.35f)},
                {"Rear cross aisle middle",new Vector3(1.45f,0,8.35f)},
                {"Rear cross aisle right",new Vector3(2.9f,0,8.35f)},
                {"Right refrigerators",new Vector3(2.85f,0,6.5f)},
                {"Ready meals",new Vector3(2.85f,0,4.9f)},
                {"Freezer left",new Vector3(2.05f,0,3.35f)},
                {"Freezer right",new Vector3(4.05f,0,3.0f)},
                {"Between chairs and freezer",new Vector3(3.15f,0,2.1f)},
                {"Seating",new Vector3(1.65f,0,1.6f)}
            };
            foreach(var target in targets)
            {
                int end=cell(target.Value);bool connected=clear[end]&&prev[end]>=0;
                checks[target.Key+" full path"]=connected;if(!connected)continue;
                var path=new List<int>();for(int n=end;n!=start;n=prev[n])path.Add(n);path.Add(start);path.Reverse();
                controller.enabled=false;player.transform.position=point(start);controller.enabled=true;Physics.SyncTransforms();
                bool reached=true;
                foreach(int n in path.Skip(1))
                {
                    var goal=point(n);
                    for(int k=0;k<4;k++)
                    {
                        var delta=Vector3.ProjectOnPlane(goal-player.transform.position,Vector3.up);
                        controller.Move(Vector3.ClampMagnitude(delta,.04f)+Vector3.down*.01f);
                    }
                    if(Vector3.ProjectOnPlane(goal-player.transform.position,Vector3.up).magnitude>.07f){failures.Add(target.Key+" goal="+goal+" actual="+player.transform.position);reached=false;break;}
                }
                checks[target.Key+" actual controller traversal"]=reached&&Vector3.ProjectOnPlane(point(end)-player.transform.position,Vector3.up).magnitude<.1f;
                distances[target.Key]=path.Count*step;
            }
            var camera=player.viewCamera.GetComponent<PlayerCameraController>();
            bool cameraEnabled=camera.enabled;var cameraRotation=player.viewCamera.transform.rotation;
            Keyboard keyboard=null;
            try
            {
                controller.enabled=false;player.transform.position=new Vector3(.1f,.03f,8.35f);controller.enabled=true;
                camera.enabled=false;player.viewCamera.transform.rotation=Quaternion.Euler(0,90,0);player.enabled=true;
                keyboard=InputSystem.AddDevice<Keyboard>();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
                float started=Time.time;
                for(int k=0;k<20&&Time.time-started<.6f;k++){EditorApplication.QueuePlayerLoopUpdate();await Task.Delay(50);}
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                checks["W key crosses rear aisle"]=player.transform.position.x>.8f&&Mathf.Abs(player.transform.position.z-8.35f)<.15f;
            }
            finally {if(keyboard!=null)InputSystem.RemoveDevice(keyboard);camera.enabled=cameraEnabled;player.viewCamera.transform.rotation=cameraRotation;}
            controller.enabled=false;player.transform.position=new Vector3(-1.56f,.03f,4.85f);controller.enabled=true;
            var interaction=player.GetComponent<PlayerInteraction>();
            checks["Moved checkout still opens trade"]=interaction.TryUseStore();
            EditorApplication.QueuePlayerLoopUpdate();await Task.Delay(200);
            checks["HUD hidden during trade"]=!row.gameObject.activeInHierarchy;
            interaction.CloseStore();EditorApplication.QueuePlayerLoopUpdate();await Task.Delay(250);
            checks["HUD restored after trade"]=row.gameObject.activeInHierarchy;
        }
        finally
        {
            if(probe)UnityEngine.Object.DestroyImmediate(probe);
            stats.health=health;stats.stamina=stamina;stats.stress=stress;
            controller.enabled=false;player.transform.SetPositionAndRotation(home,rotation);controller.enabled=true;player.enabled=enabled;
        }
        return Newtonsoft.Json.JsonConvert.SerializeObject(new{checks,distances,failures,passed=checks.Values.Count(v=>v),failed=checks.Values.Count(v=>!v)},Newtonsoft.Json.Formatting.Indented);
    }
}
