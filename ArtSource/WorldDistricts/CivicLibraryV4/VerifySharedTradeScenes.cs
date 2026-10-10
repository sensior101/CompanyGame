if(EditorApplication.isPlaying)throw new Exception("Run in Edit Mode");
var original=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var scenes=new System.Collections.Generic.List<UnityEngine.SceneManagement.Scene>();
var report=new System.Collections.Generic.List<string>();
void Check(bool ok,string label){report.Add((ok?"PASS ":"FAIL ")+label);if(!ok)throw new Exception(label);}
try
{
    var font=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Art/font/NotoSansKR-Regular SDF.asset");
    foreach(bool rental in new[]{false,true})
    {
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);scenes.Add(scene);
        var inventory=new InventoryState();
        var book=new BookInstanceData{title="다른 씬의 책",authorPlayerId="scene-author",authorName="공통 작가",isPublished=true};
        var offers=Enumerable.Range(0,19).Select(i=>new TradeOffer{give=new TradeItem{cash=0,count=1},get=new TradeItem{item=LibraryCatalog.BookItem,count=1,bookTemplate=book},isRented=()=>rental}).ToArray();
        TradeSession session=rental?new RentalTradeSession(inventory,offers):new TradeSession(inventory,offers);
        var ui=TradeWindow.Create(session,font,()=>{});
        Check(ui.gameObject.scene==scene,"Shared window created in independent "+(rental?"rental":"shop")+" scene");
        Check(ui.OfferPageCount==2,"19 offers create two pages in this scene");ui.ChangeOfferPage(1);
        var payment=ui.GetComponentsInChildren<TradePointer>().First(x=>x.name=="Payment_0");
        Check(payment.index==18,"Second page resolves real offer index across scenes");
        ui.ShowTooltip(payment,Vector2.zero);
        Check(ui.GetComponentsInChildren<TMPro.TMP_Text>().Any(x=>x.name=="Name"&&x.text=="0원"),"Shared zero-price tooltip works across scenes");
        var bookOutput=ui.GetComponentsInChildren<TradePointer>().First(x=>x.name=="Output_0");ui.ShowTooltip(bookOutput,Vector2.zero);
        Check(ui.GetComponentsInChildren<TMPro.TMP_Text>().Any(x=>x.name=="Name"&&x.text.StartsWith("다른 씬의 책\n저자 : 공통 작가")),"Title and author survive scene and page changes in shared tooltip");
        if(rental)
        {
            var output=ui.GetComponentsInChildren<TradePointer>().First(x=>x.name=="Output_0");ui.ShowTooltip(output,Vector2.zero);
            Check(ui.GetComponentsInChildren<TMPro.TMP_Text>().Any(x=>x.name=="Name"&&x.text.EndsWith("누군가가 대여중입니다.")),"Reusable rental tooltip works outside library scene");
            Check(!ui.BeginDrag(TradePointer.Kind.Output,18,Vector2.zero),"Loaned offer cannot enter common cursor");
        }
        else Check(ui.BeginDrag(TradePointer.Kind.Output,18,Vector2.zero)&&ui.Drop(TradePointer.Kind.Inventory,0)&&inventory.GetSlot(0).Count==1,"Common free drag path works outside library scene");
        UnityEngine.Object.DestroyImmediate(ui.gameObject);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
        UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);scenes.Remove(scene);
    }
    return string.Join("\n",report);
}
finally
{
    UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
    foreach(var scene in scenes)UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
    System.IO.File.WriteAllLines("../ArtSource/WorldDistricts/CivicLibraryV4/QA/SharedTradeScenes.txt",report);
}
