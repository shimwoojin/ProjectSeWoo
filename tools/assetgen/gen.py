"""B8 에셋 생성기 — ComfyUI(SDXL + 스타일 LoRA) → 배경 제거 → res://assets/ 에 직접 배치.

    usage: python gen.py [--only <group>] [--id <asset_id>] [--force] [--dry-run]

왜 ComfyUI 인가: 로컬에서 돈다. 장당 22초에 API 요금이 없고, 시드를 고정하면 같은
입력이 같은 그림을 낸다. 상용 출시(§7-2)에서 외부 생성 서비스에 의존하면 그 서비스의
약관이 우리 에셋의 라이선스가 된다 — 라이선스 표는 docs/B8-ASSET-PIPELINE.md §4.

**배경 제거는 INSPYRENET(MIT) 을 쓴다. RMBG-2.0 을 쓰면 안 된다 — CC BY-NC 라
상용 불가다.** 이 한 줄이 이 파일에서 가장 비싼 정보다.
"""
import argparse, json, pathlib, sys, time, urllib.error, urllib.request

URL = "http://127.0.0.1:8188"
ROOT = pathlib.Path(__file__).resolve().parents[2]
MANIFEST = pathlib.Path(__file__).with_name("assets.manifest.json")

# 스타일 공통. 항목별 추가 금지어는 매니페스트의 neg_extra 가 뒤에 붙는다.
#
# cartoon 의 negative 에 'drop shadow' 와 'ground' 계열이 들어간 것이 핵심이다.
# 우리 게임은 **바탕화면 위에 뜨는 투명 오버레이**라, 스프라이트 밑에 깔린 땅이나
# 그림자가 알파로 살아남으면 벽지 위에 회색 얼룩으로 찍힌다. 일반 게임이었으면
# 배경에 묻혀서 안 보였을 결함이다.
STYLES = {
    "cartoon": dict(
        lora="StickersRedmond.safetensors", strength=0.7,
        pos="cute flat cartoon game asset, 2d vector illustration, bold dark outline, "
            "simple cel shading, vibrant colors, centered, full body, plain white background",
        neg="sticker border, white outline border, drop shadow, cast shadow, ground, grass, "
            "soil, floor, pedestal, photo, realistic, 3d render, text, watermark, blurry, "
            "multiple, cropped"),
    "pixel": dict(
        lora="pixel-art-xl.safetensors", strength=1.0,
        pos="pixel art, cute game sprite, clean pixel outline, limited palette, flat colors, "
            "centered, full body, plain white background",
        neg="drop shadow, cast shadow, ground, grass, soil, floor, pedestal, 3d render, "
            "realistic, photo, blurry, smooth gradient, text, watermark, multiple, cropped"),
}


def workflow(style, entry):
    s = STYLES[style]
    neg = s["neg"] + (", " + entry["neg_extra"] if entry.get("neg_extra") else "")
    return {
        "1": {"class_type": "CheckpointLoaderSimple",
              "inputs": {"ckpt_name": "sd_xl_base_1.0.safetensors"}},
        "2": {"class_type": "LoraLoader",
              "inputs": {"model": ["1", 0], "clip": ["1", 1], "lora_name": s["lora"],
                         "strength_model": s["strength"], "strength_clip": s["strength"]}},
        "3": {"class_type": "CLIPTextEncode",
              "inputs": {"clip": ["2", 1], "text": f"{entry['prompt']}, {s['pos']}"}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 1], "text": neg}},
        "5": {"class_type": "EmptyLatentImage",
              "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},
        "6": {"class_type": "KSampler",
              "inputs": {"model": ["2", 0], "positive": ["3", 0], "negative": ["4", 0],
                         "latent_image": ["5", 0], "seed": entry["seed"], "steps": 25,
                         "cfg": 7.0, "sampler_name": "dpmpp_2m", "scheduler": "karras",
                         "denoise": 1.0}},
        "7": {"class_type": "VAEDecode", "inputs": {"samples": ["6", 0], "vae": ["1", 2]}},
        # INSPYRENET (MIT). RMBG-2.0 은 CC BY-NC 라 상용 불가 — 위 독스트링 참고.
        "9": {"class_type": "RMBG",
              "inputs": {"image": ["7", 0], "model": "INSPYRENET", "sensitivity": 1.0,
                         "process_res": 1024, "mask_blur": 0, "mask_offset": 0,
                         "invert_output": False, "refine_foreground": True,
                         "background": "Alpha", "background_color": "#222222"}},
        "10": {"class_type": "SaveImage",
               "inputs": {"images": ["9", 0], "filename_prefix": f"punchmonkey/{entry['id']}_alpha"}},
    }


def run(graph):
    """ComfyUI 에 큐를 넣고 끝날 때까지 기다린다. 산출 파일의 절대경로 목록을 돌려준다."""
    req = urllib.request.Request(
        URL + "/prompt", data=json.dumps({"prompt": graph}).encode(),
        headers={"Content-Type": "application/json"})
    pid = json.load(urllib.request.urlopen(req))["prompt_id"]
    started = time.time()
    while True:
        hist = json.load(urllib.request.urlopen(f"{URL}/history/{pid}"))
        if pid not in hist:
            time.sleep(2)
            continue
        status = hist[pid]["status"]
        errs = [m[1].get("exception_message")
                for m in status.get("messages", []) if m[0] == "execution_error"]
        if errs:
            raise RuntimeError("; ".join(str(e) for e in errs))
        outs = [OUTPUT_DIR / i["subfolder"] / i["filename"]
                for o in hist[pid]["outputs"].values() for i in o.get("images", [])]
        return outs, round(time.time() - started, 1)


def comfy_output_dir():
    """ComfyUI 가 실제로 쓰는 output 폴더. 서버에 물어봐서 경로를 추측하지 않는다."""
    stats = json.load(urllib.request.urlopen(f"{URL}/system_stats"))
    # /system_stats 는 경로를 안 준다. ComfyUI 표준 배치를 쓰되 존재는 확인한다.
    guess = pathlib.Path("C:/Tools/ComfyUI/output")
    if not guess.is_dir():
        raise SystemExit(f"ComfyUI output 폴더를 찾지 못했다: {guess}")
    return guess


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="이 그룹만 (entities | cursor)")
    ap.add_argument("--id", help="이 id 하나만")
    ap.add_argument("--force", action="store_true", help="이미 있어도 다시 생성")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    man = json.loads(MANIFEST.read_text(encoding="utf-8"))
    style = man["style"]

    entries = []
    for group, items in man["groups"].items():
        if args.only and group != args.only:
            continue
        for e in items:
            if args.id and e["id"] != args.id:
                continue
            entries.append((group, e))
    if not entries:
        raise SystemExit("매니페스트에서 고른 항목이 없다. --only / --id 를 확인해라.")

    # 이미 있는 것은 건너뛴다. 이것이 "한 장만 다시 뽑기" 가 성립하는 이유다 -
    # 마음에 안 드는 항목만 --force --id 로 돌리면 나머지 그림은 안 흔들린다.
    todo = [(g, e) for g, e in entries
            if args.force or not (ROOT / e["out"]).exists()]
    skipped = len(entries) - len(todo)
    print(f"[gen] style={style}  대상 {len(entries)}개  생성 {len(todo)}개  건너뜀 {skipped}개")

    if args.dry_run:
        for g, e in todo:
            print(f"  would gen  {g}/{e['id']:14s} seed={e['seed']:<5} -> {e['out']}")
        return 0

    if not todo:
        return 0

    try:
        urllib.request.urlopen(f"{URL}/system_stats", timeout=5)
    except (urllib.error.URLError, OSError):
        raise SystemExit(
            f"ComfyUI 가 {URL} 에 없다. C:\\Tools\\start_comfy.bat 를 먼저 띄워라.")

    global OUTPUT_DIR
    OUTPUT_DIR = comfy_output_dir()

    raw_dir = pathlib.Path(__file__).with_name("_raw")
    raw_dir.mkdir(exist_ok=True)

    failed = []
    for i, (group, e) in enumerate(todo, 1):
        print(f"  [{i}/{len(todo)}] {e['id']} (seed {e['seed']}) ...", end="", flush=True)
        try:
            outs, secs = run(workflow(style, e))
        except RuntimeError as ex:
            print(f" 실패: {ex}")
            failed.append(e["id"])
            continue
        if not outs:
            print(" 실패: 산출 이미지가 없다")
            failed.append(e["id"])
            continue
        # 원본 1024 알파를 _raw/ 에 남긴다. 후처리 수치를 바꿔 다시 돌릴 때
        # 22초짜리 생성을 또 하지 않기 위해서다 (_raw/ 는 git 에 안 올린다).
        dst = raw_dir / f"{e['id']}.png"
        dst.write_bytes(outs[-1].read_bytes())
        print(f" {secs}s -> {dst.name}")

    if failed:
        print(f"[gen] 실패 {len(failed)}개: {', '.join(failed)}", file=sys.stderr)
        return 1
    print(f"[gen] 완료. 후처리: python postprocess.py")
    return 0


if __name__ == "__main__":
    sys.exit(main())
