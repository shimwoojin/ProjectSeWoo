# DEVLOG

---

## 2026-09-01 — Day 0~1 · 프로젝트 셋업 + 오버레이 셸 스파이크

### 한 일

1. **기획서 검토 결과를 Week 0 검증 계획으로 확정** → [WEEK0-GODOT-VALIDATION.md](WEEK0-GODOT-VALIDATION.md)
   세 기획(끼끼 아일랜드 / Punchy / Tiny Quest)이 껍데기를 공유하므로, 기획 확정보다 껍데기 검증이 먼저라는 결론.
2. **GDScript 전용 → C# (.NET) 전환**
3. **Day 1-2 오버레이 셸 스파이크 구현** → [DAY1-2-SPIKE.md](DAY1-2-SPIKE.md)
4. **프로젝트명 `PcIdle` → `ProjectSeWoo`**

### 결정 사항

| 결정 | 이유 |
|---|---|
| **C# 단일 언어** (Godot .NET 빌드) | 네이티브 입력 후킹을 P/Invoke로 바로 할 수 있다. GDScript면 GDExtension C++ 모듈을 따로 빌드해야 하는데 1달 프로젝트에서 비현실적 |
| **`net8.0` 타깃** | Godot 4.4+ 기본값. 이 PC에 .NET 8 런타임(8.0.17)이 있어 그대로 실행됨. SDK는 9.0.301이지만 net8.0 빌드/실행 모두 정상 |
| **`Godot.NET.Sdk/4.7.2`** | 설치된 엔진 버전과 일치시킴 |
| **씬을 코드로 구성** | 스파이크 한정. `.tscn`은 루트 노드 하나뿐이고 나머지는 `OverlayShell.BuildScene()`이 만든다. 에디터를 안 거쳐도 상태 전부가 한 파일에서 읽히고, diff가 깨끗하다. **게임 본편에서는 에디터에서 씬을 짜야 한다** |
| **`window/stretch/mode="disabled"`** | passthrough 폴리곤 좌표가 창 픽셀 기준이라 스트레치가 걸리면 클릭 영역이 어긋난다 |
| **HUD 문자열 전부 ASCII** | 아래 함정 3 참고 |

### 밟은 함정 (다음에 또 밟지 말 것)

**1. Godot 표준 빌드는 C#을 못 읽는다**
`Godot_v4.7.2-stable_win64.exe`(표준)와 `Godot_v4.7.2-stable_mono_win64`(.NET)는 별도 다운로드다.
표준은 단일 exe, mono는 **폴더째**라 exe만 빼내면 실행되지 않는다.

**2. `Godot.Environment` vs `System.Environment` 이름 충돌**
`using Godot;` 상태에서 `Environment.ProcessorCount`를 쓰면 CS0104 모호성 에러.
`System.Environment`로 정규화해야 한다. `Godot.Environment`는 3D 월드 환경 리소스라 이름이 겹친다.

**3. Godot 기본 테마 폰트에 한글 글리프가 없다**
Label에 한글을 넣으면 두부(□)로 렌더된다. 그래서 HUD는 전부 영어로 썼다.
→ **게임 본편에서 한글 UI를 쓰려면 폰트를 번들해야 한다.** 라이선스 확인 필요(Pretendard / 나눔 계열).
Week 0 확인 항목에 추가.

**4. `per_pixel_transparency/allowed`는 런타임에 못 켠다**
`project.godot`에서 켜야 한다. 코드에서 켜려 하면 조용히 무시된다.

**5. 드래그 중에는 passthrough를 꺼야 한다**
클릭 영역(스프라이트 사각형) 밖으로 커서가 나가는 순간 마우스 이벤트가 끊겨 드래그가 풀린다.
`BeginDrag()`에서 빈 배열을 넘겨 창 전체가 마우스를 받게 하고, `EndDrag()`에서 복구한다.
추가로 창 밖으로 나가면 모션 이벤트 자체가 안 오므로, 위치 추적은 `_Process`에서 `DisplayServer.MouseGetPosition()` 폴링으로 한다.

### 구현 메모 — 플리커를 "실험"으로 만든 이유

[godot#80098](https://github.com/godotengine/godot/issues/80098): 투명창 + `window_set_mouse_passthrough` 조합에서 흰색 깜빡임 리포트.
이게 Day 1-2 최대 불확실성인데, 문서만 봐서는 "엔진이 못 하는 것"인지 "우리가 매 프레임 호출해서 그런 것"인지 알 수 없다.

그래서 `ApplyPassthrough(bool force)` 하나로 두 모드를 갖고, `F3`으로 즉시 전환하게 했다.

- `on-change` — 폴리곤이 실제로 바뀐 경우에만 `DisplayServer`를 건드린다 (회피 가설)
- `every-frame` — 매 프레임 강제 호출 (버그 재현)

`_regionWrites` 카운터로 두 모드의 실제 호출 횟수 차이가 HUD에 보인다.
**every-frame에서만 깜빡이면 회피책이 통하는 것이고, 곧 Go 판정이다.**

### 계측을 앱 안에 넣은 이유

합격 기준이 "유휴 CPU < 1%, 메모리 < 150MB"인데 작업관리자 곁눈질로는 기록이 안 남는다.
`PerfProbe`가 작업관리자와 같은 공식으로 계산한다:

```
CPU% = (프로세스 CPU 시간 델타) / (실제 경과 시간 × 논리 코어 수) × 100
```

`F9`를 누르면 노션 측정 기록표에 그대로 붙일 형태로 클립보드에 복사된다.

### 현재 상태

| | 상태 |
|---|---|
| `dotnet build` | ✅ 오류 0 · 경고 0 |
| 에디터 실행 | ✅ 동작 확인 (육안) |
| **정량 측정** | ⬜ **미실시** — CPU/메모리 10분 방치 측정 아직 안 함 |
| 플리커 재현 실험 (F3) | ⬜ 미실시 |
| 멀티모니터 / DPI 검증 (F7/F8) | ⬜ 미실시 |
| 전체화면 게임 위 동작 | ⬜ 미실시 |
| 노트북 배터리 1시간 | ⬜ 미실시 |

**즉, "떴다"까지만 확인됐고 Day 1-2 합격 기준은 아직 아무것도 판정되지 않았다.**
측정 절차와 기록표는 [DAY1-2-SPIKE.md](DAY1-2-SPIKE.md) §3~§5에 있다.

### 다음

1. DAY1-2-SPIKE.md §3 측정 절차 수행 → §5 판정표 채우기
2. Day 2-3 글로벌 입력 스파이크 (프로젝트 최대 리스크)
   - `GetLastInputInfo` 폴백부터 30분 안에 만들어 안전망 확보
   - `RawInput` / `WH_KEYBOARD_LL` 비교 + VirusTotal · Defender · Vanguard 실측
3. 스팀 앱 수수료 결제 + 세금 서류 (개발과 병렬, 시계가 돌고 있음)
