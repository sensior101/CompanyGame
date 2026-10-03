using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.World.Maps;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using static CompanyGame.Editor.WorldMaps.DistrictGeometry;

namespace CompanyGame.Editor.WorldMaps
{
    [InitializeOnLoad]
    public static class DistrictMapQA
    {
        const string Output="../ArtSource/WorldDistricts/QA/";
        const string Session="CompanyGame.DistrictTour";
        [Serializable] sealed class Tour {public int index=-1;public int frame;public double deadline;public bool finished;public bool success;public bool restore;public bool originalBackground;public string error;public List<string> checks=new List<string>();}
        static DistrictMapQA(){EditorApplication.update+=Tick;}
        static void Write(string file,object value){Directory.CreateDirectory(Output);File.WriteAllText(Output+file+".json",JsonConvert.SerializeObject(value,Formatting.Indented));}
        static void Open(int i){if(EditorApplication.isPlaying)throw new Exception("Stop Play first");var current=SceneManager.GetActiveScene();if(current.isDirty)throw new Exception("Save current scene first");EditorSceneManager.OpenScene(CityDistrictBuilder.PathFor(i),OpenSceneMode.Single);}
        public static object Inspect(int i,bool photographs=true)
        {
            Open(i);var scene=SceneManager.GetActiveScene();var root=scene.GetRootGameObjects().Single(g=>g.name=="Map_"+CityDistrictBuilder.Names[i]);var failures=new List<string>();
            var players=root.GetComponentsInChildren<PlayerMovement>();var cameras=root.GetComponentsInChildren<Camera>();var spawns=root.GetComponentsInChildren<MapSpawnPoint>();
            if(players.Length!=1||cameras.Length!=1)failures.Add("Expected one player and camera");
            if(players.Length==1&&cameras.Length==1){var camera=cameras[0].GetComponent<PlayerCameraController>();if(!camera||camera.target!=players[0].transform||players[0].viewCamera!=cameras[0])failures.Add("Player/camera references");}
            foreach(var t in root.GetComponentsInChildren<Transform>(true))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)failures.Add("Missing script "+t.name);
            foreach(var f in root.GetComponentsInChildren<MeshFilter>())if(!f.sharedMesh)failures.Add("Missing mesh "+f.name);
            foreach(var r in root.GetComponentsInChildren<Renderer>())if(r.sharedMaterials.Any(m=>!m||!m.shader||m.shader.name=="Hidden/InternalErrorShader"))failures.Add("Missing material "+r.name);
            foreach(var portal in root.GetComponentsInChildren<MapPortal>())if(!EditorBuildSettings.scenes.Any(s=>s.enabled&&s.path==portal.targetScenePath))failures.Add("Unregistered destination "+portal.targetScenePath);
            var buildings=root.transform.Find("10_World/Buildings").Cast<Transform>().ToArray();int lots=buildings.Count(t=>t.name.Contains("VacantPlot"));int expected=new[]{4,6,10,5,5}[i];if(lots!=expected)failures.Add("Vacant plots "+lots+" expected "+expected);
            // Buildings and vacant plots must not occupy each other's solid footprint.
            var bounds=new List<KeyValuePair<string,Bounds>>();foreach(var b in buildings){var cs=b.GetComponentsInChildren<Collider>();if(cs.Length==0)continue;var box=cs[0].bounds;foreach(var c in cs.Skip(1))box.Encapsulate(c.bounds);bounds.Add(new KeyValuePair<string,Bounds>(b.name,box));}
            for(int a=0;a<bounds.Count;a++)for(int b=a+1;b<bounds.Count;b++)
            {var x=bounds[a].Value;var y=bounds[b].Value;if(Mathf.Min(x.max.x,y.max.x)-Mathf.Max(x.min.x,y.min.x)>.1f&&Mathf.Min(x.max.z,y.max.z)-Mathf.Max(x.min.z,y.min.z)>.1f)failures.Add("Footprint overlap: "+bounds[a].Key+" / "+bounds[b].Key);}
            // Ground is authored as continuous slabs. Sample the entire playable area,
            // including roadside edges and gray plots; intentional water is excluded.
            Physics.SyncTransforms();int samples=0;var cc=players[0].GetComponent<CharacterController>();bool enabled=cc.enabled;cc.enabled=false;
            for(float x=-70;x<=70;x+=1.5f)for(float z=-70;z<=70;z+=1.5f)
            {if(i==2&&Mathf.Abs(z)<10&&Mathf.Abs(x-40)>6&&Mathf.Abs(x+40)>6)continue;if(i==3&&z<-55)continue;samples++;float depth=TransitMapBuilder.Excavation(i).Contains(new Vector2(x,z))?3.1f:1.2f;if(!Physics.Raycast(V(x,.19f,z),Vector3.down,out var h,depth,~0,QueryTriggerInteraction.Ignore))failures.Add("Ground gap at "+x+","+z);}
            foreach(var spawn in spawns)
            {var p=spawn.transform.position;if(!Physics.Raycast(p+Vector3.up*.2f,Vector3.down,1,~0,QueryTriggerInteraction.Ignore))failures.Add("Spawn floor "+spawn.spawnId);if(Physics.CheckCapsule(p+V(0,.4f,0),p+V(0,1.4f,0),.35f,~0,QueryTriggerInteraction.Ignore))failures.Add("Spawn blocked "+spawn.spawnId);}
            cc.enabled=enabled;
            var result=new{scene=scene.path,passed=failures.Count==0,footprint="144 x 144 m",buildings=buildings.Length-lots,vacantPlots=lots,spawns=spawns.Length,groundSamples=samples,meshRenderers=root.GetComponentsInChildren<MeshRenderer>().Length,failures};Write(CityDistrictBuilder.Names[i]+"-audit",result);
            if(photographs)
            {
                VillageSurfaceRepair.Photograph(CityDistrictBuilder.Names[i]+"-overview",V(118,i==2||i==4?143:107,-137),V(0,i==2||i==4?15:3,5),53);
                VillageSurfaceRepair.Photograph(CityDistrictBuilder.Names[i]+"-street",i==2?V(-10,8,-3):i==4?V(7,6,-29):V(-4,5,-37),i==2?V(-20,30,52):i==4?V(14,13,20):V(-20,5,-18),66);
            }
            return result;
        }
        public static string StartTour()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Already playing");if(SceneManager.GetActiveScene().isDirty)throw new Exception("Save scene first");
            EditorSceneManager.OpenScene(TerracedVillageExpansion.ScenePath,OpenSceneMode.Single);EditorSceneManager.playModeStartScene=null;
            var tour=new Tour{deadline=EditorApplication.timeSinceStartup+240,originalBackground=PlayerSettings.runInBackground};SessionState.SetString(Session,JsonUtility.ToJson(tour));Application.runInBackground=true;EditorApplication.EnterPlaymode();return "Tour started";
        }
        public static string TourStatus()=>SessionState.GetString(Session,"No tour");
        static void Tick()
        {
            string json=SessionState.GetString(Session,"");if(string.IsNullOrEmpty(json))return;var t=JsonUtility.FromJson<Tour>(json);if(t.finished)return;
            try
            {
                if(t.restore){if(EditorApplication.isPlayingOrWillChangePlaymode)return;EditorSceneManager.OpenScene(TerracedVillageExpansion.ScenePath,OpenSceneMode.Single);PlayerSettings.runInBackground=t.originalBackground;Application.runInBackground=t.originalBackground;t.finished=true;Write("play-tour",t);SessionState.SetString(Session,JsonUtility.ToJson(t));return;}
                if(EditorApplication.timeSinceStartup>t.deadline)throw new Exception("Tour timeout");if(!EditorApplication.isPlaying||EditorApplication.isPaused||SceneLoadManager.IsLoading)return;
                Application.runInBackground=true;if(Time.frameCount<t.frame+35)return;
                var scene=SceneManager.GetActiveScene();var all=scene.GetRootGameObjects();var players=all.SelectMany(g=>g.GetComponentsInChildren<PlayerMovement>()).Where(p=>p.isActiveAndEnabled).ToArray();var cameras=all.SelectMany(g=>g.GetComponentsInChildren<Camera>()).Where(c=>c.isActiveAndEnabled).ToArray();
                if(players.Length!=1||cameras.Length!=1)throw new Exception("Player/camera count in "+scene.name);
                var player=players[0];if(!Physics.Raycast(player.transform.position+Vector3.up*.25f,Vector3.down,1,~0,QueryTriggerInteraction.Ignore))throw new Exception("Ungrounded arrival "+scene.name);
                if(cameras[0].GetComponent<PlayerCameraController>().target!=player.transform||player.viewCamera!=cameras[0])throw new Exception("Camera follows wrong player");
                string expected=t.index<0||t.index>=5?TerracedVillageExpansion.ScenePath:CityDistrictBuilder.PathFor(t.index);if(scene.path!=expected)throw new Exception("Unexpected scene "+scene.path);
                t.checks.Add(scene.name+": one player, own camera, grounded arrival at "+player.transform.position.ToString("F2"));
                if(t.index>=0&&t.index<5)VillageSurfaceRepair.Photograph(scene.name+"-play",player.transform.position+V(3,3,-3),player.transform.position+V(-10,3,15),68);
                if(t.index>=5){t.success=true;t.restore=true;SessionState.SetString(Session,JsonUtility.ToJson(t));EditorApplication.ExitPlaymode();return;}
                t.index++;string target=t.index<5?CityDistrictBuilder.PathFor(t.index):TerracedVillageExpansion.ScenePath;
                if(!SceneLoadManager.TryLoadMap(target,t.index<5?"default":"station",player))throw new Exception(SceneLoadManager.LastError);
                t.frame=Time.frameCount;SessionState.SetString(Session,JsonUtility.ToJson(t));
            }
            catch(Exception ex){t.error=ex.ToString();t.success=false;t.restore=true;SessionState.SetString(Session,JsonUtility.ToJson(t));Write("play-tour",t);if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();}
        }
    }
}
