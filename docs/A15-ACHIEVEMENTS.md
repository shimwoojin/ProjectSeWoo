# A15 — 도전과제 등록

관련: [확정 기획서](기획확정-일감분배-260907.md) A15 · §3-3 (도감 100%) · §6 (누적 타수) /
[A8 스팀](A8-STEAM.md) §2-4 (스텁) / 코드 원본 `shared/Contracts/IAchievements.cs` (`AchievementIds`)

## 0. 상태 (2026-09-24)

| 항목 | 상태 |
|---|---|
| 목록 확정 (10개) | ☑ 2026-09-24. 마일스톤 1K/10K/100K/1M, 추가 과제 5개 |
| 코드 (`AchievementIds`, 해금 조건) | ☑ 목(`MockAchievements`)으로 끝까지 시험 — §3 |
| 파트너 사이트 등록 | ☑ 2026-09-24, 10개 모두 Set By = Client |
| 등록 확인 | ☑ `--steam-selftest` → `ach 등록 : 10/10`, 통계 수신 PASS — §4 |
| 아이콘 | ☑ 달성·미달성 각 10장 — `assets/_store/achievements/` (§6) |
| 누적 타수 통계 `STAT_KEYSTROKES` | ☑ 2026-09-24 등록·게시, 자체 검사 `등록됨`. 코드는 타건마다 로컬, 1분마다·해금·종료 때 서버 — §5 |

## 1. 목록

**API Name 은 코드와 한 글자도 다르면 안 된다.** 등록 뒤에는 바꾸지 않는다 — 유저가 딴
기록이 그 이름에 붙는다. 새 과제는 추가만 한다. 전부 숨김 아님, Set By = Client.

| API Name | 영어 이름 / 설명 | 한국어 이름 / 설명 | 조건 |
|---|---|---|---|
| `ACH_KEYSTROKES_1K` | First Thousand / Reach 1,000 total keystrokes. | 첫 천 타 / 누적 1,000타를 친다. | 누적 1,000타 (Lv.8, 첫날) |
| `ACH_KEYSTROKES_10K` | Warmed Up / Reach 10,000 total keystrokes. | 손이 풀렸다 / 누적 10,000타를 친다. | 누적 10,000타 (Lv.16) |
| `ACH_KEYSTROKES_100K` | Keyboard Warrior / Reach 100,000 total keystrokes. | 키보드 전사 / 누적 100,000타를 친다. | 누적 100,000타 (Lv.24) |
| `ACH_KEYSTROKES_1M` | Million Punches / Reach 1,000,000 total keystrokes. | 백만 펀치 / 누적 1,000,000타를 친다. | 누적 1,000,000타 (Lv.31) |
| `ACH_FIRST_PURCHASE` | First Decoration / Buy your first cursor decoration. | 첫 장식 / 상점에서 커서 장식을 처음 산다. | 기본 지급품 말고 장식이 하나라도 생김 |
| `ACH_SLOT_HANG` | Monkey Collector / Collect every monkey. | 원숭이 수집가 / 원숭이를 모두 모은다. | 원숭이 전부 (기본 지급품 포함). 2026-09-26 전엔 "매달림 수집가" — B17 §3-4 |
| `ACH_SLOT_TRAIL` | Decorator / Collect every decoration. | 장식 수집가 / 장식을 모두 모은다. | 장식 칸 전부. 2026-09-26 전엔 "잔상 수집가" — B17 §3-4 |
| `ACH_SLOT_BASE` | Banana Collector / Collect every banana. | 바나나 수집가 / 바나나를 모두 모은다. | 바나나 전부 (기본 지급품 포함). 2026-09-26 전엔 "바닥 수집가" — B17 §3-4 |
| `ACH_COLLECTION_100` | Complete Collection / Collect every cursor decoration. | 도감 완성 / 커서 장식을 모두 모은다. | 도감 100% (§3-3) — 장식이 늘면 조건도 같이 는다. 이미 딴 사람은 그대로(스팀은 해금을 되돌리지 않는다) |
| `ACH_ROOM_FIRST_JOIN` | Better Together / Create or join a room with friends. | 같이 치자 / 친구와 룸을 처음 만들거나 들어간다. | 실물 스팀 로비(`SteamNetSession`, A9)에 들어감 |

- 누적 타수의 "Lv." 은 `KeystrokeLevel` 곡선 기준이다. 체감 기간(첫날 / 하루 이틀 / 1~2주 /
  수개월)은 하루 타건 수를 짐작한 것이라 실측이 아니다
- `ACH_ROOM_FIRST_JOIN` — A9(실물 스팀 로비)가 9/24 에 들어와서 실제로 딸 수 있다. 목 룸으로는
  안 풀린다(§3-2). 멀티를 출시에서 빼게 되면 이 과제는 파트너 사이트에서 숨기거나 지운다 —
  딸 수 없는 과제가 목록에 있으면 도감 100% 성향의 유저에게 나쁜 신호다
- 문구는 초안이다. 스토어 페이지 톤이 정해지면 같이 다듬는다

## 2. 파트너 사이트에 넣는 순서

1. Steamworks → 앱 `5281130` → **Stats & Achievements → Achievements**
2. 과제마다 **New Achievement** → API Name / Display Name / Description 입력,
   Hidden 끔, Set By = Client
3. 한국어는 같은 화면의 언어 선택(또는 Localization)에서 Display Name·Description 을 넣는다
4. 아이콘은 달성 `<API_NAME>.png` / 미달성 `<API_NAME>_locked.png` 를 올린다. JPG 만 받으면
   같은 이름의 `.jpg` 를 쓴다 (규격은 업로드 화면에서 확인 — 공식 문서 페이지에 수치가 없었다)
5. **게시(Publish)를 눌러야 반영된다.** 스팀 인벤토리 아이템 정의 때 "게시"와 "서비스
   활성화"를 따로 눌러야 했던 것과 같은 함정을 조심한다 (ECONOMY-SERVER.md §10)

## 3. 해금 조건 — 코드가 하는 일

| 과제 | 해금 시점 | 코드 |
|---|---|---|
| 누적 타수 4개 | 누적 타수가 문턱을 넘는 순간 | `GameRoot.CheckMilestones` |
| 첫 구매 | 구매 성공 직후 | `GameRoot.OnBuyRequested` |
| 슬롯 완성 3개 · 도감 100% | 구매 뒤 도감 확인 때 | `GameRoot.CheckCollection` → `UnlockCompletedSlots` |
| 룸 첫 참가 | `INetSession.OnRoomChanged` 에서 룸에 있으면 | `GameRoot.OnRoomChangedForAchievement` |

### 3-1. 놓친 해금 회수

해금은 조건을 넘는 순간 한 번 부른다. 그 순간 스팀이 없으면(자동 시작이 스팀보다 먼저 뜬
경우 등) `Unlock` 이 조용히 버려지고, 다음 실행부터는 "이미 지난 마일스톤" 으로 건너뛰어서
**그 과제는 영영 안 풀렸다.** 그래서 스팀 통계가 처음 준비되면 `SyncAchievements` 가 이미
달성한 조건을 한 번 훑어 아직 안 풀린 것만 해금한다(`IsUnlocked` 로 먼저 확인하므로 중복
호출 없음). 보유 목록이 늦게 오면 다시 훑는다. 룸 참가는 상태가 아니라 사건이라 회수 대상이
아니다.

### 3-2. 목에서 나온 결과로는 진짜 과제를 풀지 않는다

디버그 빌드는 목 경제로 돌면서도 스팀이 켜져 있으면 도전과제만 진짜 `SteamService` 로 간다.
그대로 두면 **목 바나나로 산 장식이 개발 계정의 도감 100% 를 실제로 해금한다** — 되돌릴 수
없다. 그래서 구매·슬롯·도감은 경제가 실물이거나 도전과제가 목일 때만, 룸 참가는 멀티가
실물이거나 도전과제가 목일 때만 해금한다(`TrustEconomyForAchievements` /
`TrustNetForAchievements`). 디버그 지급(`Shift+B`, `_cheated`)은 전과 같이 막는다. 누적 타수는
실제 입력이라 이 규칙 밖이다.

**무인 실행(`--steam-selftest`, `--report=`)은 게임 레이어에 진짜 도전과제를 넘기지 않는다**
(`OverlayShell` 이 `UnavailableAchievements` 를 준다). 게임 레이어는 무인 실행에서도 뜨고, 스팀
통계가 오면 §3-1 회수가 돈다 — **9/24 등록 확인용 자체 검사가 개발 계정에 `ACH_KEYSTROKES_1K`
를 실제로 해금했다**(세이브의 누적 3,288타는 실제로 친 것이라 다음 정상 실행에서도 풀렸을
과제다). 자체 검사 자신은 `SteamService` 를 직접 읽으므로 영향이 없다.

### 3-3. 시험 (2026-09-24, 게임 씬 단독 실행 = 전부 목)

| 순서 | 결과 |
|---|---|
| 1,000타 입력 | `ACH_KEYSTROKES_1K` |
| 목 잔액을 넣고 15종 구매 | `ACH_FIRST_PURCHASE`, `ACH_SLOT_HANG/TRAIL/BASE`, `ACH_COLLECTION_100` |
| 룸 만들기 | `ACH_ROOM_FIRST_JOIN` |
| 합계 | 7/10 (1만·10만·100만 타는 미달 — 정상) |
| 목 초기화 후 회수만 | 6/10 (룸 참가는 회수 대상 아님 — 정상) |

시험 중에 **디버그 상점 구매가 전부 `ItemUnknown` 으로 실패하던 기존 버그**를 찾았다. 목
경제는 가격표(`RegisterItemPrice`)를 받아야 파는데 부르는 곳이 없었다 — 9/23 서버 권위
리와이어 뒤로 디버그에서는 `Shift+B` 로만 장식을 얻을 수 있었다. `GameRoot.AttachPlatform`
에서 `ShopCatalog` 가격을 목에 넣는다.

## 4. 등록 확인

```
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . -- --steam-selftest
```

이름마다 `등록됨` / `**미등록**` / `?` 를 찍고 `ach 등록 : N/10` 을 낸다. 그 아래 `stat` 줄이
`STAT_KEYSTROKES` 의 등록 여부와 현재 값을 낸다.

- `?` = 통계를 못 받았다 (스키마가 없거나 게시 전). **등록 전 지금은 전부 `?` 가 정상이다**
- `**미등록**` = 통계는 받았는데 그 이름이 스키마에 없다 → 파트너 사이트 입력 오타
- 읽기만 한다 — 자체 검사가 `Unlock` 을 부르면 실제 과제가 풀려 버린다. 게임 레이어의 해금도
  무인 실행에서는 막혀 있다(§3-2)

## 5. 누적 타수 통계 `STAT_KEYSTROKES`

스팀 커뮤니티 도전과제 페이지의 **진행 막대**("100,000 중 32,881")는 과제에 정수 통계(Progress
Stat)를 연결해야 나온다.

**게임 안 진행도 토스트(`IndicateAchievementProgress`)는 쓰지 않는다 (2026-09-24).** 스팀이 해금 토스트와
같은 우하단 자리에 띄워서 "안 깼는데 업적 알림이 뜬다" 로 보였다 — 켠 뒤 첫 타건, 다음 마일스톤까지 5%
마다, 구매마다(도감 N/16). 토스트는 해금할 때만 뜬다. 진행은 커뮤니티 페이지 막대로만 보인다.

### 5-1. 파트너 사이트 — Stats 에 새 통계

| 칸 | 값 | 이유 |
|---|---|---|
| API Name | `STAT_KEYSTROKES` | `StatIds.Keystrokes` 와 한 글자도 다르면 안 된다 |
| Type | **INT** | 타수는 정수. AVGRATE 는 "시간당 비율" 용이다 |
| Set By | **Client** | 게임 클라이언트가 `SetStat` 으로 쓴다. GS/Official GS 면 클라이언트 쓰기가 거부된다 |
| Increment Only | **켬** | 누적값이 줄어들 일이 없다. 켜 두면 줄이는 조작도 막힌다 |
| Max Change | **비움(제한 없음)** | 한 번 저장할 때 늘 수 있는 최대치. 작게 잡으면 **스팀이 꺼져 있던 동안 쌓인 타수를 나중에 한꺼번에 올릴 때 거부된다**(자동 시작이 스팀보다 먼저 뜬 날 등). 초당 캡(10)이 이미 입력 쪽에 있다 |
| Min Value | **0** | |
| Max Value | **비움** (또는 2147483647) | INT 최대. 코드도 여기서 멈춘다(초당 10타로 6년 넘게 걸린다) |
| Default Value | 0 | |
| Aggregated | 끔 (선택) | 켜면 스팀이 **전 유저 합계**를 모은다("전 세계 누적 타수" 같은 연출용). 지금 쓸 곳이 없다 |
| Display Name | Total Keystrokes / 누적 타수 | |

### 5-2. 파트너 사이트 — 누적 타수 과제 4개에 Progress Stat 연결

`ACH_KEYSTROKES_1K/10K/100K/1M` 각각 편집 → **Progress Stat = `STAT_KEYSTROKES`**, Min 0, Max 는
과제 문턱(1000 / 10000 / 100000 / 1000000). 다른 6개(구매·슬롯·도감·룸)는 연결하지 않는다.

통계가 Max 에 닿으면 스팀이 그 과제를 **자동으로 해금**하는 것으로 알고 있다(확인은 안 했다).
그래도 코드의 `Unlock` 은 그대로 둔다 — 이미 풀린 과제의 `Unlock` 은 아무 일도 안 한다.

**Stats·Achievements 둘 다 게시(Publish)해야 반영된다.** 게시 뒤 자체 검사의 `stat` 줄이
`등록됨 값=N` 이면 된다.

**게시해도 스팀 클라이언트가 예전 스키마를 쥐고 있을 수 있다 (2026-09-24 실측).** 도전과제를
게시한 뒤 통계를 추가·게시했는데 자체 검사가 계속 `미등록` 이었다. 로컬 캐시
`Steam\appcache\stats\UserGameStatsSchema_5281130.bin` 에 도전과제 이름은 있고 `STAT_KEYSTROKES`
는 없었다 — 클라이언트가 새 스키마를 안 받은 것이다. **스팀 클라이언트를 완전히 껐다 켜자**
캐시가 새로 받아졌고(19:00 → 19:14) `등록됨` 이 나왔다.

- 진단 순서: 자체 검사 `stat` 줄 → `**타입이 FLOAT 다**` 면 등록 타입 오류, `**미등록**` 이면 게시
  여부·이름 확인 → 그래도 그대로면 위 캐시 파일에 이름이 있는지 본다
- 이미 배포된 유저 PC 도 클라이언트가 스키마를 갱신하기 전까지 새 통계를 모를 수 있다. 그동안
  `SetStat` 은 조용히 실패(로그 한 줄)하고 도전과제 해금은 영향이 없다

### 5-3. 코드가 하는 일

| 시점 | 동작 |
|---|---|
| 타건마다 (`GameRoot.OnKeystrokes`) | `SetStat(STAT_KEYSTROKES, 누적)` — **로컬 캐시만** 바꾼다 |
| 1분마다 (`SteamService.Tick`) | 바뀐 게 있으면 `StoreStats` 로 서버에 보낸다. 타건마다 보내면 스팀이 호출을 제한한다 |
| 도전과제 해금 때 | `Unlock` 이 부르는 `StoreStats` 에 밀린 통계도 같이 실려 간다 |
| 스팀 통계가 처음 준비될 때 (`SyncAchievements`) | 현재 누적을 한 번 넣는다 — 스팀이 없는 동안 친 타수는 그때 버려졌다 |
| 종료 (`SteamService.Dispose`) | 마지막 1분 안에 친 타수를 보내고 놓는다 |

- 값은 세이브의 `TotalKeystrokes` 를 그대로 비춘다. 세이브를 새로 시작한 PC 에서는 스팀 값이 더
  커서 Increment Only 에 걸려 거부된다 — 게임은 그대로 돌고, 세이브가 스팀 값을 넘으면 다시 먹는다
  (거부는 로그 한 줄만 남긴다)
- 나중에 할 수 있는 것: 켤 때 스팀 값이 세이브보다 크면 세이브를 끌어올리기 = 누적 타수의 클라우드
  백업. 지금은 안 한다

## 6. 아이콘

`tools/make-achievement-icons.py` 가 게임 스프라이트로 만든다(손으로 그리지 않는다 — B8 과
같은 이유). 256×256, 과제마다 달성·미달성 × PNG·JPG 네 장, 모두 40장.

```
"C:\Tools\ComfyUI\.venv\Scripts\python.exe" tools\make-achievement-icons.py
```

시스템 파이썬에는 PIL 이 없어서 에셋 파이프라인과 같은 ComfyUI venv 로 돌린다. 출력 폴더
`assets/_store/achievements/` 는 `.gdignore` 라 Godot 이 임포트하지 않는다 — 게임이 아니라
스팀에 올리는 소재다(C1 스토어 소재도 `assets/_store/` 에 모으면 된다).

| 과제 | 그림 | 배경 / 테두리 |
|---|---|---|
| 누적 타수 4개 | 펀치하는 원숭이 + 숫자 배지 | 테두리가 단계를 가른다 — 1K 동 · 10K 은 · 100K 금 · 1M 다이아 |
| 첫 구매 | 노랑 원숭이(첫 티어 장식) + 바나나 | 초록 |
| 매달림 / 잔상 / 바닥 수집가 | 각 슬롯의 최고 티어 장식 | 보라 / 청록 / 갈색 |
| 도감 완성 | 바나나가 가득한 나무 + `100%` (2026-09-26 전엔 `16/16` — 장식이 늘어나므로 개수를 안 쓴다) | 금, 굵은 테두리 |
| 같이 치자 | 원숭이 둘 | 파랑 |

미달성은 흑백 + 밝기 55% + 대비 80% — 모양은 남겨서 무엇을 해야 하는지 짐작하게 한다.
64px 로 줄여서도 테두리 색·배지·그림이 구분되는 것을 확인했다(스팀이 작게 보여 주는 곳이 많다).
업로드 화면 규격이 다르면 스크립트의 `SIZE` 만 바꿔 다시 뽑는다.

