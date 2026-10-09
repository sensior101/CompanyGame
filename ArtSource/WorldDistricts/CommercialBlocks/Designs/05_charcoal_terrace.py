"""Design 05: a recessed top-floor terrace under a broad charcoal canopy."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import Building, FRONT, RIGHT

b=Building(5,'차콜 캐노피 · 깊은 테라스','차콜 메탈 / 검정 철골 / 우드 천장',(.065,.076,.083),None)
b.frame_bands(material='metal',width=.48);b.ground();b.interiors();b.rear()
ops=[(7.10,8.82,4.30,2.44)]
b.wall(*FRONT,4,7.2,ops,'metal')
for t,w,z,h in ops:b.glazing(*FRONT,t,w,z,h,1.30,off=-.13,crossbar=1.45)
b.glazing(*RIGHT,4,7.5,4.30,2.44,1.15,off=-.13,crossbar=1.45)
for y in (-3.75,3.75):b.box('Side_SteelPier',(6.0,y,5.56),(.25,.30,3.2),'metal','Facade')
b.ribbed(*FRONT,1.18,2.35,7.30,6.70,off=.18,spacing=.13)
b.ribbed((-6,4),(-6,-4),4,8,7.36,6.72,off=.18,spacing=.15)
# The third-floor glazing moves two metres back, leaving a real covered terrace.
front=((-3.65,-2.00),(5.38,-2.00));side=((5.38,-2.0),(5.38,3.65))
b.glazing(*front,4.515,9.03,7.38,2.52,1.24,off=0,outer=False)
b.glazing(*side,2.825,5.65,7.38,2.52,1.22,off=0,outer=False)
b.box('Terrace_LeftReturn',(-3.77,-2.93,8.79),(.22,2.18,3.17),'metal','Facade')
b.box('Canopy_Underside',(.65,-2.88,10.01),(10.55,2.16,.13),'wood','Terraces')
for x in (-3.65,5.55):b.box('Canopy_Support',(x,-3.63,8.80),(.12,.12,3.2),'frame','Terraces')
b.rail((-3.53,-3.63),(5.53,-3.63),7.23)
b.rail((5.53,-3.63),(5.53,3.44),7.23)
b.planter('Terrace_Planter',(1.8,-3.14,7.23),(2.30,.52,.42))
b.roof(core=(-4.55,2.1),core_size=(2.22,2.40,1.72))
b.pergola(.35,1.02,10.45,5.9,3.65,1.65)
b.box('Roof_CanopyCover',(.35,1.02,12.20),(6.12,3.86,.16),'metal','RoofEquipment')
b.rail((-3.45,-3.35),(5.30,-3.35),10.45)
b.rail((5.30,-3.35),(5.30,2.98),10.45)
b.finish()
