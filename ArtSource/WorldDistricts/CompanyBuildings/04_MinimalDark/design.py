"""Type C exterior: a straight graphite office tower with a flat roof.

Geometry recipe only. The shared office builder supplies the existing Handae
MeshBatch/box/loft helpers, materials, metre units, exports and presentation.
Front faces Blender -Y. Dimensions include the low entrance forecourt.
"""


def build(api):
    box = api.box
    mass, ribs, panes, frames = [api.MeshBatch() for _ in range(4)]
    base, lobby, entry, roof, service = [api.MeshBatch() for _ in range(5)]
    landscape, furniture = [api.MeshBatch() for _ in range(2)]

    # A compact plinth instead of a broad, multi-storey podium. Its top is a
    # single normal stair rise above the entry threshold.
    box(base, (0, 0, .15), (62, 52, .30))
    box(base, (0, 0, .36), (51.0, 45.0, .12), 1)

    # Keep the opaque shell behind the four actual glazed elevations. The
    # ground-floor volume is separate so the entry retains a tall lobby reading.
    box(mass, (0, 0, 77.40), (46.6, 40.6, 133.40))
    box(mass, (0, 0, 5.45), (44.8, 38.8, 10.10))

    # Both axes have 3 m bays. Deep black fins rather than a silver glass-grid
    # treatment distinguish this design from Type A.
    def elevation(axis, plane, lo, hi, normal, bays, label):
        step = (hi-lo)/bays
        lower, upper, floors = 10.7, 145.20, 32
        floor_height = (upper-lower)/floors
        for i in range(bays + 1):
            u = lo+i*step
            corner = i in (0, bays)
            wide = corner or i % 4 == 0
            width = 1.24 if corner else (.72 if wide else .43)
            depth = 1.38 if wide else 1.02
            bottom = .42 if wide else 10.45
            if axis == 'x' and normal == -1 and abs(u) < .01:
                bottom = 10.45  # Preserve the clear central entrance opening.
            top = 146.0
            if axis == 'x':
                box(ribs, (u, plane+normal*.38, (top+bottom)/2),
                    (width, depth, top-bottom))
            else:
                box(ribs, (plane+normal*.38, u, (top+bottom)/2),
                    (depth, width, top-bottom))

        for i in range(bays):
            u = lo+(i+.5)*step
            for floor in range(floors):
                z0 = lower+floor*floor_height+.15
                z1 = lower+(floor+1)*floor_height-.12
                # Restrained uneven window tints; no lit checkerboard facade.
                mat = 2 if (i*11+floor*17+len(label)) % 71 == 0 else (
                    1 if (i*3+floor*7) % 9 == 0 else 0)
                if axis == 'x':
                    box(panes, (u, plane-normal*.13, (z0+z1)/2),
                        (step-.38, .10, z1-z0), mat)
                    box(frames, (u, plane-normal*.04, z0-.17),
                        (step-.28, .18, .28))
                    box(frames, (u, plane+normal*.005, (z0+z1)/2),
                        (.085, .16, z1-z0))
                else:
                    box(panes, (plane-normal*.13, u, (z0+z1)/2),
                        (.10, step-.38, z1-z0), mat)
                    box(frames, (plane-normal*.04, u, z0-.17),
                        (.18, step-.28, .28))
                    box(frames, (plane+normal*.005, u, (z0+z1)/2),
                        (.16, .085, z1-z0))

            # Two storey warm curtain wall, with the front entrance reserved.
            for level, z0, z1 in [(0, .46, 5.12), (1, 5.32, 10.40)]:
                if axis == 'x' and normal == -1 and abs(u) < 3.01 and level == 0:
                    continue
                tint = 0 if normal < 0 else 1
                if axis == 'x':
                    box(lobby, (u, plane-normal*.38, (z0+z1)/2),
                        (step-.38, .10, z1-z0), tint)
                    box(frames, (u, plane-normal*.22, (z0+z1)/2),
                        (.10, .19, z1-z0))
                    box(frames, (u, plane-normal*.22, z1+.075),
                        (step-.25, .19, .15))
                else:
                    box(lobby, (plane-normal*.38, u, (z0+z1)/2),
                        (.10, step-.38, z1-z0), tint)
                    box(frames, (plane-normal*.22, u, (z0+z1)/2),
                        (.19, .10, z1-z0))
                    box(frames, (plane-normal*.22, u, z1+.075),
                        (.19, step-.25, .15))

    elevation('x', -21.0, -24.0, 24.0, -1, 16, 'Front')
    elevation('x', 21.0, -24.0, 24.0, 1, 16, 'Rear')
    elevation('y', -24.0, -21.0, 21.0, -1, 14, 'West')
    elevation('y', 24.0, -21.0, 21.0, 1, 14, 'East')

    # Flat, continuous roof edge. Fins finish flush at 146 m, with no decorative
    # crown spikes. The low service enclosure is hidden within the parapet.
    box(roof, (0, 0, 145.57), (48.08, 42.08, .38), 0)
    for y in [-20.93, 20.93]:
        box(roof, (0, y, 145.85), (48.10, .25, .30), 1)
    for x in [-23.93, 23.93]:
        box(roof, (x, 0, 145.85), (.25, 41.65, .30), 1)
    box(roof, (0, 1.5, 145.82), (17.5, 14.0, .28), 0)

    # Entry is human-scaled even though the office tower is 146 m tall.
    # Two 2.55 m door leaves are 4.5 m high, beneath a thin 6.8 m canopy.
    for x in [-1.345, 1.345]:
        box(entry, (x, -21.30, 2.71), (2.55, .12, 4.5), 1)
    for x in [-2.74, 0, 2.74]:
        box(entry, (x, -21.42, 2.75), (.14, .22, 4.66))
    box(entry, (0, -21.42, 5.13), (5.62, .24, .20))
    for x in [-.22, .22]:
        box(entry, (x, -21.64, 2.37), (.065, .10, 1.05), 2)
    box(entry, (0, -22.40, 5.45), (6.80, 3.18, .30))
    box(entry, (0, -22.42, 5.285), (6.25, 2.70, .045), 3)
    box(entry, (0, -22.45, .40), (6.60, 2.80, .04), 0)

    # Rear loading/service door and grille use the same dark material language.
    # Their outer plane is in front of glazing, preventing coplanar flicker.
    box(service, (6.0, 21.46, 2.60), (5.50, .18, 4.36))
    for x in [3.16, 8.84]:
        box(service, (x, 21.60, 2.67), (.18, .22, 4.5), 1)
    box(service, (6.0, 21.60, 4.94), (5.86, .22, .17), 1)
    for i in range(15):
        box(service, (6.0, 21.575, .68+i*.27), (5.42, .08, .065), 1)
    box(service, (-10.5, 21.46, 1.74), (1.45, .19, 2.60))
    box(service, (-9.98, 21.60, 1.53), (.05, .065, .35), 2)

    # Small architectural furniture is integral to the first exterior model;
    # broad planting masses are deliberately omitted from the facade silhouette.
    for x in [-19.0, 19.0]:
        box(landscape, (x, -24.25, .71), (4.3, 1.40, .60), 0)
        box(landscape, (x, -24.25, 1.07), (4.02, 1.17, .16), 1)
        for bx in [x-.65, x+.65]:
            box(furniture, (bx, -22.79, .60), (.12, .56, .39), 0)
        box(furniture, (x, -22.79, .82), (1.9, .60, .10), 1)

    api.finish('C_InsetShadowBody', 'Structure', mass, ['GlassDark'])
    api.finish('C_GraphiteVerticalFins', 'Structure', ribs, ['Metal'])
    api.finish('C_TowerGlazing', 'Glass', panes, ['Glass', 'GlassDark', 'GlassWarm'])
    api.finish('C_ThinMullionsAndSpandrels', 'Frames', frames, ['Metal'])
    api.finish('C_LowForecourt', 'GroundDetails', base, ['StoneDark', 'Metal'])
    api.finish('C_TallLobbyGlazing', 'Glass', lobby, ['GlassWarm', 'Glass'])
    api.finish('C_RecessedEntrance', 'Entrance', entry,
               ['Metal', 'GlassWarm', 'MetalLight', 'Wood'])
    api.finish('C_FlatRoofAndInsetPlant', 'RoofDetails', roof, ['Roof', 'Metal'])
    api.finish('C_RearServiceDoors', 'Entrance', service,
               ['Metal', 'StoneDark', 'MetalLight'])
    api.finish('C_EntrancePlanters', 'GroundDetails', landscape, ['StoneDark', 'Green'])
    api.finish('C_ForecourtBenches', 'GroundDetails', furniture, ['Metal', 'Wood'])

    return {
        'type': 'C',
        'name': 'Minimal Dark',
        'dimensions_m': [62.0, 52.0, 146.0],
        'shaft_m': [48.0, 42.0],
        'entrance_anchor': [0.0, -23.0, .42],
        'service_anchor': [6.0, 23.0, .30],
        'features': [
            'Constant graphite shaft with deep fins and broad corner posts',
            'Thin horizontal spandrels; no stone floor bands',
            'Flush flat roofline without ornamental crown spikes',
            'Tall warm glazed lobby and human-scaled double entry',
            'Four complete elevations and rear service access',
        ],
    }
