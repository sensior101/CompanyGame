"""Offline regeneration of the reviewed non-office material masks.

Inputs are the matching original-face geometry and camera projections exported
by inspect_nonoffice_surfaces.py into CompanyGame/Temp/BoundaryAudit, plus the
WallOrganizer geometry from Temp/OfficeBoundary. Run with Python 3.12 + PyMaxflow
(mesh_cut.py); the Blender importer reads the cached masks without this dependency.
Six picture frames are owned by the separate frame boundary repair workflow.
"""

# Recipe: nonoffice_masks.py
import os,sys,numpy as np
from pathlib import Path
ROOT=str(Path(__file__).resolve().parents[3]).replace('\\','/')
sys.path.insert(0,ROOT+'/CompanyGame/Temp/BoundaryLib')
sys.path.insert(0,ROOT+'/ArtSource/FurnitureCollections/WhiteWood')
from mesh_cut import Surface
OUT=ROOT+'/ArtSource/FurnitureCollections/WhiteWood/NonOfficeMasks';os.makedirs(OUT,exist_ok=True)
def pixels(d,points,r=14,view='Front'):
 out=np.zeros(len(d['faces']),bool);v=d[view];px=np.c_[v[:,0]*900,(1-v[:,1])*900]
 for x,y in points:
  dd=np.linalg.norm(px-[x,y],axis=1);sel=dd<r
  if np.any(sel):
   front=np.min(v[sel,2]);out|=sel&(v[:,2]<front+.015)
 return out
targets={
 ('Essentials','PlantTrailing'):{
  'fg':[(210,355),(400,180),(520,187),(420,278),(653,420),(480,713),(535,765),(546,610),(550,486),(530,338)],
  'bg':[(333,524),(658,560),(439,600),(321,391),(676,343),(590,335)]
 },
 ('Essentials','PlantLarge'):{
  'fg':[(363,174),(520,300),(696,344),(261,370),(345,424),(327,481),(488,552),(650,505),(432,556),(450,516),(486,489)],
  'bg':[(448,690),(360,688),(558,664),(346,611),(564,622)],
  'high_seed':.31,'low_seed':.17
 },
 ('Essentials','PlantSmall'):{
  'fg':[(421,288),(496,250),(606,257),(663,296),(375,366),(265,320),(242,406),(468,434),(608,426),(346,442),(517,445)],
  'bg':[(401,643),(551,650),(289,470),(616,480)],
  'high_seed':.64,'low_seed':.38
 },
 ('LivingRoom','Plant'):{
  'fg':[(470,148),(548,271),(306,280),(506,406),(640,482),(525,520),(352,489),(478,560),(526,401),(400,521)],
  'bg':[(470,680),(424,680),(575,635),(582,590),(408,597)],
  'high_seed':.42,'low_seed':.20
 },
 ('Kitchen','DiningChair'):{
  'fg':[(425,193),(450,290),(480,453),(400,493)],
  'bg':[(240,369),(626,382),(304,650),(670,671),(550,563),(215,666)],
  'foreground':'Linen','background':'Oak'
 },
 ('Kitchen','Stool'):{
  'fg':[(338,162),(590,181),(466,253),(302,250)],
  'bg':[(400,450),(282,483),(544,559),(605,533),(478,694),(550,292)],
  'foreground':'Linen','background':'Oak'
 },
 ('LivingRoom','Armchair'):{
  'fg':[(603,210),(614,296),(264,333),(202,431),(238,482),(420,408),(499,352),(420,481)],
  'bg':[(700,680),(365,702),(226,682),(370,554),(575,444),(310,492),(710,473),(713,285)],
  'foreground':'Linen','background':'Oak',
  'angle_fg':[(336,548),(394,558),(345,461),(253,452)],
  'angle_bg':[(321,598),(196,558),(497,580),(590,601),(469,523)],
  'pillow_fg':[(496,343),(470,334)],'pillow_bg':[(613,235),(341,393),(264,333),(603,210),(238,482)]
 },
 ('Bedroom','ChestOfDrawers'):{
  'fg':[(385,140),(346,158),(316,207),(451,176),(465,216),(301,266),(415,261),(454,300),(410,343),(304,366),(303,433),(389,442),(316,515),(371,521),(305,573),(363,579),(337,607),(347,486),(358,375),(371,239)],
  'bg':[(393,650),(290,670),(433,463),(484,387),(500,294),(392,281)],
  'background':'Oak',
  'panel_fg':[(550,390),(550,489),(510,570),(513,677),(622,633),(400,660),(417,430)],
  'panel_bg':[(315,690),(276,704),(606,305),(336,410),(634,285)],
  'panel_view':'Angle'
 },
 ('Bedroom','StorageShelf'):{
  'fg':[(381,140),(430,147),(479,153),(355,182),(343,239),(466,213),(477,251),(446,266),(347,297),(458,308),(356,348),(450,339),(370,397),(411,418),(385,446),(395,230),(410,300),(390,381)],
  'bg':[(284,360),(610,389),(557,189),(577,286),(571,433),(480,447),(435,489),(352,485),(390,689)],
  'background':'Oak',
  'panel_fg':[(566,245),(539,251),(347,211),(531,371),(576,361),(517,509),(345,508),(385,687),(526,670)],
  'panel_bg':[(285,580),(610,498),(612,210),(518,288),(488,427),(534,575),(529,745)],
  'panel_view':'Front'
 },
}
for (pack,key),config in targets.items():
 d=np.load(ROOT+'/CompanyGame/Temp/BoundaryAudit/'+pack+'_'+key+'.npz');s=Surface(d)
 fg=pixels(d,config['fg']);bg=pixels(d,config['bg'])
 if 'high_seed' in config:fg|=s.c[:,2]>config['high_seed']
 if 'low_seed' in config:bg|=s.c[:,2]<config['low_seed']
 if 'angle_fg' in config:fg|=pixels(d,config['angle_fg'],view='Angle')
 if 'angle_bg' in config:bg|=pixels(d,config['angle_bg'],view='Angle')
 leaf=s.cut(fg,bg)
 roles=np.where(leaf,config.get('foreground','Leaf'),config.get('background','Ivory')).astype('<U12')
 if 'pillow_fg' in config:
  pillow=s.cut(pixels(d,config['pillow_fg']),pixels(d,config['pillow_bg'])|~leaf)
  roles[pillow]='Terracotta'
 if 'panel_fg' in config:
  view=config.get('panel_view','Front')
  panel=s.cut(pixels(d,config['panel_fg'],view=view),pixels(d,config['panel_bg'],view=view)|leaf)
  roles[panel]='Ivory'
 np.savez_compressed(OUT+'/'+pack+'_'+key+'.npz',roles=roles,coords=d['coords'],face_count=len(roles))
 print(pack,key,len(roles),'green',np.sum(leaf),'source',np.sum(fg),'sink',np.sum(bg))


# Recipe: tv_mask.py
pack,key='LivingRoom','TVSet';d=np.load(ROOT+'/CompanyGame/Temp/BoundaryAudit/'+pack+'_'+key+'.npz');s=Surface(d)
roles=np.full(len(s.f),'Ivory',dtype='<U12')
leaf=s.cut(pixels(d,[(185,350),(151,376),(184,385),(130,407),(182,414),(137,457),(171,458),(218,472),(145,491),(169,443)],r=7),pixels(d,[(187,521),(242,487),(217,511),(257,520)],r=10))
roles[leaf]='Leaf'
tv=s.cut(pixels(d,[(475,350),(289,282),(648,277),(651,441),(601,478),(369,473),(500,456)],r=14),pixels(d,[(300,444),(700,452),(421,513),(649,507),(396,491),(268,481),(330,481),(349,484)],r=10)|leaf)
roles[tv]='Screen'
x,y,z=s.c.T
planes=((s.n[:,2]>.9)&(z>.405)&(z<.414))|((np.abs(x)>.445)&(np.abs(s.n[:,0])>.85))|(z>.417)
doors=s.cut(pixels(d,[(233,540),(226,579),(685,536),(714,585),(174,540),(170,580),(749,580)],r=18),pixels(d,[(327,535),(327,598),(617,579),(726,503),(247,503),(475,620)],r=8)|leaf|tv|planes)
roles[doors]='Oak'
legs=s.cut(pixels(d,[(205,642),(724,642)],r=10),pixels(d,[(205,603),(724,603),(434,612)],r=10)|leaf|tv|doors)
roles[legs]='Oak'
sculpt=s.cut(pixels(d,[(299,429),(277,448),(322,449)],r=8),pixels(d,[(291,481),(306,486),(344,472)],r=8)|leaf|tv|doors|legs)
roles[sculpt]='Terracotta'
np.savez_compressed(OUT+'/'+pack+'_'+key+'.npz',roles=roles,coords=d['coords'],face_count=len(roles))
print(pack,key,np.unique(roles,return_counts=True))

# Recipe: nonoffice_tables_masks.py
for key,points in [
 ('CoffeeTableDecorated',[(470,181),(554,156),(620,184),(549,207),(584,228),(653,238),(623,273),(588,293),(608,323),(508,255),(478,284),(444,293),(523,288),(537,326)]),
 ('SideTableDecorated',[(439,147),(499,145),(561,181),(378,185),(399,213),(430,201),(470,214),(532,219),(530,250),(406,244),(445,260),(462,276)])]:
 pack='LivingRoom';d=np.load(ROOT+'/CompanyGame/Temp/BoundaryAudit/'+pack+'_'+key+'.npz');s=Surface(d);x,y,z=s.c.T
 planeids=np.flatnonzero((s.n[:,2]>.99)&(z<.75));planeid=planeids[np.argmax(s.area[planeids])];level=z[planeid]
 props=s.cut(z>level+.023,z<level+.006)
 roles=np.full(len(z),'OakLight' if key=='CoffeeTableDecorated' else 'Mustard',dtype='<U12');roles[props]='Ivory'
 negative=pixels(d,[(523,405),(558,463),(461,466),(336,420)]) if key=='CoffeeTableDecorated' else pixels(d,[(458,334),(405,411),(513,408)])
 leaves=s.cut(pixels(d,points,r=8),negative|~props)
 roles[leaves]='Leaf'
 np.savez_compressed(OUT+'/'+pack+'_'+key+'.npz',roles=roles,coords=d['coords'],face_count=len(roles))
 print(key,'table plane',level,np.unique(roles,return_counts=True))

# Recipe: shoecabinet_mask.py
pack,key='Essentials','ShoeCabinet';d=np.load(ROOT+'/CompanyGame/Temp/BoundaryAudit/'+pack+'_'+key+'.npz');s=Surface(d);x,y,z=s.c.T
roles=np.full(len(z),'Ivory',dtype='<U12')
leaves=s.cut(pixels(d,[(211,235),(266,246),(300,270),(274,282),(198,285),(158,299),(145,340),(132,377),(137,400),(126,430),(123,470),(140,364),(164,317),(157,276)],r=6),pixels(d,[(249,330),(223,355),(194,392),(245,507),(360,509)],r=10))
roles[leaves]='Leaf'
positive=(y<-.36)&(s.n[:,1]<-.96)&(z>.36)&(z<.62)
negative=((np.abs(s.n[:,0])>.85)&(np.abs(x)>.44))|((s.n[:,2]>.8)&(z>.60))|(z<.28)|(y>.02)|leaves
door=s.cut(positive,negative);roles[door]='Oak'
legs=s.cut((z<.05)|pixels(d,[(219,674),(729,674)],r=9),(z>.11)|leaves|door);roles[legs]='Oak'
lamp=s.cut(pixels(d,[(670,275),(677,341),(649,288),(710,289)],r=10),pixels(d,[(668,371),(610,349),(740,348),(545,380)],r=10)|leaves|door|legs)
roles[lamp]='Terracotta'
bulb=s.cut(pixels(d,[(668,278),(642,291),(710,287)],r=10),pixels(d,[(671,344),(650,348)],r=8)|~lamp);roles[bulb]='Glow'
np.savez_compressed(OUT+'/'+pack+'_'+key+'.npz',roles=roles,coords=d['coords'],face_count=len(roles))
print(pack,key,np.unique(roles,return_counts=True))

# Recipe: office_swivel_masks.py
for pack,key,fg,bg,base_fg in [
 ('HomeOffice','OfficeChair',[(450,220),(520,356),(423,150),(350,470),(476,517),(207,397),(231,342),(366,532)],[(669,462),(696,392),(580,613),(682,514)],[(433,703),(530,751),(237,800),(544,801),(244,666),(498,652)]),
 ('LivingRoom','SwivelChair',[(518,179),(597,281),(420,109),(444,332),(314,464),(429,517),(220,348),(240,270),(600,362),(237,390),(229,410),(172,435),(111,362),(118,377),(150,497)],[(681,502),(740,430),(613,602),(89,315),(102,471),(393,659)],[(324,744),(536,794),(430,707),(590,671)])]:
 d=np.load(ROOT+'/CompanyGame/Temp/BoundaryAudit/'+pack+'_'+key+'.npz');s=Surface(d);x,y,z=s.c.T
 inner=((x<-.25)&(s.n[:,0]>.7)|(x>.25)&(s.n[:,0]<-.7))&(z>.4)&(y<.20)
 seatfront=(np.abs(x)<.30)&(s.n[:,1]<-.8)&(y<-.2)&(z>.335)&(z<.49)
 textile=s.cut(pixels(d,fg,r=17,view='Angle')|inner|seatfront,pixels(d,bg,r=9,view='Angle')|((s.n[:,1]>.9)&(y>.25)))
 roles=np.where(textile,'Olive','Oak').astype('<U12')
 base=s.cut((z<.20)|pixels(d,base_fg,r=10,view='Angle'),z>.29);roles[base]='Charcoal'
 np.savez_compressed(OUT+'/'+pack+'_'+key+'.npz',roles=roles,coords=d['coords'],face_count=len(roles))
 print(pack,key,np.unique(roles,return_counts=True))

# Recipe: nonoffice_last_masks.py
def save(pack,key,d,roles):
 np.savez_compressed(OUT+'/'+pack+'_'+key+'.npz',roles=roles.astype('<U12'),coords=d['coords'],face_count=len(roles))
 print(pack,key,np.unique(roles,return_counts=True))
pack,key='Essentials','FlowerVase';d=np.load(ROOT+'/CompanyGame/Temp/BoundaryAudit/'+pack+'_'+key+'.npz');s=Surface(d);x,y,z=s.c.T
roles=np.full(len(z),'Terracotta',dtype='<U12')
buds=[(235,126),(258,149),(229,166),(300,183),(318,217),(365,164),(293,305),(281,336),(342,360),(424,350),(413,273),(482,267),(503,293),(588,196),(562,201),(656,204),(676,234),(638,258),(579,307),(624,333),(543,368),(553,397),(598,397)]
stems=[(292,252),(335,267),(357,352),(373,423),(465,391),(504,348),(549,296),(580,259),(613,235),(505,378),(575,366),(439,337),(276,190)]
plant=s.cut(pixels(d,buds+stems,r=8),pixels(d,[(413,562),(423,701),(302,574),(511,610),(413,472)],r=13))
roles[plant]='Leaf'
flower=s.cut(pixels(d,[(343,71),(257,144),(452,140),(292,231),(261,303),(274,373),(389,373),(472,326),(453,267),(548,254),(577,241),(612,282),(639,307),(596,341),(567,398),(560,444),(505,420),(530,437),(375,375),(385,385),(301,184)],r=9,view='Angle')|pixels(d,[(228,165),(637,258)],r=8),pixels(d,[(391,372),(583,229),(626,237),(506,373),(556,304),(588,331),(518,309)],r=5)|pixels(d,[(500,281),(320,381),(314,360),(349,286),(513,332),(541,418),(365,165),(377,191),(397,189),(270,187),(570,340),(383,302),(381,415),(337,306),(513,355),(449,395),(478,284),(481,393),(592,309),(351,169),(371,207),(340,349),(430,336)],r=6,view='Angle')|~plant)
roles[flower]='Ivory';save(pack,key,d,roles)
pack,key='Kitchen','WallCabinet';d=np.load(ROOT+'/CompanyGame/Temp/BoundaryAudit/'+pack+'_'+key+'.npz');s=Surface(d);x,y,z=s.c.T
roles=np.full(len(z),'OakLight',dtype='<U12')
leafpts=[(755,407),(732,387),(712,450),(757,456)]
leaves=s.cut(pixels(d,leafpts,r=6),pixels(d,[(683,409),(681,438),(670,453),(712,468),(752,482),(619,428)],r=7))
roles[leaves]='Leaf'
contents=pixels(d,[(449,430),(546,434),(477,535),(549,535),(685,409),(681,438),(692,551),(526,469),(520,493)],r=11)
wood=pixels(d,[(473,320),(669,321),(758,551),(634,578),(716,463),(596,461),(620,417),(740,577),(121,448),(617,540),(625,341),(450,578)],r=6)
white=s.cut(contents|pixels(d,[(448,422),(440,441),(446,407),(512,441),(510,472),(496,460),(518,549),(444,528)],r=8,view='Angle'),wood|leaves);roles[white]='Ivory'
door=s.cut((x<-.175)&(y<-.398)|pixels(d,[(169,395),(276,395),(268,528),(179,525),(320,553),(326,363)],r=10),((x>-.147)|(y>-.30))|leaves|white)
roles[door]='Ivory';save(pack,key,d,roles)
pack,key='Kitchen','DishRack';d=np.load(ROOT+'/CompanyGame/Temp/BoundaryAudit/'+pack+'_'+key+'.npz');s=Surface(d);x,y,z=s.c.T
roles=np.full(len(z),'OakLight',dtype='<U12')
platepts=[[(193,359),(184,403)],[(246,343),(261,405)],[(304,352),(313,411)],[(372,327),(380,404)],[(423,345),(435,402)],[(483,340),(489,404)],[(559,337),(559,402)]]
wood=pixels(d,[(189,438),(279,450),(389,446),(494,442),(564,441),(637,454),(688,520),(487,518),(439,522),(389,532),(491,569),(678,360),(724,374),(739,379),(721,621),(429,628),(304,630),(169,589),(181,609),(229,582),(279,578),(358,580),(450,580),(522,579),(584,574)],r=5)
wood|=pixels(d,[(304,493),(377,547),(570,603),(507,622),(297,633)],r=6,view='Angle')
plates=s.cut(pixels(d,sum(platepts,[]),r=7),wood)
roles[plates]='Ivory'
for i in [0,2,4,6]:
 p=pixels(d,platepts[i],r=7)
 n=pixels(d,sum([v for j,v in enumerate(platepts) if j!=i],[]),r=7)|~plates
 part=s.cut(p,n);roles[part]='Olive'
holder=s.cut(pixels(d,[(694,515),(715,562),(656,509)],r=8),pixels(d,[(668,444),(741,441),(685,628),(626,550),(695,405)],r=6)|pixels(d,[(605,451),(644,469),(673,487),(698,546)],r=7,view='Angle')|plates)
roles[~plates]='OakLight';save(pack,key,d,roles)


# Recipe: wallorganizer_mask.py
d=np.load(ROOT+'/CompanyGame/Temp/OfficeBoundary/WallOrganizer.npz');s=Surface(d)
whole=pixels(d,[(262,292),(302,296),(277,316),(246,228),(278,222),(315,235),(233,257),(282,250),(319,258)],r=9)
whole|=pixels(d,[(239,235),(280,199),(309,214),(337,210),(333,229),(294,260),(283,300)],r=9,view='Angle')
board=pixels(d,[(212,175),(352,177),(200,301),(365,302),(270,345),(177,339),(377,347),(385,240)],r=15)
plant=s.cut(whole,board)
leaf=pixels(d,[(246,228),(278,222),(315,235),(233,257),(282,250),(321,255),(210,259),(336,234)],r=7)
leaf|=pixels(d,[(239,235),(280,199),(309,214),(337,210),(333,229),(294,260)],r=7,view='Angle')
pot=pixels(d,[(255,298),(304,299),(280,318)],r=10)|~plant
foliage=s.cut(leaf,pot)
roles=np.full(len(s.f),'Ivory',dtype='<U12');roles[plant]='Oak';roles[foliage]='Leaf'
out=ROOT+'/ArtSource/FurnitureCollections/WhiteWood/Masks/HomeOffice_WallOrganizer.npz'
np.savez_compressed(out,roles=roles,coords=d['coords'],face_count=len(roles))
print('WallOrganizer',len(roles),np.unique(roles,return_counts=True))
