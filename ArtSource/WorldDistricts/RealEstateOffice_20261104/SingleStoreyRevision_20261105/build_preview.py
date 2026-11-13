"""Preview-first human-scale connected office. Original entrance/materials reused.
No FBX/GLB export until the silhouette and doorway preview has been reviewed.
"""
import bpy,sys,ast,json,math,hashlib
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent;SOURCE=OUT.parent
LOW=SOURCE.parent/'CommercialBlocks/LowPoly_20261104'
for folder in ('Models','Previews','QA'):(OUT/folder).mkdir(parents=True,exist_ok=True)
def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),str(path),'exec'),globals())
reuse(LOW/'KoreanPalette/recolor_existing.py',{'linear','descendant','asset_objects'})
reuse(LOW/'KoreanPalette/SignageReady/retrofit_signage.py',{'box','bounds'})
reuse(LOW/'KoreanPalette/SignageReady/FrontOnly/build_front_only.py',{'render','material_specs'})
reuse(SOURCE/'build_office.py',{'empty','mat','cube'})
bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'Models/RE_01.blend'));bpy.context.view_layer.update()
stage=bpy.data.collections['Presentation_ONLY'];entrance=bpy.data.objects['Entrance'];textob=bpy.data.objects['Sign_Main_01_Lettering']
keep=set(asset_objects(entrance)+[textob]);keep.update(o for o in stage.objects if not o.name.startswith('ParcelGuide_'))
for ob in (entrance,textob):
    world=ob.matrix_world.copy();ob.parent=None;ob.matrix_world=world
for ob in list(bpy.context.scene.objects):
    if ob not in keep:bpy.data.objects.remove(ob,do_unlink=True)
collection=bpy.data.collections.new('RE_01_ASSET');bpy.context.scene.collection.children.link(collection)
for ob in keep:
    if ob.name not in stage.objects:
        for c in list(ob.users_collection):c.objects.unlink(ob)
        collection.objects.link(ob)
root=empty('RE_01');groups={n:empty(n,root) for n in ('Structure','Facade','Frames','Glass','Signage','RoofEquipment')}
groups['Entrance']=entrance;entrance.parent=root;entrance.location.y=-6.18
root['FloorCount']=1;root['Variant']='SingleStoreyRevision_20261105';root['ParcelTier']='Civic';root['ParcelSizeMetres']=48.;root['EntryClearWidthMetres']=1.8
wall=bpy.data.materials['RE_01_WarmOffWhite'];stone=bpy.data.materials['RE_01_BaseConcrete'];frame=bpy.data.materials['RE_01_CharcoalFrame'];glass=bpy.data.materials['RE_01_OpaqueSmokyGlass'];roof=bpy.data.materials['RE_01_RoofGrey'];sign=bpy.data.materials['RE_01_SignFace'];paper=bpy.data.materials['RE_01_ListingPaper'];ink=bpy.data.materials['RE_01_ListingInk'];trim=bpy.data.materials['RE_01_AluminumTrim']
# Three touching floor volumes; one connected building, not detached props.
cube('FloorSlab_Main',(0,-5.5,.07),(13,9,.14),stone)
cube('FloorSlab_Left',(-10,-6.5,.07),(7,7,.14),stone)
cube('FloorSlab_Right',(10,0,.07),(7,20,.14),stone)
cube('RearWall_Main',(0,-1.13,2.35),(13,.26,4.42),wall)
cube('RearWall_Left',(-10,-3.13,2.05),(7,.26,3.82),wall)
cube('RearWall_Right',(10,9.73,2.05),(7,.26,3.82),wall)
cube('SideWall_LeftOuter',(-13.23,-6.5,2.05),(.26,7,3.82),wall)
cube('SideWall_RightOuter',(13.23,0,2.05),(.26,19.72,3.82),wall)
cube('SideWall_Courtyard',(6.63,4.37,2.05),(.26,10.72,3.82),wall)
cube('SideWall_MainReturn',(-6.37,-2.0,2.32),(.26,2.0,4.36),wall)
# The taller central fascia joins the lower wings without open roof seams.
for x in (-6.37,6.37):cube('UpperStepWall',(x,-5.5,4.35),(.26,9,.60),wall)
for x in (-10,10):cube('FrontWingWall',(x,-9.73,2.05),(7,.26,3.82),wall,'Facade')
cube('FrontHeadBand_Main',(0,-9.73,3.79),(13,.26,1.52),wall,'Facade')
for x in (-6.08,6.08):cube('FrontPier_Main',(x,-9.73,1.59),(.84,.26,2.90),wall,'Facade')
def roof_volume(label,center,size,height):
    x,y=center;w,d=size
    cube(label+'_RoofSlab',(x,y,height-.36),(w,d,.18),roof)
    cube(label+'_RoofSurface',(x,y,height-.25),(w-.30,d-.30,.04),roof)
    for yy in (y-d/2+.13,y+d/2-.13):
        cube(label+'_Parapet',(x,yy,height-.14),(w-.12,.18,.20),wall)
        cube(label+'_Cap',(x,yy,height-.03),(w,.26,.06),frame)
    for xx in (x-w/2+.13,x+w/2-.13):
        cube(label+'_Parapet',(xx,y,height-.14),(.18,d-.30,.20),wall)
        cube(label+'_Cap',(xx,y,height-.03),(.26,d,.06),frame)
roof_volume('Left',(-9.95,-6.5),(7.1,7),4.4)
roof_volume('Right',(9.95,0),(7.1,20),4.4)
roof_volume('Main',(0,-5.5),(13.2,9),5.0)
# Main storefront: two broad display windows, not a repeated supermarket grid.
for side in (-1,1):
    center=side*3.45
    cube('WindowSill',(center,-9.70,.40),(4.44,.24,.52),wall,'Facade')
    cube('StorefrontGlass',(center,-9.715,1.82),(4.40,.055,2.30),glass,'Glass')
    for x in (side*1.18,side*5.72):cube('WindowJamb',(x,-9.77,1.80),(.12,.16,2.50),frame,'Frames')
    for z in (.60,3.0):cube('WindowRail',(center,-9.77,z),(4.66,.16,.12),frame,'Frames')
    for column in (-1,0,1):
        for rownum in range(2):
            x=center+column*1.04;z=1.20+rownum*.86
            cube('ListingCard',(x,-9.762,z),(.81,.025,.64),paper,'Facade')
            cube('ListingCardHeader',(x,-9.780,z+.20),(.69,.012,.10),sign,'Facade')
            for line in range(2):cube('ListingCardRule',(x,-9.781,z-.02-line*.13),(.55 if line==0 else .39,.013,.035),ink,'Facade')
cube('DoorTransom',(0,-9.715,2.96),(1.92,.055,.23),glass,'Glass')
# Low-wing consultation windows leave substantial calm wall surfaces.
for x in (-10.20,10.20):
    cube('WingFrontGlass',(x,-9.875,1.93),(2.64,.04,1.62),glass,'Glass')
    for dx in (-1.38,1.38):cube('WingFrontJamb',(x+dx,-9.90,1.93),(.12,.12,1.78),frame,'Frames')
    for z in (1.07,2.79):cube('WingFrontRail',(x,-9.90,z),(2.88,.12,.12),frame,'Frames')
for y in (-5.1,1.0,6.4):
    cube('SideBayGlass',(13.375,y,2.01),(.04,2.64,1.62),glass,'Glass')
    for dy in (-1.38,1.38):cube('SideBayJamb',(13.40,y+dy,2.01),(.12,.12,1.78),frame,'Frames')
    for z in (1.15,2.87):cube('SideBayRail',(13.40,y,z),(.12,2.88,.12),frame,'Frames')
for y in (2.8,7.2):
    cube('CourtyardGlass',(6.485,y,2.01),(.04,2.20,1.62),glass,'Glass')
    for dy in (-1.16,1.16):cube('CourtyardJamb',(6.45,y+dy,2.01),(.12,.12,1.78),frame,'Frames')
    for z in (1.15,2.87):cube('CourtyardRail',(6.45,y,z),(.12,2.44,.12),frame,'Frames')
# Exactly one continuous green front fascia and the original independent text mesh.
anchor=empty('Sign_Main_01',groups['Signage'],(0,-9.89,3.50));anchor['WidthMetres']=25.;anchor['HeightMetres']=.80
board=cube('Sign_Main_01_Board',(0,0,0),(25,.10,.80),sign,parent=anchor)
for z in (-.43,.43):cube('Sign_Main_01_Trim',(0,.015,z),(25.12,.14,.06),frame,parent=anchor)
for x in (-12.53,12.53):cube('Sign_Main_01_Trim',(x,.015,0),(.06,.14,.80),frame,parent=anchor)
textob.parent=anchor;textob.location=(0,-.061,0);textob.rotation_euler=(math.pi/2,0,0)
cube('Condenser',(10.7,7.5,4.36),(1.25,.64,.40),wall,'RoofEquipment')
cube('CondenserGrille',(10.7,7.164,4.36),(1.07,.035,.28),frame,'RoofEquipment')
for dx in (-.40,-.20,0,.20,.40):cube('CondenserSlat',(10.7+dx,7.138,4.36),(.045,.025,.24),trim,'RoofEquipment')
cube('RearDownpipe',(12.8,9.90,2.09),(.10,.11,3.90),frame,'RoofEquipment')
bpy.context.view_layer.update();meshes=[o for o in asset_objects(root) if o.type=='MESH'];limits=bounds(meshes)
info={'id':'RE_01','status':'Preview awaiting silhouette review','sourceBlend':'../Models/RE_01.blend','sourceSHA256':hashlib.sha256((SOURCE/'Models/RE_01.blend').read_bytes()).hexdigest(),'floorCount':1,'exportBounds':limits,'triangles':sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons),'meshObjects':len(meshes),'materials':material_specs(meshes),'parcelSize':[48,48],'roofHeights':[5.0,4.4],'doorOpening':[1.8,2.5],'listingCardSize':[.81,.64],'plan':'Central 13x9m; left 7x7m; right connected 7x20m; one building','signWidth':25,'signHeight':.8,'blend':'Models/RE_01_Preview.blend','preview':'Previews/RE_01.png','frontPreview':'Previews/RE_01_Front.png','parcelPreview':'Previews/RE_01_Parcel.png','humanPreview':'Previews/RE_01_HumanScale.png','topPreview':'Previews/RE_01_Top.png'}
(OUT/'preview_manifest.json').write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf8')
assert abs(limits['min'][2])<1e-6 and limits['max'][2]<=5.001
assert all(o.data.uv_layers for o in meshes)
assert all(not p.use_smooth for o in meshes for p in o.data.polygons)
scene=bpy.context.scene;scene.render.resolution_x=1200;scene.render.resolution_y=1000;scene.cycles.samples=32;camera=scene.camera
ground=bpy.data.objects.get('StudioGround')
if ground:ground.location.z=-.08
# Increase key-light coverage, not asset scale.
for ob in stage.objects:
    if ob.type=='LIGHT' and ob.data.type!='SUN':
        ob.location*=1.7;ob.data.energy*=2.89
        if ob.data.type=='AREA':ob.data.size*=1.7;ob.data.size_y*=1.7
def aim(location,target,ortho):
    camera.location=location;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=ortho;bpy.context.view_layer.update()
aim((35,-49,31),(0,-1,1.7),40)
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(OUT/info['blend']))
render(OUT/info['preview']);aim((0,-48,11),(0,-2,2),31);render(OUT/info['frontPreview'])
pad=mat('PresentationPad48','b8bcb6');line=mat('PresentationBorder48','63786d')
box('ParcelGuide_Pad',(0,6,-.025),(48,48,.04),pad,None,stage)
for y in (-18,30):box('ParcelGuide_Border',(0,y,.006),(48.06,.10,.012),line,None,stage)
for x in (-24,24):box('ParcelGuide_Border',(x,6,.006),(.10,48.06,.012),line,None,stage)
person=mat('PresentationPerson','a38564')
for name,loc,size in [('LegL',(-.115,-11.5,.45),(.14,.18,.90)),('LegR',(.115,-11.5,.45),(.14,.18,.90)),('Torso',(0,-11.5,1.18),(.42,.24,.60)),('Head',(0,-11.5,1.65),(.27,.25,.30)),('ArmL',(-.29,-11.5,1.17),(.12,.16,.60)),('ArmR',(.29,-11.5,1.17),(.12,.16,.60))]:box('ScalePerson_'+name,loc,size,person,None,stage)
aim((45,-55,46),(0,6,1.5),66);render(OUT/info['parcelPreview'])
aim((8,-23,7.8),(0,-9.4,1.8),14);render(OUT/info['humanPreview'])
camera.location=(0,6,80);camera.rotation_euler=(0,0,0);camera.data.ortho_scale=60;bpy.context.view_layer.update();render(OUT/info['topPreview'])
aim((45,-55,46),(0,6,1.5),66);bpy.ops.wm.save_as_mainfile(filepath=str(OUT/info['blend']))
print('SINGLE_STOREY_PREVIEW_COMPLETE',info['triangles'],limits,flush=True)
