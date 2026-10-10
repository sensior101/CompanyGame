"""Reviewed surface-stroke corrections for the two HomeOffice accessory meshes.

This retains original face indices and only writes the two cached material masks.
Screen strokes are triangle ray hits; graph cuts follow mesh adjacency/dihedral
creases, rather than projecting rectangular regions through an object.
"""
from pathlib import Path
import sys
ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'CompanyGame/Temp/BoundaryLib'))
sys.path.insert(0, str(Path(__file__).parent))
import numpy as np
from mesh_cut import Surface

# Read the shared ray-picker definitions without executing its production recipes.
_source = (Path(__file__).parent / 'stroke_homeoffice.py').read_text(encoding='utf-8-sig')
_helpers = {}
exec(_source[:_source.index('recipes=')], _helpers)
pixels = _helpers['pixels']
D = ROOT / 'CompanyGame/Temp/OfficeBoundary'
M = Path(__file__).parent / 'Masks'

def strokes(data, front=(), angle=(), back=(), radius=5):
    out = np.zeros(len(data['faces']), dtype=bool)
    for view, points in [('Front', front), ('Angle', angle), ('Back', back)]:
        if points:
            out |= pixels(data, points, r=radius, view=view)
    return out

def refine(key):
    data = np.load(D / (key + '.npz'))
    surface = Surface(data)
    components = surface.components()
    path = M / ('HomeOffice_' + key + '.npz')
    base = D / (key + '_refinement_baseline.npz')
    if not base.exists():
        base.write_bytes(path.read_bytes())
    original = np.load(base)
    roles = original['roles'].astype('<U12').copy()

    def apply(role, fg, bg, protect=None):
        positive = strokes(data, **fg)
        negative = strokes(data, **bg)
        negative |= ~np.isin(components, np.unique(components[positive]))
        if protect is not None:
            negative |= protect & ~positive
        region = surface.cut(positive, negative)
        changed = np.sum(region & (roles != role))
        roles[region] = role
        print(key, role, 'seeds', positive.sum(), negative.sum(), 'region', region.sum(), 'changed', changed, flush=True)

    if key == 'Shelf':
        # Both continuous rear uprights, including their ivory-coloured planar faces.
        apply('Oak',
              dict(front=[(267,358),(267,405),(269,562),(269,627),(267,742),(662,221),(662,265)],
                   angle=[(346,216),(340,344),(337,555)],
                   back=[(549,363),(547,434),(548,564),(548,695),(549,801)]),
              dict(front=[(533,202),(533,265),(320,445),(351,474),(389,658),(442,622),(322,243),(578,650)],
                   back=[(594,275),(470,680),(480,514)]),
              protect=roles == 'Leaf')
        # The complete joined book stacks stop at their actual shelf contact crease.
        apply('Paper',
              dict(front=[(260,441),(262,460),(304,445),(344,470),(328,487),(392,682),(318,674),(361,659),(421,634),(444,614)],
                   angle=[(332,449),(414,460),(428,649),(492,650)],
                   back=[(397,700),(458,715),(512,714),(582,548),(505,536)]),
              dict(front=[(299,510),(459,506),(397,702),(515,701),(270,616),(662,618),(450,459),(529,249),(576,652)],
                   angle=[(312,623),(539,670),(344,533)],
                   back=[(363,695),(538,742),(585,578),(420,549)]),
              protect=roles == 'Leaf')
        # Missed leaves/stems of the top vine and small lower succulent.
        apply('Leaf',
              dict(front=[(274,248),(273,230),(639,602),(625,610),(592,598),(240,310)],
                   angle=[(650,610),(661,611),(638,621)],
                   back=[(594,274),(607,255),(621,265),(233,616),(286,621)]),
              dict(front=[(314,251),(343,265),(322,284),(271,354),(578,650),(609,662),(589,677),(666,590),(555,700)],
                   angle=[(615,657),(645,654),(693,598),(350,209)],
                   back=[(579,267),(594,295),(554,328),(271,658),(235,653)]))
        # Preserve the small lamp as one ceramic object down to its contact crease.
        apply('Ivory',
              dict(front=[(530,199),(526,255),(533,278),(527,285)],
                   angle=[(555,228),(558,290),(556,309)],
                   back=[(340,258),(339,316),(341,331)]),
              dict(front=[(490,302),(567,299),(451,619),(308,441),(269,409)],
                   angle=[(561,331),(519,303),(602,306)],
                   back=[(318,347),(369,340)]),
              protect=roles == 'Leaf')
    elif key == 'DrawerUnit':
        # All four separately protruding feet, including the previously ivory rear pair.
        apply('Charcoal',
              dict(angle=[(282,773),(522,810),(624,718)], back=[(282,761),(524,807),(623,719)]),
              dict(angle=[(268,720),(522,750),(610,682)], back=[(280,716),(512,755),(619,679)]))
        # Basket corner chips and complete pot rim. Foliage is kept separate below.
        apply('Oak',
              dict(angle=[(468,209),(461,219),(514,235),(518,228),(445,306),(550,301)],
                   back=[(429,174),(407,193),(350,270),(481,264),(572,273)]),
              dict(angle=[(284,312),(385,337),(569,327),(543,197),(542,256),(580,234)],
                   back=[(328,292),(462,346),(374,219),(384,199),(349,158)]),
              protect=roles == 'Leaf')
        apply('Leaf',
              dict(angle=[(517,233),(530,219),(554,212)], back=[(355,226),(366,246),(376,209),(384,189),(394,193)]),
              dict(angle=[(508,270),(563,294),(574,239),(498,220),(471,204),(591,317)],
                   back=[(325,242),(335,269),(392,256),(417,223),(430,172),(390,283)]))

    assert len(roles) == len(data['faces']) == int(original['face_count'])
    np.savez_compressed(path, roles=roles, face_count=len(roles), vertex_count=len(data['vertices']))
    print('SAVED', path, dict(zip(*np.unique(roles, return_counts=True))), flush=True)

if __name__ == '__main__':
    for key in sys.argv[1:] or ('Shelf', 'DrawerUnit'):
        refine(key)
