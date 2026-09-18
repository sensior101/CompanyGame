"""Reusable low-poly Daldongne props. Blender Z up; fronts face -Y.

Expects box/cyl/ico/beam/mesh helpers and M materials from the scene builder.
No external fonts: Korean sign lettering is actual extruded stroke geometry.
"""
import math
import random


# Coordinates inside a unit glyph, from left/bottom to right/top.
_J = {
    'ㄱ': [[(.08,.88),(.88,.88),(.88,.08)]],
    'ㄴ': [[(.10,.90),(.10,.12),(.90,.12)]],
    'ㄷ': [[(.90,.88),(.10,.88),(.10,.12),(.90,.12)]],
    'ㄹ': [[(.10,.90),(.90,.90),(.90,.53),(.10,.53),(.10,.12),(.90,.12)]],
    'ㅁ': [[(.12,.88),(.88,.88),(.88,.12),(.12,.12),(.12,.88)]],
    'ㅂ': [[(.12,.94),(.12,.12),(.88,.12),(.88,.94)],[(.12,.58),(.88,.58)]],
    'ㅅ': [[(.49,.91),(.10,.11)],[(.49,.80),(.91,.11)]],
    'ㅇ': [[(.50,.91),(.80,.79),(.92,.50),(.80,.21),(.50,.09),(.20,.21),(.08,.50),(.20,.79),(.50,.91)]],
    'ㅈ': [[(.08,.88),(.92,.88)],[(.52,.88),(.10,.10)],[(.49,.65),(.91,.10)]],
    'ㅊ': [[(.50,1.0),(.50,.87)],[(.08,.75),(.92,.75)],[(.50,.75),(.10,.07)],[(.48,.56),(.91,.07)]],
    'ㅋ': [[(.08,.90),(.88,.90),(.88,.08)],[(.10,.51),(.88,.51)]],
    'ㅌ': [[(.90,.90),(.10,.90),(.10,.12),(.90,.12)],[(.10,.51),(.86,.51)]],
    'ㅍ': [[(.06,.89),(.94,.89)],[(.06,.12),(.94,.12)],[(.28,.89),(.28,.12)],[(.72,.89),(.72,.12)]],
    'ㅎ': [[(.50,1.0),(.50,.91)],[(.10,.79),(.90,.79)],[(.50,.65),(.77,.55),(.87,.34),(.77,.12),(.50,.03),(.23,.12),(.13,.34),(.23,.55),(.50,.65)]],
    'ㅏ': [[(.28,.04),(.28,.96)],[(.28,.53),(.91,.53)]],
    'ㅑ': [[(.28,.04),(.28,.96)],[(.28,.68),(.91,.68)],[(.28,.35),(.91,.35)]],
    'ㅓ': [[(.72,.04),(.72,.96)],[(.09,.53),(.72,.53)]],
    'ㅕ': [[(.72,.04),(.72,.96)],[(.09,.68),(.72,.68)],[(.09,.35),(.72,.35)]],
    'ㅗ': [[(.04,.26),(.96,.26)],[(.50,.26),(.50,.96)]],
    'ㅛ': [[(.04,.26),(.96,.26)],[(.31,.26),(.31,.96)],[(.69,.26),(.69,.96)]],
    'ㅜ': [[(.04,.74),(.96,.74)],[(.50,.04),(.50,.74)]],
    'ㅠ': [[(.04,.74),(.96,.74)],[(.31,.04),(.31,.74)],[(.69,.04),(.69,.74)]],
    'ㅡ': [[(.04,.50),(.96,.50)]],
    'ㅣ': [[(.50,.04),(.50,.96)]],
    '0': [[(.18,.90),(.82,.90),(.82,.10),(.18,.10),(.18,.90)]],
    '1': [[(.35,.75),(.55,.92),(.55,.10)]],
    '2': [[(.15,.88),(.84,.88),(.84,.53),(.15,.10),(.86,.10)]],
    '3': [[(.15,.90),(.84,.90),(.84,.10),(.15,.10)],[(.36,.52),(.84,.52)]],
    '4': [[(.17,.91),(.17,.47),(.90,.47)],[(.72,.91),(.72,.09)]],
    '5': [[(.86,.90),(.16,.90),(.16,.53),(.84,.53),(.84,.10),(.15,.10)]],
    '6': [[(.84,.90),(.16,.90),(.16,.10),(.84,.10),(.84,.53),(.16,.53)]],
    '7': [[(.14,.90),(.85,.90),(.42,.10)]],
    '8': [[(.16,.90),(.84,.90),(.84,.10),(.16,.10),(.16,.90)],[(.16,.52),(.84,.52)]],
    '9': [[(.84,.10),(.84,.90),(.16,.90),(.16,.53),(.84,.53)]],
}
_INITIAL = 'ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ'
_MEDIAL = 'ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ'
_FINAL = ['', 'ㄱ','ㄲ','ㄳ','ㄴ','ㄵ','ㄶ','ㄷ','ㄹ','ㄺ','ㄻ','ㄼ','ㄽ','ㄾ','ㄿ','ㅀ','ㅁ','ㅂ','ㅄ','ㅅ','ㅆ','ㅇ','ㅈ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ']
_SPLIT = {'ㄲ':'ㄱㄱ','ㄸ':'ㄷㄷ','ㅃ':'ㅂㅂ','ㅆ':'ㅅㅅ','ㅉ':'ㅈㅈ',
          'ㄳ':'ㄱㅅ','ㄵ':'ㄴㅈ','ㄶ':'ㄴㅎ','ㄺ':'ㄹㄱ','ㄻ':'ㄹㅁ','ㄼ':'ㄹㅂ',
          'ㄽ':'ㄹㅅ','ㄾ':'ㄹㅌ','ㄿ':'ㄹㅍ','ㅀ':'ㄹㅎ','ㅄ':'ㅂㅅ'}


def _glyph_segments(letter, frame):
    x,z,w,h = frame
    if letter in _SPLIT:
        pair = _SPLIT[letter]
        return _glyph_segments(pair[0],(x,z,w*.45,h)) + _glyph_segments(pair[1],(x+w*.55,z,w*.45,h))
    # Compound vertical vowels keep their second vertical stroke distinct.
    if letter in ('ㅐ','ㅒ','ㅔ','ㅖ'):
        base = {'ㅐ':'ㅏ','ㅒ':'ㅑ','ㅔ':'ㅓ','ㅖ':'ㅕ'}[letter]
        return _glyph_segments(base,(x,z,w*.64,h)) + _glyph_segments('ㅣ',(x+w*.72,z,w*.28,h))
    segments=[]
    for path in _J.get(letter,[]):
        for a,b in zip(path,path[1:]):
            segments.append(((x+a[0]*w,z+a[1]*h),(x+b[0]*w,z+b[1]*h)))
    return segments


def _syllable_segments(ch):
    v=ord(ch)-0xAC00
    if not 0 <= v < 11172:
        return _glyph_segments(ch,(.08,.08,.84,.84))
    initial=_INITIAL[v//588]
    vowel=_MEDIAL[(v%588)//28]
    final=_FINAL[v%28]
    # Reserve a consistent, separated bottom region for the final consonant.
    lo=.36 if final else .04
    hi=.98-lo
    mixed={'ㅘ':('ㅗ','ㅏ'),'ㅙ':('ㅗ','ㅐ'),'ㅚ':('ㅗ','ㅣ'),
           'ㅝ':('ㅜ','ㅓ'),'ㅞ':('ㅜ','ㅔ'),'ㅟ':('ㅜ','ㅣ'),'ㅢ':('ㅡ','ㅣ')}
    if vowel in 'ㅗㅛㅜㅠㅡ':
        segments=_glyph_segments(initial,(.15,lo+hi*.46,.70,hi*.52))
        segments+=_glyph_segments(vowel,(.06,lo,.88,hi*.37))
    elif vowel in mixed:
        horizontal,vertical=mixed[vowel]
        segments=_glyph_segments(initial,(.06,lo+hi*.42,.49,hi*.57))
        segments+=_glyph_segments(horizontal,(.04,lo,.59,hi*.34))
        segments+=_glyph_segments(vertical,(.65,lo,.31,hi*.98))
    else:
        segments=_glyph_segments(initial,(.045,lo+.02,.46,hi*.94))
        segments+=_glyph_segments(vowel,(.57,lo,.38,hi*.98))
    if final:
        segments+=_glyph_segments(final,(.12,.035,.76,.245))
    return segments


def lettering(name,label,x,y,z,width,height,mat=None):
    """One mesh containing extruded, font-independent Korean lettering.

    x/z is the label center, y is the front-facing sign surface.
    """
    mat=mat or M['white']
    advance=min(height*1.10,width/max(len(label),1))
    glyph_size=min(height,advance*.84)
    stroke=glyph_size*.064
    verts=[]
    faces=[]
    start=x-advance*len(label)/2+(advance-glyph_size)/2
    for i,ch in enumerate(label):
        for a,b in _syllable_segments(ch):
            ax=start+i*advance+a[0]*glyph_size
            az=z-glyph_size/2+a[1]*glyph_size
            bx=start+i*advance+b[0]*glyph_size
            bz=z-glyph_size/2+b[1]*glyph_size
            dx,dz=bx-ax,bz-az
            length=math.hypot(dx,dz)
            if length<1e-6: continue
            nx,nz=-dz/length*stroke/2,dx/length*stroke/2
            ex,ez=dx/length*stroke/2,dz/length*stroke/2
            corners=[(ax-ex+nx,az-ez+nz),(bx+ex+nx,bz+ez+nz),
                     (bx+ex-nx,bz+ez-nz),(ax-ex-nx,az-ez-nz)]
            j=len(verts)
            for yy in (y-.026,y+.004):
                verts.extend((xx,yy,zz) for xx,zz in corners)
            faces.extend(tuple(j+k for k in f) for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
    return mesh(name,verts,faces,mat) if verts else None


def sign(name,label,x,y,z,width,height,board='dark',border=True):
    """Upright sign facing -Y; x/y/z denotes the center of the board."""
    if border:
        box(name+'_frame',(x,y+.022,z),(width+.10,.14,height+.10),M['trim'])
    obj=box(name+'_board',(x,y-.025,z),(width,.13,height),M[board])
    lettering(name+'_Hangul',label,x,y-.105,z,width*.91,height*.66)
    return obj


def _front_window(name,x,y,z,w,h):
    box(name+'_surround',(x,y,z),(w+.15,.13,h+.15),M['trim'])
    box(name+'_lit_glass',(x,y-.08,z),(w,.05,h),M['window'])
    box(name+'_mullion',(x,y-.12,z),(.065,.04,h),M['cream'])
    box(name+'_sill',(x,y-.13,z-h/2), (w+.24,.30,.12),M['cream'])


def _planter(name,x,y,z,s=.55):
    cyl(name+'_pot',(x,y,z+.25*s),.34*s,.50*s,M['orange'],vertices=8)
    ico(name+'_bush',(x,y,z+.70*s),(.46*s,.42*s,.50*s),M['leaf2'])
    for i in range(3):
        a=i*2.1
        ico(name+'_flower_'+str(i),(x+math.cos(a)*.30*s,y+math.sin(a)*.26*s,z+.95*s),(.11*s,.11*s,.13*s),M['yellow'],subdivisions=1)


def shop(name,x,y,z,w=7,d=5,h=4.4,label='편의점',accent='red',roof='flat'):
    """Shop with individually modeled frontage, striped awning and rooftop equipment."""
    front=y-d/2
    box(name+'_foundation',(x,y,z+.12),(w+.20,d+.20,.24),M['stone'])
    box(name+'_stucco',(x,y,z+h/2),(w,d,h),M['cream'])
    for xx in (x-w/2+.17,x+w/2-.17):
        box(name+'_corner',(xx,front-.075,z+h/2),(.22,.17,h),M['trim'])
    glazing_h=h*.52
    for i,xx in enumerate((x-w*.31,x,x+w*.31)):
        _front_window(name+'_storefront_'+str(i),xx,front-.10,z+.30+glazing_h/2,w*.27,glazing_h)
    # Door handles and display shelf silhouettes stay visible through the warm panes.
    box(name+'_door_handle',(x+w*.07,front-.265,z+1.25),(.045,.08,.35),M['metal'])
    for offset in (-.31,.31):
        for j in range(3):
            box(name+'_display',(x+w*offset+(j-1)*w*.066,front-.19,z+.62),(.25,.10,.31),M['wood' if j%2 else accent])
    sign(name+'_sign',label,x,front-.21,z+h*.82,w*.92,h*.245,accent if accent in ('red','blue','wood','orange') else 'dark')
    aw_z=z+h*.65
    count=max(8,int(w*2))
    for i in range(count):
        xx=x-w*.49+(i+.5)*w*.98/count
        stripe=box(name+'_awning_'+str(i),(xx,front-.63,aw_z-.10),(w*.98/count,.97,.085),M[accent if i%2==0 else 'cream'])
        stripe.rotation_euler.x=.22
        box(name+'_valance_'+str(i),(xx,front-1.10,aw_z-.25),(w*.98/count,.065,.23),M[accent if i%2==0 else 'cream'])
    box(name+'_roof_slab',(x,y,z+h+.08),(w+.28,d+.28,.20),M['stone'])
    if roof=='flat':
        for yy in (y-d/2,y+d/2):
            box(name+'_roof_parapet',(x,yy,z+h+.30),(w+.16,.17,.38),M['trim'])
        for xx in (x-w/2,x+w/2):
            box(name+'_roof_parapet',(xx,y,z+h+.30),(.17,d,.38),M['trim'])
        box(name+'_HVAC',(x+w*.29,y+d*.18,z+h+.48),(.82,.72,.63),M['trim'])
        cyl(name+'_fan',(x+w*.29,y+d*.18,z+h+.81),.23,.026,M['metal'],vertices=8)
        box(name+'_vent',(x-w*.28,y+d*.27,z+h+.42),(.38,.42,.46),M['stone'])
    else:
        _hip_roof(name,x,y,z+h,w,d,roof)
    _planter(name+'_left_planter',x-w*.47,front-.72,z,.95)
    _planter(name+'_right_planter',x+w*.47,front-.72,z,.90)
    return None


def _hip_roof(name,x,y,z,w,d,color):
    ww,dd=w/2+.34,d/2+.34
    rise=min(w,d)*.28
    verts=[(x-ww,y-dd,z),(x+ww,y-dd,z),(x+ww,y+dd,z),(x-ww,y+dd,z),
           (x-w*.24,y,z+rise),(x+w*.24,y,z+rise)]
    mesh(name+'_hip_roof',verts,[(0,1,5,4),(1,2,5),(2,3,4,5),(3,0,4),(3,2,1,0)],M[color])
    beam(name+'_ridge',(x-w*.25,y,z+rise+.025),(x+w*.25,y,z+rise+.025),.13,M[color])
    # Geometric tile seams are deliberately sparse for a clear low-poly silhouette.
    for i in range(1,8):
        xx=x-ww+(2*ww)*i/8
        topx=max(x-w*.24,min(x+w*.24,xx))
        beam(name+'_roof_seam',(xx,y-dd,z+.025),(topx,y,z+rise+.025),.025,M['trim'])
        beam(name+'_roof_seam',(xx,y+dd,z+.025),(topx,y,z+rise+.025),.025,M['trim'])


def house(name,x,y,z,w=5.8,d=4.5,h=4.1,roof='blue'):
    box(name+'_foundation',(x,y,z+.12),(w+.16,d+.16,.24),M['stone'])
    box(name+'_walls',(x,y,z+h/2),(w,d,h),M['cream'])
    front=y-d/2
    _front_window(name+'_window_L',x-w*.28,front-.07,z+h*.56,w*.23,h*.38)
    _front_window(name+'_window_R',x+w*.28,front-.07,z+h*.56,w*.23,h*.38)
    box(name+'_door_frame',(x,front-.09,z+h*.29),(w*.20,.15,h*.58),M['trim'])
    box(name+'_door',(x,front-.19,z+h*.28),(w*.16,.06,h*.53),M['wood'])
    box(name+'_door_lite',(x,front-.23,z+h*.37),(w*.11,.035,h*.19),M['window'])
    for side in (-1,1):
        box(name+'_side_window_frame',(x+side*(w/2+.035),y,z+h*.58),(.10,d*.30,h*.37),M['trim'])
        box(name+'_side_window',(x+side*(w/2+.095),y,z+h*.58),(.035,d*.26,h*.33),M['window'])
    _hip_roof(name,x,y,z+h,w,d,roof)
    box(name+'_chimney',(x+w*.25,y+d*.25,z+h+.68),(.40,.48,1.0),M['trim'])
    box(name+'_chimney_cap',(x+w*.25,y+d*.25,z+h+1.21),(.54,.62,.12),M['stone'])
    _planter(name+'_porch_plant',x+w*.43,front-.38,z,.70)


def tree(name,x,y,z,scale=1):
    rng=random.Random(name)
    s=scale
    cyl(name+'_trunk',(x,y,z+1.45*s),.18*s,2.9*s,M['wood'],vertices=7)
    for i in range(3):
        angle=i*2.094+.35
        tip=(x+math.cos(angle)*.76*s,y+math.sin(angle)*.76*s,z+2.6*s)
        beam(name+'_branch',(x,y,z+1.55*s),tip,.17*s,M['wood'])
    for i,(dx,dy,dz,size) in enumerate([(0,0,3.5,1.18),(-.76,-.08,2.93,.95),(.68,.27,3.03,1.03),(.03,-.74,2.85,.91),(-.2,.65,3.30,.91)]):
        ico(name+'_crown_'+str(i),(x+dx*s,y+dy*s,z+dz*s),
            (size*s,size*s*(.84+rng.random()*.18),size*s*(.92+rng.random()*.16)),M[['leaf1','leaf2','leaf3'][i%3]],subdivisions=1)


def lamp(name,x,y,z,scale=1):
    s=scale
    cyl(name+'_foot',(x,y,z+.09*s),.29*s,.18*s,M['metal'],vertices=8)
    cyl(name+'_base',(x,y,z+.35*s),.15*s,.55*s,M['metal'],vertices=8)
    cyl(name+'_post',(x,y,z+1.9*s),.069*s,3.3*s,M['metal'],vertices=8)
    box(name+'_lantern_floor',(x,y,z+3.59*s),(.48*s,.48*s,.09*s),M['metal'])
    box(name+'_lantern_glow',(x,y,z+3.92*s),(.35*s,.35*s,.60*s),M['window'])
    for dx in (-.2,.2):
        for dy in (-.2,.2):
            beam(name+'_lantern_frame',(x+dx*s,y+dy*s,z+3.58*s),(x+dx*s,y+dy*s,z+4.23*s),.045*s,M['metal'])
    mesh(name+'_lantern_cap',[(x+dx*s,y+dy*s,z+4.23*s) for dx,dy in [(-.31,-.31),(.31,-.31),(.31,.31),(-.31,.31)]]+[(x,y,z+4.51*s)],[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(3,2,1,0)],M['metal'])


def bench(name,x,y,z,angle=0):
    parts=[]
    for i in range(4):
        parts.append(box(name+'_seat_'+str(i),(0,(i-1.5)*.16,.65),(2.05,.125,.10),M['wood']))
    for i in range(3):
        parts.append(box(name+'_back_'+str(i),(0,.30,.93+i*.18),(2.05,.095,.12),M['wood']))
    for xx in (-.78,.78):
        parts.append(box(name+'_leg',(xx,0,.32),(.095,.46,.64),M['metal']))
        parts.append(box(name+'_back_support',(xx,.34,.90),(.085,.085,1.0),M['metal']))
    _place_parts(parts,x,y,z,angle)


def _place_parts(parts,x,y,z,angle):
    c,s=math.cos(angle),math.sin(angle)
    for obj in parts:
        a,b,zz=obj.location
        obj.location=(x+a*c-b*s,y+a*s+b*c,z+zz)
        obj.rotation_euler.z+=angle


def car(name,x,y,z,color='yellow',bus=False,angle=0):
    """Vehicle facing local -Y; angle is radians counterclockwise around world Z."""
    parts=[]
    def b(suffix,loc,size,mat):
        obj=box(name+'_'+suffix,loc,size,M[mat]); parts.append(obj); return obj
    def wheel(xx,yy,r):
        obj=cyl(name+'_tire',(xx,yy,r+.055),r,.20,M['dark'],vertices=12)
        obj.rotation_euler.y=math.pi/2; parts.append(obj)
        obj=cyl(name+'_hub',(xx+(.11 if xx>0 else -.11),yy,r+.055),r*.51,.026,M['trim'],vertices=8)
        obj.rotation_euler.y=math.pi/2; parts.append(obj)
    if bus:
        b('chassis',(0,0,.51),(2.44,6.9,.37),'metal')
        b('body',(0,0,1.54),(2.48,6.9,2.13),color)
        b('roof',(0,0,2.66),(2.53,6.95,.16),color)
        b('windshield',(0,-3.47,1.93),(2.21,.04,1.04),'glass')
        b('front_number_panel',(0,-3.50,2.48),(1.66,.045,.28),'dark')
        lettering_obj=lettering(name+'_route_701','701',0,-3.545,2.49,1.22,.22,M['yellow'])
        if lettering_obj: parts.append(lettering_obj)
        for side in (-1,1):
            for i in range(6):
                b('passenger_window',(side*1.255,-2.55+i*.93,1.99),(.045,.79,.88),'glass')
                b('window_post',(side*1.29,-2.99+i*.93,1.99),(.07,.065,1.0),'metal')
            b('side_stripe',(side*1.264,0,.98),(.035,6.66,.12),'cream')
            for yy in (-2.08,2.10): wheel(side*1.25,yy,.46)
        b('vent_roof',(0,.48,2.89),(1.15,1.18,.38),color)
        for xx in (-.86,.86):
            b('headlight',(xx,-3.49,.91),(.35,.055,.25),'window')
        b('bumper',(0,-3.53,.52),(2.31,.15,.14),'trim')
        b('door',(1.30,-1.9,1.36),(.035,.79,1.87),'glass')
        b('door_rail',(1.34,-1.9,1.36),(.03,.035,1.87),'metal')
    else:
        b('chassis',(0,0,.44),(1.63,3.33,.29),'metal')
        b('body',(0,0,.77),(1.76,3.45,.65),color)
        b('cabin',(0,.22,1.30),(1.52,1.82,.72),color)
        b('roof',(0,.26,1.69),(1.57,1.86,.13),color)
        b('windshield',(0,-.703,1.36),(1.32,.035,.48),'glass')
        b('rear_window',(0,1.15,1.36),(1.31,.035,.46),'glass')
        for side in (-1,1):
            for yy in (-.20,.64):
                b('side_window',(side*.771,yy,1.38),(.035,.70,.43),'glass')
            b('door_handle',(side*.90,.63,.99),(.045,.19,.045),'trim')
            b('mirror',(side*.94,-.55,1.26),(.21,.19,.14),color)
            for yy in (-1.04,1.02): wheel(side*.88,yy,.33)
        for xx in (-.59,.59):
            b('headlight',(xx,-1.752,.82),(.32,.045,.23),'window')
            b('taillight',(xx,1.752,.81),(.31,.045,.19),'red')
        b('front_bumper',(0,-1.78,.52),(1.56,.16,.14),'trim')
        b('grille',(0,-1.77,.77),(.55,.055,.20),'dark')
    _place_parts(parts,x,y,z,angle)
    return parts
