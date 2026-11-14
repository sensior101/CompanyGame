"""Remodel RE_01 as one 38 x 38 m single-storey building for the actual Civic parcel.
Keeps human-size original entrance components, materials and lettering meshes.
Reuses audited commercial-block box/UV, export and render functions.
"""
import bpy,sys,ast,json,copy,math,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent;SOURCE=OUT.parent
BLOCKS=SOURCE.parent/'CommercialBlocks';LOW=BLOCKS/'LowPoly_20261104'
for folder in ('Models','Exports','Previews','Catalog','QA'):(OUT/folder).mkdir(parents=True,exist_ok=True)
def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),str(path),'exec'),globals())
reuse(LOW/'KoreanPalette/recolor_existing.py',{'linear','descendant','asset_objects'})
reuse(LOW/'KoreanPalette/SignageReady/retrofit_signage.py',{'box','bounds'})
reuse(LOW/'KoreanPalette/SignageReady/FrontOnly/build_front_only.py',{'export','render','material_specs'})
reuse(SOURCE/'build_office.py',{'empty','mat','cube'})
bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'Models/RE_01.blend'));bpy.context.view_layer.update()
stage=bpy.data.collections['Presentation_ONLY'];entrance=bpy.data.objects['Entrance'];textob=bpy.data.objects['Sign_Main_01_Lettering']
keep=set(asset_objects(entrance)+[textob]);keep.update(o for o in stage.objects if not o.name.startswith('ParcelGuide_'))
for ob in (entrance,textob):
    world=ob.matrix_world.copy();ob.parent=None;ob.matrix_world=world
for ob in list(bpy.context.scene.objects):
    if ob not in keep:bpy.data.objects.remove(ob,do_unlink=True)
collection=bpy.data.collections.new('RE_01_Parcel48_ASSET');bpy.context.scene.collection.children.link(collection)
for ob in list(keep):
    if ob.name not in stage.objects:
        for c in list(ob.users_collection):c.objects.unlink(ob)
        collection.objects.link(ob)
root=empty('RE_01');groups={name:empty(name,root) for name in ('Structure','Facade','Frames','Glass','Signage','RoofEquipment')}
groups['Entrance']=entrance;entrance.parent=root;entrance.location.y=-15.24
root['FloorCount']=1;root['ParcelTier']='Civic';root['ParcelSizeMetres']=48.;root['EntryClearWidthMetres']=1.8;root['BuildAreaMetres']='38 x 38';root['Variant']='Parcel48Revision'
wall=bpy.data.materials['RE_01_WarmOffWhite'];stone=bpy.data.materials['RE_01_BaseConcrete'];frame=bpy.data.materials['RE_01_CharcoalFrame'];glass=bpy.data.materials['RE_01_OpaqueSmokyGlass'];roof=bpy.data.materials['RE_01_RoofGrey'];sign=bpy.data.materials['RE_01_SignFace'];paper=bpy.data.materials['RE_01_ListingPaper'];ink=bpy.data.materials['RE_01_ListingInk'];trim=bpy.data.materials['RE_01_AluminumTrim']
# Both dimensions are authored independently. No uniform enlargement, and no Z scale.
cube('FloorSlab',(0,0,.07),(37.6,37.6,.14),stone)
cube('RearWall',(0,18.67,2.22),(37.6,.26,4.16),wall)
for x in (-18.67,18.67):cube('SideWall',(x,0,2.22),(.26,37.6,4.16),wall)
cube('RoofSlab',(0,0,4.40),(38,38,.20),roof)
cube('RoofSurface',(0,0,4.51),(37.28,37.28,.04),roof)
for y in (-18.80,18.80):
    cube('Parapet',(0,y,4.65),(37.8,.22,.30),wall)
    cube('ParapetCap',(0,y,4.82),(38,.40,.08),frame)
for x in (-18.80,18.80):
    cube('Parapet',(x,0,4.65),(.22,37.4,.30),wall)
    cube('ParapetCap',(x,0,4.82),(.40,38,.08),frame)
cube('FrontHeadBand',(0,-18.67,3.78),(37.6,.26,1.04),wall,'Facade')
for x in (-18.45,18.45):cube('FrontCornerPier',(x,-18.67,1.70),(.70,.26,3.12),wall,'Facade')
for x in (-1.75,1.75):cube('EntranceFlankPier',(x,-18.67,1.70),(1.28,.26,3.12),wall,'Facade')
cube('DoorTransom',(0,-18.835,2.96),(1.92,.055,.23),glass,'Glass')
cube('EntranceUpperPanel',(0,-18.67,3.25),(2.20,.26,.20),wall,'Facade')
# Eight human-scale display bays; piers separate bays rather than stretching a shop window.
display_centers=[]
for side in (-1,1):
    for index,offset in enumerate((4.15,8.25,12.35,16.45)):
        x=side*offset;display_centers.append(x)
        cube('DisplayBaySill',(x,-18.70,.40),(3.40,.20,.52),wall,'Facade')
        cube('StorefrontGlass',(x,-18.835,1.94),(3.32,.055,2.55),glass,'Glass')
        for dx in (-1.72,1.72):cube('DisplayBayJamb',(x+dx,-18.88,1.94),(.12,.15,2.67),frame,'Frames')
        for z in (.62,3.23):cube('DisplayBayRail',(x,-18.88,z),(3.56,.15,.12),frame,'Frames')
        if index<3:
            pierx=side*(offset+2.05);cube('FrontRhythmPier',(pierx,-18.67,1.70),(.54,.26,3.12),wall,'Facade')
        if index<2:
            for col in (-1,0,1):
                for rownum in range(2):
                    cx=x+col*1.04;z=1.20+rownum*.86
                    cube('ListingCard',(cx,-18.882,z),(.81,.025,.64),paper,'Facade')
                    cube('ListingCardHeader',(cx,-18.900,z+.20),(.69,.012,.10),sign,'Facade')
                    for line in range(2):cube('ListingCardRule',(cx,-18.901,z-.02-line*.13),(.55 if line==0 else .39,.013,.035),ink,'Facade')
        else:
            cube('FrostedWindowBand',(x,-18.870,1.12),(3.17,.025,.28),stone,'Facade')
# Repeat slender side windows at fixed height and scale along the longer side walls.
for side in (-1,1):
    for y in (-15.75,-11.25,-6.75,-2.25,2.25,6.75,11.25,15.75):
        cube('SideBayGlass',(side*18.828,y,1.98),(.04,2.88,2.12),glass,'Glass')
        for dy in (-1.50,1.50):cube('SideBayJamb',(side*18.865,y+dy,1.98),(.14,.12,2.30),frame,'Frames')
        for z in (.87,3.09):cube('SideBayRail',(side*18.865,y,z),(.14,3.12,.12),frame,'Frames')
        cube('SideBaySill',(side*18.865,y,.77),(.20,3.20,.10),stone,'Facade')
# Long uninterrupted fascia; original optional lettering remains separate and human-readable.
anchor=empty('Sign_Main_01',groups['Signage'],(0,-18.89,3.81));anchor['WidthMetres']=36.8;anchor['HeightMetres']=.86
board=cube('Sign_Main_01_Board',(0,0,0),(36.8,.10,.86),sign,parent=anchor)
for z in (-.46,.46):cube('Sign_Main_01_Trim',(0,.015,z),(36.92,.14,.06),frame,parent=anchor)
for x in (-18.43,18.43):cube('Sign_Main_01_Trim',(x,.015,0),(.06,.14,.86),frame,parent=anchor)
textob.parent=anchor;textob.location=(0,-.061,0);textob.rotation_euler=(math.pi/2,0,0)
for v in textob.data.vertices:v.co*=.70/.52
textob.data.update()
for x in (-12.5,12.5):
    cube('Condenser',(x,12,4.68),(1.25,.64,.40),wall,'RoofEquipment')
    cube('CondenserGrille',(x,11.664,4.68),(1.07,.035,.28),frame,'RoofEquipment')
    for dx in (-.40,-.20,0,.20,.40):cube('CondenserSlat',(x+dx,11.638,4.68),(.045,.025,.24),trim,'RoofEquipment')
cube('RearDownpipe',(17.8,18.86,2.23),(.10,.11,4.18),frame,'RoofEquipment')
bpy.context.view_layer.update();meshes=[ob for ob in asset_objects(root) if ob.type=='MESH'];limits=bounds(meshes)
row={'id':'RE_01','assetRoot':'RE_01','revision':2,'variant':'Parcel48Revision','floorCount':1,'floorHeights':[4.4],'floorBaseHeight':0,'roofSlabHeight':4.4,'width':37.6,'depth':37.6,'targetBuildArea':[38,38],'entryClearWidth':1.8,'doorOpeningHeight':2.5,'styleLabel':'Civic 부지용 단층 부동산','paletteLabel':'오프화이트 · 차콜 · 짙은 녹색','triangles':sum(len(p.vertices)-2 for ob in meshes for p in ob.data.polygons),'meshObjects':len(meshes),'exportBounds':limits,'materials':material_specs(meshes),'opaqueGlazing':True,'externalTextures':False,'frontSignCount':1,'attachedSideSigns':False,'attachedBlade':False,'groundDecorationPresent':False,'parcelSize':[48,48],'parcelTier':'Civic','maxBuildingSide':38,'setbacks':{'left':5.,'right':5.,'front':5.,'back':5.},'occupancy':{'width':38/48,'depth':38/48,'boundingArea':38*38/(48*48)},'humanScale':{'doorWidth':1.8,'doorHeight':2.5,'listingCardSize':[.81,.64],'presentationPersonHeight':1.75},'facadeBays':{'front':8,'sideEach':8},'blend':'Models/RE_01.blend','fbx':'Exports/RE_01.fbx','glb':'Exports/RE_01.glb','preview':'Previews/RE_01.png','frontPreview':'Previews/RE_01_Front.png','parcelPreview':'Previews/RE_01_Parcel48.png','topPreview':'Previews/RE_01_Top48.png','humanScalePreview':'Previews/RE_01_HumanScale.png','sourceApprovedBlend':'../Models/RE_01.blend','sourceDesign':'build_parcel48.py','validationReport':'QA/RE_01_Production.json','doorAndEntryAnchors':{name:list(bpy.data.objects[name].matrix_world.translation) for name in ('Door_Leaf_01','Door_Leaf_02','EntryAnchor','InteriorSpawnAnchor')}}
row['signs']=[{'name':anchor.name,'board':board.name,'width':36.8,'height':.86,'depth':.10,'position':list(anchor.matrix_world.translation),'material':sign.name,'boardBounds':bounds([board]),'uv':'Normalized 0..1','lettering':textob.name,'letteringContent':'부동산','replacementGuidance':'Replace the continuous board material and hide/remove the separate lettering mesh; keep anchor transform.'}]
row['listingCards']=[{'name':ob.name,'position':list(ob.matrix_world.translation),'bounds':bounds([ob])} for ob in meshes if ob.name=='ListingCard' or ob.name.startswith('ListingCard.')];row['listingCardCount']=len(row['listingCards'])
assert all(abs(limits['max'][axis]-limits['min'][axis]-38)<1e-5 for axis in (0,1)),limits
assert limits['min'][2]==0
def write_metadata():
    manifest={'schemaVersion':1,'style':'Actual Civic 48m parcel / 38 x 38m authored plan, human-sized doors','items':[row],'accessories':[]}
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    catalog=copy.deepcopy(manifest)
    for key in ('blend','fbx','glb','preview','frontPreview','parcelPreview','topPreview','humanScalePreview','sourceApprovedBlend','sourceDesign','validationReport'):catalog['items'][0][key]='../'+row[key]
    (OUT/'Catalog/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')
write_metadata()
scene=bpy.context.scene;scene.render.resolution_x=1400;scene.render.resolution_y=1100;scene.cycles.samples=32;camera=scene.camera
ground=bpy.data.objects.get('StudioGround')
if ground:ground.location.z=-.055
def aim(location,target,scale):
    camera.location=location;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale;bpy.context.view_layer.update()
aim((45,-60,39),(0,0,1.7),58)
export(root,row)
(OUT/row['validationReport']).write_text(json.dumps({'id':'RE_01','pass':True,'actualOuterXY':[38,38],'doorWidth':1.8,'doorHeight':2.5,'sourceSHA256':hashlib.sha256((SOURCE/'Models/RE_01.blend').read_bytes()).hexdigest(),'designChange':'New repeated facade bays and 38m square plan; entrance copied at original human scale; no uniform scaling.'},indent=2),encoding='utf8')
render(OUT/row['preview']);aim((0,-60,17),(0,-1,2),43);render(OUT/row['frontPreview'])
# Actual parcel border and human figure are presentation-only, excluded from all exports.
pad=mat('PresentationParcel48','b8bcb6');boundary=mat('PresentationBoundary48','63786d')
box('ParcelGuide_48m_Pad',(0,0,-.025),(48,48,.04),pad,None,stage)
for y in (-24,24):box('ParcelGuide_Edge',(0,y,.006),(48.04,.06,.012),boundary,None,stage)
for x in (-24,24):box('ParcelGuide_Edge',(x,0,.006),(.06,48.04,.012),boundary,None,stage)
person=mat('PresentationPerson','a38564')
for name,loc,size in [('LegL',(-.115,-20.8,.44),(.14,.18,.88)),('LegR',(.115,-20.8,.44),(.14,.18,.88)),('Torso',(0,-20.8,1.15),(.42,.24,.58)),('Head',(0,-20.8,1.605),(.27,.25,.29)),('ArmL',(-.29,-20.8,1.13),(.12,.16,.58)),('ArmR',(.29,-20.8,1.13),(.12,.16,.58))]:box('ScalePerson_'+name,loc,size,person,None,stage)
aim((49,-66,48),(0,0,1),73);render(OUT/row['parcelPreview'])
aim((0,0,80),(0,0,0),54);camera.rotation_euler=(0,0,0);render(OUT/row['topPreview'])
aim((8,-31,8),(0,-18.6,1.75),14);render(OUT/row['humanScalePreview'])
aim((49,-66,48),(0,0,1),73);bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(OUT/row['blend']))
print('RE_PARCEL48_COMPLETE',row['triangles'],limits,flush=True)
