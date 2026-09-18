using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using CompanyGame.Daldongne;

public static class DaldongnePlayersBuild
{
    const string Dir = "Assets/Art/Daldongne/Players";
    static Mesh rounded, ball;
    static Dictionary<string,Material> mats;
    static Transform Joint(Transform parent,string name,Vector3 pos)
    { var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=pos;return t; }
    static Transform Part(Transform parent,string name,Vector3 pos,Vector3 scale,string mat,bool sphere=false)
    {
        var t=Joint(parent,name,pos);t.localScale=scale;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh=sphere?ball:rounded;
        t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=mats[mat];return t;
    }
    static Material Mat(string name,string hex)
    {
        var path=Dir+"/Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        Color c;ColorUtility.TryParseHtmlString(hex,out c);m.name=name;m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",.18f);
        EditorUtility.SetDirty(m);return m;
    }
    static Mesh MakeMesh(string name,bool sphere)
    {
        string path=Dir+"/"+name+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing)return existing;
        var vertices=new List<Vector3>();var triangles=new List<int>();
        Action<Vector3,Vector3,Vector3> tri=(a,b,c)=>{int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);};
        var rings=new List<Vector3[]>();int count=8;
        if(sphere)
        {
            for(int r=0;r<=6;r++)
            {float theta=Mathf.PI*r/6;var ring=new Vector3[count];for(int i=0;i<count;i++){float a=2*Mathf.PI*i/count;ring[i]=new Vector3(Mathf.Cos(a)*Mathf.Sin(theta),Mathf.Cos(theta),Mathf.Sin(a)*Mathf.Sin(theta))*.5f;}rings.Add(ring);}
        }
        else
        {
            var outline=new[]{new Vector2(.32f,.5f),new Vector2(-.32f,.5f),new Vector2(-.5f,.32f),new Vector2(-.5f,-.32f),new Vector2(-.32f,-.5f),new Vector2(.32f,-.5f),new Vector2(.5f,-.32f),new Vector2(.5f,.32f)};
            foreach(float y in new[]{.5f,.4f,-.4f,-.5f}){float k=Mathf.Abs(y)>.45f?.83f:1; rings.Add(outline.Select(p=>new Vector3(p.x*k,y,p.y*k)).ToArray());}
            for(int i=0;i<count;i++){int j=(i+1)%count;tri(Vector3.up*.5f,rings[0][j],rings[0][i]);tri(Vector3.down*.5f,rings[3][i],rings[3][j]);}
        }
        for(int r=0;r<rings.Count-1;r++)for(int i=0;i<count;i++)
        {
            int j=(i+1)%count;var a=rings[r][i];var b=rings[r][j];var c=rings[r+1][i];var d=rings[r+1][j];
            // Ensure every triangle points away from the centre for either ring layout.
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),(a+b+c)/3)<0){tri(a,c,b);tri(b,c,d);}else{tri(a,b,c);tri(b,d,c);}
        }
        var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static GameObject Visual(bool female)
    {
        var root=new GameObject(female?"FemaleVisual":"MaleVisual");
        var motion=root.AddComponent<DaldongneAvatarMotion>();var h=Joint(root.transform,"Hips",new Vector3(0,.83f,0));motion.hips=h;
        string coat=female?"Sage cardigan":"Ochre jacket",pants=female?"Terracotta trousers":"Indigo denim";
        Part(h,"Hip fabric",new Vector3(0,.015f,0),new Vector3(.35f,.20f,.24f),pants);
        Part(h,"Soft jacket",new Vector3(0,.31f,0),new Vector3(female?.43f:.47f,.48f,.28f),coat);
        Part(h,"Cream shirt",new Vector3(0,.33f,.139f),new Vector3(.15f,.39f,.02f),"Oatmeal cotton");
        for(int side=-1;side<=1;side+=2)
        {
            var collar=Part(h,"Collar",new Vector3(side*.084f,.514f,.145f),new Vector3(.08f,.12f,.03f),coat);collar.localRotation=Quaternion.Euler(0,0,side*25);
            Part(h,"Patch pocket",new Vector3(side*.15f,.22f,.149f),new Vector3(.09f,.10f,.025f),coat);
        }
        Part(h,"Neck",new Vector3(0,.60f,0),new Vector3(.13f,.13f,.13f),"Warm skin");
        var head=Joint(h,"Head",new Vector3(0,.765f,0));
        Part(head,"Face",Vector3.zero,new Vector3(.36f,.40f,.32f),"Warm skin",true);
        Part(head,"Hair crown",new Vector3(0,.125f,-.024f),new Vector3(.39f,.21f,.35f),"Chestnut hair",true);
        if(female)
        {
            Part(head,"Bob back",new Vector3(0,-.018f,-.11f),new Vector3(.37f,.36f,.16f),"Chestnut hair");
            foreach(float x in new[]{-.156f,.156f})Part(head,"Bob side",new Vector3(x,-.005f,0),new Vector3(.075f,.32f,.25f),"Chestnut hair");
            var fringe=Part(head,"Side fringe",new Vector3(-.045f,.113f,.122f),new Vector3(.26f,.13f,.07f),"Chestnut hair");fringe.localRotation=Quaternion.Euler(0,0,-16);
            Part(head,"Brass hair clip",new Vector3(.143f,.095f,.116f),new Vector3(.049f,.02f,.015f),"Brass");
        }
        else
        {
            Part(head,"Short hair back",new Vector3(0,.044f,-.13f),new Vector3(.33f,.25f,.085f),"Chestnut hair");
            var fringe=Part(head,"Swept fringe",new Vector3(.015f,.13f,.10f),new Vector3(.32f,.105f,.12f),"Chestnut hair");fringe.localRotation=Quaternion.Euler(0,0,10);
        }
        foreach(float x in new[]{-.068f,.068f})
        {
            Part(head,"Eye",new Vector3(x,.025f,.15f),new Vector3(.023f,.032f,.014f),"Ink",true);
            Part(head,"Brow",new Vector3(x,.070f,.149f),new Vector3(.04f,.012f,.012f),"Chestnut hair");
            Part(head,"Ear",new Vector3(Mathf.Sign(x)*.177f,-.005f,0),new Vector3(.055f,.086f,.062f),"Warm skin",true);
        }
        Part(head,"Nose",new Vector3(0,-.025f,.16f),new Vector3(.041f,.043f,.035f),"Warm skin",true);
        Part(head,"Smile",new Vector3(0,-.082f,.141f),new Vector3(.040f,.009f,.01f),"Rose");
        for(int side=-1;side<=1;side+=2)
        {
            var arm=Joint(h,side<0?"LeftArm":"RightArm",new Vector3(side*.266f,.48f,0));
            Part(arm,"Sleeve",new Vector3(0,-.15f,0),new Vector3(.145f,.32f,.17f),coat);
            Part(arm,"Cuff",new Vector3(0,-.297f,0),new Vector3(.136f,.055f,.16f),"Oatmeal cotton");
            Part(arm,"Hand",new Vector3(0,-.368f,0),new Vector3(.105f,.12f,.12f),"Warm skin",true);
            var leg=Joint(h,side<0?"LeftLeg":"RightLeg",new Vector3(side*.105f,-.015f,0));
            Part(leg,"Upper trouser",new Vector3(0,-.15f,0),new Vector3(.169f,.33f,.205f),pants);
            var knee=Joint(leg,"Knee",new Vector3(0,-.335f,0));
            Part(knee,"Lower trouser",new Vector3(0,-.15f,0),new Vector3(.15f,.32f,.18f),pants);
            Part(knee,"Sock",new Vector3(0,-.30f,0),new Vector3(.135f,.07f,.16f),"Oatmeal cotton");
            Part(knee,"Sneaker",new Vector3(0,-.363f,.042f),new Vector3(.17f,.135f,.28f),"Canvas shoe");
            Part(knee,"Rubber sole",new Vector3(0,-.397f,.044f),new Vector3(.174f,.04f,.286f),"Oatmeal cotton");
            Part(knee,"Laces",new Vector3(0,-.303f,.074f),new Vector3(.095f,.013f,.048f),"Oatmeal cotton");
            if(side<0){motion.leftArm=arm;motion.leftLeg=leg;motion.leftKnee=knee;}
            else{motion.rightArm=arm;motion.rightLeg=leg;motion.rightKnee=knee;}
        }
        if(female)
        {
            var strap=Part(h,"Crossbody strap",new Vector3(0,.31f,.164f),new Vector3(.042f,.55f,.025f),"Leather");strap.localRotation=Quaternion.Euler(0,0,31);
            Part(h,"Satchel",new Vector3(.205f,.055f,.13f),new Vector3(.19f,.19f,.10f),"Leather");
            Part(h,"Satchel flap",new Vector3(.205f,.105f,.184f),new Vector3(.176f,.08f,.024f),"Ochre jacket");
            Part(h,"Clasp",new Vector3(.205f,.072f,.2f),new Vector3(.028f,.027f,.014f),"Brass");
        }
        else
        {
            Part(h,"Canvas backpack",new Vector3(0,.28f,-.208f),new Vector3(.32f,.37f,.17f),"Sage cardigan");
            Part(h,"Backpack pocket",new Vector3(0,.19f,-.304f),new Vector3(.23f,.16f,.06f),"Leather");
            foreach(float x in new[]{-.157f,.157f})Part(h,"Shoulder strap",new Vector3(x,.34f,.158f),new Vector3(.044f,.37f,.025f),"Leather");
        }
        motion.Pose(0,0);return root;
    }
    static void Attach(GameObject go,GameObject female,GameObject male,bool selectedFemale)
    {
        var selector=go.GetComponent<DaldongnePlayerAppearance>();if(!selector)selector=go.AddComponent<DaldongnePlayerAppearance>();
        selector.female=(GameObject)PrefabUtility.InstantiatePrefab(female,go.transform);
        selector.male=(GameObject)PrefabUtility.InstantiatePrefab(male,go.transform);
        selector.Select(selectedFemale?DaldongnePlayerAppearance.Variant.Female:DaldongnePlayerAppearance.Variant.Male);
        var cc=go.GetComponent<CharacterController>();if(!cc)cc=go.AddComponent<CharacterController>();
        cc.radius=.35f;cc.height=1.8f;cc.center=Vector3.up*.9f;cc.stepOffset=.23f;cc.slopeLimit=45;cc.skinWidth=.02f;cc.minMoveDistance=0;
        var walker=go.GetComponent<DaldongneVillageWalker>();if(!walker)walker=go.AddComponent<DaldongneVillageWalker>();walker.walking=true;
    }
    public static object Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop play mode first.");
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/Scenes/DaldongneWarmMap.unity")throw new Exception("Open warm map first.");
        Directory.CreateDirectory(Dir+"/Materials");AssetDatabase.Refresh();
        rounded=MakeMesh("Beveled octagonal mesh",false);ball=MakeMesh("Faceted portrait mesh",true);
        mats=new Dictionary<string,Material>();
        string[] names={"Warm skin","Chestnut hair","Sage cardigan","Ochre jacket","Oatmeal cotton","Terracotta trousers","Indigo denim","Canvas shoe","Leather","Brass","Ink","Rose"};
        string[] colors={"#DDA783","#3C302C","#638B7D","#C99752","#F0DFC0","#915F4F","#465B6C","#61554D","#78513B","#D7B878","#252A2A","#A15F53"};
        for(int i=0;i<names.Length;i++)mats[names[i]]=Mat(names[i],colors[i]);
        var f=Visual(true);var female=PrefabUtility.SaveAsPrefabAsset(f,Dir+"/FemaleVisual.prefab");UnityEngine.Object.DestroyImmediate(f);
        var m=Visual(false);var male=PrefabUtility.SaveAsPrefabAsset(m,Dir+"/MaleVisual.prefab");UnityEngine.Object.DestroyImmediate(m);
        foreach(bool sex in new[]{true,false})
        {
            var p=new GameObject(sex?"PlayerFemale":"PlayerMale");Attach(p,female,male,sex);
            PrefabUtility.SaveAsPrefabAsset(p,Dir+"/"+p.name+".prefab");UnityEngine.Object.DestroyImmediate(p);
        }
        var w=UnityEngine.Object.FindAnyObjectByType<DaldongneVillageWalker>();if(!w)throw new Exception("Existing map walker missing.");
        var children=w.transform.Cast<Transform>().ToArray();
        foreach(var child in children)if(new[]{"Coat","Head","Trouser","FemaleVisual","MaleVisual"}.Contains(child.name))UnityEngine.Object.DestroyImmediate(child.gameObject);
        Attach(w.gameObject,female,male,true);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        Selection.activeGameObject=w.gameObject;
        return new{scene=scene.path,folder=Dir,variants=2,defaultVariant="Female",playOnStart=true,controls="1 female / 2 male / WASD / Shift / F overview / R reset"};
    }
    public static object Preview()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Character portrait studio");SceneManager.MoveGameObjectToScene(root,scene);
        try
        {
            foreach(bool sex in new[]{true,false})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Dir+(sex?"/FemaleVisual.prefab":"/MaleVisual.prefab"));
                var o=UnityEngine.Object.Instantiate(prefab,root.transform);o.transform.localPosition=new Vector3(sex?-.57f:.57f,0,0);o.transform.localRotation=Quaternion.Euler(0,-12,0);
            }
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.SetParent(root.transform);ground.transform.localPosition=new Vector3(0,-.035f,0);ground.transform.localScale=new Vector3(20,.04f,20);
            ground.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Dir+"/Materials/Oatmeal cotton.mat");
            var light=Joint(root.transform,"Studio key",Vector3.zero).gameObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.color=new Color(1,.95f,.89f);light.transform.rotation=Quaternion.Euler(45,150,0);light.shadows=LightShadows.Soft;
            var cam=Joint(root.transform,"Portrait camera",new Vector3(2.4f,2.2f,5)).gameObject.AddComponent<Camera>();cam.transform.LookAt(new Vector3(0,.86f,0));cam.orthographic=true;cam.orthographicSize=1.35f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.77f,.77f,.68f);cam.scene=scene;
            var rt=RenderTexture.GetTemporary(1400,1100,24);var tex=new Texture2D(1400,1100,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1400,1100),0,0);tex.Apply();Directory.CreateDirectory("Assets/Screenshots");File.WriteAllBytes("Assets/Screenshots/Daldongne_Players.png",tex.EncodeToPNG());}
            finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);}
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
        AssetDatabase.Refresh();return new{image="Assets/Screenshots/Daldongne_Players.png"};
    }
}
