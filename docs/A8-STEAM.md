# A8 — Steam SDK 연동 · 앱 초기화 · 도전과제 스텁

관련: [확정 기획서](기획확정-일감분배-260907.md) §9 (W2 A8) · §3-3 (도감 100%) · §6 (누적 타수) · §11 (스팀 행정) /
[A1 계약](A1-CONTRACTS.md) / [A7 저부하](A7-PERF.md)

---

## 0. 상태 (2026-09-15)

| 산출물 | 위치 | 상태 |
|---|---|---|
| Steam 초기화 + 콜백 펌프 | `platform/SteamService.cs` | ☑ 실행 확인 |
| `IAchievements` 계약 (6번째) | `shared/Contracts/IAchievements.cs` | ☑ 커밋 |
| 도전과제 API Name 스텁 | `AchievementIds` (같은 파일) | ☑ 이름만. 파트너 사이트 등록은 A15 |
| 목 구현 | `shared/Mocks/MockAchievements.cs` | ☑ 커밋 (2026-09-17 이동) |
| 네이티브 `steam_api64.dll` | 저장소 루트 | ☑ 커밋 (SDK 1.60) |
| 자체 검사 | `--steam-selftest` | ☑ PASS (§4) |
| 진짜 앱 ID | `SteamService.DefaultAppId` | ☑ **`5281130`** (2026-09-16 수수료 결제 후 발급, §2-2) |
| 도전과제 실제 등록 | — | ◐ A15 — 목록·코드 확정(10개), 파트너 사이트 입력은 사람이 ([A15-ACHIEVEMENTS.md](A15-ACHIEVEMENTS.md)) |
| 내보낸 빌드에 dll 복사 | — | ⬜ A12 (§6) |

---

## 1. 결정 — GodotSteam 대신 Steamworks.NET

기획서 §9 는 A8 을 "Steam SDK(**GodotSteam**) 연동" 으로 적었다. 실제로는
**Steamworks.NET** (순수 C# 바인딩, NuGet `Steamworks.NET 2024.8.0` = SDK 1.60)으로 갔다.

| | GodotSteam | Steamworks.NET |
|---|---|---|
| 형태 | GDExtension (C++) | 순수 C# 어셈블리 |
| C# 에서 호출 | `Engine.GetSingleton("Steam").Call("createLobby", ...)` — 문자열 + Variant 수동 마샬링 | `SteamMatchmaking.CreateLobby(...)` — 평범한 C# 메서드 |
| 시그널/콜백 | 문자열 시그널명 | `Callback<T>` / `CallResult<T>` 제네릭 |
| 오타가 터지는 시점 | **런타임** | 컴파일 |
| Godot 4.7.2 빌드 | 엔진 버전에 맞는 바이너리를 따로 구해야 함 | 엔진과 무관 |

**바꾼 이유는 이 프로젝트가 100% C# 이기 때문이다.** 우리 코드에는 GDScript 가 한 줄도
없고, A9~A11(로비 · P2P 브로드캐스트 · 재접속)이 전부 이 위에 얹힌다. 문자열 호출로
가면 `INetSession` 실물 전체가 컴파일 타임 검증을 못 받는 코드가 되고, 그 비용이
W3 내내 누적된다. GodotSteam 의 장점(GDScript 에서 바로 쓸 수 있음)은 우리에게 해당이 없다.

> **§8-2 규칙 적용.** 기획서 문구에서 벗어났으므로 [A1-CONTRACTS.md](A1-CONTRACTS.md) §0
> 표에 날짜와 사유를 남겼다. 을 쪽 코드는 영향이 없다 — 을은 `IAchievements` 만 보고,
> 그 뒤가 무엇인지 모른다. 그게 §8-2 가 인터페이스를 먼저 고정한 이유다.

---

## 2. 구성

### 2-1. 초기화 실패는 오류가 아니라 상태다

**이 앱은 스팀을 안 켠 채 켜져 있는 시간이 더 길다.** 상주 오버레이이고(§7-1),
A6 자동 시작을 켜면 로그인 직후 — 스팀이 뜨기 한참 전에 — 우리가 먼저 뜬다.

그래서 `SteamService.Start()` 는 실패해도 예외를 던지지 않는다. 사유를 `Status` 에
남기고 `IsAvailable = false` 인 채로 계속 돈다. 앱의 나머지(오버레이 · 타건 카운트 ·
나무 · 커서 장식)는 전부 스팀과 무관하게 동작한다.

`IAchievements` 쪽도 같은 규칙이다 — `IsAvailable` 이 false 면 `Unlock` 은 조용히
버려지고 `IsUnlocked` 는 항상 false 다. **을이 분기 없이 그냥 부르면 된다.**
`IInputSource.IsAvailable` 과 같은 계약이다.

### 2-2. 앱 ID — `5281130` (2026-09-16 확정)

A8 작성 시점에는 수수료 미결제라 480(Spacewar, 밸브가 SDK 예제용으로 공개한 앱 ID)을
임시로 썼다. **2026-09-16 에 Direct 수수료를 결제하고 `5281130` 을 받았다.**
`SteamService.DefaultAppId` 가 그 값이고, `--steam-appid=<N>` 오버라이드는 그대로
남겨 뒀다 — 480 으로 되돌려 보면 "우리 앱 설정 문제인가, SDK/로컬 환경 문제인가"를
가를 수 있다. 상수 교체 전에 이 인자로 먼저 검증했다(§4-3).

> **로비(A9)를 480 으로 시험하지 말 것.** 전 세계 SDK 예제 사용자와 같은 로비 공간을
> 쓰게 된다. 진짜 앱 ID 가 생긴 지금은 그럴 이유도 없다.

**진짜 앱 ID 로 바꾸면 도전과제 통계가 오히려 안 온다 — 이건 퇴행이 아니다.**
`UserStatsReceived` 가 `k_EResultFail` 로 돌아온다(2026-09-16 실측). 파트너 사이트에
통계·도전과제 스키마가 아직 없기 때문이고, A15 에서 등록하면 풀린다. 480 일 때
`PASS` 였던 건 **Spacewar 의 도전과제 5개**가 잡혔던 것이지 우리 것이 아니었다.

결과적으로 `IsAvailable` 이 false 이므로 `Unlock` 은 조용히 버려지고 `IsUnlocked` 는
false 다 — §2-1 계약 그대로다. **을 쪽 코드는 영향이 없다.** 해금 조건 로직은
`MockAchievements` 로 끝까지 시험할 수 있다.

### 2-3. 스팀이 나중에 켜져도 붙는다

부팅 자동 시작이면 첫 시도는 거의 항상 실패한다. 한 번 실패하고 끝내면 **그 세션
내내 멀티와 도전과제가 죽는다** — 유저가 나중에 스팀을 켜도 소용이 없다.

그래서 `Tick` 이 30초마다 재시도한다. 매번 `SteamAPI.InitEx` 를 부르는 게 아니라
`SteamAPI.IsSteamRunning()` 으로 먼저 거른다(네이티브 dll 만 있으면 되는 싼 검사다).
`steam_api64.dll` 자체를 못 찾는 것 같은 회복 불가능한 실패는 `_gaveUp` 으로 재시도를 끊는다.

### 2-4. 도전과제 스텁

`AchievementIds` 에 이름만 먼저 박았다. 기획서가 확정한 두 축뿐이다:

| API Name | 조건 | 근거 |
|---|---|---|
| `ACH_COLLECTION_100` | 도감 수집률 100% | §3-3 (기획서가 유일하게 명시한 도전과제) |
| `ACH_KEYSTROKES_10K` | 누적 1만 타 | §6 마일스톤 — **수치 잠정** |
| `ACH_KEYSTROKES_100K` | 누적 10만 타 | 〃 |
| `ACH_KEYSTROKES_1M` | 누적 100만 타 | 〃 |

> **마일스톤 수치는 아직 확정이 아니다.** §6 은 "로그 스케일 레벨 환산" 만 확정했고
> 구체적인 구간은 정하지 않았다. 10배씩 세 칸은 B3(레벨 곡선)와 W2 밸런스 튜닝을 보고
> 다시 정한다. **이름에 숫자가 들어 있고 스팀에 등록한 뒤에는 API Name 을 못 바꾸므로
> A15 전에 확정해야 한다.**

---

## 3. 네이티브 `steam_api64.dll` — 여기가 가장 손이 많이 갔다

### 3-1. NuGet 패키지에 네이티브가 없다

`Steamworks.NET` NuGet 에는 **관리 어셈블리만** 들어 있다. 밸브 SDK 재배포본
(`steam_api64.dll`)은 따로 받아야 한다. 패키지의 `runtimes/win-x64/lib/...` 에 있는 것도
네이티브가 아니라 RID 별 **관리** 어셈블리다.

GitHub 릴리스의 Standalone zip 에 들어 있고, **NuGet 최신(2024.8.0)과 태그가 정확히
일치하는 릴리스가 있다.** 관리 바인딩과 네이티브 dll 의 SDK 버전이 어긋나면
`Packsize.Test()` 단계에서 갈리므로 반드시 같은 태그에서 가져온다.

```
https://github.com/rlabrecque/Steamworks.NET/releases/download/2024.8.0/Steamworks.NET-Standalone_2024.8.0.zip
  → Windows-x64/steam_api64.dll  (300,392 bytes)
  → sha256 1add7f151fa644870a735ae86e68d1f019f296130d8e7c0a7ed3ecc7482dccbc
```

저장소 루트에 커밋했다. 루트에 둔 이유는 **내보낸 빌드의 배치(exe 옆)와 같은 모양**이라
개발 실행과 배포본의 경로 규칙이 갈리지 않기 때문이다.

### 3-2. dll 을 어디서 찾을지 직접 정한다

.deps.json 에 경로가 없으므로 기본 해석은 OS 검색 순서
(실행 파일 폴더 → 시스템 폴더 → **현재 작업 디렉터리** → PATH)로 떨어진다.
여기서 현재 작업 디렉터리가 문제다 — 에디터/VS F5 로 띄우면 프로젝트 폴더지만,
**A6 자동 시작으로 뜨면 레지스트리 `Run` 이 정한 엉뚱한 폴더**가 된다.

그래서 `NativeLibrary.SetDllImportResolver` 로 후보 경로를 명시한다:

1. `OS.GetExecutablePath()` 의 폴더 — 내보낸 빌드에서 게임 exe 옆
2. `ProjectSettings.GlobalizePath("res://")` — 개발 실행에서 프로젝트 루트

둘 다 실패하면 기본 해석에 맡기고, 거기서도 실패하면 `DllNotFoundException` 이
`TryInit` 의 catch 로 들어와 사람이 읽는 사유가 된다.

### 3-3. `RestartAppIfNecessary` 는 쓰지 않는다

스팀 게임의 표준 관행은 `SteamAPI.RestartAppIfNecessary(appId)` 로 "스팀을 통해"
다시 뜨게 하는 것이다. **우리는 안 쓴다.** 이 앱은 부팅 자동 시작으로 뜨는 상주
트레이 앱이라, 로그인 직후 스팀이 아직 없는 상태에서 이걸 부르면 스팀을 강제로
띄우고 자기를 재실행한다. 유저가 요청한 적 없는 동작이고, 최악의 경우 부팅 때마다
반복된다.

**대신 §2-3 의 재시도로 해결한다.** 스팀 없이도 앱이 온전하다는 것이 이 게임의
설계 전제이므로(멀티는 관전형 옵션이다, §4), 재실행까지 해서 붙을 이유가 없다.
A12(빌드 파이프라인) / 빌드 제출 때 이 결정을 한 번 더 확인한다.

---

## 4. 실측

### 4-1. 자체 검사 — `--steam-selftest`

```
Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . -- --steam-selftest
```

스키마 `--selftest` 와 달리 **몇 프레임 펌프를 돌려야 한다** — 초기화는 동기지만
통계는 콜백으로 비동기로 오기 때문이다. 종료 코드는 0(초기화 성공) / 1(실패).

```
[steam] 연결됨 appid=480 user=ggoggal627 id=76561198411431220 (Spacewar 진단 ID)
[steam] 통계 수신 완료. 도전과제 5개
initialized : PASS
stats/ach   : PASS
elapsed     : 0.3s
```

### 4-3. 진짜 앱 ID 로 다시 (2026-09-16, 익스포트 바이너리)

`build\PunchMonkey.exe -- --steam-selftest` (오버라이드 없이 기본값으로):

```
[steam] 연결됨 appid=5281130 user=ggoggal627 id=76561198411431220
[steam] 통계 수신 실패 (k_EResultFail) - 도전과제 비활성
initialized : PASS
stats/ach   : FAIL      <- A15 전까지 정상. §2-2
elapsed     : 10.0s
```

**확인된 것 세 가지.** ① 실제 계정으로 우리 앱 ID 초기화 성공 — A9~A11 의 진입점인
`IsInitialized` · `SelfId` 가 산다. ② 익스포트 바이너리 옆에 `steam_api64.dll` 을
복사하면 붙는다(§6 A12 항목 검증). ③ 통계는 A15 전까지 안 온다.

**종료 코드는 여전히 0 이다** — `IsInitialized` 로 판정하기 때문이다. 통계까지 보고
싶으면 판정을 `IsAvailable` 로 올려야 하는데, **A15 전까지는 그러면 항상 1 이 된다.**
A15 에서 도전과제를 등록한 뒤에 올리는 것이 맞다.

`elapsed` 가 0.3s → 10.0s 로 늘어난 것도 이것 때문이다. 실패 콜백을 기다리느라
셀프테스트가 타임아웃까지 간다.

도전과제 ID 는 **읽기만** 한다. `Unlock` 을 자체 검사에 넣으면 나중에 진짜 앱 ID 로
이 검사를 돌렸을 때 실제 도전과제가 해금돼 버린다.

### 4-2. 스팀이 먹는 비용 — 개인 커밋 +24MB

A7 메모리 게이트가 조건부 Go 인 상태라(A7 §4), 스팀을 얹은 비용을 같은 조건에서 쟀다.
무인 측정에서 스팀을 강제로 켜는 `--steam` 스위치를 그 목적으로 넣었다.

```
Godot_..._win64.exe --path . -- --report=<경로> --seconds=25 --warmup=15 --cursor [--steam]
```

| 조건 | 개인 커밋 | 작업 집합 | 유휴 CPU |
|---|---|---|---|
| 커서 포함, 스팀 없음 | 355 MB | 354 MB | 0.20% |
| 커서 포함, **스팀 있음** | **379 MB** | 380 MB | 0.15% |
| 차이 | **+24 MB** | +26 MB | 오차 범위 |

**CPU 는 영향이 없다.** 콜백 펌프(`RunCallbacks`)를 매 프레임 돌리는데도 유휴 0.15% 로
§7-3 의 1% 기준 안이다.

**메모리는 여유가 줄었다.** A7 이 정한 새 기준은 `PrivWS < 300MB` 이고, 9/14 에 관측한
비율(PrivWS ≈ 개인커밋 × 0.71)을 적용하면 379MB → 약 269MB 로 여전히 기준 안이다.
다만 A7 이 추정한 276MB 에서 더 붙은 것이 아니라 **추정의 출발점 자체가 올라갔다** —
Release `PrivWS` 직접 측정(A7 §5의 남은 항목)은 이제 스팀을 켠 조건으로 해야 한다.

---

## 5. 밟은 함정

**1. `lib/netstandard2.1` 이 참조 어셈블리다**
`Steamworks.NET` NuGet 의 `lib/netstandard2.1/Steamworks.NET.dll` 은 `ref/` 와 바이트
수가 같은 **참조 어셈블리**다 (로드하면 "Reference assemblies cannot be loaded for
execution"). 실제 구현은 `runtimes/<rid>/lib/netstandard2.1/` 에만 있다. 빌드 출력
루트에 놓이는 것은 참조 쪽이라, Godot 의 어셈블리 로드 컨텍스트가 RID 자산을 제대로
고르지 못하면 런타임에 터질 구조였다. **실제로는 정상 해석됐다**(§4-1 실행 확인) —
`AssemblyDependencyResolver` 가 `.deps.json` 의 `runtimeTargets` 를 읽는다. 다만 이건
운이 좋은 게 아니라 확인해야 아는 것이었고, 엔진 버전이 올라가면 다시 봐야 한다.

**2. `Environment` 가 `Godot.Environment` 와 충돌한다**
`using Godot;` 이 있는 파일에서 `Environment.SetEnvironmentVariable` 은 모호 참조
오류다 (Godot 에 3D 환경 리소스 `Environment` 가 있다). `System.Environment` 로
명시해야 한다. 이 프로젝트의 `platform/` 파일 전부가 `using Godot;` 을 달고 있으므로
`System.*` 의 흔한 이름을 쓸 때마다 나올 수 있다.

**3. `steam_appid.txt` 는 현재 작업 디렉터리에 의존한다**
스팀으로 실행하지 않은 개발 실행에서 앱 ID 를 알려주는 표준 방법이지만, 파일을
**현재 작업 디렉터리**에서 찾는다. 자동 시작으로 뜨는 앱에서는 그게 어디일지 모른다.
환경변수(`SteamAppId` / `SteamGameId`)를 `InitEx` 직전에 설정하는 쪽으로 갔다 —
프로세스 안에서 끝나므로 작업 디렉터리와 무관하고, 저장소에 파일이 하나 덜 생긴다.

**4. `Callback<T>` 핸들을 필드로 붙들지 않으면 콜백이 조용히 죽는다**
`Callback<UserStatsReceived_t>.Create(...)` 의 반환값을 버리면 GC 가 걷어가고, 그때부터
`RunCallbacks()` 가 아무것도 부르지 않는다. 예외도 로그도 없다 — **그냥 통계가 영원히
안 온다.** A9~A11 에서 로비/P2P 콜백을 붙일 때 같은 함정이 그대로 반복된다.

**5. 무인 측정에서 스팀이 켜지면 안 된다**
`--report=` 측정이 스팀을 켜면 친구 목록에 "게임 중" 이 뜨고, 그 자체가 측정에 잡히는
부하다. `measure-renderers.ps1` 은 조건마다 재시작하므로 네 번 깜빡인다. 기본은 끄고
`--steam` 으로만 켠다 — A6 트레이 아이콘이 이미 같은 이유로 `_unattended` 에 묶여 있다.

---

## 6. 다음

| 일감 | 관계 |
|---|---|
| ~~**진짜 앱 ID 확보**~~ | ✅ 완료 (2026-09-16). `5281130`. `--steam-appid=` 로 먼저 검증한 뒤 `DefaultAppId` 교체, 익스포트 바이너리로 재확인 (§4-3) |
| A9~A11 (로비 / P2P / 재접속) | 이 위에 바로 얹는다. `SteamService.SelfId` · `IsInitialized` 가 진입점 |
| A12 빌드 파이프라인 | **내보낸 exe 옆에 `steam_api64.dll` 을 복사해야 한다.** Godot export 는 네이티브 dll 을 자동으로 안 옮긴다 (pck 안에 넣어도 소용없다). **2026-09-16 실제 익스포트로 검증함** — 복사한 뒤 `--steam-selftest` 가 익스포트 바이너리에서 종료 코드 0. 같은 익스포트에서 **`InputHelper.exe` 는 아예 안 들어간다**는 것도 같이 드러났다(익스포트 빌드는 전역 타건을 못 받는다). 둘 다 A12 가 처리할 것 |
| A15 도전과제 등록 | 파트너 사이트에 `AchievementIds` 의 4개 등록. **등록 전에 마일스톤 수치 확정** (§2-4) |
| ~~A7 Release `PrivWS` 재측정~~ | ✅ 완료 (2026-09-16, A7-PERF.md §3). **커밋 기준의 +24MB 는 Release 에서도 그대로 재현됐지만, 정식 기준인 `PrivWS` 로는 +4MB 다** — §4-2 가 "여유가 줄었다"고 쓴 것은 커밋 기준이었고, 기준을 바꾼 지금 스팀의 실질 비용은 거의 없다 |
| B 트랙 | `MockAchievements` 로 해금 조건 로직을 지금 끝까지 시험할 수 있다. 실물 교체 시 게임 코드 변경 없음 |
