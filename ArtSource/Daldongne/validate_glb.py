"""Read-only glTF geometry validation using only the Python standard library."""
import collections, json, math, pathlib, struct, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
path = pathlib.Path(sys.argv[1]) if len(sys.argv)>1 else ROOT / 'CompanyGame/Assets/Art/Daldongne/DaldongneTown.glb'
raw = path.read_bytes()
magic,version,length = struct.unpack_from('<4sII',raw)
assert magic == b'glTF' and version == 2 and length == len(raw)
chunks=[]; offset=12
while offset<len(raw):
    size,kind=struct.unpack_from('<II',raw,offset); offset+=8
    assert offset+size<=len(raw)
    chunks.append((kind,raw[offset:offset+size])); offset+=size
doc=json.loads(next(data for kind,data in chunks if kind==0x4E4F534A))
binary=next(data for kind,data in chunks if kind==0x004E4942)
assert len(doc['buffers'])==1 and doc['buffers'][0]['byteLength']<=len(binary)
views=doc['bufferViews']; accessors=doc['accessors']; errors=[]
components={5120:('b',1),5121:('B',1),5122:('h',2),5123:('H',2),5125:('I',4),5126:('f',4)}
widths={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT2':4,'MAT3':9,'MAT4':16}
for i,v in enumerate(views):
    assert v.get('buffer',0)==0
    assert v.get('byteOffset',0)+v['byteLength']<=doc['buffers'][0]['byteLength'],('view out of range',i)
values=[]; float_total=0
for i,a in enumerate(accessors):
    assert 'sparse' not in a,('unexpected sparse accessor',i)
    v=views[a['bufferView']]; char,byte=components[a['componentType']]; n=widths[a['type']]
    assert a.get('byteOffset',0)%byte==0
    stride=v.get('byteStride',n*byte)
    assert stride>=n*byte
    span=a.get('byteOffset',0)+(a['count']-1)*stride+n*byte if a['count'] else 0
    assert span<=v['byteLength'],('accessor overflow',i)
    start=v.get('byteOffset',0)+a.get('byteOffset',0)
    vals=[struct.unpack_from('<'+char*n,binary,start+k*stride) for k in range(a['count'])]
    if char=='f':
        assert all(math.isfinite(x) for tup in vals for x in tup),('non-finite float',i)
        float_total+=len(vals)*n
    if a['count'] and 'min' in a:
        actual_min=[min(v[k] for v in vals) for k in range(n)]
        actual_max=[max(v[k] for v in vals) for k in range(n)]
        assert all(abs(actual_min[k]-a['min'][k])<=max(1e-5,abs(actual_min[k])*1e-6) for k in range(n)),('min mismatch',i)
        assert all(abs(actual_max[k]-a['max'][k])<=max(1e-5,abs(actual_max[k])*1e-6) for k in range(n)),('max mismatch',i)
    values.append(vals)

def ident(): return [[float(i==j) for j in range(4)] for i in range(4)]
def mul(a,b): return [[sum(a[i][k]*b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
def local(node):
    if 'matrix' in node: return [[node['matrix'][j*4+i] for j in range(4)] for i in range(4)]
    t=node.get('translation',[0,0,0]); s=node.get('scale',[1,1,1]); x,y,z,w=node.get('rotation',[0,0,0,1])
    result=ident()
    r=[[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],
       [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],
       [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]]
    for i in range(3):
        result[i][3]=t[i]
        for j in range(3):result[i][j]=r[i][j]*s[j]
    assert all(math.isfinite(x) for row in result for x in row)
    return result
def transform(mat,p):return [sum(mat[i][j]*p[j] for j in range(3))+mat[i][3] for i in range(3)]
mesh_triangles=[]; unique_triangles=0
for mi,mesh in enumerate(doc['meshes']):
    triangles=0
    for p in mesh['primitives']:
        attrs=p['attributes']; count=accessors[attrs['POSITION']]['count']
        assert all(accessors[a]['count']==count for a in attrs.values()),('attribute count mismatch',mi)
        if 'indices' in p:
            aa=accessors[p['indices']]
            assert aa['componentType'] in (5121,5123,5125) and aa['type']=='SCALAR'
            ii=[v[0] for v in values[p['indices']]]
            assert all(0<=k<count for k in ii),('invalid index',mi)
            n=len(ii)
        else:n=count
        mode=p.get('mode',4)
        if mode==4:
            assert n%3==0,('incomplete triangle',mi)
            triangles+=n//3
        elif mode in (5,6):triangles+=max(0,n-2)
    mesh_triangles.append(triangles); unique_triangles+=triangles

visited=set(); names=[]; bounds={}; world_min=[math.inf]*3; world_max=[-math.inf]*3
material_counts=collections.Counter(); instance_triangles=0
def walk(ni,parent):
    global instance_triangles
    assert ni not in visited,('cycle or duplicate active node',ni)
    visited.add(ni); node=doc['nodes'][ni]; mat=mul(parent,local(node)); name=node.get('name',str(ni)); names.append(name)
    if 'mesh' in node:
        instance_triangles+=mesh_triangles[node['mesh']]
        lo=[math.inf]*3; hi=[-math.inf]*3
        for p in doc['meshes'][node['mesh']]['primitives']:
            material_counts[doc['materials'][p['material']]['name']]+=1
            for v in values[p['attributes']['POSITION']]:
                v=transform(mat,v)
                for k in range(3):lo[k]=min(lo[k],v[k]); hi[k]=max(hi[k],v[k])
        for k in range(3):world_min[k]=min(world_min[k],lo[k]); world_max[k]=max(world_max[k],hi[k])
        if name.startswith(('Terrain_','Road_','Walk_Bridge','Bridge_Pier','Station_','Support_Stair','Walk_Stair_ShoppingAccess')):
            bounds[name]={'min':lo,'max':hi}
    for child in node.get('children',[]):walk(child,mat)
for node in doc['scenes'][doc.get('scene',0)]['nodes']:walk(node,ident())
report={'file':str(path),'bytes':len(raw),'nodes':len(doc['nodes']),'active_nodes':len(visited),
        'meshes':len(doc['meshes']),'accessors':len(accessors),'buffer_views':len(views),
        'checked_finite_float_components':float_total,'unique_mesh_triangles':unique_triangles,
        'instanced_triangles':instance_triangles,'world_bounds_gltf_y_up':{'min':world_min,'max':world_max},
        'material_primitive_instances':dict(material_counts),'selected_node_bounds':bounds,
        'all_object_names':names,'validation':'PASS'}
out=pathlib.Path(sys.argv[2]) if len(sys.argv)>2 else pathlib.Path(__file__).with_name('glb_validation.json');out.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ('selected_node_bounds','all_object_names')},ensure_ascii=False,indent=2))
