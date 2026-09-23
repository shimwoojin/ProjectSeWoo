namespace ProjectSeWoo.Shared.Mocks;

/// <summary>
/// <see cref="IPlatformServices"/> 목 묶음. 목 5종을 한 덩어리로 들고 있다.
///
/// 쓰는 자리는 둘이다:
/// <list type="number">
///   <item><b>씬 단독 실행.</b> 에디터에서 <c>game/GameRoot.tscn</c> 만 열어
///   실행하면 셸이 없어서 <see cref="IPlatformConsumer.AttachPlatform"/> 이
///   안 온다. 그때 게임이 죽는 대신 이걸로 돈다 — 게임 레이어만 빠르게
///   고쳐 보는 흐름이 <c>Shell.tscn</c> 을 거치지 않아도 성립해야 한다.</item>
///   <item><b>계약 시험.</b> <c>IsAvailable</c>/<c>IsSupported</c> 를 꺼서
///   "전역 입력이 막힌 PC", "커서 축이 죽은 환경", "스팀 오프라인" 경로를
///   실제로 만들어 볼 수 있다. 실물로는 재현하기 어려운 것들이다.</item>
/// </list>
///
/// 프로퍼티를 인터페이스 타입이 아니라 **구체 목 타입**으로 노출한다 —
/// <c>Feed()</c>, <c>SimulateJoin()</c>, <c>IsAvailable</c> 세터처럼 시험용
/// API 는 계약에 없고 목에만 있기 때문이다. <see cref="IPlatformServices"/>
/// 쪽은 명시적 구현으로 따로 뚫어 둔다.
/// </summary>
public sealed class MockPlatformServices : IPlatformServices
{
    public MockInputSource Input { get; } = new();

    public MockSaveStore Save { get; } = new();

    public MockCursorLayer Cursor { get; } = new();

    public MockShell Shell { get; } = new();

    public MockAchievements Achievements { get; } = new();

    public MockNetSession Net { get; } = new();

    public MockEconomyService Economy { get; } = new();

    public MockInventoryService Inventory { get; } = new();

    IInputSource IPlatformServices.Input => Input;

    ISaveStore IPlatformServices.Save => Save;

    ICursorLayer IPlatformServices.Cursor => Cursor;

    IShell IPlatformServices.Shell => Shell;

    IAchievements IPlatformServices.Achievements => Achievements;

    INetSession IPlatformServices.Net => Net;

    IEconomyService IPlatformServices.Economy => Economy;

    IInventoryService IPlatformServices.Inventory => Inventory;

    public MockPlatformServices()
    {
        // 실물에서는 서버 한 트랜잭션인 "구매→지급"을 목 둘로 재현하려면
        // 서로를 알아야 한다 (docs/ECONOMY-SERVER.md).
        Economy.LinkInventory(Inventory);
    }

    /// <summary>
    /// 매 프레임 부른다. <see cref="MockInputSource"/> 의 100ms 배치와 초당 캡
    /// 창, <see cref="MockEconomyService"/> 의 나무 슬롯 성장 시계를 굴린다.
    ///
    /// <b>실물에는 이 호출이 없다.</b> 실물의 폴링은 셸이 돌리고, 계약
    /// (<see cref="IInputSource"/>)에는 틱이 아예 없다. 그래서 이 메서드는
    /// <see cref="IPlatformServices"/> 가 아니라 이 구체 타입에만 있다 —
    /// 게임 레이어가 목일 때만 틱을 돌리게 강제하는 것이 목적이다.
    /// </summary>
    public void Tick(double delta)
    {
        Input.Tick(delta);
        Economy.Tick(delta);
    }

    /// <summary>
    /// 타건이 왔다고 흉내 낸다. 실물(A4)은 포커스 없이 전역으로 받으므로
    /// 이 호출에 해당하는 것이 없다.
    /// </summary>
    public void Feed(int count = 1) => Input.Feed(count);
}

/// <summary>
/// "스팀에 못 붙었다"고만 답하는 <see cref="IAchievements"/> 구현.
///
/// 목이 아니라 **실행 경로에 들어가는 널 오브젝트**다. 무인 측정처럼 스팀을
/// 일부러 안 붙이는 실행에서 <see cref="IPlatformServices.Achievements"/> 자리를
/// 채운다 — 널을 넘기지 않겠다는 약속(<see cref="IPlatformServices"/> 주석)을
/// 지키는 쪽이 게임 레이어의 널 검사보다 싸다.
///
/// 계약이 이미 "<c>IsAvailable</c> 이 false 면 <see cref="IAchievements.Unlock"/> 은
/// 조용히 버리고 <see cref="IAchievements.IsUnlocked"/> 는 항상 false" 라고
/// 못 박아 뒀으므로, 이 구현은 그 문장을 그대로 코드로 옮긴 것뿐이다.
/// </summary>
public sealed class UnavailableAchievements : IAchievements
{
    public bool IsAvailable => false;

    public void Unlock(string id)
    {
    }

    public bool IsUnlocked(string id) => false;

    public void IndicateProgress(string id, int current, int max)
    {
    }
}
