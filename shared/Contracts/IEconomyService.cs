using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 서버 권위 경제 — 바나나 잔액, 나무 슬롯의 실제 성장 상태, 강화 레벨
/// (docs/ECONOMY-SERVER.md). 플랫폼 구현이 제공하고 게임 레이어가 소비한다.
///
/// <b>왜 이게 생겼는가.</b> 바나나로 산 커서 장식을 스팀 인벤토리에 담아
/// 커뮤니티 마켓에서 거래되게 하기로 하면서, 바나나 잔액을 로컬 세이브 파일에
/// 둘 수 없게 됐다. 세이브 파일은 유저가 손으로 고칠 수 있고, 고친 바나나로
/// 산 장식이 마켓에서 실제 돈으로 거래되면 그건 위조 화폐다. <b>그래서 잔액의
/// 진실은 서버에 있고, 로컬 값은 전부 "서버가 마지막으로 확인해 준 값 + 아직
/// 확인 안 된 예측"이다.</b>
///
/// <b>나무 슬롯도 같은 이유로 여기 들어왔다.</b> 슬롯이 언제 열리는지(성장
/// 타이머)를 클라이언트만 알면, 서버는 "지금 하베스트가 진짜 열려 있었는지"를
/// 검증할 방법이 없다 — 결국 잔액 위조와 같은 구멍이다. 그래서 <see cref="Slots"/>
/// 는 서버가 계산한 값의 미러이고, 클라이언트의 나무 애니메이션은 이 값을
/// 그리는 쪽이지 원본이 아니다.
///
/// <b>강화(§5)도 같은 원장을 쓴다.</b> 강화는 스팀 아이템이 아니라 서버 원장의
/// 숫자 레벨이지만, 슬롯 수·성장 주기·펀치당 수확량에 직접 영향을 주므로 이
/// 값이 로컬에만 있으면 위와 같은 구멍이 그대로 남는다. 그래서 강화 구매도
/// <see cref="PurchaseUpgrade"/> 로 이 인터페이스를 거친다.
///
/// <b>커서 장식(스팀 아이템)의 소유권은 여기 없다.</b> 그 진실은 스팀 인벤토리
/// 서비스이지 우리 서버가 아니다 — <see cref="IInventoryService"/> 를 본다. 다만
/// 아이템을 "사는" 행위는 바나나를 쓰는 것이므로 <see cref="PurchaseItem"/> 은
/// 여기 있다 (서버가 잔액을 깎고 스팀에 지급을 요청하는 한 트랜잭션).
///
/// <b>낙관적 갱신 + 서버 정정.</b> <see cref="RequestHarvest"/> 는 결과를 기다리지
/// 않는다 — 즉각 피드백이 이 게임의 P0 이므로 펀치 애니메이션과 카운터 증가는
/// 로컬에서 바로 일어나야 한다. 서버가 나중에 그 수확을 거부하면(시계 조작 등)
/// <see cref="OnStateChanged"/> 가 정정된 값으로 다시 불린다. 정직한 플레이에서는
/// 정정이 일어날 일이 없다 — 로컬 성장 계산이 서버와 같은 공식을 쓰기 때문이다.
/// </summary>
public interface IEconomyService
{
    /// <summary>
    /// 서버에 붙어 있고 최근에 상태를 확인했는가. false 면 모든 잔액/슬롯 값은
    /// "마지막으로 알던 값"이고, 구매 계열 메서드는
    /// <see cref="PurchaseOutcome.ServerUnavailable"/> 로 즉시 실패한다. 하베스트는
    /// 오프라인에서도 로컬 예측을 계속 쌓고, 재접속 시 <see cref="Sync"/> 가 정산한다.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>보유 바나나. 서버 확인값 + 아직 확인 안 된 로컬 예측의 합.</summary>
    long Balance { get; }

    /// <summary>나무 슬롯의 서버 기준 성장 상태. 인덱스는 슬롯 번호와 같다.</summary>
    IReadOnlyList<SlotState> Slots { get; }

    /// <summary>강화 3축의 현재 레벨 (§5).</summary>
    int UpgradeLevel(UpgradeAxis axis);

    /// <summary>
    /// 잔액, 슬롯, 강화 레벨 중 하나라도 바뀌면 불린다 — 로컬 예측이든 서버
    /// 정정이든 구분 없이 한 이벤트로 합친다. HUD 는 분기 없이 다시 그리면 된다.
    /// </summary>
    event Action OnStateChanged;

    /// <summary>
    /// 슬롯을 지금 수확한다. 로컬에서 즉시 <see cref="Slots"/> 를 비우고
    /// <see cref="Balance"/> 를 올린 뒤(낙관적 갱신), 백그라운드로 서버에 확인
    /// 요청을 보낸다. 결과를 기다릴 필요가 없는 것이 계약이다 — 펀치 연출을
    /// 막지 않는다.
    /// </summary>
    void RequestHarvest(int slotIndex);

    /// <summary>
    /// 바나나로 스팀 아이템을 산다. 서버가 잔액을 깎고 스팀 인벤토리 서비스에
    /// 지급을 요청하는 것까지 한 트랜잭션이다 — 성공하면 곧이어
    /// <see cref="IInventoryService.OnItemsChanged"/> 도 불린다.
    /// </summary>
    Task<PurchaseResult> PurchaseItem(string itemDefId);

    /// <summary>강화 레벨을 하나 올린다. 스팀 아이템이 아니라 서버 원장의 숫자다.</summary>
    Task<PurchaseResult> PurchaseUpgrade(UpgradeAxis axis);

    /// <summary>
    /// 서버와 전체 상태를 맞춘다. 재접속 직후, 또는 오프라인 예측이 오래 쌓였을
    /// 때 부른다. 로컬 예측과 서버 값이 갈리면 서버가 이긴다.
    /// </summary>
    Task Sync();
}

/// <summary>
/// 나무 슬롯 하나의 서버 기준 성장 상태.
/// </summary>
/// <param name="ElapsedMs">이번 성장 주기에서 지금까지 지난 시간(ms).</param>
/// <param name="GrowthMs">이 슬롯이 다 자라는 데 걸리는 시간(ms). 강화로 짧아진다 (§5).</param>
public readonly record struct SlotState(long ElapsedMs, long GrowthMs)
{
    /// <summary>바나나가 열려서 수확 가능한가.</summary>
    public bool Ready => ElapsedMs >= GrowthMs;
}

/// <summary>강화 3축 (§5). 세 축 다 같은 바나나 원장을 쓴다.</summary>
public enum UpgradeAxis
{
    /// <summary>펀치 1회당 수확량 +1씩.</summary>
    Power,

    /// <summary>나무 주기 단축.</summary>
    Cycle,

    /// <summary>나무 슬롯 확장. 사실상 오프라인 저장고 확장.</summary>
    Slots,
}

/// <summary>구매(아이템/강화) 결과.</summary>
public enum PurchaseOutcome
{
    Success,
    InsufficientBalance,
    ItemUnknown,
    AlreadyOwned,
    ServerUnavailable,

    /// <summary>서버가 거부했지만 사유가 위 항목에 안 맞는 경우 (레이트리밋 등).</summary>
    Rejected,
}

/// <param name="NewBalance">성공/실패 무관하게 이 호출 직후의 최신 잔액.</param>
/// <param name="GrantedItemDefId">
/// <see cref="PurchaseOutcome.Success"/> 이고 아이템 구매일 때만 값이 있다.
/// </param>
public readonly record struct PurchaseResult(
    PurchaseOutcome Outcome, long NewBalance, string GrantedItemDefId = null);
