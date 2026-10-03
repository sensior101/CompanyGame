using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using static CompanyGame.Editor.WorldMaps.DistrictGeometry;

namespace CompanyGame.Editor.WorldMaps
{
    public static class DistrictPresentation
    {
        public static object Apply()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit mode required");Init();var scene=SceneManager.GetActiveScene();var root=scene.GetRootGameObjects().Single(g=>g.name.StartsWith("Map_"));
            int upgraded=UpgradeWorldSigns(root);bool night=scene.name=="NightlifeDistrict";
            if(scene.name!="daldongnaemap")DetailDistrict(root,scene.name);
            if(night)
            {
                if(!UniversalRenderPipeline.asset)throw new Exception("URP is required");
                Folder(AssetsRoot+"/Profiles");string path=AssetsRoot+"/Profiles/NeonNight.asset";var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                if(!profile){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,path);}
                if(!profile.TryGet<Bloom>(out var bloom)){bloom=profile.Add<Bloom>();AssetDatabase.AddObjectToAsset(bloom,profile);}bloom.threshold.Override(.8f);bloom.intensity.Override(1.1f);bloom.scatter.Override(.62f);bloom.highQualityFiltering.Override(true);
                if(!profile.TryGet<Tonemapping>(out var tone)){tone=profile.Add<Tonemapping>();AssetDatabase.AddObjectToAsset(tone,profile);}tone.mode.Override(TonemappingMode.Neutral);
                if(!profile.TryGet<ColorAdjustments>(out var color)){color=profile.Add<ColorAdjustments>();AssetDatabase.AddObjectToAsset(color,profile);}color.postExposure.Override(.35f);color.saturation.Override(8);
                var parent=root.transform.Find("30_Presentation");var go=parent.Find("Neon Night Volume");if(!go)go=Group(parent,"Neon Night Volume");go.gameObject.layer=0;var volume=go.GetComponent<Volume>()??go.gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=profile;volume.priority=10;volume.weight=1;
                var camera=root.GetComponentInChildren<Camera>();camera.allowHDR=true;var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.volumeLayerMask=1;
                foreach(var name in new[]{"pink","cyan","warmLight"}){var m=M(name);m.SetColor("_EmissionColor",m.GetColor("_BaseColor")*(name=="warmLight"?2.3f:5));m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;MaterialEditor.FixupEmissiveFlag(m);m.EnableKeyword("_EMISSION");EditorUtility.SetDirty(m);}
                EditorUtility.SetDirty(profile);EditorUtility.SetDirty(bloom);EditorUtility.SetDirty(tone);EditorUtility.SetDirty(color);
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return new{scene=scene.name,upgraded,neonPostProcessing=night};
        }
        static void DetailDistrict(GameObject root,string name)
        {
            var previous=root.transform.Find("10_World/Finishing Details");if(previous)Object.DestroyImmediate(previous.gameObject);
            var parent=Group(root.transform.Find("10_World"),"Finishing Details");var b=new Batch(parent,name+"_UrbanDetail");
            var buildings=root.transform.Find("10_World/Buildings").Cast<Transform>().Where(t=>!t.name.Contains("VacantPlot")).ToArray();
            foreach(var building in buildings)
            {
                var solid=building.GetComponentsInChildren<BoxCollider>().FirstOrDefault();if(!solid)continue;var box=solid.bounds;float w=box.size.x,d=box.size.z,h=box.max.y;var p=V(box.center.x,0,box.center.z);
                bool civic=name=="CivicDistrict"||building.name.Contains("Mall");bool tower=building.name.Contains("Tower")||building.name.Contains("Apartment")||building.name.Contains("Company")||building.name.Contains("Construction");
                if(civic)
                {
                    for(float y=1.6f;y<h;y+=3.3f)
                    {
                        foreach(int s in new[]{-1,1})for(float z=-d/2+1.7f;z<d/2;z+=2.7f){b.Box(p+V(s*(w/2+.06f),y,z),V(.16f,2.25f,1.85f),"white");b.Box(p+V(s*(w/2+.15f),y,z),V(.06f,2.03f,1.65f),"glassLight");}
                        for(float x=-w/2+1.5f;x<w/2;x+=2.7f){b.Box(p+V(x,y,d/2+.06f),V(1.9f,2.2f,.16f),"white");b.Box(p+V(x,y,d/2+.15f),V(1.7f,2,.06f),"glass");}
                    }
                    foreach(int s in new[]{-1,1}){b.Box(p+V(s*(w/2-.2f),h+.7f,0),V(.25f,.75f,d),"white");b.Box(p+V(0,h+.7f,s*(d/2-.2f)),V(w,.75f,.25f),"white");}
                    for(int k=-1;k<=1;k++){b.Box(p+V(k*4,h+.7f,2),V(2.8f,.8f,2.8f),"metal");b.Box(p+V(k*4,h+1.2f,2),V(2.5f,.25f,2.5f),"white");b.Box(p+V(k*4,h+.65f,-3),V(3,.15f,2.2f),"glassLight",false,Quaternion.Euler(15,0,0));}
                    foreach(int s in new[]{-1,1}){b.Cylinder(p+V(s*(w/2-1),2.5f,-d/2-2),.05f,5,"metal");b.Box(p+V(s*(w/2-1)+.65f,4.45f,-d/2-2),V(1.25f,.75f,.02f),s<0?"white":"blue");}
                    for(int k=-2;k<=2;k++){if(k==0)continue;b.Box(p+V(k*3,.17f,-d/2-3.2f),V(2.6f,.34f,.8f),"stone");b.Facet(p+V(k*3,.65f,-d/2-3.2f),V(2.4f,.8f,.75f),"leaf");}
                    // Leave the centre of the entrance clear of planters.
                    b.Box(p+V(0,.045f,-d/2-3.8f),V(3,.06f,4),"stone");
                }
                if(tower)
                {
                    b.Box(p+V(0,h+1.45f,0),V(w*.72f,2.4f,d*.65f),"glassLight");b.Box(p+V(0,h+2.7f,0),V(w*.76f,.18f,d*.7f),"metal");
                    for(int k=-1;k<=1;k++)b.Box(p+V(k*2.1f,h+3.2f,1),V(1.5f,.8f,2.1f),"metal");
                    foreach(int s in new[]{-1,1}){foreach(int end in new[]{-1,1})b.Box(p+V(s*(w/2+.18f),h/2,end*d/2),V(.18f,h,.22f),"white");b.Box(p+V(s*(w/2+.32f),h/2,-d/2-.1f),V(.12f,h,.12f),"gold");}
                    if(building.name.Contains("Handae")){b.Box(p+V(0,h+6,0),V(w*.42f,7,d*.38f),"glass");b.Box(p+V(0,h+9.6f,0),V(w*.47f,.3f,d*.43f),"gold");b.Cylinder(p+V(0,h+13,0),.10f,6.5f,"metal");}
                }
            }
            // Curbs and inset paving joints distinguish sidewalks from the road.
            bool business=name=="BusinessDistrict",coast=name=="CoastalResidentialDistrict",luxury=name=="LuxuryResidentialDistrict";
            Rect entrance=TransitMapBuilder.Excavation(Array.IndexOf(CityDistrictBuilder.Names,name));
            var zs=business?new[]{-38f,-16,16,38}:coast?new[]{-30f,0,36}:new[]{-36f,0,36};
            foreach(float x in new[]{-40f,0,40})for(float z=coast?-30:-70;z<=70;z+=2)
            {
                if(zs.Any(c=>Mathf.Abs(z-c)<7)||(business&&Mathf.Abs(z)<12)||(luxury&&x==40&&z>-40&&z<4))continue;
                foreach(int s in new[]{-1,1}){b.Box(V(x+s*4.3f,.06f,z),V(.3f,.12f,1.94f),"stone");PaverJoint(b,V(x+s*5.9f,.008f,z),V(2.6f,.012f,.035f),entrance);}
            }
            foreach(float z in zs)for(float x=-70;x<=70;x+=2)
            {if(new[]{-40f,0,40}.Any(c=>Mathf.Abs(x-c)<7))continue;foreach(int s in new[]{-1,1}){b.Box(V(x,.06f,z+s*4.3f),V(1.94f,.12f,.3f),"stone");PaverJoint(b,V(x,.008f,z+s*5.9f),V(.035f,.012f,2.6f),entrance);}}
            b.Finish();
            if(name!="NightlifeDistrict")
            {
                string path=AssetsRoot+"/Materials/DistrictSky.mat";var sky=AssetDatabase.LoadAssetAtPath<Material>(path);if(!sky){sky=new Material(Shader.Find("Skybox/Procedural")){name="District sky"};sky.SetFloat("_Exposure",.8f);AssetDatabase.CreateAsset(sky,path);}RenderSettings.skybox=sky;
                var probeObject=Group(parent,"District Reflection");probeObject.position=V(0,18,0);var probe=probeObject.gameObject.AddComponent<ReflectionProbe>();probe.mode=ReflectionProbeMode.Realtime;probe.refreshMode=ReflectionProbeRefreshMode.OnAwake;probe.timeSlicingMode=ReflectionProbeTimeSlicingMode.IndividualFaces;probe.resolution=128;probe.size=V(160,160,160);probe.boxProjection=true;probe.intensity=.8f;probe.clearFlags=ReflectionProbeClearFlags.Skybox;probe.RenderProbe();
            }
        }
        static void PaverJoint(Batch b,Vector3 p,Vector3 size,Rect entrance)
        {
            if(!entrance.Overlaps(new Rect(p.x-size.x/2,p.z-size.z/2,size.x,size.z)))b.Box(p,size,"concrete");
        }
    }
}
