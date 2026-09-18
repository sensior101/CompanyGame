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
