using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;
using Steamworks;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="IInventoryService"/> 실물 - 스팀 인벤토리 서비스를 클라이언트
/// SDK(<c>ISteamInventory</c>)로 직접 읽는다. 우리 경제 백엔드를 거치지 않는다
/// (docs/ECONOMY-SERVER-API.md §4) - **조회는 신뢰 문제가 없다**, 내가 가진 걸
/// 내가 보는 것뿐이라 서버를 한 번 더 왕복시킬 이유가 없다. 지급(쓰기)만
/// 서버(<see cref="EconomyClient"/> → 백엔드 → <c>AddItem</c>)가 한다 - 클라이언트가
/// 스스로에게 아이템을 지급하는 경로를 열면 그게 곧 화폐 위조 경로다.
///
/// <b>id ↔ 스팀 itemdefid 매핑이 이 클래스 안에 있다.</b> 우리 카탈로그
/// (<c>game/shop/ShopCatalog.cs</c>)는 문자열 id("monkey_02")를 쓰는데, 스팀
/// 아이템 정의는 정수(<see cref="SteamItemDef_t"/>)다. 이 매핑은 우리가 정하는
/// 값이라(스팀이 강제하는 게 아니다) <b>server/src/catalog.ts 의
/// <c>steamItemDefId</c> 및 스팀 파트너 사이트의 실제 아이템 정의 등록번호와
/// 세 곳이 정확히 같아야 한다</b> - 하나라도 어긋나면 조용히 다른 아이템으로
/// 잡히거나 아무것도 안 잡힌다.
/// </summary>
public sealed class SteamInventoryService : IInventoryService, IDisposable
{
    /// <summary>
    /// id → 스팀 itemdefid. <c>game/shop/ShopCatalog.All</c> 의 순서를 그대로
    /// 따랐다(기본 지급품 <c>monkey_01</c> 제외 - §0바나나짜리라 스팀 인벤토리에
    /// 안 넣는다, docs/ECONOMY-SERVER.md). <b>server/src/catalog.ts 와 손으로
    /// 맞춰야 한다</b> - 둘 다 고치는 자동화는 아직 없다.
    /// </summary>
    private static readonly (string Id, SteamItemDef_t SteamItemDefId)[] Catalog =
    {
        ("monkey_02", (SteamItemDef_t)1), ("monkey_03", (SteamItemDef_t)2),
        ("monkey_04", (SteamItemDef_t)3), ("monkey_05", (SteamItemDef_t)4),
        ("monkey_06", (SteamItemDef_t)5), ("leaf_01", (SteamItemDef_t)6),
        ("chunk_01", (SteamItemDef_t)7), ("leaf_02", (SteamItemDef_t)8),
        ("chunk_02", (SteamItemDef_t)9), ("spark_01", (SteamItemDef_t)10),
        ("spark_02", (SteamItemDef_t)11), ("halo_01", (SteamItemDef_t)12),
        ("ring_01", (SteamItemDef_t)13), ("halo_02", (SteamItemDef_t)14),
        ("ring_02", (SteamItemDef_t)15),
    };

    private static readonly Dictionary<SteamItemDef_t, string> SteamDefToId =
        Catalog.ToDictionary(c => c.SteamItemDefId, c => c.Id);

    private readonly SteamService _steam;

    /// <summary>콜백 핸들은 반드시 살려둬야 한다 - <see cref="SteamService"/> 의
    /// 같은 주석 참고. 지역 변수면 GC 가 걷어가고 그 순간부터 콜백이 안 온다.</summary>
    private readonly Callback<SteamInventoryResultReady_t> _resultReady;

    private TaskCompletionSource<bool> _pendingRefresh;
    private SteamInventoryResult_t _pendingHandle = SteamInventoryResult_t.Invalid;

    private InventoryItem[] _items = Array.Empty<InventoryItem>();

    public SteamInventoryService(SteamService steam)
    {
        _steam = steam;
        _resultReady = Callback<SteamInventoryResultReady_t>.Create(OnResultReady);
    }

    // 2026-09-23 실측 - `--real-economy`로 처음 돌려보니 GetAllItems 결과가
    // `k_EResultFail`이었다. 아이템 정의(itemdefs.json)를 같은 날 막 등록·게시한
    // 직후였다 - `docs/A8-STEAM.md`가 기록한 것과 같은 패턴이다(도전과제 스키마도
    // 등록 직후엔 통계 수신이 k_EResultFail이었다가 시간이 지나면 풀렸다). 코드
    // 쪽 문제는 아닌 것으로 보고, 전파를 좀 더 기다린 뒤 다시 확인한다.

    public bool IsAvailable => _steam is { IsInitialized: true };

    /// <summary><see cref="OnResultReady"/> 가 성공 결과를 한 번이라도 받았는가.</summary>
    public bool IsLoaded { get; private set; }

    public IReadOnlyList<InventoryItem> Items => _items;

    public event Action OnItemsChanged;

    public bool Owns(string itemDefId) =>
        itemDefId != null && Array.Exists(_items, i => string.Equals(i.ItemDefId, itemDefId, StringComparison.Ordinal));

    /// <summary>
    /// 스팀에서 보유 목록을 다시 받아온다. <see cref="Steamworks.SteamInventory.GetAllItems"/>
    /// 가 비동기 요청을 걸고, <see cref="SteamInventoryResultReady_t"/> 콜백이 오면
    /// <see cref="OnResultReady"/> 가 마무리한다 - 콜백 펌프(<see cref="SteamService.Tick"/>)
    /// 가 이미 매 프레임 돌고 있으므로 이 클래스는 따로 틱을 돌리지 않는다.
    /// </summary>
    public Task Refresh()
    {
        if (!IsAvailable)
        {
            return Task.CompletedTask;
        }

        // 이미 요청 중이면 새로 걸지 않고 그 결과를 같이 기다린다.
        if (_pendingRefresh != null)
        {
            return _pendingRefresh.Task;
        }

        if (!Steamworks.SteamInventory.GetAllItems(out SteamInventoryResult_t handle))
        {
            GD.PushWarning("[inventory] GetAllItems 호출 실패");
            return Task.CompletedTask;
        }

        _pendingHandle = handle;
        _pendingRefresh = new TaskCompletionSource<bool>();
        return _pendingRefresh.Task;
    }

    private void OnResultReady(SteamInventoryResultReady_t cb)
    {
        // 이 콜백은 핸들별로 안 걸러져서 온다 - 우리가 건 요청이 맞는지 직접 확인한다.
        if (_pendingRefresh == null || cb.m_handle != _pendingHandle)
        {
            return;
        }

        try
        {
            if (cb.m_result != EResult.k_EResultOK)
            {
                GD.PushWarning($"[inventory] 조회 실패 ({cb.m_result})");
                return;
            }

            uint count = 0;
            Steamworks.SteamInventory.GetResultItems(cb.m_handle, null, ref count);

            var raw = new SteamItemDetails_t[count];
            if (count > 0 && !Steamworks.SteamInventory.GetResultItems(cb.m_handle, raw, ref count))
            {
                GD.PushWarning("[inventory] GetResultItems 실패");
                return;
            }

            // 매핑에 없는 정의(우리 카탈로그 밖의 스팀 아이템)는 조용히 거른다 -
            // 나중에 다른 용도의 itemdef 가 같은 앱에 추가돼도 안전하다.
            _items = raw
                .Where(d => SteamDefToId.ContainsKey(d.m_iDefinition))
                .Select(d => new InventoryItem(
                    ItemDefId: SteamDefToId[d.m_iDefinition],
                    SteamItemInstanceId: d.m_itemId.m_SteamItemInstanceID,
                    // ⚠ 미검증 - k_ESteamItemNoTrade 비트가 실제로 이 자리에 오는지,
                    // Marketable 을 판정하려면 GetItemDefinitionProperty 로 아이템
                    // "정의"의 marketable 속성을 따로 읽어야 하는지는 스팀웍스
                    // 파트너 문서로 재확인해야 한다 (docs/ECONOMY-SERVER.md).
                    Tradable: (d.m_unFlags & (ushort)ESteamItemFlags.k_ESteamItemNoTrade) == 0,
                    Marketable: false))
                .ToArray();

            IsLoaded = true;
            OnItemsChanged?.Invoke();
        }
        finally
        {
            Steamworks.SteamInventory.DestroyResult(cb.m_handle);
            _pendingHandle = SteamInventoryResult_t.Invalid;
            TaskCompletionSource<bool> pending = _pendingRefresh;
            _pendingRefresh = null;
            pending?.TrySetResult(true);
        }
    }

    public void Dispose() => _resultReady?.Dispose();
}
