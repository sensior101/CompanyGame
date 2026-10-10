"""City hall signage and two modeled flags; no scene or export ownership.

The caller supplies the existing Handae mesh batch helpers, CivicLibrary pole
helpers and the shared front-facing Korean text function. All coordinates use
metres, Z up, and the street faces -Y. Flag graphics are actual curved geometry.
"""

import math


def build(api):
    stone = api.MeshBatch()
    api.box(stone, (-13, -13.9, .94), (5.4, .6, 1.6))
    api.finish('CityHall_StoneMonumentSign', 'Signage', stone, ['Stone'])

    # Measured using the shared NotoSansKR-Regular.ttf curve -> mesh path.
    # Its glyph heights are about 0.319 x curve.size, so size is not metre height.
    # These values yield visible heights .85/.18/.42/.14 m at unchanged locations.
    api.text('CityHall_PortalTitle', '시청', (.6, -7.14, 14.25), 2.667699,
             material='SignInk', align='CENTER')
    api.text('CityHall_PortalMotto', '시민과 함께 만드는 도시', (0, -7.15, 13.65), .563070,
             material='SignInk', align='CENTER')
    api.text('CityHall_MonumentTitle', '시청', (-12.45, -14.22, 1.10), 1.318153,
             material='SignInk', align='CENTER')
    api.text('CityHall_MonumentMotto', '시민과 함께', (-12.45, -14.22, .65), .437941,
             material='SignInk', align='CENTER')

    # One restrained three-leaf civic symbol is reused on both fixed signs and
    # the city flag. It introduces no place name or real municipality identity.
    def leaf_outline(base_x, base_z, length, width, angle_degrees):
        angle = math.radians(angle_degrees)
        coords = [(0.0, 0.0)]
        for i in range(1, 12):
            t = i / 12
            coords.append((-width * .5 * math.sin(math.pi * t) ** .85,
                           length * t))
        coords.append((0.0, length))
        for i in range(11, 0, -1):
            t = i / 12
            coords.append((width * .5 * math.sin(math.pi * t) ** .85,
                           length * t))
        return [(base_x + math.cos(angle) * lateral + math.sin(angle) * up,
                 base_z - math.sin(angle) * lateral + math.cos(angle) * up)
                for lateral, up in coords]

    def raised_leaf(batch, base_x, base_z, y, length, width, angle):
        outline = leaf_outline(base_x, base_z, length, width, angle)
        front = [(x, y - .016, z) for x, z in outline]
        back = [(x, y + .016, z) for x, z in outline]
        batch.face(front)
        batch.face(list(reversed(back)))
        for i in range(len(outline)):
            j = (i + 1) % len(outline)
            batch.face([front[i], back[i], back[j], front[j]])

    emblems = api.MeshBatch()
    for x, z, y, scale in [(-1.72, 14.13, -7.175, 1.0),
                            (-14.55, .94, -14.245, .68)]:
        for angle, length in [(-38, .76), (1, .98), (39, .79)]:
            raised_leaf(emblems, x, z, y, length * scale, .29 * scale, angle)
    api.finish('CityHall_RaisedCivicLeafEmblems', 'Signage', emblems, ['SignInk'])

    pole_bases = api.MeshBatch()
    for i, x in enumerate((-14.8, -11.8)):
        prefix = 'CityHall_KoreanFlag' if i == 0 else 'CityHall_CivicFlag'
        api.box(pole_bases, (x, -8.4, .20), (.32, .32, .12))
        api.beam(prefix + '_Pole', (x, -8.4, .22), (x, -8.4, 9.74), .045,
                 matname='Frame', group='Signage', verts=16)
        api.ico(prefix + '_Finial', (x, -8.4, 9.80), (.075, .075, .075),
                'Frame', group='Signage', sub=2)
        api.beam(prefix + '_Halyard', (x - .068, -8.405, .65),
                 (x - .068, -8.405, 9.56), .008,
                 matname='White', group='Signage', verts=6)
    api.finish('CityHall_FlagpoleFootplates', 'Signage', pole_bases, ['Frame'])

    materials = ['White', 'FlagRed', 'FlagBlue', 'SignInk', 'CivicBlue', 'CivicGreen']

    def make_flag(prefix, pole_x, civic=False):
        width, height = 2.1, 1.4
        bottom = 7.9
        batch = api.MeshBatch()

        def point(u, v, offset=0.0):
            # Almost straight beside the halyard, with a slight wave at its free
            # edge. Subdivided colored patches follow this same surface.
            wave = .065 * (u / width) * math.sin(u * 3.6 - v * 1.4)
            return (pole_x + .055 + u, -8.4 + wave + offset, bottom + v)

        def face_uv(coords, material, side, offset):
            points = [point(u, v, side * offset) for u, v in coords]
            # Circle/leaf end strips can collapse one edge into a triangle.
            distinct = []
            for p in points:
                if not distinct or sum((p[i] - distinct[-1][i]) ** 2 for i in range(3)) > 1e-16:
                    distinct.append(p)
            if len(distinct) > 2 and sum((distinct[0][i] - distinct[-1][i]) ** 2
                                         for i in range(3)) < 1e-16:
                distinct.pop()
            if len(distinct) < 3:
                return
            if side > 0:
                distinct.reverse()
            batch.face(distinct, material)

        # A closed 12 mm cloth shell makes both viewing directions explicit.
        nx, nz = 32, 18
        for i in range(nx):
            ua, ub = width * i / nx, width * (i + 1) / nx
            for j in range(nz):
                va, vb = height * j / nz, height * (j + 1) / nz
                quad = [(ua, va), (ub, va), (ub, vb), (ua, vb)]
                for side in (-1, 1):
                    face_uv(quad, 0, side, .006)
        perimeter = []
        perimeter += [(width * i / nx, 0) for i in range(nx)]
        perimeter += [(width, height * j / nz) for j in range(nz)]
        perimeter += [(width - width * i / nx, height) for i in range(nx)]
        perimeter += [(0, height - height * j / nz) for j in range(nz)]
        for i, a in enumerate(perimeter):
            b = perimeter[(i + 1) % len(perimeter)]
            batch.face([point(*a, -.006), point(*a, .006),
                        point(*b, .006), point(*b, -.006)], 0)

        def graphic_quad(coords, material):
            # Printing is represented by a shallow colored mesh on each side,
            # slightly above the white cloth so export needs no alpha textures.
            for side in (-1, 1):
                face_uv(coords, material, side, .014)

        def rectangle(cx, cz, length, thickness, angle_degrees, material):
            angle = math.radians(angle_degrees)
            count = max(1, math.ceil(length / .055))
            for i in range(count):
                a, b = -length / 2 + length * i / count, -length / 2 + length * (i + 1) / count
                local = [(a, -thickness / 2), (b, -thickness / 2),
                         (b, thickness / 2), (a, thickness / 2)]
                graphic_quad([(cx + math.cos(angle) * u - math.sin(angle) * v,
                               cz + math.sin(angle) * u + math.cos(angle) * v)
                              for u, v in local], material)

        if not civic:
            # Red upper / blue lower Taegeuk, with complementary S-shaped lobes.
            # Vertical narrow strips follow the wave instead of spanning it with
            # a flat, floating disk or an externally generated picture.
            radius, cx, cz = .35, width / 2, height / 2

            def outline_z(x):
                return math.sqrt(max(0.0, radius * radius - x * x))

            def division_z(x):
                if x >= 0:
                    return -math.sqrt(max(0.0, (radius / 2) ** 2 - (x - radius / 2) ** 2))
                return math.sqrt(max(0.0, (radius / 2) ** 2 - (x + radius / 2) ** 2))

            for i in range(48):
                a, b = -radius + 2 * radius * i / 48, -radius + 2 * radius * (i + 1) / 48
                za, zb = outline_z(a), outline_z(b)
                sa, sb = division_z(a), division_z(b)
                for lower_a, lower_b, upper_a, upper_b, material in [
                    (sa, sb, za, zb, 1), (-za, -zb, sa, sb, 2),
                ]:
                    count = max(1, math.ceil(max(upper_a - lower_a, upper_b - lower_b) / .055))
                    for j in range(count):
                        p, q = j / count, (j + 1) / count
                        graphic_quad([
                            (cx + a, cz + lower_a + (upper_a - lower_a) * p),
                            (cx + b, cz + lower_b + (upper_b - lower_b) * p),
                            (cx + b, cz + lower_b + (upper_b - lower_b) * q),
                            (cx + a, cz + lower_a + (upper_a - lower_a) * q),
                        ], material)

            # Geon NW: solid/solid/solid; Gam NE: broken/solid/broken;
            # Ri SW: solid/broken/solid; Gon SE: broken/broken/broken.
            trigrams = [(-.69, .44, 55, (False, False, False)),
                        (.69, .44, -55, (True, False, True)),
                        (-.69, -.44, -55, (False, True, False)),
                        (.69, -.44, 55, (True, True, True))]
            for dx, dz, angle, broken_rows in trigrams:
                theta = math.radians(angle)
                for row, broken in enumerate(broken_rows):
                    shift = (row - 1) * .079
                    bx = cx + dx - math.sin(theta) * shift
                    bz = cz + dz + math.cos(theta) * shift
                    if broken:
                        for direction in (-1, 1):
                            offset = direction * .0975
                            rectangle(bx + math.cos(theta) * offset,
                                      bz + math.sin(theta) * offset,
                                      .125, .052, angle, 3)
                    else:
                        rectangle(bx, bz, .32, .052, angle, 3)
        else:
            # The civic flag reuses the same open three-leaf fan, in blue/green.
            for angle, length, material in [(-38, .66, 4), (1, .86, 4), (39, .69, 5)]:
                theta = math.radians(angle)
                base_u, base_v = 1.02, .29
                leaf_width = .30
                for i in range(24):
                    ta, tb = i / 24, (i + 1) / 24
                    wa = leaf_width * .5 * math.sin(math.pi * ta) ** .85
                    wb = leaf_width * .5 * math.sin(math.pi * tb) ** .85
                    local = [(-wa, ta * length), (wa, ta * length),
                             (wb, tb * length), (-wb, tb * length)]
                    graphic_quad([(base_u + math.cos(theta) * lateral + math.sin(theta) * up,
                                   base_v - math.sin(theta) * lateral + math.cos(theta) * up)
                                  for lateral, up in local], material)

        api.finish(prefix + '_WavingClothAndSymbols', 'Signage', batch, materials)

    make_flag('CityHall_KoreanFlag', -14.8)
    make_flag('CityHall_CivicFlag', -11.8, civic=True)

    return {
        'features': ['Front portal city hall title and citizen motto',
                     'Front-left stone monument sign with repeated civic leaf symbol',
                     'Two flagpoles with native mesh Korean and civic flags'],
        'pole_positions_xy_m': [[-14.8, -8.4], [-11.8, -8.4]],
        'flag_size_m': [2.1, 1.4],
        'flag_graphics': 'Colored geometry on both sides; no raster image dependency',
    }
