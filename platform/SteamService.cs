using System;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using ProjectSeWoo.Shared;
using Steamworks;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 스팀 초기화 + 콜백 펌프 + <see cref="IAchievements"/> 실물 (A8, 기획서 §9 W2).
/// 자세한 배경과 결정은 docs/A8-STEAM.md.
///
/// 기획서 §9 는 A8 을 "GodotSteam 연동" 으로 적었지만 **Steamworks.NET(순수 C# 바인딩)**
/// 으로 갔다. GodotSteam 은 GDExtension 이라 C# 에서는 모든 호출이
/// <c>Engine.GetSingleton("Steam").Call("createLobby", ...)</c> 형태의 문자열 + Variant
/// 마샬링이 되고, 시그니처 오타가 컴파일이 아니라 런타임에 터진다. 이 프로젝트는 코드가
/// 100% C# 이고 A9~A11 (로비 / P2P / 재접속) 이 전부 이 위에 얹히므로, 타입 안전을
/// 잃는 비용이 계속 누적된다 (docs/A8-STEAM.md §1).
///
/// **스팀이 없어도 앱은 정상 동작해야 한다.** 이건 상주 오버레이라 스팀을 안 켠 채
/// 켜져 있는 시간이 더 길고, 부팅 자동 시작(A6)이면 로그인 직후 스팀보다 먼저 뜬다.
/// 그래서 초기화 실패는 오류가 아니라 상태다 — <see cref="Status"/> 에 사유를 남기고
/// <see cref="IsAvailable"/> 를 false 로 둔 채 계속 돈다. 나중에 유저가 스팀을 켜면
/// <see cref="Tick"/> 가 재시도해서 붙는다 (§2-3).
/// </summary>
public sealed class SteamService : IAchievements, IDisposable
{
    /// <summary>
    /// PunchMonkey 의 스팀 앱 ID. 2026-09-16 에 Direct 수수료를 결제하고 발급받았다
    /// (기획서 §11). <c>--steam-appid=</c> 로 덮을 수 있다.
    ///
    /// **A15 전까지 도전과제 통계는 안 온다.** 파트너 사이트에 통계/도전과제 스키마가
    /// 아직 없어서 <c>UserStatsReceived</c> 가 <c>k_EResultFail</c> 로 온다(2026-09-16
    /// 실측). 따라서 <see cref="IsAvailable"/> 는 false 이고 <see cref="Unlock"/> 은
    /// 조용히 버려진다 — 계약대로다. 역설적으로 <see cref="SpacewarAppId"/> 일 때는
    /// Spacewar 의 도전과제 5개가 잡혀서 통계 수신이 PASS 였다. **"480 에서 되던 게
    /// 진짜 ID 에서 안 된다"는 퇴행이 아니라 예정된 상태다.**
    /// </summary>
    public const uint DefaultAppId = 5281130;

    /// <summary>
    /// Spacewar. 밸브가 SDK 예제용으로 공개한 앱 ID 이고 아무 계정에서나 초기화가 된다.
    /// A8~A15 의 기본값이었고 지금은 **진단용으로만 남긴다** — <c>--steam-appid=480</c>
    /// 으로 되돌려 보면 "우리 앱 설정 문제인가, SDK/로컬 환경 문제인가"를 가를 수 있다.
    /// 로비(A9)를 480 으로 시험하면 전 세계 SDK 예제 사용자와 같은 공간을 쓰게 되므로
    /// 그쪽으로는 쓰지 않는다.
    /// </summary>
    public const uint SpacewarAppId = 480;

    /// <summary>스팀이 꺼져 있을 때 재시도 간격(초). 부팅 자동 시작이 스팀보다 먼저 뜨는 경우 때문이다.</summary>
    private const double RetryIntervalSec = 30.0;

    private static bool _resolverInstalled;

    /// <summary>
    /// 콜백 핸들은 반드시 살려둬야 한다. 지역 변수로 만들면 GC 가 걷어가고
    /// 그때부터 <see cref="SteamAPI.RunCallbacks"/> 가 아무것도 부르지 않는다.
    /// </summary>
    private Callback<UserStatsReceived_t> _statsReceived;

    private bool _initialized;
    private bool _statsReady;
    private bool _gaveUp;
    private double _retryTimer;
    private uint _appId = DefaultAppId;

    /// <summary>스팀에 붙었고 통계까지 받아왔는가 (<see cref="IAchievements.IsAvailable"/>).</summary>
    public bool IsAvailable => _initialized && _statsReady;

    /// <summary>초기화 자체는 됐는가. 로비(A9~A11)는 통계와 무관하므로 이쪽을 본다.</summary>
    public bool IsInitialized => _initialized;

    /// <summary>사람이 읽는 현재 상태. HUD·로그용이고 분기 조건으로 쓰지 않는다.</summary>
    public string Status { get; private set; } = "미시작";

    /// <summary>내 스팀 ID. 초기화 전에는 기본값(0)이다. A9~A11 의 <see cref="INetSession"/> 이 쓴다.</summary>
    public PeerId SelfId { get; private set; }

    /// <summary>내 스팀 닉네임. 멀티 룸 표시용 (§4-2). 초기화 전에는 빈 문자열.</summary>
    public string PersonaName { get; private set; } = string.Empty;

    /// <summary>실제로 쓰는 앱 ID. <see cref="SpacewarAppId"/>(480) 이면 진단 실행이다.</summary>
    public uint AppId => _appId;

    /// <summary>
    /// 스팀에 붙는다. 실패해도 예외를 던지지 않는다 — 실패는 <see cref="Status"/> 로만 알린다.
    /// </summary>
    public void Start()
    {
        _appId = ResolveAppId();

        if (!TryInit())
        {
            // 실패 사유는 TryInit 이 Status 에 넣었다. 여기서 끝내지 않고 Tick 이 재시도한다.
            GD.Print($"[steam] {Status}");
        }
    }

    /// <summary>
    /// 매 프레임 부른다. 스팀 콜백은 우리가 펌프를 돌려야만 도착한다 — 로비 초대,
    /// P2P 세션 요청(A9~A11), 통계 수신이 전부 이 호출에 실려 온다.
    ///
    /// 아직 못 붙었으면 <see cref="RetryIntervalSec"/> 마다 다시 시도한다.
    /// </summary>
    public void Tick(double delta)
    {
        if (_initialized)
        {
            SteamAPI.RunCallbacks();
            FlushStatsIfDue(delta);
            return;
        }

        if (_gaveUp)
        {
            return;
        }

        _retryTimer += delta;
        if (_retryTimer < RetryIntervalSec)
        {
            return;
        }

        _retryTimer = 0.0;

        // 스팀이 아예 안 돌고 있으면 InitEx 를 부르지 않는다. 실패가 확정된 호출을
        // 30 초마다 반복하면 로그만 더러워진다.
        if (!IsSteamProcessRunning())
        {
            return;
        }

        if (TryInit())
        {
            GD.Print($"[steam] {Status} (재시도 성공)");
        }
    }

    /// <summary>
    /// <see cref="IAchievements.Unlock"/>. 스팀이 없으면 조용히 버린다 —
    /// 을이 분기 없이 부를 수 있어야 한다는 게 계약의 요점이다.
    /// </summary>
    public void Unlock(string id)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id))
        {
            return;
        }

        if (!SteamUserStats.SetAchievement(id))
        {
            // 앱 ID 가 480(Spacewar)이거나 파트너 사이트에 아직 안 올린 이름이면 여기로 온다.
            // A15 전까지는 정상적인 경로다.
            GD.Print($"[steam] 도전과제 '{id}' 설정 실패 - appid={_appId} 에 등록되지 않은 이름일 수 있다");
            return;
        }

        // SetAchievement 는 로컬 캐시만 바꾼다. StoreStats 를 불러야 스팀 서버로 가고
        // 해금 토스트가 뜬다. 밀린 통계도 같은 호출에 실려 간다.
        SteamUserStats.StoreStats();
        _statsDirty = false;
        _statsFlushTimer = 0.0;
    }

    /// <summary><see cref="IAchievements.IsUnlocked"/>.</summary>
    /// <summary>
    /// 이 이름이 파트너 사이트의 도전과제 스키마에 있는가 (A15 자체 검사용).
    /// <c>GetAchievement</c> 는 모르는 이름이면 호출 자체가 false 다 - 해금 여부와 별개다.
    /// 통계를 받기 전(<see cref="IsAvailable"/> false)에는 판단할 수 없어 null.
    /// </summary>
    public bool? IsRegistered(string id)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id))
        {
            return null;
        }

        return SteamUserStats.GetAchievement(id, out _);
    }

    /// <summary>통계를 읽는다 (자체 검사용). 모르는 이름이거나 통계 전이면 null.</summary>
    public int? TryGetStat(string id)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id))
        {
            return null;
        }

        return SteamUserStats.GetStat(id, out int value) ? value : null;
    }

    /// <summary>INT 로 못 읽은 통계가 FLOAT 로는 읽히는가 (자체 검사 진단 - 타입을 잘못 등록한 경우).</summary>
    public bool IsFloatStat(string id) =>
        IsAvailable && !string.IsNullOrEmpty(id) && SteamUserStats.GetStat(id, out float _);

    public bool IsUnlocked(string id)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id))
        {
            return false;
        }

        return SteamUserStats.GetAchievement(id, out bool achieved) && achieved;
    }

    /// <summary><see cref="IAchievements.IndicateProgress"/>.</summary>
    /// <summary>
    /// 통계를 스팀 서버로 보내는 최소 간격(초). <see cref="SetStat"/> 은 타건마다 오는데
    /// <c>StoreStats</c> 를 그만큼 부르면 스팀이 호출을 제한한다. 1분이면 커뮤니티 진행
    /// 막대가 늦어 봐야 1분이다. 해금(<see cref="Unlock"/>)과 종료(<see cref="Dispose"/>)
    /// 때는 기다리지 않고 같이 보낸다.
    /// </summary>
    private const double StatsFlushSec = 60.0;

    private bool _statsDirty;
    private double _statsFlushTimer;
    private bool _statRejectLogged;

    public void SetStat(string id, int value)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id))
        {
            return;
        }

        // Increment Only 통계는 값이 줄면 거부된다 - 세이브를 새로 시작한 PC 에서 스팀에
        // 더 큰 값이 이미 있는 경우다. 게임은 계속 돌고, 세이브가 스팀 값을 넘으면 다시 먹는다.
        if (!SteamUserStats.SetStat(id, value))
        {
            if (!_statRejectLogged)
            {
                _statRejectLogged = true;
                GD.Print($"[steam] 통계 '{id}'={value} 거부 - 미등록 이름이거나 Increment Only 에 더 작은 값");
            }

            return;
        }

        _statsDirty = true;
    }

    private void FlushStatsIfDue(double delta)
    {
        if (!_statsDirty)
        {
            return;
        }

        _statsFlushTimer += delta;
        if (_statsFlushTimer >= StatsFlushSec)
        {
            FlushStats();
        }
    }

    private void FlushStats()
    {
        _statsFlushTimer = 0.0;
        if (_statsDirty && IsAvailable)
        {
            _statsDirty = false;
            SteamUserStats.StoreStats();
        }
    }

    public void IndicateProgress(string id, int current, int max)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id) || max <= 0 || current < 0)
        {
            return;
        }

        SteamUserStats.IndicateAchievementProgress(id, (uint)current, (uint)max);
    }

    /// <summary>
    /// 스팀을 놓는다. <see cref="SteamAPI.Shutdown"/> 을 안 부르면 스팀 오버레이가
    /// 죽은 프로세스를 "플레이 중" 으로 잡고 있는 시간이 생긴다.
    /// </summary>
    public void Dispose()
    {
        if (!_initialized)
        {
            return;
        }

        // 마지막 1분 안에 친 타수가 통계에 남도록 놓기 전에 한 번 보낸다.
        FlushStats();

        _statsReceived?.Dispose();
        _statsReceived = null;
        SteamAPI.Shutdown();
        _initialized = false;
        _statsReady = false;
        Status = "종료됨";
    }

    // ------------------------------------------------------------------ 초기화

    private bool TryInit()
    {
        try
        {
            InstallNativeResolver();

            // steam_api64.dll 을 우리가 직접 못 찾으면 여기서 DllNotFoundException 이 난다.
            if (!Packsize.Test())
            {
                Status = "Packsize 불일치 - Steamworks.NET 과 steam_api64.dll 버전이 다르다";
                _gaveUp = true;
                return false;
            }

            // 스팀이 안 떠 있으면 InitEx 는 실패하는 게 정상이다. 먼저 걸러서
            // 에러 문자열 대신 사람이 읽는 사유를 남긴다.
            if (!IsSteamProcessRunning())
            {
                Status = "스팀이 실행 중이 아니다 (도전과제·멀티 비활성, 나머지는 정상)";
                return false;
            }

            // 스팀으로 실행하지 않은 개발 실행에서도 붙게 한다. steam_appid.txt 파일을
            // 두는 방법도 있지만 그건 현재 작업 디렉터리에 의존한다 - 자동 시작으로 뜨면
            // 작업 디렉터리가 어디가 될지 모르므로 환경변수 쪽이 확실하다.
            System.Environment.SetEnvironmentVariable("SteamAppId", _appId.ToString());
            System.Environment.SetEnvironmentVariable("SteamGameId", _appId.ToString());

            ESteamAPIInitResult result = SteamAPI.InitEx(out string err);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                Status = $"초기화 실패 ({result}: {err})";
                return false;
            }

            _initialized = true;
            SelfId = new PeerId(SteamUser.GetSteamID().m_SteamID);
            PersonaName = SteamFriends.GetPersonaName();

            // 도전과제 상태는 비동기로 온다. 이 콜백이 오기 전까지 IsAvailable 은 false 다.
            _statsReceived = Callback<UserStatsReceived_t>.Create(OnUserStatsReceived);
            SteamUserStats.RequestCurrentStats();

            Status = $"연결됨 appid={_appId} user={PersonaName} id={SelfId}"
                + (_appId == SpacewarAppId ? " (Spacewar 진단 ID)" : string.Empty);
            GD.Print($"[steam] {Status}");
            return true;
        }
        catch (DllNotFoundException)
        {
            Status = "steam_api64.dll 을 찾지 못했다 - 프로젝트 루트에 있어야 한다 (docs/A8-STEAM.md §3)";
            _gaveUp = true;
            return false;
        }
        catch (Exception e)
        {
            // 상주 앱이 스팀 때문에 못 뜨는 일만은 막는다.
            Status = $"초기화 예외 ({e.GetType().Name}: {e.Message})";
            _gaveUp = true;
            return false;
        }
    }

    private void OnUserStatsReceived(UserStatsReceived_t cb)
    {
        // 다른 앱의 통계 콜백이 섞여 들어올 수 있다.
        if (cb.m_nGameID != _appId)
        {
            return;
        }

        _statsReady = cb.m_eResult == EResult.k_EResultOK;
        if (!_statsReady)
        {
            GD.Print($"[steam] 통계 수신 실패 ({cb.m_eResult}) - 도전과제 비활성");
            return;
        }

        GD.Print($"[steam] 통계 수신 완료. 도전과제 {SteamUserStats.GetNumAchievements()}개");
    }

    /// <summary>
    /// <c>--steam-appid=N</c> 으로 앱 ID 를 덮어쓴다. 진짜 앱 ID 가 나오면 이 인자로
    /// 진단용으로 <see cref="SpacewarAppId"/>(480) 로 되돌려 볼 때도 쓴다.
    /// </summary>
    private static uint ResolveAppId()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            const string Prefix = "--steam-appid=";
            if (arg.StartsWith(Prefix, StringComparison.Ordinal)
                && uint.TryParse(arg.AsSpan(Prefix.Length), out uint parsed)
                && parsed != 0)
            {
                return parsed;
            }
        }

        return DefaultAppId;
    }

    /// <summary>
    /// 스팀 밖에서 exe 를 바로 실행했으면 스팀을 통해 다시 띄우도록 요청한다.
    /// true 면 스팀이 새 인스턴스를 띄우므로 <b>호출부는 즉시 종료해야 한다</b>.
    ///
    /// <b>왜 필요한가 (A14).</b> 경제·인벤토리가 스팀 세션 티켓에 기대므로 스팀 없이
    /// 뜬 인스턴스는 바나나도 장식도 없는 빈 껍데기다. A6 자동 시작(레지스트리 Run)이
    /// exe 를 직접 띄우는 대표 경로인데, 이걸 거치면 스팀이 떠 있지 않아도 스팀이 먼저
    /// 켜진 뒤 게임이 뜬다. 스팀 오버레이·소유권 확인도 이 경로에서만 제대로 붙는다.
    ///
    /// <b>릴리스에서만 부른다.</b> 개발 실행(에디터·VS F5)은 스팀으로 띄우지 않으므로
    /// 여기서 true 가 나와 매번 꺼져 버린다. 또 <see cref="TryInit"/> 이 넣는
    /// <c>SteamAppId</c> 환경변수보다 <b>먼저</b> 불러야 한다 - 그 변수가 있으면
    /// 스팀이 띄운 것으로 보고 false 를 돌려준다.
    /// </summary>
    public static bool RelaunchThroughSteamIfNeeded()
    {
        try
        {
            InstallNativeResolver();
            return SteamAPI.RestartAppIfNecessary(new AppId_t(ResolveAppId()));
        }
        catch (Exception e)
        {
            // dll 을 못 찾는 등 판단할 수 없으면 그냥 뜬다 - 상주 앱이 이것 때문에
            // 못 뜨는 쪽이 더 나쁘다. 스팀 없이 뜬 상태는 TryInit 이 재시도로 메운다.
            GD.PushWarning($"[steam] 재실행 판단 실패, 그대로 진행 ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }

    private static bool IsSteamProcessRunning()
    {
        try
        {
            return SteamAPI.IsSteamRunning();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// <c>steam_api64.dll</c> 을 어디서 찾을지 직접 정한다.
    ///
    /// Steamworks.NET 의 NuGet 패키지에는 **네이티브 dll 이 들어있지 않다** (밸브 SDK
    /// 재배포본은 따로 받아야 한다). 그래서 .deps.json 에 경로가 없고, 기본 해석은
    /// OS 검색 순서(실행 파일 폴더 → 시스템 폴더 → 현재 작업 디렉터리 → PATH)로 떨어진다.
    /// 여기서 문제가 되는 게 **현재 작업 디렉터리**다 — 에디터/VS F5 로 띄우면 프로젝트
    /// 폴더지만, A6 자동 시작으로 뜨면 레지스트리 Run 이 정한 엉뚱한 폴더가 된다.
    /// 그때마다 "내 PC 에선 되는데" 가 나오므로 후보 경로를 명시한다.
    /// </summary>
    private static void InstallNativeResolver()
    {
        if (_resolverInstalled)
        {
            return;
        }

        _resolverInstalled = true;

        NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, (name, assembly, searchPath) =>
        {
            if (name != "steam_api64" && name != "steam_api")
            {
                return IntPtr.Zero;
            }

            foreach (string dir in NativeSearchDirs())
            {
                if (string.IsNullOrEmpty(dir))
                {
                    continue;
                }

                string candidate = Path.Combine(dir, name + ".dll");
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
                {
                    return handle;
                }
            }

            // 못 찾았으면 기본 해석에 맡긴다 (IntPtr.Zero). 거기서도 실패하면
            // DllNotFoundException 이 나고 TryInit 이 받는다.
            return IntPtr.Zero;
        });
    }

    private static string[] NativeSearchDirs() => new[]
    {
        // 내보낸 빌드: 게임 exe 옆. A12 빌드 파이프라인이 여기에 복사해야 한다.
        Path.GetDirectoryName(OS.GetExecutablePath()),

        // 에디터/개발 실행: 프로젝트 루트. 저장소에 커밋된 그 파일이다.
        ProjectSettings.GlobalizePath("res://"),
    };
}
