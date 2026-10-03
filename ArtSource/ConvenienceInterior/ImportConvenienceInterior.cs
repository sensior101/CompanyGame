// Execute through Unity CLI run_script; kept outside Assets to avoid domain reloads.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class ImportConvenienceInterior
{
    const string Folder = "Assets/Art/Interiors/ConvenienceStore";
    const string PrefabPath = Folder + "/ConvenienceStoreInterior.prefab";
    const string ScenePath = "Assets/Scenes/Interiors/ConvenienceStoreInterior.unity";
    [Serializable] public class Data { public Part[] parts; public Mat[] materials; public Proxy[] colliders; public Lamp[] lights; }
    [Serializable] public class Part { public string name, group, material; public float[] vertices,normals,uvs; public int[] triangles; }
    [Serializable] public class Mat { public string name,texture; public float[] color; public float alpha,roughness,metallic,emission; }
    [Serializable] public class Proxy { public string name; public float[] center,size; }
    [Serializable] public class Lamp { public float[] position; public float intensity,range; }
    static Vector3 V(float[] v) => new Vector3(v[0],v[1],v[2]);

    public static string Build()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before importing.");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Data data;
        using(var file=File.OpenRead("../ArtSource/ConvenienceInterior/ConvenienceStore.meshdata.json.gz"))
        using(var zip=new GZipStream(file,CompressionMode.Decompress))
        using(var reader=new StreamReader(zip))data=Newtonsoft.Json.JsonConvert.DeserializeObject<Data>(reader.ReadToEnd());
        if(data==null || data.materials==null || data.parts==null)throw new InvalidDataException("Model payload is incomplete.");
        var current=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var root=new GameObject("ConvenienceStoreInterior");
            var mats=new Dictionary<string,Material>();
            foreach(var m in data.materials)
            {
                string path=Folder+"/Materials/"+m.name+".mat";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
                mat.SetColor("_BaseColor",new Color(m.color[0],m.color[1],m.color[2],m.alpha).gamma);
                mat.SetFloat("_Metallic",m.metallic);mat.SetFloat("_Smoothness",1-m.roughness);
                if(!string.IsNullOrEmpty(m.texture))
                {
                    string tp=Folder+"/Textures/"+m.texture;
                    var ti=(TextureImporter)AssetImporter.GetAtPath(tp);
                    if(ti.maxTextureSize!=4096 || ti.textureCompression!=TextureImporterCompression.CompressedHQ)
                    {ti.maxTextureSize=4096;ti.textureCompression=TextureImporterCompression.CompressedHQ;ti.mipmapEnabled=true;ti.SaveAndReimport();}
                    mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
                }
                if(m.emission>0){mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",new Color(m.color[0],m.color[1],m.color[2])*m.emission);mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;}
                if(m.alpha<1)
                {
                    mat.SetFloat("_Surface",1);mat.SetFloat("_Blend",0);mat.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);mat.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite",0);mat.SetInt("_Cull",(int)CullMode.Off);mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");mat.SetOverrideTag("RenderType","Transparent");mat.renderQueue=3000;
                    mat.SetShaderPassEnabled("ShadowCaster",false);
                }
                mats.Add(m.name,mat);EditorUtility.SetDirty(mat);
            }
            var groups=new Dictionary<string,Transform>();
            Directory.CreateDirectory(Folder+"/Meshes");
            foreach(var p in data.parts)
            {
                if(!groups.ContainsKey(p.group)) {var g=new GameObject(p.group);g.transform.SetParent(root.transform,false);groups.Add(p.group,g.transform);}
                var vertices=new Vector3[p.vertices.Length/3];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
                for(int i=0;i<vertices.Length;i++){vertices[i]=new Vector3(p.vertices[i*3],p.vertices[i*3+1],p.vertices[i*3+2]);normals[i]=new Vector3(p.normals[i*3],p.normals[i*3+1],p.normals[i*3+2]);uv[i]=new Vector2(p.uvs[i*2],p.uvs[i*2+1]);}
                string path=Folder+"/Meshes/"+p.name+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool isNew=!mesh;
                if(isNew)mesh=new Mesh();else mesh.Clear();
                mesh.name=p.name;mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=p.triangles;mesh.RecalculateBounds();
                if(isNew)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
                var go=new GameObject(p.name);go.transform.SetParent(groups[p.group],false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mats[p.material];renderer.receiveShadows=true;
                if(p.material=="Glass") renderer.shadowCastingMode=ShadowCastingMode.Off;
                go.isStatic=true;
            }
            var collision=new GameObject("20_Collision_Proxies");collision.transform.SetParent(root.transform,false);
            foreach(var p in data.colliders){var go=new GameObject(p.name);go.transform.SetParent(collision.transform,false);var box=go.AddComponent<BoxCollider>();box.center=V(p.center);box.size=V(p.size);go.isStatic=true;}
            var spawn=new GameObject("InteriorSpawnPoint");spawn.transform.SetParent(root.transform,false);spawn.transform.localPosition=new Vector3(0,.06f,1.15f);
            var lighting=new GameObject("21_Interior_Lighting");lighting.transform.SetParent(root.transform,false);
            // Six unshadowed soft fill lights avoid dozens of per-pixel shadow maps.
            foreach(float x in new[]{-2.4f,2.4f})foreach(float z in new[]{2f,5.5f,8.8f})
            {var go=new GameObject("CeilingFill");go.transform.SetParent(lighting.transform,false);go.transform.localPosition=new Vector3(x,2.95f,z);var l=go.AddComponent<Light>();l.type=LightType.Point;l.range=6;l.intensity=.85f;l.color=new Color(1,.93f,.82f);l.shadows=LightShadows.None;}
            var routes=ValidateRoutes(root);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            var cameraGo=new GameObject("Interior_Preview_Camera");var cam=cameraGo.AddComponent<Camera>();cameraGo.tag="MainCamera";
            cameraGo.transform.position=new Vector3(-.1f,1.95f,.3f);cameraGo.transform.LookAt(new Vector3(-.2f,1.28f,6.4f));cam.fieldOfView=70;cam.nearClipPlane=.04f;cam.farClipPlane=60;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.72f,.79f,.8f);cam.allowHDR=true;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.45f,.45f,.42f);RenderSettings.ambientIntensity=1;
            var sunGo=new GameObject("Storefront_Daylight");var sun=sunGo.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=.55f;sun.color=new Color(1,.91f,.77f);sun.shadows=LightShadows.Soft;sunGo.transform.rotation=Quaternion.Euler(48,-25,0);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene,ScenePath);
            // Save only assets authored here; other open work may contain unsaved assets.
            foreach(var mat in mats.Values)AssetDatabase.SaveAssetIfDirty(mat);
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>())AssetDatabase.SaveAssetIfDirty(filter.sharedMesh);
            string result=Newtonsoft.Json.JsonConvert.SerializeObject(new Report{prefab=PrefabPath,scene=ScenePath,meshParts=data.parts.Length,triangles=data.parts.Sum(p=>p.triangles.Length/3),colliders=data.colliders.Length,routes=routes,compilationFailed=EditorUtility.scriptCompilationFailed},Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText("../ArtSource/ConvenienceInterior/unity-validation.json",result);
            return result;
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(current.IsValid())SceneManager.SetActiveScene(current);}
    }

    [Serializable] public class Route {public string destination;public bool complete;public int samples;public float length;}
    [Serializable] public class Report {public string prefab,scene;public int meshParts,triangles,colliders;public Route[] routes;public bool compilationFailed;}
    static Route[] ValidateRoutes(GameObject root)
    {
        var colliders=root.GetComponentsInChildren<BoxCollider>();
        var probe=new GameObject("Temporary_Access_Probe");var capsule=probe.AddComponent<CapsuleCollider>();capsule.radius=.35f;capsule.height=1.8f;
        const float step=.12f;int nx=71,nz=79;bool[,] clear=new bool[nx,nz];
        Func<int,int,Vector3> point=(x,z)=>new Vector3(-4.2f+x*step,.96f,.25f+z*step);
        try
        {
            for(int x=0;x<nx;x++)for(int z=0;z<nz;z++)
            {
                var p=point(x,z);bool free=true;
                foreach(var box in colliders)
                {if(Physics.ComputePenetration(capsule,p,Quaternion.identity,box,box.transform.position,box.transform.rotation,out var dir,out var depth)&&depth>.002f){free=false;break;}}
                clear[x,z]=free;
            }
            Func<Vector3,int> cell=p=>Mathf.Clamp(Mathf.RoundToInt((p.x+4.2f)/step),0,nx-1)+nx*Mathf.Clamp(Mathf.RoundToInt((p.z-.25f)/step),0,nz-1);
            int start=cell(new Vector3(0,0,1.15f));var queue=new Queue<int>();var prev=Enumerable.Repeat(-1,nx*nz).ToArray();prev[start]=start;queue.Enqueue(start);
            while(queue.Count>0){int id=queue.Dequeue();int x=id%nx,z=id/nx;foreach(var d in new[]{new Vector2Int(1,0),new Vector2Int(-1,0),new Vector2Int(0,1),new Vector2Int(0,-1)}){int xx=x+d.x,zz=z+d.y;if(xx<0||xx>=nx||zz<0||zz>=nz||!clear[xx,zz])continue;int n=xx+zz*nx;if(prev[n]!=-1)continue;prev[n]=id;queue.Enqueue(n);}}
            var targets=new Dictionary<string,Vector3>{{"Checkout",new Vector3(-1.98f,0,3.8f)},{"Central aisle",new Vector3(.42f,0,6.1f)},{"Back refrigerators",new Vector3(2.4f,0,8.35f)},{"Right refrigerators",new Vector3(2.90f,0,6.6f)},{"Freezer",new Vector3(2.06f,0,2.7f)},{"Seating",new Vector3(1.7f,0,1.7f)}};
            var results=new List<Route>();
            foreach(var t in targets)
            {
                int end=cell(t.Value);bool ok=prev[end]>=0;int samples=0;
                if(ok){int c=end;while(c!=start){int next=prev[c];var a=point(c%nx,c/nx);var b=point(next%nx,next/nx);for(int s=0;s<4;s++){var p=Vector3.Lerp(a,b,s/4f);foreach(var box in colliders){if(Physics.ComputePenetration(capsule,p,Quaternion.identity,box,box.transform.position,box.transform.rotation,out var direction,out var depth)&&depth>.002f)ok=false;}}samples++;c=next;if(samples>nx*nz){ok=false;break;}}}
                results.Add(new Route{destination=t.Key,complete=ok,samples=samples,length=samples*step});
            }
            return results.ToArray();
        }
        finally{UnityEngine.Object.DestroyImmediate(probe);}
    }

    public static string Preview()
    {
        // Render a prefab in a dedicated preview scene: the user's dirty scene is untouched.
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene);
            var go=new GameObject("PreviewCamera");SceneManager.MoveGameObjectToScene(go,scene);var cam=go.AddComponent<Camera>();cam.scene=scene;
            go.transform.position=new Vector3(-.1f,1.95f,.3f);go.transform.LookAt(new Vector3(-.2f,1.28f,6.4f));cam.fieldOfView=70;cam.nearClipPlane=.04f;cam.farClipPlane=60;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.gray;
            var lightGo=new GameObject("PreviewDaylight");SceneManager.MoveGameObjectToScene(lightGo,scene);var l=lightGo.AddComponent<Light>();l.type=LightType.Directional;l.intensity=.55f;lightGo.transform.rotation=Quaternion.Euler(45,-25,0);
            var rt=new RenderTexture(1440,1080,24);cam.targetTexture=rt;cam.Render();var before=RenderTexture.active;RenderTexture.active=rt;
            var tex=new Texture2D(1440,1080,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1440,1080),0,0);tex.Apply();RenderTexture.active=before;
            const string path="../ArtSource/ConvenienceInterior/Previews/04_Unity.png";File.WriteAllBytes(path,tex.EncodeToPNG());cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
            return Path.GetFullPath(path);
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
