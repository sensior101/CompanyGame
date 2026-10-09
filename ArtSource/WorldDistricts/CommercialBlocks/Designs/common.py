"""Shared construction tools; each of the eight buildings has its own authored layout."""
import bpy, bmesh, math, json, random
from pathlib import Path
from mathutils import Vector

BASE=Path(__file__).resolve().parents[1]
RECT=[(-6,-4),(6,-4),(6,4),(-6,4)]
FRONT=((-6,-4),(6,-4)); RIGHT=((6,-4),(6,4))
BACK=((6,4),(-6,4)); LEFT=((-6,4),(-6,-4))

class Building:
    def __init__(self,number,label,finish,body_color,body_texture,metal=(.045,.051,.055)):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.scene=bpy.context.scene;self.scene.unit_settings.system='METRIC';self.scene.unit_settings.scale_length=1
        self.number=number;self.id=f'CB_{number:02d}';self.label=label;self.finish_label=finish
        self.collection=bpy.data.collections.new(self.id+'_ASSET');self.scene.collection.children.link(self.collection)
        self.root=self.empty(self.id);self.root['Storeys']=3;self.root['DimensionsMetres']='12 x 8';self.root['DesignNumber']=number
        self.groups={name:self.empty(name,self.root) for name in ['Structure','Facade','Frames','Glass','InteriorHint','Terraces','Planting','RoofEquipment','Signage','Entrance']}
        self.materials={};self.signs=[]
        self.material('wall',body_color,rough=.79,texture=body_texture)
        self.material('metal',metal,metal=.4,rough=.34)
        self.material('frame',(.024,.029,.032),metal=.55,rough=.25)
        self.material('glass',(.87,.925,.94),metal=0,rough=.055)
        shader=self.materials['glass'].node_tree.nodes.get('Principled BSDF');shader.inputs['Transmission Weight'].default_value=1;shader.inputs['IOR'].default_value=1.46
        self.materials['glass']['UnitySurface']='Transparent';self.materials['glass']['UnityAlpha']=.29
        self.material('wood',(.39,.215,.10),rough=.58,texture='Oak')
        self.material('inside',(.53,.51,.46),rough=.83)
        self.material('floor',(.29,.31,.31),rough=.82,texture='stone')
        self.material('roof',(.21,.225,.23),rough=.88)
        self.material('joint',(.12,.13,.13),rough=.86)
        self.material('steel',(.38,.40,.40),metal=.65,rough=.32)
        self.material('blind',(.39,.40,.38),rough=.80)
        self.material('lamp',(.88,.82,.67),rough=.35)
        sh=self.materials['lamp'].node_tree.nodes.get('Principled BSDF');sh.inputs['Emission Color'].default_value=(1,.88,.67,1);sh.inputs['Emission Strength'].default_value=2.2
        self.material('soil',(.085,.06,.037),rough=1)
        self.material('leaf',(.14,.235,.074),rough=.72)
        self.material('leaf_light',(.25,.34,.12),rough=.72)
        self.material('sign',metal,metal=.15,rough=.42)

    def empty(self,name,parent=None):
        o=bpy.data.objects.new(name,None);self.collection.objects.link(o);o.parent=parent;return o

    def material(self,key,color,metal=0,rough=.6,texture=None):
        m=bpy.data.materials.new(self.id+'_'+key);m.use_nodes=True;m.diffuse_color=(*color,1)
        p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
        if texture:
            path=BASE/'Exports/Textures'/f'{texture}.png'
            if path.exists():
                im=bpy.data.images.load(str(path),check_existing=True);tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im
                m.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color']);p.inputs['Base Color'].default_value=(1,1,1,1);m['TextureMap']=texture
                bump=m.node_tree.nodes.new('ShaderNodeBump');bump.inputs['Distance'].default_value=.002;bump.inputs['Strength'].default_value=.12
                m.node_tree.links.new(tex.outputs['Color'],bump.inputs['Height']);m.node_tree.links.new(bump.outputs[0],p.inputs['Normal'])
        self.materials[key]=m;return m

    def mesh(self,name,vertices,faces,material,group='Structure',parent=None,uv_image=False):
        me=bpy.data.meshes.new(name);me.from_pydata(vertices,[],faces);me.update();uv=me.uv_layers.new(name='UVMap')
        for poly in me.polygons:
            axes=[i for i in range(3) if i!=max(range(3),key=lambda k:abs(poly.normal[k]))]
            for j,li in enumerate(poly.loop_indices):
                v=me.vertices[me.loops[li].vertex_index].co
                uv.data[li].uv=[(0,0),(1,0),(1,1),(0,1)][j%4] if uv_image else (v[axes[0]],v[axes[1]])
        ob=bpy.data.objects.new(name,me);self.collection.objects.link(ob);ob.parent=parent or self.groups[group];me.materials.append(self.materials[material]);return ob

    def box(self,name,loc,size,material='wall',group='Structure',rotation=0,bevel=.016,parent=None):
        bm=bmesh.new();bmesh.ops.create_cube(bm,size=1)
        for v in bm.verts:
            for k in range(3):v.co[k]*=size[k]
        if bevel and min(size)>.027:bmesh.ops.bevel(bm,geom=list(bm.edges),offset=min(bevel,min(size)*.20),segments=2,affect='EDGES')
        bm.normal_update();me=bpy.data.meshes.new(name);bm.to_mesh(me);bm.free();me.update()
        verts=[tuple(v.co) for v in me.vertices];faces=[tuple(p.vertices) for p in me.polygons];bpy.data.meshes.remove(me)
        ob=self.mesh(name,verts,faces,material,group,parent);ob.location=loc;ob.rotation_euler.z=rotation;return ob

    def cylinder(self,name,loc,radius,depth,material='steel',group='RoofEquipment',axis='Z'):
        n=24;verts=[(math.cos(i*math.tau/n)*radius,math.sin(i*math.tau/n)*radius,z) for z in (-depth/2,depth/2) for i in range(n)]
        faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        ob=self.mesh(name,verts,faces,material,group);ob.location=loc
        if axis=='Y':ob.rotation_euler.x=math.pi/2
        if axis=='X':ob.rotation_euler.y=math.pi/2
        return ob

    @staticmethod
    def basis(a,b):
        d=Vector((b[0]-a[0],b[1]-a[1],0));length=d.length;u=d.normalized();n=Vector((u.y,-u.x,0));return u,n,length,math.atan2(u.y,u.x)

    def position(self,a,b,t,z,off=0):
        u,n,_,_=self.basis(a,b);return Vector((a[0],a[1],z))+u*t+n*off

    def strip(self,name,a,b,t,w,z,h,material='wall',depth=.30,off=0,group='Facade',bevel=.013):
        return self.box(name,self.position(a,b,t,z,off),(w,depth,h),material,group,self.basis(a,b)[3],bevel)

    def slab(self,name,z,thickness,material='wall',points=None,group='Structure',scale=1):
        p=[(x*scale,y*scale) for x,y in (points or RECT)];n=len(p)
        verts=[(x,y,z) for x,y in p]+[(x,y,z+thickness) for x,y in p]
        faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        return self.mesh(name,verts,faces,material,group)

    def glass(self,name,a,b,t,w,z,h,off=-.03,group='Glass',parent=None):
        u,n,_,_=self.basis(a,b);verts=[]
        for offset in (off,off-.009):
            verts += [tuple(self.position(a,b,q,r,offset)) for q,r in [(t-w/2,z),(t+w/2,z),(t+w/2,z+h),(t-w/2,z+h)]]
        return self.mesh(name,verts,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],'glass',group,parent,True)

    def curve_band(self,name,curve,z,h,material='wall',depth=.3,off=0,group='Facade'):
        center=Vector((curve[0][0],curve[-1][1],0));n=len(curve);verts=[]
        for level in (z,z+h):
            for delta in (off+depth/2,off-depth/2):
                for x,y in curve:
                    p=Vector((x,y,0));p+=(p-center).normalized()*delta;verts.append((p.x,p.y,level))
        faces=[]
        for i in range(n-1):
            j=i+1;faces.extend([(i,j,j+2*n,i+2*n),(i+n,i+3*n,j+3*n,j+n),(i+2*n,j+2*n,j+3*n,i+3*n),(j,i,i+n,j+n)])
        faces.extend([(0,2*n,3*n,n),(n-1,2*n-1,4*n-1,3*n-1)])
        ob=self.mesh(name,verts,faces,material,group)
        for poly in ob.data.polygons:
            if poly.index<(n-1)*4 and poly.index%4<2:poly.use_smooth=True
        return ob

    def glazing(self,a,b,t,w,z,h,grid=1.25,off=-.04,crossbar=None,blinds=False,outer=True):
        self.glass('Glazing',a,b,t,w,z,h,off)
        count=max(1,round(w/grid))
        for i in range(count+1):self.strip('Window_Mullion',a,b,t-w/2+w*i/count,.065,z+h/2,h+.075,'frame',.095,off+.03,'Frames')
        for level in (z,z+h):self.strip('Window_Transom',a,b,t,w+.065,level,.075,'frame',.10,off+.03,'Frames')
        if crossbar:self.strip('Window_Crossbar',a,b,t,w,z+crossbar,.045,'frame',.10,off+.03,'Frames')
        if outer:
            for q in (t-w/2,t+w/2):self.strip('Window_Perimeter',a,b,q,.075,z+h/2,h+.075,'frame',.05,.165,'Frames')
            for level in (z,z+h):self.strip('Window_Perimeter',a,b,t,w+.075,level,.075,'frame',.05,.165,'Frames')
            self.strip('Window_DripSill',a,b,t,w+.15,z-.048,.06,'metal',.30,.035,'Frames')
        if blinds:
            seed=int(t*17+z*29+self.number);amount=5+seed%13
            for k in range(amount):self.strip('Blind_Slat',a,b,t,w-.09,z+h-.09-k*.065,.035,'blind',.029,off-.16,'InteriorHint',0)

    def wall(self,a,b,z0,z1,openings,material='wall',depth=.32,off=0):
        length=self.basis(a,b)[2];cursor=0
        for t,w,bottom,height in sorted(openings):
            left,right=t-w/2,t+w/2
            if left>cursor+.001:self.strip('Wall_Pier',a,b,(cursor+left)/2,left-cursor,(z0+z1)/2,z1-z0,material,depth,off)
            if bottom>z0+.001:self.strip('Wall_Spandrel',a,b,t,w,(z0+bottom)/2,bottom-z0,material,depth,off)
            top=bottom+height
            if top<z1-.001:self.strip('Wall_Lintel',a,b,t,w,(top+z1)/2,z1-top,material,depth,off)
            cursor=right
        if cursor<length-.001:self.strip('Wall_Pier',a,b,(cursor+length)/2,length-cursor,(z0+z1)/2,z1-z0,material,depth,off)

    def concrete_joints(self,a,b,z0,z1,openings,spacing=1.4,off=.166):
        length=self.basis(a,b)[2]
        for i in range(1,int(length/spacing)+1):
            t=i*spacing
            if t>=length-.03:continue
            segments=[(z0,z1)]
            for c,w,z,h in openings:
                if abs(t-c)<w/2+.05:segments=[(lo,min(hi,z)) for lo,hi in segments if lo<z]+[(max(lo,z+h),hi) for lo,hi in segments if hi>z+h]
            for lo,hi in segments:
                if hi>lo+.02:self.strip('Stone_PanelJoint',a,b,t,.006,(lo+hi)/2,hi-lo,'joint',.003,off,bevel=0)

    def ribbed(self,a,b,t,w,z,h,material='metal',off=.18,spacing=.14):
        self.strip('Ribbed_Back',a,b,t,w,z,h,material,.10,off)
        count=max(1,int(w/spacing))
        for i in range(count):self.strip('Vertical_Rib',a,b,t-w/2+(i+.5)*w/count,.026,z,h,material,.06,off+.065,bevel=0)

    def frame_bands(self,material='wall',points=None,width=.42):
        self.slab('Plinth',-.12,.12,'floor',points,scale=1.025)
        for z in (4,7.2,10.4):self.slab('Floor_Ribbon',z-width,width,material,points)

    def ground(self,points=None,wood=False,curve=None,fascia='metal'):
        # All storefronts retain the same unobstructed 1.8 m entrance and separate hinges.
        a,b=FRONT
        frontLength=curve[0][0]+6 if curve else 12
        sideA=curve[-1] if curve else RIGHT[0]
        sideLength=4-sideA[1]
        for left,right in [(0,5.1),(6.9,frontLength)]:
            self.glazing(a,b,(left+right)/2,right-left,.18,3.32,1.25,off=.025,crossbar=2.75,outer=False)
            self.strip('Store_Sill',a,b,(left+right)/2,right-left,.085,.17,'metal',.21,.0,'Frames')
        self.glazing(a,b,6,1.8,2.78,.72,1.8,off=.025,outer=False)
        for j,hinge in enumerate((-0.9,.9)):
            leaf=self.empty(f'Door_Leaf_{j+1:02d}',self.groups['Entrance']);leaf.location=(hinge,-4.075,0)
            x=hinge+(0.45 if j==0 else -.45)
            pieces=[]
            for q in (x-.45,x+.45):pieces.append(self.box('Door_Jamb',(q,-4.075,1.37),(.045,.082,2.71),'frame','Entrance'))
            for z in (.055,2.71):pieces.append(self.box('Door_Rail',(x,-4.075,z),(.90,.082,.055),'frame','Entrance'))
            pieces.append(self.glass('Door_Glass',(-6,-4.075),(6,-4.075),x+6,.855,.085,2.60,0,'Entrance'))
            pieces.append(self.box('Door_Handle',(x+(.30 if j==0 else -.30),-4.145,1.31),(.03,.055,.36),'steel','Entrance'))
            bpy.context.view_layer.update()
            for ob in pieces:
                matrix=ob.matrix_world.copy();ob.parent=leaf;ob.matrix_world=matrix
        self.box('Entrance_Threshold',(0,-4.04,.018),(1.8,.29,.036),'steel','Entrance')
        for name,pos in [('EntryAnchor',(0,-4.9,0)),('InteriorSpawnAnchor',(0,-2.8,.045))]:self.empty(name,self.groups['Entrance']).location=pos
        self.glazing(sideA,RIGHT[1],sideLength/2,sideLength,.18,3.32,1.30,off=.025,crossbar=2.75,outer=False)
        if curve:
            self.curve_band('Curved_StoreGlass',curve,.18,3.32,'glass',.009,.025,'Glass')
            for i,(p,q) in enumerate(zip(curve,curve[1:])):
                L=self.basis(p,q)[2]
                for level in (.18,3.04,3.50):self.strip('Curve_Transom',p,q,L/2,L,level,.055,'frame',.085,.045,'Frames',0)
                if i%3==0:self.strip('Curve_Mullion',p,q,0,.055,1.84,3.38,'frame',.09,.045,'Frames',0)
        for a,b in (BACK,LEFT):self.wall(a,b,0,3.78,[])
        groundEdges=[(FRONT[0],(frontLength-6,-4)),(sideA,RIGHT[1])]
        for a,b in groundEdges:self.strip('Shop_SignFascia',a,b,self.basis(a,b)[2]/2,self.basis(a,b)[2],3.70,.38,fascia,.28,.07,'Facade')
        if curve:self.curve_band('Round_SignFascia',curve,3.51,.38,fascia,.28,.07)
        self.box('Rear_ServiceDoor',(3.9,4.18,1.15),(1.02,.055,2.25),'metal','Facade')
        self.box('Rear_Handle',(3.53,4.23,1.07),(.04,.06,.25),'steel','Facade')
        self.add_signs(curve)

    def add_signs(self,curve=None):
        slots=[('Sign_Main_01',*FRONT,2.65,3.7),('Sign_Main_02',*FRONT,9.20,3.7),('Sign_Corner',*RIGHT,.85,1.3),('Sign_Side',*RIGHT,4.8,3.7)]
        if curve:slots=[('Sign_Main_01',*FRONT,2.4,3.5),('Sign_Main_02',*FRONT,7.6,3.5),('Sign_Corner',curve[-1],RIGHT[1],.8,1.3),('Sign_Side',curve[-1],RIGHT[1],3.8,2.8)]
        for name,a,b,t,w in slots:
            anchor=self.empty(name,self.groups['Signage']);anchor.location=self.position(a,b,t,3.70,.28);anchor.rotation_euler.z=self.basis(a,b)[3]
            anchor['WidthMetres']=w;anchor['HeightMetres']=.32
            vertices=[tuple(self.position(a,b,q,z,.224)) for q,z in [(t-w/2,3.54),(t+w/2,3.54),(t+w/2,3.86),(t-w/2,3.86)]]
            ob=self.mesh(name+'_Board',vertices,[(0,1,2,3)],'sign','Signage',uv_image=True)
            bpy.context.view_layer.update();matrix=ob.matrix_world.copy();ob.parent=anchor;ob.matrix_world=matrix
            self.signs.append({'name':name,'width':w,'height':.32,'position':list(anchor.location)})

    def interiors(self,points=None):
        for level,z in enumerate((0,4,7.2)):
            self.slab('Interior_Floor',z,.018,'floor',points)
            self.box('Interior_Back',(-.4,3.4,z+1.7),(10.5,.11,3.2),'inside','InteriorHint')
            self.box('Interior_Ceiling',(0,1.65,z+3.02),(10.5,3.75,.06),'inside','InteriorHint')
            # Furnishing hints stay in the inner half, leaving terrace cut-outs clear.
            for j,x in enumerate((-3.3,.2,3.3)):
                if (j+level+self.number)%3==0:
                    for h in (.42,1.16,1.90,2.50):self.box('Interior_Shelf',(x,3.16,z+h),(1.8,.35,.04),'wood','InteriorHint')
                else:
                    self.box('Interior_Table',(x,1.8,z+.74),(1.6,.75,.065),'wood','InteriorHint')
                    for dx in (-.64,.64):self.box('Table_Leg',(x+dx,1.8,z+.35),(.05,.53,.70),'frame','InteriorHint')
                    self.box('Interior_Monitor',(x+.19,1.91,z+.97),(.37,.045,.30),'frame','InteriorHint')
                if (j+level)%3!=0:self.box('Ceiling_Light',(x,1.4,z+2.95),(1.25,.045,.025),'lamp','InteriorHint',bevel=0)

    def rear(self):
        for z in (4,7.2):
            for face in (BACK,LEFT):
                a,b=face;L=self.basis(a,b)[2];centers=[L*.23,L*.70];ops=[(t,.65,z+.75,1.75) for t in centers]
                self.wall(a,b,z, z+3.2,ops)
                for t,w,low,h in ops:self.glazing(a,b,t,w,low,h,1,blinds=False)
        for x in (-5.4,4.8):
            self.cylinder('Rain_Downpipe',(x,4.22,5.10),.047,10.2)
            for z in (1.6,5,8.5):self.box('Downpipe_Bracket',(x,4.18,z),(.14,.16,.065),'metal','RoofEquipment',bevel=0)

    def rail(self,a,b,z,material='frame',height=.94):
        L=self.basis(a,b)[2]
        for h in (.12,height):self.strip('Rail_Horizontal',a,b,L/2,L,z+h,.036,material,.036,0,'Terraces',0)
        count=max(2,math.ceil(L/.85))
        for i in range(count+1):self.strip('Rail_Post',a,b,L*i/count,.034,z+height/2,height,material,.034,0,'Terraces',0)
        self.strip('Rail_Mid',a,b,L/2,L,z+height*.56,.019,material,.021,0,'Terraces',0)

    def planter(self,name,loc,size):
        x,y,z=loc;w,d,h=size
        self.box(name,(x,y,z+h/2),(w,d,h),'inside','Planting',bevel=.025)
        self.box('Planter_Soil',(x,y,z+h+.008),(w-.075,d-.075,.025),'soil','Planting',bevel=0)
        rng=random.Random(self.number*971+int(x*37+y*53+z*17));verts=[];faces=[]
        for _ in range(max(35,int(w*d*105))):
            cx=x+rng.uniform(-w*.44,w*.44);cy=y+rng.uniform(-d*.40,d*.40);cz=z+h+rng.uniform(.08,.42)
            a=rng.random()*math.tau;length=rng.uniform(.07,.18);width=length*.36
            u=Vector((math.cos(a),math.sin(a),rng.uniform(-.3,.8))).normalized()*length;v=Vector((-math.sin(a),math.cos(a),0))*width;c=Vector((cx,cy,cz))
            idx=len(verts);verts.extend([tuple(c-u),tuple(c-v),tuple(c+u),tuple(c+v),tuple(c+Vector((0,0,.035)))])
            faces.extend([(idx,idx+1,idx+4),(idx+1,idx+2,idx+4),(idx+2,idx+3,idx+4),(idx+3,idx,idx+4)])
        self.mesh('Plant_Leaves',verts,faces,'leaf','Planting')

    def roof(self,points=None,core=(2.8,2.15),core_size=(2.5,2.25,1.65),parapet=True):
        self.slab('Roof_Membrane',10.405,.035,'roof',points)
        if parapet:
            p=points or RECT
            for a,b in zip(p,p[1:]+p[:1]):
                L=self.basis(a,b)[2];self.strip('Roof_Parapet',a,b,L/2,L,10.63,.44,'wall',.19,0,'Structure')
                self.strip('Roof_Cap',a,b,L/2,L+.03,10.87,.055,'metal',.27,0,'Structure')
        x,y=core;w,d,h=core_size
        self.box('Rooftop_AccessCore',(x,y,10.43+h/2),(w,d,h),'wall','RoofEquipment')
        self.box('Access_CoreCap',(x,y,10.46+h),(w+.12,d+.12,.09),'metal','RoofEquipment')
        self.box('Roof_AccessDoor',(x,y-d/2-.022,10.44+h*.45),(.78,.04,h*.82),'metal','RoofEquipment')
        self.box('Roof_DoorHandle',(x+.28,y-d/2-.071,10.44+h*.40),(.10,.05,.023),'steel','RoofEquipment',bevel=0)
        for px in (-4.4,-3.2):
            if not(abs(px-x)<w/2+.52 and abs(2.8-y)<d/2+.38):self.hvac(px,2.80,10.44)
        for px in (-5.45,5.42):self.box('Roof_Drain',(px,3.51,10.47),(.25,.25,.025),'frame','RoofEquipment',bevel=0)
        for px in (-3,0,3):self.box('Roof_Seam',(px,1.25,10.446),(.009,5.30,.005),'joint','RoofEquipment',bevel=0)

    def hvac(self,x,y,z):
        self.box('HVAC_Feet',(x,y,z+.065),(.91,.61,.13),'metal','RoofEquipment')
        self.box('HVAC_Enclosure',(x,y,z+.48),(.82,.53,.72),'steel','RoofEquipment',bevel=.018)
        self.cylinder('HVAC_Rim',(x,y-.277,z+.49),.25,.025,'metal',axis='Y')
        self.cylinder('HVAC_Fan',(x,y-.296,z+.49),.23,.018,'frame',axis='Y')
        for i in range(7):
            dz=-.18+i*.06;length=2*math.sqrt(max(.002,.23**2-dz**2))
            self.box('HVAC_Grille',(x,y-.310,z+.49+dz),(length,.015,.016),'steel','RoofEquipment',bevel=0)
        self.cylinder('HVAC_Hub',(x,y-.322,z+.49),.045,.016,'metal',axis='Y')
        self.box('HVAC_SidePanel',(x+.416,y,z+.49),(.01,.35,.46),'metal','RoofEquipment',bevel=0)
        self.box('HVAC_Pipe',(x+.22,y+.33,z+.28),(.05,.15,.38),'steel','RoofEquipment')

    def pergola(self,x,y,z,w,d,h=2.0):
        for dx in (-w/2,w/2):
            for dy in (-d/2,d/2):self.box('Pergola_Post',(x+dx,y+dy,z+h/2),(.105,.105,h),'frame','Terraces')
        for dy in (-d/2,d/2):self.box('Pergola_Edge',(x,y+dy,z+h),(w+.13,.13,.17),'metal','Terraces')
        for i in range(max(3,int(w/.6))+1):self.box('Pergola_Rafter',(x-w/2+i*w/max(3,int(w/.6)),y,z+h),(.075,d+.18,.15),'metal','Terraces')

    def finish(self):
        # A uniform first UV channel survives group joining and FBX import.
        for group in self.groups.values():
            objects=[o for o in self.collection.objects if o.type=='MESH' and o.parent==group]
            if objects:
                bpy.ops.object.select_all(action='DESELECT')
                for ob in objects:ob.select_set(True)
                bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();objects[0].name=group.name+'_Mesh'
        bpy.context.view_layer.update()
        meshes=[o for o in self.collection.objects if o.type=='MESH']
        coords=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
        bounds={'min':[min(v[k] for v in coords) for k in range(3)],'max':[max(v[k] for v in coords) for k in range(3)]}
        triangles=sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons)
        bpy.ops.object.select_all(action='DESELECT')
        for ob in self.collection.objects:ob.select_set(True)
        bpy.context.view_layer.objects.active=self.root
        bpy.ops.export_scene.fbx(filepath=str(BASE/'Exports'/f'{self.id}.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_custom_props=True,add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
        specs=[]
        for material in self.materials.values():
            sh=material.node_tree.nodes.get('Principled BSDF')
            specs.append({'name':material.name,'color':([1,1,1,1] if material.get('TextureMap') else list(material.diffuse_color)),'metallic':sh.inputs['Metallic'].default_value,'roughness':sh.inputs['Roughness'].default_value,'texture':('Textures/'+material['TextureMap']+'.png') if material.get('TextureMap') else None,'unitySurface':material.get('UnitySurface','Opaque'),'unityAlpha':material.get('UnityAlpha',1),'emissionColor':list(sh.inputs['Emission Color'].default_value),'emissionStrength':sh.inputs['Emission Strength'].default_value})
        row={'id':self.id,'revision':3,'style':self.id,'styleLabel':self.label,'palette':self.id,'paletteLabel':self.finish_label,'width':12,'depth':8,'floorHeights':[4,3.2,3.2],'roofSlabHeight':10.4,'triangles':triangles,'exportBounds':bounds,'signs':self.signs,'blend':'../Models/'+self.id+'.blend','fbx':'../Exports/'+self.id+'.fbx','preview':'Previews/'+self.id+'.png','materials':specs}
        self.presentation()
        for image in bpy.data.images:
            if image.source=='FILE':image.filepath='//../Exports/Textures/'+Path(image.filepath).name
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(BASE/'Models'/f'{self.id}.blend'))
        self.scene.render.filepath=str(BASE/'Catalog/Previews'/f'{self.id}.png');bpy.ops.render.render(write_still=True)
        catalog=BASE/'Catalog/catalog.json';rows=json.loads(catalog.read_text(encoding='utf8'))['items'] if catalog.exists() else []
        rows=[x for x in rows if x['id']!=self.id]+[row];rows.sort(key=lambda x:x['id']);catalog.write_text(json.dumps({'items':rows},ensure_ascii=False,indent=2),encoding='utf8')
        print('DESIGN_COMPLETE',self.id,self.label,triangles,flush=True)

    def presentation(self):
        scene=self.scene;stage=bpy.data.collections.new('Presentation_ONLY');scene.collection.children.link(stage)
        studio=self.material('studio',(.42,.44,.44),rough=.85)
        floor=self.box('StudioGround',(0,0,-.22),(200,200,.1),'studio',bevel=0);floor.parent=None
        for c in list(floor.users_collection):c.objects.unlink(floor)
        stage.objects.link(floor)
        world=bpy.data.worlds.new('Daylight');world.use_nodes=True;scene.world=world;world.node_tree.nodes.get('Background').inputs[0].default_value=(.64,.72,.82,1);world.node_tree.nodes.get('Background').inputs[1].default_value=.45
        light=bpy.data.lights.new('Sun','SUN');light.energy=2.5;light.angle=.065;ob=bpy.data.objects.new('Sun',light);stage.objects.link(ob);ob.rotation_euler=(.45,-.5,-.5)
        light=bpy.data.lights.new('Softbox','AREA');light.energy=1900;light.shape='DISK';light.size=12;ob=bpy.data.objects.new('Softbox',light);stage.objects.link(ob);ob.location=(0,-12,18);ob.rotation_euler=(Vector((0,0,5))-ob.location).to_track_quat('-Z','Y').to_euler()
        camera=bpy.data.cameras.new('OverviewCamera');ob=bpy.data.objects.new('OverviewCamera',camera);stage.objects.link(ob);ob.location=(20,-28,14);ob.rotation_euler=(Vector((0,0,5.65))-ob.location).to_track_quat('-Z','Y').to_euler();camera.type='ORTHO';camera.ortho_scale=19.2;scene.camera=ob
        scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True;scene.cycles.max_bounces=10;scene.cycles.transmission_bounces=8
        try:
            pref=bpy.context.preferences.addons['cycles'].preferences;pref.compute_device_type='OPTIX';pref.get_devices()
            for device in pref.devices:device.use=device.type=='OPTIX'
            if any(d.use for d in pref.devices):scene.cycles.device='GPU'
        except Exception as exc:print('CPU_RENDER_FALLBACK',exc)
        scene.render.resolution_x=1400;scene.render.resolution_y=1200;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
        scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=.5
