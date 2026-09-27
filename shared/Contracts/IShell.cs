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
    /// 옵션 창을 연다. 게임 화면의 메뉴(<c>MenuHub</c>) "설정" 탭이 부른다 (2026-09-27).
    ///
    /// 옵션 창은 셸 소유다 — 설정 저장·자동 시작·클릭 통과를 셸이 적용한다. 버튼을 셸이 직접
    /// 그리지 않고 게임 레이어가 메뉴 안에 두는 이유는 클릭 통과 영역이 사각형 하나라서다
    /// (<c>WindowSetMousePassthrough</c> 는 다각형 하나만 받는다). 버튼이 다른 구석에 있으면 그
    /// 사이 빈 공간까지 클릭을 먹는다.
    ///
    /// <paramref name="topInset"/> 은 패널이 시작할 높이(창 px)다 - 그 위를 메뉴의 탭 줄이 덮는다.
    /// 트레이에서 열 때는 0 이다(기본 여백).
    /// </summary>
    void OpenOptions(float topInset);

    /// <summary>옵션 창을 닫는다. 이미 닫혀 있으면 아무 일도 없다.</summary>
    void CloseOptions();

    bool IsOptionsOpen { get; }

    /// <summary>
    /// 옵션 창이 닫혔다 - 누가 닫았든(창의 [닫기], Esc, <see cref="CloseOptions"/>). 메뉴가 탭 줄을
    /// 같이 걷을지 판단하는 데 쓴다.
    /// </summary>
    event System.Action OptionsClosed;

    /// <summary>
    /// 작은 창을 하나 띄운다 (B10 친구 칸, <see cref="ISatelliteWindow"/>).
    /// <paramref name="contentSize"/> 는 배율 전 크기다.
    ///
    /// 위치: <paramref name="savedPosition"/> 이 지금 모니터 어딘가에 있으면 거기, 아니면
    /// <paramref name="preferredOffsets"/> 를 차례로 보고 창이 화면에 다 들어오는 첫 자리.
    /// 오프셋은 메인 창 콘텐츠 좌표(배율 전) 기준이다 - 예: "내 원숭이 바로 아래", "바로 위".
    /// 어느 것도 안 맞으면 첫 자리를 화면 안으로 밀어 넣는다.
    /// </summary>
    ISatelliteWindow OpenSatellite(string name, Vector2I contentSize, Vector2I? savedPosition, params Vector2[] preferredOffsets);
}
