# C1 — 스토어 페이지 소재 (캡슐 아트 · 스크린샷 · 설명문)

관련: [스팀 설정](STEAM-CONFIG.md) §2 / [C2 개인정보 문구](C2-PRIVACY.md) / [A15 도전과제](A15-ACHIEVEMENTS.md) §6 /
[확정 기획서](기획확정-일감분배-260907.md) §9 C1·C2, §11

> **"출시 예정" 공개 마지노선은 10/2다** (출시 가능일 10/16 의 2주 전). 기획서는 C1 을 W4 에 뒀지만
> 스토어 페이지를 열려면 최소 소재가 10/2 전에 있어야 한다. 트레일러는 공개 뒤에 붙여도 된다.

---

## 0. 상태 (2026-09-26)

| 항목 | 상태 |
|---|---|
| 캡슐 아트 10종 | ☑ **초안** — `tools/make-store-capsules.py` → `assets/_store/capsules/` (§2) |
| 스크린샷 5장 | ☑ **초안 7장** — `--store-shot` 원판 + `tools/make-store-screenshots.py` → `assets/_store/screenshots/` (§3) |
| 짧은 설명 · 긴 설명 (한/영) | ☑ 초안 (§4) |
| 태그 | ☑ 제안 (§5) |
| 시스템 요구사항 | ☑ STEAM-CONFIG §2-4 그대로 |
| 가격 | ☐ **정해야 한다** (§6) |
| 트레일러 30초 | ☐ 공개 뒤에 해도 된다 |
| LoRA 라이선스 (B8 §6) | ☐ **공개 전에 닫아야 한다** — 캡슐에 커서 장식(생성 그림)이 들어간다 |
| 문의 이메일 | ☐ C2 §4-2 와 같은 값 |

---

## 1. 무엇을 보여 줄 것인가

스토어에서 3초 안에 전달할 것은 셋이다.

1. **바탕화면에 산다** — 다른 일을 하면서 켜 두는 게임이다 (STEAM-CONFIG §2-3: 숨기지 않는다)
2. **타자를 치면 원숭이가 나무를 친다** — 입력 = 펀치 = 바나나
3. **바나나로 커서를 꾸민다** — 이 게임의 차별점. GIF 한 장으로 설명되는 장면 (기획서 §1-3)

캡슐은 2·3 (원숭이 펀치 + 꾸민 커서 + 키캡), 스크린샷은 1 을 맡는다.

---

## 2. 캡슐 아트

```
"C:\Tools\ComfyUI\.venv\Scripts\python.exe" tools\make-store-capsules.py
```

도전과제 아이콘(A15 §6)과 같은 방식이다 — **손으로 그리지 않고 게임 스프라이트를 배치한다.** 원숭이 펀치
시트(프레임 2), 바나나 나무, 바나나, 타격 이펙트(프레임 1), 커서 장식(매달림 `monkey_01`, 잔상 `spark_01`),
로고는 Pretendard Bold. 배경(하늘·구름·언덕)은 코드로 그린다.

| 파일 | 크기 | 올리는 곳 | 구도 |
|---|---|---|---|
| `header_capsule` | 920x430 | 스토어 — 헤더 캡슐 | 왼쪽 로고, 오른쪽 펀치 장면, 커서·키캡 |
| `small_capsule` | 462x174 | 스토어 — 작은 캡슐 | 한 줄 로고 + 원숭이. **로고가 읽히는 게 전부** |
| `main_capsule` | 1232x706 | 스토어 — 메인 캡슐 | 헤더와 같음 |
| `vertical_capsule` | 748x896 | 스토어 — 세로 캡슐 | 위 로고, 아래 장면 |
| `page_background` | 1438x810 | 스토어 — 페이지 배경 (선택) | 하늘·언덕만, 흐리게 |
| `library_capsule` | 600x900 | 라이브러리 — 캡슐 | 세로와 같음 |
| `library_header` | 920x430 | 라이브러리 — 헤더 | 헤더 캡슐과 같음 |
| `library_hero` | 3840x1240 | 라이브러리 — 히어로 | **글자 없음** (스팀 규칙). 장면은 가운데~오른쪽 |
| `library_logo` | 1280x720 | 라이브러리 — 로고 | 투명 PNG, 히어로 위에 얹힌다 |
| `community_icon` | 184x184 | 커뮤니티 아이콘 | 원숭이 얼굴 |

`.png` 와 `.jpg` 를 같이 뽑는다(`library_logo` 는 투명이라 PNG 만). 업로드 화면이 요구하는 규격이 표와
다르면 스크립트의 `SPECS` 만 고쳐 다시 뽑는다.

**스팀 규칙으로 지킨 것**: 모든 캡슐에 게임 이름이 읽힌다(작은 캡슐 포함). 리뷰·수상·할인·"지금 출시"
같은 문구는 없다. 히어로에는 글자가 없다.

**알고 있는 약점**

- **스프라이트를 키워서 쓴다.** 원숭이 원본이 칸당 298x324 라 메인 캡슐에서 약 1.0배, 히어로에서 약
  1.8배다. 히어로는 가까이 보면 무르다. 고해상도 원본을 받으면 `assets/_source/` 에 넣고 다시 뽑는다
- 원숭이 발밑 회색 그림자 타원은 원본 시트에 들어 있는 것이다(B8 §6-2). 캡슐에서는 땅을 딛는 느낌이라 둔다
- 디자이너가 없어서 로고는 글꼴 + 외곽선이다. 전용 로고를 만들 여유가 생기면 `logo()` 만 갈아 끼운다

---

## 3. 스크린샷

규격: **1920x1080, 16:9.** 게임 창만 찍으면 "바탕화면 게임" 이 안 보이고, 실제 바탕화면을 찍으면 개인 화면
(파일명·메신저·브라우저 탭)이 섞인다. 그래서 **게임이 자기 창을 알파째 PNG 로 저장하고, 그것을 정리된 가짜
바탕화면 위에 합성한다.** 게임 그림은 손대지 않는다.

```
# 1) 원판 - 디버그 빌드 무인 실행. 창이 20초쯤 떴다가 저절로 닫힌다. 세이브는 안 쓴다.
C:\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --store-shot=<원판 폴더>

# 2) 합성
"C:\Tools\ComfyUI\.venv\Scripts\python.exe" tools\make-store-screenshots.py <원판 폴더>
```

### 3-1. 원판 — `--store-shot` (`platform/OverlayShell.StoreShots.cs`)

목 상태를 매번 같게 세운다: 잔액 1,284 · 누적 48,213타(Lv.21) · 강화(황금 2 · 가지 2 · 빨리 익기 1) · 송이
5개(황금 익음 / 보통 익음 / 황금 익음 / 자라는 중 / 막 달림) · 장식 11/16 보유, 장착 빨간 모자 원숭이 + 노란
반짝임 + 금빛 고리. 그리고 장면마다 **보이는 창 전부**(메인·커서 장식·친구 창)를 저장한다.

| 원판 | 장면 |
|---|---|
| `idle`, `punch_0~5` | 가만히 / 익은 송이를 치는 순간 연속 |
| `harvest_0~5` | 10번째 타격 — 황금 송이가 떨어지는 연속 |
| `shop_tab0~4` | 상점 탭 5개 (매달림 · 잔상 · 바닥 · 강화 · 도감) |
| `lobby_0~3`, `lobby_window` | 가짜 친구 3명(바나나킹 · 타자왕 · 고릴라)이 치고 따는 로비 / 로비 창 |

- **창 배율 1.5** 로 찍는다(옵션 "크기" 범위 안). 1.0 으로는 1920x1080 에서 원숭이가 썸네일의 점이 된다.
  바탕화면도 150% 배율 모니터처럼 그렸다(작업 표시줄 72px, 커서 46px)
- 입력 헬퍼를 띄우지 않고 타건을 코드로 넣는다(`HelperInputSource.DebugInject`) — 찍는 동안 실제 키보드가 섞이지 않는다
- 계측 HUD 를 끄고 저전력 모드를 끈다(바뀐 게 없으면 프레임이 안 와서 캡처가 멈춘다)
- **Esc 를 누르면 안 된다** — 셸 디버그 키에서 종료다. 상점·로비는 같은 키(B·M)를 다시 눌러 닫는다
- 게임 레이어의 타입을 모르는 채로 돈다: 상점 탭은 `TabContainer` 를 찾아 넘기고, 상점·로비는 디버그 키를 흘린다

### 3-2. 합성 결과 (`assets/_store/screenshots/`) — 7장 중 5장을 고른다

| 파일 | 장면 | 추천 |
|---|---|---|
| `01_work` | 코드 편집기 옆에서 원숭이가 익은 송이를 친다, 꾸민 커서 | **1번** — 일하면서 켜 두는 게임 |
| `02_harvest` | 문서 작업 옆, 황금 송이가 떨어진다 | **2번** |
| `03_cursor` | 상점(매달림 탭) + 커서 둘레 확대 원 | **3번** — 커서 꾸미기 |
| `04_collection` | 도감 11/16 | 4번 후보 |
| `05_lobby` | 로비 창(랭킹·친구 목록) + 친구 창 3개 | **5번** |
| `06_friends` | 친구 창 3개를 화면 위쪽에 둔 모습 + 내 나무 | 4번 후보 |
| `07_upgrade` | 강화 탭 | 여분 |

**알고 있는 것**

- 화면 글자는 한국어다. 영어 스토어에는 같은 그림을 쓰거나, 스팀의 언어별 스크린샷으로 나중에 나눈다
- `03_cursor` 의 확대 원만 실제 화면이 아니다(같은 화면을 3.4배로 키운 것). 1:1 로는 장식이 작아서다
- 상점 창은 반투명이라 뒤의 나무가 비친다 — 게임 그대로다
- 편집기·문서 창은 특정 제품을 흉내 내지 않은 일반 창이다(로고 없음)
- 촬영 상태를 바꾸려면 `PrepareStoreShot`, 장면을 바꾸려면 `RunStoreShot`, 배치는 합성 스크립트의 `shot_*`
- **움짤(GIF)** — 타이핑 → 펀치 → 바나나 → 커서. 긴 설명에 넣을 수 있으면 넣는다. 트레일러와 같이 만든다

---

## 4. 설명문 초안

**쓰지 않는 말**: "마켓에서 거래 가능" (밸브 승인 전 — C2 §4-5, ECONOMY-SERVER §4). "아무것도 수집하지
않습니다" (서버가 있는 이상 거짓 — C2). 확인 안 된 부하 수치.

### 4-1. 짧은 설명 (300자 이내)

**한국어**

> 바탕화면 한쪽에 원숭이와 바나나 나무를 띄워 두세요. 일하며 타자를 칠 때마다 원숭이가 나무를 펀치하고,
> 익은 바나나가 떨어집니다. 모은 바나나로 내 마우스 커서를 꾸미세요.

**English**

> Keep a little monkey and a banana tree on the corner of your desktop. Every key you type makes the monkey
> punch the tree and knock down ripe bananas. Spend them to decorate your mouse cursor.

### 4-2. 긴 설명 — 한국어

맨 위에 C2 §1 짧은 개인정보 문구를 **그대로** 둔다 (STEAM-CONFIG §2-2 — 접히기 전에 보여야 한다).

> **이 게임은 타이핑과 마우스 클릭의 횟수만 셉니다.** 어떤 키를 눌렀는지, 어떤 버튼을 눌렀는지,
> 마우스가 어디에 있는지 읽지 않고, 저장하지 않으며, 외부로 전송하지 않습니다.
>
> 바나나와 커서 장식을 지키기 위해 게임 서버에 스팀 계정 ID 와 게임 진행 상태(잔액·나무·강화)를
> 저장합니다. 자세한 내용은 개인정보 처리 안내를 참고하세요.

**PunchMonkey 는 바탕화면에 켜 두는 방치형 게임입니다.** 창을 따로 볼 필요가 없습니다. 평소처럼 일하고,
코딩하고, 채팅하세요. 원숭이는 화면 한쪽에서 알아서 바쁩니다.

**치면 친다**
타자를 치거나 클릭할 때마다 원숭이가 나무를 펀치합니다. 바나나는 시간이 지나면 저절로 익고, 익은
송이는 열 번 맞으면 떨어집니다. 자리를 비워도 나무는 자랍니다 — 돌아와서 수확하세요.

**커서를 꾸미세요**
바나나로 커서 장식을 삽니다. 커서에 매달리는 원숭이, 따라오는 잔상, 커서 밑의 바닥 장식 — 세 칸에
16종. 커서 자체는 윈도우 기본 커서 그대로이고, 장식이 곁에 붙어 다닙니다. 모은 장식은 스팀 인벤토리에
들어갑니다.

**나무를 키우세요**
빨리 익기, 황금 바나나(따면 ×5), 가지 늘리기. 장식을 먼저 살지, 나무를 먼저 키울지는 당신 몫입니다.

**친구와 같이 치세요**
스팀 친구와 로비에 모이면 친구의 원숭이와 나무가 작은 창으로 내 바탕화면에 나타납니다. 끌어서 아무
데나 두세요. 누가 제일 많이 쳤는지 로비 랭킹으로 겨룹니다.

**방해하지 않습니다**
- 항상 위에 떠 있지만 클릭은 뒤로 통과합니다
- 전체화면 게임·영상 중에는 숨길 수 있습니다 (옵션)
- 트레이로 숨기기, Windows 시작 시 실행, 크기·투명도 조절
- 가볍게 돌도록 만들었습니다

**누적 타수 = 레벨.** 첫 천 타부터 백만 펀치까지, 도전과제 10개.

### 4-3. 긴 설명 — English

(Top: C2 §1 English short privacy notice, verbatim.)

> **This game only counts how many times you type and click.** It never reads which keys or buttons
> you press or where your mouse is — and it never stores or sends that information anywhere.
>
> To keep your bananas and cursor decorations safe, our game server stores your Steam account ID and
> your game progress (balance, tree, upgrades). See the privacy notice for details.

**PunchMonkey is an idle game that lives on your desktop.** No window to babysit. Work, code, chat as usual —
the monkey keeps itself busy in the corner of your screen.

**You type, it punches**
Every keystroke or click makes the monkey punch the tree. Bananas ripen over time on their own, and a ripe
bunch drops after ten hits. The tree keeps growing while you're away — come back and harvest.

**Decorate your cursor**
Spend bananas on cursor decorations: a monkey hanging from your cursor, a sparkling trail, a glow underneath —
16 decorations across three slots. Your actual cursor stays the standard Windows cursor; the decorations
tag along. Decorations you collect go into your Steam inventory.

**Grow your tree**
Faster ripening, golden bananas (worth ×5), more branches. Decorations first or a bigger tree first? Your call.

**Punch together**
Gather in a lobby with Steam friends and their monkeys and trees appear on your desktop as small windows.
Drag them anywhere. See who punched the most on the lobby leaderboard.

**Stays out of your way**
- Always on top, but clicks pass right through
- Can hide while fullscreen games or videos are running (option)
- Hide to tray, launch with Windows, adjust size and opacity
- Built to run light

**Your total keystrokes are your level.** From your first thousand to a million punches — 10 achievements.

### 4-4. 설명란 이미지 (`assets/_store/description/`)

`tools/make-store-screenshots.py` 가 스크린샷을 만들 때 같이 뽑는다. 너비 616px(스팀 설명란 너비). 섹션 제목(`[h2]`)
바로 아래에 한 장씩 둔다. 한/영 설명에 같은 그림을 쓴다.

| 섹션 | 파일 | 내용 |
|---|---|---|
| 치면 친다 / You type, it punches | `section_punch.gif` (420x404, 약 550KB) | 익은 송이를 치고 → 10번째에 황금 송이가 떨어진다. 원판 프레임 그대로 |
| (GIF 를 못 쓰면) | `section_harvest.png` | 수확 순간 한 장 |
| 커서를 꾸미세요 / Decorate your cursor | `section_cursor.png` | 커서 둘레 확대 원 |
| 나무를 키우세요 / Grow your tree | `section_upgrade.png` | 강화 탭 |
| 친구와 같이 치세요 / Punch together | `section_friends.png` | 친구 창 3개 (이름 · 레벨 · 로비 타수 · 도감) |

개인정보 문구와 "방해하지 않습니다" 섹션에는 그림을 넣지 않는다 — 앞은 읽혀야 하는 글이고 뒤는 글머리표로 충분하다.

### 4-5. 문장마다 확인할 것

| 문장 | 근거 / 확인 |
|---|---|
| 열 번 맞으면 떨어진다 | `17ed534` 10타 수확 |
| 16종, 세 칸 | B9 — 매달림 6 · 잔상 6 · 바닥 4 |
| 스팀 인벤토리에 들어간다 | ECONOMY-SERVER — 장식은 스팀 인벤토리 아이템 |
| 황금 ×5, 세 축 이름 | B13-UPGRADES — **B14 밸런스에서 이름·배율이 바뀌면 같이 고친다** |
| 친구 창 끌어서 두기, 로비 랭킹 | B10 · B12. **2계정 전체 시나리오 확인 전** — 공개 전에 한 번은 돌려 볼 것 |
| 전체화면 숨김 "옵션" | `63a116b` 기본값 끔 — 그래서 "숨길 수 있습니다" |
| 클릭이 뒤로 통과 | A2 · A3 |
| 도전과제 10개 | A15 |
| "가볍게" — 수치를 안 쓴 이유 | A7 실측 239MB 는 있지만 친구 창 3개는 추정치(DEVLOG 9/24 열셋째). 숫자는 확정 뒤에 |
| "타건 카운트 끄기" 를 안 쓴 이유 | 아직 동작하지 않는다 (C2 §4-1) |

---

## 5. 태그

스팀은 개발자가 태그 20개까지 붙일 수 있고, **앞쪽 태그가 더 무겁다.** STEAM-CONFIG §2-5 의 6개를 앞에 둔다.

1. `Idle` 2. `Clicker` 3. `Casual` 4. `Cute` 5. `Singleplayer` 6. `Multiplayer`
7. `Relaxing` 8. `Cozy` 9. `Cartoony` 10. `Colorful` 11. `2D` 12. `Indie`
13. `Typing` 14. `Collectathon` 15. `Funny`

- `Typing` 은 "타자 연습 게임" 으로 읽힐 수 있다. 검색 유입이 잘못 들어오면 뺀다
- `Online Co-Op` 은 붙이지 않는다 — 같이 하는 것이 관전·랭킹이지 협동 플레이가 아니다

---

## 6. 가격 — 정해야 한다

| 안 | 장점 | 걱정 |
|---|---|---|
| **$2.99 (추천)** | 충동 구매 구간. 커서 장식이 스팀 인벤토리 아이템이라 "싸게 사서 모으는" 기대와 맞는다 | 수익은 작다 |
| $4.99 | 장르 유료작의 흔한 가격 | 무료 데스크톱 펫 게임과 비교되기 쉽다 |
| 무료 | 유입 최대 | **마켓 거래가 되는 순간 부계정 파밍 표적** — ECONOMY-SERVER 의 서버 권위가 있어도 유료가 문턱 역할을 한다. 무료로 가려면 경제 설계부터 다시 봐야 한다 |

지역 가격은 스팀 권장 가격표를 그대로 쓴다. **출시 할인(10~20%)** 은 첫 주에 걸 수 있으니 기본가는
그것까지 생각해서 정한다.

---

## 7. 10/2 까지 순서

1. ☐ 캡슐 초안 확인 → 고칠 것 반영 (§2)
2. ☐ 가격 결정 (§6), 문의 이메일 결정 (C2 §4-2)
3. ☐ LoRA 라이선스 확인 (B8 §6-1) — 캡슐·게임에 생성 그림이 들어간다
4. ☑ 스크린샷 초안 (§3) — 7장 중 5장 고르기
5. ☐ 파트너 사이트에 설명문·태그·시스템 요구사항·캡슐·스크린샷 입력, 개인정보 문구 링크
6. ☐ W-8BEN · 은행 계좌 (기획서 §11) — 스토어 공개 심사와 별개지만 출시 전에 필요하다
7. ☐ 스토어 페이지 검수 제출 → "출시 예정" 공개. **밸브 검수에 며칠 걸리므로 제출은 10/2 보다 앞서야 한다**
