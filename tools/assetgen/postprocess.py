"""B8 후처리 — _raw/ 의 1024 알파 PNG → 정리 → 리사이즈 → res://assets/ 배치.

    usage: python postprocess.py [--only <group>] [--id <asset_id>]

생성(gen.py)과 후처리를 나눈 이유: 생성은 장당 22초고 후처리는 0.1초다. 피벗이나
알파 임계값을 만지는 동안 22초짜리를 다시 돌릴 이유가 없다. gen.py 가 원본을
_raw/ 에 남겨 두므로 이 스크립트만 몇 번이고 다시 돌릴 수 있다.

단계:
  1. 알파 임계 — 반투명 가장자리를 자른다
  2. 부스러기 제거 — 본체에서 떨어져 나온 작은 조각을 지운다 (아래 §부스러기)
  3. 알파 bbox 크롭
  4. LANCZOS 다운스케일 (카툰용. 픽셀아트면 BOX + 팔레트 고정이 따로 필요하다)
  5. 정사각 캔버스에 피벗대로 배치
"""
import argparse, json, pathlib, sys
from collections import deque

import numpy as np
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
MANIFEST = pathlib.Path(__file__).with_name("assets.manifest.json")
RAW = pathlib.Path(__file__).with_name("_raw")

ALPHA_CUT = 140          # 이 아래는 배경으로 본다
SPECK_RATIO = 0.02       # 가장 큰 덩어리의 2% 미만인 조각은 버린다
LABEL_RES = 256          # 덩어리 판정은 축소본에서 한다 (1024² 를 순수 파이썬으로 훑으면 느리다)


def drop_specks(alpha):
    """본체에서 떨어져 나온 부스러기를 지운다.

    INSPYRENET 이 'sticker border' 조각이나 배경의 얼룩을 남기는 일이 있다. 일반
    게임이면 배경에 묻히는데, 우리는 **바탕화면 위에 뜨는 투명 오버레이**라 그
    한 조각이 벽지 위에 그대로 찍힌다.

    가장 큰 덩어리만 남기지 않고 비율로 자르는 이유: 원숭이 꼬리나 반짝임처럼
    **본체와 떨어져 있는 것이 정상인 부품**이 있다. 가장 큰 것만 남기면 그게 날아간다.
    """
    h, w = alpha.shape
    small = np.array(Image.fromarray(alpha).resize((LABEL_RES, LABEL_RES), Image.NEAREST)) > 0
    labels = np.zeros_like(small, dtype=np.int32)
    sizes = [0]

    for y in range(LABEL_RES):
        for x in range(LABEL_RES):
            if not small[y, x] or labels[y, x]:
                continue
            tag = len(sizes)
            area = 0
            q = deque([(y, x)])
            labels[y, x] = tag
            while q:
                cy, cx = q.popleft()
                area += 1
                for ny, nx in ((cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1)):
                    if 0 <= ny < LABEL_RES and 0 <= nx < LABEL_RES \
                            and small[ny, nx] and not labels[ny, nx]:
                        labels[ny, nx] = tag
                        q.append((ny, nx))
            sizes.append(area)

    if len(sizes) <= 2:                      # 덩어리가 0개나 1개면 지울 것이 없다
        return alpha, 0

    biggest = max(sizes[1:])
    keep = np.isin(labels, [i for i in range(1, len(sizes))
                            if sizes[i] >= biggest * SPECK_RATIO])
    dropped = sum(1 for i in range(1, len(sizes)) if sizes[i] < biggest * SPECK_RATIO)

    mask = np.array(Image.fromarray((keep * 255).astype(np.uint8))
                    .resize((w, h), Image.NEAREST))
    return np.where(mask > 0, alpha, 0).astype(np.uint8), dropped


def process(src, dst, px, pivot):
    im = Image.open(src).convert("RGBA")
    rgb = np.array(im)[:, :, :3]
    alpha = np.array(im.getchannel("A"))

    alpha = np.where(alpha >= ALPHA_CUT, alpha, 0).astype(np.uint8)
    alpha, dropped = drop_specks(alpha)

    im = Image.fromarray(np.dstack([rgb, alpha]), "RGBA")
    bbox = im.getchannel("A").getbbox()
    if bbox is None:
        raise ValueError("알파가 전부 비었다 — 배경 제거가 본체까지 지웠다")
    im = im.crop(bbox)

    w, h = im.size
    k = px / max(w, h)
    im = im.resize((max(1, round(w * k)), max(1, round(h * k))), Image.LANCZOS)

    out = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    x = (px - im.width) // 2
    y = px - im.height if pivot == "bottom" else (px - im.height) // 2
    out.paste(im, (x, y), im)

    dst.parent.mkdir(parents=True, exist_ok=True)
    out.save(dst, optimize=True)
    return out.size, dropped, dst.stat().st_size


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    ap.add_argument("--id")
    args = ap.parse_args()

    man = json.loads(MANIFEST.read_text(encoding="utf-8"))
    entries = [(g, e) for g, items in man["groups"].items() for e in items
               if not (args.only and g != args.only) and not (args.id and e["id"] != args.id)]

    missing, done = [], 0
    for group, e in entries:
        src = RAW / f"{e['id']}.png"
        if not src.exists():
            missing.append(e["id"])
            continue
        dst = ROOT / e["out"]
        try:
            size, dropped, nbytes = process(src, dst, e["px"], e["pivot"])
        except ValueError as ex:
            print(f"  {e['id']:14s} 실패: {ex}", file=sys.stderr)
            missing.append(e["id"])
            continue
        note = f"  부스러기 {dropped}개 제거" if dropped else ""
        print(f"  {e['id']:14s} -> {e['out']}  {size[0]}x{size[1]}  "
              f"{nbytes / 1024:.1f}KB{note}")
        done += 1

    print(f"[post] {done}개 처리")
    if missing:
        print(f"[post] _raw 에 없음 {len(missing)}개: {', '.join(missing)}\n"
              f"       python gen.py 를 먼저 돌려라.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
