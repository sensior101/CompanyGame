"""Independent exported-file QA. Every model, palette and FBX/GLB is reimported.
May be rerun after repairs; reports actual file geometry, not recipe constants.
"""
import bpy,json,hashlib,math,sys
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree
from collections import Counter
HERE=Path(__file__).resolve().parent
variants='--material-variants' in sys.argv
styles=['WhiteWood','Eclectic','MarbleGold','ChicBlack'] if variants else ['Cream']
if variants:
 import importlib.util
 module_spec=importlib.util.spec_from_file_location('household_authoring',HERE/'build_household.py');authoring=importlib.util.module_from_spec(module_spec);module_spec.loader.exec_module(authoring)
catalog=json.loads((HERE/'RequestedCatalog.json').read_text(encoding='utf8'))
subset=None
if '--only' in sys.argv:
 subset=sys.argv[sys.argv.index('--only')+1].split(',')
 catalog=[e for e in catalog if e['id'] in subset]
rows=[];failures=[];hashes={}
for entry in catalog:
 id=entry['id'];manifest_path=HERE/'QA'/id/'Manifest.json'
 if not manifest_path.exists():failures.append({'id':id,'error':'Missing manifest'});continue
 expected=json.loads(manifest_path.read_text(encoding='utf8'))
 # Measure actual source vertices: rotated bevelled parts occupy less space
 # than the transformed eight-corner bounding box used in early manifests.
 bpy.ops.wm.read_factory_settings(use_empty=True)
 with bpy.data.libraries.load(str(HERE/'Models'/(id+'.blend')),link=False) as (src,dst):
  dst.collections=[id+'_ASSET']
 source=dst.collections[0];bpy.context.scene.collection.children.link(source);bpy.context.view_layer.update()
 source_signature=authoring.material_geometry_signature(source) if variants else None
 source_points=[o.matrix_world@v.co for o in source.objects if o.type=='MESH' for v in o.data.vertices]
 source_unique=list(dict.fromkeys(tuple(v) for v in source_points));source_index={v:i for i,v in enumerate(source_unique)}
 tree=KDTree(len(source_unique))
 for i,v in enumerate(source_unique):tree.insert(v,i)
 tree.balance()
 # Touching parts can differ by only float roundoff. Canonicalize coincident
 # coordinates within 2 micrometres so an import does not select a different
 # duplicate vertex ID at the same physical location.
 parent=list(range(len(source_unique)))
 def canonical(i):
  while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
  return i
 for i,v in enumerate(source_unique):
  for _,j,_ in tree.find_range(v,.000002):
   a,b=canonical(i),canonical(j)
   if a!=b:parent[max(a,b)]=min(a,b)
 source_triangles=Counter();source_uv={}
 for o in source.objects:
  if o.type!='MESH':continue
  for face in o.data.polygons:
   pairs=sorted((canonical(source_index[tuple(o.matrix_world@o.data.vertices[o.data.loops[l].vertex_index].co)]),tuple(o.data.uv_layers.active.data[l].uv)) for l in face.loop_indices)
   key=tuple(i for i,uv in pairs);source_triangles[key]+=1;source_uv.setdefault(key,[]).append([uv for i,uv in pairs])
 lo=[min(v[i] for v in source_points) for i in range(3)];hi=[max(v[i] for v in source_points) for i in range(3)]
 expected['bounds_min']=lo;expected['bounds_max']=hi;expected['dimensions']=[hi[i]-lo[i] for i in range(3)]
 expected['palettes']=styles
 if not variants:manifest_path.write_text(json.dumps(expected,ensure_ascii=False,indent=2),encoding='utf8')
 for palette in styles:
  variant_folder=HERE/'MaterialVariants'/palette/id
  material_count=json.loads((variant_folder/'Manifest.json').read_text(encoding='utf8'))['material_count'] if variants else len(expected['roles'])
  if variants:
   bpy.ops.wm.read_factory_settings(use_empty=True)
   with bpy.data.libraries.load(str(variant_folder/(id+'_'+palette+'.blend')),link=False) as (src,dst):dst.collections=[id+'_ASSET']
   variant_collection=dst.collections[0];bpy.context.scene.collection.children.link(variant_collection);bpy.context.view_layer.update()
   assert authoring.material_geometry_signature(variant_collection)==source_signature,(id,palette,'Saved Blender geometry changed')
   saved_images={n.image for o in variant_collection.objects if o.type=='MESH' for m in o.data.materials if m and m.use_nodes for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image}
   assert all(im.packed_file for im in saved_images),(id,palette,'Unpacked Blender image')
  for ext in ('fbx','glb'):
   path=(variant_folder if variants else HERE/'Exports'/id)/(id+'_'+palette+'.'+ext)
   try:
    assert path.exists(),'Missing export'
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if ext=='fbx':bpy.ops.import_scene.fbx(filepath=str(path))
    else:bpy.ops.import_scene.gltf(filepath=str(path))
    bpy.context.view_layer.update()
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    pts=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    low=[min(v[i] for v in pts) for i in range(3)];high=[max(v[i] for v in pts) for i in range(3)]
    dims=[high[i]-low[i] for i in range(3)]
    tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
    assert all(math.isfinite(v) for p in pts for v in p),'Non-finite vertex'
    assert len(meshes)==expected['meshes'],('Mesh count',len(meshes),expected['meshes'])
    assert tris==expected['triangles'],('Triangles',tris,expected['triangles'])
    assert all(abs(a-b)<.0002 for a,b in zip(dims,expected['dimensions'])),('Dimensions',dims,expected['dimensions'])
    assert all(abs(a-b)<.0002 for a,b in zip(low,expected['bounds_min'])),('Origin',low,expected['bounds_min'])
    assert all(o.data.uv_layers for o in meshes),'Missing UVs'
    assert all(len(p.vertices)==3 and p.area>1e-11 for o in meshes for p in o.data.polygons),'Degenerate or non-triangular face'
    materials={m.name for o in meshes for m in o.data.materials if m}
    assert len(materials)==material_count,('Material count',len(materials),material_count)
    imported_triangles=Counter();maximum_uv_error=0.
    for o in meshes:
     vertex_ids=[]
     for v in o.data.vertices:
      co,index,distance=tree.find(o.matrix_world@v.co);assert distance<.00002,('Vertex moved',distance);vertex_ids.append(canonical(index))
     for face in o.data.polygons:
      pairs=sorted((vertex_ids[o.data.loops[l].vertex_index],tuple(o.data.uv_layers.active.data[l].uv)) for l in face.loop_indices)
      key=tuple(i for i,uv in pairs);imported_triangles[key]+=1
      assert key in source_uv,'Triangle topology changed'
      error=min(max(abs(a-b) for (_,uv),other in zip(pairs,candidate) for a,b in zip(uv,other)) for candidate in source_uv[key]);maximum_uv_error=max(maximum_uv_error,error)
    assert imported_triangles==source_triangles,'Triangle topology changed'
    assert maximum_uv_error<.00002,('UV changed',maximum_uv_error)
    texture_images={n.image for o in meshes for m in o.data.materials if m and m.use_nodes for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image}
    assert all(im.size[0]>0 and im.size[1]>0 for im in texture_images),'Missing image texture'
    if variants and texture_images:
     content=path.read_bytes()
     if ext=='fbx':assert b'\x89PNG\r\n\x1a\n' in content,'FBX texture not embedded'
     else:
      import struct
      length=struct.unpack_from('<I',content,12)[0];gltf=json.loads(content[20:20+length])
      assert all('bufferView' in im for im in gltf.get('images',[])),'GLB texture references external file'
    # Vertex positions in world space are independent of imported object names/order.
    position_hash=hashlib.sha256(json.dumps(sorted(set(tuple(round(c,5) for c in p) for p in pts))).encode()).hexdigest()
    key=id+'.'+ext
    if key in hashes:assert hashes[key]==position_hash,'Palette geometry differs'
    else:hashes[key]=position_hash
    rows.append(dict(id=id,palette=palette,format=ext,triangles=tris,dimensions=dims,topology_preserved=True,maximum_uv_error=maximum_uv_error,textures=len(texture_images),textures_embedded=variants,passed=True))
   except Exception as e:
    failures.append(dict(id=id,palette=palette,format=ext,error=str(e)))
 print('QA_ITEM',id,'passes',len(rows),'failures',len(failures),flush=True)
report={'expected_models':len(catalog),'expected_variants':len(catalog)*len(styles),'expected_exports':len(catalog)*len(styles)*2,'passed_exports':len(rows),'failures':failures,'results':rows,'UnityPlayModeVerified':False}
report_path=HERE/('MaterialVariants/RoundTripReport_Partial.json' if subset else 'MaterialVariants/RoundTripReport.json') if variants else HERE/('QA/RoundTripReport_Partial.json' if subset else 'QA/RoundTripReport.json')
report_path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print('HOUSEHOLD_QA_RESULT',json.dumps({k:v for k,v in report.items() if k!='results'},ensure_ascii=False),flush=True)
assert len(rows)==len(catalog)*len(styles)*2 and not failures,'See RoundTripReport.json'
