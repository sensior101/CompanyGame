using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static class GosiwonAccessRepair
{
 const string Folder="Assets/Art/Daldongne/WarmVillage";
 static Transform holder;
 static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
 static GameObject Box(string name,Vector3 center,Vector3 size,string mat,bool collision=true){
  var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(holder,false);go.transform.localPosition=center;go.transform.localScale=size;
  go.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/ReferenceBuildings/"+mat+".mat");go.isStatic=true;if(!collision)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
 }
 static void Guard(Vector3 a,Vector3 b){
  var delta=b-a;float length=delta.magnitude;var q=Quaternion.LookRotation(delta);
  var top=Box("Handrail",(a+b)/2+V(0,.88f,0),V(.06f,.07f,length),"metal",false);top.transform.localRotation=q;
  int n=Mathf.CeilToInt(length/.8f);for(int i=0;i<=n;i++)Box("RailPost",Vector3.Lerp(a,b,(float)i/n)+V(0,.44f,0),V(.055f,.88f,.055f),"metal",false);
  var rail=new GameObject("GuardCollider");rail.transform.SetParent(holder,false);rail.transform.localPosition=(a+b)/2+V(0,.45f,0);rail.transform.localRotation=q;rail.AddComponent<BoxCollider>().size=V(.08f,.9f,length);
 }
 public static object Build(){
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play mode first.");
  for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new Exception("Save scene changes first.");
  string path=Folder+"/DaldongneWarmTown.prefab";string backup="Temp/ReferenceBuildingsBackup/DaldongneWarmTown.prefab";if(!File.Exists(backup))File.Copy(path,backup);
  var root=PrefabUtility.LoadPrefabContents(path);
  try{
   var parent=root.transform.Find("00_Terrain");var old=parent.Find("Gosiwon_Access");if(old)Object.DestroyImmediate(old.gameObject);
   holder=new GameObject("Gosiwon_Access").transform;holder.SetParent(parent,false);
   // Continuous 1.8m sidewalk, flush with the existing 12m crest landing.
   Box("East_Promenade",V(-6.2f,11.9f,24.40f),V(1.8f,.20f,9.20f),"concrete");
   Box("Front_Entry_Terrace",V(-10.75f,11.9f,20.55f),V(10.9f,.20f,1.80f),"concrete");
   Box("Front_Retaining_Platform",V(-10.75f,10.4f,20.12f),V(10.9f,2.80f,.95f),"mortar");
   Box("East_Retaining_Platform",V(-6.2f,10.4f,21.9f),V(1.8f,2.80f,4.20f),"mortar");
   for(float x=-15.8f;x<-5.4f;x+=.75f)Box("TerracePavingJoint",V(x,12.004f,20.55f),V(.016f,.006f,1.65f),"slate",false);
   for(float z=21.5f;z<29;z+=.75f)Box("SidePavingJoint",V(-6.2f,12.004f,z),V(1.65f,.006f,.016f),"slate",false);
   Guard(V(-16.2f,12,19.67f),V(-5.33f,12,19.67f));Guard(V(-5.33f,12,19.67f),V(-5.33f,12,27.65f));
   // Lower only wall pieces at the entrance crossing. The old cap was 43cm
   // above the pavement, beyond the player's unchanged 23cm step offset.
   var cap=root.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="WallCap_Crest0");
   if(cap){
    // Retain the original left segment and the distant right end, leaving an opening.
    cap.localPosition=new Vector3(-13.15f,cap.localPosition.y,cap.localPosition.z);cap.localScale=new Vector3(11.9f,cap.localScale.y,cap.localScale.z);
    var mc=cap.GetComponent<MeshCollider>();if(mc)Object.DestroyImmediate(mc);
    // Preserve the existing stair cut-out by omitting collision where WestCrest passes.
    var boxCollider=cap.GetComponent<BoxCollider>();if(boxCollider)Object.DestroyImmediate(boxCollider);
    var left=Box("CrestCapCollisionLeft",V(-18.87f,12.34f,23),V(.45f,.18f,.7f),"cream");left.GetComponent<MeshRenderer>().enabled=false;
    var right=Box("CrestCapCollisionRight",V(-11.76f,12.34f,23),V(9.12f,.18f,.7f),"cream");right.GetComponent<MeshRenderer>().enabled=false;
    Box("CrestCapEastEnd",V(-4.0f,12.34f,23),V(2.2f,.18f,.7f),"cream");
   }
   foreach(var t in root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Wall_Crest0")&&t.position.x> -7.25f&&t.position.x< -5.15f).ToArray()){
    // Only the highest two courses can obstruct a capsule on this pavement.
    if(t.position.y>11.6f)t.localPosition+=V(0,-.45f,0);
   }
   // A decorative tree and pots stood in the new passage; relocate within this terrace.
   var trees=root.transform.Find("20_Nature/Trees");if(trees)foreach(Transform t in trees)if(Vector2.Distance(new Vector2(t.position.x,t.position.z),new Vector2(-7,22))<.7f)t.position=V(-4.5f,t.position.y,24.7f);
   foreach(var t in root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("WarmPot")&&Mathf.Abs(t.position.x+6.9f)<.1f).ToArray())t.position+=V(1.6f,0,0);
   if(!PrefabUtility.SaveAsPrefabAsset(root,path))throw new Exception("Could not save repaired approach.");AssetDatabase.SaveAssets();
  }finally{PrefabUtility.UnloadPrefabContents(root);}
  return new{success=true,width=1.8f,floorHeight=12,route="CrestSquare -> East_Promenade -> Front_Entry_Terrace -> Gosiwon door"};
 }
}
