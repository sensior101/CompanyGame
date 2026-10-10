"""Type B: urban stone-and-glass office with inhabited setback terraces.

Geometry recipe only; the shared CompanyBuildings authoring driver owns Blender
scene setup, materials, Handae mesh helpers, presentation, and validated export.
Metres, -Y front. Envelope 68 x 54 x 144 m, ground datum zero.
"""


def build(api):
    mass = api.MeshBatch()
    stone = api.MeshBatch()
    roof = api.MeshBatch()
    trim = api.MeshBatch()
    ground = api.MeshBatch()
    lobby = api.MeshBatch()
    doors = api.MeshBatch()
    planters = api.MeshBatch()
    planting = api.MeshBatch()

    # The floor plate, rather than a wide multi-storey podium, establishes the
    # site envelope. Its taller central lobby remains almost the shaft width.
    api.box(ground, (0, 0, .15), (68, 54, .30))
    api.box(mass, (0, 0, 7.0), (57.2, 44.4, 13.4))
    api.box(stone, (0, 0, 13.55), (60, 48, .90))
    api.box(roof, (0, 0, 14.06), (58.8, 46.8, .10))

    # Four facade grids for the double-height public base and upper mezzanine.
    for normal in (-1, 1):
        api.panel_grid('x', normal * 23, -28.7, 28.7, .45, 12.85,
                       14, 3, normal, frame_width=.17, frame_depth=.24,
                       spandrel=.30, name=f'B_Lobby_{normal}')
        api.panel_grid('y', normal * 29, -22.4, 22.4, .45, 12.85,
                       10, 3, normal, frame_width=.17, frame_depth=.24,
                       spandrel=.30, name=f'B_LobbySide_{normal}')
        for x in (-28.8, -19.2, -9.6, 9.6, 19.2, 28.8):
            api.box(stone, (x, normal * 23.45, 6.95), (1.1, 1.1, 13.3))
        for y in (-14.8, -5.0, 5.0, 14.8):
            api.box(stone, (normal * 29.45, y, 6.95), (1.1, 1.1, 13.3))

    def tier(label, width, depth, z0, z1, xbays, ybays, floors,
             group_size, horizontal_every):
        """Facade composition using the common glazing and mesh primitives."""
        body_top = z1 - .60
        api.box(mass, (0, 0, (z0 + body_top) / 2),
                (width - 1.8, depth - 1.8, body_top - z0))
        # The exposed terrace finish sits above, not inside, the capped body.
        api.box(roof, (0, 0, z1 - .43), (width - .5, depth - .5, .14))
        for axis, half_depth, span, bays in (
                ('x', depth / 2, width, xbays),
                ('y', width / 2, depth, ybays)):
            for normal in (-1, 1):
                plane = normal * half_depth
                api.panel_grid(axis, plane, -span / 2 + .52,
                               span / 2 - .52, z0 + .52, z1 - 1.12,
                               bays, floors, normal,
                               frame_width=.13, frame_depth=.20,
                               spandrel=.25, name=f'B_{label}_{axis}_{normal}')
                # Tall stone subdivisions are spaced more widely than the
                # metal window grid, retaining the photo's large glass fields.
                pitch = span / bays
                for i in range(bays + 1):
                    if i % group_size and i not in (0, bays):
                        continue
                    u = -span / 2 + i * pitch
                    pier_w = 1.38 if i in (0, bays) else 1.02
                    center = ((u, plane + normal * .20, (z0 + z1) / 2)
                              if axis == 'x' else
                              (plane + normal * .20, u, (z0 + z1) / 2))
                    size = ((pier_w, .75, z1 - z0) if axis == 'x'
                            else (.75, pier_w, z1 - z0))
                    api.box(stone, center, size)
                # A wider stone frame surrounds paired floors, with fine
                # metal spandrels between. The result is masonry + glazing,
                # distinct from the all-metal A and C curtain-wall towers.
                z_values = [z0 + .20, z1 - .50]
                z_values += [z0 + (z1 - z0) * f / floors
                             for f in range(horizontal_every, floors,
                                            horizontal_every)]
                for z in z_values:
                    is_cornice = z == z_values[1]
                    band_h = 1.0 if is_cornice else .75
                    center = ((0, plane + normal * .18, z) if axis == 'x'
                              else (plane + normal * .18, 0, z))
                    size = ((span + .68, .78, band_h) if axis == 'x'
                            else (.78, span + .68, band_h))
                    api.box(stone, center, size)

    # Broad shaft with three progressively recessed upper volumes. Terraces
    # run continuously around every setback and remain visible from all sides.
    tier('MainShaft', 54, 44, 14, 114, 12, 10, 20, 3, 2)
    tier('UpperTerrace', 46, 36, 114, 125, 10, 8, 2, 2, 2)
    tier('CrownOffice', 34, 28, 125, 135, 8, 6, 2, 2, 2)
    tier('Penthouse', 22, 20, 135, 144, 4, 4, 2, 1, 2)

    # Low raised beds occupy terrace corners without hiding the setback form.
    # Their 0.65 m wall / 0.30 m plant heights preserve a believable human scale.
    for width, depth, z in ((54, 44, 113.65), (46, 36, 124.65),
                            (34, 28, 134.65)):
        for side in (-1, 1):
            for xside in (-1, 1):
                x = xside * (width / 2 - 3.3)
                y = side * (depth / 2 - 1.30)
                api.box(planters, (x, y, z + .40), (4.0, 1.05, .65))
                api.box(planting, (x, y, z + .85), (3.72, .82, .30))

    # Human-scale entrance is visibly smaller than the public double-height
    # lobby. The common glazing behind it reads as the recessed vestibule.
    api.box(lobby, (0, -23.45, 2.75), (6.4, .16, 4.90))
    for x in (-3.3, 0, 3.3):
        api.box(trim, (x, -23.66, 2.8), (.16, .22, 5.0))
    api.box(trim, (0, -23.66, 5.25), (6.76, .22, .16))
    api.box(stone, (0, -24.25, 5.60), (10.4, 4.8, .36))
    api.box(trim, (0, -26.64, 5.61), (10.42, .08, .30))
    for x in (-.23, .23):
        api.box(trim, (x, -23.85, 1.55), (.055, .06, .62))

    # Rear loading door and small staff entrance; no interaction scripts.
    api.box(doors, (14.4, 23.32, 2.65), (6.2, .22, 4.70))
    api.box(doors, (-21.0, 23.34, 1.65), (2.0, .20, 2.70))
    for z in (1.0, 1.55, 2.1, 2.65, 3.2, 3.75, 4.3):
        api.box(trim, (14.4, 23.49, z), (6.0, .08, .055))
    api.box(stone, (14.4, 24.02, 5.28), (7.5, 2.5, .30))

    api.finish('B_RecessedMasses', 'Structure', mass, ['GlassDark'])
    api.finish('B_StoneFacadeGrid', 'Structure', stone, ['Stone'])
    api.finish('B_ExposedTerraceFloors', 'RoofDetails', roof, ['Roof'])
    api.finish('B_GroundPlinth', 'GroundDetails', ground, ['StoneDark'])
    api.finish('B_EntryGlass', 'Entrance', lobby, ['GlassWarm'])
    api.finish('B_EntranceAndServiceMetal', 'Entrance', trim, ['Metal'])
    api.finish('B_RearServiceDoors', 'Entrance', doors, ['MetalLight'])
    api.finish('B_TerracePlanters', 'RoofDetails', planters, ['StoneDark'])
    api.finish('B_TerracePlanting', 'RoofDetails', planting, ['Green'])
    return {'features':['Neutral stone grid around larger glass fields',
                        'Three upper setbacks with continuous roof terraces',
                        'Compact public lobby and separate loading entrance',
                        'Four complete elevations with low terrace planting']}
