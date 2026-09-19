using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Authored, deterministic native Unity meshes. Fronts face -Z, units are metres.
// Only the six named building prefab contents and the Gosiwon approach are changed.
public static class ReferenceBuildingRemodel
{
 const string Base="Assets/Art/Daldongne/WarmVillage";
 const string Output=Base+"/ReferenceBuildings";
 const string Village=Base+"/DaldongneWarmTown.prefab";
 static Dictionary<string,Material> mats;
 static Transform model;
 static string building;
 static Vector3 offset;
 static Dictionary<string,List<CombineInstance>> pieces;
 static List<Mesh> temporary;
 static int partNumber;
 static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
 static void Folder(string path) { if(!AssetDatabase.IsValidFolder(path)) { Folder(Path.GetDirectoryName(path).Replace('\\','/')); AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path)); } }
 static Material Material(string name,string hex,float emission=0,float gloss=.15f) {
  ColorUtility.TryParseHtmlString(hex,out var color);
  string path=Output+"/"+name+".mat";
  var m=AssetDatabase.LoadAssetAtPath<Material>(path);
  if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
  m.SetColor("_BaseColor",color);m.SetFloat("_Smoothness",gloss);
  if(emission>0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*emission);}
  EditorUtility.SetDirty(m);return m;
 }
 static void Palette() {
  Folder(Output);mats=new Dictionary<string,Material>();
  string[] colors={"brick:#934B40","brickLight:#AE6352","brickDark:#763D36","mortar:#B0957B","cream:#D5C5A3","plaster:#DDCF92","concrete:#918D7F","roof:#344F69","roofLight:#486885","metal:#384B49","green:#396456","blue:#2F536B","glass:#477783","glassLight:#83A6A2","warm:#EACD8E","wood:#5C4436","white:#F0E9CF","orange:#E97936","yellow:#E7BD4F","leaf:#547040","leafLight:#849652","pot:#AF674B","black:#252C2B","slate:#606961","red:#C05747"};
  foreach(var item in colors){var s=item.Split(':');mats[s[0]]=Material(s[0],s[1],s[0]=="warm"?.18f:0,s[0].StartsWith("glass")?.6f:.15f);}
 }
 static Mesh Mesh(string name,Vector3[] vertices,int[] triangles) {var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32};m.vertices=vertices;m.triangles=triangles;m.RecalculateNormals();m.RecalculateBounds();temporary.Add(m);return m;}
 static Mesh cube;
 static void Begin(Transform root,string name,Vector3 center) {
  model=root;building=name;offset=center-root.position;pieces=new Dictionary<string,List<CombineInstance>>();temporary=new List<Mesh>();partNumber=0;
  // Flat face normals are retained when pieces are combined.
  var go=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=go.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(go);
 }
 static void Add(string group,string mat,Mesh mesh,Vector3 p,Vector3 scale,Quaternion rotation) {
  string key=group+"|"+mat;if(!pieces.ContainsKey(key))pieces[key]=new List<CombineInstance>();
  pieces[key].Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(p+offset,rotation,scale)});
 }
 static void Box(string group,string mat,Vector3 p,Vector3 size,bool collision=false) {
  Add(group,mat,cube,p,size,Quaternion.identity);
  if(collision){var t=new GameObject("Solid_"+group+"_"+(partNumber++)).transform;t.SetParent(model,false);t.localPosition=p+offset;t.gameObject.AddComponent<BoxCollider>().size=size;}
 }
 static void Beam(string group,string mat,Vector3 a,Vector3 b,float width) {Add(group,mat,cube,(a+b)/2,V(width,width,Vector3.Distance(a,b)),Quaternion.LookRotation(b-a));}
 static void Sphere(string group,string mat,Vector3 p,Vector3 size) {var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);Add(group,mat,go.GetComponent<MeshFilter>().sharedMesh,p,size,Quaternion.identity);Object.DestroyImmediate(go);}
 static void Cylinder(string group,string mat,Vector3 p,float radius,float height) {var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);Add(group,mat,go.GetComponent<MeshFilter>().sharedMesh,p,V(radius*2,height/2,radius*2),Quaternion.identity);Object.DestroyImmediate(go);}
 static void Finish() {
  foreach(var entry in pieces){var s=entry.Key.Split('|');string path=Output+"/"+building+"_"+s[0]+"_"+s[1]+".asset";var m=new Mesh{name=building+"_"+s[0]+"_"+s[1],indexFormat=IndexFormat.UInt32};m.CombineMeshes(entry.Value.ToArray(),true,true);m.RecalculateBounds();
   var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(m,old);Object.DestroyImmediate(m);m=old;m.UploadMeshData(false);EditorUtility.SetDirty(m);}else AssetDatabase.CreateAsset(m,path);
   var go=new GameObject(s[0]+"_"+s[1]);go.transform.SetParent(model,false);go.AddComponent<MeshFilter>().sharedMesh=m;go.AddComponent<MeshRenderer>().sharedMaterial=mats[s[1]];go.isStatic=true;
  }
  foreach(var mesh in temporary)Object.DestroyImmediate(mesh);
 }
 static void BrickFace(float x,float z,float y,float w,float h,bool side=false) {
  int rows=Mathf.CeilToInt(h/.19f);float bh=h/rows;
  for(int j=0;j<rows;j++){float start=-w/2;while(start<w/2-.01f){float length=Mathf.Min(j%2==1 && start==-w/2?.20f:.41f,w/2-start);float xx=start+length/2;var p=side?V(x,y+(j+.5f)*bh,z+xx):V(x+xx,y+(j+.5f)*bh,z);
    Box("00_Brick",new[]{"brick","brickLight","brickDark"}[(j*7+(int)((start+w)*14))%3],p,side?V(.022f,bh-.022f,length-.025f):V(length-.025f,bh-.022f,.022f));start+=length;}}
 }
 static void Wall(float x,float y,float z,float w,float h,float d,string mat,bool bricks=false) {
  Box("00_Walls",mat,V(x,y+h/2,z),V(w,h,d),true);
  if(bricks){BrickFace(x,z-d/2-.012f,y,w,h);BrickFace(x+w/2+.012f,z,y,d,h,true);BrickFace(x-w/2-.012f,z,y,d,h,true);BrickFace(x,z+d/2+.012f,y,w,h);}
 }
 static void Rail(Vector3 a,Vector3 b,float h=.82f,string color="metal") {
  Beam("30_Rails",color,a+V(0,h,0),b+V(0,h,0),.055f);Beam("30_Rails",color,a+V(0,.18f,0),b+V(0,.18f,0),.04f);
  int count=Mathf.CeilToInt(Vector3.Distance(a,b)/.25f);for(int i=0;i<=count;i++){var p=Vector3.Lerp(a,b,(float)i/count);Beam("30_Rails",color,p,p+V(0,h,0),.035f);}
 }
 static void Window(float x,float y,float z,float w,float h,bool arch=false,string frame="cream",bool bars=false) {
  Box("20_WindowFrames",frame,V(x,y,z),V(w+.17f,h+.16f,.13f));
  Box("20_Glass","glass",V(x,y,z-.076f),V(w,h,.045f));
  Box("20_Glass","glassLight",V(x-w*.22f,y+h*.06f,z-.103f),V(w*.09f,h*.76f,.01f));
  Box("20_WindowFrames",frame,V(x,y,z-.12f),V(.055f,h,.035f));Box("20_WindowFrames",frame,V(x,y-.1f,z-.12f),V(w,.045f,.035f));
  Box("20_WindowFrames","cream",V(x,y-h/2-.09f,z-.08f),V(w+.30f,.13f,.30f));
  if(arch){float r=w*.5f+.04f;for(int i=0;i<12;i++){float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;Beam("20_Arches","cream",V(x+Mathf.Cos(a)*r,y+h/2-.08f+Mathf.Sin(a)*r*.7f,z-.08f),V(x+Mathf.Cos(b)*r,y+h/2-.08f+Mathf.Sin(b)*r*.7f,z-.08f),.105f);}Box("20_Arches","cream",V(x,y+h/2+r*.7f-.03f,z-.10f),V(.17f,.23f,.18f));}
  if(bars){for(float xx=-w/2;xx<=w/2;xx+=.17f)Box("30_WindowBars","white",V(x+xx,y,z-.22f),V(.032f,h+.16f,.035f));Beam("30_WindowBars","white",V(x-w/2,y-h*.25f,z-.22f),V(x+w/2,y-h*.25f,z-.22f),.045f);}
 }
 static void Door(float x,float y,float z,string color="green",float w=.9f,float h=1.9f) {
  Box("20_Doors","cream",V(x,y+h/2,z),V(w+.18f,h+.14f,.12f));Box("20_Doors",color,V(x,y+h/2,z-.08f),V(w,h,.07f));Box("20_Doors","glass",V(x,y+h*.72f,z-.13f),V(w-.18f,h*.35f,.025f));Box("20_Doors","metal",V(x+w*.33f,y+.95f,z-.18f),V(.05f,.19f,.05f));
 }
 static void Pot(float x,float y,float z,float scale=1) {Cylinder("40_Pots","pot",V(x,y+.19f*scale,z),.20f*scale,.38f*scale);Cylinder("40_Pots","cream",V(x,y+.36f*scale,z),.225f*scale,.07f*scale);for(int i=0;i<5;i++){float a=i*2.4f;var p=V(x+Mathf.Cos(a)*.16f*scale,y+(.57f+i*.025f)*scale,z+Mathf.Sin(a)*.16f*scale);Beam("40_Plants","leaf",V(x,y+.35f*scale,z),p,.025f*scale);Sphere("40_Plants",i%2==0?"leaf":"leafLight",p,V(.28f,.22f,.23f)*scale);}}
 static void AC(float x,float y,float z) {Box("40_AirConditioners","cream",V(x,y,z),V(.85f,.62f,.40f));Box("40_AirConditioners","slate",V(x+.10f,y,z-.211f),V(.47f,.47f,.02f));for(int i=0;i<12;i++){float a=i*Mathf.PI/6;Beam("40_AirConditioners","metal",V(x+.10f,y,z-.24f),V(x+.10f+Mathf.Cos(a)*.21f,y+Mathf.Sin(a)*.21f,z-.24f),.028f);}for(int i=0;i<5;i++)Box("40_AirConditioners","metal",V(x-.31f,y-.20f+i*.10f,z-.22f),V(.16f,.025f,.025f));}
 static void Pipe(float x,float z,float height,string color="metal") {Beam("40_Pipes",color,V(x,.3f,z),V(x,height,z),.065f);for(float y=.6f;y<height;y+=1.2f)Box("40_Pipes","cream",V(x,y,z),V(.15f,.07f,.10f));}
 static void Text(string label,Vector3 p,float size,string mat) {
  var go=new GameObject("Sign_"+label);go.transform.SetParent(model,false);go.transform.localPosition=p+offset;
  var t=go.AddComponent<TextMesh>();t.text=label;t.fontSize=64;t.characterSize=.1f;t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;t.color=mats[mat].GetColor("_BaseColor");
  float renderedHeight=go.GetComponent<MeshRenderer>().bounds.size.y;
  if(renderedHeight>.001f)go.transform.localScale=Vector3.one*(size/renderedHeight);
 }
 static void Gosiwon() {
  float w=7.6f,d=5.2f,f=-d/2;
  Wall(0,0,0,w,9.5f,d,"mortar",true);
  Box("00_Placing","slate",V(0,.08f,0),V(w+.15f,.16f,d+.15f));
  for(int i=0;i<5;i++){float y=i==0?.35f:2.5f+(i-1)*2.32f;Box("10_Cornices","cream",V(0,y,0),V(w+.24f,.17f,d+.24f));Box("10_Cornices","mortar",V(0,y-.15f,0),V(w+.11f,.10f,d+.11f));}
  for(int floor=0;floor<3;floor++){float y=3.55f+floor*2.32f;for(int i=0;i<6;i++)Window(-3.02f+i*1.20f,y,f-.10f,.70f,1.35f,i!=2 && i!=3);}
  for(int i=0;i<7;i++)Box("10_Pilasters","cream",V(-3.66f+i*1.22f,5.94f,f-.055f),V(.085f,6.62f,.12f));
  // Side windows, using a temporary quarter-turn of the front-facing pieces.
  for(int floor=0;floor<3;floor++)for(int i=0;i<3;i++)SideWindow(w/2+.07f,3.55f+floor*2.32f,-1.65f+i*1.6f,.65f,1.35f);
  for(int i=0;i<3;i++){float x=-2.5f+i*2.5f;Box("20_Shops","green",V(x,1.23f,f-.11f),V(2.24f,2.14f,.18f));if(i<2){for(int k=0;k<3;k++)Window(x+(k-1)*.68f,1.22f,f-.24f,.56f,1.65f,false,"green");}else{Box("20_Shutter","slate",V(x,1.17f,f-.25f),V(2.05f,1.90f,.09f));for(int k=0;k<20;k++)Box("20_Shutter","concrete",V(x,.3f+k*.088f,f-.31f),V(2.04f,.032f,.025f));}}
  Door(.42f,.08f,f-.32f,"green",.8f,1.91f);
  for(int i=0;i<12;i++){float x=-3.65f+i*.195f;Add("30_Awning",i%2==0?"white":"red",cube,V(x,2.28f,f-.44f),V(.197f,.10f,.83f),Quaternion.Euler(-12,0,0));Box("30_Awning",i%2==0?"white":"red",V(x,2.10f,f-.83f),V(.197f,.22f,.07f));}
  Box("30_Sign","green",V(.14f,2.29f,f-.31f),V(2.6f,.38f,.16f));Text("달빛 고시원",V(.14f,2.3f,f-.405f),.23f,"white");
  Box("10_Roof","slate",V(0,9.58f,0),V(w+.28f,.19f,d+.28f));
  for(int side=-1;side<=1;side+=2){Box("10_Parapet","cream",V(side*(w/2+.06f),9.87f,0),V(.16f,.48f,d+.3f));Box("10_Parapet","cream",V(0,9.87f,side*(d/2+.06f)),V(w+.3f,.48f,.16f));}
  Wall(.3f,9.7f,1.2f,1.85f,1.8f,1.55f,"brick");Door(.3f,9.72f,.39f,"blue",.76f,1.6f);Box("10_RoofHut","cream",V(.3f,11.55f,1.2f),V(2.04f,.19f,1.72f));
  AC(-2.6f,10.04f,.7f);AC(-1.5f,10.04f,.7f);AC(2.7f,10.04f,1.5f);
  Cylinder("40_RoofTank","green",V(-2.6f,10.23f,1.9f),.43f,1.0f);Cylinder("40_RoofTank","slate",V(-2.6f,10.76f,1.9f),.46f,.12f);
  for(int i=0;i<3;i++)Pot(1.5f+i*.53f,9.7f,-1.4f,.8f);
  Box("40_RoofBench","wood",V(1.9f,10.0f,-.55f),V(1.6f,.12f,.40f));Box("40_RoofBench","wood",V(1.9f,10.32f,-.33f),V(1.6f,.52f,.1f));
  Rail(V(2,9.7f,2.4f),V(3.7f,9.7f,2.4f));Pipe(-3.7f,-2.75f,9.45f);
  for(float x=-2.9f;x<3.5f;x+=.38f)Box("10_Dentils","cream",V(x,9.17f,f-.14f),V(.15f,.2f,.2f));
 }
 static void SideWindow(float x,float y,float z,float w,float h) {
  Box("20_SideWindows","cream",V(x,y,z),V(.12f,h+.18f,w+.16f));Box("20_SideWindows","glass",V(x+.08f,y,z),V(.04f,h,w));Box("20_SideWindows","cream",V(x+.11f,y,z),V(.04f,h,.05f));Box("20_SideWindows","cream",V(x+.11f,y-.12f,z),V(.04f,.06f,w));
 }
 static void Convenience() {
  float f=-2.38f;Wall(0,0,.22f,7.9f,4.45f,4.65f,"concrete");
  Box("00_Fascia","slate",V(0,4.46f,.2f),V(8.1f,.18f,4.85f));
  Box("20_Shopfront","metal",V(0,1.45f,f),V(7.78f,2.80f,.24f));
  for(int i=0;i<6;i++){float x=-3.17f+i*1.27f;Box("20_Shopfront","warm",V(x,1.45f,f-.14f),V(1.17f,2.59f,.035f));Box("20_FrostedGlass","glassLight",V(x,.65f,f-.17f),V(1.17f,.95f,.02f));Box("20_GlassReflections","white",V(x-.37f,1.93f,f-.18f),V(.055f,.78f,.02f));Box("20_WindowStripe","white",V(x,1.15f,f-.21f),V(1.17f,.14f,.025f));for(int k=0;k<6;k++)Box("20_WindowStripe",k%2==0?"green":"orange",V(x-.45f+k*.18f,1.15f,f-.23f),V(.095f,.045f,.018f));}
  // Double door and transom reproduce the narrow framed entrance in reference 2.
  Box("20_DoorFrame","metal",V(1.23f,2.51f,f-.22f),V(2.48f,.08f,.09f));
  for(float x=.56f;x<2;x+=1.29f)Box("20_DoorHandles","metal",V(x,.99f,f-.29f),V(.045f,.47f,.10f));
  Box("30_Lightbox","metal",V(0,3.28f,f-.08f),V(8.15f,.94f,.38f));Box("30_Lightbox","white",V(0,3.28f,f-.285f),V(8.01f,.80f,.045f));
  foreach(int side in new[]{-1,1}){float x=side*2.46f;Box("30_StoreStripes","yellow",V(x,3.54f,f-.32f),V(2.65f,.11f,.025f));Box("30_StoreStripes","green",V(x,3.30f,f-.32f),V(2.65f,.26f,.025f));Box("30_StoreStripes","orange",V(x,3.08f,f-.32f),V(2.65f,.10f,.025f));}
  Box("30_SevenLogo","orange",V(0,3.59f,f-.345f),V(.53f,.13f,.04f));Beam("30_SevenLogo","orange",V(.19f,3.57f,f-.345f),V(-.02f,3.16f,f-.345f),.12f);
  Text("ELEVEN",V(0,3.08f,f-.38f),.12f,"green");Text("24",V(1.27f,1.72f,f-.25f),.24f,"orange");
  Box("30_BladeSign","green",V(3.94f,3.85f,-2.9f),V(.18f,1.02f,1.1f));Box("30_BladeSign","white",V(4.045f,3.85f,-2.9f),V(.04f,.90f,.95f));
  Box("30_BladeSign","orange",V(4.08f,4.12f,-2.9f),V(.03f,.12f,.47f));Beam("30_BladeSign","orange",V(4.08f,4.11f,-2.72f),V(4.08f,3.70f,-2.92f),.10f);Box("30_BladeSign","green",V(4.08f,3.61f,-2.9f),V(.03f,.07f,.43f));Box("30_BladeSign","orange",V(4.08f,3.49f,-2.9f),V(.03f,.13f,.10f));
  AC(-2.65f,3.98f,f-.04f);AC(-1.50f,3.98f,f-.04f);Pipe(3.65f,f-.26f,4.3f);
  Box("00_Threshold","concrete",V(1.28f,.045f,f-.50f),V(2.45f,.09f,.67f),true);
  Box("40_Crate","green",V(-3.53f,.26f,f-.49f),V(.48f,.52f,.5f));Pot(3.52f,0,f-.5f,.65f);
 }
 static void HipRoof(float x,float y,float z,float w,float d) {
  float rise=.85f;var v=new[]{V(-w/2,0,-d/2),V(w/2,0,-d/2),V(w/2,0,d/2),V(-w/2,0,d/2),V(-w*.18f,rise,0),V(w*.18f,rise,0)};
  Add("10_TiledRoof","roof",Mesh("hip",v,new[]{0,4,5,0,5,1,1,5,2,2,5,4,2,4,3,3,4,0}),V(x,y,z),Vector3.one,Quaternion.identity);
  for(int side=-1;side<=1;side+=2){for(float xx=-w/2+.1f;xx<w/2;xx+=.19f){float endX=Mathf.Clamp(xx,-w*.18f,w*.18f);Beam("10_TileRibs","roofLight",V(x+xx,y+.018f,z+side*d/2),V(x+endX,y+rise+.018f,z),.043f);}for(int row=1;row<7;row++){float t=row/7f;float half=Mathf.Lerp(w/2,w*.18f,t);Beam("10_TileCourses","roofLight",V(x-half,y+rise*t+.02f,z+side*d/2*(1-t)),V(x+half,y+rise*t+.02f,z+side*d/2*(1-t)),.035f);}}
  Beam("10_Ridge","roof",V(x-w*.18f-.13f,y+rise+.04f,z),V(x+w*.18f+.13f,y+rise+.04f,z),.16f);
  foreach(int side in new[]{-1,1})for(float zz=-d/2+.1f;zz<d/2;zz+=.2f)Beam("10_TileRibs","roofLight",V(x+side*w/2,y+.075f,z+zz),V(x+side*w*.18f,y+rise+.075f,z),.065f);
  foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})Beam("10_Hips","roofLight",V(x+sx*w/2,y+.03f,z+sz*d/2),V(x+sx*w*.18f,y+rise+.03f,z),.11f);
  Box("10_Eaves","cream",V(x,y-.07f,z),V(w,.12f,d));
 }
 static void Stairs(Vector3 a,Vector3 b,float width,string group="30_Stairs",bool collider=true) {
  var delta=b-a;var horizontal=V(delta.x,0,delta.z);int n=Mathf.CeilToInt(Mathf.Abs(delta.y)/.17f);var rot=Quaternion.LookRotation(horizontal);float len=horizontal.magnitude;
  for(int i=0;i<n;i++){var p=a+horizontal*(i+.5f)/n;float top=a.y+delta.y*(i+1)/n;Add(group,"concrete",cube,V(p.x,top-.10f,p.z),V(width,.20f,len/n+.02f),rot);}
  if(collider){var side=Vector3.Cross(Vector3.up,horizontal.normalized)*width/2;var vs=new[]{a-side,a+side,b-side,b+side,a-side-V(0,.25f,0),a+side-V(0,.25f,0),b-side-V(0,.25f,0),b+side-V(0,.25f,0)};var m=Mesh("walkable_stair_wedge",vs,new[]{0,2,1,1,2,3,4,5,6,5,7,6,0,4,2,2,4,6,1,3,5,3,7,5,0,1,4,1,5,4,2,6,3,3,6,7});for(int i=0;i<vs.Length;i++)vs[i]+=offset;m.vertices=vs;m.RecalculateBounds();string path=Output+"/"+building+"_StairCollision.asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(m,old);EditorUtility.SetDirty(old);}else{AssetDatabase.CreateAsset(Object.Instantiate(m),path);old=AssetDatabase.LoadAssetAtPath<Mesh>(path);}var go=new GameObject("Walkable_StairRamp");go.transform.SetParent(model,false);go.AddComponent<MeshCollider>().sharedMesh=old;}
 }
 static void Home(bool tiled,int variant,float width,float depth) {
  float f=-depth/2;float bodyW=width-1.30f;float bx=-.65f;
  if(tiled){
   Wall(bx,0,0,bodyW,2.25f,depth,"plaster");Wall(bx,2.25f,0,bodyW,2.35f,depth,"mortar",true);
   HipRoof(bx,4.64f,0,bodyW+.4f,depth+.36f);
   Window(-1.42f,1.27f,f-.10f,1.34f,.96f,false,"metal",true);Door(.57f,.05f,f-.11f,variant==2?"green":"blue",.82f,1.94f);
   Window(-1.45f,3.43f,f-.10f,1.12f,1.10f,false,"metal");Window(.23f,3.43f,f-.10f,.87f,1.1f,false,"metal");
   Box("30_Balcony","concrete",V(.14f,2.86f,f-.38f),V(1.37f,.12f,.62f));Rail(V(-.52f,2.91f,f-.68f),V(.81f,2.91f,f-.68f),.63f);Pot(-.28f,2.92f,f-.36f,.58f);Pot(.38f,2.92f,f-.36f,.57f);
   AC(-2.0f,2.58f,f-.28f);Pipe(-bodyW/2+bx+.14f,f-.2f,4.52f);
   // A narrow exterior stair and upper landing remain inside the original lot.
   float sx=width/2-.56f;Stairs(V(sx,0,f+.10f),V(sx,2.32f,depth/2-.67f),1.06f);Rail(V(sx+.51f,0,f+.10f),V(sx+.51f,2.32f,depth/2-.67f));Box("30_Landing","concrete",V(sx,2.24f,depth/2-.32f),V(1.12f,.16f,.74f),true);Rail(V(sx+.5f,2.32f,depth/2-.69f),V(sx+.5f,2.32f,depth/2+.01f));
   float doorX=bx+bodyW/2+.025f;Box("20_UpperEntry","cream",V(doorX,3.27f,depth/2-.53f),V(.08f,1.98f,.81f));Box("20_UpperEntry","green",V(doorX+.06f,3.27f,depth/2-.53f),V(.05f,1.85f,.70f));Box("20_UpperEntry","glass",V(doorX+.10f,3.64f,depth/2-.53f),V(.025f,.57f,.53f));Box("30_EntryCanopy","green",V(doorX+.33f,4.30f,depth/2-.53f),V(.76f,.10f,1.07f));
   Box("40_Meter","cream",V(.94f,1.54f,f-.17f),V(.18f,.29f,.10f));Pot(.97f,0,f-.36f,.65f);
   // Small ochre wall mural, kept geometric and legible at gameplay distance.
   for(int i=0;i<2;i++){float x=-2.04f+i*.72f;Box("40_Mural","orange",V(x,.55f,f-.033f),V(.48f,.26f,.012f));Sphere("40_Mural","orange",V(x+.23f,.75f,f-.04f),V(.21f,.23f,.022f));for(int j=-1;j<=1;j+=2)Beam("40_Mural","orange",V(x+j*.17f,.49f,f-.04f),V(x+j*.2f,.23f,f-.04f),.075f);}
  } else {
   // Reference 4: two brick volumes, a small enclosed yard, concrete flat roofs.
   float mainW=width*.57f,left=-width/2+mainW/2;
   Wall(left,0,.10f,mainW,4.7f,depth-.20f,"mortar",true);Box("10_FlatRoof","cream",V(left,4.77f,.10f),V(mainW+.22f,.22f,depth+.02f));
   float right=width/2-.78f;Wall(right,0,depth*.27f,1.55f,4.2f,depth*.46f,"mortar",true);Box("10_FlatRoof","cream",V(right,4.29f,depth*.27f),V(1.75f,.22f,depth*.46f+.22f));
   Window(left,3.48f,f-.045f,mainW-.6f,1.54f,false,"wood");Door(left-.5f,.02f,f-.05f,"wood",.82f,1.95f);Window(left+.63f,1.30f,f-.07f,.64f,.88f,false,"wood");
   Box("30_Balcony","cream",V(left,2.62f,f-.30f),V(mainW-.3f,.13f,.59f));Rail(V(left-mainW/2+.17f,2.69f,f-.59f),V(left+mainW/2-.17f,2.69f,f-.59f),.65f,"cream");Box("40_Laundry","green",V(left-.40f,3.08f,f-.62f),V(.50f,.65f,.055f));
   Box("00_Yard","concrete",V(right,.04f,-.80f),V(1.8f,.08f,2.4f));
   float gateZ=f+.14f;foreach(float xx in new[]{width/2-1.8f,width/2-.07f}){Wall(xx,0,gateZ,.28f,1.58f,.34f,"cream");Box("30_GateCap","cream",V(xx,1.65f,gateZ),V(.39f,.14f,.45f));}
   Rail(V(width/2-1.62f,.1f,gateZ),V(width/2-.25f,.1f,gateZ),1.35f,"wood");Beam("30_Gate","wood",V(width/2-1.65f,.78f,gateZ),V(width/2-.24f,.78f,gateZ),.09f);
   float stairX=width/2-1.7f;Stairs(V(stairX,.10f,f+.58f),V(stairX,2.30f,depth/2-.56f),.74f);Rail(V(stairX+.38f,.1f,f+.58f),V(stairX+.38f,2.3f,depth/2-.56f),.70f);
   Window(right,3.22f,depth*.04f-.08f,1.04f,1.12f,false,"wood");Pot(right,.08f,f+.7f,.75f);Pot(right,2.3f,depth/2-.7f,.65f);
   Pipe(left-mainW/2+.12f,f-.12f,4.6f);Beam("40_GasPipe","yellow",V(left-.6f,.12f,f-.12f),V(left-.6f,2.31f,f-.12f),.032f);Beam("40_GasPipe","yellow",V(left-.6f,2.31f,f-.12f),V(left+mainW/2,2.31f,f-.12f),.032f);
   for(int i=0;i<7;i++)Sphere("40_Ivy",i%2==0?"leaf":"leafLight",V(width/2-.14f,2.0f+i*.28f,depth*.04f-.10f),V(.32f,.34f,.12f));
   Box("40_Bench","white",V(left,.36f,f-.35f),V(1.43f,.1f,.4f));for(int i=-1;i<=1;i+=2)Box("40_Bench","white",V(left+i*.51f,.17f,f-.35f),V(.09f,.34f,.3f));Pot(left+1.1f,0,f-.35f,.58f);
  }
 }
 public static object Build()=>BuildSubset(null);
 public static object BuildOnly(string name)=>BuildSubset(new[]{name});
 static object BuildSubset(string[] selected) {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play mode first.");
  for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new Exception("Unsaved scene changes; save before remodeling.");
  Palette();
  string[] names={"Gosiwon","Convenience","Home_A","Home_B","Home_C","Home_D"};
  if(selected!=null&&selected.Any(n=>!names.Contains(n)))throw new ArgumentException("Unknown reference building.");
  Vector3[] centers={V(-11.5f,12,24.1f),V(-16,3,-.4f),V(7.6f,9,19.5f),V(20.5f,9,21.3f),V(23.5f,6,10.7f),V(7.4f,12,27.1f)};
  var village=AssetDatabase.LoadAssetAtPath<GameObject>(Village);
  Directory.CreateDirectory("Temp/ReferenceBuildingsBackup");
  for(int i=0;i<names.Length;i++){
   if(selected!=null&&!selected.Contains(names[i]))continue;
   string path=Base+"/Prefabs/Buildings/"+names[i]+".prefab";string backup="Temp/ReferenceBuildingsBackup/"+names[i]+".prefab";if(!File.Exists(backup))File.Copy(path,backup);
   Vector3 pivot=village.transform.Find("10_Buildings/"+names[i]).position;
   var root=PrefabUtility.LoadPrefabContents(path);
   try{foreach(Transform c in root.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(c.gameObject);
    Begin(root.transform,names[i],centers[i]-pivot); // Prefab root is at zero; keep the existing nested pivot.
    if(i==0)Gosiwon();else if(i==1)Convenience();else Home(i==2||i==4,i,i==2?6f:i==3?5.6f:i==4?5.7f:5.8f,i==2?5f:i==4?4.7f:4.8f);
    Finish();if(!PrefabUtility.SaveAsPrefabAsset(root,path))throw new Exception("Save failed: "+path);
   }finally{PrefabUtility.UnloadPrefabContents(root);}
  }
  AssetDatabase.SaveAssets();return new{success=true,buildings=selected??names};
 }
}
