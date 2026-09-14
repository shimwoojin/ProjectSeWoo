using System;
using System.Text.Json.Serialization;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 세이브 스키마 v1 (기획확정-일감분배-260907.md §7-5).
///
/// **JSON 필드 이름이 계약이다.** 기획서에 실린 예시와 한 글자라도 다르면 안 되므로
/// 프로퍼티마다 <see cref="JsonPropertyNameAttribute"/> 를 명시한다. 직렬화 옵션의
/// 명명 규칙(camelCase)에 기대지 않는 이유는, 옵션은 호출부에서 빠뜨릴 수 있지만
/// 어트리뷰트는 타입에 붙어 다니기 때문이다.
///
/// 필드를 바꾸려면 <see cref="SaveSchema.CurrentVersion"/> 을 올리고 마이그레이션을
/// 추가한다. 상대에게 알리지 않고 바꾸지 않는다 (§8-2 규칙).
/// </summary>
public sealed class SaveData
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = SaveSchema.CurrentVersion;

    /// <summary>보유 바나나. 재화는 이것 하나뿐이다 (§3-2).</summary>
    [JsonPropertyName("bananas")]
    public long Bananas { get; set; }

    /// <summary>누적 타수. 재화가 아니라 기록이다 (§6).</summary>
    [JsonPropertyName("totalKeystrokes")]
    public long TotalKeystrokes { get; set; }

    [JsonPropertyName("tree")]
    public TreeState Tree { get; set; } = new();

    [JsonPropertyName("upgrades")]
    public UpgradeState Upgrades { get; set; } = new();

    [JsonPropertyName("inventory")]
    public InventoryState Inventory { get; set; } = new();

    [JsonPropertyName("settings")]
    public SettingsState Settings { get; set; } = new();

    /// <summary>
    /// 마지막 종료 시각(UTC). 오프라인 성장 계산의 기준점이다 (§2-2).
    ///
    /// 슬롯이 상한이라 아무리 오래 비워도 포화까지만 찬다. 그래서 이 값이
    /// 조작돼도 무한 파밍이 안 된다 — 경제가 시간이 아니라 **슬롯 수**에 묶여 있다.
    /// </summary>
    [JsonPropertyName("lastQuitUtc")]
    public DateTime LastQuitUtc { get; set; } = DateTime.UnixEpoch;

    public sealed class TreeState
    {
        /// <summary>나무 슬롯 수. 기본 3, 강화로 최대 8 (§2-2, §5).</summary>
        [JsonPropertyName("slots")]
        public int Slots { get; set; } = 3;

        /// <summary>슬롯당 바나나 성장 주기(ms). 기본 8분, 강화로 최소 3분 (§2-2).</summary>
        [JsonPropertyName("growthMs")]
        public long GrowthMs { get; set; } = 480_000;

        /// <summary>슬롯별 경과 시간(ms). 길이는 <see cref="Slots"/> 와 같아야 한다.</summary>
        [JsonPropertyName("slotTimers")]
        public long[] SlotTimers { get; set; } = { 0, 0, 0 };
    }

    /// <summary>
    /// 강화 3축 (§5). W3 에 시간이 남으면 넣고 안 남으면 1.1 로 뺀다.
    ///
    /// **기능을 안 넣어도 이 필드는 v1 에 둔다.** 나중에 붙일 때 스키마 버전을
    /// 올리지 않아도 되고, W1 의 수치가 이미 강화가 붙는다는 전제로 설계돼 있다.
    /// </summary>
    public sealed class UpgradeState
    {
        /// <summary>원숭이 파워. 펀치 1회당 수확량 +1씩.</summary>
        [JsonPropertyName("power")]
        public int Power { get; set; }

        /// <summary>나무 주기 단축 레벨.</summary>
        [JsonPropertyName("cycle")]
        public int Cycle { get; set; }

        /// <summary>나무 슬롯 확장 레벨. 사실상 오프라인 저장고 확장이다.</summary>
        [JsonPropertyName("slots")]
        public int Slots { get; set; }
    }

    public sealed class InventoryState
    {
        /// <summary>구매한 커서 장식 에셋 id 목록.</summary>
        [JsonPropertyName("owned")]
        public string[] Owned { get; set; } = { "monkey_01" };

        [JsonPropertyName("equipped")]
        public EquippedState Equipped { get; set; } = new();
    }

    /// <summary>
    /// 장착 상태. 키 이름은 <see cref="CursorSlot"/> 을 소문자로 쓴 것이다.
    /// null 은 빈 슬롯이다.
    /// </summary>
    public sealed class EquippedState
    {
        [JsonPropertyName("hang")]
        public string Hang { get; set; } = "monkey_01";

        [JsonPropertyName("trail")]
        public string Trail { get; set; }

        [JsonPropertyName("base")]
        public string Base { get; set; }
    }

    /// <summary>옵션 화면의 저장 대상 (§7-4).</summary>
    public sealed class SettingsState
    {
        [JsonPropertyName("scale")]
        public float Scale { get; set; } = 1.0f;

        [JsonPropertyName("opacity")]
        public float Opacity { get; set; } = 1.0f;

        /// <summary>창 위치 [x, y]. 모니터가 사라진 경우의 폴백은 플랫폼 레이어가 한다.</summary>
        [JsonPropertyName("pos")]
        public int[] Pos { get; set; } = { 0, 0 };

        [JsonPropertyName("sound")]
        public bool Sound { get; set; } = true;

        [JsonPropertyName("autostart")]
        public bool Autostart { get; set; }
    }
}
