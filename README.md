# ProjectSeWoo

Windows 데스크톱 컴패니언 방치형 게임. Godot 4.7 + C#, 스팀 출시 목표.

바탕화면 위에 떠 있는 투명 창에서 캐릭터가 살고, 사용자의 PC 활동에 반응해 자란다.

- **팀** 2인 · **기간** 1개월 · **엔진** Godot 4.7.2 (.NET / C#)
- 현재 단계: **Week 0 — 기술 검증**. 기획 확정 전에 "이 껍데기가 Godot에서 서는가"를 먼저 판정한다.

## 문서

| | |
|---|---|
| [docs/WEEK0-GODOT-VALIDATION.md](docs/WEEK0-GODOT-VALIDATION.md) | Week 0 검증 계획 · 일자별 스파이크 · Go/No-Go 매트릭스 |
| [docs/DAY1-2-SPIKE.md](docs/DAY1-2-SPIKE.md) | 오버레이 셸 스파이크 실행 · 측정 절차 · 기록표 |
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
src/OverlayShell.cs        창 설정, 클릭 통과, 드래그, 핫키, 리포트
src/PerfProbe.cs           CPU%/메모리 샘플링
src/DebugHud.cs            HUD (ASCII 전용 — 기본 폰트에 한글 글리프 없음)
```
