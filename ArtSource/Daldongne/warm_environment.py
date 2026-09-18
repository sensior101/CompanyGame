"""Portable geometry and restrained materials for an older Seoul hillside village."""
def setup_warm_palette():
    colors={'stone':(.46,.435,.385),'cream':(.72,.655,.52),'trim':(.80,.755,.64),
      'plaster':(.74,.71,.61),'teal':(.13,.32,.31),'slate':(.12,.16,.18),
      'brick':(.43,.18,.115),'brickLight':(.56,.265,.16),'brickDark':(.30,.125,.08),
      'mortar':(.51,.45,.36),'glassClear':(.27,.52,.51),'red':(.52,.22,.135),
      'blue':(.18,.32,.40),'green':(.31,.43,.20),'wood':(.35,.21,.115),
      'pavement':(.61,.56,.45),'paver_light':(.72,.655,.53),'paver_dark':(.47,.455,.395),
      'leaf1':(.115,.235,.075),'leaf2':(.245,.355,.11),'leaf3':(.39,.47,.17),
      'grass':(.235,.295,.105),'metal':(.115,.145,.145),'window':(1,.58,.22),
      'road':(.145,.16,.17),'white':(.89,.865,.77),'orange':(.64,.34,.13),'purple':(.38,.22,.43)}
    for key,col in colors.items():
        m=bpy.data.materials.get(key) or bpy.data.materials.new(key);M[key]=m;m.use_nodes=True
        alpha=.30 if key=='glassClear' else 1
        m.diffuse_color=(*col,alpha);p=m.node_tree.nodes.get('Principled BSDF')
        p.inputs['Base Color'].default_value=(*col,alpha);p.inputs['Alpha'].default_value=alpha
        p.inputs['Roughness'].default_value=.20 if key=='glassClear' else .88
        p.inputs['Metallic'].default_value=.50 if key=='metal' else 0
        if key=='window':p.inputs['Emission Color'].default_value=(*col,1);p.inputs['Emission Strength'].default_value=1.6
        if alpha<1:m.surface_render_method='DITHERED'
    m=bpy.data.materials.new('CollisionInvisible');m.use_nodes=True;m.diffuse_color=(0,0,0,0)
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Alpha'].default_value=0;m.surface_render_method='DITHERED';M['CollisionInvisible']=m

def dress_neighborhood():
    rng=random.Random(817)
    # Brick plinths and surviving painted plaster are deliberately mixed.
    buildings=[('Hamburger',-25,-.4,3,6,4.8,4.4),('Convenience',-16,-.4,3,8,4.8,4.4),
      ('Snack',-6.5,-.4,3,6.6,4.8,4.4),('Laundry',-14.7,12.8,6,7.6,4.2,3.7),
      ('Gosiwon',-11.5,24.1,12,7.8,5.4,6),('Home_A',7.6,19.5,9,6,5,4.1),
      ('Home_B',20.5,21.3,9,5.6,4.8,4.1),('Home_C',23.5,10.7,6,5.7,4.7,4.1),('Home_D',7.4,27.1,12,5.8,4.8,4.1)]
    faces=((3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7))
    for idx,(name,x,y,z,w,d,h) in enumerate(buildings):
        body=bpy.data.objects.get(name+'_stucco') or bpy.data.objects.get(name+'_walls')
        if body:
            body.data=body.data.copy();body.data.materials.clear();body.data.materials.append(M['mortar' if idx in (0,2,4,6) else 'plaster'])
        # Front and both side wall brick courses, shallow enough to keep windows proud.
        buckets={k:([],[]) for k in ('brick','brickLight','brickDark')}
        for side,(length,height) in enumerate([(w,h if idx in(0,2,4,6) else 1.05),(d,h),(d,h)]):
            for row in range(int(height/.28)):
                for col in range(math.ceil(length/.62)+1):
                    lo=max(0,col*.62-(.31 if row%2 else 0));hi=min(length,lo+.59)
                    if hi-lo<.08:continue
                    # Peeling paint leaves patches of masonry visible on plaster houses.
                    if idx not in (0,2,4,6) and row>4 and ((col+row//2)%6 not in (0,1)):continue
                    mid=(lo+hi)/2-length/2;zz=z+.14+row*.28
                    if side==0:cx,cy=x+mid,y-d/2-.013;sx,sy=hi-lo,.038
                    else:cx,cy=x+(w/2+.013)*(1 if side==1 else -1),y+mid;sx,sy=.038,hi-lo
                    key=rng.choices(list(buckets),[7,2,1])[0];vs,fs=buckets[key];j=len(vs)
                    vs.extend((cx+dx*sx/2,cy+dy*sy/2,zz+dz*.123) for dx,dy,dz in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)])
                    fs.extend(tuple(j+i for i in f) for f in faces)
        for key,(vs,fs) in buckets.items():
            if vs:mesh(name+'_WeatheredBrick_'+key,vs,fs,key)
        # Rain pipe, patched parapet, an old electric meter and exterior AC.
        px=x+w/2-.18;fy=y-d/2-.11
        beam(name+'_Downpipe',(px,fy,z+.12),(px,fy,z+h-.2),.085,'metal')
        for zz in (z+.9,z+h-.6):box(name+'_PipeClamp',(px,fy-.025,zz),(.19,.07,.07),'slate')
        box(name+'_Meter',(x-w/2+.32,fy-.03,z+1.22),(.28,.12,.42),'stone')
        if name.startswith(('Home','Gosiwon','Laundry')):
            box(name+'_ACCase',(x+w/2+.23,y+.8,z+1.6),(.42,.92,.65),'trim')
            fan=cyl(name+'_ACFan',(x+w/2+.46,y+.8,z+1.6),.235,.025,'metal',12);fan.rotation_euler.y=math.pi/2
            for j in range(3):box(name+'_RepairPatch',(x-w*.25+j*.32,y-d/2-.042,z+h-.6-j*.15),(.29,.025,.14),'plaster')
    # Domestic rooftop silhouettes: water tanks, laundry, jars and a small kitchen garden.
    cyl('Gosiwon_WaterTank',(-13.8,24.8,19.3),.72,1.5,'teal',12)
    cyl('Gosiwon_WaterTankLid',(-13.8,24.8,20.1),.77,.12,'slate',12)
    for x in (-11.7,-8.7):beam('Gosiwon_ClothesPole',(x,25.5,18.1),(x,25.5,19.9),.065,'metal')
    beam('Gosiwon_ClothesLine',(-11.7,25.5,19.65),(-8.7,25.5,19.65),.022,'wood')
    for j in range(4):box('Gosiwon_DryingLaundry',(-11.35+j*.67,25.5,19.18),(.47,.035,.91),['cream','teal','white','pink'][j])
    for j in range(3):
        cyl('Home_B_Onggi',(19.1+j*.6,21.5,13.52),.24,.56,'brickDark',10)
        cyl('Home_B_OnggiLid',(19.1+j*.6,21.5,13.84),.27,.075,'wood',10)

def add_warm_street_life():
    # Props sit in garden pockets; clearance is applied after placement.
    def bicycle(name,x,y,z):
        for xx in (x-.65,x+.65):
            # Open wheels represented as a 16-sided wire ring.
            for i in range(16):
                a=i*math.tau/16;b=(i+1)*math.tau/16
                beam(name+'_Tyre',(xx+.43*math.cos(a),y,z+.45+.43*math.sin(a)),(xx+.43*math.cos(b),y,z+.45+.43*math.sin(b)),.065,'dark')
            for i in range(6):
                a=i*math.tau/6;beam(name+'_Spoke',(xx,y,z+.45),(xx+.4*math.cos(a),y,z+.45+.4*math.sin(a)),.018,'trim')
        pts=[(-.65,.45),(-.15,1.1),(.35,.45),(-.65,.45),(.18,1.1),(.65,.45),(.18,1.1),(-.15,1.1)]
        for a,b in zip(pts,pts[1:]):beam(name+'_Frame',(x+a[0],y,z+a[1]),(x+b[0],y,z+b[1]),.055,'teal')
        box(name+'_Seat',(x-.15,y,z+1.19),(.35,.19,.10),'wood')
        beam(name+'_HandleStem',(x+.18,y,z+1.1),(x+.3,y,z+1.36),.05,'metal')
        beam(name+'_Handle',(x+.3,y-.24,z+1.36),(x+.3,y+.24,z+1.36),.05,'metal')
    bicycle('Cafe_Bicycle',28.8,-1.7,3)
    bicycle('Station_Bicycle',-24,-18.5,1)
    # New local utility poles, warm street lamps and vending machine.
    for i,(x,y,z) in enumerate([(-28,-2,3),(-8,10,6),(-16.1,18.1,9),(16.6,16,9),(26.5,-17.8,1),(-23,-26,1)]):
        lamp('WarmLamp_'+str(i),x,y,z)
    box('WarmVending_Case',(27.4,-17.15,2.03),(1.05,.76,2.06),'cream')
    box('WarmVending_Glass',(27.2,-17.55,2.12),(.60,.026,1.16),'glass')
    for row in range(3):
        for col in range(3):cyl('WarmVending_Can',(26.99+col*.19,-17.58,1.73+row*.34),.060,.18,['red','green','yellow'][col],8)
    box('WarmVending_Payment',(27.68,-17.56,2.27),(.17,.04,.43),'slate')
    box('WarmVending_PaymentGlow',(27.68,-17.59,2.36),(.11,.013,.15),'window')
    box('WarmVending_Dispense',(27.3,-17.55,1.37),(.61,.03,.16),'dark')
    # Clay planters and small vegetable beds give the urban streets a rural edge.
    for i,(x,y,z) in enumerate([(-28,2,3),(-10.3,10.2,6),(-6.9,26,12),(13.5,20,9),(26.8,7,6),(29,-1,3),(-24.8,-27,1)]):
        cyl('WarmPot_'+str(i),(x,y,z+.30),.38,.6,'brickLight',10)
        cyl('WarmPotRim_'+str(i),(x,y,z+.56),.42,.12,'orange',10)
        ico('WarmPotFoliage_'+str(i),(x,y,z+.9),(.50,.46,.55),'leaf2')
    for x in (14.1,15.3,16.5):
        box('Garden_VegetableBed',(x,26.9,9.15),(.88,2.8,.30),'wood')
        for y in (26.1,26.7,27.3,27.9):ico('Garden_Cabbage',(x,y,9.48),(.35,.34,.27),'leaf3')

def warm_lighting():
    s=bpy.context.scene;s.world.color=(.45,.48,.52)
    for l in bpy.data.lights:
        if l.type=='SUN':
            l.energy=1.85 if 'Warm' in l.name else .55;l.color=(1,.79,.60) if 'Warm' in l.name else (.58,.72,1)
    bpy.data.lights['Sun_WarmEvening'].angle=math.radians(9)
    s.view_settings.view_transform='Khronos PBR Neutral';s.view_settings.exposure=0
    s.eevee.taa_render_samples=16;s.render.engine='BLENDER_EEVEE'
    s.camera=bpy.data.objects['Camera_Isometric'];s.camera.data.ortho_scale=82
    s['ArtDirection']='Older Seoul hillside village: terracotta brick, worn plaster, teal glazing, warm shops, urban and rural street life.'
