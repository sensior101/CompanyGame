using System;
using System.Linq;
using CompanyGame.World.Maps;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static CompanyGame.Editor.WorldMaps.DistrictGeometry;

namespace CompanyGame.Editor.WorldMaps
{
    public static class TransitMapBuilder
    {
        const float StationScale = .65f, BusScale = .72f;
        static readonly Vector3 StationOrigin = V(-31.5f,1.2f,-46.35f);
        static readonly Vector3 BusOrigin = V(41.04f,1.2f,-28.08f);
        public static Vector3 StationPosition(int i) => new[]{V(-56,0,42.5f),V(-20,0,-15.5f),V(6.5f,0,26),V(56,0,6.5f),V(56,0,-12)}[i];
        public static Vector3 BusPosition(int i) => new[]{V(56,0,-42),V(56,0,41.5f),V(56,0,-43),V(-56,0,-35.2f),V(-56,0,41.5f)}[i];
        public static float StationYaw(int i) => i==2?90:0;
        static float BusYaw(int i) => i==1||i==4?180:0;

        // The pavement and the entrance use the same dimensions; there is no
        // overlapping full ground slab underneath the descending stair flight.
        public static Rect Excavation(int i)
        {
            var p=StationPosition(i);var q=Quaternion.Euler(0,StationYaw(i),0);
            var a=p+q*V(-5.20f,0,-.20f);var b=p+q*V(5.20f,0,7.40f);
            return Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.z,b.z),Mathf.Max(a.x,b.x),Mathf.Max(a.z,b.z));
        }
        public static void GroundSlab(Batch b,int i,Rect land,float bottom,float top,string material)
        {
            var cut=Excavation(i);
            if(!land.Overlaps(cut)){Slab(b,land,bottom,top,material);return;}
            float left=Mathf.Max(land.xMin,cut.xMin),right=Mathf.Min(land.xMax,cut.xMax);
            float front=Mathf.Max(land.yMin,cut.yMin),back=Mathf.Min(land.yMax,cut.yMax);
            Slab(b,Rect.MinMaxRect(land.xMin,land.yMin,left,land.yMax),bottom,top,material);
            Slab(b,Rect.MinMaxRect(right,land.yMin,land.xMax,land.yMax),bottom,top,material);
            Slab(b,Rect.MinMaxRect(left,land.yMin,right,front),bottom,top,material);
            Slab(b,Rect.MinMaxRect(left,back,right,land.yMax),bottom,top,material);
        }
        static void Slab(Batch b,Rect r,float bottom,float top,string m)
        {if(r.width>.001f&&r.height>.001f)b.Box(V(r.center.x,(bottom+top)/2,r.center.y),V(r.width,top-bottom,r.height),m,true);}

        public static TransitDestination[] Destinations()
        {
            var list=new System.Collections.Generic.List<TransitDestination>{new TransitDestination(TerracedVillageExpansion.ScenePath,"달동네","언덕 골목과 원룸촌")};
            string[] descriptions={"옷가게 · 미용실 · 술집","도서관 · 경찰서 · 병원","한대건설 · 기업 · 공장","해변 · 아파트 · 주택","시그니엘 · 고급 주택 · 공원"};
            for(int i=0;i<5;i++)list.Add(new TransitDestination(CityDistrictBuilder.PathFor(i),CityDistrictBuilder.Labels[i],descriptions[i]));
            return list.ToArray();
        }
        public static void ApplyCurrent()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode before saving transit facilities.");
            Init();var scene=SceneManager.GetActiveScene();var root=scene.GetRootGameObjects().Single(g=>g.name.StartsWith("Map_"));
            int index=Array.IndexOf(CityDistrictBuilder.Names,scene.name);
            bool village=scene.path==TerracedVillageExpansion.ScenePath;
            if(!village&&index<0)throw new InvalidOperationException("Open one of the six district scenes.");
            foreach(var portal in root.GetComponentsInChildren<MapPortal>(true))Object.DestroyImmediate(portal.gameObject);
            var portalRoot=root.transform.Find("20_Gameplay/Portals");if(portalRoot)Object.DestroyImmediate(portalRoot.gameObject);
            foreach(var old in root.GetComponentsInChildren<TransitStop>(true))Object.DestroyImmediate(old.gameObject);
            var oldWorld=root.transform.Find("10_World/Public Transport");if(oldWorld)Object.DestroyImmediate(oldWorld.gameObject);
            var stopRoot=root.transform.Find("20_Gameplay/Transit");if(stopRoot)Object.DestroyImmediate(stopRoot.gameObject);
            stopRoot=Group(root.transform.Find("20_Gameplay"),"Transit");
            if(village)
            {
                // Keep both original structures and their original world transforms.
                Stop(stopRoot,TransitKind.Subway,V(-31.5f,1.28f,-47.6f),V(-31.5f,1.28f,-48.8f),0);
                Stop(stopRoot,TransitKind.Bus,V(41.04f,1.28f,-24.9f),V(41.04f,1.28f,-23.6f),180);
            }
            else
            {
                var source=SceneManager.GetSceneByPath(TerracedVillageExpansion.ScenePath);bool opened=!source.isLoaded;
                if(opened)source=EditorSceneManager.OpenScene(TerracedVillageExpansion.ScenePath,OpenSceneMode.Additive);
                try
                {
                    var sourceRoot=source.GetRootGameObjects().Single(g=>g.name=="Map_daldongnaemap");
                    var world=Group(root.transform.Find("10_World"),"Public Transport");
                    Station(world,sourceRoot,index);Bus(world,sourceRoot,index);
                    var rotation=Quaternion.Euler(0,StationYaw(index),0);var p=StationPosition(index);
                    Stop(stopRoot,TransitKind.Subway,p+rotation*V(0,.13f,-1.1f),p+rotation*V(0,.13f,-2.05f),StationYaw(index));
                    rotation=Quaternion.Euler(0,BusYaw(index),0);p=BusPosition(index);
                    Stop(stopRoot,TransitKind.Bus,p+rotation*V(0,.15f,2.35f),p+rotation*V(0,.15f,3.05f),BusYaw(index)+180);
                }
                finally {if(opened)EditorSceneManager.CloseScene(source,true);SceneManager.SetActiveScene(scene);}
            }
            RepairCanopyOcclusionCurrent();
            var player=root.GetComponentInChildren<PlayerMovement>(true);
            var interaction=player.GetComponent<PlayerInteraction>();if(!interaction)interaction=player.gameObject.AddComponent<PlayerInteraction>();
            interaction.uiFont=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/font/NotoSansKR-Regular SDF.asset");
            interaction.destinations=Destinations();interaction.enabled=true;
            var inventory=player.GetComponent<PlayerInventory>();if(!inventory)inventory=player.gameObject.AddComponent<PlayerInventory>();
            inventory.uiFont=interaction.uiFont;inventory.enabled=true;
            InventorySceneSetup.ApplyCurrent(10000);
            Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        }
        // The imported shelter has visual-only roof meshes. The player camera's
        // obstacle fade needs matching collision geometry to see those roofs.
        public static int RepairCanopyOcclusionCurrent()
        {
            int added=0;
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach(var mesh in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!mesh.name.StartsWith("BusStop_")||!mesh.name.Contains("Roof")||!mesh.sharedMesh||mesh.GetComponent<Collider>())continue;
                var collider=mesh.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh=mesh.sharedMesh;
                added++;
            }
            if(added>0)EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return added;
        }
        static void Stop(Transform root,TransitKind kind,Vector3 boarding,Vector3 arrival,float yaw)
        {
            var go=Group(root,kind==TransitKind.Subway?"Subway Boarding":"Bus Boarding");go.position=boarding;
            var stop=go.gameObject.AddComponent<TransitStop>();stop.kind=kind;stop.boardingPoint=go;stop.interactionRadius=3f;stop.verticalTolerance=1.3f;
            var mapRoot=root.parent;var spawns=mapRoot.Find("SpawnPoints");
            foreach(var old in spawns.GetComponentsInChildren<MapSpawnPoint>().Where(s=>s.spawnId==stop.ArrivalSpawnId).ToArray())Object.DestroyImmediate(old.gameObject);
            var sp=Group(spawns,"Spawn_"+stop.ArrivalSpawnId).gameObject.AddComponent<MapSpawnPoint>();sp.spawnId=stop.ArrivalSpawnId;sp.transform.SetPositionAndRotation(arrival,Quaternion.Euler(0,yaw,0));
        }
        static Transform Find(GameObject source,string name)=>source.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
        static Transform Clone(Transform source,Transform parent,Vector3 origin,float scale)
        {
            var t=Object.Instantiate(source.gameObject).transform;
            t.SetParent(parent,false);t.localPosition=(source.position-origin)*scale;t.localRotation=source.rotation;t.localScale=source.lossyScale*scale;
            t.name=source.name;t.gameObject.SetActive(true);return t;
        }
        static void Station(Transform world,GameObject source,int index)
        {
            var holder=Group(world,"Subway Station");
            var model=Clone(Find(source,"Station"),holder,StationOrigin,StationScale);
            Clone(Find(source,"Subway"),holder,StationOrigin,StationScale);
            Clone(Find(source,"Collider_Ramp_Subway"),holder,StationOrigin,StationScale);
            foreach(var t in model.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Station_Sign_Hangul")||t.name.StartsWith("Station_PylonHangul")))t.gameObject.SetActive(false);
            Label(holder,CityDistrictBuilder.Labels[index]+"역",V(0,2.434f,-.018f),5.35f,.44f);
            Label(holder,"지\n하\n철",V(-7.566f,1.65f,.45f),.40f,1.1f);
            var b=new Batch(holder,CityDistrictBuilder.Names[index]+"_StationFoundation");
            foreach(int side in new[]{-1,1})b.Box(V(side*5.2975f,-1.378f,6.2075f),V(.208f,2.73f,2.535f),"concrete",true);
            b.Box(V(0,-2.3075f,6.06125f),V(10.4325f,.195f,2.613f),"concrete",true);
            // A modest apron joins the pavement, without a raised trip edge.
            b.Box(V(0,-.04f,-.8f),V(11.7f,.08f,1.2f),"stone",true);
            b.Box(V(0,-.04f,-.04f),V(10.43f,.08f,.20f),"concrete",true);
            b.Finish();holder.SetPositionAndRotation(StationPosition(index),Quaternion.Euler(0,StationYaw(index),0));
        }
        static void Bus(Transform world,GameObject source,int index)
        {
            var holder=Group(world,"Bus Stop");var model=Clone(Find(source,"BusStop"),holder,BusOrigin,BusScale);
            foreach(var t in model.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("BusStop_Sign_Hangul")))t.gameObject.SetActive(false);
            Label(holder,CityDistrictBuilder.Labels[index]+" 버스정류장",V(0,2.30f,-1.74f),7.6f,.35f);
            // Repeat the stop name on the open, road-facing side of the canopy.
            var b=new Batch(holder,CityDistrictBuilder.Names[index]+"_BusApron");
            b.Box(V(0,-.03f,.1f),V(12.4f,.06f,4.8f),"stone",true);
            b.Box(V(0,2.47f,1.79f),V(8.3f,.50f,.09f),"metal");
            Label(b.root,CityDistrictBuilder.Labels[index]+" 버스정류장",V(0,2.47f,1.85f),7.8f,.35f,"signWhite",180);
            for(int i=-20;i<=20;i++)b.Box(V(i*.23f,.014f,2.22f),V(.12f,.027f,.35f),"yellow");
            b.Finish();holder.SetPositionAndRotation(BusPosition(index),Quaternion.Euler(0,BusYaw(index),0));
        }
    }
}
