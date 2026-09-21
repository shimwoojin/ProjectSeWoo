using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 구매와 장착 규칙 (§3-2). <b>화면이 아니라 규칙만 안다</b> - UI 는
/// <see cref="ShopWindow"/> 이고, 이 클래스는 세이브를 고치고 커서 레이어에
/// 알리는 것까지만 한다.
///
/// <b>세이브 객체를 직접 고친다.</b> 디스크에 언제 쓸지는 플랫폼이 정하므로
/// (<see cref="ISaveStore"/>) 여기서는 <c>MarkDirty</c> 도 안 부른다 - 호출부인
/// <see cref="GameRoot"/> 가 수확 때와 같은 자리에서 한 번에 처리한다.
///
/// <b>커서 레이어가 없어도 돈다.</b> <see cref="ICursorLayer.IsSupported"/> 가
/// false 인 환경(A2 스파이크가 실패한 PC)에서도 구매·장착 상태는 세이브에 남아야
/// 한다 - 나중에 되는 PC 에서 켜면 그대로 끼워져 있어야 하기 때문이다.
/// </summary>
public sealed class Inventory
{
    private readonly SaveData _save;
    private readonly ICursorLayer _cursor;

    /// <summary>
    /// 세이브의 <c>owned</c> 는 배열이라 추가할 때마다 새로 만들어야 한다.
    /// 중복 검사도 자주 하므로 세션 동안은 집합으로 들고 있다가, 바뀔 때만
    /// 배열로 되돌려 쓴다.
    /// </summary>
    private readonly HashSet<string> _owned;

    public Inventory(SaveData save, ICursorLayer cursor)
    {
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _cursor = cursor;

        _owned = new HashSet<string>(
            _save.Inventory.Owned ?? Array.Empty<string>(), StringComparer.Ordinal);

        // 기본 지급품이 빠진 세이브를 만나도 복구한다. 손으로 고친 파일이나
        // 예전 스키마에서 올라온 경우다 - 없으면 hang 슬롯을 영영 못 채운다.
        if (_owned.Add(ShopCatalog.StarterId))
        {
            WriteOwned();
        }
    }

    public long Bananas => _save.Bananas;

    public bool Owns(string id) => id != null && _owned.Contains(id);

    public float CollectionRate => ShopCatalog.CollectionRate(_owned);

    public int OwnedCount => _owned.Count;

    /// <summary>도감이 슬롯별 진행도를 그릴 때 쓴다 (B7).</summary>
    public int OwnedInSlot(CursorSlot slot) => ShopCatalog.OwnedInSlot(slot, _owned);

    /// <summary>16종을 전부 모았는가 (§3-3, <see cref="AchievementIds.Collection100"/>).</summary>
    public bool IsComplete => _owned.Count >= ShopCatalog.All.Length;

    public string EquippedIn(CursorSlot slot) => slot switch
    {
        CursorSlot.Hang => _save.Inventory.Equipped.Hang,
        CursorSlot.Trail => _save.Inventory.Equipped.Trail,
        CursorSlot.Base => _save.Inventory.Equipped.Base,
        _ => null,
    };

    /// <summary>살 수 있는가. 이미 가진 것과 기본 지급품은 false 다.</summary>
    public bool CanBuy(ShopCatalog.Item item) =>
        item != null && !item.IsStarter && !Owns(item.Id) && _save.Bananas >= item.Price;

    /// <summary>
    /// 구매. 성공하면 바나나가 줄고 인벤토리에 들어간다 (§3-2 "구매 즉시 인벤토리").
    ///
    /// <b>장착까지 하지는 않는다.</b> 기획서가 "구매 즉시 인벤토리에 들어가고,
    /// 장착 화면에서 슬롯에 끼운다" 로 두 단계를 나눠 뒀다 - 산 것이 곧바로
    /// 끼워지면 지금 끼운 것이 말없이 밀려난다.
    /// </summary>
    public bool TryBuy(ShopCatalog.Item item)
    {
        if (!CanBuy(item))
        {
            return false;
        }

        _save.Bananas -= item.Price;
        _owned.Add(item.Id);
        WriteOwned();
        return true;
    }

    /// <summary>
    /// 슬롯에 끼운다. <paramref name="id"/> 가 null 이면 비운다.
    ///
    /// <b>안 가진 것은 못 끼운다.</b> UI 가 막고 있지만 세이브 파일을 손으로 고친
    /// 경우가 남는다 - 그때 조용히 끼워 주면 상점을 거치지 않는 길이 생긴다.
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
            case CursorSlot.Hang: _save.Inventory.Equipped.Hang = id; break;
            case CursorSlot.Trail: _save.Inventory.Equipped.Trail = id; break;
            case CursorSlot.Base: _save.Inventory.Equipped.Base = id; break;
            default: return false;
        }

        _cursor?.Equip(slot, id);
        return true;
    }

    /// <summary>
    /// 세이브에 적힌 장착 상태를 커서 레이어에 한 번에 밀어 넣는다.
    ///
    /// <b>이게 없어서 B9 까지 장식이 화면에 안 나왔다.</b> <c>Equip</c> 을 부르는
    /// 곳이 디버그 키와 <c>--cursor-equip=</c> 뿐이라, 세이브에 <c>hang: monkey_01</c>
    /// 이 있어도 켜면 아무것도 안 붙었다 (docs/A5-CURSOR-COSMETICS.md §2-1).
    ///
    /// 못 끼우는 항목(안 가진 것, 지워진 에셋 id)은 **슬롯을 비우고 세이브도
    /// 고친다** - 그대로 두면 켤 때마다 같은 실패를 반복한다.
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

            if (!Equip(slot, id))
            {
                GD.PushWarning($"[game] 장착 복원 실패 - {slot} = {id} (안 가졌거나 표에 없다). 비운다");
                Equip(slot, null);
            }
        }
    }

    /// <summary>
    /// 슬롯의 장착을 한 칸 돌린다 - "비움 → 가진 것들 → 다시 비움" 순서다.
    ///
    /// 셸의 2/3/4 debug 키가 쓰던 자리를 B6 이 가져온 것이다(2026-09-21).
    /// 예전에는 <see cref="ICursorLayer.Equip"/> 을 직접 불러서 **세이브도
    /// 인벤토리도 모르는 채로 커서만 바뀌었다** - 커서 모양은 바뀌는데 상점에는
    /// 이전 것이 "장착 중" 으로 남아 있었다. 이제 <see cref="Equip"/> 을 거치므로
    /// 세이브·상점·커서가 같이 간다.
    ///
    /// <b>가진 것만 돈다.</b> 안 가진 것을 끼울 길을 debug 키로 열어 두면 그게
    /// 곧 상점을 건너뛰는 경로다.
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
    /// </summary>
    public bool DebugGrant(string id)
    {
        if (ShopCatalog.Find(id) == null || !_owned.Add(id))
        {
            return false;
        }

        WriteOwned();
        return true;
    }

    /// <summary>
    /// [디버그] 첫 실행 상태로 되돌린다. 구매 흐름과 도감 100% 발화를 다시
    /// 시험하려면 되돌릴 방법이 있어야 한다.
    ///
    /// <b>장착도 같이 푼다.</b> 안 그러면 안 가진 것이 끼워진 채로 남고, 다음
    /// 실행에서 <see cref="ApplyEquippedToCursor"/> 가 그걸 경고로 뱉는다.
    /// </summary>
    public void DebugResetToStarter()
    {
        _owned.Clear();
        _owned.Add(ShopCatalog.StarterId);
        WriteOwned();

        foreach (CursorSlot slot in Enum.GetValues<CursorSlot>())
        {
            if (!Owns(EquippedIn(slot)))
            {
                Equip(slot, null);
            }
        }
    }

    private void WriteOwned()
    {
        // 표 순서대로 저장한다. 세이브 파일을 눈으로 볼 때 순서가 매번 달라지면
        // diff 가 의미 없어진다 - HashSet 은 순서를 보장하지 않는다.
        _save.Inventory.Owned = ShopCatalog.All
            .Select(i => i.Id)
            .Where(_owned.Contains)
            .ToArray();
    }
}
