"""Widen the outside edges of the two upper landings after capsule-edge audit."""
def clear_warm_outer_edges():
    changed=0
    for route in FLATS:
        name,a,b,w=route
        if name not in ('RestPlaza','WestMiddleTop','CrestSquare'):continue
        lo,hi=route_bounds(a,b,w,.35);lo[2]=a[2]-.4;hi[2]=a[2]+2.4
        for o in list(bpy.data.objects):
            if o.type!='MESH' or not len(o.data.polygons) or not o.name.startswith(('Wall_','WallCap_')):continue
            ol,oh=object_bounds(o)
            if any(oh[i]<lo[i] or ol[i]>hi[i] for i in range(3)):continue
            cut_corridor(o,a,b,w+.30,'OuterLanding_'+name,.7);changed+=1
    # A retaining foundation carries the extended ridge promenade.
    o=box('Terrain_CrestWalkFoundation',(-9.4,29.0,10.43),(18.6,2.4,2.98),'stone')
    # Keep its east end below the final central stair treads.
    _,av,bv,w=next(r for r in ROUTES if r[0]=='CentralCrest')
    a,b=Vector(av),Vector(bv);d=b-a;u=Vector((d.x,d.y,0)).normalized();n=Vector((-u.y,u.x,0))*(w/2+.1)
    corners=(a-n,b-n,b+n,a+n)
    v=[tuple(p-Vector((0,0,.08))) for p in corners]+[(p.x,p.y,15) for p in corners]
    cutter=mesh('CUT_CrestFoundation',v,[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],'stone')
    bpy.context.view_layer.objects.active=o
    mod=o.modifiers.new('UnderStair','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
    bpy.ops.object.modifier_apply(modifier=mod.name);bpy.data.objects.remove(cutter,do_unlink=True)
    for o in list(bpy.data.objects):
        if o.type=='MESH' and not len(o.data.polygons):bpy.data.objects.remove(o,do_unlink=True)
    return changed

result={'edge_openings':clear_warm_outer_edges(),'note':'Upper landing retaining caps opened beyond capsule footprint; ridge path structurally supported.'}
