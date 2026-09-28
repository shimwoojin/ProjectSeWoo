"""C1 — 스토어 트레일러 30초를 합성·인코딩한다 (docs/C1-STORE.md §10).

    1) 원판 (디버그 빌드, 창이 1분쯤 떴다가 저절로 닫힌다. --fixed-fps 30 을 꼭 준다):
       Godot.exe --path . --fixed-fps 30 -- --trailer=<원판 폴더>
    2) 합성 + 인코딩:
       "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-store-trailer.py <원판 폴더>

원판은 게임이 매 프레임 창(메인·커서·친구)을 알파째 저장한 것이다(platform/OverlayShell.Trailer.cs). 그것을 스크린샷과 같은
가짜 바탕화면(tools/make-store-screenshots.py) 위에 놓는다. 창은 **게임 화면 왼쪽 위(contentX/Y) 기준**으로 놓는다 - 메뉴 칸이
열려 메인 창이 넓어져도 나무는 제자리에 있고, 게임이 그 기준으로 움직인 커서도 버튼 위에 온다.

소리는 여기서 만든다 - 게임에는 소리가 없다(B16 취소). 타자 소리(타건 프레임), 바나나가 늘 때 "퐁", 가벼운 배경 선율.
전부 코드로 합성한 것이라 라이선스 걱정이 없다.

출력: assets/_store/trailer/punchmonkey_trailer.mp4 (1920x1080, 30fps, H.264 CRF 18, AAC) + 썸네일 png.
"""

import csv
import importlib.util
import math
import os
import sys
from collections import defaultdict

import av
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets", "_store", "trailer")

# 스크린샷 스크립트의 바탕화면·편집기·작업 표시줄·화살표를 그대로 쓴다 - 스토어 그림과 영상이 같은 바탕화면이어야 한다.
_spec = importlib.util.spec_from_file_location("shots", os.path.join(ROOT, "tools", "make-store-screenshots.py"))
shots = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(shots)

W, H = shots.W, shots.H
FPS = 30
GAME_FRAMES = 840           # 게임 원판 (OverlayShell.Trailer.TrailerFrames)
TOTAL = 900                 # 30초 - 마지막 60 프레임은 끝장면
PANEL_W = 420               # 메뉴 칸 폭 (MenuHub.PanelWidth, 창 px)
CURSOR_TIP = shots.CURSOR_TIP
SR = 48000


def typed(f):
    """OverlayShell.Trailer.TrailerAt 의 타건 규칙을 그대로 옮긴 것 - 소리와 편집기 글자가 원숭이와 맞는다."""
    if f < 15:
        return False
    if f < 120:
        return f % 5 == 0
    if f < 270:
        return f % 6 == 0
    if f < 390:
        return f % 3 == 0
    if f < 540:
        return False
    if f < 570:
        return f % 3 == 0
    if f < 720:
        return False
    return f % 5 == 0


class Raw:
    def __init__(self, folder):
        self.folder = folder
        self.rows = defaultdict(dict)
        with open(os.path.join(folder, "frames.tsv"), encoding="utf-8") as fp:
            for r in csv.DictReader(fp, delimiter="\t"):
                self.rows[int(r["frame"])][r["window"]] = r

    def img(self, f, window):
        return Image.open(os.path.join(self.folder, f"{f:04d}__{window}.png")).convert("RGBA")


# ---------- 바탕 ----------

TYPE_LINE = "        _pending.Enqueue(order);"


def base_desktop(editor_right):
    bg = shots.wallpaper(0)
    shots.code_editor(bg, (40, 30, editor_right, H - shots.TASKBAR - 30))
    shots.taskbar(bg)
    return bg


def editor_line(bg, editor_right, n):
    """편집기 13번째 줄을 n 글자까지만 - 타자에 맞춰 글자가 늘어난다."""
    x0, y0 = 40, 30 + 48
    top = y0 + 64
    lh = 31
    row = 12
    d = ImageDraw.Draw(bg)
    d.rectangle((x0 + 250, top + row * lh - 4, editor_right - 6, top + row * lh + lh - 6), fill=(40, 42, 52, 255))
    mf = ImageFont.truetype(shots.MONO, 22)
    text = TYPE_LINE[:n]
    d.text((x0 + 340, top + row * lh), text, font=mf, fill=(214, 218, 228, 255))
    cx = x0 + 340 + d.textlength(text, font=mf) + 2
    d.rectangle((cx, top + row * lh - 2, cx + 2, top + row * lh + 26), fill=(230, 230, 240, 255))


# ---------- 카메라 (커서 장면 확대) ----------

def zoom_at(f):
    """390~569 커서 장면에서 1.0 → 1.7 → 1.0. 부드럽게 들어갔다 나온다."""
    def ease(t):
        t = min(max(t, 0.0), 1.0)
        return t * t * (3 - 2 * t)
    if f < 390 or f >= 570:
        return 1.0
    zin = ease((f - 390) / 30)
    zout = 1 - ease((f - 540) / 30)
    return 1.0 + 0.7 * min(zin, zout)


def apply_zoom(frame, center, z):
    if z <= 1.001:
        return frame
    cw, ch = W / z, H / z
    cx = min(max(center[0], cw / 2), W - cw / 2)
    cy = min(max(center[1], ch / 2), H - ch / 2)
    box = (round(cx - cw / 2), round(cy - ch / 2), round(cx + cw / 2), round(cy + ch / 2))
    return frame.crop(box).resize((W, H), Image.LANCZOS)


# ---------- 끝장면 ----------

def end_card(last, t):
    """마지막 2초: 흐려진 바탕 위에 로고 + 찜하기. t 0→1 로 스며든다."""
    blur = last.filter(ImageFilter.GaussianBlur(18))
    dark = Image.new("RGBA", (W, H), (10, 14, 28, round(150 * min(1, t * 2))))
    im = Image.alpha_composite(blur, dark)
    logo = Image.open(os.path.join(ROOT, "assets", "_store", "capsules", "library_logo.png")).convert("RGBA")
    box = logo.getchannel("A").getbbox()
    logo = logo.crop(box)
    s = 1100 / logo.width
    logo = logo.resize((round(logo.width * s), round(logo.height * s)), Image.LANCZOS)
    a = min(1, t * 2.5)
    logo.putalpha(logo.getchannel("A").point(lambda v: round(v * a)))
    im.alpha_composite(logo, ((W - logo.width) // 2, 250))
    d = ImageDraw.Draw(im)
    f1 = ImageFont.truetype(shots.UI_BOLD, 54)
    f2 = ImageFont.truetype(shots.UI, 36)
    ta = round(255 * min(1, max(0, (t - 0.25) * 2.5)))
    d.text((W // 2, 250 + logo.height + 90), "Wishlist now on Steam", font=f1, fill=(255, 236, 170, ta), anchor="mm")
    d.text((W // 2, 250 + logo.height + 160), "스팀에서 찜하기", font=f2, fill=(230, 232, 240, ta), anchor="mm")
    return im


# ---------- 소리 ----------

def click(rng):
    n = int(SR * 0.035)
    noise = rng.standard_normal(n)
    env = np.exp(-np.linspace(0, 9, n))
    body = np.sin(2 * np.pi * rng.uniform(1600, 2400) * np.arange(n) / SR) * np.exp(-np.linspace(0, 14, n))
    return (noise * 0.35 + body * 0.5) * env


def pling(freq, length=0.35):
    n = int(SR * length)
    t = np.arange(n) / SR
    tone = np.sin(2 * np.pi * freq * t) + 0.35 * np.sin(2 * np.pi * freq * 2 * t)
    return tone * np.exp(-t * 9)


def soundtrack(raw):
    """타자 소리 + 바나나가 늘 때 퐁 + 100BPM 오음계 선율(마림바 느낌). 전부 합성."""
    rng = np.random.default_rng(7)
    total = int(SR * TOTAL / FPS)
    mix = np.zeros(total)

    def add(sig, at_s, gain):
        i = int(at_s * SR)
        j = min(total, i + len(sig))
        if i < total:
            mix[i:j] += sig[: j - i] * gain

    for f in range(GAME_FRAMES):
        if typed(f):
            add(click(rng), f / FPS, 0.25)

    prev = None
    for f in range(GAME_FRAMES):
        rows = raw.rows.get(f, {})
        bal = int(rows["root"]["balance"]) if "root" in rows else prev
        if prev is not None and bal is not None and bal > prev:
            add(pling(1318.5), f / FPS, 0.35)
            add(pling(1760.0), f / FPS + 0.06, 0.25)
        prev = bal

    # 배경 선율 - C 장조 오음계, 8분음표, 4마디 반복. 끝장면에서 한 번 크게 맺는다.
    notes = [523.3, 659.3, 784.0, 880.0, 784.0, 659.3, 587.3, 659.3,
             523.3, 587.3, 659.3, 784.0, 880.0, 1046.5, 880.0, 784.0]
    bass = [130.8, 130.8, 174.6, 196.0]
    beat = 60 / 100
    t = 0.0
    k = 0
    while t < TOTAL / FPS - 2.2:
        add(pling(notes[k % len(notes)], 0.5), t, 0.10)
        if k % 4 == 0:
            add(pling(bass[(k // 8) % len(bass)], 1.2), t, 0.14)
        t += beat / 2
        k += 1
    for i, fr in enumerate([523.3, 659.3, 784.0, 1046.5]):
        add(pling(fr, 1.8), TOTAL / FPS - 2.0 + i * 0.05, 0.16)

    # 부드럽게 들어가고 나간다, 클리핑 방지.
    fade = int(SR * 0.4)
    mix[:fade] *= np.linspace(0, 1, fade)
    mix[-fade:] *= np.linspace(1, 0, fade)
    peak = np.max(np.abs(mix)) or 1
    mix = mix / peak * 0.8
    return np.stack([mix, mix]).astype(np.float32)


# ---------- 본체 ----------

def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    raw = Raw(sys.argv[1])
    os.makedirs(OUT, exist_ok=True)

    # 게임 화면 자리: 첫 프레임의 그려진 부분 오른쪽 끝을 스크린샷처럼 W-40, 아래 끝을 작업 표시줄 위에.
    r0 = raw.rows[0]["root"]
    box = shots.content_box(raw.img(0, "root"))
    tx = W - 40 - box[2]
    ty = H - shots.TASKBAR - 10 - box[3]
    ox, oy = int(r0["contentX"]), int(r0["contentY"])   # 원판의 게임 화면 자리(화면 좌표)

    def place(r):
        return tx + int(r["x"]) - ox, ty + int(r["y"]) - oy

    editor_right = tx - PANEL_W - 30
    base = base_desktop(editor_right)
    taskbar_strip = base.crop((0, H - shots.TASKBAR, W, H))
    friend_spots = [(W - 3 * 215 - 30, 30), (W - 2 * 215 - 30, 30), (W - 215 - 30, 30)]

    path = os.path.join(OUT, "punchmonkey_trailer.mp4")
    container = av.open(path, mode="w")
    vs = container.add_stream("libx264", rate=FPS)
    vs.width, vs.height = W, H
    vs.pix_fmt = "yuv420p"
    vs.options = {"crf": "18", "preset": "medium"}
    aus = container.add_stream("aac", rate=SR)
    aus.layout = "stereo"

    keystrokes = 0
    last = None
    tip = (0, 0)
    for f in range(TOTAL):
        if f < GAME_FRAMES:
            if typed(f):
                keystrokes += 1
            frame = base.copy()
            editor_line(frame, editor_right, keystrokes % (len(TYPE_LINE) + 1))
            rows = raw.rows[f]
            frame.alpha_composite(raw.img(f, "root"), place(rows["root"]))
            for k in range(3):
                name = f"Friend{k}"
                if name in rows:
                    frame.alpha_composite(raw.img(f, name), friend_spots[k])
            # 작업 표시줄은 창보다 위 - 메뉴 칸(창 높이 840)이 아래로 삐져 작업 표시줄을 덮지 않게.
            # 실제로는 셸이 칸을 열 때 창을 작업 영역 안으로 민다(OverlayShell.SetSidePanel).
            frame.alpha_composite(taskbar_strip, (0, H - shots.TASKBAR))
            if "CursorWindow" in rows:
                cx, cy = place(rows["CursorWindow"])
                frame.alpha_composite(raw.img(f, "CursorWindow"), (cx, cy))
                tip = (cx + CURSOR_TIP[0], cy + CURSOR_TIP[1])
            frame.alpha_composite(shots.arrow(46), (round(tip[0] - 3), round(tip[1] - 3)))
            frame = apply_zoom(frame, (tip[0] - 10, tip[1] + 90), zoom_at(f))
            last = frame
        else:
            frame = end_card(last, (f - GAME_FRAMES) / (TOTAL - GAME_FRAMES))

        if f == 300:
            frame.convert("RGB").save(os.path.join(OUT, "thumbnail.png"))
        vf = av.VideoFrame.from_image(frame.convert("RGB"))
        for pkt in vs.encode(vf):
            container.mux(pkt)
        if f % 90 == 0:
            print(f"frame {f}/{TOTAL}")

    for pkt in vs.encode():
        container.mux(pkt)

    audio = soundtrack(raw)
    step = 1024
    for i in range(0, audio.shape[1], step):
        chunk = np.ascontiguousarray(audio[:, i:i + step])
        af = av.AudioFrame.from_ndarray(chunk, format="fltp", layout="stereo")
        af.sample_rate = SR
        af.pts = i
        for pkt in aus.encode(af):
            container.mux(pkt)
    for pkt in aus.encode():
        container.mux(pkt)
    container.close()
    print(f"{path}  {os.path.getsize(path) // 1024} KB")


if __name__ == "__main__":
    main()
