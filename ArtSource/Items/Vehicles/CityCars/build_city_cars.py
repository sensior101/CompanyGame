"""도시 자동차 세트 VC01~VC11 (무브랜드). Blender 5.2.

실행 (저장소 루트):
  blender --background --factory-startup --python ArtSource/Items/Vehicles/CityCars/build_city_cars.py -- [--only VC03,VC08] [--samples 160] [--no-render] [--no-sheet]

좌표: 스펙은 Unity 기준(x 오른쪽, y 위, z 앞). Blender 내부는 (x, -z, y). FBX는 -Z forward / Y up 으로 내보내
Unity에서 Body 피벗 = 지면 중앙, 바퀴 피벗 = 허브 중심, 바퀴 축 = X.
"""
import bpy, bmesh, math, os, sys, json, time
from mathutils import Vector, Matrix
from bpy_extras.object_utils import world_to_camera_view

SRC = os.path.dirname(os.path.abspath(__file__))
REN = os.path.join(SRC, 'Renders'); os.makedirs(REN, exist_ok=True)
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
def opt(flag, default=None):
    if flag in argv:
        i = argv.index(flag)
        return argv[i + 1] if i + 1 < len(argv) and not argv[i + 1].startswith('--') else True
    return default
ONLY = opt('--only'); ONLY = set(ONLY.split(',')) if isinstance(ONLY, str) else None
SAMPLES = int(opt('--samples', 160)); NO_RENDER = bool(opt('--no-render', False)); NO_SHEET = bool(opt('--no-sheet', False))
T0 = time.time()

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'; scene.unit_settings.scale_length = 1.0; scene.unit_settings.length_unit = 'METERS'

def U(x, y, z): return Vector((x, -z, y))          # Unity -> Blender
def clamp(v, a=0.0, b=1.0): return max(a, min(b, v))
def lerp(a, b, t): return a + (b - a) * t
def _lin(keys, d):
    if d <= keys[0][0]: return keys[0][1]
    for a, b in zip(keys, keys[1:]):
        if d <= b[0]: return lerp(a[1], b[1], (d - a[0]) / (b[0] - a[0])) if b[0] > a[0] else b[1]
    return keys[-1][1]
def interp(keys, d, default_r=.0):
    """Piecewise linear through (d, value[, fillet_radius]) keys; interior corners are rounded by a
    quadratic Bezier over +-radius, so straight planes (bonnet, windscreen, roof) meet in smooth bends."""
    for i in range(1, len(keys) - 1):
        dk, vk = keys[i][0], keys[i][1]; r = keys[i][2] if len(keys[i]) > 2 else default_r
        if r <= 0: continue
        ra = min(r, (dk - keys[i - 1][0]) / 2); rb = min(r, (keys[i + 1][0] - dk) / 2)
        if dk - ra <= d <= dk + rb:
            A = (dk - ra, _lin(keys, dk - ra)); B = (dk + rb, _lin(keys, dk + rb))
            lo, hi = 0.0, 1.0
            for _ in range(30):
                t = (lo + hi) / 2; dt = (1 - t) ** 2 * A[0] + 2 * t * (1 - t) * dk + t * t * B[0]
                lo, hi = (t, hi) if dt < d else (lo, t)
            t = (lo + hi) / 2
            return (1 - t) ** 2 * A[1] + 2 * t * (1 - t) * vk + t * t * B[1]
    return _lin(keys, d)

# ---------------------------------------------------------------- materials
def srgb(h):
    v = [int(h[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return tuple(c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in v) + (1,)
MATS = {}
def mat(name, hex, metallic=0, rough=.5, coat=0, transmission=0, emission=0, emit_hex=None):
    if name in MATS: return MATS[name]
    m = bpy.data.materials.new(name); m.use_nodes = True; m.diffuse_color = srgb(hex)
    i = m.node_tree.nodes['Principled BSDF'].inputs
    i['Base Color'].default_value = srgb(hex); i['Metallic'].default_value = metallic; i['Roughness'].default_value = rough
    if coat: i['Coat Weight'].default_value = coat; i['Coat Roughness'].default_value = .04
    if transmission: i['Transmission Weight'].default_value = transmission; i['IOR'].default_value = 1.5
    if emission: i['Emission Color'].default_value = srgb(emit_hex or hex); i['Emission Strength'].default_value = emission
    MATS[name] = m; return m
TRIM = mat('Car_Trim', '#17191B', 0, .55); RUBBER = mat('Car_Rubber', '#141516', 0, .85)
CHROME = mat('Car_Chrome', '#D9DCDF', 1, .14); ALLOY = mat('Car_Alloy', '#B4B8BD', 1, .3); STEEL = mat('Car_Steel', '#4E5257', .85, .5)
DARKCHROME = mat('Car_DarkChrome', '#5A5E63', 1, .28); LAMPGLASS = mat('Car_LampGlass', '#0B0D10', .0, .05, coat=1)
GLASS = mat('Car_Glass', '#9AA6AE', 0, .0, transmission=1)          # lightly tinted, reflective
def _thin_glass(m):
    """Camera rays see glass; shadow/diffuse rays pass straight through, so the cabin is lit without caustic noise."""
    nt = m.node_tree; p = nt.nodes['Principled BSDF']; out = nt.nodes['Material Output']
    lp = nt.nodes.new('ShaderNodeLightPath'); tr = nt.nodes.new('ShaderNodeBsdfTransparent'); mx = nt.nodes.new('ShaderNodeMixShader')
    add = nt.nodes.new('ShaderNodeMath'); add.operation = 'MAXIMUM'
    nt.links.new(lp.outputs['Is Shadow Ray'], add.inputs[0]); nt.links.new(lp.outputs['Is Diffuse Ray'], add.inputs[1])
    nt.links.new(add.outputs[0], mx.inputs['Fac']); nt.links.new(p.outputs[0], mx.inputs[1]); nt.links.new(tr.outputs[0], mx.inputs[2])
    nt.links.new(mx.outputs[0], out.inputs['Surface'])
_thin_glass(GLASS)
LF = mat('Car_LightFront', '#EEF3F8', .0, .08, coat=1, emission=1.2, emit_hex='#DDEBFF'); LR = mat('Car_LightRear', '#7A0806', 0, .08, coat=1, emission=1.4, emit_hex='#FF1A0E')
AMBER = mat('Car_LightAmber', '#E8892B', 0, .15, coat=1, emission=.6)
INTERIOR = mat('Car_Interior', '#1C1D20', 0, .8); SEAT = mat('Car_Seat', '#3A3835', 0, .9); HEADLINER = mat('Car_Headliner', '#55575A', 0, .9)
PLATE = mat('Car_Plate', '#F1F2EF', 0, .35); PLATE_Y = mat('Car_PlateYellow', '#F0C330', 0, .35)
SIGN_W = mat('Car_SignWhite', '#FFFFFF', 0, .3, emission=4); SIGN_B = mat('Car_SignBlue', '#1844B8', 0, .3, emission=6, emit_hex='#2F6BFF')
SIGN_R = mat('Car_SignRed', '#C8201A', 0, .3, emission=6, emit_hex='#FF3A2A')
DECAL_B = mat('Car_DecalBlue', '#1C4FBF', .1, .4, coat=1); DECAL_W = mat('Car_DecalWhite', '#F4F5F6', .1, .4, coat=1)
def paint_mat(code, hex):
    """Car paint: soft metallic base under a crisp clear coat."""
    m = mat('Car_Paint_' + code, hex, .08, .45, coat=1.0); m.node_tree.nodes['Principled BSDF'].inputs['Coat Roughness'].default_value = .05
    return m

# ---------------------------------------------------------------- mesh helpers
COL = None
def new_obj(name, bm, mats, smooth=True):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    me.polygons.foreach_set('use_smooth', [smooth] * len(me.polygons))
    o = bpy.data.objects.new(name, me); COL.objects.link(o); return o

def box(name, center, size, m, bevel=.01, rot=None, segs=3):
    """Blender coords. size=(x,y,z). rot: 4x4 rotation about the box centre."""
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(size) * .45), segments=segs, profile=.5, affect='EDGES', clamp_overlap=True)
    M = Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return new_obj(name, bm, [m], smooth=bool(bevel))
def boxU(name, c, size, m, bevel=.01, rot=None):
    """Unity centre (x,y,z), size (width, height, depth)."""
    return box(name, U(*c), (size[0], size[2], size[1]), m, bevel, rot)
def normal_rot(n):
    return n.normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4()
def surf_box(name, body, origin_u, dir_u, size, m, embed=.03, bevel=.015):
    """Beveled box (w, h, depth) pressed into the body surface found by a ray; local Z = outward normal."""
    ok, loc, nrm, _ = body.ray_cast(U(*origin_u), U(*dir_u).normalized(), distance=30)
    if not ok: print('  ! no surface hit for', name); return None
    c = loc + nrm.normalized() * (size[2] / 2 - embed)
    return box(name, c, (size[0], size[1], size[2]), m, bevel, normal_rot(nrm))

def cylinder(name, center, radius, depth, m, axis='Z', segs=48, rot=None):
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=depth)
    R = {'Z': Matrix.Identity(4), 'X': Matrix.Rotation(math.radians(90), 4, 'Y'), 'Y': Matrix.Rotation(math.radians(90), 4, 'X')}[axis]
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4)) @ R, verts=bm.verts)
    return new_obj(name, bm, [m])

def revolve(name, profile, m, segs=64, matrix=None):
    """Closed (x, r) profile revolved around local X, then matrix applied."""
    bm = bmesh.new(); n = len(profile); ring = []
    for a in range(segs):
        t = math.tau * a / segs
        ring.append([bm.verts.new((x, r * math.cos(t), r * math.sin(t))) for x, r in profile])
    for a in range(segs):
        b = (a + 1) % segs
        for i in range(n):
            j = (i + 1) % n
            if profile[i] == profile[j]: continue
            try: bm.faces.new((ring[a][i], ring[a][j], ring[b][j], ring[b][i]))
            except ValueError: pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if matrix: bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)
    return new_obj(name, bm, [m])

def patch(name, p00, p01, p10, p11, nu, nv, m, target=None, offset=.002):
    """Bilinear grid, optionally shrink-wrapped onto target."""
    bm = bmesh.new(); grid = []
    for i in range(nu + 1):
        u = i / nu; row = []
        for j in range(nv + 1):
            v = j / nv
            row.append(bm.verts.new(p00.lerp(p01, u).lerp(p10.lerp(p11, u), v)))
        grid.append(row)
    for i in range(nu):
        for j in range(nv): bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    o = new_obj(name, bm, [m], smooth=False)
    if target:
        sw = o.modifiers.new('wrap', 'SHRINKWRAP'); sw.target = target; sw.wrap_method = 'NEAREST_SURFACEPOINT'; sw.offset = offset
    return o

FONT_B = bpy.data.fonts.load('C:/Windows/Fonts/malgunbd.ttf'); FONT_R = bpy.data.fonts.load('C:/Windows/Fonts/malgun.ttf')
def text_mesh(name, body_text, size, m, matrix, extrude=.0015, target=None, offset=.003, font=None):
    cu = bpy.data.curves.new(name, 'FONT'); cu.body = body_text; cu.size = size; cu.extrude = extrude
    cu.align_x = 'CENTER'; cu.align_y = 'CENTER'; cu.font = font or FONT_B
    t = bpy.data.objects.new(name, cu); COL.objects.link(t)
    bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(t.evaluated_get(dg), depsgraph=dg); me.transform(matrix)
    bpy.data.objects.remove(t); bpy.data.curves.remove(cu)
    me.materials.append(m); o = bpy.data.objects.new(name, me); COL.objects.link(o)
    if target:
        sw = o.modifiers.new('wrap', 'SHRINKWRAP'); sw.target = target; sw.wrap_method = 'NEAREST_SURFACEPOINT'; sw.offset = offset
    return o

def apply_mods(o):
    bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    if not me.materials:
        for m in o.data.materials: me.materials.append(m)
    old = o.data; o.modifiers.clear(); o.data = me; bpy.data.meshes.remove(old)

def merge(objs, name, sharp_deg=38):
    """Join evaluated meshes into one object; shared material slots; smooth with angle-based sharp edges."""
    bpy.context.view_layer.update(); dg = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new(); mats = []
    for o in objs:
        tmp = bpy.data.meshes.new_from_object(o.evaluated_get(dg), depsgraph=dg); tmp.transform(o.matrix_world)
        src = list(tmp.materials) if tmp.materials else list(o.data.materials)
        remap = []
        for m in src:
            if m not in mats: mats.append(m)
            remap.append(mats.index(m))
        for p in tmp.polygons: p.material_index = remap[min(p.material_index, len(remap) - 1)] if remap else 0
        bm.from_mesh(tmp); bpy.data.meshes.remove(tmp)
    for e in bm.edges:
        e.smooth = len(e.link_faces) != 2 or e.calc_face_angle(0) < math.radians(sharp_deg)
    for f in bm.faces: f.smooth = True
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    out = bpy.data.objects.new(name, me); COL.objects.link(out)
    for o in objs:
        d = o.data; bpy.data.objects.remove(o)
        if d.users == 0: bpy.data.meshes.remove(d)
    return out
def tris(me): return sum(len(p.vertices) - 2 for p in me.polygons)

# ---------------------------------------------------------------- body loft
NP = 13            # half-profile points: 0 floor centre,1 floor edge,2 sill,3 character line,4 shoulder,5 belt,6 glass base,7 glass mid,8 roof edge,9 edge top,10 pillar inner,11 roof mid,12 roof centre
def build_loft(S, paint):
    L, W, H, GC = S['L'], S['W'], S['H'], S['GC']
    d0, d1 = S.get('loft', (0, L)); LL = d1 - d0; tip = L / 2
    top = S['top']; belt_keys = S.get('belt_keys') or [(d0, S['belt'] - .04), (S['cowl'], S['belt']), (d1, S.get('belt_rear', S['belt'] + .03))]
    a_f, a_r, rnd = S.get('taper', (.10, .07, .20))
    g_full = H - interp(belt_keys, (S['roof0'] + S['roof1']) / 2)
    roof_f, floor_f = S.get('roof_f', .78), S.get('floor_f', .78)
    def wbody(d):
        w = W / 2
        sf = clamp((S['cowl'] - .3 - d) / max(.3, S['cowl'] - .3)); w *= 1 - a_f * sf ** 2
        sr = clamp((d - (S['rear0'] - .3)) / max(.3, d1 - S['rear0'] + .3)); w *= 1 - a_r * sr ** 2
        c = .35; dd = min(d - d0, d1 - d)
        if dd < c: w *= 1 - rnd * (1 - dd / c) ** 2.2
        return w
    axle_d = [tip - za for za in S['_axles']]
    def ring(d):
        w = wbody(d); t_h = interp(top, d, .25); belt = min(interp(belt_keys, d, .3), t_h - .015); g = t_h - belt
        bump = sum(math.exp(-((d - da) / .55) ** 2) for da in axle_d) * S.get('arch_bulge', .02)   # fender volume
        yf = GC; ys = GC + .10; ylow = max(ys + .08, belt - .45); ymax = belt - .17
        sb = S.get('side_bulge', .010)          # how much the door swells beyond the shoulder (small = flat, modern sides)
        wf, ws = w * floor_f, w - .035 + bump * .5
        wlow, wmax, wsh = w - sb + bump, w + bump, w - sb + bump * .4
        wg0 = w - S.get('glass_inset', .022); t = clamp(g / g_full) ** .7; e = .3 + .7 * t
        wre = wg0 - (wg0 - w * roof_f) * t; wg1 = lerp(wg0, wre, .5) + .012
        return [(0, yf), (wf, yf), (ws, ys), (wlow, ylow), (wmax, ymax), (wsh, belt), (wg0, belt + min(.014, g * .3)),
                (wg1, belt + g * .55), (wre, t_h - .035 * min(1, g / .1)), (wre - .045 * e, t_h), (wre - .13 * e, t_h + .006),
                (wre * .40, t_h + .012), (0, t_h + .016)], g
    # stations
    ds = {d0, d0 + .03, d0 + .08, d0 + .15, d0 + .25, d1, d1 - .03, d1 - .08, d1 - .15, d1 - .25}
    x = d0 + .36
    while x < d1 - .36: ds.add(round(x, 4)); x += .11
    for k in ('cowl', 'roof0', 'roof1', 'rear0'):
        ds.update((S[k] - .05, S[k], S[k] + .05))
    for key in top:
        ds.add(key[0])
        if len(key) > 2 and key[2] > 0: ds.update((key[0] - key[2] * .6, key[0] + key[2] * .6))   # support loops around bends
    for a, b in S.get('glass', []): ds.update((a, b))
    for dk in S.get('doors', []): ds.add(dk)
    ds = sorted(d for d in ds if d0 <= d <= d1)
    st = [ds[0]]
    for d in ds[1:]:
        if d - st[-1] >= .02: st.append(d)
        elif d == d1: st[-1] = d
    # lower profile points are pulled inboard at both ends: sloped bumper faces (approach/departure) instead of flat slabs
    tf = S.get('tuck_front', (.20, .18, .08, .0)); trr = S.get('tuck_rear', (.15, .14, .07, .0))
    def dz(k, d):
        if k >= 4: return 0.0
        return -tf[k] * max(0.0, 1 - (d - d0) / .6) ** 1.5 + trr[k] * max(0.0, 1 - (d1 - d) / .5) ** 1.5
    bm = bmesh.new(); V = []; G = []
    for d in st:
        pts, g = ring(d); G.append(g); z = tip - d; row = []
        for k in range(NP): row.append(bm.verts.new(U(pts[k][0], pts[k][1], z + dz(k, d))))
        for k in range(1, NP - 1): row.append(bm.verts.new(U(-pts[k][0], pts[k][1], z + dz(k, d))))
        V.append(row)
    def R(s, k): return V[s][k]
    def Lf(s, k): return V[s][0] if k == 0 else V[s][NP - 1] if k == NP - 1 else V[s][NP - 1 + k]
    glass = S.get('glass', []); bump = S.get('bumper_h', .42); clad = S.get('clad_h', .2); blen = S.get('bumper_len', .55)
    def trim_h(d): return bump if (d - d0 < blen or d1 - d < blen) else clad
    def material(k, d, y, g):
        if k <= 1 or y < trim_h(d): return 0
        if k in (6, 7):
            return 2 if g > .08 and any(a <= d <= b for a, b in glass) else 1
        if k in (10, 11):
            if S['cowl'] <= d <= S['roof0']: return 2
            if S['roof1'] <= d <= S['rear0'] and y > S.get('rear_glass_min', 9): return 2
        return 1
    def cap_material(y, d_end, front):
        if y < bump: return 0
        gm = S.get('front_glass_min') if front else S.get('rear_cap_glass_min')
        t_h = _lin(top, d_end)
        return 2 if gm is not None and gm <= y <= t_h - .04 else 1
    faces = []
    for s in range(len(st) - 1):
        dm = (st[s] + st[s + 1]) / 2; gm = (G[s] + G[s + 1]) / 2
        for k in range(NP - 1):
            for side in (R, Lf):
                vs = (side(s, k), side(s, k + 1), side(s + 1, k + 1), side(s + 1, k))
                if len(set(vs)) < 3: continue
                f = bm.faces.new(vs); yc = sum(v.co.z for v in vs) / 4
                f.material_index = material(k, dm, yc, gm)
    for s, d_end, front in ((0, d0, True), (len(st) - 1, d1, False)):
        for k in range(NP - 1):
            vs = []
            for v in (R(s, k), Lf(s, k), Lf(s, k + 1), R(s, k + 1)):
                if v not in vs: vs.append(v)
            if len(vs) < 3: continue
            f = bm.faces.new(vs); f.material_index = cap_material(sum(v.co.z for v in vs) / len(vs), d_end, front)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = new_obj('Loft', bm, [TRIM, paint, GLASS]); me = o.data
    # creases: longitudinal edges along chosen profile rows, cap rims
    CRE = {1: .5, 2: .25, 3: .3, 5: .85, 6: .55, 9: .15}
    N = 2 * NP - 2
    def key(i):
        s, r = divmod(i, N); return s, (r if r < NP else r - NP + 1), (r >= NP)
    att = me.attributes.new('crease_edge', 'FLOAT', 'EDGE'); S_last = len(st) - 1
    for e in me.edges:
        (s0, k0, l0), (s1, k1, l1) = key(e.vertices[0]), key(e.vertices[1])
        if s0 != s1 and k0 == k1: att.data[e.index].value = CRE.get(k0, 0)
        elif s0 == s1 and s0 in (0, S_last) and k0 != k1: att.data[e.index].value = S.get('cap_crease', 0)
    sub = o.modifiers.new('subd', 'SUBSURF'); sub.levels = sub.render_levels = S.get('subd', 2)
    # wheel arches
    axles = S['_axles']; bmc = bmesh.new()
    for za in axles:
        if not (d0 <= tip - za <= d1): continue
        for side in (-1, 1):
            ar = S['R'] + S.get('arch_gap', .045)
            r = bmesh.ops.create_cone(bmc, cap_ends=True, segments=72, radius1=ar, radius2=ar, depth=.38)
            M = Matrix.Translation(U(side * (W / 2 - .12), S['R'] + S.get('arch_lift', 0), za)) @ Matrix.Rotation(math.radians(90), 4, 'Y')
            bmesh.ops.transform(bmc, matrix=M, verts=r['verts'])
    cutter = new_obj('ArchCutter', bmc, [TRIM])
    b = o.modifiers.new('arch', 'BOOLEAN'); b.operation = 'DIFFERENCE'; b.object = cutter; b.solver = 'EXACT'
    try: b.material_mode = 'TRANSFER'
    except Exception: pass
    apply_mods(o); bpy.data.objects.remove(cutter)
    return o, wbody

# ---------------------------------------------------------------- wheels
def make_wheel(S):
    R, tw = S['R'], S['TW']; rr = R * S.get('rim_f', .64); hw = tw / 2
    tread = [(-hw + .03, R)]
    for gx in (-.5, -.17, .17, .5):
        g = gx * (tw - .08); tread += [(g - .006, R), (g - .004, R - .007), (g + .004, R - .007), (g + .006, R)]
    tread.append((hw - .03, R))
    tire = [(-hw, rr - .005), (-hw, rr + .01), (-hw - .005, R - .07), (-hw + .012, R - .012)] + tread + \
           [(hw - .012, R - .012), (hw + .005, R - .07), (hw, rr + .01), (hw, rr - .005), (hw - .02, rr - .02), (-hw + .02, rr - .02)]
    parts = [revolve('Tire', tire, RUBBER, 96)]
    a, ai = hw - .012, -hw + .02
    parts.append(revolve('RimBarrel', [(ai, rr - .03), (ai, rr + .012), (ai + .03, rr - .004), (a - .03, rr - .004), (a, rr + .012), (a, rr - .03)], ALLOY, 72))
    xf = hw - .045; n = S.get('spokes', 5); sw = rr * S.get('spoke_w', .27)
    parts.append(cylinder('Hub', (xf, 0, 0), rr * .30, .03, ALLOY, 'X', 48))
    for i in range(n):
        ang = math.tau * i / n
        rm = (rr * .22 + rr - .012) / 2; ln = rr - .012 - rr * .22
        parts.append(box('Spoke%d' % i, (0, 0, 0), (.022, sw, ln), ALLOY, .005,
                         Matrix.Rotation(ang, 4, 'X') @ Matrix.Translation((xf, 0, rm))))
    for i in range(5):
        parts.append(cylinder('Lug%d' % i, (xf + .014, 0, 0), .011, .012, CHROME, 'X', 12,
                              Matrix.Rotation(math.tau * i / 5 + .3, 4, 'X') @ Matrix.Translation((0, 0, rr * .19))))
    parts.append(cylinder('Cap', (xf + .02, 0, 0), rr * .11, .014, CHROME, 'X', 32))
    parts.append(cylinder('Disc', (xf - .085, 0, 0), rr - .035, .024, STEEL, 'X', 64))
    parts.append(box('Caliper', (0, 0, 0), (.045, .085, .13), TRIM, .008, Matrix.Rotation(math.radians(35), 4, 'X') @ Matrix.Translation((xf - .085, 0, rr - .08))))
    w = merge(parts, 'Car_%s_Wheel' % S['code']); w.data.name = 'Car_%s_Wheel' % S['code']
    return w

# ---------------------------------------------------------------- details
def car_details(S, body, paint, wb, parts):
    L, W, H, GC, tip = S['L'], S['W'], S['H'], S['GC'], S['L'] / 2
    d0, d1 = S.get('loft', (0, L)); z = lambda d: tip - d
    nose = interp(S['top'], d0); tail = interp(S['top'], d1); belt = S['belt']
    plate_m = PLATE_Y if S.get('commercial') else PLATE; pw, ph = ((.44, .20) if S.get('plate2') else (.52, .11))
    bumpy = S.get('bumper_h', .42)
    w2 = W / 2
    def lamp(name, s, x0, x1, y0, y1, zpos, m, housing=True, taper=0.0, frame=.02):
        """Lens strip wrapped onto the body (shrinkwrap) so it follows the curved corner and sits flush.
        taper shrinks the lens height at the x0 end (wedge-shaped modern lamps)."""
        a, b = s * x0, s * x1; ya0, ya1 = y0 + taper / 2, y1 - taper / 2
        if housing:
            parts.append(patch(name + 'Housing', U(a, ya0 - frame, zpos), U(b, y0 - frame, zpos), U(a, ya1 + frame, zpos), U(b, y1 + frame, zpos), 40, 4, TRIM, body, .0015))
        parts.append(patch(name, U(a, ya0, zpos), U(b, y0, zpos), U(a, ya1, zpos), U(b, y1, zpos), 40, 3, m, body, .004))
    def trap(name, yb, yt, hb, ht, zpos, m, offset, nu=24, nv=4, xc=0.0):
        """Trapezoid wrapped onto the nose/tail: half-widths hb (bottom) and ht (top)."""
        parts.append(patch(name, U(xc - hb, yb, zpos), U(xc + hb, yb, zpos), U(xc - ht, yt, zpos), U(xc + ht, yt, zpos), nu, nv, m, body, offset))
    def fender_lamp(name, s, zpos_sign, d_len, y_in, y_out, m, offset, rise=.0):
        """Lamp piece lying on the fender side, tapering as it sweeps away from the end of the car."""
        sx = s * (w2 + .3); za = tip * zpos_sign; zb = za - zpos_sign * d_len
        parts.append(patch(name, U(sx, y_in[0], za), U(sx, y_out[0] + rise, zb), U(sx, y_in[1], za), U(sx, y_out[1] + rise, zb), 24, 3, m, body, offset))
    # ---- front (reference: slim lamps sweeping into the fenders, wide hexagonal slat grille, bumper intake, air-curtain slots)
    F = S.get('front', {})
    ly, lh = F.get('lamp_y', nose - .055), F.get('lamp_h', .075); lxi = F.get('lamp_inner', w2 - .55)
    for s in (-1, 1):
        # dark projector unit (glossy black lens) with a thin light DRL line along its top edge, sweeping back into the fender
        lamp('HeadLamp', s, lxi, w2 + .05, ly - lh / 2, ly + lh / 2, tip + .4, LAMPGLASS, taper=.03, frame=.012)
        lamp('DRL', s, lxi + .02, w2 + .04, ly + lh / 2 - .016, ly + lh / 2 - .004, tip + .4, LF, False)
        fender_lamp('HeadLampSideHousing', s, 1, F.get('lamp_sweep', .30), (ly - lh / 2 - .012, ly + lh / 2 + .012), (ly + .02, ly + .045), TRIM, .0015)
        fender_lamp('HeadLampSide', s, 1, F.get('lamp_sweep', .30) - .02, (ly - lh / 2, ly + lh / 2), (ly + .026, ly + .04), LAMPGLASS, .004)
        fender_lamp('DRLSide', s, 1, F.get('lamp_sweep', .30) - .03, (ly + lh / 2 - .016, ly + lh / 2 - .004), (ly + .03, ly + .038), LF, .005)
    gt, gb, ght, ghb = F.get('grille', (ly + lh / 2 - .01, ly - lh / 2 - .13, .33, .26))      # top y, bottom y, half-width top/bottom
    trap('GrilleFrame', gb - .012, gt + .012, ghb + .012, ght + .012, tip + .4, CHROME, .002)
    trap('GrilleRecess', gb, gt, ghb, ght, tip + .4, TRIM, .003)
    for k in range(5):
        yy = gb + .02 + k * (gt - gb - .04) / 4; hh = lerp(ghb, ght, (yy - gb) / (gt - gb)) - .015
        trap('GrilleSlat%d' % k, yy - .006, yy + .006, hh, hh, tip + .4, CHROME, .007, 24, 1)
    it, ib = F.get('intake', (bumpy + .06, GC + .03))
    trap('IntakeFrame', ib - .008, it + .008, .46, .53, tip + .4, DARKCHROME, .002)
    trap('Intake', ib, it, .45, .52, tip + .4, TRIM, .003)
    for k in range(3):
        yy = ib + .025 + k * (it - ib - .05) / 2; trap('IntakeSlat%d' % k, yy - .005, yy + .005, .44, .44, tip + .4, TRIM, .0065, 24, 1)
    for s in (-1, 1):
        trap('AirCurtain', ib + .02, it + .10, .035, .04, tip + .4, TRIM, .003, 8, 6, s * (w2 - .13))
    parts.append(surf_box('PlateF', body, (0, F.get('plate_y', (gb + it) / 2), tip + 2), (0, 0, -1), (pw, ph, .014), plate_m, .004, .003))
    # ---- rear (reference: full-width light bar with corner lamps, plate recess in the lid, black diffuser with flat twin tips)
    if S.get('rear_lamps', True):
        Rr = S.get('rear', {})
        rly, rlh = Rr.get('bar_y', tail - .03), Rr.get('bar_h', .045)
        lamp('TailBar', 1, -(w2 - .30), w2 - .30, rly - rlh / 2, rly + rlh / 2, -tip - .4, LR, frame=.012)
        for s in (-1, 1):
            lamp('TailLamp', s, w2 - .34, w2 + .06, rly - rlh / 2 - .03, rly + rlh / 2 + .03, -tip - .4, LR, taper=.02, frame=.016)
            fender_lamp('TailLampSideHousing', s, -1, Rr.get('lamp_sweep', .20), (rly - rlh / 2 - .042, rly + rlh / 2 + .042), (rly - .002, rly + .028), TRIM, .0015)
            fender_lamp('TailLampSide', s, -1, Rr.get('lamp_sweep', .20) - .015, (rly - rlh / 2 - .03, rly + rlh / 2 + .03), (rly + .006, rly + .02), LR, .004)
        py = Rr.get('plate_y', rly - .17)
        trap('PlateRecess', py - ph / 2 - .035, py + ph / 2 + .035, pw / 2 + .06, pw / 2 + .06, -tip - .4, TRIM, .002)
        parts.append(surf_box('PlateR', body, (0, py, -tip - 2), (0, 0, 1), (pw, ph, .012), plate_m, .003, .003))
        # flat twin exhaust tips set into the diffuser band (no protruding pipes)
        for s in (-1, 1):
            trap('ExhaustBezel', bumpy - .12, bumpy - .05, .12, .13, -tip - .4, CHROME, .003, 8, 2, s * (w2 - .33))
            trap('ExhaustTip', bumpy - .105, bumpy - .065, .095, .105, -tip - .4, TRIM, .0045, 8, 2, s * (w2 - .33))
    # ---- side brightwork from the reference: window surround line along the belt, sill garnish
    if S.get('dlo_chrome', True) and S.get('doors'):
        da, db = S['doors'][0] - .02, S.get('glass', [(0, S['doors'][-1])])[-1][1] + .02
        for s in (-1, 1):
            sx = s * (w2 + .3)
            parts.append(patch('DLOChrome', U(sx, belt + .003, z(da)), U(sx, belt + .003, z(db)), U(sx, belt + .015, z(da)), U(sx, belt + .015, z(db)), 40, 1, CHROME, body, .003))
            sa, sb_ = tip - S['_axles'][0] + S['R'] + .14, tip - S['_axles'][1] - S['R'] - .14     # between the wheel arches only
            parts.append(patch('SillChrome', U(sx, GC + .10, z(sa)), U(sx, GC + .10, z(sb_)), U(sx, GC + .13, z(sa)), U(sx, GC + .13, z(sb_)), 40, 1, CHROME, body, .003))
    # mirrors: mounted on the front-door shoulder (ray-cast to the actual surface)
    for s in ((-1, 1) if S.get('mirrors', True) else ()):
        mzp = z(S['cowl'] + S.get('mirror_d', .30)); my = belt + .04
        ok, loc, nrm, _ = body.ray_cast(U(s * 3, my, mzp), U(-s, 0, 0), distance=10)
        if not ok: continue
        hx = abs(loc.x)
        parts.append(boxU('MirrorStalk', (s * (hx + .035), my + .01, mzp), (.09, .022, .06), TRIM, .006))
        parts.append(boxU('MirrorHouse', (s * (hx + .17), my + .065, mzp - .01), (.21, .115, .11), paint, .04))
        parts.append(boxU('MirrorGlass', (s * (hx + .17), my + .065, mzp - .067), (.18, .09, .008), GLASS, .002))
    # doors: seams + handles
    doors = S.get('doors', [])
    for i, dd in enumerate(doors):
        for s in (-1, 1):
            sx = s * (W / 2 + .2)
            parts.append(patch('Seam', U(sx, GC + .12, z(dd) - .004), U(sx, GC + .12, z(dd) + .004), U(sx, belt - .005, z(dd) - .004), U(sx, belt - .005, z(dd) + .004), 1, 28, TRIM, body))
            if i < len(doors) - 1 and S.get('handles', True):
                parts.append(surf_box('Handle', body, (s * 3, belt - .20, z(doors[i + 1] - .22)), (-s, 0, 0), (.19, .034, .03), paint, .012, .012))
    if 'hood_seam' in S.get('features', ['hood_seam']) or S['kind'] in ('sedan', 'hatch', 'suv', 'van'):
        hw = wb(S['cowl'] - .08) - .10
        parts.append(patch('HoodSeam', U(-hw, H + .5, z(S['cowl'] - .08) - .004), U(hw, H + .5, z(S['cowl'] - .08) - .004), U(-hw, H + .5, z(S['cowl'] - .08) + .004), U(hw, H + .5, z(S['cowl'] - .08) + .004), 36, 1, TRIM, body))
    f = S.get('features', [])
    if 'antenna' in f: parts.append(boxU('Antenna', (0, H + .012, z(S['roof1'] - .18)), (.07, .035, .17), paint, .012))
    if 'rails' in f:
        for s in (-1, 1):
            parts.append(boxU('RoofRail', (s * (W / 2 * S.get('roof_f', .8) * .86), H + .022, z((S['roof0'] + S['roof1']) / 2)), (.045, .04, S['roof1'] - S['roof0'] - .35), TRIM, .012))
    # wipers: laid flat on the windscreen surface (ray from above)
    if S.get('wipers', True):
        for sx in (-.36, .40):
            parts.append(surf_box('Wiper', body, (sx, 3, z(S['cowl'] + .09)), (0, -1, 0), (.60, .022, .014), TRIM, .006, .003))
            parts.append(surf_box('WiperArm', body, (sx + .12, 3, z(S['cowl'] + .12)), (0, -1, 0), (.30, .012, .014), TRIM, .005, .003))

def car_interior(S, parts, wb):
    L, W, H, GC, tip = S['L'], S['W'], S['H'], S['GC'], S['L'] / 2
    z = lambda d: tip - d; belt = S['belt']; wi = W / 2 - .10
    zf, zr = z(S['cowl'] + .15), z(S.get('cabin_end', S['rear0']) - .10)
    floor = S.get('floor_y', GC + .03)
    parts.append(boxU('Tub', (0, (floor + belt - .02) / 2, (zf + zr) / 2), (2 * wi, belt - .02 - floor, zf - zr), INTERIOR, .02))
    dd = S.get('dash_depth', .55)
    parts.append(boxU('Dash', (0, belt - .02, z(S['cowl'] + .2 + dd / 2)), (2 * wi, .16, dd), INTERIOR, .03))
    parts.append(boxU('Headliner', (0, H - .045, z((S['roof0'] + S['roof1']) / 2 + .02)), (2 * wi * S.get('roof_f', .78) * .92, .012, S['roof1'] - S['roof0'] - .15), HEADLINER, .003))
    def seat(x, d, w=.50, bench=False):
        ct = belt - (.36 if bench else .30); bh = .50 if bench else .60
        parts.append(boxU('Cushion', (x, ct - .07, z(d)), (w, .14, .50), SEAT, .03))
        rot = Matrix.Rotation(math.radians(-20 if bench else -16), 4, 'X')
        parts.append(box('Back', U(x, ct + bh / 2 - .02, z(d + .26)), (w, .12, bh), SEAT, .03, rot))
        if not bench:
            parts.append(box('Head', U(x, ct + .62, z(d + .35)), (.26, .09, .13), SEAT, .03, rot))
    seats = S.get('seats') or [(-.37, S['cowl'] + .95), (.37, S['cowl'] + .95)]
    for x, d in seats: seat(x, d)
    if S.get('bench', True): seat(0, S['cowl'] + 1.80, 2 * wi - .12, bench=True)
    # steering wheel (LHD)
    sx, sd = seats[0][0], S['cowl'] + .2 + dd + .08
    axis = U(0, .45, -.9).normalized(); sp = U(sx, belt + .03, z(sd))
    M = Matrix.Translation(sp) @ axis.to_track_quat('X', 'Z').to_matrix().to_4x4()
    parts.append(revolve('Steering', [(0, S.get('wheel_r', .19) + .018), (.018, S.get('wheel_r', .19)), (0, S.get('wheel_r', .19) - .018), (-.018, S.get('wheel_r', .19))], INTERIOR, 32, M))
    parts.append(cylinder('SteerHub', sp, .05, .04, INTERIOR, 'Z', 16, axis.to_track_quat('Z', 'Y').to_matrix().to_4x4()))

def truck_extras(S, body, paint, parts):
    L, W, H, GC, tip = S['L'], S['W'], S['H'], S['GC'], S['L'] / 2
    z = lambda d: tip - d; cab = S['loft'][1]
    fy = .80; bw = W - .04; b0, b1 = cab + .10, L - .05; bl = b1 - b0; bz = z((b0 + b1) / 2)
    parts.append(boxU('BedFloor', (0, fy, bz), (bw, .04, bl), STEEL, .006))
    parts.append(boxU('HeadBoard', (0, fy + .40, z(b0 + .015)), (bw, .80, .03), paint, .006))
    parts.append(boxU('HeadBoardBar', (0, fy + .82, z(b0 + .015)), (bw, .05, .05), STEEL, .006))
    for s in (-1, 1):
        parts.append(boxU('SideGate', (s * (bw / 2 - .015), fy + .20, bz), (.03, .36, bl - .06), paint, .006))
        for k in range(4):
            parts.append(boxU('GateRib', (s * (bw / 2 + .006), fy + .20, z(b0 + .25 + k * (bl - .5) / 3)), (.012, .34, .05), paint, .003))
        parts.append(boxU('Rail', (s * .45, fy - .12, z((S['loft'][1] * .4 + b1) / 2)), (.08, .16, b1 - S['loft'][1] * .4 - .4), STEEL, .005))
        parts.append(boxU('MudGuard', (s * (W / 2 - .17), S['R'] + .18, z(tip - S['_axles'][1])), (.26, .05, .95), TRIM, .006))
    parts.append(boxU('TailGate', (0, fy + .20, z(b1 - .015)), (bw, .36, .03), paint, .006))
    parts.append(boxU('RearBumper', (0, .48, z(b1 - .04)), (W - .2, .09, .07), TRIM, .012))
    for s in (-1, 1):
        parts.append(boxU('TailLampT', (s * (W / 2 - .22), .62, z(b1 - .02)), (.26, .11, .05), LR, .01))
    parts.append(boxU('PlateT', (0, .62, z(b1 + .01)), (.44, .20, .012), PLATE, .003))
    parts.append(cylinder('FuelTank', U(W / 2 - .25, .50, z(cab + 1.2)), .17, .55, STEEL, 'Y', 32))
    for s in (-1, 1):
        parts.append(boxU('Step', (s * (W / 2 - .12), GC - .06, z(1.1)), (.22, .04, .55), TRIM, .006))

def bus_extras(S, body, paint, parts):
    L, W, H, GC, tip = S['L'], S['W'], S['H'], S['GC'], S['L'] / 2
    z = lambda d: tip - d
    parts.append(boxU('RoofAC', (0, H + .10, z(L - 1.8)), (1.35, .22, 1.5), mat('Car_RoofUnit', '#C9CCCE', .1, .5), .03))
    parts.append(surf_box('DestBox', body, (0, 2.32, tip + 3), (0, 0, -1), (1.40, .24, .08), TRIM, .05, .01))
    parts.append(surf_box('DestPanel', body, (0, 2.32, tip + 3), (0, 0, -1), (1.30, .17, .09), SIGN_W, .05, .004))
    parts.append(surf_box('EngineGrille', body, (0, .95, -tip - 3), (0, 0, 1), (.9, .55, .06), TRIM, .045, .01))
    for s in (-1, 1):
        parts.append(boxU('BusMirror', (s * (W / 2 + .28), 2.05, z(.18)), (.20, .40, .12), TRIM, .02))
        parts.append(cylinder('MirrorArm', U(s * (W / 2 + .10), 2.22, z(.22)), .015, .42, STEEL, 'X', 12))
    for dd in (.55, .95, 1.35, 3.30, 3.80, 4.30):      # right-side doors
        sx = W / 2 + .2
        parts.append(patch('DoorSeam', U(sx, GC + .05, z(dd) - .005), U(sx, GC + .05, z(dd) + .005), U(sx, 2.30, z(dd) - .005), U(sx, 2.30, z(dd) + .005), 1, 30, TRIM, body))
    for a, b in ((.55, 1.35), (3.30, 4.30)):
        sx = W / 2 + .2
        parts.append(patch('DoorTop', U(sx, 2.30, z(a)), U(sx, 2.30, z(b)), U(sx, 2.31, z(a)), U(sx, 2.31, z(b)), 20, 1, TRIM, body))
    # interior
    floor = GC + .40; wi = W / 2 - .10
    parts.append(boxU('Tub', (0, (floor + S['belt'] - .02) / 2, z((.35 + L - .3) / 2)), (2 * wi, S['belt'] - .02 - floor, L - .65), INTERIOR, .02))
    parts.append(boxU('Dash', (0, S['belt'] - .35, z(.6)), (2 * wi, .25, .5), INTERIOR, .03))
    parts.append(boxU('Headliner', (0, H - .06, z(L / 2)), (2 * wi * .9, .012, L - 1.4), HEADLINER, .003))
    def seat(x, d, w=.45):
        ct = floor + .44
        parts.append(boxU('Cushion', (x, ct - .06, z(d)), (w, .12, .45), SEAT, .025))
        rot = Matrix.Rotation(math.radians(-12), 4, 'X')
        parts.append(box('Back', U(x, ct + .36, z(d + .22)), (w, .10, .72), SEAT, .025, rot))
    dd = 2.0
    while dd < L - 1.1:
        for x in (-.72, -.26, .70): seat(x, dd)
        dd += .74
    seat(-.65, 1.05, .5)
    axis = U(0, .8, -.6).normalized()
    M = Matrix.Translation(U(-.65, S['belt'] - .2, z(.55))) @ axis.to_track_quat('X', 'Z').to_matrix().to_4x4()
    parts.append(revolve('Steering', [(0, .25), (.02, .23), (0, .21), (-.02, .23)], INTERIOR, 32, M))

def taxi_extras(S, body, paint, parts):
    tip = S['L'] / 2; z = lambda d: tip - d; H = S['H']; d = (S['roof0'] + S['roof1']) / 2
    parts.append(boxU('SignBase', (0, H + .02, z(d)), (.32, .04, .12), TRIM, .008))
    parts.append(boxU('TaxiSign', (0, H + .11, z(d)), (.42, .15, .14), SIGN_W, .02))
    for s, rot in ((1, Matrix.Rotation(math.radians(90), 4, 'X')), (-1, Matrix.Rotation(math.radians(90), 4, 'X') @ Matrix.Rotation(math.pi, 4, 'Y'))):
        M = Matrix.Translation(U(0, H + .11, z(d) + s * .071)) @ rot
        parts.append(text_mesh('TaxiText', 'TAXI', .095, SIGN_B, M, .002))

def police_extras(S, body, paint, parts):
    L, W, H, tip = S['L'], S['W'], S['H'], S['L'] / 2; z = lambda d: tip - d; belt = S['belt']
    d = S['roof0'] + .35
    parts.append(boxU('BarBase', (0, H + .025, z(d)), (1.0, .05, .22), TRIM, .01))
    parts.append(boxU('BarBlue', (-.27, H + .10, z(d)), (.52, .10, .28), SIGN_B, .02))
    parts.append(boxU('BarRed', (.27, H + .10, z(d)), (.52, .10, .28), SIGN_R, .02))
    a, b = S['doors'][0] - .05, S['doors'][-1] + .35
    for s in (-1, 1):
        sx = s * (W / 2 + .2)
        parts.append(patch('Stripe', U(sx, belt - .46, z(a)), U(sx, belt - .46, z(b)), U(sx, belt - .28, z(a)), U(sx, belt - .28, z(b)), 48, 4, DECAL_B, body, .0025))
        rot = Matrix.Rotation(math.radians(90 if s > 0 else -90), 4, 'Z') @ Matrix.Rotation(math.radians(90), 4, 'X')
        M = Matrix.Translation(U(sx, belt - .37, z((S['doors'][0] + S['doors'][1]) / 2 + .05))) @ rot
        parts.append(text_mesh('PoliceText', 'POLICE', .11, DECAL_W, M, .0015, body, .0035))

# ---------------------------------------------------------------- specs
def sedan(code, name, nick, L, W, H, WB, FO, R, TW, belt, hood, nose, cowl, roof0, roof1, rear0, trunk, doors, paint, variants, **kw):
    """Modern fastback sedan: long sloping bonnet, fast screens, short high deck. Keys are (d from nose, height, bend radius)."""
    S = dict(code=code, name=name, nick=nick, kind='sedan', L=L, W=W, H=H, WB=WB, FO=FO, R=R, TW=TW, GC=.14, belt=belt,
             top=[(0, nose, 0), (.08, nose + .07, .06), (.30, nose + .12, .3), (cowl, hood, .22), (roof0, H - .02, .35), (roof0 + .35, H, .4),
                  (roof1, H - .01, .45), (rear0, trunk, .3), (L - .12, trunk - .01, .1), (L - .03, trunk - .04, .03), (L, trunk - .12, 0)],
             belt_keys=[(0, belt - .07, 0), (cowl, belt, .4), (L, belt + .07, 0)],
             cowl=cowl, roof0=roof0, roof1=roof1, rear0=rear0, rear_glass_min=trunk + .03, doors=doors,
             glass=[(doors[0] + .16, doors[1] - .04), (doors[1] + .04, doors[2] + .10)], roof_f=.76, bumper_h=.30, clad_h=.18,
             paint=paint, variants=variants, features=['antenna'])
    S.update(kw); return S
def suv(code, name, nick, L, W, H, WB, FO, R, TW, belt, hood, nose, cowl, roof0, roof1, rear0, tailtop, doors, paint, variants, **kw):
    S = dict(code=code, name=name, nick=nick, kind='suv', L=L, W=W, H=H, WB=WB, FO=FO, R=R, TW=TW, GC=.19, belt=belt,
             top=[(0, nose), (.12, nose + .09), (.5, hood - .05), (cowl, hood), (roof0, H), (roof1, H), (rear0, tailtop), (L, tailtop - .12)],
             cowl=cowl, roof0=roof0, roof1=roof1, rear0=rear0, rear_glass_min=belt + .12, doors=doors,
             glass=[(doors[0] + .18, doors[1] - .04), (doors[1] + .04, rear0 - .25)], roof_f=.82, bumper_h=.45, clad_h=.42,
             paint=paint, variants=variants, features=['rails', 'antenna'], rear_lamp_y=tailtop - .22, plate_rear_y=tailtop - .45)
    S.update(kw); return S

SPECS = [
    # C04 소나티네 — 쏘나타(DN8) 치수. 키는 (앞끝에서의 거리 d, 높이, 굽힘 반지름). 레퍼런스: Reference/Car_References_18.png 04번.
    dict(code='C04', name='소나티네', base='쏘나타급 중형 세단', kind='sedan', L=4.900, W=1.860, H=1.445, WB=2.840, FO=.945, R=.334, TW=.235, GC=.15, belt=.93,
         top=[(0, .66, 0), (.06, .74, .05), (.22, .79, .15), (.60, .85, .30), (1.55, .93, .22), (2.45, 1.40, .30), (2.80, 1.445, .40), (3.40, 1.43, .40),
              (4.22, 1.06, .30), (4.70, 1.025, .15), (4.84, 1.005, .03), (4.90, .88, 0)],
         belt_keys=[(0, .86, 0), (1.55, .93, .4), (4.90, 1.00, 0)], cowl=1.55, roof0=2.45, roof1=3.40, rear0=4.22, rear_glass_min=1.09,
         doors=[1.70, 2.80, 3.85], glass=[(1.86, 2.76), (2.84, 3.95)], roof_f=.80, floor_f=.86, taper=(.08, .10, .22), bumper_h=.23, clad_h=.0,
         front=dict(grille=(.645, .34, .33, .22), intake=(.31, .17), plate_y=.245), rear=dict(plate_y=.68),
         arch_bulge=.015, side_bulge=.008, glass_inset=.022, rim_f=.66, spokes=10, spoke_w=.13, features=['antenna'],
         paint='#EEF0EE', variants=[('White', '화이트', '#EEF0EE'), ('Silver', '실버', '#B9BEC4'), ('Navy', '네이비', '#2E4A6B'), ('Black', '블랙', '#1C1F23')]),
]

# ---------------------------------------------------------------- studio
STUDIO = bpy.data.collections.new('_Studio'); scene.collection.children.link(STUDIO)
def studio_obj(o):
    for c in o.users_collection: c.objects.unlink(o)
    STUDIO.objects.link(o)
COL = STUDIO
# Studio follows the team's prop/building presentation: three DISK softboxes (key/fill/rim), neutral grey world, AgX.
floor_m = mat('Studio_Floor', '#595B5E', 0, .42)
bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60); floor = new_obj('_Floor', bm, [floor_m], False)
world = bpy.data.worlds.new('Studio'); world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.50, .52, .55, 1); world.node_tree.nodes['Background'].inputs['Strength'].default_value = .80
scene.world = world
def area(name, loc, target, power, size, shape='DISK', size_y=None, color=(1, 1, 1)):
    d = bpy.data.lights.new(name, 'AREA'); d.energy = power; d.shape = shape; d.size = size; d.color = color
    if size_y: d.size_y = size_y
    o = bpy.data.objects.new(name, d); STUDIO.objects.link(o); o.location = loc
    o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler(); return o
# Large, dim softboxes: same illuminance as small bright ones but far lower radiance, so paint highlights stay soft.
area('_Key', (-7, -8, 5.5), (0, 0, .7), 900, 11, color=(1, .96, .91))
area('_Fill', (7, -5, 3.5), (0, 0, .7), 420, 10, color=(.90, .94, 1))
area('_Rim', (3, 7, 5), (0, 0, .7), 750, 9)
if not opt('--no-top', False): area('_Top', (0, 1, 7), (0, 0, 0), 160, 10, 'RECTANGLE', 6)
cam_d = bpy.data.cameras.new('Cam'); cam_d.lens = 65; cam_d.clip_end = 500
cam = bpy.data.objects.new('_Camera', cam_d); STUDIO.objects.link(cam); scene.camera = cam

scene.render.engine = 'CYCLES'; scene.cycles.samples = SAMPLES; scene.cycles.use_denoising = not opt('--no-denoise', False)
scene.cycles.max_bounces = 8; scene.cycles.transmission_bounces = 8; scene.cycles.glossy_bounces = 4
scene.cycles.caustics_reflective = False; scene.cycles.caustics_refractive = False   # glass without speckle
scene.cycles.blur_glossy = 1.0; scene.cycles.sample_clamp_indirect = 6.0
scene.render.use_persistent_data = True; scene.cycles.device = 'CPU'
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type = 'OPTIX'; prefs.get_devices()
    gpus = [d for d in prefs.devices if d.type == 'OPTIX']
    if gpus:
        for d in prefs.devices: d.use = d.type == 'OPTIX'
        scene.cycles.device = 'GPU'
except Exception as e: print('GPU setup failed', e)
try:
    scene.cycles.denoiser = 'OPENIMAGEDENOISE'; scene.cycles.denoising_prefilter = 'ACCURATE'; scene.cycles.denoising_use_gpu = True
except Exception as e: print('GPU setup failed', e)
print('render device', scene.cycles.device)
scene.render.resolution_x = 1400; scene.render.resolution_y = 1000; scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'; scene.render.image_settings.color_mode = 'RGB'; scene.render.film_transparent = False
scene.view_settings.view_transform = 'AgX'; scene.view_settings.exposure = .45

def fit_camera(objs, dir_b=Vector((-1.0, -1.05, .21)), fill=.84):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    center = (lo + hi) / 2; center.z = lo.z + (hi.z - lo.z) * .45
    d = dir_b.normalized(); dist = (hi - lo).length; cam_d.shift_x = cam_d.shift_y = 0
    asp = scene.render.resolution_y / scene.render.resolution_x
    for _ in range(40):
        cam.location = center + d * dist; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        bpy.context.view_layer.update()
        ndc = [world_to_camera_view(scene, cam, p) for p in pts]
        xs = [n.x for n in ndc]; ys = [n.y for n in ndc]
        cam_d.shift_x += ((min(xs) + max(xs)) / 2 - .5); cam_d.shift_y += ((min(ys) + max(ys)) / 2 - .5) * asp
        ext = max(max(xs) - min(xs), (max(ys) - min(ys)) * asp)
        if abs(ext - fill) < .004: break
        dist *= ext / fill

# ---------------------------------------------------------------- build loop
RESULTS = []; built = []
for idx, S in enumerate(SPECS):
    if ONLY and S['code'] not in ONLY: continue
    t = time.time(); code = S['code']; print('==', code, S['name'])
    COL = bpy.data.collections.new(code); scene.collection.children.link(COL)
    tip = S['L'] / 2; zf = tip - S['FO']; S['_axles'] = (zf, zf - S['WB'])
    paint = paint_mat(code, S['paint'])
    loft, wb = build_loft(S, paint)
    bpy.context.view_layer.update()
    parts = [loft]
    car_details(S, loft, paint, wb, parts)
    if S['kind'] == 'truck':
        truck_extras(S, loft, paint, parts); car_interior(S, parts, wb)
    elif S['kind'] == 'bus':
        bus_extras(S, loft, paint, parts)
    else:
        car_interior(S, parts, wb)
    if S.get('extra') == 'taxi': taxi_extras(S, loft, paint, parts)
    if S.get('extra') == 'police': police_extras(S, loft, paint, parts)
    parts = [p for p in parts if p is not None]
    body = merge(parts, 'Car_%s_Body' % code); body.data.name = 'Car_%s_Body' % code
    wheel = make_wheel(S)
    root = bpy.data.objects.new('Car_' + code, None); COL.objects.link(root); root.empty_display_size = .5
    body.parent = root
    wheels = []
    wx = S['W'] / 2 - S['TW'] / 2 - .035
    for nm, sx, za in (('FL', -1, S['_axles'][0]), ('FR', 1, S['_axles'][0]), ('RL', -1, S['_axles'][1]), ('RR', 1, S['_axles'][1])):
        o = wheel if nm == 'FL' else bpy.data.objects.new('Car_%s_Wheel_%s' % (code, nm), wheel.data)
        if o is not wheel: COL.objects.link(o)
        o.name = 'Car_%s_Wheel_%s' % (code, nm); o.parent = root
        o.location = U(sx * wx, S['R'], za)
        if sx < 0: o.rotation_euler = (0, 0, math.pi)
        wheels.append(o)
    bpy.context.view_layer.update()
    # measurements (Unity axes)
    pts = [o.matrix_world @ Vector(c) for o in [body] + wheels for c in o.bound_box]
    lo = [min(p[i] for p in pts) for i in range(3)]; hi = [max(p[i] for p in pts) for i in range(3)]
    length, width, height = hi[1] - lo[1], S['W'], hi[2] - lo[2]   # width = body spec (mirrors excluded)
    tb, tw_ = tris(body.data), tris(wheel.data)
    info = dict(code=code, id='Car_' + code, name=S['name'], base=S.get('base', S.get('nick', '')), kind=S['kind'],
                dims=dict(length=round(length, 3), width=round(width, 3), height=round(height, 3), wheelbase=S['WB'],
                          spec_length=S['L'], spec_height=S['H']),
                wheel=dict(radius=S['R'], width=S['TW'], positions={w.name.split('_')[-1]: [round(w.location.x, 4), round(w.location.z, 4), round(-w.location.y, 4)] for w in wheels}),
                triangles=dict(body=tb, wheel=tw_, total=tb + 4 * tw_),
                paint=S['paint'], variants=[dict(id=a, name=b, color=c) for a, b, c in S['variants']],
                materials=[m.name for m in body.data.materials], wheel_materials=[m.name for m in wheel.data.materials],
                files=dict(fbx='Car_%s.fbx' % code, render='Renders/Car_%s.png' % code))
    # export FBX (root at origin)
    for o in scene.objects: o.select_set(False)
    for o in [root, body] + wheels: o.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.export_scene.fbx(filepath=os.path.join(SRC, 'Car_%s.fbx' % code), use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='-Z', axis_up='Y', global_scale=1, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
                             add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True, mesh_smooth_type='OFF', path_mode='AUTO')
    for o in scene.objects: o.select_set(False)
    # render
    if not NO_RENDER:
        for c in scene.collection.children:
            c.hide_render = c.name.startswith('VC') and c.name != code
        fit_camera([body] + wheels)
        scene.render.filepath = os.path.join(REN, 'Car_%s.png' % code); bpy.ops.render.render(write_still=True)
        # extra views (check only; the sheet uses the 3/4 front render)
        scene.render.resolution_x, scene.render.resolution_y = 1200, 800
        for tag, d in (('rear34', (-1.0, 1.05, .24)), ('side', (-1.0, 0.0, .06)), ('front', (0.0, -1.0, .10)), ('rear', (0.0, 1.0, .10))):
            fit_camera([body] + wheels, Vector(d))
            scene.render.filepath = os.path.join(REN, 'Car_%s_%s.png' % (code, tag)); bpy.ops.render.render(write_still=True)
        scene.render.resolution_x, scene.render.resolution_y = 1400, 1000
    root.location = (idx * 4.0 - 20, 0, 0)
    RESULTS.append(info); built.append(code)
    print('   done %.1fs tris body=%d wheel=%d total=%d dims=%.2fx%.2fx%.2f' % (time.time() - t, tb, tw_, tb + 4 * tw_, length, width, height))
for c in scene.collection.children: c.hide_render = False

# manifest (merge with existing when building a subset)
mpath = os.path.join(SRC, 'manifest.json'); prev = {}
if ONLY and os.path.exists(mpath):
    try: prev = {v['code']: v for v in json.load(open(mpath, encoding='utf8'))['vehicles']}
    except Exception: prev = {}
for v in RESULTS: prev[v['code']] = v
vehicles = [prev[s['code']] for s in SPECS if s['code'] in prev]
json.dump(dict(set='CityCars', blender='5.2', units='m', axes='Unity: x right, y up, z forward; wheel axis X, pivot hub centre; body pivot ground centre',
               hierarchy='Car_<code> (empty) > Car_<code>_Body, Car_<code>_Wheel_FL/FR/RL/RR (shared mesh, left wheels rotated 180 deg about Y)',
               vehicles=vehicles), open(mpath, 'w', encoding='utf8'), ensure_ascii=False, indent=2)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SRC, 'CityCars.blend'))

# ---------------------------------------------------------------- comparison sheet
if not NO_SHEET:
    sh = bpy.data.scenes.new('Sheet')
    sh.render.engine = 'CYCLES'; sh.cycles.samples = 16; sh.cycles.use_denoising = False; sh.cycles.device = scene.cycles.device
    sh.view_settings.view_transform = 'Standard'; sh.view_settings.look = 'None'; sh.render.film_transparent = False
    w_ = bpy.data.worlds.new('SheetWorld'); w_.use_nodes = True; w_.node_tree.nodes['Background'].inputs['Color'].default_value = srgb('#1C1F24'); sh.world = w_
    SCOL = bpy.data.collections.new('SheetItems'); sh.collection.children.link(SCOL); COL = SCOL
    cols, rows = 4, 3; CW = 32.0; mx, gap, top_h, cap_h, gy = .9, .5, 2.1, 1.3, .45
    cw = (CW - 2 * mx - (cols - 1) * gap) / cols; ih = cw / 1.4; rh = ih + cap_h + gy
    CH = top_h + rows * rh + .3
    sh.render.resolution_x = int(CW * 100); sh.render.resolution_y = int(CH * 100); sh.render.resolution_percentage = 100
    sh.render.image_settings.file_format = 'PNG'; sh.render.image_settings.color_mode = 'RGB'
    cd = bpy.data.cameras.new('SheetCam'); cd.type = 'ORTHO'; cd.ortho_scale = CW
    sc = bpy.data.objects.new('SheetCam', cd); SCOL.objects.link(sc); sc.location = (CW / 2, -CH / 2, 10); sh.camera = sc
    def emis(name, color, image=None):
        m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree
        for n in list(nt.nodes): nt.nodes.remove(n)
        out = nt.nodes.new('ShaderNodeOutputMaterial'); em = nt.nodes.new('ShaderNodeEmission'); em.inputs['Strength'].default_value = 1
        nt.links.new(em.outputs[0], out.inputs['Surface'])
        if image:
            tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = image; nt.links.new(tex.outputs['Color'], em.inputs['Color'])
        else: em.inputs['Color'].default_value = color
        return m
    card_m = emis('SheetCard', srgb('#262A31')); ink = emis('SheetInk', srgb('#F3F4F6')); ink2 = emis('SheetInk2', srgb('#AEB5BF')); ink3 = emis('SheetInk3', srgb('#7F8792'))
    def plane(name, x, y, w, h, m, z=0):
        bm = bmesh.new(); bm.loops.layers.uv.verify()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=.5, calc_uvs=True); bmesh.ops.scale(bm, vec=(w, h, 1), verts=bm.verts)
        bmesh.ops.transform(bm, matrix=Matrix.Translation((x + w / 2, -(y + h / 2), z)), verts=bm.verts)
        o = new_obj(name, bm, [m], False); return o
    def label(name, txt, x, y, size, m, font=FONT_B, align='LEFT', z=.02):
        cu = bpy.data.curves.new(name, 'FONT'); cu.body = txt; cu.size = size; cu.font = font; cu.align_x = align; cu.align_y = 'TOP'
        o = bpy.data.objects.new(name, cu); SCOL.objects.link(o); o.location = (x, -y, z); cu.materials.append(m); return o
    label('Title', '한국 도시 자동차 세트 · %d종' % len(vehicles), mx, .45, .62, ink)
    label('Sub', 'BLENDER 5.2 · METRES · 무브랜드 · BODY / WHEEL 분리 · 실제 Blender 최종 렌더', mx, 1.25, .30, ink2, FONT_R)
    for i, v in enumerate(vehicles):
        img_path = os.path.join(REN, 'Car_%s.png' % v['code'])
        if not os.path.exists(img_path): continue
        c, r = i % cols, i // cols; x = mx + c * (cw + gap); y = top_h + r * rh
        plane('Card%d' % i, x - .15, y - .15, cw + .3, ih + cap_h + .15, card_m, 0)
        img = bpy.data.images.load(img_path); img.colorspace_settings.name = 'sRGB'
        plane('Img%d' % i, x, y, cw, ih, emis('Img%d' % i, None, img), .01)
        label('L1_%d' % i, '%s  %s · %s' % (v['code'], v['name'], v.get('base', '')), x + .1, y + ih + .22, .34, ink)
        dm = v['dims']
        label('L2_%d' % i, '%.2f × %.2f × %.2f m  ·  휠베이스 %.2f m' % (dm['length'], dm['width'], dm['height'], dm['wheelbase']), x + .1, y + ih + .68, .25, ink2, FONT_R)
        label('L3_%d' % i, '{:,} triangles (Body {:,} + Wheel {:,} × 4)'.format(v['triangles']['total'], v['triangles']['body'], v['triangles']['wheel']), x + .1, y + ih + 1.0, .22, ink3, FONT_R)
    if len(vehicles) < cols * rows:
        i = len(vehicles); c, r = i % cols, i // cols; x = mx + c * (cw + gap); y = top_h + r * rh
        plane('CardNote', x - .15, y - .15, cw + .3, ih + cap_h + .15, card_m, 0)
        note = ['공통 규격', '', '· 단위 m, 실차 치수 (전장·전폭·전고·휠베이스)', '· Body 1개 + Wheel 메시 1개를 4곳에 공유',
                '  (Wheel_FL/FR/RL/RR, 축 X, 허브 피벗)', '· Body 피벗 = 지면 중앙, +Z 전방, +Y 위', '· 재질 슬롯 Car_* 공유, 색상 변형은 Paint 재질만 교체',
                '· 유리·램프·번호판·미러·실내(좌석) 포함', '· 실존 브랜드 로고·엠블럼 없음']
        for k, line in enumerate(note):
            label('Note%d' % k, line, x + .25, y + .35 + k * .42, .30 if k == 0 else .24, ink if k == 0 else ink2, FONT_B if k == 0 else FONT_R)
    sh.render.filepath = os.path.join(REN, 'CityCars_Sheet.png'); bpy.ops.render.render(write_still=True, scene='Sheet')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SRC, 'CityCars.blend'))
print('CITY_CARS_DONE %s %.0fs' % (','.join(built), time.time() - T0))
