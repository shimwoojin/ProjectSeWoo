"""B17 — 커서 원숭이 스킨을 만든다 (docs/B17-CURSOR-REWORK.md §4-2, §6-2).

    "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-monkey-parts.py

**머리만 그림에서 가져오고 나머지(몸·팔·다리·손발·꼬리)는 게임이 그린다** (shared/Cursor/MonkeyRig.cs).
머리는 본편 원숭이(assets/entities/monkey_punch.png 대기 프레임)에서 목 위를 잘라 낸다 — 커서에 달리는 원숭이가
나무를 치는 원숭이와 같은 캐릭터여야 자연스럽다. 몸통·팔다리를 그림에서 잘라 쓰지 않는 이유: 대기 프레임은 팔이
가슴 앞에 있어서 잘라 내면 몸에 구멍이 나고, 굽은 팔은 머리 위로 뻗은 "매달림" 자세로 돌릴 수 없다. 같은 그림체
(굵은 진갈색 외곽선 + 단색 + 밝은 배)로 코드가 그리면 어떤 자세든 되고, 스킨은 색 값만 바꾸면 된다.

스킨마다 (assets/cursor/monkey/<id>/):
  head.png   머리 (털 색만 바꾼다 — 얼굴·귀 안쪽·외곽선은 그대로)
  skin.json  몸을 그릴 색 + 머리의 목·눈 좌표 + 장신구
  icon.png   상점·도감 아이콘 (128x128) — 머리 + 간단한 몸

원숭이 목록·이름은 game/shop/items.json. 여기 SKINS 에 규칙이 없는 id 는 만들지 않는다.
"""

import colorsys
import json
import pathlib

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = pathlib.Path(__file__).resolve().parents[1]
SHEET = ROOT / "assets" / "entities" / "monkey_punch.png"
OUT = ROOT / "assets" / "cursor" / "monkey"
ITEMS = ROOT / "game" / "shop" / "items.json"

CELL = (298, 324)       # 펀치 시트 한 칸
NECK_Y = 172            # 이 위가 머리 (대기 프레임 기준, 2026-09-26 격자로 잰 값)
EYES = [(160, 127), (208, 127)]   # 대기 프레임에서 두 눈의 가운데
OUTLINE = (62, 32, 18)

# 원본 털 색 (대기 프레임에서 뽑은 값). 몸을 그릴 때 기본값이다.
BASE = {"fur": (158, 96, 52), "furShade": (122, 70, 36), "belly": (250, 205, 165), "outline": OUTLINE}

# id -> (털 색조(도) 또는 None=그대로, 채도 배율, 밝기 더하기, 장신구)
SKINS = {
    "monkey_01": (None, 1.0, 0.0, None),          # 갈색 - 본편 그대로
    "monkey_02": (46, 1.05, 0.22, None),          # 노랑
    "monkey_03": (None, 0.10, 0.06, None),        # 회색
    "monkey_04": (272, 0.55, 0.12, None),         # 보라
    "monkey_05": (None, 1.0, 0.0, "cap"),         # 빨간 모자 - 갈색 + 모자
    "monkey_06": (18, 1.25, 0.10, None),          # 오랑우탄 - 주황
}


def head_from_sheet():
    sheet = Image.open(SHEET).convert("RGBA")
    frame = sheet.crop((0, 0) + CELL)
    a = np.array(frame)
    body = a[..., 3] > 8
    body[NECK_Y:, :] = False
    labels, n = ndimage.label(body)
    sizes = ndimage.sum(body, labels, range(1, n + 1))
    keep = labels == (int(np.argmax(sizes)) + 1)       # 가장 큰 덩어리 = 머리 (꼬리 끝 조각은 떨어진다)
    a[~keep, 3] = 0
    head = Image.fromarray(a, "RGBA")
    box = head.getchannel("A").getbbox()
    return head.crop(box), box


def fur_mask(rgb):
    """털: 갈색 계열(색조 10~40도), 채도 0.45 이상, 밝기 0.3~0.86. 살색 얼굴(채도 낮음)·외곽선(어두움)은 빠진다."""
    flat = rgb.reshape(-1, 3) / 255.0
    hsv = np.array([colorsys.rgb_to_hsv(*p) for p in flat]).reshape(rgb.shape)
    h, s, v = hsv[..., 0] * 360, hsv[..., 1], hsv[..., 2]
    fur = (h >= 14) & (h <= 40) & (s >= 0.45) & (v >= 0.3) & (v <= 0.86)
    # 얼굴·귀 안쪽을 뺀다: 밝은 살색 영역의 구멍까지 메운 것(눈·코·입·볼·귀 안쪽 분홍) - 코·입 선도 갈색이라
    # 색만으로는 털과 안 갈린다 (2026-09-26 노랑·보라에서 입이 털색으로 물들었다).
    light = ((v >= 0.8) & (s <= 0.55)) | (((h <= 14) | (h >= 340)) & (v >= 0.55))   # 살색 + 귀 안쪽 분홍
    face = ndimage.binary_fill_holes(ndimage.binary_closing(light, iterations=2))
    return fur & ~ndimage.binary_dilation(face, iterations=1), hsv


def shift(color, hue, sat_mul, val_add):
    h, s, v = colorsys.rgb_to_hsv(*(c / 255.0 for c in color))
    h = h if hue is None else hue / 360.0
    r, g, b = colorsys.hsv_to_rgb(h, min(1.0, s * sat_mul), min(1.0, max(0.0, v + val_add)))
    return (round(r * 255), round(g * 255), round(b * 255))


def recolor_head(head, hue, sat_mul, val_add):
    if hue is None and sat_mul == 1.0 and val_add == 0.0:
        return head.copy()
    arr = np.array(head).astype(np.float32)
    mask, hsv = fur_mask(arr[..., :3].astype(np.uint8))
    for y, x in zip(*np.nonzero(mask & (arr[..., 3] > 0))):
        h, s, v = hsv[y, x]
        nh = h if hue is None else hue / 360.0
        r, g, b = colorsys.hsv_to_rgb(nh, min(1.0, s * sat_mul), min(1.0, max(0.0, v + val_add)))
        arr[y, x, :3] = (r * 255, g * 255, b * 255)
    return Image.fromarray(arr.astype(np.uint8), "RGBA")


def draw_cap(head, color=(214, 42, 42)):
    """빨간 모자: 정수리에 반원 + 챙. 외곽선은 머리와 같은 색."""
    w, h = head.size
    d = ImageDraw.Draw(head)
    cx, top, bottom = w * 0.55, -h * 0.02, h * 0.40       # 정수리 털까지 덮는다
    rx = w * 0.36
    d.pieslice((cx - rx, top, cx + rx, top + (bottom - top) * 2), 180, 360, fill=color + (255,), outline=OUTLINE + (255,), width=5)
    d.rounded_rectangle((cx - rx * 0.2, bottom - 9, cx + rx * 1.1, bottom + 9), 9, fill=color + (255,), outline=OUTLINE + (255,), width=5)
    d.ellipse((cx - 10, top + 2, cx + 10, top + 22), fill=(250, 245, 240, 255), outline=OUTLINE + (255,), width=4)
    return head


def draw_tufts(head, color):
    """오랑우탄 볼 털: 얼굴 양옆 아래에 삐죽한 털 뭉치."""
    w, h = head.size
    d = ImageDraw.Draw(head)
    for sx in (0.16, 0.86):
        x0 = w * sx
        pts = [(x0 - 22, h * 0.70), (x0 - 30, h * 0.92), (x0 - 10, h * 0.84), (x0 - 4, h * 1.0),
               (x0 + 8, h * 0.84), (x0 + 26, h * 0.93), (x0 + 18, h * 0.70)]
        d.polygon(pts, fill=color + (255,), outline=OUTLINE + (255,), width=4)
    return head


def icon_for(head, colors):
    """상점 아이콘: 머리 + 짧은 몸과 들어 올린 팔 (리그 모습을 대충 흉내)."""
    size = 128
    c = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(c)
    fur, belly, ol = colors["fur"], colors["belly"], colors["outline"]
    # 팔 (위로)
    for x0, x1 in ((46, 30), (82, 98)):
        d.line([(x0, 86), (x1, 18)], fill=ol + (255,), width=15)
        d.line([(x0, 86), (x1, 18)], fill=fur + (255,), width=9)
        d.ellipse((x1 - 8, 10, x1 + 8, 26), fill=belly + (255,), outline=ol + (255,), width=3)
    # 몸
    d.ellipse((38, 70, 90, 122), fill=fur + (255,), outline=ol + (255,), width=4)
    d.ellipse((50, 84, 78, 116), fill=belly + (255,))
    k = 86 / head.width
    small = head.resize((round(head.width * k), round(head.height * k)), Image.LANCZOS)
    c.alpha_composite(small, ((size - small.width) // 2 + 2, 78 - small.height))
    return c


def main():
    ids = [i["id"] for i in json.loads(ITEMS.read_text(encoding="utf-8"))["items"] if i["category"] == "monkey"]
    head, box = head_from_sheet()
    neck = ((CELL[0] // 2) - box[0] - 8, NECK_Y - box[1])        # 목 = 머리 그림 안의 회전 중심
    eyes = [(x - box[0], y - box[1]) for x, y in EYES]

    for item_id in ids:
        rule = SKINS.get(item_id)
        if rule is None:
            print(f"{item_id}: 스킨 규칙이 없다 - SKINS 에 추가할 것")
            continue
        hue, sat_mul, val_add, accessory = rule
        colors = {k: shift(v, hue, sat_mul, val_add) if k in ("fur", "furShade") else v for k, v in BASE.items()}
        h = recolor_head(head, hue, sat_mul, val_add)
        if accessory == "cap":
            h = draw_cap(h)
        elif accessory == "tuft":
            h = draw_tufts(h, colors["fur"])

        folder = OUT / item_id
        folder.mkdir(parents=True, exist_ok=True)
        h.save(folder / "head.png", optimize=True)
        icon_for(h, colors).save(folder / "icon.png", optimize=True)
        skin = {
            "$comment": "tools/make-monkey-parts.py 가 만든다 - 손으로 고치지 않는다",
            "head": {"size": list(h.size), "neck": list(neck), "eyes": [list(e) for e in eyes]},
            "colors": {k: "#%02x%02x%02x" % v for k, v in colors.items()},
        }
        (folder / "skin.json").write_text(json.dumps(skin, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        print(f"{item_id}: head {h.size[0]}x{h.size[1]}, fur {skin['colors']['fur']}")


if __name__ == "__main__":
    main()
