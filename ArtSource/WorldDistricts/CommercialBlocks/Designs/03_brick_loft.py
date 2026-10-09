"""Design 03: red-brick frame, industrial grid windows and a glazed rooftop loft."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import Building, FRONT, RIGHT

b=Building(3,'적벽돌 로프트 · 루프 퍼골라','적갈색 벽돌 / 검정 철골 격자',(.33,.115,.069),'Facade_RedBrick')
b.frame_bands(width=.53);b.ground();b.interiors();b.rear()
for z in (4,7.2):
    front=[(2.80,3.65,z+.44,2.41),(8.46,5.15,z+.44,2.41)]
    b.wall(*FRONT,z,z+3.2,front,depth=.42)
    for t,w,low,h in front:b.glazing(*FRONT,t,w,low,h,1.03,off=-.13,crossbar=1.02,blinds=t<4)
    side=[(3.18,5.43,z+.44,2.41)]
    b.wall(*RIGHT,z,z+3.2,side,depth=.42)
    for t,w,low,h in side:b.glazing(*RIGHT,t,w,low,h,.90,off=-.13,crossbar=1.02)
# Emphasised brick pilasters are proud of the recessed iron-framed windows.
for x in (-5.65,-.80,5.65):b.box('Brick_Pilaster',(x,-4.08,7.20),(.48,.44,6.40),'wall','Facade')
b.roof(core=(-4.30,2.15),core_size=(2.15,2.40,1.93))
b.hvac(4.85,2.72,10.44)
b.box('Loft_RearWall',(.70,3.23,11.36),(6.25,.17,1.82),'wall','RoofEquipment')
b.box('Loft_LeftWall',(-2.45,1.63,11.36),(.18,3.35,1.82),'wall','RoofEquipment')
b.glazing((-2.40,-.02),(3.88,-.02),3.14,6.28,10.51,1.64,1.06,off=0,outer=False)
b.glazing((3.88,-.02),(3.88,3.25),1.63,3.27,10.51,1.64,1.03,off=0,outer=False)
b.box('Loft_ThinRoof',(.72,1.62,12.30),(6.65,3.62,.18),'metal','RoofEquipment')
b.pergola(.2,-1.35,10.47,7.5,2.0,1.88)
b.rail((-5.1,-3.46),(4.9,-3.46),10.45)
b.rail((4.9,-3.46),(4.9,2.85),10.45)
b.finish()
