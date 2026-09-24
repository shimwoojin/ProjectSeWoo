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

    /// <summary>누적 타수. 재화가 아니라 기록이다 (§6).</summary>
    [JsonPropertyName("totalKeystrokes")]
    public long TotalKeystrokes { get; set; }

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

    /// <summary>
    /// 커서 장식의 **로컬** 상태 - 어느 슬롯에 무엇을 끼웠는가. 소유권(어떤
    /// 장식을 "가지고" 있는가)은 v4(2026-09-23)부터 여기 없다 -
    /// docs/ECONOMY-SERVER.md 피벗으로 스팀 인벤토리 서비스가 그 진실을
    /// 대신 들고 있다(<see cref="IInventoryService"/>). 장착은 스팀이 모르는
    /// 개념이라 계속 로컬에 남는다.
    /// </summary>
    public sealed class InventoryState
    {
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

    /// <summary>
    /// 옵션 화면의 저장 대상 (§7-4).
    ///
    /// v2(A6, 2026-09-16)에서 5개 필드를 추가했다 - v1에는 §7-4가 요구하는 옵션 중
    /// 위치 잠금/알림/커서 장식/전체화면 위 숨김/타수 카운트가 빠져 있었다.
    /// 전부 additive라 기본값이 자동으로 채워지지만, 버전 태그는 올려야 한다
    /// (<see cref="SaveSchema.Migrate"/> 참고).
    ///
    /// v3(2026-09-16)에서 <see cref="CursorIndependent"/> 하나를 더 추가했다.
    /// 역시 additive다.
    /// </summary>
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

        /// <summary>
        /// 몸통 드래그로 창을 옮길 수 있는가를 반대로 뒤집은 값이다.
        /// 켜져 있으면(기본값) 클릭 통과가 걸려 실수로 안 끌린다 (§7-1).
        /// </summary>
        [JsonPropertyName("positionLocked")]
        public bool PositionLocked { get; set; } = true;

        /// <summary>알림 On/Off. 소비할 알림 기능은 아직 없다 - 옵션만 먼저 자리를 잡아둔다.</summary>
        [JsonPropertyName("notifications")]
        public bool Notifications { get; set; } = true;

        /// <summary>커서 장식(§3-1) On/Off. 끄면 재화 소비처를 다른 것으로 안내해야 한다 (§1.3).</summary>
        [JsonPropertyName("cursorEnabled")]
        public bool CursorEnabled { get; set; } = true;

        /// <summary>
        /// 전체화면으로 실행 중인 다른 앱 위에서 셸을 자동으로 숨긴다 (§7-1).
        ///
        /// <b>기본은 꺼짐이다 (2026-09-24).</b> 판정이 휴리스틱이라(docs/A6-TRAY-OPTIONS.md §3)
        /// 전체화면이 아닌 창에서도 숨는 오판이 나면 "켜 놨는데 사라졌다" 가 된다. 원하는
        /// 사람만 옵션 창에서 켠다. 꺼져 있으면 전체화면 검사 자체를 안 한다.
        /// 필드가 이미 저장된 세이브는 그 값을 그대로 쓴다 - 기본값만 바뀐다.
        /// </summary>
        [JsonPropertyName("hideOnFullscreen")]
        public bool HideOnFullscreen { get; set; }

        /// <summary>
        /// 셸 몸통이 숨겨져도 커서 장식(§3-1)은 남긴다.
        ///
        /// 기본값은 false — 지금까지의 동작("숨기면 커서도 같이 사라진다")이 그대로
        /// 기본이어야 한다. 켜면 트레이 숨기기와 전체화면 자동 숨김 **둘 다**에서
        /// 커서만 살아남는다. 숨김 이유를 둘로 갈라 옵션을 두 개 만들지 않은 것은
        /// 의도적이다 — 표시 여부를 한 줄(<c>visible = userWantsVisible &amp;&amp;
        /// !autoHiddenForFullscreen</c>)로 합쳐 둔 것이 A6의 상태 꼬임 방지책이고,
        /// 이유별 예외를 만들면 그게 깨진다.
        ///
        /// <see cref="CursorEnabled"/>가 꺼져 있으면 이 값과 무관하게 커서는 안 나온다.
        /// </summary>
        [JsonPropertyName("cursorIndependent")]
        public bool CursorIndependent { get; set; }

        /// <summary>
        /// 타건 수 집계 On/Off. 끄면 <see cref="IInputSource"/>가 사용 가능해도
        /// 게임 레이어는 시간 기반 폴백만 쓴다 - 유저가 원하면 타건 카운트 자체를
        /// 거부할 수 있어야 한다 (§7-2, §7-6 개인정보 문구와 짝을 이룬다).
        /// </summary>
        [JsonPropertyName("keystrokeCounting")]
        public bool KeystrokeCounting { get; set; } = true;
    }
}
