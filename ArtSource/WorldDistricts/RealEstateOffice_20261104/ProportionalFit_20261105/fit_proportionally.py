"""Uniformly fit the ORIGINAL RE_01 design to the actual 48 m Civic parcel.
Longest horizontal side becomes 38 m; XYZ, doors, signage and anchors share
exactly the same scale. The rejected square-plan revision is not used.
"""
import bpy,json,sys,ast,copy,hashlib,math,os
from pathlib import Path
from mathutils import Vector,Matrix
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent;SOURCE=OUT.parent
BLOCKS=SOURCE.parent/'CommercialBlocks';LOW=BLOCKS/'LowPoly_20261104'
for folder in ('Models','Exports','Previews','Catalog','QA'):(OUT/folder).mkdir(parents=True,exist_ok=True)
def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),str(path),'exec'),globals())
reuse(LOW/'KoreanPalette/recolor_existing.py',{'descendant','asset_objects'})
reuse(LOW/'KoreanPalette/SignageReady/retrofit_signage.py',{'box','bounds'})
reuse(LOW/'KoreanPalette/SignageReady/FrontOnly/build_front_only.py',{'material_specs','export','render'})
reuse(BLOCKS/'ParcelFit24_20261104/fit_parcels.py',{'mesh_signature','snapshot','verify','guide_material'})
old=json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['items'][0]
source=SOURCE/old['blend'];source_sha=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source));bpy.context.view_layer.update()
root=bpy.data.objects['RE_01'];objects=asset_objects(root);meshes=[o for o in objects if o.type=='MESH']
source_bounds=bounds(meshes);pivot=Vector(((source_bounds['min'][0]+source_bounds['max'][0])/2,(source_bounds['min'][1]+source_bounds['max'][1])/2,source_bounds['min'][2]))
scale=38/max(source_bounds['max'][a]-source_bounds['min'][a] for a in (0,1));before=snapshot(root)
processed=set()
for ob in meshes:
    if ob.data.as_pointer() in processed:continue
    processed.add(ob.data.as_pointer())
    for vertex in ob.data.vertices:vertex.co*=scale
    ob.data.update()
def depth(ob):
    count=0
    while ob.parent:count+=1;ob=ob.parent
    return count
for ob in sorted(objects,key=depth):
    if ob==root:ob.matrix_world=Matrix.Identity(4)
    else:
        matrix=before[ob.name]['world'].copy();matrix.translation=scale*(matrix.translation-pivot);ob.matrix_world=matrix
    for key in ('WidthMetres','HeightMetres','EntryClearWidthMetres'):
        if key in ob:ob[key]=float(ob[key])*scale
root['ParcelTier']='Civic';root['ParcelSizeMetres']=48.;root['MaxHorizontalExtentMetres']=38.;root['BakedUniformScale']=scale;root['Variant']='ProportionalFit_20261105'
root['AssetDescription']='Original single-storey RE_01 design uniformly enlarged; all XYZ proportions preserved'
bpy.context.view_layer.update();checks=verify(root,before,scale,pivot);limits=bounds(meshes)
assert abs(max(limits['max'][a]-limits['min'][a] for a in (0,1))-38)<.00003
assert abs(limits['min'][2])<.00003
for a in (0,1):assert abs(limits['min'][a]+limits['max'][a])<.00003
row=copy.deepcopy(old)
row.update(revision=3,variant='ProportionalFit_20261105',width=old['width']*scale,depth=old['depth']*scale,
    floorHeights=[h*scale for h in old['floorHeights']],floorBaseHeight=-pivot.z*scale,
    roofSlabHeight=(old['roofSlabHeight']-pivot.z)*scale,entryClearWidth=old['entryClearWidth']*scale,
    doorOpeningHeight=old['doorOpeningHeight']*scale,exportBounds=limits,materials=material_specs(meshes),
    parcelTier='Civic',parcelSize=[48,48],maxBuildingSide=38,fitScale=scale,scaleXYZ=[scale]*3,
    sourceExportBounds=source_bounds,sourcePivotBottomCenter=list(pivot),sourceFloorHeights=old['floorHeights'],
    sourceApprovedBlend='../'+old['blend'],sourceBlendSHA256=source_sha,sourceDesign='fit_proportionally.py',
    rootTransform='Identity; total renderer bounds bottom-center pivot',bakedUniformXYZ=True,
    geometryRule='p_new = scale * (p_source - source_bounds_bottom_center); no shape exceptions',
    originalDesignProportionsPreserved=True,topologyUVMaterialsPreserved=True,
    setbacks={'left':24+limits['min'][0],'right':24-limits['max'][0],'front':24+limits['min'][1],'back':24-limits['max'][1]},
    blend='Models/RE_01.blend',fbx='Exports/RE_01.fbx',glb='Exports/RE_01.glb',preview='Previews/RE_01.png',
    frontPreview='Previews/RE_01_Front.png',parcelPreview='Previews/RE_01_Parcel48.png',topPreview='Previews/RE_01_Top48.png',
    validationReport='QA/RE_01_ProportionalFit.json',parcelFitReport='QA/RE_01_ProportionalFit.json')
row['doorAndEntryAnchors']={name:list(bpy.data.objects[name].matrix_world.translation) for name in old['doorAndEntryAnchors']}
row['doorLeafBounds']={name:bounds(asset_objects(bpy.data.objects[name])) for name in ('Door_Leaf_01','Door_Leaf_02')}
for spec in row['signs']:
    for key in ('width','height','depth'):spec[key]*=scale
    spec['position']=list(bpy.data.objects[spec['name']].matrix_world.translation)
    spec['boardBounds']=bounds([bpy.data.objects[spec['board']]])
for card in row['listingCards']:
    card['position']=list(bpy.data.objects[card['name']].matrix_world.translation);card['bounds']=bounds([bpy.data.objects[card['name']]])
row['sourceHelpers']=[os.path.relpath(BLOCKS/'ParcelFit24_20261104/fit_parcels.py',OUT).replace('\\','/')]+['../'+p for p in old.get('sourceHelpers',[])]
# Presentation lights follow the same enlargement so the material appearance remains comparable.
stage=bpy.data.collections['Presentation_ONLY']
for ob in list(stage.objects):
    if ob.name.startswith('ParcelGuide_'):bpy.data.objects.remove(ob,do_unlink=True)
    elif ob.type=='LIGHT' and ob.data.type!='SUN':
        ob.location=scale*(ob.location-pivot);ob.data.energy*=scale*scale
        if ob.data.type=='AREA':ob.data.size*=scale;ob.data.size_y*=scale
ground=bpy.data.objects.get('StudioGround')
if ground:ground.location.z=-.08
scene=bpy.context.scene;scene.render.resolution_x=1200;scene.render.resolution_y=1200;scene.cycles.samples=40;camera=scene.camera
def aim(location,target,ortho):
    camera.location=location;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=ortho;bpy.context.view_layer.update()
aim((52,-76,48),(0,0,6.3),53)
export(root,row)
bpy.ops.wm.open_mainfile(filepath=str(OUT/row['blend']));bpy.context.view_layer.update();root=bpy.data.objects['RE_01'];checks=verify(root,before,scale,pivot)
camera=bpy.context.scene.camera;scene=bpy.context.scene;stage=bpy.data.collections['Presentation_ONLY']
report={'id':'RE_01','pass':all(c['pass'] for c in checks),'sourceBlendSHA256':source_sha,'sourceBlend':'../'+old['blend'],
    'operation':row['geometryRule'],'uniformScaleXYZ':[scale]*3,'sourceBounds':source_bounds,'resultBounds':limits,
    'sourcePivotBottomCenter':list(pivot),'objectChecks':checks,'triangles':row['triangles'],'plotGuidesPresentationOnly':True}
(OUT/row['validationReport']).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
def save_metadata():
    data={'schemaVersion':1,'style':'Original real-estate design proportionally fitted to 48 m Civic parcel','items':[row],'accessories':[]}
    (OUT/'manifest.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf8')
    catalog=copy.deepcopy(data)
    for key in ('blend','fbx','glb','preview','frontPreview','parcelPreview','topPreview','sourceApprovedBlend','sourceDesign','validationReport','parcelFitReport'):catalog['items'][0][key]='../'+row[key]
    catalog['items'][0]['sourceHelpers']=['../'+p for p in row['sourceHelpers']]
    (OUT/'Catalog/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')
save_metadata();render(OUT/row['preview'])
aim((0,-87,29),(0,-1.4,6.6),47);render(OUT/row['frontPreview'])
pad=guide_material('Presentation_Parcel48Pad',(.46,.49,.46));line=guide_material('Presentation_Parcel48Line',(.11,.23,.18))
box('ParcelGuide_48m_Pad',(0,0,-.025),(48,48,.04),pad,None,stage)
for y in (-24,24):box('ParcelGuide_Edge',(0,y,.006),(48.08,.10,.012),line,None,stage)
for x in (-24,24):box('ParcelGuide_Edge',(x,0,.006),(.10,48.08,.012),line,None,stage)
aim((56,-78,63),(0,0,5.3),71);render(OUT/row['parcelPreview'])
camera.location=(0,0,90);camera.rotation_euler=(0,0,0);camera.data.ortho_scale=54;bpy.context.view_layer.update();render(OUT/row['topPreview'])
aim((56,-78,63),(0,0,5.3),71);bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(OUT/row['blend']))
bpy.ops.wm.open_mainfile(filepath=str(OUT/row['blend']));bpy.context.view_layer.update();verify(bpy.data.objects['RE_01'],before,scale,pivot)
assert hashlib.sha256(source.read_bytes()).hexdigest()==source_sha
print('PROPORTIONAL_FIT_COMPLETE',scale,limits,flush=True)
