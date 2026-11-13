"""Expose both artwork faces of the blade sign; preserve every non-blade object."""
from pathlib import Path
import bpy,json,copy
source=Path(__file__).resolve().with_name('retrofit_signage.py')
prefix=source.read_text(encoding='utf8').split('\nargs=sys.argv')[0]
exec(compile(prefix,str(source),'exec'),globals())

def visibility(blade,board):
    depsgraph=bpy.context.evaluated_depsgraph_get()
    result=[]
    for side in (-1,1):
        for x in (-.315,0,.315):
            for z in (-.49,0,.49):
                origin=blade.matrix_world@Vector((x,side*.5,z))
                direction=(blade.matrix_world.to_3x3()@Vector((0,-side,0))).normalized()
                hit,location,normal,index,ob,matrix=bpy.context.scene.ray_cast(depsgraph,origin,direction,distance=.7)
                result.append({'side':'front' if side==-1 else 'back','sample':[x,z],
                    'firstHit':ob.name if hit else None,'pass':bool(hit and ob.name==board.name)})
    return result

manifest=OUT/'manifest.json'
rows=json.loads(manifest.read_text(encoding='utf8'))['items']
for row in rows:
    ident=row['id'];blend=OUT/row['blend']
    bpy.ops.wm.open_mainfile(filepath=str(blend));bpy.context.view_layer.update()
    root=bpy.data.objects[ident];blade=bpy.data.objects['Sign_Blade'];board=bpy.data.objects['Sign_Blade_Board']
    excluded={ob.name for ob in asset_objects(blade)}
    before={name:digest for name,digest in fingerprint(root).items() if name not in excluded}
    backplate=bpy.data.objects.get('Sign_Blade_Backing')
    if backplate:bpy.data.objects.remove(backplate,do_unlink=True)
    for ob in asset_objects(blade):
        if ob.name.startswith('Sign_Blade_WallBracket'):
            ob.location.x=-.655;size=(.45,.14,.09)
        elif ob.name=='Sign_Blade_WallPlate':
            ob.location.x=-.83;size=(.18,.26,1.18)
        else:continue
        for v in ob.data.vertices:
            for axis in range(3):v.co[axis]=(-1 if v.co[axis]<0 else 1)*size[axis]/2
        ob.data.update()
    bpy.context.view_layer.update()
    after={name:digest for name,digest in fingerprint(root).items() if name not in excluded}
    assert before==after,'Unexpected non-blade geometry change'
    rays=visibility(blade,board)
    assert all(r['pass'] for r in rays),'Blade face obscured'
    asset=asset_objects(root);meshes=[ob for ob in asset if ob.type=='MESH']
    row['meshObjects']=len(meshes)
    row['triangles']=sum(len(p.vertices)-2 for ob in meshes for p in ob.data.polygons)
    row['addedSignageTriangles']=row['triangles']-row['previousRevisionTriangles']
    row['triangleReductionPercent']=round(100*(1-row['triangles']/row['originalDesignTriangles']),2)
    row['exportBounds']=bounds(meshes)
    sign=next(s for s in row['signs'] if s['name']=='Sign_Blade')
    sign['mountBounds']=bounds(asset_objects(blade))
    sign['doubleSidedArtwork']=True
    sign['replacementGuidance']='Assign the individual face material to Sign_Blade_Board. Both broad faces are exposed and use upright 0..1 UVs. Local X is width; local Z is height. Keep the anchor and dimensions. No solid rear backing. Brackets attach only at the leftmost 2 cm edge.'
    row['bladeFaceVisibilityReport']=f'QA/{ident}_BladeVisibility.json'
    bpy.ops.object.select_all(action='DESELECT')
    for ob in asset:ob.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(OUT/row['fbx']),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_custom_props=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
    bpy.ops.export_scene.gltf(filepath=str(OUT/row['glb']),export_format='GLB',use_selection=True,export_yup=True,export_extras=True,export_materials='EXPORT')
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    bpy.ops.wm.open_mainfile(filepath=str(blend));bpy.context.view_layer.update()
    root=bpy.data.objects[ident];blade=bpy.data.objects['Sign_Blade'];board=bpy.data.objects['Sign_Blade_Board']
    saved={name:digest for name,digest in fingerprint(root).items() if name not in excluded}
    assert before==saved,'Saved non-blade geometry changed'
    rays=visibility(blade,board)
    assert all(r['pass'] for r in rays),'Saved blade face obscured'
    proof={'id':ident,'pass':before==saved and all(r['pass'] for r in rays),
       'nonBladeGeometryUnchanged':before==saved,'frontAndBackSamples':rays,
       'change':'Removed full rear backing, shortened brackets to 2 cm edge overlap, anchored plate across wall X=5.99..6.17m',
       'previousProof':row['nonSignageFingerprintReport']}
    (OUT/row['bladeFaceVisibilityReport']).write_text(json.dumps(proof,ensure_ascii=False,indent=2),encoding='utf8')
    oldproof=OUT/row['nonSignageFingerprintReport']
    report=json.loads(oldproof.read_text(encoding='utf8'))
    current=json.loads(json.dumps(outside_signage_fingerprint(root,bpy.data.objects['Signage'])))
    assert report['before']==current,'Original non-signage geometry changed'
    report['afterSaved']=current
    report['signageAfter']=[{'name':o.name,'type':o.type} for o in asset_objects(bpy.data.objects['Signage'])]
    report['newSignSpecifications']=row['signs'];report['bladeVisibilityProof']=row['bladeFaceVisibilityReport']
    oldproof.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    scene=bpy.context.scene
    try:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
        for device in prefs.devices:device.use=device.type=='OPTIX'
        if any(device.use for device in prefs.devices):scene.cycles.device='GPU'
    except Exception:pass
    scene.render.filepath=str(OUT/row['preview']);bpy.ops.render.render(write_still=True)
    print('BLADE_FIXED',ident,'bothFacesVisible',all(r['pass'] for r in rays),'triangles',row['triangles'],flush=True)
manifest.write_text(json.dumps({'schemaVersion':1,'style':'Korean low-poly buildings with five replaceable blank signs','geometryPolicy':'Only Signage subtree modified; blade artwork exposed on both sides','items':rows},ensure_ascii=False,indent=2),encoding='utf8')
catalogs=copy.deepcopy(rows)
for item in catalogs:
    for key in ('blend','fbx','glb','preview','frontPreview','sidePreview','sourceApprovedBlend','sourceDesign','nonSignageFingerprintReport','bladeFaceVisibilityReport'):item[key]='../'+item[key]
(OUT/'Catalog/catalog.json').write_text(json.dumps({'items':catalogs},ensure_ascii=False,indent=2),encoding='utf8')
