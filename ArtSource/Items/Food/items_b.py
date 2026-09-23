"""3D 로우폴리 음식 모델 2: 육류·유제품, 식료품, 빵·디저트, 식사·간식, 사탕, 음료."""
import math
import random

from items_a import BROWN, GREEN, WHITE, bowl, carton, sack
from mesh3d import PI, ngon, rnd, rrect, scl, sector, star, wob

CREAM = (250, 240, 210)


def scatter(seed, n, r):
    rng = random.Random(seed)
    return [(rng.uniform(-r, r), rng.uniform(-r, r)) for _ in range(n)]


# ---------- 육류·해산물·유제품 ----------

def sausage(m):
    for z, r in ((-.45, 0), (.45, 8)):
        s = m.mark()
        m.tube((196, 84, 60), 2.1, rnd(.3), .3, .3, seg=8, n=8)
        for x in (-.5, -.1, .3):
            m.box((110, 44, 32), (.09, .04, .38), at=(x, .3, 0), rot=(0, 25, 0))
        m.xform(s, rot=(0, r, 0), at=(0, .3, z))


def ham(m):
    m.prism((252, 232, 220), rrect(2.1, 1.5, .35), .3)
    m.prism((238, 140, 140), rrect(1.9, 1.3, .3), .34)
    m.prism((250, 190, 190), rrect(.8, .4, .15), .36, at=(-.3, 0, .1))
    m.prism((250, 190, 190), rrect(.6, .3, .12), .36, at=(.4, 0, -.2))


def bacon(m):
    for k, (dz, rot) in enumerate([(-.5, 4), (.45, -5)]):
        s = m.mark()
        for j, (col, zz, w) in enumerate([((200, 80, 70), -.15, .2), ((250, 224, 210), 0, .16), ((200, 80, 70), .15, .2)]):
            n = 14
            top = [(-.95 + 1.9 * i / n, math.sin(i * 1.2) * .08 + zz + w / 2) for i in range(n + 1)]
            bot = [(x, z - w) for x, z in top]
            m.prism(col, top + bot[::-1], .1 + j % 2 * .03)
        m.xform(s, rot=(0, rot, 0), at=(0, 0, dz))


def chicken_leg(m):
    s = m.mark()
    m.lathe((208, 124, 52), [(0, -.8), (.3, -.7), (.6, -.3), (.78, .2), (.66, .7), (.32, .95), (0, 1.0)], seg=8)
    m.rod((245, 240, 225), (0, -.8, 0), (0, -1.55, 0), .1)
    for x in (-.13, .13):
        m.sphere((245, 240, 225), .17, seg=6, rings=3, at=(x, -1.6, 0))
    m.xform(s, rot=(0, 0, 42), at=(-.1, .1, 0))


def fried_chicken(m):
    for x, z, sx, r in [(-.55, .2, .7, 10), (.55, .25, .68, -20), (0, -.5, .72, 5)]:
        m.sphere((218, 150, 60), (sx, .5, .55), seg=7, rings=4, at=(x, .4, z), rot=(0, r, 0))
        for dx, dz in ((.2, .1), (-.2, -.1), (0, .25)):
            m.sphere((176, 108, 40), .09, seg=4, rings=2, at=(x + dx, .82, z + dz))


def pork_cutlet(m):
    for i in range(4):
        x = -.72 + i * .48
        m.box((214, 148, 58), (.42, .36, 1.5), at=(x, .18, 0), rot=(0, 0, 0))
        m.box((250, 236, 214), (.32, .26, .02), at=(x, .18, .75))
    for x, z, r in [(.9, -.7, 20), (1.05, -.3, -20), (.85, -.4, 60)]:
        m.sphere((130, 190, 90), (.3, .07, .2), seg=6, rings=3, at=(x, .1, z), rot=(0, r, 0))


def steak(m):
    m.prism((250, 226, 200), wob(12, 1.02, .08, 2, 1.15, .85), .28)
    m.prism((156, 72, 52), wob(12, .92, .08, 2, 1.15, .85), .34)
    for dz in (-.3, 0, .3):
        m.box((100, 40, 32), (1.3, .03, .07), at=(0, .34, dz), rot=(0, 35, 0))
    m.rod((90, 140, 60), (.5, .34, .1), (1.0, .5, .5), .03)
    m.sphere((90, 140, 60), (.16, .04, .08), seg=5, rings=3, at=(.9, .5, .4))


def fish(m):
    m.tube((120, 170, 200), 2.1, rnd(.6), .52, .3, seg=8, n=8)
    m.prism((92, 140, 176), [(0, 0), (.6, .55), (.6, -.55)], .07, "z", at=(.85, 0, 0))
    m.prism((92, 140, 176), [(-.3, 0), (.3, 0), (0, .5)], .06, "z", at=(-.05, .48, 0))
    m.tube((200, 224, 240), 1.3, rnd(.6), .18, .26, seg=6, n=5, at=(-.1, -.18, 0))
    for z in (-.27, .27):
        m.sphere((30, 30, 40), .07, seg=5, rings=3, at=(-.7, .12, z))


def shrimp(m):
    s = m.mark()
    m.tube((240, 140, 96), 2.6, lambda t: (1 - .55 * t) * math.sin(PI / 2 * min(1, t * 4 + .1)) ** .4, .34, .34, seg=8, n=10, bend=.9)
    m.xform(s, at=(0, -.5, 0))
    for dz, r in ((-.2, -25), (0, 0), (.2, 25)):
        m.sphere((224, 96, 64), (.28, .07, .1), seg=5, rings=3, at=(.95, .3, dz), rot=(0, r, -60))
    m.sphere((30, 30, 40), .07, seg=4, rings=2, at=(-.85, .34, .2))
    m.rod((224, 96, 64), (-.9, .3, 0), (-1.6, .75, .3), .025, 3)
    m.rod((224, 96, 64), (-.9, .3, 0), (-1.6, .55, -.3), .025, 3)


def fried_egg(m):
    m.prism((252, 252, 246), wob(16, 1.0, .16, 3), .1)
    m.sphere((250, 190, 40), (.44, .32, .44), seg=8, rings=4, at=(-.1, .1, 0))


def cheese(m):
    tri = [(-1, 0), (1, -.75), (1, .75)]
    m.prism((246, 200, 60), tri, .6)
    for x, z in [(.3, 0), (-.3, 0), (.6, .3), (.65, -.35)]:
        m.cyl((222, 164, 36), .12, .02, seg=6, at=(x, .6, z))
    m.sphere((222, 164, 36), (.03, .13, .13), seg=6, rings=3, at=(1.0, .3, .0))
    m.sphere((222, 164, 36), (.03, .1, .1), seg=6, rings=3, at=(.85, .42, -.35))


def butter(m):
    m.box((250, 232, 140), (1.4, .5, .8), at=(.15, .25, 0))
    m.box((214, 220, 230), (.7, .53, .83), at=(-.55, .265, 0))
    m.box((180, 186, 198), (.7, .04, .3), at=(-.55, .53, 0))


# ---------- 식료품 ----------

def sugar(m):
    def cube(mm):
        mm.box((220, 232, 246), (.22, .22, .22), at=(0, .66, .88), rot=(0, 30, 20))
    sack(m, (240, 244, 250), (230, 120, 150), cube)


def rice(m):
    def grains(mm):
        for dx, dy in ((-.14, .05), (.12, .15), (0, -.12)):
            mm.sphere((240, 232, 200), (.13, .05, .05), seg=5, rings=3, at=(dx, .66 + dy, .88), rot=(0, 0, 30))
    sack(m, (214, 182, 130), (96, 150, 90), grains)


def salt(m):
    def crystal(mm):
        mm.sphere((120, 160, 220), (.14, .2, .05), seg=4, rings=2, at=(0, .66, .88))
    sack(m, (232, 238, 248), (70, 110, 180), crystal)


def bottle(m, body, cap, label, tall=1.0, wide=.56):
    m.lathe(body, [(0, 0), (wide - .06, 0), (wide, .1), (wide, 1.05 * tall), (.42, 1.3 * tall), (.24, 1.5 * tall), (.24, 1.75 * tall)], seg=9)
    m.cyl(cap, .27, .26, seg=9, at=(0, 1.72 * tall, 0))
    m.lathe(label, [(wide + .02, .3), (wide + .02, .95 * tall)], seg=9, caps=False)


def cooking_oil(m):
    bottle(m, (240, 200, 50), (226, 150, 30), (250, 240, 210))
    m.sphere((226, 150, 30), (.22, .28, .05), seg=6, rings=4, at=(0, .62, .6))


def soy_sauce(m):
    bottle(m, (66, 38, 28), (200, 50, 40), (250, 244, 226))
    m.box((200, 50, 40), (.3, .3, .03), at=(0, .62, .59), rot=(0, 0, 45))


def ketchup(m):
    m.lathe((208, 44, 36), [(0, 0), (.55, 0), (.6, .1), (.6, .9), (.42, 1.25), (.3, 1.4)], seg=9)
    m.cone((250, 250, 244), .3, .5, seg=9, at=(0, 1.4, 0))
    m.lathe((250, 236, 200), [(.62, .3), (.62, .85)], seg=9, caps=False)
    m.sphere((208, 44, 36), (.24, .26, .04), seg=6, rings=4, at=(0, .58, .63))


def gochujang(m):
    m.lathe((200, 60, 44), [(.8, 0), (.9, .8)], seg=12)
    m.cyl((150, 30, 24), .92, .18, seg=12, at=(0, .8, 0))
    m.lathe((250, 236, 200), [(.86, .22), (.89, .6)], seg=12, caps=False)
    m.sphere((200, 60, 44), (.3, .14, .04), seg=6, rings=3, at=(0, .42, .88))


def jar(m, body, lid, label, lid_h=.22):
    m.lathe(body, [(0, 0), (.72, 0), (.78, .1), (.78, 1.0), (.62, 1.18), (.62, 1.28)], seg=10)
    m.cyl(lid, .68, lid_h, seg=10, at=(0, 1.26, 0))
    m.lathe(label, [(.8, .35), (.8, .9)], seg=10, caps=False)


def honey(m):
    jar(m, (232, 160, 30), (150, 100, 60), (250, 240, 210))
    m.rod((196, 140, 70), (.2, 1.5, 0), (.5, 2.0, 0), .05)
    m.sphere((196, 140, 70), (.16, .16, .16), seg=6, rings=3, at=(.55, 2.05, 0))


def jam(m):
    jar(m, (176, 40, 70), (236, 236, 236), (250, 240, 220), .16)
    m.torus((216, 60, 60), .68, .05, seg=10, ring=4, at=(0, 1.36, 0))


def kimchi_jar(m):
    jar(m, (214, 70, 40), (244, 244, 240), (250, 240, 220))
    for x, z in ((-.3, .2), (.2, -.3), (.35, .3)):
        m.sphere((240, 220, 200), .08, seg=4, rings=2, at=(x, 1.02, z))


# ---------- 빵·디저트 ----------

def torus_pastry(m, dough, top, seed=0, sprinkles=True, sesame=False):
    m.torus(dough, .7, .38, seg=12, ring=6, at=(0, .38, 0))
    prof = [(.7 + .41 * math.cos(math.radians(a)), .38 + .38 * math.sin(math.radians(a)) + .03) for a in range(-10, 201, 30)]
    m.lathe(top, prof, seg=12, caps=False)
    rng = random.Random(seed)
    for _ in range(14):
        a = rng.uniform(0, 2 * PI)
        d = rng.uniform(-.22, .22)
        rho = .7 + d
        y = .38 + math.sqrt(max(.0, .4 ** 2 - d ** 2)) * .98 + .01
        if sprinkles:
            m.box(rng.choice([(240, 240, 250), (250, 220, 70), (90, 190, 230), (120, 200, 110)]), (.16, .04, .04), at=(rho * math.cos(a), y, rho * math.sin(a)), rot=(0, rng.uniform(0, 180), 0))
        if sesame:
            m.sphere((250, 240, 214), (.05, .03, .03), seg=4, rings=2, at=(rho * math.cos(a), y, rho * math.sin(a)))


def donut(m):
    torus_pastry(m, (222, 170, 100), (244, 140, 170), 1)


def bagel(m):
    torus_pastry(m, (214, 164, 96), (228, 184, 112), 4, sprinkles=False, sesame=True)


def cake_slice(m):
    tri = [(-1, 0), (.95, -.7), (.95, .7)]
    y = 0
    for col, h in [((238, 204, 140), .3), ((176, 44, 60), .08), ((238, 204, 140), .3), ((252, 240, 224), .1)]:
        m.prism(col, tri, h, at=(0, y, 0))
        y += h
    m.prism((252, 232, 236), scl(tri, 1.02), .1, at=(0, y, 0))
    y += .1
    m.sphere((252, 250, 240), (.24, .14, .24), seg=7, rings=3, at=(.55, y + .04, 0))
    m.sphere((214, 40, 60), (.26, .3, .26), seg=7, rings=4, at=(.55, y + .28, 0))


def cupcake(m, frosting=(250, 190, 205)):
    m.lathe((120, 170, 220), [(0, 0), (.46, 0), (.7, .62)], seg=10)
    for r, y, h in [(.72, .66, .24), (.52, .9, .2), (.32, 1.08, .18)]:
        m.sphere(frosting, (r, h, r), seg=8, rings=3, at=(0, y, 0))
    m.sphere((210, 40, 60), .15, seg=6, rings=3, at=(0, 1.32, 0))


def muffin(m):
    m.lathe((240, 220, 190), [(0, 0), (.46, 0), (.7, .62)], seg=10)
    m.lathe((204, 140, 66), [(.68, .5), (.86, .66), (.88, .92), (.62, 1.22), (0, 1.32)], seg=10)
    for x, y, z in [(-.3, 1.15, .2), (.3, 1.1, .3), (0, 1.3, -.2), (.4, .9, -.4), (-.5, .9, -.2)]:
        m.sphere((90, 54, 40), .09, seg=4, rings=2, at=(x, y, z))


def cookie(m):
    m.lathe((222, 172, 96), [(0, 0), (.92, 0), (1.02, .1), (.96, .22), (0, .26)], seg=10)
    for x, z in [(-.4, .2), (.3, .4), (.1, -.35), (-.35, -.4), (.55, -.15), (-.1, .05)]:
        m.sphere((100, 60, 40), (.14, .07, .12), seg=5, rings=3, at=(x, .25, z), rot=(0, x * 90, 0))


def pancake(m):
    for i, y in enumerate((0, .22, .44)):
        m.cyl((232, 180, 100), .98, .2, seg=12, at=(0, y, 0), rot=(0, i * 15, 0))
        m.cyl((242, 200, 124), .93, .02, seg=12, at=(0, y + .2, 0))
    m.box((250, 232, 140), (.36, .2, .36), at=(0, .74, 0), rot=(0, 20, 0))
    m.sphere((170, 92, 30), (.72, .09, .72), seg=9, rings=3, at=(0, .66, 0))
    for a in (20, 140, 250):
        r = math.radians(a)
        m.rod((170, 92, 30), (.72 * math.cos(r), .62, .72 * math.sin(r)), (.76 * math.cos(r), .3, .76 * math.sin(r)), .06)


def waffle(m):
    m.box((226, 166, 80), (1.7, .3, 1.7), at=(0, .15, 0))
    for d in (-.55, 0, .55):
        m.box((176, 116, 50), (.07, .05, 1.65), at=(d, .3, 0))
        m.box((176, 116, 50), (1.65, .05, .07), at=(0, .3, d))
    m.box((250, 232, 140), (.4, .2, .4), at=(0, .4, 0), rot=(0, 15, 0))


def pie(m):
    m.cyl((222, 166, 84), 1.1, .55, seg=12)
    m.cyl((170, 50, 60), .9, .1, seg=12, at=(0, .5, 0))
    for phi in (45, -45):
        s = m.mark()
        for d in (-.6, -.3, 0, .3, .6):
            ln = 2 * math.sqrt(.9 ** 2 - d ** 2)
            m.box((214, 146, 60), (.13, .06, ln), at=(d, .62, 0))
        m.xform(s, rot=(0, phi, 0))
    m.torus((222, 166, 84), 1.0, .1, seg=12, ring=4, at=(0, .58, 0))


def sandwich(m):
    tri = [(-.85, -.85), (.85, -.85), (-.85, .85)]
    y = 0
    for col, h, s in [((238, 208, 150), .22, 1.0), ((238, 150, 150), .08, 1.06), ((220, 70, 56), .07, 1.02),
                      ((246, 200, 60), .05, 1.0), ((120, 180, 80), .07, 1.1), ((196, 140, 70), .22, 1.02)]:
        m.prism(col, scl(tri, s), h, at=(0, y, 0))
        y += h
    m.prism((250, 226, 176), scl(tri, .94), .02, at=(0, y - .01, 0))
    m.rod((232, 196, 140), (-.2, y, -.2), (-.25, y + .8, -.25), .03)
    m.prism((214, 50, 50), [(0, 0), (.35, .12), (0, .24)], .02, "z", at=(-.25, y + .6, -.25))


def toast(m):
    from items_a import bread
    bread(m, (150, 90, 44), (226, 168, 86))
    m.box((250, 232, 140), (.55, .14, .3), at=(0, .35, .7), rot=(0, 0, 0))


def bun(m, col, crumbs=False):
    m.lathe(col, [(0, 0), (.95, 0), (1.02, .15), (.9, .5), (.55, .72), (0, .78)], seg=10)
    rng = random.Random(7)
    if crumbs:
        for _ in range(26):
            a, r = rng.uniform(0, 2 * PI), rng.uniform(0, .9)
            y = .78 * math.sqrt(max(0, 1 - (r / 1.0) ** 2)) + .02
            m.sphere(rng.choice([(236, 198, 140), (196, 146, 84)]), .08, seg=4, rings=2, at=(r * math.cos(a), y, r * math.sin(a)))
    else:
        for x, z in [(-.15, 0), (.15, .1), (0, -.15), (.05, .25), (-.2, .2)]:
            m.sphere((50, 34, 30), (.06, .03, .04), seg=4, rings=2, at=(x, .76, z))


def red_bean_bun(m):
    bun(m, (204, 136, 66))


def soboro_bun(m):
    bun(m, (214, 162, 96), True)


# ---------- 식사·간식 ----------

def ramen(m):
    y = bowl(m, (200, 60, 46), (245, 236, 220), (230, 170, 80), h=.7)
    m.cyl((238, 190, 90), .86, .05, seg=12, at=(0, y - .08, 0))
    m.sphere((246, 214, 110), (.7, .22, .7), seg=8, rings=3, at=(0, y - .04, 0))
    rng = random.Random(3)
    for _ in range(8):
        a = rng.uniform(0, PI)
        m.rod((240, 204, 96), (-.6 * math.cos(a), y + .16, -.6 * math.sin(a)), (.6 * math.cos(a), y + .17, .6 * math.sin(a)), .03, 4)
    m.sphere((252, 250, 242), (.3, .22, .25), seg=7, rings=3, at=(-.4, y + .12, .3))
    m.sphere((250, 190, 40), .12, seg=6, rings=3, at=(-.4, y + .22, .3))
    m.cyl((250, 250, 244), .24, .07, seg=8, at=(.4, y + .1, .35))
    m.cyl((240, 130, 160), .09, .08, seg=6, at=(.4, y + .1, .35))
    m.cyl((232, 160, 140), .33, .05, seg=8, at=(-.05, y + .12, -.3))
    m.box((30, 60, 44), (.6, .7, .03), at=(.55, y + .3, -.4), rot=(0, 25, -10))
    for x, z in ((-.1, .05), (.2, -.1), (.05, .5)):
        m.cyl(GREEN, .05, .08, seg=5, at=(x, y + .17, z))


def bibimbap(m):
    y = bowl(m, (70, 58, 52), (110, 92, 78), (250, 246, 236), h=.7)
    m.cyl((250, 246, 236), .84, .04, seg=12, at=(0, y - .06, 0))
    cols = [(96, 160, 70), (236, 130, 50), (240, 224, 170), (140, 80, 56), (150, 120, 110)]
    for k, col in enumerate(cols):
        m.prism(col, sector(.76, k * 72 + 5, k * 72 + 67, 3), .12, at=(0, y - .04, 0))
    m.prism((252, 252, 246), wob(10, .36, .12, 1), .07, at=(0, y + .08, 0))
    m.sphere((250, 190, 40), (.17, .12, .17), seg=6, rings=3, at=(0, y + .14, 0))
    m.sphere((200, 50, 36), .07, seg=5, rings=3, at=(.5, y + .1, .1))


def jajangmyeon(m):
    y = bowl(m, (246, 244, 238), (236, 236, 240), (250, 246, 238), h=.7)
    m.cyl((238, 214, 160), .86, .05, seg=12, at=(0, y - .08, 0))
    m.sphere((44, 28, 24), (.74, .44, .74), seg=8, rings=4, at=(0, y - .02, 0))
    m.sphere((84, 58, 46), (.2, .06, .12), seg=5, rings=3, at=(-.25, y + .36, .15))
    for a, b in [((-.25, .3), (.25, .38)), ((-.3, .4), (.2, .44)), ((-.1, .3), (.3, .42))]:
        m.rod((100, 170, 80), (a[0], y + a[1], -.1), (b[0], y + b[1], .2), .035, 4)


def udon(m):
    y = bowl(m, (70, 110, 150), (240, 240, 244), (230, 200, 140), h=.7)
    m.cyl((226, 190, 120), .86, .05, seg=12, at=(0, y - .08, 0))
    rng = random.Random(5)
    for _ in range(7):
        a = rng.uniform(0, PI)
        o = rng.uniform(-.3, .3)
        m.rod((250, 246, 236), (-.55 * math.cos(a) + o, y + .0, -.55 * math.sin(a)), (.55 * math.cos(a) + o, y + .02, .55 * math.sin(a)), .07, 5)
    m.cyl((250, 250, 244), .24, .07, seg=8, at=(.3, y + .1, .35))
    m.cyl((240, 130, 160), .09, .08, seg=6, at=(.3, y + .1, .35))
    m.prism((214, 150, 58), [(-.25, -.2), (.25, -.2), (0, .28)], .12, at=(-.4, y + .04, .2))
    for x, z in ((0, -.3), (.2, .0), (-.1, .05)):
        m.cyl(GREEN, .05, .08, seg=5, at=(x, y + .15, z))


def kimchi_stew(m):
    y = bowl(m, (92, 60, 44), (120, 84, 60), (60, 34, 26), h=.75)
    m.cyl((208, 50, 30), .86, .05, seg=12, at=(0, y - .06, 0))
    for x, z, r in ((-.3, -.2, 10), (.3, .1, 30), (-.05, .4, 0)):
        m.box((250, 246, 232), (.28, .2, .28), at=(x, y + .06, z), rot=(0, r, 0))
    for x, z in scatter(2, 6, .6):
        m.sphere((240, 110, 70), .09, seg=4, rings=2, at=(x, y + .0, z))
    for x, z in ((.1, -.4), (-.5, .3)):
        m.cyl(GREEN, .05, .1, seg=5, at=(x, y + .0, z))
    for s in (-1, 1):
        m.torus((92, 60, 44), .16, .06, seg=8, ring=4, at=(s * 1.05, .5, 0), rot=(90, 0, 0))


def rice_bowl(m):
    y = bowl(m, (238, 240, 244), (70, 110, 170), (250, 250, 250), h=.7)
    m.sphere((252, 252, 248), (.9, .62, .9), seg=10, rings=4, at=(0, y - .05, 0))
    m.rod((196, 140, 84), (-.5, y + .3, -.9), (.5, y + .5, .8), .04)
    m.rod((196, 140, 84), (-.3, y + .3, -.95), (.7, y + .5, .75), .04)


def seaweed_soup(m):
    y = bowl(m, (242, 236, 222), (255, 255, 250), (250, 246, 238), h=.7)
    m.cyl((150, 110, 64), .86, .05, seg=12, at=(0, y - .06, 0))
    for x, z in scatter(9, 7, .55):
        m.sphere((34, 84, 56), (.28, .05, .16), seg=5, rings=3, at=(x, y + .0, z), rot=(0, x * 200, 0))
    for x, z in ((.3, -.3), (-.4, .2)):
        m.cyl(GREEN, .05, .08, seg=5, at=(x, y - .02, z))


def salad(m):
    y = bowl(m, (176, 124, 78), (196, 146, 96), (140, 96, 60), h=.75)
    rng = random.Random(4)
    for i in range(9):
        a = i * 40
        r = math.radians(a)
        d = .55 if i % 2 else .3
        m.sphere(rng.choice([(110, 180, 80), (140, 200, 96), (90, 160, 70)]), (.5, .08, .3), seg=6, rings=3,
                 at=(d * math.cos(r), y + .05 + (i % 3) * .06, d * math.sin(r)), rot=(0, -a, rng.uniform(-18, 18)))
    for x, z in ((-.1, .1), (.35, .2), (-.3, -.3)):
        m.sphere((210, 60, 50), .19, seg=7, rings=4, at=(x, y + .2, z))
    for x, z in ((.2, -.3), (-.4, .3)):
        m.cyl((190, 224, 150), .2, .06, seg=8, at=(x, y + .15, z), rot=(20, 0, 0))
    for x, z in scatter(6, 5, .5):
        m.sphere((248, 212, 72), .05, seg=4, rings=2, at=(x, y + .3, z))


def hotteok(m):
    m.lathe((206, 130, 56), [(0, 0), (.95, 0), (1.04, .12), (.98, .28), (.62, .36), (0, .38)], seg=12)
    for x, z in scatter(8, 9, .7):
        m.sphere((150, 86, 36), .07, seg=4, rings=2, at=(x, .35, z))
    m.sphere((170, 92, 30), (.42, .07, .42), seg=7, rings=3, at=(.1, .36, 0))


def bungeoppang(m):
    s = m.mark()
    m.tube((222, 160, 70), 2.0, rnd(.55), .62, .38, seg=8, n=8)
    m.prism((200, 138, 54), [(0, 0), (.75, .6), (.75, -.6)], .14, "z", at=(.8, 0, 0))
    for z in (-.36, .36):
        m.sphere((30, 26, 26), .07, seg=5, rings=3, at=(-.65, .18, z))
    for x in (-.15, .15, .45):
        m.box((176, 116, 50), (.04, .5, .02), at=(x, 0, .385), rot=(0, 0, 15))
    m.xform(s, rot=(0, 0, 10), at=(-.2, .0, 0))


def odeng(m):
    m.rod((200, 170, 120), (0, -1.3, 0), (0, 1.5, 0), .045)
    for y, r in ((-.7, 0), (.05, 40), (.8, -25)):
        m.sphere((240, 214, 170), (.62, .34, .62), seg=8, rings=4, at=(0, y, 0), rot=(0, r, 0))
        m.cyl((240, 140, 160), .2, .04, seg=6, at=(0, y + .33, 0))


def mandu(m):
    for x, z, s, r in ((-.5, .2, 1.0, 15), (.75, -.35, .85, -25)):
        st = m.mark()
        m.sphere((250, 246, 234), (.9 * s, .5 * s, .66 * s), seg=8, rings=4, at=(0, .3 * s, 0))
        for i in range(6):
            xx = -.6 * s + i * .24 * s
            m.sphere((236, 228, 210), (.13 * s, .12 * s, .09 * s), seg=5, rings=3, at=(xx, (.68 - .5 * (xx / (.9 * s)) ** 2) * s, 0))
        m.xform(st, rot=(0, r, 0), at=(x, 0, z))


def gyeranppang(m):
    m.prism((232, 184, 80), rrect(2.0, 1.2, .35), .7)
    m.prism((244, 204, 112), rrect(1.8, 1.0, .3), .76)
    m.prism((252, 252, 246), wob(10, .46, .12, 5), .1, at=(0, .74, 0))
    m.sphere((250, 190, 40), (.24, .2, .24), seg=7, rings=3, at=(0, .88, 0))


def hot_dog(m):
    m.tube((226, 176, 100), 2.5, rnd(.35), .4, .5, seg=8, n=8, at=(0, .4, -.3))
    m.tube((226, 176, 100), 2.5, rnd(.35), .36, .5, seg=8, n=8, at=(0, .32, .32))
    m.tube((208, 80, 60), 2.8, rnd(.3), .3, .3, seg=8, n=8, at=(0, .6, 0))
    pts = [(-.9 + i * .3, .9, .1 if i % 2 else -.1) for i in range(7)]
    for a, b in zip(pts, pts[1:]):
        m.rod((250, 210, 50), a, b, .04, 4)


def fries(m):
    rng = random.Random(6)
    for x in (-.45, -.15, .15, .45):
        for z in (-.35, 0, .35):
            h = rng.uniform(1.0, 1.6)
            m.box((246, 202, 70), (.17, h, .17), at=(x, .3 + h / 2, z), rot=(rng.uniform(-8, 8), 0, rng.uniform(-8, 8)))
    m.lathe((206, 52, 44), [(0, 0), (.62, 0), (.95, 1.0)], seg=4, rot=(0, 45, 0), sc=(1, 1, 1))
    m.box((250, 210, 50), (1.0, .3, .04), at=(0, .5, .68), rot=(0, 0, 0))


def pizza(m):
    m.prism((246, 196, 76), [(-.95, -.9), (.95, -.9), (0, 1.05)], .16)
    m.tube((214, 150, 70), 2.0, rnd(.3), .16, .2, seg=8, n=6, at=(0, .15, -.92))
    for x, z in ((-.35, -.4), (.35, -.35), (0, .2)):
        m.cyl((190, 50, 44), .21, .05, seg=8, at=(x, .16, z))
    for x, z in ((.1, -.6), (-.5, -.5), (-.15, .6), (.25, .1)):
        m.box((100, 170, 70), (.12, .05, .09), at=(x, .18, z), rot=(0, x * 300, 0))
    for x, z in ((.55, -.55), (-.1, -.05), (.15, .5)):
        m.cyl((36, 30, 34), .07, .05, seg=6, at=(x, .16, z))


# ---------- 사탕·디저트 ----------

def ice_cream(m):
    m.lathe((218, 170, 100), [(0, -1.3), (.52, .05)], seg=8)
    m.torus((250, 170, 190), .5, .1, seg=8, ring=4, at=(0, .06, 0))
    m.sphere((250, 170, 190), .66, seg=9, rings=5, at=(0, .55, 0))
    m.sphere((190, 230, 210), .52, seg=9, rings=5, at=(0, 1.2, 0))
    m.sphere((210, 40, 60), .14, seg=6, rings=3, at=(0, 1.78, 0))


def popsicle(m):
    m.box((232, 196, 140), (.22, .9, .08), at=(0, -.75, 0))
    m.prism((240, 110, 140), rrect(1.0, 1.6, .28), .32, "z", at=(0, .5, 0))
    m.prism((250, 236, 200), rrect(1.02, .7, .25), .34, "z", at=(0, .05, 0))


def chocolate(m):
    s = m.mark()
    for i in range(3):
        for j in range(4):
            m.box((100, 62, 44), (.5, .2, .42), at=(-.55 + i * .55, .1, -.7 + j * .47))
            m.box((124, 80, 58), (.4, .05, .32), at=(-.55 + i * .55, .21, -.7 + j * .47))
    m.box((200, 60, 50), (1.7, .24, .95), at=(0, .12, .95))
    m.box((216, 170, 60), (1.7, .26, .3), at=(0, .13, .5))
    m.xform(s, rot=(0, -20, 0))


def candy(m):
    m.sphere((240, 120, 150), (.7, .5, .5), seg=8, rings=4, at=(0, .5, 0))
    for a in (-.2, 0, .2):
        m.sphere((252, 232, 240), (.06, .5, .52), seg=6, rings=3, at=(a, .5, 0))
    m.lathe((250, 220, 230), [(.4, 0), (0, .6)], seg=6, at=(.62, .5, 0), rot=(0, 0, -90))
    m.lathe((250, 220, 230), [(.4, 0), (0, .6)], seg=6, at=(-.62, .5, 0), rot=(0, 0, 90))


def lollipop(m):
    for k in range(10):
        m.prism((240, 90, 120) if k % 2 else (250, 240, 230), sector(.9, k * 36, k * 36 + 36, 3), .2, "z", at=(0, .45, 0))
    m.rod((238, 232, 220), (0, -.45, 0), (0, -1.6, 0), .06)


def gummy_bear(m):
    c = (230, 60, 70)
    m.sphere(c, (.52, .62, .42), seg=8, rings=4, at=(0, 0, 0))
    m.sphere(c, .44, seg=8, rings=4, at=(0, .85, 0))
    for s in (-1, 1):
        m.sphere(c, .17, seg=6, rings=3, at=(s * .32, 1.22, 0))
        m.sphere(c, (.16, .3, .16), seg=6, rings=3, at=(s * .62, .1, 0), rot=(0, 0, s * -35))
        m.sphere(c, (.22, .28, .22), seg=6, rings=3, at=(s * .26, -.68, .08))
        m.sphere((30, 20, 24), .05, seg=4, rings=2, at=(s * .15, .92, .4))
    m.sphere((250, 130, 140), (.15, .1, .1), seg=6, rings=3, at=(0, .8, .4))
    m.sphere((250, 150, 160), (.16, .28, .05), seg=5, rings=3, at=(-.2, .35, .4))


# ---------- 음료 ----------

def water(m):
    bottle(m, (196, 228, 246), (60, 120, 200), (70, 140, 210))


def can(m, body, band, band2):
    m.cyl(body, .56, 1.5, seg=10)
    m.cyl((200, 205, 212), .5, .06, seg=10, at=(0, 1.5, 0))
    m.box((200, 205, 212), (.24, .03, .14), at=(.08, 1.6, 0))
    m.lathe(band, [(.575, .45), (.575, .95)], seg=10, caps=False)
    m.lathe(band2, [(.575, .95), (.575, 1.1)], seg=10, caps=False)


def cola(m):
    can(m, (206, 40, 40), (250, 250, 250), (60, 20, 20))


def lime_soda(m):
    can(m, (60, 160, 80), (250, 250, 250), (250, 224, 60))


def orange_juice(m):
    carton(m, (244, 150, 40), (224, 120, 30), (110, 170, 60), (250, 250, 240))


def choco_milk(m):
    carton(m, (120, 78, 56), (90, 58, 40), (250, 236, 210), (120, 78, 56))


def strawberry_milk(m):
    carton(m, (248, 180, 196), (226, 140, 164), (250, 244, 240), (222, 52, 66))


def banana_milk(m):
    m.lathe((250, 226, 110), [(0, 0), (.62, 0), (.66, .1), (.66, .9), (.5, 1.1), (.3, 1.2), (.28, 1.3)], seg=9)
    m.cyl((250, 250, 244), .3, .2, seg=9, at=(0, 1.3, 0))
    m.lathe((252, 250, 236), [(.68, .3), (.68, .8)], seg=9, caps=False)
    s = m.mark()
    m.tube((246, 200, 40), .7, rnd(.5), .07, .05, seg=5, n=6, bend=1.0)
    m.xform(s, at=(0, .45, .7))


def bubble_cup(m, liquid, boba):
    m.lathe(liquid, [(0, 0), (.55, 0), (.72, 1.35)], seg=10)
    m.lathe((245, 240, 235), [(.74, 1.32), (.76, 1.42), (.5, 1.5), (0, 1.52)], seg=10)
    m.lathe((250, 240, 230), [(.63, .72), (.66, 1.0)], seg=10, caps=False)
    m.rod((240, 110, 140), (.1, 1.4, 0), (.35, 2.2, 0), .07)
    if boba:
        for a in range(0, 360, 45):
            r = math.radians(a)
            for y, rr in ((.16, .56), (.4, .63)):
                m.sphere((50, 34, 28), .1, seg=5, rings=3, at=(rr * math.cos(r + y), y, rr * math.sin(r + y)))


def bubble_tea(m):
    bubble_cup(m, (206, 160, 110), True)


def smoothie(m):
    bubble_cup(m, (150, 204, 110), False)


def tea_cup(m):
    m.cyl((250, 250, 246), 1.1, .08, seg=12)
    m.lathe((250, 250, 246), [(0, .08), (.45, .08), (.72, .3), (.76, .85)], seg=10, caps=False)
    m.cyl((150, 90, 50), .72, .02, seg=10, at=(0, .76, 0))
    m.torus((250, 250, 246), .22, .06, seg=8, ring=4, at=(.9, .5, 0), rot=(90, 0, 0))


ITEMS = [
    ("Sausage", "소시지", sausage), ("Ham", "햄", ham), ("Bacon", "베이컨", bacon), ("ChickenLeg", "닭다리", chicken_leg),
    ("FriedChicken", "치킨", fried_chicken), ("PorkCutlet", "돈가스", pork_cutlet), ("Steak", "스테이크", steak),
    ("Fish", "생선", fish), ("Shrimp", "새우", shrimp), ("FriedEgg", "달걀프라이", fried_egg),
    ("Cheese", "치즈", cheese), ("Butter", "버터", butter),
    ("Sugar", "설탕", sugar), ("Rice", "쌀", rice), ("Salt", "소금", salt), ("CookingOil", "식용유", cooking_oil),
    ("SoySauce", "간장", soy_sauce), ("Ketchup", "케첩", ketchup), ("Gochujang", "고추장", gochujang),
    ("Honey", "꿀", honey), ("Jam", "잼", jam), ("KimchiJar", "김치", kimchi_jar),
    ("Donut", "도넛", donut), ("Bagel", "베이글", bagel), ("CakeSlice", "케이크", cake_slice),
    ("Cupcake", "컵케이크", cupcake), ("Muffin", "머핀", muffin), ("Cookie", "쿠키", cookie),
    ("Pancake", "팬케이크", pancake), ("Waffle", "와플", waffle), ("Pie", "파이", pie),
    ("Sandwich", "샌드위치", sandwich), ("Toast", "토스트", toast), ("RedBeanBun", "단팥빵", red_bean_bun),
    ("SoboroBun", "소보로빵", soboro_bun),
    ("Ramen", "라면", ramen), ("Bibimbap", "비빔밥", bibimbap), ("Jajangmyeon", "짜장면", jajangmyeon),
    ("Udon", "우동", udon), ("KimchiStew", "김치찌개", kimchi_stew), ("RiceBowl", "밥", rice_bowl),
    ("SeaweedSoup", "미역국", seaweed_soup), ("Salad", "샐러드", salad), ("Hotteok", "호떡", hotteok),
    ("Bungeoppang", "붕어빵", bungeoppang), ("Odeng", "어묵꼬치", odeng), ("Mandu", "만두", mandu),
    ("Gyeranppang", "계란빵", gyeranppang), ("HotDog", "핫도그", hot_dog), ("Fries", "감자튀김", fries),
    ("Pizza", "피자", pizza),
    ("IceCream", "아이스크림", ice_cream), ("Popsicle", "얼음과자", popsicle), ("Chocolate", "초콜릿", chocolate),
    ("Candy", "사탕", candy), ("Lollipop", "막대사탕", lollipop), ("GummyBear", "곰돌이젤리", gummy_bear),
    ("Water", "생수", water), ("Cola", "콜라", cola), ("LimeSoda", "라임소다", lime_soda),
    ("OrangeJuice", "오렌지주스", orange_juice), ("ChocoMilk", "초코우유", choco_milk),
    ("StrawberryMilk", "딸기우유", strawberry_milk), ("BananaMilk", "바나나우유", banana_milk),
    ("BubbleTea", "버블티", bubble_tea), ("Smoothie", "스무디", smoothie), ("TeaCup", "차", tea_cup),
]
