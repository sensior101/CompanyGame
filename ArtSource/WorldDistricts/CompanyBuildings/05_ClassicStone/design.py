"""D: restrained classical stone office; geometry recipe for the shared driver."""


def build(api):
    box = api.box
    mass, stone, accents, roof, lobby, doors, trim, plinth = [api.MeshBatch() for _ in range(8)]
    box(plinth, (0, 0, .16), (64, 54, .32))
    box(mass, (0, 0, 6.10), (49.0, 41.0, 11.4))
    box(mass, (0, 0, 73.15), (46.6, 38.6, 122.3))

    # Tall stone base, a regular shaft, and a smaller inhabited crown.
    for z, w, d, h in [(11.85, 52.8, 44.8, 1.1), (135.2, 50.8, 42.8, 1.15),
                        (136.1, 51.6, 43.6, .55)]:
        box(stone, (0, 0, z), (w, d, h))
    for axis, plane, lo, hi, bays, normal in [
            ('x', -20, -24, 24, 12, -1), ('x', 20, -24, 24, 12, 1),
            ('y', -24, -20, 20, 10, -1), ('y', 24, -20, 20, 10, 1)]:
        api.panel_grid(axis, plane, lo, hi, 12.5, 134.6, bays, 28, normal,
                       frame_width=.11, frame_depth=.14, spandrel=.30,
                       name=f'D_Shaft_{axis}_{normal}', glass_material='Glass')
        pitch = (hi-lo)/bays
        for i in range(bays+1):
            u = lo+i*pitch
            width = 1.45 if i in (0, bays) else .88
            pos = (u, plane+normal*.38, 73.75) if axis == 'x' else (plane+normal*.38, u, 73.75)
            size = (width, 1.04, 122.5) if axis == 'x' else (1.04, width, 122.5)
            box(stone, pos, size)
            # Narrow recessed capitals distinguish the stone grid from A/C metal fins.
            for z in (13.5, 133.45):
                pos = (u, plane+normal*.45, z) if axis == 'x' else (plane+normal*.45, u, z)
                size = (width+.20, 1.15, .34) if axis == 'x' else (1.15, width+.20, .34)
                box(accents, pos, size)
        for floor in range(1, 28):
            z = 12.5+(134.6-12.5)*floor/28
            # Fine stone lintels on every floor, broader belt every seven floors.
            depth, h = (.60, .70) if floor % 7 == 0 else (.35, .36)
            pos = (0, plane+normal*.23, z) if axis == 'x' else (plane+normal*.23, 0, z)
            size = (hi-lo, depth, h) if axis == 'x' else (depth, hi-lo, h)
            box(stone, pos, size)

    # Set-back crown with real window openings between short stone pilasters.
    box(mass, (0, 0, 141.45), (33.8, 27.8, 9.9))
    box(roof, (0, 0, 136.50), (49.7, 41.7, .20))
    for axis, plane, span, bays in [('x', 14.5, 35.0, 7), ('y', 17.5, 29.0, 6)]:
        for normal in (-1, 1):
            api.panel_grid(axis, normal*plane, -span/2, span/2, 136.8, 146.35,
                           bays, 2, normal, frame_width=.15, frame_depth=.18,
                           spandrel=.30, name=f'D_Crown_{axis}_{normal}')
            for i in range(bays+1):
                u=-span/2+i*span/bays
                pos=(u,normal*(plane+.23),141.85) if axis=='x' else (normal*(plane+.23),u,141.85)
                size=(1.15,.78,10.3) if axis=='x' else (.78,1.15,10.3)
                box(stone,pos,size)
    box(stone,(0,0,147.10),(37.5,31.5,.72))
    for y in (-15.8,15.8):
        box(accents,(0,y,147.75),(38.2,.60,.50))
    for x in (-18.8,18.8):
        box(accents,(x,0,147.75),(.60,31.0,.50))
    box(roof,(0,0,147.58),(36.4,30.4,.12))

    # Ground-floor colonnade and separate four-metre paired doors.
    for normal in (-1,1):
        for x in (-24.8,-16.5,-8.25,8.25,16.5,24.8):
            box(stone,(x,normal*21.7,6.0),(1.40,1.50,11.6))
            box(accents,(x,normal*21.7,.77),(1.85,1.95,.62))
            box(accents,(x,normal*21.7,11.12),(1.75,1.85,.45))
        for y in (-13.8,-5.5,5.5,13.8):
            box(stone,(normal*25.7,y,6.0),(1.50,1.40,11.6))
        for i in range(12):
            x=-23.8+(i+.5)*47.6/12
            box(lobby,(x,normal*21.0,6.1),(3.84,.13,11.0))
            box(trim,(x-1.98,normal*21.14,6.1),(.13,.19,11.0))
        for i in range(10):
            y=-19.8+(i+.5)*39.6/10
            box(lobby,(normal*25.0,y,6.1),(.13,3.84,11.0))
            box(trim,(normal*25.14,y-1.98,6.1),(.19,.13,11.0))
    for x in (-1.2,1.2):
        box(doors,(x,-21.3,2.78),(2.28,.14,4.8),0)
    for x in (-2.45,0,2.45):
        box(trim,(x,-21.48,2.8),(.14,.22,4.92))
    box(trim,(0,-21.48,5.28),(5.0,.22,.18))
    box(stone,(0,-22.70,5.85),(10.0,5.5,.45))
    box(accents,(0,-25.38,5.88),(10.0,.16,.22))
    for x in (-.23,.23):
        box(trim,(x,-21.7,1.7),(.06,.07,.70))
    box(doors,(12.375,21.22,2.90),(6.10,.18,5.1),1)
    for z in (1,1.55,2.1,2.65,3.2,3.75,4.3,4.85):
        box(trim,(12.375,21.36,z),(6.00,.06,.045))

    api.finish('D_RecessedBacking','Structure',mass,['GlassDark'])
    api.finish('D_ClassicalStoneGrid','Structure',stone,['Stone'])
    api.finish('D_CapitalsAndCrownMouldings','Structure',accents,['Stone'])
    api.finish('D_ExposedRoofSurfaces','RoofDetails',roof,['Roof'])
    api.finish('D_WarmPublicLobby','Glass',lobby,['GlassWarm'])
    api.finish('D_EntryAndServicePanels','Entrance',doors,['Glass','Metal'])
    api.finish('D_BronzeWindowAndEntryTrim','Frames',trim,['Metal'])
    api.finish('D_StoneGroundPlinth','GroundDetails',plinth,['StoneDark'])
    return {'features':['Regular cream stone piers and window lintels',
                        'Small set-back crown with restrained classical cornices',
                        'Tall stone colonnade, bronze glazing and paired entrance',
                        'Four complete elevations and rear service entrance']}
