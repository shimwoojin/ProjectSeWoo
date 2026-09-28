"""B17 — 커서 바나나 변형을 기본 바나나 한 장에서 만든다 (docs/B17-CURSOR-REWORK.md §6-3).

    "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-banana-variants.py

바나나는 **모양·위치가 고정**이다 - 원숭이가 어느 바나나에나 똑같이 매달려야 해서, 변형은 실루엣이 같아야 한다.
그래서 그림을 새로 뽑지 않고 기본 바나나(본편 나무의 assets/entities/banana_01.png)의 **껍질 색만** 바꾼다.
껍질은 "노란 색조 + 채도 있음" 픽셀로 고르고, 꼭지·외곽선·그림자는 그대로 둔다. 명암(V)을 유지해서 입체감이 산다.

출력 (아이템마다): assets/cursor/banana/<id>/banana.png (원본 크기 - 커서 레이어가 쓴다), icon.png (128x128 - 상점·도감).
목록과 이름은 game/shop/items.json. 여기 없는 id 는 만들지 않는다.

**값은 껍질에 한 일로 매긴다** (B19, 2026-09-28 - 원숭이와 같은 생각): 색만 15 · 재질·무늬 200 · 특수 효과 500.
무늬(호피·줄무늬·스프링클·은하·용암)는 시드를 고정한 난수로 그려서 다시 돌려도 같은 그림이 나온다.
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


def peel_only(img):
    """껍질 마스크 (bool, 이미지 크기)."""
    return peel_mask(np.array(img)[..., :3])[0] & (np.array(img)[..., 3] > 0)


def paint(img, mask, rgb, keep_shade=True):
    """mask 자리를 rgb 로. keep_shade 면 원래 밝기(V)를 곱해 입체감을 남긴다."""
    arr = np.array(img).astype(np.float32)
    col = np.array(rgb, np.float32)[None, None, :]
    if keep_shade:
        v = arr[..., :3].max(-1, keepdims=True) / 255.0
        col = col * np.clip(v / 0.85, 0.35, 1.15)
    arr[..., :3] = np.where(mask[..., None], np.clip(col, 0, 255), arr[..., :3])
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def spots(img, color, count, rmin, rmax, seed, ring=None, peel=None):
    """껍질 위 얼룩 (호피). ring 이 있으면 얼룩 테두리 색. peel = 껍질 자리 (색을 이미 바꿨으면 원본에서 잰 것을 넘긴다)."""
    rng = np.random.default_rng(seed)
    peel = peel_only(img) if peel is None else peel
    ys, xs = np.nonzero(peel)
    h, w = peel.shape
    yy, xx = np.mgrid[0:h, 0:w]
    out = img
    for _ in range(count):
        i = rng.integers(len(xs))
        cx, cy = xs[i], ys[i]
        rx, ry = rng.uniform(rmin, rmax), rng.uniform(rmin, rmax) * 0.8
        blob = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2
        if ring is not None:
            out = paint(out, (blob < 1.0) & peel, ring)
            out = paint(out, (blob < 0.45) & peel, color)
        else:
            out = paint(out, (blob < 1.0) & peel, color)
    return out


def stripes(img, colors, period, angle_deg):
    """껍질에 비스듬한 줄무늬 (캔디)."""
    peel = peel_only(img)
    h, w = peel.shape
    yy, xx = np.mgrid[0:h, 0:w]
    t = np.radians(angle_deg)
    band = (np.floor((xx * np.cos(t) + yy * np.sin(t)) / period).astype(int)) % len(colors)
    out = img
    for k, c in enumerate(colors):
        out = paint(out, peel & (band == k), c)
    return out


def sprinkles(img, where, seed, count=70):
    """초코 위 알록달록 스프링클 - 짧은 둥근 막대. 껍질(where) 밖으로 나간 끝은 자른다 - 외곽선 밖으로 샜다."""
    rng = np.random.default_rng(seed)
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    ys, xs = np.nonzero(where)
    palette = [(255, 92, 120), (80, 190, 255), (255, 220, 70), (130, 230, 120), (255, 255, 255), (200, 120, 255)]
    for _ in range(count):
        i = rng.integers(len(xs))
        x, y = xs[i], ys[i]
        a = rng.uniform(0, np.pi)
        dx, dy = np.cos(a) * 4, np.sin(a) * 4
        d.line((x - dx, y - dy, x + dx, y + dy), fill=palette[rng.integers(len(palette))] + (255,), width=3)
    la = np.array(layer)
    la[..., 3] = np.where(where, la[..., 3], 0)
    img = img.copy()
    img.alpha_composite(Image.fromarray(la, "RGBA"))
    return img


def metal(img, stops):
    """금속 - 껍질 밝기를 흐리게 편 뒤(0~1) 색 단계(stops, 어두운 -> 밝은)에 대응시킨다. 기본 바나나는 거의 다 밝아서 색만 바꾼 금은
    기본 노랑과 구분이 안 됐고, 밝기를 픽셀마다 펴면 껍질 결이 모래처럼 드러났다 (2026-09-28) - 그래서 먼저 흐린다."""
    from scipy import ndimage
    arr = np.array(img).astype(np.float32)
    peel = peel_only(img)
    v = arr[..., :3].max(-1) / 255.0
    vb = ndimage.gaussian_filter(np.where(peel, v, v[peel].mean()), 3)
    lo, hi = np.percentile(vb[peel], 5), np.percentile(vb[peel], 95)
    t = np.clip((vb - lo) / max(1e-3, hi - lo), 0, 1)
    stops = np.array(stops, np.float32)
    pos = t * (len(stops) - 1)
    i = np.clip(np.floor(pos).astype(int), 0, len(stops) - 2)
    f = (pos - i)[..., None]
    col = stops[i] * (1 - f) + stops[i + 1] * f
    arr[..., :3] = np.where(peel[..., None], col, arr[..., :3])
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def rainbow(img):
    """껍질을 가로 위치에 따라 무지개 색조로. 원래 명암은 살린다."""
    arr = np.array(img).astype(np.float32)
    peel = peel_only(img)
    h, w = peel.shape
    xs = np.nonzero(peel)[1]
    x0, x1 = xs.min(), xs.max()
    hue = np.clip((np.arange(w) - x0) / max(1, x1 - x0), 0, 1) * 300.0          # 빨강 -> 보라
    out = arr.copy()
    for y, x in zip(*np.nonzero(peel)):
        _, s_, v = colorsys.rgb_to_hsv(*(arr[y, x, :3] / 255.0))
        r, g, b = colorsys.hsv_to_rgb(hue[x] / 360.0, 0.78, min(1.0, v * 1.05))
        out[y, x, :3] = (r * 255, g * 255, b * 255)
    return Image.fromarray(out.astype(np.uint8), "RGBA")


def galaxy(img, seed):
    """짙은 남보라 껍질 + 분홍 성운 얼룩 + 흰 별 점."""
    base = recolor(img, 255, sat_mul=0.9, val_mul=0.45)
    base = spots(base, (150, 70, 170), 7, 10, 20, seed, peel=peel_only(img))
    rng = np.random.default_rng(seed + 1)
    peel = peel_only(img)
    ys, xs = np.nonzero(peel)
    d = ImageDraw.Draw(base)
    for _ in range(45):
        i = rng.integers(len(xs))
        r = rng.choice([0.8, 1.2, 1.8])
        d.ellipse((xs[i] - r, ys[i] - r, xs[i] + r, ys[i] + r), fill=(255, 255, 240, 255))
    return sparkles(base, [(0.35, 0.55, 0.8), (0.62, 0.42, 0.6)], 12)


def lava(img, seed):
    """숯처럼 검은 껍질에 빛나는 주황 금 - 금은 랜덤 워크 선."""
    base = recolor(img, 12, sat_mul=0.4, val_mul=0.32)
    rng = np.random.default_rng(seed)
    peel = peel_only(img)
    ys, xs = np.nonzero(peel)
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(glow)
    for _ in range(9):
        i = rng.integers(len(xs))
        x, y = float(xs[i]), float(ys[i])
        a = rng.uniform(0, 2 * np.pi)
        pts = [(x, y)]
        for _ in range(6):
            a += rng.uniform(-0.9, 0.9)
            x, y = x + np.cos(a) * 7, y + np.sin(a) * 7
            pts.append((x, y))
        d.line(pts, fill=(255, 120, 20, 255), width=5)
        d.line(pts, fill=(255, 230, 90, 255), width=2)
    ga = np.array(glow)
    ga[..., 3] = np.where(peel, ga[..., 3], 0)                                  # 껍질 밖(외곽선·꼭지)으로 안 번지게
    base.alpha_composite(Image.fromarray(ga, "RGBA"))
    return base


VARIANTS = {
    "banana_01": lambda b: b,
    "banana_02": lambda b: recolor(b, 95, sat_mul=0.85, val_mul=0.92),                                   # 풋바나나
    "banana_03": lambda b: recolor(b, 22, sat_mul=0.8, val_mul=0.42, where=lower_part(b, 0.55)),         # 초코
    "banana_04": lambda b: recolor(b, 342, sat_mul=0.62, val_mul=1.0, val_add=0.04),                     # 딸기
    "banana_05": lambda b: recolor(b, 215, sat_mul=0.1, val_mul=0.9, val_add=0.0),                    # 은
    "banana_06": lambda b: sparkles(recolor(b, 188, sat_mul=0.45, val_mul=1.05, val_add=0.08),           # 다이아
                                    [(0.30, 0.45, 1.0), (0.62, 0.62, 0.7), (0.48, 0.30, 0.55)], 14),
    # --- 2026-09-28 B19 추가: 색만 (15) ---
    "banana_07": lambda b: recolor(b, 228, sat_mul=0.7, val_mul=0.85),                                  # 블루베리
    "banana_08": lambda b: recolor(b, 16, sat_mul=0.55, val_mul=1.0, val_add=0.08),                      # 복숭아
    # --- 재질·무늬 (200) ---
    "banana_09": lambda b: sparkles(metal(b, [(128, 72, 8), (205, 135, 18), (242, 190, 48), (255, 232, 140)]),  # 금
                                    [(0.30, 0.48, 0.9), (0.60, 0.40, 0.6)], 13),
    "banana_10": lambda b: spots(recolor(b, 36, sat_mul=1.05, val_mul=0.95), (150, 90, 30), 30, 4, 7, 101,  # 호피
                                 ring=(45, 26, 14)),
    "banana_11": lambda b: stripes(b, [(240, 60, 70), (250, 248, 245)], 11, 40),                        # 캔디
    "banana_12": lambda b: sprinkles(recolor(b, 22, sat_mul=0.8, val_mul=0.42, where=lower_part(b, 0.6)),  # 스프링클
                                     lower_part(b, 0.6) & peel_only(b), 202),
    # --- 특수 효과 (500) ---
    "banana_13": lambda b: rainbow(b),                                                                   # 무지개
    "banana_14": lambda b: galaxy(b, 303),                                                               # 은하
    "banana_15": lambda b: lava(b, 404),                                                                 # 용암
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
