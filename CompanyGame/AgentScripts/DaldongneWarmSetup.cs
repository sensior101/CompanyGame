using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CompanyGame.Daldongne;

public static class DaldongneWarmSetup
{
    public static object AddWalker()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before scene setup.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/daldongnaemap.unity")throw new InvalidOperationException("Expected warm map scene.");
        var cameras=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).ToArray();
        var camera=cameras.FirstOrDefault(c=>c.name=="Daldongne Warm Map Camera" && c.GetComponent<DaldongneMapCamera>()!=null)
            ??cameras.FirstOrDefault(c=>c.GetComponent<DaldongneMapCamera>()!=null);
        if(camera==null)throw new InvalidOperationException("The warm map's overview camera is missing.");
        var mats=AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Daldongne/WarmVillage/DaldongneWarmMeshes.asset").OfType<Material>().ToArray();
        var coat=mats.FirstOrDefault(m=>m.name=="blue");
        var skin=mats.FirstOrDefault(m=>m.name=="cream");
        var trousers=mats.FirstOrDefault(m=>m.name=="dark");
        if(coat==null || skin==null || trousers==null)
            throw new InvalidOperationException("Import the warm village material bundle before adding the walker.");
        var existing=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true))
            .Select(t=>t.gameObject).FirstOrDefault(g=>g.name=="Village walking preview");
        var go=existing?existing:new GameObject("Village walking preview");
        if(!existing)go.transform.position=new Vector3(-17.5f,1.08f,-27);
        // Reapplying setup repairs stale serialized dimensions and camera links.
        var cc=go.GetComponent<CharacterController>();
        if(!cc)cc=go.AddComponent<CharacterController>();
        cc.radius=.35f;cc.height=1.8f;cc.center=Vector3.up*.9f;
        cc.stepOffset=.23f;cc.slopeLimit=45;cc.skinWidth=.02f;cc.minMoveDistance=0;
        var walker=go.GetComponent<PlayerMovement>();
        if(!walker)walker=go.AddComponent<PlayerMovement>();
        walker.viewCamera=camera;walker.overview=camera.GetComponent<DaldongneMapCamera>();
        walker.walking=false;walker.overview.enabled=true;
        if(go.transform.childCount==0)
        {
            Part(go,"Coat",PrimitiveType.Capsule,new Vector3(0,.95f,0),new Vector3(.53f,.43f,.35f),coat);
            Part(go,"Head",PrimitiveType.Sphere,new Vector3(0,1.58f,0),Vector3.one*.33f,skin);
            foreach(float x in new[]{-.14f,.14f})Part(go,"Trouser",PrimitiveType.Cube,new Vector3(x,.32f,0),new Vector3(.19f,.64f,.23f),trousers);
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new {created=!existing,controls="F walking / overview, WASD move, Shift run, R return to station",radius=cc.radius,height=cc.height,stepOffset=cc.stepOffset,slopeLimit=cc.slopeLimit,skinWidth=cc.skinWidth};
    }
    static void Part(GameObject parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
    {
        var o=GameObject.CreatePrimitive(type);o.name=name;o.transform.SetParent(parent.transform,false);
        o.transform.localPosition=position;o.transform.localScale=scale;
        UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>());o.GetComponent<Renderer>().sharedMaterial=material;
    }
}
