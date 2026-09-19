using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Local-space authored architecture, fronts face -Z. No scene reload or save is
// required: nested prefab references receive the edit without losing scene work.
public static class ShopRemodel
{
 const string Root="Assets/Art/Daldongne/WarmVillage";
 const string Output=Root+"/ShopRemodel";
 const string Village=Root+"/DaldongneWarmTown.prefab";
 static Transform model;
 static string building;
 static Vector3 origin;
 static Dictionary<string,Material> materials;
 static Dictionary<string,List<CombineInstance>> groups;
 static List<Mesh> transient;
 static Mesh cube;
 static int serial;
 static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
 static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;var parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
 static void Palette(){
  Folder(Output);materials=new Dictionary<string,Material>();
  string[] colors={"wall:#ACAFA1","wallDark:#858B85","wallLight:#C3C3B0","concrete:#92948D","cream:#ECE1C0","green:#337C64","greenDark:#294C40","mint:#4F967C","metal:#454C44","roof:#50534B","rust:#966844","white:#ECE8D8","red:#B95140","yellow:#CDB143","blue:#317E9E","bottle:#5C7850","wood:#654A37","woodLight:#937155","woodDark:#3B2F29","woodMid:#79573E","ochre:#C7984C","warm:#EFD6A0","window:#5F7469","windowDark:#344744","black:#2A332E","chalk:#DFDBBF","bread:#CB954F","breadLight:#E2B871","pot:#98644C","leaf:#5C7249"};
  foreach(var pair in colors){var s=pair.Split(':');string path=Output+"/"+s[0]+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(!mat){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}ColorUtility.TryParseHtmlString(s[1],out var color);mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",s[0].StartsWith("window")?.45f:.13f);if(s[0]=="warm"){mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",color*.22f);}EditorUtility.SetDirty(mat);materials[s[0]]=mat;}
 }
 static void Begin(Transform t,string name,Vector3 offset){model=t;building=name;origin=offset;groups=new Dictionary<string,List<CombineInstance>>();transient=new List<Mesh>();serial=0;var temp=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=temp.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(temp);}
 static Mesh Mesh(string name,Vector3[] v,int[] tris){var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.vertices=v;mesh.triangles=tris;mesh.RecalculateNormals();mesh.RecalculateBounds();transient.Add(mesh);return mesh;}
 static void Add(string group,string mat,Mesh mesh,Vector3 p,Vector3 s,Quaternion q){var key=group+"|"+mat;if(!groups.ContainsKey(key))groups[key]=new List<CombineInstance>();groups[key].Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(p+origin,q,s)});}
 static void Box(string group,string mat,Vector3 p,Vector3 size,bool solid=false){Add(group,mat,cube,p,size,Quaternion.identity);if(solid){var go=new GameObject("Solid_"+group+"_"+serial++);go.transform.SetParent(model,false);go.transform.localPosition=p+origin;go.AddComponent<BoxCollider>().size=size;}}
 static void Beam(string group,string mat,Vector3 a,Vector3 b,float thickness){Add(group,mat,cube,(a+b)/2,V(thickness,thickness,Vector3.Distance(a,b)),Quaternion.LookRotation(b-a));}
 static void Round(string group,string mat,Vector3 p,Vector3 size){
  const int rings=5,sides=8;var v=new List<Vector3>();var tri=new List<int>();for(int r=0;r<=rings;r++){float phi=Mathf.PI*r/rings;for(int i=0;i<sides;i++){float a=2*Mathf.PI*i/sides;v.Add(V(Mathf.Sin(phi)*Mathf.Cos(a),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(a))*.5f);}}for(int r=0;r<rings;r++)for(int i=0;i<sides;i++){int a=r*sides+i,b=r*sides+(i+1)%sides;tri.AddRange(new[]{a,b,a+sides,b,b+sides,a+sides});}Add(group,mat,Mesh(group,v.ToArray(),tri.ToArray()),p,size,Quaternion.identity);
 }
 static void Text(string label,Vector3 p,float height,string mat,float maxWidth=0){
  var go=new GameObject("Lettering_"+label.Replace('\n',' '));go.transform.SetParent(model,false);go.transform.localPosition=p+origin;var text=go.AddComponent<TextMesh>();text.text=label;text.fontSize=96;text.characterSize=.10f;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=materials[mat].GetColor("_BaseColor");var bounds=go.GetComponent<Renderer>().bounds;float scale=height/Mathf.Max(.001f,bounds.size.y);if(maxWidth>0)scale=Mathf.Min(scale,maxWidth/Mathf.Max(.001f,bounds.size.x));go.transform.localScale=Vector3.one*scale;
 }
 static void Finish(){foreach(var entry in groups){var p=entry.Key.Split('|');var mesh=new Mesh{name=building+"_"+p[0]+"_"+p[1],indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(entry.Value.ToArray(),true,true);mesh.RecalculateBounds();string path=Output+"/"+mesh.name+".asset";var previous=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(previous){EditorUtility.CopySerialized(mesh,previous);Object.DestroyImmediate(mesh);mesh=previous;mesh.UploadMeshData(false);EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);var go=new GameObject(p[0]+"_"+p[1]);go.transform.SetParent(model,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=materials[p[1]];go.isStatic=true;}foreach(var mesh in transient)Object.DestroyImmediate(mesh);}
 static void SlopedAwning(float x,float y,float z,float width,float depth,string stripe,int count,bool posts=false){
  for(int i=0;i<count;i++){float xx=x-width/2+width*(i+.5f)/count;string mat=i%2==0?stripe:"white";Add("30_Awning",mat,cube,V(xx,y,z-depth/2),V(width/count+.003f,.065f,depth),Quaternion.Euler(-13,0,0));float low=y-Mathf.Sin(13*Mathf.Deg2Rad)*depth/2;Box("30_AwningValance",mat,V(xx,low-.10f,z-depth+.018f),V(width/count+.004f,.19f,.05f));Round("30_AwningHem",mat,V(xx,low-.18f,z-depth+.006f),V(width/count+.004f,.13f,.045f));}
  Beam("30_AwningFrame","metal",V(x-width/2,y+.05f,z),V(x+width/2,y+.05f,z),.055f);
  foreach(int side in new[]{-1,1}){float xx=x+side*(width/2-.07f);Beam("30_AwningFrame","metal",V(xx,y-.63f,z),V(xx,y-.12f,z-depth+.03f),.045f);if(posts)Beam("30_AwningPosts","metal",V(xx,.07f,z-depth+.03f),V(xx,y-.1f,z-depth+.03f),.045f);}
 }
 static void Pot(float x,float y,float z,float size=.4f){Box("40_Pots","pot",V(x,y+size*.42f,z),V(size*.70f,size*.84f,size*.70f));Box("40_Pots","cream",V(x,y+size*.84f,z),V(size*.8f,.06f,size*.8f));for(int i=0;i<5;i++){float a=i*2.4f;var top=V(x+Mathf.Cos(a)*size*.23f,y+size*(1.25f+.05f*i),z+Mathf.Sin(a)*size*.23f);Beam("40_Plants","leaf",V(x,y+size*.8f,z),top,.023f);Round("40_Plants","leaf",top,V(.24f,.20f,.2f));}}
 static void Frame(float x,float y,float z,float width,float height,string material="metal",float thickness=.085f){Box("20_Frames",material,V(x-width/2,y,z),V(thickness,height,.12f));Box("20_Frames",material,V(x+width/2,y,z),V(thickness,height,.12f));Box("20_Frames",material,V(x,y-height/2,z),V(width,thickness,.12f));Box("20_Frames",material,V(x,y+height/2,z),V(width,thickness,.12f));}
 static void Crate(float x,float y,float z,string color,float width=.7f){
  Box("40_Crates",color,V(x,y+.035f,z),V(width,.07f,.48f));foreach(int side in new[]{-1,1}){Box("40_Crates",color,V(x,y+.38f,z+side*.24f),V(width,.075f,.045f));for(int i=0;i<6;i++)Box("40_Crates",color,V(x-width/2+.05f+i*(width-.1f)/5,y+.23f,z+side*.24f),V(.035f,.27f,.04f));Box("40_Crates",color,V(x+side*width/2,y+.20f,z),V(.045f,.4f,.48f));}for(int i=0;i<4;i++){float xx=x-width*.33f+i*width*.22f;Round("40_Bottles","bottle",V(xx,y+.22f,z),V(.13f,.32f,.13f));Box("40_Bottles","cream",V(xx,y+.39f,z),V(.056f,.08f,.056f));}
 }
 static void Market(){
  const float w=7.5f,d=4.2f,f=-2.1f,h=3.8f;
  Box("00_Shell","wall",V(0,h/2,0),V(w,h,d),true);Box("00_Plinth","concrete",V(0,.10f,0),V(w+.08f,.20f,d+.08f));
  // Low, shallow gable roof with visible rafters and narrow corrugated ribs.
  for(int side=-1;side<=1;side+=2){var q=Quaternion.Euler(side*9,0,0);Add("10_SheetRoof","roof",cube,V(0,4.02f,side*1.12f),V(w+.45f,.12f,2.33f),q);for(float x=-3.88f;x<3.94f;x+=.21f)Beam("10_RoofRibs","metal",V(x,4.25f,0),V(x,3.90f,side*2.31f),.036f);}
  Beam("10_Ridge","roof",V(-3.95f,4.26f,0),V(3.95f,4.26f,0),.12f);Box("10_FrontGutter","wallLight",V(0,3.82f,f-.20f),V(w+.35f,.16f,.16f));
  foreach(int side in new[]{-1,1}){float x=side*w/2;var gable=Mesh("Gable",new[]{V(x,3.77f,-2.15f),V(x,3.77f,2.15f),V(x,4.21f,0)},side>0?new[]{0,2,1}:new[]{0,1,2});Add("10_Gable","wall",gable,Vector3.zero,Vector3.one,Quaternion.identity);}
  for(float x=-3.65f;x<3.7f;x+=.62f)Box("10_RafterEnds","woodDark",V(x,3.77f,f-.11f),V(.11f,.12f,.23f));
  // Uneven wall repairs and water streaks are authored as thin plaster pieces.
  for(int i=0;i<25;i++){float x=-3.62f+((i*29)%73)*.10f;float y=3.42f+(i%4)*.07f;Box("00_Weathering",i%3==0?"wallDark":"wallLight",V(x,y,f-.014f),V(.028f+(i%3)*.035f,.16f+(i%5)*.06f,.017f));}
  for(int i=0;i<18;i++)Box("00_SideWeathering",i%3==0?"wallDark":"wallLight",V(w/2+.012f,.4f+(i%5)*.62f,-1.82f+(i*.29f)%3.7f),V(.022f,.24f+(i%3)*.13f,.06f+(i%4)*.04f));
  Box("20_Recess","woodDark",V(-.05f,1.6f,f-.026f),V(7.09f,2.96f,.045f));
  for(int i=0;i<6;i++){float x=-3.03f+i*1.20f;Box("20_Glass",i<3?"windowDark":"window",V(x,1.56f,f-.082f),V(1.1f,2.60f,.045f));Frame(x,1.56f,f-.13f,1.16f,2.69f,"metal",.067f);Box("20_FrostedPanels","wallLight",V(x,.73f,f-.118f),V(1.09f,.62f,.02f));Box("20_GlassGlint","wallLight",V(x-.39f,2.3f,f-.12f),V(.025f,.62f,.012f));}
  for(float x=-.68f;x<3.54f;x+=.11f)Beam("20_DoorMesh","wallDark",V(x,.4f,f-.152f),V(Mathf.Min(3.55f,x+.65f),1.07f,f-.152f),.008f);
  for(int i=0;i<3;i++){float x=.57f+i*1.20f;Box("30_WindowPosters",i==1?"blue":"green",V(x,1.96f,f-.16f),V(.65f,1.10f,.012f));Text(i==0?"쌀 · 라면\n생필품":i==1?"시원한\n음료수":"과자 · 우유\n아이스크림",V(x,1.98f,f-.18f),.40f,"white",.57f);}
  Text("담배",V(-2.45f,2.04f,f-.16f),.23f,"cream",.64f);Text("슈퍼",V(-1.24f,1.53f,f-.16f),.23f,"cream",.64f);
  Box("20_Handles","cream",V(.02f,1.39f,f-.22f),V(.035f,.36f,.06f));Box("20_Handles","cream",V(.20f,1.39f,f-.22f),V(.035f,.36f,.06f));
  // Right half has a shallow metal roller box; the left has a fabric awning.
  Box("30_RightFascia","wallLight",V(1.82f,3.15f,f-.12f),V(3.70f,.45f,.28f));foreach(var band in new[]{new{y=3.28f,c="red"},new{y=3.18f,c="yellow"},new{y=3.08f,c="green"}})Box("30_FasciaStripes",band.c,V(1.82f,band.y,f-.27f),V(3.68f,.035f,.028f));
  SlopedAwning(-1.84f,2.97f,f-.13f,3.93f,.88f,"green",16,true);
  Box("30_SignFrame","metal",V(-1.83f,3.75f,f-.19f),V(3.89f,.94f,.18f));Box("30_SignFace","greenDark",V(-1.83f,3.75f,f-.29f),V(3.76f,.80f,.028f));
  // Leave the main lettering clear while weathering the perimeter of the old sign.
  for(int i=0;i<28;i++){float x=-3.63f+(i*19%36)*.10f;float y=i%2==0?4.09f:3.42f;Box("30_SignPeeling",i%3==0?"rust":"wallLight",V(x,y,f-.313f),V(.02f+(i%4)*.023f,.04f+(i%5)*.018f,.008f));}
  Text("연희네 슈퍼",V(-1.58f,3.75f,f-.33f),.45f,"cream",2.96f);Box("30_SignEmblem","red",V(-3.29f,3.75f,f-.33f),V(.40f,.52f,.02f));Text("쌀",V(-3.29f,3.75f,f-.36f),.26f,"white",.32f);
  // Exterior chest freezer, lid, compressor grill and a simple ice-cream emblem.
  Box("40_Freezer","white",V(-1.78f,.58f,f-.67f),V(1.44f,1.04f,.83f));Box("40_FreezerLid","wallLight",V(-1.78f,1.13f,f-.67f),V(1.50f,.09f,.90f));Frame(-1.78f,.64f,f-1.095f,1.14f,.70f,"red",.035f);Text("아이스크림",V(-1.78f,.86f,f-1.15f),.12f,"blue",1.0f);Text("ICE",V(-1.78f,.55f,f-1.15f),.27f,"red",.7f);for(int i=0;i<9;i++)Box("40_FreezerVent","metal",V(-1.83f+i*.052f,.17f,f-1.10f),V(.027f,.09f,.017f));
  Box("40_FreezerFeet","metal",V(-2.26f,.04f,f-.66f),V(.16f,.08f,.7f));Box("40_FreezerFeet","metal",V(-1.3f,.04f,f-.66f),V(.16f,.08f,.7f));
  Crate(2.85f,0,f-.51f,"green",.76f);Crate(2.85f,.42f,f-.51f,"blue",.76f);Crate(2.05f,0,f-.47f,"yellow",.69f);Crate(2.05f,.42f,f-.47f,"yellow",.69f);Text("음료",V(2.05f,.6f,f-.735f),.16f,"red",.5f);
  for(int i=0;i<3;i++)Box("40_Pallet","wood",V(.87f,.14f+i*.19f,f-.4f),V(.75f,.095f,.53f));
  foreach(float xx in new[]{-3.43f,-2.90f})Beam("40_Stool","woodDark",V(xx,.02f,f-.50f),V(xx,.52f,f-.57f),.055f);Box("40_Stool","woodLight",V(-3.165f,.52f,f-.54f),V(.69f,.07f,.42f));
  Beam("40_Drainpipe","wallDark",V(3.60f,.10f,f-.12f),V(3.60f,3.72f,f-.12f),.09f);Beam("40_GlobeBracket","metal",V(3.38f,3.09f,f),V(3.38f,3.09f,f-.45f),.07f);Round("40_GlobeLight","warm",V(3.38f,2.92f,f-.43f),V(.30f,.31f,.30f));Box("40_VerticalPlacard","blue",V(3.39f,2.50f,f-.18f),V(.20f,.48f,.06f));
  // Side service window and small lean-to, both within the old footprint.
  Box("20_SideWindow","metal",V(3.76f,2.1f,.56f),V(.09f,1.05f,1.40f));Box("20_SideWindow","windowDark",V(3.82f,2.1f,.56f),V(.035f,.9f,1.25f));for(int i=0;i<7;i++)Box("20_SideBars","cream",V(3.85f,2.1f,-.02f+i*.19f),V(.04f,.94f,.034f));
 }
 static void Loaf(float x,float y,float z,float scale=1){Round("40_Bread","bread",V(x,y,z),V(.37f,.19f,.22f)*scale);for(int i=-1;i<=1;i++)Beam("40_LoafScoring","breadLight",V(x+i*.075f*scale-.025f*scale,y+.085f*scale,z-.055f*scale),V(x+i*.075f*scale+.025f*scale,y+.085f*scale,z+.06f*scale),.017f*scale);}
 static void Bakery(){
  const float w=6.45f,d=4.55f,f=-2.275f,h=7.20f;
  Box("00_Shell","woodDark",V(0,h/2,0),V(w,h,d),true);Box("00_StoneBase","concrete",V(0,.10f,0),V(w+.1f,.20f,d+.10f));
  // Timber infill with individually spaced horizontal courses, rather than brick.
  for(int i=0;i<42;i++){float y=.26f+i*.162f;Box("00_FrontCladding",i%4==0?"woodLight":"woodMid",V(0,y,f-.019f),V(w-.16f,.153f,.04f));foreach(int side in new[]{-1,1})Box("00_SideCladding",i%3==0?"wood":"woodMid",V(side*(w/2+.019f),y,0),V(.04f,.151f,d-.08f));}
  foreach(float x in new[]{-3.09f,3.09f}){Box("00_FrontPilasters","woodDark",V(x,3.49f,f-.13f),V(.23f,6.96f,.24f));Box("00_PilasterInlay","woodLight",V(x,4.9f,f-.26f),V(.075f,3.84f,.035f));}
  foreach(float y in new[]{.3f,3.12f,3.58f,6.59f,7.09f})Box("10_TimberBands","woodDark",V(0,y,0),V(w+.14f,.18f,d+.12f));
  Box("10_Cornice","woodLight",V(0,7.04f,0),V(w+.33f,.16f,d+.3f));Box("10_Roof","roof",V(0,7.24f,0),V(w+.47f,.24f,d+.50f));
  Box("10_FrontEave","woodDark",V(0,6.84f,f-.20f),V(w+.3f,.12f,.45f));for(float x=-2.8f;x<=2.8f;x+=.49f)Box("10_EaveBrackets","woodLight",V(x,6.77f,f-.26f),V(.16f,.24f,.38f));
  // Large amber upstairs window, louvered shutters and deep wood reveal.
  Box("20_UpperReveal","woodDark",V(0,5.06f,f-.065f),V(4.60f,2.74f,.13f));Box("20_UpperGlow","warm",V(0,5.11f,f-.144f),V(3.50f,2.36f,.024f));Frame(0,5.10f,f-.20f,3.62f,2.46f,"woodLight",.09f);
  for(int i=-1;i<=1;i++)Box("20_UpperMullions","wood",V(i*.89f,5.1f,f-.245f),V(.085f,2.38f,.055f));Box("20_UpperMullions","wood",V(0,5.62f,f-.245f),V(3.50f,.09f,.055f));Box("20_UpperSill","woodDark",V(0,3.78f,f-.21f),V(4.65f,.18f,.36f));
  foreach(int side in new[]{-1,1}){float x=side*2.14f;Box("20_Shutters","wood",V(x,5.08f,f-.165f),V(.58f,2.6f,.09f));for(int j=0;j<18;j++)Box("20_ShutterSlats",j%3==0?"woodLight":"woodMid",V(x,3.89f+j*.139f,f-.225f),V(.51f,.09f,.075f));}
  // Dark silhouette of a vase and bread tins in the upstairs sill.
  Box("40_UpstairsObjects","wood",V(.92f,3.98f,f-.19f),V(.40f,.26f,.06f));Round("40_UpstairsObjects","woodMid",V(-.77f,4.00f,f-.20f),V(.18f,.28f,.06f));
  // Ground floor left display and right glazed timber door with transom grid.
  Box("20_DisplayRecess","woodDark",V(-.95f,1.70f,f-.10f),V(3.91f,2.67f,.18f));Box("20_DisplayGlow","warm",V(-.95f,1.72f,f-.205f),V(3.65f,2.39f,.04f));Frame(-.95f,1.72f,f-.25f,3.72f,2.50f,"woodLight",.11f);
  for(float x=-2.14f;x<=.3f;x+=1.23f)Box("20_DisplayMullions","wood",V(x,1.73f,f-.32f),V(.08f,2.43f,.09f));Box("20_DisplayMullions","wood",V(-.95f,2.51f,f-.32f),V(3.70f,.09f,.09f));
  Box("20_DoorFrame","woodDark",V(2.10f,1.65f,f-.1f),V(1.75f,2.9f,.20f));Box("20_DoorGlow","warm",V(2.10f,1.64f,f-.215f),V(1.41f,2.58f,.035f));Frame(2.10f,1.65f,f-.27f,1.50f,2.68f,"wood",.12f);
  Box("20_DoorPanel","wood",V(2.10f,.54f,f-.26f),V(1.42f,.55f,.09f));for(int i=0;i<5;i++)Box("20_DoorLattice","woodDark",V(2.1f,1.00f+i*.37f,f-.31f),V(1.42f,.070f,.06f));Box("20_DoorLattice","woodDark",V(2.23f,1.88f,f-.31f),V(.075f,1.83f,.06f));Box("20_DoorHandle","cream",V(1.55f,1.36f,f-.41f),V(.045f,.39f,.09f));
  Box("20_Threshold","concrete",V(2.12f,.055f,f-.35f),V(1.69f,.11f,.48f),true);
  SlopedAwning(0,3.28f,f-.19f,6.40f,.98f,"ochre",16);
  // Bread display is shallow facade geometry, kept ahead of the opaque glow pane.
  for(float y=.94f;y<1.6f;y+=.52f){Box("40_DisplayShelves","woodLight",V(-.97f,y-.11f,f-.40f),V(3.5f,.08f,.29f));for(int i=0;i<8;i++)Loaf(-2.49f+i*.43f,y,f-.46f,.88f);}
  Text("골목 빵집",V(-.95f,2.07f,f-.39f),.37f,"white",2.2f);Text("BAKERY",V(-.95f,1.75f,f-.39f),.16f,"white",1.60f);
  // Side blade sign, bench, wicker baskets and A-frame chalkboard.
  Box("30_BladeSign","woodDark",V(-3.15f,4.61f,f-.54f),V(.16f,1.05f,.78f));Box("30_BladeSign","ochre",V(-3.24f,4.61f,f-.54f),V(.035f,.89f,.64f));
  Box("30_BladeSign","ochre",V(-3.057f,4.61f,f-.54f),V(.035f,.89f,.64f));foreach(float x in new[]{-3.27f,-3.027f}){Round("30_BladeBread","cream",V(x,4.68f,f-.54f),V(.025f,.27f,.43f));for(int i=-1;i<=1;i++)Box("30_BladeBread","wood",V(x,4.72f,f-.54f+i*.105f),V(.03f,.10f,.025f));Box("30_BladeBread","wood",V(x,4.39f,f-.54f),V(.03f,.035f,.42f));}
  Box("40_Bench","woodLight",V(-1.19f,.47f,f-.77f),V(2.25f,.12f,.44f));Box("40_BenchBack","wood",V(-1.19f,.87f,f-.54f),V(2.25f,.45f,.07f));foreach(float x in new[]{-2.03f,-.37f}){Beam("40_BenchLegs","metal",V(x,.02f,f-.66f),V(x,.52f,f-.66f),.055f);Beam("40_BenchLegs","metal",V(x,.02f,f-.95f),V(x,.52f,f-.84f),.055f);}
  float bx=-2.61f;Box("40_Basket","wood",V(bx,.3f,f-.70f),V(.54f,.53f,.48f));for(int i=0;i<6;i++)Box("40_BasketWeave","ochre",V(bx,.08f+i*.085f,f-.95f),V(.56f,.032f,.026f));for(int i=0;i<3;i++){Add("40_Baguettes","bread",cube,V(bx+(i-1)*.12f,.75f,f-.7f),V(.09f,.70f,.12f),Quaternion.Euler(0,0,(i-1)*9));}
  float ax=.67f;Add("40_MenuBoard","wood",cube,V(ax,.66f,f-.78f),V(.65f,1.05f,.07f),Quaternion.Euler(-11,0,0));Box("40_MenuBoard","black",V(ax,.70f,f-.853f),V(.52f,.81f,.02f));Text("TODAY\nBREAD\nCOFFEE",V(ax,.73f,f-.88f),.48f,"chalk",.44f);foreach(int side in new[]{-1,1})Beam("40_MenuBoardLegs","wood",V(ax+side*.27f,.02f,f-.49f),V(ax+side*.27f,1.23f,f-.82f),.045f);
  Pot(2.91f,0,f-.56f,.41f);
  foreach(float x in new[]{-2.92f,2.94f}){Box("40_LanternBracket","metal",V(x,2.62f,f-.34f),V(.055f,.07f,.32f));Box("40_LanternGlow","warm",V(x,2.47f,f-.44f),V(.12f,.22f,.12f));Box("40_LanternCap","metal",V(x,2.62f,f-.44f),V(.20f,.07f,.20f));}
 }
 public static object Build(){
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before rebuilding storefront assets.");
  if(EditorUtility.scriptCompilationFailed)throw new InvalidOperationException("Unity compilation must succeed first.");
  string folder=Root+"/Prefabs/Buildings/";string marketPath=folder+"Supermarket.prefab",oldPath=folder+"Laundry.prefab";
  var source=AssetDatabase.LoadAssetAtPath<GameObject>(Village);var b=source.transform.Find("10_Buildings");var market=b.Find("Supermarket")??b.Find("Laundry");var bakery=b.Find("Bakery");if(!market||!bakery)throw new Exception("Expected the current organized village building instances.");
  Vector3 marketPivot=market.position,bakeryPivot=bakery.position;string marketGuid=AssetDatabase.AssetPathToGUID(File.Exists(marketPath)?marketPath:oldPath),bakeryGuid=AssetDatabase.AssetPathToGUID(folder+"Bakery.prefab");
  Directory.CreateDirectory("Temp/ShopRemodelBackup");foreach(var p in new[]{Village,File.Exists(marketPath)?marketPath:oldPath,folder+"Bakery.prefab"}){var dest="Temp/ShopRemodelBackup/"+Path.GetFileName(p);if(!File.Exists(dest))File.Copy(p,dest);}
  Palette();
  if(!File.Exists(marketPath)){string error=AssetDatabase.MoveAsset(oldPath,marketPath);if(!string.IsNullOrEmpty(error))throw new Exception(error);}
  foreach(var name in new[]{"Supermarket","Bakery"}){
   string path=folder+name+".prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
   try{foreach(Transform t in prefab.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(t.gameObject);prefab.name=name;
    var offset=name=="Supermarket"?V(-14.7f,6,12.8f)-marketPivot:V(17,3,-.3f)-bakeryPivot;Begin(prefab.transform,name,offset);
    if(name=="Supermarket")Market();else Bakery();Finish();if(!PrefabUtility.SaveAsPrefabAsset(prefab,path))throw new Exception("Could not save "+path);
   }finally{PrefabUtility.UnloadPrefabContents(prefab);}
  }
  // The nested instance has an explicit legacy name override; change that too.
  var village=PrefabUtility.LoadPrefabContents(Village);
  try{var t=village.transform.Find("10_Buildings/Laundry");if(t){t.name="Supermarket";PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);}if(!PrefabUtility.SaveAsPrefabAsset(village,Village))throw new Exception("Could not update village hierarchy.");}finally{PrefabUtility.UnloadPrefabContents(village);}
  AssetDatabase.SaveAssets();if(AssetDatabase.AssetPathToGUID(marketPath)!=marketGuid||AssetDatabase.AssetPathToGUID(folder+"Bakery.prefab")!=bakeryGuid)throw new Exception("Building GUID changed unexpectedly.");
  return new{success=true,marketGuid,bakeryGuid,marketPivot=marketPivot.ToString(),bakeryPivot=bakeryPivot.ToString(),preservedDirtyScenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new{SceneManager.GetSceneAt(i).path,SceneManager.GetSceneAt(i).isDirty}).ToArray()};
 }
}
