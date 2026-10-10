using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WhiteWoodFurnitureImporter
{
    [Serializable] public class ThemeCatalog { public ThemeEntry[] items; public PaletteEntry[] palette; }
    [Serializable] public class ThemeEntry : FurnitureCollectionImporter.Entry { public string themeModelPath; public string[] materials; }
    [Serializable] public class PaletteEntry { public string name, hex; public float roughness, metallic; }
    const string ThemeRoot = "Assets/Gameplay/Item/Furniture/Themes/WhiteWood";
    const string ArtRoot = "Assets/Art/Items/Furniture/Themes/WhiteWood";
    public static ThemeCatalog ReadCatalog() => JsonUtility.FromJson<ThemeCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../ArtSource/FurnitureCollections/WhiteWood/catalog.json"))));
    static string Category(ThemeEntry row) => row.functions.Contains("Sleep") ? "Sleep" : row.functions == "Storage" ? "Storage/" + row.storage : row.functions;
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash=path.LastIndexOf('/'); Folder(path.Substring(0,slash)); AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
    }
    static Texture2D Detail(string name, bool wood)
    {
        string path=ArtRoot+"/Textures/"+name+".png";
        if (!File.Exists(path))
        {
            var texture=new Texture2D(256,256,TextureFormat.RGB24,false);
            var pixels=new Color[256*256];
            for(int y=0;y<256;y++)for(int x=0;x<256;x++)
            {
                float v=wood ? .9f+.045f*Mathf.Sin(y*.5f+Mathf.Sin(x*.02454369f)*2f)+.02f*Mathf.Sin(y*1.718058f+x*.02454369f) :
                    .92f+((x%3==0||y%3==0)?-.045f:.02f)+.015f*Mathf.Sin(x*13.7f+y*7.3f);
                pixels[y*256+x]=new Color(v,v,v,1);
            }
            texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        }
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.wrapMode=TextureWrapMode.Repeat;importer.sRGBTexture=true;importer.mipmapEnabled=true;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    [MenuItem("CompanyGame/Furniture/Import White and Wood theme")]
    public static void Import()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode first.");
        Folder(ArtRoot+"/Materials");Folder(ArtRoot+"/Textures");Folder(ThemeRoot);
        var catalog=ReadCatalog();var materials=new Dictionary<string,Material>();
        var wood=Detail("OakGrain",true);var linen=Detail("LinenWeave",false);
        foreach(var swatch in catalog.palette)
        {
            string path=ArtRoot+"/Materials/"+swatch.name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            ColorUtility.TryParseHtmlString("#"+swatch.hex,out Color color);material.SetColor("_BaseColor",color);
            material.SetFloat("_Smoothness",1f-swatch.roughness);material.SetFloat("_Metallic",swatch.metallic);
            material.SetTexture("_BaseMap",swatch.name.Contains("Oak")?wood:swatch.name=="WW_Linen"||swatch.name=="WW_Olive"?linen:null);
            if(swatch.name=="WW_Glow"||swatch.name=="WW_BulbWhite")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor",swatch.name=="WW_BulbWhite"?Color.white*2f:color*.35f);
            }
            else { material.DisableKeyword("_EMISSION"); material.SetColor("_EmissionColor",Color.black); }
            material.enableInstancing=true;EditorUtility.SetDirty(material);materials.Add(swatch.name,material);
        }
        var preview=EditorSceneManager.NewPreviewScene();int count=0;
        try
        {
            foreach(var row in catalog.items)
            {
                string folder=ThemeRoot+"/"+Category(row)+"/"+row.pack;Folder(folder);
                AssetDatabase.ImportAsset(row.themeModelPath,ImportAssetOptions.ForceSynchronousImport);
                var importer=(ModelImporter)AssetImporter.GetAtPath(row.themeModelPath);
                bool deskLamp=row.pack=="HomeOffice"&&row.key=="DeskLamp";
                importer.importAnimation=false;importer.isReadable=deskLamp;importer.addCollider=false;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
                foreach(var mat in materials)importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),mat.Key),mat.Value);
                importer.SaveAndReimport();
                string sourcePath="Assets/Gameplay/Item/Furniture/"+Category(row)+"/"+row.pack+"/"+row.key+".asset";
                var original=AssetDatabase.LoadAssetAtPath<ItemData>(sourcePath);
                if(!original)throw new InvalidOperationException("Missing neutral item: "+sourcePath);
                string itemPath=folder+"/"+row.key+".asset";var item=AssetDatabase.LoadAssetAtPath<ItemData>(itemPath);
                if(!item){item=UnityEngine.Object.Instantiate(original);AssetDatabase.CreateAsset(item,itemPath);}
                item.itemId="furniture:whitewood:"+row.pack.ToLowerInvariant()+":"+row.key.ToLowerInvariant();
                item.displayName=row.label+" (화이트앤우드)";item.furnitureTheme=FurnitureTheme.WhiteWood;
                var root=new GameObject(item.displayName);SceneManager.MoveGameObjectToScene(root,preview);
                try
                {
                    var model=AssetDatabase.LoadAssetAtPath<GameObject>(row.themeModelPath);
                    var visual=(GameObject)PrefabUtility.InstantiatePrefab(model,root.transform);
                    var renderers=visual.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
                    foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                    visual.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                    bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                    var box=root.AddComponent<BoxCollider>();box.center=bounds.center;box.size=bounds.size;
                    var world=root.AddComponent<WorldObject>();world.objectType=WorldObjectType.PlaceableFurniture;world.furnitureItem=item;
                    world.functions=item.furnitureFunctions;
                    if(deskLamp)ConfigureDeskLamp(root,world);
                    item.furniturePrefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/"+row.key+".prefab");EditorUtility.SetDirty(item);
                    AssetDatabase.SetLabels(item,new[]{"Furniture","WhiteWood",row.pack});AssetDatabase.SetLabels(item.furniturePrefab,new[]{"Furniture","WhiteWood",row.pack});count++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    if(deskLamp){importer.isReadable=false;importer.SaveAndReimport();}
                }
            }
        }
        finally{EditorSceneManager.ClosePreviewScene(preview);AssetDatabase.SaveAssets();}
        Debug.Log("WhiteWood theme imported: "+count);
    }
    static void ConfigureDeskLamp(GameObject root,WorldObject world)
    {
        var targets=new List<FurnitureLight.EmissionTarget>();
        var bulbVertices=new List<Vector3>();
        Vector3 centroid=Vector3.zero,normal=Vector3.zero;float totalArea=0f;
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            var mesh=filter.sharedMesh;var renderer=filter.GetComponent<Renderer>();
            if(!mesh||!renderer)continue;
            var materials=renderer.sharedMaterials;var vertices=mesh.vertices;
            for(int slot=0;slot<materials.Length&&slot<mesh.subMeshCount;slot++)
            {
                if(!materials[slot]||materials[slot].name!="WW_BulbWhite")continue;
                targets.Add(new FurnitureLight.EmissionTarget{renderer=renderer,materialIndex=slot});
                var indices=mesh.GetTriangles(slot);
                for(int i=0;i+2<indices.Length;i+=3)
                {
                    Vector3 a=filter.transform.TransformPoint(vertices[indices[i]]),b=filter.transform.TransformPoint(vertices[indices[i+1]]),c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                    bulbVertices.Add(a);bulbVertices.Add(b);bulbVertices.Add(c);
                    Vector3 cross=Vector3.Cross(b-a,c-a);float area=cross.magnitude*.5f;
                    centroid+=(a+b+c)*(area/3f);normal+=cross*.5f;totalArea+=area;
                }
            }
        }
        if(targets.Count==0||totalArea<=.0000001f)throw new InvalidOperationException("DeskLamp must contain non-empty WW_BulbWhite faces.");
        centroid/=totalArea;
        Vector3 outward=normal.sqrMagnitude>1e-10f?normal.normalized:Vector3.down;
        // Keep the shadow-casting light outside the opaque bulb surface.
        float clearance=bulbVertices.Max(v=>Vector3.Dot(v-centroid,outward))+.012f;
        var bulb=new GameObject("Bulb Light");bulb.transform.SetParent(root.transform,false);bulb.transform.position=centroid+outward*clearance;
        var point=bulb.AddComponent<Light>();point.type=LightType.Point;point.color=Color.white;point.intensity=.15f;point.range=1.8f;
        point.shadows=LightShadows.Soft;point.shadowStrength=.65f;point.shadowBias=.02f;point.shadowNormalBias=.02f;
        var lamp=root.AddComponent<FurnitureLight>();lamp.owner=world;lamp.interactionPoint=bulb.transform;lamp.lights=new[]{point};lamp.emissionTargets=targets.ToArray();lamp.bulbEmission=Color.white*2f;lamp.SetOn(true);
    }
    public static string Validate()
    {
        var failures=new List<string>();int count=0;
        foreach(string guid in AssetDatabase.FindAssets("t:ItemData",new[]{ThemeRoot}))
        {
            var item=AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));count++;
            if(!item.furniturePrefab||item.furnitureTheme!=FurnitureTheme.WhiteWood){failures.Add(item.name+": missing theme/prefab");continue;}
            var world=item.furniturePrefab.GetComponent<WorldObject>();if(!world||world.furnitureItem!=item)failures.Add(item.name+": item link");
            if(item.itemId=="furniture:whitewood:homeoffice:desklamp")
            {
                var lamp=item.furniturePrefab.GetComponent<FurnitureLight>();
                if(!lamp||lamp.owner!=world||!world||!world.HasFunction(FurnitureFunction.Lighting)||!lamp.interactionPoint)
                    failures.Add(item.name+": missing lamp interaction binding");
                else
                {
                    if(lamp.lights.Length!=1||!lamp.lights[0]||lamp.lights[0].type!=LightType.Point||lamp.lights[0].color!=Color.white||!lamp.lights[0].transform.IsChildOf(item.furniturePrefab.transform))
                        failures.Add(item.name+": invalid white bulb light");
                    if(lamp.emissionTargets.Length==0)failures.Add(item.name+": missing bulb emission target");
                    foreach(var target in lamp.emissionTargets)
                    {
                        if(!target.renderer||target.materialIndex<0||target.materialIndex>=target.renderer.sharedMaterials.Length)
                        {failures.Add(item.name+": invalid bulb material slot");continue;}
                        var material=target.renderer.sharedMaterials[target.materialIndex];
                        if(!material||material.name!="WW_BulbWhite"||!material.IsKeywordEnabled("_EMISSION")||material.GetColor("_EmissionColor").r<=0f)
                            failures.Add(item.name+": bulb slot must use emissive WW_BulbWhite");
                    }
                }
            }
            foreach(var filter in item.furniturePrefab.GetComponentsInChildren<MeshFilter>())
            {
                if(!filter.sharedMesh||!filter.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))failures.Add(item.name+": missing mesh/UV");
                var renderer=filter.GetComponent<Renderer>();if(renderer.sharedMaterials.Length!=filter.sharedMesh.subMeshCount)failures.Add(item.name+": slot mismatch");
            }
            foreach(var renderer in item.furniturePrefab.GetComponentsInChildren<Renderer>())foreach(var mat in renderer.sharedMaterials)
                if(!mat||!AssetDatabase.GetAssetPath(mat).StartsWith(ArtRoot+"/Materials/")||mat.shader.name!="Universal Render Pipeline/Lit")failures.Add(item.name+": invalid theme material");
        }
        string report="WhiteWood items/prefabs: "+count+"; failures: "+failures.Count+"\n"+string.Join("\n",failures);
        File.WriteAllText(Path.Combine(Application.dataPath,"../Docs/FurnitureCollections/WhiteWood/UnityValidation.txt"),report);
        if(count!=61||failures.Count>0)throw new InvalidOperationException(report);
        return report;
    }
}
