using System;
using System.IO;
using System.Linq;
using CompanyGame.Daldongne;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CompanyGame.Editor.Characters
{
    /// <summary>Full-resolution, continuous skinned male driven by the existing motion owner.</summary>
    public static class MeshyMaleImporter
    {
        public const string Folder = "Assets/Art/Daldongne/Players/MeshyMale";
        const string Visual = ReferenceBoyImporter.VisualPath;
        static float Blend(float low, float high, float value) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(low, high, value));

        static BoneWeight Weight(Vector3 v, int region, float armInfluence)
        {
            bool left = v.x < 0;
            int arm = left ? 2 : 3, thigh = left ? 4 : 6, knee = thigh + 1;
            if (region == 1 || region == 6)
            {
                float head = Blend(1.28f, 1.38f, v.y);
                return Pair(1, head, 0);
            }
            if (armInfluence > .00001f)
            {
                float shoulder = (1 - Blend(1.17f, 1.28f, v.y)) * Mathf.Clamp01(armInfluence);
                float elbow = 1 - Blend(.96f, 1.075f, v.y);
                return new BoneWeight {boneIndex0=arm,weight0=shoulder*(1-elbow),
                    boneIndex1=left?8:9,weight1=shoulder*elbow,boneIndex2=0,weight2=1-shoulder};
            }
            if (region == 2 || region == 7)
            {
                float pelvis = Blend(.76f, .88f, v.y);
                float shin = 1 - Blend(.39f, .52f, v.y);
                return new BoneWeight { boneIndex0 = thigh, weight0 = (1-pelvis)*(1-shin),
                    boneIndex1 = knee, weight1 = (1-pelvis)*shin, boneIndex2 = 0, weight2 = pelvis };
            }
            return Pair(0, 1, 0);
        }
        static BoneWeight Pair(int bone, float weight, int other) => new BoneWeight
            { boneIndex0 = bone, weight0 = weight, boneIndex1 = other, weight1 = 1-weight };

        [MenuItem("Tools/Company Game/Characters/Rebuild Meshy Male")]
        public static void RebuildMenu() => Debug.Log(Rebuild());
        public static string Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Import requires edit mode and no compilation errors.");
            for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save scene changes before rebuilding.");
            string sourcePath=Folder+"/MaleBody.fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(sourcePath);
            importer.isReadable=true;
            importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.importNormals=ModelImporterNormals.Calculate;
            importer.normalSmoothingAngle=180;
            importer.normalSmoothingSource=ModelImporterNormalSmoothingSource.FromAngle;
            importer.importAnimation=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath).GetComponentInChildren<MeshFilter>();
            // Build a fresh vertex layout: imported static-mesh GPU buffers do
            // not include skin weights and must not be reused as a skin buffer.
            string meshPath=Folder+"/MaleBody.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool create=!mesh;
            if(create)mesh=new Mesh();
            else mesh.Clear();
            mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.name="MeshyMaleBody";
            var vertices=source.sharedMesh.vertices;
            var normals=source.sharedMesh.normals;
            var regions=source.sharedMesh.uv2;
            if(regions.Length!=vertices.Length || source.sharedMesh.triangles.Length/3!=226436)
                throw new InvalidDataException("Full-resolution geometry or region data missing.");
            var matrix=source.transform.localToWorldMatrix;
            var normalMatrix=matrix.inverse.transpose;
            var weights=new BoneWeight[vertices.Length];
            for(int i=0;i<vertices.Length;i++)
            {
                vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);
                normals[i]=normalMatrix.MultiplyVector(normals[i]).normalized;
                weights[i]=Weight(vertices[i],Mathf.RoundToInt(regions[i].x),regions[i].y);
            }
            mesh.vertices=vertices;mesh.normals=normals;mesh.uv=source.sharedMesh.uv;
            mesh.subMeshCount=source.sharedMesh.subMeshCount;
            for(int i=0;i<mesh.subMeshCount;i++)mesh.SetTriangles(source.sharedMesh.GetTriangles(i),i);
            mesh.boneWeights=weights;
            mesh.RecalculateBounds();mesh.RecalculateTangents();

            ColorUtility.TryParseHtmlString("#E7B89D",out var skinColor);
            ColorUtility.TryParseHtmlString("#858585",out var shortsColor);
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Male.mat");
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,Folder+"/Male.mat");}
            material.SetTexture("_BaseMap",null);
            material.SetTexture("_MainTex",null);
            material.SetColor("_BaseColor",shortsColor);material.SetFloat("_Smoothness",.18f);material.SetFloat("_Metallic",0);
            EditorUtility.SetDirty(material);
            var skinMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Skin.mat");
            if(!skinMaterial){skinMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(skinMaterial,Folder+"/Skin.mat");}
            skinMaterial.SetTexture("_BaseMap",null);skinMaterial.SetColor("_BaseColor",skinColor);
            skinMaterial.SetFloat("_Smoothness",.18f);skinMaterial.SetFloat("_Metallic",0);
            EditorUtility.SetDirty(skinMaterial);
            var root=PrefabUtility.LoadPrefabContents(Visual);
            try
            {
                foreach(Transform child in root.transform.Cast<Transform>().ToArray())UnityEngine.Object.DestroyImmediate(child.gameObject);
                string[] names={"Hips","Head","LeftArm","RightArm","LeftLeg","LeftKnee","RightLeg","RightKnee","LeftForearm","RightForearm"};
                int[] parents={-1,0,0,0,0,4,0,6,2,3};
                Vector3[] positions={new Vector3(0,.81f,0),new Vector3(0,1.34f,0),new Vector3(-.112f,1.25f,0),new Vector3(.112f,1.25f,0),
                    new Vector3(-.09f,.805f,0),new Vector3(-.115f,.46f,0),new Vector3(.09f,.805f,0),new Vector3(.115f,.46f,0),
                    new Vector3(-.17f,1.015f,0),new Vector3(.17f,1.015f,0)};
                var bones=new Transform[names.Length];
                for(int i=0;i<bones.Length;i++)
                {
                    bones[i]=new GameObject(names[i]).transform;
                    bones[i].SetParent(parents[i]<0?root.transform:bones[parents[i]],false);
                    bones[i].localPosition=positions[i]-(parents[i]<0?Vector3.zero:positions[parents[i]]);
                }
                mesh.bindposes=bones.Select(b=>b.worldToLocalMatrix*root.transform.localToWorldMatrix).ToArray();
                if(create)AssetDatabase.CreateAsset(mesh,meshPath);
                EditorUtility.SetDirty(mesh);
                mesh.UploadMeshData(false);
                var body=new GameObject("Body");body.transform.SetParent(root.transform,false);
                var skin=body.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.sharedMaterials=new[]{skinMaterial,material};
                skin.bones=bones;skin.rootBone=root.transform;skin.quality=SkinQuality.Bone4;
                skin.localBounds=new Bounds(new Vector3(0,.9f,0),new Vector3(1.0f,1.95f,1.25f));
                var motion=root.GetComponent<DaldongneAvatarMotion>();
                motion.hips=bones[0];motion.leftArm=bones[2];motion.rightArm=bones[3];
                motion.leftLeg=bones[4];motion.leftKnee=bones[5];motion.rightLeg=bones[6];motion.rightKnee=bones[7];
                motion.leftForearm=bones[8];motion.rightForearm=bones[9];
                motion.footSpacing=.85f;motion.strideScale=.82f;motion.liftScale=.6f;motion.swayScale=.5f;
                motion.Pose(0,0);
                PrefabUtility.SaveAsPrefabAsset(root,Visual);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
            return Validate();
        }

        public static string Validate()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Visual);
            var skin=prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if(!skin || !skin.sharedMesh || skin.bones.Length!=10 || skin.bones.Any(b=>!b))
                throw new InvalidOperationException("Male skin or bones missing.");
            var mesh=skin.sharedMesh;
            if(mesh.subMeshCount!=2 || skin.sharedMaterials.Length!=2 || skin.sharedMaterials.Any(m=>!m || m.GetTexture("_BaseMap")))
                throw new InvalidOperationException("Skin and shorts must use separate solid-color materials.");
            if(mesh.triangles.Length/3!=226436 || mesh.boneWeights.Length!=mesh.vertexCount || mesh.bindposes.Length!=10)
                throw new InvalidOperationException("Geometry or skinning data missing.");
            if(mesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)) ||
                mesh.boneWeights.Any(w=>Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)>.0001f))
                throw new InvalidOperationException("Invalid skin weights or positions.");
            foreach(string path in new[]{"Assets/Resources/Player.prefab","Assets/Art/Daldongne/Players/PlayerFemale.prefab","Assets/Art/Daldongne/Players/PlayerMale.prefab"})
            {
                var player=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var look=player.GetComponent<DaldongnePlayerAppearance>();
                if(!look || !look.female || !look.male || PrefabUtility.GetCorrespondingObjectFromSource(look.male)!=prefab)
                    throw new InvalidOperationException("Player appearance connection missing: "+path);
            }
            return "Male validated: 226436 triangles, 10 bones, 1 continuous skinned renderer; all player prefab links valid.";
        }
    }
}
