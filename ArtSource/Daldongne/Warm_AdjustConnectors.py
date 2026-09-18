import bpy,math,random
from mathutils import Vector
M={m.name:m for m in bpy.data.materials}
cache={}
def mesh(name, verts, faces, mat):
    d=bpy.data.meshes.new(name+'_Mesh'); d.from_pydata(verts,[],faces); d.update()
    o=bpy.data.objects.new(name,d); bpy.context.scene.collection.objects.link(o)
    d.materials.append(M[mat] if isinstance(mat,str) else mat)
    return o
def box(name,loc,size,mat):
    key=('box',mat.name if hasattr(mat,'name') else mat)
    if key not in cache:
        v=[(-.5,-.5,-.5),(.5,-.5,-.5),(.5,.5,-.5),(-.5,.5,-.5),(-.5,-.5,.5),(.5,-.5,.5),(.5,.5,.5),(-.5,.5,.5)]
        f=[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
        o=mesh(name,v,f,mat); cache[key]=o.data
    else:
        o=bpy.data.objects.new(name,cache[key]); bpy.context.scene.collection.objects.link(o)
    o.location=loc; o.scale=size
    return o
def cyl(name,loc,radius,depth,mat,vertices=8):
    key=('cyl',mat.name if hasattr(mat,'name') else mat,vertices)
    if key not in cache:
        v=[(math.cos(i*2*math.pi/vertices),math.sin(i*2*math.pi/vertices),z) for z in [-.5,.5] for i in range(vertices)]
        f=[tuple(reversed(range(vertices))),tuple(range(vertices,vertices*2))]+[(i,(i+1)%vertices,(i+1)%vertices+vertices,i+vertices) for i in range(vertices)]
        o=mesh(name,v,f,mat); cache[key]=o.data
    else:
        o=bpy.data.objects.new(name,cache[key]); bpy.context.scene.collection.objects.link(o)
    o.location=loc; o.scale=(radius,radius,depth); return o
def ico(name,loc,scale,mat,subdivisions=1):
    key=('ico',mat.name if hasattr(mat,'name') else mat,subdivisions)
    if key not in cache:
        import bmesh
        d=bpy.data.meshes.new(name+'_Mesh'); bm=bmesh.new()
        bmesh.ops.create_icosphere(bm,subdivisions=subdivisions,radius=1)
        bm.to_mesh(d); bm.free(); d.materials.append(M[mat] if isinstance(mat,str) else mat)
        cache[key]=d
    o=bpy.data.objects.new(name,cache[key]); bpy.context.scene.collection.objects.link(o)
    o.location=loc; o.scale=(scale,scale,scale) if isinstance(scale,(int,float)) else scale; return o
def beam(name,a,b,width,mat):
    va,vb=Vector(a),Vector(b); o=box(name,(va+vb)/2,(width,width,(vb-va).length),mat)
    o.rotation_euler=(vb-va).to_track_quat('Z','Y').to_euler(); return o
def polygon_prism(name,outline,zbottom,ztop,mat):
    n=len(outline); v=[(x,y,z) for z in [zbottom,ztop] for x,y in outline]
    return mesh(name,v,[tuple(reversed(range(n))),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],mat)

def rail(name,a,b,height=1.1):
    a,b=Vector(a),Vector(b); dist=(b-a).length; n=max(1,math.ceil(dist/2))
    for i in range(n+1):
        p=a.lerp(b,i/n); beam(name+'_Post',p,p+Vector((0,0,height)),.105,'metal')
    beam(name+'_Top',a+Vector((0,0,height)),b+Vector((0,0,height)),.09,'metal')
    beam(name+'_Lower',a+Vector((0,0,.42)),b+Vector((0,0,.42)),.06,'metal')

def stairs(name,a,b,width=2.8,rails=True):
    a,b=Vector(a),Vector(b); delta=b-a; rise=delta.z; horizontal=Vector((delta.x,delta.y,0)); length=horizontal.length
    count=max(2,math.ceil(abs(rise)/.22)); step=horizontal/count; angle=-math.atan2(delta.x,delta.y)
    for i in range(count):
        p=a+step*(i+.5); top=a.z+rise*(i+1)/count
        o=box('Walk_Stair_'+name+f'_{i:02d}',(p.x,p.y,top-.16),(width,length/count+.018,.32),'paver_light')
        o.rotation_euler.z=angle
    # Closed supporting wedge; thin steps remain visible on top.
    normal=Vector((-horizontal.y,horizontal.x,0)).normalized()*width/2
    v=[tuple(a-normal-Vector((0,0,.35))),tuple(a+normal-Vector((0,0,.35))),tuple(b+normal-Vector((0,0,.35))),tuple(b-normal-Vector((0,0,.35))),tuple(a-normal-Vector((0,0,1))),tuple(a+normal-Vector((0,0,1))),tuple(Vector((b.x,b.y,a.z-1))+normal),tuple(Vector((b.x,b.y,a.z-1))-normal)]
    mesh('Support_Stair_'+name,v,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],'stone')
    if rails:
        for side in [-1,1]: rail('Rail_'+name, a+normal*side,b+normal*side)

def stone_wall(name,a,b,zbase,height):
    a,b=Vector((a[0],a[1],0)),Vector((b[0],b[1],0)); delta=b-a; n=max(1,math.ceil(delta.length/2.4)); rows=math.ceil(height/.78)
    for j in range(rows):
        for i in range(n):
            p=a.lerp(b,(i+.5)/n)
            o=box('Wall_'+name,(p.x,p.y,zbase+(j+.5)*height/rows),(delta.length/n-.04,.53,height/rows-.035),'stone' if (i+j)%3 else 'paver_dark')
            o.rotation_euler.z=math.atan2(delta.y,delta.x)
    p=(a+b)/2
    o=box('WallCap_'+name,(p.x,p.y,zbase+height+.09),(delta.length+.2,.70,.18),'trim'); o.rotation_euler.z=math.atan2(delta.y,delta.x)

def path(name,x,y,z,w,d):
    box('Walk_Path_'+name,(x,y,z-.09),(w,d,.18),'pavement')
    # Visible joints use thin inset strips instead of coplanar overlapping tiles.
    for i in range(1,int(w/1.5)+1):
        xx=x-w/2+i*1.5
        if xx<x+w/2-.12: box('PavingJoint_'+name,(xx,y,z+.002),(.018,d-.08,.008),'paver_dark')
    for j in range(1,int(d/1.5)+1):
        yy=y-d/2+j*1.5
        if yy<y+d/2-.12:box('PavingJoint_'+name,(x,yy,z+.003),(w-.08,.018,.008),'paver_dark')

def planter(name,x,y,z,w=1.5,d=1.0):
    box('Planter_'+name,(x,y,z+.23),(w,d,.46),'trim')
    box('Soil_'+name,(x,y,z+.47),(w-.14,d-.14,.035),'wood')
    for i in range(3):
        xx=x+(i-1)*w*.28
        ico('Shrub_'+name,(xx,y,z+.72),(.42,.42,.40),'leaf2')
        for q in range(2): ico('Flower_'+name,(xx+random.uniform(-.2,.2),y+random.uniform(-.18,.18),z+1.05),.085,random.choice(['flower','pink','white']))


"""Walk surfaces and conservative, measured clearance corridors, metres / Blender Z-up."""
ROUTES=[
 ('ShopWest',(-21,-6,1.13),(-21,-1.4,3),1.55),
 ('ShopCenter',(2.3,-6,1.13),(2.3,-1.3,3),1.6),
 ('CentralLower',(-1.3,-.05,3),(-1.3,7,6),2.4),
 ('CentralMiddle',(-1.3,11.8,6),(-1.3,18.8,9),2.4),
 ('CentralCrest',(-1.3,21.4,9),(-1.3,28.4,12),2.4),
 ('WestLower',(-21,1.4,3),(-21,8.4,6),2.2),
 ('WestMiddle',(-21,11,6),(-24,18,9),2.2),
 ('WestCrest',(-17.5,19.5,9),(-17.5,25.6,12),2.1),
 ('CityHall',(7,-3.8,3),(7,2.7,6),5),
 ('East',(18.1,9.8,6),(18.1,16.8,9),2.2),
 ('BridgeSouth',(-17.5,-18.5,1),(-12,-12.5,5.2),2.4),
 ('BridgeNorth',(-3.8,-6.8,5.2),(-.7,-1.3,3),2.0),
 ('Subway',(-17.5,-25.72,1.01),(-17.5,-21.65,-1.85),8.8)]
FLATS=[
 ('BridgeDeck',(-12,-12.5,5.2),(-3.8,-6.8,5.2),2.4),
 ('CentralFoot',(-1.3,-.65,3),(2.3,-.65,3),1.3),
 ('CentralLanding',(-1.3,7,6),(-1.3,11.8,6),2.4),
 ('CentralUpperLanding',(-1.3,18.8,9),(-1.3,21.4,9),2.4),
 ('WestFoot',(-21,-1.4,3),(-21,1.4,3),1.6),
 ('WestLanding',(-21,8.4,6),(-21,11,6),2.2),
 ('Laundry',(-21,9.5,6),(-11.2,9.5,6),2),
 ('RestPlaza',(-24,18.9,9),(-17.5,18.9,9),1.6),
 ('WestMiddleTop',(-24,18,9),(-24,18.9,9),2.2),
 ('WestCrestFoot',(-17.5,18.9,9),(-17.5,19.5,9),2.1),
 ('WestTop',(-17.5,25.6,12),(-17.5,29,12),2.1),
 ('CrestSquare',(-17.5,29,12),(-1.3,29,12),2.4),
 ('CentralCrestTop',(-1.3,28.4,12),(-1.3,29,12),2.4),
 ('CityHallPlaza',(7,2.7,6),(7,4.4,6),10),
 ('CivicEast',(11.8,3.7,6),(14.5,3.7,6),1.8),
 ('EastLower',(14.5,3.7,6),(14.5,9.2,6),1.8),
 ('EastApproach',(14.5,9.2,6),(18.1,9.2,6),1.8),
 ('EastFoot',(18.1,9.2,6),(18.1,9.8,6),2.2),
 ('EastUpper',(18.1,16.8,9),(12.6,16.8,9),1.8),
 ('HomeAFront',(12.6,16,9),(3.4,16,9),1.6),
 ('CentralEast',(3.4,16,9),(3.4,20,9),1.6),
 ('UpperCross',(-1.3,20,9),(3.4,20,9),1.8),
 ('SubwayLanding',(-17.5,-21.65,-1.85),(-17.5,-20.1,-1.85),8.8),
 ('StationApproach',(-18,-19.04545,1),(-17.5,-18.5,1),2.4)]

def route_bounds(a,b,w,margin=0):
    a,b=Vector(a),Vector(b);d=b-a;d.z=0;n=Vector((-d.y,d.x,0)).normalized()*(w/2+margin)
    pts=[a-n,a+n,b-n,b+n]
    return [min(p[i] for p in pts) for i in range(3)],[max(p[i] for p in pts) for i in range(3)]

def object_bounds(o):
    pts=[o.matrix_world@Vector(v) for v in o.bound_box]
    return [min(p[i] for p in pts) for i in range(3)],[max(p[i] for p in pts) for i in range(3)]

def cut_corridor(o,a,b,width,name,landing=.5):
    # A vertically swept strip, aligned with the route, preserving adjacent terrain.
    a,b=Vector(a),Vector(b);d=b-a;d.z=0;u=d.normalized();n=Vector((-u.y,u.x,0))*(width/2+.10)
    low=min(a.z,b.z)-.40;high=max(a.z,b.z)+2.35
    outline=[(p.x,p.y) for p in (a-u*landing-n,b+u*landing-n,b+u*landing+n,a-u*landing+n)]
    cutter=polygon_prism('CUT_'+name,outline,low,high,'stone')
    if o.data.users>1:o.data=o.data.copy()
    bpy.context.view_layer.objects.active=o
    mod=o.modifiers.new('Clear_'+name,'BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
    bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)

def build_clear_routes():
    oldstairs=('Walk_Stair_','Support_Stair_','Walk_Bridge_','Bridge_Crossing','Bridge_Pier',
      'Rail_ShoppingAccess','Rail_Central','Rail_West','Rail_EastConnection','Rail_CityHallEntry','Rail_Bridge','Rail_Subway')
    oldpaths=('CentralLanding','CentralUpperLanding','ResidentialEast','CrestSquare','RestPlaza','WestLanding','CityHall_Plaza','CityHall_Roof','LaundryPlaza','StationApproach')
    for o in list(bpy.data.objects):
        if (o.name.startswith(oldstairs) or any(o.name.startswith(p+n) for p in ('Walk_Path_','PavingJoint_') for n in oldpaths)
          or o.name.startswith(('NPC_','ShoppingLantern','PathLantern','TerraceGuard'))):bpy.data.objects.remove(o,do_unlink=True)
    bpy.context.view_layer.update()
    cuts=0
    # Cut existing solids and paving before laying the rebuilt treads and landings.
    candidates=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith(('Terrain_','Wall_','WallCap_','Walk_Path_','PavingJoint_'))]
    for name,a,b,w in ROUTES+FLATS:
        if name=='BridgeDeck':continue
        lo,hi=route_bounds(a,b,w,.15);lo[2]=min(a[2],b[2])-.4;hi[2]=max(a[2],b[2])+2.3
        for o in candidates[:]:
            if not len(o.data.polygons):continue
            ol,oh=object_bounds(o)
            if any(oh[i]<=lo[i] or ol[i]>=hi[i] for i in range(3)):continue
            # Ground exactly level with a flat connector is already a valid floor.
            if a[2]==b[2] and oh[2]<=a[2]+.025:continue
            cut_corridor(o,a,b,w,name);cuts+=1
    # Additional foundation for the civic-side passage spanning the two tiers.
    box('Terrain_CivicWalkFoundation',(14.5,4.25,4.40),(1.8,2.9,2.98),'stone')
    def surface(name,a,b,w,thickness=.18):
        a,b=Vector(a),Vector(b);d=b-a;horizontal=Vector((d.x,d.y,0));n=Vector((-d.y,d.x,0)).normalized()*w/2
        if abs(d.z)<.001:
            o=box('Walk_Path_'+name,(a+b)/2-Vector((0,0,thickness/2)),(w,horizontal.length+.02,thickness),'pavement')
            o.rotation_euler.z=-math.atan2(d.x,d.y)
        else:
            count=math.ceil(abs(d.z)/.18);angle=-math.atan2(d.x,d.y)
            for i in range(count):
                t=(i if d.z>0 else i+1)/count;top=a.z+d.z*t;p=a+horizontal*((i+.5)/count)
                o=box('Walk_Stair_'+name+'_'+str(i).zfill(2),(p.x,p.y,top-.12),(w,horizontal.length/count+.01,.24),'paver_light')
                o.rotation_euler.z=angle
            floor=min(a.z,b.z)-.45
            v=[tuple(p) for p in(a-n-Vector((0,0,.18)),b-n-Vector((0,0,.18)),b+n-Vector((0,0,.18)),a+n-Vector((0,0,.18)))];v += [(x,y,floor) for x,y,z in v]
            mesh('Support_Stair_'+name,v,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],'stone')
            v=[tuple(p) for p in (a-n,b-n,b+n,a+n)]
            ramp=mesh('Collider_Ramp_'+name,v,[(0,1,2,3)],'CollisionInvisible');ramp['navigationSurface']=True
        return n
    for name,a,b,w in ROUTES:
        n=surface(name,a,b,w)
        if name!='Subway':
            for side in(-1,1):rail('Rail_Warm_'+name,Vector(a)+n*side,Vector(b)+n*side,1.02)
    for name,a,b,w in FLATS:
        n=surface(name,a,b,w)
        if name=='BridgeDeck':
            for side in(-1,1):rail('Rail_Warm_BridgeDeck',Vector(a)+n*side,Vector(b)+n*side,1.02)
            for p in(a,b):box('Bridge_Pier',(p[0],p[1],2.95),(.55,.65,4.15),'stone')
    tunnel=bpy.data.objects.get('Station_TunnelDark')
    if tunnel:tunnel.location.y=-19.45
    # Corridor registry is carried in the editable scene and a local Unity test manifest.
    bpy.context.scene['NavigationRoutes']=__import__('json').dumps([{'name':n,'a':a,'b':b,'width':w,'kind':'stair' if (n,a,b,w) in ROUTES else 'flat'} for n,a,b,w in ROUTES+FLATS])
    return cuts

def clear_route_props():
    import re
    # Remove complete tree clusters when their foliage intersects the player volume.
    groups={};removed=[];allroutes=ROUTES+FLATS
    for o in bpy.data.objects:
        if o.type!='MESH':continue
        match=re.match(r'(Tree_(?:Extra_)?\d+)_',o.name)
        key=match.group(1) if match else o.name
        if match or o.name.startswith(('Garden_','Planter_','Soil_','Shrub_','Flower_','WarmLamp_','WarmPot','StationBench','ShelterBench','Shelter_','Vending_')):groups.setdefault(key,[]).append(o)
    for key,objects in groups.items():
        hit=False
        for o in objects:
            lo,hi=object_bounds(o)
            for name,a,b,w in allroutes:
                # Broadphase then samples along every path. Width includes a generous clear strip.
                rl,rh=route_bounds(a,b,w,.05)
                if hi[0]<rl[0] or lo[0]>rh[0] or hi[1]<rl[1] or lo[1]>rh[1]:continue
                aa,bb=Vector(a),Vector(b);steps=max(2,math.ceil((bb-aa).length/.25))
                for j in range(steps+1):
                    p=aa.lerp(bb,j/steps)
                    if hi[2]<p.z+.14 or lo[2]>p.z+2.15:continue
                    # Nearest horizontal point to this object's world bound.
                    dx=max(lo[0]-p.x,0,p.x-hi[0]);dy=max(lo[1]-p.y,0,p.y-hi[1])
                    if math.hypot(dx,dy)<min(w/2,.85):hit=True;break
                if hit:break
            if hit:break
        if hit:
            removed.append(key)
            for o in objects:bpy.data.objects.remove(o,do_unlink=True)
    return removed

def surface(name,a,b,w,thickness=.18):
    a,b=Vector(a),Vector(b);d=b-a;horizontal=Vector((d.x,d.y,0));n=Vector((-d.y,d.x,0)).normalized()*w/2
    if abs(d.z)<.001:
        o=box('Walk_Path_'+name,(a+b)/2-Vector((0,0,thickness/2)),(w,horizontal.length+.02,thickness),'pavement')
        o.rotation_euler.z=-math.atan2(d.x,d.y)
    else:
        count=math.ceil(abs(d.z)/.18);angle=-math.atan2(d.x,d.y)
        for i in range(count):
            t=(i if d.z>0 else i+1)/count;top=a.z+d.z*t;p=a+horizontal*((i+.5)/count)
            o=box('Walk_Stair_'+name+'_'+str(i).zfill(2),(p.x,p.y,top-.12),(w,horizontal.length/count+.01,.24),'paver_light')
            o.rotation_euler.z=angle
        floor=min(a.z,b.z)-.45
        v=[tuple(p) for p in(a-n-Vector((0,0,.18)),b-n-Vector((0,0,.18)),b+n-Vector((0,0,.18)),a+n-Vector((0,0,.18)))];v += [(x,y,floor) for x,y,z in v]
        mesh('Support_Stair_'+name,v,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],'stone')
        v=[tuple(p) for p in (a-n,b-n,b+n,a+n)]
        ramp=mesh('Collider_Ramp_'+name,v,[(0,1,2,3)],'CollisionInvisible');ramp['navigationSurface']=True
    return n

for o in list(bpy.data.objects):
    if o.name.startswith(('Walk_Stair_WestCrest_','Support_Stair_WestCrest','Collider_Ramp_WestCrest','Rail_Warm_WestCrest')):
        bpy.data.objects.remove(o,do_unlink=True)
name,a,b,w=next(r for r in ROUTES if r[0]=='WestCrest')
n=surface(name,a,b,w)
for side in (-1,1):rail('Rail_Warm_'+name,Vector(a)+n*side,Vector(b)+n*side,1.02)
changed={'RestPlaza','WestMiddleTop','WestCrestFoot','WestTop','CrestSquare','CentralCrestTop','EastLower','EastApproach','EastFoot','StationApproach'}
for name,a,b,w in FLATS:
    if name not in changed:continue
    for o in list(bpy.data.objects):
        if o.name=='Walk_Path_'+name:bpy.data.objects.remove(o,do_unlink=True)
    surface(name,a,b,w)
bpy.context.view_layer.update()

"""Measured follow-up corrections after the first Unity capsule audit."""
def finish_warm_routes():
    corrected=[]
    # Landings run across a flight at a corner. Trim to the exact inclined surface,
    # instead of allowing a flat slab to bury its last 1-2 m of treads.
    pairs={'CentralCrest':['Walk_Path_CrestSquare'],
      'WestMiddle':['Walk_Path_RestPlaza'],'East':['Walk_Path_EastUpper'],
      'BridgeSouth':['Walk_Path_BridgeDeck','Bridge_Pier','Bridge_Pier.001'],'BridgeNorth':['Walk_Path_BridgeDeck']}
    for key,names in pairs.items():
        _,av,bv,w=next(r for r in ROUTES if r[0]==key)
        a,b=Vector(av),Vector(bv);d=b-a;u=Vector((d.x,d.y,0)).normalized();n=Vector((-u.y,u.x,0))*(w/2+.07)
        a=a-u*.03;b=b+u*.003
        corners=(a-n,b-n,b+n,a+n)
        vertices=[tuple(p+Vector((0,0,.005))) for p in corners]
        vertices += [(p.x,p.y,max(a.z,b.z)+3) for p in corners]
        cutter=mesh('CUT_Landing_'+key,vertices,[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'stone')
        for name in names:
            o=bpy.data.objects[name]
            if o.data.users>1:o.data=o.data.copy()
            bpy.context.view_layer.objects.active=o
            mod=o.modifiers.new('OpenLastTreads_'+key,'BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
            bpy.ops.object.modifier_apply(modifier=mod.name);corrected.append(name+':'+key)
        bpy.data.objects.remove(cutter,do_unlink=True)
    for o in list(bpy.data.objects):
        if o.name.startswith(('Wall_Middle','WallCap_Middle')):
            lo,hi=object_bounds(o)
            if hi[0]>2 and lo[0]<12 and hi[1]>4.2 and lo[1]<7:
                cut_corridor(o,(7,4.0,6),(7,7,6),10,'CivicDoorWalk',.05)
        if o.name.startswith('WarmLamp_3_') or(o.type=='MESH' and not len(o.data.polygons)):
            bpy.data.objects.remove(o,do_unlink=True)
    # Slim connector extensions join the station, staircase and street corners.
    # Above the subway entrance, the paved approach remains on the retained solid base.
    bpy.context.view_layer.update()
    # Keep paving 3.5 cm proud of grass, preventing coplanar flicker in close views.
    tops={'Terrain_Island':1,'Terrain_Terrace_Commercial':3,'Terrain_Terrace_Middle':6,
          'Terrain_Terrace_Upper':9,'Terrain_Terrace_Crest':12,'Terrain_GosiwonPodium':12}
    for name,top in tops.items():
        o=bpy.data.objects[name]
        if o.data.users>1:o.data=o.data.copy()
        for v in o.data.vertices:
            if abs(v.co.z-top)<.001:v.co.z-=.035
        o.data.update()
    bpy.context.scene['WarmNavigationRevision']='Landing slabs clipped to stair slopes; civic doorway wall cap removed; 0.35m radius capsule audit.'
    return corrected

finish_warm_routes()
result={'objects':len(bpy.data.objects),'note':'Trimmed intersecting landing slabs; opened civic portico; removed orphan lantern crown and empty cut meshes.'}

bpy.context.scene['NavigationRoutes']=__import__('json').dumps([{'name':n,'a':a,'b':b,'width':w,'kind':'stair' if (n,a,b,w) in ROUTES else 'flat'} for n,a,b,w in ROUTES+FLATS])
result={'objects':len(bpy.data.objects),'stairs':len(ROUTES),'flats':len(FLATS),'note':'Added flat turning spaces; clipped bridge pier under ramp; separated paving from terrain by 3.5 cm.'}
