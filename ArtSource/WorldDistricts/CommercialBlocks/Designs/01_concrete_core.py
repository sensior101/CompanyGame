"""Design 01: concrete horizontal ribbons, corner glazing and a dark vertical service core."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import Building, FRONT, RIGHT

b=Building(1,'콘크리트 리본 · 수직 코어','노출 콘크리트 / 세로 금속 패널',(.61,.60,.56),'Facade_ConcreteGraphite')
b.frame_bands(width=.43);b.ground();b.interiors();b.rear()
for z in (4,7.2):
    front=[(1.1,.74,z+.52,2.08),(7.18,8.72,z+.30,2.58)]
    b.wall(*FRONT,z, z+3.2,front)
    for t,w,low,h in front:b.glazing(*FRONT,t,w,low,h,1.25,blinds=t<2)
    b.concrete_joints(*FRONT,z+.03,z+3.15,front)
    side=[(2.85,5.28,z+.30,2.58)]
    b.wall(*RIGHT,z,z+3.2,side)
    for t,w,low,h in side:b.glazing(*RIGHT,t,w,low,h,1.25,crossbar=1.38)
b.ribbed(*RIGHT,6.73,2.48,8.13,8.28,off=.21)
b.box('Rear_Core_Crown',(4.85,2.96,11.39),(2.30,2.06,1.94),'metal','RoofEquipment')
b.box('Core_Cap',(4.85,2.96,12.39),(2.43,2.17,.11),'metal','RoofEquipment')
b.roof(core=(-.2,2.25),core_size=(2.30,2.15,1.62))
b.rail((-4.7,-3.44),(2.6,-3.44),10.45,height=.94)
b.rail((2.6,-3.44),(2.6,-1.50),10.45,height=.94)
b.finish()
