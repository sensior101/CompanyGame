"""City-hall site recipe; metre units, front -Y, paving datum +0.14 m.

Uses the existing CivicLibrary box/beam/ramp/ramp_rail/shrub API. Scene setup,
materials, roots, building, signage, flagpoles and export belong to the caller.
"""


def build(api):
    pavement_z = .14
    entry_z = .92

    # The complete 42 x 34 m footprint has no off-site kerb or planting meshes.
    api.box('CityHall_Site_42x34', (0, 0, .07), (42, 34, .14),
            'Pavement', 'Landscape')
    # The front extension leaves 1.43 m in front of the portal pier for the
    # ramp-to-entry turn, keeping the building and switchback positions fixed.
    api.box('CityHall_MainEntranceLanding', (0, -7.175, .53),
            (14.8, 2.75, .78), 'Concrete', 'Steps')

    # Six 130 mm risers, 360 mm treads: +0.14 to +0.92 without an extra lip.
    for i in range(6):
        ya = -10.71 + i * .36
        yb = ya + .36
        top = pavement_z + (i + 1) * .13
        api.box(f'CityHall_EntryStep_{i + 1:02d}',
                (0, (ya + yb) / 2, (pavement_z + top) / 2),
                (11.6, .36, top - pavement_z), 'Concrete', 'Steps')

    # Stair handrails stay inside the stair footprint: the central approach
    # X[-5.8,5.8], Y[-17,-10.71] remains completely free of obstacles.
    for side in (-1, 1):
        x = side * 5.68
        api.beam(f'CityHall_StairRail_{side}',
                 (x, -10.66, 1.243), (x, -8.60, 1.987), .027)
        for i in (0, 2, 5):
            y = -10.71 + (i + .5) * .36
            step_z = pavement_z + (i + 1) * .13
            rail_z = 1.243 + (y + 10.66) / 2.06 * .744
            api.beam(f'CityHall_StairPost_{side}_{i}',
                     (x, y, step_z), (x, y, rail_z), .022)

    # Accessible switchback: two 9.8 m runs gain 0.39 m apiece, about 1:25.1.
    api.ramp('CityHall_RampLower', 7.5, 17.3, -10.8, -9.4, .14, .53)
    api.box('CityHall_RampEastTurn', (18.15, -9.3, .335),
            (1.7, 3.0, .39), 'Concrete', 'Ramp')
    api.ramp('CityHall_RampUpper', 7.5, 17.3, -9.2, -7.8, .92, .53)
    api.box('CityHall_RampTopLanding', (6.7, -7.5, .53),
            (1.6, 3.4, .78), 'Concrete', 'Ramp')

    # Both long sides are protected; there is no rail across either run end.
    api.ramp_rail('CityHall_LowerOuterRail', 7.5, 17.3, -10.8, .14, .53)
    api.ramp_rail('CityHall_LowerInnerRail', 7.5, 17.3, -9.4, .14, .53)
    api.ramp_rail('CityHall_UpperInnerRail', 7.5, 17.3, -9.2, .92, .53)
    api.ramp_rail('CityHall_UpperOuterRail', 7.5, 17.3, -7.8, .92, .53)
    api.ramp_rail('CityHall_TurnFrontRail', 17.3, 19, -10.8, .53, .53)
    api.ramp_rail('CityHall_TurnRearRail', 17.3, 19, -7.8, .53, .53)
    for lift, radius in ((1.02, .024), (.54, .018)):
        api.beam(f'CityHall_TurnEastRail_{lift}',
                 (19, -10.8, .53 + lift), (19, -7.8, .53 + lift), radius)
    for y in (-10.8, -9.3, -7.8):
        api.beam(f'CityHall_TurnEastPost_{y}',
                 (19, y, .53), (19, y, 1.55), .022)

    # Top landing opens west into the central landing at Y[-8.55,-5.8].
    # Its east edge opens to the upper ramp only over Y[-9.2,-7.8].
    api.ramp_rail('CityHall_TopLandingFrontRail', 5.9, 7.5, -9.2, .92, .92)
    for name, x, ya, yb in (
            ('West', 5.9, -9.2, -8.55),
            ('East', 7.5, -7.8, -5.8)):
        for lift, radius in ((1.02, .024), (.54, .018)):
            api.beam(f'CityHall_TopLanding{name}Rail_{lift}',
                     (x, ya, entry_z + lift), (x, yb, entry_z + lift), radius)
        for y in (ya, (ya + yb) / 2, yb):
            api.beam(f'CityHall_TopLanding{name}Post_{y}',
                     (x, y, entry_z), (x, y, entry_z + 1.02), .022)

    # The west stub of the main landing needs front and side guards; all stair
    # and ramp connection mouths stay open. The rear meets the building.
    api.ramp_rail('CityHall_LandingWestFrontRail', -7.4, -5.8, -8.55, .92, .92)
    for lift, radius in ((1.02, .024), (.54, .018)):
        api.beam(f'CityHall_LandingWestSideRail_{lift}',
                 (-7.4, -8.55, entry_z + lift), (-7.4, -5.8, entry_z + lift), radius)
    for y in (-8.55, -7.175, -5.8):
        api.beam(f'CityHall_LandingWestSidePost_{y}',
                 (-7.4, y, entry_z), (-7.4, y, entry_z + 1.02), .022)

    # Rear service door: 1.7 m door at X=-3.7, Y=14.26, threshold +0.92.
    # Five raised treads plus the ground-level sixth tread give six 130 mm
    # risers. The last 280 mm is existing paving, not a zero-height mesh.
    api.box('CityHall_RearServiceLanding', (-3.7, 14.605, .53),
            (2.2, .83, .78), 'Concrete', 'Steps')
    for i in range(5):
        ya = 15.02 + i * .28
        top = entry_z - (i + 1) * .13
        api.box(f'CityHall_RearServiceStep_{i + 1:02d}',
                (-3.7, ya + .14, (pavement_z + top) / 2),
                (2.2, .28, top - pavement_z), 'Concrete', 'Steps')
    # Sixth tread / bottom approach is clear paving at Y[16.42,16.70].
    # Side rails continue along the landing without blocking either doorway
    # or the bottom approach; every part remains inside the northern site edge.
    for side in (-1, 1):
        x = -3.7 + side * 1.04
        api.beam(f'CityHall_RearLandingRail_{side}',
                 (x, 14.29, 1.94), (x, 15.02, 1.94), .027)
        api.beam(f'CityHall_RearStairRail_{side}',
                 (x, 15.02, 1.94), (x, 16.70, 1.16), .027)
        api.beam(f'CityHall_RearLandingPost_{side}',
                 (x, 14.45, entry_z), (x, 14.45, 1.94), .022)
        for y, surface_z in ((15.16, .79), (15.72, .53), (16.56, .14)):
            rail_z = 1.94 - (y - 15.02) / 1.68 * .78
            api.beam(f'CityHall_RearStairPost_{side}_{y}',
                     (x, y, surface_z), (x, y, rail_z), .022)

    def planter(name, xa, xb, ya, yb):
        """Compose a shallow planted bed from existing library primitives."""
        cx, cy = (xa + xb) / 2, (ya + yb) / 2
        width, depth = xb - xa, yb - ya
        api.box(name + '_Base', (cx, cy, .27), (width, depth, .26),
                'Concrete', 'Landscape')
        api.box(name + '_Soil', (cx, cy, .43), (width - .24, depth - .24, .06),
                'Soil', 'Landscape')
        for x in (xa + .06, xb - .06):
            api.box(name + f'_Side_{x}', (x, cy, .33), (.12, depth, .38),
                    'Concrete', 'Landscape')
        for y in (ya + .06, yb - .06):
            api.box(name + f'_End_{y}', (cx, y, .33), (width - .24, .12, .38),
                    'Concrete', 'Landscape')

    # Low perimeter beds leave a walkway outside the building. Left flagpole
    # positions (-12,-8)/(-10,-8), the sign at (-13,-14), the central axis, and
    # the entire switchback route are deliberately left unobstructed.
    planter('CityHall_WestBorderBed', -20.3, -18.4, -5.6, 13.7)
    planter('CityHall_EastBorderBed', 18.4, 20.3, -5.6, 13.7)
    # Rear planting splits around the 2.2 m service stair plus 450 mm of
    # clearance on each side. No bed rim crosses the exit path.
    planter('CityHall_RearBorderBedWest', -19.3, -5.25, 15.2, 16.45)
    planter('CityHall_RearBorderBedEast', -2.15, 19.3, 15.2, 16.45)
    planter('CityHall_FrontWestBed', -20.3, -17.4, -15.7, -8.8)
    planter('CityHall_WestForecourtBed', -16.4, -8.1, -11.8, -10.7)
    # Sparse shrubs are intentionally absent from ramp edges and turns.
    for i, (x, y) in enumerate((
            (-19.35, -3.0), (-19.35, 4.2), (-19.35, 11.5),
            (19.35, -2.6), (19.35, 4.5), (19.35, 11.5),
            (-15.3, 15.82), (-6.2, 15.82), (5.0, 15.82), (15.3, 15.82),
            (-18.85, -14.7), (-18.85, -10.2),
            (-15.2, -11.25), (-11.9, -11.25), (-9.2, -11.25))):
        api.shrub(f'CityHall_LowShrub_{i:02d}', x, y, .46, .36, False)

    return {
        'site_dimensions_xy_m': [42, 34],
        'paving_top_m': pavement_z,
        'entry_floor_m': entry_z,
        'stair_count': 6,
        'stair_riser_m': .13,
        'stair_tread_m': .36,
        'front_stair_extent_y_m': [-10.71, -8.55],
        'main_landing_extent_y_m': [-8.55, -5.8],
        'ramp_portal_turn_depth_m': 1.43,
        'ramp_run_m': 9.8,
        'ramp_rise_per_run_m': .39,
        'ramp_gradient': .39 / 9.8,
        'ramp_run_width_m': 1.4,
        'ramp_between_handrails_m': 1.352,
        'rear_service_door_width_m': 1.7,
        'rear_service_landing_width_m': 2.2,
        'rear_stair_riser_count': 6,
        'rear_stair_raised_tread_count': 5,
        'rear_stair_tread_m': .28,
        'rear_stair_ground_approach_y_m': [16.42, 16.70],
        'rear_stair_planting_clearance_each_side_m': .45,
        'planting_scope': 'Six low perimeter beds; fifteen sparse shrubs',
    }
