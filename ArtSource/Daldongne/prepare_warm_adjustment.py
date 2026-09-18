from pathlib import Path
import textwrap
p=Path(__file__).parent
base=(p/'build_map.py').read_text(encoding='utf-8')
code='import bpy,math,random\nfrom mathutils import Vector\nM={m.name:m for m in bpy.data.materials}\n'+base[base.index('cache={}'):base.index('def build():')]
routes=(p/'warm_routes.py').read_text(encoding='utf-8')
code+='\n'+routes
start=routes.index('    def surface(');end=routes.index('    for name,a,b,w in ROUTES:',start)
code+='\n'+textwrap.dedent(routes[start:end])
code+='''
for o in list(bpy.data.objects):
    if o.name.startswith(('Walk_Stair_WestCrest_','Support_Stair_WestCrest','Collider_Ramp_WestCrest','Rail_Warm_WestCrest')):
        bpy.data.objects.remove(o,do_unlink=True)
name,a,b,w=next(r for r in ROUTES if r[0]=='WestCrest')
n=surface(name,a,b,w)
for side in (-1,1):rail('Rail_Warm_'+name,Vector(a)+n*side,Vector(b)+n*side,1.02)
changed={'RestPlaza','WestMiddleTop','WestCrestFoot','WestTop','CrestSquare','CentralCrestTop','EastLower','EastApproach','EastFoot','StationApproach'}
for name,a,b,w in FLATS:
    if name not in changed:continue
    for o in list(bpy.data.objects):
        if o.name=='Walk_Path_'+name:bpy.data.objects.remove(o,do_unlink=True)
    surface(name,a,b,w)
bpy.context.view_layer.update()
'''
code+='\n'+(p/'warm_finish.py').read_text(encoding='utf-8')
code+='''
bpy.context.scene['NavigationRoutes']=__import__('json').dumps([{'name':n,'a':a,'b':b,'width':w,'kind':'stair' if (n,a,b,w) in ROUTES else 'flat'} for n,a,b,w in ROUTES+FLATS])
result={'objects':len(bpy.data.objects),'stairs':len(ROUTES),'flats':len(FLATS),'note':'Added flat turning spaces; clipped bridge pier under ramp; separated paving from terrain by 3.5 cm.'}
'''
(p/'Warm_AdjustConnectors.py').write_text(code,encoding='utf-8')
print(len(code))
