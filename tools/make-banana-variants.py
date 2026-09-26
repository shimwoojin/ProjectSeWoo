"""B17 — 커서 바나나 변형을 기본 바나나 한 장에서 만든다 (docs/B17-CURSOR-REWORK.md §6-3).

    "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-banana-variants.py

바나나는 **모양·위치가 고정**이다 - 원숭이가 어느 바나나에나 똑같이 매달려야 해서, 변형은 실루엣이 같아야 한다.
그래서 그림을 새로 뽑지 않고 기본 바나나(본편 나무의 assets/entities/banana_01.png)의 **껍질 색만** 바꾼다.
껍질은 "노란 색조 + 채도 있음" 픽셀로 고르고, 꼭지·외곽선·그림자는 그대로 둔다. 명암(V)을 유지해서 입체감이 산다.

출력 (아이템마다): assets/cursor/banana/<id>/banana.png (원본 크기 - 커서 레이어가 쓴다), icon.png (128x128 - 상점·도감).
목록과 이름은 game/shop/items.json. 여기 없는 id 는 만들지 않는다.
"""

import colorsys
import json
import pathlib

import numpy as np
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parents[1]
BASE = ROOT / "assets" / "entities" / "banana_01.png"
OUT = ROOT / "assets" / "cursor" / "banana"
ITEMS = ROOT / "game" / "shop" / "items.json"
ICON = 128


def peel_mask(rgb):
    """노란 껍질: 색조 30~58도(꼭지의 올리브색은 60도 넘는다), 채도 0.3 이상, 밝기 0.35 이상. 꼭지(초록·갈색)와 외곽선(어두움)은 빠진다."""
    hsv = np.array([colorsys.rgb_to_hsv(*(px / 255.0)) for px in rgb.reshape(-1, 3)]).reshape(rgb.shape)
    h, s, v = hsv[..., 0] * 360, hsv[..., 1], hsv[..., 2]
    return (h >= 30) & (h <= 58) & (s >= 0.3) & (v >= 0.35), hsv


def recolor(img, hue, sat_mul=1.0, val_mul=1.0, val_add=0.0, where=None):
    """껍질(또는 where) 픽셀의 색조를 hue(도)로 바꾼다. 명암은 V 를 곱·더해서 조절한다."""
    arr = np.array(img).astype(np.float32)
    rgb = arr[..., :3]
    mask, hsv = peel_mask(rgb.astype(np.uint8))
    if where is not None:
        mask &= where
    out = rgb.copy()
    for y, x in zip(*np.nonzero(mask)):
        _, s, v = hsv[y, x]
        r, g, b = colorsys.hsv_to_rgb(hue / 360.0, min(1.0, s * sat_mul), min(1.0, max(0.0, v * val_mul + val_add)))
        out[y, x] = (r * 255, g * 255, b * 255)
    arr[..., :3] = out
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def lower_part(img, fraction):
    """아래쪽 fraction 만 True - 초코 코팅처럼 끝부분만 칠할 때. 경계는 물결로."""
    w, h = img.size
    a = np.array(img.getchannel("A")) > 0
    ys = np.nonzero(a.any(axis=1))[0]
    top, bottom = ys[0], ys[-1]
    edge = bottom - (bottom - top) * fraction
    yy, xx = np.mgrid[0:h, 0:w]
    return yy > edge + np.sin(xx / w * np.pi * 6) * h * 0.02


def sparkles(img, points, size):
    """다이아 바나나의 반짝임 - 네 꼭지 별."""
    img = img.copy()
    d = ImageDraw.Draw(img)
    w, h = img.size
    for fx, fy, k in points:
        cx, cy, r = fx * w, fy * h, size * k
        d.polygon([(cx, cy - r), (cx + r * 0.22, cy - r * 0.22), (cx + r, cy), (cx + r * 0.22, cy + r * 0.22),
                   (cx, cy + r), (cx - r * 0.22, cy + r * 0.22), (cx - r, cy), (cx - r * 0.22, cy - r * 0.22)],
                  fill=(255, 255, 255, 235))
    return img


VARIANTS = {
    "banana_01": lambda b: b,
    "banana_02": lambda b: recolor(b, 95, sat_mul=0.85, val_mul=0.92),                                   # 풋바나나
    "banana_03": lambda b: recolor(b, 22, sat_mul=0.8, val_mul=0.42, where=lower_part(b, 0.55)),         # 초코
    "banana_04": lambda b: recolor(b, 342, sat_mul=0.62, val_mul=1.0, val_add=0.04),                     # 딸기
    "banana_05": lambda b: recolor(b, 215, sat_mul=0.1, val_mul=0.9, val_add=0.0),                    # 은
    "banana_06": lambda b: sparkles(recolor(b, 188, sat_mul=0.45, val_mul=1.05, val_add=0.08),           # 다이아
                                    [(0.30, 0.45, 1.0), (0.62, 0.62, 0.7), (0.48, 0.30, 0.55)], 14),
}


def icon(img):
    box = img.getchannel("A").getbbox()
    img = img.crop(box)
    k = (ICON - 8) / max(img.size)
    img = img.resize((round(img.width * k), round(img.height * k)), Image.LANCZOS)
    out = Image.new("RGBA", (ICON, ICON), (0, 0, 0, 0))
    out.alpha_composite(img, ((ICON - img.width) // 2, (ICON - img.height) // 2))
    return out


def main():
    ids = [i["id"] for i in json.loads(ITEMS.read_text(encoding="utf-8"))["items"] if i["category"] == "banana"]
    base = Image.open(BASE).convert("RGBA")
    for item_id in ids:
        make = VARIANTS.get(item_id)
        if make is None:
            print(f"{item_id}: 변형 규칙이 없다 - VARIANTS 에 추가할 것")
            continue
        img = make(base)
        folder = OUT / item_id
        folder.mkdir(parents=True, exist_ok=True)
        img.save(folder / "banana.png", optimize=True)
        icon(img).save(folder / "icon.png", optimize=True)
        print(f"{item_id}: {img.size[0]}x{img.size[1]}")


if __name__ == "__main__":
    main()
