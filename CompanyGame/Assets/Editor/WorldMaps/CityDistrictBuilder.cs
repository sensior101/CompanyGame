using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompanyGame.Daldongne;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using static CompanyGame.Editor.WorldMaps.DistrictGeometry;

namespace CompanyGame.Editor.WorldMaps
{
    public static class CityDistrictBuilder
    {
        public static readonly string[] Names={"NightlifeDistrict","CivicDistrict","BusinessDistrict","CoastalResidentialDistrict","LuxuryResidentialDistrict"};
        public static readonly string[] Labels={"번화가","행정지구","상업지구","해변주거지구","도심주거지구"};
        public static string PathFor(int i)=>"Assets/Scenes/Maps/"+Names[i]+".unity";
        static Transform terrain,buildings,props,landscape;static int kind;static string key;
        static Batch street,green;
        public static void Build(int district,bool replaceGeneratedScene=false)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play mode");
            if(district<0||district>=Names.Length)throw new ArgumentOutOfRangeException(nameof(district));
            if(File.Exists(PathFor(district))&&!replaceGeneratedScene)throw new Exception("Scene already exists; use Rebuild explicitly to replace generated content.");
            kind=district;key=Names[kind];Init();Folder("Assets/Scenes/Maps");
            var source=SceneManager.GetSceneByPath(TerracedVillageExpansion.ScenePath);
            if(!source.isLoaded)source=EditorSceneManager.OpenScene(TerracedVillageExpansion.ScenePath,OpenSceneMode.Additive);
            var sourceRoot=source.GetRootGameObjects().Single(g=>g.name=="Map_daldongnaemap");
            var sourcePlayer=sourceRoot.GetComponentInChildren<PlayerMovement>();var sourceCamera=sourcePlayer.viewCamera;
            var sourceSun=sourceRoot.GetComponentsInChildren<Light>().First(l=>l.type==LightType.Directional);
            var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            try
            {
                var root=Group(null,"Map_"+key);Group(root,"00_Systems");var world=Group(root,"10_World");terrain=Group(world,"Terrain");buildings=Group(world,"Buildings");props=Group(world,"Props");landscape=Group(world,"Landscape");
                street=new Batch(props,key+"_Streets");green=new Batch(landscape,key+"_Planting");
                Ground();
                if(kind==0)Nightlife();else if(kind==1)Civic();else if(kind==2)Business();else if(kind==3)Coastal();else Luxury();
                StreetFurniture();street.Finish();green.Finish();
                var play=Group(root,"20_Gameplay");var spawnRoot=Group(play,"SpawnPoints");var portals=Group(play,"Portals");Group(play,"NPCs");Group(play,"Interactables");
                var presentation=Group(root,"30_Presentation");var cameraRoot=Group(presentation,"Cameras");var lightRoot=Group(presentation,"Lighting");
                var player=Object.Instantiate(sourcePlayer.gameObject,Group(play,"Player")).GetComponent<PlayerMovement>();player.name="Map Player";player.spawn=V(-5,.16f,-38);if(kind==3)player.spawn=V(-5,.16f,-30);player.transform.SetPositionAndRotation(player.spawn,Quaternion.identity);player.enabled=true;
                var camera=Object.Instantiate(sourceCamera.gameObject,cameraRoot).GetComponent<Camera>();camera.name="Map Camera";camera.farClipPlane=500;player.viewCamera=camera;
                var controller = camera.GetComponent<PlayerCameraController>();
                controller.target = player.transform;

                var appearance = player.GetComponent<DaldongnePlayerAppearance>();

                if (appearance)
                {
                    controller.femaleVisuals = appearance.female;
                    controller.maleVisuals = appearance.male;
                }

                controller.firstPerson = false;
                controller.distance = 4f;
                camera.transform.position=player.spawn+V(0,4,-7);camera.transform.LookAt(player.spawn+Vector3.up);
                var sun=Object.Instantiate(sourceSun.gameObject,lightRoot).GetComponent<Light>();sun.name="District Sun";sun.transform.rotation=Quaternion.Euler(48,-32,0);sun.intensity=kind==0?.55f:1.5f;sun.color=kind==0?new Color(.55f,.66f,1):new Color(1,.91f,.76f);
                RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=kind==0?new Color(.23f,.30f,.46f):new Color(.50f,.60f,.66f);RenderSettings.ambientEquatorColor=kind==0?new Color(.22f,.23f,.33f):new Color(.48f,.49f,.45f);RenderSettings.ambientGroundColor=new Color(.23f,.24f,.23f);RenderSettings.fog=false;RenderSettings.skybox=null;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=kind==0?new Color(.045f,.067f,.13f):new Color(.54f,.67f,.73f);
                var sp=Group(spawnRoot,"Spawn_Default").gameObject.AddComponent<MapSpawnPoint>();sp.spawnId="default";sp.transform.position=player.spawn;
                for(int i=0;i<15;i++){var s=Group(spawnRoot,"Spawn_Social_"+i.ToString("00")).gameObject.AddComponent<MapSpawnPoint>();s.spawnId="social_"+i.ToString("00");s.transform.position=V(-8+(i%5)*2,.16f,kind==3?-28+i/5*2:-34+i/5*2);}
                Group(root,"90_Development");
                AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(scene,PathFor(kind)))throw new IOException("Scene save failed");
                DistrictPresentation.Apply();
                TransitMapBuilder.ApplyCurrent();
                var paths=EditorBuildSettings.scenes.ToList();if(!paths.Any(s=>s.path==PathFor(kind)))paths.Add(new EditorBuildSettingsScene(PathFor(kind),true));EditorBuildSettings.scenes=paths.ToArray();
                EditorSceneManager.playModeStartScene=null;
            }
            finally{SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}
        }
        public static void Rebuild(int district)
        {
            string path=PathFor(district);if(SceneManager.GetSceneByPath(path).isLoaded)throw new Exception("Close the district before rebuilding");
            // Keep GUID and original file until a successfully generated replacement exists.
            string backup="../_temp/DistrictBackups/"+Names[district]+".unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));if(File.Exists(path))File.Copy(path,backup,true);
            Build(district,true);
        }
        static void Ground()
        {
            var b=new Batch(terrain,key+"_Ground");
            if(kind==2){TransitMapBuilder.GroundSlab(b,kind,Rect.MinMaxRect(-72,-72,72,-10),-1,0,"paving");TransitMapBuilder.GroundSlab(b,kind,Rect.MinMaxRect(-72,10,72,72),-1,0,"paving");b.Box(V(0,-1.1f,0),V(144,.2f,20),"water");foreach(float z in new[]{-10f,10f})b.Box(V(0,-1.05f,z),V(144,2.1f,.5f),"stone",true);}
            else if(kind==3){TransitMapBuilder.GroundSlab(b,kind,Rect.MinMaxRect(-72,-38,72,72),-1,0,"paving");b.Box(V(0,-.65f,-47),V(144,1.3f,18),"sand",true);b.Box(V(0,-.4f,-64),V(144,.2f,16),"water");}
            else TransitMapBuilder.GroundSlab(b,kind,Rect.MinMaxRect(-72,-72,72,72),-1,0,"paving");
            foreach(int s in new[]{-1,1}){b.Box(V(s*71.6f,.5f,0),V(.65f,1,144),"stone",true);b.Box(V(0,.5f,s*71.6f),V(144,1,.65f),"stone",true);}
            b.Finish();
            if(kind==2)
            {
                foreach(float x in new[]{-40f,0,40f}){Road(V(x,.02f,-41),V(8,.035f,62));Road(V(x,.02f,41),V(8,.035f,62));}
                foreach(float z in new[]{-38f,-16,16,38})Road(V(0,.02f,z),V(142,.035f,8));
                foreach(float x in new[]{-40f,40f})Bridge(x);
                foreach(float z in new[]{-10.4f,10.4f})for(float x=-70;x<71;x+=2){if(Mathf.Abs(x-40)<6.5f||Mathf.Abs(x+40)<6.5f)continue;street.Box(V(x,.53f,z),V(.09f,1.06f,.1f),"metal",true);street.Box(V(x,.99f,z),V(2.05f,.08f,.08f),"metal");}
            }
            else
            {
                float south=kind==3?-32:-70;foreach(float x in new[]{-40f,0,40f})
                {if(kind==4&&x==40){Road(V(x,.02f,35),V(8,.035f,70));Road(V(x,.02f,-55),V(8,.035f,30));}else Road(V(x,.02f,(south+70)/2),V(8,.035f,70-south));}
                foreach(float z in kind==3?new[]{-30f,0,36}:new[]{-36f,0,36})Road(V(0,.025f,z),V(142,.035f,8));
            }
            foreach(float x in new[]{-40f,0,40f})foreach(float z in kind==2?new[]{-38f,38}:kind==3?new[]{-30f,0,36}:new[]{-36f,0,36})Crosswalk(x,z);
        }
        static void Road(Vector3 p,Vector3 size)
        {
            street.Box(p,size,"road");bool alongZ=size.z>size.x;float length=alongZ?size.z:size.x;
            for(float d=-length/2+2;d<length/2-1;d+=4)
            {
                var q=p+(alongZ?V(0,.025f,d):V(d,.025f,0));bool intersection=alongZ?new[]{-36f,0,36}.Any(z=>Mathf.Abs(q.z-z)<5):new[]{-40f,0,40}.Any(x=>Mathf.Abs(q.x-x)<5);
                if(!intersection)street.Box(q,alongZ?V(.12f,.012f,1.9f):V(1.9f,.012f,.12f),"yellow");
            }
        }
        static void Crosswalk(float x,float z)
        {
            for(int k=-3;k<=3;k++)foreach(int s in new[]{-1,1}){street.Box(V(x+k*.9f,.07f,z+s*5.8f),V(.5f,.016f,2.5f),"white");street.Box(V(x+s*5.8f,.07f,z+k*.9f),V(2.5f,.016f,.5f),"white");}
            foreach(int s in new[]{-1,1}){street.Box(V(x+s*6.8f,1.65f,z-6.8f),V(.09f,3.3f,.09f),"metal");street.Box(V(x+s*6.8f,3.25f,z-6.8f),V(.35f,.8f,.35f),"metal");street.Box(V(x+s*6.8f,3.45f,z-7),V(.16f,.16f,.06f),"red");street.Box(V(x+s*6.8f,3.12f,z-7),V(.16f,.16f,.06f),"cyan");}
        }
        static void Bridge(float x)
        {
            var b=new Batch(terrain,key+"_RoadBridge_"+x);b.Box(V(x,-.25f,0),V(12,.5f,22),"concrete",true);b.Box(V(x,.02f,0),V(8,.035f,23),"road");
            foreach(int s in new[]{-1,1}){b.Box(V(x+s*5,.05f,0),V(2,.1f,23),"stone",true);b.Box(V(x+s*5.8f,.62f,0),V(.18f,1.15f,23),"metal",true);for(float z=-11;z<=11;z+=1.1f)b.Box(V(x+s*5.8f,.72f,z),V(.28f,1.4f,.2f),"gold");}
            for(float z=-10;z<=10;z+=4)b.Box(V(x,.048f,z),V(.12f,.012f,2),"yellow");b.Finish();Car(V(x+2,.04f,-2),0,"white");Car(V(x-2,.04f,5),180,"blue");
        }
        static void Vacant(int n,Vector3 p,float width=13,float depth=16)
        {
            var b=new Batch(buildings,key+"_VacantPlot_"+n.ToString("00"));b.Box(V(0,.04f,0),V(width,.08f,depth),"concrete",true);
            foreach(int s in new[]{-1,1}){b.Box(V(s*(width/2-.12f),.085f,0),V(.12f,.012f,depth),"white");b.Box(V(0,.085f,s*(depth/2-.12f)),V(width,.012f,.12f),"white");}
            for(float x=-width/2+2;x<width/2;x+=2)b.Box(V(x,.085f,0),V(.023f,.009f,depth),"mortar");for(float z=-depth/2+2;z<depth/2;z+=2)b.Box(V(0,.085f,z),V(width,.009f,.023f),"mortar");
            b.Finish();b.root.localPosition=p;
        }
        static void Nightlife()
        {
            string[] shops={"별빛 게임장","모드 옷가게","오늘 헤어","달빛 포차","서울 노래방","24시 편의점","밤하늘 BAR","청춘 분식","낭만 호프","레코드 카페","불꽃 술집","미소 사진관"};int id=0,lot=0;
            foreach(float z in new[]{-55f,-18,18,55})foreach(float x in new[]{-56f,-20,20,56})
            {
                bool empty=(z==-55&&x==-56)||(z==18&&x==56)||(z==55&&x==-20)||(z==-18&&x==20);
                if(empty){Vacant(++lot,V(x,0,z),18,20);continue;}
                foreach(int side in new[]{-1,1})
                {
                    string name=shops[id%shops.Length];var p=V(x+side*5.4f,0,z);var t=Walkup(buildings,key+"_Shop_"+id,p,3+id%3,0,name,true,id);
                    var b=new Batch(t,"Shopfront",key+"_Shopfront_"+id);
                    for(int w=-1;w<=1;w++)Window(b,w*2.5f,1.35f,-4.03f,2.1f,2.4f,"metal",id%2==0?"glassLight":"glassDark");
                    string neon=id%3==0?"pink":id%3==1?"cyan":"warmLight";
                    b.Box(V(0,3.0f,-4.85f),V(8.3f,.9f,.25f),"metal");b.Box(V(0,3.0f,-5.02f),V(7.9f,.72f,.06f),neon);Label(b.root,name,V(0,3.02f,-5.08f),7.4f,.6f,"signWhite");
                    b.Box(V(3.6f,7,-4.35f),V(1.25f,4.5f,.35f),"metal");b.Box(V(3.6f,7,-4.55f),V(1.05f,4.3f,.06f),neon);Label(b.root,id%2==0?"영\n업\n중":"노\n래\n방",V(3.6f,7,-4.60f),.75f,3.7f,"signWhite");
                    b.Box(V(-.5f,2.45f,-4.75f),V(5,.14f,1.5f),id%2==0?"red":"blue",false,Quaternion.Euler(-12,0,0));
                    b.Box(V(-1,6,-4.80f),V(4.5f,2.1f,.24f),"metal");Label(b.root,id%3==0?"SEOUL NIGHT\nLIVE  /  24H":"오늘의 즐거움\nOPEN",V(-1,6,-4.96f),4.2f,1.7f,neon);
                    foreach(int edge in new[]{-1,1}){b.Box(V(-1+edge*2.25f,6,-4.96f),V(.07f,2.2f,.07f),neon);b.Box(V(-1,6+edge*1.1f,-4.96f),V(4.55f,.07f,.07f),neon);}
                    if(id%5==0){float roof=(3+id%3)*2.75f;b.Box(V(0,roof+1.8f,-2.8f),V(7,2.8f,.35f),"metal");Label(b.root,id%2==0?"SEOUL  /  NIGHT":"한대 패션거리",V(0,roof+1.8f,-3.01f),6.5f,2,neon);foreach(int edge in new[]{-1,1})b.Box(V(edge*3.5f,roof+1.8f,-3.0f),V(.12f,2.9f,.12f),neon);}
                    for(int k=0;k<3;k++)b.Box(V(-2+k*.3f,.6f,-5.3f),V(.2f,1.2f,.2f),"metal");
                    b.Finish();
                    if(id%3==0){var l=Group(props,"Neon bounce "+id).gameObject.AddComponent<Light>();l.type=LightType.Point;l.color=M(neon).GetColor("_BaseColor");l.intensity=3;l.range=8;l.shadows=LightShadows.None;l.transform.position=p+V(0,3,-6);}
                    id++;
                }
            }
            // Narrow tiled cross-block lanes connect the shopping frontages.
            foreach(float x in new[]{-56f,-20,20,56})
            {
                var opening=TransitMapBuilder.Excavation(0);
                if(x>opening.xMin&&x<opening.xMax){float front=opening.yMin,back=opening.yMax;street.Box(V(x,.025f,(-69.5f+front)/2),V(2.2f,.03f,front+69.5f),"stone");street.Box(V(x,.025f,(back+69.5f)/2),V(2.2f,.03f,69.5f-back),"stone");}
                else street.Box(V(x,.025f,0),V(2.2f,.03f,139),"stone");
            }
            Car(V(-38,.07f,16),0,"yellow");Car(V(2,.07f,48),180,"white");
        }
        static void CivicBuilding(string id,Vector3 p,string name,int floors,string accent,float w=23,float d=18)
        {
            var b=new Batch(buildings,key+"_"+id);float h=floors*3.3f;
            b.Box(V(0,h/2,0),V(w,h,d),"stone",true);b.Box(V(0,h/2,-d/2-.03f),V(w-2,h-.7f,.16f),"glass");
            for(int f=0;f<floors;f++)for(float x=-w/2+1;x<w/2;x+=2.4f){b.Box(V(x,f*3.3f+1.6f,-d/2-.18f),V(.18f,3.25f,.3f),"white");b.Box(V(x,f*3.3f,-d/2-.20f),V(2.4f,.22f,.4f),"white");}
            foreach(int s in new[]{-1,1})b.Box(V(s*(w/2-2),h/2,-d/2-.4f),V(1,h,1),"white");
            b.Box(V(0,3.05f,-d/2-1.25f),V(w-2,.25f,3),accent);b.Box(V(0,h+.2f,0),V(w+1,.4f,d+1),"white");
            b.Box(V(0,h-.8f,-d/2-.37f),V(w-4,1.1f,.3f),accent);Label(b.root,name,V(0,h-.8f,-d/2-.55f),w-5,.85f,"signWhite");
            if(id=="Hospital"){b.Box(V(w/2-1,h+2,0),V(.6f,3,.4f),"red");b.Box(V(w/2-1,h+2,0),V(2.5f,.6f,.4f),"red");}
            if(id=="Library")for(int k=-3;k<=3;k++)b.Cylinder(V(k*2.7f,1.5f,-d/2-1.2f),.25f,3,"white");
            b.Finish();b.root.position=p;
        }
        static void Civic()
        {
            CivicBuilding("Library",V(-20,0,55),"한대 시립도서관",3,"wood");CivicBuilding("Police",V(-56,0,18),"한대 경찰서",3,"blue");CivicBuilding("Hospital",V(20,0,55),"한대 종합병원",5,"blue");CivicBuilding("Community",V(56,0,18),"주민 문화센터",2,"wood");CivicBuilding("DistrictOffice",V(-20,0,18),"한대 구청",3,"blue");CivicBuilding("FireStation",V(56,0,-55),"119 안전센터",2,"red");
            var sites=new[]{V(-56,0,-55),V(-20,0,-55),V(20,0,-55),V(-56,0,55),V(56,0,55),V(56,0,-18)};for(int i=0;i<sites.Length;i++)Vacant(i+1,sites[i],19,20);
            Park(V(20,0,18),25,25,true);Park(V(-20,0,-24),25,12,false);Park(V(20,0,-18),25,25,false);Park(V(-56,0,-18),22,25,false);
            Car(V(-49,.05f,4),90,"white");
        }
        static void Factory(int i,Vector3 p)
        {
            var b=new Batch(buildings,key+"_Factory_"+i);b.Box(V(0,3,0),V(21,6,18),"concrete",true);
            for(int k=-2;k<=2;k++){b.Box(V(k*4,6.35f,0),V(4.4f,.22f,18.6f),"metal",false,Quaternion.Euler(0,0,12));b.Box(V(k*4+1.8f,6.05f,0),V(.12f,1.2f,18),"glassLight");}
            for(int k=-2;k<=2;k++){b.Box(V(k*4,1.9f,-9.08f),V(3.1f,3.8f,.18f),"metal");for(int row=0;row<10;row++)b.Box(V(k*4,.25f+row*.35f,-9.2f),V(2.9f,.025f,.08f),"concrete");}
            foreach(int s in new[]{-1,1}){b.Cylinder(V(s*8,6.5f,6),.65f,7,"brick");b.Cylinder(V(s*8,10.1f,6),.8f,.35f,"metal");}
            for(int k=0;k<3;k++)b.Box(V(7+k*.8f,.65f,-10.5f),V(.65f,1.3f,.8f),"wood");Label(b.root,"한대 산업  "+i+"공장",V(0,5,-9.2f),12,.85f,"signWhite");b.Finish();b.root.position=p;
        }
        static void Business()
        {
            Tower(buildings,key+"_HandaeConstruction",V(-20,0,55),21,19,22,"한대건설");Tower(buildings,key+"_MidsizeCompany",V(20,0,55),19,17,12,"세림 테크");Tower(buildings,key+"_SmallCompany1",V(-56,0,55),17,16,7,"다온 시스템");Tower(buildings,key+"_SmallCompany2",V(56,0,55),17,16,6,"미래 디자인");
            int f=0;foreach(var p in new[]{V(-56,0,-24),V(-20,0,-24),V(20,0,-24),V(56,0,-24),V(-56,0,-57),V(-20,0,-57)})Factory(++f,p);
            var sites=new[]{V(-62,0,26),V(-50,0,26),V(-26,0,26),V(-12,0,26),V(27,0,26),V(56,0,26),V(13,0,-57),V(27,0,-57),V(49,0,-57),V(63,0,-57)};for(int i=0;i<sites.Length;i++)Vacant(i+1,sites[i],10.5f,15);
            foreach(float x in new[]{-62f,-54,-22,-14,14,22,54,62})foreach(float z in new[]{-9.2f,9.2f}){Tree(green,V(x,0,z),.6f);Bench(green,V(x+2,0,z));}
        }
        static void Villa(int i,Vector3 p,bool luxury)
        {
            var b=new Batch(buildings,key+"_"+(luxury?"LuxuryVilla_":"BeachHouse_")+i);float w=luxury?13:11,d=10;
            b.Box(V(0,1.5f,0),V(w,3,d),"white",true);b.Box(V(-1.5f,4.55f,1),V(w-2,3.1f,d-2),"stone",true);
            b.Box(V(0,3.15f,0),V(w+.5f,.3f,d+.8f),luxury?"gold":"wood");b.Box(V(-1.5f,6.2f,1),V(w-1,.25f,d-1),"white");
            for(int k=-1;k<=1;k++){Window(b,k*3.4f,1.5f,-5.07f,2.8f,2.5f,"metal","glassLight");Window(b,-1.5f+k*2.8f,4.55f,-3.08f,2.3f,2.4f,"wood","glass");}
            b.Box(V(0,3.75f,-4.8f),V(w,.95f,.10f),"glassLight");b.Box(V(0,4.25f,-4.8f),V(w,.07f,.09f),"metal");
            for(int k=-2;k<=2;k++)b.Box(V(k*2.3f,6.65f,-2),V(.14f,.14f,6),"wood");foreach(int s in new[]{-1,1})b.Box(V(s*4.6f,5.8f,-4.7f),V(.14f,1.7f,.14f),"wood");
            b.Box(V(w/2+1,.07f,0),V(1.8f,.14f,12),"stone",true);b.Box(V(0,.05f,-6.7f),V(w+1,.1f,3),"wood",true);
            for(int k=-1;k<=1;k++){b.Box(V(k*4,.3f,6.4f),V(3,.6f,.8f),"stone");b.Facet(V(k*4,.8f,6.4f),V(3,1.1f,1.1f),"leaf");}
            b.Finish();b.root.position=p;
        }
        static void Coastal()
        {
            for(int i=0;i<4;i++)Villa(i+1,V(-54+i*36,0,-21),false);
            for(int i=0;i<3;i++)Tower(buildings,key+"_CoastalApartment_"+i,V(-56+i*36,0,54),13,15,10+i*2,"바다숲 "+(101+i),true);
            var shopSites=new[]{V(-62,0,18),V(-50,0,18),V(-26,0,18),V(-14,0,18)};
            for(int i=0;i<4;i++)Walkup(buildings,key+"_SeasideShops_"+i,shopSites[i],3,0,new[]{"파도 카페","바다 마트","해변 식당","서핑 스튜디오"}[i],true,i);
            Park(V(56,0,24),22,12,true);
            var sites=new[]{V(56,0,53),V(13,0,18),V(27,0,18),V(-18,0,-9),V(18,0,-9)};
            for(int i=0;i<sites.Length;i++)Vacant(i+1,sites[i],11,9);
            street.Box(V(0,.035f,-37),V(141,.06f,3.2f),"wood");
            for(float x=-65;x<=65;x+=13){if(x>-63&&x<-49)continue;Bench(green,V(x,0,-38));Tree(green,V(x,0,-34.7f),.85f);}
            for(float x=-62;x<65;x+=18){green.Facet(V(x,11,83),V(32,36+(x%3)*3,27),"leaf");green.Facet(V(x+8,7,76),V(24,22,20),"grass");}
            // The sandy promenade remains level; a low barrier marks the water edge.
            for(float x=-71;x<72;x+=2){street.Box(V(x,.55f,-55.5f),V(.12f,1.1f,.12f),"wood",true);street.Box(V(x,.95f,-55.5f),V(2.1f,.10f,.10f),"wood");}
        }
        static void Luxury()
        {
            for(int i=0;i<6;i++)Tower(buildings,key+"_SignielTower_"+i,V(-56+(i%3)*36,0,18+(i/3)*36),12,17,16+i%3*3,"SIGNIEL "+(101+i),true);
            var villas=new[]{V(56,0,51),V(56,0,15),V(56,0,-59),V(20,0,-59),V(-20,0,-59),V(-56,0,-59)};
            for(int i=0;i<villas.Length;i++)Villa(i+1,villas[i],true);
            CivicBuilding("LuxuryMall",V(-52,0,-19),"THE HANDAE  AVENUE",3,"gold",26,22);CivicBuilding("LuxuryMallAnnex",V(-20,0,-19),"ATELIER  GALLERY",2,"gold",24,20);
            Park(V(20,0,-19),25,25,true);Park(V(50.5f,0,-25),36,13,false);
            var sites=new[]{V(-56,0,-45),V(-20,0,-45),V(20,0,-45),V(56,0,29),V(56,0,65)};for(int i=0;i<sites.Length;i++)Vacant(i+1,sites[i],11,9);
        }
        static void Park(Vector3 p,float w,float d,bool fountain)
        {
            green.Box(p+V(0,.025f,0),V(w,.045f,d),"grass");
            // Meandering paired paths; short segments meet with deliberate overlap.
            Vector3 prev=p+V(-w/2,.065f,-d*.15f);
            for(int i=1;i<=30;i++){float t=i/30f;var next=p+V(-w/2+w*t,.065f,Mathf.Sin(t*Mathf.PI*2)*d*.23f);green.Box((prev+next)/2,V(2.3f,.05f,Vector3.Distance(prev,next)+.22f),"stone",false,Quaternion.LookRotation(next-prev));prev=next;}
            for(int k=0;k<6;k++){float x=-w*.4f+k*w*.16f;foreach(int s in new[]{-1,1})Tree(green,p+V(x,0,s*d*.38f),.8f+(k%2)*.2f);Bench(green,p+V(x,0,-d*.29f));}
            if(fountain)
            {
                green.Cylinder(p+V(0,.24f,0),2.5f,.48f,"stone");green.Cylinder(p+V(0,.49f,0),2.15f,.05f,"water");green.Cylinder(p+V(0,1.2f,0),.3f,1.5f,"stone");green.Cylinder(p+V(0,1.85f,0),1,.17f,"stone");
                for(int k=0;k<8;k++){float a=k*Mathf.PI/4;green.Beam(p+V(0,2,0),p+V(Mathf.Sin(a)*1.6f,.5f,Mathf.Cos(a)*1.6f),.055f,"signWhite");}
            }
        }
        static void StreetFurniture()
        {
            foreach(float x in new[]{-46f,-6,34,66})foreach(float z in kind==3?new[]{-28f,6,42,65}:new[]{-64f,-28,8,44,65})
            {Lamp(street,V(x,0,z));if(kind!=0&&!(kind==2&&Mathf.Abs(z)<12))Tree(green,V(x+1.5f,0,z+2),.7f);}
            for(float x=-66;x<70;x+=12){Tree(green,V(x,0,68),.85f);if(kind!=3)Tree(green,V(x,0,-68),.85f);}
            foreach(float x in new[]{-6f,6})foreach(float z in kind==3?new[]{-25f,8,44}:new[]{-30f,8,44}){street.Cylinder(V(x,.4f,z),.12f,.8f,"metal");street.Cylinder(V(x,.65f,z),.125f,.10f,"yellow");}
        }
        static void Car(Vector3 p,float yaw,string color)
        {
            var b=new Batch(props,key+"_Car_"+props.childCount);b.Box(V(0,.65f,0),V(1.7f,.6f,3.5f),color,true);b.Box(V(0,1.12f,.1f),V(1.5f,.65f,1.9f),"glassDark");b.Box(V(0,1.48f,.1f),V(1.55f,.1f,1.95f),color);
            foreach(int s in new[]{-1,1})foreach(float z in new[]{-1.1f,1.1f})b.Box(V(s*.84f,.37f,z),V(.2f,.6f,.6f),"metal");b.Box(V(0,.7f,-1.8f),V(1.4f,.15f,.05f),"warmLight");b.Finish();b.root.position=p;b.root.rotation=Quaternion.Euler(0,yaw,0);
        }
        public static void ConnectVillage()
        {
            if(SceneManager.GetActiveScene().path!=TerracedVillageExpansion.ScenePath)throw new Exception("Open village");
            TransitMapBuilder.ApplyCurrent();EditorSceneManager.playModeStartScene=null;
        }
    }
}
