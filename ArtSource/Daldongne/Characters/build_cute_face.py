"""Toy-like face authoring, executed by build_female_player.py in Blender."""

# Keep the cap/scalp fit while shortening the lower face into a broad soft U.
FACE_PROFILE=[
    (1.671,.006,.001,.001),(1.656,.006,.092,.074),
    (1.619,.006,.158,.134),(1.563,.009,.204,.174),
    (1.500,.012,.224,.182),(1.435,.014,.228,.179),
    (1.385,.018,.231,.176),(1.345,.024,.217,.170),
    (1.305,.029,.186,.150),(1.279,.032,.139,.119),
    (1.261,.035,.074,.064),(1.253,.036,.003,.004)]

def profile_tangent(index,component):
    if index==0:return (FACE_PROFILE[1][component]-FACE_PROFILE[0][component])/(FACE_PROFILE[1][0]-FACE_PROFILE[0][0])
    if index==len(FACE_PROFILE)-1:return (FACE_PROFILE[-1][component]-FACE_PROFILE[-2][component])/(FACE_PROFILE[-1][0]-FACE_PROFILE[-2][0])
    a,b,c=FACE_PROFILE[index-1:index+2]
    left=(b[component]-a[component])/(b[0]-a[0]);right=(c[component]-b[component])/(c[0]-b[0])
    # Same-sign harmonic slopes keep a smooth silhouette without overshooting.
    return 0 if left*right<=0 else 2*left*right/(left+right)

def face_profile(y):
    for index,(a,b) in enumerate(zip(FACE_PROFILE,FACE_PROFILE[1:])):
        if a[0]>=y>=b[0]:
            f=(a[0]-y)/(a[0]-b[0])
            h=b[0]-a[0]
            return tuple((2*f**3-3*f*f+1)*a[k]+(f**3-2*f*f+f)*h*profile_tangent(index,k)
                         +(-2*f**3+3*f*f)*b[k]+(f**3-f*f)*h*profile_tangent(index+1,k) for k in (1,2,3))
    return FACE_PROFILE[0][1:] if y>FACE_PROFILE[0][0] else FACE_PROFILE[-1][1:]

def g(x,y,cx,cy,rx,ry):return math.exp(-((x-cx)/rx)**2-((y-cy)/ry)**2)
def face_z(x,y):
    cz,rx,rz=face_profile(y)
    # A broad front avoids raised eyeballs and keeps both round irises readable.
    t=max(0,min(1,(y-1.285)/.085));p=2.5+t*t*(3-2*t)
    z=cz+rz*max(0,1-abs(x/max(.001,rx))**p)**(1/p)
    return z+.004*g(abs(x),y,.128,1.346,.056,.032)+.009*g(x,y,0,1.367,.017,.014)+.002*g(x,y,0,1.330,.050,.023)

angles=[-math.pi/2+math.pi*i/48 for i in range(49)]
angles += [math.pi/2+math.pi*i/16 for i in range(1,16)]
heights=[1.671,1.656,1.635,1.611,1.587,1.563,1.539,1.515,1.495,1.475]
heights += [1.455-.009*i for i in range(22)]
heights += [1.260,1.256,1.253]
vs=[];fs=[];n=len(angles)
for y in heights:
    cz,rx,rz=face_profile(y)
    for a in angles:
        x=rx*math.sin(a)
        z=face_z(x,y) if math.cos(a)>0 else cz+rz*math.cos(a)
        vs.append((x,y,z))
for j in range(len(heights)-1):
    for i in range(n):
        a=j*n+i;b=j*n+(i+1)%n;fs.append((a,b,b+n,a+n))
fs += [tuple(range(n-1,-1,-1)),tuple((len(heights)-1)*n+i for i in range(n))]
mesh('Face - soft toy cheeks and short rounded chin',vs,fs,'Skin','Head',True)

def front_surface(name,vertices,faces,mat):
    ob=mesh(name,vertices,faces,mat,'Head',True)
    bm=bmesh.new();bm.from_mesh(ob.data)
    for f in bm.faces:
        if f.normal.y>0:f.normal_flip()
    bm.to_mesh(ob.data);bm.free();ob.data.update();return ob

def surface_disk(name,cx,cy,rx,ry,mat,offset=.001):
    vertices=[];faces=[];count=24
    for r in (0,.35,.7,1):
        for i in range(count):
            a=math.tau*i/count;x=cx+rx*r*math.cos(a);y=cy+ry*r*math.sin(a)
            vertices.append((x,y,face_z(x,y)+offset))
    for j in range(3):
        for i in range(count):
            a=j*count+i;b=j*count+(i+1)%count;faces.append((a,b,b+count,a+count))
    front_surface(name,vertices,faces,mat)

loft('Neck',[(0,1.18,0,.075,.068),(0,1.26,0,.068,.061)],'Skin',n=12)
for s in (-1,1):
    label='Left' if s<0 else 'Right'
    ellipsoid(label+' ear',(s*.222,1.406,-.005),(.041,.062,.034),'Skin','Head',12,6,True)
    ellipsoid(label+' ear inset',(s*.241,1.408,.021),(.015,.029,.011),'Rose','Head',8,6,True)
    ex=s*.095;ey=1.407;rx=.046;ry=.042
    ix=ex+.003;iy=ey+.003;count=32
    def eye_z(x,y):
        u=(x-ex)/rx;v=(y-ey)/ry
        return face_z(x,y)+.0018+.003*max(0,1-u*u-v*v)
    boundary=[]
    for i in range(count):
        a=math.tau*i/count;low=0;high=rx*2
        for iteration in range(24):
            r=(low+high)/2;x=ix+r*math.cos(a);y=iy+r*math.sin(a)
            if ((x-ex)/rx)**2+((y-ey)/ry)**2<=1:low=r
            else:high=r
        boundary.append(low)
    pupil=[.032]*count;iris=[.038]*count
    def band(name,inner,outer,mat,steps):
        vertices=[];faces=[]
        for j in range(steps+1):
            for i in range(count):
                a=math.tau*i/count;r=inner[i]+(outer[i]-inner[i])*j/steps
                x=ix+r*math.cos(a);y=iy+r*math.sin(a)
                vertices.append((x,y,eye_z(x,y)))
        for j in range(steps):
            for i in range(count):
                a=j*count+i;b=j*count+(i+1)%count;faces.append((a,b,b+count,a+count))
        front_surface(label+' '+name,vertices,faces,mat)
    band('round gentle pupil',[0]*count,pupil,'EyeInk',2)
    band('chocolate brown iris',pupil,iris,'EyeIris',2)
    band('ivory eye crescent',iris,boundary,'EyeSpark',2)
    # One upper contour and two tiny outer lashes, without heavy eye sockets.
    edge=[];steps=18
    for i in range(steps+1):
        a=.08+(math.pi-.16)*i/steps;x=ex+rx*math.cos(a);y=ey+ry*math.sin(a)
        edge.append((x,y,eye_z(x,y)+.001))
    tube(label+' soft upper eyelid',edge,[.001+.0012*math.sin(math.pi*i/steps) for i in range(steps+1)],'Hair','Head',5)
    for k,a in enumerate((.40,.64)):
        x=ex+s*rx*math.cos(a);y=ey+ry*math.sin(a);z=eye_z(x,y)+.001
        tube(label+' short eyelash '+str(k),[(x,y,z),(x+s*.004,y+.004,z+.0006),(x+s*.006,y+.007,z+.001)],
             [.0018,.0012,.0002],'Hair','Head',4)
    x=ix-.011;y=iy+.015
    ellipsoid(label+' large eye catchlight',(x,y,eye_z(x,y)+.0015),(.006,.0065,.0008),'EyeSpark','Head',10,5,True)
    x=ix+.010;y=iy-.011
    ellipsoid(label+' tiny eye catchlight',(x,y,eye_z(x,y)+.001),(.0017,.0018,.0006),'EyeSpark','Head',8,4,True)
    surface_disk(label+' soft cheek blush',s*.142,1.350,.030,.015,'SkinWarm',.0013)
    # Small quiet brows stay below the cap without a mature arched expression.
    points=[]
    for i in range(9):
        t=i/8;x=s*(.069+.052*t);y=1.463+.004*math.sin(math.pi*t)-.003*t
        points.append((x,y,face_z(x,y)+.001))
    tube(label+' soft eyebrow',points,[.0005+.0011*math.sin(math.pi*i/8) for i in range(9)],'HairHighlight','Head',4)

# A peach button nose and tiny soft caret mouth follow the supplied toy reference.
ellipsoid('Small peach button nose',(0,1.366,face_z(0,1.366)+.0005),(.010,.0075,.0045),'LipGlow','Head',12,6,True)
mouth=[]
for i in range(13):
    x=-.014+.028*i/12;y=1.328+.0055*math.cos(x/.014*math.pi/2)
    mouth.append((x,y,face_z(x,y)+.0015))
tube('Tiny soft caret mouth',mouth,[.0005+.00075*math.sin(math.pi*i/12) for i in range(13)],'HairHighlight','Head',5)
