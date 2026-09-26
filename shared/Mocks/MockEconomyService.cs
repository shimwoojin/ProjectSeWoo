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
/// <b>강화는 서버와 같은 표(<see cref="UpgradeTable"/>)를 쓴다</b> (B13) — 슬롯 수·성장 시간·
/// 황금 확률·가격이 서버(<c>server/src/catalog.ts</c>)와 같은 그림이어야 목으로 한 시험이 의미가
/// 있다. 황금 여부는 서버처럼 송이가 자라기 시작할 때 굴린다.
/// </summary>
public sealed class MockEconomyService : IEconomyService
{
    private readonly List<SlotState> _slots = new();
    private readonly Dictionary<UpgradeAxis, int> _upgradeLevels = new();
    private readonly Random _rng = new();

    /// <summary>아이템 구매 가격표. 시험 코드가 <see cref="RegisterItemPrice"/> 로 채운다.</summary>
    private readonly Dictionary<string, long> _itemPrices = new(StringComparer.Ordinal);

    private readonly HashSet<string> _owned = new(StringComparer.Ordinal);

    /// <summary>구매 성공 시 스팀 인벤토리 쪽에 지급을 흉내 내기 위한 연결점.
    /// 실제로는 서버가 잔액 차감과 AddItem 호출을 한 트랜잭션으로 묶지만,
    /// 목에서는 두 목 객체를 이렇게 연결해서 같은 그림을 재현한다.</summary>
    private MockInventoryService _inventory;

    public MockEconomyService()
    {
        foreach (UpgradeAxis axis in Enum.GetValues<UpgradeAxis>())
        {
            _upgradeLevels[axis] = 0;
        }

        ResizeSlots();
    }

    /// <summary>슬롯 수를 강화 단계에 맞춘다. 새 슬롯은 0 부터 자라고 황금 여부를 새로 굴린다.</summary>
    private void ResizeSlots()
    {
        int count = UpgradeTable.SlotsAt(_upgradeLevels[UpgradeAxis.Slots]);
        long growth = UpgradeTable.GrowthMsAt(_upgradeLevels[UpgradeAxis.Cycle]);
        while (_slots.Count < count)
        {
            _slots.Add(new SlotState(0, growth, RollGolden()));
        }
    }

    /// <summary>새 송이가 황금인가. 서버의 rollGolden 과 같은 규칙.</summary>
    private bool RollGolden() =>
        _rng.Next(100) < UpgradeTable.GoldenChanceAt(_upgradeLevels[UpgradeAxis.Golden]);

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

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Ready)
            {
                continue;
            }

            var next = _slots[i] with { ElapsedMs = Math.Min(_slots[i].GrowthMs, _slots[i].ElapsedMs + deltaMs) };
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
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i] = _slots[i] with { ElapsedMs = Math.Min(_slots[i].GrowthMs, _slots[i].ElapsedMs + ms) };
        }

        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// [디버그] 슬롯 하나를 정해진 모습으로 둔다 - 성장 비율(0~1)과 황금 여부. 스토어 스크린샷
    /// (<c>--store-shot</c>)이 "익은 송이 + 황금 송이 + 자라는 중" 을 매번 같게 찍으려고 쓴다.
    /// 황금은 원래 굴림이라 이게 없으면 찍을 때마다 나무가 달라진다.
    /// </summary>
    public void DebugSetSlot(int index, double grown, bool golden)
    {
        if (index < 0 || index >= _slots.Count)
        {
            return;
        }

        long growth = _slots[index].GrowthMs;
        _slots[index] = new SlotState((long)(growth * Math.Clamp(grown, 0.0, 1.0)), growth, golden);
        OnStateChanged?.Invoke();
    }

    public void RequestHarvest(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Count || !_slots[slotIndex].Ready)
        {
            return;
        }

        Balance += _slots[slotIndex].Yield;
        _slots[slotIndex] = new SlotState(0, _slots[slotIndex].GrowthMs, RollGolden());
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

        long? price = UpgradeTable.NextPrice(axis, _upgradeLevels[axis]);
        if (price == null)
        {
            return Task.FromResult(new PurchaseResult(PurchaseOutcome.MaxLevel, Balance));
        }

        if (Balance < price.Value)
        {
            return Task.FromResult(new PurchaseResult(PurchaseOutcome.InsufficientBalance, Balance));
        }

        Balance -= price.Value;
        _upgradeLevels[axis]++;

        if (axis == UpgradeAxis.Cycle)
        {
            // 자라던 시간은 그대로 두고 목표만 줄인다 - 이미 넘었으면 곧바로 익는다(서버와 같다).
            long growth = UpgradeTable.GrowthMsAt(_upgradeLevels[axis]);
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i] = _slots[i] with { ElapsedMs = Math.Min(growth, _slots[i].ElapsedMs), GrowthMs = growth };
            }
        }

        ResizeSlots();

        GD.Print($"[mock-economy] 강화 {axis} → Lv.{_upgradeLevels[axis]} (-{price}) 잔액 {Balance}");
        OnStateChanged?.Invoke();
        return Task.FromResult(new PurchaseResult(PurchaseOutcome.Success, Balance));
    }

    public Task Sync() => Task.CompletedTask;
}
