# A5 — 커서 꾸미기 정식화

관련: [확정 기획서](기획확정-일감분배-260907.md) A5 · §3-1 · §8-2 / [A2 커서 스파이크](A2-CURSOR-SPIKE.md) / [A1 계약](A1-CONTRACTS.md) §5

> A2가 "커서를 따라다니는 클릭 통과 투명 창"이 기술적으로 성립하는지를 검증했다.
> A5는 그 창에 **실제로 3슬롯을 장착할 수 있게** 만드는 일 — `ICursorLayer` 실물과
> A2가 남긴 진단 전용 코드 정리다.

---

## 0. 상태 (2026-09-15) — 완료

| 산출물 | 위치 | 상태 |
|---|---|---|
| `ICursorLayer` 실물 | `platform/CursorLayer.cs` | ☑ (아래 §1) |
| 3슬롯(Hang/Trail/Base) 동시 장착 | 같은 파일 | ☑ |
| 에셋 로딩 (실물 우선, 없으면 자리표시자) | `ResolveTexture` | ☑ (아래 §2) |
| A2 진단 전용 코드 제거 | 같은 파일 | ☑ (아래 §3) |
| 실물 아트 (16종) | B9 (을) | ☐ 미착수 — 지금은 자리표시자로 돈다 |

---

## 1. `ICursorLayer` 실물 — `CursorLayer`가 직접 구현한다

A3의 `IShell`/`OverlayShell`과 같은 결이다. 이미 창과 상태를 쥐고 있는 클래스가
인터페이스를 직접 구현하고, 위임용 중간 클래스는 두지 않는다.

```csharp
public sealed class CursorLayer : ICursorLayer
```

`SetEnabled(bool)`과 `IsSupported`는 A2 스파이크 때부터 이미 정확히 이 시그니처로
있었다 — A5에서 새로 만든 건 `Equip(CursorSlot, string)` 하나뿐이다.

### 3슬롯 레이아웃

기획서 §3-1의 슬롯 3개를 하나의 128×128 장식 창 안에 겹쳐서 배치했다 (별도 창을
슬롯마다 만들지 않는다 — 창 하나당 OS 리소스가 붙고, A2가 이미 "창을 옮기는 비용"이
핵심 부하 요인이라고 결론 냈으므로 창 개수를 늘리는 건 최적화 방향과 반대다):

| 슬롯 | 배치 | 크기 | 비고 |
|---|---|---|---|
| Hang | 커서 위로 오프셋 | 가장 큼 | 스프링 모드일 때 흔들림 애니메이션도 이 슬롯 전담 |
| Trail | 뒤·아래로 오프셋 | 중간 | 반투명(alpha 0.6) — "잔상" |
| Base | 커서 바로 아래 | 중간 | 맨 뒤(z-index 최저) |

`Equip(slot, null)`이면 그 슬롯 스프라이트만 숨긴다. 세 슬롯은 서로 완전히
독립적이라 임의 조합(예: Hang만 장착, Trail+Base만 장착)이 전부 된다 — 상점에서
낱개로 사고 파는 기획(§3-2)과 맞아야 하는 부분이다.

### 뜻밖의 확인 — 흔들림 애니메이션이 슬롯 하나에 자연스럽게 귀속됐다

A2의 스프링 모드는 "매달린 느낌"을 내려고 창 이동 시 스프라이트를 살짝 회전시켰다.
그 회전을 이제 `CursorSlot.Hang`의 스프라이트에만 건다 — 이름 그대로 "매달려
흔들리는 원숭이"라 의미가 맞물린다. Trail/Base는 안 흔들린다.

---

## 2. 에셋 로딩 — 실물이 없어도 파이프라인은 완성돼 있다

을의 B9("1차 에셋: 커서 장식 16종")는 아직 시작 전이다(Week 2 일감이지만 착수
기록이 없다). A5가 "에셋 로딩"을 맡았으니, **실물 없이도 로딩 경로 자체는
끝내놨다**:

```csharp
private Texture2D ResolveTexture(CursorSlot slot, string assetId)
{
    string realPath = $"res://assets/cursor/{slot}/{assetId}.png";   // 소문자
    if (ResourceLoader.Exists(realPath))
    {
        return GD.Load<Texture2D>(realPath);
    }
    return _placeholderTexture;   // icon.svg
}
```

B9가 `res://assets/cursor/hang/monkey_01.png` 같은 파일을 채우는 순간 **이 메서드
호출부는 한 글자도 안 바뀌고** 실물 아트로 갈아탄다. 그 전까지는 `icon.svg` 하나를
슬롯마다 재사용하되, `assetId`를 해시해서 만든 색으로 물들인다 — 같은 아이템은
매번 같은 색으로 보여야 하므로(그래야 자리표시자로도 "이게 그 아이템"이라는 감이
온다), **`string.GetHashCode()`는 안 썼다.** .NET은 보안 때문에 프로세스마다 해시
값을 다르게 낸다 — 같은 `assetId`가 실행할 때마다 다른 색으로 보이면 자리표시자
역할도 못 한다. FNV-1a를 직접 박아서 고정했다.

---

## 3. A2 진단 전용 코드 제거

A2 문서(§2)가 클릭 통과 방식을 다섯 번 시도 끝에 `TRANSPARENT|LAYERED`로 확정했고,
나머지 둘(`WS_EX_TRANSPARENT` 단독, `WM_NCHITTEST` 후킹)은 "**쓰면 안 된다**"로
명시적으로 결론 났다. 그런데 코드에는 여전히 셋 중 아무거나 고를 수 있는
`ClickThroughMode` enum과 순환 핫키(`4`)가 남아 있었다 — **결론이 난 뒤에도 잘못된
선택지를 고를 수 있게 열어두는 것 자체가 위험**이라고 판단해 지웠다. 이제
`ApplyClickThrough()`는 분기 없이 `TRANSPARENT|LAYERED`만 건다.

같이 지운 것:

| 제거 | 이유 |
|---|---|
| `DebugFill`/`ToggleDebugFill`/분홍 사각형 | "이 창이 뭔가 그리기는 하는가"라는 A2의 질문에 답이 이미 나왔다. 지금은 실제 스프라이트가 그 역할을 대신한다 |
| `ProbeRenderTarget()` | 렌더 타깃 픽셀을 직접 읽던 A2 진단. 같은 이유로 더 필요 없다 |
| `HookWndProc`/`HookProc`/`ClearClickThrough` | `WM_NCHITTEST` 방식 전용 배관. 그 방식 자체를 지웠으니 같이 나갔다 |
| `--cursor-fill`, `--cursor-clickthru=` 무인 인자 | 위 둘에 대응하는 스크립트 인자. `docs/A2-CURSOR-SPIKE.md`에 갱신 주석 남김 |

**지운 것과 안 지운 것을 가른 기준**: A2의 질문("되는가")에 답이 이미 난 진단은
지웠고, A7(저부하 재측정)이 앞으로도 쓸 것(`Simulate`, `SkipClickThrough`,
`IntervalsMs`/`CycleInterval`)은 남겼다.

### 새 debug 키 / 인자 (상점 UI B6 이전까지 `Equip`을 시험하는 용도)

| 키/인자 | 동작 |
|---|---|
| `2` / `3` / `4` | Hang / Trail / Base 슬롯을 자리표시자 목록(빔·`demo_a`·`demo_b`·`demo_c`)에서 순환 |
| `--cursor-equip=<hang>,<trail>,<base>` | 무인 측정에서 슬롯을 미리 채운 채로 잰다. 빈 칸은 그 슬롯을 비움 (예: `--cursor-equip=monkey_01,,leaf_02`) |

B6이 상점/장착 화면을 만들면 이 자리를 그 UI가 대신 호출한다. `--cursor-equip=`은
A7이 "장식을 낀 채"와 "안 낀 채"의 렌더 비용 차이를 잴 때 쓴다.

---

## 4. 검증

무인 측정 경로로 `Equip` 전체를 실측했다(사람 손이 필요한 인터랙티브 키 대신,
포커스 문제 없이 재현 가능한 경로를 골랐다):

```
Godot.exe --path . -- --report=<경로> --seconds=6 --warmup=1 --cursor --cursor-equip=monkey_01,,leaf_02
```

```
[cursor] window id=1 size=128x128 -> OS window OK (transparent True, bg True)
[cursor] click-through TRANSPARENT|LAYERED (ex 0x8200018 -> 0x8280038)
[cursor] Hang = monkey_01
[cursor] Base = leaf_02
...
cursor on, spring, every 16ms, moves 156, skip 145, clickthru TRANSPARENT|LAYERED, equip H:monkey_01 T:- B:leaf_02
```

- Hang/Base가 각각 장착되고 Trail은 비운 그대로(`T:-`) 반영됨 — 슬롯 독립성 확인
- 클릭 통과는 여전히 `TRANSPARENT|LAYERED`로 정상 적용 — enum을 걷어낸 리팩터가
  동작을 안 바꿨는지 확인
- 프로세스 종료 후 고아 프로세스 없음 확인

**아직 안 한 것 — 육안 확인.** 세 슬롯이 실제로 겹쳐 보일 때 서로 가리지 않는지,
색 구분이 실제로 되는지는 화면을 봐야 안다. 자리표시자 아트라 큰 의미는 없고,
B9의 실물 아트가 들어온 뒤 다시 볼 사안이다.

---

## 5. 다음

| 일감 | 관계 |
|---|---|
| B9 (을) | 실물 에셋 16종을 `res://assets/cursor/<slot>/<id>.png`에 채운다. 코드 변경 불필요 |
| B6 (을) | 상점/장착 UI가 `Equip()`을 호출. 지금의 debug 키(`2`/`3`/`4`)를 대신한다 |
| A6 옵션 창 | 커서 장식 On/Off (§7-4)가 `SetEnabled`를 호출 |
| A7 저부하 재측정 | `--cursor-equip=`으로 장식을 낀 채 실측 — 지금까지의 측정은 전부 빈 슬롯 기준이었다 |
