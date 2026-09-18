using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor.SceneManagement;

public static class DaldongnePolish
{
    public static object Apply()
    {
        var bytes=File.ReadAllBytes("Assets/Art/Daldongne/DaldongneTown.glb");
        int length=BitConverter.ToInt32(bytes,12);
        var doc=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes,20,length));
        var materials=AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Daldongne/DaldongneMeshes.asset").OfType<Material>().ToDictionary(m=>m.name);
        foreach(var token in doc["materials"])
        {
            if(!materials.TryGetValue((string)token["name"],out var material))continue;
            var c=token["pbrMetallicRoughness"]?["baseColorFactor"];
            if(c==null)continue;
            var color=new Color((float)c[0],(float)c[1],(float)c[2],(float)c[3]).gamma;
            material.SetColor("_BaseColor",color);material.SetColor("_Color",color);
            EditorUtility.SetDirty(material);
        }
        const string assetPath="Assets/Art/Daldongne/Daldongne_RenderPipeline.asset";
        var active=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath)==null)
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(active),assetPath);
        var profile=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
        profile.shadowDistance=160;profile.msaaSampleCount=4;
        EditorUtility.SetDirty(profile);
        QualitySettings.renderPipeline=profile;
        // Additional camera data retains camera-side shadow and antialiasing settings in the scene.
        var camera=Camera.main;
        var data=camera.GetUniversalAdditionalCameraData();data.renderShadows=true;
        data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality=AntialiasingQuality.High;
        EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
        EditorSceneManager.SaveScene(camera.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new {pipeline=assetPath,shadowDistance=profile.shadowDistance,msaa=profile.msaaSampleCount,materials=materials.Count};
    }
}
