import bpy,json,shutil,re
from pathlib import Path
from mathutils import Vector
base=Path(__file__).resolve().parent; root=base.parents[1]
asset=root/'CompanyGame/Assets/Art/Items/Furniture/Library'
rows=[]
def tex_for(socket,seen=None):
    seen=set() if seen is None else seen
    if not socket or not socket.is_linked:return None
    for link in socket.links:
        n=link.from_node
        if n in seen:continue
        seen.add(n)
        if n.type=='TEX_IMAGE' and n.image:return n.image
        for inp in n.inputs:
            image=tex_for(inp,seen)
            if image:return image
def save_image(im,dest):
    if not im:return ''
    source=Path(bpy.path.abspath(im.filepath));ext=source.suffix.lower()
    if ext not in ['.png','.jpg','.jpeg','.tga','.exr']:ext='.png'
    out=dest/(re.sub(r'[^a-zA-Z0-9_.-]','_',im.name)+ext)
    if not out.exists():
        if im.packed_file:out.write_bytes(im.packed_file.data)
        elif source.exists():shutil.copy2(source,out)
        else:return ''
    return out.relative_to(root/'CompanyGame').as_posix()
for j in json.loads((base/'jobs.json').read_text(encoding='utf8')):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if j['input'].endswith('.blend'):bpy.ops.wm.open_mainfile(filepath=j['input'],load_ui=False)
    else:bpy.ops.import_scene.fbx(filepath=j['input'],use_image_search=True)
    objs=[o for o in bpy.context.scene.objects if o.type=='MESH']
    groups=[(j['id'],j['label'],j['category'],objs)]
    if j['id']=='outdoor_table_chair_set_01':
        groups=[('outdoor_cafe_table','야외 카페 테이블','Surface',[o for o in objs if '_table' in o.name and '_chair_' not in o.name.split('set_01')[-1]]),('outdoor_cafe_chair','야외 카페 의자','Seating',[o for o in objs if o.name.endswith('chair_01')])]
        groups[0]=(groups[0][0],groups[0][1],groups[0][2],[o for o in objs if o.name.endswith('_table')])
    for key,label,cat,group in groups:
        out=asset/'Models'/cat/key;out.mkdir(parents=True,exist_ok=True)
        tdir=out/'Textures';tdir.mkdir(exist_ok=True)
        bpy.ops.object.select_all(action='DESELECT')
        for o in group:o.hide_set(False);o.hide_viewport=False;o.select_set(True)
        bpy.context.view_layer.objects.active=group[0]
        # Bake the world transform into mesh coordinates before normalizing the asset.
        for o in group:
            mat=o.matrix_world.copy();o.parent=None;o.matrix_world=mat
        bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
        before=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in group)
        if before>80000:
            for o in group:
                bpy.context.view_layer.objects.active=o
                mod=o.modifiers.new('Unity copy reduction','DECIMATE');mod.ratio=80000/before
                bpy.ops.object.modifier_apply(modifier=mod.name)
        pts=[o.matrix_world@Vector(v) for o in group for v in o.bound_box]
        low=Vector(tuple(min(p[i] for p in pts) for i in range(3)));high=Vector(tuple(max(p[i] for p in pts) for i in range(3)))
        size=high-low;h=j['height']
        # Poly Haven models already use meters; preserve authored dimensions.
        scale=1 if j['input'].endswith('.blend') else h/max(size.z,.001)
        origin=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
        for o in group:
            for v in o.data.vertices:v.co=(v.co-origin)*scale
            o.data.update()
        materials=[]
        unique=list(dict.fromkeys(m for o in group for m in o.data.materials if m))
        if not unique:
            m=bpy.data.materials.new(key+'_Neutral');m.diffuse_color=(.55,.55,.55,1)
            for o in group:o.data.materials.append(m)
            unique=[m]
        for idx,m in enumerate(unique):
            original=m.name;m.name=key+'_M'+str(idx)
            bs=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None) if m.use_nodes else None
            info={'name':m.name,'originalName':original,'color':list(bs.inputs['Base Color'].default_value) if bs else list(m.diffuse_color),'metallic':float(bs.inputs['Metallic'].default_value) if bs else 0,'roughness':float(bs.inputs['Roughness'].default_value) if bs else .65}
            for field,socket in [('albedo','Base Color'),('normal','Normal'),('metallicMap','Metallic'),('roughnessMap','Roughness'),('emission','Emission Color')]:info[field]=save_image(tex_for(bs.inputs.get(socket)) if bs else None,tdir)
            info['alpha']=float(bs.inputs['Alpha'].default_value) if bs else 1
            materials.append(info)
        model=out/(key+'.fbx')
        bpy.ops.export_scene.fbx(filepath=str(model),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,path_mode='STRIP')
        tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in group)
        row={'id':key,'label':label,'category':cat,'functions':'Seating, WaterSource' if key=='toilet' else cat.split('/')[0] if not cat.startswith('Fixtures') else 'None','storage':cat.split('/')[1] if cat.startswith('Storage/') else 'None','placement':'Ceiling' if key=='Chandelier_02' else 'Floor','source':j['source'],'sourceHash':j['hash'],'modelPath':model.relative_to(root/'CompanyGame').as_posix(),'materials':materials,'triangles':tris,'originalTriangles':before,'dimensions':[size.x*scale,size.z*scale,size.y*scale],'note':'원본에 색상 텍스처 없음' if not any(m['albedo'] for m in materials) else '', 'reused':False}
        rows.append(row)
        (base/'new_catalog.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
        print('EXPORTED',key,before,tris,flush=True)
print('FINISHED',len(rows))
