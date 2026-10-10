
if (EditorApplication.isPlaying) throw new Exception("Integrate in Edit Mode.");
string art = "Assets/Art/Items/Vehicles/CityBicycle";
string items = "Assets/Resources/Inventory/Vehicles/CityBicycle";
foreach (var folder in new[]{art+"/Materials",art+"/Prefabs",items}) System.IO.Directory.CreateDirectory(folder);
AssetDatabase.Refresh();
var importer = (ModelImporter)AssetImporter.GetAtPath(art+"/Models/CityBicycle.fbx");
importer.globalScale=1; importer.useFileScale=true; importer.importAnimation=false;
importer.importCameras=false; importer.importLights=false; importer.addCollider=false;
importer.isReadable=true; importer.SaveAndReimport();
string[] ids={"Mint","Cream","Brown","Blue","Coral","Charcoal"};
string[] labels={"민트","크림","브라운","블루","코랄","차콜"};
string[] colors={"#7FA28D","#E7DCC7","#916750","#5E7F9F","#DA8580","#53585B"};
string[] commonNames={"Bike_Rubber","Bike_Chrome","Bike_Leather","Bike_Wicker","Bike_DarkMetal","Bike_ReflectorRed","Bike_ReflectorAmber","Bike_Lamp"};
string[] commonColors={"#292D2E","#BFC4C3","#80573F","#B58A5A","#464B4D","#DD3934","#ECA43C","#ECECDA"};
var common = new System.Collections.Generic.Dictionary<string,Material>();
var shader=Shader.Find("Universal Render Pipeline/Lit");
if (!shader) throw new Exception("URP Lit shader missing");
for(int j=0;j<commonNames.Length;j++) {
    string path=art+"/Materials/"+commonNames[j]+".mat";
    var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
    if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
    ColorUtility.TryParseHtmlString(commonColors[j],out var color);mat.SetColor("_BaseColor",color);
    mat.SetFloat("_Metallic",j==1?.8f:j==4?.6f:0);mat.SetFloat("_Smoothness",j==1?.7f:j==0?.18f:.38f);
    common.Add(commonNames[j],mat);EditorUtility.SetDirty(mat);
}
var preview=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var report=new System.Collections.Generic.List<string>();
try {
    for(int i=0;i<ids.Length;i++) {
        string paintPath=art+"/Materials/Bike_Paint_"+ids[i]+".mat";
        var paint=AssetDatabase.LoadAssetAtPath<Material>(paintPath);
        if(!paint){paint=new Material(shader);AssetDatabase.CreateAsset(paint,paintPath);}
        ColorUtility.TryParseHtmlString(colors[i],out var color);paint.SetColor("_BaseColor",color);
        paint.SetFloat("_Metallic",.35f);paint.SetFloat("_Smoothness",.62f);EditorUtility.SetDirty(paint);
        var root=new GameObject("CityBicycle_"+ids[i]);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,preview);
        var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(art+"/Models/CityBicycle.fbx"),root.transform);
        model.name="BlenderModel";
        var nodes=model.GetComponentsInChildren<Transform>();
        var front=nodes.Single(t=>t.name=="WheelFront");var rear=nodes.Single(t=>t.name=="WheelRear");
        var forward=(front.position-rear.position).normalized;
        if(Mathf.Abs(forward.y)>.01f)throw new Exception("Unexpected bicycle axis: "+forward);
        model.transform.rotation=Quaternion.FromToRotation(forward,Vector3.forward)*model.transform.rotation;
        foreach(var renderer in model.GetComponentsInChildren<Renderer>()) {
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m.name.StartsWith("Bike_Paint")?paint:common[m.name]).ToArray();
        }
        var bounds=new Bounds(root.transform.position,Vector3.zero);
        foreach(var renderer in model.GetComponentsInChildren<Renderer>())bounds.Encapsulate(renderer.bounds);
        if(bounds.size.y<1 || bounds.size.y>1.5f || bounds.size.z<1.7f || bounds.size.z>2.2f)throw new Exception("Unexpected bicycle size: "+bounds.size);
        string prefabPath=art+"/Prefabs/CityBicycle_"+ids[i]+".prefab";
        var prefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        string iconPath=art+"/Icons/CityBicycle_"+ids[i]+".png";
        var ti=(TextureImporter)AssetImporter.GetAtPath(iconPath);
        ti.textureType=TextureImporterType.Sprite;ti.spriteImportMode=SpriteImportMode.Single;
        ti.alphaIsTransparency=true;ti.mipmapEnabled=false;ti.maxTextureSize=512;ti.SaveAndReimport();
        string itemPath=items+"/CityBicycle_"+ids[i]+".asset";
        var item=AssetDatabase.LoadAssetAtPath<ItemData>(itemPath);
        if(!item){item=ScriptableObject.CreateInstance<ItemData>();AssetDatabase.CreateAsset(item,itemPath);}
        item.itemId="vehicle:city_bicycle:"+i.ToString("00")+":"+ids[i].ToLowerInvariant();
        item.displayName="시티 자전거 · "+labels[i];item.category=ItemCategory.Vehicle;item.maxStack=1;
        item.vehiclePrefab=prefab;item.vehicleSpeed=8f;item.heldPrefab=prefab;
        item.heldLocalScale=Vector3.one*.20f;item.heldLocalEulerAngles=new Vector3(-18,60,-25);
        item.heldLocalPosition=-(Quaternion.Euler(item.heldLocalEulerAngles)*new Vector3(0,.58f,.02f))*.20f;
        item.icon=AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);EditorUtility.SetDirty(item);
        report.Add(item.displayName+" "+bounds.size);
    }
} finally {UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);}
var player=PrefabUtility.LoadPrefabContents("Assets/Resources/Player.prefab");
try {
    if(!player.GetComponent<PlayerVehicle>())player.AddComponent<PlayerVehicle>();
    PrefabUtility.SaveAsPrefabAsset(player,"Assets/Resources/Player.prefab");
} finally {PrefabUtility.UnloadPrefabContents(player);}
var outlineShader=Shader.Find("CompanyGame/VehicleHoverOutline");
if(!outlineShader)throw new Exception("Vehicle hover outline shader missing");
string outlinePath="Assets/Resources/Inventory/Vehicles/VehicleHoverOutline.mat";
var outline=AssetDatabase.LoadAssetAtPath<Material>(outlinePath);
if(!outline){outline=new Material(outlineShader);AssetDatabase.CreateAsset(outline,outlinePath);}
outline.SetFloat("_OutlineWidth",.008f);EditorUtility.SetDirty(outline);
AssetDatabase.SaveAssets();
return string.Join("\n",report);
