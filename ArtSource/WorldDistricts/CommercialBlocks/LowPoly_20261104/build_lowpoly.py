"""Commercial Blocks: authored matte low-poly variants, source geometry preserved.

Run with Blender 5.2 --background --factory-startup --python build_lowpoly.py -- [1..8]
Reuses original Building layout, wall opening, transform, UV and anchor utilities.
Overrides construction at semantic component level; no decimation or source writes.
All output stays beside this script. Original Designs are read-only dependencies.
"""
import bpy, bmesh, math, json, sys, random, hashlib
sys.dont_write_bytecode = True
from pathlib import Path
from mathutils import Vector

OUT = Path(__file__).resolve().parent
SOURCE = OUT.parent
for directory in ('Models', 'Exports', 'Previews', 'QA', 'Catalog'):
    (OUT / directory).mkdir(exist_ok=True)
sys.path.insert(0, str(SOURCE / 'Designs'))
import common
OriginalBuilding = common.Building

DESIGNS = {
    1:'01_concrete_core.py', 2:'02_ivory_oak.py', 3:'03_brick_loft.py',
    4:'04_rounded_ribbon.py', 5:'05_charcoal_terrace.py',
    6:'06_sandstone_garden.py', 7:'07_ivory_portals.py', 8:'08_stepped_masonry.py',
}
# Colors are linear values. Broad warm walls / desaturated cool windows.
PALETTES = {
    1:((.60,.56,.46),(.19,.26,.25),(.12,.22,.23),(.70,.49,.27)),
    2:((.78,.69,.51),(.27,.30,.23),(.15,.26,.25),(.48,.26,.12)),
    3:((.45,.18,.11),(.19,.26,.23),(.12,.23,.23),(.53,.32,.17)),
    4:((.79,.75,.60),(.25,.39,.35),(.19,.32,.31),(.64,.43,.23)),
    5:((.19,.27,.29),(.13,.21,.23),(.23,.35,.33),(.59,.35,.17)),
    6:((.67,.49,.29),(.27,.34,.25),(.21,.31,.26),(.53,.34,.16)),
    7:((.76,.70,.54),(.26,.34,.27),(.17,.28,.26),(.55,.33,.16)),
    8:((.35,.40,.38),(.18,.27,.25),(.20,.32,.30),(.55,.36,.19)),
}

class LowPolyBuilding(OriginalBuilding):
    def __init__(self, number, label, finish, body_color, body_texture, metal=(.1,.1,.1)):
        super().__init__(number, label, finish, body_color, None, metal)
        self.root.name = self.id
        self.root['Variant'] = 'LowPoly_20261104'
        self.root['EntryClearWidthMetres'] = 1.8
        self.root['SourceLayout'] = DESIGNS[number]
        self.root['Style'] = 'matte broad colors; opaque stylized glazing; faceted foliage'
        glass = self.materials['glass']
        shader = glass.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Transmission Weight'].default_value = 0
        shader.inputs['Roughness'].default_value = .72
        glass['UnitySurface'] = 'Opaque'
        glass['UnityAlpha'] = 1
        self.materials['lamp'].node_tree.nodes.get('Principled BSDF').inputs['Emission Strength'].default_value = 0
        self.material('trim', (.78,.68,.47))
        self.material('window_glint', (.36,.49,.43))
        self.material('store_glass', (.34,.42,.33))
        self.material('accent', (.70,.40,.18))

    def material(self, key, color, metal=0, rough=.85, texture=None):
        wall, frame, glass, wood = PALETTES[self.number]
        palette = {'wall':wall, 'frame':frame, 'metal':frame, 'glass':glass,
            'wood':wood, 'inside':(.56,.43,.29), 'floor':(.48,.43,.33),
            'roof':(.28,.32,.27), 'steel':(.47,.45,.34), 'blind':(.61,.53,.36),
            'lamp':(.84,.64,.32), 'soil':(.18,.13,.08), 'leaf':(.27,.38,.14),
            'leaf_light':(.41,.49,.23), 'sign':frame, 'joint':(.39,.32,.22)}
        return super().material(key, palette.get(key,color), metal=0, rough=.86, texture=None)

    def box(self, name, loc, size, material='wall', group='Structure', rotation=0, bevel=0, parent=None):
        # Clean six-face boxes; broad joinery instead of subdivision bevels.
        size = list(size)
        if name in ('Door_Jamb','Door_Rail'):
            size[0 if name == 'Door_Jamb' else 2] = .105
            size[1] = .16
        if name == 'Door_Handle': size = [.07,.10,.45]
        if name in ('Canopy_Support','Pergola_Post'):
            size[0] = size[1] = .20
        if name in ('Roof_Cap','ParapetCap','Access_CoreCap','Core_Cap'):
            size[2] = max(size[2], .13)
        if name in ('Roof_Seam','Downpipe_Bracket','Roof_Drain'): return None
        return super().box(name,loc,size,material,group,rotation,0,parent)

    def cylinder(self,name,loc,radius,depth,material='steel',group='RoofEquipment',axis='Z'):
        n=8
        verts=[(math.cos(i*math.tau/n)*radius,math.sin(i*math.tau/n)*radius,z) for z in (-depth/2,depth/2) for i in range(n)]
        faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        ob=self.mesh(name,verts,faces,material,group);ob.location=loc
        if axis=='Y':ob.rotation_euler.x=math.pi/2
        if axis=='X':ob.rotation_euler.y=math.pi/2
        return ob

    def glazing(self,a,b,t,w,z,h,grid=1.25,off=-.04,crossbar=None,blinds=False,outer=True):
        self.glass('Window_ColorPane',a,b,t,w,z,h,off)
        count=max(1,round(w/2.5))
        thickness=.13 if w<1.4 else .16
        for i in range(count+1):
            self.strip('Window_ChunkyMullion',a,b,t-w/2+w*i/count,thickness,z+h/2,h+.12,'frame',.17,off+.065,'Frames',0)
        for level in (z,z+h):
            self.strip('Window_ChunkyRail',a,b,t,w+.12,level,.14,'frame',.18,off+.065,'Frames',0)
        if crossbar and self.number==3 and h>1.5:
            self.strip('Loft_CrossRail',a,b,t,w,z+h*.63,.11,'frame',.15,off+.07,'Frames',0)
        if outer:
            self.strip('Window_StoneSill',a,b,t,w+.34,z-.10,.16,'trim',.38,.12,'Frames',0)
        # A restrained large inset, not a reflective / transmission shader.
        if w>1.5 and h>1.4:
            self.strip('Window_ColorInset',a,b,t-w/2+.28,.17,z+h/2,h*.78,'window_glint',.015,off+.012,'Glass',0)

    def concrete_joints(self,*args,**kwargs):
        pass

    def interiors(self,points=None):
        # Exterior assets only: opaque windows do not need hundreds of hidden props.
        for z in (0,4,7.2):self.slab('Interior_Floor',z,.04,'floor',points)

    def ribbed(self,a,b,t,w,z,h,material='metal',off=.18,spacing=.14):
        self.strip('Panel_Back',a,b,t,w,z,h,material,.15,off)
        count=max(2,round(w/.48))
        for i in range(count):
            self.strip('Panel_BroadBatten',a,b,t-w/2+(i+.5)*w/count,.11,z,h,material,.12,off+.10,bevel=0)

    def rail(self,a,b,z,material='frame',height=.94):
        length=self.basis(a,b)[2]
        self.strip('Railing_Top',a,b,length/2,length+.10,z+height,.11,material,.13,0,'Terraces',0)
        self.strip('Railing_Bottom',a,b,length/2,length,z+.18,.09,material,.10,0,'Terraces',0)
        count=max(1,round(length/1.5))
        for i in range(count+1):
            self.strip('Railing_Post',a,b,length*i/count,.10,z+height/2,height,material,.10,0,'Terraces',0)

    def planter(self,name,loc,size):
        x,y,z=loc;w,d,h=size
        self.box(name,(x,y,z+h/2),(w,d,h),'wood','Planting')
        self.box('Planter_Rim',(x,y,z+h-.04),(w+.08,d+.08,.12),'trim','Planting')
        major=max(w,d);n=max(1,round(major/.78))
        for i in range(n):
            cx=x + ((i+.5)/n-.5)*w*.9 if w>=d else x
            cy=y if w>=d else y+((i+.5)/n-.5)*d*.9
            bm=bmesh.new();bmesh.ops.create_icosphere(bm,subdivisions=1,radius=1)
            me=bpy.data.meshes.new('Foliage');bm.to_mesh(me);bm.free()
            verts=[(v.co.x*min(.49,w*.48),v.co.y*min(.43,d*.62),v.co.z*.38) for v in me.vertices]
            faces=[tuple(p.vertices) for p in me.polygons];bpy.data.meshes.remove(me)
            ob=self.mesh('Foliage_Faceted',verts,faces,'leaf_light' if i%3==0 else 'leaf','Planting');ob.location=(cx,cy,z+h+.29)

    def hvac(self,x,y,z):
        self.box('AC_Block',(x,y,z+.43),(.88,.57,.78),'trim','RoofEquipment')
        self.cylinder('AC_OctagonalFan',(x,y-.30,z+.44),.245,.025,'frame',axis='Y')
        for offset in (-.12,.12):
            self.box('AC_FanCross',(x+offset,y-.323,z+.44),(.055,.025,.34),'steel','RoofEquipment')

    def pergola(self,x,y,z,w,d,h=2):
        for dx in (-w/2,w/2):
            for dy in (-d/2,d/2):self.box('Pergola_Post',(x+dx,y+dy,z+h/2),(.20,.20,h),'frame','Terraces')
        for dy in (-d/2,d/2):self.box('Pergola_Beam',(x,y+dy,z+h),(w+.24,.22,.22),'frame','Terraces')
        count=max(3,round(w/1.15))
        for i in range(count+1):self.box('Pergola_BroadRafter',(x-w/2+i*w/count,y,z+h),(.18,d+.30,.22),'wood','Terraces')

    def curve_band(self,*args,**kwargs):
        ob=super().curve_band(*args,**kwargs)
        for p in ob.data.polygons:p.use_smooth=False
        return ob

    def ground(self,points=None,wood=False,curve=None,fascia='metal'):
        super().ground(points,wood,curve,fascia)
        # Recolor storefront separately while preserving the source door hinges.
        for ob in self.collection.objects:
            if ob.type=='MESH' and ('Window_ColorPane' in ob.name or 'Door_Glass' in ob.name):
                bounds=[ob.matrix_world@v.co for v in ob.data.vertices]
                if bounds and max(p.z for p in bounds)<3.6:
                    ob.data.materials.clear();ob.data.materials.append(self.materials['store_glass'])
        # Reuse clear central doorway; shop details remain outside +/- 0.9 m.
        for x in (-5.15,4.85):
            self.planter('Street_Planter',(x,-4.11,0),(.78,.60,.55))
        for a,b in [(common.FRONT[0], curve[0] if curve else common.FRONT[1]),(curve[-1] if curve else common.RIGHT[0],common.RIGHT[1])]:
            length=self.basis(a,b)[2]
            self.strip('Shop_HeavyCornice',a,b,length/2,length,3.34,.15,'trim',.32,.12,'Facade',0)

    def add_signs(self,curve=None):
        super().add_signs(curve)
        # Keep UV sign boards and their exact original integration names.
        # Add two-color border bars; replacement graphics remain unassigned.
        for sign in self.signs:
            anchor=bpy.data.objects[sign['name']]
            ob=self.box('Sign_AccentBar',(-sign['width']/2+.10,-.014,0),(.08,.035,.25),'accent','Signage',parent=anchor)
            ob.location=(-sign['width']/2+.10,-.014,0)

    def finish(self):
        # Join by established logical group, preserving door and signage transforms.
        for group in self.groups.values():
            objects=[o for o in self.collection.objects if o.type=='MESH' and o.parent==group]
            if objects:
                bpy.ops.object.select_all(action='DESELECT')
                for ob in objects:ob.select_set(True)
                bpy.context.view_layer.objects.active=objects[0]
                bpy.ops.object.join();objects[0].name=group.name+'_Mesh'
        bpy.context.view_layer.update()
        meshes=[o for o in self.collection.objects if o.type=='MESH']
        coords=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
        bounds={'min':[round(min(v[k] for v in coords),5) for k in range(3)],'max':[round(max(v[k] for v in coords),5) for k in range(3)]}
        triangles=sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons)
        bpy.ops.object.select_all(action='DESELECT')
        for ob in self.collection.objects:ob.select_set(True)
        bpy.context.view_layer.objects.active=self.root
        bpy.ops.export_scene.fbx(filepath=str(OUT/'Exports'/f'{self.id}_LowPoly.fbx'),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_custom_props=True,add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
        bpy.ops.export_scene.gltf(filepath=str(OUT/'Exports'/f'{self.id}_LowPoly.glb'),export_format='GLB',use_selection=True,export_yup=True,export_extras=True,export_materials='EXPORT')
        original=json.loads((SOURCE/'Catalog/catalog.json').read_text(encoding='utf8'))['items']
        old=next(i for i in original if i['id']==self.id)
        row={'id':self.id,'revision':4,'variant':'LowPoly_20261104','styleLabel':self.label,
            'width':12,'depth':8,'floorHeights':[4,3.2,3.2],'roofSlabHeight':10.4,
            'triangles':triangles,'sourceTriangles':old['triangles'],
            'triangleReductionPercent':round(100*(1-triangles/old['triangles']),2),
            'meshObjects':len(meshes),'exportBounds':bounds,'entryClearWidth':1.8,
            'signs':self.signs,'opaqueGlazing':True,'externalTextures':False,
            'blend':f'Models/{self.id}_LowPoly.blend','fbx':f'Exports/{self.id}_LowPoly.fbx',
            'glb':f'Exports/{self.id}_LowPoly.glb','preview':f'Previews/{self.id}_LowPoly.png',
            'sourceDesign':'../Designs/'+DESIGNS[self.number],
            'sourceDesignSHA256':hashlib.sha256((SOURCE/'Designs'/DESIGNS[self.number]).read_bytes()).hexdigest(),
            'materials':[{'name':m.name,'color':list(m.diffuse_color),'roughness':m.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value,'metallic':0,'surface':'Opaque','unitySurface':'Opaque','unityAlpha':1,'texture':None} for m in self.materials.values()]}
        self.presentation()
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT/row['blend']))
        self.scene.render.filepath=str(OUT/row['preview']);bpy.ops.render.render(write_still=True)
        catalog=OUT/'manifest.json'
        rows=json.loads(catalog.read_text(encoding='utf8'))['items'] if catalog.exists() else []
        rows=[r for r in rows if r['id']!=self.id]+[row];rows.sort(key=lambda r:r['id'])
        catalog.write_text(json.dumps({'schemaVersion':1,'style':'CompanyGame matte low-poly commercial buildings','source':'../Models and ../Designs (read-only)','items':rows},ensure_ascii=False,indent=2),encoding='utf8')
        catalogrows=[]
        for r in rows:
            copy=dict(r)
            for key in ('blend','fbx','glb','preview'):copy[key]='../'+r[key]
            copy['sourceDesign']='../'+r['sourceDesign']
            catalogrows.append(copy)
        (OUT/'Catalog/catalog.json').write_text(json.dumps({'items':catalogrows},ensure_ascii=False,indent=2),encoding='utf8')
        print('LOWPOLY_COMPLETE',self.id,'triangles',triangles,'reduction',row['triangleReductionPercent'],flush=True)

    def presentation(self):
        scene=self.scene;stage=bpy.data.collections.new('Presentation_ONLY');scene.collection.children.link(stage)
        self.material('studio',(.19,.24,.23))
        floor=self.box('StudioGround',(0,0,-.23),(200,200,.1),'studio');floor.parent=None
        for c in list(floor.users_collection):c.objects.unlink(floor)
        stage.objects.link(floor)
        world=bpy.data.worlds.new('SoftDaylight');world.use_nodes=True;scene.world=world
        world.node_tree.nodes.get('Background').inputs[0].default_value=(.65,.73,.72,1)
        world.node_tree.nodes.get('Background').inputs[1].default_value=.7
        def light(name,type,loc,energy,size):
            data=bpy.data.lights.new(name,type);data.energy=energy
            if type=='AREA':data.shape='DISK';data.size=size
            else:data.angle=size
            ob=bpy.data.objects.new(name,data);stage.objects.link(ob);ob.location=loc
            ob.rotation_euler=(Vector((0,0,4))-ob.location).to_track_quat('-Z','Y').to_euler()
        light('BroadKey','AREA',(-8,-12,22),2300,10)
        light('WarmSun','SUN',(-8,-10,18),2.0,.12)
        light('SoftFill','AREA',(12,-1,15),1100,9)
        data=bpy.data.cameras.new('OverviewCamera');camera=bpy.data.objects.new('OverviewCamera',data);stage.objects.link(camera)
        camera.location=(20,-28,20);camera.rotation_euler=(Vector((0,0,5.5))-camera.location).to_track_quat('-Z','Y').to_euler()
        data.type='ORTHO';data.ortho_scale=19.7;scene.camera=camera
        scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True;scene.cycles.max_bounces=4
        try:
            prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
            for device in prefs.devices:device.use=device.type=='OPTIX'
            if any(d.use for d in prefs.devices):scene.cycles.device='GPU'
        except Exception:pass
        scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
        scene.render.image_settings.file_format='PNG'
        scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=.4

common.Building=LowPolyBuilding
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
numbers=[int(a) for a in args] if args else list(DESIGNS)
for number in numbers:
    path=SOURCE/'Designs'/DESIGNS[number]
    code=path.read_text(encoding='utf8')
    if number==4:
        # Deliberately eight flat corner segments, preserving 2.2 m radius.
        code=code.replace('math.pi/32','math.pi/16').replace('range(17)','range(9)')
    exec(compile(code,str(path),'exec'),{'__file__':str(path),'__name__':'__main__'})
