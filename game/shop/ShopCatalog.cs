using System;
using System.Collections.Generic;
using System.Linq;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 상점 상품표 (§3-2 · §2-2 성장 커브).
///
/// <b>목록의 원본은 <c>game/shop/items.json</c> 이다</b> (<see cref="ItemManifest"/>, docs/B17-CURSOR-REWORK.md §2).
/// 2026-09-26 전에는 이 파일에 배열로 적혀 있었고 서버·스팀 매핑·스팀 아이템 정의에 같은 목록이 세 벌 더
/// 있었다. 장식이 업데이트로 계속 늘어나므로 한 곳으로 모았다 — 새 장식은 그 JSON 한 줄 + 그림 폴더다.
/// 개수를 코드나 문구에 박지 않는다. 필요하면 <see cref="All"/> 에서 센다.
///
/// <b>전 상품 상시 노출이고 랜덤 뽑기가 없다</b> - 기획서가 "1달짜리 게임에 확률
/// UI를 붙일 이유가 없고, 유저가 '다음 목표'를 눈으로 볼 수 있어야 커브가 작동한다"
/// 고 못 박았다. 그래서 이 표는 통째로 화면에 뿌려지고, 못 사는 것도 가격과 함께
/// 보인다.
///
/// <b>가격은 §2-2 의 5티어를 그대로 쓴다</b> (JSON 의 <c>tierPrices</c>). 티어는 칸을 가로질러 고르게 편다 -
/// 한 칸을 싸게 몰아 두면 그 칸만 먼저 채우고 나머지는 한참 빈 채로 남는다.
///
/// <b>id 는 에셋 폴더 이름과 같다</b> (<see cref="ItemManifest.AssetDir"/>). 세이브의 장착에도 이 문자열이 그대로
/// 들어간다. 표시 이름만 사람 말이다.
/// </summary>
public static class ShopCatalog
{
    public sealed record Item(string Id, CursorSlot Slot, int Tier, string Name, string NameEn, string DecoKind)
    {
        /// <summary>0 이면 기본 지급품이라 살 수 없다.</summary>
        public int Price => Tier == 0 ? 0 : ItemManifest.TierPrices[Tier - 1];

        /// <summary>
        /// 처음부터 가진 것 (원숭이 하나, 바나나 하나). 빈손으로 시작하면 커서 꾸미기가 뭔지 보여줄 방법이 없고,
        /// 원숭이는 바나나가 있어야 매달린다.
        /// </summary>
        public bool IsStarter => Tier == 0;

        /// <summary>상점·도감에 그리는 아이콘.</summary>
        public string IconPath => ItemManifest.IconPath(Slot, Id);
    }

    /// <summary>JSON 순서 그대로.</summary>
    public static readonly Item[] All = ItemManifest.Items
        .Select(e => new Item(e.Id, e.Slot, e.Tier, e.NameKo, e.NameEn, e.DecoKind))
        .ToArray();

    private static readonly Dictionary<string, Item> ById =
        All.ToDictionary(i => i.Id, StringComparer.Ordinal);

    public static Item Find(string id) =>
        id != null && ById.TryGetValue(id, out Item item) ? item : null;

    public static IEnumerable<Item> ForSlot(CursorSlot slot) =>
        All.Where(i => i.Slot == slot);

    /// <summary>칸의 기본 지급품. 장식 칸은 없다(null).</summary>
    public static string StarterFor(CursorSlot slot) => ItemManifest.StarterFor(slot);

    /// <summary>도감 수집률 (§3-3). 0.0 ~ 1.0.</summary>
    public static float CollectionRate(ICollection<string> owned) =>
        All.Length == 0 ? 0f : (float)All.Count(i => owned.Contains(i.Id)) / All.Length;

    /// <summary>칸 하나의 수집 수 (B7 도감의 칸별 진행도).</summary>
    public static int OwnedInSlot(CursorSlot slot, ICollection<string> owned) =>
        All.Count(i => i.Slot == slot && owned.Contains(i.Id));

    public static int CountInSlot(CursorSlot slot) => All.Count(i => i.Slot == slot);

    /// <summary>칸의 사람 말 이름. 도감·탭 라벨이 같은 문자열을 봐야 한다.</summary>
    public static string SlotName(CursorSlot slot) => slot switch
    {
        CursorSlot.Monkey => "원숭이",
        CursorSlot.Banana => "바나나",
        CursorSlot.Deco => "장식",
        _ => slot.ToString(),
    };

    /// <summary>원숭이·바나나는 비울 수 없다 - 바나나가 원숭이의 손잡이고, 원숭이가 이 게임의 얼굴이다.</summary>
    public static bool CanBeEmpty(CursorSlot slot) => slot == CursorSlot.Deco;
}
