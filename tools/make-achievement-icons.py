"""A15 — 스팀 도전과제 아이콘 20장(달성 10 + 미달성 10)을 게임 스프라이트로 만든다.

    "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-achievement-icons.py

시스템 파이썬에는 PIL 이 없다 - 에셋 파이프라인(tools/build-assets.ps1)과 같은 ComfyUI venv 로 돌린다.
출력: assets/_store/achievements/<API_NAME>.png / .jpg, <API_NAME>_locked.png / .jpg (256x256).
그 폴더는 .gdignore 로 Godot 임포트에서 빠진다 - 게임이 아니라 스팀에 올리는 소재다.

API Name 목록은 shared/Contracts/IAchievements.cs 의 AchievementIds.All 과 같아야 한다
(docs/A15-ACHIEVEMENTS.md). 스팀 업로드 화면의 규격이 다르면 SIZE 만 바꿔 다시 뽑는다.
"""

import os
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets", "_store", "achievements")
SIZE = 256
FONT = os.path.join(ROOT, "assets", "fonts", "Pretendard-Bold.ttf")


def asset(*parts):
    return Image.open(os.path.join(ROOT, "assets", *parts)).convert("RGBA")


def punch_frame(index=2):
    """원숭이 펀치 시트(8칸 x 298x324)에서 한 칸. 2 = 왼손을 뻗은 프레임."""
    sheet = asset("entities", "monkey_punch.png")
    return sheet.crop((index * 298, 0, (index + 1) * 298, 324))


def trim(im):
    box = im.getchannel("A").getbbox()
    return im.crop(box) if box else im


def fit(im, max_side):
    im = trim(im)
    scale = max_side / max(im.size)
    return im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)


def gradient(top, bottom):
    """위->아래 세로 그라데이션 + 가장자리를 살짝 어둡게."""
    bg = Image.new("RGB", (SIZE, SIZE))
    px = bg.load()
    for y in range(SIZE):
        t = y / (SIZE - 1)
        c = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        for x in range(SIZE):
            px[x, y] = c
    vignette = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(vignette).ellipse((-SIZE * 0.25, -SIZE * 0.25, SIZE * 1.25, SIZE * 1.25), fill=255)
    vignette = vignette.filter(ImageFilter.GaussianBlur(SIZE * 0.12))
    dark = Image.new("RGB", (SIZE, SIZE), (0, 0, 0))
    return Image.composite(bg, dark, vignette).convert("RGBA")


def paste_with_shadow(canvas, sprite, center):
    x = round(center[0] - sprite.width / 2)
    y = round(center[1] - sprite.height / 2)
    shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    alpha = sprite.getchannel("A").point(lambda a: a * 0.55)
    shadow.paste((0, 0, 0, 255), (x + 4, y + 7), alpha)
    shadow = shadow.filter(ImageFilter.GaussianBlur(5))
    canvas.alpha_composite(shadow)
    canvas.alpha_composite(sprite, (x, y))


def frame(canvas, color, width=10):
    """안쪽 둥근 테두리. 누적 타수 단계(동/은/금/다이아)를 이 색으로 가른다."""
    d = ImageDraw.Draw(canvas)
    m = width // 2 + 4
    d.rounded_rectangle((m, m, SIZE - m, SIZE - m), radius=28, outline=color, width=width)
    hi = tuple(min(255, c + 70) for c in color[:3])
    d.rounded_rectangle((m + width // 2 + 1, m + width // 2 + 1, SIZE - m - width // 2 - 1, SIZE - m - width // 2 - 1),
                        radius=22, outline=hi + (120,), width=2)


def badge(canvas, text, fill):
    """아래쪽 알약 모양 숫자 배지."""
    d = ImageDraw.Draw(canvas)
    font = ImageFont.truetype(FONT, 46 if len(text) <= 3 else 40)
    w = d.textlength(text, font=font)
    pad_x, h = 18, 58
    x0 = (SIZE - w) / 2 - pad_x
    y0 = SIZE - h - 22
    d.rounded_rectangle((x0, y0, x0 + w + pad_x * 2, y0 + h), radius=h // 2, fill=fill, outline=(0, 0, 0, 160), width=3)
    d.text((SIZE / 2, y0 + h / 2 + 1), text, font=font, fill=(255, 255, 255), anchor="mm",
           stroke_width=3, stroke_fill=(40, 25, 10))


BRONZE = (196, 124, 62, 255)
SILVER = (196, 204, 214, 255)
GOLD = (240, 196, 64, 255)
DIAMOND = (120, 220, 255, 255)


def keystrokes(label, ring, top, bottom):
    def draw():
        c = gradient(top, bottom)
        paste_with_shadow(c, fit(punch_frame(), 176), (SIZE / 2, SIZE / 2 - 18))
        frame(c, ring)
        badge(c, label, ring[:3] + (235,))
        return c
    return draw


def single(sprite_fn, top, bottom, ring, size=178, extra=None):
    def draw():
        c = gradient(top, bottom)
        if extra:
            extra(c)
        paste_with_shadow(c, fit(sprite_fn(), size), (SIZE / 2, SIZE / 2 + 4))
        frame(c, ring)
        return c
    return draw


def first_purchase():
    c = gradient((74, 150, 86), (24, 62, 34))
    paste_with_shadow(c, fit(asset("cursor", "hang", "monkey_02.png"), 170), (SIZE / 2 - 10, SIZE / 2 - 6))
    paste_with_shadow(c, fit(asset("entities", "banana_04.png"), 92), (SIZE - 74, SIZE - 70))
    frame(c, (150, 220, 120, 255))
    return c


def slot_trail():
    c = gradient((48, 150, 150), (14, 58, 66))
    paste_with_shadow(c, fit(asset("cursor", "trail", "leaf_02.png"), 118), (SIZE / 2 - 34, SIZE / 2 + 20))
    paste_with_shadow(c, fit(asset("cursor", "trail", "spark_02.png"), 130), (SIZE / 2 + 30, SIZE / 2 - 24))
    frame(c, (110, 225, 215, 255))
    return c


def slot_base():
    c = gradient((150, 110, 60), (60, 38, 18))
    paste_with_shadow(c, fit(asset("cursor", "base", "halo_01.png"), 196), (SIZE / 2, SIZE / 2 + 8))
    paste_with_shadow(c, fit(asset("cursor", "base", "ring_02.png"), 120), (SIZE / 2, SIZE / 2 + 8))
    frame(c, (230, 180, 110, 255))
    return c


def collection():
    c = gradient((206, 160, 40), (92, 58, 8))
    paste_with_shadow(c, fit(asset("entities", "tree_full.png"), 196), (SIZE / 2, SIZE / 2 + 6))
    frame(c, GOLD, width=12)
    badge(c, "16/16", (200, 140, 20, 235))
    return c


def room():
    c = gradient((64, 110, 200), (18, 34, 84))
    paste_with_shadow(c, fit(asset("cursor", "hang", "monkey_03.png"), 150), (SIZE / 2 - 42, SIZE / 2 + 8))
    paste_with_shadow(c, ImageOps.mirror(fit(asset("cursor", "hang", "monkey_02.png"), 150)), (SIZE / 2 + 42, SIZE / 2 + 8))
    frame(c, (140, 180, 255, 255))
    return c


ICONS = {
    "ACH_KEYSTROKES_1K": keystrokes("1K", BRONZE, (190, 120, 60), (80, 40, 16)),
    "ACH_KEYSTROKES_10K": keystrokes("10K", SILVER, (120, 132, 150), (40, 46, 58)),
    "ACH_KEYSTROKES_100K": keystrokes("100K", GOLD, (220, 170, 50), (96, 60, 8)),
    "ACH_KEYSTROKES_1M": keystrokes("1M", DIAMOND, (70, 150, 210), (16, 40, 80)),
    "ACH_FIRST_PURCHASE": first_purchase,
    "ACH_SLOT_HANG": single(lambda: asset("cursor", "hang", "monkey_06.png"),
                            (130, 80, 170), (46, 22, 70), (200, 150, 240, 255)),
    "ACH_SLOT_TRAIL": slot_trail,
    "ACH_SLOT_BASE": slot_base,
    "ACH_COLLECTION_100": collection,
    "ACH_ROOM_FIRST_JOIN": room,
}


def locked(im):
    """미달성: 흑백 + 어둡게 + 대비를 낮춘다. 모양은 알아볼 수 있게 남긴다."""
    gray = ImageOps.grayscale(im.convert("RGB")).convert("RGB")
    gray = ImageEnhance.Brightness(gray).enhance(0.55)
    return ImageEnhance.Contrast(gray).enhance(0.8)


def main():
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, ".gdignore"), "w", encoding="utf-8"):
        pass

    for name, draw in ICONS.items():
        rgb = draw().convert("RGB")
        rgb.save(os.path.join(OUT, f"{name}.png"))
        rgb.save(os.path.join(OUT, f"{name}.jpg"), quality=92)
        lk = locked(rgb)
        lk.save(os.path.join(OUT, f"{name}_locked.png"))
        lk.save(os.path.join(OUT, f"{name}_locked.jpg"), quality=92)
        print(f"{name}  ok")

    # 한눈에 보는 미리보기 (저장소에는 안 넣는다 - 필요할 때 다시 뽑는다)
    names = list(ICONS)
    sheet = Image.new("RGB", (SIZE * 5 + 60, SIZE * 4 + 50), (30, 30, 34))
    for i, name in enumerate(names):
        col, row = i % 5, (i // 5) * 2
        sheet.paste(Image.open(os.path.join(OUT, f"{name}.png")), (10 + col * (SIZE + 10), 10 + row * (SIZE + 10)))
        sheet.paste(Image.open(os.path.join(OUT, f"{name}_locked.png")), (10 + col * (SIZE + 10), 10 + (row + 1) * (SIZE + 10)))
    preview = os.environ.get("ICON_PREVIEW")
    if preview:
        sheet.save(preview)
        print(f"preview -> {preview}")


if __name__ == "__main__":
    main()
