"""Apply reviewed junction finish, validate, export and refresh presentation views."""
import bpy,sys,ast,json,copy,hashlib,math
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent;SOURCE=OUT.parent
LOW=SOURCE.parent/'CommercialBlocks/LowPoly_20261104'
for folder in ('Exports','Catalog','QA'):(OUT/folder).mkdir(exist_ok=True)
def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),str(path),'exec'),globals())
reuse(LOW/'KoreanPalette/recolor_existing.py',{'descendant','asset_objects'})
reuse(LOW/'KoreanPalette/SignageReady/retrofit_signage.py',{'bounds'})
reuse(LOW/'KoreanPalette/SignageReady/FrontOnly/build_front_only.py',{'export','render','material_specs'})
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Models/RE_01_Preview.blend'))
root=bpy.data.objects['RE_01'];stage=bpy.data.collections['Presentation_ONLY']
for ob in asset_objects(root):
    if ob.name.startswith('FrontPier_Main'):
        side=1 if ob.location.x>0 else -1;ob.location.x=side*6.08
        for v in ob.data.vertices:v.co.x=math.copysign(.42,v.co.x)
        ob.data.update()
bpy.context.view_layer.update();meshes=[o for o in asset_objects(root) if o.type=='MESH'];limits=bounds(meshes)
info=json.loads((OUT/'preview_manifest.json').read_text(encoding='utf8'))
old=json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['items'][0]
row={'id':'RE_01','assetRoot':'RE_01','revision':4,'variant':'SingleStoreyRevision_20261105','floorCount':1,'floorHeights':[4.64],'floorBaseHeight':0,'interiorFloorHeight':.14,'roofSlabHeight':4.64,'wingRoofSlabHeight':4.04,'roofHeights':[5.,4.4],'width':27.,'depth':20.,'entryClearWidth':1.8,'doorOpeningHeight':2.5,'styleLabel':'중앙 부동산 주동과 낮은 연결 업무동','paletteLabel':old['paletteLabel'],'triangles':sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons),'meshObjects':len(meshes),'exportBounds':limits,'materials':material_specs(meshes),'opaqueGlazing':True,'externalTextures':False,'frontSignCount':1,'attachedSideSigns':False,'attachedBlade':False,'groundDecorationPresent':False,'parcelSize':[48,48],'parcelTier':'Civic','maxBuildingSide':38,'setbacksAtRecommendedPlacement':{'left':10.5,'right':10.5,'front':8.,'back':20.},'recommendedPlacementOffsetBlender':[0,-6,0],'presentationParcelCenterBlender':[0,6,0],'presentationCharacterHeight':1.8,'humanScalePreserved':True,'plan':info['plan'],'blend':'Models/RE_01.blend','fbx':'Exports/RE_01.fbx','glb':'Exports/RE_01.glb','preview':info['preview'],'frontPreview':info['frontPreview'],'parcelPreview':info['parcelPreview'],'humanScalePreview':info['humanPreview'],'topPreview':info['topPreview'],'sourceApprovedBlend':'../Models/RE_01.blend','sourceDesign':'build_preview.py','finalizationScript':'finalize_office.py','validationReport':'QA/RE_01_Production.json','doorAndEntryAnchors':{n:list(bpy.data.objects[n].matrix_world.translation) for n in ('Door_Leaf_01','Door_Leaf_02','EntryAnchor','InteriorSpawnAnchor')}}
board=bpy.data.objects['Sign_Main_01_Board'];anchor=bpy.data.objects['Sign_Main_01']
row['signs']=[{'name':anchor.name,'board':board.name,'width':25.,'height':.80,'depth':.10,'position':list(anchor.matrix_world.translation),'rotationEulerDegrees':[0,0,0],'outwardNormal':[0,-1,0],'material':'RE_01_SignFace','boardBounds':bounds([board]),'uv':'Normalized 0..1','lettering':'Sign_Main_01_Lettering','letteringContent':'부동산','replacementGuidance':'Replace only board material and hide/remove separate lettering when needed. Keep anchor and panel dimensions.'}]
row['listingCards']=[{'name':o.name,'position':list(o.matrix_world.translation),'bounds':bounds([o])} for o in meshes if o.name=='ListingCard' or o.name.startswith('ListingCard.')];row['listingCardCount']=len(row['listingCards'])
row['doorLeafBounds']={n:bounds(asset_objects(bpy.data.objects[n])) for n in ('Door_Leaf_01','Door_Leaf_02')}
deps=bpy.context.evaluated_depsgraph_get();junction=[]
for side in (-1,1):
    for x in (6.43,6.46,6.49):
        for z in (.5,1.5,2.5):
            hit,loc,normal,index,obj,matrix=bpy.context.scene.ray_cast(deps,Vector((side*x,-10.5,z)),Vector((0,1,0)),distance=1)
            okay=hit and obj.name.startswith(('FrontPier_Main','FrontWingWall'))
            junction.append({'x':side*x,'z':z,'pass':okay,'hit':obj.name if hit else None})
assert all(c['pass'] for c in junction),junction
assert all(abs(limits['max'][a]-limits['min'][a]-expected)<1e-5 for a,expected in enumerate((27,20,5)))
assert all(o.data.uv_layers for o in meshes)
assert all(not p.use_smooth for o in meshes for p in o.data.polygons)
report={'id':'RE_01','pass':True,'sourceBlendSHA256':hashlib.sha256((SOURCE/'Models/RE_01.blend').read_bytes()).hexdigest(),'bounds':limits,'triangles':row['triangles'],'frontJunctionRays':junction,'humanScale':{'doorWidth':1.8,'doorHeight':2.5,'characterHeight':1.8},'plan':'Connected volumes with original-size door and listing cards; no uniform scale and no height squash','previewOnlyObjectsNotExported':True}
(OUT/row['validationReport']).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
manifest={'schemaVersion':1,'style':'Human-scale Korean low-poly single-storey connected real-estate office','items':[row],'accessories':[]}
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
catalog=copy.deepcopy(manifest)
for key in ('blend','fbx','glb','preview','frontPreview','parcelPreview','humanScalePreview','topPreview','sourceApprovedBlend','sourceDesign','finalizationScript','validationReport'):catalog['items'][0][key]='../'+row[key]
(OUT/'Catalog/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')
scene=bpy.context.scene;camera=scene.camera
def aim(location,target,ortho):
    camera.data.type='ORTHO';camera.location=location;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=ortho;bpy.context.view_layer.update()
aim((45,-55,46),(0,6,1.5),66);export(root,row)
for ob in stage.objects:
    if ob.name.startswith(('ParcelGuide_','ScalePerson_')):ob.hide_render=True
scene.render.resolution_x=1200;scene.render.resolution_y=1000
aim((35,-49,31),(0,-1,1.7),40);render(OUT/row['preview'])
# Perspective rays originate at eye height, avoiding below-ground orthographic rays.
camera.data.type='PERSP';camera.data.lens=72;camera.location=(0,-69.7,1.8)
camera.rotation_euler=(Vector((0,-9.7,2.2))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.resolution_x=1600;scene.render.resolution_y=500;bpy.context.view_layer.update();render(OUT/row['frontPreview'])
for ob in stage.objects:
    if ob.name.startswith(('ParcelGuide_','ScalePerson_')):ob.hide_render=False
scene.render.resolution_x=1200;scene.render.resolution_y=1000
aim((45,-55,46),(0,6,1.5),66);render(OUT/row['parcelPreview'])
aim((8,-23,7.8),(0,-9.4,1.8),14);render(OUT/row['humanScalePreview'])
camera.location=(0,6,80);camera.rotation_euler=(0,0,0);camera.data.ortho_scale=60;bpy.context.view_layer.update();render(OUT/row['topPreview'])
print('SINGLE_STOREY_FINAL_COMPLETE',row['triangles'],limits,flush=True)
