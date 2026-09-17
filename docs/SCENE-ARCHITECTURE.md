# 씬 구조 — 에디터 활용 전환 계획

관련: [A1 계약](A1-CONTRACTS.md) §1 (폴더 분할) / [A3 셸 모듈화](A3-SHELL-MODULE.md) / [확정 기획서](기획확정-일감분배-260907.md) §8-3

> A2~A7까지 에디터 활용이 사실상 0이었다 — `Shell.tscn`은 루트 노드 하나뿐이고
> 나머지는 전부 `OverlayShell.BuildScene()`이 코드로 지었다. **의도된
> 스파이크 상태**였다(DAY1-2-SPIKE.md: "게임 본편으로 넘어가면 에디터에서
> 씬을 짜야 한다"). B1(코어 루프)이 그 시점이다. 이 문서는 을이 착수하기
> 전에 먼저 세워야 했던 경계 하나(§1)와, 앞으로의 씬/폴더 배치(§2)를 담는다.

---

## 0. 상태 (2026-09-17)

| 산출물 | 위치 | 상태 |
|---|---|---|
| `IInteractiveArea` 계약 | `shared/Contracts/IInteractiveArea.cs` | ☑ |
| `SceneGroups.GameRoot` 그룹 상수 | `shared/Contracts/Types.cs` | ☑ |
| 자리표시자 구현체 | `platform/PlaceholderMascot.cs` | ☑ |
| `OverlayShell`을 자리표시자/실물 양쪽으로 동작하게 리팩터 | `platform/OverlayShell.cs` | ☑ |
| `Shell.tscn` → `platform/`로 이동 | `platform/Shell.tscn` | ☑ |
| `game/` 하위 폴더 골격 | `game/{entities,ui,effects,multiplayer}/` | ☑ (빈 폴더, `.gitkeep`) |
| 실제 게임 콘텐츠 씬 (Tree/Monkey/GameRoot 등) | `game/` | ☑ B1 완료 |
| `IPlatformServices` — 실물을 게임 레이어로 넘기는 통로 | `shared/Contracts/IPlatformServices.cs` | ☑ 2026-09-17 |
| `OverlayShell` 을 4개 partial 로 분할 | `platform/OverlayShell*.cs` | ☑ 2026-09-17 (§4) |

---

## 1. `IInteractiveArea` — 클릭 영역의 방향을 뒤집었다

### 문제

`OverlayShell`(플랫폼)이 클릭 통과 폴리곤을 계산하려고 `_mascot`(게임 콘텐츠
스프라이트)를 **직접 참조**하고 있었다. 자리표시자(`icon.svg` 하나)일 때는
문제가 안 됐지만, 을이 실제 나무+원숭이 씬을 만들면 플랫폼 코드가 게임
레이어의 노드를 직접 아는 상태가 된다 — §8-3 "서로의 담당 폴더는 건드리지
않는다"가 씬 레벨에서 깨지는 것이다.

### 해법 — 방향이 반대인 계약

`IShell`/`ICursorLayer`/`IInputSource`는 전부 "갑 제공 → 을 소비"다.
`IInteractiveArea`는 **반대**다:

```csharp
public interface IInteractiveArea
{
    Rect2 GetClickableBounds();   // 셸 루트의 로컬 좌표계 기준
}
```

**을이 구현**하고(`game/GameRoot`), **갑(`OverlayShell`)이 매 프레임 읽는다.**
게임 레이어는 "여기가 클릭 영역이다"만 알려주고, 그 값으로 무엇을 할지
(배율 적용, 클릭 여백 추가, `WindowSetMousePassthrough` 호출)는 전부
플랫폼이 결정한다.

별도 "값이 바뀌었다" 이벤트는 없다 — `OverlayShell`이 이미 매 프레임
`ApplyPassthrough()`를 통해 클릭 영역을 다시 계산하므로(플리커 회피 로직과
무관하게, `BuildRegion()`은 항상 최신 값을 읽는다) 폴링만으로 충분하다.
애니메이션으로 매 프레임 값이 바뀌어도 그냥 되읽힌다.

### 타입 의존 없이 찾는 법 — `SceneGroups.GameRoot`

`OverlayShell`이 `game/GameRoot`의 **타입**을 컴파일 타임에 알면 또 같은
문제가 생긴다. 그래서 Godot 그룹으로 찾는다:

```csharp
// game/GameRoot.cs, _Ready() 안에서
AddToGroup(ProjectSeWoo.Shared.SceneGroups.GameRoot);   // "game_root"
```

```csharp
// platform/OverlayShell.cs, BuildScene() 안에서 (이미 되어 있음)
Node gameRootNode = GetTree().GetFirstNodeInGroup(SceneGroups.GameRoot);
if (gameRootNode is IInteractiveArea area)
{
    _content = area;
}
else
{
    _content = new PlaceholderMascot(_mascot);   // 지금 기본 경로
}
```

**을이 할 일은 이 두 줄이 전부다** — `game/GameRoot`를 만들고
`IInteractiveArea`를 구현하고 그룹에 등록하면, `platform/OverlayShell.cs`는
단 한 글자도 안 고쳐도 자리표시자 대신 실물을 쓴다. `PlaceholderMascot`가
사라지는 날(더 이상 아무도 안 쓰는 게 확인되면) 그게 B1 착수 완료 신호다.

### 검증

리팩터 전후 동작이 똑같은지 확인했다 — `MascotRect()`가 하던 계산(스프라이트
크기·위치를 셸 배율만큼 곱하는 것)을 그대로 `PlaceholderMascot`으로 옮겼고,
`OverlayShell`은 `CurrentHitRect()`를 통해 `_content.GetClickableBounds()`를
읽어 배율·클릭 여백을 적용한다. `--report=` 무인 실행으로 재시작해서
크래시 없음, `game/GameRoot`가 없으니 자리표시자 경로를 타는 것 확인.

---

## 2. 폴더/씬 배치

### 원칙 — 씬을 스크립트 옆에 둔다

지금까지 `scenes/`(씬 전용)와 `platform/`(코드 전용)이 갈라져 있었다.
**이걸 더 늘리지 않는다.** Godot 관례(씬+스크립트 co-location)를 따르고,
§8-3의 폴더 소유권 규칙이 씬 파일까지 자동으로 적용되게 한다.

```
platform/Shell.tscn + OverlayShell.cs   (scenes/ 에서 이동 완료)
```

`scenes/` 폴더는 이제 없다. 앞으로 새 씬은 자기 스크립트가 있는 폴더
(`platform/` 또는 `game/`)에 바로 만든다.

### 씬 단위 — 기능별로 어떻게 자르는가

씬 하나 = "재사용 가능하고 독립적으로 테스트 가능한 단위". 지금 계획된
기능 기준으로 자르면:

| 씬 | 담당 | 폴더 | 내용 |
|---|---|---|---|
| `Shell.tscn` (기존) | 갑 | `platform/` | 창 루트. 얇게 유지 - 창 mechanics만 |
| `CursorDeco.tscn` (코드→씬 전환 권장) | 갑 | `platform/cursor/` | A5의 3슬롯 배치. 흔들림 애니메이션 등은 에디터가 편함 |
| `OptionsWindow.tscn` (코드→씬 전환 권장) | 갑 | `platform/ui/` | A6 옵션창. 슬라이더/체크박스 레이아웃 |
| `GameRoot.tscn` (신규) | 을 | `game/` | 나무+원숭이+게임 내 HUD를 묶은 전체. `IInteractiveArea` 구현, `Shell.tscn`이 자식으로 instance하거나 별도 로드 |
| `Tree.tscn` | 을 | `game/entities/` | 나무 하나. 슬롯 N개(강화로 증가) |
| `TreeSlot.tscn` | 을 | `game/entities/` | 슬롯 재사용 단위 - `Tree`가 N번 instance |
| `Monkey.tscn` | 을 | `game/entities/` | 펀치 애니메이션(AnimationPlayer), 키 입력 반응 - B2 마이크로 피드백이 여기로 옮겨온다 |
| `PunchImpact.tscn`, `LeafParticle.tscn` | 을 | `game/effects/` | 파티클/이펙트 |
| `Shop.tscn`, `Inventory.tscn`, `Collection.tscn`, `Onboarding.tscn` | 을 | `game/ui/` | 상점/장착/도감/온보딩 |
| `RoomView.tscn`, `RemotePlayerView.tscn` (W3) | 을 | `game/multiplayer/` | 룸 화면, 원격 플레이어 1명당 1 instance |

`game/`의 4개 하위 폴더(`entities/`, `ui/`, `effects/`, `multiplayer/`)는
지금 빈 채로 만들어 뒀다 - 을이 시작할 자리를 미리 잡아 둔 것뿐이다.

### `Shell.tscn`과 `GameRoot.tscn`을 어떻게 연결하는가

두 가지 방법이 있다:

1. **에디터로 조립** (추천) — `Shell.tscn`을 열고 `GameRoot.tscn`을 자식으로
   드래그해서 넣는다. `OverlayShell.cs`는 코드를 안 고쳐도 된다 -
   `BuildScene()`의 그룹 조회가 씬 트리 어디에 있든 찾아낸다. 을이 게임
   내용을 바꿀 때마다 갑 코드를 안 건드려도 되는 게 핵심 이점이다.
2. **코드로 로드** — `GD.Load<PackedScene>("res://game/GameRoot.tscn").Instantiate()`를
   `OverlayShell`이 직접 호출. 1번보다 결합이 세지므로 추천하지 않는다.

**1번을 쓰려면 `Shell.tscn`을 에디터로 한 번 열어야 한다** - 지금까지 전부
코드/텍스트 편집으로만 다뤄와서, 이 프로젝트에서 처음 있는 일이다.

---

## 3. 다음

| 일감 | 관계 |
|---|---|
| B1 착수 | `game/GameRoot.tscn` 생성 + `IInteractiveArea` 구현 + `SceneGroups.GameRoot` 등록이 첫 걸음 |
| `CursorDeco`/`OptionsWindow` 코드→씬 전환 | 급하지 않음. 실제로 애니메이션/레이아웃을 자주 손볼 때가 되면 |
| `PlaceholderMascot` 제거 | `game/GameRoot`가 안정화되면 |

---

## 4. `OverlayShell` 분할 (2026-09-17)

한 파일 1,419줄이 됐고, 그중 3분의 1이 "창을 띄우는 일"이 아니라 "창이 얼마를
먹는지 재는 일"이었다. §2가 요구한 "`Shell.tscn` 은 창 mechanics만, 얇게 유지"를
파일이 스스로 어기고 있던 상태다.

`partial class` 4개로 갈랐다. **클래스를 쪼개지 않은 것은 의도다** — 계측이
셸 내부 상태 18개를 읽으므로 별도 클래스로 빼면 그만큼을 `internal` 로
열어야 하고, 그건 캡슐화가 아니라 캡슐화 시늉이다. 여기서 가르는 것은
바이너리가 아니라 **읽는 사람의 주의**다.

| 파일 | 줄 | 내용 |
|---|---|---|
| `OverlayShell.cs` | ~730 | 기동 · 씬 · `IShell`/`IPlatformServices` 실물 · 세이브 복원 · 클릭 통과 · 프레임 루프 · 드래그 |
| `OverlayShell.Visibility.cs` | ~245 | 표시/숨김(Win32 `ShowWindow`) · 트레이 · 옵션 창 · 전체화면 자동 숨김 (A6) |
| `OverlayShell.Diagnostics.cs` | ~420 | HUD 통계 · F9 리포트 · `--report=` 무인 측정 · `--steam-selftest` |
| `OverlayShell.DebugKeys.cs` | ~195 | 디버그 키 전부. 주인이 생기면 지울 것들 |

`Visibility` 가 표시/숨김과 트레이/옵션을 같이 든 이유는 넷이 전부
`ApplyVisibility()` 하나로 수렴하기 때문이다 — 그 "계산 지점이 하나뿐" 이라는
성질이 A6 상태 꼬임 방지책의 전부라, 흩어 놓으면 눈에 안 보이게 된다.

**릴리스에서 계측을 빼지 않는다.** `#if DEBUG` 로 감싸고 싶어지지만 A7 메모리
게이트는 `ExportRelease` 빌드를 상대로 재서 닫혔고(A7-PERF.md §4),
`tools/measure-renderers.ps1` 이 재는 것도 릴리스 빌드다.
