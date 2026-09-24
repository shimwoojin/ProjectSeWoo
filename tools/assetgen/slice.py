"""B8 시트 슬라이서 — 받아온 스프라이트 시트를 Godot 이 쓸 모양으로 자른다.

    usage: python slice.py [--src <path>] [--dry-run]

엔티티 아트는 '생성' 이 아니라 '받아서 자르기' 가 정식 경로다 (2026-09-21 판단 —
2D 애니메이션 스프라이트는 GPT 쪽이 로컬 ComfyUI 보다 낫다). gen.py 는 커서 장식
16종처럼 낱장이 필요한 쪽에 남는다.

**프레임 위치를 숫자로 박지 않는다.** 알파 투영으로 찾는다 — 시트를 새로 받아
여백이 달라져도 같은 명령이 그대로 돈다. 그게 "이미지는 언제든 갈아끼울 수 있어야
한다" 는 요구의 구현이다.

산출물 옆에 .frames.json 을 남긴다. 프레임 수·칸 크기를 씬(.tscn)과 사람이 같은
값으로 봐야 하기 때문이다.
"""
import argparse, json, pathlib, sys

import numpy as np
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
MANIFEST = pathlib.Path(__file__).with_name("assets.manifest.json")

ALPHA_CUT = 32
MIN_RUN = 20        # 이보다 좁은 덩어리는 부스러기로 본다


def runs(flags, min_len):
    """True 가 이어지는 구간을 [(시작, 끝)] 으로."""
    out, start = [], None
    for i, v in enumerate(flags):
        if v and start is None:
            start = i
        elif not v and start is not None:
            if i - start >= min_len:
                out.append((start, i - 1))
            start = None
    if start is not None and len(flags) - start >= min_len:
        out.append((start, len(flags) - 1))
    return out


def load(src):
    im = Image.open(src).convert("RGBA")
    return im, np.array(im.getchannel("A")) > ALPHA_CUT


def merge_runs(cols, n):
    """덩어리가 프레임 수보다 많으면 가장 좁은 틈부터 이어 붙여 n 개로 줄인다.

    이펙트 시트는 한 프레임이 여러 조각(흩어진 파편)으로 나뉜다 - 조각 사이 틈은
    프레임 사이 틈보다 훨씬 좁으므로, 좁은 틈부터 메우면 프레임 단위로 모인다.
    """
    cols = list(cols)
    while len(cols) > n:
        gaps = [cols[i + 1][0] - cols[i][1] for i in range(len(cols) - 1)]
        i = gaps.index(min(gaps))
        cols[i:i + 2] = [(cols[i][0], cols[i + 1][1])]
    return cols


def slice_strip_centroid(spec, im, mask, cols, y0, y1, dry):
    """프레임마다 알파 무게중심을 칸 가운데에 맞춰 균등 그리드로 다시 깐다.

    원숭이처럼 "몸이 제자리에 있고 팔만 움직이는" 시트는 칸 기준 상대 위치를
    보존해야 하지만(아래 slice_strip), **타격 이펙트는 모든 프레임이 한 점(맞은 자리)
    에서 터져야 한다.** 받은 시트는 프레임 중심이 칸마다 수십 px 씩 어긋나 있어서
    그대로 쓰면 폭발이 옆으로 떨며 번진다.
    """
    n, margin = spec["frames"], spec.get("margin", 8)
    alpha = np.array(im.getchannel("A"), dtype=np.float64)

    frames, centers = [], []
    for x0, x1 in cols:
        w = alpha[y0:y1 + 1, x0:x1 + 1] * mask[y0:y1 + 1, x0:x1 + 1]
        ys, xs = np.indices(w.shape)
        total = w.sum()
        centers.append(((xs * w).sum() / total, (ys * w).sum() / total))
        frames.append(im.crop((x0, y0, x1 + 1, y1 + 1)))

    half_w = max(max(cx, f.width - cx) for f, (cx, _) in zip(frames, centers))
    half_h = max(max(cy, f.height - cy) for f, (_, cy) in zip(frames, centers))
    cell_w = int(np.ceil(half_w * 2)) + margin * 2
    cell_h = int(np.ceil(half_h * 2)) + margin * 2

    if dry:
        print(f"    would write {spec['out']}  {n}프레임 x {cell_w}x{cell_h} (무게중심 정렬)")
        return

    sheet = Image.new("RGBA", (cell_w * n, cell_h), (0, 0, 0, 0))
    for i, (frame, (cx, cy)) in enumerate(zip(frames, centers)):
        sheet.alpha_composite(frame, (i * cell_w + round(cell_w / 2 - cx), round(cell_h / 2 - cy)))

    write_strip(spec, sheet, n, cell_w, cell_h)


def write_strip(spec, sheet, n, cell_w, cell_h):
    out_path = ROOT / spec["out"]
    out_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out_path, optimize=True)
    meta = {"frames": n, "cell": [cell_w, cell_h], "hframes": n, "vframes": 1,
            "source": spec["src"]}
    # newline="\n": 저장소가 LF 다. 기본값으로 두면 윈도우에서 CRLF 로 나가
    # 파일을 다시 뽑을 때마다 줄끝만 바뀐 diff 가 생긴다.
    out_path.with_suffix(".frames.json").write_text(
        json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"    {spec['out']}  {n}프레임 x {cell_w}x{cell_h}  "
          f"{out_path.stat().st_size / 1024:.1f}KB")


def slice_strip(spec, dry):
    """가로 1행 n프레임 → 균등 그리드로 재조판.

    원본이 정확한 균등 그리드가 아니다. 프레임마다 팔이 칸 경계를 몇 px 넘어서,
    그대로 hframes 를 걸면 **옆 프레임의 팔이 비쳐 들어온다.** 반대로 프레임마다
    bbox 로 잘라 가운데 맞추면 몸 위치가 프레임마다 달라져 **애니메이션이 덜덜 떤다.**

    그래서 칸 기준 상대 위치(off)를 보존한 채 칸 너비만 넓혀서 다시 조판한다.
    상대 위치가 곧 그 프레임의 움직임이므로, 보존하면 원본 모션이 그대로 남는다.
    """
    src = ROOT / spec["src"]
    im, mask = load(src)
    n, margin = spec["frames"], spec.get("margin", 8)

    ys = runs(mask.any(1), MIN_RUN)
    if not ys:
        raise ValueError("알파가 비었다")
    y0, y1 = ys[0][0], ys[-1][1]

    cols = runs(mask.any(0), MIN_RUN)
    if len(cols) > n:
        cols = merge_runs(cols, n)
    if len(cols) != n:
        # 프레임끼리 붙어 있으면 덩어리가 덜 잡힌다. 균등 분할로 되돌린다.
        print(f"    경고: 덩어리 {len(cols)}개 != frames {n}개 — 균등 분할로 처리한다")
        pitch = im.width / n
        cols = [(round(i * pitch), round((i + 1) * pitch) - 1) for i in range(n)]

    if spec.get("align") == "centroid":
        slice_strip_centroid(spec, im, mask, cols, y0, y1, dry)
        return

    pitch = im.width / n
    offs = [x0 - i * pitch for i, (x0, _) in enumerate(cols)]
    widths = [x1 - x0 + 1 for x0, x1 in cols]

    shift = max(0.0, -min(offs))                       # 음수 오프셋이 있으면 전부 민다
    cell_w = int(np.ceil(max(o + w for o, w in zip(offs, widths)) + shift)) + margin * 2
    cell_h = (y1 - y0 + 1) + margin * 2

    out_path = ROOT / spec["out"]
    if dry:
        print(f"    would write {spec['out']}  {n}프레임 x {cell_w}x{cell_h}")
        return

    sheet = Image.new("RGBA", (cell_w * n, cell_h), (0, 0, 0, 0))
    for i, ((x0, x1), off) in enumerate(zip(cols, offs)):
        frame = im.crop((x0, y0, x1 + 1, y1 + 1))
        sheet.alpha_composite(frame, (i * cell_w + margin + round(off + shift), margin))

    write_strip(spec, sheet, n, cell_w, cell_h)


def slice_atlas(spec, dry):
    """행/열로 흩어진 부품 → 낱장 PNG. 이름은 매니페스트의 rows 가 준다."""
    src = ROOT / spec["src"]
    im, mask = load(src)
    margin = spec.get("margin", 6)
    out_dir = ROOT / spec["out_dir"]

    bands = runs(mask.any(1), MIN_RUN)
    names = spec["rows"]
    if len(bands) != len(names):
        raise ValueError(f"행 {len(bands)}개인데 이름은 {len(names)}줄이다")

    for (y0, y1), row_names in zip(bands, names):
        cols = runs(mask[y0:y1 + 1].any(0), MIN_RUN)
        if len(cols) != len(row_names):
            raise ValueError(
                f"y={y0}..{y1} 행: 부품 {len(cols)}개인데 이름은 {len(row_names)}개다")
        for (x0, x1), name in zip(cols, row_names):
            part = im.crop((x0, y0, x1 + 1, y1 + 1))
            bbox = part.getchannel("A").getbbox()      # 행 높이가 아니라 부품 높이로
            part = part.crop(bbox)
            canvas = Image.new("RGBA",
                               (part.width + margin * 2, part.height + margin * 2),
                               (0, 0, 0, 0))
            canvas.alpha_composite(part, (margin, margin))
            rel = f"{spec['out_dir']}/{name}.png"
            if dry:
                print(f"    would write {rel}  {canvas.width}x{canvas.height}")
                continue
            out_dir.mkdir(parents=True, exist_ok=True)
            canvas.save(out_dir / f"{name}.png", optimize=True)
            print(f"    {rel}  {canvas.width}x{canvas.height}  "
                  f"{(out_dir / f'{name}.png').stat().st_size / 1024:.1f}KB")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", help="이 시트 하나만 (매니페스트의 src 와 같은 경로)")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    man = json.loads(MANIFEST.read_text(encoding="utf-8"))
    sheets = [s for s in man.get("sheets", [])
              if not args.src or s["src"] == args.src.replace("\\", "/")]
    if not sheets:
        raise SystemExit("매니페스트의 sheets 에서 고른 항목이 없다.")

    failed = 0
    for spec in sheets:
        src = ROOT / spec["src"]
        print(f"  {spec['src']} ({spec['kind']})")
        if not src.exists():
            print(f"    없음 — 건너뛴다", file=sys.stderr)
            failed += 1
            continue
        try:
            (slice_strip if spec["kind"] == "strip" else slice_atlas)(spec, args.dry_run)
        except ValueError as ex:
            print(f"    실패: {ex}", file=sys.stderr)
            failed += 1

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
