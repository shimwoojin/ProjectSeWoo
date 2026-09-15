# A3 — 오버레이 셸 모듈화

관련: [확정 기획서](기획확정-일감분배-260907.md) A3 · §7-1 · §8-2 / [A1 계약](A1-CONTRACTS.md) §1, §5

> A1이 `IShell`을 인터페이스로 정의하고 `MockShell`로 을을 먼저 움직이게 했다.
> A3는 그 실물을 만드는 일이다 — 동시에 Day 1-2 스파이크 코드(`src/`)를
> 정식 위치(`platform/`)로 옮기는 것도 겸한다.

---

## 0. 상태 (2026-09-15) — 완료

| 산출물 | 위치 | 상태 |
|---|---|---|
| `src/` → `platform/` 이동 | `platform/OverlayShell.cs` 등 4개 | ☑ `git mv`로 이력 보존 |
| `IShell` 실물 | `platform/OverlayShell.cs` | ☑ (아래 §1) |
| 창 위치/배율/투명도 저장·복원 | `platform/SaveIO.cs` | ☑ (아래 §2) |
| DPI/멀티모니터 안전 영역 | `IShell.GetSafeArea()` | ☑ (아래 §3) |
| 세이브 파일 I/O (A1이 "안 한 것"으로 남겼던 것) | `platform/SaveIO.cs` | ☑ 최소 구현 |

---

## 1. `IShell` 실물 — `OverlayShell`이 직접 구현한다

별도 `Shell` 클래스를 새로 만들지 않고 **`OverlayShell`이 `IShell`을 직접 구현**했다.
`HelperInputSource`가 `IInputSource`를 직접 구현하는 것과 같은 결이다 — 이미 `_win`을
쥐고 있는 클래스에 위임용 중간 클래스를 하나 더 끼우면 상태가 두 곳으로 갈린다.

### `SetScale(float)` — 루트 `Node2D.Scale`을 건드린다

```csharp
_uiScale = Mathf.Clamp(s, 0.5f, 2.0f);
Scale = Vector2.One * _uiScale;
_win.Size = new Vector2I(...);   // 창도 같이 키운다
ApplyPassthrough(force: true);   // 클릭 영역 재계산
```

**창을 같이 키우는 이유** — 안 그러면 커진 마스코트가 창 밖으로 잘리고, 클릭 영역
(passthrough 폴리곤)도 창 밖으로 나간 부분은 못 먹는다.

**`MascotRect()`에 `_uiScale`을 직접 곱해야 했다** — `_mascot.Position`/`Scale`은
이 노드의 로컬 좌표계이고, Godot 렌더링은 부모의 `Scale`을 자동 반영하지만
`DisplayServer.WindowSetMousePassthrough`에 넘기는 좌표(Win32 `SetWindowRgn`)는
그 렌더링 파이프라인을 안 거치는 별도 경로다. 렌더된 그림과 클릭 영역이 따로
계산되므로, 배율이 바뀌면 후자를 수동으로 다시 맞춰야 한다.

### `SetOpacity(float)` — `Modulate`

```csharp
_opacity = Mathf.Clamp(a, 0.1f, 1.0f);
Modulate = new Color(1f, 1f, 1f, _opacity);
```

하한을 0이 아니라 0.1로 잡았다. 옵션 화면(A6)이 아직 없는 상태에서 완전 투명(0)까지
허용하면 유저가 창을 되찾을 UI 자체가 사라진다 — 상주 앱에서 실제로 일어나는 사고
패턴이다.

### 뜻밖의 소득 — `CanvasLayer`/`Window`는 부모의 Scale/Modulate를 안 받는다

`DebugHud`(`CanvasLayer`)와 A2의 커서 장식 창(`Window`)은 둘 다 `OverlayShell`
(`Node2D`)의 자식으로 붙어 있지만, **`CanvasLayer`와 `Window`는 `CanvasItem`이
아니라서** 부모의 `Scale`/`Modulate`를 상속하지 않는다. 즉:

- 셸 배율을 바꿔도 HUD 글자 크기는 항상 그대로 — 배율을 아무리 낮춰도 HUD를 못 읽게
  되는 사고가 구조적으로 안 난다.
- 셸 투명도를 낮춰도 커서 장식 창은 영향을 안 받는다 — 서로 다른 기능이 우연히
  얽히지 않는다.

의도한 설계는 아니었고 Godot의 노드 모델이 공짜로 준 것이지만, 옵션 UI(A6)를 만들
때 "왜 이게 같이 안 흐려지지?"로 헤맬 일이 없도록 여기 기록해 둔다.

### `SetClickThrough(bool)` / `GetSafeArea()`

기존 `_passthroughOn` 토글, `ScreenGetUsableRect(현재 화면)`을 인터페이스 시그니처로
그대로 노출한 것뿐이다. 개발 PC는 모니터 3대 중 하나가 X 좌표 음수인데,
`ScreenGetUsableRect`가 절대 데스크톱 좌표를 그대로 돌려주므로 별도 처리 없이 맞는다.

---

## 2. 세이브 파일 I/O — `platform/SaveIO.cs`

A1 문서가 "아직 안 한 것"으로 남겨둔 파일 I/O를 채웠다. `SaveData`(A1) 전체를
`user://save.json`에 왕복시키되, **지금 실제로 쓰는 필드는 `Settings`(스케일/투명도/
위치)뿐**이다. 나무·인벤토리 등은 을의 B5가 채우기 전까지 기본값으로 왕복한다.
부분 필드만 담는 임시 포맷을 만들지 않은 이유는, 그러면 B5 때 다시 합쳐야 하기
때문이다.

- **원자적 쓰기** (§7-5): 임시 파일에 쓰고 `File.Move(..., overwrite: true)`로 교체.
  상주 앱은 강제 종료가 잦아서 쓰다 만 파일이 직전 정상 세이브를 덮으면 안 된다.
- **경로는 임시로 `user://`** (Godot 기본, `%APPDATA%\Godot\app_userdata\ProjectSeWoo\`).
  §7-5가 명시한 `%APPDATA%/<게임명>/save.json`은 게임명이 확정(§12)된 뒤 바꾼다 —
  지금 하드코딩하면 이름이 바뀔 때 세이브 경로가 또 바뀐다.
- **`Load()`가 실패해도 새 기본값으로 시작한다.** 상주 앱이 세이브 파일 하나 때문에
  못 뜨면 안 된다.

### 저장 시점

전체 60초 자동 저장 타이머는 아직 안 만들었다 — 지금은 저장할 게임 상태 자체가
없어서(을의 B5 이전) 실익이 적다. 대신:

- 드래그로 창을 실제로 옮겼을 때 (`EndDrag`)
- 종료 시 (`_ExitTree`) — 드래그 없이 F5/`[`/`]`/`-`/`=`만 눌러본 세션도 다음
  실행에서 복원되게 한다

### ★ 실측 중 잡은 버그 — 무인 실행이 진짜 세이브를 덮어썼다

처음 구현에서 `_ExitTree()`가 항상 `PersistWindowState()`를 불렀다. `--selftest`로
헤드리스 실행을 검증하다가, **헤드리스 환경의 의미 없는 창 위치(`-88,-88`)가 실제
유저 세이브 파일에 그대로 써지는 것**을 실측으로 잡았다:

```
"pos": [-88, -88]   <- 헤드리스 selftest 가 남긴 값. 다음 정상 실행이 이 값을 복원한다
```

`--selftest`와 `--report=`(무인 측정, `tools/measure-renderers.ps1`)는 실제 유저
세션이 아니므로 `_skipSavePersist` 플래그로 쓰기만 막았다(읽기는 그대로 둔다 —
측정도 실제 저장된 배율/투명도에서 시작하는 게 자연스럽다). **"검증 코드가 검증
대상의 데이터를 오염시킨다"는 이 프로젝트에서 처음이 아니다** — DAY1-2-SPIKE.md
§4의 "리포트에 하드코딩한 합격 기준 문자열이 판정으로 오독된다"와 같은 계열의
실수다. 계측/무인 실행 경로는 항상 "이게 진짜 상태를 건드리는가"를 따로 확인한다.

---

## 3. DPI / 멀티모니터 — 구조는 맞지만 실측은 아직

`RestoreWindowState()`가 저장된 위치를 그대로 쓰기 전에
`IsWithinAnyScreen()`으로 **지금 연결된 모니터 구성 어디에도 없으면** 기본 배치로
폴백한다. 모니터가 빠지거나 해상도가 바뀐 경우를 다룬다 — `IShell.GetSafeArea()`
문서가 약속한 "모니터가 사라진 경우의 폴백은 플랫폼 레이어가 책임진다"가 이것이다.

**다만 이건 좌표 계산의 정합성이지, DPI 배율이 다른 모니터에서 클릭 영역이 실제로
어긋나는지는 여전히 미확인이다.** `DAY1-2-SPIKE.md` §3-3이 요구하는 `F7`/`F8` 육안
확인은 이 세션에서 하지 않았다 — 여러 DPI 모니터를 오가며 직접 봐야 하는 항목이라
자동화된 검증으로 대체할 수 없다. 열려 있는 채로 남긴다.

---

## 4. 파일 변경 요약

```
git mv  src/OverlayShell.cs → platform/OverlayShell.cs   (namespace ProjectSeWoo.Platform)
git mv  src/CursorLayer.cs  → platform/CursorLayer.cs    (namespace ProjectSeWoo.Platform)
git mv  src/PerfProbe.cs    → platform/PerfProbe.cs      (namespace ProjectSeWoo.Platform)
git mv  src/DebugHud.cs     → platform/DebugHud.cs       (namespace ProjectSeWoo.Platform)
신규    platform/SaveIO.cs                                (세이브 파일 I/O)
수정    scenes/Shell.tscn                                 (스크립트 경로)
수정    docs/A1-CONTRACTS.md, A2-CURSOR-SPIKE.md, DAY1-2-SPIKE.md, README.md  (경로 갱신)
수정    platform/Mocks/MockShell.cs, MockCursorLayer.cs   (주석 갱신)
```

`src/`는 이제 없다. `game/`은 아직 `.gitkeep`뿐이다(을 미착수).

### 새 debug 키 (옵션 화면 A6 이전까지 `IShell`을 시험하는 용도)

| 키 | 동작 |
|---|---|
| `[` / `]` | 셸 배율 -0.1 / +0.1 |
| `-` / `=` | 셸 투명도 -0.1 / +0.1 |

을이 옵션 화면을 만들면 이 자리를 그 UI가 대신 호출한다.

---

## 5. 다음

| 일감 | 관계 |
|---|---|
| A5 커서 꾸미기 정식화 | `platform/CursorLayer.cs`는 위치만 옮겨졌다. 내용 정리(디버그 필드 제거, 3슬롯 장착)는 A5 몫 |
| A6 옵션 창 | `[`/`]`/`-`/`=` debug 키를 실제 UI로 교체. `SetClickThrough`(위치 잠금)도 아직 UI가 없다 |
| A7 저부하 최적화 | `Scale`/`Modulate` 적용이 렌더 비용에 미치는 영향은 별도로 안 쟀다 — A7에서 커서 창 포함 재측정할 때 같이 본다 |
| DPI 실측 (DAY1-2-SPIKE.md §3-3) | `F7`/`F8`로 다른 배율 모니터에서 클릭 영역 육안 확인 — 미실시 |
| ~~§7-5 세이브 경로 정식화~~ | ✅ 완료 (2026-09-16). `project.godot`의 `custom_user_dir_name="PunchMonkey"` → `%APPDATA%/PunchMonkey/`. §12의 게임명 최종 확정(스팀 검색 중복 확인)은 별개로 남음 |
