"""Surface-boundary material regions for the audited HomeOffice seating/audio.

The caller supplies normalized aligned face centres and aligned face normals.
classify() only returns roles. The optional speaker preprocess splits existing
surface faces along fitted boundaries; it writes no files and preserves shape.
Large curvature patches provide semantic seeds; uncertain crease strips are
labelled along the mesh surface. Speaker driver circles are fitted to the
actual boundary of the planar front patch, not painted at guessed positions.
"""
import heapq
import math
from collections import defaultdict
import numpy as np

LAST_DIAGNOSTICS = {}
_AUDIO_FITS = {}


def _topology(mesh, normals, degrees):
    n = len(mesh.polygons)
    edges = defaultdict(list)
    for face in mesh.polygons:
        for edge in face.edge_keys:
            edges[tuple(sorted(edge))].append(face.index)
    adjacent = [[] for _ in range(n)]
    parent = list(range(n))
    def find(a):
        while a != parent[a]:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    cosine = math.cos(math.radians(degrees))
    for faces in edges.values():
        if len(faces) != 2:
            continue
        a, b = faces
        dot = float(np.dot(normals[a], normals[b]))
        adjacent[a].append((b, dot))
        adjacent[b].append((a, dot))
        if dot >= cosine:
            aa, bb = find(a), find(b)
            if aa != bb:
                parent[bb] = aa
    groups = defaultdict(list)
    for i in range(n):
        groups[find(i)].append(i)
    area = np.asarray([f.area for f in mesh.polygons], dtype=float)
    patches = sorted(groups.values(), key=lambda g: float(area[g].sum()), reverse=True)
    return adjacent, patches, area, edges


def _watershed(mesh, adjacent, seeds, fallback):
    """Multi-source surface-distance watershed; expensive crossings at folds."""
    centers = np.asarray([tuple(f.center) for f in mesh.polygons], dtype=float)
    distance = np.full(len(centers), np.inf)
    result = list(fallback)
    queue = []
    for i, role in seeds.items():
        distance[i] = 0.
        result[i] = role
        queue.append((0., i))
    heapq.heapify(queue)
    while queue:
        current, i = heapq.heappop(queue)
        if current != distance[i]:
            continue
        for j, dot in adjacent[i]:
            delta = centers[j] - centers[i]
            length = math.sqrt(float(np.dot(delta, delta)))
            bend = max(0., 1. - min(1., dot))
            cost = max(length, 1e-9) * (1. + 130. * bend ** .60)
            candidate = current + cost
            if candidate < distance[j]:
                distance[j] = candidate
                result[j] = result[i]
                heapq.heappush(queue, (candidate, j))
    return result


def _chair(mesh, centers, normals, initial):
    adjacent, patches, areas, _ = _topology(mesh, normals, 10.)
    # These are the measured curvature patches of this catalog model, ordered
    # by surface area. Each was inspected in an isolated orange/gray preview.
    # Large seeds follow whole surfaces, including the shaped arms and legs.
    audited_roles = {
        0: 'Oak',       # near structural arm frame and its leg
        1: 'Linen',     # complete large upholstered back cushion
        2: 'Linen',     # opposite padded arm and main seat
        3: 'Oak',       # outside of curved rear wood shell
        4: 'Oak',       # base rails and opposite legs
        5: 'Terracotta',# separate central accent pillow
        6: 'Oak',       # inside of rear support shell
        7: 'Linen',     # near padded arm
        8: 'Oak',       # rear structural support
        9: 'Oak',       # opposite frame return
        10: 'Oak',      # lower rail upper surface
        11: 'Oak',      # opposite structural return
        12: 'Linen',    # exposed seat behind the accent pillow
    }
    total = max(float(areas.sum()), 1e-12)
    # A changed source should not silently inherit unrelated patch identities.
    expected = [.18534, .16384, .11317, .11250, .08690, .04631, .04625, .04350]
    signature = [float(areas[g].sum()) / total for g in patches[:8]]
    matched = len(signature) == 8 and all(abs(a-b) < .009 for a,b in zip(signature, expected))
    if not matched:
        raise ValueError('LoungeChair curvature signature changed; inspect source boundaries before recoloring: ' + repr(signature))
    seeds = {}
    for rank, role in audited_roles.items():
        for i in patches[rank]:
            seeds[i] = role
    result = _watershed(mesh, adjacent, seeds, ['Linen'] * len(initial))
    LAST_DIAGNOSTICS['LoungeChair'] = {'patchCount': len(patches), 'seedFaces': len(seeds), 'areaSignature': signature}
    return result


def _fit_circle(points, width, height, lower, anchor):
    """Robust fit to actual front-patch boundary vertices in a driver band."""
    z0, z1 = ((.17, .62) if lower else (.58, .94))
    r0, r1 = ((.24, .38) if lower else (.07, .145))
    center_low, center_high = ((.38, .46) if lower else (.70, .83))
    band = points[(points[:,1] > height*z0) & (points[:,1] < height*z1)]
    if len(band) < 8:
        raise ValueError('Not enough speaker front-boundary points to fit driver.')
    rng = np.random.default_rng(947 if lower else 953)
    tolerance = width * .010
    best = None
    for _ in range(2200):
        sample = band[rng.choice(len(band), 3, replace=False)]
        a, b, c = sample
        matrix = 2. * np.asarray([b-a, c-a])
        if abs(float(np.linalg.det(matrix))) < width*width*1e-5:
            continue
        center = np.linalg.solve(matrix, np.asarray([np.dot(b,b)-np.dot(a,a), np.dot(c,c)-np.dot(a,a)]))
        radius = float(np.linalg.norm(a-center))
        if not (width*r0 < radius < width*r1 and height*center_low < center[1] < height*center_high and abs(center[0]) < width*.30):
            continue
        if float(np.linalg.norm(center-anchor)) > width*.023:
            continue
        residual = np.abs(np.linalg.norm(band-center, axis=1)-radius)
        inliers = residual < tolerance
        if int(inliers.sum()) < 7:
            continue
        angle = np.arctan2(band[inliers,1]-center[1],band[inliers,0]-center[0])
        coverage = len(set(np.floor((angle+math.pi)/(2*math.pi)*24).astype(int)))
        score = coverage*3. + int(inliers.sum()) - float(residual[inliers].mean()/tolerance)
        if best is None or score > best[0]:
            best = (score, center, radius, inliers)
    if best is None:
        raise ValueError('Speaker driver circular boundary could not be fitted reliably.')
    _, center, radius, inliers = best
    for _ in range(3):
        q = band[inliers]
        matrix = np.column_stack((2*q[:,0], 2*q[:,1], np.ones(len(q))))
        solution = np.linalg.lstsq(matrix, np.sum(q*q,axis=1), rcond=None)[0]
        proposed = solution[:2]
        offset = proposed-anchor
        length = float(np.linalg.norm(offset))
        center = anchor + offset * min(1., width*.016/max(length,1e-12))
        radius = float(np.median(np.linalg.norm(q-center,axis=1)))
        inliers = np.abs(np.linalg.norm(band-center,axis=1)-radius) < tolerance
    return center, radius * .965, int(inliers.sum())



def _front_outline(points):
    """Convex rounded fascia contour measured from observed planar front faces."""
    points=sorted(set((float(p[0]),float(p[1])) for p in points))
    def cross(o,a,b):return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
    lower=[]
    for p in points:
        while len(lower)>=2 and cross(lower[-2],lower[-1],p)<=0:lower.pop()
        lower.append(p)
    upper=[]
    for p in reversed(points):
        while len(upper)>=2 and cross(upper[-2],upper[-1],p)<=0:upper.pop()
        upper.append(p)
    hull=np.asarray(lower[:-1]+upper[:-1])
    if len(hull)<6:raise ValueError('Speaker fascia contour is incomplete.')
    # Uniform perimeter sampling followed by a very small corner smoothing.
    closed=np.vstack((hull,hull[0]));length=np.linalg.norm(np.diff(closed,axis=0),axis=1)
    cumulative=np.concatenate(([0.],np.cumsum(length)))
    query=np.linspace(0.,cumulative[-1],64,endpoint=False)
    contour=np.column_stack([np.interp(query,cumulative,closed[:,j]) for j in range(2)])
    for _ in range(2):contour=.125*np.roll(contour,1,axis=0)+.75*contour+.125*np.roll(contour,-1,axis=0)
    return contour


def _inside_outline(points, contour, tolerance=1e-10):
    inside=np.ones(len(points),dtype=bool)
    for a,b in zip(contour,np.roll(contour,-1,axis=0)):
        d=b-a
        inside &= d[0]*(points[:,1]-a[1])-d[1]*(points[:,0]-a[0]) >= -tolerance
    return inside

def _speaker(key, mesh, centers, normals, initial):
    adjacent, patches, areas, edges = _topology(mesh, normals, 5.)
    physical = np.asarray([tuple(f.center) for f in mesh.polygons], dtype=float)
    vertices = np.asarray([tuple(v.co) for v in mesh.vertices], dtype=float)
    low, high = vertices.min(axis=0), vertices.max(axis=0)
    width, depth, height = high-low
    candidates = []
    for group in patches[:35]:
        normal = np.average(normals[group],axis=0,weights=areas[group])
        if normal[1] < -.8 and np.ptp(physical[group,2]) > height*.45:
            candidates.append(group)
    if not candidates:
        raise ValueError('Speaker planar front patch not found.')
    panel = max(candidates,key=lambda g:float(areas[g].sum()))
    origin = np.average(physical[panel],axis=0,weights=areas[panel])
    normal = np.average(normals[panel],axis=0,weights=areas[panel]);normal/=np.linalg.norm(normal)
    horizontal = np.cross([0.,0.,1.],normal);horizontal/=np.linalg.norm(horizontal)
    # Keep the horizontal origin at the enclosure center, while plane depth
    # remains measured from the actual planar surface.
    enclosure_center = (low+high)*.5
    boundary_ids = set()
    panel_set = set(panel)
    for edge, fs in edges.items():
        if sum(i in panel_set for i in fs) == 1:
            boundary_ids.update(edge)
    boundary = vertices[sorted(boundary_ids)]
    points = np.column_stack(((boundary-enclosure_center)@horizontal,boundary[:,2]-low[2]))
    projected = np.column_stack(((physical-enclosure_center)@horizontal,physical[:,2]-low[2]))
    plane_depth = (physical-origin)@normal
    circles = []
    for lower in (True,False):
        z0,z1 = ((.18,.61) if lower else (.63,.89))
        recessed = ((projected[:,1]>height*z0) & (projected[:,1]<height*z1) &
                    (np.abs(projected[:,0])<width*.30) &
                    (plane_depth < -width*.028) & (plane_depth > -depth*.22))
        if int(recessed.sum()) < 8:
            raise ValueError('Speaker recessed driver geometry was not found.')
        anchor = np.average(projected[recessed],axis=0,weights=areas[recessed]*(-plane_depth[recessed]))
        circles.append(_fit_circle(points,width,height,lower,anchor))
    # The two measured rims must remain distinct. Fitting near a weakly fused
    # bridge can overestimate a rim; preserve the observed separate drivers.
    distance=float(np.linalg.norm(circles[0][0]-circles[1][0]))
    total_radius=circles[0][1]+circles[1][1]
    factor=min(1.,(distance-height*.012)/total_radius)
    circles=[(c,r*factor,n) for c,r,n in circles]
    planar=((normals@normal)>.985) & (np.abs(plane_depth)<width*.026)
    for c,r,_ in circles:planar &= np.linalg.norm(projected-c,axis=1)>r*1.12
    outline_vertices=set()
    for i in np.flatnonzero(planar):outline_vertices.update(mesh.polygons[int(i)].vertices)
    front_vertices=vertices[sorted(outline_vertices)]
    outline=_front_outline(np.column_stack(((front_vertices-enclosure_center)@horizontal,front_vertices[:,2]-low[2])))
    cache_key = (int(mesh.as_pointer()), key)
    if cache_key in _AUDIO_FITS:
        fit = _AUDIO_FITS[cache_key]
        circles = fit['circles']
        outline = fit['outline']
        normal, origin, horizontal = fit['normal'], fit['origin'], fit['horizontal']
        enclosure_center, low = fit['enclosureCenter'], fit['low']
        projected = np.column_stack(((physical-enclosure_center)@horizontal,physical[:,2]-low[2]))
        plane_depth = (physical-origin)@normal
    else:
        _AUDIO_FITS[cache_key] = {'circles':circles,'normal':normal,'origin':origin,
            'horizontal':horizontal,'enclosureCenter':enclosure_center,'low':low,
            'width':width,'depth':depth,'height':height,'outline':outline}
    inside_panel = _inside_outline(projected,outline)
    result=['Oak' if inside_panel[i] and plane_depth[i]>-depth*.40 else 'Ivory' for i in range(len(initial))]
    # preprocess() has split the actual mesh along these fitted circles. The
    # resulting material boundaries follow new geometric edges without jagged
    # whole-triangle circle masks; the surface itself is not displaced.
    for center,radius,count in circles:
        inside = np.linalg.norm(projected-center,axis=1) <= radius + width*1e-7
        for i in np.flatnonzero(inside & (plane_depth > -depth*.40)):
            result[int(i)]='Charcoal'
    LAST_DIAGNOSTICS[key]={'frontPatchFaces':len(panel),'normal':normal.tolist(),
        'drivers':[{'center':c.tolist(),'radius':r,'boundaryInliers':n} for c,r,n in circles]}
    return result



def preprocess(mesh, key=None):
    """Split speaker surface faces at fitted driver circles before coordinates.

    Call only for SpeakerA/B, before reading vertices/face centres for classify.
    The original piecewise-planar surface is retained: added points lie on old
    edges, and connecting chords lie inside old faces. No FBX is written.
    A narrow preliminary refinement limits circle chord error; only faces
    touched by a driver circumference are refined, rather than the whole mesh.
    """
    import bmesh
    key = key or ('SpeakerB' if 'SpeakerB' in mesh.name else 'SpeakerA' if 'SpeakerA' in mesh.name else None)
    if key not in ('SpeakerA','SpeakerB'):
        return {'changed':False}
    normal = np.asarray([tuple(f.normal) for f in mesh.polygons],dtype=float)
    _speaker(key,mesh,None,normal,['Ivory']*len(mesh.polygons))
    fit = _AUDIO_FITS[(int(mesh.as_pointer()),key)]
    horizontal = fit['horizontal']; origin=fit['origin']; face_normal=fit['normal']
    enclosure_center=fit['enclosureCenter']; low=fit['low']; width=fit['width']; depth=fit['depth']
    before=(len(mesh.vertices),len(mesh.polygons))
    bm=bmesh.new();bm.from_mesh(mesh)
    tolerance=width*1e-7
    uv_cache={}
    def uv(vertex):
        if vertex not in uv_cache:
            co=np.asarray(tuple(vertex.co))
            uv_cache[vertex]=np.asarray([float(np.dot(co-enclosure_center,horizontal)),co[2]-low[2]])
        return uv_cache[vertex]
    def front(face):
        return float(np.dot(np.asarray(tuple(face.calc_center_median()))-origin,face_normal)) > -depth*.40
    def roots(a,b,center,radius):
        p=uv(a)-center;delta=uv(b)-uv(a)
        aa=float(np.dot(delta,delta));bb=2*float(np.dot(p,delta));cc=float(np.dot(p,p))-radius*radius
        discriminant=bb*bb-4*aa*cc
        if aa<1e-20 or discriminant < 0:return []
        root=math.sqrt(max(0.,discriminant))
        return sorted(set(t for t in ((-bb-root)/(2*aa),(-bb+root)/(2*aa)) if 1e-7<t<1-1e-7))
    # Cut the measured rounded fascia contour as well. Each half-plane cuts
    # only the remaining front polygon; this removes coarse painted zigzags
    # while keeping the exact original surface positions.
    previous_lines=[]
    for pa,pb in zip(fit['outline'],np.roll(fit['outline'],-1,axis=0)):
        direction=pb-pa
        clip_normals=np.asarray([[-(b-a)[1],(b-a)[0]] for a,b in previous_lines])
        clip_offsets=np.asarray([-np.dot(n,a) for n,(a,b) in zip(clip_normals,previous_lines)])
        def side(vertex):
            value=uv(vertex)-pa
            return float(direction[0]*value[1]-direction[1]*value[0])
        def candidate(face):
            if not front(face):return False
            centroid=np.mean([uv(v) for v in face.verts],axis=0)
            return len(previous_lines)==0 or bool(np.all(clip_normals@centroid+clip_offsets>=-tolerance))
        candidates=[face for face in bm.faces if candidate(face)]
        candidate_set=set(candidates)
        line_vertices=set()
        for face in candidates:
            for vertex in face.verts:
                if abs(side(vertex))<tolerance:line_vertices.add(vertex)
        for edge in list(bm.edges):
            if not any(face in candidate_set for face in edge.link_faces):continue
            a,b=edge.verts;sa,sb=side(a),side(b)
            if sa*sb>=0 or abs(sa-sb)<1e-15:continue
            t=sa/(sa-sb)
            if t<1e-7 or t>1-1e-7:continue
            old_a,old_b=a.co.copy(),b.co.copy()
            _,vertex=bmesh.utils.edge_split(edge,a,t);vertex.co=old_a.lerp(old_b,t);line_vertices.add(vertex)
        for face in candidates:
            crossings=[v for v in face.verts if v in line_vertices]
            if len(crossings)<2:continue
            a,b=max(((a,b) for ia,a in enumerate(crossings) for b in crossings[ia+1:]),key=lambda pair:(pair[0].co-pair[1].co).length_squared)
            if bm.edges.get((a,b)) is not None:continue
            try:bmesh.utils.face_split(face,a,b,use_exist=True)
            except ValueError:pass
        previous_lines.append((pa,pb))
    # Max chord length ~= 2.3% of cabinet width. The sagitta of these fitted
    # circle chords is below 0.1 mm for the 28 cm speakers.
    for center,radius,count in fit['circles']:
        for _ in range(5):
            long=set()
            for face in bm.faces:
                if not front(face):continue
                if any(roots(e.verts[0],e.verts[1],center,radius) for e in face.edges):
                    long.update(e for e in face.edges if e.calc_length()>width*.023)
            if not long:break
            bmesh.ops.subdivide_edges(bm,edges=list(long),cuts=1,use_grid_fill=True)
        circle_vertices=set()
        for vertex in bm.verts:
            if abs(float(np.linalg.norm(uv(vertex)-center))-radius) < tolerance:
                circle_vertices.add(vertex)
        for edge in list(bm.edges):
            if not any(front(face) for face in edge.link_faces):continue
            a,b=edge.verts;crossings=roots(a,b,center,radius)
            if not crossings:continue
            original_a=a.co.copy();original_b=b.co.copy();previous=0.;current_a=a
            for t in crossings:
                current_edge=bm.edges.get((current_a,b))
                if current_edge is None:break
                relative=(t-previous)/(1.-previous)
                new_edge,new_vertex=bmesh.utils.edge_split(current_edge,current_a,relative)
                new_vertex.co=original_a.lerp(original_b,t)
                circle_vertices.add(new_vertex);current_a=new_vertex;previous=t
        for face in list(bm.faces):
            if not front(face):continue
            crossings=[v for v in face.verts if v in circle_vertices]
            if len(crossings)<2:continue
            # A normal crossing has two boundary vertices. At tangent vertices,
            # select the farthest pair to avoid a zero-length chord.
            a,b=max(((a,b) for ia,a in enumerate(crossings) for b in crossings[ia+1:]),key=lambda pair:(pair[0].co-pair[1].co).length_squared)
            if bm.edges.get((a,b)) is not None:continue
            try:bmesh.utils.face_split(face,a,b,use_exist=True)
            except ValueError:pass
    bm.normal_update();bm.to_mesh(mesh);bm.free();mesh.update()
    # Imported corner normals cannot be interpolated safely across newly cut
    # polygon loops. Recompute smooth geometric normals to avoid stripe artefacts.
    mesh.normals_split_custom_set([(0.,0.,0.)]*len(mesh.loops))
    mesh.update()
    return {'changed':True,'verticesBefore':before[0],'verticesAfter':len(mesh.vertices),
            'facesBefore':before[1],'facesAfter':len(mesh.polygons)}

def classify(pack, key, mesh, coords, normals, initial_roles):
    """Return material roles for the owned catalog models, else pass through."""
    if pack != 'HomeOffice' or key not in ('LoungeChair','SpeakerA','SpeakerB'):
        return list(initial_roles)
    centers = np.asarray(coords,dtype=float)
    normal = np.asarray(normals,dtype=float)
    if key == 'LoungeChair':
        return _chair(mesh,centers,normal,initial_roles)
    return _speaker(key,mesh,centers,normal,initial_roles)

