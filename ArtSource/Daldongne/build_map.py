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

# PROPS_SOURCE_INSERTION_POINT

build()
result={'objects':len(bpy.data.objects),'meshes':len(bpy.data.meshes),'materials':len(bpy.data.materials),'map':'Daldongne low-poly 3D neighborhood','units':'metres','delivery_camera':bpy.context.scene.camera.name}
