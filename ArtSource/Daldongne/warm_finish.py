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
