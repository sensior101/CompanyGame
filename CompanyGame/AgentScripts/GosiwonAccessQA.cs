using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using CompanyGame.Daldongne;
public static class GosiwonAccessQA
{
 static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
 static Vector3 Point(JToken p)=>V((float)p[0],(float)p[2],(float)p[1]);
 static CharacterController motor;
 static JArray records;
 static void Place(Vector3 p){motor.enabled=false;motor.transform.position=p+Vector3.up*.06f;motor.enabled=true;Physics.SyncTransforms();for(int i=0;i<15;i++)motor.Move(Vector3.down*.02f);}
 static bool Walk(string name,Vector3 target,float speed,string variant,bool reverse){
  Vector3 origin=motor.transform.position;float length=Vector2.Distance(new Vector2(origin.x,origin.z),new Vector2(target.x,target.z));
  float vertical=-2;int stalled=0,heads=0,moves=0;bool fell=false;
  for(int i=0;i<(int)(length/speed*60)*4+180;i++){
   Vector3 remain=target-motor.transform.position;remain.y=0;if(remain.magnitude<.065f)break;
   if(motor.isGrounded && vertical<0)vertical=-2;vertical+=Physics.gravity.y/60;
   Vector3 previous=motor.transform.position;var flags=motor.Move(Vector3.ClampMagnitude(remain,speed/60)+Vector3.up*(vertical/60));moves++;
   if((flags&CollisionFlags.Above)!=0)heads++;
   var progress=motor.transform.position-previous;progress.y=0;stalled=progress.magnitude<.0015f?stalled+1:0;
   fell=motor.transform.position.y<Mathf.Min(origin.y,target.y)-.65f;if(stalled>=30||fell)break;
  }
  var error=target-motor.transform.position;float height=Mathf.Abs(error.y);error.y=0;bool passed=error.magnitude<.12f&&height<.24f&&!fell&&heads==0;
  records.Add(new JObject{["route"]=name,["variant"]=variant,["speed"]=speed,["reverse"]=reverse,["passed"]=passed,["horizontalError"]=error.magnitude,["heightError"]=height,["headContacts"]=heads,["moves"]=moves,["position"]=motor.transform.position.ToString("F3"),["nearby"]=passed?null:new JArray(Physics.OverlapCapsule(motor.transform.position+V(0,.4f,0),motor.transform.position+V(0,1.4f,0),.42f).Where(c=>c!=motor).Select(c=>c.name))});return passed;
 }
 public static object Verify(){
  if(!EditorApplication.isPlaying)throw new Exception("Run this check in Play Mode.");
  var player=UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();motor=player.GetComponent<CharacterController>();var appearance=player.GetComponent<DaldongnePlayerAppearance>();
  Vector3 oldPosition=player.transform.position;Quaternion oldRotation=player.transform.rotation;bool oldEnabled=player.enabled;var oldVariant=appearance.female.activeSelf?DaldongnePlayerAppearance.Variant.Female:DaldongnePlayerAppearance.Variant.Male;player.enabled=false;
  records=new JArray();int continuousTrips=0;
  try{
   var original=(JArray)JObject.Parse(File.ReadAllText("../ArtSource/Daldongne/warm_routes.json"))["routes"];
   foreach(var variant in new[]{DaldongnePlayerAppearance.Variant.Female,DaldongnePlayerAppearance.Variant.Male}){
    appearance.Select(variant);
    foreach(var route in original)foreach(bool reverse in new[]{false,true}){Place(Point(route[reverse?"b":"a"]));Walk((string)route["name"],Point(route[reverse?"a":"b"]),3,variant.ToString(),reverse);}
    var added=new[]{new[]{V(-6.2f,12,29),V(-6.2f,12,20.55f)},new[]{V(-6.2f,12,20.55f),V(-11.08f,12,20.55f)}};
    int segment=0;foreach(var route in added){foreach(float lane in new[]{-.44f,0,.44f})foreach(bool reverse in new[]{false,true}){var side=Vector3.Cross(Vector3.up,(route[1]-route[0]).normalized)*lane;Place(route[reverse?1:0]+side);Walk("GosiwonApproach"+segment+"_lane"+lane,route[reverse?0:1]+side,3,variant.ToString(),reverse);}segment++;}
    // No teleport between checkpoints: continuous spawn -> door -> spawn.
    var checkpoints=new[]{player.spawn,V(-23.5f,1,-27),V(-23.5f,1,-19.2f),V(-18,1,-19.2f),V(-17.5f,1,-18.5f),V(-12,5.2f,-12.5f),V(-3.8f,5.2f,-6.8f),V(-.7f,3,-1.3f),V(-1.3f,3,-.65f),V(-1.3f,3,-.05f),V(-1.3f,6,7),V(-1.3f,6,11.8f),V(-1.3f,9,18.8f),V(-1.3f,9,21.4f),V(-1.3f,12,28.4f),V(-1.3f,12,29),V(-6.2f,12,29),V(-6.2f,12,20.55f),V(-11.08f,12,20.55f),V(-11.08f,12,20.96f)};
    foreach(float speed in new[]{3f,4.5f}){
     Place(checkpoints[0]);bool passed=true;
     for(int i=1;i<checkpoints.Length;i++)if(!Walk("ContinuousOutbound_"+i,checkpoints[i],speed,variant.ToString(),false)){passed=false;break;}
     if(passed){continuousTrips++;for(int i=checkpoints.Length-2;i>=0;i--)if(!Walk("ContinuousReturn_"+i,checkpoints[i],speed,variant.ToString(),true)){passed=false;break;}if(passed)continuousTrips++;}
    }
   }
  }finally{appearance.Select(oldVariant);motor.enabled=false;player.transform.SetPositionAndRotation(oldPosition,oldRotation);motor.enabled=true;player.enabled=oldEnabled;player.SetWalking(true);Physics.SyncTransforms();}
  int failures=records.Count(r=>!(bool)r["passed"]);var result=new JObject{["passed"]=failures==0&&continuousTrips==8,["continuousTrips"]=continuousTrips,["checks"]=records.Count,["failures"]=failures,["radius"]=motor.radius,["height"]=motor.height,["stepOffset"]=motor.stepOffset,["slopeLimit"]=motor.slopeLimit,["records"]=records};
  Directory.CreateDirectory("../ArtSource/Daldongne/ReferenceBuildings");File.WriteAllText("../ArtSource/Daldongne/ReferenceBuildings/navigation-validation.json",result.ToString());
  return new{passed=(bool)result["passed"],continuousTrips,checks=records.Count,failures,failed=records.Where(r=>!(bool)r["passed"]).ToArray()};
 }
}
