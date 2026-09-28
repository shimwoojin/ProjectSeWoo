"""B17 — 커서 원숭이 스킨을 만든다 (docs/B17-CURSOR-REWORK.md §4-2, §6-2).

    "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-monkey-parts.py

**머리만 그림에서 가져오고 나머지(몸·팔·다리·손발·꼬리)는 게임이 그린다** (shared/Cursor/MonkeyRig.cs).
머리는 본편 원숭이(assets/entities/monkey_punch.png 대기 프레임)에서 목 위를 잘라 낸다 — 커서에 달리는 원숭이가
나무를 치는 원숭이와 같은 캐릭터여야 자연스럽다. 몸통·팔다리를 그림에서 잘라 쓰지 않는 이유: 대기 프레임은 팔이
가슴 앞에 있어서 잘라 내면 몸에 구멍이 나고, 굽은 팔은 머리 위로 뻗은 "매달림" 자세로 돌릴 수 없다. 같은 그림체
(굵은 진갈색 외곽선 + 단색 + 밝은 배)로 코드가 그리면 어떤 자세든 되고, 스킨은 색 값만 바꾸면 된다.

스킨마다 (assets/cursor/monkey/<id>/):
  head.png   머리 (털 색만 바꾼다 — 얼굴·귀 안쪽·외곽선은 그대로)
  skin.json  몸을 그릴 색 + 머리의 목·눈 좌표 + 글러브·꼬리 (B19 - 모자는 head.png 에 굽는다)
  (icon.png 는 여기서 안 만든다 - 게임이 실제 리그로 그린다: Godot.exe --path . -- --make-icons)

원숭이 목록·이름은 game/shop/items.json. 여기 SKINS 에 규칙이 없는 id 는 만들지 않는다.
"""

import colorsys
import json
import pathlib

import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
SHEET = ROOT / "assets" / "entities" / "monkey_punch.png"
OUT = ROOT / "assets" / "cursor" / "monkey"
ITEMS = ROOT / "game" / "shop" / "items.json"

CELL = (298, 324)       # 펀치 시트 한 칸
NECK_Y = 172            # 이 위가 머리 (대기 프레임 기준, 2026-09-26 격자로 잰 값)
EYES = [(160, 127), (208, 127)]   # 대기 프레임에서 두 눈의 가운데
OUTLINE = (62, 32, 18)

# 원본 털 색 (대기 프레임에서 뽑은 값). 몸을 그릴 때 기본값이다.
BASE = {"fur": (158, 96, 52), "furShade": (122, 70, 36), "belly": (250, 205, 165), "outline": OUTLINE}

# 스킨 표는 tools/monkey_skins.py 한 곳 (B19) - 본편 펀치 시트(make-body-skins.py)와 같이 쓴다.
import monkey_skins as ms  # noqa: E402

SKINS = ms.SKINS


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


def main():
    ids = [i["id"] for i in json.loads(ITEMS.read_text(encoding="utf-8"))["items"] if i["category"] == "monkey"]
    head, box = head_from_sheet()

    for item_id in ids:
        spec = SKINS.get(item_id)
        if spec is None:
            print(f"{item_id}: 스킨 규칙이 없다 - tools/monkey_skins.py 의 SKINS 에 추가할 것")
            continue
        hue, sat_mul, val_add = spec["fur"]
        colors = {k: shift(v, hue, sat_mul, val_add) if k in ("fur", "furShade") else v for k, v in BASE.items()}
        h = recolor_head(head, hue, sat_mul, val_add)

        # 모자는 머리 그림에 굽는다. 모자가 머리 밖으로 나가면 캔버스를 넓히고, 목·눈 좌표를 그만큼 옮긴다.
        # 원점 = 대기 칸 (0,0) 이 캔버스에서 오는 자리.
        origin = (-box[0], -box[1])
        if spec.get("hat"):
            hat_box = ms.hat_extent(spec["hat"])
            ux0, uy0 = min(box[0], hat_box[0]), min(box[1], hat_box[1])
            ux1, uy1 = max(box[2], hat_box[2]), max(box[3], hat_box[3])
            canvas = Image.new("RGBA", (ux1 - ux0, uy1 - uy0), (0, 0, 0, 0))
            canvas.alpha_composite(h, (box[0] - ux0, box[1] - uy0))
            h, origin = ms.put_hat(canvas, spec["hat"], (-ux0, -uy0)), (-ux0, -uy0)

        neck = ((CELL[0] // 2) - 8 + origin[0], NECK_Y + origin[1])      # 목 = 머리 그림 안의 회전 중심
        eyes = [(x + origin[0], y + origin[1]) for x, y in EYES]

        folder = OUT / item_id
        folder.mkdir(parents=True, exist_ok=True)
        h.save(folder / "head.png", optimize=True)
        skin = {
            "$comment": "tools/make-monkey-parts.py 가 만든다 - 손으로 고치지 않는다. 표는 tools/monkey_skins.py",
            "head": {"size": list(h.size), "headWidth": box[2] - box[0], "neck": list(neck), "eyes": [list(e) for e in eyes]},
            "colors": {k: "#%02x%02x%02x" % v for k, v in colors.items()},
        }
        if spec.get("glove"):
            skin["glove"] = spec["glove"]
        if spec.get("tail"):
            skin["tail"] = {k: v for k, v in spec["tail"].items() if v}
        (folder / "skin.json").write_text(json.dumps(skin, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        print(f"{item_id}: head {h.size[0]}x{h.size[1]}, fur {skin['colors']['fur']}, 부위 {ms.parts_count(spec)}")

    print("실행 중 부위 그림:", len(ms.write_runtime_parts()), "장")


if __name__ == "__main__":
    main()
