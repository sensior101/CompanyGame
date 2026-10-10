"""Build an original, reference-inspired POS asset. Run in a fresh Blender process."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector

HERE=Path(__file__).resolve().parent
EXPORT=HERE/'Exports'; PREVIEW=HERE/'Preview'
for d in (EXPORT,PREVIEW,EXPORT/'Textures'):d.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
scene.render.fps=30;scene.frame_start=1;scene.frame_end=91
model=bpy.data.collections.new('RetailPOS_ASSET');scene.collection.children.link(model)
studio=bpy.data.collections.new('Presentation_ONLY');scene.collection.children.link(studio)

def move_collection(o,c=model):
    for a in list(o.users_collection):a.objects.unlink(o)
    c.objects.link(o)
def empty(name,parent=None):
    o=bpy.data.objects.new(name,None);model.objects.link(o);o.parent=parent;return o
root=empty('RetailPOS');root['FurnitureFunction']='Display';root['units']='metres'
root['screen']='Screen_Main: replace image on POS_Screen_Main material'
root['drawer']='CashDrawerSlide local -Y travel 0.24m (Unity local -Z)'

def material(name,color,metal=0,rough=.4):
    m=bpy.data.materials.new(name);m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
    m.diffuse_color=(*color,1);return m
shell=material('POS_CharcoalPolymer',(.024,.028,.033),.12,.32)
trim=material('POS_EdgeGraphite',(.055,.062,.071),.55,.25)
rubber=material('POS_Rubber',(.009,.011,.014),0,.68)
metal=material('POS_BrushedSteel',(.31,.34,.37),.87,.27)
traymat=material('POS_DrawerTray',(.033,.037,.044),.06,.48)
paper=material('POS_ReceiptPaper',(.83,.82,.77),0,.82)
ink=material('POS_ReceiptInk',(.18,.2,.21),0,.65)
red=material('POS_CancelRed',(.42,.026,.019),0,.3)
amber=material('POS_CorrectAmber',(.74,.34,.035),0,.3)
green=material('POS_ConfirmGreen',(.018,.28,.12),0,.3)
lens=material('POS_ScannerLens',(.16,.002,.006),.35,.14)
status=material('POS_StatusLight',(.15,.5,.48),.1,.3)
pbs=status.node_tree.nodes.get('Principled BSDF');pbs.inputs['Emission Color'].default_value=(.15,.65,.55,1);pbs.inputs['Emission Strength'].default_value=.6

black=bpy.data.images.new('POS_BlackScreen',1024,1024,alpha=False)
black.generated_color=(0,0,0,1);black.filepath_raw=str(EXPORT/'Textures/POS_BlackScreen.png');black.file_format='PNG';black.save()
screenmats={}
for key in ('Main','Customer','Card'):
    m=material('POS_Screen_'+key,(0,0,0),0,.22)
    n=m.node_tree.nodes.new('ShaderNodeTexImage');n.name='DisplayImage';n.label='Replace with your display image';n.image=black
    bs=m.node_tree.nodes.get('Principled BSDF');m.node_tree.links.new(n.outputs['Color'],bs.inputs['Base Color']);m.node_tree.links.new(n.outputs['Color'],bs.inputs['Emission Color']);bs.inputs['Emission Strength'].default_value=.5
    screenmats[key]=m

def cube(name,loc,size,mat,parent=root,bevel=.002,rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1);o=bpy.context.object;o.name=name;move_collection(o)
    o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        m=o.modifiers.new('Manufactured edge radius','BEVEL');m.width=bevel;m.segments=3
        bpy.ops.object.modifier_apply(modifier=m.name)
        for p in o.data.polygons:p.use_smooth=True
        m=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL');m.keep_sharp=True;bpy.ops.object.modifier_apply(modifier=m.name)
    o.data.materials.append(mat);o.parent=parent;o.location=loc
    if rot:o.rotation_euler=rot
    return o
def cyl(name,loc,radius,depth,mat,parent=root,axis='Z',vertices=20):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth);o=bpy.context.object;o.name=name;move_collection(o)
    o.data.materials.append(mat);o.parent=parent;o.location=loc
    if axis=='Y':o.rotation_euler.x=math.pi/2
    if axis=='X':o.rotation_euler.y=math.pi/2
    for p in o.data.polygons:p.use_smooth=True
    return o
def rod(name,a,b,r,mat,parent=root):
    v=Vector(b)-Vector(a);o=cyl(name,(Vector(a)+Vector(b))/2,r,v.length,mat,parent)
    o.rotation_euler=v.to_track_quat('Z','Y').to_euler();return o
def cable(name,points,r,mat,parent=root):
    cu=bpy.data.curves.new(name,'CURVE');cu.dimensions='3D';cu.bevel_depth=r;cu.bevel_resolution=2;cu.resolution_u=10
    s=cu.splines.new('BEZIER');s.bezier_points.add(len(points)-1)
    for p,co in zip(s.bezier_points,points):p.co=co;p.handle_left_type='AUTO';p.handle_right_type='AUTO'
    ob=bpy.data.objects.new(name,cu);model.objects.link(ob);ob.parent=parent;cu.materials.append(mat);return ob
def screen(name,parent,size,center,mat):
    w,h=size;x,y,z=center
    verts=[(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),(x+w/2,y,z+h/2),(x-w/2,y,z+h/2)]
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],[(0,1,2,3)]);me.update()
    uv=me.uv_layers.new(name='DisplayUV')
    for i,co in enumerate([(0,0),(1,0),(1,1),(0,1)]):uv.data[i].uv=co
    o=bpy.data.objects.new(name,me);model.objects.link(o);o.parent=parent;me.materials.append(mat)
    o['Purpose']='Image display surface; full 0..1 UV; independent material';return o

body=empty('CashDrawerHousing',root)
cube('Housing_floor',(0,0,.020),(.420,.340,.019),shell,body)
cube('Housing_top',(0,0,.108),(.420,.340,.014),shell,body)
for x in (-.202,.202):cube('Housing_side',(x,0,.065),(.016,.340,.08),shell,body)
cube('Housing_rear',(0,.161,.065),(.400,.018,.08),shell,body)
cube('Front_upper_frame',(0,-.168,.105),(.420,.012,.009),trim,body,.001)
cube('Front_lower_frame',(0,-.168,.026),(.420,.012,.008),trim,body,.001)
for x in (-.172,.172):
    for y in (-.127,.127):cube('Rubber_foot',(x,y,.005),(.045,.047,.010),rubber,body,.003)
for x in (-.195,.195):
    cube('Fixed_slide_rail',(x,.014,.048),(.007,.272,.010),metal,body,.001)

slider=empty('CashDrawerSlide',root)
slider['ClosedLocalY']=0.0;slider['OpenLocalY']=-.24;slider['OpenDistanceMetres']=.24
cube('Drawer_front',(0,-.177,.064),(.388,.021,.071),shell,slider,.004)
cube('Drawer_front_inset',(0,-.189,.064),(.365,.002,.052),trim,slider,.002)
for x in (-.104,.104):cube('Cash_slot',(x,-.1907,.070),(.107,.0015,.0038),rubber,slider,.001)
cyl('Drawer_lock_bezel',(0,-.192,.061),.009,.005,metal,slider,'Y',32)
cube('Lock_keyway',(0,-.195,.061),(.002,.001,.009),rubber,slider,.0003)
cube('Tray_floor',(0,-.003,.033),(.374,.300,.008),traymat,slider,.002)
for x in (-.184,.184):cube('Tray_side',(x,-.003,.061),(.006,.300,.056),traymat,slider,.0015)
cube('Tray_rear',(0,.145,.061),(.368,.006,.056),traymat,slider,.0015)
cube('Tray_front_lip',(0,-.148,.05),(.368,.006,.032),traymat,slider,.001)
cube('Tray_coin_separator',(0,-.067,.061),(.366,.005,.048),traymat,slider,.001)
for x in (-.11,-.037,.037,.11):cube('Coin_partition',(x,-.108,.049),(.003,.078,.023),traymat,slider,.0007)
for x in (-.092,0,.092):cube('Bill_partition',(x,.039,.059),(.003,.206,.045),traymat,slider,.0007)
for x in (-.138,-.046,.046,.138):
    rod('Bill_clip_hinge',(x-.031,.113,.077),(x+.031,.113,.077),.0022,metal,slider)
    cable('Bill_clip_spring',[(x,.112,.080),(x,.08,.090),(x,.015,.047),(x,-.018,.046)],.0015,metal,slider)
for x in (-.185,.185):cube('Moving_slide_rail',(x,-.002,.042),(.006,.295,.009),metal,slider,.001)
for frame,y in ((1,0),(31,-.24),(61,-.24),(91,0)):
    slider.location.y=y;slider.keyframe_insert(data_path='location',frame=frame,group='Drawer translation')
slider.animation_data.action.name='CashDrawer_OpenClose'
for name,frame in [('CLOSED',1),('OPEN',31),('CLOSE_START',61),('CLOSED_END',91)]:scene.timeline_markers.new(name,frame=frame)
scene.frame_set(1)

printer=empty('ReceiptPrinter',root)
cube('Printer_body',(-.08,-.005,.154),(.202,.255,.080),shell,printer,.011)
cube('Printer_top_lid',(-.08,.026,.197),(.181,.175,.010),trim,printer,.006)
cube('Receipt_exit_recess',(-.08,-.089,.193),(.126,.025,.010),rubber,printer,.003)
rod('Receipt_cutter',(-.14,-.095,.198),(-.02,-.095,.198),.0012,metal,printer)
cube('Feed_button',(.007,-.075,.200),(.012,.016,.005),rubber,printer,.002)
cube('Printer_status',(.008,-.045,.204),(.006,.003,.002),status,printer,.001)
# A gently curved paper strip with geometric receipt strokes, no fabricated branding.
verts=[]
for i in range(9):
    t=i/8;y=-.09+.025*math.sin(t*1.7);z=.198+.047*t
    verts.extend([(-.134,y,z),(-.026,y,z)])
faces=[(2*i,2*i+1,2*i+3,2*i+2) for i in range(8)]
me=bpy.data.meshes.new('ReceiptPaper');me.from_pydata(verts,[],faces);me.update()
o=bpy.data.objects.new('Receipt_paper',me);model.objects.link(o);o.parent=printer;me.materials.append(paper)
for i in range(8):
    t=.12+i*.085;y=-.09+.025*math.sin(t*1.7)-.0007;z=.198+.047*t
    cube('Receipt_print_line',(-.081,y,z),(.071 if i%3 else .04,.0003,.0011),ink,printer,0)

stand=empty('MonitorStand',root)
cube('Monitor_foot',(.035,.065,.122),(.175,.158,.018),trim,stand,.005)
cube('Monitor_stand',(.035,.069,.224),(.065,.05,.195),shell,stand,.006,(-.20,0,0))
cyl('Monitor_hinge',(.035,.052,.323),.024,.129,trim,stand,'X',32)
monitor=empty('MainMonitor',root);monitor.location=(0,-.005,.380);monitor.rotation_euler.x=math.radians(-18)
cube('Rear_housing',(0,.010,0),(.408,.037,.272),shell,monitor,.009)
cube('Front_bezel',(0,-.011,0),(.405,.009,.269),trim,monitor,.006)
cube('Screen_black_gasket',(0,-.0163,.006),(.376,.003,.235),rubber,monitor,.004)
screen('Screen_Main',monitor,(.368,.227),(0,-.018,.006),screenmats['Main'])
cyl('Power_button',(.176,-.017,-.119),.0035,.0014,rubber,monitor,'Y')
cyl('Power_indicator',(.161,-.017,-.119),.0013,.0014,status,monitor,'Y',12)
for x in [-.12+i*.010 for i in range(25)]:cube('Monitor_rear_vent',(x,.029,.072),(.004,.0015,.032),rubber,monitor,.001)
for x in (-.165,.165):
    for z in (-.104,.104):cyl('Monitor_screw',(x,.030,z),.002,.001,metal,monitor,'Y',12)
cube('Monitor_io_recess',(.077,.030,-.083),(.110,.002,.025),rubber,monitor,.002)
for x in (.04,.062,.084):cube('USB_socket',(x,.032,-.083),(.013,.002,.006),metal,monitor,.0005)

customer=empty('CustomerDisplay',root)
cube('Customer_pole_base',(.117,.107,.123),(.094,.069,.015),shell,customer,.004)
cyl('Customer_pole',(.117,.117,.328),.010,.402,trim,customer)
cyl('Customer_pole_collar',(.117,.117,.515),.016,.026,shell,customer)
# Customer display faces away from the cashier. Its own image material is independent.
customer_head=empty('CustomerDisplayHead',customer);customer_head.location=(.117,.117,.551);customer_head.rotation_euler.z=math.pi
cube('Customer_housing',(0,0,0),(.163,.027,.077),shell,customer_head,.006)
cube('Customer_bezel',(0,-.014,0),(.150,.003,.064),trim,customer_head,.003)
screen('Screen_Customer',customer_head,(.136,.049),(0,-.016,0),screenmats['Customer'])

card=empty('CardTerminal',root)
cube('Card_base',(.276,-.03,.011),(.101,.123,.022),shell,card,.006)
cube('Card_pedestal',(.276,.013,.080),(.043,.047,.129),trim,card,.006,(math.radians(-18),0,0))
card_head=empty('CardTerminalHead',card);card_head.location=(.276,-.020,.17);card_head.rotation_euler.x=math.radians(-51)
cube('Card_reader_body',(0,0,0),(.087,.035,.155),shell,card_head,.009)
cube('Card_reader_face',(0,-.019,0),(.077,.004,.14),trim,card_head,.005)
screen('Screen_Card',card_head,(.056,.039),(0,-.022,.044),screenmats['Card'])
for row in range(4):
    for col in range(3):
        mat=([red,amber,green][col] if row==3 else rubber)
        cube('Card_key',((col-1)*.021,-.023,-.001-row*.017),(.016,.004,.012),mat,card_head,.002)
cube('Chip_card_slot',(0,-.002,-.077),(.058,.006,.001),rubber,card_head,.0003)

scanner=empty('BarcodeScanner',root)
cube('Scanner_dock',(-.287,-.002,.022),(.100,.120,.040),shell,scanner,.013)
cube('Scanner_dock_recess',(-.287,.01,.044),(.055,.060,.012),rubber,scanner,.008)
grip=cube('Scanner_grip',(-.287,.015,.102),(.030,.042,.120),shell,scanner,.012,(math.radians(-24),0,0))
head=empty('ScannerHead',scanner);head.location=(-.287,.035,.166);head.rotation_euler.x=math.radians(-10)
cube('Scanner_head',(0,0,0),(.091,.076,.060),shell,head,.019)
cube('Scanner_lens_frame',(0,-.039,0),(.070,.004,.038),rubber,head,.011)
cube('Scanner_red_lens',(0,-.0415,0),(.057,.002,.026),lens,head,.007)
cube('Scanner_trigger',(-.287,-.012,.135),(.021,.010,.020),rubber,scanner,.003)
cable('Scanner_cable',[(-.287,.045,.04),(-.277,.11,.022),(-.22,.145,.02),(-.15,.174,.069)],.0021,rubber)
cable('Display_cable',[(.047,.039,.304),(.064,.1,.259),(.070,.135,.152),(.085,.17,.11)],.003,rubber)
cable('Card_cable',[(.28,.028,.115),(.26,.12,.09),(.21,.166,.04),(.14,.171,.04)],.002,rubber)

# Join manufactured subassemblies, but retain three screen renderers and the sliding transform.
for ob in list(model.objects):
    if ob.type=='CURVE':
        bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob;bpy.ops.object.convert(target='MESH')
bpy.context.view_layer.update()
for parent in [body,slider,printer,stand,monitor,customer,customer_head,card,card_head,scanner,head,root]:
    parts=[o for o in model.objects if o.type=='MESH' and o.parent==parent and not o.name.startswith('Screen_')]
    if len(parts)>1:
        bpy.ops.object.select_all(action='DESELECT')
        for o in parts:o.select_set(True)
        bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();parts[0].name=parent.name+'_Mesh'
for ob in model.objects:
    if ob.type=='MESH' and not ob.data.uv_layers:
        bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.015);bpy.ops.object.mode_set(mode='OBJECT')

# Validate real geometry and the drawer's animated travel before export.
scene.frame_set(1);bpy.context.view_layer.update();closed=slider.matrix_world.translation.copy()
scene.frame_set(31);bpy.context.view_layer.update();opened=slider.matrix_world.translation.copy()
assert abs((opened-closed).y+.24)<1e-5
for key in ('Main','Customer','Card'):
    s=bpy.data.objects['Screen_'+key];assert len(s.data.polygons)==1 and len(s.data.uv_layers)==1
scene.frame_set(1)
meshobjects=[o for o in model.objects if o.type=='MESH']
triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshobjects)
manifest={'name':'RetailPOS','units':'metres','triangles':triangles,'meshCount':len(meshobjects),'screenObjects':['Screen_Main','Screen_Customer','Screen_Card'],'screenImage':'Textures/POS_BlackScreen.png','drawerObject':'CashDrawerSlide','drawerTravelMetres':.24,'blenderOpenLocalOffset':[0,-.24,0],'unityOpenLocalOffset':[0,0,-.24],'fps':30,'clips':[{'name':'CashDrawer_Open','firstFrame':1,'lastFrame':31,'loop':False},{'name':'CashDrawer_Close','firstFrame':61,'lastFrame':91,'loop':False}],'checks':{'threeSeparateScreens':True,'fullRectangleScreenUVs':True,'drawerAnimatedTravel':True},'materials':[{'name':m.name,'color':list(m.diffuse_color),'metallic':m.node_tree.nodes.get('Principled BSDF').inputs['Metallic'].default_value,'roughness':m.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value} for m in bpy.data.materials if m.use_nodes and m.node_tree.nodes.get('Principled BSDF')]}
(EXPORT/'RetailPOS.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')
bpy.ops.object.select_all(action='DESELECT')
for o in model.objects:o.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(EXPORT/'RetailPOS.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',use_custom_props=True,apply_unit_scale=True,add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='RELATIVE')

# Studio objects are deliberately excluded from the FBX.
floor=cube('Studio_floor',(0,0,-.015),(200,200,.02),material('Studio_WarmGrey',(.32,.31,.29),0,.8),None,0);move_collection(floor,studio)
def area(name,pos,power,size,target):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size
    o=bpy.data.objects.new(name,d);studio.objects.link(o);o.location=pos;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
area('Key',(-.8,-1.0,1.8),70,1.1,(0,0,.25));area('Fill',(1,-.25,.95),45,.8,(0,0,.25));area('Rim',(.1,.8,1.35),95,.8,(0,0,.3))
scene.world.color=(.23,.23,.23)
camdata=bpy.data.cameras.new('PresentationCamera');cam=bpy.data.objects.new('PresentationCamera',camdata);studio.objects.link(cam);scene.camera=cam
camdata.type='ORTHO';camdata.ortho_scale=.92
scene.render.engine='CYCLES';scene.cycles.samples=40;scene.cycles.use_denoising=True
scene.render.resolution_x=1400;scene.render.resolution_y=1200;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
def view(name,pos,target,frame=1,scale=.92):
    scene.frame_set(frame);cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();camdata.ortho_scale=scale
    scene.render.filepath=str(PREVIEW/name);bpy.ops.render.render(write_still=True)
view('RetailPOS_Closed.png',(.95,-1.5,1.02),(0,-.012,.282))
view('RetailPOS_DrawerOpen.png',(.9,-1.5,1.28),(0,-.085,.265),31,1.04)
view('RetailPOS_Rear.png',(-1,1.6,1.05),(0,.01,.28))
scene.frame_set(1);cam.location=(.95,-1.5,1.02);cam.rotation_euler=(Vector((0,-.012,.282))-cam.location).to_track_quat('-Z','Y').to_euler();camdata.ortho_scale=.92
black.filepath='//Exports/Textures/POS_BlackScreen.png'
bpy.ops.object.select_all(action='DESELECT');root.select_set(True);bpy.context.view_layer.objects.active=root
for screenarea in bpy.context.screen.areas if bpy.context.screen else []:
    if screenarea.type=='VIEW_3D':screenarea.spaces.active.region_3d.view_perspective='CAMERA'
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'RetailPOS.blend'))
print('RETAIL_POS_COMPLETE',triangles,len(meshobjects),flush=True)
