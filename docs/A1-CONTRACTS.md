# A1 — 인터페이스 4종 + 세이브 스키마 v1

관련: [확정 기획서](기획확정-일감분배-260907.md) §8-2 (인터페이스), §7-5 (세이브 스키마), §8-3 (협업 규칙)

> **이 문서의 목적은 을이 갑을 기다리지 않게 하는 것이다.**
> 갑이 실물(A3·A4·A5·A9~A11)을 만드는 동안, 을은 여기 있는 목(mock)으로
> 게임 레이어를 끝까지 돌릴 수 있다.

---

## 0. 상태 (2026-09-14)

| 산출물 | 위치 | 상태 |
|---|---|---|
| 인터페이스 4종 | `shared/Contracts/` | ☑ 커밋 |
| 공용 타입 (`CursorSlot`, `PeerId`, `RoomHandle`, `PlayerState`) | `shared/Contracts/Types.cs` | ☑ 커밋 |
| 세이브 스키마 v1 + 마이그레이션 훅 | `shared/Save/` | ☑ 커밋 |
| 목 구현 4종 | `platform/Mocks/` | ☑ 커밋 |
| 스키마 self-test | `--selftest` 인자 | ☑ PASS |

---

## 1. 폴더 분할 (§8-3)

```
shared/      둘 다 읽는다. 바꿀 때는 상대에게 알린다
platform/    갑 담당. OS·Steam 과 붙는 전부
game/        을 담당. 게임 안에서 도는 전부
src/         Day 1-2 / A2 스파이크 코드. A3 에서 platform/ 으로 정리된다
```

> **2026-09-15 갱신.** A3가 끝나서 `src/`는 더 이상 없다. 스파이크 코드
> (`OverlayShell.cs`/`CursorLayer.cs`/`PerfProbe.cs`/`DebugHud.cs`)는 전부
> `platform/`으로 옮겨졌고, `docs/DAY1-2-SPIKE.md`의 실행법은 그대로 유효하다
> (`tools/*.ps1`은 프로젝트 경로로 Godot을 띄울 뿐 개별 파일 경로에 의존하지 않는다).

---

## 2. 인터페이스 4종

시그니처는 기획서 §8-2 와 같다. 기획서가 정의하지 않은 **타입**을 채운 것이 A1 의 실제 작업이다.

### 바꿀 때의 규칙 (§8-2)

> 이 4개 인터페이스는 파일로 커밋하고, **이후 변경은 상대에게 알린 뒤에만 한다.**

기계적으로 지킬 수 있게, 변경 시 이 문서의 §0 표에 날짜와 사유를 한 줄 남긴다.

### 2-1. `IInputSource` — 타건 수 공급

```csharp
event Action<int> OnKeystrokes;   // 100ms 배치, 캡 적용 후 개수
long TotalCount { get; }
bool IsAvailable { get; }
```

**키 코드를 나타내는 타입이 이 인터페이스에 아예 없다.** 그게 §7-2·§7-6 의
"키를 읽지 않는다"를 코드 레벨에서 증명하는 첫 번째 장치다. 구현체도 콜백에서
`vkCode` 를 변수에 담지 않게 작성한다.

이건 기능이 아니라 신뢰 문제다. 스팀 리뷰에 "키로거 아니냐" 글 하나면 프로젝트가 죽는다.

`IsAvailable == false` 면 게임 레이어는 **시간 기반 폴백**으로 전환한다. 안티치트·백신이
전역 입력을 막는 환경이 실재하고, 그때 게임이 멈추는 게 아니라 성장 소스만 바뀌면 된다.
나무는 원래 시간으로 자라고(§2-2) 타이핑은 수확 트리거일 뿐이라 이게 성립한다.

### 2-2. `ICursorLayer` — 커서 장식

```csharp
void Equip(CursorSlot slot, string assetId);   // assetId 가 null 이면 슬롯을 비운다
void SetEnabled(bool on);
bool IsSupported { get; }
```

`CursorSlot` 은 `Hang` / `Trail` / `Base` 3종 (§3-1).

**A2 에서 Go 가 나왔지만 `IsSupported` 는 남긴다.** 남의 PC 에서 도는 상주 앱이라
이 환경에서 됐다는 것이 모든 환경에서 된다는 뜻이 아니다.
을은 이 값을 꺼 보고 게임이 성립하는지 확인할 것 — `MockCursorLayer.IsSupported = false`.

### 2-3. `INetSession` — 관전형 멀티

```csharp
Task<RoomHandle> CreateRoom();
Task<RoomHandle> JoinRoom(string uid);
void Broadcast(PlayerState s);
event Action<PeerId, PlayerState> OnPeerState;
event Action<PeerId> OnPeerJoin;
event Action<PeerId> OnPeerLeave;
```

기획서는 `OnPeerJoin, OnPeerLeave` 를 한 줄로 썼는데 두 줄로 나눠 선언했다.
C# 의미는 같다.

**`PlayerState` 에 없는 것이 이 설계의 핵심이다.** 재화 잔액, 나무 성장 타이머,
강화 수치는 동기화하지 않는다. 경제가 전부 로컬이라 치트 방어 서버가 필요 없고,
그게 멀티를 4주 안에 넣을 수 있게 만드는 유일한 이유다 (§4-1).

> 전송 목록은 개인정보 방침에 그대로 공개된다 (§7-6).
> **`PlayerState` 에 필드를 늘리면 스토어 문구와 게임 내 안내도 같이 고쳐야 한다.**

### 2-4. `IShell` — 오버레이 셸 제어

```csharp
void SetScale(float s);
void SetOpacity(float a);
void SetClickThrough(bool on);
Rect2I GetSafeArea();
```

`GetSafeArea()` 가 있는 이유는 **게임 레이어가 화면 크기를 직접 묻지 않게** 하기
위해서다. 모니터가 사라지거나 배율이 바뀌는 경우의 폴백은 플랫폼 레이어가 책임진다.

> 개발 PC 는 모니터가 3대고 **하나는 X 좌표가 음수**다(`-1920,250`).
> 게임 레이어가 원점을 (0,0) 으로 가정하면 거기서 깨진다.
> `MockShell.SafeArea` 에 음수 원점을 넣어서 미리 시험해 볼 것.

---

## 3. 목 구현 — `platform/Mocks/`

**목을 갑 쪽에 둔 이유**: 을이 직접 만들면 인터페이스 해석이 갈라지고, 실물로
갈아끼울 때 그 차이가 드러난다. 계약을 만든 쪽이 참조 구현도 같이 낸다.

| 목 | 실물 | 목에만 있는 것 (시험용) |
|---|---|---|
| `MockInputSource` | A4 | `Feed(count)`, `Tick(delta)`, `IsAvailable` 세터 |
| `MockCursorLayer` | A5 | `GetEquipped(slot)`, `IsEnabled`, `IsSupported` 세터 |
| `MockShell` | A3 | `SafeArea` 세터, 현재 값 조회 |
| `MockNetSession` | A9~A11 | `SimulateJoin/Leave/State`, `LastBroadcast` |

`MockInputSource` 는 전역 후킹을 하지 않는다. 게임 씬의 `_Input` 에서 `Feed()` 를
불러 주면 된다. **초당 캡(10타)과 100ms 배치는 목에도 들어 있다** — 캡 적용 후의
개수를 받는 것이 계약이므로, 게임 레이어가 실물로 갈아탈 때 동작이 달라지면 안 된다.

`MockNetSession` 은 혼자서도 룸이 만들어진다. 룸 화면(B10~B12)을 최대 인원 4명까지
`SimulateJoin` 으로 채워 놓고 레이아웃을 끝낼 수 있다. 멀티는 컷 라인 6번(=마지막)이라
(§10) 여기까지는 목으로 가는 게 맞다.

---

## 4. 세이브 스키마 v1

`shared/Save/SaveData.cs` 가 기획서 §7-5 의 JSON 과 1:1 대응한다.

**JSON 필드 이름이 계약이므로** 프로퍼티마다 `[JsonPropertyName]` 을 명시했다.
직렬화 옵션의 camelCase 규칙에 기대지 않는 이유는, 옵션은 호출부에서 빠뜨릴 수
있지만 어트리뷰트는 타입에 붙어 다니기 때문이다.

### 검증

```
Godot_..._console.exe --headless --path . -- --selftest
```

왕복 직렬화로 필드 이름 23개를 전부 확인하고, `slots` 와 `slotTimers` 길이가
맞는지, `lastQuitUtc` 가 `Z` 표기로 나가는지까지 본다. 통과하면 종료 코드 0.

**이 자동 검증이 필요한 이유**: 계약 문서와 코드가 갈라지는 것은 눈으로 안 잡힌다.
필드 하나가 `growth_ms` 로 나가도 컴파일은 통과하고, 을이 구현을 끝낸 뒤에야 드러난다.

> 기획서 예시의 `"scale": 1.0` 이 `"scale": 1` 로 나간다. JSON 은 정수/실수를
> 구분하지 않으므로 계약 위반이 아니고, 역직렬화도 정상이다.

### 마이그레이션 훅

`SaveSchema.Migrate(JsonNode)` 가 **v1 밖에 없는 지금도 존재한다.** 나중에 스키마를
바꿀 때 로드 경로를 뜯어고치지 않아도 되게 하려는 것이고, 이게 §7-5 가
"마이그레이션 훅 준비" 라고 쓴 것의 내용이다.

모르는 버전은 조용히 통과시키지 않고 예외를 던진다. 세이브가 깨진 채로 게임이 돌면
유저는 나중에야 알아차린다.

### 아직 안 한 것 — 파일 I/O

**원자적 저장(임시 파일 → rename), `%APPDATA%` 경로, 60초 자동 저장은 A1 이 아니다.**
갑의 "세이브 I/O" 범위이고 별도 일감이다. A1 은 스키마 합의까지다.

을은 `SaveData` 를 직렬화 대상으로 확정하고 게임 레이어 측(B5)을 진행하면 된다.

---

## 5. 다음

| 일감 | 담당 | 이 문서와의 관계 |
|---|---|---|
| ~~A3 셸 모듈화~~ | 갑 | ✅ 완료 (2026-09-15). `src/`→`platform/`, `IShell` 실물(`platform/OverlayShell.cs`), 위치/배율/투명도 저장·복원(`platform/SaveIO.cs`) |
| A4 글로벌 입력 | 갑 | ✅ 완료 (2026-09-14). `IInputSource` 실물 (RawInput, 카운트만) — docs/A4-GLOBAL-INPUT.md |
| A5 커서 꾸미기 정식화 | 갑 | `ICursorLayer` 실물. A2 스파이크 코드 정리 (아직 미착수 — `platform/CursorLayer.cs`는 A3에서 위치만 옮겨졌다) |
| B1~B5 | 을 | **지금 바로 시작 가능.** 목 4종으로 끝까지 돈다 |
