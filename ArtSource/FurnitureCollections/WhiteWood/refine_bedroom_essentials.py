"""Reviewed actual-surface recipes for the final eight non-office models."""
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'CompanyGame/Temp/BoundaryLib'))
sys.path.insert(0,str(Path(__file__).parent))
import numpy as np
from mesh_cut import Surface
helpers={};source=(Path(__file__).parent/'stroke_homeoffice.py').read_text(encoding='utf-8-sig');exec(source[:source.index('recipes=')],helpers)
pixels=helpers['pixels']
D=ROOT/'CompanyGame/Temp/BoundaryAudit';OUT=Path(__file__).parent/'NonOfficeMasks'

def picks(d,Front=(),Angle=(),Back=(),r=5):
 out=np.zeros(len(d['faces']),bool)
 for view,points in [('Front',Front),('Angle',Angle),('Back',Back)]:
  if points:out|=pixels(d,points,r=r,view=view)
 return out

def build(pack,key):
 d=np.load(D/(pack+'_'+key+'.npz'));s=Surface(d);components=s.components()
 base='Linen' if key=='Sofa' else 'OakLight' if key=='LaundryBasket' else 'Oak'
 roles=np.full(s.count,base,dtype='<U12')
 def cut(role,fg,bg,protect=None):
  pos=picks(d,**fg);neg=picks(d,**bg);neg|=~np.isin(components,np.unique(components[pos]))
  if protect is not None:neg|=protect&~pos
  selected=s.cut(pos,neg);roles[selected]=role
  print(pack,key,role,'seeds',pos.sum(),neg.sum(),'region',selected.sum(),flush=True)
  return selected
 if key=='Bed':
  linen=cut('Linen',dict(Angle=[(174,327),(280,348),(213,434),(311,483),(651,542),(729,501),(341,431)],Back=[(650,523),(324,434),(619,408)],Front=[(319,477),(453,480),(746,474),(232,379),(161,356)]),
   dict(Angle=[(128,486),(192,512),(244,534),(555,696),(752,572),(560,658)],Front=[(128,584),(129,418),(224,545),(437,552),(698,594),(752,537)],Back=[(567,706),(778,580),(490,620),(263,552),(560,527)]))
  cut('Olive',dict(Front=[(580,484),(668,468),(556,573),(590,581),(650,581)],Angle=[(538,493),(649,462),(431,632),(482,658),(495,683),(474,491)],Back=[(190,390),(278,404),(379,357),(204,514),(246,531),(257,540),(424,351)]),
   dict(Front=[(482,477),(744,479),(419,440),(583,604),(688,576)],Angle=[(711,503),(711,530),(361,414),(393,593),(558,680)],Back=[(363,476),(401,422),(139,441),(144,524),(288,567)]))
 elif key=='LoftBed':
  linen=cut('Linen',dict(Angle=[(350,241),(289,197),(435,265),(519,303),(595,292),(274,179),(390,282),(398,352)],Front=[(310,249),(239,252),(433,299),(542,302)],Back=[(553,294),(624,283),(363,268),(437,279),(597,320),(623,342),(493,320)]),
   dict(Angle=[(398,383),(179,337),(170,222),(558,261),(589,489)],Front=[(401,345),(420,269),(164,301),(658,293),(594,239)],Back=[(388,419),(586,438),(707,529),(454,213),(246,248)]))
  cut('Paper',dict(Front=[(157,415),(155,454),(244,561),(267,581),(169,563),(171,589),(203,579),(220,546),(230,529),(258,530)],Angle=[(164,444),(246,528),(168,509),(220,540),(199,541)]),
   dict(Front=[(157,510),(202,616),(285,536),(141,535),(195,463)],Angle=[(188,477),(256,573),(281,444),(207,429)]))
  pot=cut('Ivory',dict(Front=[(202,479),(233,474)],Angle=[(219,439),(223,451),(239,441)]),dict(Front=[(188,433),(240,442),(167,487),(208,508)],Angle=[(207,415),(219,475)]))
  cut('Leaf',dict(Front=[(214,414),(197,437),(242,434),(214,405),(207,445)],Angle=[(200,389),(218,408),(252,414),(201,426)]),dict(Front=[(216,478),(182,482),(260,470),(154,449),(219,510),(145,395),(262,394),(264,469)],Angle=[(213,447),(255,448),(193,466)]))
 elif key=='Wardrobe':
  cut('Ivory',dict(Angle=[(365,235),(341,571),(491,686),(281,450),(510,450),(387,622),(290,195)]),
   dict(Angle=[(446,134),(570,258),(579,613),(400,472),(296,713),(535,783)]))
 elif key=='BedsideCabinet':
  cut('Ivory',dict(Angle=[(258,503),(485,574),(325,647),(520,666),(186,504)]),dict(Angle=[(323,403),(623,566),(545,802),(259,724),(244,689),(555,744)]))
  cut('Ivory',dict(Angle=[(352,193),(368,287),(324,330),(455,246)],Front=[(324,214)]),dict(Angle=[(350,357),(482,320),(500,261),(437,379)]))
  cut('Paper',dict(Angle=[(495,404),(466,426),(572,408),(531,442),(453,393),(607,403)],Back=[(361,412),(463,390),(318,392)]),dict(Angle=[(533,360),(646,422),(503,467),(376,425)],Back=[(345,361),(389,456),(467,441)]))
  cut('Ivory',dict(Angle=[(524,359),(577,360),(523,381)],Back=[(387,347)]),dict(Angle=[(491,389),(598,381),(531,320),(497,288),(548,272)]))
  cut('Leaf',dict(Angle=[(514,198),(498,230),(536,206),(584,208),(637,281),(539,310),(600,306),(496,284),(534,172)],Back=[(352,338),(373,332),(408,319)]),dict(Angle=[(525,364),(583,360),(399,257),(489,392),(632,413)],Back=[(323,359),(401,366),(479,277)]))
 elif key=='LaundryBasket':
  cut('Olive',dict(Angle=[(453,226),(374,269),(319,252),(551,300),(592,464),(561,607),(679,560)],Front=[(343,225),(468,217),(638,225),(679,456),(699,549)],Back=[(344,272),(556,246),(466,173),(299,149),(402,317)]),
   dict(Angle=[(392,143),(618,180),(203,272),(384,349),(356,417),(264,534),(527,719),(456,814),(248,205)],Front=[(280,199),(503,184),(264,260),(465,267),(388,467),(557,608),(563,338)],Back=[(281,301),(531,348),(343,548),(630,454),(239,239)]))
 elif key=='Curtain':
  roles[:]='Linen'
  cut('Oak',dict(Angle=[(180,146),(701,296),(478,222),(448,264),(456,456),(436,610),(449,684),(358,402)],
                 Back=[(450,265),(428,675),(347,353),(346,560),(449,429),(536,623)]),
   dict(Angle=[(213,198),(251,221),(283,241),(317,238),(272,497),(336,661),(540,305),(580,319),(620,336),(655,354),(611,660),(541,471),(215,162),(248,177),(286,187),(317,203),(549,264),(587,281),(623,291),(657,300)],
        Back=[(222,438),(270,460),(310,432),(572,443),(611,483),(666,501),(229,160),(267,172),(306,185),(337,198),(572,259),(610,275),(647,285),(678,296)]))
 elif key=='CoatRack':
  cut('Linen',dict(Angle=[(378,224),(371,333),(454,368),(428,316),(486,296)],Back=[(530,218),(414,238),(509,286)]),
   dict(Angle=[(440,179),(513,257),(479,444),(445,703),(373,403)],Back=[(453,218),(436,304),(505,337),(386,357)]))
  cut('Linen',dict(Front=[(465,636),(490,502),(470,410)],Angle=[(440,573),(465,620),(465,493),(466,433)]),
   dict(Front=[(441,695),(407,453),(394,581),(438,375)],Angle=[(443,714),(446,385)],Back=[(454,463),(411,480),(436,610)]))
  cut('Terracotta',dict(Back=[(431,396),(436,487),(409,645),(406,704),(469,609),(460,573)],Front=[(401,452),(392,581),(416,650)]),
   dict(Back=[(456,309),(473,682),(431,746),(502,338),(417,254)],Front=[(465,499),(440,699),(470,283),(438,377)]))
 elif key=='Sofa':
  cut('Oak',dict(Front=[(184,604),(714,604),(448,571)],Angle=[(168,555),(614,682)],Back=[(126,575),(593,714),(418,637)]),
   dict(Front=[(448,548),(152,508),(702,525),(661,487),(269,567)],Angle=[(129,466),(487,536),(768,519),(589,644),(228,550)],Back=[(588,660),(139,518),(461,565),(703,558)]))
  cut('Terracotta',dict(Front=[(264,357),(226,317),(320,415)],Angle=[(285,323),(317,268),(356,377)]),
   dict(Front=[(372,355),(392,409),(316,280),(206,448),(193,389)],Angle=[(425,344),(396,282),(352,429),(230,423),(218,345)]))
 # Remove a tiny paint island only when its entire material boundary is enclosed
 # by one other role. Separate shells such as tassels have no such boundary and
 # are retained. This repairs accidental single-triangle flecks on broad cloth.
 if key in ('Bed','BedsideCabinet','LaundryBasket','LoftBed'):
  adjacency=[[] for _ in range(s.count)]
  for a,b in zip(s.a,s.b):adjacency[a].append(b);adjacency[b].append(a)
  visited=np.zeros(s.count,bool);small_limit=float(np.sum(s.area))*.0004
  for start in range(s.count):
   if visited[start]:continue
   region=[start];visited[start]=True;near=set();i=0
   while i<len(region):
    f=region[i];i+=1
    for n in adjacency[f]:
     if roles[n]!=roles[f]:near.add(str(roles[n]))
     elif not visited[n]:visited[n]=True;region.append(n)
   if len(region)<120 and len(near)==1 and float(np.sum(s.area[region]))<small_limit:
    roles[region]=next(iter(near))
 assert len(roles)==len(d['coords'])
 np.savez_compressed(OUT/(pack+'_'+key+'.npz'),roles=roles,coords=d['coords'],face_count=len(roles))
 print('SAVED',pack,key,dict(zip(*np.unique(roles,return_counts=True))),flush=True)

if __name__=='__main__':
 targets=[('Bedroom','Bed'),('Bedroom','LoftBed'),('Bedroom','Wardrobe'),('Bedroom','BedsideCabinet'),('Bedroom','LaundryBasket'),('Essentials','Curtain'),('Essentials','CoatRack'),('LivingRoom','Sofa')]
 for pack,key in targets:
  if len(sys.argv)==1 or key in sys.argv[1:]:build(pack,key)
