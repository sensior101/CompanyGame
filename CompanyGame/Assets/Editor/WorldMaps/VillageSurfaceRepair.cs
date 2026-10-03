using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
using static CompanyGame.Editor.WorldMaps.DistrictGeometry;

namespace CompanyGame.Editor.WorldMaps
{
    public static class VillageSurfaceRepair
    {
        static GameObject Root()=>SceneManager.GetActiveScene().GetRootGameObjects().Single(g=>g.name=="Map_daldongnaemap");
        public static void Station()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
            var root=Root();var map=root.transform.Find("10_World/Daldongne Warm Village");
            map.Find("10_Buildings/Station").localScale=Vector3.one;
            var landing=map.GetComponentsInChildren<Transform>().Single(t=>t.name=="Walk_Path_SubwayLanding");
            var original=PrefabUtility.GetCorrespondingObjectFromSource(landing);
            if(original){landing.localPosition=original.localPosition;landing.localRotation=original.localRotation;landing.localScale=original.localScale;PrefabUtility.RecordPrefabInstancePropertyModifications(landing);}
            TerracedVillageExpansion.RefineConnections();
            Physics.SyncTransforms();EditorSceneManager.SaveScene(root.scene);
        }
        public static void Housing()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");Init();var root=Root();
            var buildings=root.transform.Find("10_World/Daldongne Warm Village/10_Buildings");
            // These five objects were added by the expansion. Original authored homes
            // and the single original Gosiwon are intentionally retained.
            foreach(var t in buildings.Cast<Transform>().Where(t=>t.name.StartsWith("OneRoom_")).ToArray())Object.DestroyImmediate(t.gameObject);
            var old=root.transform.Find("10_World/One Room Neighborhood");if(old)Object.DestroyImmediate(old.gameObject);
            var parent=Group(root.transform.Find("10_World"),"One Room Neighborhood");
            for(int i=0;i<5;i++)Walkup(parent,"Village_OneRoom_"+(i+1),V(-26+i*13,14.4f,62.2f),3+i%2,0,new[]{"달빛빌라","햇살원룸","언덕빌라","푸른원룸","해오름빌라"}[i],false,i);
            // Infill sites have independent foundations at the original terrace level.
            var sites=new[]{V(29,1.2f,-29),V(44,1.2f,-42),V(-47,7.2f,20),V(-45,3.6f,6.5f),V(26,10.8f,36)};
            for(int i=0;i<sites.Length;i++)
            {
                var p=sites[i];Walkup(parent,"Village_OneRoom_"+(i+6),p,3+i%2,0,new[]{"솔빛원룸","가온빌라","다온원룸","산들빌라","해든원룸"}[i],false,i+2);
                // Remove only vegetation whose trunks sit inside the new footprint.
                var trees=root.transform.Find("10_World/Daldongne Warm Village/20_Nature/Trees");
                foreach(var t in trees.Cast<Transform>().ToArray())
                {var rs=t.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);if(Mathf.Abs(b.center.x-p.x)<6&&Mathf.Abs(b.center.z-p.z)<6&&Mathf.Abs(b.min.y-p.y)<2)Object.DestroyImmediate(t.gameObject);}
            }
            EditorSceneManager.MarkSceneDirty(root.scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(root.scene);Physics.SyncTransforms();
        }
        public static void Photograph(string name,Vector3 position,Vector3 focus,float fov=55,int width=1500,int height=1050)
        {
            Directory.CreateDirectory("../ArtSource/WorldDistricts/QA");
            var go=new GameObject("Temporary map photograph"){hideFlags=HideFlags.HideAndDontSave};var c=go.AddComponent<Camera>();
            var original=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>()).FirstOrDefault();
            if(original){c.CopyFrom(original);var originalData=original.GetUniversalAdditionalCameraData();var data=c.GetUniversalAdditionalCameraData();data.renderPostProcessing=originalData.renderPostProcessing;data.volumeLayerMask=originalData.volumeLayerMask;}c.enabled=false;c.orthographic=false;c.farClipPlane=650;c.fieldOfView=fov;
            c.transform.position=position;c.transform.LookAt(focus);
            var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.DefaultHDR,RenderTextureReadWrite.Default,4);var display=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);var previous=RenderTexture.active;var tex=new Texture2D(width,height,TextureFormat.RGB24,false);bool srgb=GL.sRGBWrite;
            try{c.targetTexture=rt;c.Render();GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(rt,display);RenderTexture.active=display;tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();File.WriteAllBytes("../ArtSource/WorldDistricts/QA/"+name+".png",tex.EncodeToPNG());}
            finally{GL.sRGBWrite=srgb;c.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);RenderTexture.ReleaseTemporary(display);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
        }
        public static void Connections()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
            Init();var root=Root();var previous=root.transform.Find("10_World/Surface Connections");if(previous)Object.DestroyImmediate(previous.gameObject);
            var parent=Group(root.transform.Find("10_World"),"Surface Connections");
            var b=new Batch(parent,"Station rear approach fill","Village_StationApproachFill");b.Box(V(-29.1f,1.10f,-34.0f),V(1.8f,.20f,1.8f),"concrete",true);
            // The source cheeks ended with the stairs, 3.6 m before the tunnel wall.
            // Close the below-ground sides and continue the landing to the back wall.
            foreach(float x in new[]{-39.65f,-23.35f})b.Box(V(x,-.92f,-36.8f),V(.32f,4.2f,3.9f),"concrete",true);
            b.Box(V(-31.5f,-2.35f,-37.025f),V(16.05f,.30f,4.02f),"concrete",true);b.Finish();
            var map=root.transform.Find("10_World/Daldongne Warm Village");
            var oldLanding=map.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Walk_Path_SubwayLanding");oldLanding.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(oldLanding);
            var oldLandingCollision=oldLanding.GetComponent<Collider>();if(oldLandingCollision){oldLandingCollision.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(oldLandingCollision);}
            var pit=new Bounds(V(-31.5f,-1,-40.7f),V(16.0f,8,11.3f));
            foreach(var r in map.GetComponentsInChildren<MeshRenderer>().Where(r=>(r.name.StartsWith("Rock_Shore")||r.name.StartsWith("Wall_Coastal"))&&r.bounds.Intersects(pit)))TrimShoreAtStation(r.GetComponent<MeshFilter>());
            foreach(var route in (JArray)JObject.Parse(File.ReadAllText("../ArtSource/Daldongne/warm_routes.json"))["routes"])
            {
                string name=(string)route["name"];if(name!="East"&&name!="CentralCrest"&&name!="WestMiddle")continue;
                Func<JToken,Vector3> point=t=>map.TransformPoint(V((float)t[0],(float)t[2],(float)t[1]));
                var start=point(route["a"]);var end=point(route["b"]);var delta=end-start;delta.y=0;float length=delta.magnitude;var forward=delta.normalized;var side=Vector3.Cross(Vector3.up,forward)*((float)route["width"]*.9f+.25f);
                // A broad eased landing reaches terrace height before the T junction.
                // Its rising face stays below the motor's slope limit and joins the
                // original stair/ramp without introducing a vertical lip.
                var a=end-forward*3.9f;a.y=end.y-(end.y-start.y)*3.9f/length+.005f;var mid=end-forward*1.75f;mid.y=end.y;var last=end+forward*.22f;last.y=end.y;
                var vertices=new[]{a-side,a+side,mid-side,mid+side,last-side,last+side};
                var mesh=new Mesh{name="Repair_"+name};mesh.vertices=vertices;mesh.triangles=new[]{0,2,3,0,3,1,2,4,5,2,5,3};mesh.RecalculateNormals();mesh.RecalculateBounds();mesh=SaveMesh("Village_Repair_"+name,mesh);
                var t=Group(parent,"Repair_"+name);t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=M("concrete");t.gameObject.AddComponent<MeshCollider>().sharedMesh=mesh;
            }
            var houses=root.transform.Find("10_World/One Room Neighborhood");for(int i=1;i<=5;i++)houses.Find("Village_OneRoom_"+i).position=V(-26+(i-1)*13,14.4f,62.2f);
            houses.Find("Village_OneRoom_7").position=V(44,1.2f,-42);houses.Find("Village_OneRoom_8").position=V(-47,7.2f,20);houses.Find("Village_OneRoom_9").position=V(-45,3.6f,6.5f);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);Physics.SyncTransforms();
        }
        static List<Vector3> HalfPlane(List<Vector3> input,Func<Vector3,float> distance,bool inside)
        {
            var result=new List<Vector3>();if(input.Count==0)return result;
            for(int i=0;i<input.Count;i++)
            {
                var a=input[i];var b=input[(i+1)%input.Count];float da=distance(a),db=distance(b);bool keepA=inside?da>=0:da<=0,keepB=inside?db>=0:db<=0;
                if(keepA)result.Add(a);if(keepA!=keepB)result.Add(Vector3.Lerp(a,b,da/(da-db)));
            }
            return result;
        }
        static void TrimShoreAtStation(MeshFilter filter)
        {
            // Preserve shore masonry outside the stairwell. Boolean cuts in the
            // island did not cut these separate decorative rock/wall meshes.
            var source=PrefabUtility.GetCorrespondingObjectFromSource(filter);var mesh=source?source.sharedMesh:filter.sharedMesh;
            var vertices=mesh.vertices;var triangles=mesh.triangles;var result=new List<Vector3>();
            Func<Vector3,float>[] planes={p=>p.x+39.53f,p=>-23.47f-p.x,p=>p.z+46.35f,p=>-35.01f-p.z};
            for(int i=0;i<triangles.Length;i+=3)
            {
                var remaining=new List<Vector3>{filter.transform.TransformPoint(vertices[triangles[i]]),filter.transform.TransformPoint(vertices[triangles[i+1]]),filter.transform.TransformPoint(vertices[triangles[i+2]])};
                foreach(var plane in planes)
                {
                    var outside=HalfPlane(remaining,plane,false);
                    for(int k=1;k<outside.Count-1;k++){result.Add(filter.transform.InverseTransformPoint(outside[0]));result.Add(filter.transform.InverseTransformPoint(outside[k]));result.Add(filter.transform.InverseTransformPoint(outside[k+1]));}
                    remaining=HalfPlane(remaining,plane,true);if(remaining.Count==0)break;
                }
            }
            if(result.Count==0){var renderer=filter.GetComponent<Renderer>();renderer.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);var emptyCollider=filter.GetComponent<Collider>();if(emptyCollider){emptyCollider.enabled=false;PrefabUtility.RecordPrefabInstancePropertyModifications(emptyCollider);}return;}
            var cut=new Mesh{name="SubwayCut_"+filter.name};cut.SetVertices(result);cut.SetTriangles(Enumerable.Range(0,result.Count).ToArray(),0);cut.RecalculateNormals();cut.RecalculateBounds();filter.sharedMesh=SaveMesh("Village_SubwayCut_"+filter.name,cut);PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            var collider=filter.GetComponent<MeshCollider>();if(collider){collider.sharedMesh=filter.sharedMesh;PrefabUtility.RecordPrefabInstancePropertyModifications(collider);}
        }
        public static object Survey()
        {
            var root=Root();var map=root.transform.Find("10_World/Daldongne Warm Village");Physics.SyncTransforms();
            var floor=map.Find("00_Terrain").GetComponentsInChildren<Collider>().Where(c=>c.enabled).ToArray();
            var rows=new List<object>();
            foreach(float z in new[]{-29f,20,37,36})foreach(float x in new[]{-43f,-35,26,29,44})
            {var hits=new List<float>();foreach(var c in floor)if(c.Raycast(new Ray(V(x,40,z),Vector3.down),out var hit,60)&&hit.normal.y>.6f)hits.Add(hit.point.y);rows.Add(new{x,z,y=hits.Count>0?hits.Max():-999});}
            return rows;
        }
        public static object SurfaceAudit()
        {
            var root=Root();var map=root.transform.Find("10_World/Daldongne Warm Village");Physics.SyncTransforms();
            var failures=new List<object>();int samples=0;
            var renderers=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            // Raycast against visible mesh triangles, not only the hidden ramp collider.
            var probes=new List<GameObject>();var visible=new List<MeshCollider>();
            foreach(var r in renderers)
            {
                var f=r.GetComponent<MeshFilter>();if(!f||!f.sharedMesh)continue;
                if(!r.name.StartsWith("Walk_")&&!r.name.StartsWith("Terrain_")&&!r.name.StartsWith("Station_")&&!r.name.StartsWith("Repair_")&&!r.name.StartsWith("Collider_Ramp_")&&r.name!="concrete")continue;
                var g=new GameObject("Temporary visible surface probe"){hideFlags=HideFlags.HideAndDontSave};g.transform.SetPositionAndRotation(r.transform.position,r.transform.rotation);g.transform.localScale=r.transform.lossyScale;
                var c=g.AddComponent<MeshCollider>();c.sharedMesh=f.sharedMesh;visible.Add(c);probes.Add(g);
            }
            try
            {
                Action<string,Vector3> test=(name,p)=>{samples++;bool found=false;foreach(var c in visible)if(c.Raycast(new Ray(p+Vector3.up*.22f,Vector3.down),out var hit,.52f)&&hit.normal.y>.6f){found=true;break;}if(!found)failures.Add(new{name,x=p.x,y=p.y,z=p.z});};
                foreach(var r in (JArray)JObject.Parse(File.ReadAllText("../ArtSource/Daldongne/warm_routes.json"))["routes"])
                {
                    string name=(string)r["name"];Func<JToken,Vector3> point=t=>map.TransformPoint(V((float)t[0],(float)t[2],(float)t[1]));var a=point(r["a"]);var b=point(r["b"]);var side=Vector3.Cross(Vector3.up,(b-a).normalized).normalized;float half=(float)r["width"]*.9f;
                    for(float d=0;d<=Vector3.Distance(a,b);d+=.35f)foreach(float lane in new[]{-half+.12f,0,half-.12f})test(name,Vector3.Lerp(a,b,d/Vector3.Distance(a,b))+side*lane);
                }
                for(float x=-43.8f;x<=-19.2f;x+=.45f)for(float z=-49.2f;z<=-33.6f;z+=.45f)
                {if(x>-40.3f&&x<-22.7f&&z>-46.4f&&z<-34.7f)continue;test("StationApron",V(x,1.2f,z));}
            }
            finally{foreach(var g in probes)Object.DestroyImmediate(g);Physics.SyncTransforms();}
            Directory.CreateDirectory("../ArtSource/WorldDistricts/QA");File.WriteAllText("../ArtSource/WorldDistricts/QA/village-visible-surfaces.json",JsonConvert.SerializeObject(new{samples,failures},Formatting.Indented));
            return new{samples,failures=failures.Count,groups=failures.GroupBy(f=>(string)f.GetType().GetProperty("name")?.GetValue(f)).Count()};
        }
    }
}
