"""로우폴리 3D 메시 도구 + 소프트웨어 렌더러 (numpy + Pillow만 사용).

좌표계: Y가 위. 아이템은 대략 -1.2 ~ 1.2 안에 만든다.
프리미티브는 전부 lathe(회전체) / prism(압출) / tube(축 방향 압출) 세 가지로 만든다.
"""
import math
from pathlib import Path

import numpy as np
from PIL import Image

PI = math.pi


def rotm(rx=0, ry=0, rz=0):
    """도(degree) 단위 오일러 회전. X → Y → Z 순서."""
    a, b, c = (math.radians(v) for v in (rx, ry, rz))
    X = np.array([[1, 0, 0], [0, math.cos(a), -math.sin(a)], [0, math.sin(a), math.cos(a)]])
    Y = np.array([[math.cos(b), 0, math.sin(b)], [0, 1, 0], [-math.sin(b), 0, math.cos(b)]])
    Z = np.array([[math.cos(c), -math.sin(c), 0], [math.sin(c), math.cos(c), 0], [0, 0, 1]])
    return Z @ Y @ X


def align_y(d):
    """+Y 축을 방향 d 로 돌리는 회전 행렬."""
    d = np.array(d, float)
    d /= np.linalg.norm(d)
    y = np.array([0, 1.0, 0])
    if np.allclose(d, y):
        return np.eye(3)
    if np.allclose(d, -y):
        return rotm(180, 0, 0)
    ax = np.cross(y, d)
    s = np.linalg.norm(ax)
    ax /= s
    c = float(y @ d)
    K = np.array([[0, -ax[2], ax[1]], [ax[2], 0, -ax[0]], [-ax[1], ax[0], 0]])
    return np.eye(3) + K * s + K @ K * (1 - c)


def rnd(p):
    return lambda t: math.sin(PI * t) ** p


# ---------- 지오메트리 (verts, faces) ----------

def lathe_geo(profile, seg=8, closed=False, caps=True):
    P = len(profile)
    V = []
    for r, y in profile:
        for k in range(seg):
            a = 2 * PI * k / seg
            V.append((r * math.cos(a), y, r * math.sin(a)))
    F = []
    for i in range(P if closed else P - 1):
        j = (i + 1) % P
        for k in range(seg):
            k2 = (k + 1) % seg
            F.append((i * seg + k, i * seg + k2, j * seg + k2, j * seg + k))
    if caps and not closed:
        for idx in (0, P - 1):
            if profile[idx][0] > 1e-6:
                c = len(V)
                V.append((0, profile[idx][1], 0))
                for k in range(seg):
                    F.append((c, idx * seg + k, idx * seg + (k + 1) % seg))
    return V, F


def sphere_profile(rings=5, sy=1.0):
    return [(math.sin(PI * i / rings), -math.cos(PI * i / rings) * sy) for i in range(rings + 1)]


def prism_geo(poly, h, axis="y"):
    n = len(poly)
    cx = sum(p[0] for p in poly) / n
    cy = sum(p[1] for p in poly) / n

    def pt(p, t):
        return (p[0], t, p[1]) if axis == "y" else (p[0], p[1], t)

    lo, hi = (0, h) if axis == "y" else (-h / 2, h / 2)
    V = [pt(p, lo) for p in poly] + [pt(p, hi) for p in poly] + [pt((cx, cy), lo), pt((cx, cy), hi)]
    F = []
    for i in range(n):
        j = (i + 1) % n
        F.append((i, j, n + j, n + i))
        F.append((2 * n, j, i))
        F.append((2 * n + 1, n + i, n + j))
    return V, F


def box_geo(sx, sy, sz):
    """중심이 원점인 박스."""
    V, F = prism_geo([(-sx / 2, -sz / 2), (sx / 2, -sz / 2), (sx / 2, sz / 2), (-sx / 2, sz / 2)], sy, "y")
    return [(x, y - sy / 2, z) for x, y, z in V], F


def tube_geo(L, prof, sy=1.0, sz=1.0, seg=8, n=8, bend=None, cap=True):
    """X축을 따라 뻗는 관. prof(t)는 0..1 구간의 반지름 배율, bend는 휨 반지름."""
    V, F = [], []

    def place(x, y, z):
        if not bend:
            return (x, y, z)
        th = x / bend
        rho = bend - y
        return (rho * math.sin(th), bend - rho * math.cos(th), z)

    for i in range(n + 1):
        x = (i / n - .5) * L
        w = prof(i / n)
        for k in range(seg):
            a = 2 * PI * k / seg
            V.append(place(x, w * sy * math.cos(a), w * sz * math.sin(a)))
    for i in range(n):
        for k in range(seg):
            k2 = (k + 1) % seg
            F.append((i * seg + k, i * seg + k2, (i + 1) * seg + k2, (i + 1) * seg + k))
    if cap:
        for i in (0, n):
            if prof(i / n) > 1e-3:
                c = len(V)
                V.append(place((i / n - .5) * L, 0, 0))
                for k in range(seg):
                    F.append((c, i * seg + k, i * seg + (k + 1) % seg))
    return V, F


# ---------- 메시 ----------

class Mesh:
    def __init__(self):
        self.V = []
        self.F = []  # (a, b, c, color)

    def add(self, geo, col, at=(0, 0, 0), rot=(0, 0, 0), sc=(1, 1, 1), M=None):
        V, F = geo
        if isinstance(sc, (int, float)):
            sc = (sc, sc, sc)
        A = np.array(V, float) * np.array(sc, float)
        A = A @ (M if M is not None else rotm(*rot)).T + np.array(at, float)
        b = len(self.V)
        self.V.extend(A.tolist())
        for f in F:
            for i in range(1, len(f) - 1):
                self.F.append((b + f[0], b + f[i], b + f[i + 1], tuple(col)))

    def lathe(self, col, profile, seg=8, closed=False, caps=True, **tf):
        self.add(lathe_geo(profile, seg, closed, caps), col, **tf)

    def sphere(self, col, r=(1, 1, 1), seg=8, rings=5, **tf):
        if isinstance(r, (int, float)):
            r = (r, r, r)
        self.add(lathe_geo(sphere_profile(rings), seg), col, sc=r, **tf)

    def cyl(self, col, r, h, seg=8, **tf):
        self.add(lathe_geo([(r, 0), (r, h)], seg), col, **tf)

    def cone(self, col, r, h, seg=8, **tf):
        self.add(lathe_geo([(r, 0), (0, h)], seg), col, **tf)

    def box(self, col, size, **tf):
        self.add(box_geo(*size), col, **tf)

    def prism(self, col, poly, h, axis="y", **tf):
        self.add(prism_geo(poly, h, axis), col, **tf)

    def tube(self, col, L, prof, sy=1.0, sz=1.0, seg=8, n=8, bend=None, **tf):
        self.add(tube_geo(L, prof, sy, sz, seg, n, bend), col, **tf)

    def torus(self, col, R, r, seg=10, ring=6, **tf):
        prof = [(R + r * math.cos(2 * PI * i / ring), r * math.sin(2 * PI * i / ring)) for i in range(ring)]
        self.add(lathe_geo(prof, seg, closed=True), col, **tf)

    def rod(self, col, p, q, r=.05, seg=5):
        p, q = np.array(p, float), np.array(q, float)
        d = q - p
        self.add(lathe_geo([(r, 0), (r, float(np.linalg.norm(d)))], seg), col, at=tuple(p), M=align_y(d))

    def mark(self):
        return len(self.V)

    def xform(self, start, rot=(0, 0, 0), at=(0, 0, 0), sc=1.0):
        A = np.array(self.V[start:], float) * sc
        self.V[start:] = (A @ rotm(*rot).T + np.array(at, float)).tolist()


# ---------- 폴리곤 도우미 ----------

def scl(poly, s, c=None):
    cx, cy = c or (sum(p[0] for p in poly) / len(poly), sum(p[1] for p in poly) / len(poly))
    return [(cx + (x - cx) * s, cy + (y - cy) * s) for x, y in poly]


def ngon(n, r, rot=0.0):
    return [(r * math.cos(rot + 2 * PI * i / n), r * math.sin(rot + 2 * PI * i / n)) for i in range(n)]


def star(n, ro, ri):
    return [((ro if i % 2 == 0 else ri) * math.cos(PI * i / n), (ro if i % 2 == 0 else ri) * math.sin(PI * i / n)) for i in range(2 * n)]


def wob(n, r, amp=.12, seed=0, sx=1.0, sz=1.0):
    pts = []
    for i in range(n):
        a = 2 * PI * i / n
        k = 1 + amp * (((i * 53 + seed * 17) % 9) - 4) / 4
        pts.append((r * k * math.cos(a) * sx, r * k * math.sin(a) * sz))
    return pts


def rrect(w, h, r, n=3):
    pts = []
    for (cx, cy, a0) in [(w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)]:
        for i in range(n + 1):
            a = math.radians(a0 + 90 * i / n)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def sector(r, a0, a1, n=4, inner=0.0):
    out = [(r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]
    if inner:
        out += [(inner * math.cos(math.radians(a1 - (a1 - a0) * i / n)), inner * math.sin(math.radians(a1 - (a1 - a0) * i / n))) for i in range(n + 1)]
    else:
        out.append((0, 0))
    return out


# ---------- 렌더 ----------

LIGHT = np.array([-.5, .78, .62])
LIGHT /= np.linalg.norm(LIGHT)


def render(mesh, size=512, ss=3, yaw=-32, pitch=30, fill=.88):
    V = np.array(mesh.V, float) @ (rotm(pitch, 0, 0) @ rotm(0, yaw, 0)).T
    sx, sy, sz = V[:, 0], -V[:, 1], V[:, 2]
    S = size * ss
    x0, x1, y0, y1 = sx.min(), sx.max(), sy.min(), sy.max()
    k = fill * S / max(x1 - x0, y1 - y0, 1e-6)
    px = (sx - (x0 + x1) / 2) * k + S / 2
    py = (sy - (y0 + y1) / 2) * k + S / 2
    pz = sz

    zbuf = np.full((S, S), -np.inf)
    img = np.zeros((S, S, 3))
    for fi, (a, b, c, col) in enumerate(mesh.F):
        p0, p1, p2 = V[a], V[b], V[c]
        n = np.cross(p1 - p0, p2 - p0)
        m = np.linalg.norm(n)
        if m < 1e-9:
            continue
        n /= m
        if n[2] < 0:
            n = -n
        lit = .66 + .5 * max(0.0, float(n @ LIGHT))
        lit *= 1 + (((fi * 2654435761) >> 7) % 7 - 3) * .006
        rgb = np.clip(np.array(col, float) * lit, 0, 255)
        xa, ya, xb, yb, xc, yc = px[a], py[a], px[b], py[b], px[c], py[c]
        den = (yb - yc) * (xa - xc) + (xc - xb) * (ya - yc)
        if abs(den) < 1e-9:
            continue
        bx0, bx1 = max(int(min(xa, xb, xc)), 0), min(int(max(xa, xb, xc)) + 2, S)
        by0, by1 = max(int(min(ya, yb, yc)), 0), min(int(max(ya, yb, yc)) + 2, S)
        if bx0 >= bx1 or by0 >= by1:
            continue
        X, Y = np.meshgrid(np.arange(bx0, bx1) + .5, np.arange(by0, by1) + .5)
        w0 = ((yb - yc) * (X - xc) + (xc - xb) * (Y - yc)) / den
        w1 = ((yc - ya) * (X - xc) + (xa - xc) * (Y - yc)) / den
        w2 = 1 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        z = w0 * pz[a] + w1 * pz[b] + w2 * pz[c]
        zs = zbuf[by0:by1, bx0:bx1]
        upd = inside & (z > zs)
        zs[upd] = z[upd]
        img[by0:by1, bx0:bx1][upd] = rgb
    alpha = np.isfinite(zbuf).astype(float)
    rgba = np.dstack([img * alpha[..., None], alpha])
    rgba = rgba.reshape(size, ss, size, ss, 4).mean((1, 3))
    a = rgba[..., 3:4]
    out = np.where(a > 0, rgba[..., :3] / np.maximum(a, 1e-6), 0)
    return Image.fromarray(np.dstack([np.clip(out, 0, 255), a * 255]).astype(np.uint8), "RGBA")


# ---------- OBJ 내보내기 ----------

def write_obj(mesh, path: Path):
    """색마다 재질 하나. 면 법선을 넣어 로우폴리 면이 그대로 보이게 한다."""
    path = Path(path)
    V = np.array(mesh.V)
    mats = {}
    for f in mesh.F:
        mats.setdefault(f[3], f"c{len(mats)}")
    lines = [f"mtllib {path.stem}.mtl"]
    lines += ["v %.4f %.4f %.4f" % tuple(v) for v in V]
    normals, faces = [], []
    for a, b, c, col in mesh.F:
        n = np.cross(V[b] - V[a], V[c] - V[a])
        m = np.linalg.norm(n)
        if m < 1e-9:
            continue
        normals.append("vn %.4f %.4f %.4f" % tuple(n / m))
        faces.append((col, a + 1, b + 1, c + 1, len(normals)))
    lines += normals
    cur = None
    for col, a, b, c, ni in faces:
        if col != cur:
            lines.append(f"usemtl {mats[col]}")
            cur = col
        lines.append(f"f {a}//{ni} {b}//{ni} {c}//{ni}")
    path.write_text("\n".join(lines) + "\n", newline="\n")
    mtl = []
    for col, name in mats.items():
        mtl += [f"newmtl {name}", "Kd %.3f %.3f %.3f" % tuple(v / 255 for v in col), "Ka 0 0 0", "Ks 0 0 0", ""]
    path.with_suffix(".mtl").write_text("\n".join(mtl), newline="\n")
