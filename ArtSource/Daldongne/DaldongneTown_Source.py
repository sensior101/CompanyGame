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
