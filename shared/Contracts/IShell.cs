using Godot;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 오버레이 셸 제어 (기획확정-일감분배-260907.md §8-2, §7-1). 갑 제공 → 을 소비.
///
/// 투명 + 무테 + 항상 위 + 클릭 통과 창을 게임 레이어가 다룰 수 있게 좁혀 놓은 것이다.
/// Day 1-2 스파이크에서 이 조합이 동작하는 것을 확인했다 — docs/DAY1-2-SPIKE.md.
/// </summary>
public interface IShell
{
    /// <summary>창 배율. 옵션의 "크기" (§7-4).</summary>
    void SetScale(float s);

    /// <summary>창 투명도. 옵션의 "투명도" (§7-4).</summary>
    void SetOpacity(float a);

    /// <summary>
    /// 클릭 통과 On/Off. 끄면 나무·원숭이 영역이 마우스를 받는다 (§7-1).
    /// 옵션의 "위치 잠금" 이 이것과 연결된다.
    /// </summary>
    void SetClickThrough(bool on);

    /// <summary>
    /// 창을 놓을 수 있는 영역. **멀티모니터와 작업표시줄을 고려한 값**이다 (§7-1).
    ///
    /// 게임 레이어가 화면 크기를 직접 묻지 않게 하는 것이 요점이다. 모니터가
    /// 사라지거나 배율이 바뀌는 경우의 폴백은 플랫폼 레이어가 책임진다.
    /// </summary>
    Rect2I GetSafeArea();

    /// <summary>
    /// 옵션 창을 연다(이미 열려 있으면 닫는다). 게임 화면의 [옵션] 버튼이 부른다 (2026-09-24).
    ///
    /// 옵션 창은 셸 소유다 — 설정 저장·자동 시작·클릭 통과를 셸이 적용한다. 버튼을 셸이 직접
    /// 그리지 않고 게임 레이어가 상점·멀티 옆에 두는 이유는 클릭 통과 영역이 사각형 하나라서다
    /// (<c>WindowSetMousePassthrough</c> 는 다각형 하나만 받는다). 버튼이 다른 구석에 있으면 그
    /// 사이 빈 공간까지 클릭을 먹는다. 전에는 디버그 키 O 로만 열려서 유저가 열 길이 트레이뿐이었다.
    /// </summary>
    void ToggleOptions();

    /// <summary>
    /// 창에 높이 <paramref name="height"/> 짜리 칸을 붙인다 (B10 친구 칸). 0 이면 원래대로.
    /// 값은 콘텐츠 픽셀(배율 적용 전)이다.
    ///
    /// <b>어느 쪽에 붙일지는 셸이 정한다.</b> 창 아래에 화면이 남으면 아래, 모자라면 위 -
    /// 기본 위치가 화면 오른쪽 아래 구석이라 아래로만 늘리면 화면 밖으로 나간다. 위로 늘릴
    /// 때는 창을 올린 만큼 콘텐츠를 내려서 <b>나무·원숭이가 화면에서 제자리에 있게</b> 한다.
    ///
    /// <b>방향은 친구가 들고 날 때는 안 바뀌고, 창을 끌어 놓았을 때만 다시 정한다</b>
    /// (<see cref="WindowExtensionChanged"/>). 칸 높이만 바뀌는 호출은 방향을 유지한다.
    ///
    /// <paramref name="belowOverlap"/>: 아래에 붙일 때 원래 창의 맨 아래 몇 픽셀에 칸을 겹칠지.
    /// 원래 창 밑이 비어 있으면 그만큼 창을 덜 늘린다. 칸의 게임 좌표는 아래면
    /// <c>y = 기본 높이 - belowOverlap</c> 부터, 위면 <c>y = -height</c> 부터다.
    ///
    /// 늘어난 칸은 클릭을 받지 않는다 - 클릭 영역은 여전히
    /// <see cref="IInteractiveArea.GetClickableBounds"/> 가 신고한 것뿐이다.
    /// </summary>
    WindowExtension ExtendWindow(int height, int belowOverlap = 0);

    /// <summary>
    /// 붙인 칸의 방향이 바뀌었다 - 유저가 창을 끌어서 아래에 자리가 생겼거나 없어졌다.
    /// 칸을 그리는 쪽은 새 방향에 맞춰 자리를 옮긴다. 메인 스레드에서 온다.
    /// </summary>
    event System.Action<WindowExtension> WindowExtensionChanged;
}
