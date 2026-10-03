using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>Targeted, repeatable layout migration; preserves merchandise and gameplay links.</summary>
public static class ImproveConvenienceSpace
{
    const string ScenePath = "Assets/Scenes/Interiors/ConvenienceStoreInterior.unity";
    const string PrefabPath = "Assets/Art/Interiors/ConvenienceStore/ConvenienceStoreInterior.prefab";
    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new Exception("Exit Play Mode first.");
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try { Layout(prefab.transform); PrefabUtility.SaveAsPrefabAsset(prefab,PrefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var scene = SceneManager.GetSceneByPath(ScenePath); bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            var model = roots.Single(g=>g.transform.Find("20_Collision_Proxies"));
            Layout(model.transform);
            var all = roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var npc = all.Single(t=>t.name=="ConvenienceClerk");
            Undo.RecordObject(npc,"Match clerk stature and widen staff space");
            npc.localScale = Vector3.one;
            npc.position = new Vector3(-3.50f,.016f,4.85f);
            var visual = npc.Find("MaleVisual");
            var bounds = BoundsOf(visual);
            npc.localScale = Vector3.one * (1.8f / bounds.size.y);
            bounds = BoundsOf(visual);
            npc.position += Vector3.up * (.016f - bounds.min.y);
            var talk = all.Single(t=>t.name=="Clerk_CustomerPosition");
            Undo.RecordObject(talk,"Follow checkout position");
            talk.position = new Vector3(-1.56f,.04f,4.85f);
            var camera = roots.SelectMany(g=>g.GetComponentsInChildren<PlayerCameraController>(true)).Single(c=>c.enabled);
            camera.firstPersonEyeHeight=1.6f;
            camera.targetHeight=1.4f;
            EditorUtility.SetDirty(camera);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "Saved prefab and interior: staff aisle widened 0.50m, snack row shifted 0.25m, rear row shortened 20%, freezer shortened 20% and moved 0.65m, clerk height 1.80m.";
        }
        finally { if(opened)EditorSceneManager.CloseScene(scene,true); }
    }
    public static Bounds BoundsOf(Transform t)
    {
        var rs=t.GetComponentsInChildren<Renderer>(true); var b=rs[0].bounds;
        foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);
        return b;
    }
    static void Group(Transform root,string name,Vector3 scale,Vector3 offset)
    {
        var t=root.Find(name); if(!t)throw new Exception("Missing group "+name);
        Undo.RecordObject(t,"Improve convenience store clearances");
        t.localScale=scale; t.localPosition=offset;
        if(PrefabUtility.IsPartOfPrefabInstance(t))PrefabUtility.RecordPrefabInstancePropertyModifications(t);
    }
    static void Layout(Transform root)
    {
        Group(root,"03_Checkout",Vector3.one,new Vector3(.5f,0,0));
        Group(root,"06_Snack_Gondola",Vector3.one,new Vector3(.25f,0,0));
        Group(root,"07_Grocery_Gondola",new Vector3(1,1,.8f),new Vector3(0,0,1.07f));
        Group(root,"11_IceCream_Freezer",new Vector3(1,1,.8f),new Vector3(0,0,1.22f));
        foreach(var box in root.Find("20_Collision_Proxies").GetComponentsInChildren<BoxCollider>())
        {
            Vector3 scale=Vector3.one,offset=Vector3.zero;
            if(box.name=="Checkout_Base")offset.x=.5f;
            else if(box.name.StartsWith("06_Snack_Gondola"))offset.x=.25f;
            else if(box.name.StartsWith("07_Grocery_Gondola")){scale.z=.8f;offset.z=1.07f;}
            else if(box.name=="Freezer"){scale.z=.8f;offset.z=1.22f;}
            else continue;
            Undo.RecordObject(box.transform,"Keep collision aligned with furniture");
            box.transform.localScale=scale;box.transform.localPosition=offset;
            if(PrefabUtility.IsPartOfPrefabInstance(box.transform))PrefabUtility.RecordPrefabInstancePropertyModifications(box.transform);
        }
    }
}
