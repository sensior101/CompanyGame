"""Design 06: warm masonry, deep individual windows and a planted roof terrace."""
import sys,bpy
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import Building, FRONT, RIGHT

b=Building(6,'샌드스톤 박스 · 루프 가든','밝은 베이지 조적 / 깊은 창 / 옥상 식재',(.63,.565,.46),'CreamBrick',(.095,.089,.073))
b.frame_bands(width=.44);b.ground();b.interiors();b.rear()
# Solid piers continue down to the pavement rather than an entirely glass base.
for t,w in [(1.03,.60),(4.15,.65),(8.1,.68),(11.4,.70)]:b.strip('Ground_MasonryPier',*FRONT,t,w,1.73,3.46,'wall',.37,.045)
for t,w in [(1.15,.63),(4.75,.68),(7.5,.65)]:b.strip('Ground_MasonryPier',*RIGHT,t,w,1.73,3.46,'wall',.37,.045)
for z in (4,7.2):
    front=[(2.58,2.46,z+.49,2.12),(6.16,2.18,z+.49,2.12),(9.72,1.15,z+.49,2.12)]
    b.wall(*FRONT,z,z+3.2,front,depth=.47)
    for t,w,low,h in front:b.glazing(*FRONT,t,w,low,h,1.05,off=-.19,blinds=t<3)
    side=[(1.70,1.1,z+.49,2.12),(4.03,1.1,z+.49,2.12),(6.34,1.1,z+.49,2.12)]
    b.wall(*RIGHT,z,z+3.2,side,depth=.47)
    for t,w,low,h in side:b.glazing(*RIGHT,t,w,low,h,1.2,off=-.19)
    for t,w,low,h in front:
        b.strip('Masonry_WindowLintel',*FRONT,t,w+.18,low+h+.10,.13,'wall',.40,.045)
b.ribbed(*RIGHT,7.65,.55,7.2,6.4,off=.25)
b.roof(core=(-.9,1.97),core_size=(3.9,2.50,1.72))
for ob in list(b.collection.objects):
    if ob.name.startswith(('Rooftop_AccessCore','Roof_AccessDoor','Roof_DoorHandle')):bpy.data.objects.remove(ob,do_unlink=True)
b.box('RoofRoom_Back',(-.90,3.18,11.29),(3.9,.14,1.72),'wall','RoofEquipment')
for x in (-2.79,.99):b.box('RoofRoom_Side',(x,1.97,11.29),(.13,2.50,1.72),'wall','RoofEquipment')
b.glazing((-2.70,.695),(.8,.695),1.75,3.5,10.57,1.40,1.15,off=.02,outer=False)
b.rail((-5.2,-3.45),(5.2,-3.45),10.45)
b.rail((5.2,-3.45),(5.2,3.23),10.45)
b.rail((-5.2,-3.45),(-5.2,2.73),10.45)
b.planter('Front_RoofHedge',(-.05,-2.91,10.46),(9.2,.72,.48))
b.planter('Right_RoofHedge',(4.61,.77,10.46),(.70,5.12,.48))
b.planter('Left_RoofHedge',(-4.69,.20,10.46),(.65,3.2,.48))
b.finish()
