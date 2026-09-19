// Scene-scoped evening lighting and restrained URP grading for the warm map.
// Does not modify the project's render-pipeline asset or unrelated open scenes.
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class DaldongneWarmPolish
{
    public const string ProfilePath="Assets/Art/Daldongne/WarmVillage/WarmVillageVolume.asset";
    const string VolumeName="Warm Village Evening Grade";
    const string LightsName="Warm Village Practical Lights";
    const int VolumeLayer=31;

    public static void ValidatePipeline()
    {
        var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if(pipeline==null)throw new InvalidOperationException("The warm map requires the project's existing URP pipeline.");
        if(!pipeline.supportsHDR)throw new InvalidOperationException("Enable HDR in the active URP asset before importing the warm map.");
        var serialized=new SerializedObject(pipeline);
        var entries=serialized.FindProperty("m_RendererDataList");
        var index=serialized.FindProperty("m_DefaultRendererIndex");
        if(entries==null || index==null || index.intValue>=entries.arraySize)
            throw new InvalidOperationException("URP has no configured default renderer.");
        var renderer=entries.GetArrayElementAtIndex(index.intValue).objectReferenceValue as UniversalRendererData;
        if(renderer==null || renderer.postProcessData==null)
            throw new InvalidOperationException("The default URP renderer requires its Post Process Data resource.");
    }

    public static object Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play mode before polishing the warm map.");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/daldongnaemap.unity")
            throw new InvalidOperationException("Open daldongnaemap.unity before applying its polish.");
        var roots=scene.GetRootGameObjects();
        var root=roots.SelectMany(go=>go.GetComponentsInChildren<Transform>(true))
            .Select(t=>t.gameObject).FirstOrDefault(go=>go.name=="Daldongne Warm Village");
        var camera=roots.SelectMany(go=>go.GetComponentsInChildren<Camera>(true))
            .FirstOrDefault(c=>c.name=="Daldongne Warm Map Camera");
        if(root==null || camera==null)throw new InvalidOperationException("The warm-map root or camera is missing.");
        var report=Configure(scene,camera,root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return report;
    }

    public static object Configure(Scene scene,Camera camera,GameObject mapRoot)
    {
        ValidatePipeline();
        if(camera==null || camera.gameObject.scene!=scene || mapRoot.scene!=scene)
            throw new InvalidOperationException("Polish must target the newly imported scene explicitly.");
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if(profile==null)
        {
            profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="Warm Village Evening";
            AssetDatabase.CreateAsset(profile,ProfilePath);
        }
        var bloom=GetOrAdd<Bloom>(profile);
        bloom.threshold.Override(1.12f);bloom.intensity.Override(.20f);
        bloom.scatter.Override(.48f);bloom.clamp.Override(5.0f);
        bloom.highQualityFiltering.Override(true);
        bloom.tint.Override(new Color(1f,.95f,.85f,1));
        var tonemapping=GetOrAdd<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.Neutral);
        var colors=GetOrAdd<ColorAdjustments>(profile);
        colors.postExposure.Override(-.08f);colors.contrast.Override(4f);
        colors.saturation.Override(-4f);
        var whiteBalance=GetOrAdd<WhiteBalance>(profile);
        whiteBalance.temperature.Override(6f);whiteBalance.tint.Override(1f);
        EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);

        var volumeObject=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Transform>(true))
            .Select(t=>t.gameObject).FirstOrDefault(go=>go.name==VolumeName);
        if(volumeObject==null)
        {
            volumeObject=new GameObject(VolumeName);
            SceneManager.MoveGameObjectToScene(volumeObject,scene);
        }
        volumeObject.layer=VolumeLayer;
        var volume=volumeObject.GetComponent<Volume>()??volumeObject.AddComponent<Volume>();
        volume.enabled=true;volume.isGlobal=true;volume.priority=10;volume.weight=1;
        volume.sharedProfile=profile;
        var data=camera.GetUniversalAdditionalCameraData();
        camera.allowHDR=true;data.renderShadows=true;data.renderPostProcessing=true;
        data.volumeLayerMask=1<<VolumeLayer;data.volumeTrigger=camera.transform;
        data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality=AntialiasingQuality.High;

        // Existing scene-owned sun and ambient state only; no quality/pipeline mutation.
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.42f,.50f,.60f);
        RenderSettings.ambientEquatorColor=new Color(.35f,.32f,.29f);
        RenderSettings.ambientGroundColor=new Color(.22f,.20f,.16f);
        RenderSettings.fog=false;
        var sun=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Light>(true))
            .FirstOrDefault(light=>light.name=="Warm Village Evening Sun");
        if(sun!=null)
        {
            sun.color=new Color(1,.85f,.68f);sun.intensity=1.55f;
            sun.shadows=LightShadows.Soft;sun.shadowStrength=.80f;
            sun.transform.rotation=Quaternion.Euler(42,-35,0);RenderSettings.sun=sun;
        }

        var lightRoot=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Transform>(true))
            .Select(t=>t.gameObject).FirstOrDefault(go=>go.name==LightsName);
        if(lightRoot==null)
        {
            lightRoot=new GameObject(LightsName);SceneManager.MoveGameObjectToScene(lightRoot,scene);
        }
        // Idempotent: only rebuild this script's own native light objects.
        foreach(Transform child in lightRoot.transform.Cast<Transform>().ToArray())
            UnityEngine.Object.DestroyImmediate(child.gameObject);
        var sources=mapRoot.GetComponentsInChildren<Transform>()
            .Where(t=>t.name.Contains("lantern_glow") || t.name.Contains("Lantern_glow"))
            .OrderBy(t=>t.name,StringComparer.Ordinal).ToArray();
        int spacing=Math.Max(1,(int)Math.Ceiling(sources.Length/24.0));
        int pointLights=0;
        for(int i=0;i<sources.Length;i+=spacing)
        {
            var lightObject=new GameObject("Warm light - "+sources[i].name);
            lightObject.transform.SetParent(lightRoot.transform,false);
            lightObject.transform.position=sources[i].position;
            var light=lightObject.AddComponent<Light>();light.type=LightType.Point;
            light.color=new Color(1,.76f,.47f);light.intensity=1.10f;
            light.range=4.5f;light.shadows=LightShadows.None;
            pointLights++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        return new {profile=ProfilePath,bloomIntensity=bloom.intensity.value,bloomThreshold=bloom.threshold.value,
            tonemapping="Neutral",temperature=whiteBalance.temperature.value,pointLights,
            camera=camera.name,postProcessing=data.renderPostProcessing,volumeLayer=VolumeLayer,
            pipelineChanged=false};
    }

    static T GetOrAdd<T>(VolumeProfile profile) where T:VolumeComponent
    {
        if(!profile.TryGet<T>(out var component))
        {
            component=profile.Add<T>(false);
            AssetDatabase.AddObjectToAsset(component,profile);
        }
        component.active=true;EditorUtility.SetDirty(component);
        return component;
    }
}
