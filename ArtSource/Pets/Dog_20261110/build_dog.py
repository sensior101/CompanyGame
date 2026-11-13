"""Low-poly companion dog (누렁이) with a deform rig and baked motion clips.

Run headless:
  blender --background --factory-startup --python build_dog.py
Outputs next to this file: Dog.blend, Dog.fbx, Renders/*.png, validation.json

Conventions match the player models: metres, Z up in Blender, the dog faces -Y,
FBX export axis_forward='-Z', axis_up='Y' (faces +Z in Unity). Root sits on the
ground at the origin and is never animated (in-place clips, the game moves the pet).
Legs are authored with temporary IK controls and baked to plain FK keys on the
deform bones, so the exported rig is FK-only and works as a Unity Generic rig.
"""
from pathlib import Path
from math import sin, cos, pi, radians, sqrt
import json
import random
import bpy
import bmesh
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parent
RENDERS = ROOT / 'Renders'
FPS = 30
rng = random.Random(20261110)
X = Vector((1, 0, 0))


def srgb(h):
    h = h.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in c)


PALETTE = [('Dog_Fur', '#BE8450'), ('Dog_Cream', '#F0E2C8'), ('Dog_Dark', '#2E2724'),
           ('Dog_Collar', '#B4553F'), ('Dog_Tag', '#D8B04A'), ('Dog_InnerEar', '#E2B49A')]
FUR, CREAM, DARK, COLLAR, TAG, INNER = range(6)

# ---------------------------------------------------------------- skeleton
# name: (head, tail, parent)
BONES = {
    'Root': ((0, 0, 0), (0, -.15, 0), None),
    'Hips': ((0, .20, .47), (0, .04, .47), 'Root'),
    'Spine': ((0, .04, .47), (0, -.14, .47), 'Hips'),
    'Chest': ((0, -.14, .47), (0, -.27, .50), 'Spine'),
    'Neck': ((0, -.25, .52), (0, -.32, .63), 'Chest'),
    'Head': ((0, -.32, .63), (0, -.48, .65), 'Neck'),
    'Jaw': ((0, -.37, .625), (0, -.50, .615), 'Head'),
    'Tail1': ((0, .29, .51), (0, .34, .59), 'Hips'),
    'Tail2': ((0, .34, .59), (0, .34, .67), 'Tail1'),
    'Tail3': ((0, .34, .67), (0, .29, .71), 'Tail2'),
}
for s, k in (('L', 1), ('R', -1)):
    BONES.update({
        f'Ear_{s}': ((.045 * k, -.355, .715), (.064 * k, -.362, .80), 'Head'),
        f'Front_Upper_{s}': ((.075 * k, -.22, .42), (.075 * k, -.16, .26), 'Chest'),
        f'Front_Lower_{s}': ((.075 * k, -.16, .26), (.075 * k, -.215, .07), f'Front_Upper_{s}'),
        f'Front_Paw_{s}': ((.075 * k, -.215, .07), (.075 * k, -.27, .015), f'Front_Lower_{s}'),
        f'Hind_Thigh_{s}': ((.08 * k, .20, .43), (.08 * k, .13, .27), 'Hips'),
        f'Hind_Shin_{s}': ((.08 * k, .13, .27), (.08 * k, .235, .13), f'Hind_Thigh_{s}'),
        f'Hind_Hock_{s}': ((.08 * k, .235, .13), (.08 * k, .215, .045), f'Hind_Shin_{s}'),
        f'Hind_Paw_{s}': ((.08 * k, .215, .045), (.08 * k, .165, .012), f'Hind_Hock_{s}'),
    })
HEAD_SCALE = 1.78     # big round head
SNOUT_Y, SNOUT_K = -.40, .5   # muzzle length kept in front of the eyes
BODY_Y = .72          # short, compact body
BODY_X = 1.12         # chubby, wide stance
LEG_Z = .6            # stubby legs
HEAD_PIVOT = Vector((0, -.32, .63))
HEAD_SINK = Vector((0, .03, -.055))   # head sits down into the shoulders: short neck
HEAD_BONES = {'Head', 'Jaw', 'Ear_L', 'Ear_R'}


def warp(p, hw=0.0):
    """Chibi proportions: short muzzle, big head about the neck top, short wide body, stubby legs."""
    p = Vector(p)
    if hw:
        if p.y < SNOUT_Y:
            p.y = SNOUT_Y + (p.y - SNOUT_Y) * (1 - (1 - SNOUT_K) * hw)
        p = HEAD_PIVOT + (p - HEAD_PIVOT) * (1 + (HEAD_SCALE - 1) * hw) + HEAD_SINK * hw
    # the body squashes; the head only rides along with the neck
    p.y -= (1 - BODY_Y) * (hw * HEAD_PIVOT.y + (1 - hw) * p.y)
    p.x *= 1 + (BODY_X - 1) * (1 - hw)
    p.z = p.z * LEG_Z if p.z <= .45 else p.z - .45 * (1 - LEG_Z)
    return p


BONES = {n: (tuple(warp(h, n in HEAD_BONES)), tuple(warp(t, n in HEAD_BONES or n == 'Neck')), par)
         for n, (h, t, par) in BONES.items()}
CONNECTED = {'Spine', 'Chest', 'Tail2', 'Tail3'} | {f'{p}_{s}' for s in 'LR' for p in
             ('Front_Lower', 'Front_Paw', 'Hind_Shin', 'Hind_Hock', 'Hind_Paw')}

# ---------------------------------------------------------------- mesh builder
V, RAW, W, F, PART = [], [], [], [], []


def vert(p, w, warped=False):
    hw = sum(v for k, v in w.items() if k in HEAD_BONES) / sum(w.values())
    RAW.append(Vector(p))
    V.append(Vector(p) if warped else warp(p, hw))
    W.append(dict(w))
    return len(V) - 1


def ring(c, t, rx, rtop, rbot, w, n):
    side = (X - t * X.dot(t)).normalized()
    up = t.cross(side).normalized()
    out = []
    for i in range(n):
        a = 2 * pi * i / n
        r = rtop if sin(a) >= 0 else rbot
        out.append(vert(c + side * cos(a) * rx + up * sin(a) * r, w))
    return out


def loft(part, secs, n=8, cap0=.6, cap1=.6):
    """secs: [(pos, rx, rtop, rbot, weights)], ordered so the tangent points 'forward'."""
    k = {'leg': 1.38, 'paw': 1.38, 'torso': 1.2, 'tail': 1.35, 'neck': 1.2}.get(part, 1.0)
    secs = [(s[0], s[1] * k, s[2] * k, s[3] * k, s[4]) for s in secs]
    pts = [Vector(s[0]) for s in secs]
    rings = []
    for i, s in enumerate(secs):
        a, b = pts[max(i - 1, 0)], pts[min(i + 1, len(pts) - 1)]
        rings.append(ring(pts[i], (b - a).normalized(), s[1], s[2], s[3], s[4], n))
    for r1, r2 in zip(rings, rings[1:]):
        for j in range(n):
            F.append((r1[j], r1[(j + 1) % n], r2[(j + 1) % n], r2[j]))
            PART.append(part)
    for idx, ext in ((0, cap0), (-1, cap1)):
        if ext is None:
            continue
        r, s = rings[idx], secs[idx]
        t = (pts[1] - pts[0]).normalized() if idx == 0 else (pts[-1] - pts[-2]).normalized()
        sign = -1 if idx == 0 else 1
        c = vert(pts[idx] + t * sign * ext * min(s[1], max(s[2], s[3])), s[4])
        for j in range(n):
            F.append((r[j], r[(j + 1) % n], c))
            PART.append(part)
    return rings


def blob(part, center, radius, scale, w, sub=1, warped=True):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=radius)
    base = len(V)
    for v in bm.verts:
        vert(Vector(center) + Vector(tuple(v.co[i] * scale[i] for i in range(3))), w, warped)
    for f in bm.faces:
        F.append(tuple(base + v.index for v in f.verts))
        PART.append(part)
    bm.free()


def build_mesh():
    H = {'Hips': 1}
    # torso, rear to front so the ring 'up' is +Z
    torso = [(.300, .47, .055, .05, .05, H), (.25, .475, .092, .08, .08, H), (.15, .47, .104, .09, .085, H),
             (.04, .465, .098, .088, .07, {'Spine': .6, 'Hips': .4}), (-.07, .462, .108, .095, .095, {'Spine': 1}),
             (-.18, .46, .116, .10, .115, {'Chest': .7, 'Spine': .3}), (-.255, .462, .105, .10, .12, {'Chest': 1}),
             (-.305, .47, .07, .07, .085, {'Chest': 1})]
    loft('torso', [((0, y, z), w, t, b, wt) for y, z, w, t, b, wt in torso], n=12, cap0=.5, cap1=.45)
    # neck, chest to head
    loft('neck', [((0, -.22, .49), .07, .06, .08, {'Chest': 1}), ((0, -.27, .55), .066, .058, .07, {'Chest': .4, 'Neck': .6}),
                  ((0, -.31, .61), .06, .055, .06, {'Neck': .5, 'Head': .5}), ((0, -.335, .645), .055, .05, .05, {'Head': 1})],
         n=10, cap0=None, cap1=None)
    Hd = {'Head': 1}
    head = [(-.315, .655, .055, .05, .05), (-.34, .672, .082, .07, .065), (-.375, .678, .09, .072, .06),
            (-.41, .668, .075, .06, .045), (-.435, .652, .052, .04, .032), (-.475, .646, .042, .033, .022),
            (-.505, .643, .032, .026, .016)]
    head_start = len(F)
    loft('head', [((0, y, z), w, t, b, Hd) for y, z, w, t, b in head], n=10, cap0=.5, cap1=.35)
    head_faces = range(head_start, len(F))
    # round fluffy cheeks under the skull close the seam to the neck
    blob('head', (0, -.355, .628), .058, (1.3, 1.05, .7), Hd, sub=2, warped=False)
    loft('jaw', [((0, -.40, .624), .034, .01, .013, {'Head': .3, 'Jaw': .7}), ((0, -.45, .62), .028, .009, .012, {'Jaw': 1}),
                 ((0, -.488, .622), .019, .007, .009, {'Jaw': 1})], n=8, cap0=.5, cap1=.5)
    # eyes and nose sit on the actual head surface
    hv = sorted({i for f in head_faces for i in F[f]})
    remap = {g: i for i, g in enumerate(hv)}
    tree = BVHTree.FromPolygons([V[i] for i in hv], [[remap[i] for i in F[f]] for f in head_faces])

    def surface(x, z):
        q = warp((x, -.42, z), 1)
        hit = tree.ray_cast(Vector((q.x, -1, q.z)), Vector((0, 1, 0)))
        return hit[0], hit[1]
    for k in (1, -1):
        p, nrm = surface(.04 * k, .674)
        r = .021 * HEAD_SCALE
        blob('eye', p + Vector((0, .006, 0)), r, (1, .5, 1.12), Hd)
        side = Vector((nrm.x, nrm.y, 0)).normalized()
        blob('shine', p + side * .62 * r + Vector((0, 0, .42 * r)), .28 * r, (1, 1, 1), Hd, sub=1)
    p, _ = surface(0, .655)
    blob('nose', p + Vector((0, .006, 0)), .0135 * HEAD_SCALE, (1.25, .8, .85), Hd)
    # ears: four-sided pyramids with an inner panel facing forward
    for s, k in (('L', 1), ('R', -1)):
        Ew = {f'Ear_{s}': 1}
        Eb = {f'Ear_{s}': .7, 'Head': .3}
        cx, cy, cz = .046 * k, -.357, .712
        b = [vert((cx + dx * k, cy + dy, cz + dz), Eb) for dx, dy, dz in
             ((-.028, -.012, -.004), (.026, -.014, -.014), (.026, .016, -.014), (-.028, .018, -.004))]
        tip = vert((.07 * k, -.362, .778), Ew)
        for j in range(4):
            F.append((b[j], b[(j + 1) % 4], tip)); PART.append('ear')
        F.append((b[3], b[2], b[1], b[0])); PART.append('ear')
        a = vert((cx - .015 * k, cy - .016, cz + .006), Eb)
        c = vert((cx + .013 * k, cy - .018, cz - .002), Eb)
        t2 = vert((.066 * k, -.365, .764), Ew)
        F.append((a, c, t2)); PART.append('inner')
    # legs, top to bottom (ring 'up' faces forward)
    for s, k in (('L', 1), ('R', -1)):
        U, Lo, P = f'Front_Upper_{s}', f'Front_Lower_{s}', f'Front_Paw_{s}'
        x = .075 * k
        loft('leg', [((.058 * k, -.22, .47), .05, .055, .055, {'Chest': .5, U: .5}), ((x, -.205, .40), .05, .055, .055, {U: 1}),
                     ((x, -.165, .27), .036, .042, .046, {U: .5, Lo: .5}), ((x, -.19, .16), .03, .033, .033, {Lo: 1}),
                     ((x, -.215, .078), .026, .028, .028, {Lo: .5, P: .5})], n=8, cap0=.4, cap1=None)
        loft('paw', [((x, -.212, .085), .027, .028, .028, {Lo: .4, P: .6}), ((x, -.225, .035), .033, .03, .026, {P: 1}),
                     ((x, -.255, .022), .034, .022, .02, {P: 1}), ((x, -.282, .02), .026, .016, .016, {P: 1})],
             n=8, cap0=.3, cap1=.6)
        T, Sh, Hk, HP = f'Hind_Thigh_{s}', f'Hind_Shin_{s}', f'Hind_Hock_{s}', f'Hind_Paw_{s}'
        x = .08 * k
        loft('leg', [((.055 * k, .20, .49), .052, .07, .07, {'Hips': .5, T: .5}), ((x, .175, .40), .058, .08, .08, {T: 1}),
                     ((x, .13, .27), .04, .045, .045, {T: .5, Sh: .5}), ((x, .18, .20), .031, .035, .035, {Sh: 1}),
                     ((x, .235, .13), .026, .03, .03, {Sh: .5, Hk: .5}), ((x, .217, .055), .024, .026, .026, {Hk: .5, HP: .5})],
             n=8, cap0=.4, cap1=None)
        loft('paw', [((x, .218, .06), .025, .027, .027, {Hk: .4, HP: .6}), ((x, .2, .028), .032, .028, .024, {HP: 1}),
                     ((x, .172, .02), .033, .02, .019, {HP: 1}), ((x, .148, .018), .025, .015, .015, {HP: 1})],
             n=8, cap0=.3, cap1=.6)
    # tail: thick curled 진돗개 tail
    loft('tail', [((0, .27, .49), .03, .03, .03, {'Hips': .5, 'Tail1': .5}), ((0, .315, .55), .04, .042, .042, {'Tail1': 1}),
                  ((0, .34, .61), .045, .046, .046, {'Tail1': .5, 'Tail2': .5}), ((0, .345, .665), .042, .044, .044, {'Tail2': 1}),
                  ((0, .322, .703), .034, .036, .036, {'Tail2': .4, 'Tail3': .6}), ((0, .292, .716), .022, .024, .024, {'Tail3': 1}),
                  ((0, .268, .708), .01, .011, .011, {'Tail3': 1})], n=8, cap0=.4, cap1=.4)
    # collar band around the neck root, with a name tag at the throat
    c, t = Vector((0, -.282, .568)), Vector((0, -.5, .85)).normalized()
    Cw = {'Neck': .7, 'Chest': .3}
    rs = [ring(c + t * dz, t, rx, rx * .98, rx * 1.08, Cw, 12) for dz, rx in ((-.015, .094), (.015, .092), (.015, .078), (-.015, .08))]
    for a, b in zip(rs, rs[1:] + rs[:1]):
        for j in range(12):
            F.append((a[j], a[(j + 1) % 12], b[(j + 1) % 12], b[j])); PART.append('collar')
    throat = max(rs[0], key=lambda i: V[i].dot(Vector((0, -1, -.6))))
    p = V[throat] + Vector((0, -.012, -.014))
    blob('tag', p, .013, (1, .35, 1.1), Cw, sub=1)


def finish_mesh():
    me = bpy.data.meshes.new('DogMesh')
    me.from_pydata([tuple(v) for v in V], [], F)
    me.update()
    ob = bpy.data.objects.new('DogMesh', me)
    bpy.context.scene.collection.objects.link(ob)
    for name, hexc in PALETTE:
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*srgb(hexc), 1)
        m.use_nodes = True
        b = m.node_tree.nodes.get('Principled BSDF')
        b.inputs['Base Color'].default_value = m.diffuse_color
        b.inputs['Roughness'].default_value = .88
        me.materials.append(m)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    # handmade facets: tiny seeded jitter on the fur shells, never on eyes/nose/tag
    soft = {'torso', 'neck', 'head', 'jaw', 'leg', 'paw', 'tail'}
    moved = set()
    for poly in me.polygons:
        if PART[poly.index] in soft:
            for i in poly.vertices:
                if i not in moved:
                    moved.add(i)
                    me.vertices[i].co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * .0022
    for v in me.vertices:
        v.co.z = max(v.co.z, 0.0)
    me.update()
    for poly in me.polygons:
        part, n = PART[poly.index], poly.normal
        c = sum((RAW[i] for i in poly.vertices), Vector()) / len(poly.vertices)
        mat = FUR
        if part in ('jaw', 'paw'):
            mat = CREAM
        elif part == 'torso' and (n.z < -.45 or (c.y < -.25 and n.y < -.35 and c.z < .5)):
            mat = CREAM
        elif part == 'neck' and n.y < -.3 and n.z < .45:
            mat = CREAM
        elif part == 'head' and c.y < -.40 and c.z < .664 and n.z < .5:
            mat = CREAM
        elif part == 'leg' and (c.z < .13 or (n.y < -.5 and c.y < 0 and c.z < .3)):
            mat = CREAM
        elif part == 'tail' and n.z < -.25:
            mat = CREAM
        elif part == 'shine':
            mat = CREAM
        elif part in ('eye', 'nose'):
            mat = DARK
        elif part == 'collar':
            mat = COLLAR
        elif part == 'tag':
            mat = TAG
        elif part == 'inner':
            mat = INNER
        poly.material_index = mat
        poly.use_smooth = False
    return ob


# ---------------------------------------------------------------- rig
def build_rig(mesh_ob):
    data = bpy.data.armatures.new('DogRig')
    arm = bpy.data.objects.new('Dog', data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = data.edit_bones

    def add(name, head, tail, parent, deform=True, connect=False):
        b = eb.new(name)
        b.head, b.tail = head, tail
        d = (Vector(tail) - Vector(head)).normalized()
        b.align_roll(Vector((0, -1, 0)) if abs(d.z) > .7 else Vector((0, 0, 1)))
        b.use_deform = deform
        if parent:
            b.parent = eb[parent]
            b.use_connect = connect
        return b
    for name, (h, t, p) in BONES.items():
        add(name, h, t, p, connect=name in CONNECTED)
    # temporary authoring controls (removed before export)
    for s in 'LR':
        for side, paw in (('Front', f'Front_Paw_{s}'), ('Hind', f'Hind_Paw_{s}')):
            src = eb[paw]
            c = add(f'IK_{side}_{s}', src.head.copy(), src.tail.copy(), 'Root', deform=False)
            c.roll = src.roll
        hk = eb[f'Hind_Hock_{s}']
        add(f'HockTarget_{s}', hk.head.copy(), hk.head + Vector((0, 0, .05)), 'Root', deform=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    P = arm.pose.bones
    for s in 'LR':
        for chain in (f'Front_Upper_{s}', f'Front_Lower_{s}', f'Hind_Thigh_{s}', f'Hind_Shin_{s}'):
            P[chain].lock_ik_y = P[chain].lock_ik_z = True
        ik = P[f'Front_Lower_{s}'].constraints.new('IK')
        ik.target, ik.subtarget, ik.chain_count, ik.iterations = arm, f'IK_Front_{s}', 2, 800
        ik = P[f'Hind_Shin_{s}'].constraints.new('IK')
        ik.target, ik.subtarget, ik.chain_count, ik.iterations = arm, f'HockTarget_{s}', 2, 800
        dt = P[f'Hind_Hock_{s}'].constraints.new('DAMPED_TRACK')
        dt.target, dt.subtarget, dt.track_axis = arm, f'IK_Hind_{s}', 'TRACK_Y'
        for side in ('Front', 'Hind'):
            cr = P[f'{side}_Paw_{s}'].constraints.new('COPY_ROTATION')
            cr.target, cr.subtarget = arm, f'IK_{side}_{s}'
            cr.target_space = cr.owner_space = 'WORLD'
    # skin
    mesh_ob.parent = arm
    mod = mesh_ob.modifiers.new('Armature', 'ARMATURE')
    mod.object = arm
    groups = {n: mesh_ob.vertex_groups.new(name=n) for n in BONES}
    for i, w in enumerate(W):
        total = sum(w.values())
        for n, v in w.items():
            groups[n].add([i], v / total, 'REPLACE')
    return arm


# ---------------------------------------------------------------- posing helpers
class Pose:
    def __init__(self, arm):
        self.arm = arm
        self.P = arm.pose.bones
        self.rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
        self.clear()

    def clear(self):
        self.rot = {}
        self.loc = {}
        for pb in self.P:
            pb.rotation_mode = 'QUATERNION'
            pb.matrix_basis = Matrix()

    def r(self, name, axis, deg):
        """Rotate a bone relative to its parent, about an axis given in the rest (armature) frame."""
        self.rot.setdefault(name, []).append(Quaternion(Vector(axis), radians(deg)))

    def move(self, name, v):
        self.loc[name] = self.loc.get(name, Vector()) + Vector(v)

    def target(self, name, pos):
        self.loc[name] = Vector(pos) - self.rest[name].to_translation()

    def apply(self):
        for name in set(self.rot) | set(self.loc):
            R = self.rest[name].to_3x3()
            q = Quaternion()
            for d in self.rot.get(name, []):
                q = d @ q
            basis = (R.inverted() @ q.to_matrix() @ R).to_4x4()
            basis.translation = R.inverted() @ self.loc.get(name, Vector())
            self.P[name].matrix_basis = basis
        bpy.context.view_layer.update()

    def head(self, name):
        return (self.P[name].matrix @ Matrix()).to_translation()


def rest_head(name):
    return Vector(BONES[name][0])


HOCK_OFFSET = rest_head('Hind_Hock_L') - rest_head('Hind_Paw_L')


def place_feet(p, feet):
    """feet: {key: (dy, dz, paw_pitch_deg, extra_hock_deg)} relative to the rest paw joints."""
    for s in 'LR':
        for side in ('Front', 'Hind'):
            dy, dz, pitch, hock = feet.get(f'{side}_{s}', (0, 0, 0, 0))
            base = rest_head(f'{side}_Paw_{s}')
            pos = base + Vector((0, dy, dz))
            p.target(f'IK_{side}_{s}', pos)
            if pitch:
                p.r(f'IK_{side}_{s}', X, pitch)
            if side == 'Hind':
                off = Quaternion(X, radians(hock)) @ HOCK_OFFSET
                p.target(f'HockTarget_{s}', pos + off)


def ease(t):
    t = min(max(t, 0), 1)
    return t * t * (3 - 2 * t)


def gait(phase, duty, sweep, lift):
    phase %= 1
    if phase < duty:
        s = phase / duty
        return sweep * (s - .5), 0.0, 0.0, s
    s = (phase - duty) / (1 - duty)
    dy = sweep * (.5 - (1 - cos(pi * s)) / 2)
    return dy, lift * sin(pi * s), sin(pi * s), None


# ---------------------------------------------------------------- clips
def idle(p, f, n):
    t = f / n
    br = sin(2 * pi * t * 2)
    p.move('Hips', (0, 0, .003 * br))
    p.r('Chest', X, -1.2 * br)
    p.r('Neck', X, 1.5 * sin(2 * pi * t))
    p.r('Head', (0, 0, 1), 7 * sin(2 * pi * t))
    p.r('Head', X, -2 * sin(4 * pi * t))
    for s, k in (('L', 1), ('R', -1)):
        tw = max(0, sin(2 * pi * (t * 2 - (.1 if k > 0 else .35)))) ** 6
        p.r(f'Ear_{s}', X, -14 * tw)
    p.r('Tail1', (0, 1, 0), 10 * sin(2 * pi * t * 3))
    p.r('Tail2', (0, 1, 0), 7 * sin(2 * pi * t * 3 - .6))
    place_feet(p, {})


WALK = dict(frames=12, speed=.7, duty=.6)
RUN = dict(frames=8, speed=3.5, duty=.26)


def walk(p, f, n):
    t = f / n
    sweep = WALK['speed'] * WALK['duty'] * n / FPS
    phases = {'Hind_L': 0, 'Front_L': .25, 'Hind_R': .5, 'Front_R': .75}
    feet = {}
    for key, ph in phases.items():
        dy, dz, sw, _ = gait(t - ph, WALK['duty'], sweep, .042 if key.startswith('Front') else .038)
        feet[key] = (dy, dz, 55 * sw if key.startswith('Front') else 25 * sw, 35 * sw if key.startswith('Hind') else 0)
    place_feet(p, feet)
    p.move('Hips', (0, 0, -.012 + .008 * cos(4 * pi * t)))
    p.r('Hips', (0, 1, 0), 2.5 * sin(2 * pi * t))
    p.r('Hips', (0, 0, 1), 2 * sin(2 * pi * t))
    p.r('Chest', (0, 1, 0), -3 * sin(2 * pi * (t - .25)))
    p.r('Chest', (0, 0, 1), -2 * sin(2 * pi * (t - .25)))
    p.r('Neck', X, 2 * cos(4 * pi * t))
    p.r('Head', X, -2 * cos(4 * pi * t))
    p.r('Tail1', (0, 1, 0), 12 * sin(2 * pi * t))
    p.r('Tail2', (0, 1, 0), 8 * sin(2 * pi * t - .7))
    for s in 'LR':
        p.r(f'Ear_{s}', X, -4 - 3 * cos(4 * pi * t))


def run(p, f, n):
    t = f / n
    sweep = RUN['speed'] * RUN['duty'] * n / FPS
    phases = {'Hind_L': 0, 'Hind_R': .1, 'Front_L': .5, 'Front_R': .6}
    feet = {}
    for key, ph in phases.items():
        front = key.startswith('Front')
        dy, dz, sw, _ = gait(t - ph, RUN['duty'], sweep, .085 if front else .075)
        dy -= .02 if front else -.01
        feet[key] = (dy, dz, 80 * sw if front else 35 * sw, 55 * sw if not front else 0)
    place_feet(p, feet)
    flex = cos(2 * pi * (t - .02))            # +1 gathered (hinds under), -1 stretched
    p.move('Hips', (0, .02 * flex, -.035 + .022 * cos(4 * pi * (t - .2))))
    p.r('Hips', X, -5 * sin(2 * pi * (t - .05)))
    p.r('Spine', X, 6 * flex)
    p.r('Chest', X, 4 * flex)
    p.r('Neck', X, -10 + 6 * sin(2 * pi * (t - .1)))
    p.r('Head', X, 8 - 6 * sin(2 * pi * (t - .1)))
    p.r('Tail1', X, 35)
    p.r('Tail2', X, 25 + 6 * flex)
    p.r('Tail3', X, 10)
    for s in 'LR':
        p.r(f'Ear_{s}', X, -28)


def sit_params(arm, p):
    """Solve the seated body once: rear down, chest up, shoulders over planted front paws."""
    PITCH, SP, CH = -40, 6, 8
    p.clear()
    p.r('Hips', X, PITCH); p.r('Spine', X, SP); p.r('Chest', X, CH)
    place_feet(p, {})
    p.apply()
    sh = arm.pose.bones['Front_Upper_L'].head
    drop = sh.z - (rest_head('Front_Upper_L').z - .025)
    fwd = rest_head('Front_Upper_L').y + .02 - sh.y
    return dict(pitch=PITCH, spine=SP, chest=CH, drop=drop, fwd=fwd)


SIT = {}


def sit_pose(p, a, breathe=0.0, wag=0.0, look=0.0):
    """a: 0 standing .. 1 seated."""
    e = ease(a)
    rear = ease(a * 1.25)               # the rear leads, the chest follows
    p.move('Hips', (0, SIT['fwd'] * e, -SIT['drop'] * rear + .003 * breathe))
    p.r('Hips', X, SIT['pitch'] * rear)
    p.r('Spine', X, SIT['spine'] * e)
    p.r('Chest', X, SIT['chest'] * e - 1.2 * breathe)
    p.r('Neck', X, 14 * e)
    p.r('Head', X, 16 * e - 2 * look)
    p.r('Head', (0, 0, 1), 8 * look)
    hips_y = rest_head('Hips').y + SIT['fwd'] * e
    hind_dy = min(0, (hips_y - .02) - rest_head('Hind_Paw_L').y) * e
    lift = .02 * sin(pi * min(1, a * 1.3))
    feet = {}
    for s in 'LR':
        feet[f'Hind_{s}'] = (hind_dy, lift, 0, -56 * rear)
    place_feet(p, feet)
    p.r('Tail1', X, 95 * e)
    p.r('Tail2', X, 30 * e)
    p.r('Tail1', (0, 1, 0), 14 * e + 10 * wag)
    p.r('Tail2', (0, 1, 0), 10 * wag)
    for s in 'LR':
        p.r(f'Ear_{s}', X, -6 * e)


def sit_down(p, f, n):
    sit_pose(p, f / n)


def stand_up(p, f, n):
    sit_pose(p, 1 - f / n)


def sit_loop(p, f, n):
    t = f / n
    sit_pose(p, 1, breathe=sin(4 * pi * t), wag=sin(6 * pi * t), look=sin(2 * pi * t))


ATTACK_HIT = 12


def attack(p, f, n):
    t = f / n
    crouch = ease(t / .3) * (1 - ease((t - .3) / .15))
    lunge = ease((t - .3) / .15) * (1 - ease((t - .55) / .45))
    jaw = ease((t - .28) / .1) * (1 - ease((t - .43) / .07))
    p.move('Hips', (0, .04 * crouch - .11 * lunge, -.04 * crouch - .015 * lunge))
    p.r('Hips', X, 5 * crouch + 2 * lunge)
    p.r('Chest', X, 6 * crouch + 3 * lunge)
    p.r('Neck', X, 18 * crouch + 38 * lunge)
    p.r('Head', X, -6 * crouch - 24 * lunge)
    p.r('Jaw', X, 38 * jaw)
    p.r('Tail1', X, -15 * (crouch + lunge))
    for s in 'LR':
        p.r(f'Ear_{s}', X, -35 * max(crouch, lunge))
    step = lunge
    lift = .035 * sin(pi * min(1, max(0, (t - .3) / .2)))
    place_feet(p, {'Front_L': (-.07 * step, lift, 0, 0), 'Front_R': (-.05 * step, lift * .8, 0, 0)})


CLIPS = [  # name, function, frames, loop
    ('Idle', idle, 60, True),
    ('Walk', walk, WALK['frames'], True),
    ('Run', run, RUN['frames'], True),
    ('SitDown', sit_down, 24, False),
    ('Sit', sit_loop, 60, True),
    ('StandUp', stand_up, 20, False),
    ('Attack', attack, 24, False),
]


# ---------------------------------------------------------------- bake to FK
def record(arm, p):
    deform = [b.name for b in arm.data.bones if b.use_deform]
    out, checks = {}, {}
    for name, fn, n, loop in CLIPS:
        frames, foot = [], []
        for f in range(n + 1):
            p.clear()
            fn(p, f, n)
            p.apply()
            frames.append({b: arm.pose.bones[b].matrix.copy() for b in deform})
            low = min(arm.pose.bones[f'{side}_Paw_{s}'].tail.z for s in 'LR' for side in ('Front', 'Hind'))
            miss = max((arm.pose.bones[f'Front_Lower_{s}'].tail - arm.pose.bones[f'IK_Front_{s}'].head).length for s in 'LR')
            miss = max(miss, max((arm.pose.bones[f'Hind_Shin_{s}'].tail - arm.pose.bones[f'HockTarget_{s}'].head).length for s in 'LR'))
            foot.append((low, miss))
        out[name] = frames
        checks[name] = {'frames': n + 1, 'seconds': round(n / FPS, 3), 'loop': loop,
                        'min_toe_height_m': round(min(v[0] for v in foot), 4),
                        'max_ik_miss_m': round(max(v[1] for v in foot), 4)}
    p.clear()
    return out, checks


def strip_controls(arm):
    for pb in arm.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
        pb.lock_ik_y = pb.lock_ik_z = False
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for b in [b for b in arm.data.edit_bones if not b.use_deform]:
        arm.data.edit_bones.remove(b)
    bpy.ops.object.mode_set(mode='OBJECT')


def bake(arm, recorded):
    rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    parent = {b.name: b.parent.name if b.parent else None for b in arm.data.bones}
    arm.animation_data_create()
    actions = {}
    for name, fn, n, loop in CLIPS:
        act = bpy.data.actions.new(f'Dog_{name}')
        act.use_fake_user = True
        arm.animation_data.action = act
        prev = {}
        for f, mats in enumerate(recorded[name]):
            for b, M in mats.items():
                pa = parent[b]
                if pa:
                    basis = (rest[pa].inverted() @ rest[b]).inverted() @ mats[pa].inverted() @ M
                else:
                    basis = rest[b].inverted() @ M
                loc, q, _ = basis.decompose()
                if b in prev and prev[b].dot(q) < 0:
                    q.negate()
                prev[b] = q
                pb = arm.pose.bones[b]
                pb.rotation_mode = 'QUATERNION'
                pb.location, pb.rotation_quaternion, pb.scale = loc, q, (1, 1, 1)
                pb.keyframe_insert('location', frame=f, group=b)
                pb.keyframe_insert('rotation_quaternion', frame=f, group=b)
        act.use_frame_range = True
        act.frame_start, act.frame_end = 0, n
        act.use_cyclic = loop
        actions[name] = act
    return actions


def set_action(arm, act):
    ad = arm.animation_data
    ad.action = act
    if hasattr(ad, 'action_slot') and ad.action_slot is None and len(act.slots):
        ad.action_slot = act.slots[0]


def verify_bake(arm, actions, recorded):
    err = 0.0
    sc = bpy.context.scene
    for name, act in actions.items():
        set_action(arm, act)
        frames = recorded[name]
        for f in sorted({0, len(frames) // 2, len(frames) - 1}):
            sc.frame_set(f)
            bpy.context.view_layer.update()
            for b, M in frames[f].items():
                pb = arm.pose.bones[b]
                err = max(err, (pb.head - M.to_translation()).length,
                          (pb.tail - M @ Vector((0, arm.data.bones[b].length, 0))).length)
    return err


# ---------------------------------------------------------------- render
def studio():
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = 'AgX'
    sc.world = bpy.data.worlds.new('Studio')
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes['Background']
    bg.inputs[0].default_value = (.30, .33, .39, 1)
    bg.inputs[1].default_value = .45
    for name, loc, power, size in (('Key', (-2.2, -2.6, 3.2), 380, 2.4), ('Fill', (2.8, -1.6, 1.6), 110, 3),
                                   ('Rim', (.6, 3.0, 2.4), 220, 2)):
        L = bpy.data.lights.new(name, 'AREA')
        L.energy, L.shape, L.size = power, 'DISK', size
        o = bpy.data.objects.new(name, L)
        sc.collection.objects.link(o)
        o.location = loc
        o.rotation_euler = (Vector((0, 0, .35)) - o.location).to_track_quat('-Z', 'Y').to_euler()
    gm = bpy.data.meshes.new('Ground')
    gm.from_pydata([(-20, -20, 0), (20, -20, 0), (20, 20, 0), (-20, 20, 0)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new('Ground', gm)
    gmat = bpy.data.materials.new('GroundShadow')
    gmat.use_nodes = True
    gmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.36, .39, .45, 1)
    gm.materials.append(gmat)
    sc.collection.objects.link(g)
    cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def snapshot(mesh_ob, offset, yaw, coll):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(mesh_ob.evaluated_get(dg), depsgraph=dg)
    ob = bpy.data.objects.new('snap', me)
    coll.objects.link(ob)
    ob.matrix_world = Matrix.Translation(offset) @ Matrix.Rotation(radians(yaw), 4, 'Z')
    return ob


def render_sheet(path, cam, items, width, height, cam_dir, ortho, center):
    sc = bpy.context.scene
    coll = bpy.data.collections.new('Sheet')
    sc.collection.children.link(coll)
    for fn in items:
        fn(coll)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = ortho
    cam.location = Vector(center) + Vector(cam_dir).normalized() * 8
    cam.rotation_euler = (Vector(center) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.resolution_x, sc.render.resolution_y = width, height
    sc.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(coll)


def renders(arm, mesh_ob, actions):
    RENDERS.mkdir(exist_ok=True)
    cam = studio()
    sc = bpy.context.scene
    mesh_ob.hide_render = True

    def at(act, f, x, yaw):
        def go(coll):
            set_action(arm, act)
            sc.frame_set(f)
            bpy.context.view_layer.update()
            snapshot(mesh_ob, Vector((x, 0, 0)), yaw, coll)
        return go
    idle_act = actions['Idle']
    views = [at(idle_act, 0, x, yaw) for x, yaw in ((-1.5, 0), (-.5, 90), (.5, 180), (1.5, -35))]
    render_sheet(RENDERS / 'Dog_Views.png', cam, views, 1600, 520, (0, -1, .32), 4.1, (0, 0, .3))
    render_sheet(RENDERS / 'Dog_Hero.png', cam, [at(idle_act, 0, 0, -35)], 900, 900, (.0, -1, .55), 1.0, (0, 0, .33))
    rows = [('Walk', [0, 3, 6, 9]), ('Run', [0, 2, 4, 6]), ('SitDown', [0, 8, 16, 24]),
            ('Sit', [0, 15, 30, 45]), ('Attack', [0, 7, 10, 12])]
    for name, frames in rows:
        # yaw 90 turns the dog to face +X, so a front camera sees every frame in profile
        items = [at(actions[name], f, -1.78 + i * 1.04, 90) for i, f in enumerate(frames)]
        render_sheet(RENDERS / f'Dog_{name}.png', cam, items, 1600, 400, (0, -1, .12), 4.4, (0, 0, .32))
    set_action(arm, idle_act)
    sc.frame_set(0)
    mesh_ob.hide_render = False


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    build_mesh()
    mesh_ob = finish_mesh()
    arm = build_rig(mesh_ob)
    p = Pose(arm)
    SIT.update(sit_params(arm, p))
    recorded, checks = record(arm, p)
    strip_controls(arm)
    actions = bake(arm, recorded)
    bake_err = verify_bake(arm, actions, recorded)
    set_action(arm, actions['Idle'])
    sc.frame_set(0)
    tris = sum(len(poly.vertices) - 2 for poly in mesh_ob.data.polygons)
    zs = [v.co.z for v in mesh_ob.data.vertices]
    ys = [v.co.y for v in mesh_ob.data.vertices]
    report = {
        'triangles': tris, 'vertices': len(mesh_ob.data.vertices),
        'bones': [b.name for b in arm.data.bones], 'bone_count': len(arm.data.bones),
        'materials': [m.name for m in mesh_ob.data.materials],
        'height_m': round(max(zs), 3), 'length_nose_to_tail_m': round(max(ys) - min(ys), 3),
        'fps': FPS, 'clips': checks, 'bake_max_error_m': round(bake_err, 6),
        'walk_speed_mps': WALK['speed'], 'run_speed_mps': RUN['speed'],
        'attack_hit_frame': ATTACK_HIT, 'sit_solution': {k: round(v, 4) for k, v in SIT.items()},
        'unweighted_vertices': sum(1 for v in mesh_ob.data.vertices if not v.groups),
    }
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'Dog.blend'))
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    mesh_ob.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=str(ROOT / 'Dog.fbx'), use_selection=True, object_types={'MESH', 'ARMATURE'},
                             axis_forward='-Z', axis_up='Y', add_leaf_bones=False, use_armature_deform_only=True,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             use_mesh_modifiers=False, path_mode='AUTO', use_custom_props=False)
    renders(arm, mesh_ob, actions)
    (ROOT / 'validation.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print('DOG_REPORT', json.dumps(report))


main()
