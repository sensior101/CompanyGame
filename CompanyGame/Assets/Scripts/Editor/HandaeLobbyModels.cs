using System;
using UnityEditor;
using UnityEngine;

/// <summary>Imports the supplied lobby furniture as shared meshes with URP materials and simple collision.</summary>
public static class HandaeLobbyModels
{
    const string Root="Assets/Art/WorldDistricts/HandaeHQ/Lobby/Models";
    static readonly string[] Keys={"Armchair","Sofa","Toilet","Sink"};
    static readonly string[] Files={"armchair_game.fbx","sofa_company2_game.fbx","toilet_game.fbx","sink_game.fbx"};

    public static void EnsureReady()
    {
        for(int i=0;i<Keys.Length;i++)
        {
            string path=Root+"/"+Keys[i]+"/"+Files[i];
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            if(!importer)throw new InvalidOperationException("The requested lobby model is missing: "+path);
            bool changed=importer.importAnimation||importer.addCollider||importer.isReadable||
                importer.materialImportMode!=ModelImporterMaterialImportMode.None;
            importer.importAnimation=false;importer.addCollider=false;importer.isReadable=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            if(changed)importer.SaveAndReimport();
            if(!AssetDatabase.LoadAssetAtPath<GameObject>(path))throw new InvalidOperationException("FBX import failed: "+path);
            MaterialFor(Keys[i]);
        }
    }

    static Material MaterialFor(string key)
    {
        string path=Root+"/"+key+"/"+key+"_URP.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material)
        {
            material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.name=key+"_URP";
            AssetDatabase.CreateAsset(material,path);
        }
        material.shader=Shader.Find("Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor",key=="Armchair"?new Color(.115f,.135f,.145f):key=="Sofa"?new Color(.35f,.33f,.30f):new Color(.92f,.93f,.92f));
        material.SetFloat("_Smoothness",key=="Armchair"?.34f:key=="Sofa"?.24f:.68f);
        material.SetFloat("_Metallic",0f);material.enableInstancing=true;
        if(key=="Sink")
        {
            string texturePath=Root+"/Sink/sink_diffuse.png";
            AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport);
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        }
        EditorUtility.SetDirty(material);return material;
    }

    public static GameObject Place(Transform parent,string key,string name,Vector3 position,float yaw)
    {
        int index=Array.IndexOf(Keys,key);if(index<0)throw new ArgumentException(key);
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/"+key+"/"+Files[index]);
        if(!source)throw new InvalidOperationException("Import lobby models before placing "+key);
        var container=new GameObject(name);container.transform.SetParent(parent,false);
        var model=(GameObject)PrefabUtility.InstantiatePrefab(source,container.transform);
        model.name=key+" source model";
        var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/"+key+"/"+key+"_URP.mat");
        foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            int slots=Mathf.Max(1,renderer.sharedMaterials.Length);var materials=new Material[slots];
            for(int i=0;i<slots;i++)materials[i]=material;renderer.sharedMaterials=materials;
        }
        // Exported FBXs have a grounded pivot; bounds alignment also covers import-axis conversion.
        var bounds=WorldBounds(container);
        model.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
        bounds=WorldBounds(container);
        var collider=container.AddComponent<BoxCollider>();collider.center=bounds.center;collider.size=bounds.size;
        container.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
        return container;
    }

    public static Bounds WorldBounds(GameObject obj)
    {
        var renderers=obj.GetComponentsInChildren<Renderer>(true);
        if(renderers.Length==0)throw new InvalidOperationException("Model has no visible mesh: "+obj.name);
        var bounds=renderers[0].bounds;
        for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
