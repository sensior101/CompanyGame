"""Blender-only editable retail shell and opening authoring.
Reuse existing furniture primitives. Wall-local opening rectangles reconstruct
solid panels on both sides of each opening. No runtime/game state is written.
"""
import bpy
import bmesh
import hashlib
import importlib.util
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

sys.dont_write_bytecode = True
OUT = Path(__file__).resolve().parent
ART = OUT.parents[1]
SOURCE = OUT.parent / 'ShopInteriorConcept_20261105/ShopInterior_Empty.blend'
helper = ART / 'Items/Furniture/CommercialLowPoly_20261105/build_commercial.py'
spec = importlib.util.spec_from_file_location('commercial', helper)
commercial = importlib.util.module_from_spec(spec)
spec.loader.exec_module(commercial)
h = commercial.h
for folder in ('Previews', 'QA'):
    (OUT / folder).mkdir(exist_ok=True)
HEIGHT = 3.3
THICKNESS = .20
WALLS = {
    'Front': (26.4, (0, -8.3, 0), 0),
    'Back': (26.4, (0, 8.3, 0), math.pi),
    'Left': (16.4, (-13.1, 0, 0), -math.pi/2),
    'Right': (16.4, (13.1, 0, 0), math.pi/2),
}
DOOR = dict(id='Entrance', wall='Front', u=0, sill=0, width=2.10, height=2.60)
DEMO = [
    dict(id='Window01', wall='Front', u=4.0, sill=.90, width=2.0, height=1.40),
    dict(id='Window02', wall='Back', u=-4.0, sill=1.0, width=1.80, height=1.40),
    dict(id='Window03', wall='Right', u=1.0, sill=.90, width=2.40, height=1.50),
]

def rectangle(opening):
    return (opening['u']-opening['width']/2, opening['u']+opening['width']/2,
            opening['sill'], opening['sill']+opening['height'])

def validate_openings(windows):
    ids = set()
    for window in windows:
        if not window['id'] or window['id'] in ids or window['id'] == 'Entrance':
            raise ValueError('Duplicate or reserved opening id')
        ids.add(window['id'])
        if window['wall'] not in WALLS:
            raise ValueError('Unknown wall')
        values = [window[k] for k in ('u', 'sill', 'width', 'height')]
        if not all(isinstance(v, (int, float)) and math.isfinite(v) for v in values):
            raise ValueError('Opening dimensions must be finite numbers')
        left, right, bottom, top = rectangle(window)
        half_width = WALLS[window['wall']][0]/2
        if window['width'] < .40 or window['height'] < .40 or bottom < .20 or top > HEIGHT-.15:
            raise ValueError('Invalid window size or vertical clearance')
        if left < -half_width+.25 or right > half_width-.25:
            raise ValueError('Window crosses wall corner clearance')
    for i, opening in enumerate([DOOR]+windows):
        a = rectangle(opening)
        for other in ([DOOR]+windows)[i+1:]:
            if opening['wall'] != other['wall']:
                continue
            b = rectangle(other)
            if a[0] < b[1]+.10 and b[0] < a[1]+.10 and a[2] < b[3]+.10 and b[2] < a[3]+.10:
                raise ValueError('Openings overlap or have insufficient pier clearance')

def wall_panels(wall, windows):
    width = WALLS[wall][0]
    holes = [rectangle(w) for w in [DOOR]+windows if w['wall'] == wall]
    cuts = sorted({-width/2, width/2, *[edge for hole in holes for edge in hole[:2]]})
    panels = []
    for left, right in zip(cuts, cuts[1:]):
        middle = (left+right)/2
        vertical = sorted((a[2], a[3]) for a in holes if a[0] < middle < a[1])
        bottom = 0
        for low, high in vertical+[(HEIGHT, HEIGHT)]:
            if low-bottom > .00001:
                panels.append((left, right, bottom, low))
            bottom = high
    return panels

def empty(name, parent, location=(0,0,0), yaw=0):
    obj = bpy.data.objects.new(name, None)
    h.asset.objects.link(obj)
    obj.parent = parent
    obj.location = location
    obj.rotation_euler.z = yaw
    return obj

def build(windows):
    validate_openings(windows)
    h.init(dict(id='ShopShell', label='창문 없는 기본 상가', function='Architecture', placement='Floor'))
    building = h.root
    building['InteriorWidth'] = 26.0
    building['InteriorDepth'] = 16.4
    building['CeilingHeight'] = HEIGHT
    building['WindowLayout'] = json.dumps(windows)
    for role, color in dict(Body='EBE8E0', White='F7F5EF', Wood='C7AA80', Dark='363C3B', Metal='C1C7C5').items():
        mat = h.MATS['Cream'][role]
        mat.diffuse_color = h.rgba(color)
        mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = h.rgba(color)
    floor = h.box('Floor', (26,16.4,.24), (0,0,-.12), 'Body', 0)
    ceiling = h.box('Ceiling', (26.4,16.8,.14), (0,0,3.37), 'White', 0)
    roots = {}
    for wall, (width, location, yaw) in WALLS.items():
        root = empty('Wall_'+wall, building, location, yaw)
        root['WallId'] = wall
        root['Width'] = width
        root['Height'] = HEIGHT
        root['Thickness'] = THICKNESS
        root['OpeningCoordinates'] = 'Local X = horizontal u, local Z = height'
        roots[wall] = root
        h.root = root
        for index, (left, right, bottom, top) in enumerate(wall_panels(wall, windows)):
            panel = h.box(f'{wall}_SolidPanel_{index:02d}', (right-left,THICKNESS,top-bottom),
                          ((left+right)/2,0,(bottom+top)/2), 'Body', 0)
            panel['PanelBounds'] = [left,right,bottom,top]
            panel['CollisionPurpose'] = 'Solid wall box'
    door = empty('AutomaticDoor', building, (0,-8.12,0))
    door['OpenRatio'] = 0.0
    door.id_properties_ui('OpenRatio').update(min=0,max=1)
    door['SlideDistance'] = 1.03
    door['FutureUnityComponent'] = 'CompanyGame.World.Maps.LobbyAutomaticDoor'
    h.root = door
    for x in (-1.015, 1.015):
        h.box('DoorJamb', (.07,.26,2.60), (x,-.11,1.30), 'White', .006)
    h.box('DoorHeader', (4.22,.30,.17), (0,.045,2.635), 'White', .009)
    h.box('DoorRail', (4.10,.06,.045), (0,.105,2.535), 'Metal', .003)
    h.box('SensorHousing', (.18,.055,.065), (0,-.13,2.64), 'Dark', .006)
    leaves = []
    for side, sign in (('Left',-1), ('Right',1)):
        leaf = empty(side+'Leaf', door, (sign*.495,.10,0))
        leaves.append(leaf)
        driver = leaf.driver_add('location',0).driver
        variable = driver.variables.new()
        variable.name = 'openness'
        variable.type = 'SINGLE_PROP'
        variable.targets[0].id = door
        variable.targets[0].data_path = '["OpenRatio"]'
        driver.expression = f'{sign*.495}+({sign*1.03})*openness'
        h.root = leaf
        for x in (-.47,.47):
            h.box('LeafStile',(.05,.05,2.49),(x,0,1.275),'Dark',.003)
        for z in (.055,2.495):
            h.box('LeafRail',(.89,.05,.05),(0,0,z),'Dark',.003)
        h.box('DoorGlass',(.89,.012,2.39),(0,0,1.275),'Glass',0)
        h.box('SafetyStripe',(.89,.014,.045),(0,-.001,1.05),'White',0)
    for frame, value in ((1,0),(20,0),(45,1),(80,1),(105,0),(120,0)):
        door['OpenRatio'] = value
        door.keyframe_insert(data_path='["OpenRatio"]', frame=frame)
    bpy.context.scene.frame_end = 120
    bpy.context.scene.frame_set(1)
    for window in windows:
        root = empty(window['id'], roots[window['wall']], (window['u'],0,window['sill']))
        root['OpeningId'] = window['id']
        root['OpeningWidth'] = window['width']
        root['OpeningHeight'] = window['height']
        h.root = root
        width, height = window['width'], window['height']
        for x in (-width/2+.035,width/2-.035):
            h.box('WindowJamb',(.07,.22,height),(x,0,height/2),'White',.003)
        for z in (.035,height-.035):
            h.box('WindowRail',(width-.14,.22,.07),(0,0,z),'White',.003)
        h.box('WindowGlass',(width-.14,.014,height-.14),(0,0,height/2),'Glass',0)
    h.root = building
    for obj in h.PARTS:
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.uv.smart_project(island_margin=.01)
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()
    return building, roots, door, leaves, ceiling

def setup_view(location, target, scale):
    # Reuse the furniture camera setup and select GPU again after factory reset.
    commercial.camera_scene(location,target,scale,(1500,1000))
    scene=bpy.context.scene
    scene.cycles.samples=24
    preferences=bpy.context.preferences.addons['cycles'].preferences
    try:
        preferences.compute_device_type='OPTIX'
        preferences.get_devices()
        for device in preferences.devices:
            device.use=device.type=='OPTIX'
        scene.cycles.device='GPU' if any(d.type=='OPTIX' for d in preferences.devices) else 'CPU'
    except Exception:
        scene.cycles.device='CPU'
    bpy.context.preferences.filepaths.save_version=0

def render(name):
    bpy.context.scene.render.filepath=str(OUT/'Previews'/f'{name}.png')
    bpy.ops.render.render(write_still=True)

def verify_geometry(roots, door, leaves, windows):
    for obj in h.PARTS:
        bm=bmesh.new();bm.from_mesh(obj.data)
        assert all(edge.is_manifold for edge in bm.edges),obj.name
        assert all(face.calc_area()>1e-10 for face in bm.faces),obj.name
        bm.free()
    expected=2.10*2.60+sum(w['width']*w['height'] for w in windows)
    actual=sum(WALLS[wall][0]*HEIGHT-sum((r-l)*(t-b) for l,r,b,t in wall_panels(wall,windows)) for wall in WALLS)
    assert abs(actual-expected)<1e-6
    # Check ray openings through full wall thickness on every authored opening.
    deps=bpy.context.evaluated_depsgraph_get()
    for opening in [DOOR]+windows:
        point=Vector((opening['u'],-.5,opening['sill']+opening['height']/2))
        wall=roots[opening['wall']]
        for obj in wall.children:
            if obj.type!='MESH':continue
            local=obj.matrix_world.inverted() @ (wall.matrix_world @ point)
            direction=obj.matrix_world.inverted().to_3x3() @ (wall.matrix_world.to_3x3() @ Vector((0,1,0)))
            hit=obj.ray_cast(local,direction,distance=1,depsgraph=deps)[0]
            assert not hit,opening['id']
    bpy.context.scene.frame_set(50)
    bpy.context.view_layer.update()
    gap=leaves[1].location.x-leaves[0].location.x-.99
    assert gap>=1.96
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    assert abs(leaves[1].location.x-leaves[0].location.x-.99)<1e-5
    return dict(window_count=len(windows),wall_opening_area=round(actual,4),door_open_gap=round(gap,4),closed_meshes=True,opening_rays_clear=True)

def validate_layout_operations():
    # Install, move, remove all use the same deterministic wall reconstruction.
    first=DEMO[:1]
    moved=[dict(first[0],u=7.0)]
    validate_openings(first);validate_openings(moved);validate_openings([])
    def solid_at(windows,u,z):
        return any(l<u<r and b<z<t for l,r,b,t in wall_panels('Front',windows))
    assert not solid_at(first,4,1.5)
    assert solid_at(moved,4,1.5) and not solid_at(moved,7,1.5)
    assert solid_at([],4,1.5) and solid_at([],7,1.5)
    cases=[DEMO+[dict(DEMO[0],id='DuplicateArea')], [dict(DEMO[0],u=0)],
           [dict(DEMO[0],u=13)], [dict(DEMO[0],sill=3)], [dict(DEMO[0],width=-1)]]
    for case in cases:
        try:validate_openings(case)
        except ValueError:continue
        raise AssertionError('Invalid opening accepted')
    return dict(install_move_remove=True,rejected_invalid_cases=len(cases))

def main():
    if '--layout' in sys.argv:
        path=Path(sys.argv[sys.argv.index('--layout')+1]).resolve()
        windows=json.loads(path.read_text(encoding='utf8'))['windows']
        building,roots,door,leaves,ceiling=build(windows)
        result=verify_geometry(roots,door,leaves,windows)
        setup_view((25,-29,19),(0,0,1),36)
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ShopShell_CustomLayout.blend'),compress=True)
        (OUT/'QA/custom_layout_validation.json').write_text(json.dumps(result,indent=2),encoding='utf8')
        print('CUSTOM_LAYOUT_COMPLETE',json.dumps(result),flush=True)
        return
    source_hash=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    operations=validate_layout_operations()
    building,roots,door,leaves,ceiling=build([])
    base=verify_geometry(roots,door,leaves,[])
    setup_view((21,-30,18),(0,0,1),35)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ShopShell_NoWindows.blend'),compress=True)
    render('01_Exterior_Closed')
    bpy.context.scene.frame_set(50)
    render('02_Exterior_Open')
    bpy.context.scene.frame_set(1)
    ceiling.hide_render=True
    for obj in roots['Right'].children:obj.hide_render=True
    setup_view((22,-28,26),(0,0,0),36)
    render('03_Interior_Cutaway')
    building,roots,door,leaves,ceiling=build(DEMO)
    demo=verify_geometry(roots,door,leaves,DEMO)
    setup_view((25,-29,19),(0,0,1),36)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'WindowPlacement_Demo.blend'),compress=True)
    render('04_WindowPlacement_Demo')
    assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==source_hash
    manifest=dict(interior=[26,16.4,HEIGHT],wall_thickness=THICKNESS,
        wall_frames={key:dict(width=value[0],origin=value[1],yaw=value[2]) for key,value in WALLS.items()},
        default_windows=[],door=DOOR,door_slide_distance=1.03,
        demo_windows=DEMO,source=str(SOURCE),source_sha256=source_hash,
        scope='Blender authoring only; runtime installation, persistence and networking pending')
    (OUT/'layout.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    (OUT/'WindowLayout.example.json').write_text(json.dumps(dict(windows=DEMO),indent=2),encoding='utf8')
    (OUT/'QA/validation.json').write_text(json.dumps(dict(base=base,demo=demo,operations=operations,source_preserved=True),indent=2),encoding='utf8')
    print('SHELL_COMPLETE',json.dumps(base),flush=True)

if __name__=='__main__':
    main()
