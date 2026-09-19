using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CompanyGame.Daldongne;
using Newtonsoft.Json.Linq;

// Run with unity command run_script --file AgentScripts/AvatarMotionQA.cs
// --entry AvatarMotionQA.Validate. Uses isolated prefab instances in Edit Mode.
public static class AvatarMotionQA
{
    const string Folder = "Assets/Art/Daldongne/Players/";
    sealed class Rest
    {
        public Transform t;
        public Vector3 p;
        public Quaternion q;
    }
    sealed class MeshPoints
    {
        public Transform t;
        public Vector3[] points;
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    static MeshPoints[] FootMeshes(Transform knee) => knee.GetComponentsInChildren<MeshFilter>()
        .Where(m => m.sharedMesh && m.sharedMesh.isReadable)
        .Select(m => new MeshPoints { t=m.transform, points=m.sharedMesh.vertices }).ToArray();
    static float Floor(Transform root, MeshPoints[] meshes)
    {
        float y=float.PositiveInfinity;
        foreach(var mesh in meshes) foreach(var v in mesh.points)
            y=Mathf.Min(y,root.InverseTransformPoint(mesh.t.TransformPoint(v)).y);
        return y;
    }
    static void Restored(Rest[] joints)
    {
        foreach(var j in joints)
        {
            Require(Vector3.Distance(j.t.localPosition,j.p)<.0001f,j.t.name+" rest position drifted");
            Require(Quaternion.Angle(j.t.localRotation,j.q)<.06f,j.t.name+" rest rotation drifted");
        }
    }
    public static object RuntimeLifecycle()
    {
        Require(EditorApplication.isPlaying,"Run runtime lifecycle validation in Play Mode.");
        Require(Time.deltaTime>0,"Wait for a running Play frame before this check.");
        var records=new JArray();
        foreach(string variant in new[]{"FemaleVisual","MaleVisual"})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+variant+".prefab");
            Require(prefab,"Missing visual prefab "+variant);
            // Visual-only clones stay outside the map/camera; the scene player
            // and its controller, selection, position and current pose are untouched.
            var avatar=UnityEngine.Object.Instantiate(prefab,new Vector3(10000,10000,10000),Quaternion.identity);
            try
            {
                avatar.name=variant+" runtime lifecycle QA";
                avatar.SetActive(true);
                var motion=avatar.GetComponent<DaldongneAvatarMotion>();
                motion.Pose(0,0);
                var joints=avatar.GetComponentsInChildren<Transform>(true)
                    .Where(t=>t!=avatar.transform)
                    .Select(t=>new Rest{t=t,p=t.localPosition,q=t.localRotation}).ToArray();
                for(int i=0;i<8;i++)motion.Pose(3,.1f);
                Require(Quaternion.Angle(motion.leftKnee.localRotation,joints.First(j=>j.t==motion.leftKnee).q)>3,
                    variant+" did not animate before lifecycle check");
                // These are real Unity callbacks on a live MonoBehaviour.
                avatar.SetActive(false);Restored(joints);
                avatar.SetActive(true);Restored(joints);
                for(int i=0;i<8;i++)motion.Pose(3,.1f);
                avatar.transform.position+=Vector3.right*20;
                Vector3 teleported=avatar.transform.position;
                // Invoke the real measured-displacement path using this Play
                // frame's deltaTime, rather than passing a synthetic speed to Pose.
                motion.SendMessage("LateUpdate",SendMessageOptions.RequireReceiver);
                Restored(joints);
                Require(avatar.transform.position==teleported,variant+" moved its root during teleport reset");
                records.Add(new JObject{{"variant",variant},{"realSetActiveCallbacksRestoredPose",true},
                    {"lateUpdateMeasuredTeleportRestoredPose",true},{"teleportDistance",20},
                    {"deltaTime",Time.deltaTime},{"rootPositionPreserved",true}});
            }
            finally{UnityEngine.Object.DestroyImmediate(avatar);}
        }
        var report=new JObject{{"passed",true},{"utc",DateTime.UtcNow.ToString("o")},
            {"mode","Play Mode isolated visual clones"},{"scenePlayerUntouched",true},{"variants",records}};
        File.WriteAllText(Path.GetFullPath("../ArtSource/Daldongne/Characters/runtime_motion_validation.json"),report.ToString());
        return report.ToString();
    }
    public static object Validate()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Run gait validation in Edit Mode.");
        var scene=EditorSceneManager.NewPreviewScene();
        var records=new JArray();int samples=0;
        try
        {
            foreach(string variant in new[]{"FemaleVisual","MaleVisual"})
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+variant+".prefab");
                Require(prefab,"Missing visual prefab "+variant);
                var avatar=UnityEngine.Object.Instantiate(prefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(avatar,scene);
                try
                {
                    var motion=avatar.GetComponent<DaldongneAvatarMotion>();
                    var joints=avatar.GetComponentsInChildren<Transform>(true)
                        .Select(t=>new Rest{t=t,p=t.localPosition,q=t.localRotation}).ToArray();
                    var left=FootMeshes(motion.leftKnee);var right=FootMeshes(motion.rightKnee);
                    float floorL=Floor(avatar.transform,left), floorR=Floor(avatar.transform,right);
                    float worstPenetration=0,maxKnee=0,minKnee=360,maxLift=0,maxPelvis=0;
                    var gaitRecords=new JArray();
                    foreach(float speed in new[]{.4f,3f,3.6f,4.5f})
                    {
                        motion.Pose(99,1f/120);Restored(joints);
                        int airborne=0,grounded=0;float maxFlight=0;
                        for(int i=0;i<600;i++)
                        {
                            float dt=i%3==0?1f/60:1f/120;
                            motion.Pose(speed,dt);samples++;
                            foreach(var j in joints)
                            {
                                var p=j.t.localPosition;var q=j.t.localRotation;
                                Require(Finite(p.x)&&Finite(p.y)&&Finite(p.z)&&Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w),variant+" non-finite pose");
                            }
                            float knee=Mathf.DeltaAngle(0,motion.leftKnee.localEulerAngles.x);
                            Require(knee>=-.05f&&knee<135,variant+" reversed/overbent knee "+knee);
                            if(i>120){minKnee=Mathf.Min(minKnee,knee);maxKnee=Mathf.Max(maxKnee,knee);}
                            float a=Floor(avatar.transform,left)-floorL,b=Floor(avatar.transform,right)-floorR;
                            worstPenetration=Mathf.Min(worstPenetration,Mathf.Min(a,b));
                            maxLift=Mathf.Max(maxLift,Mathf.Max(a,b));
                            maxPelvis=Mathf.Max(maxPelvis,Vector3.Distance(motion.hips.localPosition,joints.First(j=>j.t==motion.hips).p));
                            Require(Mathf.Min(a,b)>-.006f,variant+" sole penetrates authored floor "+Mathf.Min(a,b));
                            float clearance=Mathf.Min(a,b);
                            if(speed<=3.1f)Require(Mathf.Abs(clearance)<.008f,variant+" walking lost its support foot");
                            else Require(clearance<.025f,variant+" excessive running flight height");
                            if(i>120)
                            {
                                if(clearance>.002f)airborne++;else grounded++;
                                maxFlight=Mathf.Max(maxFlight,clearance);
                            }
                        }
                        if(speed>=4.5f)
                        {
                            Require(airborne>20,variant+" run never has a flight phase");
                            Require(grounded>300,variant+" run lacks a stable support phase");
                            Require(maxFlight>.010f,variant+" running flight is not distinct from walking");
                        }
                        gaitRecords.Add(new JObject{{"speed",speed},{"flightSamples",airborne},
                            {"groundedSamples",grounded},{"maxFlightClearance",maxFlight}});
                        for(int i=0;i<360;i++)
                        {
                            motion.Pose(0,1f/120);
                            Require(Mathf.Min(Floor(avatar.transform,left)-floorL,Floor(avatar.transform,right)-floorR)>-.006f,
                                variant+" foot penetrated floor while stopping from "+speed);
                        }
                        Restored(joints);
                    }
                    Require(maxKnee-minKnee>25,variant+" lacks swing knee flexion");
                    Require(maxLift>.035f,variant+" has no foot clearance");
                    Require(maxPelvis<.20f,variant+" excessive pelvis displacement");
                    motion.Pose(3,.03f);motion.Pose(100,.03f);Restored(joints);
                    motion.Pose(3,.03f);motion.Pose(float.NaN,.03f);Restored(joints);
                    motion.Pose(3,float.NaN);Restored(joints);
                    motion.Pose(3,0);Restored(joints);
                    // Ordinary MonoBehaviours do not receive activation callbacks
                    // on these Edit Mode preview instances. Exercise the callback
                    // bodies explicitly; runtime activation is a separate Play test.
                    motion.Pose(3,.03f);
                    typeof(DaldongneAvatarMotion).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motion,null);
                    Restored(joints);
                    typeof(DaldongneAvatarMotion).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(motion,null);
                    Restored(joints);
                    for(int i=0;i<60;i++)motion.Pose(3,1f/240);
                    motion.Pose(99,1f/60);Restored(joints);
                    records.Add(new JObject{{"variant",variant},{"worstSolePenetration",worstPenetration},
                        {"minKneeDegrees",minKnee},{"maxKneeDegrees",maxKnee},{"maxSwingClearance",maxLift},
                        {"maxPelvisDisplacement",maxPelvis},{"restoredAfterStopAndTeleportSpeed",true},
                        {"restoredAfterExplicitLifecycleCallsInEditMode",true},{"gaits",gaitRecords}});
                }
                finally{UnityEngine.Object.DestroyImmediate(avatar);}
            }
            var report=new JObject{{"passed",true},{"utc",DateTime.UtcNow.ToString("o")},{"samples",samples},
                {"speeds",new JArray(.4,3,3.6,4.5)},{"variants",records}};
            File.WriteAllText(Path.GetFullPath("../ArtSource/Daldongne/Characters/motion_validation.json"),report.ToString());
            return report.ToString();
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
