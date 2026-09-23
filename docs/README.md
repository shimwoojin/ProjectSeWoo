# 문서 지도

PunchMonkey (저장소·어셈블리 이름은 `ProjectSeWoo`) 문서 목록과 읽는 순서.
파일이 15개가 넘어서, **"뭘 먼저 읽나"** 를 여기서 가른다.

> 게임명은 `PunchMonkey`, 코드 식별자는 `ProjectSeWoo` 다. 둘이 다른 것은 의도다 —
> 유저에게 보이는 이름만 바꾸고 네임스페이스·솔루션은 그대로 뒀다
> (DEVLOG 2026-09-16).

---

## 처음 왔다면

| 당신이 | 읽을 것 |
|---|---|
| **을 (게임 레이어 담당)** | **[B-TRACK-START.md](B-TRACK-START.md) 하나로 시작한다.** 거기서 읽는 순서를 다시 안내한다. 이 지도로 돌아올 필요 없다 |
| 갑 (플랫폼 담당) / 나중의 나 | 아래 §2 일감별 문서. 판단 근거는 전부 [DEVLOG.md](DEVLOG.md) 에 있다 |
| 무슨 게임인지부터 | [기획확정-일감분배-260907.md](기획확정-일감분배-260907.md) §2 |

---

## 1. 뼈대 문서 — 이 셋이 계약이다

| 문서 | 무엇 |
|---|---|
| [기획확정-일감분배-260907.md](기획확정-일감분배-260907.md) | **확정 기획서.** 게임 설계 · 4주 일정 · 역할 분담 · 컷 라인 · 스팀 행정 체크리스트 |
| [A1-CONTRACTS.md](A1-CONTRACTS.md) | 인터페이스 6종과 목(Mock). **갑/을이 서로를 안 기다려도 되는 이유** |
| [SCENE-ARCHITECTURE.md](SCENE-ARCHITECTURE.md) | 씬을 어디에 어떻게 만드는가. `IInteractiveArea`, `GameRoot` 접점 |

인터페이스 시그니처를 바꿔야 하면 **A1 §0 변경 이력 표에 날짜와 사유를 남기고
상대에게 알린 뒤에만** 한다 (기획서 §8-2).

---

## 1-1. 경제 서버 피벗 (2026-09-23~, 기획서 §4-1 을 뒤집는 트랙)

| 문서 | 무엇 |
|---|---|
| [ECONOMY-SERVER.md](ECONOMY-SERVER.md) | 바나나·나무 슬롯·강화를 서버 권위로, 커서 장식을 스팀 인벤토리로 옮기는 결정. 스팀 커뮤니티 마켓 승인 게이트 |
| [ECONOMY-SERVER-API.md](ECONOMY-SERVER-API.md) | 백엔드 REST 계약 + 비용 없이 가는 호스팅 구조 |
| [../server/README.md](../server/README.md) | Cloudflare Workers 실행법. 실배포·실구매까지 검증 완료(§3) |

**기존 4주 일정·역할 분담·컷 라인(기획서 §8~§10)은 이 트랙에 적용하지 않는다.**
일정은 자율로 관리한다.

---

## 2. 갑 트랙 일감 문서

각 문서는 **결과보다 "왜 그렇게 했는가"** 를 남긴다. 되짚을 일이 생기면 여기부터.

| 일감 | 문서 | 한 줄 |
|---|---|---|
| A2 커서 추종 창 | [A2-CURSOR-SPIKE.md](A2-CURSOR-SPIKE.md) | 클릭 통과는 `WS_EX_TRANSPARENT` 만으로 안 된다 |
| A3 셸 모듈화 | [A3-SHELL-MODULE.md](A3-SHELL-MODULE.md) | `IShell` 실물, 세이브 파일 I/O |
| A4 글로벌 입력 | [A4-GLOBAL-INPUT.md](A4-GLOBAL-INPUT.md) | **별도 헬퍼 프로세스.** 엔진 안에서는 RawInput 을 못 받았다 |
| A5 커서 꾸미기 | [A5-CURSOR-COSMETICS.md](A5-CURSOR-COSMETICS.md) | 3슬롯 장착, `ICursorLayer` 실물 |
| A6 트레이·옵션 | [A6-TRAY-OPTIONS.md](A6-TRAY-OPTIONS.md) | 자동 시작, 세이브 스키마 v2 |
| A7 저부하 | [A7-PERF.md](A7-PERF.md) | **메모리 기준을 `PrivWS` < 300MB 로 재설정 → 실측 239MB, 확정 Go** |
| A8 스팀 | [A8-STEAM.md](A8-STEAM.md) | GodotSteam 대신 Steamworks.NET. 앱 ID `5281130` |
| A12 빌드·배포 | [A12-BUILD.md](A12-BUILD.md) | **Godot 익스포트만으로는 돌아가는 빌드가 안 나온다.** 파이프라인 + Depot 업로드 |
| 스팀 설정 (C1·C2 연결) | [STEAM-CONFIG.md](STEAM-CONFIG.md) | **빌드를 올리는 것과 실행되게 하는 것은 다른 일이다.** 앱 페이지 · 스토어 페이지 설정값과 근거 |

---

## 3. 기록

| 문서 | 무엇 |
|---|---|
| [DEVLOG.md](DEVLOG.md) | **판단과 근거, 그리고 밟은 함정.** 전용 문서에 안 적히는 것이 여기 쌓인다 |
| [WEEK0-GODOT-VALIDATION.md](WEEK0-GODOT-VALIDATION.md) | 착수 전 엔진 검증 |
| [DAY1-2-SPIKE.md](DAY1-2-SPIKE.md) | 초기 스파이크. 측정 절차와 렌더러 A/B 근거 |

**막히면 DEVLOG 의 "밟은 함정" 목록부터 본다.** 2026-09-16 기준 27개이고, 대부분
**증상과 원인이 어긋나는** 것들이다 (예: 로그인 ID 가 틀렸는데 에러는 "비밀번호가
틀렸다" 로 나온다).

---

## 4. 도구

| 스크립트 | 언제 |
|---|---|
| `tools/setup-steamcmd.cmd` | **새 빌드 머신 준비** (더블클릭). steamcmd 설치 + 최초 로그인. [A12-BUILD.md](A12-BUILD.md) §5-5 |
| `tools/build-release.ps1` | 스팀에 올릴 릴리스 빌드. [A12-BUILD.md](A12-BUILD.md) |
| `tools/measure-renderers.ps1` | 메모리·CPU 실측. [A7-PERF.md](A7-PERF.md) §3 |
| `tools/inspect-windows.ps1` | 창 스타일 플래그 확인 (클릭 통과 검증). [A2-CURSOR-SPIKE.md](A2-CURSOR-SPIKE.md) |

새 `.ps1` 은 **UTF-8 BOM 으로 저장**해야 한다. BOM 이 없으면
`powershell -File` 이 ANSI 로 읽어서 한글 주석이 깨지고, **깨진 바이트가 따옴표를
망가뜨려 문법 오류처럼 보인다** (DEVLOG 함정 27).

---

## 5. 지금 상태 (2026-09-16)

| | |
|---|---|
| 갑 트랙 | A1~A8, A12 완료. **W1~W2 물량 소진** |
| 을 트랙 | **미착수.** `game/` 은 아직 빈 폴더 — 프로젝트 최대 리스크 |
| 스팀 행정 | 수수료 결제 완료, 앱 ID `5281130`, depot `5281131`, 첫 빌드 업로드 완료 (BuildID `25338283`) |
| 남은 마감 | 스토어 페이지 공개 **10/2** (출시 2주 전) · 출시 가능일 **10/16~** |

세부는 [DEVLOG.md](DEVLOG.md) 의 2026-09-16 항목들과
[기획확정-일감분배-260907.md](기획확정-일감분배-260907.md) §11.
