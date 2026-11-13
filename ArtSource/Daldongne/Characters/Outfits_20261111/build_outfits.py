"""Basic outfit (T-shirt + long trousers) for the current Meshy articulated bodies.

Each garment is cut from the body surface that Unity uses (Players/Meshy<Sex>/<Sex>Articulated.fbx) with straight
planes (neckline, sleeve ends, hem, waist, ankles), smoothed so it bridges body detail like cloth, held a few millimetres
off the skin, and given a thin 2.5 mm wall. Every garment vertex keeps the bone weights of the skin it came from, so the
cloth follows any pose the body takes (walking, sitting, fighting) instead of being a rigid shell.

Run (both sexes):  blender -b --factory-startup --python build_outfits.py
Output: Outfits_20261111/<Sex>BasicOutfit.blend (editable, body included for fitting), the Unity FBX
CompanyGame/Assets/Art/Daldongne/Players/Outfits/<Sex>BasicOutfit.fbx (rig + Top + Bottom), renders and fit.json.
"""
import bpy, bmesh, json, math, sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
PLAYERS = ROOT / 'CompanyGame/Assets/Art/Daldongne/Players'
DEST = PLAYERS / 'Outfits'

TOP_COLOR = (0.56, 0.64, 0.72, 1)     # light slate; tinted per character in Unity
BOTTOM_COLOR = (0.18, 0.22, 0.30, 1)  # navy
THICKNESS = .0025


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def load_body(sex):
    bpy.ops.import_scene.fbx(filepath=str(PLAYERS / f'Meshy{sex}/{sex}Articulated.fbx'))
    rig = bpy.data.objects[f'{sex}Rig']
    body = bpy.data.objects[f'{sex}Body']
    return rig, body


def bone_head(rig, name):
    return rig.matrix_world @ rig.data.bones[name].head_local


def body_bvh(body):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = body.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    bm = bmesh.new(); bm.from_mesh(mesh); bm.transform(body.matrix_world)
    # Welded, with consistent outward normals: parts of the exported body face inward, which would put cloth inside the arm.
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=.0001)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    tree = BVHTree.FromBMesh(bm)
    evaluated.to_mesh_clear()
    return tree, bm


def cut(source, name, planes, keep_min_faces=200, hole=None):
    """A copy of source with everything on the positive side of each (point, normal) plane removed.
    hole: (centre, rx, ry, min_z) removes the faces inside that upright elliptic cylinder above min_z (a neckline)."""
    obj = source.copy(); obj.data = source.data.copy(); obj.name = name; obj.data.name = name
    bpy.context.collection.objects.link(obj)
    obj.parent = source.parent; obj.matrix_world = source.matrix_world.copy()
    inv = obj.matrix_world.inverted()
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bm.verts.layers.deform.verify()
    # The exported body is split along UV/normal seams; weld it into one surface first (0.1 mm, local units are cm).
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=.01)
    for point, normal in planes:
        co = inv @ point
        no = (inv.to_3x3() @ normal).normalized()
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_outer=True)
    if hole:
        centre, rx, ry, min_z = hole
        mw = obj.matrix_world
        doomed = []
        for f in bm.faces:
            c = mw @ f.calc_center_median()
            if c.z > min_z and ((c.x - centre.x) / rx) ** 2 + ((c.y - centre.y) / ry) ** 2 < 1: doomed.append(f)
        bmesh.ops.delete(bm, geom=doomed, context='FACES')
    # Drop islands the planes left behind (bits of fingers, ears).
    bm.faces.ensure_lookup_table()
    seen, islands = set(), []
    for f in bm.faces:
        if f in seen: continue
        stack, island = [f], []
        seen.add(f)
        while stack:
            g = stack.pop(); island.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h not in seen: seen.add(h); stack.append(h)
        islands.append(island)
    for island in islands:
        if len(island) < keep_min_faces:
            bmesh.ops.delete(bm, geom=island, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data); bm.free()
    obj.data.materials.clear()
    return obj


def decimate(obj, ratio):
    mod = obj.modifiers.new('Decimate', 'DECIMATE'); mod.ratio = ratio; mod.use_collapse_triangulate = False
    bpy.context.view_layer.objects.active = obj
    # The armature modifier must stay last and unapplied.
    while obj.modifiers[0].name != 'Decimate': bpy.ops.object.modifier_move_up(modifier='Decimate')
    bpy.ops.object.modifier_apply(modifier='Decimate')


def fit(obj, tree, offset_of, iterations=40, inflate=True, shape=None):
    """Inflate along the skin normal, smooth like fabric, then keep every vertex at least 70% of its offset off the skin.
    shape(p) -> p, applied before smoothing, can widen the garment beyond the skin (straight trouser legs)."""
    mw, inv = obj.matrix_world, obj.matrix_world.inverted()
    bm = bmesh.new(); bm.from_mesh(obj.data); bm.verts.ensure_lookup_table()
    pos = [mw @ v.co for v in bm.verts]
    offsets = [offset_of(p) for p in pos]
    if inflate:
        for i, v in enumerate(bm.verts):
            hit, normal, _, _ = tree.find_nearest(pos[i])
            if hit is not None: pos[i] = hit + normal.normalized() * offsets[i]
    if shape: pos = [shape(p) for p in pos]
    boundary = [v.is_boundary for v in bm.verts]
    neighbours = [[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]
    for _ in range(iterations):
        new = pos[:]
        for i in range(len(pos)):
            nb = neighbours[i]
            if not nb: continue
            if boundary[i]:
                nb = [j for j in nb if boundary[j]]  # a hem slides along itself, so it stays a clean line
                if not nb: continue
            avg = sum((pos[j] for j in nb), Vector()) / len(nb)
            new[i] = pos[i].lerp(avg, .5)
        pos = new
        for i in range(len(pos)):  # never sink into the skin
            hit, normal, _, _ = tree.find_nearest(pos[i])
            if hit is None: continue
            n = normal.normalized(); d = (pos[i] - hit).dot(n)
            floor = offsets[i] * .7
            if d < floor: pos[i] = pos[i] + n * (floor - d)
    for i, v in enumerate(bm.verts): v.co = inv @ pos[i]
    bm.to_mesh(obj.data); bm.free()


def closest_on_leg(p, chain):
    best = None
    for a, b in zip(chain, chain[1:]):
        ab = b - a; t = max(0., min(1., (p - a).dot(ab) / ab.length_squared)); q = a + ab * t
        if best is None or (p - q).length < (p - best).length: best = q
    return best


def straighten_legs(obj, tree, rig, body, offset_of, female):
    """Straight-leg trousers: below the underwear edge each leg keeps at least its width there, tapering to the ankle,
    so the body's built-in shorts (or briefs) do not print through as a ridge."""
    chains = {s: [bone_head(rig, s + 'UpLeg'), bone_head(rig, s + 'Leg'), bone_head(rig, s + 'Foot')] for s in ('Left', 'Right')}
    # Lowest underwear point per leg (material slot 1 is the body's underwear).
    mesh = body.data; low = {}
    for poly in mesh.polygons:
        if poly.material_index != 1: continue
        for vi in poly.vertices:
            p = body.matrix_world @ mesh.vertices[vi].co
            side = 'Left' if p.x > 0 else 'Right'
            # between knee and hip only (slot 1 also holds bits elsewhere, e.g. at the feet)
            if chains[side][1].z < p.z < chains[side][0].z and (side not in low or p.z < low[side]): low[side] = p.z
    mw = obj.matrix_world
    pts = [mw @ v.co for v in obj.data.vertices]
    radius_at_hem = {}
    for side, hem in low.items():
        rs = sorted(((p - closest_on_leg(p, chains[side])) * Vector((1, 1, 0))).length
                    for p in pts if (p.x > 0) == (side == 'Left') and hem - .01 < p.z < hem + .02)
        radius_at_hem[side] = rs[int(len(rs) * .85)] if rs else .08
    ankle_r = .05 if female else .056

    def shape(p):
        side = 'Left' if p.x > 0 else 'Right'
        blend_top = .06  # fade the widening in above the edge so it leaves no flare
        if side not in low or p.z > low[side] + blend_top: return p
        hem, foot = low[side], chains[side][2].z
        t = max(0., min(1., (hem - p.z) / (hem - foot)))
        want = radius_at_hem[side] + (ankle_r - radius_at_hem[side]) * t ** .8
        if p.z > hem:
            q = closest_on_leg(p, chains[side]); d = ((p - q) * Vector((1, 1, 0))).length
            want = d + (max(want, d) - d) * (1 - (p.z - hem) / blend_top)
        q = closest_on_leg(p, chains[side]); d = (p - q) * Vector((1, 1, 0))
        if d.length >= want or d.length < 1e-6: return p
        n = p + d.normalized() * (want - d.length)
        sign = 1 if side == 'Left' else -1
        if n.x * sign < .004: n.x = .004 * sign  # never past the middle into the other leg
        return n

    fit(obj, tree, offset_of, 30, inflate=False, shape=shape)
    print('STRAIGHT_LEGS hem z', {k: round(v, 3) for k, v in low.items()}, 'hem radius', {k: round(v, 3) for k, v in radius_at_hem.items()})


def masked_body(body, garments, reach=.035, edge=.03):
    """A copy of the body without the faces the garments cover, so skin and underwear never show through the cloth.
    Faces within `edge` of a garment opening stay, so a moving hem or sleeve never shows a gap."""
    from mathutils.kdtree import KDTree
    trees, edges = [], []
    for g in garments:
        bm = bmesh.new(); bm.from_mesh(g.data); bm.transform(g.matrix_world)
        trees.append(BVHTree.FromBMesh(bm))
        edges += [v.co.copy() for v in bm.verts if v.is_boundary]
        bm.free()
    kd = KDTree(len(edges))
    for i, co in enumerate(edges): kd.insert(co, i)
    kd.balance()
    masked = body.copy(); masked.data = body.data.copy(); masked.name = body.name + 'Masked'; masked.data.name = masked.name
    bpy.context.collection.objects.link(masked)
    masked.parent = body.parent; masked.matrix_world = body.matrix_world.copy()
    mw = masked.matrix_world
    covered = []
    for v in masked.data.vertices:
        p = mw @ v.co
        near = any((t.find_nearest(p)[0] is not None and (t.find_nearest(p)[0] - p).length < reach) for t in trees)
        covered.append(near and kd.find(p)[2] > edge)
    bm = bmesh.new(); bm.from_mesh(masked.data)
    doomed = [f for f in bm.faces if all(covered[v.index] for v in f.verts)]
    bmesh.ops.delete(bm, geom=doomed, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(masked.data); bm.free()
    return masked, len(doomed)


def finish(obj, color, material_name):
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.selected_objects: o.select_set(False)
    obj.select_set(True)
    # Four bones per vertex is what Unity skins with by default.
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    sol = obj.modifiers.new('Solidify', 'SOLIDIFY')
    sol.thickness = THICKNESS; sol.offset = 1.0; sol.use_rim = True; sol.use_even_offset = True
    while obj.modifiers[0].name != 'Solidify': bpy.ops.object.modifier_move_up(modifier='Solidify')
    bpy.ops.object.modifier_apply(modifier='Solidify')
    mat = bpy.data.materials.new(material_name); mat.diffuse_color = color
    mat.use_nodes = True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = color
    mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .85
    obj.data.materials.append(mat)
    for p in obj.data.polygons: p.use_smooth = True
    obj.data.update()


def build(sex):
    reset()
    rig, body = load_body(sex)
    tree, body_bm = body_bvh(body)
    hips = bone_head(rig, 'Hips'); neck = bone_head(rig, 'neck')
    arm_l, fore_l = bone_head(rig, 'LeftArm'), bone_head(rig, 'LeftForeArm')
    arm_r, fore_r = bone_head(rig, 'RightArm'), bone_head(rig, 'RightForeArm')
    foot_l, foot_r = bone_head(rig, 'LeftFoot'), bone_head(rig, 'RightFoot')
    female = sex == 'Female'

    # ---- top: crew neck (lower in front, which is -Y), sleeves to mid upper arm, hem over the trouser waist ----
    sleeve = .5 if female else .48
    sl = arm_l.lerp(fore_l, sleeve); sr = arm_r.lerp(fore_r, sleeve)
    hem_z = hips.z - .045
    head_cut = neck.z + .07  # above the arms in the T-pose
    top = cut(body, f'{sex}Top', [
        (Vector((0, 0, head_cut)), Vector((0, 0, 1))),
        (sl, (fore_l - arm_l).normalized()),
        (sr, (fore_r - arm_r).normalized()),
        (Vector((0, 0, hem_z)), Vector((0, 0, -1))),
    ], hole=(Vector((neck.x, neck.y - .012, 0)), .078 if not female else .072, .072 if not female else .066, neck.z - .075))
    # ---- bottom: waist above the hip joint, straight legs to just above the ankle ----
    waist_z = hips.z + (.06 if female else .07)
    knee_z = bone_head(rig, 'LeftLeg').z
    bottom = cut(body, f'{sex}Bottom', [
        (Vector((0, 0, waist_z)), Vector((0, 0, 1))),
        (foot_l + Vector((0, 0, .075)), Vector((0, 0, -1))),
        (foot_r + Vector((0, 0, .075)), Vector((0, 0, -1))),
    ])
    for g in (top, bottom): decimate(g, .8)

    def top_offset(p):
        # Looser over the belly and back than at the shoulders; sleeves a little open.
        on_arm = abs(p.x) > abs(arm_l.x) * .95
        return .013 if on_arm else (.011 if p.z > neck.z - .12 else .014)

    def bottom_offset(p):
        # Trouser legs get looser toward the ankle (straight cut); a snug waistband.
        if p.z > hips.z - .02: return .007
        t = max(0., min(1., (hips.z - p.z) / (hips.z - foot_l.z)))
        knee = max(0., 1 - abs(p.z - knee_z) / .09)  # room for the knee to bend
        return .010 + .012 * t + .008 * knee

    fit(top, tree, top_offset, 60)
    # Long smoothing turns the body's built-in shorts edge above the knee into a plain taper.
    fit(bottom, tree, bottom_offset, 80)
    straighten_legs(bottom, tree, rig, body, bottom_offset, female)
    masked, hidden = masked_body(body, (top, bottom))
    print('MASKED_BODY hidden faces', hidden, 'kept', len(masked.data.polygons))
    finish(top, TOP_COLOR, f'{sex}Top')
    finish(bottom, BOTTOM_COLOR, f'{sex}Bottom')
    body_bm.free()

    HERE.mkdir(parents=True, exist_ok=True); DEST.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / f'{sex}BasicOutfit.blend'))
    for o in bpy.context.selected_objects: o.select_set(False)
    for o in (rig, top, bottom, masked): o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(DEST / f'{sex}BasicOutfit.fbx'), use_selection=True,
        object_types={'MESH', 'ARMATURE'}, axis_forward='-Z', axis_up='Y', add_leaf_bones=False,
        bake_anim=False, use_mesh_modifiers=False, path_mode='AUTO', use_custom_props=False)
    return rig, body, top, bottom, masked


# ---------------------------------------------------------------- poses, penetration and renders

def pose(rig, rotations):
    """rotations: bone -> (world axis, degrees), applied parent first around the bone head."""
    for pb in rig.pose.bones: pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    order = [b.name for b in rig.data.bones]  # parents come before children
    for name in order:
        if name not in rotations: continue
        axis, degrees = rotations[name]
        pb = rig.pose.bones[name]
        head = pb.head.copy()
        R = Matrix.Rotation(math.radians(degrees), 4, axis)
        pb.matrix = Matrix.Translation(head) @ R @ Matrix.Translation(-head) @ pb.matrix
        bpy.context.view_layer.update()


POSES = {
    'Rest': {},
    'Walk': {'LeftArm': ('Y', 72), 'RightArm': ('Y', -72), 'LeftUpLeg': ('X', -32), 'RightUpLeg': ('X', 24),
             'RightLeg': ('X', 30), 'LeftForeArm': ('Z', 20), 'RightForeArm': ('Z', -20)},
    'Sit': {'LeftArm': ('Y', 70), 'RightArm': ('Y', -70), 'LeftUpLeg': ('X', -88), 'RightUpLeg': ('X', -88),
            'LeftLeg': ('X', 88), 'RightLeg': ('X', 88), 'LeftForeArm': ('Z', 45), 'RightForeArm': ('Z', -45)},
    'Reach': {'LeftArm': ('Y', -60), 'RightArm': ('Y', 60), 'Spine02': ('X', 15)},
}


def penetration(body, garment):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    tree, bm = body_bvh(body)
    ev = garment.evaluated_get(depsgraph); mesh = ev.to_mesh()
    inside, near, total = 0, 0, len(mesh.vertices)
    for v in mesh.vertices:
        p = garment.matrix_world @ v.co
        hit, normal, _, _ = tree.find_nearest(p)
        if hit is None: continue
        d = (p - hit).dot(normal.normalized())
        if d < -.002: inside += 1
        elif d < .001: near += 1
    ev.to_mesh_clear(); bm.free()
    return {'vertices': total, 'inside_skin_over_2mm': inside, 'within_1mm': near,
            'inside_percent': round(100 * inside / max(1, total), 2)}


def render(sex, pose_name, path):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 900, 1100
    scene.render.film_transparent = False
    world = bpy.data.worlds.new('W'); scene.world = world; world.color = (.92, .92, .9)
    cams = []
    for label, direction in (('Front', Vector((0, -1, 0))), ('Side', Vector((1, 0, 0))), ('ThreeQuarter', Vector((.7, -.7, 0)).normalized())):
        cam_data = bpy.data.cameras.new(label); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 2.2
        cam = bpy.data.objects.new(label, cam_data); scene.collection.objects.link(cam)
        target = Vector((0, 0, .9))
        cam.location = target + direction * 4
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.camera = cam
        scene.render.filepath = str(path / f'{sex}_{pose_name}_{label}.png')
        bpy.ops.render.render(write_still=True)
        cams.append(cam)
    for c in cams: bpy.data.objects.remove(c, do_unlink=True)


def check(sex, rig, body, top, bottom, masked):
    skin = bpy.data.materials.get('Skin') or bpy.data.materials.new('Skin')
    skin.diffuse_color = (.9, .72, .62, 1)
    masked.data.materials.clear()
    for m in body.data.materials: masked.data.materials.append(m)
    for i, m in enumerate(body.data.materials):
        if m: m.diffuse_color = (.9, .72, .62, 1) if i == 0 else (.25, .25, .25, 1)
    renders = HERE / 'Renders'; renders.mkdir(exist_ok=True)
    report = {'sex': sex,
              'top': {'triangles': sum(len(p.vertices) - 2 for p in top.data.polygons), 'vertices': len(top.data.vertices)},
              'bottom': {'triangles': sum(len(p.vertices) - 2 for p in bottom.data.polygons), 'vertices': len(bottom.data.vertices)},
              'poses': {}}
    for o in (top, bottom):
        unweighted = sum(1 for v in o.data.vertices if not v.groups or sum(g.weight for g in v.groups) < .99)
        report[o.name.replace(sex, '').lower()]['unweighted_vertices'] = unweighted
    report['masked_body'] = {'faces': len(masked.data.polygons), 'full_body_faces': len(body.data.polygons)}
    for name, rotations in POSES.items():
        pose(rig, {k: (v[0], v[1]) for k, v in rotations.items()})
        report['poses'][name] = {'top': penetration(body, top), 'bottom': penetration(body, bottom)}
        body.hide_render = True  # what the game shows: the masked body under the clothes
        render(sex, name, renders)
        body.hide_render = False
    pose(rig, {})
    (HERE / f'{sex}-fit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('OUTFIT_FIT', json.dumps(report))


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ['Male', 'Female']
    for sex in args:
        rig, body, top, bottom, masked = build(sex)
        check(sex, rig, body, top, bottom, masked)
