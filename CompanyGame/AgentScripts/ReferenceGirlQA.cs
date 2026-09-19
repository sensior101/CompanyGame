using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using CompanyGame.Daldongne;
using CompanyGame.Editor.Characters;
using Newtonsoft.Json.Linq;

public static class ReferenceGirlQA
{
    public static object Rebuild() => ReferenceGirlImporter.Rebuild();
    static string Output => Path.GetFullPath("../ArtSource/Daldongne/Characters");
    static void Capture(Camera cam,string name,int width=900,int height=1050)
    {
        var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var old=RenderTexture.active;var previous=cam.targetTexture;
        var tex=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            tex.ReadPixels(new Rect(0,0,width,height),0,0);tex.Apply();
            File.WriteAllBytes(Path.Combine(Output,name+".png"),tex.EncodeToPNG());
        }
        finally{cam.targetTexture=previous;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);}
    }
    public static object Preview()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Use Edit Mode for preview.");
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Reference girl preview studio");SceneManager.MoveGameObjectToScene(root,scene);
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.22f,.26f,.25f));
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceGirlImporter.VisualPath);
            var girl=UnityEngine.Object.Instantiate(prefab,root.transform);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform,false);
            floor.transform.localPosition=new Vector3(0,-.03f,0);floor.transform.localScale=new Vector3(200,.05f,200);floor.GetComponent<Renderer>().sharedMaterial=material;
            var key=new GameObject("Soft key").AddComponent<Light>();key.transform.SetParent(root.transform,false);
            key.type=LightType.Directional;key.intensity=1.8f;key.color=new Color(1,.94f,.84f);key.transform.rotation=Quaternion.Euler(40,155,0);key.shadows=LightShadows.Soft;
            var fill=new GameObject("Fill").AddComponent<Light>();fill.transform.SetParent(root.transform,false);
            fill.type=LightType.Directional;fill.intensity=.65f;fill.color=new Color(.79f,.88f,1);fill.transform.rotation=Quaternion.Euler(25,-50,0);
            var cam=new GameObject("Portrait camera").AddComponent<Camera>();cam.transform.SetParent(root.transform,false);cam.scene=scene;
            cam.orthographic=true;cam.orthographicSize=1.03f;cam.nearClipPlane=.01f;cam.farClipPlane=100;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.22f,.26f,.25f);
            var views=new Dictionary<string,Vector3>{{"Unity_Hero",new Vector3(2.7f,1.8f,5)},
                {"Unity_Front",new Vector3(0,1.25f,5)},{"Unity_Back",new Vector3(0,1.25f,-5)},{"Unity_Side",new Vector3(5,1.25f,0)}};
            foreach(var pair in views){cam.transform.position=pair.Value;cam.transform.LookAt(new Vector3(0,.91f,0));Capture(cam,pair.Key);}
            // Keep the same studio lighting so the portrait also reveals cap shadows,
            // intersecting facial features, and gaps around either ear.
            cam.orthographicSize=.405f;
            var portraits=new Dictionary<string,Vector3>{{"Unity_FaceFront",new Vector3(0,1.43f,5)},
                {"Unity_EarLeft",new Vector3(-4,1.52f,2.8f)},{"Unity_EarRight",new Vector3(4,1.52f,2.8f)}};
            foreach(var pair in portraits){cam.transform.position=pair.Value;cam.transform.LookAt(new Vector3(0,1.43f,0));Capture(cam,pair.Key,1100,1100);}
            // Additional portrait fill lets the sculpt be judged independently
            // from the original hard cap/nose shadows; existing QA views remain.
            var portraitFill=new GameObject("Frontal portrait fill").AddComponent<Light>();
            portraitFill.transform.SetParent(root.transform,false);portraitFill.type=LightType.Directional;
            portraitFill.intensity=.85f;portraitFill.color=new Color(1,.96f,.93f);
            portraitFill.transform.rotation=Quaternion.Euler(8,180,0);portraitFill.shadows=LightShadows.None;
            cam.transform.position=new Vector3(0,1.43f,5);cam.transform.LookAt(new Vector3(0,1.43f,0));Capture(cam,"Unity_FaceSoft",1100,1100);
            cam.transform.position=new Vector3(2.5f,1.50f,5);cam.transform.LookAt(new Vector3(0,1.43f,0));Capture(cam,"Unity_FaceThreeQuarterSoft",1100,1100);
            cam.transform.position=new Vector3(4,1.43f,2.8f);cam.transform.LookAt(new Vector3(0,1.43f,0));Capture(cam,"Unity_FaceObliqueSoft",1100,1100);
            cam.transform.position=new Vector3(5,1.43f,.1f);cam.transform.LookAt(new Vector3(0,1.43f,0));Capture(cam,"Unity_FaceProfileSoft",1100,1100);
            portraitFill.enabled=false;
            cam.orthographicSize=1.03f;
            girl.GetComponent<DaldongneAvatarMotion>().Pose(3,.15f);
            cam.transform.position=new Vector3(2.7f,1.8f,5);cam.transform.LookAt(new Vector3(0,.91f,0));Capture(cam,"Unity_Walk");
            return new{passed=true,images=12,folder=Output};
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Object.DestroyImmediate(material);}
    }

    public static object CheckScenes()
    {
        var setup=EditorSceneManager.GetSceneManagerSetup();var records=new JArray();
        try
        {
            foreach(string path in new[]{"Assets/Scenes/daldongnaemap.unity","Assets/Scenes/Maps/DaldongnePocketGarden.unity","Assets/Scenes/Templates/SmallMapTemplate.unity"})
            {
                var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
                if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                var looks=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<DaldongnePlayerAppearance>(true)).ToArray();
                if(looks.Length!=1)throw new Exception("Expected exactly one player: "+path);
                foreach(var look in looks)
                {
                    if(!look.female || !look.male || !look.female.transform.Find("Hips/Head/Hair"))throw new Exception("Stale/missing visual: "+path);
                    if(look.female.GetComponentsInChildren<MeshRenderer>(true).Length!=9)throw new Exception("Unexpected renderer count: "+path);
                    if(look.GetComponentsInChildren<Collider>(true).Length!=1)throw new Exception("Unexpected player collision: "+path);
                    records.Add(new JObject{["scene"]=path,["femaleLinked"]=true,["malePreserved"]=true,["colliders"]=1});
                }
                if(opened)EditorSceneManager.CloseScene(scene,true);
            }
        }
        finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
        var result=new JObject{["passed"]=true,["scenes"]=records};File.WriteAllText(Path.Combine(Output,"scene_validation.json"),result.ToString());return result;
    }

    public static object PlayCapture()
    {
        if(!EditorApplication.isPlaying)throw new Exception("Expected Play Mode.");
        var walker=UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        var look=walker.GetComponent<DaldongnePlayerAppearance>();look.Select(DaldongnePlayerAppearance.Variant.Female);
        if(look.female.GetComponentsInChildren<MeshRenderer>().Length!=9 || look.male.activeSelf)throw new Exception("Runtime female switch failed.");
        var motion=look.female.GetComponent<DaldongneAvatarMotion>();motion.Pose(3,.12f);
        if(Quaternion.Angle(motion.leftLeg.localRotation,motion.rightLeg.localRotation)<1)throw new Exception("Walk pose not applied.");
        var cam=walker.viewCamera;var pos=cam.transform.position;var rot=cam.transform.rotation;float size=cam.orthographicSize;
        try
        {
            Capture(cam,"Unity_InGame",1440,900);
            cam.orthographicSize=1.5f;cam.transform.position=walker.transform.position+new Vector3(2.4f,1.9f,4);
            cam.transform.LookAt(walker.transform.position+Vector3.up*.85f);Capture(cam,"Unity_InGame_Close",1100,1100);
        }
        finally{cam.transform.SetPositionAndRotation(pos,rot);cam.orthographicSize=size;}
        return new{passed=true,playing=true,femaleActive=look.female.activeSelf,maleActive=look.male.activeSelf,walking=walker.walking};
    }
}
