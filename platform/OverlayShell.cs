using System;
using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Shared.Mocks;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 오버레이 셸. <see cref="IShell"/> 실물이다 (A3, 기획확정-일감분배-260907.md §8-2/§7-1).
///
/// Day 1-2 스파이크(투명/무테/항상위/클릭통과/플리커)와 A2 커서 스파이크가 여기서
/// 시작됐고, 둘 다 Go 판정이 났다 — docs/DAY1-2-SPIKE.md, docs/A2-CURSOR-SPIKE.md.
/// A3 이전에는 <c>src/</c>에 있던 스파이크 코드였고, 지금 이 클래스로 모듈화됐다.
///
/// 검증했던 것 (docs/WEEK0-GODOT-VALIDATION.md §2):
///   1. 투명 + 무테 + 항상 위 + 클릭 통과가 동시에 되는가
///   2. 몸통 드래그로 창이 따라오는가
///   3. 멀티모니터 / DPI 스케일링에서 좌표가 어긋나지 않는가
///   4. 유휴 CPU &lt; 1%, 메모리 &lt; 150MB 를 만족하는가
///   5. godot#80098 흰색 깜빡임이 우리 환경에서 재현되는가, 회피책이 통하는가
///
/// 5번이 스파이크 설계 이유였다. passthrough 폴리곤을 매 프레임 갱신하는 모드와
/// 상태가 바뀔 때만 갱신하는 모드를 F3로 즉시 전환할 수 있게 해서, 플리커가
/// "Godot이 못 하는 것"인지 "우리가 잘못 부른 것"인지를 눈으로 갈랐다 (Go 판정).
/// </summary>
public partial class OverlayShell : Node2D, IShell, IPlatformServices
{
    /// <summary>클릭 영역을 신고된 것보다 조금 넓게 잡는다. 가장자리 클릭이 새는 걸 막는다.</summary>
    private const int HitPadding = 8;

    /// <summary>0 = 무제한. 상주 앱에서 부하와 반응성의 균형점을 찾기 위한 후보들.</summary>
    private static readonly int[] FpsCaps = { 60, 30, 10, 0 };

    private CursorLayer _cursor;
    private HelperInputSource _input;

    private Window _win;

    private Line2D _outline;
    private DebugHud _hud;

    /// <summary>
    /// 클릭 영역의 출처. <c>game/GameRoot</c> 가 신고한다 -
    /// shared/Contracts/IInteractiveArea.cs. 씬이 깨져서 못 찾은 경우에만 null 이고,
    /// 그때는 <see cref="BuildRegion"/>/<see cref="CurrentHitRect"/> 가 빈 값으로 답한다.
    /// </summary>
    private IInteractiveArea _content;

    /// <summary>스케일 적용 전 원본 창 크기. project.godot 의 viewport 크기다.</summary>
    private Vector2I _baseWindowSize;

    /// <summary>
    /// 셸이 띄운 작은 창들 (B10 친구 칸, <see cref="OpenSatellite"/>). 배율·투명도·숨기기를
    /// 메인 창과 같이 따르게 하고, 끌기 틱을 돌린다.
    /// </summary>
    private readonly System.Collections.Generic.List<SatelliteWindow> _satellites = new();

    /// <summary>
    /// 지금 세션의 옵션 값 (§7-4). 세이브에서 읽어와 이 객체 하나로 유지한다 -
    /// Scale/Opacity/PositionLocked 뿐 아니라 A6이 추가한 옵션(§7-4) 전부 여기 있다.
    /// 값이 바뀔 때마다 <see cref="PersistSettings"/>가 저장을 예약한다.
    /// 옵션 UI(A6)가 아직 시험용 debug 키(OverlayShell.DebugKeys.cs)와 같이 쓰인다.
    ///
    /// <b><see cref="_save"/>.Data.Settings 와 같은 객체다</b> - 별도 사본이 아니라
    /// 참조다. 셸이 여기를 고치면 그게 곧 세이브 객체가 고쳐진 것이고,
    /// <see cref="PersistSettings"/>는 "바뀌었다"고 알리기만 하면 된다.
    /// </summary>
    private SaveData.SettingsState _settings = new();

    /// <summary>
    /// 세이브 파일의 단일 소유자 (B5, shared/Contracts/ISaveStore.cs).
    /// 셸의 옵션과 게임의 나무/재화가 <b>같은 객체 하나</b>를 고친다 - 양쪽이 각자
    /// 읽고-고치고-쓰면 서로의 변경이 조용히 사라지기 때문이다.
    /// </summary>
    private SaveStore _save;

    /// <summary>
    /// A8 스팀. 스팀이 없어도 null 이 아니다 - 붙었는지는 <see cref="SteamService.IsAvailable"/>
    /// 가 답한다. 무인 실행에서만 null 이다 (아래 _Ready 참고).
    /// </summary>
    private SteamService _steam;

    /// <summary>
    /// <see cref="INetSession"/> 자리. <see cref="ShouldUseRealNet"/> 이 실물
    /// (<see cref="SteamNetSession"/>, A9)과 목 중 무엇을 꽂을지 정한다 - 경제와 같은
    /// 규칙이다. <see cref="_Ready"/> 에서 바꿔 끼우기 전까지도 널이 아니다
    /// (<see cref="IPlatformServices.Net"/> 주석의 약속).
    /// </summary>
    private INetSession _net = new MockNetSession();

    /// <summary>
    /// <see cref="IPlatformServices.Achievements"/> 로 넘길 실물. 스팀을 안 붙인
    /// 실행(무인 측정 등)에서는 <see cref="UnavailableAchievements"/> 가 들어온다 -
    /// <see cref="_steam"/> 이 널일 수 있는 것과 달리 이쪽은 절대 널이 아니다.
    /// </summary>
    private IAchievements _achievements = new UnavailableAchievements();

    /// <summary>
    /// <see cref="IPlatformServices.Economy"/> 자리 (docs/ECONOMY-SERVER.md).
    /// <see cref="ResolveEconomyMode"/> 가 실물(<see cref="EconomyClient"/>)과
    /// 목(<see cref="MockEconomyService"/>) 중 무엇을 꽂을지 정한다 - 릴리스는
    /// 항상 실물, 디버그는 기본 목(<c>--real-economy</c> 로 실물 강제)이다.
    /// <see cref="_net"/> 이 A9~A11 전까지 목인 것과 같은 규칙으로, 어느 쪽이든
    /// 게임 레이어에는 절대 널을 넘기지 않는다. <see cref="_Ready"/> 에서 채운다.
    /// </summary>
    private IEconomyService _economy;

    /// <summary>
    /// <see cref="IPlatformServices.Inventory"/> 자리. <see cref="_economy"/> 와
    /// 항상 짝을 맞춰 같은 모드(실물/목)로 켠다 - 하나만 실물이면 "구매는 서버에서
    /// 성공했는데 상점엔 안 가진 것으로 보인다"는 반쪽 상태가 된다(§5-6).
    /// 목일 때는 <see cref="MockEconomyService.LinkInventory"/> 로 서로 링크한다.
    /// </summary>
    private IInventoryService _inventoryService;

    /// <summary>--steam-selftest 로 떴는가. 스팀 연결만 확인하고 바로 종료한다.</summary>
    private bool _steamSelftest;

    private double _steamSelftestElapsed;

    /// <summary>무인 실행(--selftest, --report=, headless)인가. 레지스트리·세이브
    /// 파일·트레이 아이콘처럼 "우리 프로세스 밖으로 새어나가는" 부작용은 전부 이걸로 막는다.</summary>
    private bool _unattended;

    /// <summary>스팀을 통해 다시 띄우고 꺼지는 중인 인스턴스인가. 이때는 아무것도 안 만들었으므로
    /// <c>_Process</c>·<c>_ExitTree</c> 가 손대지 않고 빠진다 (Quit 은 몇 프레임 뒤에 끝난다).</summary>
    private bool _relaunching;

    // --- 토글 상태 ---
    private bool _updateEveryFrame;
    private bool _showOutline;
    private bool _lowPower = true;
    private int _fpsCapIndex;

    // --- 드래그 ---
    private bool _dragging;
    private Vector2I _dragOffset;
    private Vector2I _dragStart;

    // --- 클릭 통과 ---

    /// <summary>마지막으로 실제 적용한 passthrough 폴리곤. "바뀔 때만 쓰기"의 비교 대상이다.</summary>
    private Vector2[] _appliedRegion = Array.Empty<Vector2>();

    public override void _Ready()
    {
        InstallCrashLogging();

        // 스팀 밖에서 exe 를 직접 띄웠으면(자동 시작 등) 스팀을 통해 다시 뜨고 이
        // 인스턴스는 바로 끝낸다. 창·헬퍼·세이브를 만들기 전이어야 한다 - 곧 꺼질
        // 인스턴스가 세이브를 쓰거나 헬퍼를 남기면 안 된다. 릴리스에서만 (개발 실행은
        // 스팀으로 안 띄우므로 매번 꺼진다), 무인 실행 제외.
        if (!OS.IsDebugBuild() && !IsUnattendedRun() && SteamService.RelaunchThroughSteamIfNeeded())
        {
            GD.Print("[steam] 스팀 밖에서 실행됨 - 스팀을 통해 다시 띄우고 종료");
            _relaunching = true;
            GetTree().Quit();
            return;
        }

        _win = GetWindow();
        _baseWindowSize = _win.Size;
        _unattended = IsUnattendedRun();

        // per_pixel_transparency/allowed 는 project.godot 에서 이미 켰다.
        // 여기서 켜려고 하면 조용히 무시된다.
        _win.Borderless = true;
        _win.AlwaysOnTop = true;
        _win.Transparent = true;
        GetTree().Root.TransparentBg = true;
        EnforceTaskbarMinimize();

        // WEEK0-GODOT-VALIDATION.md §4 "창 닫기 = 종료가 아니라 트레이로". 이 창엔
        // OS 닫기 버튼이 없지만(Borderless), Alt+F4 등으로 OS 가 요청을 보낼 수 있다.
        _win.CloseRequested += OnCloseRequested;

        ApplyPowerSettings();

        // 창 상태 복원(RestoreWindowState)도, 게임 레이어도 이 객체를 본다.
        // 무인 실행에서는 읽기만 하고 디스크에 안 쓴다.
        _save = new SaveStore(_unattended);

        BuildScene();

        _options = new OptionsWindow { Name = "Options" };
        AddChild(_options);
        WireOptionsEvents();

        // A4 실물. 별도 헬퍼 프로세스로 전역 타건 수를 받는다 (docs/A4-GLOBAL-INPUT.md).
        _input = new HelperInputSource();
        if (!IsStoreShotRun())
        {
            _input.Start();
        }

        // A2 커서 추종 창. 여기서 IsSupported 가 false 로 나오면 기획서 §1.3 의
        // C안이 성립하지 않는다는 뜻이고, 그게 이 스파이크가 먼저 답해야 할 질문이다.
        _cursor = new CursorLayer(this);
        // 투명은 창 생성 시점에 정해지므로 이 인자만 다른 것들보다 먼저 읽는다.
        _cursor.Build(opaque: Array.IndexOf(OS.GetCmdlineUserArgs(), "--cursor-opaque") >= 0);

        // 커서 원숭이가 타건·클릭에 반응한다 (B17 §5). 횟수만 간다 - 헬퍼가 키를 모른다.
        _input.OnKeystrokes += _cursor.OnKeystrokes;

        // 메인 창을 클릭(활성화)하면 그 창이 "항상 위" 무리의 맨 위로 온다 - 커서 창을 바로 다시 올린다.
        GetWindow().FocusEntered += _cursor.BringToFront;

        // 창 배율/투명도/위치/옵션 전부 복원 (§7-1, §7-4). SetScale 이 안에서
        // ApplyPassthrough 까지 걸어주므로 별도로 부를 필요가 없다.
        RestoreWindowState();

        // A6 트레이 아이콘. 무인 실행에서는 안 만든다 - measure-renderers.ps1 이
        // 렌더러 A/B 를 네 번 돌리는 동안 시스템 트레이에 아이콘이 네 번 깜빡이면 안 된다.
        if (!_unattended)
        {
            SetupTray();
        }

        _fullscreenWatcher = new FullscreenWatcher();

        // A8 스팀 (docs/A8-STEAM.md). 무인 실행에서는 안 붙인다 - measure-renderers.ps1 이
        // 렌더러 A/B 를 네 번 돌리는 동안 스팀 친구 목록에 "게임 중"이 네 번 뜨면 안 되고,
        // 그 자체가 측정에 잡히는 부하다. --steam-selftest 만 예외로 연결을 확인한다.
        // --steam 은 무인 측정에서 스팀을 일부러 켜는 스위치다. A7 메모리 게이트가
        // 조건부 Go 인 상태라(docs/A7-PERF.md §4) "스팀이 얼마를 더 먹는가"를
        // 같은 조건에서 비교할 수 있어야 한다.
        _steamSelftest = Array.IndexOf(OS.GetCmdlineUserArgs(), "--steam-selftest") >= 0;
        bool forceSteam = Array.IndexOf(OS.GetCmdlineUserArgs(), "--steam") >= 0;
        if (!_unattended || _steamSelftest || forceSteam)
        {
            _steam = new SteamService();
            _steam.Start();

            // **무인 실행(--steam-selftest, --report=)은 게임에 진짜 도전과제를 넘기지 않는다.**
            // 게임 레이어는 무인 실행에서도 떠서, 스팀 통계가 오면 "놓친 해금 회수"
            // (GameRoot.SyncAchievements)가 돈다 - 2026-09-24 등록 확인용 자체 검사가 개발
            // 계정에 ACH_KEYSTROKES_1K 를 실제로 해금했다. 자체 검사는 _steam 을 직접 읽으므로
            // 영향이 없다.
            _achievements = _unattended ? new UnavailableAchievements() : _steam;
        }

        // A9 멀티 룸. 스팀 로비라 스팀을 안 붙인 실행(무인 측정)에서는 목으로 남는다.
        // --friends=N (무인 측정) 이면 스팀이 있어도 목이다 - 가짜 친구를 들여야 한다.
        if (ShouldUseRealNet() && _steam != null && MeasureFriendCount() == 0)
        {
            _net = new SteamNetSession(_steam);
        }

        // 경제(잔액·원장) + 인벤토리(스팀 소유권) - 항상 같은 모드로 짝을 맞춘다
        // (§5-6, 필드 주석 참고). 릴리스는 실물, 디버그는 --real-economy 로만 실물.
        if (ShouldUseRealEconomy())
        {
            _economy = new EconomyClient(ResolveEconomyUrl(), _steam);
            _inventoryService = new SteamInventoryService(_steam);
        }
        else
        {
            var mockEconomy = new MockEconomyService();
            var mockInventory = new MockInventoryService();

            // 실물에서는 서버 한 트랜잭션인 "구매→지급"을 목 둘로 재현하려면
            // 서로를 알아야 한다 (docs/ECONOMY-SERVER.md).
            mockEconomy.LinkInventory(mockInventory);

            _economy = mockEconomy;
            _inventoryService = mockInventory;
        }

        // 여기까지 와야 실물 5종이 전부 존재한다. 그래서 게임 레이어에 넘기는 것도
        // 여기가 처음 가능한 지점이다 - BuildScene() 에서 찾아둔 그 노드지만,
        // 그때는 _input/_cursor/_steam 이 아직 없었다.
        PrepareStoreShot();
        AttachPlatformToGame();

        var tick = new Timer { WaitTime = 0.5, Autostart = true };
        tick.Timeout += OnTick;
        AddChild(tick);

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--selftest") >= 0)
        {
            // 세이브 스키마가 기획서 §7-5 의 JSON 과 맞는지 확인하고 끝낸다.
            // 계약 문서와 코드가 갈라지는 것은 눈으로 안 잡히고, 을이 구현을
            // 끝낸 뒤에야 드러난다.
            GD.Print(SaveSchema.Describe());

            // 멀티 상태 전송 형식(A10)도 계약이라 같이 본다. 조작된 패킷을 거르는지까지.
            string codecFail = PlayerStateCodec.SelfTest();
            GD.Print(codecFail == null
                ? "[net] PlayerState 전송 형식 self-test PASS"
                : $"[net] PlayerState 전송 형식 self-test FAIL: {codecFail}");

            // 아이템 목록(items.json, B17)도 계약이다 - 게임·서버·스팀이 같은 파일을 읽는다.
            string itemsFail = ItemManifest.LoadError ?? ItemManifest.Validate();
            GD.Print(itemsFail == null
                ? $"[items] 아이템 목록 self-test PASS ({ItemManifest.Items.Count}개)"
                : $"[items] 아이템 목록 self-test FAIL: {itemsFail}");

            GetTree().Quit(SaveSchema.SelfTest() == null && codecFail == null && itemsFail == null ? 0 : 1);
            return;
        }

        ParseAutoReportArgs();
        StartMeasureFriends();
        StartStoreShot();
        StartMakeIcons();

        GD.Print($"[shell] ready. screens={DisplayServer.GetScreenCount()} cores={_perf.Cores}");
    }

    /// <summary>
    /// 무인 실행인가. 헤드리스(--selftest 는 보통 --headless 와 같이 온다) 뿐 아니라
    /// --report= 무인 측정도 포함한다 - 이 값이 true 인 동안은 세이브 파일, 레지스트리,
    /// 트레이 아이콘처럼 **프로세스 밖으로 새어나가는 부작용**을 전부 막는다.
    ///
    /// A3 실측에서 이걸 안 하니 헤드리스 selftest 가 헤드리스 환경의 엉뚱한 창 위치
    /// (예: -88,-88)를 유저의 진짜 세이브 파일에 덮어썼다 - 같은 실수를 레지스트리로
    /// 반복하지 않으려고 이번엔 처음부터 하나의 플래그로 묶었다.
    /// </summary>
    /// <summary>
    /// 처리 안 된 예외를 Godot 로그(<c>%APPDATA%/PunchMonkey/logs/</c>)에 남긴다 (A14).
    ///
    /// Godot 은 자기 콜백(<c>_Process</c> 등) 안의 예외는 잡아서 찍어 주지만,
    /// 백그라운드 스레드에서 터진 예외는 기록 없이 프로세스를 끝내고, 아무도
    /// await 하지 않은 Task 의 예외는 아무 흔적 없이 사라진다. 유저가 "그냥
    /// 꺼졌다" 고만 할 때 받아 볼 게 로그 파일뿐이라 여기서 붙잡는다.
    /// </summary>
    private static void InstallCrashLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            GD.PrintErr($"[crash] 처리 안 된 예외 (종료={e.IsTerminating}): {e.ExceptionObject}");

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            GD.PrintErr($"[crash] 관찰 안 된 Task 예외: {e.Exception}");
            e.SetObserved();
        };
    }

    private static bool IsUnattendedRun()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return true;
        }

        string[] args = OS.GetCmdlineUserArgs();

        // "...selftest" 로 끝나는 인자는 전부 무인으로 본다. --steam-selftest 를
        // 이름으로 하나씩 열거하다 A7 Release 측정 때 빠뜨린 게 드러났다 - 헤드리스로만
        // 돌리던 것을 창 있는 채로 돌리자 세이브 파일을 덮고 Run 키를 건드렸다.
        // 앞으로 --net-selftest 같은 게 늘어도 같은 실수가 반복되지 않게 접미사로 잡는다.
        return Array.Exists(args, a => a.EndsWith("selftest", StringComparison.Ordinal))
            || Array.Exists(args, a => a.StartsWith("--report=", StringComparison.Ordinal))
            || IsStoreShotRun()
            || IsMakeIconsRun();
    }

    /// <summary>
    /// 경제(<see cref="EconomyClient"/>) + 인벤토리(<see cref="SteamInventoryService"/>)
    /// 를 실물로 켤지 목으로 켤지 (docs/ECONOMY-SERVER.md §5-6).
    ///
    /// <b>릴리스 빌드는 항상 실물이다</b> - 출시본이 목(가짜 잔액·가짜 소유권)으로
    /// 돌면 안 된다. <b>디버그 빌드는 기본이 목이다</b> - <c>game/GameRoot.cs</c>
    /// 의 Shift+B(전 상품 지급)/Shift+R(초기화)/G(성장 앞당기기) 디버그 키가
    /// 전부 목 전용 캐스팅이라, 기본을 실물로 두면 그 키들이 조용히 죽는다.
    /// <c>--real-economy</c> 로 디버그 빌드에서도 실물을 강제로 켜서 배포된
    /// 서버·스팀 인벤토리를 직접 시험할 수 있다.
    /// </summary>
    private static bool ShouldUseRealEconomy() =>
        !OS.IsDebugBuild() || Array.IndexOf(OS.GetCmdlineUserArgs(), "--real-economy") >= 0;

    /// <summary>
    /// 멀티 세션을 실물(<see cref="SteamNetSession"/>)로 켤지 목으로 켤지 (A9).
    ///
    /// <see cref="ShouldUseRealEconomy"/> 와 같은 규칙이다 - 릴리스는 항상 실물,
    /// 디버그는 기본 목. 목에서만 도는 Shift+M(가짜 친구) 디버그 키가 있어서
    /// 룸 화면을 혼자 고칠 때는 목이 편하고, 친구와 실제로 붙어 볼 때
    /// <c>--real-net</c> 으로 켠다.
    /// </summary>
    private static bool ShouldUseRealNet() =>
        !OS.IsDebugBuild() || Array.IndexOf(OS.GetCmdlineUserArgs(), "--real-net") >= 0;

    /// <summary>
    /// <c>--economy-url=</c> 로 배포 URL 을 덮는다 - <c>wrangler dev</c> 로 띄운
    /// 로컬 서버를 겨냥할 때 쓴다. 없으면 <see cref="EconomyClient.DefaultBaseUrl"/>.
    /// </summary>
    private static string ResolveEconomyUrl()
    {
        const string Prefix = "--economy-url=";
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return arg[Prefix.Length..];
            }
        }

        return EconomyClient.DefaultBaseUrl;
    }

    // ------------------------------------------------------------------ 씬 구성

    /// <summary>
    /// 셸이 직접 만드는 노드를 짓는다. 히트 외곽선과 HUD 뿐이다 - 둘 다 디버그용이라
    /// 에디터에 둘 이유가 없다. 보이는 콘텐츠는 전부 <c>game/GameRoot.tscn</c> 쪽이고,
    /// <c>Shell.tscn</c> 이 그걸 자식으로 물고 있다 (docs/SCENE-ARCHITECTURE.md §2).
    /// </summary>
    private void BuildScene()
    {
        // game/GameRoot 가 자기 자신을 이 그룹에 등록해 두면 그걸 쓴다. 타입 이름이
        // 아니라 그룹으로 찾는 이유는 platform/이 game/의 구체 타입을 컴파일 타임에
        // 알면 안 되기 때문이다 (shared/Contracts/IInteractiveArea.cs 문서 참고).
        Node gameRootNode = GetTree().GetFirstNodeInGroup(SceneGroups.GameRoot);
        if (gameRootNode is IInteractiveArea area)
        {
            _content = area;
        }
        else
        {
            // B1 이 끝나서 자리표시자 마스코트(PlaceholderMascot)는 없앴다 -
            // Shell.tscn 이 game/GameRoot.tscn 을 자식으로 물고 있으므로 여기 오면
            // 씬이 깨진 것이다. 크래시 대신 창 전체가 클릭을 받게 두고(BuildRegion)
            // 시끄럽게 남긴다 - 조용히 클릭이 전부 통과하면 원인을 엉뚱한 데서 찾는다.
            GD.PrintErr("[shell] game/GameRoot 를 못 찾았다. Shell.tscn 의 자식 구성을 확인할 것"
                + " - 클릭 영역 없이 뜬다");
        }

        _outline = new Line2D
        {
            Name = "HitOutline",
            Width = 1.0f,
            DefaultColor = new Color(0.30f, 0.75f, 1.0f, 0.85f),
            Visible = false,
        };
        AddChild(_outline);

        _hud = new DebugHud { Name = "Hud" };
        AddChild(_hud);
    }

    // ------------------------------------------------------------------ IPlatformServices 실물

    IInputSource IPlatformServices.Input => _input;

    ISaveStore IPlatformServices.Save => _save;

    ICursorLayer IPlatformServices.Cursor => _cursor;

    IShell IPlatformServices.Shell => this;

    IAchievements IPlatformServices.Achievements => _achievements;

    INetSession IPlatformServices.Net => _net;

    IEconomyService IPlatformServices.Economy => _economy;

    IInventoryService IPlatformServices.Inventory => _inventoryService;

    /// <summary>
    /// 게임 레이어에 실물을 물려준다 (shared/Contracts/IPlatformServices.cs).
    ///
    /// <see cref="BuildScene"/> 이 <see cref="IInteractiveArea"/> 를 찾은 것과 같은
    /// 그룹 조회를 한 번 더 한다 - 두 계약은 방향만 같을 뿐 서로 독립이라
    /// (클릭 영역만 신고하고 실물은 안 받는 구현도 문법상 가능하다) 한쪽 캐스팅
    /// 결과를 다른 쪽에 재활용하지 않는다.
    ///
    /// <b>이게 없으면 조용히 아무 일도 안 일어난다.</b> 실제로 B1 직후가 그 상태였다 -
    /// <c>HelperInputSource.OnKeystrokes</c> 의 구독자가 0명이라 전역 타건이
    /// 게임에 닿지 않았고, 대신 게임이 자기 목을 창 포커스로 먹이고 있어서
    /// "되는 것처럼" 보였다. 그래서 여기서는 성공/실패를 반드시 로그로 남긴다.
    /// </summary>
    private void AttachPlatformToGame()
    {
        Node gameRootNode = GetTree().GetFirstNodeInGroup(SceneGroups.GameRoot);

        if (gameRootNode is not IPlatformConsumer consumer)
        {
            // 자리표시자만 있는 실행(게임 씬 없이 셸만 띄운 경우)에서는 정상이다.
            GD.Print("[shell] IPlatformConsumer 없음 - 실물을 넘길 곳이 없다");
            return;
        }

        consumer.AttachPlatform(this);

        // 어떤 자리가 실물이고 어떤 자리가 목인지 기동 로그 한 줄로 남긴다.
        GD.Print($"[shell] 실물 전달 완료 - input={_input.Status}"
            + $" cursor={(_cursor.IsSupported ? "실물" : "미지원")}"
            + $" ach={(_steam == null ? "없음" : _steam.Status)} net={(_net is SteamNetSession ? "실물(스팀 로비)" : "목")}"
            + $" economy={(_economy is MockEconomyService ? "목" : "실물")}"
            + $" inventory={(_inventoryService is MockInventoryService ? "목" : "실물")}");
    }

    // ------------------------------------------------------------------ IShell 실물

    /// <summary>게임 화면의 [옵션] 버튼 (<see cref="IShell.ToggleOptions"/>). 트레이 "설정" 과 같은 창을 연다.</summary>
    void IShell.ToggleOptions() => ToggleOptionsWindow();

    /// <summary>
    /// 창 배율. 옵션 창(A6) "크기" 슬라이더, debug 키 <c>[</c>/<c>]</c>로 시험한다.
    ///
    /// 루트 <see cref="Node2D.Scale"/>을 바꿔서 게임 콘텐츠/외곽선을 같이 키운다.
    /// <see cref="DebugHud"/>/<see cref="OptionsWindow"/>는 <c>CanvasLayer</c>라 이
    /// 노드의 Transform/Modulate를 물려받지 않는다 - 배율/투명도를 바꿔도 HUD와
    /// 옵션 UI는 항상 또렷하게 남는다 (docs/A3-SHELL-MODULE.md §1). 창 크기도 같이
    /// 키우는 이유는, 안 키우면 커진 나무/원숭이가 창 밖으로 잘려서 클릭 영역
    /// (passthrough 폴리곤)도 창 밖으로 나가 못 먹는 부분이 생기기 때문이다.
    /// </summary>
    public void SetScale(float s)
    {
        // 상한/하한은 실제 체감으로 잡은 자리 표시자다 - "창이 사라지거나 화면을
        // 뒤덮는" 극단만 막는다. A6 옵션 슬라이더도 이 범위로 맞췄다(OptionsWindow).
        _settings.Scale = Mathf.Clamp(s, 0.5f, 2.0f);
        Scale = Vector2.One * _settings.Scale;
        foreach (SatelliteWindow satellite in _satellites)
        {
            satellite.SetScale(_settings.Scale);
        }

        _win.Size = new Vector2I(
            Mathf.RoundToInt(_baseWindowSize.X * _settings.Scale),
            Mathf.RoundToInt(_baseWindowSize.Y * _settings.Scale));

        // 마스코트 크기가 바뀌었으니 클릭 영역도 다시 계산해야 한다.
        ApplyPassthrough(force: true);
    }

    /// <summary>
    /// 창 투명도. 옵션 창 "투명도" 슬라이더, debug 키 <c>-</c>/<c>=</c>로 시험한다.
    ///
    /// 하한을 0 이 아니라 0.1로 잡은 이유: 완전 투명(0)까지 허용하면 유저가 옵션
    /// 창조차 못 찾을 만큼 자기 자신을 안 보이게 만들 수 있다. 상주 앱에서
    /// "설정으로 자기 자신을 못 보이게 만들고 되돌릴 방법이 없다"는 실제로 발생하는
    /// 사고 패턴이다.
    /// </summary>
    public void SetOpacity(float a)
    {
        _settings.Opacity = Mathf.Clamp(a, 0.1f, 1.0f);
        Modulate = new Color(1f, 1f, 1f, _settings.Opacity);
        foreach (SatelliteWindow satellite in _satellites)
        {
            satellite.SetOpacity(_settings.Opacity);
        }
    }

    /// <summary>
    /// 클릭 통과 On/Off. 옵션의 "위치 잠금"이 이것이다 (§7-1, §7-4).
    ///
    /// on(잠금) = 게임 콘텐츠 영역만 클릭을 받고 나머지는 통과 - 실수로 안 끌리고,
    /// 뒤에 있는 다른 창 작업도 안 막는다. off(잠금 해제) = 창 전체가 클릭을 받아서
    /// 작은 히트박스를 정확히 안 눌러도 어디서든 끌 수 있다 - 처음
    /// 위치를 잡을 때 편하라고 두는 탈출구다.
    /// </summary>
    public void SetClickThrough(bool on)
    {
        _settings.PositionLocked = on;
        ApplyPassthrough(force: true);
    }

    /// <summary>
    /// 창을 놓을 수 있는 영역. 멀티모니터·작업표시줄을 고려한 현재 화면의 작업 영역이다.
    ///
    /// 개발 PC는 모니터가 3대고 하나는 X 좌표가 음수다(shared/Contracts/IShell.cs 문서
    /// 참고). <c>ScreenGetUsableRect</c>는 절대 데스크톱 좌표를 그대로 돌려주므로
    /// 음수 원점도 별도 처리 없이 맞는다 - 게임 레이어가 (0,0)을 원점으로 가정하지만
    /// 않으면 된다.
    /// </summary>
    public Rect2I GetSafeArea() =>
        DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());

    /// <inheritdoc cref="IShell.OpenSatellite"/>
    public ISatelliteWindow OpenSatellite(
        string name, Vector2I contentSize, Vector2I? savedPosition, params Vector2[] preferredOffsets)
    {
        var size = new Vector2I(
            Mathf.RoundToInt(contentSize.X * _settings.Scale),
            Mathf.RoundToInt(contentSize.Y * _settings.Scale));

        var satellite = new SatelliteWindow(
            this, name, contentSize, PlaceSatellite(size, savedPosition, preferredOffsets), _settings.Scale,
            closed => _satellites.Remove(closed));
        satellite.SetOpacity(_settings.Opacity);
        _satellites.Add(satellite);
        Callable.From(_cursor.BringToFront).CallDeferred();   // 새 창이 커서 창 위로 뜬다 - 뜬 다음 다시 올린다

        // 메인 창이 지금 숨겨져 있으면(트레이·전체화면) 같이 숨긴 채로 시작한다.
        satellite.SetVisible(_shellWindowVisible);
        return satellite;
    }

    /// <summary>
    /// 위성 창의 처음 자리. 기억해 둔 자리가 지금 모니터에 있으면 거기(모니터를 뺐거나
    /// 해상도가 바뀌었으면 버린다 - 메인 창 복원과 같은 규칙), 아니면 메인 창 기준 후보를
    /// 차례로 보고 화면에 다 들어오는 첫 자리.
    /// </summary>
    private Vector2I PlaceSatellite(Vector2I size, Vector2I? saved, Vector2[] offsets)
    {
        if (saved is { } s && IsWithinAnyScreen(s))
        {
            return s;
        }

        Rect2I usable = GetSafeArea();
        Vector2I first = _win.Position;
        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2I candidate = _win.Position + (Vector2I)(offsets[i] * _settings.Scale).Round();
            if (i == 0)
            {
                first = candidate;
            }

            if (usable.Encloses(new Rect2I(candidate, size)))
            {
                return candidate;
            }
        }

        // 다 안 맞으면 첫 후보를 화면 안으로 밀어 넣는다.
        return new Vector2I(
            Mathf.Clamp(first.X, usable.Position.X, Math.Max(usable.Position.X, usable.End.X - size.X)),
            Mathf.Clamp(first.Y, usable.Position.Y, Math.Max(usable.Position.Y, usable.End.Y - size.Y)));
    }

    /// <summary>
    /// 세이브에서 옵션 전부를 복원한다 (§7-1 "위치·크기 저장, 재실행 시 복원", §7-4).
    ///
    /// 첫 실행(세이브 없음)이거나, 저장된 위치가 지금 모니터 구성 어디에도 없으면
    /// (모니터가 빠졌거나 해상도가 바뀌었거나) 기본 배치로 폴백한다. <see cref="GetSafeArea"/>
    /// 문서가 약속한 "모니터가 사라진 경우의 폴백은 플랫폼 레이어가 책임진다"가 이것이다 -
    /// 게임 레이어는 이 판단을 몰라도 된다.
    /// </summary>
    private void RestoreWindowState()
    {
        _settings = _save.Data.Settings;

        SetScale(_settings.Scale);
        SetOpacity(_settings.Opacity);
        SetClickThrough(_settings.PositionLocked);
        ApplyVisibility();

        // 레지스트리를 세이브 값과 맞춘다. 무인 실행에서는 절대 안 한다 - 유저의
        // 실제 Windows 시작 프로그램 목록을 자동 측정/셀프테스트가 건드리면 안 된다.
        if (!_unattended)
        {
            Autostart.SetEnabled(_settings.Autostart);
        }

        var savedPos = new Vector2I(_settings.Pos[0], _settings.Pos[1]);

        if (_save.HadFile && IsWithinAnyScreen(savedPos))
        {
            _win.Position = savedPos;
        }
        else
        {
            MoveToScreen(DisplayServer.WindowGetCurrentScreen());
        }
    }

    private static bool IsWithinAnyScreen(Vector2I pos)
    {
        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            if (DisplayServer.ScreenGetUsableRect(i).HasPoint(pos))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 지금 옵션 전부와 창 위치를 세이브 객체에 반영하고 저장을 예약한다.
    ///
    /// <b>디스크를 직접 건드리지 않는다.</b> B5 부터 이 파일에는 게임 상태(나무·재화)도
    /// 같이 들어 있고, 셸이 자기 몫만 들고 통째로 덮어쓰면 그 사이의 게임 변경이
    /// 사라진다. 소유자를 하나로 묶은 것이 <see cref="SaveStore"/>다.
    ///
    /// <see cref="_settings"/>는 세이브 객체 안의 <c>Settings</c> 그 자체이므로
    /// 여기서 따로 대입할 것이 창 위치 한 줄뿐이다.
    /// </summary>
    private void PersistSettings()
    {
        _settings.Pos = new[] { _win.Position.X, _win.Position.Y };
        _save.MarkDirty();
    }

    // ------------------------------------------------------------------ 클릭 통과

    /// <summary>
    /// 창 픽셀 좌표 기준의 클릭 수신 폴리곤.
    /// 빈 배열을 넘기면 passthrough 가 꺼지고 창 전체가 마우스를 가로챈다(Godot 기본 동작).
    /// </summary>
    private Vector2[] BuildRegion()
    {
        // _content 가 없으면 신고된 클릭 영역도 없다 - BuildScene 이 이미 에러를 찍었다.
        if (!_settings.PositionLocked || _content == null)
        {
            return Array.Empty<Vector2>();
        }

        Rect2 r = CurrentHitRect();
        return new[]
        {
            r.Position,
            new Vector2(r.End.X, r.Position.Y),
            r.End,
            new Vector2(r.Position.X, r.End.Y),
        };
    }

    /// <summary>
    /// 클릭을 받을 창-픽셀 좌표 기준 사각형. 값의 출처는 <see cref="_content"/>
    /// (게임 레이어가 신고한 것, shared/Contracts/IInteractiveArea.cs) 이고,
    /// 여기서는 플랫폼 몫(배율 적용, 클릭 여백)만 더한다.
    ///
    /// <see cref="IInteractiveArea.GetClickableBounds"/>는 셸 루트의 로컬 좌표계
    /// 값을 돌려준다. <see cref="SetScale"/>이 루트에 <see cref="Node2D.Scale"/>을
    /// 걸어 두므로, passthrough 에 넘길 **창 픽셀** 좌표를 얻으려면
    /// <see cref="SaveData.SettingsState.Scale"/>을 직접 곱해야 한다 - Godot 렌더링은
    /// 이 배율을 자동으로 반영하지만, Win32 <c>SetWindowRgn</c>에 넘기는 이 좌표는
    /// 그 파이프라인을 안 거친다.
    /// </summary>
    private Rect2 CurrentHitRect()
    {
        if (_content == null)
        {
            return new Rect2();
        }

        Rect2 local = _content.GetClickableBounds();
        var scaled = new Rect2(local.Position * _settings.Scale, local.Size * _settings.Scale);
        return scaled.Grow(HitPadding);
    }

    /// <summary>
    /// force=false 면 폴리곤이 실제로 바뀐 경우에만 DisplayServer 를 건드린다.
    /// 이 "바뀔 때만 쓰기"가 godot#80098 플리커 회피 가설이고, F3로 끄면 매 프레임 쓰기가 된다.
    /// _regionWrites 카운터로 두 모드의 실제 호출 횟수 차이를 확인할 수 있다.
    /// </summary>
    private void ApplyPassthrough(bool force)
    {
        Vector2[] region = BuildRegion();

        if (!force && SameRegion(region, _appliedRegion))
        {
            return;
        }

        DisplayServer.WindowSetMousePassthrough(region);
        _appliedRegion = region;
        _regionWrites++;

        if (_showOutline)
        {
            RefreshOutline(region);
        }
    }

    private static bool SameRegion(Vector2[] a, Vector2[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].IsEqualApprox(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshOutline(Vector2[] region)
    {
        if (region.Length == 0)
        {
            _outline.Points = Array.Empty<Vector2>();
            return;
        }

        var loop = new Vector2[region.Length + 1];
        region.CopyTo(loop, 0);
        loop[^1] = region[0];
        _outline.Points = loop;
    }

    // ------------------------------------------------------------------ 프레임 루프

    public override void _Process(double delta)
    {
        if (_relaunching)
        {
            return;
        }

        if (_dragging)
        {
            // 마우스가 창 밖으로 나가면 모션 이벤트가 끊긴다. 그래서 위치는 폴링으로 따라간다.
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                EndDrag();
            }
            else
            {
                _win.Position = DisplayServer.MouseGetPosition() - _dragOffset;
            }
        }

        if (_debugHideRemaining > 0.0)
        {
            _debugHideRemaining -= delta;
            if (_debugHideRemaining <= 0.0)
            {
                _userWantsVisible = true;
                ApplyVisibility();
            }
        }

        ApplyPassthrough(force: _updateEveryFrame);
        _cursor.Tick(delta);
        _input.Tick(delta);

        // 스팀 콜백은 우리가 펌프를 돌려야 도착한다. 못 붙은 상태면 여기서 재시도까지 한다.
        _steam?.Tick(delta);

        // 친구 칸 창 끌기 (B10). 목록이 틱 안에서 줄 수 있어(닫기) 복사해서 돈다.
        foreach (SatelliteWindow satellite in _satellites.ToArray())
        {
            satellite.Tick();
        }

        // 멀티 룸의 콜백 등록·타수 전송·변경 알림 묶기. 스팀 콜백 펌프 바로 뒤라
        // 이번 프레임에 도착한 로비 소식이 같은 프레임에 화면까지 간다.
        (_net as SteamNetSession)?.Tick(delta);
        (_net as MockNetSession)?.Tick(delta);

        // 목일 때만 성장 시계를 우리가 돌린다 - 실물은 서버(요청 시점 재계산)가 시간의
        // 주인이라 틱이 없다(IEconomyService 계약에 Tick 이 없는 이유와 같다).
        if (_economy is MockEconomyService mockEconomy)
        {
            mockEconomy.Tick(delta);
        }

        _save.Tick(delta);

        TickDiagnostics(delta);
    }

    private void OnTick()
    {
        SampleDiagnostics();
        CheckFullscreen();
        EnforceTaskbarMinimize();

        // 커서 장식은 늘 맨 위 (B17) - 메인 창·친구 창도 "항상 위" 라 활성화 순서로 뒤집힌다.
        _cursor.BringToFront();
    }

    // ------------------------------------------------------------------ 입력

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_relaunching || @event is not InputEventMouseButton mb)
        {
            return;
        }

        // 좌클릭 말고도 세는 이유: 커서 장식 창의 클릭 통과를 검증하려면 마우스 입력
        // **경로마다** 따로 확인해야 한다. 좌클릭만 통과하고 우클릭·휠이 막히는 실패가
        // 실제로 가능하고, 그건 "조금 불편"이 아니라 출시 불가다.
        switch (mb.ButtonIndex)
        {
            case MouseButton.Left:
                if (mb.Pressed)
                {
                    BeginDrag();
                }
                else
                {
                    EndDrag();
                }

                break;

            case MouseButton.Right:
                if (mb.Pressed)
                {
                    _rclicks++;
                }

                break;

            case MouseButton.WheelUp:
            case MouseButton.WheelDown:
                if (mb.Pressed)
                {
                    _wheels++;
                }

                break;

            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    private void BeginDrag()
    {
        _dragging = true;
        _clicks++;
        _dragOffset = DisplayServer.MouseGetPosition() - _win.Position;
        _dragStart = _win.Position;

        // 드래그 중에는 창 전체가 마우스를 받게 한다.
        // 그러지 않으면 커서가 히트 영역을 벗어나는 순간 드래그가 끊긴다.
        DisplayServer.WindowSetMousePassthrough(Array.Empty<Vector2>());
        _appliedRegion = Array.Empty<Vector2>();
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;

        // 드래그로 창이 실제로 움직였는가. 누르고 떼기만 한 것과 구분한다.
        if (_win.Position != _dragStart)
        {
            _drags++;
            PersistSettings();
        }

        ApplyPassthrough(force: true);
    }

    // ------------------------------------------------------------------ 창 배치 / 부하

    private void ApplyPowerSettings()
    {
        OS.LowProcessorUsageMode = _lowPower;
        OS.LowProcessorUsageModeSleepUsec = 6900;
        Engine.MaxFps = FpsCaps[_fpsCapIndex];
    }

    /// <summary>지정한 모니터의 작업 영역 우하단에 창을 붙인다.</summary>
    private void MoveToScreen(int screen)
    {
        Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
        _win.Position = new Vector2I(
            usable.Position.X + usable.Size.X - _win.Size.X - 24,
            usable.Position.Y + usable.Size.Y - _win.Size.Y - 24);
    }

    public override void _ExitTree()
    {
        if (_relaunching)
        {
            return;
        }

        // 옵션 전부와 창 위치를 여기서 한 번 더 남긴다. 드래그 없이 바로 끈 세션도
        // 다음 실행에서 지금 상태(예: [ 로 바꾼 스케일)를 복원하려면 필요하다.
        PersistSettings();

        // 게임 레이어(GameRoot._ExitTree)가 자기 상태를 먼저 써 넣는다 - Godot 은
        // 자식의 _ExitTree 를 부모보다 먼저 부른다. 여기서 디스크로 내보낸다.
        _save.FlushNow();

        // RawInput 등록과 WndProc 후킹을 되돌린다. 상주 앱이라 프로세스가
        // 오래 살고, 남겨두면 다음 실행에서 무엇이 원인인지 알기 어려워진다.
        _input?.Dispose();

        // 트레이 아이콘을 지운다 - 안 지우면 프로세스가 죽어도 재부팅 전까지
        // "죽은" 아이콘이 트레이에 남아 있다가 클릭할 때야 사라지는 흔한 버그가 난다.
        _tray?.Dispose();

        // 로비를 먼저 나간다 - 방장이면 룸 타수를 로비에 맡겨야 다시 들어올 때 이어진다.
        // 스팀을 놓은 뒤에는 로비 API 를 부를 수 없다.
        (_net as IDisposable)?.Dispose();

        // 스팀을 안 놓으면 친구 목록에 죽은 프로세스가 한동안 "게임 중"으로 남는다.
        _steam?.Dispose();

        // 실물일 때만 놓을 게 있다 - HttpClient(EconomyClient), 스팀 콜백 핸들
        // (SteamInventoryService). 목은 IDisposable 이 아니다.
        (_economy as IDisposable)?.Dispose();
        (_inventoryService as IDisposable)?.Dispose();
    }
}
