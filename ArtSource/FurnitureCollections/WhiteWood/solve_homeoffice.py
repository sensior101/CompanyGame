import sys,os,json,numpy as np
ROOT=r'C:/서현/프로젝트/companyGame';sys.path.insert(0,ROOT+'/CompanyGame/Temp/BoundaryLib');sys.path.insert(0,ROOT+'/ArtSource/FurnitureCollections/WhiteWood')
from mesh_cut import Surface
SRC=ROOT+'/CompanyGame/Temp/OfficeBoundary';OUT=ROOT+'/ArtSource/FurnitureCollections/WhiteWood/Masks';os.makedirs(OUT,exist_ok=True)
def solve(key):
 data=np.load(SRC+'/'+key+'.npz');s=Surface(data);x,y,z=s.c.T;nx,ny,nz=s.n.T;roles=np.full(s.count,'Oak',dtype='<U20');comp=s.components()
 def apply(role,pos,neg,prior=None):
  pos=np.asarray(pos,bool)&~np.asarray(neg,bool);mask=s.cut(pos,neg,prior,prior_weight=.3);roles[mask]=role;print(key,role,int(mask.sum()),flush=True);return mask
 if key=='Desk':
  top=apply('Ivory',(z>.585)&(z<.620)&((nz>.92)|(y<-.38)),(z<.55)|(z>.637),(z>.563)&(z<.625))
  # Upright laptop and its metal base, detached at their physical bevels.
  laptop=apply('Metal',(abs(x)<.13)&(z>.647)&(z<.69),(abs(x)>.225)|(z<.622)|(z>.97),(abs(x)<.19)&(z>.626))
  apply('Screen',(abs(x)<.13)&(z>.73),(abs(x)>.22)|(z<.671),(abs(x)<.195)&(z>.677))
  apply('Paper',(x>.245)&(x<.36)&(z>.80),(x<.21)|(x>.39)|(z<.62),(x>.23)&(x<.37)&(z>.63))
  apply('Terracotta',(x>.395)&(z>.72),(x<.36)|(z<.625),(x>.37)&(z>.633))
  # Small planter foliage: detached leaf shells and the neck above the wooden pot.
  apply('Leaf',(x<-.31)&(z>.80)|(comp>0),(x>-.29)|((z<.73)&(comp==0))|((y>.32)&(z<.77)),(x<-.31)&(z>.74))
  apply('Ivory',(x>.19)&(x<.25)&(z>.70),(x<.16)|(x>.27)|(z<.624),(x>.17)&(x<.265)&(z>.634))
 elif key=='DeskLamp':
  roles[:]='Orange'
  fit=json.load(open(SRC+'/bulb_sphere.json'));center=np.array(fit['center']);radius=fit['radius'];delta=data['centers']-center;dist=np.linalg.norm(delta,axis=1);err=abs(dist-radius);radial=np.sum(s.n*delta/np.maximum(dist[:,None],1e-9),axis=1)
  apply('BulbWhite',(err<.0015)&(radial>.87),(err>.007)|(radial<.3),(err<.004)&(radial>.7))
 elif key=='ComputerSet':
  roles[:]='Oak'
  top=apply('Ivory',(z>.214)&(z<.256),(z<.198)|(z>.263), (z>.202)&(z<.26))
  apply('Charcoal',(z>.53)|((abs(x)<.15)&(z>.29)&(z<.44)), (z<.25)|((x>.28)&(z<.52))|((x<-.25)&(z<.5)),(z>.267)&(x<.34)&(x>-.47))
  # Keyboard and mouse stay ivory on a wood tray.
  kb=comp==1
  apply('Ivory',kb&(z>.056),~kb|(z<.037),kb&(z>.043))
  apply('Leaf',(x>.30)&(z>.415)&(z<.52), (x<.25)|(z<.365)|(z>.54), (x>.27)&(z>.381)&(z<.53))
  apply('Ivory',(x<-.30)&(z>.29)&(z<.35), (x>-.27)|(z<.249)|(z>.4),(x<-.29)&(z>.264))
 elif key=='Shelf':
  roles[:]='Oak'
  # Foliage shells are independent from the shelf. The trailing vine has its own shell.
  plant=(comp>0)
  roles[comp>1]='Leaf'
  apply('Leaf',(comp==1)&(z>.91),(comp!=1)|((z<.847)&(x>-.37)&(x<-.18)&(y>-.4)),(comp==1)&(z>.869))
  apply('Leaf',(x>.16)&(x<.4)&(z>.24)&(z<.30)&(y<.36),(z<.217)|(z>.32)|(x<.12)|(x>.43)|(y>.39),(x>.16)&(x<.42)&(z>.225)&(z<.31))
  apply('Ivory',(x>.07)&(x<.24)&(z>.76)&(z<.9),(x<.025)|(x>.28)|(z<.727)|(z>.91),(x>.03)&(x<.25)&(z>.735))
  apply('Paper', (x<-.15)&(z>.47)&(z<.51), (x>-.02)|(z<.431)|(z>.55)|(y>.35)|(comp>0), (x<-.06)&(z>.447)&(z<.53))
  apply('Paper',(x<.12)&(x>-.24)&(z>.172)&(z<.23),(z<.135)|(z>.269)|(x<-.29)|(x>.15)|(y>.35),(x<.13)&(x>-.26)&(z>.15)&(z<.26))
 elif key=='Bookcase':
  roles[:]='Oak'
  apply('Ivory',(abs(x)<.42)&(y<-.25)&(z>.072)&(z<.295)&(ny<-.65),(z>.31)|(z<.041)|(y>-.17)|(abs(x)>.46),(abs(x)<.44)&(z<.302)&(z>.05)&(y<-.24))
  # Green is connected through plant stems, never a rectangular paint band on the case.
  apply('Leaf',((x>.14)&(z>.932))|((x>.43)&(z>.63)&(y<.2)),(x<.1)|(z<.607)|((x<.24)&(z<.88))|((y>.34)&(z<.91))|((abs(nx)>.995)&(z<.89)),(x>.19)&(z>.90)|(x>.46)&(z>.63))
  apply('Leaf',(x<-.12)&(x>-.39)&(z>.371)&(z<.435),(x<-.42)|(x>-.10)|(z<.36)|(z>.445)|(y>.16),(x<-.12)&(x>-.4)&(z>.365)&(z<.44))
 elif key=='DrawerUnit':
  roles[:]='Ivory'
  apply('Charcoal',(z<.045),(z>.073),(z<.061))
  apply('Oak',(z>.82)&(comp==0),(z<.784)|(comp>0),(z>.8)&(comp==0))
  roles[comp>0]='Leaf'
 elif key=='WallOrganizer':
  roles[:]='Ivory'
  pot=apply('Oak',(x>-.305)&(x<-.21)&(z>.73)&(z<.77)&(y<-.18), (z<.695)|(x<-.36)|(x>-.16)|(y>.20)|(z>.89), (x>-.34)&(x<-.175)&(z>.713)&(z<.809)&(y<.03))
  apply('Leaf',(x>-.38)&(x<-.15)&(z>.82)&(z<.871)&(y<-.08),(z<.788)|(x<-.41)|(x>-.125)|(y>.24)|(z>.89),(x>-.4)&(x<-.14)&(z>.80)&(z<.887)&(y<.05))
 np.savez_compressed(OUT+'/HomeOffice_'+key+'.npz',roles=roles,face_count=len(roles),vertex_count=len(s.v));print('SAVED',key,flush=True)
for key in (sys.argv[1:] or ['Desk','DeskLamp','ComputerSet','Shelf','Bookcase','DrawerUnit','WallOrganizer']):solve(key)
