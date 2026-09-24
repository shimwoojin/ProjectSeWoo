using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Shared.Mocks;

namespace ProjectSeWoo.Game;

/// <summary>
/// 구매와 장착 규칙 (§3-2). <b>화면이 아니라 규칙만 안다</b> - UI 는
/// <see cref="ShopWindow"/> 이고, 이 클래스는 서버/스팀에 요청을 보내고 커서
/// 레이어에 알리는 것까지만 한다.
///
/// <b>2026-09-23, docs/ECONOMY-SERVER.md 피벗.</b> 예전에는 <c>SaveData</c> 를
/// 직접 고쳤다 - 바나나도, 보유 목록도 전부 로컬 세이브 필드였다. 이제 잔액과
/// 나무 슬롯의 진실은 <see cref="IEconomyService"/>(서버 원장)에, 커서 장식
/// 소유권의 진실은 <see cref="IInventoryService"/>(스팀 인벤토리)에 있다.
/// 이 클래스에 남은 로컬 상태는 **장착**(<see cref="SaveData.EquippedState"/>)
/// 하나뿐이다 - 스팀은 "장착"을 모르는 개념이라 여기 남는다.
///
/// <b>기본 지급품(<see cref="ShopCatalog.StarterId"/>)은 스팀 인벤토리에 없다.</b>
/// 값이 0바나나라 마켓 거래 대상이 될 이유가 없고, 그래서 굳이 스팀에 그랜트를
/// 걸지 않는다 - "가지고 있다"의 진실을 물을 필요 없이 <see cref="Owns"/> 가
/// 항상 참으로 답한다.
/// </summary>
public sealed class Inventory
{
    private readonly SaveData.EquippedState _equipped;
    private readonly IEconomyService _economy;
    private readonly IInventoryService _steamInventory;
    private readonly ICursorLayer _cursor;

    public Inventory(
        SaveData.EquippedState equipped,
        IEconomyService economy,
        IInventoryService steamInventory,
        ICursorLayer cursor)
    {
        _equipped = equipped ?? throw new ArgumentNullException(nameof(equipped));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _steamInventory = steamInventory ?? throw new ArgumentNullException(nameof(steamInventory));
        _cursor = cursor;
    }

    public long Bananas => _economy.Balance;

    /// <summary>
    /// 지금 서버에 붙어 있어 구매가 되는가 (docs/ECONOMY-SERVER.md §7 "구매만 온라인 필수").
    /// 목은 항상 true 다.
    /// </summary>
    public bool Online => _economy.IsAvailable;

    /// <summary>기본 지급품은 항상 가진 것으로 친다(위 클래스 주석). 그 외에는
    /// 스팀 인벤토리 서비스가 답한다.</summary>
    public bool Owns(string id)
    {
        ShopCatalog.Item item = ShopCatalog.Find(id);
        return item != null && (item.IsStarter || _steamInventory.Owns(id));
    }

    public float CollectionRate =>
        ShopCatalog.All.Length == 0 ? 0f : (float)OwnedCount / ShopCatalog.All.Length;

    public int OwnedCount => ShopCatalog.All.Count(i => Owns(i.Id));

    /// <summary>도감이 슬롯별 진행도를 그릴 때 쓴다 (B7).</summary>
    public int OwnedInSlot(CursorSlot slot) => ShopCatalog.ForSlot(slot).Count(i => Owns(i.Id));

    /// <summary>16종을 전부 모았는가 (§3-3, <see cref="AchievementIds.Collection100"/>).</summary>
    public bool IsComplete => ShopCatalog.All.All(i => Owns(i.Id));

    public string EquippedIn(CursorSlot slot) => slot switch
    {
        CursorSlot.Hang => _equipped.Hang,
        CursorSlot.Trail => _equipped.Trail,
        CursorSlot.Base => _equipped.Base,
        _ => null,
    };

    /// <summary>살 수 있는가. 이미 가진 것과 기본 지급품은 false 다.
    /// <b>UI 힌트용이다</b> - 진짜 판정은 서버가 <see cref="TryBuy"/> 안에서 다시 한다.</summary>
    public bool CanBuy(ShopCatalog.Item item) =>
        item != null && !item.IsStarter && !Owns(item.Id) && _economy.Balance >= item.Price;

    /// <summary>
    /// 구매. 서버가 잔액을 깎고 스팀 인벤토리에 지급하는 것까지 한 트랜잭션으로
    /// 처리한다 (docs/ECONOMY-SERVER-API.md §2-3) - 그래서 <b>비동기다</b>.
    /// 성공하면 <see cref="IInventoryService.OnItemsChanged"/> 가 뒤따라 불려서
    /// <see cref="Owns"/> 가 곧바로 참이 된다.
    /// </summary>
    /// <returns>결과. 실패 사유를 화면에 보여 주려고 성공 여부만이 아니라 그대로 돌려준다.</returns>
    public async Task<PurchaseOutcome> TryBuy(ShopCatalog.Item item)
    {
        if (item == null || item.IsStarter || Owns(item.Id))
        {
            return PurchaseOutcome.AlreadyOwned;
        }

        if (_economy.Balance < item.Price)
        {
            return PurchaseOutcome.InsufficientBalance;
        }

        PurchaseResult result = await _economy.PurchaseItem(item.Id);
        return result.Outcome;
    }

    /// <summary>
    /// 슬롯에 끼운다. <paramref name="id"/> 가 null 이면 비운다. 로컬
    /// (<see cref="SaveData.EquippedState"/>)에 적고 커서 레이어에 민다 -
    /// 스팀에는 아무것도 쓰지 않는다(위 클래스 주석).
    ///
    /// <b>안 가진 것은 못 끼운다.</b> UI 가 막고 있지만 세이브 파일을 손으로 고친
    /// 경우가 남는다 - 그때 조용히 끼워 주면 상점을 건너뛰는 경로가 생긴다.
    /// </summary>
    public bool Equip(CursorSlot slot, string id)
    {
        if (id != null)
        {
            ShopCatalog.Item item = ShopCatalog.Find(id);
            if (item == null || item.Slot != slot || !Owns(id))
            {
                return false;
            }
        }

        switch (slot)
        {
            case CursorSlot.Hang: _equipped.Hang = id; break;
            case CursorSlot.Trail: _equipped.Trail = id; break;
            case CursorSlot.Base: _equipped.Base = id; break;
            default: return false;
        }

        _cursor?.Equip(slot, id);
        return true;
    }

    /// <summary>
    /// 세이브에 적힌 장착 상태를 커서 레이어에 한 번에 밀어 넣는다. 켤 때 한 번
    /// 부른다 - <see cref="IInventoryService"/> 가 아직 첫 <see cref="IInventoryService.Refresh"/>
    /// 전이면(스팀 콜백이 늦게 올 수 있다) 안 가진 것으로 오판해 슬롯을 비울 수
    /// 있다는 점은 알려진 한계다 - 실물 붙일 때 <c>Refresh</c> 완료를 기다린
    /// 뒤 이 메서드를 부르도록 호출 순서를 맞출 것.
    /// </summary>
    public void ApplyEquippedToCursor()
    {
        foreach (CursorSlot slot in Enum.GetValues<CursorSlot>())
        {
            string id = EquippedIn(slot);
            if (id == null)
            {
                _cursor?.Equip(slot, null);
                continue;
            }

            // **보유 목록을 아직 못 받았으면 세이브를 믿는다.** 스팀 없이(자동 시작이
            // 스팀보다 먼저) 뜨면 산 장식이 전부 "안 가진 것" 으로 보여서, 아래 경로가
            // 세이브의 장착을 비우고 그대로 저장했다 - 켤 때마다 장착이 풀렸다. 목록이
            // 오면 GameRoot 가 이 메서드를 다시 불러 진짜로 확인한다.
            if (!_steamInventory.IsLoaded)
            {
                ShopCatalog.Item item = ShopCatalog.Find(id);
                _cursor?.Equip(slot, item != null && item.Slot == slot ? id : null);
                continue;
            }

            if (!Equip(slot, id))
            {
                GD.PushWarning($"[game] 장착 복원 실패 - {slot} = {id} (안 가졌거나 표에 없다). 비운다");
                Equip(slot, null);
            }
        }
    }

    /// <summary>
    /// 슬롯의 장착을 한 칸 돌린다 - "비움 → 가진 것들 → 다시 비움" 순서다.
    /// <b>가진 것만 돈다</b> - 안 가진 것을 끼울 길을 열면 상점을 건너뛰는 경로가 된다.
    /// </summary>
    /// <returns>새로 장착된 id. 빈 슬롯이면 null.</returns>
    public string CycleEquipped(CursorSlot slot)
    {
        var options = new List<string> { null };
        options.AddRange(ShopCatalog.ForSlot(slot).Where(i => Owns(i.Id)).Select(i => i.Id));

        // 지금 낀 것이 목록에 없으면(손으로 고친 세이브) IndexOf 가 -1 이라
        // 다음이 0번(비움)이 된다 - 그게 안전한 쪽이다.
        int next = (options.IndexOf(EquippedIn(slot)) + 1) % options.Count;

        Equip(slot, options[next]);
        return options[next];
    }

    /// <summary>
    /// [디버그] 값을 안 치르고 넣는다. <see cref="GameRoot"/> 의 Shift+B 전용이고,
    /// 릴리스 빌드에서는 호출부가 아예 안 돈다.
    ///
    /// <b>실물 스팀 인벤토리에는 이 경로가 없다.</b> 클라이언트가 스스로에게
    /// 아이템을 지급하는 길을 여는 것은 정확히 docs/ECONOMY-SERVER-API.md §4 가
    /// 막으려는 구멍이다 - 그래서 <see cref="MockInventoryService"/> 일 때만
    /// 동작하고, 그 외에는 조용히 실패한다(경고만 남긴다).
    /// </summary>
    public bool DebugGrant(string id)
    {
        if (ShopCatalog.Find(id) == null || Owns(id))
        {
            return false;
        }

        if (_steamInventory is not MockInventoryService mock)
        {
            GD.PushWarning("[game][디버그] 실물 스팀 인벤토리에는 DebugGrant 가 없다 - 목일 때만 동작한다");
            return false;
        }

        mock.MockGrant(id);
        return true;
    }

    /// <summary>
    /// [디버그] 첫 실행 상태로 되돌린다. 구매 흐름과 도감 100% 발화를 다시
    /// 시험하려면 되돌릴 방법이 있어야 한다. 위 <see cref="DebugGrant"/> 와 같은
    /// 이유로 목일 때만 동작한다.
    ///
    /// <b>장착도 같이 푼다.</b> 안 그러면 안 가진 것이 끼워진 채로 남고, 다음
    /// 실행에서 <see cref="ApplyEquippedToCursor"/> 가 그걸 경고로 뱉는다.
    /// </summary>
    public void DebugResetToStarter()
    {
        if (_steamInventory is MockInventoryService mock)
        {
            mock.DebugClear();
        }
        else
        {
            GD.PushWarning("[game][디버그] 실물 스팀 인벤토리에는 DebugResetToStarter 가 없다");
        }

        foreach (CursorSlot slot in Enum.GetValues<CursorSlot>())
        {
            if (!Owns(EquippedIn(slot)))
            {
                Equip(slot, null);
            }
        }
    }
}
