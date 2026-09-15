# ProjectSeWoo

Windows 데스크톱 컴패니언 방치형 게임. Godot 4.7 + C#, 스팀 출시 목표.

바탕화면 위에 떠 있는 투명 창에서 캐릭터가 살고, 사용자의 PC 활동에 반응해 자란다.

- **팀** 2인 · **기간** 1개월 · **엔진** Godot 4.7.2 (.NET / C#)
- 현재 단계: **Week 2 — 메타 완성 + 에셋 파이프라인** (기획확정-일감분배-260907.md §9). Week 0/1의
  기술 검증(오버레이 셸, 커서 추종 창, 글로벌 입력)은 전부 Go 판정이 났다.

## 문서

| | |
|---|---|
| [docs/기획확정-일감분배-260907.md](docs/기획확정-일감분배-260907.md) | 확정 기획서 + 2인(갑/을) 일감 분배, 주차별 계획 |
| [docs/WEEK0-GODOT-VALIDATION.md](docs/WEEK0-GODOT-VALIDATION.md) | Week 0 검증 계획 · 일자별 스파이크 · Go/No-Go 매트릭스 |
| [docs/DAY1-2-SPIKE.md](docs/DAY1-2-SPIKE.md) | 오버레이 셸 스파이크 실행 · 측정 절차 · 기록표 |
| [docs/A1-CONTRACTS.md](docs/A1-CONTRACTS.md) | 인터페이스 4종 + 세이브 스키마 v1 + 목 구현 |
| [docs/A2-CURSOR-SPIKE.md](docs/A2-CURSOR-SPIKE.md) | 커서 추종 창 스파이크 (클릭 통과 시행착오 포함) |
| [docs/A3-SHELL-MODULE.md](docs/A3-SHELL-MODULE.md) | 셸 모듈화, `IShell` 실물, 세이브 파일 I/O |
| [docs/A4-GLOBAL-INPUT.md](docs/A4-GLOBAL-INPUT.md) | 글로벌 입력 (RawInput, 별도 헬퍼 프로세스) |
| [docs/A5-CURSOR-COSMETICS.md](docs/A5-CURSOR-COSMETICS.md) | 커서 꾸미기 정식화, `ICursorLayer` 실물, 3슬롯 장착 |
| [docs/A6-TRAY-OPTIONS.md](docs/A6-TRAY-OPTIONS.md) | 트레이 아이콘, 자동 시작, 옵션 창, 세이브 스키마 v2 |
| [docs/A7-PERF.md](docs/A7-PERF.md) | 저부하 최종 실측(커서 창 포함), .NET 런타임 튜닝 한계, 메모리 게이트 판단 |
| [docs/DEVLOG.md](docs/DEVLOG.md) | 개발 로그 · 결정 사항 · 밟은 함정 |

## 실행

**Godot .NET(mono) 빌드가 필요하다.** 표준 빌드는 C#을 읽지 못한다.

```
Godot_v4.7.2-stable_mono_win64.exe --path .
```

에디터 없이 컴파일만 확인:

```
dotnet build ProjectSeWoo.csproj
```

조작 키와 측정 절차는 [docs/DAY1-2-SPIKE.md](docs/DAY1-2-SPIKE.md)에 있다.

## 구조

```
project.godot              투명/무테/항상위/per-pixel 투명 + 스트레치 1:1 고정
ProjectSeWoo.csproj        Godot.NET.Sdk 4.7.2, net8.0
scenes/Shell.tscn          루트 노드 하나. 나머지는 코드로 구성
shared/Contracts/          갑/을 인터페이스 4종 (IInputSource, ICursorLayer, INetSession, IShell)
shared/Save/               세이브 스키마 v2 + 마이그레이션 훅
platform/                  갑 담당. OS와 붙는 전부
  OverlayShell.cs            창 설정, 클릭 통과, 드래그, 핫키, 리포트. IShell 실물
  CursorLayer.cs             A2 커서 추종 창. ICursorLayer 실물, 3슬롯 장착
  HelperInputSource.cs       IInputSource 실물 (별도 헬퍼 프로세스 IPC)
  InputHelper/               별도 exe. RawInput 으로 타건 수만 센다
  SaveIO.cs                  세이브 파일 I/O (원자적 쓰기)
  OptionsWindow.cs           옵션 창 UI (§7-4 전 항목)
  TrayIcon.cs                트레이 아이콘 + 우클릭 메뉴
  Autostart.cs               시작 프로그램 등록 (HKCU Run 키)
  FullscreenWatcher.cs       전체화면 앱 위 자동 숨김 휴리스틱
  PerfProbe.cs               CPU%/메모리 샘플링
  DebugHud.cs                HUD (ASCII 전용 — 기본 폰트에 한글 글리프 없음)
  Mocks/                     4개 인터페이스의 목 구현 (을이 갑을 안 기다리고 쓴다)
game/                      을 담당. 게임 안에서 도는 전부 (아직 착수 전)
tools/VsLauncher/          Visual Studio F5 디버깅용 런처 (게임 아님, 배포 제외)
```

`tools/VsLauncher`는 VS에서 F5로 게임을 띄우고 중단점을 잡기 위한 껍데기 프로젝트다.
`ProjectSeWoo`는 클래스 라이브러리라 VS가 직접 실행하지 못해서 필요하다.
엔진 경로는 `tools/VsLauncher/Properties/launchSettings.json`의 `executablePath` 한 줄이므로,
각자 환경에 맞게 고쳐서 쓴다.
