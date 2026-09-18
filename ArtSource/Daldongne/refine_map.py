"""Revision 2: open stair corridors, correct bridge support, subway entrance and details.
Execute after build_map + props with helper definitions in scope.
"""
def remove_named(prefixes):
    for o in list(bpy.data.objects):
        if any(o.name.startswith(p) for p in prefixes):bpy.data.objects.remove(o,do_unlink=True)

def cut_box(target_name,name,x0,x1,y0,y1,z0,z1):
    target=bpy.data.objects.get(target_name)
    if target.data.users>1:target.data=target.data.copy()
    cutter=box('CUT_'+name,((x0+x1)/2,(y0+y1)/2,(z0+z1)/2),(x1-x0,y1-y0,z1-z0),'stone')
    bpy.context.view_layer.objects.active=target
    target.select_set(True)
    mod=target.modifiers.new('Open_'+name,'BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cutter
    bpy.ops.object.modifier_apply(modifier=mod.name)
    target.select_set(False);bpy.data.objects.remove(cutter,do_unlink=True)

cuts=[('Commercial','ShoppingWest',-22.2,-19.2,-5.3,-.85,.8,4),
 ('Commercial','ShoppingCentre',-.9,2.1,-5.3,-.85,.8,4),
 ('Middle','CentralLower',-2.8,.4,4.8,6.65,2.9,7),
 ('Middle','WestLower',-22.6,-19.4,4.8,8.4,2.9,7),
 ('Upper','CentralMiddle',-2.8,.4,14.8,18.65,5.9,10),
 ('Upper','WestMiddle',-26,-21,14.8,17.75,5.9,10),
 ('Upper','EastConnection',16.7,19.9,14.8,17.55,5.9,10),
 ('Crest','CentralCrest',-2.8,.4,22.8,27.95,8.9,13),
 ('Crest','WestCrest',-21,-17,24.1,26.2,8.9,13)]
for tier,name,*coords in cuts:cut_box('Terrain_Terrace_'+tier,name,*coords)

# Open the raised shop promenade above the approach stairs.
remove_named(['Walk_Path_ShopFront','PavingJoint_ShopFront'])
for i,(lo,hi) in enumerate([(-29,-22.2),(-19.2,-.9),(2.1,29)]):path('ShopFront_'+str(i),(lo+hi)/2,-4,3,hi-lo,2)
# Remove remnants of the ledge caps that crossed the approach corridors.
for o in list(bpy.data.objects):
    if o.name.startswith(('Wall_Commercial','WallCap_Commercial')) and not 'Edge' in o.name:
        if (abs(o.location.x+20.7)<2.5 or abs(o.location.x-.6)<2.4):bpy.data.objects.remove(o,do_unlink=True)

# Descending bridge supports must remain below their destination treads.
support=bpy.data.objects['Support_Stair_BridgeNorth']
for i in range(4,8):support.data.vertices[i].co.z=2.0
support.data.update()

# Eastern path formerly intersected the blue-roof house; relocate it into the garden gap.
remove_named(['Walk_Stair_EastConnection','Support_Stair_EastConnection','Rail_EastConnection','Walk_Path_ResidentialEast','PavingJoint_ResidentialEast'])
stairs('EastConnection',(18.3,11,6),(18.3,17.45,9),2.6)
path('ResidentialEast',18.3,18.6,9,3,2.3)

# A genuinely descending station entry, set into the island instead of on a raised stair block.
remove_named(['Walk_Stair_SubwayVisible','Support_Stair_SubwayVisible','Station_EntryDark'])
cut_box('Terrain_Island','SubwayEntry',-22.1,-12.9,-25.72,-21.4,-2.25,1.4)
cut_box('Walk_Path_StationSquare','SubwayEntryPlaza',-22.1,-12.9,-25.72,-21.4,-2.25,1.4)
for o in list(bpy.data.objects):
    if o.name.startswith('PavingJoint_StationSquare'):bpy.data.objects.remove(o,do_unlink=True)
stairs('SubwayVisible',(-17.5,-25.72,1.01),(-17.5,-21.65,-1.85),8.8,False)
support=bpy.data.objects['Support_Stair_SubwayVisible']
for i in range(4,8):support.data.vertices[i].co.z=-2.7
support.data.update()
box('Station_TunnelDark',(-17.5,-21.39,-.57),(9.18,.08,2.64),'dark')
for x in [-22.03,-12.97]:box('Station_EntrySidewall',(x,-23.58,-.49),(.15,4.28,3.05),'stone')
box('Station_EntryThreshold',(-17.5,-25.98,1.04),(9.2,.4,.13),'yellow')
lettering('Station_Line2Digit','2',-13.4,-25.86,4.99,.39,.55,'white')

# The exit road becomes a supported short causeway at the island edge.
box('Terrain_RoadCauseway',(7,-29.5,-.85),(8.3,9,3.72),'stone')
for x in [3,11]:rail('CausewayGuard',(x,-27.5,1.19),(x,-34,1.19))
# Keep the T-junction open: no curb or yellow boundary running through the joining road.
for o in list(bpy.data.objects):
    if o.name.startswith(('Curb_Main','Road_YellowEdge')) and o.location.y<-12.5 and o.scale.x>50:
        y=o.location.y; z=o.location.z; sy=o.scale.y; sz=o.scale.z; mat=o.data.materials[0]
        original=o.name.split('.')[0]
        bpy.data.objects.remove(o,do_unlink=True)
        for lo,hi in [(-30.5,2.9),(11.1,30.5)]:box(original+'_JunctionOpen',((lo+hi)/2,y,z),(hi-lo,sy,sz),mat)

# Friendly scale figures are decorative and do not obstruct the walk mesh.
people=[(-17,-27,1),(-24,-20,1),(-10.4,-10.3,5.68),(-17,-3.8,3),(-9,-3.8,3),(7,-.3,5),
 (11,2.8,6),(21,-3.8,3),(26,-15.3,1),(-14,9,6),(-22,18.7,9),(-10,27.8,12),(18.3,19.1,9)]
for i,(x,y,z) in enumerate(people):
    shirt=['blue','red','cream','green'][i%4]
    for dx in [-.13,.13]:
        box('NPC_Leg',(x+dx,y,z+.36),(.18,.22,.62),'dark');box('NPC_Shoe',(x+dx,y-.08,z+.06),(.21,.36,.12),'metal')
    box('NPC_Shirt',(x,y,z+.94),(.52,.30,.57),shirt)
    ico('NPC_Head',(x,y,z+1.43),(.22,.22,.27),'cream')
    ico('NPC_Hair',(x,y+.012,z+1.56),(.235,.23,.15),'wood')
    for dx in [-.34,.34]:beam('NPC_Arm',(x+dx,y,z+1.14),(x+dx,y-.05,z+.70),.15,shirt)

# Ground-cover gardens soften the retaining walls without filling walking corridors.
for i,(x,y,z) in enumerate([(-29,0,3),(-28,19,9),(-25,27,9),(-18,17.2,9),(-11,17,9),(-6,16.8,9),
 (2.6,23.2,12),(11,30,12),(26,18,9),(28,10,6),(15,6.6,6),(-10,7,6),(-26,-16,1),(-6,-22,1),(16,-24,1),(25,-21,1)]):
    tree('Tree_Extra_'+str(i),x,y,z,scale=.72)
    planter('ExtraFlower_'+str(i),x+.8,y-.35,z,1.1,.75)

# Slightly lower sun exposure preserves the warm window color; both cameras are deliverable.
bpy.data.lights['Sun_WarmEvening'].energy=2.1
bpy.context.scene.view_settings.view_transform='AgX'
bpy.context.scene.camera=bpy.data.objects['Camera_Isometric']
bpy.context.scene.camera.data.ortho_scale=84
bpy.context.scene['RevisionNotes']='Stair corridors carved; east stair relocated; bridge supports fixed; descending subway entrance; supported road exit.'
result={'objects':len(bpy.data.objects),'corridors_carved':len(cuts),'scene':'Daldongne final geometry','triangles_instanced':sum(len(p.vertices)-2 for o in bpy.data.objects if o.type=='MESH' for p in o.data.polygons)}
