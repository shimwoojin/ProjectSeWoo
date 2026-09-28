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
import sys

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
sys.path.insert(0, str(ROOT / "tools"))
import monkey_skins as ms  # noqa: E402

OUTLINE = parts.OUTLINE


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


def region_offset(frame_alpha, template, box, rx=50, ry=30):
    """대기 칸의 한 부분(template, box 자리)이 이 칸에서 가장 잘 겹치는 (dx, dy). 펀치 때 몸이 기울고 옮겨 간다."""
    hh, hw = template.shape
    best, best_score = (0, 0), -1
    for dy in range(-ry, ry + 1, 2):
        for dx in range(-rx, rx + 1, 2):
            x0, y0 = box[0] + dx, box[1] + dy
            if x0 < 0 or y0 < 0 or x0 + hw > frame_alpha.shape[1] or y0 + hh > frame_alpha.shape[0]:
                continue
            win = frame_alpha[y0:y0 + hh, x0:x0 + hw]
            score = np.count_nonzero(win & template) - np.count_nonzero(win ^ template) * 0.5
            if score > best_score:
                best, best_score = (dx, dy), score
    return best


# 칸마다 주먹: (x, y, 모양, 각도). 원판 시트의 살색 덩어리를 재서 잡았다 (2026-09-28).
#   "side"  = 뻗은 주먹(2·5). 옆모습 글러브를 각도(0 = 오른쪽)로 눕힌다 - 손목이 팔 쪽
#   "front" = 가드·끌어올린 주먹. 관객을 향해 있어서 **앞모습 글러브(패드만)** 를 세워 붙인다. 각도는 살짝 기울기.
#             처음엔 옆모습 글러브를 위로 세웠더니 손목 입구가 아래로 보였다 (2026-09-28 갑)
# 2 는 다른 손이 가려져 한 개다.
FISTS = [
    [(152, 194, "front", -6), (218, 189, "front", 8)],
    [(89, 146, "front", -14), (214, 185, "front", 8)],
    [(256, 162, "side", 0)],
    [(145, 194, "front", -6), (216, 183, "front", 8)],
    [(134, 203, "front", -6), (246, 137, "front", 14)],
    [(148, 195, "front", -6), (256, 167, "side", 0)],
    [(145, 195, "front", -6), (220, 183, "front", 8)],
    [(138, 195, "front", -6), (210, 188, "front", 8)],
]
GLOVE_H = 80              # 옆모습 글러브 길이 px. 눕히면 폭(~67)이 세로가 된다 - 뻗은 주먹 높이(~50)와 가드 글러브 폭(64)에 맞춘다
GLOVE_FRONT_W = 64        # 앞모습 글러브 폭 px (가드 주먹 지름 ~30 을 넉넉히 덮는다)
# 옆모습 글러브는 **주먹 패드의 가운데**를 주먹에 맞춘다 - 그림 가운데로 맞추고 팔 쪽으로 물렸더니 패드가 주먹보다 뒤·위에 와서
# 원래 주먹 앞 외곽선이 글러브 밖으로 보였다 (2026-09-28 갑: "펀치 질렀을 때 글러브 위치가 안 맞아"). 패드 앞 끝이 주먹 앞 끝
# (칸 2 에서 x 288)을 덮도록 주먹 가운데보다 이만큼 앞으로 민다.
GLOVE_FORWARD = 10
GLOVE_FRONT_UP = 2        # 앞모습 글러브를 주먹보다 이만큼 위로
FIST_NEAR = 26            # 주먹 자리에서 이 거리 안의 살색 덩어리를 그 주먹(손)으로 본다           # 글러브 가운데를 팔 쪽으로 이만큼 - 손목(흰 띠)이 팔에 걸친다

TAIL_BOX = (25, 160, 99, 245)     # 대기 칸의 꼬리 자리 (귀 아래, 몸통 왼쪽)
TAIL_CENTER = (70, 200)           # 소용돌이 가운데 - 줄무늬를 여기서 방사로 나눈다
TAIL_TIP_AT = (56, 172)           # 꼬리 말린 윗부분 - 리본·별 등을 묶는 자리
TAIL_TIP_SIZE = 44
STRIPES = 10                      # 소용돌이 한 바퀴를 몇 칸으로 나눠 번갈아 칠할지
STRIPE_RADIUS = 36                # 소용돌이 가운데에서 이 반지름 안만 - 몸통에 붙는 밑동까지 칠하면 칸 경계가 네모로 보였다


def paste_rotated(dst, im, center, angle_deg):
    """im 을 angle 만큼(화면 기준 시계 방향) 돌려 가운데가 center 에 오게 합친다."""
    r = im.rotate(-angle_deg, resample=Image.BICUBIC, expand=True)
    dst.alpha_composite(r, (round(center[0] - r.width / 2), round(center[1] - r.height / 2)))


def glove_sprite(variant, front=False):
    """(그림, 패드 가운데). 패드 가운데 = 그림 가운데로부터의 (dx, dy) - 옆모습 글러브를 주먹에 맞출 때 쓴다.
    크기는 효과(불꽃·눈꽃)를 뺀 글러브 몸통으로 정한다."""
    g, (bx, by, bw, bh) = ms.glove_layout(variant, front)
    k = GLOVE_FRONT_W / bw if front else GLOVE_H / bh
    g = g.resize((max(1, round(g.width * k)), max(1, round(g.height * k))), Image.LANCZOS)
    body = np.array(g)[round(by * k):round((by + bh) * k), round(bx * k):round((bx + bw) * k), 3] > 40
    cut = round(body.shape[0] * (1.0 if front else ms.GLOVE_FRONT_KEEP))    # 옆모습은 손목 띠 위(패드)만
    ys, xs = np.nonzero(body[:cut])
    return g, (bx * k + xs.mean() - g.width / 2, by * k + ys.mean() - g.height / 2)


def tip_sprite(tip):
    t = ms.tip_image(tip)
    t = t.crop(t.getchannel("A").getbbox())
    t.thumbnail((TAIL_TIP_SIZE, TAIL_TIP_SIZE), Image.LANCZOS)
    return t


def paint_stripes(out, fur, frame_x, offset, stripe_rgb, top=0):
    """꼬리 털에 줄무늬 - 소용돌이 가운데에서 본 각도로 칸을 나눠 하나 건너 칠한다. 원래 밝기(그늘)는 살린다."""
    a = np.array(out)
    x0, y0, x1, y1 = TAIL_BOX
    dx, dy = offset
    ys, xs = np.mgrid[0:a.shape[0], 0:a.shape[1]]
    lx, ly = xs - frame_x - dx, ys - dy - top
    region = (lx >= x0) & (lx < x1) & (ly >= y0) & (ly < y1) & fur
    ang = np.arctan2(ly - TAIL_CENTER[1], lx - TAIL_CENTER[0])
    band = (np.floor((ang + np.pi) / (2 * np.pi / STRIPES)).astype(int) % 2) == 0
    m = region & band & (np.hypot(lx - TAIL_CENTER[0], ly - TAIL_CENTER[1]) < STRIPE_RADIUS)
    v = a[..., :3].astype(np.float32).max(-1) / 255.0
    shade = np.clip(v / 0.62, 0.55, 1.15)[..., None]              # 털 평균 밝기 대비 - 그늘진 곳은 줄무늬도 어둡게
    col = np.clip(np.array(stripe_rgb, np.float32)[None, None, :] * shade, 0, 255).astype(np.uint8)
    a[..., :3] = np.where(m[..., None], col, a[..., :3])
    return Image.fromarray(a, "RGBA")


class SheetContext:
    """원판 시트와 칸마다 머리·꼬리가 옮겨 간 자리 - 스킨마다 다시 재지 않는다."""

    def __init__(self):
        self.sheet = Image.open(parts.SHEET).convert("RGBA")
        self.cw, self.ch = parts.CELL
        self.frames = self.sheet.width // self.cw
        head, self.box = parts.head_from_sheet()
        head_alpha = np.array(head)[..., 3] > 8
        full_alpha = np.array(self.sheet)[..., 3] > 8
        tail_tpl = full_alpha[TAIL_BOX[1]:TAIL_BOX[3], TAIL_BOX[0]:TAIL_BOX[2]]
        self.fur, _ = fur_mask(np.array(self.sheet)[..., :3])
        self.fur &= full_alpha
        self.head_off, self.tail_off = [], []
        for f in range(self.frames):
            fa = full_alpha[:, f * self.cw:(f + 1) * self.cw]
            self.head_off.append(region_offset(fa, head_alpha, self.box))
            self.tail_off.append(region_offset(fa, tail_tpl, TAIL_BOX, rx=20, ry=16))
        self.gloves, self.tips = {}, {}
        self.fists = self._fist_masks()

    def _fist_masks(self):
        """칸마다 주먹(손) 살색 덩어리 - 글러브 밖으로 삐져나오는 손을 팔 털색으로 칠할 자리. FISTS 자리에서 가장 가까운 것."""
        a = np.array(self.sheet)
        hsv = hsv_of(a[..., :3])
        h, s, v = hsv[..., 0] * 360, hsv[..., 1], hsv[..., 2]
        tan = (a[..., 3] > 200) & (h >= 15) & (h <= 45) & (s >= 0.12) & (s <= 0.5) & (v >= 0.8)
        out = []
        for f, fists in enumerate(FISTS):
            cell = np.zeros_like(tan)
            cell[:, f * self.cw:(f + 1) * self.cw] = tan[:, f * self.cw:(f + 1) * self.cw]
            lab, n = ndimage.label(ndimage.binary_opening(cell, iterations=2))
            masks = []
            for x, y, _, _ in fists:
                best, best_d = None, FIST_NEAR
                for i in range(1, n + 1):
                    ys, xs = np.nonzero(lab == i)
                    if len(xs) < 150 or len(xs) > 2500:        # 얼굴·배는 크다
                        continue
                    d = np.hypot(xs.mean() - (f * self.cw + x), ys.mean() - y)
                    if d < best_d:
                        best, best_d = i, d
                masks.append(ndimage.binary_dilation(lab == best, iterations=2) if best else None)
            out.append(masks)
        return out

    def fur_padded(self, top):
        """털 마스크를 칸 위 여백(top)만큼 내린 것 - 모자 쓴 시트에 줄무늬를 칠할 때."""
        if top == 0:
            return self.fur
        return np.vstack([np.zeros((top, self.fur.shape[1]), bool), self.fur])


def build_sheet(spec, ctx):
    """스킨 하나의 펀치 시트. 순서: 털 색 -> 꼬리 줄무늬 -> 꼬리 끝 -> 모자 -> 글러브(맨 앞).
    모자를 쓰면 칸 위에 ms.HAT_HEADROOM 만큼 여백을 더한다 (칸 높이 324 -> 420) - 모든 자리가 그만큼 내려간다."""
    cw, ch, frames = ctx.cw, ctx.ch, ctx.frames
    top = ms.HAT_HEADROOM if spec.get("hat") else 0
    body = recolor(ctx.sheet, *spec["fur"])
    out = Image.new("RGBA", (body.width, ch + top), (0, 0, 0, 0))
    out.alpha_composite(body, (0, top))
    tail = spec.get("tail") or {}
    if tail.get("stripe"):
        for f in range(frames):
            out = paint_stripes(out, ctx.fur_padded(top), f * cw, ctx.tail_off[f], ms.hex_rgb(tail["stripe"]), top)
    if tail.get("tip"):
        t = ctx.tips.setdefault(tail["tip"], tip_sprite(tail["tip"]))
        for f, (dx, dy) in enumerate(ctx.tail_off):
            out.alpha_composite(t, (f * cw + TAIL_TIP_AT[0] + dx - t.width // 2, top + TAIL_TIP_AT[1] + dy - t.height // 2))
    if spec.get("hat"):
        for f, (dx, dy) in enumerate(ctx.head_off):
            out = ms.put_hat(out, spec["hat"], (f * cw + dx, top + dy), clip_x=(f * cw, (f + 1) * cw))
    if spec.get("glove"):
        # 글러브 밖으로 삐져나온 손(살색)을 팔 털색으로 - 글러브 아래로 손목·팔이 이어져 보인다 (2026-09-28 갑: "글러브 뒤로 손이 보여")
        fur_rgb = np.array(ms.fur_colors(spec["fur"])[0], np.float32)
        oa = np.array(out)
        for masks in ctx.fists:
            for m in masks:
                if m is None:
                    continue
                mm = np.vstack([np.zeros((top, m.shape[1]), bool), m]) if top else m
                mm &= oa[..., 3] > 0
                shade = np.clip(oa[..., :3].astype(np.float32).max(-1) / 250.0, 0.55, 1.0)[..., None]
                oa[..., :3] = np.where(mm[..., None], np.clip(fur_rgb[None, None, :] * shade, 0, 255).astype(np.uint8), oa[..., :3])
        out = Image.fromarray(oa, "RGBA")
        side, side_pad = ctx.gloves.setdefault((spec["glove"], False), glove_sprite(spec["glove"]))
        front, _ = ctx.gloves.setdefault((spec["glove"], True), glove_sprite(spec["glove"], True))
        for f, fists in enumerate(FISTS):
            for x, y, kind, ang in fists:
                if kind == "side":
                    # 그림은 주먹이 위라서 (ang + 90) 만큼 돌려 눕힌다. 패드 가운데가 돌아간 자리를 빼서 패드가 주먹에 오게.
                    rot = np.radians(ang + 90)
                    dx, dy = side_pad
                    px, py = dx * np.cos(rot) - dy * np.sin(rot), dx * np.sin(rot) + dy * np.cos(rot)
                    fwd = np.radians(ang)
                    c = (f * cw + x + np.cos(fwd) * GLOVE_FORWARD - px, top + y + np.sin(fwd) * GLOVE_FORWARD - py)
                    paste_rotated(out, side, c, ang + 90)
                else:
                    paste_rotated(out, front, (f * cw + x, top + y - GLOVE_FRONT_UP), ang)
    # 칸 밖으로 나간 그림(뻗은 글러브·넓은 모자)이 옆 칸에 번지지 않게 칸마다 잘라 다시 붙인다
    clean = Image.new("RGBA", out.size, (0, 0, 0, 0))
    for f in range(frames):
        clean.alpha_composite(out.crop((f * cw, 0, (f + 1) * cw, out.height)), (f * cw, 0))
    return clean


def monkey_sheets():
    ctx = SheetContext()
    ids = [i["id"] for i in json.loads(ITEMS.read_text(encoding="utf-8"))["items"] if i["category"] == "monkey"]
    for item_id in ids:
        spec = ms.SKINS.get(item_id)
        if spec is None:
            print(f"{item_id}: 스킨 규칙이 없다 - tools/monkey_skins.py 의 SKINS 에 추가할 것")
            continue
        if spec["fur"] == ms.BROWN and ms.parts_count(spec) == 0:
            continue                                   # 원판 그대로 - 게임이 monkey_punch.png 를 쓴다
        path = SKIN_OUT / item_id / "punch.png"
        path.parent.mkdir(parents=True, exist_ok=True)
        build_sheet(spec, ctx).save(path, optimize=True)
        print(f"{item_id}: punch.png 부위 {ms.parts_count(spec)}")
    print("머리 자리:", ctx.head_off)
    print("꼬리 자리:", ctx.tail_off)


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
