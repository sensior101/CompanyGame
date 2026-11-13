"""Additional commercial furniture: geometry only, metres, front -Y, up Z.

Reuses HouseholdLowPoly's closed geometry primitives. The caller initializes h,
materials, prepares normals/UVs, corrects mounting origins and saves each asset.
"""
from math import pi, sin, cos

h = None
ENTRIES = []


def configure(household):
    global h
    h = household


def item(identifier, label, category, placement='Floor'):
    def register(fn):
        ENTRIES.append(dict(id=identifier, label=label, category=category,
                            placement=placement, function='Decoration', build=fn))
        return fn
    return register


def image_panel(name, width, height, y, z):
    """Intentionally single-sided + explicit full-image UV, looking toward -Y."""
    obj = h.mesh(name, [(-width/2,y,z-height/2),(width/2,y,z-height/2),
                        (width/2,y,z+height/2),(-width/2,y,z+height/2)],
                 [(0,1,2,3)], 'White')
    uv = obj.data.uv_layers.new(name='ImageUV')
    for loop, xy in zip(uv.data, ((0,0),(1,0),(1,1),(0,1))):
        loop.uv = xy
    material = h.MATS['Cream']['White'].copy()
    material.name = name + '_ReplaceableWhite'
    material.diffuse_color = (1,1,1,1)
    material.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (1,1,1,1)
    obj.data.materials.clear()
    obj.data.materials.append(material)
    obj['ImageSurface'] = True
    obj['ReplaceImageHere'] = True
    return obj


def wheels(width, depth, z=.065):
    for x in (-width/2,width/2):
        for y in (-depth/2,depth/2):
            h.cyl('RubberCaster',.055,.032,(x,y,z),'Rubber',12,(0,pi/2,0))
            h.cyl('CasterHub',.020,.036,(x,y,z),'Metal',10,(0,pi/2,0))
            h.box('CasterFork',(.045,.025,.065),(x,y,z+.043),'Metal',.004)


@item('UmbrellaStand','우산꽂이','Fixtures')
def umbrella_stand():
    h.rounded_loft('OakDripTray',[(0,.32,.26,.035),(.045,.32,.26,.035)],'Wood')
    h.box('DripTrayInset',(.27,.21,.012),(0,0,.048),'Rubber',.009)
    for x in (-.145,.145):
        for y in (-.115,.115):
            h.box('CornerPost',(.026,.026,.48),(x,y,.27),'Body',.005)
    for z in (.13,.46):
        for y in (-.115,.115):
            h.box('HorizontalRail',(.30,.022,.035),(0,y,z),'Body',.005)
        for x in (-.145,.145):
            h.box('SideRail',(.022,.23,.035),(x,0,z),'Body',.005)
    for x in (-.09,-.03,.03,.09):
        for y in (-.115,.115):
            h.box('VerticalSlat',(.018,.016,.31),(x,y,.295),'Body',.003)
    h.rod('OpeningDivider',(-.145,0,.46),(.145,0,.46),.009,'Metal')
    h.root['Construction']='Open top slatted rack with recessed removable drip tray'


@item('WallMenuBoard','벽걸이 메뉴판','Fixtures','Wall')
def wall_menu():
    h.box('MenuBacking',(.75,.035,.93),(0,0,.465),'Wood',.006)
    for x in (-.3575,.3575):
        h.box('OakSide',(.035,.045,.93),(x,-.020,.465),'Wood',.004)
    for z in (.0175,.9125):
        h.box('OakRail',(.68,.045,.035),(0,-.020,z),'Wood',.004)
    image_panel('MenuImageSurface',.68,.86,-.044,.465)
    for x in (-.25,.25):
        h.box('RearWallMount',(.055,.016,.075),(x,.025,.79),'Metal',.003)
    h.root['Mounting']='Rear wall plane Y=0 after caller alignment; image faces -Y'


@item('AFrameSign','입간판','Fixtures')
def a_frame():
    # Rigid front placard leans back; rear frame spreads to form a true A silhouette.
    angle=-.16
    parts_start=len(h.PARTS)
    h.box('FrontBacking',(.58,.028,.78),(0,0,.55),'Wood',.005)
    for x in (-.29,.29):
        h.box('FrontLeg',(.045,.05,1.02),(x,0,.51),'Wood',.005)
    for z in (.18,.92):
        h.box('FrontRail',(.62,.05,.04),(0,0,z),'Wood',.004)
    image_panel('SignImageSurface',.535,.69,-.027,.55)
    from mathutils import Matrix, Vector
    tilt=Matrix.Rotation(angle,3,'X')
    for obj in h.PARTS[parts_start:]:
        obj.location=tilt@obj.location+Vector((0,-.20,.012))
        obj.rotation_euler=(tilt@obj.rotation_euler.to_matrix()).to_euler()
    for x in (-.29,.29):
        h.rod('RearLeg',(x,.27,.015),(x,-.045,1.01),.024,'Wood',4)
        h.cyl('TopHinge',.025,.052,(x,-.045,.98),'Metal',12,(0,pi/2,0))
        h.rod('SpreadLimiter',(x,-.13,.42),(x,.14,.42),.007,'Metal',6)
    h.rod('RearBrace',(-.29,.21,.20),(.29,.21,.20),.018,'Wood',4)


@item('WaterDispenser','정수기','Appliances')
def water_dispenser():
    h.box('Plinth',(.32,.33,.055),(0,0,.0275),'Rubber')
    h.box('LowerCabinet',(.34,.34,.62),(0,0,.36),'Body',.025)
    for x in (-.15,.15):
        h.box('RecessSide',(.04,.34,.24),(x,0,.79),'Body',.007)
    h.box('RecessBack',(.27,.045,.24),(0,.145,.79),'Rubber',.003)
    h.box('UpperHeader',(.34,.34,.11),(0,0,.965),'Body',.018)
    h.box('DripTray',(.27,.27,.025),(0,-.055,.665),'Metal',.004)
    for x in (-.10,-.05,0,.05,.10):
        h.box('TraySlot',(.012,.19,.003),(x,-.065,.679),'Rubber',.001)
    for x,role in ((-.072,'Red'),(.072,'Body')):
        h.rod('TapSpout',(x,.06,.90),(x,-.08,.90),.014,'Metal')
        h.rod('TapNozzle',(x,-.08,.90),(x,-.08,.86),.014,'Metal')
        h.box('TapLever',(.035,.065,.018),(x,-.03,.928),role,.004)
    h.cyl('BottleSocket',.10,.04,(0,0,1.04),'Rubber',16)
    h.lathe('RefillBottle',[(0,1.06),(.055,1.06),(.055,1.10),(.13,1.18),
                           (.145,1.23),(.145,1.47),(.12,1.51),(0,1.51)],role='Glass',n=16)
    for z in (1.25,1.41):h.ring('BottleRib',.145,.008,(0,0,z),'Glass',n=16)


@item('PedalTrashBin','페달 휴지통','Fixtures')
def pedal_bin():
    h.lathe('BinBody',[(0,0),(.15,0),(.165,.04),(.17,.46),(.16,.475),
                       (.148,.475),(.145,.05),(0,.05)],role='Body',n=20)
    h.cyl('ClosedLid',.177,.027,(0,0,.49),'Metal',20)
    h.ring('LidLip',.168,.011,(0,0,.48),'Body',n=20)
    h.box('Pedal',(.12,.12,.026),(0,-.181,.038),'Rubber',.009)
    h.box('PedalLink',(.035,.035,.41),(0,.16,.235),'Metal',.004)
    h.cyl('LidHinge',.026,.11,(0,.15,.478),'Metal',12,(0,pi/2,0))


@item('RoundGroupTable','원형 단체 테이블','Tables')
def round_table():
    h.cyl('OakRoundTop',.70,.045,(0,0,.7175),'Wood',48)
    h.cyl('UndertopPlate',.26,.03,(0,0,.68),'Metal',20)
    h.cyl('Pedestal',.075,.60,(0,0,.365),'Body',16,r2=.065)
    h.lathe('WeightedBase',[(0,0),(.39,0),(.40,.025),(.35,.055),(.09,.08),(0,.08)],role='Body',n=24)
    h.root['SurfaceHeightMetres']=.74


@item('LowFloorTable','좌식 테이블','Tables')
def low_table():
    h.rounded_loft('OakTop',[(.28,1.10,.70,.10),(.32,1.10,.70,.10)],'Wood')
    for x in (-.43,.43):
        h.box('Trestle',(.055,.54,.26),(x,0,.14),'Body',.012)
        h.box('OakFoot',(.12,.60,.035),(x,0,.0175),'Wood',.008)
    h.box('Stretcher',(.86,.04,.065),(0,0,.18),'Wood',.008)
    h.root['SurfaceHeightMetres']=.32


@item('HangingTableNumber','천장형 테이블 번호판','Fixtures','Ceiling')
def table_number():
    h.box('OakNumberPlaque',(.32,.038,.21),(0,0,.105),'Wood',.012)
    h.box('WhiteNumberFace',(.29,.008,.18),(0,-.023,.105),'White',.008)
    for x in (-.105,.105):
        h.rod('SuspensionWire',(x,0,.21),(x,0,.61),.003,'Metal',6)
        h.cyl('CeilingFixing',.022,.016,(x,0,.618),'Metal',12)
    # Closed, modeled seven-segment glyphs avoid open converted-font outlines.
    for cx,segments in ((-.06,'abcdef'),(.06,'bc')):
        positions={'a':(0,.065,False),'b':(.033,.033,True),'c':(.033,-.033,True),
                   'd':(0,-.065,False),'e':(-.033,-.033,True),'f':(-.033,.033,True)}
        for seg in segments:
            x,z,vertical=positions[seg]
            h.box('Digit01_'+seg,(.011 if vertical else .06,.006,.055 if vertical else .011),
                  (cx+x,-.031,.105+z),'Rubber',.002)
    h.root['NumberText']='01'
    h.root['Mounting']='Ceiling top Z=0 after caller alignment; plaque hangs downward'


@item('CommercialSink','업소용 싱크대','Kitchen')
def commercial_sink():
    h.legs(1.02,.52,.87,'Metal',.024)
    h.box('LowerRack',(1.04,.52,.025),(0,0,.22),'Metal',.005)
    # Counter split around a real opening; no hidden slab across the basin.
    h.basin('OpenStainlessBowl',.61,.48,.23,.90,'Metal',x=-.22,y=-.025)
    h.box('Drainboard',(.40,.68,.035),(.36,0,.8825),'Metal',.008)
    h.box('LeftRim',(.055,.68,.035),(-.5725,0,.8825),'Metal',.005)
    for y,d in ((-.303,.074),(.2725,.135)):
        h.box('BasinCounterRail',(.65,d,.035),(-.22,y,.8825),'Metal',.004)
    for x in (.22,.29,.36,.43,.50):
        h.box('DrainboardRib',(.010,.48,.006),(x,-.015,.903),'Metal',.002)
    h.box('Splashback',(1.20,.025,.16),(0,.3275,.965),'Metal',.005)
    h.faucet(x=-.22,y=.245,z=.90)
    h.rod('WastePipe',(-.22,-.025,.65),(-.22,-.025,.35),.024,'Metal')
    h.root['BasinOpeningMetres']=[.56,.43]


@item('CommercialFridge','업소용 냉장고','Kitchen')
def commercial_fridge():
    h.legs(.72,.61,.12,'Metal',.028)
    h.box('RefrigeratorCase',(.85,.75,1.78),(0,0,1.01),'Body',.015)
    h.box('BottomVent',(.77,.025,.15),(0,-.383,.215),'Metal',.003)
    for z in (.17,.20,.23,.26):
        h.box('VentSlot',(.68,.006,.009),(0,-.399,z),'Rubber',.001)
    for x in (-.205,.205):
        h.box('DoorGasket',(.405,.025,1.40),(x,-.386,1.025),'Rubber',.004)
        h.box('TallDoor',(.387,.035,1.38),(x,-.413,1.025),'White',.006)
        h.handle('VerticalFridgePull',x+(.14 if x<0 else -.14),-.473,1.12,.44,True)
    h.box('Header',( .82,.04,.14),(0,-.39,1.81),'Metal',.004)
    h.box('TemperatureDisplay',(.12,.008,.045),(.23,-.415,1.81),'Screen',.002)
    for x in (-.36,.36):
        for z in (.53,1.51):h.box('DoorHinge',(.025,.045,.07),(x,-.43,z),'Metal',.003)


@item('CommercialFreezer','업소용 상부개방 냉동고','Kitchen')
def commercial_freezer():
    h.legs(1.05,.55,.065,'Rubber',.026)
    h.box('ChestBody',(1.20,.70,.75),(0,0,.44),'Body',.025)
    h.box('LidGasket',(1.19,.69,.022),(0,0,.824),'Rubber',.004)
    h.box('TopOpeningLid',(1.23,.73,.055),(0,0,.8625),'White',.014)
    h.handle('ChestFrontPull',0,-.40,.805,.30)
    for x in (-.40,.40):h.box('RearLidHinge',(.11,.06,.08),(x,.35,.817),'Metal',.008)
    h.box('CompressorVent',(.25,.018,.19),(.40,-.359,.22),'Metal',.004)
    for z in (.16,.19,.22,.25,.28):h.box('CoolingSlot',(.21,.006,.008),(.40,-.371,z),'Rubber',.001)
    h.box('Thermostat',(.10,.008,.055),(-.43,-.361,.28),'Screen',.003)
    h.cyl('PowerIndicator',.006,.004,(-.49,-.368,.23),'Red',10,(pi/2,0,0))
    h.root['Construction']='Chest-style top hinged lid; wide low silhouette'


@item('ServingCart','서빙 카트','Kitchen')
def serving_cart():
    wheels(.65,.40)
    for x in (-.325,.325):
        for y in (-.20,.20):h.rod('TubularPost',(x,y,.12),(x,y,.91),.014,'Metal')
    for z in (.23,.54,.85):
        h.box('OakTray',(.69,.46,.028),(0,0,z),'Wood',.008)
        for y in (-.222,.222):h.box('TrayRim',(.69,.016,.043),(0,y,z+.025),'Body',.004)
        for x in (-.337,.337):h.box('TrayEnd',(.016,.43,.043),(x,0,z+.025),'Body',.004)
    h.rod('PushHandle',(.325,-.20,.94),(.325,.20,.94),.019,'Metal')
    h.rod('HandleUpright',(.325,-.20,.85),(.325,-.20,.94),.014,'Metal')
    h.rod('HandleUpright',(.325,.20,.85),(.325,.20,.94),.014,'Metal')


@item('RedPlasticChair','빨간 플라스틱 의자','Seating')
def red_chair():
    # Tapered flat-section legs and perforated curved back evoke molded monobloc plastic.
    h.rounded_loft('MouldedSeat',[(.415,.43,.43,.065),(.45,.47,.46,.075)],'Red')
    for sx in (-1,1):
        for sy in (-1,1):
            o=h.rounded_loft('SplayedMouldedLeg',[(0,.055,.060,.010),(.40,.045,.055,.009)],'Red')
            for v in o.data.vertices:
                t=v.co.z/.40
                v.co.x+=sx*(.235-.055*t)
                v.co.y+=sy*(.225-.055*t)
        h.rod('BackSide',(sx*.205,.17,.43),(sx*.215,.245,.85),.028,'Red',8)
        h.rod('ArmUpright',(sx*.225,-.13,.44),(sx*.25,-.10,.65),.020,'Red',8)
        h.rod('MouldedArm',(sx*.25,-.12,.65),(sx*.23,.22,.68),.026,'Red',8)
    h.box('BackLowerRail',(.44,.04,.065),(0,.205,.51),'Red',.014,(-.16,0,0))
    h.rounded_loft('BackCrown',[(.80,.45,.05,.015),(.87,.43,.055,.015)],'Red',loc=(0,.245,0))
    for x in (-.14,-.07,0,.07,.14):
        h.box('OpenBackSlat',(.043,.027,.29),(x,.226,.665),'Red',.009,(-.15,0,0))
    h.root['Construction']='Moulded red monobloc chair: splayed legs, arms and five open back slats'


@item('TerraceTable','테라스 테이블','Tables')
def terrace_table():
    for i in range(9):
        h.box('OakOutdoorSlat',(.078,.80,.033),(-.352+i*.088,0,.7235),'Wood',.005)
    for x in (-.29,.29):
        h.box('UnderTopRail',(.035,.71,.045),(x,0,.685),'Metal',.005)
        h.rod('CrossLeg',(x,-.33,.018),(x,.30,.67),.022,'Body')
        h.rod('CrossLeg',(x,.33,.018),(x,-.30,.67),.022,'Body')
        h.cyl('FoldingPivot',.029,.055,(x,0,.34),'Metal',12,(0,pi/2,0))
    h.rod('CrossBrace',(-.29,.21,.15),(.29,.21,.15),.018,'Metal')
    h.root['SurfaceHeightMetres']=.74


@item('PatioParasol','테라스 파라솔','Outdoor')
def parasol():
    h.cyl('WeightedStand',.30,.065,(0,0,.0325),'Body',24)
    h.cyl('StandSocket',.05,.19,(0,0,.15),'Metal',16)
    h.cyl('OakPole',.027,2.38,(0,0,1.25),'Wood',12)
    h.lathe('ClosedFabricCanopy',[(0,2.58),(.12,2.56),(.65,2.39),(1.20,2.17),
                                 (1.20,2.15),(.65,2.37),(.12,2.54),(0,2.56)],role='Fabric',n=8)
    for i in range(8):
        a=i*pi/4
        h.rod('CanopyRib',(.035*cos(a),.035*sin(a),2.49),
              (1.18*cos(a),1.18*sin(a),2.15),.008,'Wood',6)
        h.rod('Spreader',(.025*cos(a),.025*sin(a),1.97),
              (.54*cos(a),.54*sin(a),2.36),.008,'Metal',6)
    h.cyl('SlidingHub',.046,.085,(0,0,1.98),'Metal',12)
    h.cyl('TopFinial',.035,.06,(0,0,2.60),'Wood',12,r2=.022)
    h.root['CanopyDiameterMetres']=2.40


@item('CommercialOven','업소용 오븐','Kitchen')
def commercial_oven():
    h.legs(.68,.60,.16,'Metal',.028)
    h.box('OvenBody',(.82,.75,.80),(0,0,.56),'Body',.015)
    h.box('StainlessTop',(.85,.78,.035),(0,0,.9775),'Metal',.009)
    h.box('DoorGasket',(.72,.024,.54),(0,-.385,.48),'Rubber',.005)
    h.box('StainlessDoor',(.69,.035,.51),(0,-.412,.48),'Metal',.012)
    h.box('WindowSeal',(.53,.010,.33),(0,-.435,.46),'Rubber',.009)
    h.box('DarkOvenWindow',(.49,.008,.29),(0,-.444,.46),'Screen',.008)
    h.handle('WideOvenPull',0,-.49,.70,.52)
    h.box('ControlFascia',(.76,.025,.12),(0,-.39,.85),'Metal',.004)
    for x in (-.27,-.16,.22):
        h.cyl('Dial',.025,.025,(x,-.417,.85),'Rubber',16,(pi/2,0,0))
        h.box('DialPointer',(.004,.004,.012),(x,-.432,.86),'White',.001)
    h.box('TimerDisplay',(.14,.008,.045),(.045,-.407,.85),'Screen',.002)
    for x in (-.29,-.19,-.09,.01,.11,.21,.31):
        h.box('BottomVent',(.055,.006,.012),(x,-.389,.22),'Rubber',.001)


@item('DeskTelephone','탁상 전화기','Appliances','Surface')
def telephone():
    h.rounded_loft('PhoneBase',[(0,.215,.195,.025),(.028,.215,.195,.025),
                               (.065,.19,.16,.022)],'Body')
    h.box('Display',(.095,.045,.004),(.035,.037,.068),'Screen',.003)
    for col in range(3):
        for row in range(4):
            h.box('KeypadButton',(.020,.014,.007),(.008+col*.028,-.012-row*.020,.067),'Rubber',.003)
    for y in (-.055,.065):
        h.box('HandsetCradle',(.055,.042,.024),(-.070,y,.074),'Body',.006)
        h.cushion('HandsetEarpiece',(.060,.050,.034),(-.070,y,.100),'Rubber')
    h.box('HandsetGrip',(.030,.12,.023),(-.070,.005,.109),'Rubber',.009)
    # Connected short cylinders make a closed-volume curled cord without curve conversion.
    points=[]
    for i in range(73):
        t=i/72
        points.append((-.116+.008*cos(t*12*pi),-.055+.11*t,.027+.008*sin(t*12*pi)))
    for a,b in zip(points,points[1:]):h.rod('CurledPhoneCord',a,b,.0018,'Rubber',6)
    h.rod('CordLead',(-.07,-.07,.09),points[0],.002,'Rubber',6)
    h.rod('CordReturn',points[-1],(-.09,.06,.04),.002,'Rubber',6)


@item('Beanbag','빈백','Seating')
def beanbag():
    # An asymmetric upholstered sack: wide belly, scooped seat and raised back.
    n=32
    profiles=[(.28,.025,.0),(.42,.10,.0),(.46,.24,.01),(.40,.37,.04),
              (.31,.41,.10),(.23,.34,.10),(.12,.30,.08)]
    verts=[(0,0,.012)]
    for radius,z,shift in profiles:
        for i in range(n):
            a=2*pi*i/n
            back=max(0,sin(a))
            lift=back*back*(.28 if z>.30 else .04)
            verts.append((radius*cos(a),radius*.90*sin(a)+shift,z+lift))
    top=len(verts);verts.append((0,.08,.295))
    faces=[(0,1+(i+1)%n,1+i) for i in range(n)]
    for j in range(len(profiles)-1):
        a=1+j*n;b=a+n
        for i in range(n):faces.append((a+i,a+(i+1)%n,b+(i+1)%n,b+i))
    a=1+(len(profiles)-1)*n
    faces.extend((a+i,a+(i+1)%n,top) for i in range(n))
    sack=h.mesh('SculptedBeanbag',verts,faces,'Fabric')
    for poly in sack.data.polygons:poly.use_smooth=True
    # Closed rods follow two tailored seam lines along the outer sack.
    for idx in (20,28):
        points=[verts[1+j*n+idx] for j in range(len(profiles))]
        for p,q in zip(points,points[1:]):h.rod('TailoredSeam',p,q,.003,'Accent',6)
    h.box('FabricCareTag',(.035,.008,.018),(.31,-.28,.14),'White',.002,(0,0,-.5))


@item('SalonChair','미용실 의자','Seating')
def salon_chair():
    h.lathe('HydraulicBase',[(0,0),(.29,0),(.31,.025),(.28,.055),(.085,.075),(0,.075)],role='Metal',n=24)
    h.cyl('HydraulicColumn',.055,.32,(0,0,.23),'Metal',16)
    h.cyl('PumpSleeve',.078,.15,(0,0,.14),'Rubber',16)
    h.box('UnderSeatFrame',(.48,.46,.07),(0,0,.405),'Metal',.012)
    h.cushion('SalonSeat',(.54,.51,.12),(0,-.015,.49),'Fabric')
    h.cushion('SalonBack',(.53,.12,.43),(0,.215,.72),'Fabric',(-.12,0,0),True)
    for x in (-.295,.295):
        for y in (-.17,.17):h.rod('ChromeArmSupport',(x,y,.43),(x,y,.65),.014,'Metal')
        h.box('OakArmrest',(.075,.51,.045),(x,0,.665),'Wood',.017)
    for x in (-.19,.19):h.rod('FootrestSupport',(x,-.21,.40),(x,-.49,.19),.018,'Metal')
    h.box('ChromeFootrest',(.46,.17,.028),(0,-.49,.18),'Metal',.006)
    for y in (-.54,-.49,-.44):h.box('FootrestGrip',(.39,.015,.004),(0,y,.197),'Rubber',.002)
    h.rod('PumpPedalArm',(.055,.0,.13),(.22,.0,.10),.012,'Metal')
    h.box('HydraulicPedal',(.10,.06,.018),(.24,0,.10),'Rubber',.005)
    h.root['SeatHeightMetres']=.55
