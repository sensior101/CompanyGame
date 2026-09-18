// Scoped importer for the uncompressed, material-only GLB delivered with this map.
// The GLB remains the portable source; all Unity assets are saved through Editor APIs.
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

public static class DaldongneImport
{
    const string Folder = "Assets/Art/Daldongne";
    const string Source = Folder + "/DaldongneTown.glb";
    const string Bundle = Folder + "/DaldongneMeshes.asset";
    const string Prefab = Folder + "/DaldongneTown.prefab";
    const string ScenePath = "Assets/Scenes/DaldongneMap.unity";
    static JObject doc;
    static byte[] binary;
    static int colliderCount;

    public static object Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before importing the map.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Prefab) != null)
            throw new InvalidOperationException("Map prefab already exists. Open Assets/Scenes/DaldongneMap.unity to use it.");
        LoadGlb();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (!shader) throw new InvalidOperationException("No compatible Lit shader is available.");
        var materials = ((JArray)doc["materials"]).Select(token => MakeMaterial(token, shader)).ToArray();
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
                indices.Add(triangles); materialSlots.Add((int?)primitive["material"] ?? 0);
            }
            var mesh = new Mesh {name=(string)token["name"] ?? "DaldongneMesh", indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.subMeshCount=indices.Count;
            for(int i=0;i<indices.Count;i++) mesh.SetTriangles(indices[i],i);
            if (normals.All(n=>n.sqrMagnitude<.001f)) mesh.RecalculateNormals();
            mesh.RecalculateBounds(); meshes.Add(mesh); slots.Add(materialSlots.ToArray());
        }
        AssetDatabase.CreateAsset(meshes[0],Bundle);
        foreach(var mesh in meshes.Skip(1)) AssetDatabase.AddObjectToAsset(mesh,Bundle);
        foreach(var mat in materials) AssetDatabase.AddObjectToAsset(mat,Bundle);
        AssetDatabase.SaveAssets();

        // A clean existing scene can be switched without changing or saving its contents.
        bool anyDirty=Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, anyDirty ? NewSceneMode.Additive : NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var root=new GameObject("Daldongne Town");
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
                if (NeedsCollision(go.name)) {go.AddComponent<MeshCollider>().sharedMesh=m;colliderCount++;}
            }
        }
        for(int i=0;i<nodes.Length;i++)
            if(doc["nodes"][i]["children"] is JArray children)
                foreach(var child in children) nodes[(int)child].transform.SetParent(nodes[i].transform,false);
        // Exported camera/light nodes carry no mesh; use native Unity lighting below.
        var prefab=PrefabUtility.SaveAsPrefabAssetAndConnect(root,Prefab,InteractionMode.AutomatedAction);
        CreateLightingAndCamera();
        var spawn=new GameObject("Player Spawn - Station Forecourt");spawn.transform.position=new Vector3(-17,1.2f,-27);
        var citySpawn=new GameObject("Destination - City Hall");citySpawn.transform.position=new Vector3(7,6.2f,3);
        EditorSceneManager.SaveScene(scene,ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject=root;
        var view=SceneView.lastActiveSceneView;
        if(view!=null) view.LookAt(new Vector3(0,6,2),Quaternion.Euler(36,-33,0),48,true,true);
        var report=new {scene=ScenePath,prefab=Prefab,nodes=nodes.Length,meshes=meshes.Count,materials=materials.Length,colliders=colliderCount,triangles=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3),preservedDirtyScene=anyDirty};
        File.WriteAllText("../ArtSource/Daldongne/unity_validation.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }

    static void LoadGlb()
    {
        byte[] bytes=File.ReadAllBytes(Source);
        if(bytes.Length<20 || BitConverter.ToUInt32(bytes,0)!=0x46546C67 || BitConverter.ToUInt32(bytes,4)!=2 || BitConverter.ToUInt32(bytes,8)!=bytes.Length)
            throw new InvalidDataException("Invalid GLB 2 header.");
        for(int offset=12;offset<bytes.Length;)
        {
            int size=checked((int)BitConverter.ToUInt32(bytes,offset));uint type=BitConverter.ToUInt32(bytes,offset+4);offset+=8;
            if(size<0 || offset+size>bytes.Length) throw new InvalidDataException("GLB chunk outside file.");
            if(type==0x4E4F534A) doc=JObject.Parse(Encoding.UTF8.GetString(bytes,offset,size));
            if(type==0x004E4942) {binary=new byte[size];Array.Copy(bytes,offset,binary,0,size);}
            offset+=size;
        }
        if(doc==null || binary==null) throw new InvalidDataException("GLB is missing JSON or binary data.");
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
        var color=ColorValue(pbr?["baseColorFactor"],Color.white).gamma;
        m.SetColor("_BaseColor",color);m.SetColor("_Color",color);
        m.SetFloat("_Metallic",(float?)pbr?["metallicFactor"]??0);
        m.SetFloat("_Smoothness",1-((float?)pbr?["roughnessFactor"]??.85f));
        var emission=ColorValue(token["emissiveFactor"],Color.black);
        float strength=(float?)token["extensions"]?["KHR_materials_emissive_strength"]?["emissiveStrength"]??1;
        if(emission.maxColorComponent>0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",emission*strength);}
        if((bool?)token["doubleSided"]==true)m.SetFloat("_Cull",0);
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
    static bool NeedsCollision(string n) => n.StartsWith("Terrain_") || n.StartsWith("Walk_") || n=="Road_Main" || n=="Road_Exit" || n.StartsWith("Building_CityHall") || n.EndsWith("_stucco") || n.EndsWith("_walls") || n.StartsWith("Station_EntrySidewall");
    static void CreateLightingAndCamera()
    {
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.60f,.69f,.79f);
        RenderSettings.ambientEquatorColor=new Color(.48f,.51f,.54f);
        RenderSettings.ambientGroundColor=new Color(.30f,.32f,.28f);
        RenderSettings.fog=false;
        var sun=new GameObject("Daldongne Evening Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.7f;sun.color=new Color(1,.84f,.65f);sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(45,-35,0);RenderSettings.sun=sun;
        var camera=new GameObject("Daldongne Map Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.orthographic=true;camera.orthographicSize=42;camera.farClipPlane=500;camera.nearClipPlane=.1f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.37f,.46f,.54f);
        camera.transform.position=new Vector3(47,63,-72);camera.transform.LookAt(new Vector3(0,6,2));
        camera.gameObject.AddComponent<AudioListener>();
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("CompanyGame.Daldongne.DaldongneMapCamera")).FirstOrDefault(t=>t!=null);
        if(type!=null)camera.gameObject.AddComponent(type);
    }
}
