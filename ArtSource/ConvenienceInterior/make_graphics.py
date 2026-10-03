"""Original packaging and Korean shop graphics, no reference-photo pixels used."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import random

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'CompanyGame/Assets/Art/Interiors/ConvenienceStore/Textures'
OUT.mkdir(parents=True, exist_ok=True)
FONT = 'C:/Windows/Fonts/malgunbd.ttf'
def font(n): return ImageFont.truetype(FONT, n)
def label(d,xy,s,size=40,fill='white',anchor='mm'):
    d.text(xy,s,font=font(size),fill=fill,anchor=anchor,stroke_width=0)

palette=['#DB3626','#EDA921','#247EB9','#418C39','#F0D461','#733674','#E97825','#25545B']
names=['바삭칩','감자톡','옥수수칩','양파링','허니칩','매콤스낵','고소한칩','김스낵']
atlas=Image.new('RGB',(4096,4096),'white')
for i in range(64):
    random.seed(i+207)
    tile=Image.new('RGB',(512,512),palette[i%8]); d=ImageDraw.Draw(tile)
    d.rectangle((12,10,500,30),fill='#F4E8B8'); d.rectangle((12,482,500,502),fill='#F4E8B8')
    for x in range(18,500,12): d.line((x,10,x,30),fill='#C4B480',width=2)
    label(d,(45,52),'매일 좋은 맛',19,anchor='lm')
    cat=i//8
    name=names[i%8] if cat<2 else ['얼큰라면','맛있는 우유','후레시 주스','스파클링','달콤초코','오늘의 한끼'][cat-2]
    d.rounded_rectangle((32,87,480,215),radius=36,fill='#FFF5DB')
    label(d,(256,148),name,61 if len(name)<6 else 47,fill='#42231B')
    label(d,(256,239),['ORIGINAL','CRUNCHY','HOT & SPICY','FRESH DAILY','VITAMIN C','ZERO SUGAR','SWEET BREAK','READY TO EAT'][cat],22)
    if cat<2:
        for j in range(11):
            x=random.randint(50,365); y=random.randint(274,413)
            d.ellipse((x,y,x+100,y+54),fill='#FFCE67',outline='#B7782D',width=4)
            for k in range(4): d.arc((x+8,y+8+k*5,x+92,y+36+k*3),0,160,fill='#E6A947',width=2)
    elif cat==2:
        d.ellipse((64,278,448,453),fill='#E9D8AC',outline='#4E2821',width=8)
        d.ellipse((86,290,426,418),fill='#CF4C19')
        for j in range(25):
            x=random.randint(112,330); y=random.randint(308,365)
            d.arc((x,y,x+70,y+27),0,310,fill='#F8C95B',width=5)
        d.ellipse((257,312,337,361),fill='#FFF2CD'); d.ellipse((277,319,310,351),fill='#FDBD35')
    elif cat in (3,4,5):
        d.ellipse((132,288,380,452),fill='#FDF4D7')
        if cat==3:
            d.polygon([(186,423),(186,321),(228,286),(287,286),(331,321),(331,423)],fill='#B8DCF4')
            label(d,(257,371),'MILK',31,fill='#245B81')
        else:
            for j in range(3):
                x=130+j*73; d.ellipse((x,306,x+117,427),fill=['#F8A523','#6AAA42','#E85645'][i%3],outline='white',width=5)
            d.ellipse((232,290,274,312),fill='#257741')
    elif cat==6:
        for x in (100,205,310):
            d.rounded_rectangle((x,285,x+93,420),radius=12,fill='#583326',outline='#CA935F',width=5)
            d.line((x+10,352,x+83,352),fill='#AD7850',width=4)
    else:
        d.rounded_rectangle((65,285,447,442),radius=25,fill='#292F30')
        d.rounded_rectangle((85,302,242,424),radius=10,fill='#F9ECCF')
        for j in range(6):
            x=258+(j%2)*70; y=302+(j//2)*39
            d.ellipse((x,y,x+57,y+34),fill=['#B95925','#68A345','#DFA342'][j%3])
    label(d,(256,468),['맛있게 바삭!  60 g','행복한 간식시간','진한 국물  110 g','100%  250 ml','상큼한 하루  350 ml','시원하게 즐겨요','COCOA  70 g','매일 신선하게'][cat],18)
    atlas.paste(tile,((i%8)*512,(i//8)*512))
atlas.save(OUT/'Products.png')

signs=[('음료  ·  유제품','DRINKS & DAIRY','#146F55'),('간편식','FRESH FOOD','#146F55'),('오늘도 좋은 하루','FRESH COFFEE','#D94F29'),('따뜻한 한끼','HOT & FRESH','#EC9D25'),('맛있는 간식','EVERYDAY FAVORITES','#D94F29'),('즐거운 디저트 타임','ICE CREAM','#38A9CD'),('언제나 가까이','YOUR NEIGHBORHOOD STORE','#65BADA'),('직원 전용','STAFF ONLY','#726B61'),('컵라면 · 즉석식품','QUICK MEALS','#146F55'),('행복한 간식시간','SNACKS','#EBC63F'),('₩ 1,500','2 + 1','#F5EFD9'),('₩ 2,000','BEST PRICE','#F5EFD9'),('24  /  DAILY','매일, 당신 가까이','#146F55'),('나가는 곳  →','EXIT','#249B64'),('신선한 오늘','FRESH EVERY DAY','#146F55'),('COFFEE','₩ 1,500','#512F26')]
atlas=Image.new('RGB',(4096,2048),'white')
for i,(s,sub,bg) in enumerate(signs):
    t=Image.new('RGB',(1024,512),bg);d=ImageDraw.Draw(t)
    fg='#294C3F' if i in (9,10,11) else '#FFF9E5'
    d.rectangle((25,25,999,487),outline=fg,width=3)
    label(d,(512,170),s,72 if len(s)<11 else 62,fg)
    label(d,(512,274),sub,36,fg)
    if i in (2,3,4,5,15):
        for j in range(5):
            x=330+j*80
            if i in (2,15):
                d.rounded_rectangle((x,337,x+55,433),radius=8,fill='#FAE1AB'); d.ellipse((x,329,x+55,348),fill='#653D26')
            else:
                d.ellipse((x,350,x+66,429),fill=['#E5BC6A','#8A522C','#EECB8F','#DF806D','#D6E389'][j])
    else:
        d.line((380,376,644,376),fill=fg,width=8)
    atlas.paste(t,((i%4)*1024,(i//4)*512))
atlas.save(OUT/'Signs.png')
print(OUT)
