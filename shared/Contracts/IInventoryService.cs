using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 스팀 인벤토리 서비스가 들고 있는 커서 장식 소유권 (docs/ECONOMY-SERVER.md).
/// 플랫폼 구현이 제공하고 게임 레이어가 소비한다.
///
/// <b>소유권의 진실은 스팀이다.</b> 아이템을 스팀 인벤토리 서비스로 옮기는
/// 이유가 스팀 커뮤니티 마켓 거래이므로, "가지고 있다"는 사실 자체가 밸브
/// 서버에 있어야 한다 — 우리 세이브 파일이 그 사실을 따로 들고 있으면 두
/// 진실이 어긋날 수 있다 (마켓에서 팔았는데 우리 세이브엔 남아있는 경우).
///
/// <b>장착은 여기 없다.</b> 스팀은 "장착"이라는 개념을 모른다 — 그건 우리
/// 게임 안에서만 의미가 있는 로컬 UX 상태다(<see cref="SaveData.EquippedState"/>).
/// 이 인터페이스는 소유권 조회만 하고, "가진 것 중에 골라 끼운다"를 합치는
/// 자리는 게임 레이어(<c>game/shop/Inventory.cs</c>)다 - <see cref="IShell"/>이
/// 설정 저장을 몰라도 되는 것과 같은 경계로, 이 인터페이스는 어느 세이브
/// 필드가 장착을 담는지 알 필요가 없다.
///
/// <b>구매는 여기 없다.</b> 바나나를 쓰는 행위이므로
/// <see cref="IEconomyService.PurchaseItem"/> 이 하고, 성공하면 그 결과로
/// <see cref="OnItemsChanged"/> 가 불린다. 이 인터페이스는 "지금 뭘 가지고
/// 있나"를 읽고 캐시를 새로고침하는 것까지만 한다.
/// </summary>
public interface IInventoryService
{
    /// <summary>
    /// 스팀 인벤토리 서비스에 붙어 있는가. false 면 <see cref="Items"/> 는
    /// 마지막으로 알던 스냅샷이고, 오프라인/스팀 미실행 환경의 표시용이다.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>마지막으로 확인한 보유 아이템 스냅샷.</summary>
    IReadOnlyList<InventoryItem> Items { get; }

    event Action OnItemsChanged;

    bool Owns(string itemDefId);

    /// <summary>
    /// 스팀에서 보유 목록을 다시 받아온다. 구매 직후, 또는 다른 기기에서 마켓
    /// 거래가 있었을 수 있는 재접속 시 부른다.
    /// </summary>
    Task Refresh();
}

/// <summary>스팀 인벤토리 서비스가 돌려주는 아이템 인스턴스 하나.</summary>
/// <param name="ItemDefId">
/// 파트너 사이트에 등록한 아이템 정의 id (기존 <c>ShopCatalog.Item.Id</c>와 대응).
/// </param>
/// <param name="SteamItemInstanceId">
/// 스팀이 매긴 개별 인스턴스 id. 마켓 거래 내역 조회에 쓴다.
/// </param>
/// <param name="Tradable">
/// 지금 다른 유저에게 거래 가능한가 (스팀은 신규 지급 아이템에 거래 유예를 걸 수 있다).
/// </param>
/// <param name="Marketable">
/// 지금 커뮤니티 마켓에 올릴 수 있는가. 밸브 승인 전에는 항상 false
/// (docs/ECONOMY-SERVER.md §4).
/// </param>
public readonly record struct InventoryItem(
    string ItemDefId, ulong SteamItemInstanceId, bool Tradable, bool Marketable);
