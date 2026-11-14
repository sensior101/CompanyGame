"""Material-only variants for the 19 extras. Reuse approved commercial finishes."""
import bpy, sys, json, hashlib, importlib.util, math
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
BASE=Path(__file__).resolve().parent
OUT=BASE/'MaterialVariants'
for name in ('Textures','Previews','QA'):(OUT/name).mkdir(parents=True,exist_ok=True)
spec=importlib.util.spec_from_file_location('shared_styles',BASE.parent/'CommercialLowPoly_20261105/build_styles.py')
s=importlib.util.module_from_spec(spec);spec.loader.exec_module(s)
s.OUT=OUT
h=s.h
ENTRIES=json.loads((BASE/'QA/catalog.json').read_text(encoding='utf8'))
ORIGINAL_SURFACE=s.surface

def surface(style,obj,entry):
    role=obj['Role'];name=obj.name.lower()
    if obj.get('ImageSurface'):return ORIGINAL_SURFACE(style,obj,entry)
    if style=='GlossBlack' and entry['id']=='HangingTableNumber' and name.startswith('whitenumberface'):
        return ('NumberFace','FAF9F5',0,.38,None)
    if role in ('Screen','Water'):return h.style_surface('WhiteWood',role,obj.name,entry)
    if role=='Red' and (entry['id']!='RedPlasticChair' or style=='WhiteWood'):
        return ('RedPlastic','CB342C',0,.37,None)
    if role=='Fabric' and entry['category']!='Seating':
        color={'WhiteWood':'EEEAE1','WhiteBlack':'EEEDE9','GlossBlack':'171B20','MarbleGold':'EEE7D7'}[style]
        return ('Upholstery',color,0,.82,None)
    if entry['category']=='Tables' and name.startswith(('oakroundtop','oaktop','oakoutdoorslat')):
        if style=='WhiteBlack':return ('PorcelainWhite','F4F3EF',0,.38,None)
        if style=='MarbleGold':return ('CalacattaMarble','F2EFE5',0,.23,'Calacatta')
    if entry['id']=='RedPlasticChair':
        trim=any(word in name for word in ('leg','arm','backside','rail'))
        if style=='WhiteBlack':return ('BlackSatin' if trim else 'PorcelainWhite','1B1E23' if trim else 'F4F3EF',0,.30 if trim else .38,None)
        if style=='MarbleGold':return ('PolishedGold','D9AE58',1,.20,None) if trim else ('CalacattaMarble','F2EFE5',0,.23,'Calacatta')
    return ORIGINAL_SURFACE(style,obj,entry)
s.surface=surface

def setup():
    try:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
        s.GPU=any(d.type=='OPTIX' for d in prefs.devices)
    except Exception:s.GPU=False

def build(only=None):
    protected=list((BASE/'Models').glob('*.blend'))+[BASE/'CommercialExtras_WhiteWood_RoomScale.blend']
    protected+=list((BASE.parent/'CommercialLowPoly_20261105/MaterialVariants').rglob('*.blend'))
    baseline={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected}
    (OUT/'QA/SourceBaseline.json').write_text(json.dumps(baseline,indent=2),encoding='utf8')
    h.make_style_textures(OUT/'Textures')
    records=[row for row in json.loads((OUT/'QA/catalog.json').read_text(encoding='utf8')) if row['id'] not in only] if only else []
    for entry in ENTRIES:
        if only and entry['id'] not in only:continue
        bpy.ops.wm.open_mainfile(filepath=str(BASE/'Models'/f"{entry['id']}.blend"),load_ui=False,use_scripts=False)
        col=bpy.data.collections[entry['id']+'_ASSET'];signature=h.material_geometry_signature(col);cache={}
        s.reflection_lights((0,0,entry['dimensions'][2]/2))
        for style in s.STYLES:
            folder=OUT/style/'Models';folder.mkdir(parents=True,exist_ok=True)
            s.apply_style(col,entry,style,cache)
            for obj in col.objects:
                if obj.type=='EMPTY':obj['Theme']=style
            s.configure_render();path=folder/f"{entry['id']}_{style}.blend";s.save(path)
            records.append(dict(id=entry['id'],label=entry['label'],style=style,blend=str(path.relative_to(OUT)),geometry_signature=signature,dimensions=entry['dimensions']))
        print('STYLED',entry['id'],flush=True)
    (OUT/'QA/catalog.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf8')

def showroom(styles=None):
    deskentry=next(e for e in s.ENTRIES if e['id']=='PlainDesk')
    for style in (styles or s.STYLES):
        bpy.ops.wm.open_mainfile(filepath=str(BASE/'CommercialExtras_WhiteWood_RoomScale.blend'),load_ui=False,use_scripts=False)
        cache={}
        for entry in ENTRIES:s.apply_style(bpy.data.collections[entry['id']+'_ASSET'],entry,style,cache)
        s.apply_style(bpy.data.collections['PlainDesk_ASSET'],deskentry,style,cache)
        for entry in ENTRIES:
            for obj in bpy.data.collections[entry['id']+'_ASSET'].objects:
                if obj.type=='EMPTY':obj['Theme']=style
        s.reflection_lights((0,0,0),True);s.configure_render()
        bpy.context.scene.render.resolution_x=1400;bpy.context.scene.render.resolution_y=940
        bpy.context.scene.render.filepath=str(OUT/'Previews'/f'{style}_Room.png')
        s.save(OUT/style/f'CommercialExtras_{style}.blend')
        bpy.ops.render.render(write_still=True);print('SHOWROOM',style,flush=True)

def comparisons(pages=range(4)):
    for page in pages:
        subset=ENTRIES[page*5:page*5+5]
        bpy.ops.wm.read_factory_settings(use_empty=True)
        for row,style in enumerate(s.STYLES):
            for column,entry in enumerate(subset):
                identifier=entry['id'];path=OUT/style/'Models'/f'{identifier}_{style}.blend'
                with bpy.data.libraries.load(str(path),link=False) as (src,dst):dst.collections=[identifier+'_ASSET']
                col=dst.collections[0];bpy.context.scene.collection.children.link(col)
                root=next(o for o in col.objects if o.type=='EMPTY')
                # Presentation thumbnails only: originals retain metre scale.
                scale=1.65/max(entry['dimensions']);root.scale=(scale,)*3
                root.location=(column*2.9,-row*3.5,-entry['bounds_min'][2]*scale)
        bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015))
        mat=bpy.data.materials.new('CompareGround');mat.use_nodes=True
        mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=h.rgba('879591')
        mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.85
        bpy.context.object.data.materials.append(mat)
        target=(5.8,-5.25,.5)
        s.commercial.camera_scene((5.8,-19,20),target,16.6,(2000,1600))
        s.reflection_lights(target,True);s.configure_render()
        bpy.context.scene.render.filepath=str(OUT/'Previews'/f'Compare_{page+1:02d}.png')
        bpy.ops.render.render(write_still=True);print('COMPARISON',page+1,flush=True)

def validate():
    records=json.loads((OUT/'QA/catalog.json').read_text(encoding='utf8'));assert len(records)==76
    for record in records:
        bpy.ops.wm.open_mainfile(filepath=str(OUT/record['blend']),load_ui=False,use_scripts=False)
        col=bpy.data.collections[record['id']+'_ASSET']
        assert h.material_geometry_signature(col)==record['geometry_signature'],record['blend']
        for obj in col.objects:
            if obj.type!='MESH':continue
            mat=obj.data.materials[0];shader=mat.node_tree.nodes['Principled BSDF']
            assert mat['Style']==record['style']
            if obj.get('ImageSurface'):
                assert tuple(shader.inputs['Base Color'].default_value)==(1,1,1,1)
                assert obj.data.uv_layers.active.name=='ImageUV'
            if mat['SurfaceFinish']=='PolishedBlackMetal':
                assert shader.inputs['Metallic'].default_value>.9 and shader.inputs['Roughness'].default_value<.21
            if obj['Role']=='Fabric':assert shader.inputs['Metallic'].default_value==0
            if obj['Role']=='Screen':assert mat['SurfaceFinish']=='Screen'
            if record['style']=='GlossBlack' and record['id']=='HangingTableNumber' and obj.name.lower().startswith('whitenumberface'):
                assert min(shader.inputs['Base Color'].default_value[:3])>.9
            for node in mat.node_tree.nodes:
                if node.type=='TEX_IMAGE':assert node.image and node.image.packed_file
        print('CHECKED',record['id'],record['style'],flush=True)
    baseline=json.loads((OUT/'QA/SourceBaseline.json').read_text(encoding='utf8'))
    assert all(hashlib.sha256(Path(path).read_bytes()).hexdigest()==digest for path,digest in baseline.items())
    for style in s.STYLES:
        bpy.ops.wm.open_mainfile(filepath=str(OUT/style/f'CommercialExtras_{style}.blend'),load_ui=False,use_scripts=False)
        for entry in ENTRIES:
            col=bpy.data.collections[entry['id']+'_ASSET']
            lo,hi=s.commercial.bounds([o for o in col.objects if o.type=='MESH'])
            assert lo[0]>=-13.001 and hi[0]<=13.001 and lo[1]>=-8.201 and hi[1]<=8.201 and lo[2]>=-.001 and hi[2]<=3.301
            assert all(o.data.materials[0]['Style']==style for o in col.objects if o.type=='MESH')
    report=dict(validated_models=76,validated_rooms=4,source_files_unchanged=len(baseline),geometry_uv_transforms_preserved=True,packed_textures=True,white_image_surfaces=True,gloss_black_metal_checked=True,textiles_nonmetallic=True,unity_imported=False)
    (OUT/'QA/validation.json').write_text(json.dumps(report,indent=2),encoding='utf8');print('VALIDATION_PASSED',report,flush=True)

if __name__=='__main__':
    setup()
    if '--repair-number' in sys.argv:
        build(['HangingTableNumber']);showroom(['GlossBlack']);comparisons([1])
    elif '--verify-only' not in sys.argv:
        if '--render-only' not in sys.argv:build()
        showroom();comparisons()
    validate()
