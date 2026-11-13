"""Attach usable blank signs to approved buildings without regenerating their meshes.

Only descendants of the existing Signage empty are modified. Existing door and
entry objects, building materials, mesh topology and transforms are preserved.
Blender --background --python retrofit_signage.py -- 1 4
"""
import bpy, json, sys, hashlib, copy, ast, math
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent
SOURCE=OUT.parent
for folder in ('Models','Exports','Previews','Catalog','QA'):(OUT/folder).mkdir(exist_ok=True)
# Reuse audited exact-mesh fingerprint and color conversion helpers without
# executing the source color generator or rebuilding its approved geometry.
helper_path=SOURCE/'recolor_existing.py'
helper_tree=ast.parse(helper_path.read_text(encoding='utf8'))
helper_names={'linear','descendant','asset_objects','fingerprint','combined'}
helper_nodes=[node for node in helper_tree.body if isinstance(node,ast.FunctionDef) and node.name in helper_names]
exec(compile(ast.Module(body=helper_nodes,type_ignores=[]),str(helper_path),'exec'),globals())

def outside_signage_fingerprint(root,signage):
    all_hashes=fingerprint(root)
    excluded={ob.name for ob in asset_objects(signage)}
    geometry={name:digest for name,digest in all_hashes.items() if name not in excluded}
    colors={ob.name:[(m.name,list(m.diffuse_color),list(m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value)) for m in ob.data.materials] for ob in asset_objects(root) if ob.name not in excluded and ob.type=='MESH'}
    return {'geometry':geometry,'materialColors':colors}

def box(name,center,size,material,parent,collection):
    x,y,z=(s/2 for s in size)
    verts=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    faces=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(3,7,6,2),(0,4,7,3),(1,2,6,5)]
    mesh=bpy.data.meshes.new(name+'_Mesh');mesh.from_pydata(verts,[],faces);mesh.update()
    uv=mesh.uv_layers.new(name='UVMap')
    for face in mesh.polygons:
        coords=((1,0),(1,1),(0,1),(0,0)) if face.index==3 else ((0,0),(1,0),(1,1),(0,1))
        for li,coord in zip(face.loop_indices,coords):uv.data[li].uv=coord
    ob=bpy.data.objects.new(name,mesh);collection.objects.link(ob)
    ob.parent=parent;ob.location=center;mesh.materials.append(material)
    return ob

def material_copy(original,name,hexcode=None):
    m=original.copy();m.name=name
    if hexcode:
        color=(*linear(hexcode),1)
        m.diffuse_color=color;m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=color
    return m

def bounds(objects):
    coords=[ob.matrix_world@v.co for ob in objects if ob.type=='MESH' for v in ob.data.vertices]
    return {'min':[round(min(v[k] for v in coords),6) for k in range(3)],'max':[round(max(v[k] for v in coords),6) for k in range(3)]}

def install(number):
    old=next(r for r in json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['items'] if r['id']==f'CB_{number:02d}')
    ident=old['id'];source=SOURCE/old['blend']
    bpy.ops.wm.open_mainfile(filepath=str(source));bpy.context.view_layer.update()
    root=bpy.data.objects[ident];signage=bpy.data.objects['Signage'];collection=root.users_collection[0]
    before=outside_signage_fingerprint(root,signage)
    oldsigns={s['name']:copy.deepcopy(s) for s in old['signs']}
    previous=[{'name':ob.name,'type':ob.type} for ob in asset_objects(signage)]
    # Retain original four integration anchors, replacing only their descendants.
    anchors={name:bpy.data.objects[name] for name in oldsigns}
    keep={signage,*anchors.values()}
    for ob in list(asset_objects(signage)):
        if ob not in keep:bpy.data.objects.remove(ob,do_unlink=True)
    trim=material_copy(bpy.data.materials[ident+'_steel'],ident+'_Sign_Trim','777a76')
    backing=material_copy(bpy.data.materials[ident+'_metal'],ident+'_Sign_Backing','424643')
    records=[]
    for name in ('Sign_Main_01','Sign_Main_02','Sign_Corner','Sign_Side','Sign_Blade'):
        blade=name=='Sign_Blade'
        anchor=anchors.get(name)
        if anchor is None:
            anchor=bpy.data.objects.new(name,None);collection.objects.link(anchor);anchor.parent=signage
        if blade:
            width,height=.90,1.40
            anchor.location=(6.91,3.78,3.90);anchor.rotation_euler=(0,0,0)
        else:
            height=.80
            if name.startswith('Sign_Main'):width=4.20 if number==4 else 4.50
            elif name=='Sign_Corner':width=1.40 if number==4 else 1.50
            else:width=2.80 if number==4 else 4.20
            anchor.location.z=3.60
            if name.startswith('Sign_Main'):anchor.location.y=-4.48
            else:anchor.location.x=6.48
        anchor['WidthMetres']=width;anchor['HeightMetres']=height
        anchor['ReplaceableBoard']=name+'_Board'
        anchor['Surface']='Blank opaque sign; supply individual branding material'
        anchor['MountType']='Perpendicular blade' if blade else 'Wall fascia'
        face=material_copy(bpy.data.materials[ident+'_sign'],ident+'_'+name+'_Face','262a29')
        # Board local X=width, local Z=height; outward surface is local -Y.
        # All six faces have normalized 0..1 UVs. Each sign has an independent material.
        board=box(name+'_Board',(0,0,0),(width,.065,height),face,anchor,collection)
        board['ReplacementMaterial']=face.name;board['ArtworkAspectRatio']=width/height
        if not blade:
            box(name+'_Backing',(0,.050,0),(width+.10,.08,height+.10),backing,anchor,collection)
        for z in (-height/2-.025,height/2+.025):
            box(name+'_Trim',(0,-.006,z),(width+.10,.09,.05),trim,anchor,collection)
        for x in (-width/2-.025,width/2+.025):
            box(name+'_Trim',(x,-.006,0),(.05,.09,height),trim,anchor,collection)
        if blade:
            for z in (-.48,.48):
                box(name+'_WallBracket',(-.655,.025,z),(.45,.14,.09),backing,anchor,collection)
            box(name+'_WallPlate',(-.83,.025,0),(.18,.26,1.18),backing,anchor,collection)
        else:
            for x in (-width*.32,width*.32):
                box(name+'_MountBracket',(x,.18,0),(.14,.21,.38),backing,anchor,collection)
        bpy.context.view_layer.update()
        mount_objects=asset_objects(anchor)
        records.append({'name':name,'width':width,'height':height,'depth':.065,
            'position':list(anchor.matrix_world.translation),
            'rotationEulerDegrees':[round(math.degrees(v),5) for v in anchor.rotation_euler],
            'outwardNormal':list(anchor.matrix_world.to_3x3()@Vector((0,-1,0))),
            'board':board.name,'material':face.name,'uv':'UVMap normalized 0..1',
            'artworkAspectRatio':round(width/height,5),
            'mountType':'Blade' if blade else 'WallFascia',
            'doubleSidedArtwork':blade,
            'boardBounds':bounds([board]),'mountBounds':bounds(mount_objects),
            'replacementGuidance':('Assign the individual face material to Sign_Blade_Board. Both broad faces are exposed and use upright 0..1 UVs. Local X is width; local Z is height. Keep the anchor and dimensions. Brackets touch only the leftmost 2 cm edge.' if blade else 'Assign an individual material to the named Board mesh. Retain anchor transform and dimensions. Artwork front is local -Y; local X is width and local Z is height. Opaque base color texture; no resizing of facade or door objects.')})
    bpy.context.view_layer.update()
    after=outside_signage_fingerprint(root,signage)
    if before!=after:raise RuntimeError('Non-signage geometry or color changed')
    asset=asset_objects(root);meshes=[ob for ob in asset if ob.type=='MESH']
    triangles=sum(len(p.vertices)-2 for ob in meshes for p in ob.data.polygons)
    row=copy.deepcopy(old)
    for key in ('geometryUnchanged','geometryFingerprint','geometryFingerprintReport'):row.pop(key,None)
    stem=ident+'_SignageReady'
    row.update(revision=6,variant='SignageReady',signageModified=True,nonSignageGeometryUnchanged=True,
        sourceApprovedBlend='../'+old['blend'],sourceTriangles=old['sourceTriangles'],previousRevisionTriangles=old['triangles'],
        triangles=triangles,triangleReductionPercent=round(100*(1-triangles/old['sourceTriangles']),2),
        meshObjects=len(meshes),exportBounds=bounds(meshes),signs=records,
        originalDesignTriangles=old['sourceTriangles'],addedSignageTriangles=triangles-old['triangles'],
        blend=f'Models/{stem}.blend',fbx=f'Exports/{stem}.fbx',glb=f'Exports/{stem}.glb',
        preview=f'Previews/{stem}.png',frontPreview=f'Previews/{stem}_Front.png',sidePreview=f'Previews/{stem}_Side.png',
        nonSignageFingerprintReport=f'QA/{ident}_NonSignageFingerprint.json')
    row['sourceDesign']='../'+old['sourceDesign']
    mats=sorted({m for ob in meshes for m in ob.data.materials},key=lambda m:m.name)
    row['materials']=[]
    for m in mats:
        shader=m.node_tree.nodes.get('Principled BSDF')
        row['materials'].append({'name':m.name,'color':list(shader.inputs['Base Color'].default_value),
           'roughness':shader.inputs['Roughness'].default_value,'metallic':shader.inputs['Metallic'].default_value,
           'surface':'Opaque','unitySurface':'Opaque','unityAlpha':1,'texture':None})
    bpy.ops.object.select_all(action='DESELECT')
    for ob in asset:ob.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(OUT/row['fbx']),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_custom_props=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    bpy.ops.export_scene.gltf(filepath=str(OUT/row['glb']),export_format='GLB',use_selection=True,export_yup=True,export_extras=True,export_materials='EXPORT')
    scene=bpy.context.scene;scene.render.filepath=str(OUT/row['preview'])
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/row['blend']))
    bpy.ops.wm.open_mainfile(filepath=str(OUT/row['blend']));bpy.context.view_layer.update()
    saved=outside_signage_fingerprint(bpy.data.objects[ident],bpy.data.objects['Signage'])
    if saved!=before:raise RuntimeError('Saved non-signage fingerprint differs')
    report={'id':ident,'source':str(source),'sourceBlendSHA256':hashlib.sha256(source.read_bytes()).hexdigest(),
        'scope':'All asset objects except Signage and its descendants; geometry, UVs, transforms, hierarchy, material slots and colors',
        'before':before,'afterSaved':saved,'pass':before==saved,
        'signageBefore':previous,'signageAfter':[{'name':o.name,'type':o.type} for o in asset_objects(bpy.data.objects['Signage'])],
        'newSignSpecifications':records}
    (OUT/row['nonSignageFingerprintReport']).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    scene=bpy.context.scene
    try:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
        for device in prefs.devices:device.use=device.type=='OPTIX'
        if any(device.use for device in prefs.devices):scene.cycles.device='GPU'
    except Exception:pass
    bpy.ops.render.render(write_still=True)
    camera=scene.camera
    scene.render.resolution_x=800;scene.render.resolution_y=800;scene.cycles.samples=20
    camera.data.ortho_scale=16.7
    for key,position in [('frontPreview',(0,-30,10)),('sidePreview',(30,0,10))]:
        camera.location=position;camera.rotation_euler=(Vector((0,0,6))-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(OUT/row[key]);bpy.ops.render.render(write_still=True)
    manifest=OUT/'manifest.json'
    rows=json.loads(manifest.read_text(encoding='utf8'))['items'] if manifest.exists() else []
    rows=sorted([r for r in rows if r['id']!=ident]+[row],key=lambda r:r['id'])
    manifest.write_text(json.dumps({'schemaVersion':1,'style':'Korean low-poly buildings with five replaceable blank signs','geometryPolicy':'Only Signage subtree modified','items':rows},ensure_ascii=False,indent=2),encoding='utf8')
    catalogs=copy.deepcopy(rows)
    for item in catalogs:
        for key in ('blend','fbx','glb','preview','frontPreview','sidePreview','sourceApprovedBlend','sourceDesign','nonSignageFingerprintReport'):item[key]='../'+item[key]
    (OUT/'Catalog/catalog.json').write_text(json.dumps({'items':catalogs},ensure_ascii=False,indent=2),encoding='utf8')
    print('SIGNAGE_COMPLETE',ident,'triangles',triangles,'outsideSignageUnchanged',before==saved,flush=True)

args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
for number in ([int(x) for x in args] if args else range(1,9)):install(number)
