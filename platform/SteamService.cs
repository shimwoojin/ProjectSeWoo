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
    /// Spacewar. 밸브가 SDK 예제용으로 공개한 앱 ID 이고, 개발 중 아무 계정에서나
    /// 초기화가 된다. **우리 앱 ID 가 나오기 전까지의 임시값이다** — 스팀 수수료
    /// 결제 후 앱이 생성되면 <c>--steam-appid=</c> 로 덮거나 이 상수를 바꾼다
    /// (기획서 §11, 아직 미결제 상태라 A8 시점에 진짜 ID 가 없다).
    ///
    /// 480 으로는 도전과제가 "성공적으로 호출은 되지만 우리 것이 아니다" — Spacewar 에
    /// 우리 API Name 이 등록돼 있지 않아 <see cref="Unlock"/> 이 false 를 돌려준다.
    /// 그게 정상이고, 실제 해금 검증은 A15 에서 진짜 앱 ID 로 한다.
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
    private uint _appId = SpacewarAppId;

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

    /// <summary>실제로 쓰는 앱 ID. 480 이면 아직 임시값이다.</summary>
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
        // 해금 토스트가 뜬다.
        SteamUserStats.StoreStats();
    }

    /// <summary><see cref="IAchievements.IsUnlocked"/>.</summary>
    public bool IsUnlocked(string id)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id))
        {
            return false;
        }

        return SteamUserStats.GetAchievement(id, out bool achieved) && achieved;
    }

    /// <summary><see cref="IAchievements.IndicateProgress"/>.</summary>
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
                + (_appId == SpacewarAppId ? " (Spacewar 임시 ID)" : string.Empty);
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
    /// 먼저 검증하고, 확인되면 <see cref="SpacewarAppId"/> 자리를 바꾼다.
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

        return SpacewarAppId;
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
