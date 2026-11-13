using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using CompanyGame.World.Maps;
using CompanyGame.Editor.WorldMaps;

// One-off asset acceptance checks outside Assets; no runtime system is introduced.
public static class VerifyAppliedOffice
{
    const string Review="../ArtSource/WorldDistricts/RealEstateScaleReview_20261105/Review/";
    public static object Main()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlaying||scene.path!="Assets/Scenes/Maps/CivicDistrict.unity")throw new Exception("Expected Civic edit scene");
        var qa=GameObject.Find("_QA_ScaleCharacter");
        if(qa){var baked=qa.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).Where(m=>m&&m.name=="QA_CharacterBake").Distinct().ToArray();UnityEngine.Object.DestroyImmediate(qa);foreach(var m in baked)UnityEngine.Object.DestroyImmediate(m);}
        var holder=GameObject.Find("Map_CivicDistrict/10_World/Buildings/CivicDistrict_Community").transform;
        var office=holder.Find("RE_01");var rs=office.GetComponentsInChildren<MeshRenderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
        var checks=new Dictionary<string,bool>();
        checks["dimensions_27_20_5"]=Vector3.Distance(b.size,new Vector3(27,5,20))<.01f;
        checks["world_scale_identity"]=Vector3.Distance(office.lossyScale,Vector3.one)<.001f;
        var parcel=GameObject.Find("Parcel Sites/CivicDistrict_Community").GetComponent<MapParcel>();
        checks["parcel_unchanged"]=EditorJsonUtility.ToJson(parcel)==File.ReadAllText(Review+"ParcelBefore.json");
        checks["front_setback_8m"]=Mathf.Abs(b.min.z-(parcel.Center.z-parcel.size.y*.5f)-8)<.01f;
        var others=holder.parent.Cast<Transform>().Where(t=>t!=holder).SelectMany(t=>t.GetComponentsInChildren<Transform>(true));
        var snapshot=JsonConvert.SerializeObject(others.Select(t=>new{id=GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(),name=t.name,p=t.position.ToString("F6"),r=t.rotation.ToString("F6"),s=t.lossyScale.ToString("F6")}));
        checks["other_buildings_unchanged"]=snapshot==File.ReadAllText(Review+"OtherBuildingsBefore.json");
        var doors=office.GetComponentsInChildren<Transform>().Where(t=>t.name=="Door_Leaf_01"||t.name=="Door_Leaf_02").Select(t=>{var renderers=t.GetComponentsInChildren<MeshRenderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);return bounds.size.y;}).ToArray();
        checks["both_doors_2_5m"]=doors.Length==2&&doors.All(h=>Mathf.Abs(h-2.5f)<.01f);
        checks["materials_valid"]=rs.All(r=>r.sharedMaterials.All(m=>m&&m.shader.name=="Universal Render Pipeline/Lit"));
        checks["meshes_valid"]=office.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh&&f.sharedMesh.vertexCount>0);
        checks["model_guid_preserved"]=AssetDatabase.AssetPathToGUID("Assets/Art/WorldDistricts/RealEstateOffice/Models/RE_01.fbx")=="aa14643d5f4777b40806117e72f18cd9";
        checks["prefab_guid_preserved"]=AssetDatabase.AssetPathToGUID("Assets/Art/WorldDistricts/RealEstateOffice/Prefabs/RE_01.prefab")=="5dcd1a120bc5b5d4396646129ae92c18";
        var colliders=GameObject.Find(MapColliderRebuilder.GroupName).GetComponentsInChildren<Collider>().Where(c=>c.name.StartsWith("RE_01_")).ToArray();
        checks["109_current_physical_meshes"]=colliders.Length==109&&colliders.All(c=>c.enabled&&c is MeshCollider mc&&mc.sharedMesh);
        var currentMeshes=office.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).ToArray();
        checks["colliders_reference_current_model"]=colliders.All(c=>c is MeshCollider mc&&currentMeshes.Contains(mc.sharedMesh));
        var startWanted=new Vector3(b.center.x,.1f,b.min.z-1.2f);NavMeshHit start;
        bool sampled=NavMesh.SamplePosition(startWanted,out start,1.5f,NavMesh.AllAreas);checks["front_approach_on_navmesh"]=sampled;
        var routes=new List<object>();
        foreach(var v in new[]{new Vector2(-1,-1),new Vector2(0,-1),new Vector2(1,-1),new Vector2(-1,0),new Vector2(1,0),new Vector2(-1,1),new Vector2(0,1),new Vector2(1,1)})
        {
            var wanted=new Vector3(b.center.x+v.x*15.5f,.1f,b.center.z+v.y*12);NavMeshHit end;var path=new NavMeshPath();
            bool ok=sampled&&NavMesh.SamplePosition(wanted,out end,1.5f,NavMesh.AllAreas)&&NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
            checks["route_"+v]=ok;routes.Add(new{target=wanted.ToString("F3"),complete=ok,corners=path.corners.Length});
        }
        checks["no_preview_character"]=!GameObject.Find("_QA_ScaleCharacter");
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        checks["scene_saved"]=!scene.isDirty;
        var result=new{pass=checks.Values.All(x=>x),dimensions=b.size.ToString("F5"),doorHeights=doors,checks,routes};
        File.WriteAllText(Review+"UnityValidation.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
    }
}
