using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class BuildFurnitureLibrary
{
    const string Root="Assets/Art/Items/Furniture/Library";
    [Serializable] public class MaterialRow { public string name,albedo,normal,metallicMap,roughnessMap,emission;public float[] color;public float metallic,roughness,alpha=1; }
    [Serializable] public class Row { public string id,label,category,modelPath,source,note,functions,storage,placement,prefabPath,preview;public bool reused;public int triangles;public MaterialRow[] materialSpecs; }
    [Serializable] public class Catalog {public Row[] items;}
    [Serializable] public class Result {public int count;public List<string> errors=new List<string>();public Row[] items;}
    public static void Run()
    {
        if(!Application.dataPath.Replace('\\','/').Contains("/ArtSource/FurnitureLibrary/Validation/Assets"))throw new Exception("Isolated validation project only");
        Directory.CreateDirectory("FurnitureLibraryQA/Previews");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var rows=JsonUtility.FromJson<Catalog>(File.ReadAllText(Root+"/Catalog.json")).items;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.7f,.7f,.7f);RenderSettings.skybox=null;
        var light=new GameObject("Preview Light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(45,-35,0);
        var camera=new GameObject("Preview Camera").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.18f,.2f);camera.nearClipPlane=.01f;camera.farClipPlane=200;
        var result=new Result{items=rows};
        foreach(var row in rows)
        {
            GameObject go=null;
            try {
                string folder=Root+"/Prefabs/"+row.category;Directory.CreateDirectory(folder);AssetDatabase.Refresh();
                var importer=AssetImporter.GetAtPath(row.modelPath) as ModelImporter;if(!importer)throw new Exception("Model missing");
                importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.addCollider=false;importer.isReadable=false;
                if(!row.reused){
                    importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
                    string mf=Path.GetDirectoryName(row.modelPath).Replace('\\','/')+"/Materials";Directory.CreateDirectory(mf);AssetDatabase.Refresh();
                    foreach(var spec in row.materialSpecs){
                        string mp=mf+"/"+spec.name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(mp);
                        if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,mp);}
                        Color c=new Color(spec.color[0],spec.color[1],spec.color[2],spec.alpha).gamma;c.a=spec.alpha;
                        m.SetColor("_BaseColor",string.IsNullOrEmpty(spec.albedo)?c:Color.white);m.SetFloat("_Metallic",spec.metallic);m.SetFloat("_Smoothness",1-spec.roughness);
                        var albedo=Tex(spec.albedo,false,true);if(albedo)m.SetTexture("_BaseMap",albedo);
                        var normal=Tex(spec.normal,true,false);if(normal){m.SetTexture("_BumpMap",normal);m.EnableKeyword("_NORMALMAP");}
                        if(!string.IsNullOrEmpty(spec.roughnessMap)||!string.IsNullOrEmpty(spec.metallicMap)){
                            var rough=Tex(spec.roughnessMap,false,false,true);var metal=Tex(spec.metallicMap,false,false,true);
                            var map=new Texture2D(512,512,TextureFormat.RGBA32,false,true);var colors=new Color32[512*512];
                            for(int y=0;y<512;y++)for(int x=0;x<512;x++){float u=(x+.5f)/512,v=(y+.5f)/512;byte met=(byte)(255*(metal?metal.GetPixelBilinear(u,v).r:spec.metallic));byte smooth=(byte)(255*(1-(rough?rough.GetPixelBilinear(u,v).r:spec.roughness)));colors[y*512+x]=new Color32(met,met,met,smooth);}
                            map.SetPixels32(colors);map.Apply();string packed=mf+"/"+spec.name+"_MetallicSmoothness.png";File.WriteAllBytes(packed,map.EncodeToPNG());Object.DestroyImmediate(map);AssetDatabase.ImportAsset(packed);
                            m.SetTexture("_MetallicGlossMap",Tex(packed,false,false));m.SetFloat("_Smoothness",1);m.EnableKeyword("_METALLICSPECGLOSSMAP");
                            Tex(spec.roughnessMap,false,false,false);Tex(spec.metallicMap,false,false,false);
                        }
                        if(spec.alpha<.99f){m.SetFloat("_Surface",1);m.SetFloat("_SrcBlend",5);m.SetFloat("_DstBlend",10);m.SetFloat("_ZWrite",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");m.renderQueue=3000;}
                        var emission=Tex(spec.emission,false,true);if(emission){m.SetTexture("_EmissionMap",emission);m.SetColor("_EmissionColor",Color.white);m.EnableKeyword("_EMISSION");}
                        EditorUtility.SetDirty(m);importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),spec.name),m);
                    }
                }
                importer.SaveAndReimport();
                go=new GameObject(row.id);var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(row.modelPath),go.transform);
                var rs=go.GetComponentsInChildren<Renderer>();if(rs.Length==0)throw new Exception("No renderers");
                var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                visual.transform.position-=new Vector3(b.center.x,b.min.y,b.center.z);
                b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                if(b.size.y<.005f||b.size.magnitude>30)throw new Exception("Unexpected model size "+b.size);
                var box=go.AddComponent<BoxCollider>();box.center=b.center;box.size=b.size;
                row.prefabPath=folder+"/"+row.id+".prefab";
                var prefab=PrefabUtility.SaveAsPrefabAsset(go,row.prefabPath);AssetDatabase.SetLabels(prefab,new[]{"Furniture",row.category.Replace('/','-'),row.label,row.reused?"WhiteWood":"Downloaded"});
                if(rs.Any(r=>r.sharedMaterials.Any(m=>!m||m.shader.name!="Universal Render Pipeline/Lit")))throw new Exception("Invalid URP material");
                camera.transform.position=b.center+new Vector3(1,.7f,1.25f).normalized*(b.size.magnitude*2+1);camera.transform.LookAt(b.center);camera.orthographicSize=Mathf.Max(.1f,b.size.magnitude*.58f);
                var rt=RenderTexture.GetTemporary(384,384,24);var tex=new Texture2D(384,384,TextureFormat.RGB24,false);var prev=RenderTexture.active;
                camera.targetTexture=rt;camera.Render();camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,384,384),0,0);tex.Apply();
                row.preview="Previews/"+row.id+".png";File.WriteAllBytes("FurnitureLibraryQA/"+row.preview,tex.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=prev;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(tex);
                result.count++;Debug.Log("FURNITURE_OK "+row.id);
            }catch(Exception e){result.errors.Add(row.id+": "+e.Message);Debug.LogError(e);}
            finally{if(go)Object.DestroyImmediate(go);}
        }
        AssetDatabase.SaveAssets();File.WriteAllText("FurnitureLibraryQA/Result.json",JsonUtility.ToJson(result,true));
        if(result.errors.Count>0)throw new Exception("Furniture import failures: "+result.errors.Count);
        Debug.Log("FURNITURE_LIBRARY_PASS "+result.count);
    }
    static Texture2D Tex(string path,bool normal,bool srgb,bool readable=false){
        if(string.IsNullOrEmpty(path))return null;
        var imp=AssetImporter.GetAtPath(path) as TextureImporter;if(!imp)throw new Exception("Missing texture "+path);
        var type=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
        if(imp.textureType!=type||imp.sRGBTexture!=srgb||imp.isReadable!=readable||imp.maxTextureSize!=2048){imp.textureType=type;imp.sRGBTexture=srgb;imp.isReadable=readable;imp.maxTextureSize=2048;imp.SaveAndReimport();}
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
