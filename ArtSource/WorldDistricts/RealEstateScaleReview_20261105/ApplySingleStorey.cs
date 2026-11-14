using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompanyGame.Editor.WorldMaps;
using CompanyGame.Editor.Citizens;
using CompanyGame.World.Maps;

// One-off editor orchestration; reuses existing model remaps, collision factory and navigation builder.
public static class ApplySingleStoreyOffice
{
    const string Review="../ArtSource/WorldDistricts/RealEstateScaleReview_20261105/Review/";
    const string Source="../ArtSource/WorldDistricts/RealEstateOffice_20261104/SingleStoreyRevision_20261105/";
    const string Art="Assets/Art/WorldDistricts/RealEstateOffice";
    public static object RefreshSign()
    {
        var office=GameObject.Find("Map_CivicDistrict/10_World/Buildings/CivicDistrict_Community/RE_01").transform;
        Func<string> physicalSnapshot=()=>string.Join("\n",office.GetComponentsInChildren<MeshFilter>().Where(f=>Physical(f.transform,office)).OrderBy(f=>f.name).Select(f=>f.name+"|"+f.transform.position.ToString("F6")+"|"+string.Join(";",f.sharedMesh.vertices.Select(v=>v.ToString("F6")))+"|"+string.Join(",",f.sharedMesh.triangles)));
        var before=physicalSnapshot();
        File.Copy(Source+"Exports/RE_01.fbx",Art+"/Models/RE_01.fbx",true);
        File.Copy(Source+"Catalog/catalog.json",Art+"/SourceCatalog.json",true);
        AssetDatabase.ImportAsset(Art+"/Models/RE_01.fbx",ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(Art+"/SourceCatalog.json",ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        bool same=before==physicalSnapshot();
        if(!same)throw new Exception("Sign refresh unexpectedly changed physical geometry");
        AssetDatabase.SaveAssets();
        return new{physicalGeometryUnchanged=same};
    }
    public static object DoorPreview(){var v=SceneView.lastActiveSceneView;v.sceneLighting=true;v.drawGizmos=false;v.LookAt(new Vector3(76.8f,2,2.22f),Quaternion.Euler(8,-12,0),6.4f,false,true);v.Repaint();return true;}
    public static object CharacterPreview()
    {
        var old=GameObject.Find("_QA_ScaleCharacter");if(old)UnityEngine.Object.DestroyImmediate(old);
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Daldongne/Players/MaleVisual.prefab");
        var c=(GameObject)PrefabUtility.InstantiatePrefab(asset);c.name="_QA_ScaleCharacter";c.hideFlags=HideFlags.HideAndDontSave;
        c.transform.SetPositionAndRotation(new Vector3(75.35f,.02f,.57f),Quaternion.Euler(0,180,0));
        foreach(var mb in c.GetComponentsInChildren<MonoBehaviour>())mb.enabled=false;
        foreach(var skin in c.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=new Mesh{name="QA_CharacterBake",hideFlags=HideFlags.HideAndDontSave};skin.BakeMesh(mesh);
            var go=new GameObject("QA_CharacterBake"){hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(skin.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.forceRenderingOff=true;
        }
        DoorPreview();return true;
    }
    public static object OfficePreview(){var v=SceneView.lastActiveSceneView;v.sceneLighting=true;v.drawGizmos=false;v.LookAt(new Vector3(76.8f,1.7f,11.6f),Quaternion.Euler(24,-28,0),22,false,true);v.Repaint();return true;}
    public static object Main()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlaying||scene.isDirty||scene.path!="Assets/Scenes/Maps/CivicDistrict.unity")throw new Exception("Expected clean Civic scene in Edit Mode");
        var holder=GameObject.Find("Map_CivicDistrict/10_World/Buildings/CivicDistrict_Community").transform;
        var parcel=GameObject.Find("Parcel Sites/CivicDistrict_Community").GetComponent<MapParcel>();
        string parcelBefore=EditorJsonUtility.ToJson(parcel);File.WriteAllText(Review+"ParcelBefore.json",parcelBefore);
        var other=holder.parent.Cast<Transform>().Where(t=>t!=holder).ToArray();string before=Snapshot(other);
        File.WriteAllText(Review+"OtherBuildingsBefore.json",before);
        var modelPath=Art+"/Models/RE_01.fbx";string modelGuid=AssetDatabase.AssetPathToGUID(modelPath),prefabGuid=AssetDatabase.AssetPathToGUID(Art+"/Prefabs/RE_01.prefab");
        File.Copy(Source+"Exports/RE_01.fbx",modelPath,true);
        File.Copy(Source+"Catalog/catalog.json",Art+"/SourceCatalog.json",true);
        AssetDatabase.ImportAsset(modelPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(Art+"/SourceCatalog.json",ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        var office=holder.Find("RE_01").gameObject;
        if(Vector3.Distance(office.transform.lossyScale,Vector3.one)>.001f)throw new Exception("Scene must not scale the authored building");
        var rs=office.GetComponentsInChildren<MeshRenderer>();var b=BoundsOf(rs);
        if(b.size.x<26.5f||b.size.x>28||b.size.z<19.5f||b.size.z>21||b.size.y<4.8f||b.size.y>5.8f)throw new Exception("Unexpected authored dimensions: "+b);
        if(rs.Any(r=>r.sharedMaterials.Any(m=>!m||m.shader.name!="Universal Render Pipeline/Lit")))throw new Exception("Material remap failed");
        // Move the complete authored building, keeping eight metres in front for player decoration.
        var paving=parcel.GetComponentInChildren<MeshRenderer>().bounds;
        var desiredCenter=new Vector3(parcel.Center.x,paving.max.y+b.extents.y,parcel.Center.z-parcel.size.y*.5f+8+b.extents.z);
        office.transform.position+=desiredCenter-b.center;
        WorldMapTools.FitBuildingWithinTier(holder,LandPlot.SizeTier.Civic);
        var generated=GameObject.Find(MapColliderRebuilder.GroupName).transform;
        foreach(var t in generated.Cast<Transform>().Where(t=>t.name.StartsWith("RE_01_")).ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
        var plans=MapColliderRebuilder.Scan(scene).Plans.Where(p=>p.Source.transform.IsChildOf(office.transform)&&Physical(p.Source.transform,office.transform)).GroupBy(p=>p.Source).Select(g=>g.First()).ToArray();
        var create=typeof(MapColliderRebuilder).GetMethod("Create",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(var plan in plans){plan.Shape=ColliderShape.Mesh;var c=(Collider)create.Invoke(null,new object[]{generated,plan});c.name="RE_01_Structure_"+plan.Source.name;}

        // The previous enlarged design overlapped a tree and a lamp. Their original positions now clear
        // the smaller human-scale building, so restore only those original shared meshes and colliders.
        var originalMeshes=JArray.Parse(File.ReadAllText("../ArtSource/WorldDistricts/ParcelSizingCorrection_20261104/Review/SharedMeshesBefore.json"));
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();int restored=0;
        foreach(var row in originalMeshes)
        {
            var f=all.Single(t=>t.name==(string)row["group"]).Find((string)row["name"]).GetComponent<MeshFilter>();
            if(!AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith(Art+"/SceneMeshes/RE_",StringComparison.Ordinal))continue;
            var old=AssetDatabase.LoadAllAssetsAtPath((string)row["path"]).OfType<Mesh>().Single(m=>m.name==(string)row["mesh"]);
            f.sharedMesh=old;PrefabUtility.RecordPrefabInstancePropertyModifications(f);restored++;
        }
        var treeBox=new Bounds(new Vector3(94.5f,50,14),new Vector3(4,110,4));var lampBox=new Bounds(new Vector3(92.4f,50,10.5f),new Vector3(2,110,3.5f));int enabled=0;
        foreach(var c in generated.GetComponentsInChildren<Collider>())
        {
            if(c.enabled)continue;
            // Disabled collider.bounds is empty; inspect its actual enabled bounds before deciding.
            bool namedTree=new[]{"Furniture_leaf","Furniture_leafLight","Furniture_wood"}.Contains(c.name),namedLamp=c.name=="Furniture_metal";
            if(!namedTree&&!namedLamp)continue;
            c.enabled=true;Physics.SyncTransforms();var cb=c.bounds;
            bool match=namedTree&&treeBox.Contains(cb.min)&&treeBox.Contains(cb.max)||namedLamp&&lampBox.Contains(cb.min)&&lampBox.Contains(cb.max);
            if(match)enabled++;else c.enabled=false;
        }
        var surface=GameObject.Find("Citizen Navigation").GetComponent<Unity.AI.Navigation.NavMeshSurface>();
        var flags=BindingFlags.Static|BindingFlags.NonPublic;var nav=typeof(CitizenNavBuilder);
        nav.GetMethod("Bake",flags).Invoke(null,new object[]{surface,scene});var starts=nav.GetMethod("WriteStarts",flags).Invoke(null,new object[]{surface,scene,false});
        if(EditorJsonUtility.ToJson(parcel)!=parcelBefore||Snapshot(other)!=before)throw new Exception("Parcel or unrelated building changed");
        if(modelGuid!=AssetDatabase.AssetPathToGUID(modelPath)||prefabGuid!=AssetDatabase.AssetPathToGUID(Art+"/Prefabs/RE_01.prefab"))throw new Exception("Asset GUID changed");
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        b=BoundsOf(office.GetComponentsInChildren<MeshRenderer>());Selection.activeGameObject=office;
        var result=new{dimensions=b.size.ToString("F5"),center=b.center.ToString("F5"),frontSetback=b.min.z-(parcel.Center.z-parcel.size.y*.5f),modelGuid,prefabGuid,colliders=plans.Length,restoredSharedMeshes=restored,restoredColliders=enabled,starts,navTriangles=UnityEngine.AI.NavMesh.CalculateTriangulation().indices.Length/3};
        File.WriteAllText(Review+"UnityApplication.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
    }
    static bool Physical(Transform t,Transform root)
    {
        if(t.name.StartsWith("DoorHandle"))return false;
        for(var p=t.parent;p&&p!=root;p=p.parent)
            if(new[]{"Structure","Entrance","Frames","Glass"}.Contains(p.name))return true;
        return new[]{"WindowSill","FrontWingWall","FrontHeadBand","FrontPier"}.Any(n=>t.name.StartsWith(n));
    }
    public static Bounds BoundsOf(MeshRenderer[] rs){var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
    static string Snapshot(Transform[] roots)=>JsonConvert.SerializeObject(roots.SelectMany(t=>t.GetComponentsInChildren<Transform>(true)).Select(t=>new{id=GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(),name=t.name,p=t.position.ToString("F6"),r=t.rotation.ToString("F6"),s=t.lossyScale.ToString("F6")}));
}
