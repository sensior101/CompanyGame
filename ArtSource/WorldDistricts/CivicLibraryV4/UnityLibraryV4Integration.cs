// Reproducible editor-only build and QA. Install only in the isolated validation
// project's Assets/Editor. All gameplay uses the project's existing components.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CompanyGame.World.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.CivicLibraryV4
{
    public static class UnityLibraryV4Integration
    {

        const string Art = "Assets/Art/WorldDistricts/CivicLibraryV4";
        const string InteriorScene = "Assets/Scenes/Interiors/CivicLibraryInterior.unity";
        const string DistrictScene = "Assets/Scenes/Maps/CivicDistrict.unity";
        const string Qa = "LibraryV4QA";
        [Serializable] public class Mat { public string name; public float[] color; public float metallic, roughness, alpha; }
        [Serializable] public class Node { public string name, parent; public float[] position, rotation; }
        [Serializable] public class Inventory { public Mat[] materials; public Node[] objects; }
        [Serializable] public class Check { public string name, detail; public bool pass; }
        [Serializable] public class Report {
            public bool pass; public int seats, colliders, renderers, triangles;
            public List<Check> checks = new List<Check>(); public List<string> captures = new List<string>();
        }
        static void Add(Report r,string name,bool pass,string detail) => r.checks.Add(new Check{name=name,pass=pass,detail=detail});
        static void Invoke(object obj,string method) => obj.GetType().GetMethod(method, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(obj,null);
        public static void Build() {
            if (!Application.dataPath.Replace('\\','/').Contains("/Temp/CivicLibraryV4Validation/Assets")) throw new Exception("Isolated project only");
            Directory.CreateDirectory(Qa); Directory.CreateDirectory(Art+"/Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var inv=JsonUtility.FromJson<Inventory>(File.ReadAllText(Art+"/inventory.json"));
            var mats=new Dictionary<string,Material>();
            foreach(var s in inv.materials) {
                string path=Art+"/Materials/"+s.name+".mat";
                var m=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m,path);}
                var c=new Color(s.color[0],s.color[1],s.color[2],s.alpha).gamma; c.a=s.alpha;
                m.SetColor("_BaseColor",c); m.SetFloat("_Metallic",s.metallic); m.SetFloat("_Smoothness",1-s.roughness);
                if(s.alpha<1){m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",5);m.SetFloat("_DstBlend",10);m.SetFloat("_ZWrite",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");m.renderQueue=3000;m.SetFloat("_Cull",0);}
                if(s.name=="MAT_WarmLight"){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",c*1.5f);}
                EditorUtility.SetDirty(m); mats[s.name]=m;
            }
            var importer=(ModelImporter)AssetImporter.GetAtPath(Art+"/Library_V4.fbx");
            importer.globalScale=1; importer.useFileScale=true; importer.isReadable=true;importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;importer.addCollider=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            foreach(var pair in mats) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),pair.Key),pair.Value);
            importer.SaveAndReimport();
            var district=EditorSceneManager.OpenScene(DistrictScene,OpenSceneMode.Single); ConfigureDistrict(district);
            var cameraSource=Components<Camera>(district).Single(c=>c.CompareTag("MainCamera"));
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var map=new GameObject("Map_CivicLibraryInterior").transform;
            var building=Child(map,"10_World");
            var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/Library_V4.fbx"),building);
            model.name="Library_V4";
            // Correct the FBX coordinate conversion by comparing authored entry markers.
            var entry=Named(model.transform,"SPAWN_Entry");
            if(entry.position.z>0) model.transform.Rotate(0,180,0,Space.World);
            var nodes=model.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name,t=>t);
            foreach(var mf in model.GetComponentsInChildren<MeshFilter>(true)) {
                if(mf.name.StartsWith("COLLIDER_MESH_")) {
                    var mr=mf.GetComponent<Renderer>(); if(mr) mr.enabled=false;
                    var col=mf.gameObject.AddComponent<MeshCollider>();col.sharedMesh=mf.sharedMesh;
                    string visual=mf.name.Substring("COLLIDER_MESH_".Length);
                    if(nodes.TryGetValue(visual,out var target) && target!=mf.transform) mf.transform.SetParent(target,true);
                } else if(mf.name.StartsWith("COL_")) {var r=mf.GetComponent<Renderer>();if(r)r.enabled=false;}
            }
            foreach(var anchor in nodes.Values.Where(t=>t.name.StartsWith("SEAT_")).ToArray()) {
                Transform owner=null;
                var spec=inv.objects.First(n=>n.name==anchor.name);
                if(!string.IsNullOrEmpty(spec.parent)) owner=nodes[spec.parent];
                else if(anchor.name.StartsWith("SEAT_Tier_")) owner=nodes[anchor.name.Replace("SEAT_","Cushion_")];
                else {
                    string prefix=anchor.name.StartsWith("SEAT_Sofa_Entry")?"Sofa_Entry_":null;
                    if(prefix!=null){owner=Child(model.transform,"Seating_Sofa_Entry");foreach(var t in nodes.Values.Where(t=>t.name.StartsWith(prefix)).ToArray())t.SetParent(owner,true);}
                    else owner=model.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && (r.name.Contains("Cushion")||r.name.Contains("Seat"))).OrderBy(r=>(r.bounds.center-anchor.position).sqrMagnitude).First().transform;
                }
                var w=owner.GetComponent<WorldObject>()??owner.gameObject.AddComponent<WorldObject>();w.objectType=WorldObjectType.InteractiveFixture;w.functions=FurnitureFunction.Seating;
                anchor.SetParent(owner,true);
                // Blender Z rotation maps to Unity Y; anchors must remain upright.
                anchor.rotation=Quaternion.Euler(0,-spec.rotation[2]*Mathf.Rad2Deg+180,0);
                var seat=anchor.gameObject.AddComponent<Seat>();seat.owner=w;seat.radius=1.8f;
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(model,Art+"/CivicLibraryV4.prefab",InteractionMode.AutomatedAction);
            var gameplay=Child(map,"20_Gameplay");
            Spawn(gameplay,"default",entry.position,0); Spawn(gameplay,"library_entry",entry.position,0);
            var exit=Child(gameplay,"Library Exit Portal");exit.position=new Vector3(0,.04f,-9.55f);Portal(exit,DistrictScene,"library_exit","도서관 밖으로 나가기",.75f);
            Object.Instantiate(cameraSource.gameObject,map).name="60_PlayerCamera";
            var lighting=Child(map,"50_Lighting");
            var sun=Child(lighting,"Daylight").gameObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.4f;sun.color=new Color(1,.95f,.86f);sun.transform.rotation=Quaternion.Euler(48,-30,0);sun.shadows=LightShadows.Soft;
            RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.65f,.65f,.6f);RenderSettings.fog=false;RenderSettings.skybox=null;
            foreach(var pos in new[]{new Vector3(-10,3,-3),new Vector3(0,6,1),new Vector3(10,3,1),new Vector3(-9,6,5),new Vector3(9,6,5)}){
                var l=Child(lighting,"Fill_"+pos).gameObject.AddComponent<Light>();l.type=LightType.Point;l.transform.position=pos;l.range=13;l.intensity=1.5f;l.color=new Color(1,.93f,.8f);l.shadows=LightShadows.None;
            }
            Directory.CreateDirectory("Assets/Scenes/Interiors");AssetDatabase.Refresh();EditorSceneManager.SaveScene(scene,InteriorScene);EditorSceneManager.CloseScene(district,true);
            AddBuildScene(InteriorScene);AddBuildScene(DistrictScene);AssetDatabase.SaveAssets();
            Validate();
        }
        public static void Validate(){
            Directory.CreateDirectory(Qa); var r=new Report();
            var district=EditorSceneManager.OpenScene(DistrictScene,OpenSceneMode.Single);BenchLogicQA(r,district);
            var scene=EditorSceneManager.OpenScene(InteriorScene,OpenSceneMode.Single);
            var root=scene.GetRootGameObjects().Single(g=>g.name=="Map_CivicLibraryInterior");
            r.seats=root.GetComponentsInChildren<Seat>().Length;r.colliders=root.GetComponentsInChildren<MeshCollider>().Length;
            r.renderers=root.GetComponentsInChildren<Renderer>().Count(x=>x.enabled);
            r.triangles=root.GetComponentsInChildren<MeshFilter>().Where(x=>x.GetComponent<Renderer>().enabled).Sum(x=>x.sharedMesh.triangles.Length/3);
            Add(r,"Seat anchors",r.seats==62,"Count="+r.seats);
            Add(r,"Authored collision meshes",r.colliders==261,"Count="+r.colliders);
            Add(r,"URP materials",root.GetComponentsInChildren<Renderer>().All(x=>x.sharedMaterials.All(m=>m&&m.shader.name=="Universal Render Pipeline/Lit")),"All imported materials remapped");
            Add(r,"No replacement player",!root.GetComponentsInChildren<CharacterController>().Any(),"Uses persistent project player");
            Physics.SyncTransforms();
            Walk(r,"Entry to circulation stair",new[]{new Vector3(0,.04f,-8.6f),new Vector3(3.45f,0,-8.6f),new Vector3(3.45f,0,-4)});
            Walk(r,"Dedicated stair to upper level",new[]{new Vector3(3.45f,0,-4),new Vector3(3.45f,3.6f,6.3f)});
            Walk(r,"Upper bridges and west stairs",new[]{new Vector3(3.45f,3.6f,6.3f),new Vector3(6.2f,3.6f,6.4f),new Vector3(6.2f,3.6f,-8.3f),new Vector3(-6.2f,3.6f,-8.3f),new Vector3(-6.2f,3.6f,6.4f),new Vector3(-13.4f,3.6f,6.4f),new Vector3(-13.4f,0,-2)});
            Capture(r,"Unity_Entry.png",new Vector3(0,2,-8.6f),new Vector3(0,3,3),70);
            Capture(r,"Unity_Upper.png",new Vector3(6,5.3f,7),new Vector3(-2,2.2f,-4),70);
            r.pass=r.checks.All(c=>c.pass);File.WriteAllText(Qa+"/Report.json",JsonUtility.ToJson(r,true));
            if(!r.pass)throw new Exception("V4 QA failed; see Report.json");Debug.Log("V4_BUILD_QA_PASS");
        }
        static void ConfigureDistrict(Scene scene)
        {
            var root = scene.GetRootGameObjects().Single(t => t.name == "Map_CivicDistrict").transform;
            var library = Named(root, "CivicDistrict_Library");
            var bench = Named(library, "Bench");
            var world = bench.GetComponent<WorldObject>() ?? bench.gameObject.AddComponent<WorldObject>();
            world.objectType = WorldObjectType.InteractiveFixture; world.functions = FurnitureFunction.Seating;
            world.furnitureItem = null;
            for (int i = 0; i < 2; i++)
            {
                var point = Child(bench, "PublicBenchSeat_" + (i + 1));
                point.SetPositionAndRotation(library.TransformPoint(new Vector3(i == 0 ? -11.15f : -9.85f, .565f, -15.37f)), Quaternion.Euler(0, 180, 0));
                var seat = point.GetComponent<Seat>() ?? point.gameObject.AddComponent<Seat>();
                seat.owner = world; seat.radius = 2; seat.assignedOccupant = null;
            }
            var entry = Child(root, "Library Interior Portal");
            entry.position = library.TransformPoint(new Vector3(0, .8f, -9.6f));
            Portal(entry, InteriorScene, "library_entry", "도서관 들어가기", 1.6f);
            Spawn(root, "library_exit", library.TransformPoint(new Vector3(0, .80f, -10.1f)), 180);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save exterior portal and bench.");
        }

        static void BenchLogicQA(Report report, Scene scene)
        {
            var seats = Components<Seat>(scene).Where(s => s.name.StartsWith("PublicBenchSeat_", StringComparison.Ordinal)).ToArray();
            Add(report, "Public bench has two usable positions", seats.Length == 2 && seats.All(s => s.owner && s.owner.HasFunction(FurnitureFunction.Seating)), "Count=" + seats.Length);
            if (seats.Length != 2) return;
            var player = new GameObject("__BenchExistingComponentQA");
            try
            {
                var movement = player.AddComponent<PlayerMovement>();
                var seating = player.AddComponent<PlayerSeating>();
                Invoke(movement, "Awake"); Invoke(seating, "Awake");
                var seat = seats[0]; foreach (var s in seats) Invoke(s, "OnEnable");
                Vector3 approach = seat.transform.position + seat.transform.forward * .92f; approach.y = .06f;
                var rotation = Quaternion.Euler(0, 0, 0);
                player.transform.SetPositionAndRotation(approach, rotation); Physics.SyncTransforms();
                Add(report, "Bench range and visible interaction target", seat.InRange(player.transform) && Seat.FindNearest(player.transform) == seat,
                    "Approach=" + approach.ToString("F3") + "; target=" + (Seat.FindNearest(player.transform) ? Seat.FindNearest(player.transform).name : "none"));
                bool sat = seating.TrySit(seat);
                Add(report, "Bench uses existing PlayerSeating.TrySit", sat && seating.IsSeated && seat.Occupant == player.transform && !player.GetComponent<CharacterController>().enabled, "Existing component path, temporary QA actor only.");
                var second = new GameObject("__BenchOccupancyQA");
                try { second.transform.position = approach; Add(report, "Occupied bench position refuses another actor", !seat.Reserve(second.transform) && Seat.FindNearest(second.transform) != seat, "No occupied-seat prompt target."); }
                finally { Object.DestroyImmediate(second); }
                bool stood = seating.TryStand();
                Add(report, "Space stand path restores exact approach pose", stood && !seating.IsSeated && seat.Occupant == null &&
                    Vector3.Distance(player.transform.position, approach) < .001f && Quaternion.Angle(player.transform.rotation, rotation) < .01f &&
                    movement.enabled && player.GetComponent<CharacterController>().enabled,
                    "TryStand() is the existing Space branch; exact saved position/rotation restored.");
            }
            finally { Object.DestroyImmediate(player); Physics.SyncTransforms(); }
        }

        static void Walk(Report report, string label, Vector3[] points)
        {
            var go = new GameObject("__LibraryWalkQA"); var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = .35f; cc.center = Vector3.up * .9f; cc.stepOffset = .23f; cc.slopeLimit = 45; cc.skinWidth = .02f; cc.minMoveDistance = 0;
            string failure = null; int samples = 0;
            try
            {
                cc.enabled = false; go.transform.position = points[0] + Vector3.up * .03f; cc.enabled = true; Physics.SyncTransforms();
                for (int i = 0; i < 5; i++) cc.Move(Vector3.down * .02f);
                for (int i = 1; i < points.Length && failure == null; i++)
                {
                    int limit = Mathf.CeilToInt(XZ(points[i] - go.transform.position).magnitude / .045f) + 80;
                    for (int step = 0; step < limit; step++)
                    {
                        Vector3 delta = points[i] - go.transform.position; delta.y = 0;
                        if (delta.magnitude < .06f) break;
                        cc.Move(Vector3.ClampMagnitude(delta, .055f) + Vector3.down * .03f); samples++;
                    }
                    Vector3 p = go.transform.position;
                    if (XZ(p - points[i]).magnitude > .12f || Mathf.Abs(p.y - points[i].y) > .16f)
                        failure = "Waypoint " + i + " expected " + points[i].ToString("F3") + ", reached " + p.ToString("F3");
                }
                Add(report, label, failure == null, failure ?? (samples + " CharacterController movement samples; radius .35, height 1.8, step .23."));
            }
            finally { Object.DestroyImmediate(go); Physics.SyncTransforms(); }
        }
        static bool Clear(Vector3 feet) => !Physics.CheckCapsule(feet + Vector3.up * .37f, feet + Vector3.up * 1.47f, .35f, ~0, QueryTriggerInteraction.Ignore);
        static void Capture(Report report, string name, Vector3 pos, Vector3 target, float fov)
        {
            var go = new GameObject("__LibraryCapture"); var camera = go.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
            camera.fieldOfView = fov; camera.nearClipPlane = .06f; camera.farClipPlane = 300; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.22f, .25f, .29f);
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            var rt = RenderTexture.GetTemporary(1600, 1000, 24, RenderTextureFormat.ARGB32); var old = RenderTexture.active;
            try
            {
                camera.targetTexture = rt; camera.Render(); camera.Render(); camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
                string path = Qa + "/" + name; File.WriteAllBytes(path, image.EncodeToPNG()); report.captures.Add(path);
            }
            finally { camera.targetTexture = null; RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(image); Object.DestroyImmediate(go); }
        }
        static void Portal(Transform t, string path, string spawn, string display, float radius)
        {
            // Match the shared building interaction owner and its prompt/input
            // priority; remove only this builder's earlier legacy portal component.
            var legacy = t.GetComponent<MapPortal>(); if (legacy) Object.DestroyImmediate(legacy);
            var p = t.GetComponent<StoreInteractionPoint>() ?? t.gameObject.AddComponent<StoreInteractionPoint>(); p.targetScenePath = path;
            p.targetSpawnId = spawn; p.prompt = display; p.radius = radius; p.heightTolerance = 1;
        }
        static void Spawn(Transform parent, string id, Vector3 position, float yaw)
        {
            var t = Child(parent, "Spawn_" + id); t.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            var spawn = t.GetComponent<MapSpawnPoint>() ?? t.gameObject.AddComponent<MapSpawnPoint>(); spawn.spawnId = id;
        }
        static void AddBuildScene(string path)
        {
            var entries = EditorBuildSettings.scenes.ToList(); var existing = entries.FirstOrDefault(s => s.path == path);
            if (existing != null) existing.enabled = true; else entries.Add(new EditorBuildSettingsScene(path, true)); EditorBuildSettings.scenes = entries.ToArray();
        }
        static Transform Child(Transform parent, string name)
        { var t = parent.Find(name); if (t) return t; t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        static Transform Named(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)
            ?? throw new InvalidOperationException("Required Blender object missing: " + name);
        static IEnumerable<T> Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        static Color C(float[] a) => a != null && a.Length >= 3 ? new Color(a[0], a[1], a[2], a.Length > 3 ? a[3] : 1) : Color.white;
        static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    }
}
