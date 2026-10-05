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
    /// <summary>
    /// 내 창 배율. 옵션의 "내 창 크기" (§7-4). 친구 칸·내 커서 크기는 옵션에서 따로 정하고 셸 안에서 끝난다 (2026-09-30).
    /// </summary>
    void SetScale(float s);

    /// <summary>
    /// 친구 칸 안의 커서 장식 크기 - 옵션 "친구 커서 크기" (2026-09-30). 1 이 원래 크기, <b>0 이면 숨긴다.</b>
    /// 친구 칸 안은 게임 레이어가 그리므로(<c>RemotePlayerView</c>) 이 값만 넘긴다.
    /// </summary>
    float FriendCursorScale { get; }

    /// <summary><see cref="FriendCursorScale"/> 가 바뀌었다.</summary>
    event System.Action FriendCursorScaleChanged;

    /// <summary>창 투명도. 옵션의 "투명도" (§7-4).</summary>
    void SetOpacity(float a);

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
    /// <paramref name="width"/> 는 패널이 차지할 창 왼쪽 폭(창 px)이다 - 메뉴 칸(<see cref="SetSidePanel"/>) 안에만
    /// 뜨게 한다 (2026-09-28). 트레이에서 열 때는 둘 다 0 이다(창 전체, 기본 여백).
    /// </summary>
    void OpenOptions(float topInset, float width);

    /// <summary>옵션 창을 닫는다. 이미 닫혀 있으면 아무 일도 없다.</summary>
    void CloseOptions();

    bool IsOptionsOpen { get; }

    /// <summary>
    /// 옵션 창이 닫혔다 - 누가 닫았든(창의 [닫기], Esc, <see cref="CloseOptions"/>). 메뉴가 탭 줄을
    /// 같이 걷을지 판단하는 데 쓴다.
    /// </summary>
    event System.Action OptionsClosed;

    /// <summary>
    /// 메뉴 칸 (2026-09-28). 메인 창을 <b>왼쪽으로</b> <paramref name="width"/> 만큼(창 px, 배율과 무관) 넓혀 게임 화면을
    /// 가리지 않는 자리를 만든다. 0 이면 원래 크기로 돌린다.
    ///
    /// 넓힌 칸은 창 좌표 x 0 ~ <paramref name="width"/> 이다 - 게임 화면(셸 루트 아래)은 그만큼 오른쪽으로 옮겨 그리고,
    /// 화면에서 보이는 자리는 그대로다. <c>CanvasLayer</c> 창(상점·강화·로비·설정·안내)은 배율·위치를 안 물려받으므로
    /// 그 칸에 스스로 붙는다. 칸의 높이는 배율 1 때의 창 높이로 고정이다 (2026-09-30 - 예전엔 창 높이를 따라 "내 창 크기"
    /// 를 키우면 메뉴가 아래로 길어졌다). 창은 칸이 열려 있는 동안 그 높이보다 작아지지 않는다.
    ///
    /// 화면 왼쪽(또는 아래)에 자리가 모자라면 창을 화면 안에 두고 게임 화면이 잠깐 비켜난다 - 닫으면 제자리로
    /// 돌아온다. 열린 동안 창을 끌어 옮겼으면 옮긴 자리에 남는다.
    ///
    /// <paramref name="overlap"/> 은 칸이 게임 화면 왼쪽을 덮어도 되는 폭이다 (<b>게임 좌표, 배율 전</b> - 셸이 배율을
    /// 곱한다). 게임 화면 왼쪽 가장자리에 그릴 것이 없으면 그만큼 칸이 나무·원숭이 쪽으로 붙는다 (2026-09-30). 창은
    /// 칸 폭에서 그 몫을 뺀 만큼만 넓어지고, 게임 화면도 그만큼만 오른쪽으로 옮겨 그린다.
    /// </summary>
    void SetSidePanel(int width, float overlap);

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
