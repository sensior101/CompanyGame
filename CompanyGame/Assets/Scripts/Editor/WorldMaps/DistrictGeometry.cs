using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using Object = UnityEngine.Object;

namespace CompanyGame.Editor.WorldMaps
{
    // Authored in metres. Details are combined per material, with simple dedicated
    // collision volumes, so floor count does not multiply physics objects.
    public static class DistrictGeometry
    {
        public const string AssetsRoot="Assets/Art/WorldDistricts";
        static readonly Dictionary<string,Material> palette=new Dictionary<string,Material>();
        static Mesh cube, cylinder, crown;
        public static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
        public static void Folder(string p)
        {if(AssetDatabase.IsValidFolder(p))return;Folder(Path.GetDirectoryName(p).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(p).Replace('\\','/'),Path.GetFileName(p));}
        public static Transform Group(Transform p,string name)
        {var g=new GameObject(name).transform;g.SetParent(p,false);return g;}
        public static Mesh SaveMesh(string key,Mesh mesh)
        {
            mesh.name=key;string p=AssetsRoot+"/Meshes/"+key+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if(old){EditorUtility.CopySerialized(mesh,old);EditorUtility.SetDirty(old);Object.DestroyImmediate(mesh);return old;}
            AssetDatabase.CreateAsset(mesh,p);return mesh;
        }
        public static void Init()
        {
            Folder(AssetsRoot+"/Meshes");Folder(AssetsRoot+"/Materials");
            if(!cube){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=g.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(g);}
            if(!cylinder){var g=GameObject.CreatePrimitive(PrimitiveType.Cylinder);cylinder=g.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(g);}
            if(!crown)
            {
                var vs=new List<Vector3>();var ts=new List<int>();int sides=7;
                for(int k=0;k<sides;k++)
                {
                    float a=k*Mathf.PI*2/sides,b=(k+1)*Mathf.PI*2/sides;
                    var p=V(Mathf.Cos(a)*.5f,-.22f,Mathf.Sin(a)*.5f);var q=V(Mathf.Cos(b)*.5f,-.22f,Mathf.Sin(b)*.5f);
                    var r=V(Mathf.Cos(a)*.38f,.27f,Mathf.Sin(a)*.38f);var s=V(Mathf.Cos(b)*.38f,.27f,Mathf.Sin(b)*.38f);
                    foreach(var v in new[]{p,r,s,p,s,q,r,V(0,.5f,0),s,p,q,V(0,-.5f,0)}){ts.Add(vs.Count);vs.Add(v);}
                }
                crown=new Mesh{name="Faceted canopy"};crown.SetVertices(vs);crown.SetTriangles(ts,0);crown.RecalculateNormals();crown.RecalculateBounds();
            }
            Mat("paving",new Color(.65f,.65f,.61f));Mat("stone",new Color(.80f,.77f,.67f));Mat("road",new Color(.19f,.22f,.25f));
            Mat("white",new Color(.88f,.88f,.81f));Mat("concrete",new Color(.50f,.51f,.49f));Mat("cream",new Color(.75f,.70f,.57f));
            Mat("brick",new Color(.45f,.22f,.16f));Mat("brickLight",new Color(.58f,.34f,.24f));Mat("mortar",new Color(.31f,.29f,.26f));
            Mat("metal",new Color(.13f,.20f,.22f),.5f,.4f);Mat("gold",new Color(.71f,.57f,.30f),.7f,.55f);
            Mat("glass",new Color(.19f,.43f,.51f),.65f,.8f);Mat("glassLight",new Color(.35f,.62f,.68f),.6f,.85f);
            Mat("glassDark",new Color(.10f,.23f,.29f),.5f,.8f);Mat("wood",new Color(.42f,.25f,.12f));
            Mat("grass",new Color(.31f,.42f,.22f));Mat("leaf",new Color(.24f,.36f,.17f));Mat("leafLight",new Color(.47f,.56f,.24f));
            Mat("sand",new Color(.81f,.74f,.56f));Mat("water",new Color(.10f,.42f,.53f),.2f,.8f);
            Mat("yellow",new Color(.95f,.69f,.18f));Mat("blue",new Color(.12f,.27f,.49f));Mat("red",new Color(.65f,.13f,.13f));
            Mat("pink",new Color(.9f,.08f,.35f),0,.4f,2.2f);Mat("cyan",new Color(.07f,.7f,.9f),0,.4f,2.5f);
            Mat("warmLight",new Color(1,.71f,.31f),0,.3f,1.6f);Mat("signWhite",new Color(.75f,.91f,1),0,.3f,1.6f);
        }
        public static Material Mat(string name,Color color,float metallic=0,float smooth=.22f,float emission=0)
        {
            if(palette.TryGetValue(name,out var m)&&m)return m;
            string p=AssetsRoot+"/Materials/"+name+".mat";m=AssetDatabase.LoadAssetAtPath<Material>(p);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smooth);if(emission>0){m.SetColor("_EmissionColor",color*emission);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;MaterialEditor.FixupEmissiveFlag(m);m.EnableKeyword("_EMISSION");}AssetDatabase.CreateAsset(m,p);}
            palette[name]=m;return m;
        }
        public static Material M(string n)=>palette[n];
        public sealed class Batch
        {
            public readonly Transform root; readonly string key;
            readonly Dictionary<string,List<CombineInstance>> parts=new Dictionary<string,List<CombineInstance>>();
            public Batch(Transform parent,string name,string assetKey=null){root=Group(parent,name);key=assetKey??name;}
            void Add(string material,Mesh mesh,Vector3 p,Vector3 size,Quaternion rot)
            {if(!parts.ContainsKey(material))parts[material]=new List<CombineInstance>();parts[material].Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(p,rot,size)});}
            public void Box(Vector3 p,Vector3 size,string material, bool collision=false,Quaternion? rotation=null)
            {
                var rot=rotation??Quaternion.identity;Add(material,cube,p,size,rot);
                if(collision){var t=Group(root,"Solid");t.localPosition=p;t.localRotation=rot;var c=t.gameObject.AddComponent<BoxCollider>();c.size=size;}
            }
            public void Cylinder(Vector3 p,float radius,float height,string material){Add(material,cylinder,p,V(radius*2,height/2,radius*2),Quaternion.identity);}
            public void Facet(Vector3 p,Vector3 size,string material){Add(material,crown,p,size,Quaternion.identity);}
            public void Beam(Vector3 a,Vector3 b,float width,string m){Box((a+b)/2,V(width,width,Vector3.Distance(a,b)),m,false,Quaternion.LookRotation(b-a));}
            public void Finish()
            {
                foreach(var part in parts)
                {
                    var mesh=new Mesh{name=key+"_"+part.Key,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(part.Value.ToArray(),true,true);mesh.RecalculateBounds();
                    var t=Group(root,part.Key);t.gameObject.AddComponent<MeshFilter>().sharedMesh=SaveMesh(key+"_"+part.Key,mesh);t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=M(part.Key);t.gameObject.isStatic=true;
                }
            }
        }
        public static void Label(Transform parent,string text,Vector3 p,float width,float height=1, string color="signWhite",float yaw=0)
        {
            WorldLabel(parent,text,p,width,height,M(color).GetColor("_BaseColor"),yaw);
        }
        static void WorldLabel(Transform parent,string text,Vector3 p,float width,float height,Color color,float yaw)
        {
            var g=new GameObject("Sign_"+text.Replace("\n"," "),typeof(RectTransform));var t=g.transform;t.SetParent(parent,false);t.localPosition=p;t.localRotation=Quaternion.Euler(0,yaw,0);
            var tm=g.AddComponent<TextMeshPro>();tm.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/font/NotoSansKR-Regular SDF.asset");
            tm.text=text;tm.color=color;tm.alignment=TextAlignmentOptions.Center;tm.enableWordWrapping=false;tm.enableAutoSizing=true;tm.fontSizeMin=.2f;tm.fontSizeMax=30;tm.rectTransform.sizeDelta=new Vector2(width,height);tm.isOrthographic=false;
            const string path=AssetsRoot+"/Materials/KoreanWorldSigns.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(tm.font.material){name="Korean World Signs",shader=Shader.Find("TextMeshPro/Distance Field")};mat.SetFloat("unity_GUIZTestMode",(float)CompareFunction.LessEqual);AssetDatabase.CreateAsset(mat,path);}
            tm.fontSharedMaterial=mat;tm.ForceMeshUpdate(true,true);
        }
        public static int UpgradeWorldSigns(GameObject root)
        {
            Init();int count=0;
            foreach(var old in root.GetComponentsInChildren<TextMesh>().Where(t=>t.name.StartsWith("Sign_")).ToArray())
            {
                var r=old.GetComponent<Renderer>();var size=r.bounds.size;
                WorldLabel(old.transform.parent,old.text,old.transform.localPosition,Mathf.Max(.3f,size.x),Mathf.Max(.2f,size.y),old.color,old.transform.localEulerAngles.y);
                Object.DestroyImmediate(old.gameObject);count++;
            }
            return count;
        }
        public static void Window(Batch b,float x,float y,float z,float w,float h,string trim="white",string pane="glass")
        {
            b.Box(V(x,y,z),V(w+.18f,h+.18f,.18f),trim);b.Box(V(x,y,z-.105f),V(w,h,.07f),pane);
            b.Box(V(x,y,z-.15f),V(.045f,h,.06f),trim);b.Box(V(x,y-h/2-.1f,z-.18f),V(w+.3f,.12f,.4f),trim);
        }
        public static Transform Walkup(Transform parent,string id,Vector3 p,int floors=4,float yaw=0,string name="달빛빌라",bool shop=false,int style=0)
        {
            var b=new Batch(parent,id);float w=8.4f,d=7.8f,h=floors*2.75f;string wall=style%3==0?"brick":style%3==1?"cream":"brickLight";
            b.Box(V(0,h/2,0),V(w,h,d),wall,true);b.Box(V(0,.1f,0),V(w+.5f,.2f,d+.5f),"concrete",true);
            for(int floor=0;floor<floors;floor++)
            {
                float y=floor*2.75f;
                b.Box(V(0,y+.12f,0),V(w+.12f,.15f,d+.12f),"stone");
                for(int i=-1;i<=1;i++)if(floor>0||i!=0)Window(b,i*2.6f,y+1.55f,-d/2-.07f,1.6f,1.4f,"cream",(i+floor)%4==0?"warmLight":"glassDark");
                for(int i=-1;i<=1;i++)
                {
                    b.Box(V(i*2.6f,y+1.5f,d/2+.02f),V(1.5f,1.4f,.15f),"white");b.Box(V(i*2.6f,y+1.5f,d/2+.11f),V(1.34f,1.2f,.07f),"glass");
                    if(floor>0){b.Box(V(i*2.6f,y+.48f,-d/2-.42f),V(.85f,.48f,.4f),"white");for(int slat=0;slat<4;slat++)b.Box(V(i*2.6f,y+.32f+slat*.10f,-d/2-.64f),V(.63f,.025f,.03f),"metal");}
                }
                foreach(int s in new[]{-1,1})foreach(float z in new[]{-2f,1f})
                {b.Box(V(s*(w/2+.02f),y+1.55f,z),V(.12f,1.5f,1.4f),"cream");b.Box(V(s*(w/2+.09f),y+1.55f,z),V(.05f,1.3f,1.22f),"glass");}
                // Fine brick joints only on the exposed street facade.
                if(wall!="cream")for(int row=0;row<8;row++)
                {b.Box(V(0,y+.3f+row*.3f,-d/2-.012f),V(w,.018f,.022f),"mortar");for(int k=0;k<11;k++)b.Box(V(-3.8f+k*.75f+(row%2)*.34f,y+.44f+row*.3f,-d/2-.014f),V(.018f,.27f,.025f),"mortar");}
            }
            b.Box(V(0,1.1f,-d/2-.06f),V(1.45f,2.2f,.2f),"metal");b.Box(V(0,1.16f,-d/2-.18f),V(1.19f,1.86f,.06f),"glass");b.Box(V(.45f,1.08f,-d/2-.23f),V(.05f,.42f,.07f),"gold");
            b.Box(V(0,2.48f,-d/2-.40f),V(2.1f,.16f,.85f),"metal");b.Box(V(0,h+.13f,0),V(w+.4f,.26f,d+.4f),"concrete");
            foreach(int s in new[]{-1,1}){b.Box(V(s*w/2,h+.48f,0),V(.18f,.6f,d),"cream");b.Box(V(0,h+.48f,s*d/2),V(w,.6f,.18f),"cream");b.Cylinder(V(s*(w/2-.18f),h/2,-d/2-.18f),.055f,h,"metal");}
            b.Box(V(2,h+.9f,1.5f),V(2.6f,1.55f,2.6f),"cream");b.Box(V(2,h+1.73f,1.5f),V(2.9f,.12f,2.9f),"metal");
            b.Cylinder(V(-2,h+.6f,1.4f),.8f,1,"blue");b.Box(V(-1,h+.7f,-1.9f),V(2.3f,.1f,1.2f),"glassDark",false,Quaternion.Euler(16,0,0));
            b.Box(V(0,3.08f,-d/2-.15f),V(3.2f,.53f,.17f),shop?"blue":"cream");Label(b.root,name,V(0,3.08f,-d/2-.25f),2.9f,.4f,shop?"signWhite":"metal");
            for(int k=0;k<3;k++){b.Box(V(3.1f+k*.25f,.95f,-d/2-.12f),V(.18f,.3f,.14f),"metal");b.Cylinder(V(-3.6f+k*.5f,.22f,-4.3f),.18f,.44f,"brick");b.Facet(V(-3.6f+k*.5f,.59f,-4.3f),V(.45f,.55f,.45f),"leaf");}
            b.Finish();b.root.localPosition=p;b.root.localRotation=Quaternion.Euler(0,yaw,0);return b.root;
        }
        public static Transform Tower(Transform parent,string id,Vector3 p,float w,float d,int floors,string label,bool luxury=false)
        {
            var b=new Batch(parent,id);float podium=3.8f,h=podium+floors*2.7f;
            b.Box(V(0,h/2,0),V(w,h,d),"glass",true);b.Box(V(0,1.9f,0),V(w+3,3.8f,d+3),luxury?"stone":"glassDark",true);
            for(int f=0;f<=floors;f++)
            {
                float y=podium+f*2.7f;b.Box(V(0,y,0),V(w+.22f,.19f,d+.22f),luxury?"white":"metal");
                for(float x=-w/2+.7f;x<w/2;x+=1.7f)foreach(int s in new[]{-1,1})b.Box(V(x,y+1.25f,s*(d/2+.025f)),V(1.43f,2.14f,.07f),(f+(int)(x*2))%5==0?"glassLight":"glass");
                if(luxury&&f%2==0)foreach(int s in new[]{-1,1}){b.Box(V(s*w/2,y+.08f,0),V(1.4f,.14f,d-.5f),"white");b.Box(V(s*(w/2+.62f),y+.55f,0),V(.06f,.8f,d-.5f),"glassLight");}
            }
            for(float x=-w/2;x<=w/2+.01f;x+=w/8)foreach(int s in new[]{-1,1})b.Box(V(x,h/2,s*(d/2+.10f)),V(.11f,h,.10f),luxury?"white":"metal");
            for(float z=-d/2;z<=d/2+.01f;z+=d/6)foreach(int s in new[]{-1,1})b.Box(V(s*(w/2+.1f),h/2,z),V(.1f,h,.11f),"metal");
            b.Box(V(0,h+.55f,0),V(w+1,1.1f,d+1),luxury?"gold":"metal");b.Box(V(0,h+1.2f,0),V(w-2,.3f,d-2),"glassLight");
            for(int k=-1;k<=1;k++)Window(b,k*2.3f,1.6f,-d/2-1.56f,1.9f,2.9f,"metal","glassLight");
            b.Box(V(0,3.5f,-d/2-2.2f),V(w,.17f,2.2f),luxury?"gold":"metal");Label(b.root,label,V(0,3.1f,-d/2-1.7f),w-1,.55f,luxury?"gold":"signWhite");
            if(!luxury)Label(b.root,label,V(0,h-2,-d/2-.24f),w-1,1.15f,"signWhite");
            b.Finish();b.root.localPosition=p;return b.root;
        }
        public static void Tree(Batch b,Vector3 p,float scale=1)
        {b.Cylinder(p+V(0,1.35f*scale,0),.16f*scale,2.7f*scale,"wood");b.Beam(p+V(0,1.5f*scale,0),p+V(.7f*scale,2.8f*scale,.2f),.15f,"wood");b.Facet(p+V(0,3.4f*scale,0),V(2.8f,2.9f,2.7f)*scale,"leaf");b.Facet(p+V(.75f,3.8f,.3f)*scale,V(1.9f,2,1.8f)*scale,"leafLight");}
        public static void Bench(Batch b,Vector3 p)
        {for(int k=0;k<5;k++)b.Box(p+V(0,.49f,-.25f+k*.12f),V(1.9f,.08f,.09f),"wood");for(int k=0;k<3;k++)b.Box(p+V(0,.78f+k*.16f,.32f),V(1.9f,.11f,.08f),"wood");foreach(int s in new[]{-1,1}){b.Box(p+V(s*.72f,.24f,0),V(.09f,.5f,.64f),"metal");b.Box(p+V(s*.72f,.72f,.34f),V(.08f,.8f,.08f),"metal");}}
        public static void Lamp(Batch b,Vector3 p)
        {b.Cylinder(p+V(0,2.4f,0),.065f,4.8f,"metal");b.Cylinder(p+V(0,.25f,0),.17f,.5f,"metal");b.Box(p+V(0,4.65f,-.55f),V(.15f,.15f,1.15f),"metal");b.Box(p+V(0,4.6f,-1.1f),V(.55f,.13f,.8f),"warmLight");}
    }
}
