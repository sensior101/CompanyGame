using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;

public static class ImportFoodPhotoSprites
{
    const string Folder="Assets/Art/Items/ConvenienceFood/";
    // Coordinates are traced on the user's unchanged 1280x720 JPEG, top-left origin.
    static readonly Dictionary<string,int[]> Outlines=new Dictionary<string,int[]>{
        {"TriangleGimbap",new[]{157,43,167,44,184,78,205,121,229,172,251,214,281,263,301,291,306,309,271,317,217,330,144,344,63,345,13,321,10,309,37,269,64,215,93,165,123,111,144,69}},
        {"CupRamen",new[]{369,51,399,48,441,53,482,65,517,84,527,100,518,116,505,131,499,171,487,224,474,275,462,311,446,327,422,336,393,340,365,338,341,330,328,317,319,281,308,231,297,177,288,139,289,121,278,112,273,99,278,87,296,74,325,62}},
        {"LunchBox",new[]{611,78,633,78,678,90,719,103,772,118,815,134,836,147,842,161,837,194,815,265,794,326,782,342,764,349,732,340,681,326,625,308,570,290,516,271,500,259,497,245,508,216,531,179,558,134,580,102,595,84}},
        {"Water",new[]{908,17,924,13,951,14,969,20,975,31,972,51,966,61,971,78,987,93,1000,115,1007,143,1007,183,1004,217,1007,248,1001,292,989,329,974,350,954,363,932,363,909,355,890,342,877,321,870,294,867,259,868,230,865,190,862,162,865,132,876,104,889,84,904,71,908,57,902,43,904,25}},
        {"EnergyBar",new[]{1115,46,1154,56,1210,69,1271,87,1267,110,1251,148,1235,198,1227,251,1213,310,1207,353,1206,374,1174,365,1125,350,1078,335,1042,324,1049,301,1064,268,1079,213,1092,157,1100,112,1107,73}},
        {"EnergyDrink",new[]{93,363,122,359,159,361,184,369,195,380,197,415,199,472,200,531,202,595,204,649,201,674,190,689,167,697,126,698,98,689,79,677,74,660,74,607,72,548,72,488,73,431,73,395,78,376}},
        {"Cola",new[]{326,351,347,349,369,353,377,363,376,385,384,410,398,434,409,454,415,480,418,526,413,572,418,615,420,655,413,680,402,695,385,701,370,696,351,701,329,698,311,690,304,676,299,654,299,623,306,579,303,543,301,501,304,466,311,444,323,422,331,393,320,386,319,369}},
        {"Chips",new[]{517,353,555,364,610,374,677,382,734,388,741,396,732,412,720,455,710,507,703,561,695,617,693,663,687,700,648,690,584,678,523,668,475,661,442,654,451,633,466,588,478,539,490,491,503,442,510,402,511,368}},
        {"Chocolate",new[]{856,404,887,410,918,426,952,442,993,454,1014,466,1001,484,984,503,975,523,957,544,942,568,931,593,902,620,878,643,854,668,827,689,810,695,788,683,765,670,746,650,722,642,711,631,716,610,730,584,738,563,754,541,778,526,795,505,815,478,832,455,843,431}},
        {"Jelly",new[]{1020,398,1067,398,1124,399,1184,394,1240,389,1241,417,1237,454,1244,492,1249,537,1253,579,1262,624,1271,670,1228,677,1162,685,1093,689,1024,694,1023,665,1028,621,1031,578,1030,535,1028,491,1022,450,1015,419}}
    };
    public static string Import()
    {
        if(EditorApplication.isPlaying)throw new Exception("Exit Play Mode first.");
        string path=Folder+"StoreFoods.jpg";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;
        importer.maxTextureSize=2048;importer.npotScale=TextureImporterNPOTScale.None;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;importer.mipmapEnabled=false;
        var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.Tight;importer.SetTextureSettings(settings);importer.SaveAndReimport();
        var factory=new SpriteDataProviderFactories();factory.Init();var provider=factory.GetSpriteEditorDataProviderFromObject(importer);
        if(provider==null)throw new Exception("No sprite provider.");provider.InitSpriteEditorDataProvider();
        var edit=provider.GetDataProvider<ISpriteFrameEditCapability>();
        if(edit==null || !edit.GetEditCapability().HasCapability(EEditCapability.CreateAndDeleteSprite))throw new Exception("Slicing is unsupported; no sprite metadata changed.");
        var outlineProvider=provider.GetDataProvider<ISpriteOutlineDataProvider>();
        var ids=provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if(outlineProvider==null || ids==null)throw new Exception("Outline/name providers required.");
        var old=provider.GetSpriteRects().ToDictionary(r=>r.name);var rects=new List<SpriteRect>();
        foreach(var pair in Outlines)
        {
            var points=Enumerable.Range(0,pair.Value.Length/2).Select(i=>new Vector2(pair.Value[i*2],720-pair.Value[i*2+1])).ToArray();
            float minX=points.Min(v=>v.x),minY=points.Min(v=>v.y),maxX=points.Max(v=>v.x),maxY=points.Max(v=>v.y);
            var rect=new Rect(minX,minY,maxX-minX,maxY-minY);
            var sr=new SpriteRect{name=pair.Key,rect=rect,pivot=new Vector2(.5f,.5f),alignment=SpriteAlignment.Center,spriteID=old.ContainsKey(pair.Key)?old[pair.Key].spriteID:GUID.Generate()};rects.Add(sr);
        }
        provider.SetSpriteRects(rects.ToArray());ids.SetNameFileIdPairs(rects.Select(r=>new SpriteNameFileIdPair(r.name,r.spriteID)));
        foreach(var r in rects)
        {
            var coords=Outlines[r.name];var points=Enumerable.Range(0,coords.Length/2).Select(i=>new Vector2(coords[i*2],720-coords[i*2+1])-r.rect.center).ToArray();
            outlineProvider.SetOutlines(r.spriteID,new List<Vector2[]>{points});outlineProvider.SetTessellationDetail(r.spriteID,1);
        }
        provider.Apply();importer.SaveAndReimport();
        var sprites=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s=>s.name);
        foreach(var pair in sprites)
        {
            var item=AssetDatabase.LoadAssetAtPath<ItemData>(Folder+pair.Key+".asset");if(!item)throw new Exception("Missing item "+pair.Key);
            item.icon=pair.Value;item.heldPrefab=BuildModel(pair.Key,pair.Value);
            item.heldLocalPosition=new Vector3(0,.09f,0);item.heldLocalEulerAngles=Vector3.zero;item.heldLocalScale=Vector3.one;
            EditorUtility.SetDirty(item);AssetDatabase.SaveAssetIfDirty(item);
        }
        return string.Join("\n",sprites.Select(s=>s.Key+": "+s.Value.vertices.Length+" mesh vertices, original photo sprite"));
    }
    static GameObject BuildModel(string name,Sprite sprite)
    {
        string folder=Folder+"PhotoModels";if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder(Folder.TrimEnd('/'),"PhotoModels");
        var face=Material(folder+"/PhotoFace.mat",sprite.texture,Color.white);
        var edge=Material(folder+"/PhotoEdge.mat",sprite.texture,new Color(.65f,.65f,.65f));
        var source=sprite.vertices;var uvSource=sprite.uv;var tri=sprite.triangles;int n=source.Length;
        float scale=.3f/Mathf.Max(sprite.bounds.size.x,sprite.bounds.size.y),halfDepth=.027f;
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var fronts=new List<int>();var sides=new List<int>();
        for(int layer=0;layer<2;layer++)for(int i=0;i<n;i++){vertices.Add(new Vector3(source[i].x*scale,source[i].y*scale,layer==0?-halfDepth:halfDepth));uv.Add(uvSource[i]);}
        var edges=new Dictionary<(int,int),int>();
        for(int i=0;i<tri.Length;i+=3)
        {
            int a=tri[i],b=tri[i+1],c=tri[i+2];fronts.AddRange(new[]{a,b,c,c+n,b+n,a+n});
            foreach(var e in new[]{(a,b),(b,c),(c,a)}){var key=(Math.Min(e.Item1,e.Item2),Math.Max(e.Item1,e.Item2));edges[key]=edges.TryGetValue(key,out int count)?count+1:1;}
        }
        foreach(var pair in edges.Where(e=>e.Value==1))
        {
            int a=pair.Key.Item1,b=pair.Key.Item2,start=vertices.Count;
            foreach(int i in new[]{a,b,b+n,a+n}){vertices.Add(vertices[i]);uv.Add(uv[i]);}
            sides.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        string meshPath=folder+"/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if(!mesh){mesh=new Mesh{name=name+"_PhotoExtrusion"};AssetDatabase.CreateAsset(mesh,meshPath);}else mesh.Clear();
        mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=2;mesh.SetTriangles(fronts,0);mesh.SetTriangles(sides,1);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
        var go=new GameObject(name+"_PhotoModel",typeof(MeshFilter),typeof(MeshRenderer));
        try{go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterials=new[]{face,edge};return PrefabUtility.SaveAsPrefabAsset(go,folder+"/"+name+".prefab");}
        finally{UnityEngine.Object.DestroyImmediate(go);}
    }
    static Material Material(string path,Texture texture,Color tint)
    {
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,path);}
        material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",tint);material.SetFloat("_Cull",0);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);return material;
    }
}
