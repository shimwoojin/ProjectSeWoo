using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Shared.Mocks;

/// <summary>
/// <see cref="IInventoryService"/> 목 구현. 스팀 없이 소유권을 메모리에서
/// 굴린다 (docs/ECONOMY-SERVER.md).
///
/// 장착 상태는 이 클래스에 없다 - 계약대로 로컬(<see cref="SaveData.EquippedState"/>)
/// 이 관리한다. 소유권은 <see cref="MockGrant"/> 로만 늘어난다 - 실물에서 그
/// 자리를 채우는 것이 <see cref="IEconomyService.PurchaseItem"/> 성공 결과이므로,
/// 목도 같은 경로로만 늘어나야 시험이 실물과 같은 그림이 된다
/// (<see cref="MockEconomyService.LinkInventory"/>).
/// </summary>
public sealed class MockInventoryService : IInventoryService
{
    private readonly HashSet<string> _owned = new(StringComparer.Ordinal);

    public MockInventoryService()
    {
    }

    /// <summary>기본 지급품이 있으면 생성 시 넣어 둔다. 기존 <c>ShopCatalog.StarterId</c>
    /// 처럼 "처음부터 하나는 가지고 있다"는 게임 쪽 전제를 목에서도 재현하려는
    /// 용도다 - 호출부가 필요할 때만 쓴다.</summary>
    public void SeedOwned(string itemDefId)
    {
        if (_owned.Add(itemDefId))
        {
            OnItemsChanged?.Invoke();
        }
    }

    /// <summary>목이 항상 붙어 있다고 답한다. 스팀 오프라인 경로를 시험하려면 꺼 본다.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>목은 처음부터 보유 목록을 안다. 스팀 미조회 경로를 시험하려면 꺼 본다.</summary>
    public bool IsLoaded { get; set; } = true;

    public IReadOnlyList<InventoryItem> Items => _owned
        .Select(id => new InventoryItem(id, SteamItemInstanceId: 0, Tradable: true, Marketable: false))
        .ToList();

    public event Action OnItemsChanged;

    public bool Owns(string itemDefId) => itemDefId != null && _owned.Contains(itemDefId);

    public Task Refresh() => Task.CompletedTask;

    /// <summary>
    /// 스팀의 <c>AddItem</c> 지급을 흉내 낸다. <see cref="MockEconomyService"/> 가
    /// 구매 성공 시 이걸 부른다 - 게임 레이어가 직접 부르지 않는다 (계약대로
    /// 구매는 <see cref="IEconomyService"/> 쪽 일이다).
    /// </summary>
    public void MockGrant(string itemDefId)
    {
        if (_owned.Add(itemDefId))
        {
            OnItemsChanged?.Invoke();
        }
    }

    /// <summary>[디버그] 보유 목록을 통째로 비운다. <c>game/shop/Inventory.cs</c> 의
    /// Shift+R 전용 - 실물 스팀 인벤토리에는 대응하는 API 가 없다.</summary>
    public void DebugClear()
    {
        if (_owned.Count == 0)
        {
            return;
        }

        _owned.Clear();
        OnItemsChanged?.Invoke();
    }
}
