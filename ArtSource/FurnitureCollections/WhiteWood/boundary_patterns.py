"""Exact material edges for printed artwork and the rug's checker pattern.

Only already identified picture paper is cut; the wooden frame keeps its
reviewed assignment. Splitting existing faces preserves the source shape.
"""
import math
import bmesh
from mathutils import Vector


def finish(pack, key, mesh, roles, rotation, low, size):
    rug = pack == 'Bedroom' and key == 'Rug'
    frame = key in ('FrameA', 'FrameB', 'FrameC')
    if not (rug or frame):
        return
    bm = bmesh.new()
    bm.from_mesh(mesh)
    paper_slots = {roles.index(r) for r in ('Paper', 'Terracotta', 'Olive', 'Charcoal')}
    inverse = rotation.transposed()
    if frame:
        paper_points = [rotation @ v.co for f in bm.faces
                        if f.material_index in paper_slots for v in f.verts]
        x_min = (min(p.x for p in paper_points)-low.x)/size.x-.5
        x_max = (max(p.x for p in paper_points)-low.x)/size.x-.5
        z_min = (min(p.z for p in paper_points)-low.z)/size.z
        z_max = (max(p.z for p in paper_points)-low.z)/size.z

    def cut(axis_a, axis_b, p, q):
        faces = list(bm.faces) if rug else [f for f in bm.faces if f.material_index in paper_slots]
        normal = Vector((0, 0, 0))
        normal[axis_a] = (q[1] - p[1]) / size[axis_a]
        normal[axis_b] = (p[0] - q[0]) / size[axis_b]
        point = low.copy()
        point[axis_a] += (p[0] + (.5 if axis_a < 2 else 0)) * size[axis_a]
        point[axis_b] += (p[1] + (.5 if axis_b < 2 else 0)) * size[axis_b]
        edges = {e for f in faces for e in f.edges}
        verts = {v for f in faces for v in f.verts}
        bmesh.ops.bisect_plane(bm, geom=list(verts) + list(edges) + faces,
                              dist=1e-7, plane_co=inverse @ point,
                              plane_no=(inverse @ normal).normalized(),
                              clear_inner=False, clear_outer=False)

    def cut_art(p, q):
        def fitted(v):
            return (x_min+(v[0]+.5)*(x_max-x_min), z_min+v[1]*(z_max-z_min))
        cut(0, 2, fitted(p), fitted(q))

    if rug:
        for i in range(1, 6):
            x = -.5 + i / 6
            cut(0, 1, (x, -.5), (x, .5))
        for i in range(1, 4):
            y = -.5 + i / 4
            cut(0, 1, (-.5, y), (.5, y))
    elif key in ('FrameB', 'FrameC'):
        polygon = [(-.05 + .29 * math.cos(i * math.tau / 48),
                    .55 + .33 * math.sin(i * math.tau / 48)) for i in range(48)]
        for p, q in zip(polygon, polygon[1:] + polygon[:1]):
            cut_art(p, q)
    else:
        levels = [.18 + i * .04 for i in range(17)]
        for z in levels:
            cut_art((-.5, z), (.5, z))
        for a, b in zip(levels, levels[1:]):
            cut_art((.18 * math.sin(a * 9) - .06, a),
                    (.18 * math.sin(b * 9) - .06, b))

    for f in bm.faces:
        if not rug and f.material_index not in paper_slots:
            continue
        c = rotation @ f.calc_center_median()
        x = (c.x - low.x) / size.x - .5
        y = (c.y - low.y) / size.y - .5
        z = (c.z - low.z) / size.z
        if frame:
            x = (x-x_min)/(x_max-x_min)-.5
            z = (z-z_min)/(z_max-z_min)
        if rug:
            role = 'Terracotta' if (min(5, max(0, int((x + .5) * 6))) +
                                   min(3, max(0, int((y + .5) * 4)))) % 2 == 0 else 'Linen'
        elif key in ('FrameB', 'FrameC'):
            inside = all((q[0]-p[0])*(z-p[1])-(q[1]-p[1])*(x-p[0]) >= -1e-7
                         for p,q in zip(polygon, polygon[1:]+polygon[:1]))
            role = ('Terracotta' if key == 'FrameB' else 'Olive') if inside else 'Paper'
        else:
            k = min(15, max(0, int((z - .18) / .04)))
            a, b = levels[k:k+2]
            t = (z-a)/(b-a)
            edge = (.18*math.sin(a*9)-.06)*(1-t)+(.18*math.sin(b*9)-.06)*t
            role = ('Terracotta' if z > .5 else 'Charcoal') if .18 < z < .82 and x < edge else 'Paper'
        f.material_index = roles.index(role)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
