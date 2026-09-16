# A12 — 빌드 파이프라인 + Steam Depot 업로드

관련: [확정 기획서](기획확정-일감분배-260907.md) A12 · §11 / [A4 글로벌 입력](A4-GLOBAL-INPUT.md) / [A8 스팀](A8-STEAM.md) §6 / [A7 저부하](A7-PERF.md)

> **Godot 익스포트만으로는 돌아가는 빌드가 안 나온다.** 우리 패키지 세 조각 중
> 둘을 Godot 이 모른다. 2026-09-16 에 손으로 익스포트했을 때 그 둘이 다 빠져
> 있었고, 둘 다 **에러 없이 조용히 기능만 죽는다.** 그래서 순서를 스크립트로
> 굳혔다 — `tools/build-release.ps1`.

---

## 0. 상태 (2026-09-16)

| 항목 | 결과 |
|---|---|
| 빌드 스크립트 | ☑ `tools/build-release.ps1` |
| Depot 콘텐츠 | ☑ `build/dist/` — 파일 191개, **183MB** |
| 검증 4종 (스팀·앱 ID·헬퍼 동봉·헬퍼 실행) | ☑ 전부 PASS |
| SteamPipe VDF 생성 | ☑ `-DepotId` 로 생성. **Depot ID 는 아직 미확인** |
| 실제 업로드 | ☐ Depot ID 확인 후 |
| A13 Defender / VirusTotal 재검증 | ☐ 이 빌드로 한다 |

---

## 1. 패키지가 세 조각인 이유

| 조각 | 누가 만드나 | 없으면 |
|---|---|---|
| `PunchMonkey.exe` + `.pck` + `data_ProjectSeWoo_windows_x86_64/` | Godot 익스포트 | 게임이 없다 |
| `steam_api64.dll` | **우리가 복사** | 스팀이 안 붙는다 — 도전과제·멀티 사망 (A8 §6) |
| `InputHelper.exe` | **우리가 빌드+복사** | 전역 타건을 못 받는다 — **핵심 루프 사망** (A4) |

`data_.../` 안에 이미 .NET 런타임이 통째로 들어 있다(`coreclr.dll` 등 76.5MB).
Godot 이 C# 프로젝트를 익스포트할 때 넣어 주는 것이고, 폴더 이름은 exe 이름이
아니라 **`assembly_name`** 을 따른다 — 그래서 게임명이 `PunchMonkey` 인데도
`data_ProjectSeWoo_...` 다. 정상이다.

### 1-1. 조용한 실패가 이 문서의 존재 이유다

둘 다 **빠져도 게임은 멀쩡히 뜬다.**

- `steam_api64.dll` 없음 → `[steam] 스팀이 실행 중이 아니다` 비슷한 상태 문자열만
  남고 앱은 계속 돈다. A8 이 "초기화 실패는 오류가 아니라 상태" 로 설계했기
  때문이고, 그 설계 자체는 옳다
- `InputHelper.exe` 없음 → `[input] 헬퍼 실행 파일 없음`. 창은 뜨고 원숭이도
  보이는데 **타건이 영원히 0** 이다

크래시가 나면 차라리 낫다. 스크린샷으로는 정상으로 보이는 빌드가 나가는 게
이 프로젝트에서 가장 비싼 실패라, 빌드 스크립트가 **매번 실행해서 확인한다**(§3).

---

## 2. InputHelper 는 NativeAOT 로 뽑는다

**이게 A12 에서 실제로 판단이 필요했던 지점이다.**

`InputHelper.csproj` 는 `SelfContained=false` 다. 기본 `dotnet publish` 결과는
150KB 짜리 apphost + `InputHelper.dll` + `runtimeconfig.json` 이고, 실행하려면
**유저 PC 에 .NET 8 런타임이 설치돼 있어야 한다.**

게임 본체는 상관없다 — Godot 이 런타임을 `data_.../` 에 동봉한다. 그런데
**헬퍼는 그 런타임을 못 쓴다.** `hostfxr` 이 `Program Files\dotnet\shared` 의
공용 런타임을 찾지, 옆 폴더의 사설 복사본을 찾지 않는다. 결과는 §1-1 의 두
번째 항목 그대로다 — **클린 PC 에서 헬퍼만 조용히 안 뜨고 타수가 0 이 된다.**

| 방식 | 크기 | 런타임 의존 | 판단 |
|---|---|---|---|
| 프레임워크 의존 (기본) | 150KB | **있음** | ✗ 클린 PC 에서 죽는다 |
| 자체 포함 단일 파일 | ~70MB | 없음 | △ 실행할 때마다 임시 폴더에 자기를 푼다 |
| **NativeAOT** | **1.8MB** | 없음 | ✓ **채택** |

AOT 가 크기·기동·백신(A13) 모두 유리하다. 상주 앱의 보조 프로세스라 기동이
잦고, 자기를 임시 폴더에 푸는 동작은 백신 휴리스틱과 상성이 나쁘다.

> **대가: 빌드 머신에 MSVC C++ 빌드 도구가 필요하다.** AOT 링크가 `link.exe` 를
> 쓴다. 스크립트가 없으면 명확한 메시지로 죽는다 — **프레임워크 의존으로
> 우회하지 말 것.** 우회하면 이 절의 실패가 그대로 돌아온다.

### 2-1. `vswhere` 가 PATH 에 없으면 설치돼 있어도 실패한다

AOT 링크 단계가 `vswhere.exe` 로 MSVC 를 찾는데, 그게 PATH 에 없으면 링커
경로를 조립하다 깨진다. 에러가 `link.exe` 를 가리켜서 **MSVC 가 없는 것처럼
보이지만 원인은 vswhere 다.** 스크립트가 `C:\Program Files (x86)\Microsoft
Visual Studio\Installer` 를 PATH 앞에 붙여서 처리한다.

### 2-2. AOT 가 조용히 안 걸리는 경우를 크기로 잡는다

AOT 설정이 무시되면 같은 이름의 150KB apphost 가 나온다. 파일이 있으니 복사도
되고 검증도 "동봉됨" 으로 통과한다. 스크립트가 **500KB 미만이면 거부**한다.

---

## 3. 검증 4종 — 파일이 있는 것과 도는 것은 다르다

`build-release.ps1` 이 매 빌드마다 돌린다.

| # | 보는 것 | 방법 | 실패하면 |
|---|---|---|---|
| 1 | 스팀이 붙는가 | `-- --steam-selftest` 종료 코드 | 경고 (스팀 미기동일 수 있음) |
| 2 | 앱 ID 가 맞는가 | 출력의 `appid=` 와 `DefaultAppId` 대조 | 경고 |
| 3 | 헬퍼가 동봉됐는가 | `헬퍼 실행 파일 없음` 문자열 부재 | **중단** |
| 4 | **헬퍼가 실제로 도는가** | 12초 `--report=` 후 `헬퍼 정상` 확인 | **중단** |

4번이 3번과 별개인 이유는 §2-2 와 같다 — **NativeAOT 는 마샬링이 걸린 코드를
조용히 망가뜨릴 수 있고, 헬퍼가 죽어도 게임은 멀쩡히 뜬다.** 심장박동까지 봐야
"도는 것" 이 확인된다. 이 검증이 12초를 쓰는 값은 충분히 한다.

> 도전과제 통계(`stats/ach`)는 **판정에 넣지 않는다.** A15 전까지 `k_EResultFail`
> 이 정상이기 때문이다 (A8 §4-3). 여기에 넣으면 빌드가 W4 까지 계속 빨간불이다.

### 3-1. `Start-Process -PassThru` 의 종료 코드는 믿을 수 없다

Windows PowerShell 5.1 에서 `Start-Process -PassThru` 가 돌려준 객체는
**`-Wait` 를 준 경우에만 `.ExitCode` 가 채워진다.** `WaitForExit()` 을 직접
불러도 빈 값이다 (세 조건으로 확인: `-Wait` 만 값이 오고 나머지는 빈 값).
그런데 `-Wait` 에는 타임아웃이 없어서 빌드가 영원히 멈출 수 있다.

**빈 값은 비교에서 조용히 거짓이 된다** — 검증 1번이 PASS 인데도 화면에는
`스팀 초기화 FAIL (종료 코드 )` 로 찍혔다. 판정이 말없이 무의미해지는 자리라
`Invoke-Native`(.NET `Process` 직접 사용)로 갈았다. 타임아웃과 종료 코드를
둘 다 얻는다.

---

## 4. 쓰는 법

```powershell
# 빌드만
powershell -ExecutionPolicy Bypass -File tools\build-release.ps1

# 빌드 + SteamPipe VDF 생성
powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -DepotId <번호>
```

산출물은 `build/dist/` 하나에 모인다. **그 폴더가 그대로 Depot 콘텐츠 루트다** —
빌드 부산물이 섞이면 그대로 유저에게 배포되므로 매번 비우고 다시 채운다.

```
build/dist/
   76.5 MB  data_ProjectSeWoo_windows_x86_64/   (.NET 런타임 + GodotSharp)
  104.3 MB  PunchMonkey.exe
    1.8 MB  InputHelper.exe
    0.3 MB  steam_api64.dll
    0.0 MB  PunchMonkey.pck
  ------------------------------------------------
    183 MB  (파일 191개)
```

### 4-1. `build/` 는 자기 자신의 pck 에 들어가면 안 된다

`export_filter="all_resources"` 에 `exclude_filter` 가 비어 있으면 **직전 익스포트
산출물이 다음 pck 안으로 들어간다.** 첫 익스포트 때는 `build/` 가 비어 있어서
안 보이고, 두 번째부터 pck 가 불어난다. `exclude_filter="build/*"` 로 막았다.

**`.godot/*` 는 같이 제외하면 안 된다** — 임포트된 텍스처(`.godot/imported/`)와
`uid_cache.bin` 이 거기 있어서 아이콘이 깨진다.

---

## 5. Depot 업로드

**Depot ID 를 아직 모른다.** 파트너 사이트 > SteamPipe > Depots 에서 확인한다.
보통 앱 ID+1 (`5281131`) 이지만 **보장된 규칙이 아니므로 추측하지 않는다** —
틀린 depot 에 올리면 되돌리기가 번거롭다.

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -DepotId <번호>
steamcmd +login <빌드계정> +run_app_build "<repo>\build\steampipe\app_build_5281130.vdf" +quit
```

생성되는 VDF 두 개:

- `depot_build_<DepotId>.vdf` — 콘텐츠 루트를 통째로(`LocalPath "*"`, 재귀) 넣고
  `*.pdb` 만 뺀다. 파일이 늘 때마다 VDF 를 고치지 않아도 되게 한 것이다
- `app_build_5281130.vdf` — `desc` 에 커밋 해시가 들어간다. 나중에 "이 빌드가
  어느 커밋이냐" 를 파트너 사이트에서 바로 볼 수 있다

### 5-1. `setlive` 는 비워 둔다

채워 넣으면 업로드가 **곧바로 그 브랜치에 공개된다.** 검수 전 빌드가 유저에게
나가는 사고를 스크립트가 만들면 안 된다. 브랜치 공개는 파트너 사이트에서 사람이
직접 한다.

### 5-2. 빌드 계정은 따로 판다

메인 계정으로 SteamPipe 를 돌리지 않는다 — 나중에 CI 에 자격 증명을 넣게 되면
그때 메인 계정이 걸려 있으면 곤란하다.

---

## 6. 다음

| 일감 | 관계 |
|---|---|
| **Depot ID 확인 → 첫 업로드** | §5. 이것만 하면 바로 올라간다 |
| A13 Defender / VirusTotal | **이 빌드로 한다.** NativeAOT 헬퍼가 새 변수다 — 서명 없는 1.8MB 네이티브 exe 가 키보드 입력을 읽으므로, 휴리스틱에 걸릴 후보 1순위다 |
| A14 첫 실행 플로우 | A7 §3-4 의 첫 회차 메모리 이상치(290MB)도 같이 본다 |
| 게임 콘텐츠 | **지금 `build/dist` 에 들어 있는 것은 자리표시자 마스코트뿐이다.** B 트랙이 붙기 전까지 이 빌드는 "파이프라인이 도는가" 의 증거이지 게임이 아니다 |
| 코드 서명 | 미결정. 인증서 비용 대 Defender 경고 빈도를 A13 결과를 보고 판단한다 |
