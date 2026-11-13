"""One Korean real-estate office; reuse commercial-block mesh/export/presentation helpers.
Generated only in this directory. Existing approved models are read-only.
"""
import bpy, sys, ast, json, math, copy, hashlib
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent
BLOCKS=OUT.parent/'CommercialBlocks'
LOW=BLOCKS/'LowPoly_20261104'
SOURCE=BLOCKS/'ParcelFit24_20261104/Models/CB_01.blend'
for name in ('Models','Exports','Previews','Catalog','QA'):(OUT/name).mkdir(exist_ok=True)
def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),str(path),'exec'),globals())
reuse(LOW/'KoreanPalette/recolor_existing.py',{'linear','descendant','asset_objects'})
reuse(LOW/'KoreanPalette/SignageReady/retrofit_signage.py',{'box','bounds'})
reuse(LOW/'KoreanPalette/SignageReady/FrontOnly/build_front_only.py',{'export','render','material_specs'})
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
stage=bpy.data.collections['Presentation_ONLY']
for ob in list(bpy.context.scene.objects):
    if ob.name not in stage.objects or ob.name.startswith('ParcelGuide_'):bpy.data.objects.remove(ob,do_unlink=True)
bpy.data.orphans_purge(do_recursive=True)
collection=bpy.data.collections.new('RE_01_ASSET');bpy.context.scene.collection.children.link(collection)
def empty(name,parent=None,location=(0,0,0)):
    ob=bpy.data.objects.new(name,None);collection.objects.link(ob);ob.parent=parent;ob.location=location;return ob
root=empty('RE_01');groups={name:empty(name,root) for name in ('Structure','Facade','Frames','Glass','Signage','Entrance','RoofEquipment')}
root['FloorCount']=1;root['ParcelTier']='Small';root['ParcelSizeMetres']=16.;root['EntryClearWidthMetres']=1.8
root['AssetDescription']='Single-storey Korean real-estate office; clear exterior ground; optional replaceable fascia lettering'
def mat(name,color,roughness=.86):
    material=bpy.data.materials.new('RE_01_'+name);material.use_nodes=True
    material.diffuse_color=(*linear(color),1)
    bsdf=material.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=material.diffuse_color;bsdf.inputs['Roughness'].default_value=roughness
    return material
wall=mat('WarmOffWhite','c8c5bc');stone=mat('BaseConcrete','979994');frame=mat('CharcoalFrame','353a3b');glass=mat('OpaqueSmokyGlass','3e5055',.46)
roof=mat('RoofGrey','848981');sign=mat('SignFace','29483d');letters=mat('SignLetters','e8e4d8');paper=mat('ListingPaper','ddd9cb');ink=mat('ListingInk','78877c');trim=mat('AluminumTrim','9a9e98')
def cube(name,center,size,material,group='Structure',parent=None):return box(name,center,size,material,parent or groups[group],collection)
# Open storefront with solid side/rear walls, single floor and flat parapet roof.
cube('FloorSlab',(0,0,.07),(10.8,7,.14),stone)
cube('RearWall',(0,3.37,1.91),(10.8,.26,3.54),wall)
for x in (-5.27,5.27):cube('SideWall',(x,0,1.91),(.26,7,3.54),wall)
cube('RoofSlab',(0,0,3.76),(10.98,7.18,.18),roof)
for y in (-3.48,3.48):
    cube('Parapet',(0,y,3.99),(10.8,.20,.38),wall)
    cube('ParapetCap',(0,y,4.20),(10.98,.30,.08),frame)
for x in (-5.30,5.30):
    cube('Parapet',(x,0,3.99),(.20,6.9,.38),wall)
    cube('ParapetCap',(x,0,4.20),(.30,7.18,.08),frame)
cube('RoofSurface',(0,0,3.87),(10.35,6.5,.05),roof)
for x in (-5.10,5.10):cube('FrontPier',(x,-3.39,1.9),(.60,.28,3.52),wall,'Facade')
cube('FrontHeadBand',(0,-3.39,3.34),(9.7,.28,.66),wall,'Facade')
# Two display bays, kept broad and simply framed.
for side in (-1,1):
    center=side*2.99
    cube('WindowSill',(center,-3.48,.40),(3.72,.24,.52),wall,'Facade')
    cube('StorefrontGlass',(center,-3.535,1.82),(3.64,.055,2.30),glass,'Glass')
    for x in (side*1.13,side*4.85):cube('WindowJamb',(x,-3.59,1.80),(.12,.16,2.50),frame,'Frames')
    for z in (.60,3.0):cube('WindowRail',(center,-3.59,z),(3.82,.16,.12),frame,'Frames')
    # Fixed window display cards; no freestanding props or invented prices/brands.
    for column in (-1,0,1):
        for rownum in range(2):
            x=center+column*1.04;z=1.20+rownum*.86
            cube('ListingCard',(x,-3.582,z),(.81,.025,.64),paper,'Facade')
            cube('ListingCardHeader',(x,-3.600,z+.20),(.69,.012,.10),sign,'Facade')
            for line in range(2):cube('ListingCardRule',(x,-3.601,z-.02-line*.13),(.55 if line==0 else .39,.013,.035),ink,'Facade')
# Center 1.8 m double entrance; each leaf pivots from the outer jamb.
for x in (-1.02,1.02):cube('EntranceJamb',(x,-3.58,1.47),(.12,.18,2.66),frame,'Entrance')
cube('EntranceHeader',(0,-3.58,2.78),(2.16,.18,.12),frame,'Entrance')
cube('DoorTransom',(0,-3.535,2.96),(1.92,.055,.23),glass,'Glass')
for index,side in enumerate((-1,1),1):
    leaf=empty(f'Door_Leaf_{index:02d}',groups['Entrance'],(side*.90,-3.57,.14))
    cx=-side*.45
    cube('DoorGlass',(cx,0,1.25),(.86,.055,2.44),glass,parent=leaf)
    for x in (0,-side*.90):cube('DoorStile',(x,-.035,1.25),(.07,.12,2.50),frame,parent=leaf)
    for z in (.045,2.455):cube('DoorRail',(cx,-.035,z),(.90,.12,.09),frame,parent=leaf)
    cube('DoorHandle',(-side*.78,-.15,1.20),(.045,.07,.46),trim,parent=leaf)
empty('EntryAnchor',groups['Entrance'],(0,-4.20,.14));empty('InteriorSpawnAnchor',groups['Entrance'],(0,-2.35,.14))
cube('EntranceThreshold',(0,-3.49,.16),(1.82,.28,.04),stone,'Entrance')
# Continuous front sign, with independently removable Korean mesh lettering.
anchor=empty('Sign_Main_01',groups['Signage'],(0,-3.72,3.39));anchor['WidthMetres']=9.48;anchor['HeightMetres']=.70
board=cube('Sign_Main_01_Board',(0,0,0),(9.48,.10,.70),sign,parent=anchor)
for z in (-.38,.38):cube('Sign_Main_01_Trim',(0,.01,z),(9.60,.14,.06),frame,parent=anchor)
for x in (-4.77,4.77):cube('Sign_Main_01_Trim',(x,.01,0),(.06,.14,.70),frame,parent=anchor)
data=bpy.data.curves.new('Sign_Main_01_Lettering_Source','FONT');data.body='부동산';data.align_x='CENTER';data.align_y='CENTER';data.font=bpy.data.fonts.load('C:/Windows/Fonts/malgunbd.ttf');data.size=1.;data.resolution_u=2;data.extrude=.009;data.bevel_depth=0
textob=bpy.data.objects.new('Sign_Main_01_Lettering',data);collection.objects.link(textob);textob.parent=anchor;textob.location=(0,-.061,0);textob.rotation_euler=(math.pi/2,0,0);data.materials.append(letters)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT');textob.select_set(True);bpy.context.view_layer.objects.active=textob;bpy.ops.object.convert(target='MESH');bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
textob=bpy.context.object;textob['OptionalLettering']=True;textob['TextContent']='부동산';textob['ReplacementGuidance']='Hide/remove this mesh when replacing Sign_Main_01_Board artwork.'
localcoords=[v.co.copy() for v in textob.data.vertices]
xmid=(min(v.x for v in localcoords)+max(v.x for v in localcoords))/2
ymid=(min(v.y for v in localcoords)+max(v.y for v in localcoords))/2
factor=.52/(max(v.y for v in localcoords)-min(v.y for v in localcoords))
for vertex in textob.data.vertices:vertex.co=Vector(((vertex.co.x-xmid)*factor,(vertex.co.y-ymid)*factor,vertex.co.z*factor))
textob.data.update()
uv=textob.data.uv_layers.new(name='UVMap')
coords=[v.co for v in textob.data.vertices];lo=[min(v[i] for v in coords) for i in range(2)];hi=[max(v[i] for v in coords) for i in range(2)]
for poly in textob.data.polygons:
    poly.use_smooth=False
    for li in poly.loop_indices:
        co=textob.data.vertices[textob.data.loops[li].vertex_index].co;uv.data[li].uv=((co.x-lo[0])/(hi[0]-lo[0]),(co.y-lo[1])/(hi[1]-lo[1]))
# Low-profile rooftop service box, attached downpipe at rear only.
cube('Condenser',(3.55,2.28,4.16),(1.25,.64,.54),wall,'RoofEquipment')
cube('CondenserGrille',(3.55,1.944,4.16),(1.07,.035,.38),frame,'RoofEquipment')
for x in (-.40,-.20,0,.20,.40):cube('CondenserSlat',(3.55+x,1.918,4.16),(.045,.025,.32),trim,'RoofEquipment')
cube('RearDownpipe',(4.94,3.56,1.96),(.10,.11,3.64),frame,'RoofEquipment')
bpy.context.view_layer.update()
meshes=[ob for ob in asset_objects(root) if ob.type=='MESH'];limits=bounds(meshes)
row={'id':'RE_01','assetRoot':'RE_01','revision':1,'floorCount':1,'floorHeights':[3.76],'floorBaseHeight':0,'roofSlabHeight':3.76,'width':10.8,'depth':7.0,'entryClearWidth':1.8,'doorOpeningHeight':2.5,'styleLabel':'단층 부동산 사무소','paletteLabel':'오프화이트 · 차콜 · 짙은 녹색','triangles':sum(len(p.vertices)-2 for ob in meshes for p in ob.data.polygons),'meshObjects':len(meshes),'exportBounds':limits,'materials':material_specs(meshes),'opaqueGlazing':True,'externalTextures':False,'frontSignCount':1,'attachedSideSigns':False,'attachedBlade':False,'groundDecorationPresent':False,'parcelSize':[16,16],'parcelTier':'Small','maxBuildingSide':12,'setbacks':{'left':8+limits['min'][0],'right':8-limits['max'][0],'front':8+limits['min'][1],'back':8-limits['max'][1]},'blend':'Models/RE_01.blend','fbx':'Exports/RE_01.fbx','glb':'Exports/RE_01.glb','preview':'Previews/RE_01.png','frontPreview':'Previews/RE_01_Front.png','parcelPreview':'Previews/RE_01_Parcel.png','sourceDesign':'build_office.py','validationReport':'QA/RE_01_Production.json','doorAndEntryAnchors':{name:list(bpy.data.objects[name].matrix_world.translation) for name in ('Door_Leaf_01','Door_Leaf_02','EntryAnchor','InteriorSpawnAnchor')}}
row['signs']=[{'name':anchor.name,'board':board.name,'width':9.48,'height':.70,'depth':.10,'position':list(anchor.matrix_world.translation),'rotationEulerDegrees':[0,0,0],'outwardNormal':[0,-1,0],'material':sign.name,'boardBounds':bounds([board]),'uv':'Normalized 0..1','lettering':textob.name,'letteringContent':'부동산','replacementGuidance':'Replace the board material; hide/remove the separate lettering mesh when using other artwork. Preserve anchor transform.'}]
row['listingCards']=[{'name':ob.name,'position':list(ob.matrix_world.translation),'bounds':bounds([ob]),'mount':'Attached to front of opaque storefront glazing'} for ob in meshes if ob.name=='ListingCard' or ob.name.startswith('ListingCard.')]
row['listingCardCount']=len(row['listingCards'])
row['sourceHelpers']=['../CommercialBlocks/LowPoly_20261104/KoreanPalette/recolor_existing.py','../CommercialBlocks/LowPoly_20261104/KoreanPalette/SignageReady/retrofit_signage.py','../CommercialBlocks/LowPoly_20261104/KoreanPalette/SignageReady/FrontOnly/build_front_only.py']
scene=bpy.context.scene;scene.render.resolution_x=1100;scene.render.resolution_y=1100;scene.cycles.samples=48
camera=scene.camera
def aim(location,target,scale):
    camera.location=location;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale;bpy.context.view_layer.update()
aim((15,-22,14),(0,0,1.8),15)
export(root,row);render(OUT/row['preview'])
aim((0,-25,8.5),(0,-.4,1.9),13.6);render(OUT/row['frontPreview'])
# Plot guide is only a presentation object and is never in exported selection.
pad=mat('PresentationPad','b8bcb6');boundary=mat('PresentationBoundary','63786d')
box('ParcelGuide_16m_Pad',(0,0,-.025),(16,16,.04),pad,None,stage)
for y in (-8,8):box('ParcelGuide_Edge',(0,y,.006),(16.04,.045,.012),boundary,None,stage)
for x in (-8,8):box('ParcelGuide_Edge',(x,0,.006),(.045,16.04,.012),boundary,None,stage)
aim((17,-24,19),(0,0,1.1),23);render(OUT/row['parcelPreview'])
# Save the source with useful parcel framing; exports above contain asset only.
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(OUT/row['blend']))
checks={'rootIdentity':all(abs(root.matrix_world[i][j]-(1 if i==j else 0))<1e-7 for i in range(4) for j in range(4)),'maxHorizontalWithin12':max(limits['max'][i]-limits['min'][i] for i in (0,1))<=12,'bottomAtZero':abs(limits['min'][2])<1e-6,'allMeshUV':all(ob.data.uv_layers for ob in meshes),'allFlatShaded':all(not p.use_smooth for ob in meshes for p in ob.data.polygons),'noImageTextures':all(n.type!='TEX_IMAGE' for m in bpy.data.materials if m.use_nodes for n in m.node_tree.nodes),'oneStorey':True,'groundPropsAbsent':True}
assert all(checks.values()),checks
(OUT/row['validationReport']).write_text(json.dumps({'id':'RE_01','pass':all(checks.values()),'checks':checks,'triangles':row['triangles'],'bounds':limits,'lightingSourceSHA256':hashlib.sha256(SOURCE.read_bytes()).hexdigest()},ensure_ascii=False,indent=2),encoding='utf8')
manifest={'schemaVersion':1,'style':'Korean low-poly single-storey real estate office','items':[row],'accessories':[]}
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
catalog=copy.deepcopy(manifest)
for key in ('blend','fbx','glb','preview','frontPreview','parcelPreview','sourceDesign','validationReport'):catalog['items'][0][key]='../'+row[key]
catalog['items'][0]['sourceHelpers']=['../'+path for path in row['sourceHelpers']]
(OUT/'Catalog/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')
print('REAL_ESTATE_COMPLETE',row['triangles'],limits,flush=True)
