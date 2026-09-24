using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;
using Steamworks;
using HttpClient = System.Net.Http.HttpClient;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="IEconomyService"/> 실물 - 경제 백엔드(docs/ECONOMY-SERVER-API.md)와
/// 진짜 HTTPS 로 통신한다.
///
/// <b>2026-09-23, <c>OverlayShell</c> 에 실제로 꽂혔다.</b> <see cref="SteamInventoryService"/>
/// (스팀 인벤토리 실물)와 짝을 맞춰서 붙었다 - 잔액만 실물이고 소유권은 목이면
/// "서버에서 구매는 성공했는데 상점 화면은 여전히 안 가진 것으로 본다"는
/// 반쪽짜리 상태가 되기 때문에 (docs/ECONOMY-SERVER.md §5-6), 둘을 항상 같이
/// 켜고 같이 끈다. 릴리스 빌드는 항상 이 실물을 쓰고, 디버그 빌드는 기본이
/// 목(<see cref="ProjectSeWoo.Shared.Mocks.MockEconomyService"/>)이며
/// <c>--real-economy</c> 로 켜야 이 실물이 돈다 - Shift+B/Shift+R/G 같은
/// 디버그 키가 목에서만 동작하기 때문이다.
///
/// <b>인증 흐름</b> (docs/ECONOMY-SERVER-API.md §1): 스팀 세션 티켓을
/// <see cref="SteamUser.GetAuthSessionTicket"/> 로 로컬에서 만들고, 그걸
/// <c>POST /v1/session</c> 으로 백엔드에 보내 검증받은 뒤 짧은 수명의 자체 서명
/// 토큰을 받는다. 이후 호출은 그 토큰만 싣는다 - 매 요청마다 스팀에 티켓을
/// 새로 만들면 밸브 쪽 레이트리밋에 걸리기 쉽다.
///
/// <b>낙관적 갱신은 계약(<see cref="IEconomyService"/>)이 요구하는 것이다.</b>
/// <see cref="RequestHarvest"/> 는 응답을 기다리지 않고 로컬 값을 먼저 바꾼 뒤
/// 백그라운드에서 서버 확인을 보낸다 - 펀치 연출이 네트워크 왕복 시간만큼
/// 멈추면 §2-3 의 P0(즉각 피드백)가 깨진다.
///
/// <b>슬롯은 응답 사이에 로컬 시계로 자란다 (2026-09-24).</b> 서버 값은 응답을 받은
/// 순간의 스냅샷일 뿐이라, 그 사이를 안 채우면 켤 때 익어 있던 바나나를 다 딴 뒤로
/// 나무가 멈췄다 - 익은 슬롯이 없으면 하베스트 요청이 안 나가고, 요청이 안 나가면
/// 새 스냅샷도 안 온다. 목은 <c>Tick</c> 으로 같은 일을 해서 디버그에선 안 보였다.
/// 성장 공식이 서버와 같으므로(<c>server/src/economy.ts</c> 의 <c>recomputeSlots</c>)
/// 로컬 예측은 다음 응답과 거의 그대로 맞는다.
/// </summary>
public sealed class EconomyClient : IEconomyService, IDisposable
{
    /// <summary>
    /// 배포된 경제 백엔드 URL (docs/ECONOMY-SERVER.md §9). `--economy-url=`
    /// 로 덮을 수 있다 - <c>wrangler dev</c> 로 띄운 로컬 서버를 겨냥할 때 쓴다.
    /// </summary>
    public const string DefaultBaseUrl = "https://punchmonkey-economy.shimwoojin627.workers.dev";

    /// <summary>
    /// 세션 티켓 버퍼 크기(byte). 스팀 인증 티켓은 보통 1KB 를 넘지 않는다 -
    /// 넘치면 <see cref="AcquireSteamAuthTicketHex"/> 가 null 을 돌려주고
    /// 로그를 남기므로, 실측에서 잘리는 일이 생기면 여기서 바로 드러난다.
    /// </summary>
    private const int TicketBufferSize = 1024;

    /// <summary>세션 토큰의 실제 만료보다 이만큼 일찍 재발급을 시작한다 - 요청을
    /// 보내는 도중에 막 만료되는 경합을 피하기 위한 여유분이다.</summary>
    private static readonly TimeSpan SessionRenewMargin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// HTTP 요청 하나의 상한. 기본값(100초)이면 네트워크가 이상할 때 기동이 그만큼
    /// 멈춘다 - <c>GameRoot</c> 가 첫 <see cref="Sync"/> 를 기다린 뒤에 HUD 를 세우기
    /// 때문이다. 요청은 전부 작은 JSON 이라 10초면 넉넉하다.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly SteamService _steam;

    private string _sessionToken;
    private DateTime _sessionExpiresUtc = DateTime.MinValue;

    /// <summary>세션 발급이 이미 날아가 있으면 그 Task 를 같이 기다린다 - 짧은 시간에
    /// 하베스트 여러 번이 몰리면(타건 배치) 세션 요청이 중복으로 나가는 것을 막는다.</summary>
    private Task<bool> _sessionInFlight;

    private long _balance;
    private SlotState[] _slots = Array.Empty<SlotState>();

    /// <summary>슬롯을 로컬로 진행시키는 시계와, 마지막으로 진행시킨 시각(ms).</summary>
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _advancedAtMs;
    private readonly Dictionary<UpgradeAxis, int> _upgradeLevels = new();

    /// <param name="baseUrl">배포된 워커 URL. 예: https://punchmonkey-economy.&lt;계정&gt;.workers.dev</param>
    /// <param name="steam">인증 티켓을 만드는 데 쓴다. <see cref="SteamService.IsInitialized"/>
    /// 가 false 인 동안은 <see cref="IsAvailable"/> 도 계속 false 다.</param>
    public EconomyClient(string baseUrl, SteamService steam)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        _steam = steam;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = RequestTimeout };
    }

    /// <summary>세션 토큰을 들고 있고 아직 안 만료됐는가. 하베스트는 이게 false 여도
    /// 로컬 예측을 계속 쌓는다 - 구매만 이 값으로 즉시 실패 처리한다.</summary>
    public bool IsAvailable => _sessionToken != null && DateTime.UtcNow < _sessionExpiresUtc;

    public long Balance => _balance;

    public IReadOnlyList<SlotState> Slots
    {
        get
        {
            AdvanceLocal();
            return _slots;
        }
    }

    public event Action OnStateChanged;

    public int UpgradeLevel(UpgradeAxis axis) => _upgradeLevels.GetValueOrDefault(axis);

    public void RequestHarvest(int slotIndex)
    {
        AdvanceLocal();
        if (slotIndex < 0 || slotIndex >= _slots.Length || !_slots[slotIndex].Ready)
        {
            return;
        }

        // 낙관적 갱신. 파워 보정치는 서버만 정확히 알므로 +1 로 최소 추정만 하고,
        // 서버 응답이 오면 ApplyState 가 진짜 값으로 덮어쓴다.
        _slots[slotIndex] = new SlotState(0, _slots[slotIndex].GrowthMs);
        _balance += 1;
        OnStateChanged?.Invoke();

        _ = ReconcileHarvestAsync(slotIndex, Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// 마지막 진행 이후 흐른 시간만큼 안 익은 슬롯을 키운다. 다 자란 슬롯은
    /// 성장 시간에서 멈춘다 - 서버의 <c>recomputeSlots</c> 와 같은 규칙이다.
    /// </summary>
    private void AdvanceLocal()
    {
        long now = _clock.ElapsedMilliseconds;
        long delta = now - _advancedAtMs;
        if (delta <= 0)
        {
            return;
        }

        _advancedAtMs = now;
        for (int i = 0; i < _slots.Length; i++)
        {
            SlotState s = _slots[i];
            if (!s.Ready)
            {
                _slots[i] = new SlotState(Math.Min(s.GrowthMs, s.ElapsedMs + delta), s.GrowthMs);
            }
        }
    }

    private async Task ReconcileHarvestAsync(int slotIndex, string clientRequestId)
    {
        try
        {
            if (!await EnsureSessionAsync())
            {
                // 세션이 없으면 확인 요청 자체를 못 보낸다 - 다음 Sync() 가 정리한다.
                return;
            }

            HarvestResponseWire response = await PostAsync<HarvestResponseWire>(
                "/v1/economy/harvest", new { slotIndex, clientRequestId });

            if (response != null)
            {
                ApplyState(response.Balance, response.Slots);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"[economy] 하베스트 확인 실패 (슬롯 {slotIndex}) - {e.Message}. 로컬 낙관값 유지");
        }
    }

    public async Task<PurchaseResult> PurchaseItem(string itemDefId)
    {
        if (!await EnsureSessionAsync())
        {
            return new PurchaseResult(PurchaseOutcome.ServerUnavailable, _balance);
        }

        try
        {
            PurchaseResponseWire response = await PostAsync<PurchaseResponseWire>(
                "/v1/economy/purchase/item",
                new { itemDefId, clientRequestId = Guid.NewGuid().ToString("N") });

            if (response == null)
            {
                return new PurchaseResult(PurchaseOutcome.ServerUnavailable, _balance);
            }

            _balance = response.Balance;
            OnStateChanged?.Invoke();
            return new PurchaseResult(ParseOutcome(response.Outcome), response.Balance, response.GrantedItemDefId);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[economy] 구매 요청 실패 ({itemDefId}) - {e.Message}");
            return new PurchaseResult(PurchaseOutcome.ServerUnavailable, _balance);
        }
    }

    public async Task<PurchaseResult> PurchaseUpgrade(UpgradeAxis axis)
    {
        if (!await EnsureSessionAsync())
        {
            return new PurchaseResult(PurchaseOutcome.ServerUnavailable, _balance);
        }

        try
        {
            string axisName = axis.ToString().ToLowerInvariant();
            PurchaseResponseWire response = await PostAsync<PurchaseResponseWire>(
                "/v1/economy/purchase/upgrade",
                new { axis = axisName, clientRequestId = Guid.NewGuid().ToString("N") });

            if (response == null)
            {
                return new PurchaseResult(PurchaseOutcome.ServerUnavailable, _balance);
            }

            _balance = response.Balance;
            if (response.Upgrades != null)
            {
                _upgradeLevels[UpgradeAxis.Power] = response.Upgrades.Power;
                _upgradeLevels[UpgradeAxis.Cycle] = response.Upgrades.Cycle;
                _upgradeLevels[UpgradeAxis.Slots] = response.Upgrades.Slots;
            }

            OnStateChanged?.Invoke();
            return new PurchaseResult(ParseOutcome(response.Outcome), response.Balance);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[economy] 강화 요청 실패 ({axis}) - {e.Message}");
            return new PurchaseResult(PurchaseOutcome.ServerUnavailable, _balance);
        }
    }

    public async Task Sync()
    {
        if (!await EnsureSessionAsync())
        {
            return;
        }

        try
        {
            EconomyStateWire response = await GetAsync<EconomyStateWire>("/v1/economy/state");
            if (response == null)
            {
                return;
            }

            ApplyState(response.Balance, response.Slots);
            _upgradeLevels[UpgradeAxis.Power] = response.Upgrades.Power;
            _upgradeLevels[UpgradeAxis.Cycle] = response.Upgrades.Cycle;
            _upgradeLevels[UpgradeAxis.Slots] = response.Upgrades.Slots;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[economy] 상태 동기화 실패 - {e.Message}");
        }
    }

    public void Dispose() => _http.Dispose();

    // ------------------------------------------------------------------ 세션

    /// <summary>
    /// 유효한 세션 토큰을 보장한다. 이미 있으면 즉시 true - 스팀 티켓을 매번 새로
    /// 만들지 않는다(docs/ECONOMY-SERVER-API.md §1).
    /// </summary>
    private Task<bool> EnsureSessionAsync()
    {
        if (IsAvailable)
        {
            return Task.FromResult(true);
        }

        // 이미 날아간 세션 요청이 있으면 같이 기다린다 - 100ms 타건 배치 안에서
        // 하베스트 여러 번이 각자 세션을 새로 발급받으려 드는 것을 막는다.
        if (_sessionInFlight != null)
        {
            return _sessionInFlight;
        }

        // **동기로 끝난 실패는 잡아 두지 않는다.** CreateSessionAsync 가 첫 await 전에
        // false 로 끝나면(스팀 미초기화, 티켓 발급 실패) 그 안의 finally 가 필드를
        // 비우는 것보다 여기서 대입하는 게 나중이라, 끝난 실패 Task 가 필드에 영원히
        // 남아 이후 모든 요청이 재시도 없이 실패했다.
        Task<bool> attempt = CreateSessionAsync();
        if (!attempt.IsCompleted)
        {
            _sessionInFlight = attempt;
        }

        return attempt;
    }

    private async Task<bool> CreateSessionAsync()
    {
        try
        {
            if (_steam is not { IsInitialized: true })
            {
                return false;
            }

            string ticketHex = AcquireSteamAuthTicketHex();
            if (ticketHex == null)
            {
                return false;
            }

            SessionResponseWire response = await PostAsync<SessionResponseWire>(
                "/v1/session", new { ticket = ticketHex });

            if (response == null)
            {
                return false;
            }

            _sessionToken = response.Token;

            // 서버는 ISO 8601 을 "Z" 접미사로 보낸다(session.ts) - RoundtripKind
            // 하나로 충분히 Utc Kind 로 파싱된다. AdjustToUniversal 을 같이 주면
            // "RoundtripKind 는 다른 보정 플래그와 못 섞는다"는 .NET 예외가 난다
            // (2026-09-23 실측 - --real-economy 로 처음 돌려보고서야 드러났다).
            _sessionExpiresUtc = DateTime.Parse(
                response.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                - SessionRenewMargin;
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[economy] 세션 발급 실패 - {e.Message}");
            return false;
        }
        finally
        {
            _sessionInFlight = null;
        }
    }

    /// <summary>
    /// 스팀 세션 티켓을 만들어 16진수 문자열로 돌려준다. 서버가
    /// <c>ISteamUserAuth/AuthenticateUserTicket</c> 로 검증할 원본이다.
    ///
    /// <see cref="GetAuthSessionTicketResponse_t"/> 콜백(스팀이 나중에 티켓을
    /// 무효화하는 것을 알려준다)은 아직 안 받는다 - 세션 토큰 자체가 짧은
    /// 수명(§1 <see cref="SessionRenewMargin"/>)이라 재발급이 자연스럽게
    /// 도니 당장은 필요하지 않다. 장시간 세션을 유지할 일이 생기면 그때 붙인다.
    /// </summary>
    private string AcquireSteamAuthTicketHex()
    {
        try
        {
            var buffer = new byte[TicketBufferSize];
            var identity = new SteamNetworkingIdentity();
            HAuthTicket handle = SteamUser.GetAuthSessionTicket(buffer, buffer.Length, out uint ticketLength, ref identity);

            if (handle == HAuthTicket.Invalid || ticketLength == 0)
            {
                GD.PushWarning("[economy] 스팀 인증 티켓 발급 실패 (핸들 무효)");
                return null;
            }

            return Convert.ToHexString(buffer, 0, (int)ticketLength);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[economy] 스팀 인증 티켓 발급 예외 - {e.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------ HTTP

    private void ApplyState(long balance, SlotStateWire[] slots)
    {
        _balance = balance;
        _slots = slots?.Select(s => new SlotState(s.ElapsedMs, s.GrowthMs)).ToArray() ?? Array.Empty<SlotState>();
        _advancedAtMs = _clock.ElapsedMilliseconds;
        OnStateChanged?.Invoke();
    }

    private static PurchaseOutcome ParseOutcome(string outcome) => outcome switch
    {
        "success" => PurchaseOutcome.Success,
        "insufficient_balance" => PurchaseOutcome.InsufficientBalance,
        "item_unknown" => PurchaseOutcome.ItemUnknown,
        "already_owned" => PurchaseOutcome.AlreadyOwned,
        _ => PurchaseOutcome.Rejected,
    };

    private async Task<T> PostAsync<T>(string path, object body)
    {
        string json = JsonSerializer.Serialize(body, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        return await SendAsync<T>(request);
    }

    private async Task<T> GetAsync<T>(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<T>(request);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request)
    {
        if (_sessionToken != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _sessionToken);
        }

        using HttpResponseMessage response = await _http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            GD.PushWarning($"[economy] {request.Method} {request.RequestUri} -> HTTP {(int)response.StatusCode}: {body}");
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    // ------------------------------------------------------------------ 서버 응답 (와이어 포맷)
    //
    // server/src/types.ts 와 1:1 로 맞춘다. 그쪽을 바꾸면 여기도 같이 본다 -
    // 컴파일 타임에 안 잡히는 어긋남이라 docs/ECONOMY-SERVER-API.md 가 유일한
    // 근거 문서다.

    private sealed class SessionResponseWire
    {
        [JsonPropertyName("token")] public string Token { get; set; }
        [JsonPropertyName("expiresAt")] public string ExpiresAt { get; set; }
    }

    private sealed class SlotStateWire
    {
        [JsonPropertyName("elapsedMs")] public long ElapsedMs { get; set; }
        [JsonPropertyName("growthMs")] public long GrowthMs { get; set; }
    }

    private sealed class UpgradesWire
    {
        [JsonPropertyName("power")] public int Power { get; set; }
        [JsonPropertyName("cycle")] public int Cycle { get; set; }
        [JsonPropertyName("slots")] public int Slots { get; set; }
    }

    private sealed class EconomyStateWire
    {
        [JsonPropertyName("balance")] public long Balance { get; set; }
        [JsonPropertyName("slots")] public SlotStateWire[] Slots { get; set; }
        [JsonPropertyName("upgrades")] public UpgradesWire Upgrades { get; set; }
        [JsonPropertyName("lastSyncUtc")] public string LastSyncUtc { get; set; }
    }

    private sealed class HarvestResponseWire
    {
        [JsonPropertyName("accepted")] public bool Accepted { get; set; }
        [JsonPropertyName("reason")] public string Reason { get; set; }
        [JsonPropertyName("balance")] public long Balance { get; set; }
        [JsonPropertyName("slots")] public SlotStateWire[] Slots { get; set; }
    }

    private sealed class PurchaseResponseWire
    {
        [JsonPropertyName("outcome")] public string Outcome { get; set; }
        [JsonPropertyName("balance")] public long Balance { get; set; }
        [JsonPropertyName("grantedItemDefId")] public string GrantedItemDefId { get; set; }
        [JsonPropertyName("upgrades")] public UpgradesWire Upgrades { get; set; }
    }
}
