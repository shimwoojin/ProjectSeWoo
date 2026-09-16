using System;
using System.Runtime.InteropServices;
using Godot;
using ProjectSeWoo.Shared;

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
public partial class OverlayShell : Node2D, IShell
{
    /// <summary>클릭 영역을 스프라이트보다 조금 넓게 잡는다. 가장자리 클릭이 새는 걸 막는다.</summary>
    private const int HitPadding = 8;

    private const float MascotScale = 1.5f;

    /// <summary>0 = 무제한. 상주 앱에서 부하와 반응성의 균형점을 찾기 위한 후보들.</summary>
    private static readonly int[] FpsCaps = { 60, 30, 10, 0 };

    /// <summary>H(debug 숨김)가 스스로 돌아오기까지의 시간. 숨은 창은 키를 못 받는다.</summary>
    private const double DebugHideSeconds = 3.0;

    private readonly PerfProbe _perf = new();
    private CursorLayer _cursor;
    private HelperInputSource _input;

    private Window _win;

    /// <summary>game/GameRoot 가 없을 때만 만든다. 있으면 null로 남는다 - <see cref="_content"/> 참고.</summary>
    private Sprite2D _mascot;

    private Line2D _outline;
    private DebugHud _hud;

    /// <summary>
    /// 클릭 영역의 출처. <see cref="PlaceholderMascot"/>(자리표시자) 또는
    /// game/GameRoot(실물) 둘 중 하나가 항상 들어있다 - shared/Contracts/IInteractiveArea.cs.
    /// </summary>
    private IInteractiveArea _content;

    /// <summary>스케일 적용 전 원본 창 크기. project.godot 의 viewport 크기다.</summary>
    private Vector2I _baseWindowSize;

    /// <summary>
    /// 지금 세션의 옵션 값 (§7-4). 세이브에서 읽어와 이 객체 하나로 유지한다 -
    /// Scale/Opacity/PositionLocked 뿐 아니라 A6이 추가한 옵션(§7-4) 전부 여기 있다.
    /// 값이 바뀔 때마다 <see cref="PersistSettings"/>가 이 객체를 그대로 디스크에 쓴다.
    /// 옵션 UI(A6)가 아직 시험용 debug 키(아래 _UnhandledKeyInput)와 같이 쓰인다.
    /// </summary>
    private SaveData.SettingsState _settings = new();

    // --- A6: 표시 여부는 "유저가 원하는가"와 "전체화면 앱이 떠서 자동으로 숨겼는가"
    // 둘의 조합이다 (ApplyVisibility). 커서 장식도 같은 조합을 따르되 옵션
    // (CursorEnabled)까지 하나 더 곱해진다. ---
    private bool _userWantsVisible = true;
    private bool _autoHiddenForFullscreen;

    /// <summary>
    /// 셸 창이 지금 실제로 보이는가. <see cref="_userWantsVisible"/> 등이 "원하는 것"이라면
    /// 이쪽은 <see cref="SetShellWindowVisible"/>가 OS 에 물어서 되읽은 "실제"다.
    /// </summary>
    private bool _shellWindowVisible = true;

    /// <summary>창 숨김 미지원 플랫폼 경고를 한 번만 찍기 위한 표식. 0.5초 틱마다 도배하면 안 된다.</summary>
    private bool _hideUnsupportedLogged;

    /// <summary>H(debug 숨김)의 남은 시간. 0 이하면 쉬는 중이다.</summary>
    private double _debugHideRemaining;

    private TrayIcon _tray;
    private OptionsWindow _options;
    private FullscreenWatcher _fullscreenWatcher;

    /// <summary>
    /// A8 스팀. 스팀이 없어도 null 이 아니다 - 붙었는지는 <see cref="SteamService.IsAvailable"/>
    /// 가 답한다. 무인 실행에서만 null 이다 (아래 _Ready 참고).
    /// </summary>
    private SteamService _steam;

    /// <summary>--steam-selftest 로 떴는가. 스팀 연결만 확인하고 바로 종료한다.</summary>
    private bool _steamSelftest;

    private double _steamSelftestElapsed;

    /// <summary>무인 실행(--selftest, --report=, headless)인가. 레지스트리·세이브
    /// 파일·트레이 아이콘처럼 "우리 프로세스 밖으로 새어나가는" 부작용은 전부 이걸로 막는다.</summary>
    private bool _unattended;

    // --- A5 커서 장착 debug 데모. B6(상점/장착 UI)가 나오기 전까지 Key2/3/4로
    // 슬롯별 자리표시자 에셋을 순환한다. null 은 "빈 슬롯". ---
    private static readonly string[] DemoAssetIds = { null, "demo_a", "demo_b", "demo_c" };
    private readonly int[] _demoEquipIndex = new int[3];

    // --- 토글 상태 ---
    private bool _updateEveryFrame;
    private bool _showOutline;
    private bool _lowPower = true;
    private int _fpsCapIndex;

    // --- 드래그 ---
    private bool _dragging;
    private Vector2I _dragOffset;
    private Vector2I _dragStart;

    // --- 무인 측정 모드 (tools/measure-renderers.ps1) ---
    private string _autoReportPath;
    private double _autoWarmupSec = 15.0;
    private double _autoDurationSec;
    private double _autoElapsed;
    private bool _autoWarmedUp;
    private bool _autoCursor;
    private bool _autoCursorSim;
    private bool _autoCursorNoPass;
    private string _autoCursorEquip;
    private string _autoCursorIntervalMs;
    private string _autoCursorMode;

    // --- 계측 ---
    private Vector2[] _appliedRegion = Array.Empty<Vector2>();
    private long _regionWrites;
    private int _clicks;
    private int _rclicks;
    private int _wheels;
    private int _drags;
    private double _uptime;
    private double _punch;

    public override void _Ready()
    {
        _win = GetWindow();
        _baseWindowSize = _win.Size;
        _unattended = IsUnattendedRun();

        // per_pixel_transparency/allowed 는 project.godot 에서 이미 켰다.
        // 여기서 켜려고 하면 조용히 무시된다.
        _win.Borderless = true;
        _win.AlwaysOnTop = true;
        _win.Transparent = true;
        GetTree().Root.TransparentBg = true;

        // WEEK0-GODOT-VALIDATION.md §4 "창 닫기 = 종료가 아니라 트레이로". 이 창엔
        // OS 닫기 버튼이 없지만(Borderless), Alt+F4 등으로 OS 가 요청을 보낼 수 있다.
        _win.CloseRequested += OnCloseRequested;

        ApplyPowerSettings();
        BuildScene();

        _options = new OptionsWindow { Name = "Options" };
        AddChild(_options);
        WireOptionsEvents();

        // A4 실물. 별도 헬퍼 프로세스로 전역 타건 수를 받는다 (docs/A4-GLOBAL-INPUT.md).
        _input = new HelperInputSource();
        _input.Start();

        // A2 커서 추종 창. 여기서 IsSupported 가 false 로 나오면 기획서 §1.3 의
        // C안이 성립하지 않는다는 뜻이고, 그게 이 스파이크가 먼저 답해야 할 질문이다.
        _cursor = new CursorLayer(this);
        // 투명은 창 생성 시점에 정해지므로 이 인자만 다른 것들보다 먼저 읽는다.
        _cursor.Build(opaque: Array.IndexOf(OS.GetCmdlineUserArgs(), "--cursor-opaque") >= 0);

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
        }

        var tick = new Timer { WaitTime = 0.5, Autostart = true };
        tick.Timeout += OnTick;
        AddChild(tick);

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--selftest") >= 0)
        {
            // 세이브 스키마가 기획서 §7-5 의 JSON 과 맞는지 확인하고 끝낸다.
            // 계약 문서와 코드가 갈라지는 것은 눈으로 안 잡히고, 을이 구현을
            // 끝낸 뒤에야 드러난다.
            GD.Print(SaveSchema.Describe());
            GetTree().Quit(SaveSchema.SelfTest() == null ? 0 : 1);
            return;
        }

        ParseAutoReportArgs();

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
            || Array.Exists(args, a => a.StartsWith("--report=", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>--steam-selftest</c> 진행. A8 이 실제로 붙는지 사람 눈 없이 확인하는 경로다.
    ///
    /// 초기화는 동기지만 **도전과제 통계는 콜백으로 비동기로 온다** - 그래서 바로
    /// 판정하지 못하고 몇 프레임 펌프를 돌려야 한다. 스키마 <c>--selftest</c> 가
    /// 한 줄로 끝나는 것과 다른 이유가 이것이다.
    ///
    /// 종료 코드: 0 = 초기화 성공, 1 = 실패(사유는 Status 에 찍힌다).
    /// **스팀이 안 떠 있는 것도 1 이다** - 이 자체 검사는 "붙을 수 있는 환경인가" 를
    /// 묻는 것이고, 앱의 정상 동작 여부와는 별개다 (스팀 없이도 앱은 돈다).
    /// </summary>
    private void TickSteamSelftest(double delta)
    {
        const double TimeoutSec = 10.0;

        _steamSelftestElapsed += delta;

        bool done = _steam != null && _steam.IsAvailable;
        if (!done && _steamSelftestElapsed < TimeoutSec)
        {
            return;
        }

        GD.Print("--- steam selftest ---");
        GD.Print($"status      : {_steam?.Status ?? "(서비스 없음)"}");
        GD.Print($"initialized : {Verdict(_steam?.IsInitialized == true)}");
        GD.Print($"stats/ach   : {Verdict(_steam?.IsAvailable == true)}");
        GD.Print($"appid       : {_steam?.AppId}");
        GD.Print($"steam id    : {_steam?.SelfId}");
        GD.Print($"persona     : {_steam?.PersonaName}");
        GD.Print($"elapsed     : {_steamSelftestElapsed:F1}s");

        // 도전과제 스텁 배선 확인. **읽기만 한다** - Unlock 을 여기서 부르면 나중에
        // 진짜 앱 ID 로 이 검사를 돌렸을 때 실제 도전과제가 해금돼 버린다.
        // appid=480 에서는 우리 이름이 등록돼 있지 않으므로 전부 false 가 정상이다.
        GD.Print($"ach ids     : {AchievementIds.KeystrokeMilestones.Length + 1}개 정의됨");
        GD.Print($"  {AchievementIds.Collection100} = {_steam?.IsUnlocked(AchievementIds.Collection100)}");
        foreach ((string id, int threshold) in AchievementIds.KeystrokeMilestones)
        {
            GD.Print($"  {id} ({threshold:N0}타) = {_steam?.IsUnlocked(id)}");
        }

        _steamSelftest = false;
        GetTree().Quit(_steam?.IsInitialized == true ? 0 : 1);
    }

    // ------------------------------------------------------------------ 씬 구성

    /// <summary>
    /// 씬을 코드로 짓는다. .tscn 은 루트 노드 하나뿐이다.
    /// 스파이크 단계에서는 이게 낫다 - 에디터를 안 거쳐도 상태 전부가 한 파일에서 읽힌다.
    /// 게임 본편으로 넘어가면 당연히 에디터에서 씬을 짜야 한다.
    /// </summary>
    private void BuildScene()
    {
        // game/GameRoot 가 자기 자신을 이 그룹에 등록해 두면 그걸 쓰고, 없으면
        // 자리표시자 마스코트로 채운다. 타입 이름이 아니라 그룹으로 찾는 이유는
        // platform/이 game/의 구체 타입을 컴파일 타임에 알면 안 되기 때문이다
        // (shared/Contracts/IInteractiveArea.cs 문서 참고).
        Node gameRootNode = GetTree().GetFirstNodeInGroup(SceneGroups.GameRoot);
        if (gameRootNode is IInteractiveArea area)
        {
            _content = area;
            GD.Print("[shell] game/GameRoot 발견 - 자리표시자 마스코트를 안 만든다");
        }
        else
        {
            var texture = GD.Load<Texture2D>("res://icon.svg");

            _mascot = new Sprite2D
            {
                Name = "Mascot",
                Texture = texture,
                Centered = true,
                Scale = Vector2.One * MascotScale,
                Position = new Vector2(_win.Size.X / 2f, 160f),
            };
            AddChild(_mascot);
            _content = new PlaceholderMascot(_mascot);
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

    // ------------------------------------------------------------------ IShell 실물

    /// <summary>
    /// 창 배율. 옵션 창(A6) "크기" 슬라이더, debug 키 <c>[</c>/<c>]</c>로 시험한다.
    ///
    /// 루트 <see cref="Node2D.Scale"/>을 바꿔서 마스코트/외곽선을 같이 키운다.
    /// <see cref="DebugHud"/>/<see cref="OptionsWindow"/>는 <c>CanvasLayer</c>라 이
    /// 노드의 Transform/Modulate를 물려받지 않는다 - 배율/투명도를 바꿔도 HUD와
    /// 옵션 UI는 항상 또렷하게 남는다 (docs/A3-SHELL-MODULE.md §1). 창 크기도 같이
    /// 키우는 이유는, 안 키우면 커진 마스코트가 창 밖으로 잘려서 클릭 영역
    /// (passthrough 폴리곤)도 창 밖으로 나가 못 먹는 부분이 생기기 때문이다.
    /// </summary>
    public void SetScale(float s)
    {
        // 상한/하한은 실제 체감으로 잡은 자리 표시자다 - "창이 사라지거나 화면을
        // 뒤덮는" 극단만 막는다. A6 옵션 슬라이더도 이 범위로 맞췄다(OptionsWindow).
        _settings.Scale = Mathf.Clamp(s, 0.5f, 2.0f);
        Scale = Vector2.One * _settings.Scale;
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
    }

    /// <summary>
    /// 클릭 통과 On/Off. 옵션의 "위치 잠금"이 이것이다 (§7-1, §7-4).
    ///
    /// on(잠금) = 마스코트 영역만 클릭을 받고 나머지는 통과 - 실수로 안 끌리고,
    /// 뒤에 있는 다른 창 작업도 안 막는다. off(잠금 해제) = 창 전체가 클릭을 받아서
    /// 마스코트의 작은 히트박스를 정확히 안 눌러도 어디서든 끌 수 있다 - 처음
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
        bool hasSave = SaveIO.Exists();
        SaveData save = SaveIO.Load();
        _settings = save.Settings;

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

        var savedPos = new Vector2I(save.Settings.Pos[0], save.Settings.Pos[1]);

        if (hasSave && IsWithinAnyScreen(savedPos))
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
    /// 지금 옵션 전부와 창 위치를 세이브 파일에 반영한다.
    ///
    /// 전체 <see cref="SaveData"/>를 새로 만들지 않고 매번 <see cref="SaveIO.Load"/>로
    /// 읽어서 <c>Settings</c>만 고쳐 쓴다 - 을의 B5가 나무/인벤토리를 채운 뒤에는
    /// 이 파일에 게임 상태도 같이 들어있을 것이고, 셸이 그걸 기본값으로 덮어쓰면 안 된다.
    /// </summary>
    private void PersistSettings()
    {
        if (_unattended)
        {
            return;
        }

        SaveData save = SaveIO.Load();
        _settings.Pos = new[] { _win.Position.X, _win.Position.Y };
        save.Settings = _settings;
        SaveIO.Save(save);
    }

    /// <summary>
    /// 실제 표시 여부를 계산해서 창/커서에 적용하는 유일한 지점.
    ///
    /// "보이는가"는 서로 독립적인 두 이유의 조합이다 - 유저가 트레이에서 숨겼는가
    /// (<see cref="_userWantsVisible"/>), 전체화면 앱이 떠서 자동으로 숨겼는가
    /// (<see cref="_autoHiddenForFullscreen"/>). 둘 중 하나라도 "숨겨라"면 숨긴다.
    /// 이 메서드 하나로만 창/커서 표시를 바꾸면, "트레이로 숨겼는데 전체화면이
    /// 끝나자 다시 나타났다" 같은 상태 꼬임이 구조적으로 안 생긴다.
    ///
    /// 커서 장식은 그 위에 옵션 두 개를 더 곱한다 -
    /// <see cref="SaveData.SettingsState.CursorEnabled"/>(아예 쓸 것인가)와
    /// <see cref="SaveData.SettingsState.CursorIndependent"/>(셸이 숨어도 남길 것인가).
    /// 뒤쪽은 숨김 **이유**를 구분하지 않는다. 위 한 줄로 합쳐 둔 것이 상태 꼬임
    /// 방지책이라, 이유별 예외를 만들면 그 이점이 사라진다.
    /// </summary>
    private void ApplyVisibility()
    {
        bool visible = _userWantsVisible && !_autoHiddenForFullscreen;
        SetShellWindowVisible(visible);
        _cursor.SetEnabled(_settings.CursorEnabled && (visible || _settings.CursorIndependent));
    }

    // --- 셸 창 숨기기 -------------------------------------------------------
    //
    // **Godot 은 메인 창의 Visible 을 못 바꾼다.** scene/main/window.cpp 의
    // set_visible 이 "Can't change visibility of main window" 로 막는다. A6 이
    // 그걸 모르고 _win.Visible 에 그대로 썼고, 그래서 트레이 "숨기기"와 전체화면
    // 자동 숨김이 **에러만 찍고 아무 일도 안 했다**(2026-09-16 실행 로그). 커서
    // 레이어는 서브 창이라 똑같은 코드가 멀쩡히 동작했고, 그래서 더 늦게 드러났다.
    //
    // 대신 OS 에 직접 건다. 최소화(WindowSetMode(Minimized))는 안 쓴다 - 작업
    // 표시줄 항목이 생겼다 사라지고 복원 애니메이션이 붙는다. 상주 오버레이가
    // 할 동작이 아니다. ShowWindow 는 ex-style(클릭 통과)도 passthrough 영역도
    // 건드리지 않아서 복원한 뒤 다시 걸어 줄 것이 없다.

    private const int SwHide = 0;

    /// <summary>보이되 포커스는 뺏지 않는다. 오버레이가 남의 창에서 포커스를 가져가면 안 된다.</summary>
    private const int SwShowNoActivate = 4;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// 셸 창을 실제로 숨기고/보인다. 부르는 곳은 <see cref="ApplyVisibility"/> 하나뿐이다.
    /// </summary>
    private void SetShellWindowVisible(bool visible)
    {
        if (_shellWindowVisible == visible)
        {
            return;
        }

        if (OS.GetName() != "Windows")
        {
            // 트레이도 FullscreenWatcher 도 Windows 전용이라 여기 올 일은 거의 없다.
            // 그래도 조용히 넘기지 않는다 - "숨겼다고 생각했는데 안 숨었다" 가 정확히
            // 방금 고친 버그였다.
            if (!_hideUnsupportedLogged)
            {
                _hideUnsupportedLogged = true;
                GD.PrintErr($"[shell] 창 숨김 미지원 플랫폼 ({OS.GetName()}) - 계속 보인다");
            }

            return;
        }

        long handle = DisplayServer.WindowGetNativeHandle(
            DisplayServer.HandleType.WindowHandle, _win.GetWindowId());

        if (handle == 0)
        {
            GD.PrintErr("[shell] HWND 를 못 얻었다. 창을 숨길 수 없다");
            return;
        }

        var hwnd = new IntPtr(handle);
        ShowWindow(hwnd, visible ? SwShowNoActivate : SwHide);

        // A2 의 교훈 그대로 되읽어서 확인한다 - "걸었다" 와 "걸렸다" 는 다르다.
        // 이번 버그도 아무도 결과를 안 물어봐서 통과한 것이다.
        bool actual = IsWindowVisible(hwnd);
        _shellWindowVisible = actual;

        if (actual != visible)
        {
            GD.PrintErr($"[shell] 창 숨김 실패: 요청 {visible}, 실제 {actual}");
        }
    }

    // ------------------------------------------------------------------ A6: 트레이 / 옵션 창 / 자동 숨김

    /// <summary>
    /// WEEK0-GODOT-VALIDATION.md §4 "트레이 아이콘 + 메뉴(보이기/숨기기/설정/종료)".
    /// Godot 4.3+ 내장 API만 쓴다(<see cref="TrayIcon"/>). macOS/Windows만 지원한다.
    /// </summary>
    private void SetupTray()
    {
        _tray = new TrayIcon();
        _tray.OnToggleVisibility += () =>
        {
            _userWantsVisible = !_userWantsVisible;
            ApplyVisibility();
        };
        _tray.OnOpenSettings += OpenOptionsWindow;
        _tray.OnQuit += () => GetTree().Quit();

        var icon = GD.Load<Texture2D>("res://icon.svg");
        _tray.Build(icon, "ProjectSeWoo");

        if (!_tray.IsSupported)
        {
            GD.Print("[shell] 트레이 아이콘 미지원 - 창을 닫으면 트레이로 숨는 대신 그대로 숨는다");
        }
    }

    /// <summary>
    /// 창 닫기 요청(Alt+F4 등)을 종료가 아니라 숨기기로 바꾼다
    /// (WEEK0-GODOT-VALIDATION.md §4). 트레이가 없는 환경(미지원 플랫폼)에서는
    /// 되찾을 방법이 없어지므로, 그때는 그냥 종료한다.
    /// </summary>
    private void OnCloseRequested()
    {
        if (_tray is { IsSupported: true })
        {
            _userWantsVisible = false;
            ApplyVisibility();
        }
        else
        {
            GetTree().Quit();
        }
    }

    /// <summary>
    /// OptionsWindow는 값이 바뀌면 이벤트만 쏜다 - 실제로 적용하고 저장하는 건 여기서 한다
    /// (docs/A6-TRAY-OPTIONS.md §1 "옵션 UI는 저장을 모른다").
    /// </summary>
    private void WireOptionsEvents()
    {
        _options.ScaleChanged += v => { SetScale(v); PersistSettings(); };
        _options.OpacityChanged += v => { SetOpacity(v); PersistSettings(); };
        _options.PositionLockedChanged += v => { SetClickThrough(v); PersistSettings(); };
        _options.SoundChanged += v => { _settings.Sound = v; PersistSettings(); };
        _options.NotificationsChanged += v => { _settings.Notifications = v; PersistSettings(); };
        _options.CursorEnabledChanged += v => { _settings.CursorEnabled = v; ApplyVisibility(); PersistSettings(); };
        _options.CursorIndependentChanged += v => { _settings.CursorIndependent = v; ApplyVisibility(); PersistSettings(); };
        _options.HideOnFullscreenChanged += v => { _settings.HideOnFullscreen = v; PersistSettings(); };
        _options.KeystrokeCountingChanged += v => { _settings.KeystrokeCounting = v; PersistSettings(); };
        _options.AutostartChanged += v =>
        {
            _settings.Autostart = v;
            if (!_unattended)
            {
                Autostart.SetEnabled(v);
            }

            PersistSettings();
        };

        // 옵션 창이 열린 동안은 창 전체가 클릭을 받아야 한다 - 안 그러면 패널이
        // 마스코트 클릭 영역 밖으로 나가는 순간 슬라이더/체크박스를 못 누른다.
        // 닫히면 위치 잠금 값대로 되돌린다.
        _options.Closed += () => ApplyPassthrough(force: true);
    }

    private void OpenOptionsWindow()
    {
        _options.SetValues(_settings, _unattended ? _settings.Autostart : Autostart.IsEnabled());
        _options.Open();
        DisplayServer.WindowSetMousePassthrough(Array.Empty<Vector2>());
    }

    private void ToggleOptionsWindow()
    {
        if (_options.IsOpen)
        {
            _options.Close();
        }
        else
        {
            OpenOptionsWindow();
        }
    }

    /// <summary>
    /// 전체화면으로 실행 중인 다른 앱 위에서 자동으로 숨긴다 (§7-1, §7-4).
    /// 0.5초 틱(<see cref="OnTick"/>)마다 확인한다 - 매 프레임 P/Invoke 를 부를
    /// 이유가 없다. 휴리스틱의 한계는 docs/A6-TRAY-OPTIONS.md §3 참고 - 아직
    /// 실제 전체화면 게임으로는 검증하지 못했다.
    /// </summary>
    private void CheckFullscreen()
    {
        bool shouldHide = _settings.HideOnFullscreen && _fullscreenWatcher.IsOtherAppFullscreen();
        if (shouldHide == _autoHiddenForFullscreen)
        {
            return;
        }

        _autoHiddenForFullscreen = shouldHide;
        ApplyVisibility();
    }

    // ------------------------------------------------------------------ 클릭 통과

    /// <summary>
    /// 창 픽셀 좌표 기준의 클릭 수신 폴리곤.
    /// 빈 배열을 넘기면 passthrough 가 꺼지고 창 전체가 마우스를 가로챈다(Godot 기본 동작).
    /// </summary>
    private Vector2[] BuildRegion()
    {
        if (!_settings.PositionLocked)
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
        _uptime += delta;

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

        // 클릭 시 살짝 튀는 연출. 자리표시자 전용이다 - 실제 펀치 애니메이션은
        // 게임 레이어(B2 마이크로 피드백)가 GameRoot 안에서 직접 맡는다.
        if (_punch > 0.0)
        {
            _punch = Math.Max(0.0, _punch - delta * 4.0);
            if (_mascot != null)
            {
                _mascot.Scale = Vector2.One * (MascotScale * (1.0f + (float)_punch * 0.18f));
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

        if (_steamSelftest)
        {
            TickSteamSelftest(delta);
        }

        if (_autoReportPath != null)
        {
            TickAutoReport(delta);
        }
    }

    private void OnTick()
    {
        _perf.Sample();
        _hud.SetStats(BuildStats());
        CheckFullscreen();
    }

    // ------------------------------------------------------------------ 입력

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb)
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
        _punch = 1.0;
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

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.F1:
                _hud.Visible = !_hud.Visible;
                break;

            case Key.F2:
                // A6부터는 진짜 옵션이다 - 옵션 창의 "위치 잠금" 체크박스와 정확히
                // 같은 경로(SetClickThrough)를 부른다.
                SetClickThrough(!_settings.PositionLocked);
                PersistSettings();
                break;

            case Key.F3:
                _updateEveryFrame = !_updateEveryFrame;
                GD.Print($"[shell] passthrough update = {(_updateEveryFrame ? "EVERY FRAME (flicker repro)" : "ON CHANGE")}");
                break;

            case Key.F4:
                _win.AlwaysOnTop = !_win.AlwaysOnTop;
                break;

            case Key.F5:
                _fpsCapIndex = (_fpsCapIndex + 1) % FpsCaps.Length;
                Engine.MaxFps = FpsCaps[_fpsCapIndex];
                _perf.Reset();
                break;

            case Key.F6:
                _lowPower = !_lowPower;
                ApplyPowerSettings();
                _perf.Reset();
                break;

            case Key.F7:
                MoveToScreen((DisplayServer.WindowGetCurrentScreen() + 1) % DisplayServer.GetScreenCount());
                break;

            case Key.F8:
                _showOutline = !_showOutline;
                _outline.Visible = _showOutline;
                RefreshOutline(_appliedRegion);
                break;

            case Key.F9:
                DisplayServer.ClipboardSet(BuildReport());
                GD.Print("[shell] report copied to clipboard");
                break;

            case Key.F10:
                _perf.Reset();
                _regionWrites = 0;
                _clicks = 0;
                _rclicks = 0;
                _wheels = 0;
                _drags = 0;
                _uptime = 0.0;
                break;

            // 숨김이 실제로 먹는지 트레이 없이 확인하기 위한 debug 키. 그냥 숨기면
            // 창이 포커스를 잃어 **다시 켤 키를 못 받는다** - 트레이로만 되돌릴 수
            // 있게 된다. 그래서 잠깐 숨겼다 스스로 돌아온다.
            case Key.H:
                _userWantsVisible = false;
                _debugHideRemaining = DebugHideSeconds;
                ApplyVisibility();
                break;

            case Key.F11:
                // 옵션 창의 "커서 장식" 체크박스와 같은 경로.
                _settings.CursorEnabled = !_settings.CursorEnabled;
                ApplyVisibility();
                PersistSettings();
                _perf.Reset();
                break;

            case Key.F12:
                _cursor.CycleInterval();
                _perf.Reset();
                break;

            case Key.Key1:
                _cursor.CycleMode();
                _perf.Reset();
                break;

            // A5 의 3슬롯 장착을 옵션/상점 UI(B6) 없이 시험하기 위한 debug 키.
            // 을이 상점 화면을 만들면 이 자리를 그 UI가 대신 호출한다.
            case Key.Key2:
                CycleDemoEquip(CursorSlot.Hang);
                break;

            case Key.Key3:
                CycleDemoEquip(CursorSlot.Trail);
                break;

            case Key.Key4:
                CycleDemoEquip(CursorSlot.Base);
                break;

            // IShell 실물을 옵션 창 없이 빠르게 시험하기 위한 debug 키.
            // 옵션 창의 슬라이더와 정확히 같은 SetScale/SetOpacity를 부른다.
            case Key.Bracketleft:
                SetScale(_settings.Scale - 0.1f);
                break;

            case Key.Bracketright:
                SetScale(_settings.Scale + 0.1f);
                break;

            case Key.Minus:
                SetOpacity(_settings.Opacity - 0.1f);
                break;

            case Key.Equal:
                SetOpacity(_settings.Opacity + 0.1f);
                break;

            case Key.O:
                // A6 옵션 창을 트레이 없이/트레이 지원이 없는 환경에서도 열 수 있게.
                ToggleOptionsWindow();
                break;

            case Key.Escape:
                if (_options.IsOpen)
                {
                    _options.Close();
                }
                else
                {
                    GetTree().Quit();
                }

                break;

            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    // ------------------------------------------------------------------ 창 배치 / 부하

    private void ApplyPowerSettings()
    {
        OS.LowProcessorUsageMode = _lowPower;
        OS.LowProcessorUsageModeSleepUsec = 6900;
        Engine.MaxFps = FpsCaps[_fpsCapIndex];
    }

    /// <summary>
    /// A5 커서 장착 debug 데모. 슬롯 하나를 자리표시자 목록에서 순환시킨다.
    /// B6이 상점/장착 UI를 만들면 이 자리를 그 UI가 대신 호출한다.
    /// </summary>
    private void CycleDemoEquip(CursorSlot slot)
    {
        int i = (int)slot;
        _demoEquipIndex[i] = (_demoEquipIndex[i] + 1) % DemoAssetIds.Length;
        _cursor.Equip(slot, DemoAssetIds[_demoEquipIndex[i]]);
    }

    /// <summary>지정한 모니터의 작업 영역 우하단에 창을 붙인다.</summary>
    private void MoveToScreen(int screen)
    {
        Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
        _win.Position = new Vector2I(
            usable.Position.X + usable.Size.X - _win.Size.X - 24,
            usable.Position.Y + usable.Size.Y - _win.Size.Y - 24);
    }

    // ------------------------------------------------------------------ 리포트

    private string BuildStats()
    {
        int screen = DisplayServer.WindowGetCurrentScreen();
        Rect2I usable = DisplayServer.ScreenGetUsableRect(screen);
        Rect2 hit = CurrentHitRect();

        return string.Join("\n", new[]
        {
            $"cpu   {_perf.CpuPercent,5:F2}%  avg {_perf.AvgCpuPercent,5:F2}%  peak {_perf.PeakCpuPercent,5:F2}%",
            $"mem   priv {_perf.PrivateCommitMb,4} MB   ws {_perf.WorkingSetMb,4} MB"
                + $"   godot {_perf.GodotStaticMb} MB   clr {_perf.ManagedHeapMb} MB",
            $"fps   {Engine.GetFramesPerSecond(),5:F0}  cap {(Engine.MaxFps == 0 ? "none" : Engine.MaxFps.ToString())}  lowpower {OnOff(_lowPower)}",
            $"rend  {RenderingServer.GetCurrentRenderingMethod()} / {RenderingServer.GetCurrentRenderingDriverName()}",
            "",
            $"pass  {OnOff(_settings.PositionLocked)}   update {(_updateEveryFrame ? "every-frame" : "on-change")}   writes {_regionWrites}",
            $"in    total {_input.TotalCount}  cap-drop {_input.DroppedByCap}"
                + $"  decay-drop {_input.DroppedByDecay}  [{_input.Status}]",
            $"      available {OnOff(_input.IsAvailable)}  restarts {_input.Restarts}",
            $"ontop {OnOff(_win.AlwaysOnTop)}   outline {OnOff(_showOutline)}"
                + $"   in L{_clicks} R{_rclicks} W{_wheels} D{_drags}",
            _cursor.StatusLine(),
            "",
            $"win   pos {_win.Position.X},{_win.Position.Y}  size {_win.Size.X}x{_win.Size.Y}"
                + $"  uiscale {_settings.Scale:F2}  opacity {_settings.Opacity:F2}  save {OnOff(SaveIO.Exists())}",
            $"opts  cursor {OnOff(_settings.CursorEnabled)}  indep {OnOff(_settings.CursorIndependent)}"
                + $"  sound {OnOff(_settings.Sound)}  notify {OnOff(_settings.Notifications)}"
                + $"  hideFs {OnOff(_settings.HideOnFullscreen)}  keys {OnOff(_settings.KeystrokeCounting)}"
                + $"  autostart {OnOff(_settings.Autostart)}",
            // winShown 은 OS 에 되물은 값이다. 나머지 둘이 "원하는 것" 이고 이게 "된 것" 이라,
            // 셋이 어긋나면 숨김이 먹지 않았다는 뜻이다 - 그걸 눈으로 못 봐서 생긴 버그가 있었다.
            $"      want {OnOff(_userWantsVisible)}  autoHidden {OnOff(_autoHiddenForFullscreen)}"
                + $"  winShown {OnOff(_shellWindowVisible)}",
            $"hit   {hit.Position.X:F0},{hit.Position.Y:F0} .. {hit.End.X:F0},{hit.End.Y:F0}",
            $"scr   #{screen} of {DisplayServer.GetScreenCount()}  {usable.Size.X}x{usable.Size.Y}"
                + $"  dpi {DisplayServer.ScreenGetDpi(screen)}  scale {DisplayServer.ScreenGetScale(screen):F2}"
                + $"  {DisplayServer.ScreenGetRefreshRate(screen):F0}Hz",
            $"up    {_uptime:F0}s",
        });
    }

    // ------------------------------------------------------------------ 무인 측정

    /// <summary>
    /// 렌더러 A/B(DAY1-2-SPIKE.md §4)를 사람이 네 번 재시작하며 F9를 누르는 대신
    /// 스크립트로 돌리기 위한 모드. 인자는 Godot 자체 옵션과 섞이지 않게 <c>--</c> 뒤에 둔다:
    /// <code>Godot.exe --path . --rendering-method mobile -- --report=&lt;경로&gt; --seconds=60</code>
    ///
    /// <c>--seconds</c>는 워밍을 포함한 총 실행 시간이다. 워밍 구간이 따로 있는 이유는,
    /// 기동 직후의 CPU 스파이크와 30~60초에 걸쳐 안정되는 메모리가 유휴 판정에 섞이면
    /// 안 되기 때문이다. 워밍이 끝나는 순간 계측을 리셋하므로, 리포트의 <c>uptime</c>이
    /// 곧 실제 측정 구간이 된다.
    /// </summary>
    private void ParseAutoReportArgs()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--report=", StringComparison.Ordinal))
            {
                _autoReportPath = arg["--report=".Length..];
            }
            else if (TryParseSeconds(arg, "--seconds=", out double duration))
            {
                _autoDurationSec = duration;
            }
            else if (TryParseSeconds(arg, "--warmup=", out double warmup))
            {
                _autoWarmupSec = warmup;
            }
            else if (arg == "--cursor")
            {
                // 커서 레이어를 켠 채로 잰다. §7-3 이 "커서 창을 포함해서 실측"하라고
                // 요구하므로, 셸 단독 숫자만으로는 저부하 판정을 닫을 수 없다.
                _autoCursor = true;
            }
            else if (arg == "--cursor-sim")
            {
                _autoCursorSim = true;
            }
            else if (arg == "--cursor-nopass")
            {
                _autoCursorNoPass = true;
            }
            else if (arg.StartsWith("--cursor-interval=", StringComparison.Ordinal))
            {
                _autoCursorIntervalMs = arg["--cursor-interval=".Length..];
            }
            else if (arg.StartsWith("--cursor-mode=", StringComparison.Ordinal))
            {
                _autoCursorMode = arg["--cursor-mode=".Length..];
            }
            else if (arg.StartsWith("--cursor-equip=", StringComparison.Ordinal))
            {
                // A5. "hang,trail,base" 순서의 쉼표 구분, 빈 칸은 그 슬롯을 비워둔다.
                // A7 이 "장식을 낀 채로" 저부하를 잴 때 이 인자를 쓴다 - 빈 슬롯과
                // 채운 슬롯의 렌더 비용 차이를 보려면 필요하다.
                _autoCursorEquip = arg["--cursor-equip=".Length..];
            }
        }

        ApplyAutoCursorArgs();

        if (_autoReportPath == null)
        {
            return;
        }

        // 세이브/레지스트리를 안 건드리는 건 _unattended(IsUnattendedRun)가 이미
        // --report= 를 감지해서 _Ready() 맨 앞에서 처리했다. 여기서 더 할 일은 없다.

        if (_autoDurationSec <= 0.0)
        {
            _autoDurationSec = 60.0;
        }

        // 워밍이 전체 시간을 잡아먹으면 측정 구간이 사라진다.
        _autoWarmupSec = Math.Clamp(_autoWarmupSec, 0.0, _autoDurationSec * 0.5);

        GD.Print($"[shell] auto report -> {_autoReportPath}"
            + $" (warmup {_autoWarmupSec:F0}s, measure {_autoDurationSec - _autoWarmupSec:F0}s)");
    }

    /// <summary>
    /// 커서 레이어를 스크립트가 요구한 상태로 맞춘다. 인터랙티브 핫키(F11/F12/1)와
    /// 같은 조작을 인자로 노출하는 것뿐이라, 사람이 손으로 재든 스크립트가 재든
    /// 같은 상태를 만든다.
    /// </summary>
    private void ApplyAutoCursorArgs()
    {
        if (!_autoCursor)
        {
            return;
        }

        if (int.TryParse(_autoCursorIntervalMs, out int ms))
        {
            // 후보 배열에 없는 값을 넘기면 조용히 무시되는 게 최악이다. 못 맞추면 말한다.
            int idx = Array.IndexOf(CursorLayer.IntervalsMs, ms);
            if (idx < 0)
            {
                GD.PrintErr($"[shell] --cursor-interval={ms} 는 후보에 없다"
                    + $" ({string.Join("/", CursorLayer.IntervalsMs)}). 기본값을 쓴다");
            }
            else
            {
                while (_cursor.IntervalIndex != idx)
                {
                    _cursor.CycleInterval();
                }
            }
        }

        if (!string.IsNullOrEmpty(_autoCursorMode)
            && Enum.TryParse(_autoCursorMode, ignoreCase: true, out CursorLayer.FollowMode mode))
        {
            while (_cursor.Mode != mode)
            {
                _cursor.CycleMode();
            }
        }

        _cursor.Simulate = _autoCursorSim;
        _cursor.SkipClickThrough = _autoCursorNoPass;

        _cursor.SetEnabled(true);

        // Equip 은 SetEnabled(true) 뒤에 불러야 한다 - CursorLayer.Equip 이
        // 스프라이트 가시성을 지금의 Enabled 값으로 정하기 때문이다.
        if (!string.IsNullOrEmpty(_autoCursorEquip))
        {
            string[] ids = _autoCursorEquip.Split(',');
            CursorSlot[] order = { CursorSlot.Hang, CursorSlot.Trail, CursorSlot.Base };
            for (int i = 0; i < order.Length && i < ids.Length; i++)
            {
                _cursor.Equip(order[i], string.IsNullOrEmpty(ids[i]) ? null : ids[i]);
            }
        }

        _cursor.ResetCounters();
    }

    private static bool TryParseSeconds(string arg, string prefix, out double value)
    {
        value = 0.0;
        return arg.StartsWith(prefix, StringComparison.Ordinal)
            && double.TryParse(
                arg[prefix.Length..],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
    }

    private void TickAutoReport(double delta)
    {
        _autoElapsed += delta;

        if (!_autoWarmedUp)
        {
            if (_autoElapsed < _autoWarmupSec)
            {
                return;
            }

            _autoWarmedUp = true;
            _perf.Reset();
            _cursor.ResetCounters();
            _uptime = 0.0;
            return;
        }

        if (_autoElapsed < _autoDurationSec)
        {
            return;
        }

        // 마지막 구간을 한 번 더 반영하고 쓴다. 0.5초 틱 사이에서 끝날 수 있기 때문이다.
        _perf.Sample();

        try
        {
            // BOM 을 붙여서 쓴다. Windows PowerShell 5.1 의 Get-Content 는 BOM 이 없으면
            // UTF-8 을 ANSI 로 읽어서 리포트의 한글이 깨진다.
            System.IO.File.WriteAllText(
                _autoReportPath, BuildReport(), new System.Text.UTF8Encoding(true));
            GD.Print($"[shell] auto report written: {_autoReportPath}");
        }
        catch (Exception e)
        {
            // 쓰기가 실패해도 종료는 한다. 스크립트는 파일 부재를 실패로 읽는다.
            GD.PrintErr($"[shell] auto report failed: {e.Message}");
        }

        GetTree().Quit();
    }

    /// <summary>F9. 노션 측정 기록표에 그대로 붙일 수 있는 형태로 뽑는다.</summary>
    private string BuildReport()
    {
        int screen = DisplayServer.WindowGetCurrentScreen();

        return string.Join("\n", new[]
        {
            "=== ProjectSeWoo overlay shell spike ===",
            $"uptime         {_uptime:F0}s",
            $"cpu avg/peak   {_perf.AvgCpuPercent:F2}% / {_perf.PeakCpuPercent:F2}%   (cores {_perf.Cores})",
            $"memory         {_perf.PrivateCommitMb} MB private commit"
                + $" / {_perf.WorkingSetMb} MB working set"
                + $" (godot {_perf.GodotStaticMb} MB, clr heap {_perf.ManagedHeapMb} MB)",
            $"renderer       {RenderingServer.GetCurrentRenderingMethod()} / {RenderingServer.GetCurrentRenderingDriverName()}",
            // 스팀은 개인 커밋을 수십 MB 먹는다. 리포트에 이 줄이 없으면 측정값이
            // 스팀을 켠 것인지 아닌지 나중에 알 수가 없다 - A7 Release 재측정에서
            // 실제로 회차마다 50MB 씩 흔들렸고, 원인이 스팀인지 판별할 방법이 없었다.
            $"steam          {(_steam == null ? "off (요청 안 함)" : _steam.Status)}",
            $"fps cap        {(Engine.MaxFps == 0 ? "none" : Engine.MaxFps.ToString())}, low power {OnOff(_lowPower)}",
            $"passthrough    {OnOff(_settings.PositionLocked)}, update {(_updateEveryFrame ? "every-frame" : "on-change")},"
                + $" writes {_regionWrites}",
            $"always on top  {OnOff(_win.AlwaysOnTop)}",
            $"screen         #{screen} of {DisplayServer.GetScreenCount()},"
                + $" dpi {DisplayServer.ScreenGetDpi(screen)},"
                + $" scale {DisplayServer.ScreenGetScale(screen):F2},"
                + $" {DisplayServer.ScreenGetRefreshRate(screen):F0}Hz",
            $"window         {_win.Position.X},{_win.Position.Y} {_win.Size.X}x{_win.Size.Y},"
                + $" uiscale {_settings.Scale:F2}, opacity {_settings.Opacity:F2}, save {OnOff(SaveIO.Exists())}",
            $"visibility     userWants {OnOff(_userWantsVisible)}, autoHiddenForFullscreen {OnOff(_autoHiddenForFullscreen)},"
                + $" winVisible {OnOff(_win.Visible)}",
            $"input on body: left {_clicks}, right {_rclicks},"
                + $" wheel {_wheels}, drag-moved {_drags}",
            _input.StatusLine(),
            _cursor.StatusLine(),
            _cursor.PositionLine(),
            "",
            // 자동 판정은 숫자로 확인되는 두 항목만 한다.
            // 플리커 / 드래그 / 멀티모니터는 사람이 눈으로 봐야 하므로 미정으로 남긴다.
            $"[auto] idle cpu avg < 1%       {Verdict(_perf.AvgCpuPercent < 1.0)}"
                + $"   ({_perf.AvgCpuPercent:F2}%, sampled {_uptime:F0}s)",
            // 메모리 기준은 2026-09-16 에 개인 커밋 150MB 에서 OS PrivWS 300MB 로
            // 재설정됐다(A7-PERF.md §4-2). PrivWS 는 프로세스가 자기 자신에 대해
            // 싸게 구할 수 없어서 measure-renderers.ps1 이 WMI 로 밖에서 찍는다.
            // 옛 기준을 그대로 두면 이미 조건부 Go 로 판정한 빌드가 리포트마다
            // FAIL 을 찍어서, 표와 리포트가 정반대를 말하게 된다.
            $"[info] private commit         {_perf.PrivateCommitMb} MB (참고값 - 판정 기준 아님)",
            "[info] 메모리 판정            OS PrivWS < 300MB - measure-renderers.ps1 표에서 본다",
            "[eye ] no flicker              ?   <- F3 로 every-frame 과 비교해서 직접 채운다",
            "[eye ] drag ok on every screen ?   <- F7/F8 로 모니터별 확인 후 직접 채운다",
        });
    }

    public override void _ExitTree()
    {
        // 옵션 전부와 창 위치를 여기서 한 번 더 남긴다. 드래그 없이 바로 끈 세션도
        // 다음 실행에서 지금 상태(예: [ 로 바꾼 스케일)를 복원하려면 필요하다.
        PersistSettings();

        // RawInput 등록과 WndProc 후킹을 되돌린다. 상주 앱이라 프로세스가
        // 오래 살고, 남겨두면 다음 실행에서 무엇이 원인인지 알기 어려워진다.
        _input?.Dispose();

        // 트레이 아이콘을 지운다 - 안 지우면 프로세스가 죽어도 재부팅 전까지
        // "죽은" 아이콘이 트레이에 남아 있다가 클릭할 때야 사라지는 흔한 버그가 난다.
        _tray?.Dispose();

        // 스팀을 안 놓으면 친구 목록에 죽은 프로세스가 한동안 "게임 중"으로 남는다.
        _steam?.Dispose();
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string Verdict(bool pass) => pass ? "PASS" : "FAIL";
}
