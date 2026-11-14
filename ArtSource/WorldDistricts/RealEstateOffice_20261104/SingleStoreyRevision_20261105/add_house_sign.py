"""Add an integrated shallow gable to the existing continuous front sign only."""
import bpy,sys,ast,json,copy,shutil,hashlib,math
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent;SOURCE=OUT.parent
LOW=SOURCE.parent/'CommercialBlocks/LowPoly_20261104'
BACKUP=OUT/'History/BeforeHouseSign_20261105'
def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),str(path),'exec'),globals())
reuse(LOW/'KoreanPalette/recolor_existing.py',{'descendant','asset_objects','fingerprint'})
reuse(LOW/'KoreanPalette/SignageReady/retrofit_signage.py',{'bounds'})
reuse(LOW/'KoreanPalette/SignageReady/FrontOnly/build_front_only.py',{'export','render','material_specs'})
for folder in ('Models','Exports','Previews','Catalog','QA'):
    for source in (OUT/folder).glob('*'):
        if source.is_file():
            dest=BACKUP/folder/source.name;dest.parent.mkdir(parents=True,exist_ok=True)
            if not dest.exists():shutil.copyfile(source,dest)
if not (BACKUP/'manifest.json').exists():shutil.copyfile(OUT/'manifest.json',BACKUP/'manifest.json')
bpy.ops.wm.open_mainfile(filepath=str(BACKUP/'Models/RE_01.blend'));bpy.context.view_layer.update()
root=bpy.data.objects['RE_01'];before=fingerprint(root)
board=bpy.data.objects['Sign_Main_01_Board'];anchor=bpy.data.objects['Sign_Main_01']
top=next(o for o in asset_objects(anchor) if o.type=='MESH' and o.name.startswith('Sign_Main_01_Trim') and o.location.z>.4)
def extrude_profile(ob,profile,depth):
    old=ob.data;count=len(profile)
    verts=[(x,y,z) for y in (-depth/2,depth/2) for x,z in profile]
    faces=[tuple(range(count)),tuple(range(2*count-1,count-1,-1))]
    faces.extend((i,i+count,(i+1)%count+count,(i+1)%count) for i in range(count))
    mesh=bpy.data.meshes.new(old.name+'_HouseOutline');mesh.from_pydata(verts,[],faces);mesh.update()
    for material in old.materials:mesh.materials.append(material)
    uv=mesh.uv_layers.new(name='UVMap');xmin=min(x for x,z in profile);xmax=max(x for x,z in profile);zmin=min(z for x,z in profile);zmax=max(z for x,z in profile)
    for poly in mesh.polygons:
        poly.use_smooth=False
        for loop in poly.loop_indices:
            v=mesh.vertices[mesh.loops[loop].vertex_index].co
            if poly.index<2:
                u=(v.x-xmin)/(xmax-xmin);uv.data[loop].uv=((1-u) if poly.index==1 else u,(v.z-zmin)/(zmax-zmin))
            else:
                corner=list(poly.loop_indices).index(loop);uv.data[loop].uv=((0,0),(1,0),(1,1),(0,1))[corner]
    ob.data=mesh
extrude_profile(board,[(-12.5,-.4),(12.5,-.4),(12.5,.4),(1.55,.4),(0,.9),(-1.55,.4),(-12.5,.4)],.10)
# Continuous mitered trim follows the horizontal sides and the two roof slopes.
chain=[Vector(p) for p in ((-12.56,.4),(-1.55,.4),(0,.9),(1.55,.4),(12.56,.4))]
normals=[]
for a,b in zip(chain,chain[1:]):
    direction=(b-a).normalized();normals.append(Vector((-direction.y,direction.x)))
upper=[]
for index,p in enumerate(chain):
    if index==0:offset=normals[0]*.06
    elif index==len(chain)-1:offset=normals[-1]*.06
    else:offset=(normals[index-1]+normals[index])*(.06/(1+normals[index-1].dot(normals[index])))
    upper.append(p+offset)
profile=[(p.x-top.location.x,p.y-top.location.z) for p in chain+list(reversed(upper))]
extrude_profile(top,profile,.14)
bpy.context.view_layer.update();after=fingerprint(root);changed=[name for name in before if before[name]!=after[name]]
assert set(changed)=={board.name,top.name},changed
meshes=[o for o in asset_objects(root) if o.type=='MESH'];limits=bounds(meshes)
assert limits==json.loads((BACKUP/'manifest.json').read_text(encoding='utf8'))['items'][0]['exportBounds']
manifest=json.loads((BACKUP/'manifest.json').read_text(encoding='utf8'));row=manifest['items'][0]
row.update(revision=5,triangles=sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons),materials=material_specs(meshes),finalizationScript='add_house_sign.py',baseFinalizationScript='finalize_office.py',signShape='One continuous green fascia with integrated shallow central house-roof outline',signageOnlyModified=True,houseSignReport='QA/RE_01_HouseSign.json')
spec=row['signs'][0];spec.update(height=1.30,mainBandHeight=.80,gableBaseWidth=3.10,gableRise=.50,gablePeakWorldZ=4.40,boardBounds=bounds([board]),shape='Continuous fascia with centered gable',artworkAspectRatio=25/1.3)
report={'id':'RE_01','pass':True,'changedMeshes':changed,'unchangedObjects':len(before)-len(changed),'beforeSHA256':before,'afterSHA256':after,'geometryOutsideTwoSignMeshesUnchanged':True,'boundsUnchanged':True,'mainBandHeight':.8,'gableBaseWidth':3.1,'gableRise':.5,'boardPeakZ':4.4,'outlineBounds':bounds([top]),'previousTriangles':2416,'triangles':row['triangles'],'backup':'History/BeforeHouseSign_20261105'}
(OUT/row['houseSignReport']).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
production=json.loads((BACKUP/row['validationReport']).read_text(encoding='utf8'));production['triangles']=row['triangles'];production['houseSignOnlyUpdate']=row['houseSignReport']
(OUT/row['validationReport']).write_text(json.dumps(production,ensure_ascii=False,indent=2),encoding='utf8')
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
catalog=copy.deepcopy(manifest)
for key in ('blend','fbx','glb','preview','frontPreview','parcelPreview','humanScalePreview','topPreview','sourceApprovedBlend','sourceDesign','finalizationScript','baseFinalizationScript','validationReport','houseSignReport'):catalog['items'][0][key]='../'+row[key]
(OUT/'Catalog/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')
scene=bpy.context.scene;camera=scene.camera;stage=bpy.data.collections['Presentation_ONLY']
def aim(location,target,ortho):
    camera.data.type='ORTHO';camera.location=location;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=ortho;bpy.context.view_layer.update()
aim((45,-55,46),(0,6,1.5),78);export(root,row)
bpy.ops.wm.open_mainfile(filepath=str(OUT/row['blend']));bpy.context.view_layer.update();root=bpy.data.objects['RE_01']
assert fingerprint(root)==after,'Saved geometry differs from validated geometry'
scene=bpy.context.scene;camera=scene.camera;stage=bpy.data.collections['Presentation_ONLY']
# Close view first so the integrated roof silhouette can be reviewed promptly.
scene.render.resolution_x=1200;scene.render.resolution_y=1000
aim((8,-23,7.8),(0,-9.4,1.8),14);render(OUT/row['humanScalePreview'])
for ob in stage.objects:
    if ob.name.startswith(('ParcelGuide_','ScalePerson_')):ob.hide_render=True
aim((35,-49,31),(0,-1,1.7),40);render(OUT/row['preview'])
camera.data.type='PERSP';camera.data.lens=72;camera.location=(0,-69.7,1.8);camera.rotation_euler=(Vector((0,-9.7,2.2))-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.resolution_x=1600;scene.render.resolution_y=500;bpy.context.view_layer.update();render(OUT/row['frontPreview'])
for ob in stage.objects:
    if ob.name.startswith(('ParcelGuide_','ScalePerson_')):ob.hide_render=False
scene.render.resolution_x=1200;scene.render.resolution_y=1000
aim((45,-55,46),(0,6,1.5),78);render(OUT/row['parcelPreview'])
camera.location=(0,6,80);camera.rotation_euler=(0,0,0);camera.data.ortho_scale=60;bpy.context.view_layer.update();render(OUT/row['topPreview'])
print('HOUSE_SIGN_COMPLETE',changed,row['triangles'],flush=True)
