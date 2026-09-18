import bpy
for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)
for m in list(bpy.data.materials): bpy.data.materials.remove(m)
"""Higgsfield / Blender 5.2 — authored low-poly hillside neighborhood.
Run concatenated with props.py (inserted before build() invocation).
Coordinates in metres, +Z up, -Y toward the storefronts.
"""
import bpy, math, random
from mathutils import Vector
random.seed(170926)

M = {}
def material(key, color, emission=0, roughness=.85, metallic=0):
    m = bpy.data.materials.new(key)
    m.diffuse_color = (*color,1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Roughness'].default_value=roughness
    p.inputs['Metallic'].default_value=metallic
    if emission:
        p.inputs['Emission Color'].default_value=(*color,1)
        p.inputs['Emission Strength'].default_value=emission
    M[key]=m
    return m

palette = {
 'stone':(.48,.51,.52),'cream':(.79,.73,.57),'trim':(.87,.83,.69),
 'glass':(.105,.28,.30),'window':(1,.62,.21),'metal':(.10,.15,.16),
 'wood':(.43,.23,.10),'red':(.68,.19,.10),'blue':(.13,.30,.59),
 'green':(.28,.59,.15),'yellow':(.95,.62,.08),'leaf1':(.18,.34,.07),
 'leaf2':(.33,.49,.09),'leaf3':(.50,.62,.16),'orange':(.91,.35,.09),
 'white':(.92,.91,.79),'dark':(.11,.15,.18),'grass':(.27,.36,.13),
 'pavement':(.67,.64,.52),'paver_light':(.76,.71,.59),'paver_dark':(.58,.57,.50),
 'rock1':(.30,.36,.39),'rock2':(.42,.46,.47),'rock3':(.48,.51,.48),
 'road':(.18,.21,.23),'water':(.055,.27,.40),'foam':(.42,.68,.68),
 'pink':(.91,.47,.54),'flower':(1,.73,.17),'purple':(.48,.30,.59)
}
for key,c in palette.items(): material(key,c,emission=2.1 if key=='window' else 0)

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

def build():
    scene=bpy.context.scene
    scene.unit_settings.system='METRIC'
    # Island shoreline and faceted masonry cliff.
    outline=[(32*math.cos(2*math.pi*i/32),2+31*math.sin(2*math.pi*i/32)) for i in range(32)]
    polygon_prism('Terrain_Island',outline,-3.2,1.0,'grass')
    for i in range(32):
        a=outline[i]; b=outline[(i+1)%32]
        stone_wall('Coastal_'+str(i),a,b,-2.7,3.3)
        for j in range(3):
            t=(j+.5)/3; x=a[0]*(1-t)+b[0]*t; y=a[1]*(1-t)+b[1]*t
            n=Vector((x,y-2,0)).normalized()
            ico('Rock_Shore',(x+n.x*.6,y+n.y*.6,-2.7), (random.uniform(.8,1.5),random.uniform(.9,1.4),random.uniform(.8,1.5)),random.choice(['rock1','rock2','rock3']))
            if j%2==0: ico('Foam_Shore',(x+n.x*1.5,y+n.y*1.5,-3.48),(1.0,.40,.025),'foam')
    water_outline=[(40*math.cos(2*math.pi*i/64),2+39*math.sin(2*math.pi*i/64)) for i in range(64)]
    polygon_prism('Water_Diorama',water_outline,-4.8,-3.55,'water')
    # The terraces step upward toward the residential district.
    tiers=[('Commercial',[(-29,-5),(29,-5),(30,9),(26,18),(-27,18),(-31,6)],1,3),
           ('Middle',[(-28,5),(27,5),(28,14),(24,24),(-23,25),(-29,16)],3,6),
           ('Upper',[(-25,15),(25,15),(22,25),(11,31),(-13,30),(-24,24)],6,9),
           ('Crest',[(-19,23),(18,23),(11,31),(-12,30),(-20,27)],9,12)]
    for name,poly,z0,z1 in tiers:
        polygon_prism('Terrain_Terrace_'+name,poly,z0-.1,z1,'grass')
        # Leave stair entrances open in the long south retaining walls.
        yy=poly[0][1]
        spans={'Commercial':[(-29,-22),(-19,-1),(2,29)],'Middle':[(-28,-23),(-19,-3),(1,27)],'Upper':[(-25,-24),(-20,-3),(1,25)],'Crest':[(-19,-3),(1,18)]}[name]
        for k,(x1,x2) in enumerate(spans): stone_wall(name+str(k),(x1,yy),(x2,yy),z0,z1-z0+.25)
        for k in range(1,len(poly)):
            stone_wall(name+'Edge'+str(k),poly[k],poly[(k+1)%len(poly)],z0,z1-z0+.20)
    # Asphalt T intersection and sidewalks.
    box('Road_Main', (0,-9,1.035),(62,8,.10),'road')
    box('Road_Exit', (7,-23,1.04),(8,23,.10),'road')
    for y in [-13.10,-4.90]:
        box('Curb_Main',(0,y,1.16),(61,.26,.25),'trim')
        box('Road_YellowEdge',(0,y+(.24 if y<-9 else -.24),1.097),(61,.12,.015),'yellow')
    for x in [2.9,11.1]:
        box('Curb_Exit',(x,-23,1.16),(.25,22,.25),'trim')
        box('Road_YellowEdge',(x+(.24 if x<7 else -.24),-23,1.099),(.12,22,.015),'yellow')
    for x in range(-29,31,4):
        if x<2 or x>12: box('Road_CentreDash',(x,-9,1.103),(1.8,.12,.018),'white')
    for y in range(-31,-14,4): box('Road_CentreDash',(7,y,1.11),(.13,1.8,.015),'white')
    for x in [-7,17]:
        for j in range(7):box('Road_ZebraCrossing',(x,-12+j*.95,1.115),(2.5,.50,.02),'white')
    for j in range(7):box('Road_ZebraExit',(3.8+j*.95,-18,1.12),(.52,2.3,.02),'white')
    path('ShopFront',0,-4.0,3,58,2)
    # Low stair threshold from road to shop promenade; joins across entire strip.
    for a,b in [((-20.7,-5,1.3),(-20.7,-1,3)),((.6,-5,1.3),(.6,-1,3))]: stairs('ShoppingAccess',a,b,2.5)
    path('CoastalWest',-18,-16,1,24,3)
    path('CoastalEast',21,-16,1,17,3)
    # Main commercial landmarks.
    shop('Hamburger',-25,-.4,3,w=6,d=4.8,label='햄버거',accent='orange')
    shop('Convenience',-16,-.4,3,w=8,d=4.8,label='편의점',accent='blue')
    shop('Snack',-6.5,-.4,3,w=6.6,d=4.8,label='분식집',accent='red')
    shop('Bakery',17,-.4,3,w=7,d=4.8,label='베이커리',accent='orange')
    shop('Cafe',25,-.4,3,w=7,d=4.8,label='카페',accent='dark')
    # City hall: octagonal civic landmark and generous stair forecourt.
    poly=[(7+5.2*math.cos(math.pi/8+i*math.pi/4),9+4.5*math.sin(math.pi/8+i*math.pi/4)) for i in range(8)]
    polygon_prism('Building_CityHall',poly,6,12.2,'cream')
    polygon_prism('Building_CityHall_Cornice',[(7+(x-7)*1.05,9+(y-9)*1.05) for x,y in poly],11.9,12.35,'trim')
    for x in [3.6,5.3,7,8.7,10.4]:
        box('CityHall_Window',(x,4.84,8.55),(1.4,.10,4.25),'glass')
        box('CityHall_InnerGlow',(x,4.76,8.12),(.88,.035,2.7),'window')
        box('CityHall_Mullion',(x+.78,4.67,8.55),(.12,.13,4.30),'metal')
    sign('CityHall_Sign','시청',7,4.53,11.5,6.6,1.45,'dark')
    polygon_prism('Terrain_CityHallPodium',[(1,1.35),(13,1.35),(13,8),(1,8)],3,5.90,'stone')
    path('CityHall_Plaza',7,3.05,6,10,3.4)
    stairs('CityHallEntry',(7,-3.8,3),(7,1.5,6),5.7)
    path('CityHall_Roof',7,9,12.36,7.3,6.2)
    planter('CityHallRoofA',4.4,8,12.38,1.8,1.4); planter('CityHallRoofB',9.8,10.8,12.38,1.8,1.4)
    box('CityHall_RoofTower',(8.5,10.1,13.25),(1.3,1.3,1.8),'trim')
    for x in [3.5,10.7]:
        beam('CityHall_Flagpole',(x,2.65,6),(x,2.65,10.8),.055,'metal')
        box('CityHall_Flag',(x+.55,2.65,10.22),(1.1,.045,.68),'white')
        ico('CityHall_FlagEmblem',(x+.55,2.59,10.22),(.22,.03,.22),'red' if x<7 else 'green')
    # Stair network, landings and neighborhood paths.
    stairs('CentralLower',(-1.2,-.1,3),(-1.2,6.5,6),2.8)
    path('CentralLanding',-1.2,9.8,6,3.0,6.6)
    stairs('CentralMiddle',(-1.2,12,6),(-1.2,18.5,9),2.8)
    path('CentralUpperLanding',-1.2,20.5,9,3,4)
    stairs('CentralCrest',(-1.2,21.7,9),(-1.2,27.8,12),2.8)
    stairs('WestLower',(-21,1.8,3),(-21,8.3,6),2.8)
    path('LaundryPlaza',-15,9.7,6,9.2,3.5)
    path('WestLanding',-21,10,6,3.2,3.6)
    stairs('WestMiddle',(-21.5,11,6),(-24,17.6,9),2.8)
    path('RestPlaza',-22,19,9,7.8,3.1)
    stairs('WestCrest',(-22,19.8,9),(-18.5,26,12),2.6)
    path('CrestSquare',-12,27.8,12,12,3)
    path('ResidentialEast',17.5,17.5,9,3,5)
    stairs('EastConnection',(23,10,6),(21,16.4,9),2.6)
    shop('Laundry',-14.7,12.8,6,w=7.6,d=4.2,h=3.7,label='빨래방',accent='dark')
    polygon_prism('Terrain_GosiwonPodium',[(-16.3,20.7),(-6.7,20.7),(-6.7,27),(-16.3,27)],9,12,'stone')
    shop('Gosiwon',-11.5,24.1,12,w=7.8,d=5.4,h=6.0,label='고시원',accent='dark')
    # Small upstairs windows articulate the boarding-house scale.
    for x in [-14,-11.6,-9.2]:
        box('Gosiwon_UpperWindow',(x,21.36,16.9),(1.3,.10,1.5),'window')
        box('Gosiwon_UpperLintel',(x,21.28,17.7),(1.55,.20,.14),'trim')
    # Hillside homes with distinct pitched roofs.
    house('Home_A',7.6,19.5,9,w=6,d=5,roof='blue')
    house('Home_B',20.5,21.3,9,w=5.6,d=4.8,roof='red')
    house('Home_C',23.5,10.7,6,w=5.7,d=4.7,roof='blue')
    house('Home_D',7.4,27.1,12,w=5.8,d=4.8,roof='red')
    # Neighborhood rest shelter with vending machines and benches.
    for x in [-25,-19.3]:
        for y in [20.3,23.2]:box('Shelter_Post',(x,y,10.3),(.22,.22,2.6),'wood')
    box('Shelter_Roof',(-22.15,21.75,11.7),(6.6,3.6,.34),'blue')
    sign('Shelter_Sign','쉼터',-22.15,19.72,11.55,4.5,1.25,'dark')
    bench('RestBench',-22,21.8,9)
    for i,col in enumerate(['red','blue']):
        box('VendingMachine',(-25+i*.85,19.8,9.9),(.75,.68,1.8),col)
        box('VendingMachine_Display',(-25+i*.85,19.44,10.1),(.53,.04,.92),'glass')
        for n in range(3):box('VendingMachine_Bottle',(-25.18+i*.85+n*.17,19.40,10.14),(.10,.035,.44),'window')
    # Subway station and pedestrian bridge across the road.
    path('StationSquare',-17.5,-23.0,1,14,9)
    for x in [-23.2,-11.8]:
        box('Station_StonePylon',(x,-23.1,2.4),(.6,4.7,2.8),'stone')
        rail('Station_SafetyRail',(x,-20.5,1.1),(x,-17.5,1.1))
    box('Station_EntryDark',(-17.5,-23.8,1.14),(10.5,4.7,.16),'dark')
    stairs('SubwayVisible',(-17.5,-21.8,1.25),(-17.5,-25.7,3.0),8.8,False)
    for x in [-22.4,-12.6]:box('Station_CanopyColumn',(x,-23.9,3.2),(.18,.18,4.1),'metal')
    box('Station_GlassCanopy',(-17.5,-23.8,5.24),(10.5,3.6,.17),'glass')
    sign('Station_Sign','달동네역',-17.5,-25.69,4.95,10.1,1.65,'dark')
    # Green line 2 token.
    token=cyl('Station_Line2',(-13.4,-25.78,4.99),.49,.10,'green',16); token.rotation_euler.x=math.pi/2
    path('StationApproach',-17.5,-18.2,1,4,2.5)
    stairs('BridgeSouth',(-17.5,-18.2,1),(-12.1,-12.5,5.5),2.8)
    a,b=Vector((-12.1,-12.5,5.5)),Vector((-7.8,-6.9,5.5));mid=(a+b)/2
    o=box('Walk_Bridge_Deck',mid,(2.8,(b-a).length,.32),'paver_light');o.rotation_euler.z=-math.atan2(b.x-a.x,b.y-a.y)
    normal=Vector((-(b-a).y,(b-a).x,0)).normalized()*1.4
    for side in [-1,1]: rail('Bridge_Crossing',a+normal*side,b+normal*side)
    for p in [a,b]:
        box('Bridge_Pier',(p.x,p.y,2.95),(.75,.9,5.1),'stone')
    stairs('BridgeNorth',(-7.8,-6.9,5.5),(-3.8,-2.6,3),2.8)
    # East bus stop with a vivid blue roof, transparent-looking teal panels.
    path('BusStopSquare',23,-15.9,1,11,3.4)
    for x in [18.7,26.8]:box('BusStop_Column',(x,-15.6,2.55),(.16,.16,3.1),'metal')
    box('BusStop_BackGlass',(22.8,-17,2.4),(8.2,.10,2.8),'glass')
    box('BusStop_Canopy',(22.8,-15.5,4.2),(9.3,3.5,.20),'blue')
    sign('BusStop_Sign','버스정류장',22.8,-17.32,4.28,8.1,1.35,'blue')
    bench('BusStopBench',23,-16.2,1)
    car('TownBus',22,-10.7,1.1,color='green',bus=True,angle=math.pi/2)
    car('YellowCar',-23,-10.7,1.1,color='yellow',angle=math.pi/2)
    car('RedCar',8.9,-25,1.1,color='red',angle=0)
    car('CreamVan',-1,-7.1,1.1,color='cream',angle=-math.pi/2)
    # Street furniture and warm lanterns create landmarks along the walking routes.
    for x in [-28,-20,-10,1.5,12.2,21,29]:
        lamp('ShoppingLantern',x,-4.25,3)
    for x,y,z in [(-25,-20,1),(-10,-21,1),(-27,-15,1),(-14,-15,1),(15,-17,1),(28,-17,1),(-19,8.7,6),(-8,9,6),(-3.4,7,6),(-3.4,18.5,9),(1,27.5,12),(-18,26,12),(-25,18,9),(19,16,9),(12.5,3,6)]:lamp('PathLantern',x,y,z)
    for x in [-27,-18,-11,12,20,28]:planter('ShopFlowerBox',x,-3.6,3,1.25,.8)
    for x,y,z in [(-24,-19,1),(-11,-19,1),(15,-16,1),(28,-16,1),(-19,10,6),(-8,10,6),(3,2.9,6),(11,2.9,6),(-16,27.9,12)]:planter('NeighborhoodFlowers',x,y,z)
    bench('CafeTerrace',27,-3.4,3);bench('StationBench',-23,-20,1)
    # Dense planting occupies purposeful garden pockets, leaving roads, plazas and stairs clear.
    groves=[(-27,2.6,3),(-27,8,6),(-27,12,6),(-27,16,9),(-25,25,9),(-21,28,12),
       (-17,18,9),(-13,18,9),(-9,18,9),(-7,22,9),(-5,27,12),(2.4,29,12),(13,29,12),
       (17,29,9),(23,26,9),(26,21,9),(28,16,6),(28,7,6),(26,4,3),
       (14,9,6),(17,9,6),(15,13,6),(20,12,6),(14,17,9),(3,14,6),(3,18,9),
       (-7,6.5,6),(-17,6.5,6),(-24,6.8,6),(-23,3.2,3),(-10,3.8,3),
       (-28,-14.5,1),(-25,-17.5,1),(-28,-21,1),(-24,-25,1),(-9,-23,1),(-6,-20,1),(-4,-16,1),
       (15,-21,1),(18,-24,1),(21,-20,1),(25,-19,1),(28,-14,1)]
    for i,(x,y,z) in enumerate(groves):
        tree('Tree_'+str(i),x,y,z,scale=random.uniform(.77,1.18))
        for q in range(2):
            xx=x+random.uniform(-1.0,1); yy=y+random.uniform(-1,1)
            ico('Garden_Bush',(xx,yy,z+.38),(.65,.7,.55),random.choice(['leaf1','leaf2','leaf3']))
            for j in range(3):ico('Garden_Flower',(xx+random.uniform(-.4,.4),yy+random.uniform(-.4,.4),z+.84),.105,random.choice(['white','flower','pink']))
    # Upper terrace safety rails and overhead neighborhood cables.
    for a,b,z in [((-17,20),(-6,20),9),((3,15),(13,15),9),((15,5),(28,5),6),((-19,23),(-5,23),12)]:rail('TerraceGuard',(*a,z+.1),(*b,z+.1))
    for x,y,z in [(-5,24,12),(14,26,9),(26,17,9)]:
        beam('UtilityPole',(x,y,z),(x,y,z+6.5),.16,'wood'); box('UtilityCrossbar',(x,y,z+6.1),(1.8,.14,.14),'wood')
    pts=[(-5,24,18.1),(14,26,15.1),(26,17,15.1)]
    for a,b in zip(pts,pts[1:]):
        a,b=Vector(a),Vector(b)
        for offset in [-.5,.5]:
            prev=None
            for i in range(9):
                t=i/8;p=a.lerp(b,t)+Vector((offset,0,-1.3*4*t*(1-t)))
                if prev is not None:beam('OverheadCable',prev,p,.024,'metal')
                prev=p
    # Exportable cameras and portable lighting are part of the editable source.
    world=bpy.data.worlds.new('Dusk_Ambient');scene.world=world;world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.43,.55,.68,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.55
    def light(name,typ,loc,energy,color):
        d=bpy.data.lights.new(name,typ);d.energy=energy;d.color=color
        o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;return o
    sun=light('Sun_WarmEvening','SUN',(-30,-35,50),2.5,(1,.79,.57));sun.rotation_euler=(math.radians(23),math.radians(-27),math.radians(-35));sun.data.angle=.15
    fill=light('SkyFill','SUN',(25,10,40),.7,(.65,.78,1));fill.rotation_euler=(.45,.5,2.8)
    for x,y,z in [(-16,-3.5,5),(7,3.5,8),(22,-3.5,5),(-17,-23,3)]:
        l=light('Warm_Landmark','POINT',(x,y,z),100,(1,.56,.20));l.data.shadow_soft_size=2
    d=bpy.data.cameras.new('Camera_Isometric'); cam=bpy.data.objects.new('Camera_Isometric',d);scene.collection.objects.link(cam)
    cam.location=(47,-72,63);target=Vector((0,2,6));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();d.type='ORTHO';d.ortho_scale=90;scene.camera=cam
    d2=bpy.data.cameras.new('Camera_TopDown');cam2=bpy.data.objects.new('Camera_TopDown',d2);scene.collection.objects.link(cam2);cam2.location=(0,2,100);d2.type='ORTHO';d2.ortho_scale=82
    scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=1100;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.media_type='IMAGE'
    scene.render.film_transparent=False
    scene.view_settings.view_transform='AgX'
    scene['MapName']='달동네 / Daldongne';scene['ReferenceLayout']='User supplied isometric + top down hillside town';scene['Units']='metres';scene['Navigation']='Connected stairs / commercial promenade / central spine / upper residential loops'

"""Reusable low-poly Daldongne props. Blender Z up; fronts face -Y.

Expects box/cyl/ico/beam/mesh helpers and M materials from the scene builder.
No external fonts: Korean sign lettering is actual extruded stroke geometry.
"""
import math
import random


# Coordinates inside a unit glyph, from left/bottom to right/top.
_J = {
    'ㄱ': [[(.08,.88),(.88,.88),(.88,.08)]],
    'ㄴ': [[(.10,.90),(.10,.12),(.90,.12)]],
    'ㄷ': [[(.90,.88),(.10,.88),(.10,.12),(.90,.12)]],
    'ㄹ': [[(.10,.90),(.90,.90),(.90,.53),(.10,.53),(.10,.12),(.90,.12)]],
    'ㅁ': [[(.12,.88),(.88,.88),(.88,.12),(.12,.12),(.12,.88)]],
    'ㅂ': [[(.12,.94),(.12,.12),(.88,.12),(.88,.94)],[(.12,.58),(.88,.58)]],
    'ㅅ': [[(.49,.91),(.10,.11)],[(.49,.80),(.91,.11)]],
    'ㅇ': [[(.50,.91),(.80,.79),(.92,.50),(.80,.21),(.50,.09),(.20,.21),(.08,.50),(.20,.79),(.50,.91)]],
    'ㅈ': [[(.08,.88),(.92,.88)],[(.52,.88),(.10,.10)],[(.49,.65),(.91,.10)]],
    'ㅊ': [[(.50,1.0),(.50,.87)],[(.08,.75),(.92,.75)],[(.50,.75),(.10,.07)],[(.48,.56),(.91,.07)]],
    'ㅋ': [[(.08,.90),(.88,.90),(.88,.08)],[(.10,.51),(.88,.51)]],
    'ㅌ': [[(.90,.90),(.10,.90),(.10,.12),(.90,.12)],[(.10,.51),(.86,.51)]],
    'ㅍ': [[(.06,.89),(.94,.89)],[(.06,.12),(.94,.12)],[(.28,.89),(.28,.12)],[(.72,.89),(.72,.12)]],
    'ㅎ': [[(.50,1.0),(.50,.91)],[(.10,.79),(.90,.79)],[(.50,.65),(.77,.55),(.87,.34),(.77,.12),(.50,.03),(.23,.12),(.13,.34),(.23,.55),(.50,.65)]],
    'ㅏ': [[(.28,.04),(.28,.96)],[(.28,.53),(.91,.53)]],
    'ㅑ': [[(.28,.04),(.28,.96)],[(.28,.68),(.91,.68)],[(.28,.35),(.91,.35)]],
    'ㅓ': [[(.72,.04),(.72,.96)],[(.09,.53),(.72,.53)]],
    'ㅕ': [[(.72,.04),(.72,.96)],[(.09,.68),(.72,.68)],[(.09,.35),(.72,.35)]],
    'ㅗ': [[(.04,.26),(.96,.26)],[(.50,.26),(.50,.96)]],
    'ㅛ': [[(.04,.26),(.96,.26)],[(.31,.26),(.31,.96)],[(.69,.26),(.69,.96)]],
    'ㅜ': [[(.04,.74),(.96,.74)],[(.50,.04),(.50,.74)]],
    'ㅠ': [[(.04,.74),(.96,.74)],[(.31,.04),(.31,.74)],[(.69,.04),(.69,.74)]],
    'ㅡ': [[(.04,.50),(.96,.50)]],
    'ㅣ': [[(.50,.04),(.50,.96)]],
    '0': [[(.18,.90),(.82,.90),(.82,.10),(.18,.10),(.18,.90)]],
    '1': [[(.35,.75),(.55,.92),(.55,.10)]],
    '2': [[(.15,.88),(.84,.88),(.84,.53),(.15,.10),(.86,.10)]],
    '3': [[(.15,.90),(.84,.90),(.84,.10),(.15,.10)],[(.36,.52),(.84,.52)]],
    '4': [[(.17,.91),(.17,.47),(.90,.47)],[(.72,.91),(.72,.09)]],
    '5': [[(.86,.90),(.16,.90),(.16,.53),(.84,.53),(.84,.10),(.15,.10)]],
    '6': [[(.84,.90),(.16,.90),(.16,.10),(.84,.10),(.84,.53),(.16,.53)]],
    '7': [[(.14,.90),(.85,.90),(.42,.10)]],
    '8': [[(.16,.90),(.84,.90),(.84,.10),(.16,.10),(.16,.90)],[(.16,.52),(.84,.52)]],
    '9': [[(.84,.10),(.84,.90),(.16,.90),(.16,.53),(.84,.53)]],
}
_INITIAL = 'ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ'
_MEDIAL = 'ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ'
_FINAL = ['', 'ㄱ','ㄲ','ㄳ','ㄴ','ㄵ','ㄶ','ㄷ','ㄹ','ㄺ','ㄻ','ㄼ','ㄽ','ㄾ','ㄿ','ㅀ','ㅁ','ㅂ','ㅄ','ㅅ','ㅆ','ㅇ','ㅈ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ']
_SPLIT = {'ㄲ':'ㄱㄱ','ㄸ':'ㄷㄷ','ㅃ':'ㅂㅂ','ㅆ':'ㅅㅅ','ㅉ':'ㅈㅈ',
          'ㄳ':'ㄱㅅ','ㄵ':'ㄴㅈ','ㄶ':'ㄴㅎ','ㄺ':'ㄹㄱ','ㄻ':'ㄹㅁ','ㄼ':'ㄹㅂ',
          'ㄽ':'ㄹㅅ','ㄾ':'ㄹㅌ','ㄿ':'ㄹㅍ','ㅀ':'ㄹㅎ','ㅄ':'ㅂㅅ'}


def _glyph_segments(letter, frame):
    x,z,w,h = frame
    if letter in _SPLIT:
        pair = _SPLIT[letter]
        return _glyph_segments(pair[0],(x,z,w*.45,h)) + _glyph_segments(pair[1],(x+w*.55,z,w*.45,h))
    # Compound vertical vowels keep their second vertical stroke distinct.
    if letter in ('ㅐ','ㅒ','ㅔ','ㅖ'):
        base = {'ㅐ':'ㅏ','ㅒ':'ㅑ','ㅔ':'ㅓ','ㅖ':'ㅕ'}[letter]
        return _glyph_segments(base,(x,z,w*.64,h)) + _glyph_segments('ㅣ',(x+w*.72,z,w*.28,h))
    segments=[]
    for path in _J.get(letter,[]):
        for a,b in zip(path,path[1:]):
            segments.append(((x+a[0]*w,z+a[1]*h),(x+b[0]*w,z+b[1]*h)))
    return segments


def _syllable_segments(ch):
    v=ord(ch)-0xAC00
    if not 0 <= v < 11172:
        return _glyph_segments(ch,(.08,.08,.84,.84))
    initial=_INITIAL[v//588]
    vowel=_MEDIAL[(v%588)//28]
    final=_FINAL[v%28]
    # Reserve a consistent, separated bottom region for the final consonant.
    lo=.36 if final else .04
    hi=.98-lo
    mixed={'ㅘ':('ㅗ','ㅏ'),'ㅙ':('ㅗ','ㅐ'),'ㅚ':('ㅗ','ㅣ'),
           'ㅝ':('ㅜ','ㅓ'),'ㅞ':('ㅜ','ㅔ'),'ㅟ':('ㅜ','ㅣ'),'ㅢ':('ㅡ','ㅣ')}
    if vowel in 'ㅗㅛㅜㅠㅡ':
        segments=_glyph_segments(initial,(.15,lo+hi*.46,.70,hi*.52))
        segments+=_glyph_segments(vowel,(.06,lo,.88,hi*.37))
    elif vowel in mixed:
        horizontal,vertical=mixed[vowel]
        segments=_glyph_segments(initial,(.06,lo+hi*.42,.49,hi*.57))
        segments+=_glyph_segments(horizontal,(.04,lo,.59,hi*.34))
        segments+=_glyph_segments(vertical,(.65,lo,.31,hi*.98))
    else:
        segments=_glyph_segments(initial,(.045,lo+.02,.46,hi*.94))
        segments+=_glyph_segments(vowel,(.57,lo,.38,hi*.98))
    if final:
        segments+=_glyph_segments(final,(.12,.035,.76,.245))
    return segments


def lettering(name,label,x,y,z,width,height,mat=None):
    """One mesh containing extruded, font-independent Korean lettering.

    x/z is the label center, y is the front-facing sign surface.
    """
    mat=mat or M['white']
    advance=min(height*1.10,width/max(len(label),1))
    glyph_size=min(height,advance*.84)
    stroke=glyph_size*.064
    verts=[]
    faces=[]
    start=x-advance*len(label)/2+(advance-glyph_size)/2
    for i,ch in enumerate(label):
        for a,b in _syllable_segments(ch):
            ax=start+i*advance+a[0]*glyph_size
            az=z-glyph_size/2+a[1]*glyph_size
            bx=start+i*advance+b[0]*glyph_size
            bz=z-glyph_size/2+b[1]*glyph_size
            dx,dz=bx-ax,bz-az
            length=math.hypot(dx,dz)
            if length<1e-6: continue
            nx,nz=-dz/length*stroke/2,dx/length*stroke/2
            ex,ez=dx/length*stroke/2,dz/length*stroke/2
            corners=[(ax-ex+nx,az-ez+nz),(bx+ex+nx,bz+ez+nz),
                     (bx+ex-nx,bz+ez-nz),(ax-ex-nx,az-ez-nz)]
            j=len(verts)
            for yy in (y-.026,y+.004):
                verts.extend((xx,yy,zz) for xx,zz in corners)
            faces.extend(tuple(j+k for k in f) for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
    return mesh(name,verts,faces,mat) if verts else None


def sign(name,label,x,y,z,width,height,board='dark',border=True):
    """Upright sign facing -Y; x/y/z denotes the center of the board."""
    if border:
        box(name+'_frame',(x,y+.022,z),(width+.10,.14,height+.10),M['trim'])
    obj=box(name+'_board',(x,y-.025,z),(width,.13,height),M[board])
    lettering(name+'_Hangul',label,x,y-.105,z,width*.91,height*.66)
    return obj


def _front_window(name,x,y,z,w,h):
    box(name+'_surround',(x,y,z),(w+.15,.13,h+.15),M['trim'])
    box(name+'_lit_glass',(x,y-.08,z),(w,.05,h),M['window'])
    box(name+'_mullion',(x,y-.12,z),(.065,.04,h),M['cream'])
    box(name+'_sill',(x,y-.13,z-h/2), (w+.24,.30,.12),M['cream'])


def _planter(name,x,y,z,s=.55):
    cyl(name+'_pot',(x,y,z+.25*s),.34*s,.50*s,M['orange'],vertices=8)
    ico(name+'_bush',(x,y,z+.70*s),(.46*s,.42*s,.50*s),M['leaf2'])
    for i in range(3):
        a=i*2.1
        ico(name+'_flower_'+str(i),(x+math.cos(a)*.30*s,y+math.sin(a)*.26*s,z+.95*s),(.11*s,.11*s,.13*s),M['yellow'],subdivisions=1)


def shop(name,x,y,z,w=7,d=5,h=4.4,label='편의점',accent='red',roof='flat'):
    """Shop with individually modeled frontage, striped awning and rooftop equipment."""
    front=y-d/2
    box(name+'_foundation',(x,y,z+.12),(w+.20,d+.20,.24),M['stone'])
    box(name+'_stucco',(x,y,z+h/2),(w,d,h),M['cream'])
    for xx in (x-w/2+.17,x+w/2-.17):
        box(name+'_corner',(xx,front-.075,z+h/2),(.22,.17,h),M['trim'])
    glazing_h=h*.52
    for i,xx in enumerate((x-w*.31,x,x+w*.31)):
        _front_window(name+'_storefront_'+str(i),xx,front-.10,z+.30+glazing_h/2,w*.27,glazing_h)
    # Door handles and display shelf silhouettes stay visible through the warm panes.
    box(name+'_door_handle',(x+w*.07,front-.265,z+1.25),(.045,.08,.35),M['metal'])
    for offset in (-.31,.31):
        for j in range(3):
            box(name+'_display',(x+w*offset+(j-1)*w*.066,front-.19,z+.62),(.25,.10,.31),M['wood' if j%2 else accent])
    sign(name+'_sign',label,x,front-.21,z+h*.82,w*.92,h*.245,accent if accent in ('red','blue','wood','orange') else 'dark')
    aw_z=z+h*.65
    count=max(8,int(w*2))
    for i in range(count):
        xx=x-w*.49+(i+.5)*w*.98/count
        stripe=box(name+'_awning_'+str(i),(xx,front-.63,aw_z-.10),(w*.98/count,.97,.085),M[accent if i%2==0 else 'cream'])
        stripe.rotation_euler.x=.22
        box(name+'_valance_'+str(i),(xx,front-1.10,aw_z-.25),(w*.98/count,.065,.23),M[accent if i%2==0 else 'cream'])
    box(name+'_roof_slab',(x,y,z+h+.08),(w+.28,d+.28,.20),M['stone'])
    if roof=='flat':
        for yy in (y-d/2,y+d/2):
            box(name+'_roof_parapet',(x,yy,z+h+.30),(w+.16,.17,.38),M['trim'])
        for xx in (x-w/2,x+w/2):
            box(name+'_roof_parapet',(xx,y,z+h+.30),(.17,d,.38),M['trim'])
        box(name+'_HVAC',(x+w*.29,y+d*.18,z+h+.48),(.82,.72,.63),M['trim'])
        cyl(name+'_fan',(x+w*.29,y+d*.18,z+h+.81),.23,.026,M['metal'],vertices=8)
        box(name+'_vent',(x-w*.28,y+d*.27,z+h+.42),(.38,.42,.46),M['stone'])
    else:
        _hip_roof(name,x,y,z+h,w,d,roof)
    _planter(name+'_left_planter',x-w*.47,front-.72,z,.95)
    _planter(name+'_right_planter',x+w*.47,front-.72,z,.90)
    return None


def _hip_roof(name,x,y,z,w,d,color):
    ww,dd=w/2+.34,d/2+.34
    rise=min(w,d)*.28
    verts=[(x-ww,y-dd,z),(x+ww,y-dd,z),(x+ww,y+dd,z),(x-ww,y+dd,z),
           (x-w*.24,y,z+rise),(x+w*.24,y,z+rise)]
    mesh(name+'_hip_roof',verts,[(0,1,5,4),(1,2,5),(2,3,4,5),(3,0,4),(3,2,1,0)],M[color])
    beam(name+'_ridge',(x-w*.25,y,z+rise+.025),(x+w*.25,y,z+rise+.025),.13,M[color])
    # Geometric tile seams are deliberately sparse for a clear low-poly silhouette.
    for i in range(1,8):
        xx=x-ww+(2*ww)*i/8
        topx=max(x-w*.24,min(x+w*.24,xx))
        beam(name+'_roof_seam',(xx,y-dd,z+.025),(topx,y,z+rise+.025),.025,M['trim'])
        beam(name+'_roof_seam',(xx,y+dd,z+.025),(topx,y,z+rise+.025),.025,M['trim'])


def house(name,x,y,z,w=5.8,d=4.5,h=4.1,roof='blue'):
    box(name+'_foundation',(x,y,z+.12),(w+.16,d+.16,.24),M['stone'])
    box(name+'_walls',(x,y,z+h/2),(w,d,h),M['cream'])
    front=y-d/2
    _front_window(name+'_window_L',x-w*.28,front-.07,z+h*.56,w*.23,h*.38)
    _front_window(name+'_window_R',x+w*.28,front-.07,z+h*.56,w*.23,h*.38)
    box(name+'_door_frame',(x,front-.09,z+h*.29),(w*.20,.15,h*.58),M['trim'])
    box(name+'_door',(x,front-.19,z+h*.28),(w*.16,.06,h*.53),M['wood'])
    box(name+'_door_lite',(x,front-.23,z+h*.37),(w*.11,.035,h*.19),M['window'])
    for side in (-1,1):
        box(name+'_side_window_frame',(x+side*(w/2+.035),y,z+h*.58),(.10,d*.30,h*.37),M['trim'])
        box(name+'_side_window',(x+side*(w/2+.095),y,z+h*.58),(.035,d*.26,h*.33),M['window'])
    _hip_roof(name,x,y,z+h,w,d,roof)
    box(name+'_chimney',(x+w*.25,y+d*.25,z+h+.68),(.40,.48,1.0),M['trim'])
    box(name+'_chimney_cap',(x+w*.25,y+d*.25,z+h+1.21),(.54,.62,.12),M['stone'])
    _planter(name+'_porch_plant',x+w*.43,front-.38,z,.70)


def tree(name,x,y,z,scale=1):
    rng=random.Random(name)
    s=scale
    cyl(name+'_trunk',(x,y,z+1.45*s),.18*s,2.9*s,M['wood'],vertices=7)
    for i in range(3):
        angle=i*2.094+.35
        tip=(x+math.cos(angle)*.76*s,y+math.sin(angle)*.76*s,z+2.6*s)
        beam(name+'_branch',(x,y,z+1.55*s),tip,.17*s,M['wood'])
    for i,(dx,dy,dz,size) in enumerate([(0,0,3.5,1.18),(-.76,-.08,2.93,.95),(.68,.27,3.03,1.03),(.03,-.74,2.85,.91),(-.2,.65,3.30,.91)]):
        ico(name+'_crown_'+str(i),(x+dx*s,y+dy*s,z+dz*s),
            (size*s,size*s*(.84+rng.random()*.18),size*s*(.92+rng.random()*.16)),M[['leaf1','leaf2','leaf3'][i%3]],subdivisions=1)


def lamp(name,x,y,z,scale=1):
    s=scale
    cyl(name+'_foot',(x,y,z+.09*s),.29*s,.18*s,M['metal'],vertices=8)
    cyl(name+'_base',(x,y,z+.35*s),.15*s,.55*s,M['metal'],vertices=8)
    cyl(name+'_post',(x,y,z+1.9*s),.069*s,3.3*s,M['metal'],vertices=8)
    box(name+'_lantern_floor',(x,y,z+3.59*s),(.48*s,.48*s,.09*s),M['metal'])
    box(name+'_lantern_glow',(x,y,z+3.92*s),(.35*s,.35*s,.60*s),M['window'])
    for dx in (-.2,.2):
        for dy in (-.2,.2):
            beam(name+'_lantern_frame',(x+dx*s,y+dy*s,z+3.58*s),(x+dx*s,y+dy*s,z+4.23*s),.045*s,M['metal'])
    mesh(name+'_lantern_cap',[(x+dx*s,y+dy*s,z+4.23*s) for dx,dy in [(-.31,-.31),(.31,-.31),(.31,.31),(-.31,.31)]]+[(x,y,z+4.51*s)],[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(3,2,1,0)],M['metal'])


def bench(name,x,y,z,angle=0):
    parts=[]
    for i in range(4):
        parts.append(box(name+'_seat_'+str(i),(0,(i-1.5)*.16,.65),(2.05,.125,.10),M['wood']))
    for i in range(3):
        parts.append(box(name+'_back_'+str(i),(0,.30,.93+i*.18),(2.05,.095,.12),M['wood']))
    for xx in (-.78,.78):
        parts.append(box(name+'_leg',(xx,0,.32),(.095,.46,.64),M['metal']))
        parts.append(box(name+'_back_support',(xx,.34,.90),(.085,.085,1.0),M['metal']))
    _place_parts(parts,x,y,z,angle)


def _place_parts(parts,x,y,z,angle):
    c,s=math.cos(angle),math.sin(angle)
    for obj in parts:
        a,b,zz=obj.location
        obj.location=(x+a*c-b*s,y+a*s+b*c,z+zz)
        obj.rotation_euler.z+=angle


def car(name,x,y,z,color='yellow',bus=False,angle=0):
    """Vehicle facing local -Y; angle is radians counterclockwise around world Z."""
    parts=[]
    def b(suffix,loc,size,mat):
        obj=box(name+'_'+suffix,loc,size,M[mat]); parts.append(obj); return obj
    def wheel(xx,yy,r):
        obj=cyl(name+'_tire',(xx,yy,r+.055),r,.20,M['dark'],vertices=12)
        obj.rotation_euler.y=math.pi/2; parts.append(obj)
        obj=cyl(name+'_hub',(xx+(.11 if xx>0 else -.11),yy,r+.055),r*.51,.026,M['trim'],vertices=8)
        obj.rotation_euler.y=math.pi/2; parts.append(obj)
    if bus:
        b('chassis',(0,0,.51),(2.44,6.9,.37),'metal')
        b('body',(0,0,1.54),(2.48,6.9,2.13),color)
        b('roof',(0,0,2.66),(2.53,6.95,.16),color)
        b('windshield',(0,-3.47,1.93),(2.21,.04,1.04),'glass')
        b('front_number_panel',(0,-3.50,2.48),(1.66,.045,.28),'dark')
        lettering_obj=lettering(name+'_route_701','701',0,-3.545,2.49,1.22,.22,M['yellow'])
        if lettering_obj: parts.append(lettering_obj)
        for side in (-1,1):
            for i in range(6):
                b('passenger_window',(side*1.255,-2.55+i*.93,1.99),(.045,.79,.88),'glass')
                b('window_post',(side*1.29,-2.99+i*.93,1.99),(.07,.065,1.0),'metal')
            b('side_stripe',(side*1.264,0,.98),(.035,6.66,.12),'cream')
            for yy in (-2.08,2.10): wheel(side*1.25,yy,.46)
        b('vent_roof',(0,.48,2.89),(1.15,1.18,.38),color)
        for xx in (-.86,.86):
            b('headlight',(xx,-3.49,.91),(.35,.055,.25),'window')
        b('bumper',(0,-3.53,.52),(2.31,.15,.14),'trim')
        b('door',(1.30,-1.9,1.36),(.035,.79,1.87),'glass')
        b('door_rail',(1.34,-1.9,1.36),(.03,.035,1.87),'metal')
    else:
        b('chassis',(0,0,.44),(1.63,3.33,.29),'metal')
        b('body',(0,0,.77),(1.76,3.45,.65),color)
        b('cabin',(0,.22,1.30),(1.52,1.82,.72),color)
        b('roof',(0,.26,1.69),(1.57,1.86,.13),color)
        b('windshield',(0,-.703,1.36),(1.32,.035,.48),'glass')
        b('rear_window',(0,1.15,1.36),(1.31,.035,.46),'glass')
        for side in (-1,1):
            for yy in (-.20,.64):
                b('side_window',(side*.771,yy,1.38),(.035,.70,.43),'glass')
            b('door_handle',(side*.90,.63,.99),(.045,.19,.045),'trim')
            b('mirror',(side*.94,-.55,1.26),(.21,.19,.14),color)
            for yy in (-1.04,1.02): wheel(side*.88,yy,.33)
        for xx in (-.59,.59):
            b('headlight',(xx,-1.752,.82),(.32,.045,.23),'window')
            b('taillight',(xx,1.752,.81),(.31,.045,.19),'red')
        b('front_bumper',(0,-1.78,.52),(1.56,.16,.14),'trim')
        b('grille',(0,-1.77,.77),(.55,.055,.20),'dark')
    _place_parts(parts,x,y,z,angle)
    return parts


build()
result={'objects':len(bpy.data.objects),'meshes':len(bpy.data.meshes),'materials':len(bpy.data.materials),'map':'Daldongne low-poly 3D neighborhood','units':'metres','delivery_camera':bpy.context.scene.camera.name}

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

"""Final wall openings and render settings. Run after refine_map.py."""
wall_cuts=0
for tier,name,x0,x1,y0,y1,z0,z1 in cuts:
    # A cap often spans multiple bricks. Clip it rather than deleting the entire wall.
    for o in list(bpy.data.objects):
        if not o.name.startswith(('Wall_','WallCap_')):continue
        points=[o.matrix_world@Vector(v) for v in o.bound_box]
        lo=[min(v[j] for v in points) for j in range(3)]
        hi=[max(v[j] for v in points) for j in range(3)]
        if hi[0]<=x0 or lo[0]>=x1 or hi[1]<=y0 or lo[1]>=y1 or hi[2]<=z0 or lo[2]>=z1:continue
        cut_box(o.name,'WallOpening_'+name,x0,x1,y0,y1,z0,z1)
        wall_cuts+=1
        if len(o.data.polygons)==0:bpy.data.objects.remove(o,do_unlink=True)

s=bpy.context.scene
s.eevee.taa_render_samples=24
s.render.resolution_x=1100;s.render.resolution_y=1100
s.view_settings.view_transform='Khronos PBR Neutral'
# Organize the editable file into meaningful collections, keeping every prop editable.
categories={}
def category(n):
    if n.startswith(('Terrain','Wall','Rock','Water','Foam')):return '01 Terrain and retaining walls'
    if n.startswith(('Walk','Support_Stair','Rail','Bridge','Paving','Curb','Road','Causeway')):return '02 Roads paths and stairs'
    if n.startswith(('Hamburger','Convenience','Snack','Cafe','Bakery','Laundry','Gosiwon','Home','Building','CityHall','Station','Shelter','Vending','BusStop')):return '03 Buildings and landmarks'
    if n.startswith(('Tree','Garden','Planter','Shrub','Flower','Soil')):return '04 Trees and flower gardens'
    if n.startswith(('TownBus','YellowCar','RedCar','CreamVan','NPC')):return '05 Vehicles and scale figures'
    if n.startswith(('Sun','Sky','Warm','Camera')):return '07 Cameras and lighting'
    return '06 Street furniture'
for o in list(s.objects):
    key=category(o.name)
    if key not in categories:
        col=bpy.data.collections.get(key) or bpy.data.collections.new(key)
        if col.name not in s.collection.children:s.collection.children.link(col)
        categories[key]=col
    col=categories[key]
    if o.name not in col.objects:col.objects.link(o)
    for existing in list(o.users_collection):
        if existing!=col:existing.objects.unlink(o)
result={'wall_openings':wall_cuts,'objects':len(s.objects),'collections':list(categories),'triangles_instanced':sum(len(p.vertices)-2 for o in s.objects if o.type=='MESH' for p in o.data.polygons)}

import bpy, math, random
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


"""Reusable low-poly Daldongne props. Blender Z up; fronts face -Y.

Expects box/cyl/ico/beam/mesh helpers and M materials from the scene builder.
No external fonts: Korean sign lettering is actual extruded stroke geometry.
"""
import math
import random


# Coordinates inside a unit glyph, from left/bottom to right/top.
_J = {
    'ㄱ': [[(.08,.88),(.88,.88),(.88,.08)]],
    'ㄴ': [[(.10,.90),(.10,.12),(.90,.12)]],
    'ㄷ': [[(.90,.88),(.10,.88),(.10,.12),(.90,.12)]],
    'ㄹ': [[(.10,.90),(.90,.90),(.90,.53),(.10,.53),(.10,.12),(.90,.12)]],
    'ㅁ': [[(.12,.88),(.88,.88),(.88,.12),(.12,.12),(.12,.88)]],
    'ㅂ': [[(.12,.94),(.12,.12),(.88,.12),(.88,.94)],[(.12,.58),(.88,.58)]],
    'ㅅ': [[(.49,.91),(.10,.11)],[(.49,.80),(.91,.11)]],
    'ㅇ': [[(.50,.91),(.80,.79),(.92,.50),(.80,.21),(.50,.09),(.20,.21),(.08,.50),(.20,.79),(.50,.91)]],
    'ㅈ': [[(.08,.88),(.92,.88)],[(.52,.88),(.10,.10)],[(.49,.65),(.91,.10)]],
    'ㅊ': [[(.50,1.0),(.50,.87)],[(.08,.75),(.92,.75)],[(.50,.75),(.10,.07)],[(.48,.56),(.91,.07)]],
    'ㅋ': [[(.08,.90),(.88,.90),(.88,.08)],[(.10,.51),(.88,.51)]],
    'ㅌ': [[(.90,.90),(.10,.90),(.10,.12),(.90,.12)],[(.10,.51),(.86,.51)]],
    'ㅍ': [[(.06,.89),(.94,.89)],[(.06,.12),(.94,.12)],[(.28,.89),(.28,.12)],[(.72,.89),(.72,.12)]],
    'ㅎ': [[(.50,1.0),(.50,.91)],[(.10,.79),(.90,.79)],[(.50,.65),(.77,.55),(.87,.34),(.77,.12),(.50,.03),(.23,.12),(.13,.34),(.23,.55),(.50,.65)]],
    'ㅏ': [[(.28,.04),(.28,.96)],[(.28,.53),(.91,.53)]],
    'ㅑ': [[(.28,.04),(.28,.96)],[(.28,.68),(.91,.68)],[(.28,.35),(.91,.35)]],
    'ㅓ': [[(.72,.04),(.72,.96)],[(.09,.53),(.72,.53)]],
    'ㅕ': [[(.72,.04),(.72,.96)],[(.09,.68),(.72,.68)],[(.09,.35),(.72,.35)]],
    'ㅗ': [[(.04,.26),(.96,.26)],[(.50,.26),(.50,.96)]],
    'ㅛ': [[(.04,.26),(.96,.26)],[(.31,.26),(.31,.96)],[(.69,.26),(.69,.96)]],
    'ㅜ': [[(.04,.74),(.96,.74)],[(.50,.04),(.50,.74)]],
    'ㅠ': [[(.04,.74),(.96,.74)],[(.31,.04),(.31,.74)],[(.69,.04),(.69,.74)]],
    'ㅡ': [[(.04,.50),(.96,.50)]],
    'ㅣ': [[(.50,.04),(.50,.96)]],
    '0': [[(.18,.90),(.82,.90),(.82,.10),(.18,.10),(.18,.90)]],
    '1': [[(.35,.75),(.55,.92),(.55,.10)]],
    '2': [[(.15,.88),(.84,.88),(.84,.53),(.15,.10),(.86,.10)]],
    '3': [[(.15,.90),(.84,.90),(.84,.10),(.15,.10)],[(.36,.52),(.84,.52)]],
    '4': [[(.17,.91),(.17,.47),(.90,.47)],[(.72,.91),(.72,.09)]],
    '5': [[(.86,.90),(.16,.90),(.16,.53),(.84,.53),(.84,.10),(.15,.10)]],
    '6': [[(.84,.90),(.16,.90),(.16,.10),(.84,.10),(.84,.53),(.16,.53)]],
    '7': [[(.14,.90),(.85,.90),(.42,.10)]],
    '8': [[(.16,.90),(.84,.90),(.84,.10),(.16,.10),(.16,.90)],[(.16,.52),(.84,.52)]],
    '9': [[(.84,.10),(.84,.90),(.16,.90),(.16,.53),(.84,.53)]],
}
_INITIAL = 'ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ'
_MEDIAL = 'ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ'
_FINAL = ['', 'ㄱ','ㄲ','ㄳ','ㄴ','ㄵ','ㄶ','ㄷ','ㄹ','ㄺ','ㄻ','ㄼ','ㄽ','ㄾ','ㄿ','ㅀ','ㅁ','ㅂ','ㅄ','ㅅ','ㅆ','ㅇ','ㅈ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ']
_SPLIT = {'ㄲ':'ㄱㄱ','ㄸ':'ㄷㄷ','ㅃ':'ㅂㅂ','ㅆ':'ㅅㅅ','ㅉ':'ㅈㅈ',
          'ㄳ':'ㄱㅅ','ㄵ':'ㄴㅈ','ㄶ':'ㄴㅎ','ㄺ':'ㄹㄱ','ㄻ':'ㄹㅁ','ㄼ':'ㄹㅂ',
          'ㄽ':'ㄹㅅ','ㄾ':'ㄹㅌ','ㄿ':'ㄹㅍ','ㅀ':'ㄹㅎ','ㅄ':'ㅂㅅ'}


def _glyph_segments(letter, frame):
    x,z,w,h = frame
    if letter in _SPLIT:
        pair = _SPLIT[letter]
        return _glyph_segments(pair[0],(x,z,w*.45,h)) + _glyph_segments(pair[1],(x+w*.55,z,w*.45,h))
    # Compound vertical vowels keep their second vertical stroke distinct.
    if letter in ('ㅐ','ㅒ','ㅔ','ㅖ'):
        base = {'ㅐ':'ㅏ','ㅒ':'ㅑ','ㅔ':'ㅓ','ㅖ':'ㅕ'}[letter]
        return _glyph_segments(base,(x,z,w*.64,h)) + _glyph_segments('ㅣ',(x+w*.72,z,w*.28,h))
    segments=[]
    for path in _J.get(letter,[]):
        for a,b in zip(path,path[1:]):
            segments.append(((x+a[0]*w,z+a[1]*h),(x+b[0]*w,z+b[1]*h)))
    return segments


def _syllable_segments(ch):
    v=ord(ch)-0xAC00
    if not 0 <= v < 11172:
        return _glyph_segments(ch,(.08,.08,.84,.84))
    initial=_INITIAL[v//588]
    vowel=_MEDIAL[(v%588)//28]
    final=_FINAL[v%28]
    # Reserve a consistent, separated bottom region for the final consonant.
    lo=.36 if final else .04
    hi=.98-lo
    mixed={'ㅘ':('ㅗ','ㅏ'),'ㅙ':('ㅗ','ㅐ'),'ㅚ':('ㅗ','ㅣ'),
           'ㅝ':('ㅜ','ㅓ'),'ㅞ':('ㅜ','ㅔ'),'ㅟ':('ㅜ','ㅣ'),'ㅢ':('ㅡ','ㅣ')}
    if vowel in 'ㅗㅛㅜㅠㅡ':
        segments=_glyph_segments(initial,(.15,lo+hi*.46,.70,hi*.52))
        segments+=_glyph_segments(vowel,(.06,lo,.88,hi*.37))
    elif vowel in mixed:
        horizontal,vertical=mixed[vowel]
        segments=_glyph_segments(initial,(.06,lo+hi*.42,.49,hi*.57))
        segments+=_glyph_segments(horizontal,(.04,lo,.59,hi*.34))
        segments+=_glyph_segments(vertical,(.65,lo,.31,hi*.98))
    else:
        segments=_glyph_segments(initial,(.045,lo+.02,.46,hi*.94))
        segments+=_glyph_segments(vowel,(.57,lo,.38,hi*.98))
    if final:
        segments+=_glyph_segments(final,(.12,.035,.76,.245))
    return segments


def lettering(name,label,x,y,z,width,height,mat=None):
    """One mesh containing extruded, font-independent Korean lettering.

    x/z is the label center, y is the front-facing sign surface.
    """
    mat=mat or M['white']
    advance=min(height*1.10,width/max(len(label),1))
    glyph_size=min(height,advance*.84)
    stroke=glyph_size*.064
    verts=[]
    faces=[]
    start=x-advance*len(label)/2+(advance-glyph_size)/2
    for i,ch in enumerate(label):
        for a,b in _syllable_segments(ch):
            ax=start+i*advance+a[0]*glyph_size
            az=z-glyph_size/2+a[1]*glyph_size
            bx=start+i*advance+b[0]*glyph_size
            bz=z-glyph_size/2+b[1]*glyph_size
            dx,dz=bx-ax,bz-az
            length=math.hypot(dx,dz)
            if length<1e-6: continue
            nx,nz=-dz/length*stroke/2,dx/length*stroke/2
            ex,ez=dx/length*stroke/2,dz/length*stroke/2
            corners=[(ax-ex+nx,az-ez+nz),(bx+ex+nx,bz+ez+nz),
                     (bx+ex-nx,bz+ez-nz),(ax-ex-nx,az-ez-nz)]
            j=len(verts)
            for yy in (y-.026,y+.004):
                verts.extend((xx,yy,zz) for xx,zz in corners)
            faces.extend(tuple(j+k for k in f) for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
    return mesh(name,verts,faces,mat) if verts else None


def sign(name,label,x,y,z,width,height,board='dark',border=True):
    """Upright sign facing -Y; x/y/z denotes the center of the board."""
    if border:
        box(name+'_frame',(x,y+.022,z),(width+.10,.14,height+.10),M['trim'])
    obj=box(name+'_board',(x,y-.025,z),(width,.13,height),M[board])
    lettering(name+'_Hangul',label,x,y-.105,z,width*.91,height*.66)
    return obj


def _front_window(name,x,y,z,w,h):
    box(name+'_surround',(x,y,z),(w+.15,.13,h+.15),M['trim'])
    box(name+'_lit_glass',(x,y-.08,z),(w,.05,h),M['window'])
    box(name+'_mullion',(x,y-.12,z),(.065,.04,h),M['cream'])
    box(name+'_sill',(x,y-.13,z-h/2), (w+.24,.30,.12),M['cream'])


def _planter(name,x,y,z,s=.55):
    cyl(name+'_pot',(x,y,z+.25*s),.34*s,.50*s,M['orange'],vertices=8)
    ico(name+'_bush',(x,y,z+.70*s),(.46*s,.42*s,.50*s),M['leaf2'])
    for i in range(3):
        a=i*2.1
        ico(name+'_flower_'+str(i),(x+math.cos(a)*.30*s,y+math.sin(a)*.26*s,z+.95*s),(.11*s,.11*s,.13*s),M['yellow'],subdivisions=1)


def shop(name,x,y,z,w=7,d=5,h=4.4,label='편의점',accent='red',roof='flat'):
    """Shop with individually modeled frontage, striped awning and rooftop equipment."""
    front=y-d/2
    box(name+'_foundation',(x,y,z+.12),(w+.20,d+.20,.24),M['stone'])
    box(name+'_stucco',(x,y,z+h/2),(w,d,h),M['cream'])
    for xx in (x-w/2+.17,x+w/2-.17):
        box(name+'_corner',(xx,front-.075,z+h/2),(.22,.17,h),M['trim'])
    glazing_h=h*.52
    for i,xx in enumerate((x-w*.31,x,x+w*.31)):
        _front_window(name+'_storefront_'+str(i),xx,front-.10,z+.30+glazing_h/2,w*.27,glazing_h)
    # Door handles and display shelf silhouettes stay visible through the warm panes.
    box(name+'_door_handle',(x+w*.07,front-.265,z+1.25),(.045,.08,.35),M['metal'])
    for offset in (-.31,.31):
        for j in range(3):
            box(name+'_display',(x+w*offset+(j-1)*w*.066,front-.19,z+.62),(.25,.10,.31),M['wood' if j%2 else accent])
    sign(name+'_sign',label,x,front-.21,z+h*.82,w*.92,h*.245,accent if accent in ('red','blue','wood','orange') else 'dark')
    aw_z=z+h*.65
    count=max(8,int(w*2))
    for i in range(count):
        xx=x-w*.49+(i+.5)*w*.98/count
        stripe=box(name+'_awning_'+str(i),(xx,front-.63,aw_z-.10),(w*.98/count,.97,.085),M[accent if i%2==0 else 'cream'])
        stripe.rotation_euler.x=.22
        box(name+'_valance_'+str(i),(xx,front-1.10,aw_z-.25),(w*.98/count,.065,.23),M[accent if i%2==0 else 'cream'])
    box(name+'_roof_slab',(x,y,z+h+.08),(w+.28,d+.28,.20),M['stone'])
    if roof=='flat':
        for yy in (y-d/2,y+d/2):
            box(name+'_roof_parapet',(x,yy,z+h+.30),(w+.16,.17,.38),M['trim'])
        for xx in (x-w/2,x+w/2):
            box(name+'_roof_parapet',(xx,y,z+h+.30),(.17,d,.38),M['trim'])
        box(name+'_HVAC',(x+w*.29,y+d*.18,z+h+.48),(.82,.72,.63),M['trim'])
        cyl(name+'_fan',(x+w*.29,y+d*.18,z+h+.81),.23,.026,M['metal'],vertices=8)
        box(name+'_vent',(x-w*.28,y+d*.27,z+h+.42),(.38,.42,.46),M['stone'])
    else:
        _hip_roof(name,x,y,z+h,w,d,roof)
    _planter(name+'_left_planter',x-w*.47,front-.72,z,.95)
    _planter(name+'_right_planter',x+w*.47,front-.72,z,.90)
    return None


def _hip_roof(name,x,y,z,w,d,color):
    ww,dd=w/2+.34,d/2+.34
    rise=min(w,d)*.28
    verts=[(x-ww,y-dd,z),(x+ww,y-dd,z),(x+ww,y+dd,z),(x-ww,y+dd,z),
           (x-w*.24,y,z+rise),(x+w*.24,y,z+rise)]
    mesh(name+'_hip_roof',verts,[(0,1,5,4),(1,2,5),(2,3,4,5),(3,0,4),(3,2,1,0)],M[color])
    beam(name+'_ridge',(x-w*.25,y,z+rise+.025),(x+w*.25,y,z+rise+.025),.13,M[color])
    # Geometric tile seams are deliberately sparse for a clear low-poly silhouette.
    for i in range(1,8):
        xx=x-ww+(2*ww)*i/8
        topx=max(x-w*.24,min(x+w*.24,xx))
        beam(name+'_roof_seam',(xx,y-dd,z+.025),(topx,y,z+rise+.025),.025,M['trim'])
        beam(name+'_roof_seam',(xx,y+dd,z+.025),(topx,y,z+rise+.025),.025,M['trim'])


def house(name,x,y,z,w=5.8,d=4.5,h=4.1,roof='blue'):
    box(name+'_foundation',(x,y,z+.12),(w+.16,d+.16,.24),M['stone'])
    box(name+'_walls',(x,y,z+h/2),(w,d,h),M['cream'])
    front=y-d/2
    _front_window(name+'_window_L',x-w*.28,front-.07,z+h*.56,w*.23,h*.38)
    _front_window(name+'_window_R',x+w*.28,front-.07,z+h*.56,w*.23,h*.38)
    box(name+'_door_frame',(x,front-.09,z+h*.29),(w*.20,.15,h*.58),M['trim'])
    box(name+'_door',(x,front-.19,z+h*.28),(w*.16,.06,h*.53),M['wood'])
    box(name+'_door_lite',(x,front-.23,z+h*.37),(w*.11,.035,h*.19),M['window'])
    for side in (-1,1):
        box(name+'_side_window_frame',(x+side*(w/2+.035),y,z+h*.58),(.10,d*.30,h*.37),M['trim'])
        box(name+'_side_window',(x+side*(w/2+.095),y,z+h*.58),(.035,d*.26,h*.33),M['window'])
    _hip_roof(name,x,y,z+h,w,d,roof)
    box(name+'_chimney',(x+w*.25,y+d*.25,z+h+.68),(.40,.48,1.0),M['trim'])
    box(name+'_chimney_cap',(x+w*.25,y+d*.25,z+h+1.21),(.54,.62,.12),M['stone'])
    _planter(name+'_porch_plant',x+w*.43,front-.38,z,.70)


def tree(name,x,y,z,scale=1):
    rng=random.Random(name)
    s=scale
    cyl(name+'_trunk',(x,y,z+1.45*s),.18*s,2.9*s,M['wood'],vertices=7)
    for i in range(3):
        angle=i*2.094+.35
        tip=(x+math.cos(angle)*.76*s,y+math.sin(angle)*.76*s,z+2.6*s)
        beam(name+'_branch',(x,y,z+1.55*s),tip,.17*s,M['wood'])
    for i,(dx,dy,dz,size) in enumerate([(0,0,3.5,1.18),(-.76,-.08,2.93,.95),(.68,.27,3.03,1.03),(.03,-.74,2.85,.91),(-.2,.65,3.30,.91)]):
        ico(name+'_crown_'+str(i),(x+dx*s,y+dy*s,z+dz*s),
            (size*s,size*s*(.84+rng.random()*.18),size*s*(.92+rng.random()*.16)),M[['leaf1','leaf2','leaf3'][i%3]],subdivisions=1)


def lamp(name,x,y,z,scale=1):
    s=scale
    cyl(name+'_foot',(x,y,z+.09*s),.29*s,.18*s,M['metal'],vertices=8)
    cyl(name+'_base',(x,y,z+.35*s),.15*s,.55*s,M['metal'],vertices=8)
    cyl(name+'_post',(x,y,z+1.9*s),.069*s,3.3*s,M['metal'],vertices=8)
    box(name+'_lantern_floor',(x,y,z+3.59*s),(.48*s,.48*s,.09*s),M['metal'])
    box(name+'_lantern_glow',(x,y,z+3.92*s),(.35*s,.35*s,.60*s),M['window'])
    for dx in (-.2,.2):
        for dy in (-.2,.2):
            beam(name+'_lantern_frame',(x+dx*s,y+dy*s,z+3.58*s),(x+dx*s,y+dy*s,z+4.23*s),.045*s,M['metal'])
    mesh(name+'_lantern_cap',[(x+dx*s,y+dy*s,z+4.23*s) for dx,dy in [(-.31,-.31),(.31,-.31),(.31,.31),(-.31,.31)]]+[(x,y,z+4.51*s)],[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(3,2,1,0)],M['metal'])


def bench(name,x,y,z,angle=0):
    parts=[]
    for i in range(4):
        parts.append(box(name+'_seat_'+str(i),(0,(i-1.5)*.16,.65),(2.05,.125,.10),M['wood']))
    for i in range(3):
        parts.append(box(name+'_back_'+str(i),(0,.30,.93+i*.18),(2.05,.095,.12),M['wood']))
    for xx in (-.78,.78):
        parts.append(box(name+'_leg',(xx,0,.32),(.095,.46,.64),M['metal']))
        parts.append(box(name+'_back_support',(xx,.34,.90),(.085,.085,1.0),M['metal']))
    _place_parts(parts,x,y,z,angle)


def _place_parts(parts,x,y,z,angle):
    c,s=math.cos(angle),math.sin(angle)
    for obj in parts:
        a,b,zz=obj.location
        obj.location=(x+a*c-b*s,y+a*s+b*c,z+zz)
        obj.rotation_euler.z+=angle


def car(name,x,y,z,color='yellow',bus=False,angle=0):
    """Vehicle facing local -Y; angle is radians counterclockwise around world Z."""
    parts=[]
    def b(suffix,loc,size,mat):
        obj=box(name+'_'+suffix,loc,size,M[mat]); parts.append(obj); return obj
    def wheel(xx,yy,r):
        obj=cyl(name+'_tire',(xx,yy,r+.055),r,.20,M['dark'],vertices=12)
        obj.rotation_euler.y=math.pi/2; parts.append(obj)
        obj=cyl(name+'_hub',(xx+(.11 if xx>0 else -.11),yy,r+.055),r*.51,.026,M['trim'],vertices=8)
        obj.rotation_euler.y=math.pi/2; parts.append(obj)
    if bus:
        b('chassis',(0,0,.51),(2.44,6.9,.37),'metal')
        b('body',(0,0,1.54),(2.48,6.9,2.13),color)
        b('roof',(0,0,2.66),(2.53,6.95,.16),color)
        b('windshield',(0,-3.47,1.93),(2.21,.04,1.04),'glass')
        b('front_number_panel',(0,-3.50,2.48),(1.66,.045,.28),'dark')
        lettering_obj=lettering(name+'_route_701','701',0,-3.545,2.49,1.22,.22,M['yellow'])
        if lettering_obj: parts.append(lettering_obj)
        for side in (-1,1):
            for i in range(6):
                b('passenger_window',(side*1.255,-2.55+i*.93,1.99),(.045,.79,.88),'glass')
                b('window_post',(side*1.29,-2.99+i*.93,1.99),(.07,.065,1.0),'metal')
            b('side_stripe',(side*1.264,0,.98),(.035,6.66,.12),'cream')
            for yy in (-2.08,2.10): wheel(side*1.25,yy,.46)
        b('vent_roof',(0,.48,2.89),(1.15,1.18,.38),color)
        for xx in (-.86,.86):
            b('headlight',(xx,-3.49,.91),(.35,.055,.25),'window')
        b('bumper',(0,-3.53,.52),(2.31,.15,.14),'trim')
        b('door',(1.30,-1.9,1.36),(.035,.79,1.87),'glass')
        b('door_rail',(1.34,-1.9,1.36),(.03,.035,1.87),'metal')
    else:
        b('chassis',(0,0,.44),(1.63,3.33,.29),'metal')
        b('body',(0,0,.77),(1.76,3.45,.65),color)
        b('cabin',(0,.22,1.30),(1.52,1.82,.72),color)
        b('roof',(0,.26,1.69),(1.57,1.86,.13),color)
        b('windshield',(0,-.703,1.36),(1.32,.035,.48),'glass')
        b('rear_window',(0,1.15,1.36),(1.31,.035,.46),'glass')
        for side in (-1,1):
            for yy in (-.20,.64):
                b('side_window',(side*.771,yy,1.38),(.035,.70,.43),'glass')
            b('door_handle',(side*.90,.63,.99),(.045,.19,.045),'trim')
            b('mirror',(side*.94,-.55,1.26),(.21,.19,.14),color)
            for yy in (-1.04,1.02): wheel(side*.88,yy,.33)
        for xx in (-.59,.59):
            b('headlight',(xx,-1.752,.82),(.32,.045,.23),'window')
            b('taillight',(xx,1.752,.81),(.31,.045,.19),'red')
        b('front_bumper',(0,-1.78,.52),(1.56,.16,.14),'trim')
        b('grille',(0,-1.77,.77),(.55,.055,.20),'dark')
    _place_parts(parts,x,y,z,angle)
    return parts

"""Portable geometry and restrained materials for an older Seoul hillside village."""
def setup_warm_palette():
    colors={'stone':(.46,.435,.385),'cream':(.72,.655,.52),'trim':(.80,.755,.64),
      'plaster':(.74,.71,.61),'teal':(.13,.32,.31),'slate':(.12,.16,.18),
      'brick':(.43,.18,.115),'brickLight':(.56,.265,.16),'brickDark':(.30,.125,.08),
      'mortar':(.51,.45,.36),'glassClear':(.27,.52,.51),'red':(.52,.22,.135),
      'blue':(.18,.32,.40),'green':(.31,.43,.20),'wood':(.35,.21,.115),
      'pavement':(.61,.56,.45),'paver_light':(.72,.655,.53),'paver_dark':(.47,.455,.395),
      'leaf1':(.115,.235,.075),'leaf2':(.245,.355,.11),'leaf3':(.39,.47,.17),
      'grass':(.235,.295,.105),'metal':(.115,.145,.145),'window':(1,.58,.22),
      'road':(.145,.16,.17),'white':(.89,.865,.77),'orange':(.64,.34,.13),'purple':(.38,.22,.43)}
    for key,col in colors.items():
        m=bpy.data.materials.get(key) or bpy.data.materials.new(key);M[key]=m;m.use_nodes=True
        alpha=.30 if key=='glassClear' else 1
        m.diffuse_color=(*col,alpha);p=m.node_tree.nodes.get('Principled BSDF')
        p.inputs['Base Color'].default_value=(*col,alpha);p.inputs['Alpha'].default_value=alpha
        p.inputs['Roughness'].default_value=.20 if key=='glassClear' else .88
        p.inputs['Metallic'].default_value=.50 if key=='metal' else 0
        if key=='window':p.inputs['Emission Color'].default_value=(*col,1);p.inputs['Emission Strength'].default_value=1.6
        if alpha<1:m.surface_render_method='DITHERED'
    m=bpy.data.materials.new('CollisionInvisible');m.use_nodes=True;m.diffuse_color=(0,0,0,0)
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Alpha'].default_value=0;m.surface_render_method='DITHERED';M['CollisionInvisible']=m

def dress_neighborhood():
    rng=random.Random(817)
    # Brick plinths and surviving painted plaster are deliberately mixed.
    buildings=[('Hamburger',-25,-.4,3,6,4.8,4.4),('Convenience',-16,-.4,3,8,4.8,4.4),
      ('Snack',-6.5,-.4,3,6.6,4.8,4.4),('Laundry',-14.7,12.8,6,7.6,4.2,3.7),
      ('Gosiwon',-11.5,24.1,12,7.8,5.4,6),('Home_A',7.6,19.5,9,6,5,4.1),
      ('Home_B',20.5,21.3,9,5.6,4.8,4.1),('Home_C',23.5,10.7,6,5.7,4.7,4.1),('Home_D',7.4,27.1,12,5.8,4.8,4.1)]
    faces=((3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7))
    for idx,(name,x,y,z,w,d,h) in enumerate(buildings):
        body=bpy.data.objects.get(name+'_stucco') or bpy.data.objects.get(name+'_walls')
        if body:
            body.data=body.data.copy();body.data.materials.clear();body.data.materials.append(M['mortar' if idx in (0,2,4,6) else 'plaster'])
        # Front and both side wall brick courses, shallow enough to keep windows proud.
        buckets={k:([],[]) for k in ('brick','brickLight','brickDark')}
        for side,(length,height) in enumerate([(w,h if idx in(0,2,4,6) else 1.05),(d,h),(d,h)]):
            for row in range(int(height/.28)):
                for col in range(math.ceil(length/.62)+1):
                    lo=max(0,col*.62-(.31 if row%2 else 0));hi=min(length,lo+.59)
                    if hi-lo<.08:continue
                    # Peeling paint leaves patches of masonry visible on plaster houses.
                    if idx not in (0,2,4,6) and row>4 and ((col+row//2)%6 not in (0,1)):continue
                    mid=(lo+hi)/2-length/2;zz=z+.14+row*.28
                    if side==0:cx,cy=x+mid,y-d/2-.013;sx,sy=hi-lo,.038
                    else:cx,cy=x+(w/2+.013)*(1 if side==1 else -1),y+mid;sx,sy=.038,hi-lo
                    key=rng.choices(list(buckets),[7,2,1])[0];vs,fs=buckets[key];j=len(vs)
                    vs.extend((cx+dx*sx/2,cy+dy*sy/2,zz+dz*.123) for dx,dy,dz in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)])
                    fs.extend(tuple(j+i for i in f) for f in faces)
        for key,(vs,fs) in buckets.items():
            if vs:mesh(name+'_WeatheredBrick_'+key,vs,fs,key)
        # Rain pipe, patched parapet, an old electric meter and exterior AC.
        px=x+w/2-.18;fy=y-d/2-.11
        beam(name+'_Downpipe',(px,fy,z+.12),(px,fy,z+h-.2),.085,'metal')
        for zz in (z+.9,z+h-.6):box(name+'_PipeClamp',(px,fy-.025,zz),(.19,.07,.07),'slate')
        box(name+'_Meter',(x-w/2+.32,fy-.03,z+1.22),(.28,.12,.42),'stone')
        if name.startswith(('Home','Gosiwon','Laundry')):
            box(name+'_ACCase',(x+w/2+.23,y+.8,z+1.6),(.42,.92,.65),'trim')
            fan=cyl(name+'_ACFan',(x+w/2+.46,y+.8,z+1.6),.235,.025,'metal',12);fan.rotation_euler.y=math.pi/2
            for j in range(3):box(name+'_RepairPatch',(x-w*.25+j*.32,y-d/2-.042,z+h-.6-j*.15),(.29,.025,.14),'plaster')
    # Domestic rooftop silhouettes: water tanks, laundry, jars and a small kitchen garden.
    cyl('Gosiwon_WaterTank',(-13.8,24.8,19.3),.72,1.5,'teal',12)
    cyl('Gosiwon_WaterTankLid',(-13.8,24.8,20.1),.77,.12,'slate',12)
    for x in (-11.7,-8.7):beam('Gosiwon_ClothesPole',(x,25.5,18.1),(x,25.5,19.9),.065,'metal')
    beam('Gosiwon_ClothesLine',(-11.7,25.5,19.65),(-8.7,25.5,19.65),.022,'wood')
    for j in range(4):box('Gosiwon_DryingLaundry',(-11.35+j*.67,25.5,19.18),(.47,.035,.91),['cream','teal','white','pink'][j])
    for j in range(3):
        cyl('Home_B_Onggi',(19.1+j*.6,21.5,13.52),.24,.56,'brickDark',10)
        cyl('Home_B_OnggiLid',(19.1+j*.6,21.5,13.84),.27,.075,'wood',10)

def add_warm_street_life():
    # Props sit in garden pockets; clearance is applied after placement.
    def bicycle(name,x,y,z):
        for xx in (x-.65,x+.65):
            # Open wheels represented as a 16-sided wire ring.
            for i in range(16):
                a=i*math.tau/16;b=(i+1)*math.tau/16
                beam(name+'_Tyre',(xx+.43*math.cos(a),y,z+.45+.43*math.sin(a)),(xx+.43*math.cos(b),y,z+.45+.43*math.sin(b)),.065,'dark')
            for i in range(6):
                a=i*math.tau/6;beam(name+'_Spoke',(xx,y,z+.45),(xx+.4*math.cos(a),y,z+.45+.4*math.sin(a)),.018,'trim')
        pts=[(-.65,.45),(-.15,1.1),(.35,.45),(-.65,.45),(.18,1.1),(.65,.45),(.18,1.1),(-.15,1.1)]
        for a,b in zip(pts,pts[1:]):beam(name+'_Frame',(x+a[0],y,z+a[1]),(x+b[0],y,z+b[1]),.055,'teal')
        box(name+'_Seat',(x-.15,y,z+1.19),(.35,.19,.10),'wood')
        beam(name+'_HandleStem',(x+.18,y,z+1.1),(x+.3,y,z+1.36),.05,'metal')
        beam(name+'_Handle',(x+.3,y-.24,z+1.36),(x+.3,y+.24,z+1.36),.05,'metal')
    bicycle('Cafe_Bicycle',28.8,-1.7,3)
    bicycle('Station_Bicycle',-24,-18.5,1)
    # New local utility poles, warm street lamps and vending machine.
    for i,(x,y,z) in enumerate([(-28,-2,3),(-8,10,6),(-16.1,18.1,9),(16.6,16,9),(26.5,-17.8,1),(-23,-26,1)]):
        lamp('WarmLamp_'+str(i),x,y,z)
    box('WarmVending_Case',(27.4,-17.15,2.03),(1.05,.76,2.06),'cream')
    box('WarmVending_Glass',(27.2,-17.55,2.12),(.60,.026,1.16),'glass')
    for row in range(3):
        for col in range(3):cyl('WarmVending_Can',(26.99+col*.19,-17.58,1.73+row*.34),.060,.18,['red','green','yellow'][col],8)
    box('WarmVending_Payment',(27.68,-17.56,2.27),(.17,.04,.43),'slate')
    box('WarmVending_PaymentGlow',(27.68,-17.59,2.36),(.11,.013,.15),'window')
    box('WarmVending_Dispense',(27.3,-17.55,1.37),(.61,.03,.16),'dark')
    # Clay planters and small vegetable beds give the urban streets a rural edge.
    for i,(x,y,z) in enumerate([(-28,2,3),(-10.3,10.2,6),(-6.9,26,12),(13.5,20,9),(26.8,7,6),(29,-1,3),(-24.8,-27,1)]):
        cyl('WarmPot_'+str(i),(x,y,z+.30),.38,.6,'brickLight',10)
        cyl('WarmPotRim_'+str(i),(x,y,z+.56),.42,.12,'orange',10)
        ico('WarmPotFoliage_'+str(i),(x,y,z+.9),(.50,.46,.55),'leaf2')
    for x in (14.1,15.3,16.5):
        box('Garden_VegetableBed',(x,26.9,9.15),(.88,2.8,.30),'wood')
        for y in (26.1,26.7,27.3,27.9):ico('Garden_Cabbage',(x,y,9.48),(.35,.34,.27),'leaf3')

def warm_lighting():
    s=bpy.context.scene;s.world.color=(.45,.48,.52)
    for l in bpy.data.lights:
        if l.type=='SUN':
            l.energy=1.85 if 'Warm' in l.name else .55;l.color=(1,.79,.60) if 'Warm' in l.name else (.58,.72,1)
    bpy.data.lights['Sun_WarmEvening'].angle=math.radians(9)
    s.view_settings.view_transform='Khronos PBR Neutral';s.view_settings.exposure=0
    s.eevee.taa_render_samples=16;s.render.engine='BLENDER_EEVEE'
    s.camera=bpy.data.objects['Camera_Isometric'];s.camera.data.ortho_scale=82
    s['ArtDirection']='Older Seoul hillside village: terracotta brick, worn plaster, teal glazing, warm shops, urban and rural street life.'

"""Warm Seoul-outskirts landmarks; execute after the original map refinements.

Expects the builder geometry helpers and palette in the calling namespace.
Only landmark geometry is replaced. Existing terrain and walking meshes survive.
All facade bricks are batched by material, and every frontage faces -Y.
"""

def replace_landmarks():
    import math
    import random
    rng = random.Random(4319)

    # Carefully scoped deletion leaves the bridge, stairs and all path cuts intact.
    prefixes = ('Building_CityHall', 'CityHall_', 'Bakery_', 'Cafe_',
                'BusStop_', 'BusStopBench', 'CafeTerrace',
                'Planter_CityHallRoof', 'Soil_CityHallRoof',
                'Shrub_CityHallRoof', 'Flower_CityHallRoof')
    station_keep = ('Station_TunnelDark', 'Station_EntrySidewall',
                    'Station_EntryThreshold', 'Station_SafetyRail')
    for obj in list(bpy.data.objects):
        if (obj.name.startswith(prefixes) or
                (obj.name.startswith('Station_') and not obj.name.startswith(station_keep))):
            bpy.data.objects.remove(obj, do_unlink=True)

    # A geometry-only brick veneer: individual joints, muted variation, few draws.
    def brick_wall(name, a, b, z, height, brick_w=.63, brick_h=.29, depth=.065,
                   holes=()):
        ax,ay = a; bx,by = b
        length=math.hypot(bx-ax,by-ay); ux=(bx-ax)/length; uy=(by-ay)/length
        nx,ny=uy,-ux
        buckets={k:([],[]) for k in ('brick','brickLight','brickDark')}
        rows=max(1,math.ceil(height/brick_h)); bh=height/rows
        for row in range(rows):
            start=-brick_w*.5 if row%2 else 0
            for i in range(math.ceil(length/brick_w)+1):
                lo=max(0,start+i*brick_w); hi=min(length,start+(i+1)*brick_w)
                if hi-lo<.05: continue
                t=(lo+hi)/2; zz=z+(row+.5)*bh
                if any(hi>hx0 and lo<hx1 and zz+bh/2>hz0 and zz-bh/2<hz1
                       for hx0,hx1,hz0,hz1 in holes): continue
                key=rng.choices(('brick','brickLight','brickDark'),(7,2,1))[0]
                verts,faces=buckets[key]; j=len(verts)
                x=ax+ux*t+nx*depth/2; y=ay+uy*t+ny*depth/2
                hw=(hi-lo-.028)/2; hh=(bh-.027)/2
                for dz in (-hh,hh):
                    for du,dn in ((-hw,-depth/2),(hw,-depth/2),(hw,depth/2),(-hw,depth/2)):
                        verts.append((x+ux*du+nx*dn,y+uy*du+ny*dn,zz+dz))
                faces.extend(tuple(j+k for k in f) for f in
                             ((3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)))
        for key,(verts,faces) in buckets.items():
            if verts: mesh(name+'_BrickCourses_'+key,verts,faces,key)

    def facade_window(name,x,y,z,w,h,trim='trim',warm=True):
        box(name+'_reveal',(x,y+.035,z),(w+.22,.15,h+.20),trim)
        box(name+'_glass',(x,y-.052,z),(w,.045,h),'glassClear')
        if warm:
            box(name+'_warmInterior',(x,y-.079,z-h*.18),(w*.87,.018,h*.44),'window')
        for xx in (x-w/2,x,x+w/2):
            box(name+'_vertical',(xx,y-.104,z),(.062,.067,h),trim)
        for zz in (z-h/2,z+h*.08,z+h/2):
            box(name+'_horizontal',(x,y-.108,zz),(w+.08,.07,.063),trim)
        box(name+'_sill',(x,y-.15,z-h/2-.095),(w+.35,.32,.13),'trim')

    def lantern(name,x,y,z):
        box(name+'_wallPlate',(x,y+.06,z+.10),(.20,.12,.48),'metal')
        beam(name+'_arm',(x,y,z+.22),(x,y-.35,z+.22),.065,'metal')
        cyl(name+'_glow',(x,y-.35,z-.08),.14,.39,'window',8)
        cyl(name+'_cap',(x,y-.35,z+.15),.23,.09,'metal',8)
        cyl(name+'_foot',(x,y-.35,z-.31),.19,.07,'metal',8)
        for dx in (-.115,.115):
            beam(name+'_cage',(x+dx,y-.44,z-.30),(x+dx,y-.44,z+.16),.028,'metal')

    def pot(name,x,y,z,s=.62,flowers=False):
        cyl(name+'_terracotta',(x,y,z+.25*s),.36*s,.50*s,'brick',10)
        cyl(name+'_rim',(x,y,z+.50*s),.39*s,.085*s,'brickLight',10)
        cyl(name+'_soil',(x,y,z+.54*s),.32*s,.02,'wood',10)
        for i in range(4):
            a=i*2.4
            ico(name+'_foliage',(x+math.cos(a)*.23*s,y+math.sin(a)*.23*s,z+.81*s),
                (.37*s,.29*s,.45*s),'leaf2' if i%2 else 'leaf3')
            if flowers:
                ico(name+'_flower',(x+math.cos(a)*.27*s,y+math.sin(a)*.27*s,z+1.13*s),.095*s,'white')

    def cafe_chair(name,x,y,z):
        box(name+'_seat',(x,y,z+.48),(.46,.43,.085),'wood')
        for dx in (-.18,.18):
            for dy in (-.16,.16):
                beam(name+'_leg',(x+dx,y+dy,z),(x+dx*.9,y+dy*.9,z+.45),.045,'wood')
        for dx in (-.19,.19):
            beam(name+'_backPost',(x+dx,y+.16,z+.42),(x+dx,y+.16,z+.94),.045,'wood')
        box(name+'_back',(x,y+.16,z+.83),(.45,.06,.21),'trim')

    def cup(name,x,y,z):
        cyl(name+'_saucer',(x,y,z+.012),.13,.025,'white',12)
        cyl(name+'_cup',(x,y,z+.091),.08,.135,'white',12)
        cyl(name+'_coffee',(x,y,z+.162),.060,.008,'wood',12)
        # Square little handle keeps an intentional low-poly silhouette.
        beam(name+'_handle',(x+.078,y,z+.055),(x+.14,y,z+.10),.022,'white')
        beam(name+'_handle',(x+.14,y,z+.10),(x+.078,y,z+.15),.022,'white')

    # CITY HALL: red masonry, white cornices, two wings, four-column portico.
    z=6
    for side,x in (('West',2.8),('East',11.2)):
        box('Building_CityHall_'+side+'_walls',(x,9.65,9.3),(3.3,6.5,6.6),'mortar')
        brick_wall('CityHall_'+side+'_front',(x-1.65,6.4),(x+1.65,6.4),z,6.55)
        if side=='East': brick_wall('CityHall_East_side',(12.85,6.4),(12.85,12.9),z,6.55)
        else: brick_wall('CityHall_West_side',(1.15,12.9),(1.15,6.4),z,6.55)
        box('CityHall_'+side+'_foundation',(x,9.65,6.21),(3.5,6.7,.42),'stone')
        for zz,thick in ((9.35,.16),(12.65,.26),(13.03,.15)):
            box('CityHall_'+side+'_cornice',(x,9.65,zz),(3.65,6.85,thick),'trim')
        box('CityHall_'+side+'_flatRoof',(x,9.65,12.82),(3.36,6.58,.16),'slate')
        for xx in (x-.81,x+.81):
            for wz in (7.65,10.65):
                facade_window('CityHall_'+side+'_window',xx,6.30,wz,.89,1.77)
        # Slim cast-stone corner blocks create an old municipal-building rhythm.
        for i in range(12):
            for xx in (x-1.57,x+1.57):
                box('CityHall_'+side+'_quoin',(xx,6.29,6.45+i*.51),(.30,.18,.22),'plaster')
    box('Building_CityHall_Central_walls',(7,10.02,9.55),(5.1,5.78,7.1),'brick')
    brick_wall('CityHall_Central_front',(4.45,7.10),(9.55,7.10),6,7.05)
    facade_window('CityHall_Entry',7,6.98,7.60,2.2,3.15,trim='trim',warm=True)
    box('CityHall_DoorHandle_L',(6.86,6.80,7.5),(.035,.10,.49),'metal')
    box('CityHall_DoorHandle_R',(7.14,6.80,7.5),(.035,.10,.49),'metal')
    box('CityHall_PorticoFloor',(7,5.95,6.05),(5.75,2.3,.10),'paver_light')
    for i,x in enumerate((4.70,5.95,8.05,9.30)):
        box('CityHall_ColumnBase_'+str(i),(x,5.18,6.18),(.76,.76,.36),'trim')
        cyl('CityHall_ColumnPlinth',(x,5.18,6.49),.31,.26,'plaster',12)
        cyl('CityHall_ColumnShaft',(x,5.18,9.31),.245,5.42,'plaster',12)
        cyl('CityHall_ColumnCapital',(x,5.18,12.10),.35,.23,'trim',12)
        box('CityHall_ColumnAbacus',(x,5.18,12.28),(.77,.77,.19),'trim')
    box('CityHall_PorticoLintel',(7,5.91,12.55),(6.05,2.75,.36),'trim')
    box('CityHall_PorticoFrieze',(7,5.84,12.91),(5.85,2.62,.37),'plaster')
    sign('CityHall_MunicipalSign','시청',7,4.48,12.86,2.7,.60,'slate')
    # Solid triangular pediment and a pitched slate roof, ridge parallel to +Y.
    mesh('CityHall_ClockPediment',[(4,4.48,13.20),(10,4.48,13.20),(7,4.48,15.15),
                                (4,4.69,13.20),(10,4.69,13.20),(7,4.69,15.15)],
         [(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],'plaster')
    mesh('CityHall_SlateGable',[(3.78,4.20,13.17),(10.22,4.20,13.17),(7,4.20,15.28),
                              (3.78,13.10,13.17),(10.22,13.10,13.17),(7,13.10,15.28)],
         [(0,3,5,2),(2,5,4,1),(3,4,5)],'slate')
    for y in (4.20,13.10):
        for x in (3.78,10.22): beam('CityHall_RoofFascia',(x,y,13.17),(7,y,15.28),.14,'trim')
    for yy in (5.45,6.80,8.15,9.5,10.85,12.2):
        for xx in (3.80,10.20): beam('CityHall_SlateSeam',(xx,yy,13.20),(7,yy,15.30),.035,'metal')
    clock=cyl('CityHall_ClockRim',(7,4.345,14.15),.49,.11,'metal',24)
    clock.rotation_euler.x=math.pi/2
    face=cyl('CityHall_ClockFace',(7,4.276,14.15),.42,.025,'white',24)
    face.rotation_euler.x=math.pi/2
    for i in range(12):
        a=i*math.pi/6
        beam('CityHall_ClockTick',(7+math.sin(a)*.33,4.251,14.15+math.cos(a)*.33),
             (7+math.sin(a)*.38,4.251,14.15+math.cos(a)*.38),.025,'metal')
    beam('CityHall_ClockMinute',(7,4.22,14.15),(7.24,4.22,14.32),.045,'metal')
    beam('CityHall_ClockHour',(7,4.215,14.15),(6.93,4.215,14.33),.060,'metal')
    for xx in (2.05,11.95):
        pot('CityHall_EntryPlanter',xx,5.58,6,1.05,True)
        lantern('CityHall_EntryLantern',xx,6.2,9.2)
    # Thin roof aerial, a familiar detail on older Seoul public buildings.
    beam('CityHall_Aerial',(2.55,10.9,13.13),(2.55,10.9,15.5),.045,'metal')
    for h,w in ((14.2,1.7),(14.7,1.35),(15.2,.7)):
        beam('CityHall_AerialCrossarm',(2.55-w/2,10.9,h),(2.55+w/2,10.9,h),.025,'metal')

    # BAKERY: dusty teal, handmade ochre roof tiles, striped olive awning.
    x,y,z=17,-.30,3; w,d,h=6.75,4.6,4.0; front=y-d/2
    box('Bakery_stucco',(x,y,z+h/2),(w,d,h),'teal')
    box('Bakery_BasePlinth',(x,y,z+.16),(w+.10,d+.10,.32),'stone')
    brick_wall('Bakery_LowerFront',(x-w/2,front),(x+w/2,front),z+.29,.65)
    brick_wall('Bakery_EastWall',(x+w/2,front),(x+w/2,y+d/2),z+.28,1.25)
    facade_window('Bakery_BreadWindow',15.65,front-.08,4.70,2.65,2.28,trim='wood')
    facade_window('Bakery_Door',18.65,front-.08,4.43,1.23,2.75,trim='trim')
    box('Bakery_DoorHandle',(19.0,front-.24,4.2),(.04,.07,.32),'metal')
    box('Bakery_DisplayShelf',(15.65,front-.27,4.25),(2.53,.30,.13),'wood')
    for i in range(7):
        bx=14.59+i*.34
        ico('Bakery_BreadLoaf',(bx,front-.35,4.42),(.15,.10,.13),'cream')
        for j in (-1,1):
            beam('Bakery_LoafScore',(bx+j*.045-.025,front-.445,4.43),(bx+j*.045+.025,front-.445,4.49),.014,'wood')
    sign('Bakery_WoodSign','달동네 빵집',17,front-.13,6.60,4.9,.71,'wood')
    for i in range(14):
        xx=x-w*.48+(i+.5)*w*.96/14
        mat='leaf3' if i%2==0 else 'white'
        o=box('Bakery_OliveAwning',(xx,front-.51,5.98),(w*.96/14,.87,.09),mat)
        o.rotation_euler.x=.18
        box('Bakery_ScallopedValance',(xx,front-.925,5.86),(w*.96/14,.07,.24),mat)
    # Mansard apron around an inset flat roof; all tiles are geometry.
    outer=[(x-w/2-.17,y-d/2-.17),(x+w/2+.17,y-d/2-.17),
           (x+w/2+.17,y+d/2+.17),(x-w/2-.17,y+d/2+.17)]
    inner=[(x-w/2+.42,y-d/2+.42),(x+w/2-.42,y-d/2+.42),
           (x+w/2-.42,y+d/2-.42),(x-w/2+.42,y+d/2-.42)]
    for side in range(4):
        a=Vector((*outer[side],7.0)); b=Vector((*outer[(side+1)%4],7.0))
        c=Vector((*inner[side],8.10)); e=Vector((*inner[(side+1)%4],8.10))
        mesh('Bakery_MansardRoof',list(map(tuple,(a,b,e,c))),[(0,1,2,3)],'cream')
        cols=math.ceil((b-a).length/.47)
        for row in range(4):
            t0=row/4; t1=(row+1)/4-.015
            aa=a.lerp(c,t0); bb=b.lerp(e,t0); cc=a.lerp(c,t1); dd=b.lerp(e,t1)
            for col in range(cols):
                u0=col/cols+.009; u1=(col+1)/cols-.009
                pts=[aa.lerp(bb,u0),aa.lerp(bb,u1),cc.lerp(dd,u1),cc.lerp(dd,u0)]
                mesh('Bakery_OchreTile',[tuple(p+Vector((0,0,.028))) for p in pts],[(0,1,2,3)],
                     'cream' if (row+col)%5 else 'brickLight')
        beam('Bakery_RoofTopTrim',c,e,.14,'trim')
    box('Bakery_FlatRoof',(x,y,8.06),(w-1.05,d-1.05,.12),'plaster')
    box('Bakery_Chimney',(19.25,1.0,8.6),(.64,.70,1.12),'mortar')
    brick_wall('Bakery_ChimneyFront',(18.93,.65),(19.57,.65),8.10,1.02,brick_w=.29,brick_h=.18)
    box('Bakery_ChimneyCap',(19.25,1.0,9.2),(.87,.91,.16),'slate')
    lantern('Bakery_LeftLantern',13.91,front-.10,5.08)
    lantern('Bakery_RightLantern',20.08,front-.10,5.08)
    for xx in (13.91,20.02): pot('Bakery_FrontPot',xx,-2.75,z,.61,True)
    # Greenery hugs the facade above head height, never the promenade.
    for i in range(11):
        xx=14.1+i*.57
        ico('Bakery_AwningVine',(xx,front-.16,6.35),(.27,.20,.20),'leaf2' if i%2 else 'leaf3')

    # CAFE: pale plaster, generous glass, coffee shelves, ivy and a tiny deck.
    x,y,z=25,.25,3; w,d,h=5.75,4.15,5.12; front=y-d/2
    box('Cafe_stucco',(x,y,z+h/2),(w,d,h),'plaster')
    box('Cafe_StoneBase',(x,y,z+.28),(w+.1,d+.1,.56),'stone')
    for zz in (6.35,7.83,8.16): box('Cafe_IvoryCornice',(x,y,zz),(w+.24,d+.24,.15),'trim')
    box('Cafe_FlatRoof',(x,y,8.14),(w,d,.12),'slate')
    for xx in (x-w/2,x+w/2):box('Cafe_Parapet',(xx,y,8.43),(.16,d+.17,.50),'plaster')
    for yy in (front,y+d/2):box('Cafe_Parapet',(x,yy,8.43),(w+.15,.16,.50),'plaster')
    facade_window('Cafe_CoffeeWindow',23.8,front-.06,5.15,2.15,3.13,trim='trim',warm=False)
    facade_window('Cafe_GlassDoor',26.05,front-.065,4.42,1.17,2.72,trim='trim',warm=False)
    box('Cafe_DoorHandle',(26.38,front-.235,4.32),(.04,.08,.30),'wood')
    box('Cafe_WindowShelf',(23.8,front-.23,4.03),(2.19,.31,.10),'wood')
    for zz in (4.10,4.92):
        box('Cafe_CoffeeDisplayShelf',(23.8,front-.17,zz),(1.97,.16,.06),'wood')
        for xx in (23.12,23.52,23.92,24.32): cup('Cafe_DisplayCup',xx,front-.22,zz+.04)
    sign('Cafe_Sign','달빛 카페',24.75,front-.11,7.30,3.72,.79,'teal')
    sign('Cafe_MenuTitle','오늘의 커피',26.09,front-.14,6.60,1.49,.40,'slate')
    box('Cafe_MenuBoard',(26.09,front-.13,6.05),(1.49,.10,.82),'wood')
    box('Cafe_MenuSlate',(26.09,front-.193,6.05),(1.36,.04,.69),'slate')
    for j in range(4):
        for col in (0,1):
            box('Cafe_MenuChalk',(25.77+col*.63,front-.22,6.28-j*.15),(.41 if col==0 else .22,.012,.022),'white')
    # Narrow deck is contained within the former parcel, behind the public path.
    for i in range(13):box('Cafe_DeckBoard',(22.26+i*.45,-2.40,3.055),(.43,1.10,.105),'wood')
    cyl('Cafe_PatioTable',(23.30,-2.38,3.70),.40,.07,'trim',16)
    cyl('Cafe_PatioTableLeg',(23.30,-2.38,3.38),.055,.62,'metal',8)
    cup('Cafe_PatioCup',23.16,-2.38,3.74)
    cafe_chair('Cafe_PatioChair',22.55,-2.43,3.10)
    pot('Cafe_DeckFlowers',27.52,-2.54,3.10,.67,True)
    pot('Cafe_EntryPot',25.06,-2.01,3.1,.42)
    # Vines descend from the right cornice, evoking the supplied wisteria reference.
    for i in range(8):
        xx=24.0+i*.50
        ico('Cafe_CorniceIvy',(xx,front-.18,7.98),(.42,.27,.27),'leaf2')
        for k in range(2+(i%3)):
            ico('Cafe_Wisteria',(xx+.06,front-.23,7.67-k*.14),(.087,.075,.15),'purple')
    for i in range(10):
        zz=3.7+i*.37
        ico('Cafe_WallIvy',(27.89,-.84,zz),(.21,.37,.25),'leaf2' if i%2 else 'leaf3')
    lantern('Cafe_PorchLantern',27.27,front-.08,5.97)

    # STATION: a real-world glass-and-steel enclosure around the existing stairs.
    # Mouth [-22.1,-12.9] x [-25.72,-21.4] remains completely open.
    x=-17.5
    for xx in (-22.34,-12.66):
        for yy in (-25.52,-23.36,-21.20):
            box('Station_SteelColumn',(xx,yy,2.73),(.14,.14,3.42),'metal')
        box('Station_LowStoneCheek',(xx,-23.35,1.32),(.24,4.55,.64),'stone')
        box('Station_GlazedSide',(xx,-23.35,2.93),(.045,4.48,2.40),'glassClear')
        for zz in (1.73,2.8,4.14):
            beam('Station_SideMullion',(xx,-25.59,zz),(xx,-21.11,zz),.082,'trim')
    for yy in (-25.59,-21.11):
        beam('Station_Header',(-22.42,yy,4.35),(-12.58,yy,4.35),.18,'metal')
    box('Station_BackGlass',(-17.5,-21.13,2.95),(9.53,.04,2.38),'glassClear')
    for xx in (-20.73,-17.5,-14.27):box('Station_BackMullion',(xx,-21.13,2.95),(.07,.10,2.54),'trim')
    box('Station_RoofSteel',(-17.5,-23.35,4.5),(10.17,4.96,.15),'metal')
    box('Station_RoofSkylight',(-17.5,-23.35,4.595),(7.35,3.42,.045),'glassClear')
    for xx in (-21.23,-18.77,-16.27,-13.77):
        beam('Station_SkylightRib',(xx,-25.10,4.63),(xx,-21.60,4.63),.07,'trim')
    sign('Station_Sign','달동네역',-17.5,-25.67,4.12,4.9,.68,'teal')
    # Thin silver handrails follow the true descending steps at their outer edges.
    for xx in (-21.99,-13.01):
        a=Vector((xx,-25.62,1.06)); b=Vector((xx,-21.68,-1.76))
        beam('Station_DescendingHandrail',a+Vector((0,0,.90)),b+Vector((0,0,.90)),.065,'trim')
        for t in (0,.33,.67,1):
            p=a.lerp(b,t);beam('Station_HandrailPost',p,p+Vector((0,0,.9)),.050,'trim')
    # Line 2 pylon is left of the aperture, never centered in a walking route.
    box('Station_PylonFoot',(-23.32,-25.38,1.065),(.63,.49,.13),'stone')
    box('Station_PylonPole',(-23.32,-25.38,2.11),(.13,.13,2.11),'trim')
    box('Station_Line2Pylon',(-23.32,-25.38,3.55),(.65,.32,2.64),'teal')
    sign('Station_Line2Header','2',-23.32,-25.57,4.42,.53,.62,'yellow')
    for i,ch in enumerate('달동네역'):
        lettering('Station_PylonHangul',ch,-23.32,-25.565,3.86-i*.35,.37,.29)
    token=cyl('Station_Line2',(-14.08,-25.82,4.13),.26,.05,'green',20)
    token.rotation_euler.x=math.pi/2
    lettering('Station_Line2Digit','2',-14.08,-25.86,4.13,.25,.33)
    # Tactile studs are low relief on the existing yellow threshold.
    for i in range(35):
        for yy in (-26.055,-25.91):
            cyl('Station_TactileDot',(-21.72+i*.25,yy,1.115),.035,.017,'cream',6)

    # BUS SHELTER: slender dark frame, translucent teal panels, timber bench.
    for xx in (18.65,22.82,26.95):
        box('BusStop_SteelPost',(xx,-16.67,2.46),(.12,.13,2.92),'metal')
    for xx in (18.65,26.95):box('BusStop_FrontPost',(xx,-14.50,2.40),(.10,.10,2.80),'metal')
    box('BusStop_BackGlass',(22.8,-16.70,2.53),(8.23,.045,2.62),'glassClear')
    for zz in (1.35,2.33,3.52):box('BusStop_GlassRail',(22.8,-16.74,zz),(8.39,.07,.064),'metal')
    for xx in (18.65,26.95):box('BusStop_SideGlass',(xx,-15.62,2.40),(.035,2.08,2.37),'glassClear')
    roof=box('BusStop_SlenderRoof',(22.8,-15.60,3.98),(8.99,2.70,.13),'metal');roof.rotation_euler.x=-.055
    glazing=box('BusStop_RoofGlass',(22.8,-15.57,4.055),(8.51,2.19,.037),'glassClear');glazing.rotation_euler.x=-.055
    for xx in (18.75,20.75,22.80,24.85,26.85):
        beam('BusStop_RoofRib',(xx,-16.9,4.12),(xx,-14.27,3.98),.063,'trim')
    sign('BusStop_Sign','달동네 버스정류장',22.8,-16.88,3.67,6.3,.46,'metal')
    box('BusStop_RouteCase',(19.30,-16.79,2.40),(1.02,.14,1.78),'metal')
    box('BusStop_RoutePanel',(19.30,-16.88,2.40),(.84,.025,1.57),'plaster')
    lettering('BusStop_RouteNumber','701',19.30,-16.91,2.94,.67,.30,'teal')
    for i in range(6):
        yy=-16.917; zz=2.64-i*.175
        box('BusStop_TimetableLine',(19.34,yy,zz),(.49,.012,.025),'teal')
        ico('BusStop_RouteNode',(19.0,yy,zz),(.035,.012,.035),'green')
    for i in range(4):box('BusStop_BenchSlat',(23.15,-16.15+i*.12,1.54),(4.65,.095,.085),'wood')
    for xx in (21.3,23.15,25.0):
        beam('BusStop_BenchLeg',(xx,-15.98,1.02),(xx,-15.98,1.5),.10,'metal')
    for zz in (1.88,2.10):box('BusStop_BenchBack',(23.15,-16.39,zz),(4.65,.085,.16),'wood')
    box('BusStop_WarmLight',(23,-16.54,3.75),(4.65,.06,.045),'window')

    # Batch tile geometry too, while keeping the editable architectural parts.
    for prefix in ('Bakery_OchreTile','Bakery_MansardRoof'):
        groups={}
        for obj in list(bpy.context.scene.objects):
            if obj.type=='MESH' and obj.name.startswith(prefix):
                groups.setdefault(obj.data.materials[0].name,[]).append(obj)
        for key,objects in groups.items():
            if len(objects)<2: continue
            verts=[];faces=[]
            for obj in objects:
                offset=len(verts)
                verts.extend(tuple(obj.matrix_world@v.co) for v in obj.data.vertices)
                faces.extend(tuple(offset+i for i in p.vertices) for p in obj.data.polygons)
            mat=objects[0].data.materials[0]
            for obj in objects:bpy.data.objects.remove(obj,do_unlink=True)
            mesh(prefix+'_Combined_'+key,verts,faces,mat)

    bpy.context.scene['WarmLandmarksRevision']='Seoul brick city hall, teal bakery, ivy cafe, glazed subway and local bus shelter'

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

setup_warm_palette()
replace_landmarks()
dress_neighborhood()
add_warm_street_life()
route_cuts=build_clear_routes()
bpy.context.view_layer.update()
removed_props=clear_route_props()
warm_lighting()
result={"objects":len(bpy.data.objects),"route_cuts":route_cuts,"stairs":len(ROUTES),"flat_connections":len(FLATS),"removed_obstructions":removed_props,"triangles":sum(len(p.vertices)-2 for o in bpy.data.objects if o.type=="MESH" for p in o.data.polygons)}

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
