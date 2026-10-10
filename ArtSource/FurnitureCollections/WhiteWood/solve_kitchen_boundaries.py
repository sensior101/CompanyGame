"""Surface cuts with audited front/angle seeds for the three kitchen sets."""
import os,sys,numpy as np
ROOT='C:/서현/프로젝트/companyGame'
sys.path.insert(0,ROOT+'/CompanyGame/Temp/BoundaryLib')
sys.path.insert(0,ROOT+'/ArtSource/FurnitureCollections/WhiteWood')
from mesh_cut import Surface
OUT=ROOT+'/ArtSource/FurnitureCollections/WhiteWood/NonOfficeMasks'

def pixels(d,points,r=10,view='Front'):
    out=np.zeros(len(d['faces']),bool);v=d[view];px=np.c_[v[:,0]*900,(1-v[:,1])*900]
    for point in points:
        selected=np.linalg.norm(px-point,axis=1)<r
        if selected.any():
            front=np.min(v[selected,2]);out|=selected&(v[:,2]<front+.015)
    return out

def solve(key):
    d=np.load(ROOT+'/CompanyGame/Temp/KitchenBoundary/Kitchen_'+key+'.npz');s=Surface(d)
    x,y,z=s.c.T;nx,ny,nz=s.n.T
    roles=np.full(s.count,'Oak',dtype='<U20')
    def p(points,r=10,view='Front'):return pixels(d,points,r,view)
    def cut(role,pos,neg,prior=None,weight=.3):
        result=s.cut(pos&~neg,neg,prior,prior_weight=weight);roles[result]=role;return result
    if key=='Shelf':
        ceramic=cut('Ivory',p([(318,242),(484,247),(535,250),(598,218),(487,435),(575,420),(638,435),(547,650),(533,602)],15)|p([(496,226),(552,244),(536,274),(497,245)],8,'Angle'),
                    (abs(x)>.46)|((nz>.96)&((z<.13)|((z>.39)&(z<.45))|((z>.72)&(z<.78))))|p([(331,650),(325,400),(300,506),(400,715),(400,295)]),
                    ((z>.12)&(z<.32)&(x>0))|((z>.44)&(z<.67)&(x>0))|((z>.78)&(abs(x)<.4)))
        metal=cut('Charcoal',(abs(x)>.478),ceramic|(abs(x)<.435),abs(x)>.457)
        leaf=cut('Leaf',p([(274,133),(358,130),(278,180),(322,181),(385,178),(374,194),(402,199)],8)|p([(294,129),(340,137),(382,181),(361,166),(248,178),(344,216)],8,'Angle'),
                 p([(280,243),(354,253),(341,272),(503,240),(599,220),(253,300),(423,299)],12)|metal|(z<.75),
                 (x<-.03)&(z>.86))
        cut('Charcoal',p([(555,570)],8),(z<.29)|(x<.1)|(x>.3), (x>.12)&(z>.31)&(z<.36))
    elif key=='CookingIsland':
        # Ivory countertop/body surround, wood inset cabinet faces/feet.
        roles[:]='Ivory'
        cabinet=cut('Oak',(z<.53)&(ny<-.985)&(abs(x)<.44),
                    (z>.66)|(abs(x)>.49)|(y>-.2)|(nz>.6), (z<.56)&(y<-.37))
        cut('Oak',z<.035,z>.08,z<.055)
        stove=cut('Charcoal',p([(217,378),(454,384),(555,385),(480,358),(277,366),(366,371)],9)|p([(209,350),(346,388),(449,391),(514,448),(470,360)],11,'Angle'),
                  p([(433,419),(733,416),(398,591),(640,430),(316,302),(318,280),(601,338),(703,354)],12)|(z<.61)|(x>.255),
                  (z>.60)&(z<.73)&(x<.24))
        pot=cut('Ivory',p([(315,300),(293,283),(364,316),(320,346),(241,292),(396,292)],9),
                (z<.75)|(x>.02)|(x<-.37)|p([(261,367),(364,369),(484,380)],8),
                (z>.70)&(x<-.065)&(x>-.33))
        cut('Charcoal',stove|((z>.689)&(z<.754)&(x<-.05)&(x>-.345))|p([(350,365),(375,365),(270,345),(279,347),(311,354)],7,'Angle'),
            (z>.77)|(z<.60)|(x>.255), stove|((z>.67)&(z<.765)&(x<.02)&(x>-.36)))
        pot=cut('Ivory',(z>.795)&(z<.925)&(x>-.35)&(x<-.025),
                (z<.727)|(x>.015)|(x<-.375),
                (z>.747)&(x>-.36)&(x<-.01))
        cut('Charcoal',p([(318,253)],8),(z<.91)|(x>-.08)|(x<-.33),(z>.945)&(x<-.1))
        cut('Oak',p([(575,311),(607,265),(621,280)],8)|(p([(591,320)],8,'Angle')),
                p([(606,360),(629,337),(644,359),(691,362)],10)|(z<.69)|(x<.15),
                (z>.84)&(x>.17)&(x<.29))
        plant=cut('Leaf',p([(666,248),(674,275),(672,304),(694,317),(719,284),(724,312),(740,298)],8)|p([(632,322),(646,349),(674,347),(698,364),(629,370),(654,390)],8,'Angle'),
                  p([(679,359),(723,359),(704,380),(640,356),(615,269)],9)|(z<.76)|(x<.27),
                  (x>.31)&(z>.85))
    elif key=='Sink':
        roles[:]='Ivory'
        cabinet=cut('Oak',(ny<-.94)&(z<.50)&(z>.07)&(abs(x)<.45),
                    (z>.605)|(abs(x)>.485)|(y>-.28)|(nz>.75), (z<.555)&(y<-.37))
        cut('Oak',z<.04,z>.09,z<.06)
        basin=cut('Metal',(abs(x)<.17)&(y>-.32)&(y<.25)&(z<.635)&(z>.48),
                  (abs(x)>.185)|(y>.285)|(y<-.355)|(z>.651)|(z<.44),
                  (abs(x)<.175)&(y>-.34)&(y<.27)&(z>.45)&(z<.645))
        faucet=cut('Brass',p([(434,293),(438,352),(448,301)],7)|p([(480,281),(486,349)],8,'Angle'),
                   (z<.66)|(abs(x)>.12)|p([(383,384),(502,379)],9), (abs(x)<.08)&(z>.69))
        board=cut('Oak',p([(654,328),(662,273),(683,357)],12)|p([(691,336),(715,296),(733,365)],10,'Angle'),
                  (z<.65)|(x<.22)|(y<-.1), (x>.25)&(z>.70))
        leaf=cut('Leaf',p([(136,328),(196,281),(228,306),(187,325),(187,362),(151,486),(141,427)],8)|p([(157,274),(193,260),(226,245),(247,263),(262,276),(187,294),(169,333),(169,342),(171,350),(157,382),(143,428)],7,'Angle'),
                 p([(223,374),(230,355),(232,386),(254,357),(301,356)],7)|p([(211,337),(235,327),(292,318),(346,328),(282,395)],8,'Angle')|(x>-.23)|(z<.32),
                 (x<-.32)&(z>.60))
    np.savez_compressed(OUT+'/Kitchen_'+key+'.npz',roles=roles,coords=d['coords'],face_count=len(roles))
    print(key,dict(zip(*np.unique(roles,return_counts=True))),flush=True)

if __name__=='__main__':
    for key in (sys.argv[1:] or ['Shelf','CookingIsland','Sink']):solve(key)
