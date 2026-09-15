# A6 — 트레이 아이콘 / 자동 시작 / 옵션 창

관련: [확정 기획서](기획확정-일감분배-260907.md) A6 · §7-4 · §7-1 / [WEEK0 검증 계획](WEEK0-GODOT-VALIDATION.md) §4 / [A3 셸 모듈화](A3-SHELL-MODULE.md)

> §7-4가 요구하는 옵션 화면 "전 항목"을 채우는 작업이다. 시작하면서 세이브
> 스키마 v1에 그 항목 중 5개가 애초에 빠져 있는 걸 발견해서, 스키마부터 고쳤다.

---

## 0. 상태 (2026-09-16) — 완료

| 산출물 | 위치 | 상태 |
|---|---|---|
| 세이브 스키마 v2 (§7-4 옵션 5종 추가) | `shared/Save/SaveData.cs`, `SaveSchema.cs` | ☑ (아래 §1) |
| 옵션 창 (§7-4 전 항목) | `platform/OptionsWindow.cs` | ☑ (아래 §2) |
| 트레이 아이콘 + 메뉴 | `platform/TrayIcon.cs` | ☑ (아래 §3) |
| 창 닫기 → 트레이 | `OverlayShell.OnCloseRequested` | ☑ |
| 자동 시작 (레지스트리) | `platform/Autostart.cs` | ☑ (아래 §4) |
| 전체화면 앱 위 자동 숨김 | `platform/FullscreenWatcher.cs` | ☑ 구현, **실제 검증은 미완료** (§5) |

---

## 1. 세이브 스키마 v2 — v1에 §7-4 옵션 5개가 빠져 있었다

§7-4: "크기 / 투명도 / 위치 잠금 / 사운드 On-Off / 알림 On-Off / 커서 장식 On-Off /
전체화면 앱 위 숨김 / 자동 시작 / 타수 카운트 On-Off". A1이 만든 v1 스키마와
대조하면 **위치 잠금·알림·커서 장식·전체화면 숨김·타건 카운트 5개가 없었다**
(크기/투명도/사운드/자동시작만 있었다). A6이 "옵션 창 (§7-4 전 항목)"으로
명시돼 있으니 이 구멍은 A6이 메운다.

```csharp
[JsonPropertyName("positionLocked")]    public bool PositionLocked { get; set; } = true;
[JsonPropertyName("notifications")]     public bool Notifications { get; set; } = true;
[JsonPropertyName("cursorEnabled")]     public bool CursorEnabled { get; set; } = true;
[JsonPropertyName("hideOnFullscreen")]  public bool HideOnFullscreen { get; set; } = true;
[JsonPropertyName("keystrokeCounting")] public bool KeystrokeCounting { get; set; } = true;
```

전부 additive라 v1 세이브를 읽어도 C# 기본값이 채워져 크래시하지 않는다.
그래도 `SaveSchema.CurrentVersion`을 2로 올리고 `MigrateV1ToV2`를 실제로
채웠다 — A1이 마이그레이션 훅을 "지금은 호출 지점만 만들어 둔다"고 남겨둔 것을
처음으로 실제 사용한 것이다. v1 세이브를 읽으면 버전 태그가 2로 갱신되고,
다음 저장부터는 완전한 v2로 다시 써진다 (실측: v1 파일을 만들어서 로드 →
크래시 없음 → `settings`에 5개 필드가 기본값으로 채워진 것을 리포트로 확인).

## 2. 옵션 창 — `OptionsWindow`는 저장을 모른다

`CanvasLayer`다 (`DebugHud`와 같은 이유 - 셸 배율을 바꿔도 옵션 UI 자체는
안 흔들려야 한다). **값이 바뀌면 이벤트만 쏘고, 세이브/IShell/CursorLayer/
Autostart에 실제로 적용하는 것은 전부 `OverlayShell.WireOptionsEvents()`가
한다.** 옵션 UI가 다른 서브시스템을 몰라도 되게 하려는 것 - `IShell`이
게임 레이어를 위해 존재하는 것과 같은 분리 원칙이다.

슬라이더 2개(크기/투명도) + 체크박스 7개(위치 잠금/사운드/알림/커서 장식/
전체화면 숨김/타건 카운트/자동 시작) + 닫기 버튼. 자동 시작 체크박스만 예외로,
세이브 값이 아니라 **레지스트리의 실제 값**(`Autostart.IsEnabled()`)으로
초기화한다 - 유저가 Windows "시작 앱" 설정에서 수동으로 꺼버렸을 수 있어서다.

### 클릭 통과와의 상호작용 — 옵션 창이 열린 동안은 전체 캡처

셸 창은 평소 마스코트 영역만 클릭을 받는다(위치 잠금 on). 옵션 패널은 그
영역 밖에도 그려지므로, **열려 있는 동안은 `SetMousePassthrough`를 빈 배열로
바꿔 창 전체가 클릭을 받게 한다** - 안 그러면 슬라이더를 마스코트 밖에서
못 누른다. 닫히면(`OptionsWindow.Closed` 이벤트) 위치 잠금 값대로 되돌린다.

### debug 키

트레이가 없어도(미지원 플랫폼) 열 수 있게 `O` 키로 토글한다. `[`/`]`(배율),
`-`/`=`(투명도), `F2`(위치 잠금), `F11`(커서 장식)도 전부 이제 진짜 옵션이다 -
"debug 키였다가 A6에서 진짜가 됐다"가 아니라, **F2/F11도 옵션 창의 체크박스와
정확히 같은 메서드(`SetClickThrough`/`ApplyVisibility`)를 부르도록 고쳤다.**
값을 두 군데서 따로 관리하면 언젠가 어긋난다.

## 3. 트레이 아이콘 — Godot 4.3+ 내장 API만 쓴다

`DisplayServer.CreateStatusIndicator` + `NativeMenu`. 플러그인/GDExtension
불필요. 좌클릭 = 보이기/숨기기 토글, 우클릭 = OS가 자동으로 `NativeMenu` 메뉴를
띄운다(보이기/숨기기, 설정..., 종료). `DisplayServer.HasFeature(Feature.StatusIndicator)`로
지원 여부를 먼저 확인한다 - macOS/Windows만 되고 Linux는 안 된다.

**무인 실행(`--selftest`, `--report=`)에서는 트레이를 아예 안 만든다.**
`tools/measure-renderers.ps1`이 렌더러 A/B 네 조건을 돌리는 동안 시스템
트레이에 아이콘이 네 번 나타났다 사라지면 안 된다.

## 4. 자동 시작 — `HKCU\...\Run`

관리자 권한 불필요, 이 유저 계정에만 적용. `Microsoft.Win32.Registry`는
`net8.0`(비-Windows TFM)에서도 그냥 쓸 수 있었다 - CA1416 경고만 뜬다
(A4의 `MemoryMappedFile.OpenExisting`과 같은 패턴, 이 프로젝트는 이미
그 경고를 받아들이기로 했다).

**개발 중 주의 - `OS.GetExecutablePath()`가 게임 exe가 아니라 Godot 엔진 exe를
가리킨다.** 실측: 개발 환경에서 자동 시작을 켜면 레지스트리에
`Godot_v4.7.2-stable_mono_win64.exe` 경로가 등록된다. 익스포트 빌드(A12)에서만
의미가 있는 기능이고, 그때는 정확히 게임 exe를 가리킨다. **테스트 후 반드시
껐다 - 실제 이 개발 PC의 시작 프로그램에 Godot 엔진이 등록된 채로 남을
뻔했다.**

무인 실행에서는 레지스트리를 절대 안 건드린다(`_unattended` 가드) - 세이브
파일과 똑같이, 셀프테스트/자동측정이 유저의 실제 시스템 상태를 오염시키면 안 된다.
A3에서 헤드리스 selftest가 세이브 파일에 엉뚱한 창 위치를 덮어썼던 사고
(A3-SHELL-MODULE.md §2)와 같은 종류의 실수를 레지스트리로 반복하지 않으려고,
이번엔 `_unattended` 플래그 하나로 세이브/레지스트리/트레이 생성을 한 번에 묶었다.

## 5. 전체화면 앱 위 자동 숨김 — 구현했지만 실제 검증은 못 했다

`FullscreenWatcher.IsOtherAppFullscreen()`: 포그라운드 창의 사각형이 그
모니터 전체(작업표시줄 영역 포함)를 덮는지로 판정하는 흔한 휴리스틱이다.
일반 최대화 창은 작업 영역(작업표시줄 제외) 기준이라 여기 안 걸린다 -
그게 이 휴리스틱의 핵심이고, **이 부분은 실측으로 확인했다**: 지금 이
개발 PC에서 포그라운드인 Visual Studio(최대화, 1536x864 모니터에서
310,56~1224,785)로 직접 돌려보면 정확히 `false`가 나온다.

**못 한 것 - 실제 전체화면 위에서 `true`가 나오는지.** 자동화된 테스트로
보더리스 풀스크린 창을 띄우고 포그라운드로 강제해 보려 했는데, **Windows가
백그라운드/스크립트로 실행된 프로세스의 `SetForegroundWindow` 호출을
막았다**(포커스 강탈 방지 - 사용자가 지금 쓰고 있는 창을 다른 프로세스가
마음대로 뺏지 못하게 하는 OS 차원의 보호 기능). 그래서 이 세션에서는
"정상적으로 안 걸린다"만 확인했고, "제대로 걸리는가"는 확인하지 못했다.
실제 전체화면 게임/영상을 띄우고 셸이 사라지는지 사람이 눈으로 봐야 한다.

무인 실행에서 트레이/자동시작은 아예 안 만들지만, **이 폴링은 계속 돈다** -
A7이 "장식을 낀 채" 뿐 아니라 "이 감시까지 포함한" 실제 상시 부하를 재야
하기 때문이다. 0.5초 틱마다 한 번, P/Invoke 호출 몇 개뿐이라 비용은 낮을
것으로 예상하지만 A7에서 숫자로 확인한다.

### 아직 못 채운 것 — 예외 클래스 명단

`IgnoredClasses`(`Progman`/`WorkerW`/`Shell_TrayWnd`)는 알려진 오탐 후보를
미리 적어둔 것이고, 실사용 중에 다른 오탐이 나오면(예: 특정 런처가 자기
창을 모니터 전체 크기로 그리는데 게임은 아닌 경우) 여기 추가해야 한다.

---

## 6. 표시 여부 상태 기계 — `ApplyVisibility`

"보이는가"를 결정하는 지점을 하나로 모았다. 서로 독립적인 두 이유가 있다 -
유저가 트레이에서 숨겼는가(`_userWantsVisible`), 전체화면 앱이 떠서 자동으로
숨겼는가(`_autoHiddenForFullscreen`). 커서 장식은 그 위에 옵션
(`CursorEnabled`)까지 한 번 더 곱한다.

```
visible = userWantsVisible && !autoHiddenForFullscreen
_win.Visible = visible
_cursor.Enabled = visible && CursorEnabled
```

이렇게 묶지 않았으면 "트레이로 숨겼는데 전체화면 앱이 끝나자 다시 나타났다"
같은 상태 꼬임이 났을 것이다 - 두 개의 숨김 사유가 각자 `_win.Visible`을
직접 건드렸다면 나중 것이 먼저 것을 덮어썼을 것이기 때문이다.

---

## 7. 다음

| 일감 | 관계 |
|---|---|
| 전체화면 감지 실제 검증 | 실제 전체화면 게임/영상으로 육안 확인 (§5) |
| A7 저부하 재측정 | `FullscreenWatcher` 폴링 비용을 포함해서 재야 한다 |
| A12 빌드 파이프라인 | 익스포트 빌드에서 `Autostart`가 정확히 게임 exe를 등록하는지 재확인 |
| B6 상점/장착 UI | `2`/`3`/`4` debug 키를 대신한다 (A5) |
| 게임명 확정 (§12) | 세이브 경로를 `user://`에서 `%APPDATA%/<게임명>/`로 (A3-SHELL-MODULE.md §2) |
