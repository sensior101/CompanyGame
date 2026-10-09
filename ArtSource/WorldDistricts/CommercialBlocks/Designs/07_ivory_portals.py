"""Design 07: offset pale volumes with large, genuinely recessed rectangular terraces."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import Building, FRONT, RIGHT

b=Building(7,'아이보리 포털 · 엇갈린 테라스','밝은 콘크리트 / 깊은 사각 프레임 / 식재',(.72,.70,.63),'Facade_IvoryCharcoal')
b.frame_bands(width=.56);b.ground();b.interiors();b.rear()
# Second floor: solid left wing, open terrace on the front/right corner.
left=((-6,-4),(.25,-4));ops=[(1.48,1.0,4.52,2.12),(4.23,1.48,4.52,2.12)]
b.wall(*left,4,7.2,ops,depth=.42)
for t,w,z,h in ops:b.glazing(*left,t,w,z,h,1.3,off=-.15)
b.box('LowerTerrace_Return',(.34,-2.98,5.61),(.42,2.18,3.18),'wall','Facade')
b.glazing((.50,-1.93),(4.57,-1.93),2.035,4.07,4.18,2.49,1.25,off=0,outer=False)
b.glazing((4.57,-1.93),(4.57,3.85),2.89,5.78,4.18,2.49,1.3,off=0,outer=False)
b.box('LowerTerrace_CornerPier',(5.77,-3.74,5.32),(.46,.48,2.64),'wall','Facade')
b.box('LowerTerrace_RearPier',(5.78,3.71,5.32),(.44,.55,2.64),'wall','Facade')
b.rail((.65,-3.60),(5.50,-3.60),4.03)
b.rail((5.52,-3.6),(5.52,3.36),4.03)
b.planter('LowerTerrace_Planter',(2.75,-3.11,4.035),(3.15,.58,.44))
# Third floor: opposite front corner opens into a larger portal.
right=((.10,-4),(6,-4));ops=[(1.70,1.33,7.72,2.05),(4.25,1.16,7.72,2.05)]
b.wall(*right,7.2,10.4,ops,depth=.42)
for t,w,z,h in ops:b.glazing(*right,t,w,z,h,1.2,off=-.16)
b.box('UpperPortal_LeftPier',(-5.73,-3.68,8.52),(.54,.60,2.64),'wall','Facade')
b.box('UpperPortal_RightPier',(-.19,-3.68,8.52),(.49,.60,2.64),'wall','Facade')
b.box('UpperPortal_Return',(-.17,-2.86,8.8),(.36,2.25,3.20),'wall','Facade')
b.glazing((-5.44,-1.86),(-.41,-1.86),2.515,5.03,7.40,2.42,1.28,off=0,outer=False)
b.box('UpperPortal_WoodCeiling',(-2.96,-2.88,10.02),(5.30,2.15,.10),'wood','Terraces')
b.rail((-5.36,-3.46),(-.62,-3.46),7.23)
b.planter('UpperPortal_Planter',(-3.1,-3.03,7.235),(2.9,.55,.45))
side=[(2.24,2.62,7.72,2.05),(6.08,1.10,7.72,2.05)]
b.wall(*RIGHT,7.2,10.4,side,depth=.42)
for t,w,z,h in side:b.glazing(*RIGHT,t,w,z,h,1.25,off=-.16)
b.roof(core=(-3.98,2.02),core_size=(2.5,2.45,1.60))
b.pergola(1.33,1.64,10.45,5.40,2.45,1.35)
b.rail((-4.80,-3.40),(5.06,-3.40),10.45)
b.rail((5.06,-3.40),(5.06,2.97),10.45)
b.planter('Roof_Planter',(1.85,-2.96,10.46),(4.60,.67,.45))
b.finish()
