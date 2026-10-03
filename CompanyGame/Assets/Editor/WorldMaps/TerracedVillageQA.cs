using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace CompanyGame.Editor.WorldMaps
{
    public static class TerracedVillageQA
    {
        const string Output=TerracedVillageExpansion.Output;
        static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
        static GameObject Root()=>SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="Map_daldongnaemap");
        static void Write(string name,object value){Directory.CreateDirectory(Output);File.WriteAllText(Output+"/"+name+".json",JsonConvert.SerializeObject(value,Formatting.Indented));}
        public static object Capture()
        {
            var root=Root();Directory.CreateDirectory(Output);var original=root.GetComponentInChildren<Camera>();var go=new GameObject("Temporary terraced village photograph"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(original);camera.enabled=false;camera.orthographic=false;camera.farClipPlane=400;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            try
            {
                Shot(camera,"Overview",V(91,87,-105),V(0,9,10),46);
                Shot(camera,"ResidentialAlley",V(-35,27,42),V(-5,18,59),58);
                Shot(camera,"KoreanHomes",V(38,25,71),V(0,18,59),53);
                Shot(camera,"OriginalShopsAndSteps",V(49,25,-27),V(12,7,8),52);
                Shot(camera,"VisibleSlope",V(-45,15,-9),V(-37,7,9),52);
            }
            finally{Object.DestroyImmediate(go);}
            return new{images=5,folder=Path.GetFullPath(Output)};
        }
        static void Shot(Camera camera,string name,Vector3 position,Vector3 focus,float fov)
        {
            camera.transform.position=position;camera.transform.LookAt(focus);camera.fieldOfView=fov;
            var rt=RenderTexture.GetTemporary(1600,1100,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default,4);var previous=RenderTexture.active;var texture=new Texture2D(1600,1100,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1600,1100),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(texture);}
        }
        public static object Navigation()
        {
            var root=Root();var map=root.transform.Find("10_World/Daldongne Warm Village");
            var routes=new List<TerracedVillageExpansion.Route>();
            foreach(var r in (JArray)JObject.Parse(File.ReadAllText("../ArtSource/Daldongne/warm_routes.json"))["routes"])
            {
                Func<JToken,Vector3> p=t=>map.TransformPoint(V((float)t[0],(float)t[2],(float)t[1]));
                routes.Add(new TerracedVillageExpansion.Route{name=(string)r["name"],width=(float)r["width"]*1.8f,points=new[]{p(r["a"]),p(r["b"])}});
            }
            routes.AddRange(JsonUtility.FromJson<TerracedVillageExpansion.Routes>(File.ReadAllText(Output+"/additional-routes.json")).routes);
            Func<Vector3,Vector3> world=p=>map.TransformPoint(p);
            routes.Add(new TerracedVillageExpansion.Route{name="GosiwonEastPromenade",width=3.24f,points=new[]{world(V(-6.2f,12,29)),world(V(-6.2f,12,20.55f))}});
            routes.Add(new TerracedVillageExpansion.Route{name="GosiwonFrontTerrace",width=3.24f,points=new[]{world(V(-6.2f,12,20.55f)),world(V(-11.08f,12,20.55f))}});
            routes.Add(new TerracedVillageExpansion.Route{name="BakeryFront",width=2.88f,points=new[]{world(V(13.6f,3,-3.7f)),world(V(20.4f,3,-3.7f))}});
            var motorObject=new GameObject("Temporary movement probe"){hideFlags=HideFlags.HideAndDontSave};var motor=motorObject.AddComponent<CharacterController>();
            motor.radius=.35f;motor.height=1.8f;motor.center=Vector3.up*.9f;motor.stepOffset=.23f;motor.slopeLimit=45;motor.skinWidth=.02f;motor.minMoveDistance=0;
            var player=root.GetComponentInChildren<PlayerMovement>().GetComponent<CharacterController>();bool enabled=player.enabled;player.enabled=false;Physics.SyncTransforms();
            var records=new List<object>();var failures=new List<object>();
            try
            {
                foreach(var route in routes)foreach(float lane in new[]{-Mathf.Max(.4f,route.width/2-.55f),0,Mathf.Max(.4f,route.width/2-.55f)})foreach(bool reverse in new[]{false,true})
                {
                    var points=route.points.Select((p,i)=>{var d=route.points[Mathf.Min(i+1,route.points.Length-1)]-route.points[Mathf.Max(i-1,0)];d.y=0;return p+Vector3.Cross(Vector3.up,d.normalized)*lane;}).ToArray();if(reverse)Array.Reverse(points);
                    motor.enabled=false;motor.transform.position=points[0]+Vector3.up*.08f;motor.enabled=true;Physics.SyncTransforms();for(int i=0;i<15;i++)motor.Move(Vector3.down*.02f);
                    bool passed=true;float vertical=-2;int segment=0;
                    foreach(var target in points.Skip(1))
                    {
                        segment++;float distance=Vector2.Distance(new Vector2(target.x,target.z),new Vector2(motor.transform.position.x,motor.transform.position.z));int frames=Mathf.CeilToInt(distance/.075f)*4+60;int stalled=0;
                        for(int frame=0;frame<frames;frame++)
                        {
                            var remaining=target-motor.transform.position;remaining.y=0;if(remaining.magnitude<.065f)break;
                            if(motor.isGrounded&&vertical<0)vertical=-2;vertical+=Physics.gravity.y/60;var old=motor.transform.position;motor.Move(Vector3.ClampMagnitude(remaining,.075f)+Vector3.up*(vertical/60));
                            var moved=motor.transform.position-old;moved.y=0;stalled=moved.magnitude<.0015f?stalled+1:0;
                            if(stalled>25||motor.transform.position.y<Mathf.Min(points[0].y,target.y)-1){passed=false;break;}
                        }
                        var delta=target-motor.transform.position;delta.y=0;if(delta.magnitude>.13f||Mathf.Abs(target.y-motor.transform.position.y)>.32f)passed=false;if(!passed)break;
                    }
                    var item=new{route=route.name,lane,reverse,passed,segment,position=motor.transform.position.ToString("F3"),nearby=passed?null:Physics.OverlapCapsule(motor.transform.position+V(0,.4f,0),motor.transform.position+V(0,1.4f,0),.43f).Where(c=>c!=motor).Select(c=>c.name).Distinct().ToArray()};
                    records.Add(item);if(!passed)failures.Add(item);
                }
                motor.enabled=false;
                var spawns=root.GetComponentsInChildren<MapSpawnPoint>().Where(s=>s.spawnId.StartsWith("social_")).ToArray();var spawnRecords=new List<object>();
                foreach(var spawn in spawns){var p=spawn.transform.position;bool floor=Physics.Raycast(p+Vector3.up*.2f,Vector3.down,1,~0,QueryTriggerInteraction.Ignore);bool clear=!Physics.CheckCapsule(p+V(0,.4f,0),p+V(0,1.4f,0),.35f,~0,QueryTriggerInteraction.Ignore);bool spaced=spawns.Where(s=>s!=spawn).All(s=>Vector3.Distance(p,s.transform.position)>=1.9f);var item=new{spawn=spawn.spawnId,passed=floor&&clear&&spaced,floor,clear,spaced};spawnRecords.Add(item);if(!item.passed)failures.Add(item);}
                var report=new{passed=failures.Count==0,mode=Application.isPlaying?"Play Mode":"Edit Mode",routes=routes.Count,traversals=records.Count,spawnCount=spawns.Length,failures,spawnRecords,records};Write("navigation",report);
                return new{report.passed,report.mode,report.routes,report.traversals,report.spawnCount,failures};
            }
            finally{player.enabled=enabled;Object.DestroyImmediate(motorObject);Physics.SyncTransforms();}
        }
        public static object Audit()
        {
            var root=Root();var map=root.transform.Find("10_World/Daldongne Warm Village");var errors=new List<string>();
            var buildings=map.Find("10_Buildings").Cast<Transform>().ToArray();int residential=buildings.Count(t=>t.name.StartsWith("Home_")||t.name=="Gosiwon"||t.name.StartsWith("OneRoom_"));
            int oneRooms=root.transform.Find("10_World/One Room Neighborhood")?.childCount??0;
            if(oneRooms!=10)errors.Add("Expected 10 one-room buildings.");
            if(buildings.Count(t=>t.name.Contains("Gosiwon"))!=1)errors.Add("Keep exactly the original Gosiwon.");
            if(Vector3.Distance(map.localScale,V(1.8f,1.2f,1.8f))>.01f)errors.Add("Wrong horizontal scale.");
            foreach(var t in buildings)if(Vector3.Distance(t.lossyScale,t.name=="Station"?V(1.8f,1.2f,1.8f):Vector3.one*1.2f)>.015f)errors.Add("Building scale changed: "+t.name);
            foreach(var t in root.GetComponentsInChildren<Transform>(true))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)errors.Add("Missing script: "+t.name);
            foreach(var r in root.GetComponentsInChildren<Renderer>(true))if(r.sharedMaterials.Any(m=>!m||!m.shader||m.shader.name=="Hidden/InternalErrorShader"))errors.Add("Missing material: "+r.name);
            foreach(var c in root.GetComponentsInChildren<MeshCollider>(true))if(!c.sharedMesh)errors.Add("Missing collision: "+c.name);
            foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))if(!f.sharedMesh)errors.Add("Missing mesh: "+f.name);
            var steps=map.Find("00_Terrain/Stairs").Cast<Transform>().Where(t=>t.GetComponentsInChildren<Renderer>().Any(r=>r.enabled&&r.name.StartsWith("Walk_Stair_"))).Select(t=>t.name).ToArray();
            var slopes=map.Find("40_Colliders").Cast<Transform>().Where(t=>t.GetComponent<Renderer>()&&t.GetComponent<Renderer>().enabled).Select(t=>t.name).ToArray();
            if(steps.Length<9||slopes.Length!=3)errors.Add("Stair/slope mix is wrong.");
            if(EditorUtility.scriptCompilationFailed)errors.Add("Compilation failed.");
            var result=new{passed=errors.Count==0,errors,residential=residential+oneRooms,oneRooms,buildings=buildings.Length+oneRooms,preservedOriginalBuildings=buildings.Count(t=>!t.name.StartsWith("OneRoom_")),horizontalExpansion=1.5f,originalAreaMultiplier=2.25f,preservedLevelsMetres=new[]{1.2f,3.6f,7.2f,10.8f,14.4f},steps,slopes,
                sceneGuid=AssetDatabase.AssetPathToGUID(TerracedVillageExpansion.ScenePath),compileErrors=EditorUtility.scriptCompilationFailed};Write("assets",result);return result;
        }
        public static object RemoveDraftScenes()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play mode.");Root();const string backupFolder="../_temp/TerracedExpansionRecovery";Directory.CreateDirectory(backupFolder);var removed=new List<string>();
            var paths=new List<string>{"Assets/Scenes/DaldongneMap.unity","Assets/Scenes/SampleScene.unity"};
            if(AssetDatabase.IsValidFolder("Assets/_Recovery"))paths.AddRange(AssetDatabase.FindAssets("t:Scene",new[]{"Assets/_Recovery"}).Select(AssetDatabase.GUIDToAssetPath));
            foreach(var path in paths)
            {
                if(!File.Exists(path))continue;if(SceneManager.GetSceneByPath(path).isLoaded)throw new Exception("Draft open: "+path);
                // Recovery scenes were untracked at task start. Preserve a local copy outside Assets.
                if(path.StartsWith("Assets/_Recovery")){string backup=backupFolder+"/"+Path.GetFileName(path)+".backup";if(!File.Exists(backup))File.Copy(path,backup);}
                if(!AssetDatabase.DeleteAsset(path))throw new IOException("Could not remove "+path);removed.Add(path);
            }
            if(AssetDatabase.IsValidFolder("Assets/_Recovery")&&Directory.GetFiles("Assets/_Recovery").Length==0)AssetDatabase.DeleteAsset("Assets/_Recovery");
            EditorBuildSettings.scenes=EditorBuildSettings.scenes.Where(s=>File.Exists(s.path)).ToArray();Write("retired-scenes",new{removed});return removed;
        }
    }
}
