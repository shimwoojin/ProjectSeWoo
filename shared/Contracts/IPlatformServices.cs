namespace ProjectSeWoo.Shared;

/// <summary>
/// 플랫폼 실물 묶음 (기획확정-일감분배-260907.md §8-2). 갑 제공 → 을 소비.
///
/// <b>왜 계약이 하나 더 필요한가</b> — A1~A8 로 실물 5종이 전부 생겼는데도
/// <c>game/GameRoot</c> 는 목(mock)을 <c>new</c> 해서 쓰고 있었다. 그 결과
/// <c>HelperInputSource.OnKeystrokes</c>(A4 실물)의 구독자가 **한 명도 없었고**,
/// 오버레이 창에 포커스가 있을 때만 게임이 돌았다 — 별도 헬퍼 프로세스를 만든
/// 이유 자체가 작동하지 않는 상태였다.
///
/// 원인은 배선 실수가 아니라 **조립 지점이 설계에 없던 것**이다. 실물은 전부
/// <c>OverlayShell</c> 이 들고 있는데 그걸 게임 레이어로 넘길 통로가 없어서,
/// 을은 목을 직접 만드는 것 말고 선택지가 없었다. 그리고 목이
/// <c>platform/Mocks/</c> 에 있었으므로 <c>game/</c> 이 <c>platform/</c> 을
/// 직접 참조하게 됐다 — §8-3 폴더 소유권 규칙이 깨진 자리다.
/// (그래서 목은 이 커밋에서 <c>shared/Mocks/</c> 로 옮겼다.)
///
/// <b>왜 5종을 묶는가</b> — 인자를 5개 받는 <c>Attach</c> 를 만들면 A9~A11 로
/// <see cref="INetSession"/> 실물이 붙거나 7번째 계약이 생길 때마다 시그니처가
/// 바뀌고, 그때마다 <c>game/</c> 이 같이 흔들린다. 묶어 두면 늘어나는 것은
/// 프로퍼티 하나뿐이고 기존 호출부는 안 바뀐다.
///
/// <b>널이 아니다.</b> 전 프로퍼티가 항상 값을 갖는다 — 스팀이 없어도, 멀티
/// 실물이 아직 없어도 마찬가지다. 게임 레이어가 분기 없이 그냥 부르면 되게
/// 하는 것이 <see cref="IInputSource.IsAvailable"/>/<see cref="IAchievements.IsAvailable"/>
/// 부터 지켜 온 규칙이고, 여기서 널을 허용하면 그 규칙이 무너진다.
/// 실물이 없는 자리는 "쓸 수 없다고 답하는 구현"이 채운다.
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// 전역 타건 수 (A4 실물 = <c>platform/HelperInputSource</c>).
    ///
    /// <b>틱을 돌리지 마라.</b> 실물의 폴링은 셸이 <c>_Process</c> 에서 이미
    /// 굴리고 있다. 계약(<see cref="IInputSource"/>)에 틱이 없는 것이 그 뜻이고,
    /// 게임 레이어가 할 일은 <see cref="IInputSource.OnKeystrokes"/> 를 구독하는 것뿐이다.
    /// </summary>
    IInputSource Input { get; }

    /// <summary>
    /// 세이브 파일 (A3 실물 = <c>platform/SaveStore</c>).
    ///
    /// 게임 레이어가 <c>SaveIO</c> 를 직접 부르지 않는 이유는
    /// <see cref="ISaveStore"/> 주석에 있다 - 한 파일에 쓰는 주체가 둘이라
    /// 소유자를 하나로 묶어야 한다.
    /// </summary>
    ISaveStore Save { get; }

    /// <summary>커서 장식 (A5 실물 = <c>platform/CursorLayer</c>).</summary>
    ICursorLayer Cursor { get; }

    /// <summary>오버레이 셸 (A3 실물 = <c>platform/OverlayShell</c> 자기 자신).</summary>
    IShell Shell { get; }

    /// <summary>
    /// 스팀 도전과제 (A8 실물 = <c>platform/SteamService</c>).
    /// 스팀이 없는 실행에서는 "항상 사용 불가"로 답하는 구현이 들어온다.
    /// </summary>
    IAchievements Achievements { get; }

    /// <summary>
    /// 관전형 멀티 세션.
    ///
    /// <b>A9~A11 전까지는 목이다.</b> 실물이 없다고 널을 넘기면 을이 룸 화면
    /// (B10~B12)을 만들 수 없는데, 멀티는 컷 라인 6번(=마지막)이라 W3 실물을
    /// 기다리는 것이 순서상 맞지 않는다 (§10). 실물이 붙는 날 이 프로퍼티가
    /// 돌려주는 것만 바뀌고 <c>game/</c> 은 안 바뀐다.
    /// </summary>
    INetSession Net { get; }
}

/// <summary>
/// 게임 레이어가 실물을 받는 구멍. <b>방향이 반대다</b> —
/// <see cref="IPlatformServices"/> 는 "갑 제공 → 을 소비"인데, 이건
/// <see cref="IInteractiveArea"/> 와 같이 <b>을이 구현하고 갑이 부른다.</b>
///
/// 찾는 방법도 <see cref="IInteractiveArea"/> 와 같다 —
/// <c>game/GameRoot</c> 가 <see cref="SceneGroups.GameRoot"/> 그룹에 자기를
/// 등록해 두면 <c>OverlayShell</c> 이 그룹으로 찾아 캐스팅한다. platform/ 이
/// game/ 의 구체 타입을 컴파일 타임에 참조하는 일이 없다.
///
/// <b>⚠ 호출 시점이 <c>_Ready()</c> 보다 늦다.</b> Godot 은 자식의
/// <c>_Ready()</c> 를 부모보다 **먼저** 부르는데, 실물을 만드는 것은 부모
/// (<c>OverlayShell._Ready()</c>)다. 그래서 게임 레이어의 <c>_Ready()</c> 안에서는
/// 아직 아무것도 받지 못한 상태다 — 입력 구독처럼 실물이 필요한 배선은 전부
/// <see cref="AttachPlatform"/> 안에서 해야 한다. 여기서 순서를 잘못 잡으면
/// 증상이 "조용히 아무 일도 안 일어남"이라 눈에 안 띈다.
/// </summary>
public interface IPlatformConsumer
{
    /// <summary>
    /// 실물을 넘긴다. 한 세션에 한 번만 불린다.
    /// </summary>
    void AttachPlatform(IPlatformServices platform);
}
