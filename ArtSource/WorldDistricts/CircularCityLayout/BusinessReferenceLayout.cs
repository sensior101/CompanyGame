using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CompanyGame.World.Maps;
using Object = UnityEngine.Object;

// Explicit scene authoring from the user's October 5 reference. Never runs at game startup.
public static class BusinessReferenceLayout
{
    const string ScenePath="Assets/Scenes/Maps/BusinessDistrict.unity";
    const string AssetsPath="Assets/Art/WorldDistricts/CircularCity/BusinessReference";
    const string Templates="Assets/Art/WorldDistricts/CircularCity";
    const float Radius=350, RingInner=330, RingOuter=342, ParcelWidth=96, ParcelDepth=80, VacantSide=88;
    static string Output=>Path.GetFullPath("../ArtSource/WorldDistricts/CircularCityLayout");
    static Transform root;
    static int serial;
    static List<Rect> roads=new List<Rect>();
    static List<(string name,Rect rect)> parcels=new List<(string,Rect)>();
    static List<(string name,Vector3 point)> goals=new List<(string,Vector3)>();
    static MeshBatch lines,kerbs,lamps,green,treeTrunks,treeLeaves;
    static Material M(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/WorldDistricts/Materials/"+name+".mat");
    static string Json(object o)=>Newtonsoft.Json.JsonConvert.SerializeObject(o,Newtonsoft.Json.Formatting.Indented);
    static Rect R(float x,float z,float w,float d)=>new Rect(x-w/2,z-d/2,w,d);
    static Vector3 V(float x,float z,float y=0)=>new Vector3(x,y,z);
    static Transform Find(Scene s,string name)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t=>t.name==name);
    static void Guard(){if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new Exception("Leave Play Mode and finish compilation before authoring.");}
    static GameObject Group(string name,Transform parent=null){var g=new GameObject(name);g.transform.SetParent(parent?parent:root,false);return g;}

    sealed class MeshBatch
    {
        public List<Vector3> vertices=new List<Vector3>();
        public List<Vector2> uv=new List<Vector2>();
        public List<List<int>> triangles=new List<List<int>>();
        public MeshBatch(int materials=1){for(int i=0;i<materials;i++)triangles.Add(new List<int>());}
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int m=0){int i=vertices.Count;vertices.AddRange(new[]{a,b,c,d});uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});triangles[m].AddRange(new[]{i,i+1,i+2,i,i+2,i+3});}
        public void Box(Vector3 p,Vector3 s,int m=0)
        {
            var a=p-s/2;var b=p+s/2;
            Quad(new Vector3(a.x,a.y,a.z),new Vector3(a.x,b.y,a.z),new Vector3(b.x,b.y,a.z),new Vector3(b.x,a.y,a.z),m);
            Quad(new Vector3(b.x,a.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(a.x,b.y,b.z),new Vector3(a.x,a.y,b.z),m);
            Quad(new Vector3(a.x,a.y,b.z),new Vector3(a.x,b.y,b.z),new Vector3(a.x,b.y,a.z),new Vector3(a.x,a.y,a.z),m);
            Quad(new Vector3(b.x,a.y,a.z),new Vector3(b.x,b.y,a.z),new Vector3(b.x,b.y,b.z),new Vector3(b.x,a.y,b.z),m);
            Quad(new Vector3(a.x,b.y,a.z),new Vector3(a.x,b.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(b.x,b.y,a.z),m);
            Quad(new Vector3(a.x,a.y,b.z),new Vector3(a.x,a.y,a.z),new Vector3(b.x,a.y,a.z),new Vector3(b.x,a.y,b.z),m);
        }
        public void Polygon(List<Vector2> p,float y)
        {
            int k=vertices.Count;foreach(var v in p){vertices.Add(V(v.x,v.y,y));uv.Add(v*.02f);}
            for(int i=1;i<p.Count-1;i++)triangles[0].AddRange(new[]{k,k+i+1,k+i});
        }
        public void Beam(Vector3 a,Vector3 b,float width,int m=0)
        {
            var delta=b-a;var side=Vector3.Cross(delta.normalized,Vector3.up);
            if(side.sqrMagnitude<.1f)side=Vector3.right;
            side=side.normalized*width/2;var up=Vector3.Cross(side.normalized,delta.normalized)*width/2;
            Quad(a-side-up,a-side+up,b-side+up,b-side-up,m);Quad(a+side+up,a+side-up,b+side-up,b+side+up,m);
            Quad(a-side+up,a+side+up,b+side+up,b-side+up,m);Quad(a+side-up,a-side-up,b-side-up,b+side-up,m);
        }
        public void Crown(Vector3 p,float radius)
        {
            const int sides=8;for(int i=0;i<sides;i++)
            {float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;var aa=p+V(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius);var bb=p+V(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius);
                Quad(p+Vector3.up*radius,bb,aa,p+Vector3.up*radius);Quad(p-Vector3.up*radius*.8f,aa,bb,p-Vector3.up*radius*.8f);}
        }
    }
    static GameObject Mesh(string name,MeshBatch b,Material[] materials,bool collider=false,Transform parent=null)
    {
        string path=AssetsPath+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var mesh=existing?existing:new Mesh{name=name};mesh.Clear();mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.SetVertices(b.vertices);mesh.SetUVs(0,b.uv);mesh.subMeshCount=b.triangles.Count;
        for(int i=0;i<b.triangles.Count;i++)mesh.SetTriangles(b.triangles[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();
        mesh.UploadMeshData(false);if(existing)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
        var g=Group(name,parent);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterials=materials;if(collider)g.AddComponent<MeshCollider>().sharedMesh=mesh;g.isStatic=true;return g;
    }
    static GameObject Box(string name,Vector3 p,Vector3 size,Material mat,bool collider=true,Transform parent=null)
    {var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent?parent:root,false);g.transform.position=p;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=mat;if(!collider)Object.DestroyImmediate(g.GetComponent<Collider>());g.isStatic=true;return g;}
    static GameObject Prefab(string path,string name,Vector3 p,Quaternion rotation)
    {var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!source)throw new Exception("Missing asset "+path);var g=(GameObject)PrefabUtility.InstantiatePrefab(source,root);g.name=name;g.transform.position=p;g.transform.rotation=rotation;return g;}
    static List<Vector2> Circle(float r)=>Enumerable.Range(0,256).Select(i=>new Vector2(Mathf.Cos(i*Mathf.PI*2/256)*r,Mathf.Sin(i*Mathf.PI*2/256)*r)).ToList();
    static List<Vector2> Clip(List<Vector2> p,float z,bool greater)
    {var o=new List<Vector2>();for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];float da=a.y-z,db=b.y-z;bool ia=greater?da>=0:da<=0,ib=greater?db>=0:db<=0;if(ia)o.Add(a);if(ia!=ib)o.Add(Vector2.Lerp(a,b,da/(da-db)));}return o;}
    static void CutGroundOpening(MeshBatch batch,Rect opening)
    {
        // Split each existing ground triangle into four disjoint regions around the station stairwell.
        // Keeping the circle's original triangles preserves its shoreline and the shared collision mesh.
        var oldVertices=batch.vertices.ToArray();var oldTriangles=batch.triangles[0].ToArray();
        batch.vertices.Clear();batch.uv.Clear();batch.triangles[0].Clear();
        List<Vector2> HalfPlane(List<Vector2> polygon,int axis,float limit,bool less)
        {
            var result=new List<Vector2>();
            for(int i=0;i<polygon.Count;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Count];
                float av=axis==0?a.x:a.y,bv=axis==0?b.x:b.y;
                bool insideA=less?av<=limit:av>=limit,insideB=less?bv<=limit:bv>=limit;
                if(insideA)result.Add(a);
                if(insideA!=insideB)result.Add(Vector2.Lerp(a,b,(limit-av)/(bv-av)));
            }
            return result;
        }
        void Add(List<Vector2> polygon)
        {
            if(polygon.Count<3)return;
            int start=batch.vertices.Count;
            foreach(var p in polygon){batch.vertices.Add(V(p.x,p.y));batch.uv.Add(p*.02f);}
            for(int i=1;i<polygon.Count-1;i++)batch.triangles[0].AddRange(new[]{start,start+i,start+i+1});
        }
        for(int i=0;i<oldTriangles.Length;i+=3)
        {
            var triangle=new List<Vector2>(3);
            for(int j=0;j<3;j++){var v=oldVertices[oldTriangles[i+j]];triangle.Add(new Vector2(v.x,v.z));}
            Add(HalfPlane(triangle,0,opening.xMin,true));
            Add(HalfPlane(triangle,0,opening.xMax,false));
            var middle=HalfPlane(HalfPlane(triangle,0,opening.xMin,false),0,opening.xMax,true);
            Add(HalfPlane(middle,1,opening.yMin,true));
            Add(HalfPlane(middle,1,opening.yMax,false));
        }
    }
    static void Annulus(MeshBatch b,float inner,float outer,float y,bool banksOnly=true)
    {for(int i=0;i<256;i++){float a=i*Mathf.PI*2/256,c=(i+1)*Mathf.PI*2/256;if(banksOnly&&Mathf.Abs(Mathf.Sin((a+c)/2)*inner)<39)continue;b.Quad(V(Mathf.Cos(a)*inner,Mathf.Sin(a)*inner,y),V(Mathf.Cos(c)*inner,Mathf.Sin(c)*inner,y),V(Mathf.Cos(c)*outer,Mathf.Sin(c)*outer,y),V(Mathf.Cos(a)*outer,Mathf.Sin(a)*outer,y));}}
    static void Ground()
    {
        var land=new MeshBatch();land.Polygon(Clip(Circle(Radius),32,true),0);land.Polygon(Clip(Circle(Radius),-32,false),0);
        CutGroundOpening(land,Rect.MinMaxRect(288.8f,84.95f,299.2f,92.5f));
        Mesh("NorthAndSouthLand",land,new[]{M("paving")},true);
        var water=new MeshBatch();const float edge=1200,waterY=-1.14f;
        // The world water plane is below ground here; keep it out of the descending stairwell.
        water.Quad(V(-edge,-edge,waterY),V(-edge,edge,waterY),V(288.2f,edge,waterY),V(288.2f,-edge,waterY));
        water.Quad(V(299.8f,-edge,waterY),V(299.8f,edge,waterY),V(edge,edge,waterY),V(edge,-edge,waterY));
        water.Quad(V(288.2f,-edge,waterY),V(288.2f,84.85f,waterY),V(299.8f,84.85f,waterY),V(299.8f,-edge,waterY));
        water.Quad(V(288.2f,92.5f,waterY),V(288.2f,edge,waterY),V(299.8f,edge,waterY),V(299.8f,92.5f,waterY));
        Mesh("SurroundingWater",water,new[]{M("water")});
        var road=new MeshBatch();Annulus(road,RingInner,RingOuter,.045f);Mesh("PerimeterRoad_NorthSouthArcs",road,new[]{M("road")});
        var coastalGreen=new MeshBatch();Annulus(coastalGreen,342.8f,347.3f,.06f);Mesh("CoastalGreenbelt",coastalGreen,new[]{M("grass")});
        var seawall=new MeshBatch();for(int i=0;i<256;i++)
        {float a=i*Mathf.PI*2/256,b=(i+1)*Mathf.PI*2/256;var p=V(Mathf.Cos(a)*Radius,Mathf.Sin(a)*Radius);var q=V(Mathf.Cos(b)*Radius,Mathf.Sin(b)*Radius);if(Mathf.Abs(((p+q)/2).z)<32)continue;seawall.Quad(p-Vector3.up*3,q-Vector3.up*3,q+Vector3.up*.8f,p+Vector3.up*.8f);var g=Group("CoastBoundary_"+i);g.transform.position=(p+q)/2+Vector3.up*.4f;g.transform.rotation=Quaternion.LookRotation(q-p);g.AddComponent<BoxCollider>().size=new Vector3(.45f,.8f,Vector3.Distance(p,q)+.1f);}
        Mesh("StoneSeawall",seawall,new[]{M("stone")});
        for(int side=-1;side<=1;side+=2)
        {
            // Railings stop at both bridge approaches, leaving continuous pedestrian access.
            foreach(var span in new[]{new Vector2(-346,-157),new Vector2(-133,133),new Vector2(157,346)})
            {Box("RiverQuay",V((span.x+span.y)/2,side*32,-.7f),new Vector3(span.y-span.x,1.6f,.8f),M("stone"));
                Box("RiverSafetyRail",V((span.x+span.y)/2,side*33,1),new Vector3(span.y-span.x,.1f,.12f),M("metal"));
                for(float x=span.x;x<span.y;x+=4)kerbs.Box(V(x,side*33,.55f),new Vector3(.1f,1.1f,.1f));}
        }
    }
    static void Road(float x,float z,float w,float d)
    {var r=R(x,z,w,d);roads.Add(r);Box("Street_"+(serial++),V(x,z,.025f),new Vector3(w,.05f,d),M("road"),false);}
    static void H(float z){float edge=Mathf.Sqrt(337*337-z*z);Road(0,z,edge*2,14);}
    static void Vertical(float x,float lo,float hi,float w=14)=>Road(x,(lo+hi)/2,w,hi-lo);
    static void RoadNetwork()
    {
        foreach(float z in new[]{47,160,-47,-153})H(z);
        foreach(float x in new[]{-145,145}){Vertical(x,-153,-47,16);Vertical(x,47,160,16);}
        Vertical(0,47,160);Vertical(0,-153,-47);
        foreach(float x in new[]{-80,80})Vertical(x,160,Mathf.Sqrt(337*337-x*x));
        foreach(float x in new[]{-110,0,110})Vertical(x,-Mathf.Sqrt(337*337-x*x),-153,10);
        foreach(float x in new[]{-145,145})Bridge(x);
        // Dashed lane lines omit junction centres. Curb breaks follow the same junction geometry.
        foreach(var r in roads)
        {
            bool v=r.height>r.width;float len=v?r.height:r.width;
            for(float t=3;t<len-2;t+=8){var p=v?V(r.center.x,r.yMin+t,.062f):V(r.xMin+t,r.center.y,.062f);if(roads.Any(a=>a!=r&&a.Contains(new Vector2(p.x,p.z))))continue;lines.Box(p,v?new Vector3(.16f,.014f,3.5f):new Vector3(3.5f,.014f,.16f),1);}
            for(float t=1;t<len-1;t+=2)foreach(int side in new[]{-1,1}){var p=v?V(r.center.x+side*(r.width/2+.3f),r.yMin+t,.08f):V(r.xMin+t,r.center.y+side*(r.height/2+.3f),.08f);if(new Vector2(p.x,p.z).magnitude>329||Mathf.Abs(p.z)<38||roads.Any(a=>a.Contains(new Vector2(p.x,p.z))))continue;kerbs.Box(p,v?new Vector3(.3f,.14f,1.9f):new Vector3(1.9f,.14f,.3f));}
        }
        foreach(var vr in roads.Where(r=>r.height>r.width))foreach(var hr in roads.Where(r=>r.width>r.height))if(vr.Overlaps(hr))
        {
            float x=vr.center.x,z=hr.center.y;foreach(int side in new[]{-1,1})
            {float zz=z+side*(hr.height/2+2);if(zz>vr.yMin&&zz<vr.yMax)for(float u=-vr.width/2+1;u<vr.width/2-1;u+=1.4f)lines.Box(V(x+u,zz,.067f),new Vector3(.7f,.015f,2.8f));
                float xx=x+side*(vr.width/2+2);if(xx>hr.xMin&&xx<hr.xMax)for(float u=-hr.height/2+1;u<hr.height/2-1;u+=1.4f)lines.Box(V(xx,z+u,.067f),new Vector3(2.8f,.015f,.7f));}
        }
        for(int i=0;i<256;i++){float a=(i+.5f)*Mathf.PI*2/256;var p=V(Mathf.Cos(a)*336,Mathf.Sin(a)*336,.06f);if(Mathf.Abs(p.z)<56||roads.Any(r=>r.Contains(new Vector2(p.x,p.z))))continue;var tangent=V(-Mathf.Sin(a),Mathf.Cos(a));lines.Beam(p-tangent*1.6f,p+tangent*1.6f,.16f,1);}
    }
    static void Bridge(float x)
    {
        var g=Group(x<0?"Bridge_West":"Bridge_East");Box("BridgeDeck",V(x,0,-.13f),new Vector3(23,.36f,94),M("concrete"),true,g.transform);
        roads.Add(R(x,0,16,94));Box("BridgeAsphalt",V(x,0,.058f),new Vector3(16,.025f,94),M("road"),false,g.transform);
        var b=new MeshBatch(3);
        foreach(int side in new[]{-1,1})
        {float xx=x+side*10.7f;Box("BridgeRail",V(xx,0,.8f),new Vector3(.18f,1.3f,70),M("metal"),true,g.transform);
            foreach(float z in new[]{-22,22}){b.Box(V(xx,z,11.2f),new Vector3(.9f,22.4f,1.1f));b.Box(V(xx+side*.5f,z,11),new Vector3(.08f,21,.15f),2);for(int k=1;k<=5;k++)foreach(int s in new[]{-1,1})b.Beam(V(xx,z,21.5f),V(xx,z+s*k*4.3f,.4f),.1f,1);}
        }
        Mesh(x<0?"WestBridgeStructure":"EastBridgeStructure",b,new[]{M("concrete"),M("metal"),M("warmLight")},false,g.transform);
        goals.Add((g.name,V(x,0,.18f)));
    }
    static Rect Parcel(string name,float x,float z,bool empty=false)
    {
        float width=empty?VacantSide:ParcelWidth,depth=empty?VacantSide:ParcelDepth;
        var r=R(x,z,width,depth);parcels.Add((name,r));Box(name,V(x,z,.04f),new Vector3(width,.08f,depth),M(empty?"concrete":"stone"));
        foreach(int s in new[]{-1,1}){lines.Box(V(x+s*width/2,z,.086f),new Vector3(.2f,.016f,depth));lines.Box(V(x,z+s*depth/2,.086f),new Vector3(width,.016f,.2f));}
        if(empty)
        {for(float u=-width/2+11;u<width/2;u+=11)kerbs.Box(V(x+u,z,.083f),new Vector3(.035f,.006f,depth-.5f));for(float u=-depth/2+11;u<depth/2;u+=11)kerbs.Box(V(x,z+u,.083f),new Vector3(width-.5f,.006f,.035f));}
        goals.Add((name,V(x,z+(z<0?1:-1)*(depth/2+1),.15f)));return r;
    }
    static void Landmarks()
    {
        Parcel("HeadquartersParcel",0,216);Prefab("Assets/Art/WorldDistricts/HandaeHQ/HandaeHQ.prefab","BusinessDistrict_HandaeConstruction",V(0,216,.09f),Quaternion.identity);
        int i=1;foreach(float z in new[]{216,104})foreach(int s in new[]{-1,1})Parcel("VacantPlot_North_"+(i++).ToString("00"),s*(z==216?150:220),z,true);
        i=5;foreach(float x in new[]{-165,-55,55,165})Parcel("VacantPlot_South_"+(i++).ToString("00"),x,-207,true);
        for(i=0;i<2;i++)
        {float x=i==0?-72:72;Parcel("CompanyParcel_"+(i+1),x,104);var g=Prefab(Templates+"/LargeCityBuilding_"+(i==0?4:6)+".prefab","BasicCompany_"+(i+1),V(x,104,.09f),Quaternion.identity);
            Sign("Company_"+(i+1),i==0?"DAE HAN\nBUSINESS CENTER":"CENTRAL\nOFFICE",V(x,81.4f,6),Quaternion.identity,1.15f);
        }
        var retail=Group("RiversideRetailDistrict");int n=0;
        foreach(float x in new[]{-210,-70,70,210})
        {Parcel("RetailParcel_"+(n+1),x,-102);Retail(retail.transform,++n,x,-102);}
        // A central forecourt keeps the HQ frontage legible from the river.
        Box("HQ_EntryWalk",V(0,173,.03f),new Vector3(18,.06f,10),M("stone"));
    }
    static void Sign(string name,string text,Vector3 pos,Quaternion rot,float scale)
    {
        var g=Group(name);g.transform.position=pos;g.transform.rotation=rot;var t=g.AddComponent<TextMesh>();t.text=text;t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;t.fontSize=56;t.characterSize=scale;t.color=new Color(.93f,.93f,.85f);g.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        t.font.RequestCharactersInTexture(text,56,FontStyle.Normal);
        string path=AssetsPath+"/WorldSignText.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(AssetsPath+"/WorldSign.shader");if(!shader)throw new Exception("Missing world sign shader");
        if(!mat){mat=new Material(shader);mat.name="WorldSignText";AssetDatabase.CreateAsset(mat,path);}mat.shader=shader;mat.shaderKeywords=new string[0];mat.SetColor("_Color",Color.white);mat.renderQueue=2450;
        mat.SetTexture("_MainTex",t.font.material.mainTexture);EditorUtility.SetDirty(mat);g.GetComponent<MeshRenderer>().sharedMaterial=mat;
    }
    static void Retail(Transform parent,int index,float x,float z)
    {
        // Four substantial frontage buildings, each ~0.65 of HQ footprint; regular window modules.
        var g=Group("RetailComplex_"+index,parent);g.transform.position=V(x,z,.09f);float w=58,d=42,h=22+index%2*3.6f;
        var b=new MeshBatch(6);b.Box(new Vector3(0,h/2,0),new Vector3(w,h,d),0);b.Box(new Vector3(0,2.3f,0),new Vector3(w+.12f,4.6f,d+.12f),2);
        int floors=index%2==0?5:6;float fh=(h-4.6f)/(floors-1);
        for(int f=0;f<floors;f++)
        {
            float y=f==0?2.3f:4.6f+(f-.5f)*fh,wh=f==0?3.9f:fh-.7f;
            foreach(int s in new[]{-1,1})
            {for(int k=0;k<18;k++){float xx=-w/2+(k+.5f)*w/18;b.Box(new Vector3(xx,y,s*(d/2+.16f)),new Vector3(w/18-.2f,wh,.15f),((k+f+index)%9==0)?3:2);b.Box(new Vector3(xx-w/36,y,s*(d/2+.26f)),new Vector3(.12f,wh+.2f,.18f),1);}
                for(int k=0;k<13;k++){float zz=-d/2+(k+.5f)*d/13;b.Box(new Vector3(s*(w/2+.16f),y,zz),new Vector3(.15f,wh,d/13-.22f),2);}}
            b.Box(new Vector3(0,f==0?4.5f:4.6f+f*fh,0),new Vector3(w+.7f,.35f,d+.7f),1);
        }
        foreach(int s in new[]{-1,1}){b.Box(new Vector3(0,4.1f,s*(d/2+1.1f)),new Vector3(w+1,.28f,2.4f),4);for(int k=0;k<6;k++)b.Box(new Vector3(-24+k*9.6f,5.25f,s*(d/2+.4f)),new Vector3(8,1.15f,.24f),5);}
        b.Box(new Vector3(0,h+.15f,0),new Vector3(w+.7f,.3f,d+.7f),1);
        foreach(int s in new[]{-1,1}){b.Box(new Vector3(s*(w/2-.3f),h+.7f,0),new Vector3(.45f,1.1f,d),0);b.Box(new Vector3(0,h+.7f,s*(d/2-.3f)),new Vector3(w,1.1f,.45f),0);}
        b.Box(new Vector3(-12,h+.5f,5),new Vector3(20,.2f,19),4);b.Box(new Vector3(15,h+1.6f,8),new Vector3(10,2.5f,9),1);
        // Broad two-storey retail frontage connects each large main building to the river promenade.
        // It stays inside the 96 x 80 m parcel, with pedestrian space on all four sides.
        b.Box(new Vector3(0,3.4f,27),new Vector3(86,6.8f,12),0);
        b.Box(new Vector3(0,7,27),new Vector3(87,.4f,13),1);
        for(int k=0;k<24;k++)
        {float xx=-43+(k+.5f)*86/24;b.Box(new Vector3(xx,2.3f,33.13f),new Vector3(86/24-.22f,3.8f,.16f),k%5==0?3:2);b.Box(new Vector3(xx-43f/24,2.3f,33.25f),new Vector3(.12f,4.2f,.2f),1);}
        for(int k=0;k<8;k++)b.Box(new Vector3(-37.625f+k*10.75f,5.6f,33.25f),new Vector3(9.7f,1.15f,.22f),5);
        b.Box(new Vector3(0,4.5f,34.3f),new Vector3(88,.28f,3),4);
        var mesh=Mesh("RetailGeometry_"+index,b,new[]{M(index%2==0?"stone":"brickLight"),M("metal"),M("glass"),M("warmLight"),M("cream"),M("signWhite")},false,g.transform);mesh.transform.localPosition=Vector3.zero;
        var c=g.AddComponent<BoxCollider>();c.center=new Vector3(0,h/2,0);c.size=new Vector3(w,h,d);
        var arcade=g.AddComponent<BoxCollider>();arcade.center=new Vector3(0,3.4f,27);arcade.size=new Vector3(86,6.8f,12);
        Sign("RetailSign_"+index,"RIVERSIDE  " +new[]{"MARKET","GALLERY","DINING","SHOPPING"}[index-1],V(x,z-d/2-.5f,7),Quaternion.identity,.65f);
        Sign("RetailRiverSign_"+index,"RIVERSIDE  "+index,V(x,z+33.5f,5.6f),Quaternion.Euler(0,180,0),.65f);
    }
    static void Tree(float x,float z,float size=1)
    {treeTrunks.Box(V(x,z,2),new Vector3(.4f,4,.4f));treeLeaves.Crown(V(x,z,5.2f),2.6f*size);}
    static void Lamp(float x,float z)
    {lamps.Box(V(x,z,3.2f),new Vector3(.15f,6.4f,.15f));lamps.Box(V(x,z,6.5f),new Vector3(1.25f,.2f,1.25f),1);}
    static bool Free(float x,float z,float margin)
    {var p=new Vector2(x,z);return p.magnitude<329&&Mathf.Abs(z)>37&&!roads.Any(r=>R(r.center.x,r.center.y,r.width+margin*2,r.height+margin*2).Contains(p))&&!parcels.Any(r=>R(r.rect.center.x,r.rect.center.y,r.rect.width+margin*2,r.rect.height+margin*2).Contains(p));}
    static void Landscape()
    {
        // Trees and lighting on the perimeters of occupied parcels; development pads remain empty.
        foreach(var parcel in parcels)
        {var r=parcel.rect;foreach(int side in new[]{-1,1})
            {for(float x=r.xMin+5;x<r.xMax;x+=14){float z=side<0?r.yMin-3:r.yMax+3;if(Free(x,z,1)){Tree(x,z);green.Box(V(x,z,.055f),new Vector3(4,.07f,3));}}
                for(float z=r.yMin+8;z<r.yMax;z+=15){float x=side<0?r.xMin-3:r.xMax+3;if(Free(x,z,1)){Tree(x,z);green.Box(V(x,z,.055f),new Vector3(3,.07f,4));}}}
            if(!parcel.name.StartsWith("Vacant"))for(int side=-1;side<=1;side+=2)for(int k=0;k<5;k++){float z=r.yMin+8+k*15;if(parcel.name.StartsWith("Retail")&&z>r.center.y+17)continue;Tree(r.center.x+side*40,z,.85f);}
        }
        for(int i=0;i<144;i++){float a=i*Mathf.PI*2/144;float x=Mathf.Cos(a)*345,z=Mathf.Sin(a)*345;if(Mathf.Abs(z)<38)continue;if(i%2==0)Tree(x,z,.8f);else Lamp(x,z);}
        foreach(int side in new[]{-1,1})for(float x=-330;x<=330;x+=18){if(Mathf.Abs(Mathf.Abs(x)-145)<16)continue;Tree(x,side*37,.8f);Lamp(x+6,side*36);}
        foreach(var r in roads)
        {bool v=r.height>r.width;float length=v?r.height:r.width;for(float t=11;t<length-8;t+=28)foreach(int side in new[]{-1,1})
            {float x=v?r.center.x+side*(r.width/2+1.8f):r.xMin+t,z=v?r.yMin+t:r.center.y+side*(r.height/2+1.8f);if(Free(x,z,.2f))Lamp(x,z);}}
        // Landscaped northern cap and south plaza use remaining spaces without adding extra companies.
        for(float z=267;z<315;z+=12)for(float x=-220;x<=220;x+=16)if(Free(x,z,4)){green.Box(V(x,z,.03f),new Vector3(12,.04f,8));Tree(x,z,.9f);}
        for(float z=-307;z< -263;z+=13)for(float x=-190;x<=190;x+=17)if(Free(x,z,4)){green.Box(V(x,z,.03f),new Vector3(12,.04f,8));Tree(x,z,.9f);}
    }
    static void Transport(Scene scene)
    {
        var subway=V(282,85,.2f);var bus=V(282,-85,.2f);
        foreach(var stop in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TransitStop>(true)))
        {stop.transform.position=stop.kind==TransitKind.Subway?subway:bus;if(stop.boardingPoint)stop.boardingPoint.position=stop.transform.position;}
        Prefab(Templates+"/Subway_Station.prefab","Subway Station",V(294,85),Quaternion.identity);
        Prefab(Templates+"/Bus_Stop.prefab","Bus Stop",V(294,-85),Quaternion.identity);
        int social=0;foreach(var spawn in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MapSpawnPoint>(true)))
        {spawn.transform.position=spawn.spawnId=="subway"?subway:spawn.spawnId=="bus"?bus:spawn.spawnId=="default"?V(14,65,.2f):V(13+(social%4)*2,69+(social++/4)*2,.2f);goals.Add(("Spawn_"+spawn.spawnId,spawn.transform.position));}
        foreach(var cam in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)))cam.farClipPlane=Mathf.Max(cam.farClipPlane,1600);
        foreach(var portal in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MapPortal>(true)).Where(p=>p.name=="ToIndustrialDistrict"))portal.transform.position=V(282,-68,.2f);
    }
    public static string Build()
    {
        Guard();if(SceneManager.GetActiveScene().isDirty)throw new Exception("Current scene has unsaved edits.");
        Directory.CreateDirectory(Output+"/Backups");Directory.CreateDirectory(Output+"/Reports");Directory.CreateDirectory(Output+"/Renders");
        string backup=Output+"/Backups/BusinessDistrict_before_reference_20261005.unity";if(!File.Exists(backup))File.Copy(ScenePath,backup);
        Directory.CreateDirectory(AssetsPath);AssetDatabase.Refresh();var s=EditorSceneManager.OpenScene(ScenePath);
        var world=Find(s,"10_World");if(!world)throw new Exception("Missing map world.");foreach(var t in world.Cast<Transform>().ToArray())Object.DestroyImmediate(t.gameObject);
        root=new GameObject("BusinessReferenceLayout").transform;root.SetParent(world,false);serial=0;roads.Clear();parcels.Clear();goals.Clear();
        lines=new MeshBatch(2);kerbs=new MeshBatch();lamps=new MeshBatch(2);green=new MeshBatch();treeTrunks=new MeshBatch();treeLeaves=new MeshBatch();
        Ground();RoadNetwork();Landmarks();Transport(s);Landscape();
        Mesh("StreetMarkings",lines,new[]{M("white"),M("yellow")});Mesh("KerbsAndJoints",kerbs,new[]{M("stone")});Mesh("StreetLighting",lamps,new[]{M("metal"),M("warmLight")});Mesh("LandscapedBeds",green,new[]{M("grass")});Mesh("StreetTreeTrunks",treeTrunks,new[]{M("wood")});Mesh("StreetTreeCanopies",treeLeaves,new[]{M("leaf")});
        Physics.SyncTransforms();string report=Validate(s);EditorSceneManager.MarkSceneDirty(s);if(!EditorSceneManager.SaveScene(s))throw new Exception("Scene save failed.");AssetDatabase.SaveAssets();
        if(SceneView.lastActiveSceneView){SceneView.lastActiveSceneView.LookAt(V(0,0,0),Quaternion.Euler(64,0,0),440,true);SceneView.RepaintAll();}
        return report;
    }
    static string Validate(Scene scene)
    {
        var errors=new List<string>();foreach(var p in parcels)
        {
            if(roads.Any(r=>r.Overlaps(p.rect)))errors.Add(p.name+" overlaps road");
            foreach(float x in new[]{p.rect.xMin,p.rect.xMax})foreach(float z in new[]{p.rect.yMin,p.rect.yMax})if(new Vector2(x,z).magnitude>RingInner)errors.Add(p.name+" intersects ring road");
        }
        for(int i=0;i<parcels.Count;i++)for(int j=i+1;j<parcels.Count;j++)if(parcels[i].rect.Overlaps(parcels[j].rect))errors.Add("Parcel pair overlap");
        var north=parcels.Count(p=>p.name.StartsWith("VacantPlot_North"));var south=parcels.Count(p=>p.name.StartsWith("VacantPlot_South"));
        if(north!=4||south!=4)errors.Add("Vacant parcel count");if(root.Cast<Transform>().Count(t=>t.name.StartsWith("BasicCompany_"))!=2)errors.Add("Company count");
        int bridges=root.Cast<Transform>().Count(t=>t.name=="Bridge_West"||t.name=="Bridge_East"),hq=root.Cast<Transform>().Count(t=>t.name=="BusinessDistrict_HandaeConstruction"),retail=root.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("RetailComplex_"));
        if(bridges!=2||hq!=1||retail!=4)errors.Add("Landmark count mismatch");
        var buildingChecks=new List<object>();
        foreach(var t in root.GetComponentsInChildren<Transform>().Where(t=>t.name=="BusinessDistrict_HandaeConstruction"||t.name.StartsWith("BasicCompany_")||t.name.StartsWith("RetailComplex_")||t.name=="HQ_EntryWalk"))
        {var rs=t.GetComponentsInChildren<Renderer>().Where(r=>!(r is ParticleSystemRenderer)).ToArray();if(rs.Length==0)continue;var b=rs.Select(r=>r.bounds).Aggregate((a,c)=>{a.Encapsulate(c);return a;});bool overlap=roads.Any(r=>r.Overlaps(R(b.center.x,b.center.z,b.size.x,b.size.z)));if(overlap)errors.Add(t.name+" geometry overlaps road");buildingChecks.Add(new{name=t.name,width=b.size.x,depth=b.size.z,height=b.size.y,roadOverlap=overlap});}
        var sizes=root.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("VacantPlot_")||r.name=="HeadquartersParcel").Select(r=>new{name=r.name,width=r.bounds.size.x,depth=r.bounds.size.z}).ToArray();
        if(sizes.Any(v=>Mathf.Abs(v.width-(v.name.StartsWith("VacantPlot_")?VacantSide:ParcelWidth))>.01f||Mathf.Abs(v.depth-(v.name.StartsWith("VacantPlot_")?VacantSide:ParcelDepth))>.01f))errors.Add("Parcel size mismatch");
        var report=Json(new{scene=scene.path,diameter=Radius*2,riverWidth=64,bridgeCount=bridges,headquartersCount=hq,basicCompanies=2,retailComplexes=retail,northVacant=north,southVacant=south,parcelSizes=sizes,buildingChecks,roadRects=roads.Select(r=>new{x=r.x,y=r.y,w=r.width,h=r.height}),routeGoals=goals.Select(g=>new{name=g.name,x=g.point.x,y=g.point.y,z=g.point.z}),errors,geometryChecksPassed=errors.Count==0});
        File.WriteAllText(Output+"/Reports/BusinessDistrict_reference.json",report);if(errors.Count>0)throw new Exception(string.Join("; ",errors));return report;
    }
    public static string Capture()
    {
        Guard();var s=SceneManager.GetActiveScene();if(s.path!=ScenePath)throw new Exception("Open BusinessDistrict first.");
        var go=new GameObject("ReferenceReviewCamera"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.orthographic=true;cam.useOcclusionCulling=false;cam.farClipPlane=2200;cam.nearClipPlane=.3f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.065f,.11f,.15f);
        bool fog=RenderSettings.fog;RenderSettings.fog=false;
        try{foreach(string view in new[]{"Top","Perspective","BridgeDetail"})
        {cam.orthographicSize=view=="Top"?365:390;cam.transform.position=view=="Top"?V(0,0,900):new Vector3(0,790,-660);cam.transform.LookAt(view=="Top"?Vector3.zero:new Vector3(0,25,0));if(view=="Top")cam.transform.rotation=Quaternion.Euler(90,0,0);
            if(view=="BridgeDetail"){cam.orthographicSize=73;cam.transform.position=new Vector3(220,80,-150);cam.transform.LookAt(new Vector3(145,3,-25));}
            var rt=new RenderTexture(1800,1800,24){antiAliasing=4};cam.targetTexture=rt;cam.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1800,1800,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1800,1800),0,0);tex.Apply();File.WriteAllBytes(Output+"/Renders/BusinessDistrict_Reference_"+view+".png",tex.EncodeToPNG());RenderTexture.active=previous;cam.targetTexture=null;Object.DestroyImmediate(tex);rt.Release();Object.DestroyImmediate(rt);}}
        finally{RenderSettings.fog=fog;Object.DestroyImmediate(go);}return "Captured top and perspective reference views.";
    }
    public static string ValidateRoutes()
    {
        Guard();var scene=SceneManager.GetActiveScene();if(scene.path!=ScenePath)throw new Exception("Wrong scene");Physics.SyncTransforms();
        var report=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Output+"/Reports/BusinessDistrict_reference.json"));
        const int n=141;const float step=5;var free=new bool[n*n];var reached=new bool[n*n];var heights=new float[n*n];
        for(int iz=0;iz<n;iz++)for(int ix=0;ix<n;ix++)
        {float x=(ix-70)*step,z=(iz-70)*step;int id=iz*n+ix;if(x*x+z*z>348*348)continue;
            if(!Physics.Raycast(V(x,z,2.5f),Vector3.down,out RaycastHit hit,3.5f,~0,QueryTriggerInteraction.Ignore)||hit.normal.y<.85f||hit.point.y<-.1f)continue;
            heights[id]=hit.point.y;free[id]=!Physics.CheckCapsule(hit.point+Vector3.up*.4f,hit.point+Vector3.up*1.65f,.32f,~0,QueryTriggerInteraction.Ignore);}
        Func<float,float,int> index=(x,z)=>Mathf.Clamp(Mathf.RoundToInt(z/step)+70,0,140)*n+Mathf.Clamp(Mathf.RoundToInt(x/step)+70,0,140);
        int start=index(14,65);if(!free[start])throw new Exception("Default spawn route grid blocked.");var q=new Queue<int>();q.Enqueue(start);reached[start]=true;
        while(q.Count>0){int a=q.Dequeue();foreach(int delta in new[]{-1,1,-n,n}){int b=a+delta;if(b<0||b>=n*n||Mathf.Abs(a%n-b%n)>1||!free[b]||reached[b]||Mathf.Abs(heights[a]-heights[b])>.35f)continue;Vector3 p=V((a%n-70)*step,(a/n-70)*step,heights[a]),end=V((b%n-70)*step,(b/n-70)*step,heights[b]);if(Physics.CapsuleCast(p+Vector3.up*.4f,p+Vector3.up*1.65f,.32f,(end-p).normalized,Vector3.Distance(p,end),~0,QueryTriggerInteraction.Ignore))continue;reached[b]=true;q.Enqueue(b);}}
        var results=report["routeGoals"].Select(g=>{float x=(float)g["x"],z=(float)g["z"];int target=index(x,z);return new{name=(string)g["name"],connected=reached[target]};}).ToArray();
        var result=Json(new{method="Editor physics capsule traversal on 5m grid; not Play Mode or NavMesh",reachableCells=reached.Count(v=>v),destinations=results,passed=results.All(r=>r.connected)});File.WriteAllText(Output+"/Reports/BusinessDistrict_reference_routes.json",result);return result;
    }
}
