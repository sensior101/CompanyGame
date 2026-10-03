using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using CompanyGame.Daldongne;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace CompanyGame.Editor.Characters
{
    /// <summary>Imports the editable Blender source into the existing rigid-joint player.</summary>
    public static class ReferenceGirlImporter
    {
        public const string Folder = "Assets/Art/Daldongne/Players/ReferenceGirl";
        public const string VisualPath = "Assets/Art/Daldongne/Players/FemaleVisual.prefab";
        static readonly string[] Scenes = {
            "Assets/Scenes/daldongnaemap.unity",
            "Assets/Scenes/Maps/DaldongnePocketGarden.unity",
            "Assets/Scenes/Templates/SmallMapTemplate.unity"
        };

        [MenuItem("Tools/Company Game/Characters/Rebuild Reference Girl")]
        public static void RebuildMenu() => Debug.Log(Rebuild());

        static Vector3 V(JToken a) => new Vector3((float)a[0], (float)a[1], (float)a[2]);
        static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before importing a character.");
            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Resolve script compilation errors first.");
            for (int i=0; i<SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save scene edits before migrating character instances.");
        }

        public static string Rebuild() => Rebuild(false);

        public static string Rebuild(bool male)
        {
            RequireEditMode();
            string folder=male?"Assets/Art/Daldongne/Players/ReferenceBoy":Folder;
            string visualPath=male?"Assets/Art/Daldongne/Players/MaleVisual.prefab":VisualPath;
            string assetName=male?"ReferenceBoy":"ReferenceGirl";
            string palettePath=folder+"/"+(male?"BoyPalette.png":"GirlPalette.png");
            var doc=JObject.Parse(File.ReadAllText(folder+"/"+assetName+".meshdata.json"));
            if ((int)doc["version"]!=1) throw new InvalidDataException("Unsupported character source version.");
            var materialTokens=(JArray)doc["materials"];
            var parts=(JArray)doc["parts"];
            var names=materialTokens.Select(m=>(string)m["name"]).ToArray();
            // Reject unsupported source geometry before changing any persisted assets.
            if(names.Length==0 || names.Length>16)
                throw new InvalidDataException("Character palette must contain between 1 and 16 entries.");
            long sourceTriangles=0;
            foreach(var part in parts)
            {
                var indices=part["triangles"] as JArray;
                if(indices==null || indices.Count%3!=0)
                    throw new InvalidDataException("Invalid source triangle list: "+part["name"]);
                sourceTriangles+=indices.Count/3;
            }
            if(sourceTriangles>20000)
                throw new InvalidDataException("Character source exceeds the 20000 triangle geometry budget.");
            // One palette texture and one material keep the nine animated renderers inexpensive.
            var palette=new Texture2D(256,16,TextureFormat.RGBA32,false);
            for(int x=0;x<256;x++)
            {
                if(!ColorUtility.TryParseHtmlString((string)materialTokens[Math.Min(x/16,names.Length-1)]["color"],out var color))
                    throw new InvalidDataException("Invalid character palette color.");
                for(int y=0;y<16;y++) palette.SetPixel(x,y,color);
            }
            palette.Apply(); File.WriteAllBytes(palettePath,palette.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(palette);
            AssetDatabase.ImportAsset(palettePath,ImportAssetOptions.ForceSynchronousImport);
            var textureImporter=(TextureImporter)AssetImporter.GetAtPath(palettePath);
            textureImporter.textureType=TextureImporterType.Default;textureImporter.sRGBTexture=true;
            textureImporter.mipmapEnabled=false;textureImporter.filterMode=FilterMode.Point;
            textureImporter.wrapMode=TextureWrapMode.Clamp;textureImporter.textureCompression=TextureImporterCompression.Uncompressed;
            textureImporter.SaveAndReimport();
            var shader=Shader.Find("Universal Render Pipeline/Lit");
            if(!shader) throw new InvalidOperationException("URP Lit shader is required.");
            string matPath=folder+"/"+assetName+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,matPath);}
            material.SetColor("_BaseColor",Color.white);
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(palettePath));
            material.SetFloat("_Smoothness",.18f);material.SetFloat("_Metallic",0);
            EditorUtility.SetDirty(material);

            var root=PrefabUtility.LoadPrefabContents(visualPath);
            try
            {
                // Preserve the root and motion component fileIDs used by existing nested prefabs.
                foreach(Transform child in root.transform.Cast<Transform>().ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                var motion=root.GetComponent<DaldongneAvatarMotion>();
                if(!motion) motion=root.AddComponent<DaldongneAvatarMotion>();
                var joints=new Dictionary<string,Transform>();
                foreach(var j in doc["joints"])
                {
                    string name=(string)j["name"], parent=(string)j["parent"];
                    var t=new GameObject(name).transform;t.SetParent(parent==null?root.transform:joints[parent],false);
                    t.localPosition=V(j["position"]);joints.Add(name,t);
                    var positions=new List<Vector3>();var normals=new List<Vector3>();
                    var uv=new List<Vector2>();var triangles=new List<int>();
                    foreach(var p in parts.Where(p=>(string)p["joint"]==name))
                    {
                        int first=positions.Count;var vs=(JArray)p["vertices"];var ns=(JArray)p["normals"];
                        int mat=Array.IndexOf(names,(string)p["material"]);
                        if(mat<0 || vs.Count!=ns.Count || vs.Count%3!=0)throw new InvalidDataException("Invalid source mesh: "+p["name"]);
                        for(int k=0;k<vs.Count;k+=3)
                        {
                            positions.Add(new Vector3((float)vs[k],(float)vs[k+1],(float)vs[k+2]));
                            normals.Add(new Vector3((float)ns[k],(float)ns[k+1],(float)ns[k+2]));
                            uv.Add(new Vector2((mat+.5f)/16f,.5f));
                        }
                        foreach(int index in p["triangles"])
                        {
                            if(index<0 || index>=vs.Count/3) throw new InvalidDataException("Triangle outside mesh.");
                            triangles.Add(first+index);
                        }
                    }
                    if(positions.Count==0)continue;
                    string meshPath=folder+"/"+name+".asset";
                    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    bool create=!mesh;
                    if(create)mesh=new Mesh();
                    else mesh.Clear();
                    mesh.name=assetName+"_"+name;
                    mesh.indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16;
                    mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(false);
                    if(create)AssetDatabase.CreateAsset(mesh,meshPath);
                    EditorUtility.SetDirty(mesh);
                    t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
                    t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
                }
                motion.hips=joints["Hips"];motion.leftArm=joints["LeftArm"];motion.rightArm=joints["RightArm"];
                motion.leftLeg=joints["LeftLeg"];motion.rightLeg=joints["RightLeg"];
                motion.leftKnee=joints["LeftKnee"];motion.rightKnee=joints["RightKnee"];
                // The source uses the same 0.767 m hip pivot as the existing motion script.
                motion.Pose(0,0);
                PrefabUtility.SaveAsPrefabAsset(root,visualPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            int migrated=ReconnectUnpackedSceneVisuals(male,visualPath);
            string report=Validate(male);
            return assetName+" imported; migrated "+migrated+" unpacked scene visuals. "+report;
        }

        static int ReconnectUnpackedSceneVisuals(bool male,string visualPath)
        {
            int count=0;var setup=EditorSceneManager.GetSceneManagerSetup();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            try
            {
                foreach(string path in Scenes)
                {
                    var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid() || !scene.isLoaded;
                    if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    bool dirty=false;
                    foreach(var look in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DaldongnePlayerAppearance>(true)))
                    {
                        var old=male?look.male:look.female;
                        if(!old)throw new InvalidOperationException("Character reference missing in "+path);
                        if(PrefabUtility.GetCorrespondingObjectFromSource(old)==prefab)continue;
                        var replacement=(GameObject)PrefabUtility.InstantiatePrefab(prefab,old.transform.parent);
                        replacement.transform.localPosition=old.transform.localPosition;
                        replacement.transform.localRotation=old.transform.localRotation;
                        replacement.transform.localScale=old.transform.localScale;
                        replacement.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
                        if(male)look.male=replacement;else look.female=replacement;
                        replacement.SetActive(look.selected==(male?DaldongnePlayerAppearance.Variant.Male:DaldongnePlayerAppearance.Variant.Female));
                        UnityEngine.Object.DestroyImmediate(old);EditorUtility.SetDirty(look);dirty=true;count++;
                    }
                    if(dirty){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
                    if(opened)EditorSceneManager.CloseScene(scene,true);
                }
            }
            finally {EditorSceneManager.RestoreSceneManagerSetup(setup);}
            return count;
        }

        [MenuItem("Tools/Company Game/Characters/Validate Reference Girl")]
        public static void ValidateMenu() => Debug.Log(Validate());
        public static string Validate() => Validate(false);

        public static string Validate(bool male)
        {
            string visualPath=male?"Assets/Art/Daldongne/Players/MaleVisual.prefab":VisualPath;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            if(!prefab)throw new InvalidOperationException("Character visual missing.");
            var motion=prefab.GetComponent<DaldongneAvatarMotion>();
            if(!motion || !motion.hips || !motion.leftArm || !motion.rightArm || !motion.leftLeg || !motion.rightLeg || !motion.leftKnee || !motion.rightKnee)
                throw new InvalidOperationException("Animation joints missing.");
            if(prefab.GetComponentsInChildren<Collider>(true).Length!=0)throw new InvalidOperationException("Visual must not contain colliders.");
            int triangles=0;var renderers=prefab.GetComponentsInChildren<MeshRenderer>(true);
            foreach(var r in renderers)
            {
                var mesh=r.GetComponent<MeshFilter>().sharedMesh;
                if(!mesh || !EditorUtility.IsPersistent(mesh) || !r.sharedMaterial || !r.sharedMaterial.mainTexture)
                    throw new InvalidOperationException("Mesh, palette or material is not persisted.");
                if(mesh.normals.Length!=mesh.vertexCount)throw new InvalidOperationException("Missing normals.");
                if(mesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))throw new InvalidOperationException("Non-finite vertex.");
                var vertices=mesh.vertices;var normals=mesh.normals;var indices=mesh.triangles;
                int opposed=0;
                for(int i=0;i<indices.Length;i+=3)
                {
                    int a=indices[i],b=indices[i+1],c=indices[i+2];
                    var face=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                    if(Vector3.Dot(face,normals[a]+normals[b]+normals[c]) < -1e-8f)opposed++;
                }
                // Allow a few sharp corner interpolation artifacts, but catch a flipped importer.
                if(opposed>indices.Length/300)throw new InvalidOperationException("Triangle winding disagrees with normals: "+mesh.name);
                triangles+=mesh.triangles.Length/3;
            }
            if(renderers.Length!=9 || triangles>20000)throw new InvalidOperationException("Unexpected character geometry budget.");
            foreach(string name in new[]{"PlayerFemale","PlayerMale"})
            {
                var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Daldongne/Players/"+name+".prefab");
                var appearance=player.GetComponent<DaldongnePlayerAppearance>();
                if(!player.GetComponent<CharacterController>() || !player.GetComponent<PlayerMovement>() || !appearance.female || !appearance.male)
                    throw new InvalidOperationException("Playable prefab connections missing: "+name);
                if(!appearance.female.transform.Find("Hips/Head/Hair"))throw new InvalidOperationException("Playable prefab still uses previous female.");
                if(male && !appearance.male.transform.Find("Hips/Head/Hair"))throw new InvalidOperationException("Playable prefab still uses previous male.");
            }
            var report=new JObject{["passed"]=true,["triangles"]=triangles,["renderers"]=renderers.Length,
                ["materials"]=renderers.Select(r=>r.sharedMaterial).Distinct().Count(),["rig"]="Existing rigid joint motion",
                ["prefab"]=visualPath,["utc"]=DateTime.UtcNow.ToString("O")};
            Directory.CreateDirectory("../ArtSource/Daldongne/Characters");
            File.WriteAllText("../ArtSource/Daldongne/Characters/"+(male?"unity_boy_asset_validation.json":"unity_asset_validation.json"),report.ToString());
            return report.ToString();
        }
    }
}
