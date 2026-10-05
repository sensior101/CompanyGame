"""Rebuild reviewed LivingRoom lamp face masks on the original FBX topology.

Neutral geometry/projection data is exported by Temp/LampBoundary/inspect.py.
No mesh subdivision or geometry edits are used: graph cuts follow shade and
globe creases instead of assigning whole height bands to the luminous material.
"""
from pathlib import Path
import sys
import numpy as np

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[2] / 'CompanyGame'
sys.path.insert(0, str(PROJECT / 'Temp/BoundaryLib'))
from mesh_cut import Surface


def pixels(data, points, radius=9, view='Front'):
    projected = data[view]
    screen = np.c_[projected[:, 0] * 900, (1 - projected[:, 1]) * 900]
    result = np.zeros(len(screen), bool)
    for point in points:
        selected = np.linalg.norm(screen - point, axis=1) < radius
        if selected.any():
            front = projected[selected, 2].min()
            result |= selected & (projected[:, 2] < front + .015)
    return result


def solve(key):
    data = np.load(PROJECT / 'Temp/LampBoundary' / f'LivingRoom_{key}.npz')
    surface = Surface(data)
    c, n = data['coords'], data['normals']
    z = c[:, 2]
    if key == 'TableLamp':
        # The physical shade underside is a narrow downward-facing annulus.
        # The cylindrical stem, even where it touches the shade, stays orange.
        positive = (z > .59) & (z < .62) & (n[:, 2] < -.985)
        negative = (z < .55) | (z > .66) | (n[:, 2] > -.82)
        prior = (z > .57) & (z < .64) & (n[:, 2] < -.6)
        glow = surface.cut(positive & ~negative, negative, prior)
        roles = np.full(len(c), 'Terracotta', dtype='<U24')
    else:
        # Fit the globe from its front surface; pole/cap have different normals
        # and are explicitly seeded as brass through their visible boundaries.
        p = data['Front']
        px = np.c_[p[:, 0] * 900, (1 - p[:, 1]) * 900]
        sample = (px[:, 0] > 395) & (px[:, 0] < 505) & (px[:, 1] > 250) & (px[:, 1] < 340)
        sample &= p[:, 2] < p[sample, 2].min() + .3
        points = data['centers'][sample]
        fit = np.linalg.lstsq(np.c_[2 * points, np.ones(len(points))], np.sum(points * points, axis=1), rcond=None)[0]
        center = fit[:3]
        radius = np.sqrt(fit[3] + np.sum(center * center))
        delta = data['centers'] - center
        distance = np.linalg.norm(delta, axis=1)
        residual = abs(distance - radius)
        alignment = np.sum(n * delta / np.maximum(distance[:, None], 1e-9), axis=1)
        positive = (residual < .002) & (alignment > .96) & (z < .86)
        negative = (residual > .025) | (alignment < .6) | (z > .91) | (z < .58)
        negative |= pixels(data, [(438, 207), (462, 207), (474, 176), (442, 136)], radius=9)
        negative |= pixels(data, [(410, 249), (437, 250)], radius=8, view='Angle')
        prior = (residual < .014) & (alignment > .85)
        glow = surface.cut(positive & ~negative, negative, prior)
        roles = np.full(len(c), 'Brass', dtype='<U24')
    roles[glow] = 'Glow'
    output = HERE / 'NonOfficeMasks' / f'LivingRoom_{key}.npz'
    np.savez_compressed(output, roles=roles, coords=c, face_count=np.array(len(c)))
    print(key, len(c), dict(zip(*np.unique(roles, return_counts=True))))


if __name__ == '__main__':
    for item in sys.argv[1:] or ['TableLamp', 'FloorLamp']:
        solve(item)

