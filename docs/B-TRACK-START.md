# 을 시작 가이드 — B1부터 어떻게 붙는가

관련: [확정 기획서](기획확정-일감분배-260907.md) §2 · §9 (Week 1) / [A1 계약](A1-CONTRACTS.md) / [씬 구조](SCENE-ARCHITECTURE.md)

> **이 문서는 을을 대상으로 쓴다.** 갑 쪽(A1~A8, A12 + 씬 구조 정비)이 끝나서
> 지금 시점부터 B1(코어 루프)을 바로 시작할 수 있다. **갑의 실물을 하나도
> 기다릴 필요가 없다** — 그게 A1이 목(Mock)을 먼저 만들어 둔 이유다.

> **2026-09-16 갱신.** 게임명이 `PunchMonkey` 로 확정됐고, 스팀 앱 ID 가 실물
> (`5281130`)로 바뀌었고, 릴리스 빌드 파이프라인(A12)이 생겼다. **셋 다 `game/`
> 코드에는 영향이 없다** — 을이 신경 쓸 것은 §2 개발 환경의 `GODOT` 환경변수
> 한 줄뿐이다. 그 외 오늘 바뀐 것의 요약은 [docs/README.md](README.md).

---

## 0. 지금 뭐가 준비돼 있는가

| 것 | 위치 | 을이 알아야 할 것 |
|---|---|---|
| 인터페이스 6종 | `shared/Contracts/` | 시그니처만 보면 된다. 구현은 몰라도 됨 |
| 목(Mock) 6종 | `shared/Mocks/` | §3 참고. **이제 직접 `new` 하지 않는다** — §3 갱신분 |
| 세이브 스키마 v2 | `shared/Save/SaveData.cs` | 이 클래스가 직렬화 대상. §7-5 JSON과 1:1 대응 |
| 실물 5종(A3~A6, A8) | `platform/` | **안 봐도 된다.** 목과 똑같이 동작하는 걸로 취급 |
| `game/` 코어 루프 (B1) | `game/GameRoot.*`, `game/entities/` | **이미 있다.** B2 부터 이어서 붙인다 |
| `Shell.tscn` | `platform/Shell.tscn` | 손대지 않는다. `GameRoot`를 자식으로 넣는 것만 예외 |
| 릴리스 빌드 (A12) | `tools/build-release.ps1` | **안 써도 된다.** 스팀에 올릴 때만 쓴다 — 개발 중에는 §2 의 VS F5 로 돈다 |

**갑의 실물 코드(`platform/OverlayShell.cs` 등)를 읽을 필요가 없다.** 인터페이스
시그니처가 목과 실물에 똑같이 걸려 있고, 어느 쪽이 들어오는지는 셸이 정한다 —
게임 레이어 코드는 양쪽에서 한 글자도 안 바뀐다. 읽어야 할 갑 쪽 파일은
`shared/Contracts/` 뿐이다.

---

## 1. 읽는 순서

1. [기획확정-일감분배-260907.md](기획확정-일감분배-260907.md) §2(코어 루프)·§3(커서 꾸미기)·§6(누적 타수) — **게임 설계 본문**
2. [A1-CONTRACTS.md](A1-CONTRACTS.md) §2(인터페이스 4종)·§3(목 구현) — 뭘 호출하면 되는지
3. [SCENE-ARCHITECTURE.md](SCENE-ARCHITECTURE.md) 전체 — 씬을 어디에 어떻게 만드는지, `GameRoot`가 뭘 해야 하는지
4. 이 문서 §4 — 실제 착수 순서

---

## 2. 개발 환경

`docs/DEVLOG.md`(2026-09-13 항목)에 이미 정리돼 있다. 요약:

- Godot **.NET(mono) 빌드** 필요 (표준 빌드는 C# 못 읽음) — `Godot_v4.7.2-stable_mono_win64`
- 코드는 C# 단일. `ProjectSeWoo.sln`을 Visual Studio로 열면 `ProjectSeWoo`/`VsLauncher`/`InputHelper` 세 프로젝트가 보인다
- **씬 편집은 Godot 에디터, 코드 디버깅은 VS F5(`VsLauncher` 시작 프로젝트로 고정)** — 둘이 별도 프로세스라 동시에 켜놔도 된다
- 실행: `Godot_v4.7.2-stable_mono_win64.exe --path .`

> ### ⚠ `GODOT` 환경변수를 먼저 설정할 것
>
> VS F5 는 `VsLauncher` 가 Godot 실행 파일을 찾아서 띄우는 구조다. 찾는 순서는
> **`GODOT` 환경변수 → 하드코딩된 폴백 경로** 인데, **폴백은 갑의 PC 경로다.**
> 을의 PC 에 그 경로가 있을 리 없으므로 환경변수를 설정하지 않으면 F5 가 바로
> 실패한다.
>
> ```powershell
> # 값은 Godot .NET(mono) exe 의 전체 경로. 예:
> setx GODOT "C:\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
> ```
>
> `setx` 후에는 **Visual Studio 를 다시 띄워야** 새 환경변수가 붙는다.
>
> (2026-09-16 에 그 폴백 경로 자체에 오타가 있던 것도 고쳤다. 갑 PC 에서는
> 환경변수가 있어서 그 줄을 탈 일이 없어 안 드러났고, **을이 첫 F5 를 누르는
> 순간 만날 자리였다.**)

---

## 3. 목(Mock) — 실물이 없을 때만 쓴다

`shared/Mocks/`에 있다 (2026-09-17 에 `platform/Mocks/` 에서 이동, 네임스페이스는
`ProjectSeWoo.Shared.Mocks`).

> ### ⚠ 2026-09-17 갱신 — 이제 목을 직접 `new` 하지 않는다
>
> **A1~A8 실물이 전부 끝났으므로 기본값은 실물이다.** `GameRoot` 는
> `IPlatformConsumer` 를 구현하고, 셸이 `AttachPlatform(IPlatformServices)` 로
> 실물 5종을 통째로 넘긴다 (`shared/Contracts/IPlatformServices.cs`).
>
> **`_Ready()` 안에서 입력을 구독하면 안 된다.** Godot 은 자식의 `_Ready` 를
> 부모보다 먼저 부르는데 실물을 만드는 것은 부모(`OverlayShell`)라, 그 시점에는
> 아직 아무것도 안 와 있다. 실물이 필요한 배선은 전부 `AttachPlatform` 안에서 한다 —
> 순서를 틀리면 증상이 **"조용히 아무 일도 안 일어남"** 이라 눈에 안 띈다.
> (실제로 B1 직후가 그 상태였다.)
>
> 목은 두 자리에 남는다. ① `Shell.tscn` 없이 `game/GameRoot.tscn` 만 단독
> 실행할 때 — `MockPlatformServices` 로 자동 폴백한다. ② 계약 시험 —
> `IsAvailable`/`IsSupported` 를 꺼서 "전역 입력이 막힌 PC", "커서 축이 죽은
> 환경", "스팀 오프라인" 을 만들어 볼 때. 실물로는 재현하기 어려운 것들이다.

| 목 | 시험용 API | 예시 |
|---|---|---|
| `MockInputSource` | `Feed(count)`, `Tick(delta)`, `IsAvailable` 세터 | 키 입력 대신 `_input.Feed(1)`을 아무 데서나 불러서 "타건이 왔다"를 흉내 |
| `MockCursorLayer` | `Equip(slot, id)`, `SetEnabled`, `IsSupported` 세터, `GetEquipped(slot)` | 상점에서 산 아이템을 장착했을 때 `_cursor.Equip(CursorSlot.Hang, "monkey_02")` |
| `MockShell` | `SetScale/SetOpacity/SetClickThrough`, `SafeArea` 세터 | 게임 레이어가 창 크기를 직접 묻지 않고 `_shell.GetSafeArea()`로 배치 |
| `MockNetSession` | `SimulateJoin/Leave/State`, `LastBroadcast` | 룸 화면(B10~B12)을 혼자서도 4명까지 채워서 레이아웃 테스트 |
| `MockAchievements` | `IsAvailable` 세터, `Reset()` | 해금 조건(도감 100% / 타수 마일스톤)을 `AchievementIds` 표로 돌려서 시험. **실물은 A15 전까지 해금이 안 되므로 조건 로직 검증은 목으로만 가능하다** |

`MockShell.SafeArea`에 **음수 원점**을 넣어서 꼭 한 번 시험해 볼 것 — 개발 PC가
모니터 3대에 하나는 X가 음수다. 게임 레이어가 (0,0)을 원점으로 가정하면
거기서 깨진다.

---

## 4. B1 착수 순서

### 4-1. `game/GameRoot.tscn` + `GameRoot.cs` 만들기

에디터에서 `game/` 폴더에 새 씬 → 루트를 `Node2D`로. 스크립트를 같은 폴더에
`GameRoot.cs`로 붙인다. **이 클래스가 `IInteractiveArea`를 구현하고
그룹에 등록하는 두 줄이 갑 쪽과 연결되는 유일한 접점이다** (SCENE-ARCHITECTURE.md §1):

> **2026-09-17 — 이 절은 끝났다.** `game/GameRoot.tscn`/`.cs` 가 이미 있다.
> 아래 골격은 **갑과 맞닿는 두 지점이 무엇인지** 보여주려고 남긴다. 실제 파일은
> `game/GameRoot.cs` 를 그대로 보면 된다.

```csharp
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

//                     (1) 클릭 영역을 신고한다   (2) 실물을 받는다
public partial class GameRoot : Node2D, IInteractiveArea, IPlatformConsumer
{
    private IPlatformServices _platform;

    public override void _Ready()
    {
        AddToGroup(SceneGroups.GameRoot);   // <- 이게 없으면 갑이 우리를 못 찾는다

        // 씬 노드 잡기, 세이브 읽기 같은 "우리만으로 되는 것" 은 여기서.
        // **실물이 필요한 배선은 여기 두면 안 된다** - 아직 안 왔다.
    }

    public void AttachPlatform(IPlatformServices platform)
    {
        _platform = platform;
        _platform.Input.OnKeystrokes += OnKeystrokes;   // <- 전역 타건이 여기로 온다
    }

    private void OnKeystrokes(int count)
    {
        // 원숭이 펀치 트리거, 빈 나무면 헛펀치
    }

    // 나무 + 원숭이의 합 영역. 셸 루트 로컬 좌표계 기준이다.
    public Rect2 GetClickableBounds() => Transform * _tree.GetBounds().Merge(_monkey.GetBounds());
}
```

`_platform.Input` 은 **틱을 돌리지 않는다** — 실물의 폴링은 셸이 이미 굴리고
있고, 계약(`IInputSource`)에 틱이 아예 없는 것이 그 뜻이다. 목으로 단독 실행할
때만 `MockPlatformServices.Tick(delta)` 를 부른다 (`GameRoot._Process` 참고).

### 4-2. `Shell.tscn`에 `GameRoot`를 자식으로 넣기

**이미 되어 있다** (`platform/Shell.tscn`). `OverlayShell` 은 그룹 조회로 찾으므로
씬 트리 어디에 넣든 코드를 안 고친다. 실행해서

```
[shell] 실물 전달 완료 - input=헬퍼 정상 cursor=실물 ach=... net=mock(A9~A11 대기)
```

로그가 뜨면 연결된 것이다. 반대로 `[game] 플랫폼 미연결 - 목으로 돈다` 가 뜨면
`AddToGroup(SceneGroups.GameRoot)` 가 빠졌거나 `Shell.tscn` 자식 구성이 깨진 것이다.

### 4-3. `game/entities/`에 콘텐츠 씬 만들기

SCENE-ARCHITECTURE.md §2 표 순서대로:

1. `TreeSlot.tscn` — 슬롯 하나. [비어있음] → (타이머) → [바나나 열림] 상태 표현
2. `Tree.tscn` — `TreeSlot`을 `SaveData.Tree.Slots`개 만큼 instance
3. `Monkey.tscn` — 펀치 애니메이션(AnimationPlayer 4종 교차, §2-3)

### 4-4. 세이브 필드에 맞춰 상태를 유지한다

`shared/Save/SaveData.cs`가 계약이다. 나무 슬롯 수·타이머는
`SaveData.TreeState`(`Slots`/`GrowthMs`/`SlotTimers`), 재화는
`SaveData.Bananas`, 커서 장착은 `SaveData.InventoryState.Equipped` 그대로
쓰면 된다 — **이 타입을 직접 직렬화 대상으로 쓰고, 별도 포맷을 만들지 않는다.**

> **2026-09-17 (B5) — `SaveIO` 를 직접 부르지 않는다.**
> `_platform.Save`(`ISaveStore`)를 쓴다. 세션에 `SaveData` 인스턴스는 **하나뿐**이고
> 셸과 게임이 그 객체를 같이 고친다:
>
> ```csharp
> _store.Data.Bananas += harvested;   // 고치고
> _store.MarkDirty();                 // 알리면 끝. 디스크 쓰기는 플랫폼이 묶어서 한다
> ```
>
> 각자 `SaveIO.Load()` → 자기 몫 수정 → `SaveIO.Save()` 를 하면 사이에 낀 상대의
> 변경이 사라진다. 드래그 한 번에 바나나가 되돌아가는 종류의 버그고 타이밍에
> 달려 있어 잡기 어렵다. `FlushNow()` 는 종료 경로에서만 부른다.
>
> 오프라인 성장은 `Tree.AdvanceOffline(ms)` 이고, 기준점 `lastQuitUtc` 는 실제로는
> "마지막으로 저장한 시각" 이다 — 그래서 강제 종료도 정상 종료와 같은 경로를 탄다.

---

## 5. B1~B5 요약 (기획서 §9 Week 1)

| # | 일감 | 추정 | 핵심 수치/사양 (§2) |
|---|---|---|---|
| B1 | 코어 루프 프로토 | 2d | 슬롯 3개, 성장 8분, 펀치 1회=바나나 1개. §2-1/§2-2 |
| B2 | 마이크로 피드백 | 1d | **빈 나무를 쳐도 반응 필수.** 펀치 4종 랜덤 교차, 나무 흔들림, 잎 파티클. §2-3 표 그대로 |
| B3 | 타수 카운터 + 레벨 | 0.5d | 재화 아님, 기록용. 로그 스케일 레벨 환산. §6 |
| B4 | 플레이스홀더 아트 + 씬 정리 | 0.5d | 실물 에셋(B9)은 W2. 지금은 자리표시자로 씬 구조부터 |
| B5 | 세이브/로드 게임 레이어 측 | 1d | `SaveData` 직렬화 대상 확정. 파일 I/O는 갑이 이미 만듦(§4-4) |

**게이트 없음.** A2(커서) 같은 "먼저 될지 안 될지 확인해야 하는" 항목이 아니다 —
목으로 끝까지 돌아가는 게 이미 확인된 설계다.

---

## 6. 협업 규칙 재확인 (§8-3)

- `game/` 폴더만 건드린다. `platform/`, `shared/`는 **바꿀 일이 생기면 먼저 알린다.**
- 인터페이스 시그니처를 바꿔야 하면 `A1-CONTRACTS.md` §0 표에 날짜+사유를 남기고
  상대에게 알린 뒤에만 (§8-2 규칙, `IInteractiveArea`도 동일하게 적용).
- 세이브 스키마 필드를 바꾸면 `SaveSchema.CurrentVersion`을 올리고
  `Migrate`를 채운다 (v1→v2 실제 사례가 `shared/Save/SaveSchema.cs`에 있다 - 그대로 따라 하면 됨).
- 금요일 데모 빌드 + 주말 실사용 (§8-3).

---

## 7. 막히면

| 상황 | 참고 |
|---|---|
| 인터페이스가 뭘 요구하는지 모르겠다 | `shared/Contracts/*.cs` 문서 주석 - 계약 근거가 전부 적혀 있다 |
| 목이 실물과 다르게 동작하는 것 같다 | `A1-CONTRACTS.md` §3 표, 안 맞으면 갑에게 알린다 |
| 클릭 영역이 이상하게 잡힌다 | `SCENE-ARCHITECTURE.md` §1, `IInteractiveArea.GetClickableBounds()`가 **셸 루트 로컬 좌표**를 기대한다는 것 확인 |
| 세이브 필드 이름이 헷갈린다 | `shared/Save/SaveData.cs` - JSON 필드명은 어트리뷰트로 고정, 기획서 §7-5 예시와 대조 |
| 개발 환경 자체가 안 된다 | `docs/DEVLOG.md` 2026-09-01/09-13 항목의 "밟은 함정" 목록 |
