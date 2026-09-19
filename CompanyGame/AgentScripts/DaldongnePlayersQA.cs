using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using CompanyGame.Daldongne;

public static class DaldongnePlayersQA
{
    static Vector3 Point(JToken p) {return new Vector3((float)p[0],(float)p[2],(float)p[1]);}
    public static object Verify()
    {
        if(!EditorApplication.isPlaying)throw new Exception("Run in Play mode.");
        var walker=UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        var cc=walker.GetComponent<CharacterController>();var look=walker.GetComponent<DaldongnePlayerAppearance>();
        if(!walker.walking || walker.overview.enabled)throw new Exception("Expected automatic player camera on Play.");
        if(walker.GetComponentsInChildren<Collider>(true).Length!=1)throw new Exception("Unexpected visual colliders.");
        string folder=Path.GetFullPath("../ArtSource/Daldongne");
        var routes=(JArray)JObject.Parse(File.ReadAllText(Path.Combine(folder,"warm_routes.json")))["routes"];
        var records=new JArray();bool animation=true;
        try
        {
            foreach(var variant in new[]{DaldongnePlayerAppearance.Variant.Female,DaldongnePlayerAppearance.Variant.Male})
            {
                Vector3 before=walker.transform.position;look.Select(variant);
                if(look.female.activeSelf==look.male.activeSelf || walker.transform.position!=before)throw new Exception("Selection changed player state.");
                var avatar=(variant==DaldongnePlayerAppearance.Variant.Female?look.female:look.male).GetComponent<DaldongneAvatarMotion>();
                avatar.Pose(3,.17f);animation &= Quaternion.Angle(avatar.leftLeg.localRotation,avatar.rightLeg.localRotation)>1;
                foreach(var route in routes)foreach(bool reverse in new[]{false,true})
                {
                    Vector3 start=Point(route[reverse?"b":"a"]),end=Point(route[reverse?"a":"b"]);
                    cc.enabled=false;walker.transform.position=start+Vector3.up*.8f;Physics.SyncTransforms();
                    var hits=Physics.SphereCastAll(start+Vector3.up*.9f,.35f,Vector3.down,1.3f,~0,QueryTriggerInteraction.Ignore)
                        .Where(h=>h.normal.y>.65f && h.distance>.00001f).OrderBy(h=>h.distance).ToArray();
                    if(hits.Length==0)throw new Exception("Missing start support: "+route["name"]);
                    start.y=start.y+.9f-hits[0].distance-.35f+.05f;
                    walker.transform.position=start;cc.enabled=true;Physics.SyncTransforms();
                    int moves=0,stalled=0,head=0;float length=Vector3.Distance(start,end);
                    for(int i=0;i<Mathf.CeilToInt(length/.075f)*4+80;i++)
                    {
                        var remain=end-walker.transform.position;remain.y=0;if(remain.magnitude<=.075f)break;
                        var previous=walker.transform.position;var flags=cc.Move(Vector3.ClampMagnitude(remain,.075f)+Vector3.down*.10f);moves++;
                        if((flags&CollisionFlags.Above)!=0)head++;
                        var progress=walker.transform.position-previous;progress.y=0;stalled=progress.magnitude<.006f?stalled+1:0;
                        if(stalled>=18 || walker.transform.position.y<Mathf.Min(start.y,end.y)-.5f)break;
                    }
                    var error=end-walker.transform.position;float height=Mathf.Abs(error.y);error.y=0;
                    records.Add(new JObject{["variant"]=variant.ToString(),["route"]=(string)route["name"],["reverse"]=reverse,["passed"]=error.magnitude<=.12f && height<=.23f && head==0,["remaining"]=error.magnitude,["heightError"]=height,["moves"]=moves});
                }
            }
        }
        finally{look.Select(DaldongnePlayerAppearance.Variant.Female);walker.ResetToSpawn();walker.SetWalking(false);}
        bool camera=!walker.walking && walker.overview.enabled;
        walker.SetWalking(true);camera &= walker.walking && !walker.overview.enabled;
        int failures=records.Count(r=>!(bool)r["passed"]);
        var result=new JObject{["passed"]=failures==0 && animation && camera,["variants"]=2,["traversals"]=records.Count,["failures"]=failures,["animation"]=animation,["camera"]=camera,["records"]=records};
        File.WriteAllText(Path.Combine(folder,"players_validation.json"),result.ToString());
        if(!(bool)result["passed"])throw new Exception("Player QA failed. See players_validation.json.");
        return new{passed=true,variants=2,traversals=records.Count,failures,animation,camera};
    }
}
