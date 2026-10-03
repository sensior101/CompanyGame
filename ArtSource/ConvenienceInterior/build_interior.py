"""Reference-led Korean convenience store. Blender 5.x, metres, Z up.

Run make_graphics.py first, then blender -b -t 8 --python build_interior.py.
Named furniture groups remain editable. Native Unity mesh data carries UVs and
normals explicitly; collider proxies are separate from decorative merchandise.
"""
import bpy, math, json, random, sys, gzip
from pathlib import Path
from mathutils import Vector

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[1]
OUT=ROOT/'CompanyGame/Assets/Art/Interiors/ConvenienceStore'
PREVIEW=HERE/'Previews'
OUT.mkdir(parents=True,exist_ok=True); PREVIEW.mkdir(exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
random.seed(211)
MAT={}; SPEC=[]; GEO={}; COLL=[]; LIGHTS=[]
def linear(v): return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
def material(name,color,rough=.5,metal=0,emit=0,texture=None,alpha=1):
    rgb=[linear(int(color[k:k+2],16)/255) for k in (1,3,5)]
    m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=(*rgb,alpha)
    n=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
    if n is None:
        n=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
        out=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(n.outputs['BSDF'],out.inputs['Surface'])
    n.inputs['Base Color'].default_value=(*rgb,alpha); n.inputs['Roughness'].default_value=rough;n.inputs['Metallic'].default_value=metal
    n.inputs['Alpha'].default_value=alpha
    if emit:
        n.inputs['Emission Color'].default_value=(*rgb,1);n.inputs['Emission Strength'].default_value=emit
    if texture:
        im=bpy.data.images.load(str(OUT/'Textures'/texture));im.pack()
        t=m.node_tree.nodes.new('ShaderNodeTexImage');t.image=im
        m.node_tree.links.new(t.outputs['Color'],n.inputs['Base Color'])
    if alpha<1:
        m.surface_render_method='DITHERED'
    MAT[name]=m;SPEC.append(dict(name=name,color=rgb,alpha=alpha,roughness=rough,metallic=metal,emission=emit,texture=texture or ''))
material('Porcelain','#EFE3CE',.19)
material('Grout','#BDB7A9',.7)
material('Ivory','#EEECE4',.4)
material('Ceiling','#E6E1D5',.65)
material('Steel','#818F91',.28,.7)
material('Charcoal','#303739',.42,.2)
material('Green','#248F4C',.42)
material('DeepGreen','#145C48',.4)
material('Orange','#EA5226',.42)
material('Lime','#99BA3A',.42)
material('Oak','#DAB37C',.42)
material('Cream','#FFF5D5',.6)
material('Blue','#31A0CD',.4)
material('Water','#ABDDE5',.17,.15)
material('WhiteLED','#FFF4DD',.22,emit=4)
material('CoolLED','#DAF3FF',.2,emit=3)
material('Screen','#448FAE',.3,emit=.45)
material('Glass','#DCEDEF',.12,.15,alpha=.10)
material('Products','#FFFFFF',.48,texture='Products.png')
material('Signs','#FFFFFF',.42,texture='Signs.png')
material('Red','#C83627',.4)
material('Yellow','#E9BA30',.4)

# Direct mesh accumulation avoids thousands of operator calls and keeps meshes grouped.
GROUP='Architecture'; origin=(0,0); angle=0
def group(name):
    global GROUP;GROUP=name
def frame(x=0,y=0,a=0):
    global origin,angle;origin=(x,y);angle=a
def pt(v):
    x,y,z=v;c=math.cos(angle);s=math.sin(angle)
    return (origin[0]+x*c-y*s,origin[1]+x*s+y*c,z)
def mesh(v,f,mat,uvs=None):
    key=(GROUP,mat)
    data=GEO.setdefault(key,[[],[],[]]);n=len(data[0]);data[0].extend(pt(p) for p in v)
    for i,face in enumerate(f):
        data[1].append(tuple(n+j for j in face));data[2].append(uvs[i] if uvs else [(0,0)]*len(face))
def box(c,s,mat):
    x,y,z=c;a,b,h=[v/2 for v in s]
    v=[(x-a,y-b,z-h),(x+a,y-b,z-h),(x+a,y+b,z-h),(x-a,y+b,z-h),(x-a,y-b,z+h),(x+a,y-b,z+h),(x+a,y+b,z+h),(x-a,y+b,z+h)]
    mesh(v,[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)],mat)
def atlas_uv(i,kind):
    cols,rows=(8,8) if kind=='Products' else (4,4)
    l=(i%cols+.012)/cols;r=(i%cols+.988)/cols;t=1-(i//cols+.012)/rows;b=1-(i//cols+.988)/rows
    return [(l,b),(r,b),(r,t),(l,t)]
def card(x,y,z,w,h,i,kind='Signs'):
    uv=atlas_uv(i,kind)
    if kind=='Signs' and h/w<.20:
        l,b=uv[0];r,t=uv[2];span=t-b
        uv=[(l,t-span*.44),(r,t-span*.44),(r,t-span*.20),(l,t-span*.20)]
    mesh([(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),(x+w/2,y,z+h/2),(x-w/2,y,z+h/2)],[(0,1,2,3)],kind,[uv])
def topcard(x,y,z,w,d,i):
    mesh([(x-w/2,y-d/2,z),(x+w/2,y-d/2,z),(x+w/2,y+d/2,z),(x-w/2,y+d/2,z)],[(0,1,2,3)],'Products',[atlas_uv(i,'Products')])
def cyl(c,r,h,mat,n=12,r2=None):
    x,y,z=c;r2=r if r2 is None else r2
    v=[(x+rr*math.cos(j*2*math.pi/n),y+rr*math.sin(j*2*math.pi/n),zz) for zz,rr in ((z-h/2,r),(z+h/2,r2)) for j in range(n)]
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]+[(j,(j+1)%n,(j+1)%n+n,j+n) for j in range(n)]
    mesh(v,faces,mat)
def proxy(name,c,s):
    p=pt(c);cosa=abs(math.cos(angle));sina=abs(math.sin(angle))
    COLL.append(dict(name=name,center=[p[0],p[2],p[1]],size=[s[0]*cosa+s[1]*sina,s[2],s[1]*cosa+s[0]*sina]))
def solid(name,c,s,mat): box(c,s,mat);proxy(name,c,s)
def bag(x,y,z,i,w=.18,h=.24):
    # Tapered crimped edges and a plump front distinguish packets from boxes.
    d=.075;v=[]
    for zz,ww,dd in ((z,.88*w,.025),(z+.06,w,d),(z+h-.04,.96*w,d),(z+h,.83*w,.025)):
        v.extend([(x-ww/2,y-dd/2,zz),(x+ww/2,y-dd/2,zz),(x+ww/2,y+dd/2,zz),(x-ww/2,y+dd/2,zz)])
    faces=[(0,3,2,1),(12,13,14,15)]
    baseuv=atlas_uv(i,'Products');uvs=[baseuv,baseuv]
    l,b=baseuv[0];r,t=baseuv[2];levels=[0,.06/h,(h-.04)/h,1]
    for k in range(3):
        for j in range(4):
            faces.append((k*4+j,k*4+(j+1)%4,(k+1)*4+(j+1)%4,(k+1)*4+j))
            uv0=b+(t-b)*levels[k];uv1=b+(t-b)*levels[k+1]
            uvs.append([(l,uv0),(r,uv0),(r,uv1),(l,uv1)])
    mesh(v,faces,'Products',uvs)
def bottle(x,y,z,i,ht=.245):
    r=.038
    cyl((x,y,z+ht*.35),r,ht*.7,'Water' if i%3 else 'Green')
    cyl((x,y,z+ht*.77),r,ht*.14,'Water',r2=.019)
    cyl((x,y,z+ht*.88),.019,ht*.12,'Water')
    cyl((x,y,z+ht*.97),.023,ht*.08,['Blue','Ivory','Orange'][i%3])
    card(x,y-r-.001,z+ht*.41,.069,ht*.43,32+i%16,'Products')
def cup(x,y,z,i):
    cyl((x,y,z+.065),.056,.13,'Cream',r2=.072)
    card(x,y-.064,z+.067,.112,.105,16+i%8,'Products')
    cyl((x,y,z+.135),.076,.016,'Red' if i%2 else 'Yellow')

# 9 x 10 m interior. The front opening is a clear 1.8 m entry.
group('01_Architecture');frame()
solid('Floor',(0,5,-.12),(9.24,10.24,.24),'Grout')
for ix in range(15):
    for iy in range(17): box((-4.2+ix*.6,.3+iy*.6,.008),(.594,.594,.016),'Porcelain')
solid('Wall_Left',(-4.59,5,1.65),(.18,10.2,3.3),'Ivory')
solid('Wall_Right',(4.59,5,1.65),(.18,10.2,3.3),'Ivory')
solid('Wall_Back',(0,10.09,1.65),(9.36,.18,3.3),'Ivory')
for side in (-1,1):box((side*4.48,5,.07),(.04,10,.14),'Charcoal')
box((0,9.98,.07),(9,.04,.14),'Charcoal')
for zz,h,mat in ((2.64,.22,'Green'),(2.80,.07,'Ivory'),(2.91,.13,'Orange'),(3.02,.05,'Ivory')):
    box((0,9.975,zz),(9,.03,h),mat)
    for side in (-1,1):box((side*4.475,5,zz),(.03,10,h),mat)
for z in (.6,1.2,1.8,2.4):
    box((0,9.97,z),(9,.008,.006),'Grout')
    for side in (-1,1):box((side*4.47,5,z),(.008,10,.006),'Grout')
group('02_Glass_Storefront')
for x,w in ((-2.7,3.6),(2.7,3.6)):
    solid('FrontWindow', (x,0,1.35),(w,.045,2.7),'Glass')
    for zz,hh,mat in ((.65,.19,'Green'),(.82,.04,'Ivory'),(.90,.08,'Orange')):box((x,-.03,zz),(w,.025,hh),mat)
    box((x,0,2.76),(w,.09,.09),'Steel')
for x in (-4.5,-2.8,-.9,.9,2.8,4.5):box((x,0,1.4),(.055,.09,2.8),'Steel')
box((0,0,2.84),(9,.12,.16),'Charcoal')
box((0,0,3.08),(9,.12,.25),'Ivory')
card(0,-.075,3.08,1.5,.22,12)
box((0,.07,.022),(1.82,.22,.025),'Steel')
box((0,.75,.022),(1.8,1.12,.025),'Charcoal')
for j in range(28):box((0,.22+j*.039,.038),(1.75,.012,.006),'Steel')
# Sliding glass panels are modeled in their open position outside the clear portal.
for side in (-1,1):
    box((side*1.38,.09,1.32),(.90,.028,2.58),'Glass')
    for x in (side*1.38-.45,side*1.38+.45):box((x,.085,1.32),(.045,.045,2.64),'Charcoal')
    box((side*1.08,.03,1.15),(.023,.08,.42),'Steel')
card(0,.08,2.64,.46,.23,13)

# Register to the left, staff-side shelving, coffee and hot-food equipment.
group('03_Checkout');frame(-3.05,3.85,math.pi/2)
solid('Checkout_Base',(0,0,.46),(4.3,.84,.92),'Ivory')
box((0,0,.97),(4.42,.99,.09),'Cream');box((0,-.431,.22),(4.3,.028,.26),'DeepGreen')
box((0,-.46,.06),(4.3,.03,.12),'Charcoal')
for x in (-1.42,0,1.42):
    box((x,-.46,.57),(1.36,.032,.45),'Ivory');card(x,-.481,.57,.95,.38,14)
box((.25,0,1.05),(.56,.4,.07),'Charcoal')
box((.25,.10,1.20),(.09,.10,.3),'Charcoal')
box((.25,-.01,1.43),(.51,.07,.36),'Charcoal')
box((.25,-.052,1.43),(.445,.008,.29),'Screen')
for j in range(3):
    for k in range(4):box((.1+k*.095,-.058,1.36+j*.065),(.072,.006,.047),'Cream' if j==2 else 'Blue')
box((-.19,-.1,1.07),(.18,.23,.06),'Charcoal');box((.8,-.05,1.05),(.36,.28,.03),'Steel')
box((1.56,.25,1.36),(.79,.05,.70),'Steel')
for xx in (1.19,1.93):box((xx,0,1.36),(.045,.55,.70),'Steel')
box((1.56,0,1.71),(.79,.55,.035),'Steel')
box((1.56,-.285,1.40),(.67,.013,.47),'Glass')
for zz in (1.14,1.32,1.50):
    box((1.56,-.02,zz),(.67,.49,.022),'Steel')
    for j in range(4):cyl((1.31+j*.16,-.10,zz+.055),.059,.085,'Oak')
box((1.56,-.285,1.70),(.76,.025,.10),'Orange');card(1.56,-.30,1.70,.64,.085,3)
box((-1.5,.07,1.32),(.49,.5,.62),'Charcoal')
box((-1.5,-.19,1.40),(.35,.015,.21),'Steel');card(-1.5,-.205,1.40,.29,.16,15)
for x in (-1.60,-1.42):cyl((x,-.18,1.08),.044,.13,'Cream');box((x,-.16,1.24),(.03,.1,.05),'Steel')
for x in (-.83,-.68):
    for j in range(5):cyl((x,.02,1.025+j*.035),.049,.039,'Ivory')
frame(-4.24,4.6,math.pi/2);group('04_BackCounter')
solid('BackCounter',(0,0,.45),(5.0,.42,.9),'Ivory')
box((0,0,.93),(5.1,.48,.05),'Steel')
for z in (1.08,1.37,1.66,1.95,2.24):
    box((0,0,z),(4.9,.39,.03),'Ivory')
    for j in range(27):
        x=-2.34+j*.174;box((x,0,z+.10),(.125,.1,.17),'Cream')
        card(x,-.058,z+.10,.115,.15,40+(j%8),'Products')
frame(-3.6,4.0,math.pi/2);group('05_Hanging_Menu')
for k in range(3):
    x=-1.42+k*1.42
    for xx in (x-.50,x+.50):box((xx,0,2.93),(.019,.019,.68),'Steel')
    box((x,0,2.48),(1.36,.07,.64),'Charcoal');card(x,-.043,2.48,1.25,.56,2+k)

def rack(name,cx,cy,length,width=.90,height=1.62):
    group(name);frame(cx,cy,math.pi/2)
    solid(name+'_Base',(0,0,.085),(length,width,.17),'Charcoal')
    proxy(name+'_ShelfVolume',(0,0,.84),(length,width,1.68))
    box((0,0,height/2),(length,.035,height),'Ivory')
    for x in (-length/2+.035,length/2-.035):box((x,0,height/2),(.055,.065,height),'Steel')
    for level,z in enumerate((.20,.50,.80,1.10,1.40)):
        box((0,0,z),(length,width,.036),'Ivory')
        for side in (-1,1):
            box((0,side*(width/2+.004),z+.015),(length,.025,.067),'Steel')
        n=int(length/.205)
        for side in (-1,1):
            for j in range(n):
                x=-length/2+.12+j*.205;y=side*(width/2-.08)
                if side==1:
                    # Back face UV card, rotated within the furniture frame.
                    oldo,olda=origin,angle
                    p=pt((x,y,0));frame(p[0],p[1],olda+math.pi)
                    if level==0:cup(0,0,z+.025,j)
                    else:bag(0,0,z+.025,(j+level*3)%16)
                    frame(*oldo,olda)
                elif level==0:cup(x,y,z+.025,j)
                else:bag(x,y,z+.025,(j+level*3)%16)
                # Two additional rows keep the shelf visibly stocked in oblique views.
                if level>0:
                    for dep in (.13,.26):bag(x,y-side*dep,z+.025,(j+level*3)%16)
            for j in range(int(length/.40)):
                x=-length/2+.20+j*.40
                if side==-1:card(x,-width/2-.019,z+.017,.27,.044,10+j%2)
                else:
                    oldo,olda=origin,angle;p=pt((x,width/2+.019,0));frame(p[0],p[1],olda+math.pi);card(0,0,z+.017,.27,.044,10+j%2);frame(*oldo,olda)
    # Front end-cap: same visual language as reference, five filled tiers.
    for side in (-1,1):
        frame(cx,cy+side*(length/2+.18),0 if side==-1 else math.pi)
        solid(name+'_Endcap',(0,0,.82),(width,.35,1.64),'Ivory')
        # Stock protrudes forward from the solid back panel.
        for k,z in enumerate((.20,.50,.80,1.10,1.40)):
            box((0,-.19,z),(width,.34,.035),'Ivory')
            for j in range(4):
                if k==0:cup(-width/2+.13+j*.2,-.27,z+.025,j)
                else:bag(-width/2+.13+j*.2,-.27,z+.025,(j+k*2)%16,w=.18)
            box((0,-.372,z), (width,.025,.065),'Steel');card(0,-.388,z,.68,.047,10+k%2)
    frame()
rack('06_Snack_Gondola',-.80,5.15,4.2,1.05)
rack('07_Grocery_Gondola',1.45,6.35,3.25,.95)

def fridge(name,cx,cy,yaw,doors):
    group(name);frame(cx,cy,yaw)
    w=doors*.78
    solid(name+'_Body',(0,.28,1.19),(w,.68,2.38),'Charcoal')
    proxy(name+'_DoorAndShelves',(0,-.29,1.19),(w,.52,2.38))
    box((0,-.071,1.27),(w-.06,.035,2.10),'Ivory')
    box((0,-.08,2.48),(w,.12,.26),'DeepGreen');card(0,-.149,2.48,w-.12,.23,0)
    for d in range(doors):
        x=(d-(doors-1)/2)*.78
        box((x,-.10,.19),(.73,.54,.07),'Steel')
        for level,z in enumerate((.28,.64,1.,1.36,1.72,2.08)):
            box((x,-.28,z),(.72,.45,.025),'Ivory')
            for j in range(6):bottle(x-.30+j*.12,-.37,z+.016,j+d*2+level,ht=.25 if level<4 else .19)
            card(x,-.519,z,.63,.026,11)
        for xx in (x-.37,x+.37):
            box((xx,-.531,1.25),(.024,.027,2.08),'Steel');box((xx+.018,-.494,1.25),(.011,.015,2.0),'CoolLED')
        for z in (.22,2.29):box((x,-.531,z),(.76,.045,.038),'Steel')
        box((x,-.527,1.25),(.70,.006,2.02),'Glass')
        box((x+.27,-.57,1.25),(.018,.025,.54),'Charcoal')
    frame()
fridge('08_Back_Drink_Refrigerators',2.46,9.40,0,5)
fridge('09_Right_Drink_Refrigerators',4.04,7.54,-math.pi/2,5)

group('10_Ready_Meal_Case');frame(4.04,4.62,-math.pi/2)
solid('ReadyMealCase',(0,.1,1.03),(1.52,.66,2.06),'Steel')
proxy('ReadyMealShelves',(0,-.43,1.03),(1.52,.40,2.06))
box((0,-.248,1.12),(1.40,.025,1.76),'Charcoal')
for k,z in enumerate((.30,.66,1.02,1.38,1.74)):
    box((0,-.40,z),(1.45,.45,.038),'Ivory')
    for j in range(5):
        x=-.57+j*.285
        box((x,-.46,z+.105),(.235,.12,.17),'Charcoal');card(x,-.523,z+.107,.215,.15,56+j,'Products')
    box((0,-.626,z+.014),(1.46,.025,.06),'Steel');card(0,-.642,z+.018,1.30,.041,11)
box((0,-.20,2.22),(1.52,.17,.27),'DeepGreen');card(0,-.29,2.22,1.43,.235,1)
frame()
group('11_IceCream_Freezer');frame(3.13,1.79,0)
card(0,-.007,.52,.94,.71,5)
frame()

group('11_IceCream_Freezer');frame(3.13,2.85,math.pi/2)
solid('Freezer',(0,0,.47),(2.10,1.03,.94),'Ivory')
box((0,0,.075),(2.12,1.05,.15),'Charcoal');card(0,-.521,.51,1.95,.72,5)
box((0,0,.94),(2.13,1.07,.07),'Steel')
box((0,0,.982),(1.94,.88,.027),'Charcoal')
for x in (-.52,.52):
    box((x,0,1.015),(.99,.97,.028),'Glass')
    for yy in (-.49,.49):box((x,yy,1.03),(1.03,.033,.033),'Steel')
    for xx in (x-.5,x+.5):box((xx,0,1.03),(.03,.97,.03),'Steel')
    box((x,-.34,1.065),(.26,.027,.035),'Steel')
for j in range(9):
    for k in range(4):
        x=-.88+j*.217;y=-.33+k*.215
        box((x,y,1.001),(.18,.18,.018),'Cream');topcard(x,y,1.013,.18,.18,48+(j+k)%8)
frame()

group('12_Back_Ramen_Shelves');frame(-.80,9.67,0)
solid('RamenShelf',(0,0,.08),(1.62,.45,.16),'Charcoal');box((0,.17,1.1),(1.6,.03,2.2),'Ivory')
proxy('RamenShelfVolume',(0,0,1.12),(1.64,.47,2.24))
for k,z in enumerate((.22,.56,.90,1.24,1.58,1.92)):
    box((0,0,z),(1.64,.47,.035),'Ivory')
    for j in range(9):cup(-.70+j*.176,-.10,z+.025,j+k)
    card(0,-.246,z,1.46,.045,10)
box((0,0,2.25),(1.65,.1,.23),'DeepGreen');card(0,-.06,2.25,1.55,.20,8)
frame()
group('13_Staff_Door');frame()
box((-2.70,9.96,1.09),(1.02,.07,2.18),'Steel')
box((-2.70,9.907,1.09),(.88,.025,2.06),'Ceiling');card(-2.70,9.888,1.59,.36,.17,7)
box((-2.37,9.87,1.0),(.13,.05,.035),'Charcoal')
box((-2.70,9.85,2.06),(.42,.13,.055),'Steel');card(-4.0,9.965,1.55,.64,1.08,6)

group('14_Window_Seating');frame(2.82,.75,0)
solid('Table',(0,0,.76),(1.7,.59,.065),'Oak')
for x in (-.69,.69):
    for y in (-.20,.20):solid('TableLeg',(x,y,.38),(.035,.035,.76),'Charcoal')
for x in (-.48,.48):
    solid('ChairSeat',(x,.60,.445),(.42,.40,.055),'Lime')
    solid('ChairBack',(x,.80,.71),(.42,.035,.43),'Lime')
    for xx in (x-.17,x+.17):
        for yy in (.45,.76):box((xx,yy,.225),(.023,.023,.45),'Steel')
frame()
group('15_Entrance_Display');frame(-1.80,.74,0)
solid('WaterStand',(0,0,.22),(.60,.53,.44),'Ivory');card(0,-.276,.26,.53,.26,14)
for x in (-.19,0,.19):
    for y in (-.12,.12):bottle(x,y,.45,1,.36)
frame()
group('16_Ceiling');frame()
box((0,5,3.35),(9.15,10.15,.10),'Ceiling')
for x in range(16):box((-4.5+x*.6,5,3.29),(.009,10,.012),'Grout')
for y in range(18):box((0,y*.6,3.29),(9,.009,.012),'Grout')
group('17_Fluorescent_Fixtures')
for x in (-3,-.7,1.65,3.75):
    for y in (1.55,4.15,6.75,9.15):
        box((x,y,3.245),(.18,1.6,.07),'Ivory')
        for xx in (x-.048,x+.048):box((xx,y,3.194),(.037,1.49,.035),'WhiteLED')
        LIGHTS.append(dict(position=[x,3.10,y],range=3.5,intensity=1.8))
group('18_Cassette_AC')
for y in (3.0,7.6):
    box((.45,y,3.245),(1.10,1.10,.10),'Ivory');box((.45,y,3.181),(.69,.69,.024),'Charcoal')
    for j in range(17):box((.13+j*.04,y,3.162),(.015,.65,.014),'Steel')
    for side in (-1,1):
        box((.45+side*.47,y,3.184),(.065,.88,.026),'Charcoal')
        box((.45,y+side*.47,3.184),(.88,.065,.026),'Charcoal')
group('19_Security_Cameras')
for x,y in ((-4.10,1.0),(4.12,9.5)):
    cyl((x,y,3.19),.09,.05,'Ivory');cyl((x,y,3.12),.067,.10,'Charcoal',r2=.085)
frame()

# Material-batched meshes, named by semantic group, with editable bevels on furniture.
for (name,mat),(v,f,uvs) in GEO.items():
    me=bpy.data.meshes.new(name+'_'+mat);me.from_pydata(v,[],f);me.update()
    uv=me.uv_layers.new(name='UVMap')
    for poly,coords in zip(me.polygons,uvs):
        for li,co in zip(poly.loop_indices,coords):uv.data[li].uv=co
    ob=bpy.data.objects.new(name+'__'+mat,me);bpy.context.collection.objects.link(ob);me.materials.append(MAT[mat])
    ob['semantic_group']=name
    if mat not in ('Products','Signs','Glass','WhiteLED','CoolLED','Grout'):
        mod=ob.modifiers.new('Soft manufactured edges','BEVEL');mod.width=.007 if mat!='Porcelain' else .003;mod.segments=2
        mod=ob.modifiers.new('Weighted face normals','WEIGHTED_NORMAL');mod.keep_sharp=True

# Stage lights and two photographic reference viewpoints.
sc=bpy.context.scene;sc.render.engine='CYCLES';sc.cycles.samples=32
sc.cycles.use_denoising=True;sc.cycles.max_bounces=6
sc.world.color=(.22,.22,.22)
sc.world.use_nodes=True;sc.world.node_tree.nodes['Background'].inputs[0].default_value=(.58,.69,.78,1);sc.world.node_tree.nodes['Background'].inputs[1].default_value=.28
def area(name,p,power,size,color,target):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
    ob=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(ob);ob.location=p;ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
area('Daylight through storefront',(-2,-3,4),1100,6,(1,.86,.67),(0,4,0))
for x in (-2.4,2.4):
    for y in (2,5.5,8.8):area('Ceiling soft illumination',(x,y,3.10),135,2.7,(1,.95,.84),(x,y,0))
def camera(name,pos,target,lens):
    data=bpy.data.cameras.new(name);ob=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(ob);ob.location=pos
    ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler();data.lens=lens;data.clip_start=.04;return ob
c1=camera('01_Entrance_View',(-.10,.30,1.95),(-.2,6.4,1.28),20)
c2=camera('02_Reverse_View',(.03,9.15,2.05),(.15,2.8,1.1),19)
c3=camera('03_Cutaway_View',(13,-14,15),(0,5,.8),43)
sc.camera=c1;sc.render.resolution_x=1440;sc.render.resolution_y=1080;sc.render.resolution_percentage=100
sc.view_settings.view_transform='AgX'
sc.view_settings.look='AgX - Medium High Contrast'
sc.view_settings.exposure=-.4
# Native Unity stream. Each polygon corner carries its own normal + UV.
deps=bpy.context.evaluated_depsgraph_get();parts=[]
for ob in list(sc.objects):
    if ob.type!='MESH':continue
    ev=ob.evaluated_get(deps);me=ev.to_mesh();me.calc_loop_triangles()
    verts=[];normals=[];uvs=[];indices=[];lookup={}
    for tri in me.loop_triangles:
        ids=[]
        for li in tri.loops:
            co=me.vertices[me.loops[li].vertex_index].co;no=me.corner_normals[li].vector;uv=me.uv_layers.active.data[li].uv
            key=tuple(round(a,6) for a in (*co,*no,*uv))
            if key not in lookup:
                lookup[key]=len(verts)//3;verts.extend(round(a,5) for a in (co.x,co.z,co.y));normals.extend(round(a,5) for a in (no.x,no.z,no.y));uvs.extend(round(a,5) for a in uv)
            ids.append(lookup[key])
        indices.extend((ids[0],ids[2],ids[1]))
    parts.append(dict(name=ob.name,group=ob['semantic_group'],material=ob.data.materials[0].name,vertices=verts,normals=normals,uvs=uvs,triangles=indices))
    ev.to_mesh_clear()
payload=dict(name='ConvenienceStoreInterior',materials=SPEC,parts=parts,colliders=COLL,lights=LIGHTS,spawn=[0,.05,1.15],size=[9,3.3,10])
with gzip.open(HERE/'ConvenienceStore.meshdata.json.gz','wt',encoding='utf-8') as stream:json.dump(payload,stream,separators=(',',':'))
report=dict(meshParts=len(parts),triangles=sum(len(p['triangles'])//3 for p in parts),materials=len(SPEC),colliders=len(COLL),dimensionsMetres=[9,10,3.3],entryWidth=1.8)
(HERE/'model-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(report,flush=True)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'ConvenienceStoreInterior.blend'))
bpy.ops.export_scene.gltf(filepath=str(HERE/'ConvenienceStoreInterior.glb'),export_format='GLB',export_cameras=False,export_lights=False)
if '--no-render' not in sys.argv:
    for cam,name in ((c1,'01_Entrance'),(c2,'02_Reverse')):
        sc.camera=cam;sc.render.filepath=str(PREVIEW/(name+'.png'));bpy.ops.render.render(write_still=True)
    for ob in sc.objects:
        if ob.type=='MESH' and ob.name.startswith(('16_','17_','18_','19_','02_')):ob.hide_render=True
        if ob.type=='MESH' and ob.name.startswith('01_'):
            # Cutaway is a separate presentation render; full source remains enclosed.
            ob.hide_render=True
    # Keep a simple platform for the cutaway presentation.
    bpy.ops.mesh.primitive_cube_add(size=1,location=(0,5,-.14));platform=bpy.context.object;platform.scale=(9.2,10.2,.20);platform.data.materials.append(MAT['Porcelain'])
    sc.camera=c3;sc.render.filepath=str(PREVIEW/'03_Cutaway.png');bpy.ops.render.render(write_still=True)
print('CONVENIENCE_INTERIOR_COMPLETE',flush=True)
