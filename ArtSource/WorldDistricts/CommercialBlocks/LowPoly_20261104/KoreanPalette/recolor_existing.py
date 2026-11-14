"""Color-only Korean urban palette on the approved low-poly .blend assets.

Blender --background --factory-startup --python recolor_existing.py -- 3 5
No mesh generation, deletion, transform, UV, anchor or topology edits.
Opening the approved .blend is intentional: rebuilding could change accepted geometry.
"""
import bpy, json, sys, hashlib, copy
from pathlib import Path
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent
SOURCE=OUT.parent
for name in ('Models','Exports','Previews','Catalog','QA'):(OUT/name).mkdir(exist_ok=True)

# sRGB facade references, converted to Blender linear values explicitly.
# wall / metal & frame / upper glazing / wood / stone trim
PALETTES={
 1:('aaa79f','393c3c','3e4a50','65503d','b9b6ad'),
 2:('ccc8bc','34312e','3e4648','59402d','d2cec3'),
 3:('684036','292c2d','354248','594332','a39b8c'),
 4:('d0cec6','383b3c','485359','6b5541','d4d1c8'),
 5:('363b3e','303437','3d484c','70503a','747673'),
 6:('b1a18b','41413e','424b4b','66503a','bcb2a0'),
 7:('cec9bc','363837','424b4e','68503c','d1cbbc'),
 8:('737571','303434','404c50','65503b','8e908b'),
}
LABELS={1:'콘크리트 회색 · 차콜 코어',2:'오프화이트 · 짙은 월넛',3:'짙은 적갈색 벽돌 · 철제 프레임',4:'중성 화이트 · 흑연색 프레임',5:'짙은 차콜 · 우드 테라스',6:'샌드 베이지 · 회색 창틀',7:'밝은 아이보리 · 차콜',8:'회색 석재 · 짙은 창틀'}

def linear(hexcode):
    channels=[int(hexcode[i:i+2],16)/255 for i in (0,2,4)]
    return tuple(c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4 for c in channels)

def descendant(ob,root):
    while ob:
        if ob==root:return True
        ob=ob.parent
    return False

def asset_objects(root):
    return sorted((o for o in bpy.context.scene.objects if descendant(o,root)),key=lambda o:o.name)

def fingerprint(root):
    result={}
    for ob in asset_objects(root):
        data={'name':ob.name,'type':ob.type,'parent':ob.parent.name if ob.parent else None,
              'matrix_world':[list(row) for row in ob.matrix_world],
              'matrix_local':[list(row) for row in ob.matrix_local],
              'matrix_parent_inverse':[list(row) for row in ob.matrix_parent_inverse],
              'hide_render':ob.hide_render}
        if ob.type=='MESH':
            me=ob.data
            data.update(vertices=[list(v.co) for v in me.vertices],
                edges=[list(e.vertices) for e in me.edges],
                loops=[[l.vertex_index,l.edge_index] for l in me.loops],
                polygons=[[list(p.vertices),p.material_index,p.use_smooth] for p in me.polygons],
                uv={layer.name:[list(uv.uv) for uv in layer.data] for layer in me.uv_layers},
                materialSlots=[m.name if m else None for m in me.materials])
        encoded=json.dumps(data,sort_keys=True,separators=(',',':')).encode('utf8')
        result[ob.name]=hashlib.sha256(encoded).hexdigest()
    return result

def combined(mapping):
    return hashlib.sha256(json.dumps(mapping,sort_keys=True,separators=(',',':')).encode()).hexdigest()

def colors(number):
    wall,frame,glass,wood,trim=PALETTES[number]
    return {'wall':wall,'metal':frame,'frame':frame,'glass':glass,'wood':wood,'trim':trim,
        'inside':'70665b','floor':'82817a','roof':'727675','joint':'53534e','steel':'8a8c88',
        'blind':'857d6f','lamp':'c8bd9f','soil':'46372b','leaf':'485735',
        'leaf_light':'657148','sign':frame,'window_glint':'677170',
        'store_glass':'505b5a','accent':'978c76','studio':'989b99'}

def recolor(number):
    old=next(r for r in json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['items'] if r['id']==f'CB_{number:02d}')
    ident=old['id'];source=SOURCE/old['blend']
    bpy.ops.wm.open_mainfile(filepath=str(source))
    root=bpy.data.objects[ident]
    bpy.context.view_layer.update()
    before=fingerprint(root)
    palette=colors(number)
    changed=[]
    for material in bpy.data.materials:
        prefix=ident+'_'
        if not material.name.startswith(prefix):continue
        key=material.name[len(prefix):]
        if key not in palette:raise ValueError('Missing explicit palette: '+material.name)
        color=(*linear(palette[key]),1)
        oldcolor=list(material.diffuse_color)
        material.diffuse_color=color
        material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=color
        changed.append({'name':material.name,'before':oldcolor,'after':list(color),'srgbHex':'#'+palette[key]})
    # Neutral presentation only; approved camera and all asset transforms untouched.
    scene=bpy.context.scene
    scene.world.node_tree.nodes.get('Background').inputs[0].default_value=(.8,.8,.8,1)
    scene.world.node_tree.nodes.get('Background').inputs[1].default_value=.7
    for light in bpy.data.lights:
        light.color=(1,1,1)
        if light.name=='BroadKey':light.energy=2300
        elif light.name=='WarmSun':light.energy=2.0
        elif light.name=='SoftFill':light.energy=1100
    scene.view_settings.exposure=.4
    scene.cycles.samples=32
    if before!=fingerprint(root):raise RuntimeError('Geometry changed before export')
    bpy.ops.object.select_all(action='DESELECT')
    for ob in asset_objects(root):ob.select_set(True)
    bpy.context.view_layer.objects.active=root
    stem=ident+'_KoreanPalette'
    bpy.ops.export_scene.fbx(filepath=str(OUT/'Exports'/f'{stem}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_custom_props=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    bpy.ops.export_scene.gltf(filepath=str(OUT/'Exports'/f'{stem}.glb'),export_format='GLB',use_selection=True,export_yup=True,export_extras=True,export_materials='EXPORT')
    bpy.context.preferences.filepaths.save_version=0
    blend=OUT/'Models'/f'{stem}.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    # Compare saved file, not just live scene memory.
    bpy.ops.wm.open_mainfile(filepath=str(blend))
    bpy.context.view_layer.update()
    after=fingerprint(bpy.data.objects[ident])
    if before!=after:raise RuntimeError('Saved geometry fingerprint mismatch')
    report={'id':ident,'source':str(source),'output':str(blend),
        'sourceBlendSHA256':hashlib.sha256(source.read_bytes()).hexdigest(),
        'fields':'vertices,edges,loops,polygons,face material indices,flat flags,all UVs,material slot order,world/local/parent-inverse matrices,parent hierarchy,object types,render visibility',
        'beforeSHA256':combined(before),'afterSavedSHA256':combined(after),
        'objects':[{'name':n,'beforeSHA256':before[n],'afterSHA256':after[n],'pass':before[n]==after[n]} for n in before],
        'materials':changed,'pass':before==after}
    (OUT/'QA'/f'{ident}_GeometryFingerprint.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    scene=bpy.context.scene
    scene.render.filepath=str(OUT/'Previews'/f'{stem}.png')
    try:
        prefs=bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type='OPTIX';prefs.get_devices()
        for device in prefs.devices:device.use=device.type=='OPTIX'
        if any(device.use for device in prefs.devices):scene.cycles.device='GPU'
    except Exception:pass
    bpy.ops.render.render(write_still=True)
    row=copy.deepcopy(old)
    row.update(revision=5,variant='KoreanPalette',paletteLabel=LABELS[number],
       blend=f'Models/{stem}.blend',fbx=f'Exports/{stem}.fbx',glb=f'Exports/{stem}.glb',preview=f'Previews/{stem}.png',
       sourceApprovedBlend='../'+old['blend'],geometryFingerprint=combined(after),geometryUnchanged=True,
       geometryFingerprintReport=f'QA/{ident}_GeometryFingerprint.json')
    row['sourceDesign']='../'+old['sourceDesign']
    for material in row['materials']:
        key=material['name'][len(ident)+1:]
        material['color']=[*linear(palette[key]),1]
        material['srgbHex']='#'+palette[key]
    manifest=OUT/'manifest.json'
    rows=json.loads(manifest.read_text(encoding='utf8'))['items'] if manifest.exists() else []
    rows=sorted([r for r in rows if r['id']!=ident]+[row],key=lambda r:r['id'])
    manifest.write_text(json.dumps({'schemaVersion':1,'style':'Korean urban neutral facade palette','geometryPolicy':'unchanged approved low-poly assets; only material base colors updated','items':rows},ensure_ascii=False,indent=2),encoding='utf8')
    catalogrows=copy.deepcopy(rows)
    for item in catalogrows:
        for key in ('blend','fbx','glb','preview','sourceDesign','sourceApprovedBlend','geometryFingerprintReport'):item[key]='../'+item[key]
    (OUT/'Catalog/catalog.json').write_text(json.dumps({'items':catalogrows},ensure_ascii=False,indent=2),encoding='utf8')
    print('PALETTE_COMPLETE',ident,'geometry-identical',combined(after),flush=True)

args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
for number in ([int(a) for a in args] if args else range(1,9)):recolor(number)
