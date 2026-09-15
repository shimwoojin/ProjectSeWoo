using Godot;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 클릭을 받아야 하는 화면 영역을 신고한다. **방향이 나머지 3개 계약과 반대다** —
/// <see cref="IShell"/>/<see cref="ICursorLayer"/>/<see cref="IInputSource"/>는
/// "갑 제공 → 을 소비"인데, 이건 **을이 구현하고 갑(`OverlayShell`)이 매 프레임
/// 읽는다.**
///
/// <b>왜 필요한가</b> — 지금까지 `OverlayShell`(플랫폼)이 클릭 통과 폴리곤을
/// 계산하려고 마스코트 스프라이트를 직접 참조했다. 게임 콘텐츠가 자리표시자
/// (`icon.svg` 하나)일 때는 문제가 없었지만, 을이 실제 나무/원숭이 씬을 만들면
/// 플랫폼 코드가 게임 레이어의 노드를 직접 아는 상태가 된다 — §8-3의 폴더
/// 소유권 규칙이 씬 레벨에서 깨지는 것이다.
///
/// 이 인터페이스가 경계를 세운다: **게임 레이어는 "여기가 클릭 영역이다"만
/// 알려주고, 그 값으로 무엇을 할지(passthrough 폴리곤 계산, 배율 적용, 여백
/// 추가)는 전부 플랫폼이 결정한다.** `game/GameRoot`가 이걸 구현하고, 자기
/// 자신을 <see cref="SceneGroups.GameRoot"/> 그룹에 등록해 두면 `OverlayShell`이
/// 타입 의존 없이(그룹으로) 찾아서 쓴다 — platform/이 game/의 타입을 컴파일
/// 타임에 참조하는 일이 없다.
///
/// 지금은 `platform/PlaceholderMascot.cs`가 유일한 구현체다. `game/GameRoot`가
/// 생기는 날 자리표시자를 대체한다.
/// </summary>
public interface IInteractiveArea
{
    /// <summary>
    /// 클릭을 받을 사각형. **이 콘텐츠의 부모 Node2D 로컬 좌표계** 기준이다 —
    /// 셸의 배율(<see cref="IShell.SetScale"/>)이나 클릭 여백은 플랫폼이 별도로
    /// 적용하므로 여기 넣지 않는다. 애니메이션 등으로 매 프레임 값이 달라져도
    /// 된다 — 플랫폼이 매 프레임 다시 읽는다(별도 변경 이벤트가 필요 없다).
    /// </summary>
    Rect2 GetClickableBounds();
}
