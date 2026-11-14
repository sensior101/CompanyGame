"""Instagram Reels devlog teaser (1080x1920, 30fps). Run: python make_teaser.py"""
import glob, os, subprocess, math
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import imageio_ffmpeg

ROOT = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(ROOT)
OUT = os.path.join(ROOT, "CompanyGame_Teaser_Reel.mp4")
W, H, FPS = 1080, 1920, 30
BG, INK, ACC, SUB = (247, 242, 232), (34, 34, 40), (255, 122, 69), (120, 116, 108)
F = "C:/Windows/Fonts/"
def font(sz, bold=True): return ImageFont.truetype(F + ("malgunbd.ttf" if bold else "malgun.ttf"), sz)

def A(p): return os.path.join(ART, p)
def load(p): return Image.open(A(p)).convert("RGBA")
def ease(t): t = max(0, min(1, t)); return 1 - (1 - t) ** 3
def fit(im, w, h):
    im = im.copy(); im.thumbnail((w, h), Image.LANCZOS); return im

def card(im, w, h, radius=48):
    """Image on a rounded card with shadow."""
    im = im.convert("RGB").resize((w, h), Image.LANCZOS)
    m = Image.new("L", (w, h), 0); ImageDraw.Draw(m).rounded_rectangle((0, 0, w, h), radius, fill=255)
    out = Image.new("RGBA", (w + 80, h + 80), (0, 0, 0, 0))
    sh = Image.new("RGBA", out.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((40, 52, 40 + w, 52 + h), radius, fill=(0, 0, 0, 60))
    out.alpha_composite(sh.filter(ImageFilter.GaussianBlur(18)))
    rgba = im.convert("RGBA"); rgba.putalpha(m); out.alpha_composite(rgba, (40, 40))
    return out

def cover(im, w, h):
    s = max(w / im.width, h / im.height); im = im.resize((math.ceil(im.width * s), math.ceil(im.height * s)), Image.LANCZOS)
    x, y = (im.width - w) // 2, (im.height - h) // 2; return im.crop((x, y, x + w, y + h))

def text(d, xy, s, f, fill=INK, anchor="mm"): d.text(xy, s, font=f, fill=fill, anchor=anchor)

def chip(d, cx, cy, s, f, fill=ACC, fg=(255, 255, 255)):
    b = d.textbbox((0, 0), s, font=f); w, h = b[2] - b[0] + 56, b[3] - b[1] + 34
    d.rounded_rectangle((cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2), h / 2, fill=fill); text(d, (cx, cy), s, f, fg)

def base(t_scene, tag):
    fr = Image.new("RGBA", (W, H), BG + (255,)); d = ImageDraw.Draw(fr)
    text(d, (W / 2, 150), "COMPANY GAME  ·  DEVLOG", font(34), SUB)
    if tag: chip(d, W / 2, 230, tag, font(36))
    return fr, d

def caption(d, lines, t, y=1560):
    for i, (s, sz, c) in enumerate(lines):
        a = ease((t - 0.15 * i) / 0.35)
        text(d, (W / 2, y + i * (sz + 30) + (1 - a) * 40), s, font(sz), c + (int(255 * a),))

# ---------- assets ----------
# Source has 12 static lead-in frames and a frozen tail; frames 12..42 are one seamless step cycle (43 == 12).
walk = sorted(glob.glob(A("Daldongne/Characters/HandmadeHairCollection_20261105/Ponytail_WalkAttached_Frames/*.png")))[12:43]
FOOD = "Items/Food/Crafted_20261012/"
burger_v1, burger_v2 = load(FOOD + "QA/Before/Renders/Food_Hamburger.png"), load(FOOD + "Renders/Food_Hamburger.png")
foods = sorted(glob.glob(A(FOOD + "Renders/*.png")))
foods_cards = [card(Image.open(p), 300, 300, 36) for p in foods[::3][:45]]
bldg = sorted(glob.glob(A("WorldDistricts/CommercialBlocks/ParcelFit24_20261104/Previews/CB_0[1-6].png")))
bldg_cards = [card(Image.open(p), 470, 470, 40) for p in bldg[:6]]
store = card(cover(Image.open(A("ConvenienceInterior/Previews/03_Cutaway.png")), 1440, 1080), 900, 675)
lounge = card(cover(Image.open(A("WorldDistricts/RealEstateInterior_20261107/QA/04_Lounge_Eye.png")), 1600, 1000), 900, 562)
hairs = [card(cover(Image.open(p), 720, 800), 440, 489, 40) for p in sorted(glob.glob(A("Daldongne/Characters/HandmadeHairCollection_20261105/FiveStyles_20261105/*_Front.png")))[:4]]

# ---------- scenes: (duration, draw(t, local_frame)) ----------
def s_hook(t, i):
    fr, d = base(t, None)
    a = ease(t / 0.4)
    text(d, (W / 2, 820 - (1 - a) * 60), "회사 키우는 게임", font(110), INK + (int(255 * a),))
    text(d, (W / 2, 960 - (1 - a) * 60), "만드는 중 🛠".replace("🛠", ""), font(110), ACC + (int(255 * a),))
    b = ease((t - 0.6) / 0.4)
    text(d, (W / 2, 1130), "작업과정 몰래 공개합니다 🤫".replace("🤫", ""), font(48, False), SUB + (int(255 * b),))
    # blinking cursor bar
    if int(t * 4) % 2 == 0: d.rectangle((W / 2 - 60, 1220, W / 2 + 60, 1232), fill=ACC)
    return fr

def s_walk(t, i):
    fr, d = base(t, "STEP 1 · 캐릭터")
    im = Image.open(walk[i % len(walk)]).convert("RGB")
    c = card(im, 820, 1128, 56); s = 0.92 + 0.08 * ease(t / 0.5)
    c = c.resize((int(c.width * s), int(c.height * s)), Image.LANCZOS)
    fr.alpha_composite(c, ((W - c.width) // 2, 300))
    caption(d, [("포니테일 흔들리는 거 보이시나요", 54, INK), ("머리카락 붙이는 데만 며칠 씀", 40, SUB)], t, 1560)
    return fr

def s_hair(t, i):
    fr, d = base(t, "STEP 1 · 캐릭터")
    for k, c in enumerate(hairs):
        a = ease((t - k * 0.12) / 0.4)
        x, y = 60 + (k % 2) * 500, 330 + (k // 2) * 560 + int((1 - a) * 120)
        if a > 0: fr.alpha_composite(c, (x, y))
    caption(d, [("헤어 스타일도 하나씩 손으로", 54, INK)], t, 1560 + 120)
    return fr

def s_burger(t, i):
    fr, d = base(t, "STEP 2 · 음식")
    v1, v2 = card(burger_v1, 860, 860, 56), card(burger_v2, 860, 860, 56)
    x, y = (W - v1.width) // 2, 360
    fr.alpha_composite(v1, (x, y))
    p = ease((t - 1.1) / 0.8)  # wipe left->right
    if p > 0:
        cut = int(v2.width * p); fr.alpha_composite(v2.crop((0, 0, cut, v2.height)), (x, y))
        d.rectangle((x + cut - 4, y + 40, x + cut + 4, y + v2.height - 40), fill=ACC)
    label = "v1  →  v2" if p > 0.5 else "v1"
    chip(d, W / 2, 1360, label, font(44), INK)
    caption(d, [("햄버거 리메이크 전 / 후", 54, INK), ("빵이 빵처럼 보이기 시작함", 40, SUB)], t, 1560)
    return fr

def s_foodgrid(t, i):
    fr, d = base(t, "STEP 2 · 음식")
    shift = int(t * 220)
    for k, c in enumerate(foods_cards):
        col, row = k % 3, k // 3
        x = 15 + col * 345; y = 320 + row * 345 - shift + (col % 2) * 170
        if -400 < y < 1500: fr.alpha_composite(c, (x, y))
    d.rectangle((0, 1440, W, H), fill=BG)
    d.rectangle((0, 0, W, 290), fill=BG); text(d, (W / 2, 150), "COMPANY GAME  ·  DEVLOG", font(34), SUB); chip(d, W / 2, 230, "STEP 2 · 음식", font(36))
    n = min(135, int(135 * ease(t / 1.6)))
    text(d, (W / 2, 1580), f"{n}개", font(130), ACC)
    caption(d, [("메뉴 하나하나 다 3D로 빚었어요", 46, INK)], t, 1720)
    return fr

def s_town(t, i):
    fr, d = base(t, "STEP 3 · 동네")
    for k, c in enumerate(bldg_cards):
        a = ease((t - k * 0.1) / 0.35)
        x, y = 30 + (k % 2) * 520, 310 + (k // 2) * 410 - int((1 - a) * 200)
        if a > 0: fr.alpha_composite(c, (x, y))
    d.rectangle((0, 1500, W, H), fill=BG)
    caption(d, [("동네 상가도 짓고", 54, INK), ("여기 다 내 가게 될 예정", 40, SUB)], t, 1600)
    return fr

def s_interior(t, i):
    fr, d = base(t, "STEP 4 · 가게")
    a, b = ease(t / 0.4), ease((t - 0.5) / 0.4)
    fr.alpha_composite(store, ((W - store.width) // 2 - int((1 - a) * 300), 320))
    if b > 0: fr.alpha_composite(lounge, ((W - lounge.width) // 2 + int((1 - b) * 300), 1000))
    caption(d, [("편의점, 부동산… 안까지 들어가져요", 46, INK)], t, 1720)
    return fr

def s_outro(t, i):
    fr = Image.new("RGBA", (W, H), INK + (255,)); d = ImageDraw.Draw(fr)
    a = ease(t / 0.5)
    text(d, (W / 2, 640), "아직 만드는 중이지만", font(52, False), (200, 196, 188, int(255 * a)))
    sc = 0.8 + 0.2 * ease((t - 0.4) / 0.5); f = font(int(150 * sc))
    b = ease((t - 0.4) / 0.4)
    text(d, (W / 2, 860), "COMPANY", f, (255, 255, 255, int(255 * b)))
    text(d, (W / 2, 1020), "GAME", f, ACC + (int(255 * b),))
    c = ease((t - 1.2) / 0.4)
    text(d, (W / 2, 1210), "사장님 될 준비 되셨나요?", font(54), (255, 255, 255, int(255 * c)))
    e = ease((t - 2.0) / 0.4)
    if e > 0: chip(d, W / 2, 1420, "팔로우하고 개발일지 같이 봐요 →", font(42), ACC)
    text(d, (W / 2, 1560), "🎮 COMING SOON".replace("🎮 ", ""), font(36), (150, 146, 138, int(255 * e)))
    return fr

SCENES = [(2.6, s_hook), (3.4, s_walk), (2.4, s_hair), (3.6, s_burger), (2.8, s_foodgrid), (2.6, s_town), (2.8, s_interior), (4.0, s_outro)]

# ---------- audio: procedural lo-fi beat + cut SFX (no third-party music) ----------
def make_audio(path):
    import numpy as np, wave
    SR = 44100; total = sum(d for d, _ in SCENES); N = int(SR * total)
    L = np.zeros(N); R = np.zeros(N); rng = np.random.default_rng(7)
    BPM = 100; beat = 60 / BPM
    def add(sig, t, gain=1.0, pan=0.0):
        s = int(t * SR); e = min(N, s + len(sig))
        if s >= N or e <= s: return
        L[s:e] += sig[:e - s] * gain * (1 - max(0, pan)); R[s:e] += sig[:e - s] * gain * (1 + min(0, pan))
    def env(n, a=0.005, d=0.3):
        t = np.arange(n) / SR; return np.minimum(1, t / a) * np.exp(-t / d)
    def tone(f, dur, d=0.4, harm=(1, .5, .25)):
        n = int(dur * SR); t = np.arange(n) / SR
        return sum(h * np.sin(2 * np.pi * f * (k + 1) * t) for k, h in enumerate(harm)) * env(n, 0.01, d)
    def mtof(m): return 440 * 2 ** ((m - 69) / 12)
    kick_n = int(.35 * SR); kt = np.arange(kick_n) / SR
    kick = np.sin(2 * np.pi * (50 * kt + 60 * (1 - np.exp(-kt * 30)) / 30 * 1)) * np.exp(-kt * 9)
    kick = np.sin(2 * np.pi * np.cumsum(45 + 110 * np.exp(-kt * 35)) / SR) * np.exp(-kt * 8)
    snare = rng.normal(0, 1, int(.2 * SR)) * env(int(.2 * SR), .001, .06)
    hat = np.diff(rng.normal(0, 1, int(.05 * SR) + 1)) * env(int(.05 * SR), .001, .012)
    chords = [[53, 57, 60, 64], [52, 55, 59, 62], [50, 53, 57, 60], [48, 52, 55, 59]]  # Fmaj7 Em7 Dm7 Cmaj7
    melody = [72, 69, 67, 69, 72, 74, 72, 67, 71, 67, 64, 67, 69, 72, 69, 65]
    outro = sum(d for d, _ in SCENES[:-1])
    t = 0.0; b = 0
    while t < total:
        bar, pos = divmod(b, 4)
        drums = t < outro + 0.05
        if drums and t > 0.3:
            if pos in (0, 2): add(kick, t, .9)
            if pos in (1, 3): add(snare, t, .25)
            for h in (0, .5): add(hat, t + h * beat, .18, .4)
        if pos == 0:
            for m in chords[bar % 4]: add(tone(mtof(m), beat * 4.2, 1.6, (1, .3, .1)), t, .07, (m % 3 - 1) * .3)
            add(tone(mtof(chords[bar % 4][0] - 12), beat * 4, 1.2, (1, .2)), t, .22)
        if t > 2.6 - 0.01:
            add(tone(mtof(melody[b % 16]), beat, .25, (1, .4, .2, .1)), t + (.5 * beat if b % 3 == 2 else 0), .12, .2)
        t += beat; b += 1
    # whoosh on each scene cut
    cut = 0.0
    for d, _ in SCENES[:-1]:
        cut += d; n = int(.45 * SR); x = np.arange(n) / n
        w = rng.normal(0, 1, n); w = np.convolve(w, np.ones(12) / 12, "same") * np.sin(np.pi * x) ** 2
        add(w, cut - .25, .35, 0)
    # pop when STEP chips / captions land
    cut = 0.0
    for d, fn in SCENES[:-1]:
        add(tone(1200, .12, .04, (1,)), cut + .05, .12); add(tone(900, .12, .05, (1,)), cut + .5, .08)
        cut += d
    # counter ticks in food grid
    fstart = sum(d for d, fn in SCENES[:4])
    for k in range(18): add(tone(2000 + k * 40, .03, .01, (1,)), fstart + 1.6 * (k / 18) ** .5, .06)
    # outro: big hit + sparkle chime
    add(kick, outro + .4, 1.0); add(tone(mtof(41), 3, 1.5, (1, .3)), outro + .4, .3)
    for k, m in enumerate([72, 76, 79, 84]): add(tone(mtof(m), 1.5, .7, (1, .2)), outro + 1.2 + k * .09, .12, (k - 1.5) * .3)
    add(tone(mtof(84), 1.5, .6, (1, .3)), outro + 2.0, .1)
    fade = np.ones(N); fn_ = int(.8 * SR); fade[-fn_:] = np.linspace(1, 0, fn_)
    st = np.stack([L, R], 1) * fade[:, None]; st = np.tanh(st * 1.4) / np.tanh(1.4) * .85
    with wave.open(path, "wb") as wf:
        wf.setnchannels(2); wf.setsampwidth(2); wf.setframerate(SR)
        wf.writeframes((st * 32767).astype("<i2").tobytes())

def main():
    ff = imageio_ffmpeg.get_ffmpeg_exe()
    wav = os.path.join(ROOT, "_teaser_audio.wav"); make_audio(wav)
    p = subprocess.Popen([ff, "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
                          "-i", wav, "-shortest",
                          "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-preset", "medium",
                          "-c:a", "aac", "-movflags", "+faststart", OUT], stdin=subprocess.PIPE)
    prev = None
    for dur, fn in SCENES:
        n = int(dur * FPS)
        for i in range(n):
            fr = fn(i / FPS, i).convert("RGB")
            if prev is not None and i < 6:  # quick crossfade
                fr = Image.blend(prev, fr, (i + 1) / 7)
            p.stdin.write(fr.tobytes())
            last = fr
        prev = last
        if fn is s_hook: thumb = None
    p.stdin.close(); p.wait(); os.remove(wav)
    s_outro(3.0, 0).convert("RGB").save(os.path.join(ROOT, "Teaser_Cover.png"))
    print("wrote", OUT)

if __name__ == "__main__":
    main()
