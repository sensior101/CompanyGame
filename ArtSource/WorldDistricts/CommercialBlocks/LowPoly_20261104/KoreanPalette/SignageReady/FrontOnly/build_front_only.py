"""One continuous front board per approved building; optional blade as separate asset.

Reuses previous asset, box/UV/material/fingerprint/export conventions. Never
regenerates the building body. Outputs only in this new revision directory.
Blender --background --python-exit-code 1 --python build_front_only.py -- 1 4 blade
"""
import bpy,json,sys,copy,ast,hashlib,math
from pathlib import Path
from mathutils import Vector,Matrix
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent
SOURCE=OUT.parent
for folder in ('Models','Exports','Previews','Catalog','QA'):(OUT/folder).mkdir(exist_ok=True)

def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    nodes=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names]
    exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'),globals())

reuse(SOURCE.parent/'recolor_existing.py',{'linear','descendant','asset_objects','fingerprint','combined'})
reuse(SOURCE/'retrofit_signage.py',{'outside_signage_fingerprint','box','bounds'})

def material_specs(meshes):
    result=[]
    for mat in sorted({m for ob in meshes for m in ob.data.materials},key=lambda m:m.name):
        p=mat.node_tree.nodes.get('Principled BSDF')
        result.append({'name':mat.name,'color':list(p.inputs['Base Color'].default_value),
           'roughness':p.inputs['Roughness'].default_value,'metallic':p.inputs['Metallic'].default_value,
           'surface':'Opaque','unitySurface':'Opaque','unityAlpha':1,'texture':None})
    return result

def export(root,row):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in asset_objects(root):ob.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(OUT/row['fbx']),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_custom_props=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    bpy.ops.export_scene.gltf(filepath=str(OUT/row['glb']),export_format='GLB',use_selection=True,export_yup=True,export_extras=True,export_materials='EXPORT')
    bpy.context.scene.render.filepath=str(OUT/row['preview'])
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/row['blend']))

def render(path):
    scene=bpy.context.scene;scene.render.filepath=str(path)
    try:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
        for device in prefs.devices:device.use=device.type=='OPTIX'
        if any(d.use for d in prefs.devices):scene.cycles.device='GPU'
    except Exception:pass
    bpy.ops.render.render(write_still=True)

def save_catalog(row,accessory=False):
    path=OUT/'manifest.json'
    manifest=json.loads(path.read_text(encoding='utf8')) if path.exists() else {'schemaVersion':1,'items':[],'accessories':[]}
    key='accessories' if accessory else 'items'
    manifest[key]=sorted([r for r in manifest.get(key,[]) if r['id']!=row['id']]+[row],key=lambda r:r['id'])
    manifest['style']='Continuous front-only signs; optional blade model packaged separately'
    path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    catalog=copy.deepcopy(manifest)
    for item in catalog['items']+catalog['accessories']:
        for field in ('blend','fbx','glb','preview','frontPreview','sourceApprovedBlend','sourceDesign','nonSignageFingerprintReport','validationReport'):
            if field in item:item[field]='../'+item[field]
    (OUT/'Catalog/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')

def building(number):
    old=next(r for r in json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['items'] if r['id']==f'CB_{number:02d}')
    ident=old['id'];source=SOURCE/old['blend']
    bpy.ops.wm.open_mainfile(filepath=str(source));bpy.context.view_layer.update()
    root=bpy.data.objects[ident];signage=bpy.data.objects['Signage'];anchor=bpy.data.objects['Sign_Main_01']
    before=outside_signage_fingerprint(root,signage)
    collection=root.users_collection[0]
    face=bpy.data.materials[ident+'_Sign_Main_01_Face'];trim=bpy.data.materials[ident+'_Sign_Trim'];backing=bpy.data.materials[ident+'_Sign_Backing']
    for ob in list(asset_objects(signage)):
        if ob not in (signage,anchor):bpy.data.objects.remove(ob,do_unlink=True)
    width=9.2 if number==4 else 10.8;height=.8
    anchor.location=(-1.1 if number==4 else 0,-4.48,3.6)
    anchor.rotation_euler=(0,0,0)
    anchor['WidthMetres']=width;anchor['HeightMetres']=height
    anchor['MountType']='Continuous front fascia'
    board=box('Sign_Main_01_Board',(0,0,0),(width,.065,height),face,anchor,collection)
    board['ReplacementMaterial']=face.name;board['ArtworkAspectRatio']=width/height
    box('Sign_Main_01_Backing',(0,.05,0),(width+.1,.08,height+.1),backing,anchor,collection)
    for z in (-.425,.425):box('Sign_Main_01_Trim',(0,-.006,z),(width+.1,.09,.05),trim,anchor,collection)
    for x in (-width/2-.025,width/2+.025):box('Sign_Main_01_Trim',(x,-.006,0),(.05,.09,height),trim,anchor,collection)
    for ratio in (-.40,-.13,.13,.40):box('Sign_Main_01_MountBracket',(width*ratio,.18,0),(.14,.21,.38),backing,anchor,collection)
    bpy.context.view_layer.update()
    assert before==outside_signage_fingerprint(root,signage),'Building geometry or colors changed'
    asset=asset_objects(root);meshes=[ob for ob in asset if ob.type=='MESH']
    triangles=sum(len(p.vertices)-2 for ob in meshes for p in ob.data.polygons)
    row=copy.deepcopy(old)
    for key in ('sidePreview','bladeFaceVisibilityReport','addedSignageTriangles'):row.pop(key,None)
    stem=ident+'_FrontOnly'
    row.update(revision=7,variant='FrontOnly',nonSignageGeometryUnchanged=True,signageModified=True,
       sourceApprovedBlend='../'+old['blend'],previousRevisionTriangles=old['triangles'],
       triangles=triangles,meshObjects=len(meshes),exportBounds=bounds(meshes),
       triangleReductionPercent=round(100*(1-triangles/row['originalDesignTriangles']),2),
       blend=f'Models/{stem}.blend',fbx=f'Exports/{stem}.fbx',glb=f'Exports/{stem}.glb',
       preview=f'Previews/{stem}.png',frontPreview=f'Previews/{stem}_Front.png',
       nonSignageFingerprintReport=f'QA/{ident}_NonSignageFingerprint.json',materials=material_specs(meshes),
       attachedBlade=False,attachedSideSigns=False,frontSignCount=1)
    row['sourceDesign']='../'+old['sourceDesign']
    row['signs']=[{'name':'Sign_Main_01','width':width,'height':height,'depth':.065,
       'position':list(anchor.matrix_world.translation),'rotationEulerDegrees':[0,0,0],'outwardNormal':[0,-1,0],
       'board':board.name,'material':face.name,'uv':'UVMap normalized 0..1','artworkAspectRatio':width/height,
       'mountType':'ContinuousFrontFascia','boardBounds':bounds([board]),'mountBounds':bounds(asset_objects(anchor)),
       'replacementGuidance':'One uninterrupted front board. Replace only this board material with a single wide artwork image. Keep the anchor transform and size. Front faces local -Y; width is local X and height is local Z.'}]
    export(root,row)
    bpy.ops.wm.open_mainfile(filepath=str(OUT/row['blend']));bpy.context.view_layer.update()
    saved=outside_signage_fingerprint(bpy.data.objects[ident],bpy.data.objects['Signage'])
    assert before==saved,'Saved building geometry or colors changed'
    proof={'id':ident,'pass':before==saved,'sourceBlendSHA256':hashlib.sha256(source.read_bytes()).hexdigest(),
        'before':before,'afterSaved':saved,'change':'Replaced five split signs by one continuous front board. No side or blade geometry remains attached.',
        'removedAnchors':['Sign_Main_02','Sign_Corner','Sign_Side','Sign_Blade']}
    (OUT/row['nonSignageFingerprintReport']).write_text(json.dumps(proof,ensure_ascii=False,indent=2),encoding='utf8')
    render(OUT/row['preview'])
    scene=bpy.context.scene;camera=scene.camera
    scene.render.resolution_x=800;scene.render.resolution_y=800;scene.cycles.samples=20
    camera.data.ortho_scale=16.7;camera.location=(0,-30,10)
    camera.rotation_euler=(Vector((0,0,6))-camera.location).to_track_quat('-Z','Y').to_euler()
    render(OUT/row['frontPreview'])
    save_catalog(row)
    print('FRONT_COMPLETE',ident,'oneBoard',width,'triangles',triangles,flush=True)

def blade_asset():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'Models/CB_01_SignageReady.blend'));bpy.context.view_layer.update()
    blade=bpy.data.objects['Sign_Blade'];plate=bpy.data.objects['Sign_Blade_WallPlate']
    original_hashes={name:digest for name,digest in fingerprint(blade).items()}
    coords=[plate.matrix_world@v.co for v in plate.data.vertices]
    origin=Vector((min(v.x for v in coords),plate.matrix_world.translation.y,plate.matrix_world.translation.z))
    asset=asset_objects(blade);keep=set(asset)
    stage=bpy.data.collections['Presentation_ONLY'];keep.update(stage.objects)
    collection=blade.users_collection[0];collection.name='OptionalBladeSign_ASSET'
    root=bpy.data.objects.new('OptionalBladeSign',None);collection.objects.link(root)
    root['MountNormal']='+X';root['UpAxis']='+Z';root['MountPlane']='X=0 wall attachment face'
    root['OptionalAccessory']=True
    anchor=bpy.data.objects.new('WallAttachmentAnchor',None);collection.objects.link(anchor);anchor.parent=root
    old_world=blade.matrix_world.copy();blade.parent=root
    blade.matrix_world=Matrix.Translation(-origin)@old_world
    keep.update((root,anchor))
    for ob in list(bpy.context.scene.objects):
        if ob not in keep:bpy.data.objects.remove(ob,do_unlink=True)
    bpy.context.view_layer.update()
    mesh_objects=[ob for ob in asset_objects(root) if ob.type=='MESH']
    material_names={}
    for ob in mesh_objects:
        for material in ob.data.materials:
            if material.name not in material_names:
                name='Blade_Face' if 'Face' in material.name else ('Blade_Trim' if 'Trim' in material.name else 'Blade_Mount')
                material_names[material.name]=name
    for old_name,new_name in material_names.items():bpy.data.materials[old_name].name=new_name
    # All source building scene objects are removed, and unused source meshes are purged.
    bpy.data.orphans_purge(do_recursive=True)
    board=bpy.data.objects['Sign_Blade_Board']
    board['ReplacementMaterial']='Blade_Face'
    blade['ReplacementMaterial']='Blade_Face'
    scene=bpy.context.scene
    ground=bpy.data.objects.get('StudioGround')
    if ground:ground.location.z=-.88
    camera=scene.camera;camera.location=(3.2,-5,2.1)
    camera.rotation_euler=(Vector((.72,0,0))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=2.7
    scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.cycles.samples=32
    bpy.context.view_layer.update()
    row={'id':'OptionalBladeSign','assetType':'OptionalWallMountedBlankLogo','revision':1,
      'blend':'Models/OptionalBladeSign.blend','fbx':'Exports/OptionalBladeSign.fbx','glb':'Exports/OptionalBladeSign.glb','preview':'Previews/OptionalBladeSign.png',
      'triangles':sum(len(p.vertices)-2 for ob in mesh_objects for p in ob.data.polygons),'meshObjects':len(mesh_objects),
      'exportBounds':bounds(mesh_objects),'materials':material_specs(mesh_objects),
      'pivot':{'object':'OptionalBladeSign','position':[0,0,0],'wallPlane':'X=0','outwardNormal':[1,0,0],'up':[0,0,1]},
      'mountAnchor':'WallAttachmentAnchor','boardAnchor':'Sign_Blade','board':'Sign_Blade_Board',
      'panelWidth':.9,'panelHeight':1.4,'doubleSidedArtwork':True,'boardPosition':list(blade.matrix_world.translation),
      'placementGuidance':'Separate optional model, not included in any building. Place its origin / WallAttachmentAnchor on a wall, rotate +X outward and keep +Z upright. Assign artwork only to Blade_Face. Player placement controls are not implemented.',
      'sourceApprovedBlend':'../Models/CB_01_SignageReady.blend','validationReport':'QA/OptionalBladeSign_Validation.json'}
    export(root,row)
    bpy.ops.wm.open_mainfile(filepath=str(OUT/row['blend']));bpy.context.view_layer.update()
    root=bpy.data.objects['OptionalBladeSign'];blade=bpy.data.objects['Sign_Blade'];board=bpy.data.objects['Sign_Blade_Board']
    scene=bpy.context.scene
    depsgraph=bpy.context.evaluated_depsgraph_get();rays=[]
    for side in (-1,1):
        for x in (-.3,0,.3):
            for z in (-.45,0,.45):
                pos=blade.matrix_world@Vector((x,side*.5,z));direction=(blade.matrix_world.to_3x3()@Vector((0,-side,0))).normalized()
                hit,loc,normal,index,obj,matrix=scene.ray_cast(depsgraph,pos,direction,distance=.7)
                rays.append({'side':side,'sample':[x,z],'firstHit':obj.name if hit else None,'pass':bool(hit and obj.name==board.name)})
    assert all(r['pass'] for r in rays),'Optional blade artwork obscured'
    proof={'id':row['id'],'pass':all(r['pass'] for r in rays),'pivotAtWallAttachment':list(root.location)==[0,0,0],
        'sourceBladeMeshesReused':True,'sourceWallPlaneOrigin':list(origin),'visibilityRays':rays,
        'exportBounds':row['exportBounds'],'triangles':row['triangles'],'noBuildingObjects':not any(o.name.startswith('CB_') for o in bpy.context.scene.objects)}
    (OUT/row['validationReport']).write_text(json.dumps(proof,ensure_ascii=False,indent=2),encoding='utf8')
    render(OUT/row['preview']);save_catalog(row,True)
    print('OPTIONAL_BLADE_COMPLETE',row['triangles'],row['exportBounds'],flush=True)

args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
for arg in (args or [str(i) for i in range(1,9)]+['blade']):
    if arg=='blade':blade_asset()
    else:building(int(arg))
