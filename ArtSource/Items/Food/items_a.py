"""3D 로우폴리 음식 모델 1: 기본 12종 + 과일 + 채소."""
import math

from mesh3d import PI, ngon, rnd, rrect, sector, scl, star, wob

GREEN = (96, 160, 64)
BROWN = (110, 76, 48)
WHITE = (248, 246, 238)


# ---------- 공용 부품 ----------

def bowl(m, body, rim, inner, r=1.0, h=.62, at=(0, 0, 0)):
    """그릇. 국물/내용물을 얹을 높이를 돌려준다."""
    m.lathe(body, [(0, 0), (.42 * r, 0), (.5 * r, .05), (.8 * r, .4 * h), (r, h)], seg=12, caps=False, at=at)
    m.lathe(rim, [(.93 * r, h - .02), (1.02 * r, h - .02), (1.02 * r, h + .05), (.93 * r, h + .05)], seg=12, closed=True, at=at)
    m.lathe(inner, [(.94 * r, h), (.7 * r, .34 * h + .04), (0, .3 * h)], seg=12, caps=False, at=at)
    return at[1] + h * .78


def apple_prof(sy=.88, dm=.16, n=8):
    out = []
    for i in range(n + 1):
        ph = PI * i / n
        y = -math.cos(ph) * sy
        if ph > PI - .55:
            y -= dm * (ph - (PI - .55)) / .55
        elif ph < .45:
            y += dm * .5 * (.45 - ph) / .45
        out.append((math.sin(ph), y))
    return out


def fruit(col, sy=.88, dm=.16, stem=True, leaf=True, cap=None, star_cap=False, blush=None):
    def f(m):
        m.lathe(col, apple_prof(sy, dm), seg=9, sc=(1, 1, 1))
        if blush:
            m.sphere(blush, (.3, .38, .25), seg=6, rings=4, at=(.5, .1, .62))
        top = sy - dm
        if stem:
            m.rod(BROWN, (0, top - .02, 0), (.06, top + .3, 0), .05)
        if leaf:
            m.sphere(GREEN, (.36, .05, .17), seg=6, rings=3, at=(.34, top + .16, 0), rot=(0, 0, 25))
        if cap:
            m.cyl(cap, .2, .04, at=(0, top - .05, 0))
        if star_cap:
            m.prism((84, 140, 64), star(5, .5, .18), .07, at=(0, top - .06, 0), rot=(0, 20, 0))
    return f


def sack(m, body, band, emblem):
    m.lathe(body, [(0, 0), (.72, 0), (.86, .18), (.88, .95), (.62, 1.3), (.34, 1.5)], seg=10, caps=False)
    m.lathe(body, [(.34, 1.5), (.52, 1.86), (.24, 1.8)], seg=8, caps=False)
    m.torus(band, .36, .07, seg=8, ring=4, at=(0, 1.52, 0))
    m.box(WHITE, (.66, .56, .06), at=(0, .66, .84), rot=(0, 0, 0))
    emblem(m)


def carton(m, front, roof, label, accent):
    m.box(front, (.9, 1.1, .9), at=(0, .55, 0))
    m.prism(roof, [(-.45, 0), (.45, 0), (0, .38)], .9, "z", at=(0, 1.1, 0))
    m.box(roof, (.08, .05, .9), at=(0, 1.5, 0))
    m.box(label, (.6, .55, .04), at=(0, .55, .46))
    m.sphere(accent, (.14, .2, .05), seg=6, rings=4, at=(0, .55, .5))


# ---------- 기본 12종 ----------

def bread(m, crust=(190, 124, 58), crumb=(248, 226, 176)):
    prof = [(-.62, 0), (.62, 0), (.62, .5), (.8, .66), (.74, .98), (.42, 1.14), (0, 1.18), (-.42, 1.14), (-.74, .98), (-.8, .66), (-.62, .5)]
    m.prism(crust, prof, 1.3, "z", at=(0, -.6, 0))
    m.prism(crumb, scl(prof, .86, (0, .55)), 1.34, "z", at=(0, -.6, 0))


def croissant(m):
    sizes = [(.34, .26), (.5, .36), (.66, .46), (.82, .56), (.66, .46), (.5, .36), (.34, .26)]
    for i, (rx, ry) in enumerate(sizes):
        a = math.radians(192 + i * 26)
        m.sphere((224, 158, 72) if i % 2 else (206, 138, 58), (rx, ry, .4), seg=8, rings=4,
                 at=(1.3 * math.cos(a), ry * .6, 1.3 * math.sin(a)), rot=(0, -math.degrees(a), 0))


def baguette(m):
    s = m.mark()
    m.tube((214, 154, 84), 2.5, rnd(.5), .3, .3, seg=8, n=10)
    for x in (-.7, -.25, .2, .65):
        m.sphere((246, 214, 152), (.2, .05, .09), seg=6, rings=3, at=(x, .29, 0), rot=(0, 0, 0))
    m.xform(s, rot=(0, 0, -20))


def rice_ball(m):
    tri = [(-.9, 0), (.9, 0), (.14, 1.55), (-.14, 1.55)]
    rr = [(-.86, .05), (.86, .05), (.3, 1.35), (-.3, 1.35)]
    m.prism((246, 245, 236), rr, .75, "z", at=(0, 0, 0))
    m.prism((244, 243, 232), scl(rr, .96), .82, "z")
    m.prism((40, 72, 56), [(-.86, .05), (.86, .05), (.62, .62), (-.62, .62)], .8, "z")
    m.box((250, 240, 214), (.5, .24, .04), at=(0, .34, .41))


def kimbap(m):
    fills = [((-.13, -.1), (240, 200, 60)), ((.13, -.1), (232, 122, 52)), ((-.13, .12), (96, 152, 66)), ((.13, .12), (226, 122, 132)), ((0, 0), (150, 92, 62))]
    for x, z in [(-.6, -.45), (.6, -.45), (0, .55)]:
        m.cyl((36, 62, 50), .5, .34, seg=10, at=(x, 0, z))
        m.cyl((248, 245, 235), .42, .36, seg=10, at=(x, 0, z))
        for (dx, dz), col in fills:
            m.cyl(col, .1, .38, seg=6, at=(x + dx, 0, z + dz))


def tteokbokki(m):
    y = bowl(m, (240, 234, 222), (250, 246, 238), (250, 246, 238))
    m.cyl((196, 56, 36), .86, .04, seg=12, at=(0, y - .06, 0))
    for x, z, r in [(-.35, -.1, 20), (.1, -.3, 80), (.4, .1, -30), (-.05, .25, 60), (.25, .4, 10)]:
        m.tube((232, 96, 52), .75, rnd(.35), .18, .18, seg=6, n=4, at=(x, y + .08, z), rot=(0, r, 0))
    for x, z in [(-.5, .3), (.2, -.05), (.55, -.3)]:
        m.cyl((104, 170, 70), .06, .1, seg=5, at=(x, y + .12, z))


def hamburger(m):
    m.lathe((212, 148, 72), [(0, 0), (.88, 0), (.94, .1), (.9, .3), (0, .3)], seg=12)
    m.cyl((112, 66, 44), .95, .26, seg=12, at=(0, .3, 0))
    m.prism((246, 196, 52), ngon(4, 1.05, PI / 4), .05, at=(0, .56, 0), rot=(0, 10, 0))
    m.prism((108, 176, 72), star(9, 1.06, .92), .1, at=(0, .6, 0), rot=(0, 12, 0))
    m.lathe((222, 156, 78), [(0, .7), (.98, .7), (.98, .82), (.9, 1.05), (.65, 1.3), (.3, 1.48), (0, 1.52)], seg=12)
    for x, y, z in [(-.3, 1.42, -.1), (.2, 1.44, -.25), (.35, 1.32, .25), (-.35, 1.28, .35), (0, 1.5, .15)]:
        m.sphere((250, 232, 190), (.1, .04, .06), seg=5, rings=3, at=(x, y, z))


def milk(m):
    carton(m, (244, 247, 252), (176, 200, 232), (74, 124, 194), (248, 250, 255))


def tofu(m):
    m.box((250, 248, 236), (1.5, .6, 1.0), at=(0, .3, 0))
    for x in (-.28, .28):
        m.sphere((66, 54, 48), (.07, .09, .03), seg=6, rings=3, at=(x, .34, .5))
    m.box((66, 54, 48), (.2, .03, .02), at=(0, .18, .5))


def egg(m):
    prof = [(math.sin(PI * i / 7) * (1 - .16 * -math.cos(PI * i / 7)), -math.cos(PI * i / 7) * 1.25) for i in range(8)]
    m.lathe((238, 208, 162), prof, seg=9, sc=(.85, 1, .85), at=(0, 1.2, 0))


def flour(m):
    def wheat(mm):
        mm.box((196, 150, 60), (.05, .46, .05), at=(0, .66, .88))
        for i in range(3):
            for s in (-1, 1):
                mm.sphere((224, 176, 66), (.13, .04, .04), seg=5, rings=3, at=(s * .1, .58 + i * .14, .88), rot=(0, 0, s * 35))
    sack(m, (230, 206, 158), (198, 72, 62), wheat)


def coffee(m):
    m.lathe((246, 240, 228), [(0, 0), (.5, 0), (.66, 1.3)], seg=10)
    m.lathe((176, 120, 72), [(.535, .32), (.6, .92)], seg=10, caps=False, sc=(1.02, 1, 1.02))
    m.lathe((92, 62, 46), [(.72, 1.25), (.74, 1.36), (.6, 1.4), (.42, 1.5), (0, 1.5)], seg=10)


# ---------- 과일 ----------

def lemon(m):
    s = m.mark()
    m.tube((244, 214, 64), 2.1, rnd(.55), .72, .72, seg=8, n=8)
    m.sphere(GREEN, (.36, .05, .17), seg=6, rings=3, at=(.7, .62, 0), rot=(0, 0, 30))
    m.xform(s, rot=(0, 0, -15))


def strawberry(m):
    prof = [(0, -1.05), (.32, -.85), (.62, -.4), (.8, .15), (.72, .6), (.4, .82), (0, .8)]
    m.lathe((222, 52, 66), prof, seg=8)
    for a in range(0, 360, 60):
        m.sphere((90, 156, 64), (.36, .05, .14), seg=5, rings=3, at=(.3 * math.cos(math.radians(a)), .82, .3 * math.sin(math.radians(a))), rot=(0, -a, 18))
    for y, r in [(-.5, .5), (-.1, .74), (.3, .78), (.55, .58)]:
        for a in range(0, 360, 72):
            aa = math.radians(a + y * 40)
            m.sphere((252, 226, 120), .06, seg=4, rings=2, at=(r * math.cos(aa) * 1.0, y, r * math.sin(aa)))
    m.rod(GREEN, (0, .8, 0), (0, 1.1, 0), .05)


def banana(m):
    s = m.mark()
    m.tube((246, 214, 72), 2.6, rnd(.5), .26, .3, seg=5, n=10, bend=1.7)
    m.xform(s, at=(0, -.55, 0))


def grapes(m):
    pos = [(-.5, .55, 0), (0, .55, 0), (.5, .55, 0), (-.25, .1, .15), (.25, .1, .15), (-.5, .5, .4), (.5, .5, .4), (0, -.35, .1), (-.2, .2, .45), (.2, .2, .45), (0, .55, .45), (0, -.1, .5)]
    for i, (x, y, z) in enumerate(pos):
        m.sphere((112 + i % 3 * 8, 60, 140), .34, seg=7, rings=4, at=(x, y - .2, z))
    m.rod(BROWN, (0, .5, 0), (.1, 1.1, 0), .06)
    m.sphere(GREEN, (.4, .05, .2), seg=5, rings=3, at=(.35, .95, 0), rot=(0, 0, 20))


def watermelon(m):
    half = [(math.cos(math.radians(a)), math.sin(math.radians(a))) for a in range(180, 361, 20)]
    m.prism((70, 150, 80), half, .3, "z", at=(0, 1.0, 0))
    m.prism((208, 234, 176), scl(half, .92, (0, 0)), .32, "z", at=(0, 1.0, 0))
    m.prism((236, 72, 86), scl(half, .85, (0, 0)), .34, "z", at=(0, 1.0, 0))
    for x, y in [(-.4, -.25), (0, -.4), (.4, -.25), (-.2, -.65), (.25, -.6)]:
        m.sphere((40, 30, 30), (.05, .09, .03), seg=5, rings=3, at=(x, 1.0 + y, .18))


def pear(m):
    prof = [(0, -1), (.35, -.97), (.7, -.75), (.86, -.35), (.74, .1), (.5, .42), (.3, .72), (.22, .98), (0, 1.02)]
    m.lathe((190, 204, 82), prof, seg=9)
    m.rod(BROWN, (0, .98, 0), (.08, 1.35, 0), .05)
    m.sphere(GREEN, (.36, .05, .17), seg=5, rings=3, at=(.36, 1.2, 0), rot=(0, 0, 25))


def cherry(m):
    m.sphere((196, 30, 50), .45, seg=7, rings=4, at=(-.5, -.5, 0))
    m.sphere((210, 36, 56), .45, seg=7, rings=4, at=(.5, -.62, .1))
    m.rod(BROWN, (-.5, -.1, 0), (0, 1.0, 0), .04)
    m.rod(BROWN, (.5, -.2, .1), (0, 1.0, 0), .04)
    m.sphere(GREEN, (.4, .05, .2), seg=5, rings=3, at=(.3, .95, 0), rot=(0, 0, 20))


# ---------- 채소 ----------

def carrot(m, col=(238, 130, 44), r=.3, leaf=GREEN):
    s = m.mark()
    m.lathe(col, [(0, -1.15), (.09, -1.0), (r * .75, -.4), (r, .55), (r * .9, .85), (0, .88)], seg=7)
    for a, tilt in [(0, 0), (25, 20), (-25, -20)]:
        m.sphere(leaf, (.09, .38, .06), seg=5, rings=3, at=(math.sin(math.radians(a)) * .25, 1.15, 0), rot=(0, 0, -tilt))
    m.xform(s, rot=(0, 0, 40))


def radish(m):
    carrot(m, (244, 244, 234), .38, (110, 170, 80))


def potato(m):
    m.sphere((196, 152, 96), (1.0, .7, .78), seg=7, rings=4, rot=(0, 20, 8))
    for x, y, z in [(-.4, .5, .4), (.35, .55, -.3), (.1, .1, .6)]:
        m.sphere((150, 108, 64), .07, seg=4, rings=2, at=(x, y, z))


def sweet_potato(m):
    s = m.mark()
    m.tube((166, 72, 92), 2.3, rnd(.55), .42, .4, seg=7, n=8, bend=3.5)
    m.xform(s, rot=(0, 0, -12), at=(0, -.2, 0))


def onion(m):
    m.sphere((226, 168, 84), (.9, .8, .9), seg=8, rings=5, at=(0, .8, 0))
    m.cone((196, 140, 70), .28, .6, seg=6, at=(0, 1.35, 0))
    m.rod((220, 210, 180), (0, .1, 0), (.05, -.15, 0), .12)


def garlic(m):
    m.sphere((246, 240, 226), (.85, .75, .85), seg=8, rings=5, at=(0, .75, 0))
    m.cone((236, 228, 210), .2, .55, seg=6, at=(0, 1.3, 0))
    for a in range(0, 360, 72):
        m.rod((214, 200, 178), (.0, 1.35, 0), (.8 * math.cos(math.radians(a)), .6, .8 * math.sin(math.radians(a))), .02, 3)


def cabbage(m):
    m.sphere((154, 204, 104), (1, .92, 1), seg=8, rings=5, at=(0, .9, 0))
    for a in range(0, 360, 90):
        r = math.radians(a)
        m.sphere((116, 176, 84), (.55, .5, .12), seg=6, rings=4, at=(.55 * math.cos(r), .85, .55 * math.sin(r)), rot=(0, -a + 90, 15))


def broccoli(m):
    m.lathe((150, 196, 100), [(.16, 0), (.26, .55), (.34, .9)], seg=6, caps=True)
    for x, y, z, r in [(-.5, 1.2, 0, .5), (.5, 1.2, .1, .5), (0, 1.55, 0, .55), (-.2, 1.2, .5, .45), (.25, 1.15, -.45, .45)]:
        m.sphere((60, 140, 64), r, seg=7, rings=4, at=(x, y - .1, z))


def cucumber(m):
    s = m.mark()
    m.tube((78, 150, 64), 2.4, rnd(.28), .32, .32, seg=7, n=8)
    for x in (-.6, -.1, .5):
        m.sphere((150, 200, 110), .05, seg=4, rings=2, at=(x, .3, .1))
    m.xform(s, rot=(0, 0, -18))


def corn(m):
    s = m.mark()
    m.tube((248, 212, 72), 2.4, rnd(.4), .5, .5, seg=8, n=10)
    for a in (0, 120, 240):
        r = math.radians(a)
        m.sphere((104, 168, 72), (.85, .2, .32), seg=6, rings=3, at=(-.75, .3 * math.cos(r), .3 * math.sin(r)), rot=(a, 0, -8))
    m.xform(s, rot=(0, 0, 25))


def mushroom(m):
    m.lathe((236, 228, 206), [(.2, 0), (.26, .5), (.34, .75)], seg=7)
    m.lathe((176, 110, 66), [(0, .62), (1.0, .62), (.95, .82), (.7, 1.1), (.32, 1.3), (0, 1.34)], seg=9)
    for x, y, z in [(-.3, 1.22, .1), (.3, 1.15, .3), (.1, 1.3, -.3)]:
        m.sphere((240, 228, 200), (.14, .05, .14), seg=5, rings=3, at=(x, y, z))


def chili(m):
    s = m.mark()
    m.tube((208, 44, 40), 2.4, lambda t: (1 - t * .75) * math.sin(PI / 2 * min(1, t * 5 + .15)) ** .6, .3, .3, seg=6, n=8, bend=2.6)
    m.rod(GREEN, (-1.15, .0, 0), (-1.3, .25, 0), .06)
    m.sphere(GREEN, (.14, .1, .14), seg=5, rings=3, at=(-1.15, .02, 0))
    m.xform(s, rot=(0, 0, 8))


def eggplant(m):
    prof = [(0, -1), (.4, -.95), (.6, -.6), (.58, 0), (.4, .55), (.28, .8), (0, .85)]
    s = m.mark()
    m.lathe((100, 52, 120), prof, seg=8)
    m.prism((84, 140, 64), star(5, .38, .18), .08, at=(0, .8, 0))
    m.rod(GREEN, (0, .85, 0), (.06, 1.15, 0), .07)
    m.xform(s, rot=(0, 0, 30))


def pumpkin(m):
    m.sphere((238, 140, 40), (.55, .75, .55), seg=7, rings=4, at=(0, .8, 0))
    for a in range(0, 360, 72):
        r = math.radians(a)
        m.sphere((238, 140, 40), (.5, .75, .5), seg=7, rings=4, at=(.45 * math.cos(r), .8, .45 * math.sin(r)))
    m.rod(BROWN, (0, 1.45, 0), (.1, 1.85, 0), .1)


def scallion(m):
    for a in (-18, 0, 18):
        s = m.mark()
        m.cyl((240, 244, 224), .09, .7, seg=5)
        m.lathe((90, 156, 64), [(.1, .7), (.14, 1.4), (.06, 2.0), (0, 2.1)], seg=5)
        m.xform(s, rot=(0, a * 2, a), at=(a * .012, -1.1, 0))


ITEMS = [
    ("Bread", "식빵", bread), ("Croissant", "크루아상", croissant), ("Baguette", "바게트", baguette),
    ("RiceBall", "삼각김밥", rice_ball), ("Kimbap", "김밥", kimbap), ("Tteokbokki", "떡볶이", tteokbokki),
    ("Hamburger", "햄버거", hamburger), ("Milk", "우유", milk), ("Tofu", "두부", tofu), ("Egg", "달걀", egg),
    ("Flour", "밀가루", flour), ("Coffee", "커피", coffee),
    ("Apple", "사과", fruit((214, 60, 52))),
    ("GreenApple", "청사과", fruit((150, 192, 74))),
    ("Orange", "오렌지", fruit((242, 148, 38), .95, .05, False, False, cap=(80, 140, 60))),
    ("Tangerine", "귤", fruit((248, 168, 52), .72, .06, False, False, cap=(80, 140, 60))),
    ("Lemon", "레몬", lemon),
    ("Peach", "복숭아", fruit((250, 168, 128), .92, .12, True, True)),
    ("Tomato", "토마토", fruit((224, 66, 50), .78, .12, True, False, star_cap=True)),
    ("Strawberry", "딸기", strawberry), ("Banana", "바나나", banana), ("Grapes", "포도", grapes),
    ("Watermelon", "수박", watermelon), ("Pear", "배", pear), ("Cherry", "체리", cherry),
    ("Carrot", "당근", carrot), ("Potato", "감자", potato), ("SweetPotato", "고구마", sweet_potato),
    ("Onion", "양파", onion), ("Garlic", "마늘", garlic), ("Cabbage", "양배추", cabbage),
    ("Broccoli", "브로콜리", broccoli), ("Cucumber", "오이", cucumber), ("Corn", "옥수수", corn),
    ("Mushroom", "버섯", mushroom), ("Chili", "고추", chili), ("Eggplant", "가지", eggplant),
    ("Pumpkin", "호박", pumpkin), ("Radish", "무", radish), ("Scallion", "대파", scallion),
]
