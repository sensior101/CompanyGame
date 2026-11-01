"""Preserve Meshy's generated bodies and extend its humanoid rig for Unity.

Run in a separate Blender background process, never in an authored open scene.
Source GLBs remain immutable. QA renders and measurements go into the QA folder.
"""
from pathlib import Path
import bpy
import json
import sys
import numpy as np
import bmesh
from mathutils import Vector

HERE = Path(__file__).resolve().parent
SOURCE = HERE / 'MeshyArticulated_20261028'
QA = SOURCE / 'QA'


def load(sex):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE / f'{sex}Rigged.glb'))
    armature = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    body = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    return armature, body


def render(name, center, direction, size):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = 'BOTH'
    scene.display.shading.background_type = 'WORLD'
    if scene.world is None:
        scene.world = bpy.data.worlds.new('QAWorld')
    scene.world.color = (.12, .12, .12)
    camera_data = bpy.data.cameras.new(name)
    camera = bpy.data.objects.new(name, camera_data)
    scene.collection.objects.link(camera)
    camera.location = Vector(center) + Vector(direction).normalized() * 5
    camera.rotation_euler = (Vector(center)-camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = size
    scene.camera = camera
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(QA / f'{name}.png')
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera, do_unlink=True)


def inspect(sex):
    armature, body = load(sex)
    points = [body.matrix_world @ v.co for v in body.data.vertices]
    bones = {b.name: {'head': list(armature.matrix_world @ b.head_local),
                      'tail': list(armature.matrix_world @ b.tail_local),
                      'parent': b.parent.name if b.parent else None}
             for b in armature.data.bones}
    result = {'model': sex, 'bones': bones,
              'bounds': [[min(v[i] for v in points), max(v[i] for v in points)] for i in range(3)]}
    for side in ['Left', 'Right']:
        hand = Vector(bones[side+'Hand']['head'])
        sign = 1 if hand.x > 0 else -1
        verts = [list(p) for p in points if p.x*sign > hand.x*sign-.025]
        result[side+'HandVertices'] = verts
        center = Vector((sum(v[0] for v in verts)/len(verts), sum(v[1] for v in verts)/len(verts), sum(v[2] for v in verts)/len(verts)))
        size = max(max(v[i] for v in verts)-min(v[i] for v in verts) for i in range(3)) * 1.3
        render(sex+side+'HandTop', center, (0,0,1), size)
        render(sex+side+'HandFront', center, (0,-1,.15), size)
    (QA / f'{sex}-inspection.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({'model':sex,'bounds':result['bounds'],'bones':len(bones)}, ensure_ascii=False))


def landmarks(armature, body, side):
    points=np.array([body.matrix_world @ v.co for v in body.data.vertices])
    wrist=np.array(armature.matrix_world @ armature.data.bones[side+'Hand'].head_local)
    sign=1 if wrist[0]>0 else -1
    outward=points[:,0]*sign
    length=outward.max()-abs(wrist[0])
    hand=points[outward>abs(wrist[0])+length*.20]
    lower,upper=hand[:,1].min(),hand[:,1].max()
    distal=hand[(hand[:,0]*sign>abs(wrist[0])+length*.68) & (hand[:,1]>lower+(upper-lower)*.30)]
    centers=np.quantile(distal[:,1],[.12,.37,.63,.88])
    for _ in range(30):
        labels=np.argmin(abs(distal[:,1,None]-centers[None,:]),axis=1)
        centers=np.array([distal[labels==i,1].mean() for i in range(4)])
    centers.sort()
    labels=np.argmin(abs(distal[:,1,None]-centers[None,:]),axis=1)
    output={}
    for i,name in enumerate(['Index','Middle','Ring','Pinky']):
        group=distal[labels==i]
        tipcloud=group[group[:,0]*sign>(group[:,0]*sign).max()-.006]
        tip=tipcloud.mean(axis=0)
        tip[0]=sign*(group[:,0]*sign).max()
        base=np.array([sign*(abs(wrist[0])+length*.50),tip[1]*.85+wrist[1]*.15,tip[2]])
        output[name]=(Vector(base),Vector(tip))
    thumb=hand[hand[:,1]<centers[0]-.015]
    direction=np.array([sign*.65,-.75,0.0])
    projected=thumb@direction
    tip=thumb[projected>projected.max()-.006].mean(axis=0)
    base=wrist+np.array([sign*length*.27,-.035,-.004])
    # Generated fused thumb clouds may include an adjacent finger tip. Bound the
    # thumb by the measured index to keep a usable opposition arc and silhouette.
    vector=tip-base
    thumb_length=min(np.linalg.norm(vector),(output['Index'][1]-output['Index'][0]).length*.74)
    tip=base+vector/np.linalg.norm(vector)*thumb_length
    output['Thumb']=(Vector(base),Vector(tip))
    return output


def add_fingers(armature, body, all_landmarks):
    bpy.ops.object.select_all(action='DESELECT')
    armature.select_set(True);bpy.context.view_layer.objects.active=armature
    bpy.ops.object.mode_set(mode='EDIT')
    # glTF import guesses display lengths 100x too long; shorten without changing axes.
    for bone in armature.data.edit_bones:
        bone.tail=bone.head+(bone.tail-bone.head)*.01
    inv=armature.matrix_world.inverted()
    for side,fingers in all_landmarks.items():
        for name,(base,tip) in fingers.items():
            parent=armature.data.edit_bones[side+'Hand']
            for i,(start,end) in enumerate(zip([0,.40,.72],[.40,.72,1])):
                bone=armature.data.edit_bones.new(side+'Hand'+name+str(i+1))
                bone.head=inv@(base+(tip-base)*start)
                bone.tail=inv@(base+(tip-base)*end)
                bone.parent=parent
                bone.use_connect=i>0
                bone.align_roll(Vector((0,0,1)))
                parent=bone
    bpy.ops.object.mode_set(mode='OBJECT')
    for side,fingers in all_landmarks.items():
        for name in fingers:
            for i in range(1,4):body.vertex_groups.new(name=side+'Hand'+name+str(i))


def repair_female_bra_materials(body, texture):
    """Correct only the existing female bra's material assignments.

    Vertices on a UV boundary are poor representatives of a whole triangle.
    Sample its interior, then remove disconnected colour flecks and enclosed
    pinholes. Virtual edge matching crosses UV seams without welding the mesh.
    Edge lengths and texture support distinguish narrow neckline slivers from
    shoulder straps, independently of the hand processing's face IDs.
    """
    mesh = body.data
    points = np.array([body.matrix_world @ vertex.co for vertex in mesh.vertices])
    centres = np.array([points[list(face.vertices)].mean(axis=0) for face in mesh.polygons])
    region = {i for i, (x, y, z) in enumerate(centres) if abs(x) < .24 and 1.08 < z < 1.46}
    width, height = texture.size
    pixels = np.array(texture.pixels[:]).reshape(height, width, 4)
    uv = mesh.uv_layers.active.data
    barycentric = np.array([(i+1, j+1, 13-i-j) for i in range(13) for j in range(13-i)], dtype=float) / 15
    before = [face.material_index for face in mesh.polygons]
    assigned = before.copy()
    support = np.zeros(len(mesh.polygons))
    for index in region:
        face = mesh.polygons[index]
        if len(face.vertices) != 3:
            raise ValueError('Female bra correction requires the existing triangulated model.')
        samples = barycentric @ np.array([uv[loop].uv[:] for loop in face.loop_indices])
        colour = pixels[np.clip((samples[:, 1]*height).astype(int), 0, height-1),
                        np.clip((samples[:, 0]*width).astype(int), 0, width-1), :3]
        support[index] = np.mean(colour.max(axis=1)-colour.min(axis=1) < .065)
        assigned[index] = int(support[index] > .5)

    keys = [tuple(np.round(point, 6)) for point in points]
    edge_faces = {}
    face_edges = [[] for face in mesh.polygons]
    edge_lengths = {}
    for face in mesh.polygons:
        vertices = list(face.vertices)
        for a, b in zip(vertices, vertices[1:]+vertices[:1]):
            edge = tuple(sorted((keys[a], keys[b])))
            edge_faces.setdefault(edge, []).append(face.index)
            face_edges[face.index].append(edge)
            edge_lengths[edge] = float(np.linalg.norm(points[a]-points[b]))
    adjacent = [set() for face in mesh.polygons]
    for faces in edge_faces.values():
        for index in faces:
            adjacent[index].update(other for other in faces if other != index)

    def components(indices):
        pending = set(indices)
        groups = []
        while pending:
            seed = pending.pop(); group = {seed}; stack = [seed]
            while stack:
                neighbours = adjacent[stack.pop()] & pending
                pending.difference_update(neighbours); group.update(neighbours); stack.extend(neighbours)
            groups.append(group)
        return groups

    garment = max(components(i for i in region if assigned[i] == 1), key=len)
    for index in region:
        if assigned[index] == 1 and index not in garment:
            assigned[index] = 0
    for group in components(i for i in region if assigned[i] == 0):
        boundary = set().union(*(adjacent[index] for index in group)) - group
        closed = all(len(edge_faces[edge]) > 1 for index in group for edge in face_edges[index])
        if len(group) <= 2 and closed and boundary and all(assigned[index] == 1 for index in boundary):
            for index in group:
                assigned[index] = 1

    # Only narrow slivers with substantial opposite-colour edge contact and
    # ambiguous texture support. Broad neighbour voting would widen the straps.
    slivers = []
    for index in region:
        opposite = 1-assigned[index]
        neighbours = {other for other in adjacent[index] if assigned[other] == opposite}
        perimeter = sum(edge_lengths[edge] for edge in face_edges[index])
        contact = sum(edge_lengths[edge] for edge in face_edges[index]
                      if any(other != index and assigned[other] == opposite for other in edge_faces[edge]))
        ratio = contact / max(perimeter, 1e-12)
        if len(neighbours) == 2 and ((assigned[index] == 0 and ratio >= .85 and support[index] >= .30)
                or (assigned[index] == 1 and ratio >= .90 and support[index] <= .80)):
            slivers.append((index, opposite))
    for index, material in slivers:
        assigned[index] = material
    changed = []
    for face, material in zip(mesh.polygons, assigned):
        if face.material_index != material:
            changed.append({'face': face.index, 'before': face.material_index, 'after': material})
            face.material_index = material
    return changed


def solid_materials(body, sex=None):
    texture=None
    for material in body.data.materials:
        bsdf=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        link=next(iter(bsdf.inputs['Base Color'].links),None)
        if link and link.from_node.type=='TEX_IMAGE':texture=link.from_node.image;break
    if texture is None:raise RuntimeError('Source base color texture missing')
    width,height=texture.size
    pixels=np.array(texture.pixels[:]).reshape(height,width,4)
    uv=body.data.uv_layers.active.data
    clothing=[]
    for face in body.data.polygons:
        samples=[]
        for loop in face.loop_indices:
            u,v=uv[loop].uv
            samples.append(pixels[min(height-1,max(0,int(v*height))),min(width-1,max(0,int(u*width))),:3])
        color=np.mean(samples,axis=0)
        clothing.append(float(max(color)-min(color))<.065)
    body.data.materials.clear()
    for name,color in [('Skin',(.793,.479,.337,1)),('Clothing',(.235,.235,.235,1))]:
        mat=bpy.data.materials.new(name);mat.diffuse_color=color
        mat.use_nodes=True;mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=color
        mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.82
        body.data.materials.append(mat)
    for face,is_clothing in zip(body.data.polygons,clothing):face.material_index=int(is_clothing)
    if sex == 'Female':
        repair_female_bra_materials(body, texture)
    return sum(face.material_index == 1 for face in body.data.polygons)


def refine_female_bra_boundaries(body):
    """Paint the reviewed garment faces up to the original modeled seams.

    Geometry keys survive Blender face reordering. A geometry digest prevents
    applying this authored selection to a different source mesh. No vertices,
    normals, UVs or skin weights are created or moved here.
    """
    import hashlib
    selection = json.loads((SOURCE / 'FemaleArticulated.bra-materials.json').read_text(encoding='utf-8'))
    precision = selection['coordinate_scale']
    positions = [body.matrix_world @ v.co for v in body.data.vertices]
    keys = {}
    for face in body.data.polygons:
        points = [positions[i] for i in face.vertices]
        center = sum(points, Vector()) / len(points)
        if not (1.08 < center.z < 1.46 and abs(center.x) < .24):
            continue
        key = tuple(sorted(tuple(round(float(v) * precision) for v in point) for point in points))
        if key in keys:
            raise RuntimeError('Duplicate triangle in female garment selection region')
        keys[key] = face
    digest = hashlib.sha256(json.dumps(sorted(keys), separators=(',', ':')).encode()).hexdigest()
    if digest != selection['region_geometry_sha256']:
        raise RuntimeError('Female garment geometry changed; review the authored seam selection before rebuilding')
    updates = []
    selected = set()
    for entry in selection['faces']:
        key = tuple(tuple(point) for point in entry['triangle'])
        if key not in keys or key in selected or entry['material'] not in (0, 1):
            raise RuntimeError('Invalid female garment material selection')
        selected.add(key)
        updates.append((keys[key], entry['material']))
    changed = sum(face.material_index != material for face, material in updates)
    for face, material in updates:
        face.material_index = material
    body.data.update()
    return {'method': 'authored original mesh seam faces', 'checked_region_faces': len(keys),
            'selected_faces': len(updates), 'changed_faces': changed,
            'vertices': len(body.data.vertices), 'triangles': len(body.data.polygons),
            'added_vertices': 0, 'added_triangles': 0, 'region_geometry_sha256': digest}


def rebuild_hand_surfaces(armature,body,all_landmarks):
    """Retopologize the fused generated digits, matching their measured silhouettes.

    Separate closed finger surfaces overlap the palm at their roots. Each surface
    has explicit bend rings, so independently curled fingers cannot drag neighbors.
    All surfaces remain in the same skinned renderer with the existing material slots.
    """
    bm=bmesh.new();bm.from_mesh(body.data)
    deform=bm.verts.layers.deform.verify()
    inv=body.matrix_world.inverted()

    def ring_surface(rings,weights,initial=None):
        loops=[initial] if initial else []
        for points,influences in zip(rings,weights):
            loop=[]
            for point in points:
                v=bm.verts.new(inv@point)
                for name,weight in influences.items():
                    v[deform][body.vertex_groups[name].index]=weight
                loop.append(v)
            loops.append(loop)
        sides=len(loops[0])
        for first,second in zip(loops,loops[1:]):
            center=sum((v.co for v in first+second),Vector())/(2*sides)
            for i in range(sides):
                face=bm.faces.new((first[i],first[(i+1)%sides],second[(i+1)%sides],second[i]))
                face.material_index=0
                face.normal_update()
                if face.normal.dot(face.calc_center_median()-center)<0:face.normal_flip()
        axis=sum((v.co for v in loops[-1]),Vector())/sides-sum((v.co for v in loops[0]),Vector())/sides
        if initial is None:
            cap=bm.faces.new(tuple(reversed(loops[0])));cap.material_index=0;cap.normal_update()
            if cap.normal.dot(axis)>0:cap.normal_flip()
        cap=bm.faces.new(tuple(loops[-1]));cap.material_index=0;cap.normal_update()
        if cap.normal.dot(axis)<0:cap.normal_flip()

    for side,fingers in all_landmarks.items():
        wrist=armature.matrix_world@armature.data.bones[side+'Hand'].head_local
        sign=1 if wrist.x>0 else -1
        cut=wrist+Vector((sign*.008,0,0))
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
            dist=1e-7,plane_co=inv@cut,
            plane_no=body.matrix_world.to_3x3().transposed()@Vector((sign,0,0)),
            clear_outer=True,clear_inner=False)
        boundary=[e for e in bm.edges if e.is_boundary and all(abs((body.matrix_world@v.co).x-cut.x)<.0001 for v in e.verts)]
        boundary_vertices=list({v for e in boundary for v in e.verts})
        bmesh.ops.remove_doubles(bm,verts=boundary_vertices,dist=.00001)
        boundary=[e for e in bm.edges if e.is_boundary and all(abs((body.matrix_world@v.co).x-cut.x)<.0001 for v in e.verts)]
        boundary_vertices=list({v for e in boundary for v in e.verts})
        if len(boundary_vertices)<8:raise RuntimeError('Incomplete wrist cross section: '+side)
        center=sum((body.matrix_world@v.co for v in boundary_vertices),Vector())/len(boundary_vertices)
        boundary_vertices.sort(key=lambda v:np.arctan2((body.matrix_world@v.co).z-center.z,(body.matrix_world@v.co).y-center.y))
        angles=[np.arctan2((body.matrix_world@v.co).z-center.z,(body.matrix_world@v.co).y-center.y) for v in boundary_vertices]
        base_x=sum(fingers[n][0].x for n in ['Index','Middle','Ring','Pinky'])/4
        ys=[fingers[n][0].y for n in ['Index','Middle','Ring','Pinky']]
        center_y=(min(ys)+max(ys))*.5
        palm_width=(max(ys)-min(ys))*.5+.012
        palm_rings=[]
        palm_weights=[]
        for fraction,width,depth in [(.40,palm_width*.85,.027),(.70,palm_width*.98,.025),(1.03,palm_width*.96,.018)]:
            center=Vector((wrist.x+(base_x-wrist.x)*fraction,
                           wrist.y+(center_y-wrist.y)*max(0,fraction),wrist.z+.002))
            palm_rings.append([center+Vector((0,np.cos(a)*width,np.sin(a)*depth)) for a in angles])
            palm_weights.append({side+'Hand':1.0})
        ring_surface(palm_rings,palm_weights,initial=boundary_vertices)
        for name,(base,tip) in fingers.items():
            axis=(tip-base).normalized()
            lateral=axis.cross(Vector((0,0,1))).normalized()
            vertical=lateral.cross(axis).normalized()
            radius=.014 if name=='Thumb' else (.010 if name=='Pinky' else .012)
            rings=[];weights=[]
            for t,r in [(-.06,1.05),(0,1.05),(.27,1.0),(.36,1.02),(.40,1.02),(.44,.98),(.63,.92),(.69,.91),(.72,.91),(.75,.88),(.93,.79),(1.0,.60),(1.025,.28)]:
                center=base+(tip-base)*t
                rings.append([center+(lateral*np.cos(a)+vertical*np.sin(a)*.84)*radius*r for a in np.arange(8)*np.pi/4])
                if t<.32:influences={1:1.0}
                elif t<.48:
                    b=(t-.32)/.16;influences={1:1-b,2:b}
                elif t<.64:influences={2:1.0}
                elif t<.80:
                    b=(t-.64)/.16;influences={2:1-b,3:b}
                else:influences={3:1.0}
                weights.append({side+'Hand'+name+str(i):w for i,w in influences.items()})
            ring_surface(rings,weights)
    # Source UV seams split many surface islands. Preserve their winding; a global
    # recalculate can flip open islands, which Unity correctly culls as backfaces.
    bm.normal_update()
    bmesh.ops.triangulate(bm,faces=list(bm.faces))
    bm.to_mesh(body.data);bm.free();body.data.update()


def build(sex):
    armature,body=load(sex)
    all_landmarks={side:landmarks(armature,body,side) for side in ['Left','Right']}
    clothing_faces=solid_materials(body, sex)
    add_fingers(armature,body,all_landmarks)
    rebuild_hand_surfaces(armature,body,all_landmarks)
    seam_report = refine_female_bra_boundaries(body) if sex == 'Female' else None
    clothing_faces = sum(face.material_index == 1 for face in body.data.polygons)
    body.name=sex+'Body';armature.name=sex+'Rig'
    body.data.calc_loop_triangles()
    weights={g.name:0.0 for g in body.vertex_groups}
    for vertex in body.data.vertices:
        for group in vertex.groups:weights[body.vertex_groups[group.group].name]+=group.weight
    fingers={name:weight for name,weight in weights.items() if any(x in name for x in ['Thumb','Index','Middle','Ring','Pinky'])}
    assert len(fingers)==30 and min(fingers.values())>0
    assert clothing_faces>100 and clothing_faces<len(body.data.polygons)*.40
    report={'model':sex,'triangles':len(body.data.loop_triangles),'bones':len(armature.data.bones),
            'weighted_fingers':fingers,'clothing_faces':clothing_faces,
            'landmarks':{s:{n:[list(a),list(b)] for n,(a,b) in fs.items()} for s,fs in all_landmarks.items()},
            'clothing_seams':seam_report}
    (QA/f'{sex}-rig-validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    dest=HERE.parents[2]/'CompanyGame/Assets/Art/Daldongne/Players'/('Meshy'+sex)
    dest.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True);armature.select_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/f'{sex}Articulated.blend'))
    bpy.ops.export_scene.fbx(filepath=str(dest/f'{sex}Articulated.fbx'),use_selection=True,
        object_types={'MESH','ARMATURE'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
        bake_anim=False,use_mesh_modifiers=False,path_mode='AUTO')
    print(json.dumps({k:v for k,v in report.items() if k not in ['landmarks','weighted_fingers']}))
    render(sex+'ArticulatedFront',(0,0,.9),(0,-1,0),2.2)
    # Both hands must deform independently before Unity retargeting is checked.
    for side in ['Right','Left']:
        for point in [False,True]:
            for name in all_landmarks[side]:
                for i,angle in enumerate([.8,1.15,.85],1):
                    bone=armature.pose.bones[side+'Hand'+name+str(i)]
                    bone.rotation_mode='XYZ'
                    bone.rotation_euler.x=0 if point and name=='Index' else -angle
            bpy.context.view_layer.update()
            center=armature.matrix_world@armature.data.bones[side+'Hand'].head_local
            center.x+=.1 if side=='Left' else -.1
            render(sex+side+('Point' if point else 'Fist'),center,(0,-1,1),.38)
    return report


def export_animations(sex):
    """Keep the downloaded motion, remove redundant bodies/textures from game clips."""
    dest=HERE.parents[2]/'CompanyGame/Assets/Art/Daldongne/Players'/('Meshy'+sex)/'Animations'
    dest.mkdir(parents=True,exist_ok=True)
    for motion in ['Walk','Run','Dance']:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(SOURCE/f'{sex}{motion}.fbx'))
        armature=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
        action=armature.animation_data.action
        start,end=action.frame_range
        bpy.context.scene.frame_start=int(start)
        bpy.context.scene.frame_end=int(end)
        bpy.ops.object.select_all(action='DESELECT')
        armature.select_set(True);bpy.context.view_layer.objects.active=armature
        bpy.ops.export_scene.fbx(filepath=str(dest/f'{sex}{motion}.fbx'),use_selection=True,
            object_types={'ARMATURE'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
            bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,
            bake_anim_simplify_factor=0.0,path_mode='AUTO')
        print(json.dumps({'model':sex,'motion':motion,'frames':[int(start),int(end)],'actions':len(bpy.data.actions)}))


def stage_hand_repair(sex, destination):
    """Hand-only trial: retain authored body/garment and transplant the connected source hands.

    All files go to the explicitly supplied staging folder. Public outputs and source GLBs are never saved here.
    """
    import hashlib
    out = Path(destination).resolve(); out.mkdir(parents=True, exist_ok=True)
    if out == SOURCE.resolve() or 'Assets' in out.parts:
        raise ValueError('Hand trials must be staged outside source/output Assets folders')
    source_arm, source_body = load(sex)
    def connected_landmarks(side):
        # Measure real fingertip surface islands. Vertex-density k-means can
        # split one finger in two and send the thumb bone toward the index.
        wrist=source_arm.matrix_world@source_arm.data.bones[side+'Hand'].head_local
        sign=1 if wrist.x>0 else -1
        points=[source_body.matrix_world@v.co for v in source_body.data.vertices]
        length=max((p.x-wrist.x)*sign for p in points)
        def islands(fraction):
            bm=bmesh.new();bm.from_mesh(source_body.data)
            for v in bm.verts:v.co=source_body.matrix_world@v.co
            bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-7,
                plane_co=wrist+Vector((sign*length*fraction,0,0)),plane_no=Vector((sign,0,0)),clear_inner=True,clear_outer=False)
            bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
            remaining=set(bm.verts);result=[]
            while remaining:
                seed=remaining.pop();group={seed};stack=[seed]
                while stack:
                    current=stack.pop();neighbours={e.other_vert(current) for e in current.link_edges}&remaining
                    group.update(neighbours);remaining.difference_update(neighbours);stack.extend(neighbours)
                if len(group)>15:result.append([v.co.copy() for v in group])
            bm.free();return sorted(result,key=lambda ps:sum(p.y for p in ps)/len(ps))
        distal=islands(.86)
        if len(distal)!=3:raise RuntimeError('Unexpected source digit islands: '+side)
        # The shorter pinky may remain fused to the ring near its base. Use
        # the ring's isolated distal outer edge to select only the outer digit.
        pinky_edge=max(p.y for p in distal[-1])+.002
        pinky=[p for p in points if (p.x-wrist.x)*sign>length*.55 and p.y>pinky_edge]
        output={}
        for name,cloud in zip(['Index','Middle','Ring','Pinky'],distal+[pinky]):
            end=max(p.x*sign for p in cloud)
            tipcloud=[p for p in cloud if p.x*sign>end-.004]
            tip=Vector(tuple((min(p[i] for p in tipcloud)+max(p[i] for p in tipcloud))*.5 for i in range(3)))
            tip.x=sign*end
            base=Vector((wrist.x+sign*length*.48,tip.y*.90+wrist.y*.10,tip.z))
            output[name]=(base,tip)
        thumb=[p for p in points if (p.x-wrist.x)*sign>length*.15 and p.y<wrist.y-.075]
        min_y=min(p.y for p in thumb);tipcloud=[p for p in thumb if p.y<min_y+.005]
        tip=Vector(tuple((min(p[i] for p in tipcloud)+max(p[i] for p in tipcloud))*.5 for i in range(3)))
        base=wrist+Vector((sign*length*.22,-.037,-.009))
        output['Thumb']=(base,tip)
        return output
    original = {side: connected_landmarks(side) for side in ['Left', 'Right']}
    wrists = {side: (source_arm.matrix_world @ source_arm.data.bones[side+'Hand'].head_local).copy() for side in original}
    source_hands = {}
    for side in original:
        wrist = wrists[side]; sign = 1 if wrist.x > 0 else -1
        bm = bmesh.new(); bm.from_mesh(source_body.data)
        for vertex in bm.verts: vertex.co = source_body.matrix_world @ vertex.co
        bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
            dist=1e-8, plane_co=wrist+Vector((sign*.008, 0, 0)), plane_no=Vector((sign,0,0)),
            clear_inner=True, clear_outer=False)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-7)
        # Relax the source's hard triangular ridges while preserving its
        # connected palm, thenar and fingertip surfaces and the wrist seam.
        for iteration in range(3):
            movable=[v for v in bm.verts if (v.co.x-wrist.x)*sign>.018]
            bmesh.ops.smooth_vert(bm,verts=movable,factor=.22,use_axis_x=True,use_axis_y=True,use_axis_z=True)
        # Two local cuts around each bend give existing source triangles deformation support.
        for fraction in [.34, .40, .46, .66, .72, .78]:
            x = sum(a.x+(b.x-a.x)*fraction for name,(a,b) in original[side].items() if name != 'Thumb') / 4
            bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                dist=1e-8, plane_co=Vector((x,0,0)), plane_no=Vector((1,0,0)),
                clear_inner=False, clear_outer=False)
        bm.verts.ensure_lookup_table(); bm.verts.index_update()
        source_hands[side] = ([v.co.copy() for v in bm.verts], [[v.index for v in f.verts] for f in bm.faces])
        bm.free()

    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / f'{sex}Articulated.blend'))
    armature = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    body = next(o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.vertices) > 1000)
    for bone in armature.pose.bones: bone.matrix_basis.identity()
    mesh = body.data; inv = body.matrix_world.inverted()
    outside = lambda p: wrists['Right'].x-.008-1e-6 <= p.x <= wrists['Left'].x+.008+1e-6
    group_names = {g.index:g.name for g in body.vertex_groups}
    old_world = [body.matrix_world @ v.co for v in mesh.vertices]
    retained_faces = [f for f in mesh.polygons if all(outside(old_world[i]) for i in f.vertices)]
    retained_ids = sorted({i for f in retained_faces for i in f.vertices})
    vertex_map = {old:new for new,old in enumerate(retained_ids)}
    positions = [mesh.vertices[i].co.copy() for i in retained_ids]
    influences = [{group_names[g.group]:g.weight for g in mesh.vertices[i].groups} for i in retained_ids]
    faces = [[vertex_map[i] for i in f.vertices] for f in retained_faces]
    face_materials = [f.material_index for f in retained_faces]
    face_smooth = [f.use_smooth for f in retained_faces]
    uv_layers = {layer.name:[[tuple(layer.data[i].uv) for i in f.loop_indices] for f in retained_faces] for layer in mesh.uv_layers}
    retained_normals = [mesh.corner_normals[i].vector.copy() for f in retained_faces for i in f.loop_indices]
    def key(p): return tuple(round(float(v),6) for v in p)
    def body_digest(points, face_list, mats, weights):
        payload = []
        for face, mat in zip(face_list,mats):
            payload.append((mat,tuple((key(points[i]),tuple(sorted((k,round(v,7)) for k,v in weights[i].items()))) for i in face)))
        return hashlib.sha256(repr(payload).encode()).hexdigest()
    before_hash = body_digest(positions,faces,face_materials,influences)
    def mesh_snapshot(obj, polygon_count):
        data=obj.data;names={g.index:g.name for g in obj.vertex_groups}
        fs=list(data.polygons)[:polygon_count]
        records={
            'geometry':[(tuple(tuple(float(x) for x in data.vertices[i].co) for i in f.vertices),f.material_index,f.use_smooth) for f in fs],
            'weights':[[tuple(sorted((names[g.group],round(float(g.weight),7)) for g in data.vertices[i].groups)) for i in f.vertices] for f in fs],
            'uv':{layer.name:[[tuple(float(x) for x in layer.data[i].uv) for i in f.loop_indices] for f in fs] for layer in data.uv_layers},
            'material_slots':[m.name_full if m else None for m in data.materials],
        }
        return {k:hashlib.sha256(repr(v).encode()).hexdigest() for k,v in records.items()}
    # Retained polygons may not be contiguous in the authored source.
    def original_snapshot():
        records={
            'geometry':[(tuple(tuple(float(x) for x in mesh.vertices[i].co) for i in f.vertices),f.material_index,f.use_smooth) for f in retained_faces],
            'weights':[[tuple(sorted((group_names[g.group],round(float(g.weight),7)) for g in mesh.vertices[i].groups)) for i in f.vertices] for f in retained_faces],
            'uv':{layer.name:[[tuple(float(x) for x in layer.data[i].uv) for i in f.loop_indices] for f in retained_faces] for layer in mesh.uv_layers},
            'material_slots':[m.name_full if m else None for m in mesh.materials],
        }
        return {k:hashlib.sha256(repr(v).encode()).hexdigest() for k,v in records.items()}
    protected_before=original_snapshot()
    bone_before = {b.name:tuple(tuple(row) for row in b.matrix_local) for b in armature.data.bones if not any(n in b.name for n in ['HandIndex','HandMiddle','HandRing','HandPinky','HandThumb'])}
    hierarchy_before={b.name:b.parent.name if b.parent else None for b in armature.data.bones}
    boundary = {key(body.matrix_world @ p):i for i,p in enumerate(positions)
                if any(abs((body.matrix_world@p).x-(wrists[s].x+(1 if wrists[s].x>0 else -1)*.008))<2e-6 for s in wrists)}
    boundary_before = sorted(boundary)
    new_landmarks = {}
    def smooth(a,b,t):
        t=max(0.,min(1.,(t-a)/(b-a))); return t*t*(3-2*t)
    def shape_point(p, side, digit=None):
        wrist=wrists[side]; sign=1 if wrist.x>0 else -1
        q=p.copy(); x=(p.x-wrist.x)*sign
        # Keep the cut and its tangent unchanged; shorten the old 23-27cm hands gradually.
        amount=smooth(.008,.055,x)
        q.x=wrist.x+sign*(x-(x-.008)*.25*amount)
        return q
    def classify(p, side):
        candidates=[]
        for name,(a,b) in original[side].items():
            axis=b-a; t=(p-a).dot(axis)/axis.length_squared
            d=(p-(a+axis*max(0.,min(1.,t)))).length
            candidates.append((d,name,t))
        return min(candidates)
    for side,(hand_points,hand_faces) in source_hands.items():
        local_map={}
        for i,p in enumerate(hand_points):
            k=key(p)
            if k in boundary: local_map[i]=boundary[k]; continue
            distance,digit,t=classify(p,side)
            digit_weight=smooth(-.16,.10,t) if distance<.045 else 0.
            q=shape_point(p,side,digit if digit_weight>0 else None)
            local_map[i]=len(positions); positions.append(inv@q)
            weights={side+'Hand':1.-digit_weight}
            if digit_weight>0:
                if t<.32: joint={1:1.}
                elif t<.48: blend=(t-.32)/.16; joint={1:1-blend,2:blend}
                elif t<.64: joint={2:1.}
                elif t<.80: blend=(t-.64)/.16; joint={2:1-blend,3:blend}
                else: joint={3:1.}
                weights.update({side+'Hand'+digit+str(j):w*digit_weight for j,w in joint.items() if w>0})
            influences.append({name:w for name,w in weights.items() if w>0})
        for face in hand_faces:
            mapped=[local_map[i] for i in face]
            if len(set(mapped))<3: continue
            faces.append(mapped); face_materials.append(0); face_smooth.append(True)
        new_landmarks[side]={name:(shape_point(a,side,name),shape_point(b,side,name)) for name,(a,b) in original[side].items()}

    new_mesh=bpy.data.meshes.new(mesh.name+'_HandRepair')
    new_mesh.from_pydata(positions,[],faces); new_mesh.update()
    for material in mesh.materials: new_mesh.materials.append(material)
    for face,material,smooth_face in zip(new_mesh.polygons,face_materials,face_smooth):
        face.material_index=material; face.use_smooth=smooth_face
    for name,source_faces in uv_layers.items():
        layer=new_mesh.uv_layers.new(name=name)
        for face,values in zip(new_mesh.polygons,source_faces):
            for loop,uv in zip(face.loop_indices,values): layer.data[loop].uv=uv
    body.data=new_mesh
    # Blender 5.2 stores vertex group definitions with the mesh; restore their
    # original names before writing the retained and newly skinned vertices.
    for name in group_names.values():
        if name not in body.vertex_groups: body.vertex_groups.new(name=name)
    for index,weights in enumerate(influences):
        for name,value in weights.items(): body.vertex_groups[name].add([index],value,'REPLACE')
    new_mesh.update()
    normals=[corner.vector.copy() for corner in new_mesh.corner_normals]
    normals[:len(retained_normals)]=retained_normals
    new_mesh.normals_split_custom_set(normals)
    bpy.ops.object.select_all(action='DESELECT'); armature.select_set(True); bpy.context.view_layer.objects.active=armature
    bpy.ops.object.mode_set(mode='EDIT'); arm_inv=armature.matrix_world.inverted()
    for side,fingers in new_landmarks.items():
        for name,(a,b) in fingers.items():
            for index,(start,end) in enumerate(zip([0,.40,.72],[.40,.72,1.]),1):
                bone=armature.data.edit_bones[side+'Hand'+name+str(index)]
                bone.head=arm_inv@(a+(b-a)*start); bone.tail=arm_inv@(a+(b-a)*end)
                bone.align_roll(Vector((0,0,1)))
    bpy.ops.object.mode_set(mode='OBJECT')
    after_bones={b.name:tuple(tuple(row) for row in b.matrix_local) for b in armature.data.bones if b.name in bone_before}
    assert after_bones==bone_before, 'A non-finger rest bone changed'
    assert hierarchy_before=={b.name:b.parent.name if b.parent else None for b in armature.data.bones}, 'Bone hierarchy changed'
    protected_after=mesh_snapshot(body,len(retained_faces))
    assert protected_before==protected_after, 'Actual authored body/garment data changed'
    actual_points=[v.co.copy() for v in new_mesh.vertices]
    actual_faces=[list(f.vertices) for f in list(new_mesh.polygons)[:len(retained_faces)]]
    actual_group_names={g.index:g.name for g in body.vertex_groups}
    actual_weights=[{actual_group_names[g.group]:g.weight for g in v.groups} for v in new_mesh.vertices]
    after_hash=body_digest(actual_points,actual_faces,[f.material_index for f in list(new_mesh.polygons)[:len(retained_faces)]],actual_weights)
    assert before_hash==after_hash, 'Authored body/garment or retained weights changed'
    final_normals=[new_mesh.corner_normals[i].vector.copy() for f in list(new_mesh.polygons)[:len(retained_faces)] for i in f.loop_indices]
    normal_error=max((a-b).length for a,b in zip(retained_normals,final_normals))
    # Blender re-encodes custom loop normals in a local angular basis; the
    # observed roundtrip error is below 0.057 degrees at the edited wrist seam.
    assert normal_error<.001, 'Protected body custom normals changed beyond Blender encoding precision'
    finger_weights={b.name:sum(w.get(b.name,0) for w in actual_weights) for b in armature.data.bones if b.name not in bone_before}
    assert len(finger_weights)==30 and min(finger_weights.values())>0, 'Missing finger skin weights'
    assert sorted(key(body.matrix_world@positions[i]) for i in boundary.values())==boundary_before, 'Wrist seam moved'
    new_mesh.calc_loop_triangles()
    assert len(new_mesh.loop_triangles)<=18000, 'Triangle budget exceeded'
    report={'model':sex,'stage':'connected original hand trial','body_sha256_before':before_hash,'body_sha256_after':after_hash,
            'protected_actual_before':protected_before,'protected_actual_after':protected_after,'normal_max_error':normal_error,
            'nonfinger_rest_sha256':hashlib.sha256(repr(after_bones).encode()).hexdigest(),
            'hierarchy_sha256':hashlib.sha256(repr(hierarchy_before).encode()).hexdigest(),'weighted_fingers':finger_weights,
            'retained_body_faces':len(retained_faces),
            'preserved_bones':len(bone_before),'bones':len(armature.data.bones),'triangles':len(new_mesh.loop_triangles),
            'wrist_boundary_vertices':len(boundary),'landmarks':{s:{n:[list(a),list(b)] for n,(a,b) in fs.items()} for s,fs in new_landmarks.items()}}
    (out/f'{sex}-hand-repair.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); armature.select_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out/f'{sex}Articulated.blend'))
    bpy.ops.export_scene.fbx(filepath=str(out/f'{sex}Articulated.fbx'),use_selection=True,
        object_types={'MESH','ARMATURE'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
        bake_anim=False,use_mesh_modifiers=False,path_mode='AUTO')
    global QA
    old_qa=QA; QA=out
    try:
        for pose in ['Open','Fist','Point','Hold']:
            for bone in armature.pose.bones: bone.matrix_basis.identity()
            for side in ['Left','Right']:
                for name in original[side]:
                    angles=[0,0,0] if pose=='Open' or (pose=='Point' and name=='Index') else [80,100,60] if pose in ['Fist','Point'] else [45,65,40]
                    if name=='Thumb' and pose!='Open': angles=[35,25,20]
                    for index,angle in enumerate(angles,1):
                        bone=armature.pose.bones[side+'Hand'+name+str(index)]
                        bone.rotation_mode='XYZ'; bone.rotation_euler.x=-np.deg2rad(angle)
                bpy.context.view_layer.update()
                center=wrists[side]+Vector(((1 if wrists[side].x>0 else -1)*.095,0,-.015))
                for label,direction in [('Back',(0,0,1)),('Palm',(0,0,-1)),('Side',(0,-1,.15)),('Quarter',(-.6,-1,1))]:
                    render(f'{sex}{side}{pose}{label}',center,direction,.30)
        print('HAND_REPAIR_RESULT',json.dumps(report))
    finally: QA=old_qa
    return report


def stage_pelvis_weight_repair(sex, destination):
    """Stage a weight-only pelvis correction from the CURRENT authored blend.

    Skin and clothing share one position-based blend so their boundary stays
    continuous. Original GLBs, authored blends and Unity assets are never saved.
    """
    import hashlib
    if sex not in ('Female', 'Male'):
        raise ValueError('Expected Female or Male')
    out = Path(destination).resolve()
    if out == SOURCE.resolve() or SOURCE.resolve() in out.parents or any(p.lower() == 'assets' for p in out.parts):
        raise ValueError('Pelvis trials must be staged outside the authored source and Unity Assets folders')
    source = (SOURCE / f'{sex}Articulated.blend').resolve()
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    bpy.ops.wm.open_mainfile(filepath=str(source))
    armatures = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if len(armatures) != 1:
        raise RuntimeError('Expected one authored armature')
    armature = armatures[0]
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH'
              and any(m.type == 'ARMATURE' and m.object == armature for m in o.modifiers)]
    if len(meshes) != 1:
        raise RuntimeError('Expected one continuous authored body/garment skin')
    body = meshes[0]; mesh = body.data
    marker = 'companygame_pelvis_weight_repair'
    version = '20261030-v1'
    previous_version = body.get(marker)
    if previous_version is not None and previous_version != version:
        raise RuntimeError('A different pelvis weight correction already exists; review it before applying another')
    already_applied = previous_version == version
    names = {g.index: g.name for g in body.vertex_groups}
    required = ('Hips', 'LeftUpLeg', 'RightUpLeg')
    if any(n not in armature.data.bones or n not in body.vertex_groups for n in required):
        raise RuntimeError('Authored pelvis/upper-leg bones and groups are required')
    leg_names = set()
    for name in ('LeftUpLeg', 'RightUpLeg'):
        root = armature.data.bones[name]
        leg_names.add(name); leg_names.update(b.name for b in root.children_recursive)
    points = [body.matrix_world @ v.co for v in mesh.vertices]
    height = max(p.z for p in points) - min(p.z for p in points)
    heads = {n: armature.matrix_world @ armature.data.bones[n].head_local for n in required}
    full_z = (heads['LeftUpLeg'].z + heads['RightUpLeg'].z) * .5
    start_z = full_z - .12 * height / 1.8
    if not .3 * height < full_z - min(p.z for p in points) < .7 * height:
        raise RuntimeError('Unexpected authored up axis or upper-leg height')
    weights_before = [{names[g.group]: float(g.weight) for g in v.groups} for v in mesh.vertices]
    region = {i for i, p in enumerate(points) if p.z > start_z
              and any(weights_before[i].get(n, 0.) > 0 for n in leg_names)}
    def digest(value):
        return hashlib.sha256(repr(value).encode('utf-8')).hexdigest()
    def snapshot():
        return {
            'geometry': digest([tuple(float(c) for c in v.co) for v in mesh.vertices]),
            'topology': digest(([tuple(e.vertices) for e in mesh.edges],
                                [(tuple(f.vertices), f.material_index, f.use_smooth) for f in mesh.polygons])),
            'normals': digest([tuple(n.vector) for n in mesh.corner_normals]),
            'uv': digest({layer.name: [tuple(v.uv) for v in layer.data] for layer in mesh.uv_layers}),
            'materials': digest([(m.name_full, tuple(m.diffuse_color)) if m else None for m in mesh.materials]),
            'bones': digest([(b.name, b.parent.name if b.parent else None, tuple(b.head_local), tuple(b.tail_local),
                              tuple(tuple(row) for row in b.matrix_local)) for b in armature.data.bones]),
            'pose': digest([(b.name, tuple(tuple(row) for row in b.matrix_basis)) for b in armature.pose.bones]),
            'object_transforms': digest([tuple(tuple(row) for row in o.matrix_world) for o in (body, armature)]),
            'vertex_groups': digest([g.name for g in body.vertex_groups]),
        }
    protected_before = snapshot()
    changed = []; moved_total = 0.
    for index in ([] if already_applied else sorted(region)):
        t = min(1., max(0., (points[index].z - start_z) / (full_z - start_z)))
        amount = t * t * (3. - 2. * t)
        moved = 0.
        for name, value in weights_before[index].items():
            if name not in leg_names or value <= 0:
                continue
            transfer = value * amount; moved += transfer
            remaining = value - transfer
            if remaining > 0: body.vertex_groups[name].add([index], remaining, 'REPLACE')
            else: body.vertex_groups[name].remove([index])
        if moved > 0:
            body.vertex_groups['Hips'].add([index], weights_before[index].get('Hips', 0.) + moved, 'REPLACE')
            changed.append(index); moved_total += moved
    weights_after = [{names[g.group]: float(g.weight) for g in v.groups} for v in mesh.vertices]
    protected_after = snapshot()
    if protected_before != protected_after:
        raise AssertionError('Pelvis weight repair changed protected mesh, materials, normals or bones')
    outside_before = digest([(i, sorted(w.items())) for i, w in enumerate(weights_before) if i not in region])
    outside_after = digest([(i, sorted(w.items())) for i, w in enumerate(weights_after) if i not in region])
    assert outside_before == outside_after, 'Weights outside pelvis region changed'
    unaffected_before = digest([[(n, v) for n, v in sorted(w.items()) if n != 'Hips' and n not in leg_names] for w in weights_before])
    unaffected_after = digest([[(n, v) for n, v in sorted(w.items()) if n != 'Hips' and n not in leg_names] for w in weights_after])
    assert unaffected_before == unaffected_after, 'Non-leg/non-pelvis influences changed'
    sum_error = max(abs(sum(a.values()) - sum(b.values())) for a, b in zip(weights_before, weights_after))
    assert sum_error < 2e-6, 'Vertex weight totals changed'
    assert all(v >= 0 for w in weights_after for v in w.values()), 'Negative skin weight'
    upper = [i for i in region if points[i].z >= full_z]
    upper_leg_max = max((sum(weights_after[i].get(n, 0.) for n in leg_names) for i in upper), default=0.)
    assert upper_leg_max < 1e-6, 'Upper pelvis still follows a leg'
    clothing = {i for f in mesh.polygons if f.material_index == 1 for i in f.vertices}
    report = {'model': sex, 'stage': 'pelvis weight transfer', 'version': version, 'already_applied': already_applied,
              'source': str(source), 'source_sha256': source_hash,
              'transition_start_world_z': start_z, 'full_hips_world_z': full_z, 'body_height': height,
              'bone_heads_world': {n: list(p) for n, p in heads.items()}, 'region_vertices': len(region),
              'changed_vertices': len(changed), 'changed_clothing_vertices': sum(i in clothing for i in changed),
              'changed_skin_only_vertices': sum(i not in clothing for i in changed), 'weight_transferred': moved_total,
              'upper_pelvis_max_leg_weight': upper_leg_max, 'weight_sum_max_error': sum_error,
              'protected_before': protected_before, 'protected_after': protected_after,
              'outside_region_weights_sha256_before': outside_before, 'outside_region_weights_sha256_after': outside_after,
              'unaffected_influences_sha256_before': unaffected_before, 'unaffected_influences_sha256_after': unaffected_after,
              'changed_vertex_indices': changed}
    out.mkdir(parents=True, exist_ok=True)
    body[marker] = version
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.wm.save_as_mainfile(filepath=str(out / f'{sex}Articulated.blend'))
    bpy.ops.export_scene.fbx(filepath=str(out / f'{sex}Articulated.fbx'), use_selection=True,
        object_types={'MESH', 'ARMATURE'}, axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
        bake_anim=False, use_mesh_modifiers=False, path_mode='AUTO', use_custom_props=False)
    assert hashlib.sha256(source.read_bytes()).hexdigest() == source_hash, 'Authored source file changed'
    (out / f'{sex}-pelvis-weight-repair.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('PELVIS_WEIGHT_REPAIR_RESULT', json.dumps({k: v for k, v in report.items() if k != 'changed_vertex_indices'}))
    return report


if __name__ == '__main__':
    arguments = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    if '--build' in arguments:
        raise RuntimeError('The legacy --build pipeline reconstructs the retired tube hands and overwrites authored body/garment work. '
            'Use --stage-hands --output=<folder-outside-Assets> to rebuild only the connected source hands; see Characters/README.md.')
    folder = next((a.split('=', 1)[1] for a in arguments if a.startswith('--output=')), None)
    if '--output' in arguments:
        index = arguments.index('--output')
        if index + 1 >= len(arguments): raise ValueError('--output needs a staging folder')
        folder = arguments[index + 1]; arguments = arguments[:index] + arguments[index + 2:]
    if '--stage-pelvis' in arguments and '--stage-hands' in arguments:
        raise ValueError('Stage hands and pelvis separately')
    if '--stage-pelvis' not in arguments and '--stage-hands' not in arguments:
        QA.mkdir(parents=True, exist_ok=True)
    for sex in [a for a in arguments if not a.startswith('--')] or ['Female','Male']:
        if '--stage-pelvis' in arguments:
            if not folder: raise ValueError('--stage-pelvis requires --output=<staging-folder>')
            stage_pelvis_weight_repair(sex, folder)
        elif '--stage-hands' in arguments:
            if not folder:raise ValueError('--stage-hands requires --output=<staging-folder>')
            stage_hand_repair(sex,folder)
        elif '--animations' in arguments:export_animations(sex)
        else:inspect(sex)
