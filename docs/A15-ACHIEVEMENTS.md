# A15 — 도전과제 등록

관련: [확정 기획서](기획확정-일감분배-260907.md) A15 · §3-3 (도감 100%) · §6 (누적 타수) /
[A8 스팀](A8-STEAM.md) §2-4 (스텁) / 코드 원본 `shared/Contracts/IAchievements.cs` (`AchievementIds`)

## 0. 상태 (2026-09-24)

| 항목 | 상태 |
|---|---|
| 목록 확정 (10개) | ☑ 2026-09-24. 마일스톤 1K/10K/100K/1M, 추가 과제 5개 |
| 코드 (`AchievementIds`, 해금 조건) | ☑ 목(`MockAchievements`)으로 끝까지 시험 — §3 |
| 파트너 사이트 등록 | ☐ **사람이 한다** — §2 |
| 등록 확인 | ☐ 등록·게시 뒤 `--steam-selftest` 가 `ach 등록 : 10/10` 이어야 한다 — §4 |
| 아이콘 | ☐ 달성·미달성 각 10장 |
| 진행도 통계(커뮤니티 진행 막대) | ☐ 안 한다(선택). §5 |

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
| `ACH_SLOT_HANG` | Hanging Around / Collect every hanging decoration. | 매달림 수집가 / 매달림 장식을 모두 모은다. | 매달림 6종 (기본 지급품 포함) |
| `ACH_SLOT_TRAIL` | Trailblazer / Collect every trail decoration. | 잔상 수집가 / 잔상 장식을 모두 모은다. | 잔상 6종 |
| `ACH_SLOT_BASE` | Solid Ground / Collect every base decoration. | 바닥 수집가 / 바닥 장식을 모두 모은다. | 바닥 4종 |
| `ACH_COLLECTION_100` | Complete Collection / Collect all 16 cursor decorations. | 도감 완성 / 커서 장식 16종을 모두 모은다. | 도감 16/16 (§3-3) |
| `ACH_ROOM_FIRST_JOIN` | Better Together / Create or join a room with friends. | 같이 치자 / 친구와 룸을 처음 만들거나 들어간다. | 룸에 들어감 (**실물 로비 A9 전에는 해금 안 됨**) |

- 누적 타수의 "Lv." 은 `KeystrokeLevel` 곡선 기준이다. 체감 기간(첫날 / 하루 이틀 / 1~2주 /
  수개월)은 하루 타건 수를 짐작한 것이라 실측이 아니다
- `ACH_ROOM_FIRST_JOIN` 은 멀티를 1.1 로 미루면 **등록하지 않는 편이 낫다.** 딸 수 없는
  과제가 목록에 있으면 도감 100% 성향의 유저에게 나쁜 신호다. 코드에 이름이 있어도
  등록 안 된 이름의 `Unlock` 은 조용히 실패할 뿐이다
- 문구는 초안이다. 스토어 페이지 톤이 정해지면 같이 다듬는다

## 2. 파트너 사이트에 넣는 순서

1. Steamworks → 앱 `5281130` → **Stats & Achievements → Achievements**
2. 과제마다 **New Achievement** → API Name / Display Name / Description 입력,
   Hidden 끔, Set By = Client
3. 한국어는 같은 화면의 언어 선택(또는 Localization)에서 Display Name·Description 을 넣는다
4. 아이콘은 달성·미달성 각각 올린다 (규격은 업로드 화면에서 확인 — 공식 문서 페이지에
   수치가 없었다)
5. **게시(Publish)를 눌러야 반영된다.** 스팀 인벤토리 아이템 정의 때 "게시"와 "서비스
   활성화"를 따로 눌러야 했던 것과 같은 함정을 조심한다 (ECONOMY-SERVER.md §10)

## 3. 해금 조건 — 코드가 하는 일

| 과제 | 해금 시점 | 코드 |
|---|---|---|
| 누적 타수 4개 | 누적 타수가 문턱을 넘는 순간. 다음 문턱까지 진행도 토스트 | `GameRoot.CheckMilestones` |
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

이름마다 `등록됨` / `**미등록**` / `?` 를 찍고 마지막에 `ach 등록 : N/10` 을 낸다.

- `?` = 통계를 못 받았다 (스키마가 없거나 게시 전). **등록 전 지금은 전부 `?` 가 정상이다**
- `**미등록**` = 통계는 받았는데 그 이름이 스키마에 없다 → 파트너 사이트 입력 오타
- 읽기만 한다 — 자체 검사가 `Unlock` 을 부르면 실제 과제가 풀려 버린다

## 5. 진행도 통계 (선택, 안 함)

스팀 커뮤니티 페이지의 진행 막대는 과제에 INT 통계(Progress Stat)를 연결해야 나온다. 게임 안
진행도 토스트(`IndicateAchievementProgress`)는 통계 없이도 뜬다. 막대까지 원하면
`STAT_KEYSTROKES` 같은 통계를 등록하고 코드에서 `SetStat` + 주기적 `StoreStats` 를 붙여야 한다
— 출시 필수는 아니라 보류.
