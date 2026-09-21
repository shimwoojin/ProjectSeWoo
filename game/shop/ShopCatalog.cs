using System;
using System.Collections.Generic;
using System.Linq;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 상점 상품표 (§3-2 · §2-2 성장 커브).
///
/// <b>전 상품 상시 노출이고 랜덤 뽑기가 없다</b> - 기획서가 "1달짜리 게임에 확률
/// UI를 붙일 이유가 없고, 유저가 '다음 목표'를 눈으로 볼 수 있어야 커브가 작동한다"
/// 고 못 박았다. 그래서 이 표는 통째로 화면에 뿌려지고, 못 사는 것도 가격과 함께
/// 보인다.
///
/// <b>가격은 §2-2 의 5티어를 그대로 쓴다.</b> 티어당 시간 기준이 문서에 적혀 있어서
/// (T1 30분 ~ T5 3일) 숫자를 여기서 새로 정하면 그 근거와 끊긴다. 밸런싱(B14)은
/// <see cref="TierPrices"/> 한 줄만 고치면 된다.
///
/// <b>id 는 에셋 파일명과 같다.</b> `res://assets/cursor/{slot}/{id}.png` 로 바로
/// 이어지고(A5 `CursorLayer.ResolveTexture`), 세이브의 `inventory.owned` 에도 이
/// 문자열이 그대로 들어간다. 표시 이름만 사람 말이다.
/// </summary>
public static class ShopCatalog
{
    /// <summary>§2-2 성장 커브. 인덱스가 곧 티어-1 이다.</summary>
    public static readonly int[] TierPrices = { 10, 40, 150, 500, 1500 };

    /// <summary>
    /// 첫 실행에 이미 가진 것. 세이브 스키마의 <c>inventory.owned</c> 기본값과
    /// 같아야 한다 (shared/Save/SaveData.cs) - **가격이 없는 유일한 상품이다.**
    /// 빈손으로 시작하면 커서 꾸미기가 뭔지 보여줄 방법이 없다.
    /// </summary>
    public const string StarterId = "monkey_01";

    public sealed record Item(string Id, CursorSlot Slot, int Tier, string Name)
    {
        /// <summary>0 이면 기본 지급품이라 살 수 없다.</summary>
        public int Price => Tier == 0 ? 0 : TierPrices[Tier - 1];

        public bool IsStarter => Tier == 0;
    }

    /// <summary>
    /// 16종. 슬롯별 물량은 §3-1 표대로 6 / 6 / 4 다.
    ///
    /// 티어는 슬롯을 가로질러 고르게 폈다 - 한 슬롯을 싸게 몰아 두면 그 슬롯만
    /// 먼저 채우고 나머지는 한참 빈 채로 남는다. 티어마다 세 슬롯이 하나씩 있어야
    /// "다음에 뭘 살까" 가 매 구간에 생긴다.
    /// </summary>
    public static readonly Item[] All =
    {
        // Hang - 커서에 매달려 흔들리는 원숭이 (6)
        new(StarterId,   CursorSlot.Hang,  0, "갈색 원숭이"),
        new("monkey_02", CursorSlot.Hang,  1, "노랑 원숭이"),
        new("monkey_03", CursorSlot.Hang,  2, "회색 원숭이"),
        new("monkey_04", CursorSlot.Hang,  3, "보라 원숭이"),
        new("monkey_05", CursorSlot.Hang,  4, "빨간 모자 원숭이"),
        new("monkey_06", CursorSlot.Hang,  5, "오랑우탄"),

        // Trail - 커서를 따라오는 잔상 (6)
        new("leaf_01",   CursorSlot.Trail, 1, "초록 잎"),
        new("chunk_01",  CursorSlot.Trail, 2, "과일 조각"),
        new("leaf_02",   CursorSlot.Trail, 3, "단풍잎"),
        new("chunk_02",  CursorSlot.Trail, 4, "바나나"),
        new("spark_01",  CursorSlot.Trail, 3, "노란 반짝임"),
        new("spark_02",  CursorSlot.Trail, 5, "푸른 반짝임"),

        // Base - 커서 뒤에 깔리는 장식 (4)
        new("halo_01",   CursorSlot.Base,  1, "금빛 고리"),
        new("ring_01",   CursorSlot.Base,  2, "잎 화환"),
        new("halo_02",   CursorSlot.Base,  4, "연잎"),
        new("ring_02",   CursorSlot.Base,  5, "나무 단면"),
    };

    private static readonly Dictionary<string, Item> ById =
        All.ToDictionary(i => i.Id, StringComparer.Ordinal);

    public static Item Find(string id) =>
        id != null && ById.TryGetValue(id, out Item item) ? item : null;

    public static IEnumerable<Item> ForSlot(CursorSlot slot) =>
        All.Where(i => i.Slot == slot);

    /// <summary>도감 수집률 (§3-3). 0.0 ~ 1.0.</summary>
    public static float CollectionRate(ICollection<string> owned) =>
        All.Length == 0 ? 0f : (float)All.Count(i => owned.Contains(i.Id)) / All.Length;

    /// <summary>슬롯 하나의 수집 수 (B7 도감의 슬롯별 진행도).</summary>
    public static int OwnedInSlot(CursorSlot slot, ICollection<string> owned) =>
        All.Count(i => i.Slot == slot && owned.Contains(i.Id));

    public static int CountInSlot(CursorSlot slot) => All.Count(i => i.Slot == slot);

    /// <summary>슬롯의 사람 말 이름. 도감·탭 라벨이 같은 문자열을 봐야 한다.</summary>
    public static string SlotName(CursorSlot slot) => slot switch
    {
        CursorSlot.Hang => "매달림",
        CursorSlot.Trail => "잔상",
        CursorSlot.Base => "바닥",
        _ => slot.ToString(),
    };
}
