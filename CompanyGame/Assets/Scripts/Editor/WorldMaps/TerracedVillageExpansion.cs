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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.WorldMaps
{
    /// <summary>Expands the existing Korean hillside village without replacing its architecture or terraces.</summary>
    public static class TerracedVillageExpansion
    {
        public const string ScenePath="Assets/Scenes/daldongnaemap.unity";
        public const string AssetsFolder="Assets/Art/Daldongne/TerracedExpansion";
        public const string Output="../ArtSource/Daldongne/TerracedExpansion";
        const string Original="Assets/Art/Daldongne/WarmVillage/";
        static readonly string[] RampNames={"ShopWest","WestLower","East"};
        static Transform map, additions;
        static readonly List<Route> extraRoutes=new List<Route>();
        static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
        [Serializable] public sealed class Route {public string name;public Vector3[] points;public float width;}
        [Serializable] public sealed class Routes {public Route[] routes;}
        static void Folder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
        static Transform Group(Transform parent,string name)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;}
        static GameObject Root()
        {var scene=SceneManager.GetActiveScene();if(scene.path!=ScenePath)throw new Exception("Open "+ScenePath);return scene.GetRootGameObjects().Single(g=>g.name=="Map_daldongnaemap");}
        static void Palette()
        {
            Folder(AssetsFolder);Folder(AssetsFolder+"/Meshes");
            materials.Clear();
            foreach(string name in new[]{"concrete","cream","brick","brickDark","mortar","metal","wood","leaf","leafLight","pot","slate","white","green"})
                materials[name]=AssetDatabase.LoadAssetAtPath<Material>(Original+"ReferenceBuildings/"+name+".mat");
        }
        static Mesh SaveMesh(string name,Vector3[] vertices,int[] triangles)
        {
            var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=AssetsFolder+"/Meshes/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(old){EditorUtility.CopySerialized(mesh,old);EditorUtility.SetDirty(old);Object.DestroyImmediate(mesh);return old;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static GameObject Box(string name,Vector3 p,Vector3 size,string material,bool collision=false,Quaternion? rotation=null)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(additions,false);go.transform.position=p;go.transform.localScale=size;
            if(rotation.HasValue)go.transform.rotation=rotation.Value;go.GetComponent<Renderer>().sharedMaterial=materials[material];go.isStatic=true;
            if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
        }
        static void Beam(string name,Vector3 a,Vector3 b,float width,string material)
        {Box(name,(a+b)/2,V(width,width,Vector3.Distance(a,b)),material,false,Quaternion.LookRotation(b-a));}
        static void Shelf(string name,float bottom,float top,float spread)
        {
            // A small rear extension repeats the original four retaining levels.
            var outline=new[]{new Vector2(-34-spread,52.7f),new Vector2(34+spread,52.7f),new Vector2(31+spread,63+spread),new Vector2(18+spread,67+spread),new Vector2(-20-spread,67+spread),new Vector2(-32-spread,63+spread)};
            var vertices=new List<Vector3>();var triangles=new List<int>();
            foreach(float y in new[]{bottom,top})foreach(var p in outline)vertices.Add(V(p.x,y,p.y));
            int n=outline.Length;
            for(int i=1;i<n-1;i++)triangles.AddRange(new[]{n,n+i+1,n+i,0,i,i+1});
            for(int i=0;i<n;i++){int j=(i+1)%n;triangles.AddRange(new[]{i,i+n,j+n,i,j+n,j});}
            // Separate triangle vertices for the same flat faceted retaining-wall style.
            var flat=triangles.Select(i=>vertices[i]).ToArray();var mesh=SaveMesh(name,flat,Enumerable.Range(0,flat.Length).ToArray());
            var go=Group(additions,name).gameObject;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=materials["mortar"];go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;
            for(int i=1;i<n;i++)
            {
                int j=(i+1)%n;var a=V(outline[i].x,top+.03f,outline[i].y);var b=V(outline[j].x,top+.03f,outline[j].y);Beam(name+"_Cap",a,b,.20f,"cream");
                int pieces=Mathf.CeilToInt(Vector3.Distance(a,b)/1.1f);
                for(int k=0;k<pieces;k++)
                {var p=Vector3.Lerp(a,b,(k+.5f)/pieces);Box(name+"_StoneJoint",V(p.x,(bottom+top)/2,p.z),V(.035f,top-bottom-.05f,.035f),"concrete");}
            }
        }
        static Bounds BoundsOf(Transform t)
        {var rs=t.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
        static void Residence(string source,string name,Vector3 p)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Original+"Prefabs/Buildings/"+source+".prefab");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(asset,map.Find("10_Buildings"));go.name=name;go.transform.localScale=V(2f/3f,1,2f/3f);
            var b=BoundsOf(go.transform);go.transform.position+=p-V(b.center.x,b.min.y,b.center.z);
            var after=BoundsOf(go.transform);float front=after.min.z;
            Box(name+"_Foundation",V(p.x,14.34f,p.z),V(after.size.x+.45f,.12f,after.size.z+.35f),"concrete",true);
            Walk(name+"_Approach",new[]{V(p.x,14.42f,55.7f),V(p.x,14.42f,front-.6f)},2.8f);
            // Existing Korean homes bring their own tile roofs, brick, doors, meters and laundry.
        }
        static void Walk(string name,Vector3[] controls,float width,bool curve=false)
        {
            var points=new List<Vector3>();
            for(int segment=0;segment<controls.Length-1;segment++)
            {
                var a=controls[Mathf.Max(0,segment-1)];var b=controls[segment];var c=controls[segment+1];var d=controls[Mathf.Min(controls.Length-1,segment+2)];
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(b,c)/.4f));
                for(int i=0;i<count;i++)
                {float t=i/(float)count;points.Add(curve?.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t):Vector3.Lerp(b,c,t));}
            }
            points.Add(controls.Last());var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<points.Count;i++)
            {
                var delta=points[Mathf.Min(i+1,points.Count-1)]-points[Mathf.Max(0,i-1)];delta.y=0;var side=Vector3.Cross(Vector3.up,delta.normalized)*width/2;
                vertices.Add(points[i]-side);vertices.Add(points[i]+side);
                if(i>0){int a=(i-1)*2;triangles.AddRange(new[]{a,a+2,a+3,a,a+3,a+1});}
            }
            var mesh=SaveMesh(name,vertices.ToArray(),triangles.ToArray());var go=Group(additions,name).gameObject;go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=materials["concrete"];go.AddComponent<MeshCollider>().sharedMesh=mesh;go.isStatic=true;
            extraRoutes.Add(new Route{name=name,width=width,points=points.ToArray()});
        }
        static void Pot(Vector3 p,int index)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name="AlleyPot_"+index;go.transform.SetParent(additions,false);go.transform.position=p+Vector3.up*.22f;go.transform.localScale=V(.43f,.22f,.43f);go.GetComponent<Renderer>().sharedMaterial=materials["pot"];Object.DestroyImmediate(go.GetComponent<Collider>());
            for(int i=0;i<3;i++)
            {var plant=GameObject.CreatePrimitive(PrimitiveType.Sphere);plant.name="Pot leaves";plant.transform.SetParent(additions,false);plant.transform.position=p+V((i-1)*.12f,.52f+i*.07f,0);plant.transform.localScale=V(.28f,.44f,.29f);plant.GetComponent<Renderer>().sharedMaterial=materials[i%2==0?"leaf":"leafLight"];Object.DestroyImmediate(plant.GetComponent<Collider>());}
        }
        static void AlleyDetails()
        {
            // Red-brick garden edges, pots, outdoor seating and cables use the original palette.
            for(int i=0;i<6;i++)
            {
                float x=-32+i*12.5f;Pot(V(x,14.42f,58),i);Pot(V(x+.7f,14.42f,58.2f),i+8);
                Box("Alley brick planter",V(x,14.66f,65.2f),V(2.4f,.52f,.75f),"brick",true);
                for(int k=0;k<8;k++)Box("Planter masonry course",V(x-1.03f+k*.29f,14.7f,64.81f),V(.26f,.16f,.04f),k%2==0?"brickDark":"brick");
                Pot(V(x,14.94f,65.2f),i+16);
            }
            foreach(float x in new[]{-32f,32f})
            {
                Box("Concrete utility pole",V(x,17.5f,57.6f),V(.17f,6.2f,.17f),"concrete",true);
                Box("Utility crossbar",V(x,20.35f,57.6f),V(1.7f,.12f,.14f),"wood");
                for(int j=-1;j<=1;j++)Box("Pole insulator",V(x+j*.65f,20.5f,57.6f),V(.13f,.20f,.13f),"cream");
            }
            foreach(float z in new[]{57.15f,57.85f})
            {Vector3 previous=V(-32,20.5f,z);for(int i=1;i<=32;i++){float t=i/32f;var next=V(Mathf.Lerp(-32,32,t),20.5f-Mathf.Sin(t*Mathf.PI)*2,z);Beam("Alley overhead wire",previous,next,.022f,"metal");previous=next;}}
            for(int i=0;i<6;i++)Box("Alley bench slat",V(-32,14.94f,60.7f+i*.1f),V(2,.08f,.08f),"wood");
            foreach(int s in new[]{-1,1})Box("Alley bench support",V(-32+s*.75f,14.68f,61),V(.11f,.5f,.6f),"metal",true);
        }
        static void ConvertRamps()
        {
            foreach(var name in RampNames)
            {
                var stair=map.Find("00_Terrain/Stairs/"+name);
                foreach(var renderer in stair.GetComponentsInChildren<Renderer>())if(renderer.name.StartsWith("Walk_Stair_"))renderer.enabled=false;
                var ramp=map.Find("40_Colliders/Collider_Ramp_"+name);var r=ramp.GetComponent<Renderer>();r.enabled=true;r.sharedMaterial=materials["concrete"];r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
        }
        static void ClipRearWall(MeshFilter filter,float limit)
        {
            var original=PrefabUtility.GetCorrespondingObjectFromSource(filter);
            var source=original?original.sharedMesh:filter.sharedMesh;
            var vertices=source.vertices;var indices=source.triangles;var result=new List<Vector3>();
            for(int i=0;i<indices.Length;i+=3)
            {
                var polygon=new List<Vector3>{filter.transform.TransformPoint(vertices[indices[i]]),filter.transform.TransformPoint(vertices[indices[i+1]]),filter.transform.TransformPoint(vertices[indices[i+2]])};
                var clipped=new List<Vector3>();
                for(int j=0;j<polygon.Count;j++)
                {
                    var a=polygon[j];var b=polygon[(j+1)%polygon.Count];bool insideA=a.z<=limit,insideB=b.z<=limit;
                    if(insideA)clipped.Add(a);
                    if(insideA!=insideB)clipped.Add(Vector3.Lerp(a,b,(limit-a.z)/(b.z-a.z)));
                }
                for(int j=1;j<clipped.Count-1;j++)
                {result.Add(filter.transform.InverseTransformPoint(clipped[0]));result.Add(filter.transform.InverseTransformPoint(clipped[j]));result.Add(filter.transform.InverseTransformPoint(clipped[j+1]));}
            }
            var mesh=SaveMesh("Trimmed_"+filter.name,result.ToArray(),Enumerable.Range(0,result.Count).ToArray());filter.sharedMesh=mesh;
            var collider=filter.GetComponent<MeshCollider>();if(collider)collider.sharedMesh=mesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);if(collider)PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }
        public static void RefineConnections()
        {
            if(EditorApplication.isPlaying)throw new Exception("Stop Play mode.");
            var root=Root();map=root.transform.Find("10_World/Daldongne Warm Village");var extension=root.transform.Find("10_World/Terraced Village Extension");
            if(!extension)throw new Exception("Apply the terraced expansion first.");Palette();
            var previous=extension.Find("Alley and promenade details");if(previous)Object.DestroyImmediate(previous.gameObject);
            additions=Group(extension,"Alley and promenade details");
            foreach(var renderer in map.Find("00_Terrain/Walls").GetComponentsInChildren<MeshRenderer>(true))
            {var b=renderer.bounds;if(b.max.z>52.65f&&b.min.z<52.65f&&b.max.y>14.40f)ClipRearWall(renderer.GetComponent<MeshFilter>(),52.65f);}
            var tunnel=map.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Station_TunnelDark");
            // The station, its stairwell cut and its stairs are one assembly. Unlike
            // detached houses, its horizontal scale must follow the terrain.
            var station=map.Find("10_Buildings/Station");station.localScale=Vector3.one;
            // Restore the station's prefab-relative tunnel wall. The original wall stays
            // where the canopy and stairwell model it; shorten the landing path below
            // instead of visually moving the wall away from the station.
            var sourceTunnel=PrefabUtility.GetCorrespondingObjectFromSource(tunnel);
            if(sourceTunnel){tunnel.localPosition=sourceTunnel.localPosition;PrefabUtility.RecordPrefabInstancePropertyModifications(tunnel);}
            // Keep reset and return travel on the expanded station plaza, clear of the subway trench.
            var player=root.GetComponentInChildren<PlayerMovement>();player.spawn=V(-18,1.28f,-31);player.transform.position=player.spawn;
            foreach(var spawn in root.GetComponentsInChildren<MapSpawnPoint>(true))if(spawn.spawnId=="station")spawn.transform.position=player.spawn;
            foreach(var portal in root.GetComponentsInChildren<MapPortal>(true))if(portal.targetScenePath.EndsWith("DaldongnePocketGarden.unity"))portal.transform.position=V(-24,1.28f,-32);
            TerracedBuildingDetails.Apply(root);
            extraRoutes.Clear();extraRoutes.AddRange(JsonUtility.FromJson<Routes>(File.ReadAllText(Output+"/additional-routes.json")).routes.Where(r=>r.name!="Station garden curve"));
            Walk("Station garden curve",new[]{V(-21,1.255f,-46.5f),V(-18,1.255f,-44),V(-15,1.255f,-39),V(-10,1.255f,-33),V(-2,1.255f,-24.5f)},3.0f,true);
            // Small additions to the existing station green; the road and station remain in place.
            foreach(var q in new[]{V(-19,1.2f,-39),V(-10,1.2f,-41),V(-5,1.2f,-35)})
            {
                Box("Station garden brick planter",q+Vector3.up*.23f,V(1.5f,.46f,.72f),"brick",true);
                Pot(q+V(-.4f,.46f,0),30);Pot(q+V(.4f,.46f,0),31);
            }
            foreach(float x in new[]{-23f,-7f})
            {
                var q=x<-20?V(x,1.2f,-39):V(-12,1.2f,-28);
                for(int i=0;i<5;i++)Box("Station garden bench slat",q+V(0,.5f,i*.12f),V(1.9f,.075f,.10f),"wood");
                for(int i=0;i<3;i++)Box("Station garden bench back",q+V(0,.83f+i*.15f,.55f),V(1.9f,.11f,.07f),"wood");
                foreach(int side in new[]{-1,1})Box("Station garden bench leg",q+V(side*.73f,.23f,.24f),V(.10f,.46f,.5f),"metal",true);
            }
            File.WriteAllText(Output+"/additional-routes.json",JsonUtility.ToJson(new Routes{routes=extraRoutes.ToArray()},true));
            foreach(var t in root.GetComponentsInChildren<Transform>(true))if(PrefabUtility.IsPartOfPrefabInstance(t))PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            if(PrefabUtility.IsPartOfPrefabInstance(player))PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);Physics.SyncTransforms();
        }
        [MenuItem("Tools/Company Game/Maps/Expand Existing Terraced Village")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play mode.");
            var root=Root();map=root.transform.Find("10_World/Daldongne Warm Village");if(!map)throw new Exception("Original terraced village is required.");
            if(root.transform.Find("10_World/Terraced Village Extension"))throw new Exception("Already expanded. Edit the existing objects instead of applying twice.");
            Directory.CreateDirectory(Output);Palette();extraRoutes.Clear();
            if(Mathf.Abs(map.localScale.x-1.2f)<.01f)
            {
                map.localScale=V(1.8f,1.2f,1.8f);
                foreach(Transform t in map.Find("10_Buildings"))t.localScale=Vector3.Scale(t.localScale,V(2f/3f,1,2f/3f));
                foreach(Transform t in map.Find("20_Nature/Trees"))t.localScale=Vector3.Scale(t.localScale,V(2f/3f,1,2f/3f));
                foreach(var spawn in root.GetComponentsInChildren<MapSpawnPoint>(true))spawn.transform.position=Vector3.Scale(spawn.transform.position,V(1.5f,1,1.5f));
                foreach(var portal in root.GetComponentsInChildren<MapPortal>(true))portal.transform.position=Vector3.Scale(portal.transform.position,V(1.5f,1,1.5f));
                var player=root.GetComponentInChildren<PlayerMovement>();player.spawn=Vector3.Scale(player.spawn,V(1.5f,1,1.5f));player.spawn.y=1.28f;player.transform.position=player.spawn;
            }
            else if(Mathf.Abs(map.localScale.x-1.8f)>.01f)throw new Exception("Unexpected source scale.");
            additions=Group(root.transform.Find("10_World"),"Terraced Village Extension");
            Shelf("Lower masonry terrace",-3.8f,3.552f,4.5f);Shelf("Middle masonry terrace",3.50f,7.152f,3);Shelf("Upper masonry terrace",7.1f,10.752f,1.5f);Shelf("Residential crest extension",10.7f,14.352f,0);
            // Remove only the former back edge now enclosed by the new rear terrace.
            foreach(var renderer in map.Find("00_Terrain/Walls").GetComponentsInChildren<Renderer>())
            {
                var b=renderer.bounds;
                if(b.center.z>53.1f&&b.center.y>13.4f&&b.center.y<15.2f)
                {renderer.enabled=false;foreach(var c in renderer.GetComponents<Collider>())c.enabled=false;}
            }
            Walk("Crest shared alley",new[]{V(-33,14.42f,55.7f),V(-12,14.42f,55.2f),V(10,14.42f,55.9f),V(32,14.42f,55.7f)},3.6f,true);
            Walk("Central crest connection",new[]{V(-2.34f,14.42f,52.2f),V(-2.34f,14.42f,55.7f)},3.2f);
            Walk("West crest connection",new[]{V(-31.5f,14.42f,52.2f),V(-31.5f,14.42f,55.7f)},3.2f);
            Residence("Home_A","OneRoom_Home_01",V(-26,14.4f,60.5f));
            Residence("Home_C","OneRoom_Home_02",V(-13,14.4f,60.5f));
            Residence("Home_D","OneRoom_Home_03",V(0,14.4f,60.5f));
            Residence("Home_A","OneRoom_Home_04",V(13,14.4f,60.5f));
            Residence("Home_B","OneRoom_Home_05",V(26,14.4f,60.5f));
            ConvertRamps();AlleyDetails();
            var spawnParent=root.transform.Find("20_Gameplay/SpawnPoints");
            for(int i=0;i<15;i++){var t=Group(spawnParent,"SocialSpawn_"+(i+1).ToString("00"));t.position=V(-18+(i%5)*2,1.28f,-29-(i/5)*2);t.gameObject.AddComponent<MapSpawnPoint>().spawnId="social_"+(i+1).ToString("00");}
            var camera=root.GetComponentInChildren<PlayerMovement>().viewCamera;camera.transform.position=V(78,85,-102);camera.transform.LookAt(V(0,10,9));camera.farClipPlane=400;
            foreach(var t in root.GetComponentsInChildren<Transform>(true))if(PrefabUtility.IsPartOfPrefabInstance(t))PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            foreach(var r in map.GetComponentsInChildren<Renderer>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            foreach(var c in map.GetComponentsInChildren<Collider>(true))PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            EditorBuildSettings.scenes=EditorBuildSettings.scenes.Where(s=>s.path!="Assets/Scenes/DaldongneMap.unity"&&s.path!="Assets/Scenes/SampleScene.unity").OrderBy(s=>s.path==ScenePath?0:1).ToArray();
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
            File.WriteAllText(Output+"/additional-routes.json",JsonUtility.ToJson(new Routes{routes=extraRoutes.ToArray()},true));
            RefineConnections();Physics.SyncTransforms();
        }
    }
}
