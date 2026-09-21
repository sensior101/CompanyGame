"""두부컴퍼니 음식 아이템 스프라이트 생성기 (로우폴리 스타일).

실행: python generate_food.py
- PNG(512x512, 투명 배경)를 CompanyGame/Assets/Art/Items/Food/ 에 쓴다.
- 처음 만드는 PNG/폴더에는 Unity .meta(Sprite 설정)를 함께 쓴다. 이미 있는 .meta 는 건드리지 않아 GUID가 유지된다.
- 미리보기 시트는 이 폴더의 Food_Sheet.png 로 쓴다.
"""
import math
import uuid
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
OUT = REPO / "CompanyGame" / "Assets" / "Art" / "Items" / "Food"
META_TEMPLATE = REPO / "CompanyGame" / "Assets" / "Art" / "Items" / "Currency" / "Currency_100.png.meta"

SIZE = 512
SS = 4  # 슈퍼샘플링 배율. 그린 뒤 축소해서 안티앨리어싱
LIGHT = (-0.6, -0.8)  # 좌상단 광원


def mix(c, t, k):
    return tuple(round(c[i] + (t[i] - c[i]) * k) for i in range(3))


def shade(c, k):
    return mix(c, (255, 255, 255), k) if k > 0 else mix(c, (0, 0, 0), -k)


def ell(cx, cy, rx, ry, n=12, rot=0.0):
    r = math.radians(rot)
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        x, y = rx * math.cos(t), ry * math.sin(t)
        pts.append((cx + x * math.cos(r) - y * math.sin(r), cy + x * math.sin(r) + y * math.cos(r)))
    return pts


def arc(cx, cy, rx, ry, a0, a1, n=8):
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


class Canvas:
    def __init__(self):
        self.img = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.k = SIZE * SS / 100

    def _s(self, pts):
        return [(x * self.k, y * self.k) for x, y in pts]

    def flat(self, pts, color):
        self.d.polygon(self._s(pts), fill=color + (255,))

    def low(self, pts, base, spread=0.16):
        """중심에서 뻗는 삼각 패치마다 빛 방향에 따라 명암을 준다."""
        self.flat(pts, base)
        n = len(pts)
        cx = sum(p[0] for p in pts) / n
        cy = sum(p[1] for p in pts) / n
        for i in range(n):
            a, b = pts[i], pts[(i + 1) % n]
            dx, dy = (a[0] + b[0]) / 2 - cx, (a[1] + b[1]) / 2 - cy
            m = math.hypot(dx, dy) or 1
            k = (dx / m * LIGHT[0] + dy / m * LIGHT[1]) * spread + ((i * 37) % 7 - 3) * 0.012
            if abs(k) >= 0.012:
                self.flat([(cx, cy), a, b], shade(base, k))

    def out(self):
        return self.img.resize((SIZE, SIZE), Image.LANCZOS)


def bread(c):
    crust = [(22, 84), (22, 54), (15, 46), (17, 33), (29, 24), (44, 21), (58, 21), (71, 24), (83, 33), (85, 46), (78, 54), (78, 84)]
    c.low(crust, (196, 132, 62), 0.2)
    crumb = [(28, 80), (28, 56), (22, 47), (24, 38), (33, 31), (46, 28), (54, 28), (67, 31), (76, 38), (78, 47), (72, 56), (72, 80)]
    c.low(crumb, (248, 226, 176), 0.08)
    for x, y in [(40, 50), (58, 44), (52, 64), (38, 68), (63, 62)]:
        c.flat(ell(x, y, 1.6, 1.2, 6), (226, 196, 140))


def croissant(c):
    cx, cy, R = 50, 70, 40
    for ang, w in [(-180, 12), (0, 12), (-150, 17), (-30, 17), (-120, 24), (-60, 24), (-90, 30)]:
        def p(a, r):
            return (cx + r * math.cos(math.radians(a)), cy + r * 0.92 * math.sin(math.radians(a)))
        seg = [p(ang - 17, R - w), p(ang, R - w - 1), p(ang + 17, R - w), p(ang + 17, R), p(ang, R + 2), p(ang - 17, R)]
        c.low(seg, (224, 158, 68), 0.26)
        c.flat([p(ang - 8, R - w * .35), p(ang + 3, R - w * .35), p(ang + 3, R - w * .05), p(ang - 8, R - w * .05)], (246, 204, 126))


def baguette(c):
    rot = -35
    c.low(ell(50, 52, 44, 11, 16, rot), (212, 152, 82), 0.2)
    a = math.radians(rot)
    for t in (-26, -9, 8, 25):
        x, y = 50 + t * math.cos(a), 52 + t * math.sin(a)
        c.flat(ell(x, y, 6.5, 2.4, 8, rot + 62), (244, 210, 148))


def rice_ball(c):
    body = [(46, 18), (54, 18), (60, 24), (84, 66), (86, 74), (80, 80), (20, 80), (14, 74), (16, 66), (40, 24)]
    c.low(body, (247, 246, 238), 0.1)
    nori = [(21.7, 56), (78.3, 56), (84, 66), (86, 74), (80, 80), (20, 80), (14, 74), (16, 66)]
    c.low(nori, (44, 74, 58), 0.14)
    c.flat([(38, 61), (62, 61), (62, 73), (38, 73)], (250, 240, 214))
    c.flat(ell(50, 67, 3.6, 3.6, 8), (226, 96, 88))
    for x, y in [(46, 34), (54, 42), (42, 46)]:
        c.flat(ell(x, y, 2.2, 1.4, 6, 30), (230, 228, 216))


def kimbap(c):
    fills = [((-4.5, -3.5), (240, 200, 60)), ((4.5, -3.5), (232, 122, 52)), ((-4.5, 4), (96, 152, 66)),
             ((4.5, 4), (226, 122, 132)), ((0, 0), (150, 92, 62))]
    for x, y in [(32, 38), (68, 38), (50, 66)]:
        c.low(ell(x, y, 19, 17, 12, 15), (38, 64, 52), 0.16)
        c.low(ell(x, y, 15, 13, 12, 15), (248, 245, 235), 0.06)
        for (dx, dy), col in fills:
            c.flat(ell(x + dx * 1.3, y + dy * 1.3, 3.6, 3.2, 6), col)


def tteokbokki(c):
    c.low([(14, 50), (86, 50), (82, 66), (73, 82), (27, 82), (18, 66)], (240, 234, 222), 0.14)
    c.low(ell(50, 50, 36, 9, 16), (250, 246, 238), 0.08)
    c.low(ell(50, 50, 32, 6.6, 16), (196, 56, 36), 0.1)
    for x, y, r in [(33, 47, -20), (49, 43, 8), (65, 47, 25), (43, 51, 62), (58, 52, -40)]:
        c.low(ell(x, y, 10, 4.8, 8, r), (232, 96, 52), 0.22)
    for x, y in [(36, 52), (54, 47), (70, 51), (46, 41)]:
        c.flat(ell(x, y, 1.8, 1.4, 6), (104, 170, 70))


def hamburger(c):
    c.low([(20, 66), (80, 66), (80, 74), (72, 81), (28, 81), (20, 74)], (212, 148, 72), 0.16)
    c.low([(17, 55), (83, 55), (84, 60), (80, 66), (20, 66), (16, 60)], (112, 66, 44), 0.14)
    c.flat([(20, 50), (80, 50), (80, 56), (64, 56), (60, 62), (56, 56), (20, 56)], (246, 196, 52))
    c.flat([(15, 46), (85, 46), (85, 51), (78, 55), (70, 50), (62, 55), (54, 50), (46, 55), (38, 50), (30, 55), (22, 50), (15, 52)], (108, 176, 72))
    c.low(arc(50, 46, 34, 27, 180, 360, 10), (222, 156, 78), 0.2)
    for x, y in [(36, 32), (48, 26), (62, 32), (42, 38), (56, 39), (70, 40), (28, 40)]:
        c.flat(ell(x, y, 2.6, 1.5, 6, 20), (250, 232, 190))


def milk(c):
    c.flat([(28, 44), (62, 44), (62, 88), (28, 88)], (244, 247, 252))
    c.low([(62, 44), (76, 38), (76, 82), (62, 88)], (198, 210, 228), 0.05)
    c.low([(28, 44), (45, 28), (62, 44)], (214, 228, 246), 0.08)
    c.low([(45, 28), (62, 44), (76, 38), (59, 24)], (170, 194, 226), 0.08)
    c.flat([(33, 54), (57, 54), (57, 76), (33, 76)], (74, 124, 194))
    c.flat([(45, 58), (52, 67), (49, 72), (41, 72), (38, 67)], (248, 250, 255))


def tofu(c):
    c.low([(50, 30), (82, 44), (50, 58), (18, 44)], (252, 250, 238), 0.06)
    c.low([(18, 44), (50, 58), (50, 86), (18, 72)], (234, 230, 214), 0.05)
    c.low([(50, 58), (82, 44), (82, 72), (50, 86)], (208, 204, 186), 0.05)
    for x, y in [(28, 65), (39, 70)]:
        c.flat(ell(x, y, 2, 2.6, 8, 24), (66, 54, 48))
    c.flat([(30, 73), (33, 76), (37, 77.5), (40, 78), (37, 79), (33, 78)], (66, 54, 48))


def egg(c):
    pts = []
    for i in range(16):
        t = 2 * math.pi * i / 16
        pts.append((50 + 25 * math.cos(t) * (1 - 0.18 * math.sin(t)), 54 - 34 * math.sin(t)))
    c.low(pts, (236, 206, 160), 0.18)
    c.flat(ell(41, 38, 5, 9, 8, 25), (250, 232, 198))


def flour(c):
    c.low([(26, 36), (74, 36), (80, 52), (78, 84), (70, 90), (30, 90), (22, 84), (20, 52)], (230, 206, 158), 0.14)
    c.low([(28, 37), (72, 37), (70, 26), (61, 30), (50, 22), (39, 30), (30, 26)], (218, 192, 142), 0.16)
    c.flat([(25, 38), (75, 38), (75, 44), (25, 44)], (198, 72, 62))
    c.flat([(34, 52), (66, 52), (66, 78), (34, 78)], (252, 248, 236))
    c.flat([(49, 56), (51, 56), (51, 74), (49, 74)], (196, 150, 60))
    for y in (60, 66, 72):
        for s in (-1, 1):
            c.flat(ell(50 + s * 4.5, y - 2, 4.2, 1.8, 6, s * -40), (224, 176, 66))
    c.flat(ell(50, 56, 1.8, 3, 6), (224, 176, 66))


def coffee(c):
    c.low([(30, 38), (70, 38), (64, 88), (36, 88)], (246, 240, 228), 0.06)
    c.flat([(31.5, 54), (68.5, 54), (66, 74), (34, 74)], (176, 120, 72))
    c.flat(ell(50, 64, 4.4, 4.4, 8), (250, 236, 210))
    c.low([(26, 28), (74, 28), (74, 38), (26, 38)], (92, 62, 46), 0.14)
    c.low([(32, 22), (68, 22), (74, 28), (26, 28)], (118, 82, 60), 0.14)


ITEMS = [
    ("Bread", bread), ("Croissant", croissant), ("Baguette", baguette), ("RiceBall", rice_ball),
    ("Kimbap", kimbap), ("Tteokbokki", tteokbokki), ("Hamburger", hamburger), ("Milk", milk),
    ("Tofu", tofu), ("Egg", egg), ("Flour", flour), ("Coffee", coffee),
]


def write_meta(path, template, folder=False):
    meta = path.with_name(path.name + ".meta")
    if meta.exists():
        return
    if folder:
        meta.write_text("fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
                        "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % uuid.uuid4().hex, newline="\n")
        return
    lines = template.read_text().splitlines()
    guid_done = False
    for i, ln in enumerate(lines):
        if ln.startswith("guid:") and not guid_done:
            lines[i] = "guid: " + uuid.uuid4().hex
            guid_done = True
        elif ln.strip().startswith("spriteID:"):
            lines[i] = "    spriteID: " + uuid.uuid4().hex
        elif ln.strip().startswith("maxTextureSize:"):
            lines[i] = ln.split(":")[0] + ": 1024"
    meta.write_text("\n".join(lines) + "\n", newline="\n")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    write_meta(OUT, None, folder=True)
    sheet = Image.new("RGBA", (SIZE // 2 * 4, SIZE // 2 * 3), (240, 232, 218, 255))
    for i, (name, fn) in enumerate(ITEMS):
        c = Canvas()
        fn(c)
        img = c.out()
        p = OUT / f"Food_{name}.png"
        img.save(p)
        write_meta(p, META_TEMPLATE)
        sheet.alpha_composite(img.resize((SIZE // 2, SIZE // 2), Image.LANCZOS), (i % 4 * SIZE // 2, i // 4 * SIZE // 2))
        print(p.name)
    sheet.save(HERE / "Food_Sheet.png")


if __name__ == "__main__":
    main()
