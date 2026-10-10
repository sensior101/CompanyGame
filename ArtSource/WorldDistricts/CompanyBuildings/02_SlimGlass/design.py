"""Type A: slim glazed office, reference-led proportional exterior in metres.

Geometry recipe only. The shared office pipeline owns materials, scene setup,
the existing Handae mesh helpers, validation, rendering and Unity exports.
"""


def build(api):
    box = api.box
    finish = api.finish

    # A compact two-storey base supports a constant-section 48 x 40 m shaft.
    # The dark backing is recessed 0.8 m behind the outer curtain-glass planes.
    mass = api.MeshBatch()
    box(mass, (0, 0, 76.25), (46.4, 38.4, 127.5))
    box(mass, (0, 0, 6.2), (56.8, 44.8, 11.6))
    finish('A_RecessedShadowMass', 'Structure', mass, ['GlassDark'])

    plinth = api.MeshBatch()
    box(plinth, (0, 0, .18), (60, 52, .36))
    finish('A_CompactGroundPlinth', 'GroundDetails', plinth, ['StoneDark'])

    # Hairline horizontal joints and continuous silver fins make the shaft read
    # as a glass tower, rather than the stone lattice of the preceding building.
    elevations = [
        ('Front', 'x', -20, -24, 24, 16, -1),
        ('Rear', 'x', 20, -24, 24, 16, 1),
        ('West', 'y', -24, -20, 20, 14, -1),
        ('East', 'y', 24, -20, 20, 14, 1),
    ]
    for label, axis, plane, lo, hi, bays, normal in elevations:
        api.panel_grid(axis, plane, lo, hi, 12.5, 140, bays, 32, normal,
                       frame_width=.075, frame_depth=.12, spandrel=.13,
                       glass_material='Glass', frame_material='MetalLight',
                       name='A_Tower_' + label, fins=0)

    def crown_height(x):
        if x < -8.001:
            return 148.0
        if x < 8.001:
            return 146.0
        return 144.0

    fins = api.MeshBatch()
    crown_rails = api.MeshBatch()
    for label, axis, plane, lo, hi, bays, normal in elevations:
        for i in range(bays + 1):
            u = lo + (hi - lo) * i / bays
            x = u if axis == 'x' else plane
            top = crown_height(x)
            if axis == 'x':
                box(fins, (u, plane + normal * .34, (12.4 + top) / 2),
                    (.20, .84, top - 12.4))
            else:
                box(fins, (plane + normal * .34, u, (12.4 + top) / 2),
                    (.84, .20, top - 12.4))
        # The modest tie rail sits behind, keeping individual fin ends visible.
        if axis == 'x':
            box(crown_rails, (0, plane - normal * .08, 140.15), (48, .15, .20))
        else:
            box(crown_rails, (plane - normal * .08, 0, 140.15), (.15, 40, .20))
    finish('A_ContinuousSilverFins', 'Frames', fins, ['MetalLight'])
    finish('A_RecessedCrownTieRails', 'RoofDetails', crown_rails, ['Metal'])

    # Recessed, three-step roof screens give the reference's quiet crown profile.
    # Screen panels stay below the fin tips and leave the roof as a real volume.
    roof = api.MeshBatch()
    screen = api.MeshBatch()
    screen_frames = api.MeshBatch()
    for left, right, top in [(-23.2, -8, 145.5), (-8, 8, 143.5), (8, 23.2, 141.5)]:
        height = top - 140.0
        box(screen, ((left + right) / 2, 0, 140 + height / 2),
            (right - left, 38.4, height))
        box(roof, ((left + right) / 2, 0, top + .06), (right - left, 38.4, .12))
        for y in (-19.25, 19.25):
            box(screen_frames, ((left + right) / 2, y, top - .12),
                (right - left, .12, .20))
    finish('A_SteppedRoofScreen', 'RoofDetails', screen, ['Metal'])
    finish('A_SteppedRoofCaps', 'RoofDetails', roof, ['Roof'])
    finish('A_RoofScreenEdging', 'RoofDetails', screen_frames, ['MetalLight'])

    # Darker glazed base: all four elevations are modeled; the front grid is
    # interrupted at its central, human-scale entrance rather than covering it.
    for side, lo, hi, bays in [('Left', -29.4, -4.1, 8), ('Right', 4.1, 29.4, 8)]:
        api.panel_grid('x', -23.4, lo, hi, .45, 12.1, bays, 2, -1,
                       frame_width=.14, frame_depth=.26, spandrel=.24,
                       glass_material='GlassDark', frame_material='Metal',
                       name='A_Lobby_Front_' + side, fins=0)
    for label, axis, plane, lo, hi, bays, normal in [
        ('West', 'y', -29.4, -23.4, 23.4, 14, -1),
        ('East', 'y', 29.4, -23.4, 23.4, 14, 1),
    ]:
        api.panel_grid(axis, plane, lo, hi, .45, 12.1, bays, 2, normal,
                       frame_width=.14, frame_depth=.26, spandrel=.24,
                       glass_material='GlassDark', frame_material='Metal',
                       name='A_Lobby_' + label, fins=0)
    # A clear rear loading bay occupies x=11..21; the rest remains curtain wall.
    for label, lo, hi, bays in [('West', -29.4, 10.8, 12), ('East', 21.2, 29.4, 3)]:
        api.panel_grid('x', 23.4, lo, hi, .45, 12.1, bays, 2, 1,
                       frame_width=.14, frame_depth=.26, spandrel=.24,
                       glass_material='GlassDark', frame_material='Metal',
                       name='A_Lobby_Rear_' + label, fins=0)

    base_edges = api.MeshBatch()
    box(base_edges, (0, 0, 12.28), (59.9, 47.9, .36))
    for y in (-23.65, 23.65):
        box(base_edges, (0, y, 6.3), (59.7, .20, .28))
    for x in (-29.65, 29.65):
        box(base_edges, (x, 0, 6.3), (.20, 47.3, .28))
    finish('A_BaseSlenderMetalBands', 'Frames', base_edges, ['Metal'])

    # Paired 2.2 m doors make a 4.4 m entry under a 4.9 m high canopy.
    entry_glass = api.MeshBatch()
    entry_frames = api.MeshBatch()
    canopy = api.MeshBatch()
    for x in (-1.12, 1.12):
        box(entry_glass, (x, -23.48, 2.6), (2.12, .10, 4.30))
        box(entry_frames, (x - 1.08, -23.60, 2.6), (.10, .12, 4.40))
        box(entry_frames, (x + 1.08, -23.60, 2.6), (.10, .12, 4.40))
        box(entry_frames, (x, -23.60, 4.8), (2.25, .12, .10))
        handle_x = x + .84 if x < 0 else x - .84
        box(entry_frames, (handle_x, -23.76, 2.2), (.075, .075, 1.0))
    for x in (-3.16, 3.16):
        box(entry_glass, (x, -23.44, 2.6), (1.83, .10, 4.3))
    box(entry_glass, (0, -23.44, 8.53), (8.12, .10, 6.54))
    for x in (-4.08, 4.08):
        box(entry_frames, (x, -23.56, 6.28), (.14, .20, 11.7))
    box(entry_frames, (0, -23.55, 8.58), (.10, .15, 6.44))
    box(canopy, (0, -24.08, 5.08), (9.8, 3.1, .22))
    box(canopy, (0, -25.61, 4.94), (9.8, .06, .07))
    finish('A_MainEntryDoorsAndTransom', 'Entrance', entry_glass, ['Glass'])
    finish('A_EntryFramesAndHandles', 'Entrance', entry_frames, ['MetalLight'])
    finish('A_FloatingEntranceCanopy', 'Entrance', canopy, ['Metal'])

    service = api.MeshBatch()
    service_frames = api.MeshBatch()
    box(service, (16, 23.46, 2.77), (9.8, .12, 4.64))
    box(service, (16, 23.42, 8.52), (10.1, .12, 6.55))
    for x in (10.92, 21.08):
        box(service_frames, (x, 23.59, 6.26), (.18, .22, 11.62))
    box(service_frames, (16, 23.6, 5.13), (10.3, .20, .20))
    for i in range(17):
        box(service_frames, (16, 23.57, .65 + i * .26), (9.75, .045, .035))
    finish('A_RearServicePanels', 'Entrance', service, ['Metal'])
    finish('A_RearServiceFrameAndShutterSlats', 'Entrance', service_frames, ['MetalLight'])

    # Low planters locate the entry without adding surrounding scenery.
    planters = api.MeshBatch()
    planting = api.MeshBatch()
    for x in (-24, 24):
        box(planters, (x, -24.7, .65), (7.0, 1.9, .58))
        box(planting, (x, -24.7, 1.04), (6.6, 1.5, .22))
    finish('A_EntryPlanters', 'GroundDetails', planters, ['StoneDark'])
    finish('A_EntryPlanting', 'GroundDetails', planting, ['Green'])

    return {
        'reference_type': 'A - Slim glass tower',
        'features': [
            'Constant rectangular glass shaft above a compact two-storey lobby',
            'Continuous silver vertical fins and restrained three-step crown',
            '148 m maximum height and a compact ground plinth',
            'Four modeled elevations, glazed main entry and rear service shutter',
        ],
    }
