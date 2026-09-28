"""B19 — 원숭이 스킨 표와 꾸밈 부위(모자 · 글러브 · 꼬리) 붙이기. make-monkey-parts.py(커서 머리·skin.json)와
make-body-skins.py(본편 펀치 시트)가 같이 읽는다 — **두 원숭이가 같은 표에서 나와야 갈라지지 않는다.**

스킨 = 털 색 + 부위 세 칸(모자 / 글러브 / 꼬리). 값은 꾸민 부위 수로 매긴다 (docs/B19-MONKEY-SKINS.md):

    털 색만      15      (monkey_02~04, 06)
    부위 1개     200
    부위 2개     500     (한 가지 테마로 묶는다)
    부위 3개     2,000   (털 색까지 바꾼 풀세트 - 처음 3,000 에서 내렸다, 밸런스 docs/B19 §3)

부위 그림 원본은 assets/_source/parts/ (tools/assetgen 매니페스트의 "parts" 그룹, ComfyUI 로 뽑고 시드를 고정했다 - .gdignore 라
게임에 안 들어간다). 게임이 실행 중에 쓰는 것(커서 원숭이의 글러브·꼬리 끝)만 assets/cursor/parts/ 에 만든다 - 모자는 머리 그림에 굽는다.
글러브 색 변형은 빨간 글러브 한 장에서 색만 바꿔 만든다 — 변형마다 새로 뽑으면 모양이 제각각이 된다.
"""

import colorsys
import pathlib

import numpy as np
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "assets" / "_source" / "parts"
RUNTIME = ROOT / "assets" / "cursor" / "parts"

# ---------------------------------------------------------------------------------------------- 스킨 표

# fur: (색조(도) 또는 None=그대로, 채도 배율, 밝기 더하기)
BROWN = (None, 1.0, 0.0)

# id -> dict(fur, hat, glove, tail). tail = dict(stripe="#hex" 줄무늬 색 | None, tip=부위 id | None)
SKINS = {
    # --- 털 색만 (15) -----------------------------------------------------------------------------
    "monkey_01": dict(fur=BROWN),                                   # 갈색 - 기본 지급
    "monkey_02": dict(fur=(46, 1.05, 0.22)),                        # 노랑
    "monkey_03": dict(fur=(None, 0.10, 0.06)),                      # 회색
    "monkey_04": dict(fur=(272, 0.55, 0.12)),                       # 보라
    "monkey_06": dict(fur=(18, 1.25, 0.10)),                        # 오랑우탄

    # --- 부위 1개 (200) ---------------------------------------------------------------------------
    "monkey_05": dict(fur=BROWN, hat="cap"),                        # 빨간 모자 (9/26 코드로 그린 모자 -> 생성 그림)
    "monkey_07": dict(fur=BROWN, glove="red"),                      # 빨간 글러브
    "monkey_08": dict(fur=BROWN, glove="blue"),                     # 파란 글러브
    "monkey_09": dict(fur=BROWN, glove="black"),                    # 검은 글러브
    "monkey_10": dict(fur=BROWN, tail=dict(stripe="#3e2012")),      # 줄무늬 꼬리
    "monkey_11": dict(fur=BROWN, tail=dict(tip="bow")),             # 리본 꼬리
    "monkey_12": dict(fur=BROWN, hat="beanie"),                     # 비니
    "monkey_13": dict(fur=BROWN, hat="straw"),                      # 밀짚모자

    # --- 부위 2개, 테마 (500) --------------------------------------------------------------------
    "monkey_14": dict(fur=BROWN, hat="sweatband", glove="red"),     # 권투 선수
    "monkey_15": dict(fur=BROWN, hat="cowboy", glove="brown"),      # 카우보이
    "monkey_16": dict(fur=(None, 0.10, 0.06), hat="pirate", tail=dict(stripe="#c8322b")),   # 해적 (회색 털)
    "monkey_17": dict(fur=BROWN, hat="chef", glove="white"),        # 요리사
    "monkey_18": dict(fur=(None, 0.10, 0.06), hat="viking", glove="iron"),                   # 바이킹 (회색 털)
    "monkey_19": dict(fur=BROWN, glove="pink", tail=dict(tip="heart")),                        # 러블리
    "monkey_20": dict(fur=BROWN, glove="yellow", tail=dict(tip="bolt")),                       # 번개
    "monkey_21": dict(fur=BROWN, glove="blue", tail=dict(tip="star")),                         # 별빛

    # --- 부위 3개 + 털 색 (2,000) ----------------------------------------------------------------
    "monkey_22": dict(fur=(38, 1.1, 0.12), hat="crown", glove="gold", tail=dict(stripe="#f2c230")),   # 황금 왕
    "monkey_23": dict(fur=(272, 0.55, 0.12), hat="wizard", glove="purple", tail=dict(tip="star")),    # 마법사
    "monkey_24": dict(fur=(8, 1.2, 0.05), hat="bandana", glove="fire", tail=dict(tip="flame")),       # 불꽃 파이터
    "monkey_25": dict(fur=(200, 0.35, 0.22), hat="icecrown", glove="ice", tail=dict(tip="snow")),     # 얼음 왕자
}

# ---------------------------------------------------------------------------------------------- 글러브

# 빨간 글러브(glove_base) 한 장에서 빨강 영역만 색을 바꾼다: (색조, 채도 배율, 밝기 더하기). None 이면 원본 그대로.
GLOVE_TINTS = {
    "red": None,
    "blue": (212, 0.95, 0.0),
    "black": (None, 0.15, -0.45),
    "pink": (330, 0.55, 0.12),
    "yellow": (48, 1.0, 0.12),
    "white": (None, 0.06, 0.25),
    "brown": (26, 0.75, -0.18),
    "iron": (210, 0.12, -0.12),
    "purple": (276, 0.8, 0.0),
    "gold": (44, 0.95, 0.10),
}
# 특수 글러브: 색을 바꾼 기본 글러브 뒤에 꼬리 끝 그림(불꽃·눈꽃)을 깐다. 글러브를 따로 뽑으면(2026-09-28 시도) 종합격투기
# 장갑이나 맨주먹이 나왔고, 무엇보다 다른 글러브와 모양이 달라진다. (색 규칙, 뒤에 깔 꼬리 끝, 크기 배율)
GLOVE_SPECIAL = {"fire": ((14, 1.1, 0.06), "flame", 0.8), "ice": ((192, 0.45, 0.25), "snow", 1.05)}


def _hsv(a):
    x = a.astype(np.float32) / 255.0
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


def _rgb(hsv):
    h, s, v = hsv[..., 0] * 6.0, hsv[..., 1], hsv[..., 2]
    i = np.floor(h).astype(int) % 6
    f = h - np.floor(h)
    p, q, t = v * (1 - s), v * (1 - s * f), v * (1 - s * (1 - f))
    out = np.choose(i[..., None].repeat(3, -1), [
        np.stack([v, t, p], -1), np.stack([q, v, p], -1), np.stack([p, v, t], -1),
        np.stack([p, q, v], -1), np.stack([t, p, v], -1), np.stack([v, p, q], -1)])
    return np.clip(out * 255 + 0.5, 0, 255).astype(np.uint8)


# 앞모습 글러브 = 원판에서 손목 띠 위(패드 주먹)만. 가드 주먹은 관객을 향해 있어서 옆모습 글러브를 세우면 손목 입구가
# 아래로 보였다 (2026-09-28 갑). 원판의 패드 아래 진한 외곽선이 이 높이다.
GLOVE_FRONT_KEEP = 0.53


def glove_image(variant, front=False):
    """글러브 한 장. 옆모습은 주먹이 위·손목이 아래(원본 방향), 앞모습(front)은 패드 주먹만."""
    return glove_layout(variant, front)[0]


def glove_layout(variant, front=False):
    """(그림, 글러브 몸통 자리 (x, y, w, h)). 불꽃·얼음은 뒤에 효과를 깔아 그림이 커지는데, 크기·패드 위치는 몸통으로 맞춰야 한다
    - 효과까지 합친 높이로 줄였더니 글러브가 작아져 뻗은 주먹 끝이 보였다 (2026-09-28)."""
    rule, behind, scale = GLOVE_SPECIAL.get(variant, (GLOVE_TINTS.get(variant), None, 1.0))
    glove = _tint_glove(rule)
    glove = glove.crop(glove.getchannel("A").getbbox())
    if front:
        glove = glove.crop((0, 0, glove.width, round(glove.height * GLOVE_FRONT_KEEP)))
    if behind is None:
        return glove, (0, 0, glove.width, glove.height)
    fx = tip_image(behind)
    k = glove.width * scale / fx.width
    fx = fx.resize((round(fx.width * k), round(fx.height * k)), Image.LANCZOS)
    w, h = max(glove.width, fx.width), glove.height + round(fx.height * 0.35)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.alpha_composite(fx, ((w - fx.width) // 2, 0))                         # 주먹 위로 솟게
    canvas.alpha_composite(glove, ((w - glove.width) // 2, h - glove.height))
    return canvas, ((w - glove.width) // 2, h - glove.height, glove.width, glove.height)


def _tint_glove(rule):
    base = Image.open(SRC / "glove" / "base.png").convert("RGBA")
    if rule is None:
        return base
    a = np.array(base)
    hsv = _hsv(a[..., :3])
    h, s, v = hsv[..., 0] * 360, hsv[..., 1], hsv[..., 2]
    red = ((h <= 20) | (h >= 335)) & (s >= 0.35) & (v >= 0.25) & (a[..., 3] > 0)   # 빨간 가죽 (흰 손목·외곽선은 빠진다)
    hue, sat_mul, val_add = rule
    new = hsv.copy()
    if hue is not None:
        new[..., 0] = hue / 360.0
    new[..., 1] = np.clip(hsv[..., 1] * sat_mul, 0, 1)
    new[..., 2] = np.clip(hsv[..., 2] + val_add, 0.04, 1)
    a[..., :3] = np.where(red[..., None], _rgb(new), a[..., :3])
    return Image.fromarray(a, "RGBA")


def all_gloves():
    return list(GLOVE_TINTS) + list(GLOVE_SPECIAL)


# ---------------------------------------------------------------------------------------------- 모자

# 대기 칸(프레임 0)의 머리. 원숭이가 오른쪽(나무)으로 살짝 돌아서 **정수리 가운데는 얼굴 가운데(x 171)보다 왼쪽**이다 -
# 첫 시안은 모자를 얼굴 가운데에 맞춰서 전부 앞으로 쏠려 "얹힌" 것처럼 보였다 (2026-09-28 갑). 알파를 행마다 재서 잡았다:
#   y 50: x 97~212 / y 80: x 80~231 / y 90: x 78~234 (여기까지 귀 없음, 폭 156) / 얼굴 살색 위 끝 y ≈ 85 / 눈 위 끝 y ≈ 112
HEAD_CX = 156
EARS_TOP_Y = 86           # 귀 위 끝 - 모자 속 털을 지워도 이 줄 아래(귀)는 안 건드린다

# 모자마다: (그림 폭 px, 가운데 x, 아래 끝 y, 기울기 도, 세로 배율, 씌우는 방식)
#   **아래 끝은 눈 바로 위(y ~112)** - 테가 이마를 감싸야 쓴 것으로 보인다. 첫 조정(아래 끝 ~100)은 모자가 정수리 위에 올라앉아
#   지운 털 자리가 납작한 선으로 드러났다 (2026-09-28 갑: "모자 이미지가 원숭이 위에 얹혀 있는 느낌")
#   폭은 **테(머리가 들어가는 입구)가 정수리 폭(~160)보다 조금 넓게** - 챙 있는 모자는 챙까지라 더 넓다
#   아래 끝은 챙·테의 아래 끝. 세로 배율 < 1 은 머리에 눌러 쓴 모양 (생성 그림은 빈 모자라 위로 길다)
#   씌우는 방식: "cover" = 모자 아래 끝보다 위의 원숭이(정수리·앞머리 털)를 지운다 - 모자 속으로 들어간 것처럼
#                "band"  = 이마 띠라 지우지 않는다 (털이 띠 위로 나온다)
HAT_FIT = {
    "cap": (192, 174, 116, -4, 0.70, "cover"),        # 챙이 오른쪽(얼굴 쪽)으로 나온다. 빈 모자라 위로 높다 - 눌렀다
    "beanie": (170, 156, 116, -2, 0.52, "cover"),     # 방울 달린 빈 비니가 위로 길다 - 가장 많이 눌렀다. 접은 단이 눈썹 위까지
    "straw": (228, 157, 116, -4, 0.72, "cover"),      # 챙이 넓고 평평하다 - 폭을 줄이고 눌러서 챙이 머리 옆에 걸치게
    "sweatband": (176, 157, 110, -4, 0.85, "band"),     # 이마(눈 위)를 가로지른다 - 정수리 털은 띠 위로 나온다
    "cowboy": (210, 176, 118, -3, 0.80, "cover"),     # 3/4 시점이라 모자 몸통이 그림 왼쪽에 있다 - 그림 가운데를 오른쪽으로
    "pirate": (214, 157, 116, -3, 0.90, "cover"),
    "chef": (186, 155, 114, -4, 0.80, "cover"),
    "viking": (244, 156, 112, 0, 0.95, "cover"),
    "crown": (146, 156, 74, -6, 0.95, "cover"),       # 정수리에 얹는다 - 아래 끝이 정수리 둥근 곳(폭 ~140)에 걸친다
    "wizard": (230, 160, 112, -5, 0.85, "cover"),
    "bandana": (190, 158, 110, -4, 0.90, "band"),
    "icecrown": (150, 156, 74, -6, 0.95, "cover"),
}

# 좌우를 뒤집을 모자 - 야구 모자 그림은 챙이 왼쪽(원숭이 뒤통수 쪽)이었다. 원숭이는 오른쪽(나무)을 본다.
HAT_FLIP = {"cap"}
# 그림 위쪽 이 비율만 쓴다 - 얼굴까지 덮는 투구(바이킹)는 이마 띠 아래를 버려야 모자처럼 얹힌다. 두건·헤어밴드 그림은
# 띠가 두 줄이라 위 한 줄만.
HAT_KEEP_TOP = {"viking": 0.525, "bandana": 0.46, "sweatband": 0.46}

# 모자를 대기 칸 높이(324)에 다 못 담는다 - 머리 폭에 맞추면 비니·마법사 모자가 칸 위로 100px 가까이 나간다. 모자를 쓴
# 스킨의 펀치 시트는 칸 위에 이만큼 여백을 더한다 (게임은 Monkey.SetSkin 이 여백만큼 스프라이트를 올린다).
HAT_HEADROOM = 96

# 이마 띠(band) 끝을 머리 외곽선 안쪽 이만큼(px)에서 끊는다 - 원판 외곽선 굵기가 4~5px 다.
BAND_INSET = 5


def _hat_sprite(hat):
    width, _, _, tilt, sy, _ = HAT_FIT[hat]
    im = Image.open(SRC / "hat" / f"{hat}.png").convert("RGBA")
    im = im.crop(im.getchannel("A").getbbox())
    if hat in HAT_FLIP:
        im = im.transpose(Image.FLIP_LEFT_RIGHT)
    if hat in HAT_KEEP_TOP:
        im = im.crop((0, 0, im.width, round(im.height * HAT_KEEP_TOP[hat])))
        im = im.crop(im.getchannel("A").getbbox())   # 두 줄 사이 빈 줄이 남으면 아래 끝이 떠 보인다
    k = width / im.width
    im = im.resize((max(1, round(im.width * k)), max(1, round(im.height * k * sy))), Image.LANCZOS)
    if tilt:
        im = im.rotate(-tilt, resample=Image.BICUBIC, expand=True)
        im = im.crop(im.getchannel("A").getbbox())   # 돌리면 생기는 투명 테두리 - 안 자르면 아래 끝이 그만큼 떠 보인다
    return im


def put_hat(img, hat, origin=(0, 0), clip_x=None):
    """img 에 모자를 씌운다. origin = img 에서 대기 칸 (0,0) 이 오는 자리. clip_x = (x0, x1) 이면 그 폭 밖은 안 그린다(옆 칸).
    "cover" 모자는 먼저 모자 아래 끝 위쪽의 원숭이를 지운다 - 정수리·앞머리 털이 모자 밖으로 삐져나오면 얹어 둔 것처럼 보인다.
    지우는 줄은 귀 위 끝(EARS_TOP_Y)보다 위만이라 귀는 안 건드린다."""
    _, cx, bottom, _, _, mode = HAT_FIT[hat]
    im = _hat_sprite(hat)
    x = round(origin[0] + cx - im.width / 2)
    y = round(origin[1] + bottom - im.height)
    out = img.copy()
    a = np.array(out)
    x0, x1 = (0, a.shape[1]) if clip_x is None else clip_x
    if mode == "cover":
        # 세로줄마다 그 줄의 모자 아래 끝보다 위에 있고 모자가 안 덮는 원숭이만 지운다 - 모자 위로 삐져나온 정수리 털.
        # 한 줄(y)로 통째로 지우면 챙 가운데처럼 모자가 덜 내려온 곳에 머리가 납작하게 잘려 보였다 (2026-09-28).
        ha = np.array(im)[..., 3] > 40
        limit = round(origin[1] + EARS_TOP_Y - 2)                   # 귀는 안 건드린다
        for cx_ in range(im.width):
            col = ha[:, cx_]
            if not col.any():
                continue
            gx = x + cx_
            if gx < max(0, x0) or gx >= min(a.shape[1], x1):
                continue
            hat_bottom = y + int(np.nonzero(col)[0].max())
            stop = min(hat_bottom, limit)
            if stop <= 0:
                continue
            rows = np.arange(stop)
            covered = np.zeros(stop, bool)
            inside = (rows >= y) & (rows < y + im.height)
            covered[inside] = col[rows[inside] - y]
            a[:stop, gx, 3] = np.where(covered, a[:stop, gx, 3], 0)
        out = Image.fromarray(a, "RGBA")
    layer = Image.new("RGBA", out.size, (0, 0, 0, 0))
    layer.alpha_composite(im, (max(0, x), max(0, y)), (max(0, -x), max(0, -y)))
    la = np.array(layer)
    la[:, :x0, 3] = 0
    la[:, x1:, 3] = 0
    if mode == "band":
        # 이마 띠는 머리를 감으니 **머리 윤곽 밖으로 나가면 안 된다** - 띠 그림은 곧은 막대라 양 끝이 머리 밖으로 삐져나왔다
        # (2026-09-28 갑). 머리 윤곽을 BAND_INSET 만큼 안으로 줄인 곳만 남긴다 - 윤곽 끝까지 칠하면 띠가 머리 외곽선을 덮어서
        # 잘라 붙인 것처럼 보였다. 외곽선이 띠 끝을 감싸야 머리를 두른 것으로 읽힌다. 위로 솟은 리본은 머리 안이라 남는다.
        from scipy import ndimage
        head = ndimage.binary_erosion(np.array(img)[..., 3] > 40, iterations=BAND_INSET)
        la[..., 3] = np.where(head, la[..., 3], 0)
    out.alpha_composite(Image.fromarray(la, "RGBA"))
    return out


def hat_extent(hat):
    """대기 칸 좌표로 모자가 차지하는 (x0, y0, x1, y1) - 커서 머리 그림의 캔버스를 넓힐 때 쓴다."""
    _, cx, bottom, _, _, _ = HAT_FIT[hat]
    im = _hat_sprite(hat)
    x = round(cx - im.width / 2)
    return x, bottom - im.height, x + im.width, bottom


# ---------------------------------------------------------------------------------------------- 꼬리

TAIL_TIPS = ["bow", "star", "heart", "bolt", "flame", "snow"]


# 꼬리 끝 그림에서 위쪽 이 비율만 쓴다 - 불꽃 그림은 위에 떨어진 불씨 하나 + 아래 모닥불이라 위 불씨만 쓴다.
TIP_KEEP_TOP = {"flame": 0.36}


def tip_image(tip):
    im = Image.open(SRC / "tail" / f"{tip}.png").convert("RGBA")
    if tip in TIP_KEEP_TOP:
        im = im.crop(im.getchannel("A").getbbox())
        im = im.crop((0, 0, im.width, round(im.height * TIP_KEEP_TOP[tip])))
        im = im.crop(im.getchannel("A").getbbox())
    return im


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def fur_colors(fur):
    """털 규칙 -> (털, 털 그늘) RGB. make-monkey-parts 의 BASE 색에 같은 규칙을 건다."""
    hue, sat_mul, val_add = fur
    out = []
    for c in ((158, 96, 52), (122, 70, 36)):
        h, s, v = colorsys.rgb_to_hsv(*(x / 255.0 for x in c))
        h = h if hue is None else hue / 360.0
        r, g, b = colorsys.hsv_to_rgb(h, min(1.0, s * sat_mul), min(1.0, max(0.0, v + val_add)))
        out.append((round(r * 255), round(g * 255), round(b * 255)))
    return out


def parts_count(spec):
    tail = spec.get("tail") or {}
    return sum(1 for x in (spec.get("hat"), spec.get("glove"), tail.get("stripe") or tail.get("tip")) if x)


def write_runtime_parts(px=64):
    """커서 원숭이가 실행 중에 읽는 글러브 · 꼬리 끝 그림 (MonkeySkin.PartTexture). 원숭이 옆 12px 로 그리니 64px 면 넉넉하다."""
    out = []
    for kind, names, load in (("glove", all_gloves(), glove_image), ("tail", TAIL_TIPS, tip_image)):
        folder = RUNTIME / kind
        folder.mkdir(parents=True, exist_ok=True)
        for name in names:
            im = load(name)
            im = im.crop(im.getchannel("A").getbbox())
            im.thumbnail((px, px), Image.LANCZOS)
            im.save(folder / f"{name}.png", optimize=True)
            out.append(f"{kind}/{name}.png")
    return out
