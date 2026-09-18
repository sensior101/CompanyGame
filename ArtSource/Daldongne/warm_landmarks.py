"""Warm Seoul-outskirts landmarks; execute after the original map refinements.

Expects the builder geometry helpers and palette in the calling namespace.
Only landmark geometry is replaced. Existing terrain and walking meshes survive.
All facade bricks are batched by material, and every frontage faces -Y.
"""

def replace_landmarks():
    import math
    import random
    rng = random.Random(4319)

    # Carefully scoped deletion leaves the bridge, stairs and all path cuts intact.
    prefixes = ('Building_CityHall', 'CityHall_', 'Bakery_', 'Cafe_',
                'BusStop_', 'BusStopBench', 'CafeTerrace',
                'Planter_CityHallRoof', 'Soil_CityHallRoof',
                'Shrub_CityHallRoof', 'Flower_CityHallRoof')
    station_keep = ('Station_TunnelDark', 'Station_EntrySidewall',
                    'Station_EntryThreshold', 'Station_SafetyRail')
    for obj in list(bpy.data.objects):
        if (obj.name.startswith(prefixes) or
                (obj.name.startswith('Station_') and not obj.name.startswith(station_keep))):
            bpy.data.objects.remove(obj, do_unlink=True)

    # A geometry-only brick veneer: individual joints, muted variation, few draws.
    def brick_wall(name, a, b, z, height, brick_w=.63, brick_h=.29, depth=.065,
                   holes=()):
        ax,ay = a; bx,by = b
        length=math.hypot(bx-ax,by-ay); ux=(bx-ax)/length; uy=(by-ay)/length
        nx,ny=uy,-ux
        buckets={k:([],[]) for k in ('brick','brickLight','brickDark')}
        rows=max(1,math.ceil(height/brick_h)); bh=height/rows
        for row in range(rows):
            start=-brick_w*.5 if row%2 else 0
            for i in range(math.ceil(length/brick_w)+1):
                lo=max(0,start+i*brick_w); hi=min(length,start+(i+1)*brick_w)
                if hi-lo<.05: continue
                t=(lo+hi)/2; zz=z+(row+.5)*bh
                if any(hi>hx0 and lo<hx1 and zz+bh/2>hz0 and zz-bh/2<hz1
                       for hx0,hx1,hz0,hz1 in holes): continue
                key=rng.choices(('brick','brickLight','brickDark'),(7,2,1))[0]
                verts,faces=buckets[key]; j=len(verts)
                x=ax+ux*t+nx*depth/2; y=ay+uy*t+ny*depth/2
                hw=(hi-lo-.028)/2; hh=(bh-.027)/2
                for dz in (-hh,hh):
                    for du,dn in ((-hw,-depth/2),(hw,-depth/2),(hw,depth/2),(-hw,depth/2)):
                        verts.append((x+ux*du+nx*dn,y+uy*du+ny*dn,zz+dz))
                faces.extend(tuple(j+k for k in f) for f in
                             ((3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)))
        for key,(verts,faces) in buckets.items():
            if verts: mesh(name+'_BrickCourses_'+key,verts,faces,key)

    def facade_window(name,x,y,z,w,h,trim='trim',warm=True):
        box(name+'_reveal',(x,y+.035,z),(w+.22,.15,h+.20),trim)
        box(name+'_glass',(x,y-.052,z),(w,.045,h),'glassClear')
        if warm:
            box(name+'_warmInterior',(x,y-.079,z-h*.18),(w*.87,.018,h*.44),'window')
        for xx in (x-w/2,x,x+w/2):
            box(name+'_vertical',(xx,y-.104,z),(.062,.067,h),trim)
        for zz in (z-h/2,z+h*.08,z+h/2):
            box(name+'_horizontal',(x,y-.108,zz),(w+.08,.07,.063),trim)
        box(name+'_sill',(x,y-.15,z-h/2-.095),(w+.35,.32,.13),'trim')

    def lantern(name,x,y,z):
        box(name+'_wallPlate',(x,y+.06,z+.10),(.20,.12,.48),'metal')
        beam(name+'_arm',(x,y,z+.22),(x,y-.35,z+.22),.065,'metal')
        cyl(name+'_glow',(x,y-.35,z-.08),.14,.39,'window',8)
        cyl(name+'_cap',(x,y-.35,z+.15),.23,.09,'metal',8)
        cyl(name+'_foot',(x,y-.35,z-.31),.19,.07,'metal',8)
        for dx in (-.115,.115):
            beam(name+'_cage',(x+dx,y-.44,z-.30),(x+dx,y-.44,z+.16),.028,'metal')

    def pot(name,x,y,z,s=.62,flowers=False):
        cyl(name+'_terracotta',(x,y,z+.25*s),.36*s,.50*s,'brick',10)
        cyl(name+'_rim',(x,y,z+.50*s),.39*s,.085*s,'brickLight',10)
        cyl(name+'_soil',(x,y,z+.54*s),.32*s,.02,'wood',10)
        for i in range(4):
            a=i*2.4
            ico(name+'_foliage',(x+math.cos(a)*.23*s,y+math.sin(a)*.23*s,z+.81*s),
                (.37*s,.29*s,.45*s),'leaf2' if i%2 else 'leaf3')
            if flowers:
                ico(name+'_flower',(x+math.cos(a)*.27*s,y+math.sin(a)*.27*s,z+1.13*s),.095*s,'white')

    def cafe_chair(name,x,y,z):
        box(name+'_seat',(x,y,z+.48),(.46,.43,.085),'wood')
        for dx in (-.18,.18):
            for dy in (-.16,.16):
                beam(name+'_leg',(x+dx,y+dy,z),(x+dx*.9,y+dy*.9,z+.45),.045,'wood')
        for dx in (-.19,.19):
            beam(name+'_backPost',(x+dx,y+.16,z+.42),(x+dx,y+.16,z+.94),.045,'wood')
        box(name+'_back',(x,y+.16,z+.83),(.45,.06,.21),'trim')

    def cup(name,x,y,z):
        cyl(name+'_saucer',(x,y,z+.012),.13,.025,'white',12)
        cyl(name+'_cup',(x,y,z+.091),.08,.135,'white',12)
        cyl(name+'_coffee',(x,y,z+.162),.060,.008,'wood',12)
        # Square little handle keeps an intentional low-poly silhouette.
        beam(name+'_handle',(x+.078,y,z+.055),(x+.14,y,z+.10),.022,'white')
        beam(name+'_handle',(x+.14,y,z+.10),(x+.078,y,z+.15),.022,'white')

    # CITY HALL: red masonry, white cornices, two wings, four-column portico.
    z=6
    for side,x in (('West',2.8),('East',11.2)):
        box('Building_CityHall_'+side+'_walls',(x,9.65,9.3),(3.3,6.5,6.6),'mortar')
        brick_wall('CityHall_'+side+'_front',(x-1.65,6.4),(x+1.65,6.4),z,6.55)
        if side=='East': brick_wall('CityHall_East_side',(12.85,6.4),(12.85,12.9),z,6.55)
        else: brick_wall('CityHall_West_side',(1.15,12.9),(1.15,6.4),z,6.55)
        box('CityHall_'+side+'_foundation',(x,9.65,6.21),(3.5,6.7,.42),'stone')
        for zz,thick in ((9.35,.16),(12.65,.26),(13.03,.15)):
            box('CityHall_'+side+'_cornice',(x,9.65,zz),(3.65,6.85,thick),'trim')
        box('CityHall_'+side+'_flatRoof',(x,9.65,12.82),(3.36,6.58,.16),'slate')
        for xx in (x-.81,x+.81):
            for wz in (7.65,10.65):
                facade_window('CityHall_'+side+'_window',xx,6.30,wz,.89,1.77)
        # Slim cast-stone corner blocks create an old municipal-building rhythm.
        for i in range(12):
            for xx in (x-1.57,x+1.57):
                box('CityHall_'+side+'_quoin',(xx,6.29,6.45+i*.51),(.30,.18,.22),'plaster')
    box('Building_CityHall_Central_walls',(7,10.02,9.55),(5.1,5.78,7.1),'brick')
    brick_wall('CityHall_Central_front',(4.45,7.10),(9.55,7.10),6,7.05)
    facade_window('CityHall_Entry',7,6.98,7.60,2.2,3.15,trim='trim',warm=True)
    box('CityHall_DoorHandle_L',(6.86,6.80,7.5),(.035,.10,.49),'metal')
    box('CityHall_DoorHandle_R',(7.14,6.80,7.5),(.035,.10,.49),'metal')
    box('CityHall_PorticoFloor',(7,5.95,6.05),(5.75,2.3,.10),'paver_light')
    for i,x in enumerate((4.70,5.95,8.05,9.30)):
        box('CityHall_ColumnBase_'+str(i),(x,5.18,6.18),(.76,.76,.36),'trim')
        cyl('CityHall_ColumnPlinth',(x,5.18,6.49),.31,.26,'plaster',12)
        cyl('CityHall_ColumnShaft',(x,5.18,9.31),.245,5.42,'plaster',12)
        cyl('CityHall_ColumnCapital',(x,5.18,12.10),.35,.23,'trim',12)
        box('CityHall_ColumnAbacus',(x,5.18,12.28),(.77,.77,.19),'trim')
    box('CityHall_PorticoLintel',(7,5.91,12.55),(6.05,2.75,.36),'trim')
    box('CityHall_PorticoFrieze',(7,5.84,12.91),(5.85,2.62,.37),'plaster')
    sign('CityHall_MunicipalSign','시청',7,4.48,12.86,2.7,.60,'slate')
    # Solid triangular pediment and a pitched slate roof, ridge parallel to +Y.
    mesh('CityHall_ClockPediment',[(4,4.48,13.20),(10,4.48,13.20),(7,4.48,15.15),
                                (4,4.69,13.20),(10,4.69,13.20),(7,4.69,15.15)],
         [(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],'plaster')
    mesh('CityHall_SlateGable',[(3.78,4.20,13.17),(10.22,4.20,13.17),(7,4.20,15.28),
                              (3.78,13.10,13.17),(10.22,13.10,13.17),(7,13.10,15.28)],
         [(0,3,5,2),(2,5,4,1),(3,4,5)],'slate')
    for y in (4.20,13.10):
        for x in (3.78,10.22): beam('CityHall_RoofFascia',(x,y,13.17),(7,y,15.28),.14,'trim')
    for yy in (5.45,6.80,8.15,9.5,10.85,12.2):
        for xx in (3.80,10.20): beam('CityHall_SlateSeam',(xx,yy,13.20),(7,yy,15.30),.035,'metal')
    clock=cyl('CityHall_ClockRim',(7,4.345,14.15),.49,.11,'metal',24)
    clock.rotation_euler.x=math.pi/2
    face=cyl('CityHall_ClockFace',(7,4.276,14.15),.42,.025,'white',24)
    face.rotation_euler.x=math.pi/2
    for i in range(12):
        a=i*math.pi/6
        beam('CityHall_ClockTick',(7+math.sin(a)*.33,4.251,14.15+math.cos(a)*.33),
             (7+math.sin(a)*.38,4.251,14.15+math.cos(a)*.38),.025,'metal')
    beam('CityHall_ClockMinute',(7,4.22,14.15),(7.24,4.22,14.32),.045,'metal')
    beam('CityHall_ClockHour',(7,4.215,14.15),(6.93,4.215,14.33),.060,'metal')
    for xx in (2.05,11.95):
        pot('CityHall_EntryPlanter',xx,5.58,6,1.05,True)
        lantern('CityHall_EntryLantern',xx,6.2,9.2)
    # Thin roof aerial, a familiar detail on older Seoul public buildings.
    beam('CityHall_Aerial',(2.55,10.9,13.13),(2.55,10.9,15.5),.045,'metal')
    for h,w in ((14.2,1.7),(14.7,1.35),(15.2,.7)):
        beam('CityHall_AerialCrossarm',(2.55-w/2,10.9,h),(2.55+w/2,10.9,h),.025,'metal')

    # BAKERY: dusty teal, handmade ochre roof tiles, striped olive awning.
    x,y,z=17,-.30,3; w,d,h=6.75,4.6,4.0; front=y-d/2
    box('Bakery_stucco',(x,y,z+h/2),(w,d,h),'teal')
    box('Bakery_BasePlinth',(x,y,z+.16),(w+.10,d+.10,.32),'stone')
    brick_wall('Bakery_LowerFront',(x-w/2,front),(x+w/2,front),z+.29,.65)
    brick_wall('Bakery_EastWall',(x+w/2,front),(x+w/2,y+d/2),z+.28,1.25)
    facade_window('Bakery_BreadWindow',15.65,front-.08,4.70,2.65,2.28,trim='wood')
    facade_window('Bakery_Door',18.65,front-.08,4.43,1.23,2.75,trim='trim')
    box('Bakery_DoorHandle',(19.0,front-.24,4.2),(.04,.07,.32),'metal')
    box('Bakery_DisplayShelf',(15.65,front-.27,4.25),(2.53,.30,.13),'wood')
    for i in range(7):
        bx=14.59+i*.34
        ico('Bakery_BreadLoaf',(bx,front-.35,4.42),(.15,.10,.13),'cream')
        for j in (-1,1):
            beam('Bakery_LoafScore',(bx+j*.045-.025,front-.445,4.43),(bx+j*.045+.025,front-.445,4.49),.014,'wood')
    sign('Bakery_WoodSign','달동네 빵집',17,front-.13,6.60,4.9,.71,'wood')
    for i in range(14):
        xx=x-w*.48+(i+.5)*w*.96/14
        mat='leaf3' if i%2==0 else 'white'
        o=box('Bakery_OliveAwning',(xx,front-.51,5.98),(w*.96/14,.87,.09),mat)
        o.rotation_euler.x=.18
        box('Bakery_ScallopedValance',(xx,front-.925,5.86),(w*.96/14,.07,.24),mat)
    # Mansard apron around an inset flat roof; all tiles are geometry.
    outer=[(x-w/2-.17,y-d/2-.17),(x+w/2+.17,y-d/2-.17),
           (x+w/2+.17,y+d/2+.17),(x-w/2-.17,y+d/2+.17)]
    inner=[(x-w/2+.42,y-d/2+.42),(x+w/2-.42,y-d/2+.42),
           (x+w/2-.42,y+d/2-.42),(x-w/2+.42,y+d/2-.42)]
    for side in range(4):
        a=Vector((*outer[side],7.0)); b=Vector((*outer[(side+1)%4],7.0))
        c=Vector((*inner[side],8.10)); e=Vector((*inner[(side+1)%4],8.10))
        mesh('Bakery_MansardRoof',list(map(tuple,(a,b,e,c))),[(0,1,2,3)],'cream')
        cols=math.ceil((b-a).length/.47)
        for row in range(4):
            t0=row/4; t1=(row+1)/4-.015
            aa=a.lerp(c,t0); bb=b.lerp(e,t0); cc=a.lerp(c,t1); dd=b.lerp(e,t1)
            for col in range(cols):
                u0=col/cols+.009; u1=(col+1)/cols-.009
                pts=[aa.lerp(bb,u0),aa.lerp(bb,u1),cc.lerp(dd,u1),cc.lerp(dd,u0)]
                mesh('Bakery_OchreTile',[tuple(p+Vector((0,0,.028))) for p in pts],[(0,1,2,3)],
                     'cream' if (row+col)%5 else 'brickLight')
        beam('Bakery_RoofTopTrim',c,e,.14,'trim')
    box('Bakery_FlatRoof',(x,y,8.06),(w-1.05,d-1.05,.12),'plaster')
    box('Bakery_Chimney',(19.25,1.0,8.6),(.64,.70,1.12),'mortar')
    brick_wall('Bakery_ChimneyFront',(18.93,.65),(19.57,.65),8.10,1.02,brick_w=.29,brick_h=.18)
    box('Bakery_ChimneyCap',(19.25,1.0,9.2),(.87,.91,.16),'slate')
    lantern('Bakery_LeftLantern',13.91,front-.10,5.08)
    lantern('Bakery_RightLantern',20.08,front-.10,5.08)
    for xx in (13.91,20.02): pot('Bakery_FrontPot',xx,-2.75,z,.61,True)
    # Greenery hugs the facade above head height, never the promenade.
    for i in range(11):
        xx=14.1+i*.57
        ico('Bakery_AwningVine',(xx,front-.16,6.35),(.27,.20,.20),'leaf2' if i%2 else 'leaf3')

    # CAFE: pale plaster, generous glass, coffee shelves, ivy and a tiny deck.
    x,y,z=25,.25,3; w,d,h=5.75,4.15,5.12; front=y-d/2
    box('Cafe_stucco',(x,y,z+h/2),(w,d,h),'plaster')
    box('Cafe_StoneBase',(x,y,z+.28),(w+.1,d+.1,.56),'stone')
    for zz in (6.35,7.83,8.16): box('Cafe_IvoryCornice',(x,y,zz),(w+.24,d+.24,.15),'trim')
    box('Cafe_FlatRoof',(x,y,8.14),(w,d,.12),'slate')
    for xx in (x-w/2,x+w/2):box('Cafe_Parapet',(xx,y,8.43),(.16,d+.17,.50),'plaster')
    for yy in (front,y+d/2):box('Cafe_Parapet',(x,yy,8.43),(w+.15,.16,.50),'plaster')
    facade_window('Cafe_CoffeeWindow',23.8,front-.06,5.15,2.15,3.13,trim='trim',warm=False)
    facade_window('Cafe_GlassDoor',26.05,front-.065,4.42,1.17,2.72,trim='trim',warm=False)
    box('Cafe_DoorHandle',(26.38,front-.235,4.32),(.04,.08,.30),'wood')
    box('Cafe_WindowShelf',(23.8,front-.23,4.03),(2.19,.31,.10),'wood')
    for zz in (4.10,4.92):
        box('Cafe_CoffeeDisplayShelf',(23.8,front-.17,zz),(1.97,.16,.06),'wood')
        for xx in (23.12,23.52,23.92,24.32): cup('Cafe_DisplayCup',xx,front-.22,zz+.04)
    sign('Cafe_Sign','달빛 카페',24.75,front-.11,7.30,3.72,.79,'teal')
    sign('Cafe_MenuTitle','오늘의 커피',26.09,front-.14,6.60,1.49,.40,'slate')
    box('Cafe_MenuBoard',(26.09,front-.13,6.05),(1.49,.10,.82),'wood')
    box('Cafe_MenuSlate',(26.09,front-.193,6.05),(1.36,.04,.69),'slate')
    for j in range(4):
        for col in (0,1):
            box('Cafe_MenuChalk',(25.77+col*.63,front-.22,6.28-j*.15),(.41 if col==0 else .22,.012,.022),'white')
    # Narrow deck is contained within the former parcel, behind the public path.
    for i in range(13):box('Cafe_DeckBoard',(22.26+i*.45,-2.40,3.055),(.43,1.10,.105),'wood')
    cyl('Cafe_PatioTable',(23.30,-2.38,3.70),.40,.07,'trim',16)
    cyl('Cafe_PatioTableLeg',(23.30,-2.38,3.38),.055,.62,'metal',8)
    cup('Cafe_PatioCup',23.16,-2.38,3.74)
    cafe_chair('Cafe_PatioChair',22.55,-2.43,3.10)
    pot('Cafe_DeckFlowers',27.52,-2.54,3.10,.67,True)
    pot('Cafe_EntryPot',25.06,-2.01,3.1,.42)
    # Vines descend from the right cornice, evoking the supplied wisteria reference.
    for i in range(8):
        xx=24.0+i*.50
        ico('Cafe_CorniceIvy',(xx,front-.18,7.98),(.42,.27,.27),'leaf2')
        for k in range(2+(i%3)):
            ico('Cafe_Wisteria',(xx+.06,front-.23,7.67-k*.14),(.087,.075,.15),'purple')
    for i in range(10):
        zz=3.7+i*.37
        ico('Cafe_WallIvy',(27.89,-.84,zz),(.21,.37,.25),'leaf2' if i%2 else 'leaf3')
    lantern('Cafe_PorchLantern',27.27,front-.08,5.97)

    # STATION: a real-world glass-and-steel enclosure around the existing stairs.
    # Mouth [-22.1,-12.9] x [-25.72,-21.4] remains completely open.
    x=-17.5
    for xx in (-22.34,-12.66):
        for yy in (-25.52,-23.36,-21.20):
            box('Station_SteelColumn',(xx,yy,2.73),(.14,.14,3.42),'metal')
        box('Station_LowStoneCheek',(xx,-23.35,1.32),(.24,4.55,.64),'stone')
        box('Station_GlazedSide',(xx,-23.35,2.93),(.045,4.48,2.40),'glassClear')
        for zz in (1.73,2.8,4.14):
            beam('Station_SideMullion',(xx,-25.59,zz),(xx,-21.11,zz),.082,'trim')
    for yy in (-25.59,-21.11):
        beam('Station_Header',(-22.42,yy,4.35),(-12.58,yy,4.35),.18,'metal')
    box('Station_BackGlass',(-17.5,-21.13,2.95),(9.53,.04,2.38),'glassClear')
    for xx in (-20.73,-17.5,-14.27):box('Station_BackMullion',(xx,-21.13,2.95),(.07,.10,2.54),'trim')
    box('Station_RoofSteel',(-17.5,-23.35,4.5),(10.17,4.96,.15),'metal')
    box('Station_RoofSkylight',(-17.5,-23.35,4.595),(7.35,3.42,.045),'glassClear')
    for xx in (-21.23,-18.77,-16.27,-13.77):
        beam('Station_SkylightRib',(xx,-25.10,4.63),(xx,-21.60,4.63),.07,'trim')
    sign('Station_Sign','달동네역',-17.5,-25.67,4.12,4.9,.68,'teal')
    # Thin silver handrails follow the true descending steps at their outer edges.
    for xx in (-21.99,-13.01):
        a=Vector((xx,-25.62,1.06)); b=Vector((xx,-21.68,-1.76))
        beam('Station_DescendingHandrail',a+Vector((0,0,.90)),b+Vector((0,0,.90)),.065,'trim')
        for t in (0,.33,.67,1):
            p=a.lerp(b,t);beam('Station_HandrailPost',p,p+Vector((0,0,.9)),.050,'trim')
    # Line 2 pylon is left of the aperture, never centered in a walking route.
    box('Station_PylonFoot',(-23.32,-25.38,1.065),(.63,.49,.13),'stone')
    box('Station_PylonPole',(-23.32,-25.38,2.11),(.13,.13,2.11),'trim')
    box('Station_Line2Pylon',(-23.32,-25.38,3.55),(.65,.32,2.64),'teal')
    sign('Station_Line2Header','2',-23.32,-25.57,4.42,.53,.62,'yellow')
    for i,ch in enumerate('달동네역'):
        lettering('Station_PylonHangul',ch,-23.32,-25.565,3.86-i*.35,.37,.29)
    token=cyl('Station_Line2',(-14.08,-25.82,4.13),.26,.05,'green',20)
    token.rotation_euler.x=math.pi/2
    lettering('Station_Line2Digit','2',-14.08,-25.86,4.13,.25,.33)
    # Tactile studs are low relief on the existing yellow threshold.
    for i in range(35):
        for yy in (-26.055,-25.91):
            cyl('Station_TactileDot',(-21.72+i*.25,yy,1.115),.035,.017,'cream',6)

    # BUS SHELTER: slender dark frame, translucent teal panels, timber bench.
    for xx in (18.65,22.82,26.95):
        box('BusStop_SteelPost',(xx,-16.67,2.46),(.12,.13,2.92),'metal')
    for xx in (18.65,26.95):box('BusStop_FrontPost',(xx,-14.50,2.40),(.10,.10,2.80),'metal')
    box('BusStop_BackGlass',(22.8,-16.70,2.53),(8.23,.045,2.62),'glassClear')
    for zz in (1.35,2.33,3.52):box('BusStop_GlassRail',(22.8,-16.74,zz),(8.39,.07,.064),'metal')
    for xx in (18.65,26.95):box('BusStop_SideGlass',(xx,-15.62,2.40),(.035,2.08,2.37),'glassClear')
    roof=box('BusStop_SlenderRoof',(22.8,-15.60,3.98),(8.99,2.70,.13),'metal');roof.rotation_euler.x=-.055
    glazing=box('BusStop_RoofGlass',(22.8,-15.57,4.055),(8.51,2.19,.037),'glassClear');glazing.rotation_euler.x=-.055
    for xx in (18.75,20.75,22.80,24.85,26.85):
        beam('BusStop_RoofRib',(xx,-16.9,4.12),(xx,-14.27,3.98),.063,'trim')
    sign('BusStop_Sign','달동네 버스정류장',22.8,-16.88,3.67,6.3,.46,'metal')
    box('BusStop_RouteCase',(19.30,-16.79,2.40),(1.02,.14,1.78),'metal')
    box('BusStop_RoutePanel',(19.30,-16.88,2.40),(.84,.025,1.57),'plaster')
    lettering('BusStop_RouteNumber','701',19.30,-16.91,2.94,.67,.30,'teal')
    for i in range(6):
        yy=-16.917; zz=2.64-i*.175
        box('BusStop_TimetableLine',(19.34,yy,zz),(.49,.012,.025),'teal')
        ico('BusStop_RouteNode',(19.0,yy,zz),(.035,.012,.035),'green')
    for i in range(4):box('BusStop_BenchSlat',(23.15,-16.15+i*.12,1.54),(4.65,.095,.085),'wood')
    for xx in (21.3,23.15,25.0):
        beam('BusStop_BenchLeg',(xx,-15.98,1.02),(xx,-15.98,1.5),.10,'metal')
    for zz in (1.88,2.10):box('BusStop_BenchBack',(23.15,-16.39,zz),(4.65,.085,.16),'wood')
    box('BusStop_WarmLight',(23,-16.54,3.75),(4.65,.06,.045),'window')

    # Batch tile geometry too, while keeping the editable architectural parts.
    for prefix in ('Bakery_OchreTile','Bakery_MansardRoof'):
        groups={}
        for obj in list(bpy.context.scene.objects):
            if obj.type=='MESH' and obj.name.startswith(prefix):
                groups.setdefault(obj.data.materials[0].name,[]).append(obj)
        for key,objects in groups.items():
            if len(objects)<2: continue
            verts=[];faces=[]
            for obj in objects:
                offset=len(verts)
                verts.extend(tuple(obj.matrix_world@v.co) for v in obj.data.vertices)
                faces.extend(tuple(offset+i for i in p.vertices) for p in obj.data.polygons)
            mat=objects[0].data.materials[0]
            for obj in objects:bpy.data.objects.remove(obj,do_unlink=True)
            mesh(prefix+'_Combined_'+key,verts,faces,mat)

    bpy.context.scene['WarmLandmarksRevision']='Seoul brick city hall, teal bakery, ivy cafe, glazed subway and local bus shelter'
