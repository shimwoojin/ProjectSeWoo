"""C1 — 스팀 스토어·라이브러리 캡슐 아트를 게임 스프라이트로 만든다.

    "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-store-capsules.py

make-achievement-icons.py 와 같은 방식이다 - 손으로 그리지 않고 게임에 들어간 그림(원숭이 펀치 시트, 나무,
바나나, 타격 이펙트, 커서 장식)을 배치한다. 시스템 파이썬에는 PIL 이 없어서 ComfyUI venv 로 돌린다.
출력: assets/_store/capsules/ (.gdignore 로 Godot 임포트에서 빠진다). 규격과 올리는 자리는 docs/C1-STORE.md §2.

규칙(스팀 가이드): 캡슐에는 게임 이름이 읽히게 들어가야 하고, 리뷰·수상·할인 문구는 넣지 않는다.
라이브러리 히어로에는 글자를 넣지 않는다(로고는 라이브러리 로고로 따로 올린다).
"""

import math
import os
import random
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets", "_store", "capsules")
FONT = os.path.join(ROOT, "assets", "fonts", "Pretendard-Bold.ttf")
TITLE = ("Punch", "Monkey")

SKY_TOP = (74, 160, 226)
SKY_BOTTOM = (196, 236, 250)
HILL_FAR = (132, 196, 110)
HILL_NEAR = (92, 170, 78)
GROUND = (70, 142, 58)
INK = (58, 30, 12)
BANANA = (255, 214, 58)
BANANA_HI = (255, 244, 170)


# ---------- 스프라이트 ----------

def asset(*parts):
    return Image.open(os.path.join(ROOT, "assets", *parts)).convert("RGBA")


def cell(sheet, index, w, h):
    return sheet.crop((index * w, 0, (index + 1) * w, h))


MONKEY_CELL = (298, 324)
MONKEY_FIST = (287, 158)          # 펀치 프레임 2 에서 뻗은 주먹 끝 (칸 기준)
FX_CELL = (379, 403)


def monkey(height, index=2):
    im = cell(asset("entities", "monkey_punch.png"), index, *MONKEY_CELL)
    s = height / MONKEY_CELL[1]
    return im.resize((round(im.width * s), round(im.height * s)), Image.LANCZOS), s


def scaled(im, height=None, width=None):
    s = height / im.height if height else width / im.width
    return im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)


def trim(im):
    box = im.getchannel("A").getbbox()
    return im.crop(box) if box else im


def with_shadow(canvas, sprite, x, y, offset=(0.02, 0.03), blur=0.025, strength=0.45):
    """sprite 를 (x, y) 좌상단에 붙이되 아래로 흐린 그림자를 깐다. offset·blur 는 스프라이트 높이 비율."""
    h = sprite.height
    sh = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    alpha = sprite.getchannel("A").point(lambda a: a * strength)
    sh.paste((20, 30, 10, 255), (round(x + offset[0] * h), round(y + offset[1] * h)), alpha)
    sh = sh.filter(ImageFilter.GaussianBlur(max(1, blur * h)))
    canvas.alpha_composite(sh)
    canvas.alpha_composite(sprite, (round(x), round(y)))


# ---------- 배경 ----------

def background(W, H, horizon=0.72, sun=(0.82, 0.18)):
    bg = Image.new("RGBA", (W, H))
    d = ImageDraw.Draw(bg)
    for y in range(H):
        t = min(1.0, y / (H * horizon))
        c = tuple(round(SKY_TOP[i] + (SKY_BOTTOM[i] - SKY_TOP[i]) * t) for i in range(3))
        d.line([(0, y), (W, y)], fill=c + (255,))

    # 해 쪽 빛 번짐
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    r = max(W, H) * 0.35
    cx, cy = sun[0] * W, sun[1] * H
    ImageDraw.Draw(glow).ellipse((cx - r, cy - r, cx + r, cy + r), fill=(255, 250, 220, 150))
    bg.alpha_composite(glow.filter(ImageFilter.GaussianBlur(r * 0.45)))

    # 구름 몇 덩이
    rnd = random.Random(7)
    clouds = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    cd = ImageDraw.Draw(clouds)
    for _ in range(5):
        x = rnd.uniform(0, W)
        y = rnd.uniform(0.08, 0.4) * H * horizon
        s = rnd.uniform(0.05, 0.09) * max(W, H * 1.4)
        for k in range(4):
            ox = (k - 1.5) * s * 0.55
            oy = -s * 0.18 if k in (1, 2) else 0
            rr = s * (0.42 if k in (1, 2) else 0.32)
            cd.ellipse((x + ox - rr, y + oy - rr, x + ox + rr, y + oy + rr), fill=(255, 255, 255, 190))
    bg.alpha_composite(clouds.filter(ImageFilter.GaussianBlur(max(1, W * 0.002))))

    # 언덕 두 겹 + 땅
    gy = horizon * H
    hills = ImageDraw.Draw(bg)
    for color, base, amp, freq, phase in ((HILL_FAR, gy - H * 0.06, H * 0.05, 2.2, 0.6),
                                          (HILL_NEAR, gy - H * 0.015, H * 0.035, 3.1, 2.0)):
        pts = [(0, H)]
        for x in range(0, W + 8, 8):
            pts.append((x, base - amp * (0.5 + 0.5 * math.sin(x / W * freq * math.pi + phase))))
        pts.append((W, H))
        hills.polygon(pts, fill=color + (255,))
    if gy < H:
        hills.rectangle((0, gy, W, H), fill=GROUND + (255,))
    return bg


# ---------- 로고 ----------

def logo(width, lines=2):
    """'Punch / Monkey' 글자 로고. 바나나 노랑 + 진갈색 외곽선 + 그림자. 투명 PNG."""
    big = 400
    font = ImageFont.truetype(FONT, big)
    words = ["".join(TITLE)] if lines == 1 else list(TITLE)
    stroke = round(big * 0.09)
    probe = ImageDraw.Draw(Image.new("L", (1, 1)))
    sizes = [probe.textbbox((0, 0), w, font=font, stroke_width=stroke) for w in words]
    line_h = round(big * 0.98)
    tw = max(b[2] - b[0] for b in sizes)
    pad = round(big * 0.25)
    W = tw + pad * 2
    H = line_h * len(words) + pad * 2

    fill = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    mask = Image.new("L", (W, H), 0)
    outline = Image.new("L", (W, H), 0)
    md, od = ImageDraw.Draw(mask), ImageDraw.Draw(outline)
    for i, w in enumerate(words):
        # 두 줄이면 둘째 줄을 살짝 오른쪽으로 - 계단처럼 보이게
        shift = (i * big * 0.18) if lines == 2 else 0
        x = W / 2 + shift - (big * 0.09 if lines == 2 else 0)
        y = pad + line_h * i + line_h / 2
        od.text((x, y), w, font=font, fill=255, anchor="mm", stroke_width=stroke, stroke_fill=255)
        md.text((x, y), w, font=font, fill=255, anchor="mm")

    # 글자 안쪽 세로 그라데이션 (위 밝게)
    grad = Image.new("RGBA", (W, H))
    gd = ImageDraw.Draw(grad)
    for y in range(H):
        t = y / H
        c = tuple(round(BANANA_HI[i] + (BANANA[i] - BANANA_HI[i]) * min(1, t * 1.8)) for i in range(3))
        gd.line([(0, y), (W, y)], fill=c + (255,))

    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow.paste((30, 15, 5, 170), (0, round(big * 0.06)), outline)
    out.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(big * 0.03)))
    out.paste((*INK, 255), (0, 0), outline)
    out.paste(grad, (0, 0), mask)
    out = trim(out)
    return scaled(out, width=width)


# ---------- 커서 · 키캡 ----------

def cursor_arrow(size):
    """윈도우 기본 화살표 모양 (흰 몸통 + 검은 테두리). 커서 '장식' 을 보여 주려고 본체를 그린다."""
    pts = [(0, 0), (0, 16), (4, 12.5), (7, 19), (9.5, 18), (6.6, 11.6), (12, 11.6)]
    s = size / 19
    im = Image.new("RGBA", (round(13 * s) + 4, round(20 * s) + 4), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    p = [(2 + x * s, 2 + y * s) for x, y in pts]
    d.polygon(p, fill=(255, 255, 255, 255), outline=(0, 0, 0, 255), width=max(2, round(s * 0.9)))
    return im


def decorated_cursor(canvas, x, y, size):
    """커서 + 매달린 원숭이(hang) + 반짝이 잔상(trail). 바닥(base) 장식은 작게 줄이면 무엇인지 안 읽혀서 뺐다."""
    rnd = random.Random(3)
    for k in range(4):
        sp = scaled(asset("cursor", "deco", "spark_01", "icon.png"), height=size * (0.55 - k * 0.09))
        sp = sp.rotate(rnd.uniform(-25, 25), resample=Image.BICUBIC, expand=True)
        sp.putalpha(sp.getchannel("A").point(lambda a, k=k: a * (1 - k * 0.2)))
        canvas.alpha_composite(sp, (round(x - size * (0.55 + k * 0.5)), round(y + size * (0.35 + k * 0.28))))
    hang = scaled(trim(asset("cursor", "monkey", "monkey_01", "icon.png")), height=size * 2.1)
    arrow = cursor_arrow(size)
    canvas.alpha_composite(hang, (round(x - hang.width * 0.28), round(y + size * 0.55)))
    with_shadow(canvas, arrow, x, y, offset=(0.05, 0.08), blur=0.05, strength=0.5)


def keycaps(canvas, y, key_h, letters, x0=None, pressed=None):
    """아래 앞쪽에 키캡 한 줄. pressed 인덱스는 눌려 내려가고 '+1' 이 뜬다."""
    W = canvas.width
    gap = key_h * 0.14
    total = len(letters) * key_h + (len(letters) - 1) * gap
    x = (W - total) / 2 if x0 is None else x0
    font = ImageFont.truetype(FONT, round(key_h * 0.42))
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    r = key_h * 0.16
    for i, ch in enumerate(letters):
        down = key_h * 0.08 if i == pressed else 0
        kx, ky = x + i * (key_h + gap), y + down
        d.rounded_rectangle((kx, ky + key_h * 0.1, kx + key_h, ky + key_h * 1.1), r, fill=(150, 150, 162, 255))
        d.rounded_rectangle((kx, ky, kx + key_h, ky + key_h * (1.0 if not down else 0.96)), r,
                            fill=(246, 246, 250, 255), outline=(120, 120, 132, 255), width=max(1, round(key_h * 0.03)))
        d.rounded_rectangle((kx + key_h * 0.12, ky + key_h * 0.08, kx + key_h * 0.88, ky + key_h * 0.8), r * 0.8,
                            fill=(255, 255, 255, 255))
        d.text((kx + key_h * 0.5, ky + key_h * 0.44), ch, font=font, fill=(70, 70, 84, 255), anchor="mm")
        if i == pressed:
            pf = ImageFont.truetype(FONT, round(key_h * 0.5))
            d.text((kx + key_h * 0.5, ky - key_h * 0.45), "+1", font=pf, fill=BANANA + (255,), anchor="mm",
                   stroke_width=max(2, round(key_h * 0.06)), stroke_fill=INK + (255,))
    sh = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    sh.paste((10, 30, 10, 140), (0, round(key_h * 0.12)), layer.getchannel("A"))
    canvas.alpha_composite(sh.filter(ImageFilter.GaussianBlur(key_h * 0.1)))
    canvas.alpha_composite(layer)


# ---------- 장면 ----------

def punch_scene(canvas, ground_y, monkey_h, monkey_x, bananas=True, fx=True):
    """원숭이(펀치 프레임)가 오른쪽 나무 줄기를 치는 장면. 나무 위치는 주먹 끝에서 정한다."""
    m, s = monkey(monkey_h)
    my = ground_y - m.height
    fist = (monkey_x + MONKEY_FIST[0] * s, my + MONKEY_FIST[1] * s)

    tree = asset("entities", "tree_full.png")
    tree = scaled(tree, height=monkey_h * 1.95)
    # 줄기 가운데(너비의 약 48%)가 주먹 바로 오른쪽에 오게
    tx = fist[0] + monkey_h * 0.06 - tree.width * 0.48
    ty = ground_y - tree.height * 0.985
    with_shadow(canvas, tree, tx, ty, offset=(0.015, 0.012), blur=0.012, strength=0.35)

    if bananas:
        rnd = random.Random(11)
        for k, (fx_, fy_, rot, hs) in enumerate(((0.30, 0.52, -30, 0.26), (0.72, 0.60, 25, 0.22),
                                                   (0.95, 0.38, 60, 0.2))):
            b = scaled(trim(asset("entities", f"banana_0{k % 4 + 1}.png")), height=monkey_h * hs)
            b = b.rotate(rot + rnd.uniform(-8, 8), resample=Image.BICUBIC, expand=True)
            with_shadow(canvas, b, tx + tree.width * fx_ - b.width / 2, ty + tree.height * fy_,
                        offset=(0.04, 0.06), blur=0.05, strength=0.35)

    with_shadow(canvas, m, monkey_x, my, offset=(0.01, 0.0), blur=0.01, strength=0.0)

    if fx:
        e = cell(asset("effects", "punch_effect.png"), 1, *FX_CELL)
        e = scaled(trim(e), height=monkey_h * 0.55)
        canvas.alpha_composite(e, (round(fist[0] - e.width * 0.42), round(fist[1] - e.height * 0.5)))
    return (tx, ty, tree.width, tree.height)


def place_logo(canvas, lg, cx, cy):
    canvas.alpha_composite(lg, (round(cx - lg.width / 2), round(cy - lg.height / 2)))


# ---------- 규격별 ----------

def wide(W, H, logo_w=0.46, keys=True, cursor=True):
    """가로형 (헤더·메인): 왼쪽 로고, 오른쪽 장면."""
    c = background(W, H, horizon=0.78)
    gy = H * 0.86
    mh = H * 0.46
    punch_scene(c, gy, mh, W * 0.52)
    if cursor:
        decorated_cursor(c, W * 0.30, H * 0.62, H * 0.1)
    if keys:
        keycaps(c, H * 0.84, H * 0.1, "PUNCH", x0=W * 0.05, pressed=2)
    lg = logo(round(W * logo_w), lines=2)
    place_logo(c, lg, W * 0.26, H * 0.33)
    return c


def small(W, H):
    """작은 캡슐 462x174: 스팀이 목록에서 가장 작게 쓰는 곳. 로고가 읽히는 게 전부다."""
    c = background(W, H, horizon=0.8, sun=(0.9, 0.1))
    m, _ = monkey(H * 0.92)
    with_shadow(c, m, W * 0.75, H * 1.02 - m.height)
    lg = logo(round(W * 0.7), lines=1)
    place_logo(c, lg, W * 0.37, H * 0.5)
    return c


def tall(W, H, keys=True):
    """세로형 (세로 캡슐·라이브러리 캡슐): 위 로고, 아래 장면."""
    c = background(W, H, horizon=0.8, sun=(0.8, 0.1))
    gy = H * 0.88
    mh = H * 0.27
    punch_scene(c, gy, mh, W * 0.02)
    decorated_cursor(c, W * 0.14, H * 0.43, H * 0.065)
    if keys:
        keycaps(c, H * 0.9, H * 0.065, "PUNCH", pressed=2)
    lg = logo(round(W * 0.8), lines=2)
    place_logo(c, lg, W * 0.5, H * 0.17)
    return c


def hero(W, H):
    """라이브러리 히어로 3840x1240: 글자 없음. 스팀이 왼쪽 아래에 로고를 얹으므로 장면은 가운데~오른쪽."""
    c = background(W, H, horizon=0.8, sun=(0.75, 0.15))
    punch_scene(c, H * 0.9, H * 0.46, W * 0.46)
    decorated_cursor(c, W * 0.36, H * 0.42, H * 0.09)
    return c


def page_background(W, H):
    """스토어 페이지 배경 1438x810: 스팀이 어둡게 깔아 쓰는 곳 - 장면 없이 하늘·언덕만, 흐리게."""
    c = background(W, H, horizon=0.7, sun=(0.7, 0.2))
    return c.filter(ImageFilter.GaussianBlur(W * 0.004))


def community_icon(S):
    """커뮤니티 아이콘 184x184: 원숭이 얼굴."""
    c = background(S, S, horizon=1.2, sun=(0.7, 0.2))
    m, _ = monkey(S * 1.9, index=0)
    c.alpha_composite(m, (round(S * 0.5 - m.width * 0.52), round(-S * 0.08)))
    return c


def library_logo(W, H):
    """라이브러리 로고 1280x720 투명 PNG: 히어로 위에 얹힌다."""
    c = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    lg = logo(round(W * 0.96), lines=2)
    if lg.height > H:
        lg = scaled(lg, height=H)
    place_logo(c, lg, W / 2, H / 2)
    return c


SPECS = [
    # 이름, 크기, 만드는 함수, 투명 여부
    ("header_capsule", (920, 430), lambda: wide(920, 430), False),
    ("small_capsule", (462, 174), lambda: small(462, 174), False),
    ("main_capsule", (1232, 706), lambda: wide(1232, 706, logo_w=0.44), False),
    ("vertical_capsule", (748, 896), lambda: tall(748, 896), False),
    ("page_background", (1438, 810), lambda: page_background(1438, 810), False),
    ("library_capsule", (600, 900), lambda: tall(600, 900), False),
    ("library_header", (920, 430), lambda: wide(920, 430), False),
    ("library_hero", (3840, 1240), lambda: hero(3840, 1240), False),
    ("library_logo", (1280, 720), lambda: library_logo(1280, 720), True),
    ("community_icon", (184, 184), lambda: community_icon(184), False),
]


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, size, make, transparent in SPECS:
        im = make()
        assert im.size == size, (name, im.size, size)
        if transparent:
            im.save(os.path.join(OUT, f"{name}.png"))
        else:
            im.convert("RGB").save(os.path.join(OUT, f"{name}.png"))
            im.convert("RGB").save(os.path.join(OUT, f"{name}.jpg"), quality=92)
        print(f"{name:18s} {size[0]}x{size[1]}")


if __name__ == "__main__":
    main()
