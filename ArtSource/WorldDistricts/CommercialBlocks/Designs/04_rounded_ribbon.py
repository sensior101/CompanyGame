"""Design 04: continuous rounded corner, pale floor ribbons and roof planting."""
import sys,math
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from common import Building

radius=2.2;center=(3.8,-1.8)
arc=[(center[0]+radius*math.cos(-math.pi/2+i*math.pi/32),center[1]+radius*math.sin(-math.pi/2+i*math.pi/32)) for i in range(17)]
outline=[(-6,-4)]+arc+[(6,4),(-6,4)]
b=Building(4,'라운드 코너 · 화이트 리본','밝은 석재 / 곡면 유리 / 루프 가든',(.72,.70,.63),'Facade_IvoryCharcoal')
b.frame_bands(points=outline,width=.50);b.ground(curve=arc,fascia='wall');b.interiors(points=outline);b.rear()
for z in (4,7.2):
    # Straight glazing and curve segments share the same inner offset and floor levels.
    for a,c in [((-6,-4),arc[0]),(arc[-1],(6,4))]:
        L=b.basis(a,c)[2]
        b.glazing(a,c,L/2,L,z+.19,2.47,1.20,off=-.25,crossbar=None,outer=False)
        for t in (0,L):b.strip('Ribbon_EndPier',a,c,t,.15,z+1.5,3.0,'wall',.32,0)
    for i,(a,c) in enumerate(zip(arc,arc[1:])):
        L=b.basis(a,c)[2]
        for level in (z+.19,z+2.66):b.strip('Curved_Transom',a,c,L/2,L+.006,level,.065,'frame',.085,-.22,'Frames',0)
        if i%4==0:b.strip('Curved_Mullion',a,c,0,.055,z+1.425,2.54,'frame',.085,-.22,'Frames',0)
    # The white horizontal ribbon is uninterrupted at the round corner.
    b.curve_band('Curved_UpperGlass',arc,z+.19,2.47,'glass',.009,-.25,'Glass')
    for a,c in [((-6,-4),arc[0]),(arc[-1],(6,4))]:
        L=b.basis(a,c)[2];b.strip('White_RibbonFascia',a,c,L/2,L,z+.08,.16,'wall',.33,.035)
    b.curve_band('White_CurvedRibbon',arc,z,.16,'wall',.33,.035)
b.roof(points=outline,core=(-2.9,2.25),core_size=(2.8,2.0,1.50),parapet=False)
for a,c in [((-6,-4),arc[0]),(arc[-1],(6,4)),((6,4),(-6,4)),((-6,4),(-6,-4))]:
    L=b.basis(a,c)[2];b.strip('Parapet',a,c,L/2,L,10.63,.44,'wall',.19)
    b.strip('ParapetCap',a,c,L/2,L,10.87,.055,'metal',.27)
b.curve_band('Curved_Parapet',arc,10.41,.44,'wall',.19)
b.curve_band('Curved_ParapetCap',arc,10.8425,.055,'metal',.27)
# A small glazed rooftop pavilion sits behind the planted terrace, not a fourth full floor.
b.glazing((-1.1,.30),(3.5,.30),2.3,4.6,10.50,1.34,1.12,off=0,outer=False)
b.glazing((3.5,.3),(3.5,2.8),1.25,2.5,10.50,1.34,1.12,off=0,outer=False)
b.box('Pavilion_Back',(1.2,2.82,11.18),(4.60,.13,1.47),'wall','RoofEquipment')
b.box('Pavilion_Left',(-1.15,1.55,11.18),(.12,2.65,1.47),'wall','RoofEquipment')
b.box('Pavilion_Cap',(1.20,1.57,11.99),(4.86,2.87,.16),'wall','RoofEquipment')
b.rail((-5.3,-3.50),(3.50,-3.50),10.45)
for a,c in zip(arc[::4],arc[4::4]):
    # Rail follows the inset curve on the terrace.
    a=(center[0]+(a[0]-center[0])*.82,center[1]+(a[1]-center[1])*.82)
    c=(center[0]+(c[0]-center[0])*.82,center[1]+(c[1]-center[1])*.82)
    b.rail(a,c,10.45)
b.rail((5.48,-1.70),(5.48,3.30),10.45)
b.planter('Roof_FrontPlanter',(-2.70,-3.0,10.46),(3.5,.60,.42))
b.planter('Roof_SidePlanter',(5.05,1.48,10.46),(.60,3.4,.42))
b.finish()
