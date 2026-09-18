using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
// Scoped importer for the uncompressed, material-only GLB delivered with this map.
// The GLB remains the portable source; all Unity assets are saved through Editor APIs.

public static class DaldongneWarmImport
{
    public const string Folder = "Assets/Art/Daldongne/WarmVillage";
    public const string Source = Folder + "/DaldongneWarmTown.glb";
    public const string Bundle = Folder + "/DaldongneWarmMeshes.asset";
    public const string Prefab = Folder + "/DaldongneWarmTown.prefab";
    public const string ScenePath = "Assets/Scenes/DaldongneWarmMap.unity";
    static JObject doc;
    static byte[] binary;
    static int colliderCount;
    static HashSet<string> rampKeys;

    public static object Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before importing the map.");
        // Never overwrite an earlier revision or save an unrelated dirty scene.
        foreach (string asset in new [] {Prefab, Bundle, ScenePath})
            if (File.Exists(asset) || AssetDatabase.LoadMainAssetAtPath(asset) != null)
                throw new InvalidOperationException("Warm village output already exists: " + asset);
        DaldongneWarmPolish.ValidatePipeline();
        LoadGlb();
        EnsureFolder(Folder); EnsureFolder("Assets/Scenes");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("No compatible Lit shader is available.");
        var materialTokens = doc["materials"] as JArray ?? new JArray();
        var materials = materialTokens.Count > 0 ? materialTokens.Select(token => MakeMaterial(token, shader)).ToArray() : new [] {new Material(shader) {name="WarmVillageDefault"}};
        rampKeys = new HashSet<string>(((JArray)doc["nodes"]).Select(n => (string)n["name"] ?? "")
            .Where(n => n.StartsWith("Collider_Ramp_", StringComparison.Ordinal))
            .Select(n => BaseName(n.Substring("Collider_Ramp_".Length))), StringComparer.Ordinal);
        var meshes = new List<Mesh>();
        var slots = new List<int[]>();
        foreach (var token in doc["meshes"])
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>();
            var indices = new List<int[]>(); var materialSlots = new List<int>();
            foreach (var primitive in token["primitives"])
            {
                if ((int?)primitive["mode"] != null && (int)primitive["mode"] != 4)
                    throw new InvalidDataException("Only triangle primitives are supported by this scoped importer.");
                int first = positions.Count;
                var p = ReadVectors((int)primitive["attributes"]["POSITION"]);
                positions.AddRange(p);
                int? normalId = (int?)primitive["attributes"]["NORMAL"];
                normals.AddRange(normalId.HasValue ? ReadVectors(normalId.Value) : new Vector3[p.Length]);
                int[] triangles = primitive["indices"] != null ? ReadIndices((int)primitive["indices"]) : Enumerable.Range(0,p.Length).ToArray();
                for (int i=0; i<triangles.Length; i+=3)
                {
                    int b = triangles[i+1]; triangles[i+1] = triangles[i+2]; triangles[i+2] = b;
                    for (int j=0;j<3;j++)
                    {
                        if (triangles[i+j]<0 || triangles[i+j]>=p.Length) throw new InvalidDataException("Triangle index is out of bounds.");
                        triangles[i+j] += first;
                    }
                }
                int materialId=(int?)primitive["material"] ?? 0;
                if(materialId<0 || materialId>=materials.Length)throw new InvalidDataException("Invalid primitive material index.");
                indices.Add(triangles); materialSlots.Add(materialId);
            }
            var mesh = new Mesh {name=(string)token["name"] ?? "DaldongneMesh", indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.subMeshCount=indices.Count;
            for(int i=0;i<indices.Count;i++) mesh.SetTriangles(indices[i],i);
            if (normals.All(n=>n.sqrMagnitude<.001f)) mesh.RecalculateNormals();
            mesh.RecalculateBounds(); meshes.Add(mesh); slots.Add(materialSlots.ToArray());
        }
        if(meshes.Count==0)throw new InvalidDataException("The warm map contains no meshes.");
        AssetDatabase.CreateAsset(meshes[0],Bundle);
        foreach(var mesh in meshes.Skip(1)) AssetDatabase.AddObjectToAsset(mesh,Bundle);
        foreach(var mat in materials) AssetDatabase.AddObjectToAsset(mat,Bundle);
        AssetDatabase.SaveAssetIfDirty(meshes[0]);

        // A clean existing scene can be switched without changing or saving its contents.
        bool anyDirty=Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, anyDirty ? NewSceneMode.Additive : NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var root=new GameObject("Daldongne Warm Village");
        var nodes=((JArray)doc["nodes"]).Select(n=>new GameObject((string)n["name"] ?? "MapPart")).ToArray();
        colliderCount=0;
        for(int i=0;i<nodes.Length;i++)
        {
            var token=doc["nodes"][i]; var go=nodes[i]; go.transform.SetParent(root.transform,false);
            SetTransform(go.transform,token);
            int? meshId=(int?)token["mesh"];
            if(meshId.HasValue)
            {
                var m=meshes[meshId.Value]; go.AddComponent<MeshFilter>().sharedMesh=m;
                var renderer=go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials=slots[meshId.Value].Select(j=>materials[j]).ToArray();
                go.isStatic=true;
                if (NeedsCollision(go.name))
                {
                    var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=m;
                    collider.convex=false;colliderCount++;
                }
                if(IsHiddenCollision(go.name))
                {
                    renderer.enabled=false;
                    renderer.shadowCastingMode=ShadowCastingMode.Off;
                    renderer.receiveShadows=false;
                }
                else if(renderer.sharedMaterials.All(mat=>mat.GetFloat("_Surface")>.5f))
                    renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
        }
        for(int i=0;i<nodes.Length;i++)
            if(doc["nodes"][i]["children"] is JArray children)
                foreach(var child in children) nodes[(int)child].transform.SetParent(nodes[i].transform,false);
        // Exported camera/light nodes carry no mesh; use native Unity lighting below.
        var prefab=PrefabUtility.SaveAsPrefabAssetAndConnect(root,Prefab,InteractionMode.AutomatedAction);
        var camera=CreateLightingAndCamera();
        var polish=DaldongneWarmPolish.Configure(scene,camera,root);
        var spawn=new GameObject("Player Spawn - Station Forecourt");spawn.transform.position=new Vector3(-17,1.2f,-27);
        var citySpawn=new GameObject("Destination - City Hall");citySpawn.transform.position=new Vector3(7,6.2f,3);
        EditorSceneManager.SaveScene(scene,ScenePath);
        AssetDatabase.SaveAssetIfDirty(meshes[0]);
        Selection.activeGameObject=root;
        var view=SceneView.lastActiveSceneView;
        if(view!=null) view.LookAt(new Vector3(0,6,2),Quaternion.Euler(36,-33,0),48,true,true);
        var report=new {scene=ScenePath,prefab=Prefab,nodes=nodes.Length,meshes=meshes.Count,materials=materials.Length,colliders=colliderCount,rampKeys=rampKeys.OrderBy(k=>k).ToArray(),hiddenCollisionMeshes=nodes.Count(n=>IsHiddenCollision(n.name)),transparentMaterials=materials.Count(m=>m.GetFloat("_Surface")>.5f),triangles=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3),preservedDirtyScene=anyDirty,postprocess=polish};
        string reportPath=Path.GetFullPath(Path.Combine(Application.dataPath,"../../ArtSource/Daldongne/unity_warm_validation.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath,Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }

    static void EnsureFolder(string folder)
    {
        var parts=folder.Split('/');string current=parts[0];
        for(int i=1;i<parts.Length;i++)
        {
            string next=current+"/"+parts[i];
            if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,parts[i]);
            current=next;
        }
    }

    static void LoadGlb()
    {
        doc=null;binary=null;
        byte[] bytes=File.ReadAllBytes(Source);
        if(bytes.Length<20 || BitConverter.ToUInt32(bytes,0)!=0x46546C67 || BitConverter.ToUInt32(bytes,4)!=2 || BitConverter.ToUInt32(bytes,8)!=bytes.Length)
            throw new InvalidDataException("Invalid GLB 2 header.");
        for(int offset=12;offset<bytes.Length;)
        {
            if(offset+8>bytes.Length)throw new InvalidDataException("Truncated GLB chunk header.");
            int size=checked((int)BitConverter.ToUInt32(bytes,offset));uint type=BitConverter.ToUInt32(bytes,offset+4);offset+=8;
            if(size<0 || offset+size>bytes.Length) throw new InvalidDataException("GLB chunk outside file.");
            if(type==0x4E4F534A) doc=JObject.Parse(Encoding.UTF8.GetString(bytes,offset,size));
            if(type==0x004E4942) {binary=new byte[size];Array.Copy(bytes,offset,binary,0,size);}
            offset+=size;
        }
        if(doc==null || binary==null) throw new InvalidDataException("GLB is missing JSON or binary data.");
        if(!(doc["nodes"] is JArray) || !(doc["meshes"] is JArray))throw new InvalidDataException("GLB requires node and mesh arrays.");
        if(doc["textures"] is JArray textures && textures.Count>0) throw new InvalidDataException("This map importer supports material colors; use a general glTF importer for textured files.");
    }
    static int Address(int accessorId,int index,int elementBytes)
    {
        var a=doc["accessors"][accessorId];var view=doc["bufferViews"][(int)a["bufferView"]];
        int stride=(int?)view["byteStride"] ?? elementBytes;
        int offset=((int?)view["byteOffset"]??0)+((int?)a["byteOffset"]??0)+index*stride;
        if(offset<0 || offset+elementBytes>binary.Length) throw new InvalidDataException("Accessor outside GLB buffer.");
        return offset;
    }
    static Vector3[] ReadVectors(int id)
    {
        var a=doc["accessors"][id];
        if((int)a["componentType"]!=5126 || (string)a["type"]!="VEC3") throw new InvalidDataException("Expected float VEC3 accessor.");
        var array=new Vector3[(int)a["count"]];
        for(int i=0;i<array.Length;i++)
        {
            int p=Address(id,i,12);array[i]=new Vector3(BitConverter.ToSingle(binary,p),BitConverter.ToSingle(binary,p+4),-BitConverter.ToSingle(binary,p+8));
            if(!float.IsFinite(array[i].x)||!float.IsFinite(array[i].y)||!float.IsFinite(array[i].z)) throw new InvalidDataException("Non-finite vertex.");
        }
        return array;
    }
    static int[] ReadIndices(int id)
    {
        var a=doc["accessors"][id];int type=(int)a["componentType"];int size=type==5121?1:type==5123?2:type==5125?4:0;
        if(size==0) throw new InvalidDataException("Unsupported index type.");
        var array=new int[(int)a["count"]];
        if(array.Length%3!=0) throw new InvalidDataException("Triangle index count must be divisible by three.");
        for(int i=0;i<array.Length;i++){int p=Address(id,i,size);array[i]=size==1?binary[p]:size==2?BitConverter.ToUInt16(binary,p):checked((int)BitConverter.ToUInt32(binary,p));}
        return array;
    }
    static Color ColorValue(JToken value,Color fallback)
    {
        if(!(value is JArray a)||a.Count<3)return fallback;
        return new Color((float)a[0],(float)a[1],(float)a[2],a.Count>3?(float)a[3]:1);
    }
    static Material MakeMaterial(JToken token,Shader shader)
    {
        var m=new Material(shader){name=(string)token["name"]??"TownMaterial",enableInstancing=true};
        var pbr=token["pbrMetallicRoughness"];
        // glTF factors are linear; Unity's color properties use authoring-space colors.
        var linearColor=ColorValue(pbr?["baseColorFactor"],Color.white);
        var color=linearColor.gamma;color.a=linearColor.a;
        m.SetColor("_BaseColor",color);m.SetColor("_Color",color);
        m.SetFloat("_Metallic",(float?)pbr?["metallicFactor"]??0);
        m.SetFloat("_Smoothness",1-((float?)pbr?["roughnessFactor"]??.85f));
        var emission=ColorValue(token["emissiveFactor"],Color.black);
        float strength=(float?)token["extensions"]?["KHR_materials_emissive_strength"]?["emissiveStrength"]??1;
        if(emission.maxColorComponent>0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",emission*strength);}
        if((bool?)token["doubleSided"]==true)m.SetFloat("_Cull",0);
        string alphaMode=(string)token["alphaMode"]??"OPAQUE";
        bool transparent=alphaMode!="MASK" && (alphaMode=="BLEND" || color.a<.999f);
        if(transparent)
        {
            m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);
            m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);m.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite",0);m.SetFloat("_AlphaClip",0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType","Transparent");m.renderQueue=(int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster",false);
        }
        else if(alphaMode=="MASK")
        {
            m.SetFloat("_AlphaClip",1);m.SetFloat("_Cutoff",(float?)token["alphaCutoff"]??.5f);
            m.EnableKeyword("_ALPHATEST_ON");m.SetOverrideTag("RenderType","TransparentCutout");
            m.renderQueue=(int)RenderQueue.AlphaTest;
        }
        else
        {
            m.SetFloat("_Surface",0);m.SetFloat("_ZWrite",1);
            m.SetOverrideTag("RenderType","Opaque");m.renderQueue=(int)RenderQueue.Geometry;
        }
        return m;
    }
    static Vector3 Vec(JToken a,Vector3 fallback) => a is JArray v&&v.Count==3?new Vector3((float)v[0],(float)v[1],(float)v[2]):fallback;
    static void SetTransform(Transform t,JToken n)
    {
        if(n["matrix"] is JArray matrix)
        {
            Matrix4x4 m=Matrix4x4.zero;
            for(int i=0;i<16;i++)m[i]=(float)matrix[i];
            var mirror=Matrix4x4.Scale(new Vector3(1,1,-1));m=mirror*m*mirror;
            t.localPosition=m.GetColumn(3);t.localRotation=m.rotation;t.localScale=m.lossyScale;return;
        }
        Vector3 p=Vec(n["translation"],Vector3.zero);t.localPosition=new Vector3(p.x,p.y,-p.z);
        t.localScale=Vec(n["scale"],Vector3.one);
        if(n["rotation"] is JArray q)t.localRotation=new Quaternion(-(float)q[0],-(float)q[1],(float)q[2],(float)q[3]);
    }
    static string BaseName(string n)
    {
        int dot=n.LastIndexOf('.');
        return dot>=0 && int.TryParse(n.Substring(dot+1),out _) ? n.Substring(0,dot) : n;
    }
    static bool IsHiddenCollision(string n) => n.StartsWith("Collider_",StringComparison.Ordinal) || n.StartsWith("Collision_",StringComparison.Ordinal);
    static bool NeedsCollision(string name)
    {
        string n=BaseName(name);
        if(IsHiddenCollision(n))return true;
        if(n.StartsWith("Walk_Stair_",StringComparison.Ordinal))
        {
            string key=n.Substring("Walk_Stair_".Length);
            return !rampKeys.Any(r=>key==r || key.StartsWith(r+"_",StringComparison.Ordinal));
        }
        // Large structural surfaces are solid. Trim, flowers, lamps, NPCs,
        // signage, bread, furniture, cars and veneer tiles stay decorative.
        return n.StartsWith("Terrain_",StringComparison.Ordinal) || n.StartsWith("Walk_",StringComparison.Ordinal)
            || n.StartsWith("Wall_",StringComparison.Ordinal) || n.StartsWith("WallCap_",StringComparison.Ordinal)
            || n=="Road_Main" || n=="Road_Exit" || n.StartsWith("Building_",StringComparison.Ordinal)
            || n.EndsWith("_stucco",StringComparison.Ordinal) || n.EndsWith("_walls",StringComparison.Ordinal)
            || n.StartsWith("Station_EntrySidewall",StringComparison.Ordinal)
            || n.StartsWith("Station_TunnelDark",StringComparison.Ordinal)
            || n.StartsWith("Support_Stair_",StringComparison.Ordinal) || n.StartsWith("Bridge_Pier",StringComparison.Ordinal)
            || n.IndexOf("_foundation",StringComparison.OrdinalIgnoreCase)>=0
            || n.EndsWith("_BasePlinth",StringComparison.Ordinal) || n.EndsWith("_StoneBase",StringComparison.Ordinal)
            || n=="CityHall_PorticoFloor";
    }
    static Camera CreateLightingAndCamera()
    {
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.60f,.69f,.79f);
        RenderSettings.ambientEquatorColor=new Color(.48f,.51f,.54f);
        RenderSettings.ambientGroundColor=new Color(.30f,.32f,.28f);
        RenderSettings.fog=false;
        var sun=new GameObject("Warm Village Evening Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.55f;sun.color=new Color(1,.85f,.68f);sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(42,-35,0);RenderSettings.sun=sun;
        var camera=new GameObject("Daldongne Warm Map Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.orthographic=true;camera.orthographicSize=42;camera.farClipPlane=500;camera.nearClipPlane=.1f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.43f,.47f,.48f);
        camera.transform.position=new Vector3(47,63,-72);camera.transform.LookAt(new Vector3(0,6,2));
        // No map audio is authored, so an additive import does not create a second listener.
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("CompanyGame.Daldongne.DaldongneMapCamera")).FirstOrDefault(t=>t!=null);
        if(type!=null)camera.gameObject.AddComponent(type);
        return camera;
    }
}
// Scene-scoped evening lighting and restrained URP grading for the warm map.
// Does not modify the project's render-pipeline asset or unrelated open scenes.

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
        if(scene.path!="Assets/Scenes/DaldongneWarmMap.unity")
            throw new InvalidOperationException("Open DaldongneWarmMap.unity before applying its polish.");
        var roots=scene.GetRootGameObjects();
        var root=roots.FirstOrDefault(go=>go.name=="Daldongne Warm Village");
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

        var volumeObject=scene.GetRootGameObjects().FirstOrDefault(go=>go.name==VolumeName);
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
        RenderSettings.ambientSkyColor=new Color(.61f,.67f,.72f);
        RenderSettings.ambientEquatorColor=new Color(.51f,.49f,.44f);
        RenderSettings.ambientGroundColor=new Color(.32f,.31f,.25f);
        RenderSettings.fog=false;
        var sun=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Light>(true))
            .FirstOrDefault(light=>light.name=="Warm Village Evening Sun");
        if(sun!=null)
        {
            sun.color=new Color(1,.85f,.68f);sun.intensity=1.55f;
            sun.shadows=LightShadows.Soft;sun.shadowStrength=.80f;
            sun.transform.rotation=Quaternion.Euler(42,-35,0);RenderSettings.sun=sun;
        }

        var lightRoot=scene.GetRootGameObjects().FirstOrDefault(go=>go.name==LightsName);
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
