"""B18 — 본편 원숭이 스킨 시트 + 나무 단계 그림을 만든다 (docs/B18-BODY-SKINS.md).

    "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-body-skins.py

**본편 원숭이 = 장착한 커서 원숭이 스킨의 색.** 새 아이템을 만들지 않는다 - 커서 원숭이를 사면 나무를 치는
원숭이도 같이 바뀐다. 색 규칙은 커서 스킨과 같은 표(make-monkey-parts.py 의 SKINS)와 같은 털 마스크를 쓴다 -
두 원숭이가 다른 색이 되면 안 된다. 원판 8프레임을 **색만 바꿔서** 만드는 이유: 프레임을 새로 생성하면 프레임
사이 일관성이 깨진다(B17 §1-1). 모자(monkey_05)는 커서 머리에 그린 모자를 프레임마다 머리 자리를 찾아 붙인다.

  assets/cursor/monkey/<id>/punch.png   monkey_punch.png 와 같은 크기·같은 칸 (monkey_01 은 만들지 않는다 - 원판)

**나무 단계 = 강화 레벨 합.** 원판(tree_empty.png)에 색·덧그림만 더한다 - 크기·줄기·왕관 자리가 그대로라야
Tree.cs 의 송이 자리표(SlotAnchors, 텍스처 픽셀 좌표)가 모든 단계에 맞는다.

  assets/entities/tree_stage_1.png   무성한 나무 - 잎이 짙어지고 밑동에 덤불
  assets/entities/tree_stage_2.png   꽃 핀 나무   - 1 + 왕관에 꽃
  assets/entities/tree_stage_3.png   황금 나무    - 잎 끝이 금빛, 꽃, 반짝임
"""

import colorsys
import importlib.util
import json
import math
import pathlib

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = pathlib.Path(__file__).resolve().parents[1]
ENT = ROOT / "assets" / "entities"
SKIN_OUT = ROOT / "assets" / "cursor" / "monkey"
ITEMS = ROOT / "game" / "shop" / "items.json"

_spec = importlib.util.spec_from_file_location("parts", ROOT / "tools" / "make-monkey-parts.py")
parts = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(parts)

OUTLINE = parts.OUTLINE
CAP_DROP = 14   # 모자를 머리 자리보다 이만큼 아래로 (px)


# ---------------------------------------------------------------------------------------------- 원숭이

def hsv_of(rgb):
    """colorsys 를 픽셀마다 부르면 시트 한 장에 수십 초라 numpy 로 한다. 값 범위는 colorsys 와 같다(h 0~1)."""
    x = rgb.astype(np.float32) / 255.0
    r, g, b = x[..., 0], x[..., 1], x[..., 2]
    mx, mn = x.max(-1), x.min(-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-6
    rm, gm = nz & (mx == r), nz & (mx == g) & (mx != r)
    bm = nz & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6
    h[gm] = (b - r)[gm] / d[gm] + 2
    h[bm] = (r - g)[bm] / d[bm] + 4
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return np.stack([h / 6.0, s, mx], -1)


def rgb_of(hsv):
    h, s, v = hsv[..., 0] * 6.0, hsv[..., 1], hsv[..., 2]
    i = np.floor(h).astype(int) % 6
    f = h - np.floor(h)
    p, q, t = v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f))
    out = np.choose(i[..., None].repeat(3, -1), [
        np.stack([v, t, p], -1), np.stack([q, v, p], -1), np.stack([p, v, t], -1),
        np.stack([p, q, v], -1), np.stack([t, p, v], -1), np.stack([v, p, q], -1)])
    return np.clip(out * 255 + 0.5, 0, 255).astype(np.uint8)


def fur_mask(rgb):
    """make-monkey-parts.fur_mask 와 같은 규칙 (numpy 판). 살색 얼굴·배·손발·귀 안쪽과 외곽선은 빠진다."""
    hsv = hsv_of(rgb)
    h, s, v = hsv[..., 0] * 360, hsv[..., 1], hsv[..., 2]
    fur = (h >= 14) & (h <= 40) & (s >= 0.45) & (v >= 0.3) & (v <= 0.86)
    light = ((v >= 0.8) & (s <= 0.55)) | (((h <= 14) | (h >= 340)) & (v >= 0.55))
    face = ndimage.binary_fill_holes(ndimage.binary_closing(light, iterations=2))
    return fur & ~ndimage.binary_dilation(face, iterations=1), hsv


def recolor(img, hue, sat_mul, val_add):
    a = np.array(img)
    mask, hsv = fur_mask(a[..., :3])
    mask &= a[..., 3] > 0
    new = hsv.copy()
    if hue is not None:
        new[..., 0] = hue / 360.0
    new[..., 1] = np.minimum(1.0, hsv[..., 1] * sat_mul)
    new[..., 2] = np.clip(hsv[..., 2] + val_add, 0, 1)
    a[..., :3] = np.where(mask[..., None], rgb_of(new), a[..., :3])
    return Image.fromarray(a, "RGBA")


def head_offset(frame_alpha, head_alpha, rest_box):
    """대기 프레임의 머리(rest_box 자리)를 이 프레임에서 가장 잘 겹치는 자리로 옮기는 (dx, dy). 펀치 때 몸이 기운다."""
    hh, hw = head_alpha.shape
    best, best_score = (0, 0), -1
    for dy in range(-30, 31, 2):
        for dx in range(-50, 51, 2):
            x0, y0 = rest_box[0] + dx, rest_box[1] + dy
            if x0 < 0 or y0 < 0 or x0 + hw > frame_alpha.shape[1] or y0 + hh > frame_alpha.shape[0]:
                continue
            win = frame_alpha[y0:y0 + hh, x0:x0 + hw]
            score = np.count_nonzero(win & head_alpha) - np.count_nonzero(win ^ head_alpha) * 0.5
            if score > best_score:
                best, best_score = (dx, dy), score
    return best


def monkey_sheets():
    sheet = Image.open(parts.SHEET).convert("RGBA")
    cw, ch = parts.CELL
    frames = sheet.width // cw
    head, box = parts.head_from_sheet()
    head_alpha = np.array(head)[..., 3] > 8
    cap_layer = parts.draw_cap(Image.new("RGBA", head.size, (0, 0, 0, 0)))   # 모자만 (머리 없이)

    offsets = []
    for f in range(frames):
        fa = np.array(sheet.crop((f * cw, 0, (f + 1) * cw, ch)))[..., 3] > 8
        offsets.append(head_offset(fa, head_alpha, box))

    ids = [i["id"] for i in json.loads(ITEMS.read_text(encoding="utf-8"))["items"] if i["category"] == "monkey"]
    for item_id in ids:
        rule = parts.SKINS.get(item_id)
        if rule is None:
            print(f"{item_id}: 스킨 규칙이 없다 - make-monkey-parts.py 의 SKINS 에 추가할 것")
            continue
        hue, sat_mul, val_add, accessory = rule
        if hue is None and sat_mul == 1.0 and val_add == 0.0 and accessory is None:
            continue                                   # 원판 그대로 - 게임이 monkey_punch.png 를 쓴다
        out = recolor(sheet, hue, sat_mul, val_add)
        if accessory == "cap":
            for f, (dx, dy) in enumerate(offsets):
                # 커서 머리는 정수리 털이 짧게 잘려 모자가 딱 맞지만, 본편은 털이 위로 뻗어 있어서 그대로 붙이면 모자가
                # 떠 보였다(첫 시안). CAP_DROP 만큼 내려 이마에 씌운다.
                out.alpha_composite(cap_layer, (f * cw + box[0] + dx, box[1] + dy + CAP_DROP))
        path = SKIN_OUT / item_id / "punch.png"
        out.save(path, optimize=True)
        print(f"{item_id}: punch.png {out.size[0]}x{out.size[1]}")
    print("머리 자리 (프레임별 dx,dy):", offsets)


# ---------------------------------------------------------------------------------------------- 나무

# 송이 자리 (Tree.cs SlotAnchors, 텍스처 픽셀) - 꽃·반짝임이 송이를 가리지 않게 여기서 떨어뜨린다.
ANCHORS = [(160, 268), (375, 268), (267, 205), (75, 240), (460, 240), (190, 165), (345, 165), (267, 110)]

# 꽃 자리 - 왕관 잎 위, 송이 자리에서 떨어진 곳 (2026-09-28 tree_empty.png 를 보고 잡았다)
FLOWERS = [(118, 118), (410, 120), (245, 48), (330, 70), (40, 200), (500, 196), (60, 318), (478, 318), (160, 212), (382, 212)]
SPARKLES = [(95, 80), (450, 70), (300, 20), (20, 260), (515, 270), (205, 95)]


def leaf_mask(a):
    hsv = hsv_of(a[..., :3])
    h, s, v = hsv[..., 0] * 360, hsv[..., 1], hsv[..., 2]
    return (h >= 55) & (h <= 170) & (s >= 0.25) & (v >= 0.25) & (a[..., 3] > 0), hsv


def lush(img):
    """잎을 조금 더 짙고 진하게."""
    a = np.array(img)
    m, hsv = leaf_mask(a)
    new = hsv.copy()
    new[..., 0] = np.where(m, hsv[..., 0] + 14 / 360, hsv[..., 0])        # 연두 → 초록
    new[..., 1] = np.minimum(1, hsv[..., 1] * 1.25)
    new[..., 2] = np.clip(hsv[..., 2] - 0.07, 0, 1)
    a[..., :3] = np.where(m[..., None], rgb_of(new), a[..., :3])
    return Image.fromarray(a, "RGBA")


def golden(img):
    """잎을 통째로 금빛(호박색 쪽)으로. 초록과 섞으면 올리브색으로 탁해져서(2026-09-28 첫 시안) 색조를 한 값으로 바꾼다.
    송이(노랑, 약 50도)와 구분되게 조금 붉은 쪽(40도). 36도·밝기 그대로는 가을 낙엽처럼 갈색으로 보였다(둘째 시안)."""
    a = np.array(img)
    m, hsv = leaf_mask(a)
    new = hsv.copy()
    new[..., 0] = 40 / 360
    new[..., 1] = np.clip(hsv[..., 1] * 0.95 + 0.08, 0, 1)
    new[..., 2] = np.clip(hsv[..., 2] * 1.08 + 0.04, 0, 1)
    a[..., :3] = np.where(m[..., None], rgb_of(new), a[..., :3])
    return Image.fromarray(a, "RGBA")


def bushes(img, colors=((70, 150, 45), (104, 182, 58)), light=(150, 214, 90)):
    """밑동 양옆에 둥근 덤불. 뾰족한 풀잎은 원판 그림체와 안 맞았다(첫 시안). 캔버스 안에서만 - 크기를 바꾸면 송이 자리가 틀어진다.
    외곽선을 전부 먼저, 채움을 나중에 - 덩어리끼리 이음매가 안 생긴다 (B17 §9-1 과 같은 이유)."""
    d = ImageDraw.Draw(img)
    h = img.size[1]
    blobs = [(118, h - 30, 30), (152, h - 44, 34), (188, h - 28, 26),
             (346, h - 28, 26), (384, h - 44, 34), (420, h - 30, 30)]
    for x, y, r in blobs:
        d.ellipse((x - r - 3, y - r - 3, x + r + 3, min(h - 1, y + r + 3)), fill=OUTLINE + (255,))
    for i, (x, y, r) in enumerate(blobs):
        d.ellipse((x - r, y - r, x + r, min(h - 4, y + r)), fill=colors[i % 2] + (255,))
        d.ellipse((x - r * 0.55, y - r * 0.75, x + r * 0.1, y - r * 0.2), fill=light + (255,))   # 윗면 빛
    return img


def flower(d, cx, cy, r, petal, center=(250, 206, 60)):
    for i in range(5):
        ang = math.radians(i * 72 - 90)
        px, py = cx + math.cos(ang) * r * 0.62, cy + math.sin(ang) * r * 0.62
        d.ellipse((px - r * 0.5, py - r * 0.5, px + r * 0.5, py + r * 0.5), fill=petal + (255,), outline=OUTLINE + (255,), width=3)
    d.ellipse((cx - r * 0.3, cy - r * 0.3, cx + r * 0.3, cy + r * 0.3), fill=center + (255,), outline=OUTLINE + (255,), width=3)


def flowers(img, petal):
    d = ImageDraw.Draw(img)
    for i, (x, y) in enumerate(FLOWERS):
        if min(math.dist((x, y), a) for a in ANCHORS) < 38:
            print(f"  꽃 ({x},{y}) 이 송이 자리에 너무 가깝다 - 건너뜀")
            continue
        flower(d, x, y, 17 if i % 3 else 21, petal)
    return img


def sparkles(img):
    d = ImageDraw.Draw(img)
    for x, y in SPARKLES:
        r = 13
        pts = [(x, y - r), (x + r * 0.28, y - r * 0.28), (x + r, y), (x + r * 0.28, y + r * 0.28),
               (x, y + r), (x - r * 0.28, y + r * 0.28), (x - r, y), (x - r * 0.28, y - r * 0.28)]
        d.polygon(pts, fill=(255, 244, 170, 255), outline=(150, 100, 20, 255), width=2)
    return img


def tree_stages():
    base = Image.open(ENT / "tree_empty.png").convert("RGBA")
    s1 = bushes(lush(base))
    s2 = flowers(s1.copy(), petal=(246, 140, 170))
    s3 = sparkles(flowers(bushes(golden(base), colors=((200, 140, 32), (232, 176, 56)), light=(255, 226, 130)), petal=(255, 250, 240)))
    for n, im in ((1, s1), (2, s2), (3, s3)):
        assert im.size == base.size
        im.save(ENT / f"tree_stage_{n}.png", optimize=True)
        print(f"tree_stage_{n}.png {im.size[0]}x{im.size[1]}")


if __name__ == "__main__":
    monkey_sheets()
    tree_stages()
