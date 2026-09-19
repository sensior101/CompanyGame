using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static class ReferenceBuildingReview
{
 public static object Capture(){
  string dir=Path.GetFullPath("../ArtSource/Daldongne/ReferenceBuildings");Directory.CreateDirectory(dir);
  var camGo=new GameObject("Temporary reference review camera");var c=camGo.AddComponent<Camera>();c.orthographic=true;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.19f,.23f,.24f);c.nearClipPlane=.1f;c.farClipPlane=180;
  var root=Object.FindObjectsByType<Transform>().First(t=>t.name=="10_Buildings");
  var findings=root.Cast<Transform>().Where(t=>new[]{"Gosiwon","Convenience","Home_A","Home_B","Home_C","Home_D"}.Contains(t.name)).Select(t=>new{t.name,p=t.position.ToString(),bounds=t.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(t.position,Vector3.zero),(b,r)=>{b.Encapsulate(r.bounds);return b;}).ToString()}).ToArray();
  try{
   foreach(var name in new[]{"Gosiwon","Convenience","Home_A","Home_B","Home_C","Home_D"}){
    var t=root.Find(name);Vector3 target=t.position+Vector3.up*(name=="Gosiwon"?4.8f:2.2f);c.orthographicSize=name=="Gosiwon"?7.8f:5.1f;c.transform.position=target+new Vector3(10,8,-15);c.transform.LookAt(target);Shot(c,Path.Combine(dir,name+".png"));
    var others=Object.FindObjectsByType<Renderer>().Where(r=>r.enabled&&!r.transform.IsChildOf(t)).ToArray();
    try{foreach(var r in others)r.enabled=false;Shot(c,Path.Combine(dir,name+"_Model.png"));}finally{foreach(var r in others)r.enabled=true;}
   }
   c.orthographicSize=16;c.transform.position=new Vector3(-10,48,22);c.transform.rotation=Quaternion.Euler(90,0,0);Shot(c,Path.Combine(dir,"Gosiwon_Access_Top.png"));
   ContactSheet(dir);
  }finally{Object.DestroyImmediate(camGo);}
  return new{dir,findings};
 }
 static void ContactSheet(string dir){
  // Review contact sheet assembled only from the actual Unity camera captures.
  var sheet=new Texture2D(2400,2000,TextureFormat.RGB24,false);
  var names=new[]{"Gosiwon","Convenience","Home_A","Home_B"};
  try{for(int i=0;i<names.Length;i++){var tile=new Texture2D(2,2,TextureFormat.RGB24,false);try{tile.LoadImage(File.ReadAllBytes(Path.Combine(dir,names[i]+"_Model.png")));sheet.SetPixels((i%2)*1200,(1-i/2)*1000,1200,1000,tile.GetPixels());}finally{Object.DestroyImmediate(tile);}}sheet.Apply();File.WriteAllBytes(Path.Combine(dir,"Remodeling_Overview.png"),sheet.EncodeToPNG());}finally{Object.DestroyImmediate(sheet);}
 }
 static void Shot(Camera camera,string path){
  var previous=RenderTexture.active;var target=RenderTexture.GetTemporary(1200,1000,24,RenderTextureFormat.ARGB32);var tex=new Texture2D(1200,1000,TextureFormat.RGB24,false);
  try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;tex.ReadPixels(new Rect(0,0,1200,1000),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());}
  finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(tex);}
 }
 public static object Audit(){
  const string folder="Assets/Art/Daldongne/WarmVillage";var errors=new System.Collections.Generic.List<string>();var rows=new Newtonsoft.Json.Linq.JArray();
  foreach(var name in new[]{"Gosiwon","Convenience","Home_A","Home_B","Home_C","Home_D"}){
   string path=folder+"/Prefabs/Buildings/"+name+".prefab";var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!root){errors.Add("Missing prefab: "+name);continue;}
   foreach(var t in root.GetComponentsInChildren<Transform>(true))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)errors.Add("Missing script: "+name+"/"+t.name);
   foreach(var m in root.GetComponentsInChildren<MeshFilter>())if(!m.sharedMesh)errors.Add("Missing mesh: "+name+"/"+m.name);
   foreach(var r in root.GetComponentsInChildren<Renderer>())if(r.sharedMaterials.Any(m=>!m||!m.shader||m.shader.name=="Hidden/InternalErrorShader"))errors.Add("Missing material: "+name+"/"+r.name);
   foreach(var c in root.GetComponentsInChildren<MeshCollider>())if(!c.sharedMesh)errors.Add("Missing collision mesh: "+name);
   rows.Add(new Newtonsoft.Json.Linq.JObject{["name"]=name,["guid"]=AssetDatabase.AssetPathToGUID(path),["renderers"]=root.GetComponentsInChildren<Renderer>().Length,["triangles"]=root.GetComponentsInChildren<MeshFilter>().Where(m=>m.sharedMesh).Sum(m=>m.sharedMesh.triangles.Length/3),["colliders"]=root.GetComponentsInChildren<Collider>().Length});
  }
  if(EditorUtility.scriptCompilationFailed)errors.Add("Unity script compilation failed.");
  var report=new Newtonsoft.Json.Linq.JObject{["passed"]=errors.Count==0,["errors"]=new Newtonsoft.Json.Linq.JArray(errors),["compilationFailed"]=EditorUtility.scriptCompilationFailed,["buildings"]=rows};
  File.WriteAllText("../ArtSource/Daldongne/ReferenceBuildings/asset-validation.json",report.ToString());return report;
 }
}
