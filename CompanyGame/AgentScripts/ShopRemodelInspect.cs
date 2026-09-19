using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;
public static class ShopRemodelInspect
{
 public static object Inspect(){
  const string folder="Assets/Art/Daldongne/WarmVillage";
  var root=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/DaldongneWarmTown.prefab");var buildings=root.transform.Find("10_Buildings");
  var result=new JObject{["scenes"]=new JArray(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new JObject{["path"]=SceneManager.GetSceneAt(i).path,["dirty"]=SceneManager.GetSceneAt(i).isDirty})),["playing"]=EditorApplication.isPlaying};
  var rows=new JArray();foreach(var name in new[]{"Laundry","Supermarket","Bakery"}){var t=buildings.Find(name);if(!t)continue;var renderers=t.GetComponentsInChildren<MeshRenderer>();Bounds b=new Bounds();bool first=true;foreach(var r in renderers){var m=r.GetComponent<MeshFilter>();if(!m||!m.sharedMesh)continue;var lb=m.sharedMesh.bounds;for(int j=0;j<8;j++){var p=r.transform.TransformPoint(lb.center+Vector3.Scale(lb.extents,new Vector3((j&1)==0?-1:1,(j&2)==0?-1:1,(j&4)==0?-1:1)));if(first){b=new Bounds(p,Vector3.zero);first=false;}else b.Encapsulate(p);}}
   rows.Add(new JObject{["name"]=name,["pivot"]=JArray.FromObject(new[]{t.position.x,t.position.y,t.position.z}),["boundsCenter"]=JArray.FromObject(new[]{b.center.x,b.center.y,b.center.z}),["boundsSize"]=JArray.FromObject(new[]{b.size.x,b.size.y,b.size.z}),["path"]=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject),["guid"]=AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)),["colliders"]=new JArray(t.GetComponentsInChildren<Collider>().Select(c=>new JObject{["name"]=c.name,["pos"]=c.transform.position.ToString(),["scale"]=c.transform.lossyScale.ToString()}))});
  }
  result["buildings"]=rows;Directory.CreateDirectory("../ArtSource/Daldongne/ShopRemodel");if(!File.Exists("../ArtSource/Daldongne/ShopRemodel/before.json"))File.WriteAllText("../ArtSource/Daldongne/ShopRemodel/before.json",result.ToString());return result;
 }
}
