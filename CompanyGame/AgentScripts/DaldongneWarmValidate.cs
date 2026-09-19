// Read-only scene physics audit. Invoke after DaldongneWarmImport.Build().
// Routes contain Blender [x,y,z]; Unity coordinates here are [x,z,y].
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public static class DaldongneWarmValidate
{
    const float Radius=.35f, Height=1.8f, Spacing=.25f, GroundTolerance=.23f;
    sealed class Geometry
    {
        public Collider collider;
        public Vector3[] vertices;
        public int[] indices;
        public Bounds bounds;
        public bool closedBody;
    }
    static readonly Dictionary<Collider,Geometry> cache=new Dictionary<Collider,Geometry>();

    public static object Run()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/daldongnaemap.unity")
            throw new InvalidOperationException("Open daldongnaemap.unity before the navigation audit.");
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../ArtSource/Daldongne"));
        var input=JObject.Parse(File.ReadAllText(Path.Combine(folder,"warm_routes.json")));
        var routes=input["routes"] as JArray;
        if(routes==null || routes.Count==0)throw new InvalidDataException("No warm-map routes were supplied.");
        Physics.SyncTransforms();cache.Clear();
        var colliders=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Collider>(true))
            .Where(c=>c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
        if(colliders.Length==0)throw new InvalidOperationException("The imported scene contains no active solid colliders.");
        foreach(var collider in colliders)CacheGeometry(collider);
        var closedBodies=cache.Values.Where(g=>g.closedBody).ToArray();
        var failures=new JArray();var summaries=new JArray();
        int samples=0,supportHits=0,clearanceQueries=0,belowFootContacts=0,interiorQueries=0,footprintCasts=0;
        float greatestAcceptedLift=0;
        foreach(var route in routes)
        {
            string name=(string)route["name"]??"Unnamed route";
            Vector3 a=ReadPoint(route["a"]),b=ReadPoint(route["b"]);
            float width=(float?)route["width"]??1;
            Vector3 flat=b-a;flat.y=0;
            Vector3 right=flat.sqrMagnitude>.00001f?Vector3.Cross(Vector3.up,flat.normalized):Vector3.right;
            float edge=Mathf.Max(0,Mathf.Min(width/2-.4f,.5f));
            float[] offsets=edge>.001f?new [] {-edge,0,edge}:new [] {0f};
            int intervals=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/Spacing));
            int routeFailures=failures.Count,routeSamples=0;
            for(int i=0;i<=intervals;i++)
            foreach(float offset in offsets)
            {
                float t=i/(float)intervals;
                Vector3 nominal=Vector3.Lerp(a,b,t)+right*offset;
                var hits=Physics.RaycastAll(nominal+Vector3.up*.32f,Vector3.down,1.05f,~0,QueryTriggerInteraction.Ignore)
                    .Where(h=>h.collider.gameObject.scene==scene && h.normal.y>.55f)
                    .OrderBy(h=>h.distance).ToArray();
                Vector3 foot=nominal;string support=null;
                if(hits.Length==0)
                    AddFailure(failures,name,t,offset,nominal,nominal,"missing_ground",new string[0],"No upward-facing support within 0.32 m above / 0.73 m below the route.");
                else
                {
                    var hit=hits[0];support=hit.collider.name;supportHits++;
                    if(Mathf.Abs(hit.point.y-nominal.y)>GroundTolerance)
                        AddFailure(failures,name,t,offset,nominal,hit.point,"ground_height_mismatch",new [] {support},"Support differs from the intended foot height by more than 0.23 m.");
                    foot.y=hit.point.y+.025f;
                    // Sweep the actual bottom sphere: at a ramp/landing junction the
                    // center ray can hit a flat while the sphere rests on the ramp edge.
                    footprintCasts++;
                    if(TryFootprintSupport(scene,nominal,out var supportedFoot,out var footprintHit))
                    {
                        float lift=supportedFoot.y-hit.point.y;
                        if(supportedFoot.y-nominal.y>GroundTolerance || lift>GroundTolerance
                            || nominal.y-supportedFoot.y>GroundTolerance)
                            AddFailure(failures,name,t,offset,nominal,supportedFoot,"footprint_height_mismatch",
                                new [] {footprintHit.collider.name},"The physical sphere support requires more than 0.23 m height adjustment; it was not accepted as a floor placement.");
                        else
                        {
                            foot=supportedFoot;
                            greatestAcceptedLift=Mathf.Max(greatestAcceptedLift,lift);
                        }
                    }
                    else AddFailure(failures,name,t,offset,nominal,foot,"missing_footprint_support",
                        new [] {support},"The center ray found ground but the physical foot sphere found no upward-facing floor.");
                }
                Vector3 low=foot+Vector3.up*Radius, high=foot+Vector3.up*(Height-Radius);
                var overlaps=Physics.OverlapCapsule(low,high,Radius,~0,QueryTriggerInteraction.Ignore)
                    .Where(c=>c.gameObject.scene==scene).Distinct().ToArray();
                clearanceQueries++;
                var blockers=new HashSet<string>(StringComparer.Ordinal);
                foreach(var collider in overlaps)
                {
                    bool knownGround=IsGroundFamily(collider.name);
                    if(knownGround && cache.TryGetValue(collider,out var geometry)
                        && !IntersectsAboveFoot(geometry,low,high,Radius,foot.y+.004f))
                    {
                        belowFootContacts++;continue;
                    }
                    blockers.Add(collider.name);
                }
                // PhysX triangle meshes can miss a capsule completely enclosed in a
                // solid mesh. Ray parity catches uncleared terrace/wall volumes too.
                Vector3 torso=foot+Vector3.up*.85f;
                foreach(var geometry in closedBodies)
                {
                    if(!geometry.bounds.Contains(torso))continue;
                    interiorQueries++;
                    if(PointInside(geometry,torso))blockers.Add(geometry.collider.name);
                }
                if(blockers.Count>0)
                    AddFailure(failures,name,t,offset,nominal,foot,"blocked_capsule",blockers.OrderBy(n=>n),"Actual 0.35 m radius / 1.8 m high capsule intersects or is enclosed by solid geometry.");
                samples++;routeSamples++;
            }
            summaries.Add(new JObject { ["route"]=name,["samples"]=routeSamples,
                ["failures"]=failures.Count-routeFailures,["passed"]=failures.Count==routeFailures });
        }
        var result=new JObject { ["scene"]=scene.path,["timestampUtc"]=DateTime.UtcNow.ToString("O"),
            ["input"]=Path.Combine(folder,"warm_routes.json"),["radius"]=Radius,["height"]=Height,
            ["maxSampleSpacing"]=Spacing,["offsetRule"]="center and +/- min(width / 2 - 0.4, 0.5) m",
            ["coordinateOrder"]="All point arrays in this report are Blender [x,y,z].",
            ["routeCount"]=routes.Count,["sampleCount"]=samples,["solidColliders"]=colliders.Length,
            ["supportHits"]=supportHits,["clearanceQueries"]=clearanceQueries,
            ["footprintSphereCasts"]=footprintCasts,["greatestAcceptedFootLift"]=greatestAcceptedLift,
            ["contactsVerifiedBelowFoot"]=belowFootContacts,["solidInteriorQueries"]=interiorQueries,
            ["passed"]=failures.Count==0,["failureCount"]=failures.Count,
            ["routes"]=summaries,["failures"]=failures };
        string output=Path.Combine(folder,"warm_navigation_validation.json");
        File.WriteAllText(output,result.ToString(Formatting.Indented));
        return new {passed=failures.Count==0,routes=routes.Count,samples,failures=failures.Count,report=output,
            failedRoutes=summaries.Where(r=>(int)r["failures"]>0).Select(r=>(string)r["route"]).ToArray()};
    }

    static bool TryFootprintSupport(Scene scene,Vector3 nominal,out Vector3 foot,out RaycastHit support)
    {
        Vector3 origin=nominal+Vector3.up*(Radius+.55f);
        var hits=Physics.SphereCastAll(origin,Radius,Vector3.down,1.30f,~0,QueryTriggerInteraction.Ignore)
            .Where(h=>h.collider.gameObject.scene==scene && IsGroundFamily(h.collider.name)
                && h.normal.y>.65f && h.distance>.00001f)
            .OrderBy(h=>h.distance).ToArray();
        foot=nominal;support=default(RaycastHit);
        if(hits.Length==0)return false;
        support=hits[0];foot.y=origin.y-support.distance-Radius+.025f;
        return true;
    }

    // Actual CharacterController.Move calls, not interpolation or repeated teleports.
    // Every authored ramp and flat connection is walked in both directions.
    public static object Traverse()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/daldongnaemap.unity")
            throw new InvalidOperationException("Open the warm map before the controller traversal audit.");
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../ArtSource/Daldongne"));
        var routes=(JArray)JObject.Parse(File.ReadAllText(Path.Combine(folder,"warm_routes.json")))["routes"];
        var reports=new JArray();bool wasDirty=scene.isDirty;
        // An unsaved additive scene shares the actual physics world. Its probe can
        // be removed without adding objects or dirtying the user's map scene.
        var probeScene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        GameObject probe=null;
        try
        {
            probe=new GameObject("Temporary Warm Village Traversal Probe");
            probe.hideFlags=HideFlags.HideAndDontSave;
            var controller=probe.AddComponent<CharacterController>();
            controller.radius=Radius;controller.height=Height;controller.center=Vector3.up*(Height/2);
            controller.slopeLimit=45;controller.stepOffset=.23f;controller.skinWidth=.02f;
            controller.minMoveDistance=0;controller.detectCollisions=true;
            SceneManager.SetActiveScene(scene);
            // A dirty unrelated scene may have been preserved by the importer.
            // Ignore its colliders for this temporary controller only.
            for(int s=0;s<SceneManager.sceneCount;s++)
            {
                var other=SceneManager.GetSceneAt(s);
                if(other==scene || other==probeScene || !other.isLoaded)continue;
                foreach(var c in other.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Collider>(true)))
                    if(c.enabled)Physics.IgnoreCollision(controller,c,true);
            }
            Physics.SyncTransforms();
            foreach(var route in routes)
            foreach(bool reverse in new [] {false,true})
            {
                string name=(string)route["name"];
                Vector3 start=ReadPoint(route[reverse?"b":"a"]),end=ReadPoint(route[reverse?"a":"b"]);
                Vector3 horizontal=end-start;horizontal.y=0;
                float length=horizontal.magnitude;
                Vector3 direction=length>.0001f?horizontal/length:Vector3.forward;
                bool initialSupport=TryFootprintSupport(scene,start,out var initialFoot,out var initialHit);
                bool plausibleStart=initialSupport && Mathf.Abs(initialFoot.y-start.y)<=GroundTolerance;
                if(!plausibleStart)initialFoot=start+Vector3.up*.04f;
                controller.enabled=false;probe.transform.position=initialFoot+Vector3.up*.025f;
                controller.enabled=true;Physics.SyncTransforms();
                int moves=0,stalled=0,sideContacts=0,headContacts=0;
                bool fell=false;float largestGroundDeviation=0;
                int budget=Mathf.CeilToInt(length/.075f)*4+80;
                for(int iteration=0;iteration<budget;iteration++)
                {
                    Vector3 remaining=end-probe.transform.position;remaining.y=0;
                    if(remaining.magnitude<=.075f)break;
                    Vector3 before=probe.transform.position;
                    Vector3 horizontalMove=Vector3.ClampMagnitude(remaining,.075f);
                    // Continuous downward velocity keeps the capsule on descending
                    // ramps; climbing is resolved solely by CharacterController.
                    CollisionFlags flags=controller.Move(horizontalMove+Vector3.down*.10f);
                    moves++;
                    if((flags&CollisionFlags.Sides)!=0)sideContacts++;
                    if((flags&CollisionFlags.Above)!=0)headContacts++;
                    Vector3 after=probe.transform.position;
                    Vector3 progress=after-before;progress.y=0;
                    stalled=progress.magnitude<.006f?stalled+1:0;
                    float along=Mathf.Clamp01(Vector3.Dot(after-start,direction)/Mathf.Max(length,.0001f));
                    float intendedHeight=Mathf.Lerp(start.y,end.y,along);
                    largestGroundDeviation=Mathf.Max(largestGroundDeviation,Mathf.Abs(after.y-intendedHeight));
                    if(after.y<intendedHeight-.45f){fell=true;break;}
                    if(stalled>=18)break;
                }
                Vector3 finish=probe.transform.position,delta=end-finish;delta.y=0;
                float endHeightError=Mathf.Abs(finish.y-end.y);
                bool passed=plausibleStart && delta.magnitude<=.12f && endHeightError<=GroundTolerance
                    && !fell && headContacts==0 && largestGroundDeviation<=GroundTolerance+.03f;
                var nearby=Physics.OverlapCapsule(finish+Vector3.up*Radius,finish+Vector3.up*(Height-Radius),
                    Radius+.035f,~0,QueryTriggerInteraction.Ignore)
                    .Where(c=>c.gameObject.scene==scene).Select(c=>c.name).Distinct().OrderBy(n=>n).ToArray();
                reports.Add(new JObject { ["route"]=name,["kind"]=(string)route["kind"],
                    ["direction"]=reverse?"b_to_a":"a_to_b",["passed"]=passed,["moves"]=moves,
                    ["start"]=Point(start),["end"]=Point(end),["actualEnd"]=Point(finish),
                    ["remainingHorizontal"]=delta.magnitude,["endHeightError"]=endHeightError,
                    ["maxVerticalDeviation"]=largestGroundDeviation,["initialSupportPlausible"]=plausibleStart,
                    ["fell"]=fell,["stalledMoves"]=stalled,["sideContacts"]=sideContacts,
                    ["headContacts"]=headContacts,["nearbyAtEnd"]=new JArray(nearby) });
            }
        }
        finally
        {
            if(probe!=null)UnityEngine.Object.DestroyImmediate(probe);
            if(probeScene.IsValid() && probeScene.isLoaded)EditorSceneManager.CloseScene(probeScene,true);
            if(scene.IsValid() && scene.isLoaded)SceneManager.SetActiveScene(scene);
            Physics.SyncTransforms();
        }
        int failed=reports.Count(r=>!(bool)r["passed"]);
        var output=new JObject { ["scene"]=scene.path,["timestampUtc"]=DateTime.UtcNow.ToString("O"),
            ["radius"]=Radius,["height"]=Height,["stepOffset"]=.23f,["skinWidth"]=.02f,["slopeLimit"]=45,
            ["test"]="Actual CharacterController.Move, all supplied routes in both directions; only initial placement is teleported.",
            ["coordinateOrder"]="Blender [x,y,z]",["traversals"]=reports.Count,["failureCount"]=failed,
            ["passed"]=failed==0,["mapDirtyBefore"]=wasDirty,["mapDirtyAfter"]=scene.isDirty,["results"]=reports };
        string path=Path.Combine(folder,"warm_controller_validation.json");
        File.WriteAllText(path,output.ToString(Formatting.Indented));
        return new {passed=failed==0,traversals=reports.Count,failed,report=path,
            failedRoutes=reports.Where(r=>!(bool)r["passed"]).Select(r=>(string)r["route"]+":"+(string)r["direction"]).ToArray()};
    }

    static Vector3 ReadPoint(JToken p)
    {
        if(!(p is JArray a) || a.Count!=3)throw new InvalidDataException("Route endpoints must be three coordinates.");
        return new Vector3((float)a[0],(float)a[2],(float)a[1]);
    }
    static JArray Point(Vector3 p)=>new JArray(p.x,p.z,p.y);
    static void AddFailure(JArray errors,string name,float t,float offset,Vector3 nominal,Vector3 actual,string type,IEnumerable<string> objects,string detail)
    {
        errors.Add(new JObject { ["route"]=name,["t"]=t,["offset"]=offset,["type"]=type,
            ["expectedFoot"]=Point(nominal),["testedFoot"]=Point(actual),
            ["objects"]=new JArray(objects),["detail"]=detail });
    }
    static bool IsGroundFamily(string n)=>n.StartsWith("Terrain_",StringComparison.Ordinal)
        ||n.StartsWith("Walk_",StringComparison.Ordinal)||n.StartsWith("Collider_Ramp_",StringComparison.Ordinal)
        ||n.StartsWith("Support_Stair_",StringComparison.Ordinal)||n.StartsWith("Road_",StringComparison.Ordinal)
        ||n.IndexOf("_foundation",StringComparison.OrdinalIgnoreCase)>=0
        ||n.EndsWith("_BasePlinth",StringComparison.Ordinal)||n.EndsWith("_StoneBase",StringComparison.Ordinal)
        ||n=="CityHall_PorticoFloor";
    static bool IsClosedBody(string n)=>n.StartsWith("Terrain_",StringComparison.Ordinal)
        ||n.StartsWith("Wall_",StringComparison.Ordinal)||n.StartsWith("WallCap_",StringComparison.Ordinal)
        ||n.StartsWith("Building_",StringComparison.Ordinal)||n.StartsWith("Support_Stair_",StringComparison.Ordinal)
        ||n.EndsWith("_stucco",StringComparison.Ordinal)||n.EndsWith("_walls",StringComparison.Ordinal);
    static void CacheGeometry(Collider collider)
    {
        if(!(collider is MeshCollider meshCollider) || meshCollider.sharedMesh==null)return;
        Mesh mesh=meshCollider.sharedMesh;
        cache[collider]=new Geometry {collider=collider,indices=mesh.triangles,
            vertices=mesh.vertices.Select(v=>collider.transform.TransformPoint(v)).ToArray(),
            bounds=collider.bounds,closedBody=IsClosedBody(collider.name)};
    }

    static bool IntersectsAboveFoot(Geometry g,Vector3 low,Vector3 high,float radius,float footY)
    {
        Vector3 min=Vector3.Min(low,high)-Vector3.one*radius;
        Vector3 max=Vector3.Max(low,high)+Vector3.one*radius;
        float radiusSquared=radius*radius+1e-7f;
        for(int i=0;i<g.indices.Length;i+=3)
        {
            var a=g.vertices[g.indices[i]];var b=g.vertices[g.indices[i+1]];var c=g.vertices[g.indices[i+2]];
            Vector3 tmin=Vector3.Min(a,Vector3.Min(b,c)),tmax=Vector3.Max(a,Vector3.Max(b,c));
            if(tmax.y<=footY || tmax.x<min.x || tmin.x>max.x || tmax.z<min.z || tmin.z>max.z || tmin.y>max.y)continue;
            // Clip the actual triangle above the sole plane. Only the remaining
            // geometry can block the body; a tall collider AABB is insufficient.
            var polygon=new List<Vector3>(4);Vector3[] input={a,b,c};
            Vector3 previous=c;bool previousInside=c.y>footY;
            foreach(var current in input)
            {
                bool inside=current.y>footY;
                if(inside!=previousInside)
                    polygon.Add(Vector3.Lerp(previous,current,(footY-previous.y)/(current.y-previous.y)));
                if(inside)polygon.Add(current);
                previous=current;previousInside=inside;
            }
            for(int j=1;j+1<polygon.Count;j++)
                if(SegmentTriangleDistanceSquared(low,high,polygon[0],polygon[j],polygon[j+1])<=radiusSquared)return true;
        }
        return false;
    }

    static bool PointInside(Geometry g,Vector3 point)
    {
        Vector3 direction=new Vector3(.8231f,.3163f,.4697f).normalized;
        var intersections=new List<float>();
        for(int i=0;i<g.indices.Length;i+=3)
        {
            float t;
            if(RayTriangle(point,direction,g.vertices[g.indices[i]],g.vertices[g.indices[i+1]],g.vertices[g.indices[i+2]],out t) && t>.0001f)
                intersections.Add(t);
        }
        intersections.Sort();int distinct=0;float previous=float.NegativeInfinity;
        foreach(float t in intersections)
            if(t-previous>.0001f){distinct++;previous=t;}
        return distinct%2==1;
    }
    static bool RayTriangle(Vector3 origin,Vector3 direction,Vector3 a,Vector3 b,Vector3 c,out float t)
    {
        Vector3 e1=b-a,e2=c-a,p=Vector3.Cross(direction,e2);float determinant=Vector3.Dot(e1,p);t=0;
        if(Mathf.Abs(determinant)<1e-8f)return false;
        float inverse=1/determinant;Vector3 s=origin-a;float u=Vector3.Dot(s,p)*inverse;
        if(u<-.000001f||u>1.000001f)return false;
        Vector3 q=Vector3.Cross(s,e1);float v=Vector3.Dot(direction,q)*inverse;
        if(v<-.000001f||u+v>1.000001f)return false;
        t=Vector3.Dot(e2,q)*inverse;return true;
    }
    static float SegmentTriangleDistanceSquared(Vector3 p,Vector3 q,Vector3 a,Vector3 b,Vector3 c)
    {
        float t;Vector3 delta=q-p;
        if(RayTriangle(p,delta,a,b,c,out t) && t>=0 && t<=1)return 0;
        return Mathf.Min((p-ClosestTriangle(p,a,b,c)).sqrMagnitude,
            (q-ClosestTriangle(q,a,b,c)).sqrMagnitude,
            SegmentDistanceSquared(p,q,a,b),SegmentDistanceSquared(p,q,b,c),SegmentDistanceSquared(p,q,c,a));
    }
    static Vector3 ClosestTriangle(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        Vector3 ab=b-a,ac=c-a,ap=p-a;
        float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
        if(d1<=0&&d2<=0)return a;
        Vector3 bp=p-b;float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
        if(d3>=0&&d4<=d3)return b;
        float vc=d1*d4-d3*d2;
        if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
        Vector3 cp=p-c;float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
        if(d6>=0&&d5<=d6)return c;
        float vb=d5*d2-d1*d6;
        if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
        float va=d3*d6-d5*d4;
        if(va<=0&&(d4-d3)>=0&&(d5-d6)>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
        float denominator=va+vb+vc;
        if(Mathf.Abs(denominator)<1e-12f)return a;
        float inv=1/denominator;return a+ab*(vb*inv)+ac*(vc*inv);
    }
    static float SegmentDistanceSquared(Vector3 p1,Vector3 q1,Vector3 p2,Vector3 q2)
    {
        Vector3 d1=q1-p1,d2=q2-p2,r=p1-p2;
        float a=Vector3.Dot(d1,d1),e=Vector3.Dot(d2,d2),f=Vector3.Dot(d2,r),s,t;
        if(a<1e-10f&&e<1e-10f)return r.sqrMagnitude;
        if(a<1e-10f){s=0;t=Mathf.Clamp01(f/e);}
        else
        {
            float c=Vector3.Dot(d1,r);
            if(e<1e-10f){t=0;s=Mathf.Clamp01(-c/a);}
            else
            {
                float b=Vector3.Dot(d1,d2),denominator=a*e-b*b;
                s=denominator!=0?Mathf.Clamp01((b*f-c*e)/denominator):0;
                t=(b*s+f)/e;
                if(t<0){t=0;s=Mathf.Clamp01(-c/a);}
                else if(t>1){t=1;s=Mathf.Clamp01((b-c)/a);}
            }
        }
        return ((p1+d1*s)-(p2+d2*t)).sqrMagnitude;
    }
}
