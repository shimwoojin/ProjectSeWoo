using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 커서 장식 아이템 목록 — <c>res://game/shop/items.json</c> 을 읽는다 (docs/B17-CURSOR-REWORK.md §2).
///
/// <b>아이템의 유일한 원본이 그 JSON 이다.</b> 예전에는 게임 <c>ShopCatalog</c>, 서버 <c>catalog.ts</c>,
/// <c>SteamInventoryService</c> 의 id↔itemdefid 표, 스팀 <c>itemdefs.json</c> 네 곳에 손으로 맞춰 적었다 —
/// 장식이 업데이트로 계속 늘어나므로 한 곳으로 모았다. 서버는 같은 JSON 을 번들에 넣고, itemdefs 는
/// <c>tools/make-itemdefs.py</c> 가 만든다.
///
/// 게임(<c>ShopCatalog</c>)과 플랫폼(<c>SteamInventoryService</c>)이 둘 다 읽어야 해서 <c>shared/</c> 에 둔다 —
/// 플랫폼은 게임 타입을 모른다.
///
/// 그림은 규칙으로 찾는다: <see cref="AssetDir"/> 폴더의 <c>icon.png</c>(+ 분류별 리소스).
/// </summary>
public static class ItemManifest
{
    public const string ResPath = "res://game/shop/items.json";

    /// <summary>
    /// 아이템 id 규칙. <see cref="PlayerStateCodec"/> 이 받는 id 와 같다 — 이 값이 에셋 경로에 들어가므로
    /// <c>../</c> 같은 것을 형식에서 막는다.
    /// </summary>
    public static readonly Regex IdPattern = new("^[a-z0-9_]{1,32}$", RegexOptions.Compiled);

    public sealed record Entry(
        string Id,
        CursorSlot Slot,
        int Tier,
        int? SteamItemDefId,
        string NameKo,
        string NameEn,
        string DecoKind)
    {
        /// <summary>tier 0 = 기본 지급품. 가격이 없고 스팀 인벤토리에도 없다.</summary>
        public bool IsStarter => Tier == 0;
    }

    private static int[] _tierPrices = Array.Empty<int>();
    private static Entry[] _items = Array.Empty<Entry>();
    private static string _loadError;

    static ItemManifest() => Load();

    /// <summary>티어별 가격. 인덱스 = tier - 1.</summary>
    public static IReadOnlyList<int> TierPrices => _tierPrices;

    /// <summary>JSON 순서 그대로. 상점·도감이 이 순서로 그린다.</summary>
    public static IReadOnlyList<Entry> Items => _items;

    /// <summary>읽기에 실패했으면 이유, 아니면 null. <c>--selftest</c> 와 기동 로그가 본다.</summary>
    public static string LoadError => _loadError;

    public static string CategoryName(CursorSlot slot) => slot switch
    {
        CursorSlot.Monkey => "monkey",
        CursorSlot.Banana => "banana",
        CursorSlot.Deco => "deco",
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    /// <summary>아이템 한 개의 에셋 폴더. <c>icon.png</c> 는 모든 아이템에 있다.</summary>
    public static string AssetDir(CursorSlot slot, string id) => $"res://assets/cursor/{CategoryName(slot)}/{id}/";

    public static string IconPath(CursorSlot slot, string id) => AssetDir(slot, id) + "icon.png";

    private static void Load()
    {
        try
        {
            using FileAccess file = FileAccess.Open(ResPath, FileAccess.ModeFlags.Read);
            if (file == null)
            {
                Fail($"{ResPath} 을 못 열었다 ({FileAccess.GetOpenError()}) - 내보내기 include_filter 확인");
                return;
            }

            Parse(file.GetAsText());
        }
        catch (Exception e)
        {
            Fail($"{ResPath} 읽기 실패 - {e.GetType().Name}: {e.Message}");
        }
    }

    private static void Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        JsonElement root = doc.RootElement;

        _tierPrices = root.GetProperty("tierPrices").EnumerateArray().Select(e => e.GetInt32()).ToArray();

        var items = new List<Entry>();
        foreach (JsonElement e in root.GetProperty("items").EnumerateArray())
        {
            string category = e.GetProperty("category").GetString();
            CursorSlot? slot = category switch
            {
                "monkey" => CursorSlot.Monkey,
                "banana" => CursorSlot.Banana,
                "deco" => CursorSlot.Deco,
                _ => null,
            };

            string id = e.GetProperty("id").GetString();
            if (slot == null)
            {
                GD.PrintErr($"[items] {id}: 모르는 분류 '{category}' - 건너뛴다");
                continue;
            }

            JsonElement name = e.GetProperty("name");
            items.Add(new Entry(
                id,
                slot.Value,
                e.GetProperty("tier").GetInt32(),
                e.TryGetProperty("steamItemDefId", out JsonElement steam) ? steam.GetInt32() : null,
                name.GetProperty("ko").GetString(),
                name.GetProperty("en").GetString(),
                e.TryGetProperty("deco", out JsonElement deco) && deco.TryGetProperty("kind", out JsonElement kind)
                    ? kind.GetString()
                    : null));
        }

        _items = items.ToArray();
        _loadError = Validate();
        if (_loadError != null)
        {
            GD.PrintErr($"[items] {_loadError}");
        }
    }

    private static void Fail(string why)
    {
        _loadError = why;
        GD.PrintErr($"[items] {why}");
    }

    /// <summary>
    /// 목록이 규칙에 맞는지. 틀린 채로 출시되면 상점·구매·스팀 지급이 조용히 어긋난다 — <c>--selftest</c> 가 부른다.
    /// </summary>
    /// <returns>문제가 없으면 null.</returns>
    public static string Validate()
    {
        if (_items.Length == 0)
        {
            return "아이템이 하나도 없다";
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var steamIds = new HashSet<int>();
        foreach (Entry item in _items)
        {
            if (!IdPattern.IsMatch(item.Id ?? ""))
            {
                return $"id '{item.Id}' 가 규칙([a-z0-9_] 1~32)에 안 맞는다";
            }

            if (!ids.Add(item.Id))
            {
                return $"id '{item.Id}' 가 두 번 나온다";
            }

            if (item.Tier < 0 || item.Tier > _tierPrices.Length)
            {
                return $"{item.Id}: tier {item.Tier} 에 가격이 없다";
            }

            if (item.IsStarter != (item.SteamItemDefId == null))
            {
                return $"{item.Id}: 기본 지급품(tier 0)만 steamItemDefId 가 없어야 한다";
            }

            if (item.SteamItemDefId is int steam && !steamIds.Add(steam))
            {
                return $"steamItemDefId {steam} 가 두 번 나온다";
            }

            if (item.Slot == CursorSlot.Deco && item.DecoKind is not ("halo" or "trail" or "float"))
            {
                return $"{item.Id}: 장식 종류(deco.kind)가 halo/trail/float 가 아니다";
            }
        }

        // 원숭이와 바나나는 비어 있을 수 없다 - 바나나가 원숭이의 손잡이다 (B17 §4-1).
        foreach (CursorSlot slot in new[] { CursorSlot.Monkey, CursorSlot.Banana })
        {
            if (_items.Count(i => i.Slot == slot && i.IsStarter) != 1)
            {
                return $"{CategoryName(slot)} 기본 지급품이 정확히 하나여야 한다";
            }
        }

        return null;
    }

    /// <summary>분류의 기본 지급품 id. 장식은 없다(null) — 빈 칸으로 시작한다.</summary>
    public static string StarterFor(CursorSlot slot) =>
        _items.FirstOrDefault(i => i.Slot == slot && i.IsStarter)?.Id;
}
