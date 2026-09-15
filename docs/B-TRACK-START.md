# 을 시작 가이드 — B1부터 어떻게 붙는가

관련: [확정 기획서](기획확정-일감분배-260907.md) §2 · §9 (Week 1) / [A1 계약](A1-CONTRACTS.md) / [씬 구조](SCENE-ARCHITECTURE.md)

> **이 문서는 을을 대상으로 쓴다.** 갑 쪽(A1~A7 + 씬 구조 정비)이 끝나서
> 지금 시점부터 B1(코어 루프)을 바로 시작할 수 있다. **갑의 실물을 하나도
> 기다릴 필요가 없다** — 그게 A1이 목(Mock) 4종을 먼저 만들어 둔 이유다.

---

## 0. 지금 뭐가 준비돼 있는가

| 것 | 위치 | 을이 알아야 할 것 |
|---|---|---|
| 인터페이스 5종 | `shared/Contracts/` | 시그니처만 보면 된다. 구현은 몰라도 됨 |
| 목(Mock) 4종 | `platform/Mocks/` | **지금 당장 쓸 것.** §1 참고 |
| 세이브 스키마 v2 | `shared/Save/SaveData.cs` | 이 클래스가 직렬화 대상. §7-5 JSON과 1:1 대응 |
| 실물 4종(A3~A6) | `platform/` | **안 봐도 된다.** 목과 똑같이 동작하는 걸로 취급 |
| `game/` 폴더 골격 | `game/{entities,ui,effects,multiplayer}/` | 여기가 을 작업 공간. 지금은 전부 빈 폴더 |
| `Shell.tscn` | `platform/Shell.tscn` | 손대지 않는다. `GameRoot`를 자식으로 넣는 것만 예외 |

**갑의 실물 코드(`platform/OverlayShell.cs` 등)를 읽을 필요가 없다.** 목이 인터페이스와
똑같이 동작하도록 이미 맞춰져 있다(A1 문서 §3). 나중에 목에서 실물로 갈아끼울 때
게임 레이어 코드는 한 줄도 안 바뀌는 게 이 구조의 요점이다.

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

---

## 3. 목(Mock) 4종 — 지금 이걸로 시작한다

`platform/Mocks/`에 있다. `GameRoot`가 생성자나 `_Ready()`에서 직접 `new` 해서 쓰면 된다.

| 목 | 시험용 API | 예시 |
|---|---|---|
| `MockInputSource` | `Feed(count)`, `Tick(delta)`, `IsAvailable` 세터 | 키 입력 대신 `_input.Feed(1)`을 아무 데서나 불러서 "타건이 왔다"를 흉내 |
| `MockCursorLayer` | `Equip(slot, id)`, `SetEnabled`, `IsSupported` 세터, `GetEquipped(slot)` | 상점에서 산 아이템을 장착했을 때 `_cursor.Equip(CursorSlot.Hang, "monkey_02")` |
| `MockShell` | `SetScale/SetOpacity/SetClickThrough`, `SafeArea` 세터 | 게임 레이어가 창 크기를 직접 묻지 않고 `_shell.GetSafeArea()`로 배치 |
| `MockNetSession` | `SimulateJoin/Leave/State`, `LastBroadcast` | 룸 화면(B10~B12)을 혼자서도 4명까지 채워서 레이아웃 테스트 |

`MockShell.SafeArea`에 **음수 원점**을 넣어서 꼭 한 번 시험해 볼 것 — 개발 PC가
모니터 3대에 하나는 X가 음수다. 게임 레이어가 (0,0)을 원점으로 가정하면
거기서 깨진다.

---

## 4. B1 착수 순서

### 4-1. `game/GameRoot.tscn` + `GameRoot.cs` 만들기

에디터에서 `game/` 폴더에 새 씬 → 루트를 `Node2D`로. 스크립트를 같은 폴더에
`GameRoot.cs`로 붙인다. **이 클래스가 `IInteractiveArea`를 구현하고
그룹에 등록하는 두 줄이 갑 쪽과 연결되는 유일한 접점이다** (SCENE-ARCHITECTURE.md §1):

```csharp
using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Platform.Mocks;

namespace ProjectSeWoo.Game;

public partial class GameRoot : Node2D, IInteractiveArea
{
    private MockInputSource _input;
    private MockCursorLayer _cursor;
    private MockShell _shell;
    private MockNetSession _net;

    // TODO: Tree/Monkey 노드로 교체
    private Sprite2D _placeholder;

    public override void _Ready()
    {
        AddToGroup(SceneGroups.GameRoot);   // <- 이게 없으면 플랫폼이 자리표시자를 계속 쓴다

        _input = new MockInputSource();
        _cursor = new MockCursorLayer();
        _shell = new MockShell();
        _net = new MockNetSession();

        _input.OnKeystrokes += OnKeystrokes;

        // TODO: 나무/원숭이 씬 instance, 세이브 로드
    }

    public override void _Process(double delta)
    {
        _input.Tick(delta);
        // TODO: 나무 슬롯 성장 타이머
    }

    private void OnKeystrokes(int count)
    {
        // TODO: 원숭이 펀치 트리거, 빈 나무면 헛펀치
    }

    // IInteractiveArea - 지금은 자리표시자 스프라이트 기준. 나무/원숭이가
    // 생기면 그 노드들의 합 영역으로 바꾼다.
    public Rect2 GetClickableBounds()
    {
        Vector2 size = _placeholder.Texture.GetSize() * _placeholder.Scale;
        return new Rect2(_placeholder.Position - size * 0.5f, size);
    }
}
```

키 입력을 직접 받으려면(테스트용으로) `_UnhandledInput`에서
`if (@event is InputEventKey { Pressed: true }) _input.Feed(1);` 정도로 흉내
낼 수 있다 — 실물(A4)은 포커스 없이 전역으로 받지만, 목은 그럴 필요가 없다
(§0 표, IInputSource 계약은 "가끔 이벤트가 온다"까지만 보장한다).

### 4-2. `Shell.tscn`에 `GameRoot`를 자식으로 넣기

Godot 에디터로 `platform/Shell.tscn`을 열고, `game/GameRoot.tscn`을 씬 트리에
드래그해서 자식으로 넣는다. **`OverlayShell.cs`는 코드를 한 줄도 안 고친다** —
`BuildScene()`의 그룹 조회가 자동으로 찾아서 자리표시자 대신 쓴다. 실행해서
`[shell] game/GameRoot 발견 - 자리표시자 마스코트를 안 만든다` 로그가 뜨면 연결 확인된 것.

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
파일 I/O(`platform/SaveIO.cs`)는 이미 갑이 만들어 뒀지만 지금은 `Settings`
필드만 쓰고 있다 — B5에서 나무/인벤토리 필드도 왕복시키게 되면 자동으로 저장된다
(`SaveIO.Save(SaveData)`가 객체 전체를 쓰므로 을 쪽 코드 변경 없이 바로 됨).

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
