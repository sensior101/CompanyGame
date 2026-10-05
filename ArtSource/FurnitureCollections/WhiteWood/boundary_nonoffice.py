"""Boundary-aware corrections for the non-office WhiteWood furniture.

Called before assigning Blender material slots. Coordinates/normals are face
centres/normals in the furniture's aligned frame; centres are normalized to
x/y [-.5,.5], z [0,1]. This module never exports assets or changes a scene.
"""
import heapq
import math
from collections import Counter, defaultdict
import numpy as np
import os
import sys
import subprocess
import tempfile


def _topology(mesh):
    """Actual face adjacency and disconnected shells, never AABB grouping."""
    edge_faces = defaultdict(list)
    for f in mesh.polygons:
        for edge in f.edge_keys:
            edge_faces[tuple(sorted(edge))].append(f.index)
    adjacent = [[] for _ in mesh.polygons]
    for fs in edge_faces.values():
        if len(fs) == 2:
            a, b = fs
            adjacent[a].append(b)
            adjacent[b].append(a)
    component = np.full(len(adjacent), -1, dtype=np.int32)
    groups = []
    for start in range(len(adjacent)):
        if component[start] >= 0:
            continue
        ci = len(groups)
        todo = [start]
        component[start] = ci
        group = []
        while todo:
            i = todo.pop()
            group.append(i)
            for j in adjacent[i]:
                if component[j] < 0:
                    component[j] = ci
                    todo.append(j)
        groups.append(group)
    return adjacent, component, groups


def _mincut(centres, normals, adjacent, foreground, background, scale=None):
    """Hard-seeded minimum surface boundary, solved in the bundled runtime."""
    foreground = np.asarray(foreground, dtype=bool)
    background = np.asarray(background, dtype=bool) & ~foreground
    if not np.any(foreground) or not np.any(background):
        return foreground
    a = np.asarray([i for i, js in enumerate(adjacent) for j in js if i < j])
    b = np.asarray([j for i, js in enumerate(adjacent) for j in js if i < j])
    delta = centres[a] - centres[b]
    if scale is not None:
        delta *= np.asarray(scale)
    length = np.linalg.norm(delta, axis=1)
    dot = np.clip(np.sum(normals[a] * normals[b], axis=1), -1., 1.)
    angle = np.arccos(dot)
    capacity = np.maximum(length, 1e-7) * (.035 + np.exp(-angle * angle / .10))
    project = os.path.abspath(os.path.join(os.path.dirname(__file__), '../../../CompanyGame'))
    runtime = os.path.expanduser('~/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe')
    cache = os.path.join(project, 'Temp', 'BoundaryAudit')
    os.makedirs(cache, exist_ok=True)
    fd, path = tempfile.mkstemp(suffix='.npz', prefix='nonoffice-cut-', dir=cache)
    os.close(fd)
    try:
        np.savez(path, a=a, b=b, capacity=capacity, foreground=foreground, background=background)
        subprocess.run([runtime, os.path.abspath(__file__), '--cut-worker', path,
                        os.path.join(project, 'Temp', 'BoundaryLib')], check=True,
                       creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        return np.load(path + '.npy')
    finally:
        for f in (path, path + '.npy'):
            if os.path.exists(f):
                os.remove(f)


def _cut_worker(path, library):
    sys.path.insert(0, library)
    import maxflow
    d = np.load(path)
    fg, bg = d['foreground'], d['background']
    graph = maxflow.Graph[float](len(fg), len(d['a']))
    graph.add_nodes(len(fg))
    for a, b, w in zip(d['a'], d['b'], d['capacity']):
        graph.add_edge(int(a), int(b), float(w), float(w))
    hard = max(10., float(np.sum(d['capacity'])) * 10.)
    for i in np.flatnonzero(fg):
        graph.add_tedge(int(i), hard, 0.)
    for i in np.flatnonzero(bg):
        graph.add_tedge(int(i), 0., hard)
    graph.maxflow()
    np.save(path + '.npy', np.asarray([graph.get_segment(i) == 0 for i in range(len(fg))]))


if __name__ == '__main__' and '--cut-worker' in sys.argv:
    at = sys.argv.index('--cut-worker')
    _cut_worker(sys.argv[at + 1], sys.argv[at + 2])
    sys.exit(0)


def _propagate(centres, normals, adjacent, seeds, fallback):
    """Surface-distance watershed with a strong penalty at actual creases.

    Seeds are semantically unambiguous faces, rather than every face on one
    side of a global plane. Unlabelled transition faces choose a region by
    following the surface; crossing a fold is more expensive than following
    a smooth surface. Tiny disconnected shells use their own existing role.
    """
    count = len(adjacent)
    distance = np.full(count, np.inf)
    result = list(fallback)
    queue = []
    for i, role in seeds.items():
        distance[i] = 0.
        result[i] = role
        queue.append((0., i))
    heapq.heapify(queue)
    while queue:
        d, i = heapq.heappop(queue)
        if d != distance[i]:
            continue
        for j in adjacent[i]:
            delta = centres[j] - centres[i]
            length = math.sqrt(float(np.dot(delta, delta)))
            dot = max(-1., min(1., float(np.dot(normals[i], normals[j]))))
            # Angle-sensitive; low-poly seams must remain valid boundaries.
            cost = max(length, 1e-6) * (1. + 85. * (1. - dot) ** .8)
            alt = d + cost
            if alt < distance[j]:
                distance[j] = alt
                result[j] = result[i]
                heapq.heappush(queue, (alt, j))
    return result


def _eroded_seeds(roles, adjacent, iterations=4):
    """Remove the uncertain strip surrounding the old coordinate masks."""
    uncertain = {i for i, ns in enumerate(adjacent)
                 if any(roles[j] != roles[i] for j in ns)}
    for _ in range(iterations):
        uncertain |= {j for i in tuple(uncertain) for j in adjacent[i]}
    return {i: role for i, role in enumerate(roles) if i not in uncertain}


def _planar_cleanup(centres, normals, areas, adjacent, roles):
    """A physical planar panel cannot be half painted by a coordinate cut."""
    result = list(roles)
    assigned = np.zeros(len(roles), dtype=bool)
    total_area = float(np.sum(areas))
    for start in np.argsort(-areas):
        if assigned[start]:
            continue
        ref = normals[start]
        origin = centres[start]
        todo = [int(start)]
        assigned[start] = True
        patch = []
        while todo:
            i = todo.pop()
            patch.append(i)
            for j in adjacent[i]:
                if assigned[j]:
                    continue
                if float(np.dot(normals[j], ref)) < .96:
                    continue
                if abs(float(np.dot(centres[j] - origin, ref))) > .008:
                    continue
                assigned[j] = True
                todo.append(j)
        if len(patch) < 3 or sum(areas[i] for i in patch) < total_area * .0005:
            continue
        votes = defaultdict(float)
        for i in patch:
            votes[roles[i]] += areas[i]
        role = max(votes, key=votes.get)
        for i in patch:
            result[i] = role
    return result


def _largest_planar_front(centres, normals, areas):
    """Find the recessed picture plane from its measured area/depth peak."""
    front = np.flatnonzero(normals[:, 1] < -.985)
    if not len(front):
        return set()
    # Model reconstruction is slightly noisy; use a 1%-depth band, selected
    # by accumulated real polygon area, rather than a guessed frame inset.
    buckets = defaultdict(float)
    for i in front:
        buckets[round(float(centres[i, 1]) / .01)] += float(areas[i])
    peak = max(buckets, key=buckets.get) * .01
    candidates = set(int(i) for i in front if abs(centres[i, 1] - peak) < .018)
    return candidates


def classify(pack, key, mesh, coords, normals, initial_roles):
    """Return corrected roles, preserving all unrelated model data.

    Concrete corrections cover picture-frame insets, the microwave plant
    shell, and the vanity chair shell. Other models return their original
    role list so a caller's more specific semantic paint remains intact.
    """
    roles = list(initial_roles)
    cache = os.path.join(os.path.dirname(__file__),'NonOfficeMasks',pack+'_'+key+'.npz')
    if os.path.exists(cache) and os.environ.get('WHITEWOOD_REBUILD_NONOFFICE') != '1':
        data=np.load(cache)
        if int(data['face_count'])==len(roles) and np.allclose(data['coords'],coords,atol=1e-5):
            return data['roles'].tolist()
        raise ValueError('Reviewed WhiteWood face mask no longer matches source geometry: '+pack+'/'+key)
    if pack == 'HomeOffice' or not (
            'Frame' in key or (pack == 'Kitchen' and key == 'MicrowaveCabinet')
            or (pack == 'Bedroom' and key == 'Vanity')):
        return roles
    centres = np.asarray(coords, dtype=float)
    normal = np.asarray(normals, dtype=float)
    areas = np.array([f.area for f in mesh.polygons])
    adjacent, component, groups = _topology(mesh)

    if 'Frame' in key:
        # Keep the image material within the actual recessed paper plane.
        # A frame's curved border is always wood, irrespective of its normal.
        panel = _largest_planar_front(centres, normal, areas)
        roles = ['Oak'] * len(roles)
        for i in panel:
            roles[i] = 'Paper'
        # Preserve the requested abstract artwork, but only within the
        # geometrically established paper area, never across the wooden rim.
        for i in panel:
            if initial_roles[i] in ('Terracotta', 'Olive', 'Charcoal'):
                roles[i] = initial_roles[i]
        return roles

    sizes = sorted(range(len(groups)), key=lambda c: -len(groups[c]))
    if pack == 'Kitchen' and key == 'MicrowaveCabinet':
        leaf_shells = [ci for ci in sizes[1:]
                       if min(centres[i, 2] for i in groups[ci]) > .73
                       and max(centres[i, 0] for i in groups[ci]) < -.10]
        leaf_indices = set(i for ci in leaf_shells for i in groups[ci])
        x,y,z=centres.T
        leaf_mask=np.asarray([i in leaf_indices for i in range(len(roles))])
        roles=['OakLight']*len(roles)
        appliance=_mincut(centres,normal,adjacent,
                          (z>.66)&(x>-.22),
                          (z<.54)|(x<-.31)|leaf_mask)
        for i in np.flatnonzero(appliance):roles[i]='Ivory'
        pot=_mincut(centres,normal,adjacent,
                    (x<-.28)&(z>.68)&(z<.76),
                    (z<.643)|(x>-.20)|leaf_mask|((normal[:,2]>.94)&(z<.65)))
        for i in np.flatnonzero(pot):roles[i]='Ivory'
        vessels=_mincut(centres,normal,adjacent,
                        (z>.32)&(z<.46)&(np.abs(x)<.34),
                        (z<.265)|(z>.53)|(np.abs(x)>.41))
        for i in np.flatnonzero(vessels):roles[i]='Ivory'
        # Door glass is a measured planar inset of the appliance front.
        candidates=np.flatnonzero(appliance & (normal[:,1]<-.98)&(x<.20)&(x>-.24)&(z>.68)&(z<.87))
        if len(candidates):
            seed=int(candidates[np.argmax(areas[candidates])]);plane=normal[seed];origin=centres[seed]
            front=appliance&(normal@plane>.985)&(np.abs((centres-origin)@plane)<.01)&(x<.25)&(z>.65)&(z<.90)
            panel=_mincut(centres,normal,adjacent,front,
                          ~appliance|(x>.27)|(x<-.27)|(z<.635)|(z>.91)|(normal@plane<.82))
            for i in np.flatnonzero(panel):roles[i]='Screen'
        for i in leaf_indices:roles[i]='Leaf'
        return _planar_cleanup(centres,normal,areas,adjacent,roles)

    if pack == 'Bedroom' and key == 'Vanity':
        chair_shells = [ci for ci in sizes[1:]
                        if min(centres[i, 1] for i in groups[ci]) < -.30
                        and max(centres[i, 2] for i in groups[ci]) < .75]
        chair_indices = set(i for ci in chair_shells for i in groups[ci])
        x,y,z = centres.T
        chair_mask = np.asarray([i in chair_indices for i in range(len(roles))])
        roles = ['Linen' if chair_mask[i] else 'Ivory' for i in range(len(roles))]
        # Legs are separated at their narrow joint to the upholstered seat or
        # cabinet, not a horizontal painted stripe crossing each leg.
        feet = _mincut(centres, normal, adjacent, z < .095, z > .255)
        for i in np.flatnonzero(feet): roles[i] = 'Oak'
        mirror = _mincut(centres, normal, adjacent,
                         (x > -.285)&(x < .19)&(z > .615)&(y > .20),
                         (x < -.33)|(x > .235)|(z < .555)|chair_mask)
        for i in np.flatnonzero(mirror): roles[i] = 'Brass'
        candidates=np.flatnonzero(mirror & (normal[:,1] < -.98))
        if len(candidates):
            front=int(candidates[np.argmax(areas[candidates])])
            plane=normal[front];origin=centres[front]
            panel=mirror & (normal @ plane > .986) & (np.abs((centres-origin) @ plane)<.004)
            for i in np.flatnonzero(panel):roles[i]='Mirror'
        # The cosmetics/flower vessel are wood; foliage follows its stem neck.
        vessel=_mincut(centres, normal, adjacent,
                       (x>.28)&(z>.66)&(z<.76),
                       (x<.19)|(z<.60)|(z>.83)|chair_mask)
        for i in np.flatnonzero(vessel):roles[i]='Oak'
        leaves=_mincut(centres, normal, adjacent,
                       (x>.23)&(z>.77),
                       (x<.18)|(z<.735)|chair_mask|mirror)
        for i in np.flatnonzero(leaves):roles[i]='Leaf'
        return roles
    return roles
