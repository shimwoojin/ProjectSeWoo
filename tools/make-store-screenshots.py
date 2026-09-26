"""C1 — 스토어 스크린샷을 합성한다 (docs/C1-STORE.md §3).

    1) 원판 찍기 (디버그 빌드, 창이 20초쯤 떴다가 저절로 닫힌다):
       Godot.exe --path . -- --store-shot=<원판 폴더>
    2) 합성:
       "C:\\Tools\\ComfyUI\\.venv\\Scripts\\python.exe" tools\\make-store-screenshots.py <원판 폴더>

원판은 게임이 창 텍스처를 알파째 저장한 것이다(platform/OverlayShell.StoreShots.cs) - 메인 창, 커서 장식 창,
친구 창. 그것을 **정리된 가짜 바탕화면**(배경 그림 + 코드 편집기 / 문서 창 + 작업 표시줄) 위에 올린다.
실제 바탕화면을 찍지 않으므로 파일명·메신저 같은 개인 화면이 섞이지 않는다.

게임 창 그림은 손대지 않는다(크기·색 그대로). 바탕화면은 150% 배율 모니터처럼 그린다 - 원판도 옵션 배율
1.5 로 찍었다. 시스템 커서(화살표)는 창 텍스처에 없으므로 여기서 그린다. 커서 장식 창의 가운데가 커서
끝점이다(platform/CursorLayer.cs MoveWindow).

출력: assets/_store/screenshots/NN_이름.png / .jpg (1920x1080).
"""

import csv
import os
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets", "_store", "screenshots")
W, H = 1920, 1080
TASKBAR = 72

FONTS = "C:/Windows/Fonts"
MONO = os.path.join(FONTS, "consola.ttf")
UI = os.path.join(FONTS, "malgun.ttf")
UI_BOLD = os.path.join(FONTS, "malgunbd.ttf")


def font(path, size):
    return ImageFont.truetype(path, size)


# ---------- 원판 ----------

class Raw:
    def __init__(self, folder):
        self.folder = folder
        self.pos = {}
        with open(os.path.join(folder, "manifest.tsv"), encoding="utf-8") as f:
            for row in csv.DictReader(f, delimiter="\t"):
                self.pos[(row["shot"], row["window"])] = (int(row["x"]), int(row["y"]))

    def img(self, shot, window="root"):
        return Image.open(os.path.join(self.folder, f"{shot}__{window}.png")).convert("RGBA")

    def has(self, shot, window):
        return (shot, window) in self.pos


def content_box(im):
    """창 안에서 실제로 그려진 부분(알파가 있는 곳)."""
    return im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()


# ---------- 바탕화면 ----------

def wallpaper(seed_hue=0):
    """부드러운 그라데이션 + 흐린 덩어리 몇 개. 게임 배경(하늘·언덕)과 헷갈리지 않게 차분한 색."""
    top, bottom = [((38, 52, 92), (86, 70, 128)), ((30, 70, 86), (70, 110, 120))][seed_hue]
    bg = Image.new("RGBA", (W, H))
    d = ImageDraw.Draw(bg)
    for y in range(H):
        t = y / H
        d.line([(0, y), (W, y)], fill=tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    blobs = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    bd = ImageDraw.Draw(blobs)
    for cx, cy, r, col in ((0.2, 0.3, 380, (120, 150, 230, 90)), (0.75, 0.7, 460, (230, 140, 190, 70)),
                           (0.9, 0.15, 300, (140, 220, 210, 60))):
        bd.ellipse((cx * W - r, cy * H - r, cx * W + r, cy * H + r), fill=col)
    bg.alpha_composite(blobs.filter(ImageFilter.GaussianBlur(160)))
    return bg


def taskbar(bg):
    bar = Image.new("RGBA", (W, TASKBAR), (22, 24, 32, 235))
    d = ImageDraw.Draw(bar)
    d.line([(0, 0), (W, 0)], fill=(255, 255, 255, 30))
    colors = [(90, 140, 240), (240, 190, 70), (80, 190, 120), (230, 100, 90), (160, 120, 230), (70, 180, 210)]
    size, gap = 44, 20
    x = W / 2 - (len(colors) * (size + gap) - gap) / 2
    for i, c in enumerate(colors):
        d.rounded_rectangle((x, 14, x + size, 14 + size), 10, fill=c + (255,))
        if i in (1, 3):   # 실행 중 표시
            d.rounded_rectangle((x + 14, TASKBAR - 8, x + size - 14, TASKBAR - 4), 2, fill=(200, 210, 230, 255))
        x += size + gap
    f = font(UI, 20)
    d.text((W - 40, 22), "오후 3:42", font=f, fill=(235, 238, 245, 255), anchor="ra")
    d.text((W - 40, 46), "2026-10-16", font=font(UI, 16), fill=(180, 186, 200, 255), anchor="ra")
    bg.alpha_composite(bar, (0, H - TASKBAR))


def app_window(bg, box, title, dark=True):
    """일반 앱 창 틀 (특정 제품을 흉내 내지 않는다). 안쪽 영역을 돌려준다."""
    x0, y0, x1, y1 = box
    sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((x0 + 6, y0 + 14, x1 + 6, y1 + 18), 14, fill=(0, 0, 0, 120))
    bg.alpha_composite(sh.filter(ImageFilter.GaussianBlur(18)))
    d = ImageDraw.Draw(bg)
    body, chrome, ink = ((30, 31, 36), (43, 44, 51), (200, 204, 214)) if dark else ((243, 244, 247), (225, 228, 234), (60, 64, 72))
    d.rounded_rectangle(box, 12, fill=body + (255,))
    d.rounded_rectangle((x0, y0, x1, y0 + 48), 12, fill=chrome + (255,))
    d.rectangle((x0, y0 + 30, x1, y0 + 48), fill=chrome + (255,))
    d.text((x0 + 22, y0 + 24), title, font=font(UI, 19), fill=ink + (255,), anchor="lm")
    # 닫기·최대화·최소화 - 글자로 쓰면 글꼴에 따라 두부가 나서 선으로 그린다
    cy = y0 + 24
    cx = x1 - 30
    d.line([(cx - 7, cy - 7), (cx + 7, cy + 7)], fill=ink + (255,), width=2)
    d.line([(cx - 7, cy + 7), (cx + 7, cy - 7)], fill=ink + (255,), width=2)
    d.rectangle((cx - 54 - 7, cy - 7, cx - 54 + 7, cy + 7), outline=ink + (255,), width=2)
    d.line([(cx - 108 - 7, cy), (cx - 108 + 7, cy)], fill=ink + (255,), width=2)
    return (x0, y0 + 48, x1, y1)


CODE = [
    ("k", "using"), ("t", " System.Collections.Generic;"), None,
    ("k", "public sealed class"), ("t", " OrderQueue"), None,
    ("t", "{"), None,
    ("k", "    private readonly"), ("t", " Queue<Order> _pending = "), ("k", "new"), ("t", "();"), None,
    ("k", "    private int"), ("t", " _processed;"), None,
    None,
    ("c", "    // In arrival order - unpaid orders go back to the end"), None,
    ("k", "    public"), ("t", " "), ("k", "void"), ("f", " Enqueue"), ("t", "(Order order)"), None,
    ("t", "    {"), None,
    ("k", "        if"), ("t", " (order.Status == OrderStatus.Cancelled)"), None,
    ("k", "            return"), ("t", ";"), None,
    None,
    ("t", "        _pending.Enqueue(order);"), None,
    ("t", "    }"), None,
    None,
    ("k", "    public"), ("t", " IEnumerable<Order> "), ("f", "Drain"), ("t", "("), ("k", "int"), ("t", " max)"), None,
    ("t", "    {"), None,
    ("k", "        while"), ("t", " (_pending.Count > 0 && max-- > 0)"), None,
    ("t", "        {"), None,
    ("t", "            Order next = _pending.Dequeue();"), None,
    ("k", "            if"), ("t", " (!next.IsPaid)"), None,
    ("t", "            {"), None,
    ("t", "                _pending.Enqueue(next);"), None,
    ("k", "                continue"), ("t", ";"), None,
    ("t", "            }"), None,
    None,
    ("t", "            _processed++;"), None,
    ("k", "            yield return"), ("t", " next;"), None,
    ("t", "        }"), None,
    ("t", "    }"), None,
    ("t", "}"), None,
]


def code_editor(bg, box):
    inner = app_window(bg, box, "OrderQueue.cs - 편집기")
    x0, y0, x1, y1 = inner
    d = ImageDraw.Draw(bg)
    d.rectangle((x0, y0, x0 + 250, y1 - 12), fill=(37, 38, 44, 255))
    tf = font(UI, 18)
    for i, name in enumerate(["src", "Order.cs", "OrderQueue.cs", "Payment.cs", "Program.cs",
                              "tests", "OrderQueueTests.cs", "README.md"]):
        y = y0 + 22 + i * 34
        if i == 2:
            d.rectangle((x0, y - 6, x0 + 250, y + 26), fill=(55, 60, 80, 255))
        folder = name in ("src", "tests")
        if folder:
            d.polygon([(x0 + 18, y + 8), (x0 + 30, y + 8), (x0 + 24, y + 16)], fill=(160, 166, 180, 255))
        d.text((x0 + (38 if folder else 36) + (0 if folder or name == "README.md" else 14), y), name,
               font=tf, fill=(190, 196, 210, 255))
    d.rectangle((x0 + 250, y0, x1, y0 + 44), fill=(37, 38, 44, 255))
    d.rectangle((x0 + 250, y0, x0 + 450, y0 + 44), fill=(30, 31, 36, 255))
    d.text((x0 + 270, y0 + 22), "OrderQueue.cs", font=tf, fill=(230, 232, 240, 255), anchor="lm")

    mf = font(MONO, 22)
    palette = {"k": (198, 120, 221), "t": (214, 218, 228), "f": (97, 175, 239), "c": (110, 150, 110)}
    line, x = 0, 0
    top = y0 + 64
    lh = 31
    cursor_line = 12
    d.rectangle((x0 + 250, top + cursor_line * lh - 4, x1, top + cursor_line * lh + lh - 6), fill=(40, 42, 52, 255))
    d.text((x0 + 310, top), "1", font=mf, fill=(100, 104, 116, 255), anchor="ra")
    for tok in CODE:
        if tok is None:
            line += 1
            x = 0
            if top + line * lh > y1 - 30:
                break
            d.text((x0 + 310, top + line * lh), str(line + 1), font=mf, fill=(100, 104, 116, 255), anchor="ra")
            continue
        kind, text = tok
        d.text((x0 + 340 + x, top + line * lh), text, font=mf, fill=palette[kind] + (255,))
        x += d.textlength(text, font=mf)
    # 입력 커서
    d.rectangle((x0 + 340 + 402, top + cursor_line * lh - 2, x0 + 340 + 404, top + cursor_line * lh + 26),
                fill=(230, 230, 240, 255))


DOC = [
    ("h", "3분기 제품 회의록"),
    ("m", "2026년 10월 16일 · 참석 5명"),
    ("s", "1. 지난 분기 돌아보기"),
    ("p", "신규 가입은 목표를 넘겼지만 첫 주 이탈이 예상보다 컸다. 온보딩 화면을 세 장으로 줄인 뒤"),
    ("p", "이탈이 조금 줄었고, 다음 분기에도 같은 지표로 본다."),
    ("s", "2. 이번 분기 목표"),
    ("p", "· 첫 주 유지율 5%p 올리기"),
    ("p", "· 설정 화면 정리 — 자주 쓰는 항목을 위로"),
    ("p", "· 고객 문의 응답 시간 하루 이내"),
    ("s", "3. 결정한 것"),
    ("p", "알림은 기본값을 끄고, 필요한 사람만 켜게 한다. 첫 실행 안내에 알림 설정 위치를 적는다."),
]


def document(bg, box):
    inner = app_window(bg, box, "회의록.docx - 문서", dark=False)
    x0, y0, x1, y1 = inner
    d = ImageDraw.Draw(bg)
    d.rectangle((x0, y0, x1, y1 - 12), fill=(214, 218, 226, 255))
    px0, px1 = x0 + 90, x1 - 90
    d.rectangle((px0, y0 + 30, px1, y1 - 12), fill=(255, 255, 255, 255))
    y = y0 + 90
    styles = {"h": (UI_BOLD, 38, (30, 32, 40), 64), "m": (UI, 20, (120, 124, 134), 56),
              "s": (UI_BOLD, 26, (40, 44, 56), 48), "p": (UI, 21, (60, 64, 74), 38)}
    for kind, text in DOC:
        path, size, color, step = styles[kind]
        if kind == "s":
            y += 14
        d.text((px0 + 70, y), text, font=font(path, size), fill=color + (255,))
        y += step
        if y > y1 - 40:
            break
    return (px0 + 70, y)


# ---------- 커서 ----------

def arrow(size):
    pts = [(0, 0), (0, 16), (4, 12.5), (7, 19), (9.5, 18), (6.6, 11.6), (12, 11.6)]
    s = size / 19
    im = Image.new("RGBA", (round(13 * s) + 6, round(20 * s) + 6), (0, 0, 0, 0))
    ImageDraw.Draw(im).polygon([(3 + x * s, 3 + y * s) for x, y in pts], fill=(255, 255, 255, 255),
                               outline=(0, 0, 0, 255), width=max(2, round(s * 0.8)))
    return im


def cursor(bg, raw, shot, tip):
    """커서 장식 창(가운데 = 커서 끝점) + 시스템 화살표(150% 배율 크기)."""
    if raw.has(shot, "CursorWindow"):
        deco = raw.img(shot, "CursorWindow")
        bg.alpha_composite(deco, (round(tip[0] - deco.width / 2), round(tip[1] - deco.height / 2)))
    a = arrow(46)
    bg.alpha_composite(a, (round(tip[0] - 3), round(tip[1] - 3)))


def magnifier(bg, center, radius, zoom):
    """커서 둘레를 확대한 원 - 1:1 로는 장식이 작아서. 원 안은 같은 화면을 키운 것이다."""
    cx, cy = center
    src = bg.crop((round(cx - radius / zoom), round(cy - radius / zoom), round(cx + radius / zoom), round(cy + radius / zoom)))
    src = src.resize((radius * 2, radius * 2), Image.LANCZOS)
    mask = Image.new("L", src.size, 0)
    ImageDraw.Draw(mask).ellipse((0, 0, radius * 2 - 1, radius * 2 - 1), fill=255)
    return src, mask


def place_magnifier(bg, src, mask, at, line_to):
    r = src.width // 2
    d = ImageDraw.Draw(bg)
    d.line([line_to, (at[0], at[1])], fill=(255, 255, 255, 200), width=3)
    sh = Image.new("RGBA", bg.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).ellipse((at[0] - r + 6, at[1] - r + 12, at[0] + r + 6, at[1] + r + 12), fill=(0, 0, 0, 140))
    bg.alpha_composite(sh.filter(ImageFilter.GaussianBlur(16)))
    bg.paste(src, (at[0] - r, at[1] - r), mask)
    d.ellipse((at[0] - r, at[1] - r, at[0] + r, at[1] + r), outline=(255, 255, 255, 255), width=6)
    d.ellipse((line_to[0] - 60, line_to[1] - 60, line_to[0] + 60, line_to[1] + 60), outline=(255, 255, 255, 200), width=3)


# ---------- 장면 ----------

def game_at(bg, raw, shot, right=40, bottom_gap=10):
    """메인 창을 오른쪽 아래, 그려진 부분의 바닥이 작업 표시줄 바로 위에 오게 놓는다. 창 좌상단을 돌려준다."""
    im = raw.img(shot)
    box = content_box(im)
    x = W - right - box[2]
    y = H - TASKBAR - bottom_gap - box[3]
    bg.alpha_composite(im, (x, y))
    return x, y


def friends(bg, raw, shot, spots):
    for k, spot in enumerate(spots):
        if raw.has(shot, f"Friend{k}"):
            bg.alpha_composite(raw.img(shot, f"Friend{k}"), spot)


def shot_work(raw):
    bg = wallpaper(0)
    code_editor(bg, (40, 30, 1270, H - TASKBAR - 30))
    game_at(bg, raw, "punch_1")
    cursor(bg, raw, "punch_1", (960, 600))
    taskbar(bg)
    return bg


def shot_harvest(raw):
    bg = wallpaper(1)
    document(bg, (60, 30, 1250, H - TASKBAR - 30))
    game_at(bg, raw, "harvest_2")
    cursor(bg, raw, "harvest_2", (820, 520))
    taskbar(bg)
    return bg


def shot_cursor(raw):
    bg = wallpaper(0)
    code_editor(bg, (40, 30, 1270, H - TASKBAR - 30))
    game_at(bg, raw, "shop_tab0")
    tip = (760, 420)
    cursor(bg, raw, "shop_tab0", tip)
    src, mask = magnifier(bg, (tip[0] + 6, tip[1] - 4), 200, 3.4)
    place_magnifier(bg, src, mask, (400, 640), (tip[0] + 6, tip[1] - 4))
    taskbar(bg)
    return bg


def shot_collection(raw):
    bg = wallpaper(1)
    document(bg, (60, 30, 1250, H - TASKBAR - 30))
    game_at(bg, raw, "shop_tab4")
    cursor(bg, raw, "shop_tab4", (1500, 330))
    taskbar(bg)
    return bg


def shot_lobby(raw):
    bg = wallpaper(0)
    code_editor(bg, (40, 30, 1100, H - TASKBAR - 30))
    gx, gy = game_at(bg, raw, "lobby_window")
    # 친구 창은 끌어서 아무 데나 둔다 - 로비 창 왼쪽에 세로로 세워 둔 모습
    friends(bg, raw, "lobby_window", [(1030, 50), (1030, 370), (1030, 690)])
    cursor(bg, raw, "lobby_window", (1420, 560))
    taskbar(bg)
    return bg


def shot_friends(raw):
    bg = wallpaper(1)
    document(bg, (60, 30, 1250, H - TASKBAR - 30))
    game_at(bg, raw, "lobby_2")
    friends(bg, raw, "lobby_2", [(1290, 40), (1500, 40), (1710, 40)])
    cursor(bg, raw, "lobby_2", (760, 460))
    taskbar(bg)
    return bg


def shot_upgrade(raw):
    bg = wallpaper(0)
    code_editor(bg, (40, 30, 1270, H - TASKBAR - 30))
    game_at(bg, raw, "shop_tab3")
    cursor(bg, raw, "shop_tab3", (1560, 300))
    taskbar(bg)
    return bg


SHOTS = [
    ("01_work", shot_work),
    ("02_harvest", shot_harvest),
    ("03_cursor", shot_cursor),
    ("04_collection", shot_collection),
    ("05_lobby", shot_lobby),
    ("06_friends", shot_friends),
    ("07_upgrade", shot_upgrade),
]


# ---------- 긴 설명(About This Game) 섹션 이미지 ----------

DESC_OUT = os.path.join(ROOT, "assets", "_store", "description")
DESC_W = 616   # 스팀 설명란 너비. 더 크게 올려도 이 너비로 줄여 보여 준다

# 스크린샷에서 잘라 쓸 영역 (x0, y0, x1, y1) - 섹션 제목 아래 한 장씩 (docs/C1-STORE.md §4-2)
SECTIONS = [
    ("section_cursor", "03_cursor", (190, 300, 1410, 870)),       # 커서를 꾸미세요 - 확대 원 + 커서
    ("section_upgrade", "07_upgrade", (1250, 150, 1880, 470)),    # 나무를 키우세요 - 강화 탭
    ("section_friends", "06_friends", (1262, 20, 1920, 330)),     # 친구와 같이 치세요 - 친구 창 3개
    ("section_harvest", "02_harvest", (1260, 600, 1920, 955)),   # GIF 를 못 쓸 때 "치면 친다" 대신
]

# 펀치 GIF: 원판 프레임 순서와 한 장당 시간(ms). 첫 타격 → (중간 8타 생략) → 10번째 타격에 황금 송이 낙하
GIF_FRAMES = ([("idle", 500)] + [(f"punch_{k}", 70) for k in range(6)] + [("idle", 250)]
              + [(f"harvest_{k}", 80) for k in range(6)] + [("harvest_5", 900)])
GIF_CROP = (40, 150, 530, 634)   # 원판(배율 1.5) 안에서 나무·원숭이만 - 버튼(635~)은 뺀다
GIF_HUD = (0, 0, 150, 185)       # 나무 꼭대기 옆에 걸치는 HUD 마지막 줄(도감) - 지운다


def resize_w(im, width):
    return im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)


def punch_gif(raw):
    """"치면 친다" 섹션의 움짤. 게임 원판 프레임 그대로, 배경만 바탕화면 그림 한 조각."""
    x0, y0, x1, y1 = GIF_CROP
    back = wallpaper(1).crop((1300, 300, 1300 + (x1 - x0), 300 + (y1 - y0)))
    frames, times = [], []
    for shot, ms in GIF_FRAMES:
        f = back.copy()
        src = raw.img(shot)
        src.paste((0, 0, 0, 0), GIF_HUD)
        f.alpha_composite(src.crop(GIF_CROP))
        frames.append(resize_w(f, 420).convert("RGB").quantize(colors=128, method=Image.Quantize.MEDIANCUT))
        times.append(ms)
    path = os.path.join(DESC_OUT, "section_punch.gif")
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=times, loop=0, optimize=True)
    return path


def sections(shots, raw):
    os.makedirs(DESC_OUT, exist_ok=True)
    for name, shot, box in SECTIONS:
        im = resize_w(shots[shot].crop(box), DESC_W)
        im.save(os.path.join(DESC_OUT, f"{name}.png"))
        print(f"{name:16s} {im.width}x{im.height}")
    path = punch_gif(raw)
    print(f"section_punch.gif {os.path.getsize(path) // 1024}KB")


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    raw = Raw(sys.argv[1])
    os.makedirs(OUT, exist_ok=True)
    shots = {}
    for name, make in SHOTS:
        im = make(raw).convert("RGB")
        assert im.size == (W, H)
        im.save(os.path.join(OUT, f"{name}.png"))
        im.save(os.path.join(OUT, f"{name}.jpg"), quality=92)
        shots[name] = im
        print(name)
    sections(shots, raw)


if __name__ == "__main__":
    main()
