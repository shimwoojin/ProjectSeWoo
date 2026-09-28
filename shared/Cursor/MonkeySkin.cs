using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 원숭이 스킨 한 벌 — 머리 그림 + 몸을 그릴 색 (docs/B17-CURSOR-REWORK.md §4-2).
/// <c>assets/cursor/monkey/&lt;id&gt;/</c> 의 <c>head.png</c> · <c>skin.json</c> 을 읽는다. 둘 다
/// <c>tools/make-monkey-parts.py</c> 가 만든다 — 머리만 그림이고 나머지는 <see cref="MonkeyRig"/> 가 그린다.
/// </summary>
public sealed class MonkeySkin
{
    private static readonly Dictionary<string, MonkeySkin> Cache = new(StringComparer.Ordinal);

    public string Id { get; private init; }

    public Texture2D Head { get; private init; }

    /// <summary>
    /// 머리 자체의 폭(px). 모자(B19)가 머리 밖으로 나가면 그림이 넓어지는데, 리그는 이 폭을 기준으로 줄여서 모자를 써도 머리
    /// 크기가 같다. skin.json 에 없으면 그림 폭.
    /// </summary>
    public float HeadWidthPx { get; private init; }

    /// <summary>머리 그림 안의 목(회전 중심), 픽셀.</summary>
    public Vector2 Neck { get; private init; }

    /// <summary>머리 그림 안의 두 눈 가운데, 픽셀. 표정(감은 눈·웃는 눈)을 덧그리는 자리.</summary>
    public Vector2[] Eyes { get; private init; } = Array.Empty<Vector2>();

    public Color Fur { get; private init; } = new("#9e6034");

    public Color FurShade { get; private init; } = new("#7a4624");

    public Color Belly { get; private init; } = new("#facda5");

    public Color Outline { get; private init; } = new("#3e2012");

    /// <summary>권투 글러브 (B19). 주먹이 위, 손목이 아래인 그림. 없으면 맨손.</summary>
    public Texture2D Glove { get; private init; }

    /// <summary>꼬리 줄무늬 색 (B19). 없으면 털 한 색.</summary>
    public Color? TailStripe { get; private init; }

    /// <summary>꼬리 끝 장식 (B19 - 리본·별·하트 등). 없으면 없음.</summary>
    public Texture2D TailTip { get; private init; }

    /// <summary>B19 부위 그림 폴더. 스킨마다 다시 그리지 않고 여기서 같이 쓴다 (tools/monkey_skins.py).</summary>
    private const string PartsDir = "res://assets/cursor/parts/";

    /// <summary>스킨을 읽는다. 없거나 깨졌으면 null — 호출부는 기본 원숭이로 떨어진다.</summary>
    public static MonkeySkin Load(string id)
    {
        if (id == null || !ItemManifest.IdPattern.IsMatch(id))
        {
            return null;
        }

        if (Cache.TryGetValue(id, out MonkeySkin cached))
        {
            return cached;
        }

        string dir = ItemManifest.AssetDir(CursorSlot.Monkey, id);
        MonkeySkin skin = null;
        try
        {
            using FileAccess file = FileAccess.Open(dir + "skin.json", FileAccess.ModeFlags.Read);
            if (file != null && ResourceLoader.Exists(dir + "head.png"))
            {
                using JsonDocument doc = JsonDocument.Parse(file.GetAsText());
                JsonElement head = doc.RootElement.GetProperty("head");
                JsonElement colors = doc.RootElement.GetProperty("colors");
                var eyes = new List<Vector2>();
                foreach (JsonElement e in head.GetProperty("eyes").EnumerateArray())
                {
                    eyes.Add(Vec(e));
                }

                skin = new MonkeySkin
                {
                    Id = id,
                    Head = GD.Load<Texture2D>(dir + "head.png"),
                    HeadWidthPx = head.TryGetProperty("headWidth", out JsonElement hw) ? hw.GetSingle() : 0,
                    Neck = Vec(head.GetProperty("neck")),
                    Eyes = eyes.ToArray(),
                    Fur = new Color(colors.GetProperty("fur").GetString()),
                    FurShade = new Color(colors.GetProperty("furShade").GetString()),
                    Belly = new Color(colors.GetProperty("belly").GetString()),
                    Outline = new Color(colors.GetProperty("outline").GetString()),
                    Glove = PartTexture(doc.RootElement, "glove", "glove/"),
                    TailStripe = doc.RootElement.TryGetProperty("tail", out JsonElement tail)
                                 && tail.TryGetProperty("stripe", out JsonElement stripe) && stripe.ValueKind == JsonValueKind.String
                        ? new Color(stripe.GetString())
                        : null,
                    TailTip = doc.RootElement.TryGetProperty("tail", out JsonElement tail2) ? PartTexture(tail2, "tip", "tail/") : null,
                };
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[cursor] 원숭이 스킨 {id} 읽기 실패 - {e.GetType().Name}: {e.Message}");
        }

        Cache[id] = skin;
        return skin;
    }

    private static Vector2 Vec(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle());

    /// <summary>
    /// skin.json 의 부위 이름 -> 그림. 이름도 id 규칙으로 거른다 - 경로에 들어간다. 그림이 없으면(가져오기 전) null 이고
    /// 원숭이는 그 부위 없이 그려진다.
    /// </summary>
    private static Texture2D PartTexture(JsonElement owner, string key, string folder)
    {
        if (!owner.TryGetProperty(key, out JsonElement e) || e.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string name = e.GetString();
        string path = PartsDir + folder + name + ".png";
        return name != null && ItemManifest.IdPattern.IsMatch(name) && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }
}
