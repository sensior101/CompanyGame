using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompanyGame.Editor.WorldMaps;
using CompanyGame.Editor.Citizens;
using CompanyGame.World.Maps;

// One-time orchestration of existing import, collider, trimming and navigation APIs.
public static class ReplaceProportionalOffice
{
    const string Review="../ArtSource/WorldDistricts/ProportionalSizing_20261105/Review/";
    const string Source="../ArtSource/WorldDistricts/RealEstateOffice_20261104/ProportionalFit_20261105/";
    const string Art="Assets/Art/WorldDistricts/RealEstateOffice";
    public static object Main()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlaying||scene.isDirty||scene.path!="Assets/Scenes/Maps/CivicDistrict.unity") throw new Exception("Expected clean Civic scene in Edit Mode");
        var holder=GameObject.Find("Map_CivicDistrict/10_World/Buildings/CivicDistrict_Community").transform;
        var office=holder.Find("RE_01").gameObject;
        var parcel=GameObject.Find("Parcel Sites/CivicDistrict_Community").GetComponent<MapParcel>();
        string parcelBefore=EditorJsonUtility.ToJson(parcel);
        File.WriteAllText(Review+"ParcelBefore.json",parcelBefore);
        var other=holder.parent.Cast<Transform>().Where(t=>t!=holder).ToArray();
        string before=Snapshot(other);File.WriteAllText(Review+"OtherBuildingsBefore.json",before);
        string modelPath=Art+"/Models/RE_01.fbx";
        string modelGuid=AssetDatabase.AssetPathToGUID(modelPath);
        string prefabGuid=AssetDatabase.AssetPathToGUID(Art+"/Prefabs/RE_01.prefab");
        File.Copy(Source+"Exports/RE_01.fbx",modelPath,true);
        File.Copy(Source+"Catalog/catalog.json",Art+"/SourceCatalog.json",true);
        AssetDatabase.ImportAsset(modelPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(Art+"/SourceCatalog.json",ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
        office=holder.Find("RE_01").gameObject;
        var rs=office.GetComponentsInChildren<MeshRenderer>();var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
        if(Mathf.Abs(bounds.size.x-38)>.01f||Mathf.Abs(bounds.size.z-25.668933f)>.01f||Mathf.Abs(bounds.size.y-15.331511f)>.01f)throw new Exception("Imported proportional dimensions disagree: "+bounds);
        if(rs.Any(r=>r.sharedMaterials.Any(m=>!m||m.shader.name!="Universal Render Pipeline/Lit")))throw new Exception("Material remap failed");
        if(modelGuid!=AssetDatabase.AssetPathToGUID(modelPath)||prefabGuid!=AssetDatabase.AssetPathToGUID(Art+"/Prefabs/RE_01.prefab"))throw new Exception("Asset identity changed");
        WorldMapTools.FitBuildingWithinTier(holder,LandPlot.SizeTier.Civic);
        var generated=GameObject.Find(MapColliderRebuilder.GroupName).transform;
        foreach(var t in generated.Cast<Transform>().Where(t=>t.name.StartsWith("RE_01_")).ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
        var structuralNames=new[]{"SideWall","RearWall","FloorSlab","RoofSlab","RoofSurface","Parapet","FrontPier","FrontHeadBand","WindowSill","WindowJamb","WindowRail","EntranceThreshold","EntranceJamb","EntranceHeader","DoorTransom","StorefrontGlass","DoorGlass","DoorStile","DoorRail"};
        var plans=MapColliderRebuilder.Scan(scene).Plans.Where(p=>p.Source.transform.IsChildOf(office.transform)&&structuralNames.Any(n=>p.Source.name==n||p.Source.name.StartsWith(n+"."))).ToArray();
        var create=typeof(MapColliderRebuilder).GetMethod("Create",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(var plan in plans)
        {
            // Enlarged jambs and piers need collision too. Exact meshes preserve authored openings.
            plan.Shape=ColliderShape.Mesh;
            var c=(Collider)create.Invoke(null,new object[]{generated,plan});c.name="RE_01_"+c.name;
        }
        var treeBox=new Bounds(new Vector3(94.5f,50,14),new Vector3(4,110,4));
        var lampBox=new Bounds(new Vector3(92.4f,50,10.5f),new Vector3(2,110,3.5f));
        var tree=CityHallRecovery.TrimParcelDetails("CivicDistrict_Community",parcel.parcelId,Art,"RE_TreeTrim_",new[]{"Furniture_leaf","Furniture_leafLight","Furniture_wood"},"CivicDistrict_Planting",treeBox);
        var lamp=CityHallRecovery.TrimParcelDetails("CivicDistrict_Community",parcel.parcelId,Art,"RE_LampTrim_",new[]{"Furniture_metal"},"CivicDistrict_Streets",lampBox);
        Physics.SyncTransforms();
        var surface=GameObject.Find("Citizen Navigation").GetComponent<Unity.AI.Navigation.NavMeshSurface>();
        var flags=BindingFlags.Static|BindingFlags.NonPublic;var nav=typeof(CitizenNavBuilder);
        nav.GetMethod("Bake",flags).Invoke(null,new object[]{surface,scene});
        var starts=nav.GetMethod("WriteStarts",flags).Invoke(null,new object[]{surface,scene,false});
        if(EditorJsonUtility.ToJson(parcel)!=parcelBefore||Snapshot(other)!=before)throw new Exception("Parcel or unrelated building changed");
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=office;
        var report=new{bounds=bounds.ToString("F5"),modelGuid,prefabGuid,colliders=plans.Length,tree,lamp,starts,navTriangles=UnityEngine.AI.NavMesh.CalculateTriangulation().indices.Length/3,parcelUnchanged=true,otherBuildingsUnchanged=true};
        File.WriteAllText(Review+"UnityReplacement.json",JsonConvert.SerializeObject(report,Formatting.Indented));
        return report;
    }
    public static string Snapshot(Transform[] roots)=>JsonConvert.SerializeObject(roots.SelectMany(t=>t.GetComponentsInChildren<Transform>(true)).Select(t=>new{id=GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(),name=t.name,p=t.position.ToString("F6"),r=t.rotation.ToString("F6"),s=t.lossyScale.ToString("F6")}));
}
