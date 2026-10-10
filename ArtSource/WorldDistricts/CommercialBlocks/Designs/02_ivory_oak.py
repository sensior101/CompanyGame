"""Design 02: ivory structural grid with recessed vertical oak panels."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import Building, FRONT, RIGHT

b=Building(2,'아이보리 프레임 · 오크 패널','아이보리 석재 / 세로 오크 / 다크 브론즈',(.72,.70,.63),'Facade_IvoryCharcoal',(.075,.061,.047))
b.frame_bands(width=.48);b.ground();b.interiors();b.rear()
for z in (4,7.2):
    ops=[(2.28,2.38,z+.25,2.54),(7.55,5.8,z+.25,2.54)]
    b.wall(*FRONT,z,z+3.2,ops)
    for t,w,low,h in ops:b.glazing(*FRONT,t,w,low,h,1.18,crossbar=1.44)
    b.ribbed(*FRONT,3.98,.90,z+1.42,2.72,'wood',off=.185,spacing=.105)
    b.ribbed(*FRONT,.51,.63,z+1.42,2.72,'wood',off=.185,spacing=.105)
    b.ribbed(*FRONT,10.96,.76,z+1.42,2.72,'wood',off=.185,spacing=.105)
    side=[(2.06,2.73,z+.25,2.54),(5.58,2.45,z+.25,2.54)]
    b.wall(*RIGHT,z,z+3.2,side)
    for t,w,low,h in side:b.glazing(*RIGHT,t,w,low,h,1.22,crossbar=1.44)
    b.ribbed(*RIGHT,3.99,.58,z+1.42,2.72,'wood',off=.185,spacing=.105)
    b.concrete_joints(*FRONT,z+.03,z+3.16,ops,spacing=2.3)
# Full-height pale end pier gives this building a different silhouette from the dark core type.
b.box('Ivory_EndPier',(6.05,3.74,7.20),(.38,.52,6.40),'wall','Facade')
b.roof(core=(1.40,2.0),core_size=(2.75,2.25,1.36))
b.rail((-4.75,-3.45),(4.8,-3.45),10.45)
b.rail((4.8,-3.45),(4.8,1.3),10.45)
b.box('Roof_OakScreen',(-3.0,2.75,11.09),(1.45,.15,1.25),'wood','RoofEquipment')
b.finish()
