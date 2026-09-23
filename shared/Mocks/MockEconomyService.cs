using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Shared.Mocks;

/// <summary>
/// <see cref="IEconomyService"/> 목 구현. 실제 서버 없이 잔액·나무 슬롯·강화
/// 레벨을 메모리에서 굴린다 (docs/ECONOMY-SERVER.md).
///
/// 실물은 아직 없다 — 백엔드가 만들어지기 전까지 <c>platform/</c> 이 이 목을
/// 그대로 <see cref="IPlatformServices.Economy"/> 자리에 꽂아 둔다. 게임
/// 레이어는 그 사실을 몰라도 된다 (다른 목들과 같은 규칙).
///
/// <b>여기서 나는 잔액/가격 숫자는 실제 밸런스가 아니다.</b> 서버가 정하는
/// 값이고 (docs/ECONOMY-SERVER-API.md), 이 목은 "구매·수확 흐름이 끊기지
/// 않는가"를 시험하는 용도다. 슬롯 기본값(3슬롯 · 8분)만 기획서 §2-2 수치를
/// 그대로 맞췄다 - 기존 <c>SaveData.TreeState</c> 기본값과 같다.
/// </summary>
public sealed class MockEconomyService : IEconomyService
{
    private readonly SlotState[] _slots;
    private readonly Dictionary<UpgradeAxis, int> _upgradeLevels = new();

    /// <summary>아이템 구매 가격표. 시험 코드가 <see cref="RegisterItemPrice"/> 로 채운다.</summary>
    private readonly Dictionary<string, long> _itemPrices = new(StringComparer.Ordinal);

    private readonly HashSet<string> _owned = new(StringComparer.Ordinal);

    /// <summary>구매 성공 시 스팀 인벤토리 쪽에 지급을 흉내 내기 위한 연결점.
    /// 실제로는 서버가 잔액 차감과 AddItem 호출을 한 트랜잭션으로 묶지만,
    /// 목에서는 두 목 객체를 이렇게 연결해서 같은 그림을 재현한다.</summary>
    private MockInventoryService _inventory;

    public MockEconomyService()
    {
        _slots = new SlotState[3];
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new SlotState(0, 480_000);
        }

        foreach (UpgradeAxis axis in Enum.GetValues<UpgradeAxis>())
        {
            _upgradeLevels[axis] = 0;
        }
    }

    /// <summary>목이 항상 붙어 있다고 답한다. 서버 단절 경로를 시험하려면 꺼 본다.</summary>
    public bool IsAvailable { get; set; } = true;

    public long Balance { get; private set; }

    public IReadOnlyList<SlotState> Slots => _slots;

    public event Action OnStateChanged;

    public int UpgradeLevel(UpgradeAxis axis) => _upgradeLevels[axis];

    /// <summary>이 목을 <see cref="MockInventoryService"/> 와 묶는다. 구매 성공 시
    /// 그쪽에 지급을 흉내 낸다 - 두 서비스가 실제로는 한 트랜잭션인 것을 재현한다.</summary>
    public void LinkInventory(MockInventoryService inventory) => _inventory = inventory;

    /// <summary>아이템 가격을 등록한다. 등록 안 된 id 는 <see cref="PurchaseOutcome.ItemUnknown"/>.</summary>
    public void RegisterItemPrice(string itemDefId, long price) => _itemPrices[itemDefId] = price;

    /// <summary>바나나를 얹는다. 시험 초기화용 - 실물이면 오직 수확/서버 보정으로만 는다.</summary>
    public void GrantBananas(long amount)
    {
        Balance += amount;
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// 서버 클럭을 흉내 낸다. 실물이라면 요청이 올 때마다 서버가 경과 시간으로
    /// 재계산하는 것을, 목에서는 씬의 <c>_Process</c> 가 매 프레임 불러서 재현한다.
    /// </summary>
    public void Tick(double deltaSeconds)
    {
        long deltaMs = (long)(deltaSeconds * 1000.0);
        bool becameReady = false;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Ready)
            {
                continue;
            }

            var next = new SlotState(_slots[i].ElapsedMs + deltaMs, _slots[i].GrowthMs);
            _slots[i] = next;
            becameReady |= next.Ready;
        }

        if (becameReady)
        {
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// [디버그] 모든 슬롯의 성장을 <paramref name="ms"/> 만큼 앞당긴다. 8분을
    /// 기다리지 않고 수확까지 확인하려는 용도 - 기존 <c>Tree.DebugAdvance</c>가
    /// 하던 일이 슬롯 성장의 진실이 서버(목)로 넘어오면서 여기로 옮겨왔다.
    /// 호출부(<c>GameRoot</c>)가 <see cref="OS.IsDebugBuild"/>로 막고, 실물
    /// 서버가 붙으면 이 메서드 자체가 없으므로 자연히 막힌다.
    /// </summary>
    public void DebugAdvanceAll(long ms)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new SlotState(Math.Min(_slots[i].GrowthMs, _slots[i].ElapsedMs + ms), _slots[i].GrowthMs);
        }

        OnStateChanged?.Invoke();
    }

    public void RequestHarvest(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length || !_slots[slotIndex].Ready)
        {
            return;
        }

        _slots[slotIndex] = new SlotState(0, _slots[slotIndex].GrowthMs);
        Balance += 1 + UpgradeLevel(UpgradeAxis.Power);
        OnStateChanged?.Invoke();
    }

    public Task<PurchaseResult> PurchaseItem(string itemDefId)
    {
        if (!IsAvailable)
        {
            return Task.FromResult(new PurchaseResult(PurchaseOutcome.ServerUnavailable, Balance));
        }

        if (!_itemPrices.TryGetValue(itemDefId, out long price))
        {
            return Task.FromResult(new PurchaseResult(PurchaseOutcome.ItemUnknown, Balance));
        }

        if (_owned.Contains(itemDefId))
        {
            return Task.FromResult(new PurchaseResult(PurchaseOutcome.AlreadyOwned, Balance));
        }

        if (Balance < price)
        {
            return Task.FromResult(new PurchaseResult(PurchaseOutcome.InsufficientBalance, Balance));
        }

        Balance -= price;
        _owned.Add(itemDefId);
        _inventory?.MockGrant(itemDefId);
        OnStateChanged?.Invoke();

        GD.Print($"[mock-economy] 구매 {itemDefId} (-{price}) 잔액 {Balance}");
        return Task.FromResult(new PurchaseResult(PurchaseOutcome.Success, Balance, itemDefId));
    }

    public Task<PurchaseResult> PurchaseUpgrade(UpgradeAxis axis)
    {
        if (!IsAvailable)
        {
            return Task.FromResult(new PurchaseResult(PurchaseOutcome.ServerUnavailable, Balance));
        }

        // 임의 가격 곡선. 실제 밸런스는 서버가 정한다 (docs/ECONOMY-SERVER.md §2).
        long price = 50L * (_upgradeLevels[axis] + 1);
        if (Balance < price)
        {
            return Task.FromResult(new PurchaseResult(PurchaseOutcome.InsufficientBalance, Balance));
        }

        Balance -= price;
        _upgradeLevels[axis]++;

        if (axis == UpgradeAxis.Cycle)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                long shortened = Math.Max(180_000, _slots[i].GrowthMs - 30_000);
                _slots[i] = new SlotState(_slots[i].ElapsedMs, shortened);
            }
        }

        // UpgradeAxis.Slots 는 레벨만 오르고 슬롯 배열은 안 늘어난다 - 목은 구매
        // 흐름을 시험하는 용도라 배열 크기 변경까지는 재현하지 않는다. 실물
        // 서버는 슬롯 수만큼 SlotState 목록 길이를 바꿔서 응답한다.
        OnStateChanged?.Invoke();
        return Task.FromResult(new PurchaseResult(PurchaseOutcome.Success, Balance));
    }

    public Task Sync() => Task.CompletedTask;
}
