"""Design 08: staggered grey masonry volumes, a projecting ledge and corner loggia."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import Building, FRONT, RIGHT

b=Building(8,'그레이 조적 · 스텝드 로지아','회갈색 벽돌 / 돌출 발코니 / 후퇴 옥탑',(.24,.25,.245),'CharcoalBrick',(.052,.05,.047))
b.frame_bands(width=.52);b.ground();b.interiors();b.rear()
for t,w in [(1.18,.72),(4.22,.66),(8.17,.63),(11.3,.76)]:b.strip('Ground_BrickPier',*FRONT,t,w,1.73,3.46,'wall',.40,.065)
lower=[(2.80,3.67,4.50,2.13),(8.84,3.20,4.50,2.13)]
b.wall(*FRONT,4,7.2,lower,depth=.46)
for t,w,z,h in lower:b.glazing(*FRONT,t,w,z,h,1.12,off=-.18,crossbar=1.04)
side=[(2.48,2.40,4.50,2.13),(6.05,1.17,4.50,2.13)]
b.wall(*RIGHT,4,7.2,side,depth=.46)
for t,w,z,h in side:b.glazing(*RIGHT,t,w,z,h,1.10,off=-.18,crossbar=1.04)
# A projecting slab forms a small ledge below the third-floor corner loggia.
b.box('Projecting_BalconySlab',(2.95,-4.18,7.04),(5.72,.88,.30),'wall','Terraces')
upperLeft=((-6,-4),(-.65,-4));ops=[(1.50,.85,7.74,2.02),(3.83,1.24,7.74,2.02)]
b.wall(*upperLeft,7.2,10.4,ops,depth=.46)
for t,w,z,h in ops:b.glazing(*upperLeft,t,w,z,h,1.2,off=-.18)
b.box('Loggia_LeftReturn',(-.56,-2.91,8.8),(.36,2.28,3.20),'wall','Facade')
b.glazing((-.36,-1.92),(4.48,-1.92),2.42,4.84,7.40,2.45,1.15,off=0,outer=False)
b.glazing((4.48,-1.92),(4.48,1.20),1.56,3.12,7.40,2.45,1.05,off=0,outer=False)
b.box('Loggia_CornerPier',(5.74,-3.67,8.54),(.50,.57,2.68),'wall','Facade')
b.box('Loggia_SideReturn',(5.22,1.28,8.8),(1.61,.37,3.20),'wall','Facade')
rearSide=((6,1.20),(6,4));ops=[(1.42,.77,7.74,2.02)]
b.wall(*rearSide,7.2,10.4,ops,depth=.46)
for t,w,z,h in ops:b.glazing(*rearSide,t,w,z,h,1.0,off=-.18)
b.box('Loggia_WoodSoffit',(2.37,-2.94,10.06),(5.95,2.07,.10),'wood','Terraces')
b.rail((-.13,-3.84),(5.38,-3.84),7.23)
b.rail((5.39,-3.84),(5.39,.95),7.23)
b.planter('Loggia_Planter',(1.70,-3.27,7.235),(2.70,.58,.40))
# The rooftop mass occupies the left rear, balancing the open right-hand terrace.
b.roof(core=(-3.36,1.48),core_size=(3.45,3.12,1.95))
b.box('Roof_OffsetScreen',(2.68,3.17,11.27),(4.68,.24,1.62),'wall','RoofEquipment')
b.pergola(2.86,1.72,10.45,3.83,2.10,1.60)
b.box('Roof_LoggiaCanopy',(2.86,1.72,12.14),(4.01,2.31,.16),'metal','RoofEquipment')
b.rail((-4.97,-3.42),(5.10,-3.42),10.45)
b.rail((5.10,-3.42),(5.10,2.88),10.45)
b.finish()
