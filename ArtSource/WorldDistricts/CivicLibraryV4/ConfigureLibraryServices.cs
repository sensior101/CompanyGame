
if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new Exception("Stop Play Mode and finish compilation first.");
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.path != "Assets/Scenes/Interiors/CivicLibraryInterior.unity") throw new Exception("Open the existing library interior first.");
var book = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Resources/Inventory/Books/BlankBook.asset");
book.category = ItemCategory.Book; book.maxStack = 99; EditorUtility.SetDirty(book); AssetDatabase.SaveAssetIfDirty(book);

string art = "Assets/Art/Items/RetailPOS";
System.IO.Directory.CreateDirectory(art + "/Materials"); AssetDatabase.Refresh();
var importer = (ModelImporter)AssetImporter.GetAtPath(art + "/RetailPOS.fbx");
// The authored mesh coordinates are metres; its FBX file-unit tag is centimetres.
importer.useFileScale = false; importer.globalScale = 1f;
importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
importer.importCameras = false; importer.importLights = false; importer.SaveAndReimport();
var data = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("../ArtSource/Items/RetailPOS/Exports/RetailPOS.json"));
foreach (var item in data["materials"])
{
    string name = (string)item["name"];
    string path = art + "/Materials/" + name + ".mat";
    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
    bool screen = name.StartsWith("POS_Screen");
    if (!material) { material = new Material(Shader.Find(screen ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
    var values = item["color"];
    material.SetColor("_BaseColor", new Color((float)values[0], (float)values[1], (float)values[2], (float)values[3]));
    if (screen) { material.SetColor("_BaseColor", Color.white); material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(art + "/POS_BlackScreen.png")); }
    else { material.SetFloat("_Metallic", (float)item["metallic"]); material.SetFloat("_Smoothness", 1f - (float)item["roughness"]); }
    EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
}
importer.SaveAndReimport();
var all = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
var desk = all.Single(x => x.name == "DESK_Checkout_Top").GetComponent<Renderer>();
var counter = all.Single(x => x.name == "CAFE_Main_Worktop").GetComponent<Renderer>();
foreach (var old in all.Where(x => x.name == "CAFE_POS_Screen" || x.name == "CAFE_POS_Stand"))
{ old.gameObject.SetActive(false); PrefabUtility.RecordPrefabInstancePropertyModifications(old.gameObject); }
var staffRoot = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "LibraryStaff");
if (!staffRoot) staffRoot = new GameObject("LibraryStaff");
var pos = all.FirstOrDefault(x => x.name == "Cafe_RetailPOS");
if (!pos)
{
    var obj = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(art + "/RetailPOS.fbx"), scene);
    obj.name = "Cafe_RetailPOS"; pos = obj.transform;
}
pos.SetParent(staffRoot.transform, false);
pos.SetPositionAndRotation(new Vector3(-8.85f, counter.bounds.max.y, -6.16f), Quaternion.Euler(0, 180, 0));
pos.localScale = Vector3.one;
var posBounds = pos.GetComponentsInChildren<Renderer>().Select(x=>x.bounds).Aggregate((a,b)=> {a.Encapsulate(b);return a;});
pos.position += Vector3.up * (counter.bounds.max.y + .004f - posBounds.min.y);

System.IO.Directory.CreateDirectory("Assets/Resources/Inventory/LibraryCafe"); AssetDatabase.Refresh();
ItemData CafeItem(string id, string title, string icon)
{
    string path = "Assets/Resources/Inventory/LibraryCafe/" + id + ".asset";
    var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
    if (!item) { item = ScriptableObject.CreateInstance<ItemData>(); AssetDatabase.CreateAsset(item, path); }
    item.itemId = id; item.displayName = title; item.category = ItemCategory.Food; item.maxStack = 99;
    item.icon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Items/Food/" + icon + ".png");
    EditorUtility.SetDirty(item); AssetDatabase.SaveAssetIfDirty(item); return item;
}
var coffee = CafeItem("item_coffee", "커피", "Food_Coffee");
var tea = CafeItem("item_tea", "차", "Food_TeaCup");
var cookie = CafeItem("item_cookie", "쿠키", "Food_Cookie");
var offers = new[] {
    new TradeOffer { give=new TradeItem {cash=3000,count=1}, get=new TradeItem{item=coffee,count=1}},
    new TradeOffer { give=new TradeItem {cash=2500,count=1}, get=new TradeItem{item=tea,count=1}},
    new TradeOffer { give=new TradeItem {cash=1500,count=1}, get=new TradeItem{item=cookie,count=1}}
};
GameObject Staff(string name, bool female, Vector3 position, LibraryDesk.Service service)
{
    var existing = staffRoot.transform.Find(name);
    GameObject go = existing ? existing.gameObject : new GameObject(name);
    go.transform.SetParent(staffRoot.transform, true);
    // Keep staff positions/facing edited in the scene when refreshing services.
    if (!existing) go.transform.SetPositionAndRotation(position, Quaternion.Euler(0,180,0));
    if (go.transform.childCount == 0)
    {
        string path = "Assets/Art/Daldongne/Players/" + (female ? "FemaleVisual" : "MaleVisual") + ".prefab";
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
        visual.transform.SetParent(go.transform, false);
        // Use the existing character rig, with its standing rest pose and no player controllers.
    }
    var collider = go.GetComponent<CapsuleCollider>(); if (!collider) collider = go.AddComponent<CapsuleCollider>();
    collider.center = new Vector3(0,.87f,0); collider.height=1.74f; collider.radius=.24f;
    var dialogue = go.GetComponent<DialogueData>(); if (!dialogue) dialogue = go.AddComponent<DialogueData>();
    dialogue.npcId = name; dialogue.radius = 2.45f;
    dialogue.head = go.GetComponentsInChildren<Transform>().FirstOrDefault(x=>x.name=="Head");
    dialogue.line = service == LibraryDesk.Service.Library ? "어서오세요. 무엇을 도와드릴까요?" :
        service == LibraryDesk.Service.CafeCashier ? "주문 도와드리겠습니다" : "커피 향이랑 책 냄새, 은근 잘 어울리지 않나요?";
    dialogue.options = service == LibraryDesk.Service.Library ? LibraryDesk.CreateOptions() : Array.Empty<DialogueOption>();
    var route = go.GetComponent<LibraryDesk>();
    if(service==LibraryDesk.Service.Library)
    { if(!route)route=go.AddComponent<LibraryDesk>();route.service=service; }
    else if(route)UnityEngine.Object.DestroyImmediate(route);
    if(service==LibraryDesk.Service.CafeCashier)
    {
        var trader=go.GetComponent<NpcTrader>();if(!trader)trader=go.AddComponent<NpcTrader>();
        trader.offers=offers;trader.openAfterDialogue=true;trader.radius=dialogue.radius;trader.heightTolerance=2f;
    }
    return go;
}
float deskBack = desk.bounds.max.z + .43f;
Staff("Library_Desk_Female", true, new Vector3(4.3f,.02f,deskBack), LibraryDesk.Service.Library);
Staff("Library_Desk_Male", false, new Vector3(5.5f,.02f,deskBack), LibraryDesk.Service.Library);
Staff("Library_Cafe_Cashier", true, new Vector3(-8.85f,.02f,-5.1f), LibraryDesk.Service.CafeCashier);
Staff("Library_Cafe_Conversation", true, new Vector3(-11.4f,.02f,-5.1f), LibraryDesk.Service.CafeConversation);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return new { scene=scene.path, staff=staffRoot.GetComponentsInChildren<DialogueData>().Select(x=>new{x.name,position=x.transform.position.ToString(),trader=(bool)x.GetComponent<NpcTrader>()}).ToArray(), pos=pos.position.ToString(), book=book.IsBook };
