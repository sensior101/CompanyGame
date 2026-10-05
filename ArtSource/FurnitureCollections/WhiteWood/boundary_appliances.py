"""Reviewed surface graph-cut masks for the washer and wall clock.

The masks preserve source topology. Geometry and normalized coordinates are
checked before assigning them so a changed FBX cannot inherit stale face IDs.
"""
import os
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
CACHE = os.path.join(HERE, 'ApplianceMasks')

# Measured outline of the existing small washer display inset. Only this
# sparse front patch needs new material edges; the remaining topology stays.
DISPLAY = ((.149,.818),(.398,.818),(.412,.832),(.412,.900),(.398,.914),(.149,.914),(.137,.902),(.137,.830))

def preprocess(pack, key, mesh):
    if pack != 'Essentials' or key != 'Washer':
        return
    import bmesh
    from mathutils import Vector
    vertices=np.array([tuple(v.co) for v in mesh.vertices]);lo=vertices.min(0);size=np.ptp(vertices,axis=0)
    bm=bmesh.new();bm.from_mesh(mesh)
    for p,q in zip(DISPLAY,DISPLAY[1:]+DISPLAY[:1]):
        bm.verts.index_update();bm.edges.index_update();bm.faces.index_update()
        selected=[]
        for face in bm.faces:
            c=np.array([tuple(v.co) for v in face.verts]);c=(c-lo)/size-np.array([.5,.5,0])
            if c[:,0].max()>.09 and c[:,0].min()<.45 and c[:,2].max()>.79 and c[:,2].min()<.95 and c[:,1].min()<-.40:
                selected.append(face)
        edges={e for f in selected for e in f.edges};verts={v for f in selected for v in f.verts}
        a=Vector((lo[0]+(p[0]+.5)*size[0],0,lo[2]+p[1]*size[2]));b=Vector((lo[0]+(q[0]+.5)*size[0],0,lo[2]+q[1]*size[2]))
        normal=Vector((b.z-a.z,0,a.x-b.x)).normalized()
        bmesh.ops.bisect_plane(bm,geom=sorted(verts,key=lambda v:v.index)+sorted(edges,key=lambda e:e.index)+selected,dist=1e-7,plane_co=a,plane_no=normal,clear_inner=False,clear_outer=False)
    bmesh.ops.triangulate(bm,faces=[f for f in bm.faces if len(f.verts)>3]);bm.normal_update();bm.to_mesh(mesh);bm.free();mesh.update()

def classify(pack, key, mesh, coords, normals, initial_roles):
    if pack != 'Essentials' or key not in ('Washer', 'WallClock'):
        return list(initial_roles)
    path = os.path.join(CACHE, pack + '_' + key + '.npz')
    data = np.load(path)
    if int(data['face_count']) != len(mesh.polygons) or not np.allclose(data['coords'], coords, atol=1e-5):
        raise ValueError('Reviewed appliance mask does not match source geometry: ' + key)
    return data['roles'].tolist()

def solve(key):
    import sys
    root = os.path.abspath(os.path.join(HERE, '../../..'))
    sys.path.insert(0, os.path.join(root, 'CompanyGame/Temp/BoundaryLib'))
    from mesh_cut import Surface
    data = np.load(os.path.join(root, 'CompanyGame/Temp/ApplianceAudit', key + '.npz'))
    surface = Surface(data)
    x,y,z=surface.c.T
    nx,ny,nz=surface.n.T
    roles=np.full(surface.count,'Ivory' if key=='Washer' else 'Oak',dtype='<U20')
    def apply(role, positive, negative, prior):
        mask=surface.cut(positive & ~negative, negative, prior, prior_weight=.3)
        roles[mask]=role
        return mask
    if key=='WallClock':
        r=np.sqrt(x*x+(z-.497)**2)
        apply('Ivory', (y>-.347)&(y<-.325)&(ny<-.98)&(r<.46),
              (y<-.356)|(y>-.29)|(r>.487)|((ny>-.8)&(y<-.32)), (y>-.349)&(y<-.31)&(r<.467))
    else:
        r=np.sqrt(((x+.003)/.355)**2+((z-.49)/.30)**2)
        door=apply('Metal', ((y<-.47)&(z<.78)&(z>.19))|((r<.55)&(y<-.30)),
                   (r>1.16)|(y>-.28)|(z>.805)|(z<.155), (r<1.035)&(y<-.30))
        apply('Glass', (r<.60)&(y>-.36)&(y<-.29),
              ~door|(r>.83)|(y<-.465),door&(r<.78)&(y>-.455))
        inside=np.ones(surface.count,dtype=bool)
        for p,q in zip(DISPLAY,DISPLAY[1:]+DISPLAY[:1]):
            inside &= (q[0]-p[0])*(z-p[1])-(q[1]-p[1])*(x-p[0]) >= -1e-7
        roles[inside&(y<-.4)]='Screen'
    os.makedirs(CACHE,exist_ok=True)
    np.savez_compressed(os.path.join(CACHE,'Essentials_'+key+'.npz'),roles=roles,coords=surface.c,face_count=surface.count)
    print(key,dict(zip(*np.unique(roles,return_counts=True))),flush=True)

if __name__=='__main__':
    for name in ['Washer','WallClock']:
        solve(name)
