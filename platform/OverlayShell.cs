using System;
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

    private readonly PerfProbe _perf = new();
    private CursorLayer _cursor;
    private HelperInputSource _input;

    private Window _win;
    private Sprite2D _mascot;
    private Line2D _outline;
    private DebugHud _hud;

    /// <summary>스케일 적용 전 원본 창 크기. project.godot 의 viewport 크기다.</summary>
    private Vector2I _baseWindowSize;

    // --- IShell 상태. 옵션 화면(A6)이 아직 없어서 지금은 디버그 키(아래 _UnhandledKeyInput)로
    // 시험한다. 값은 SaveIO 를 거쳐 재실행 시 복원된다 (RestoreWindowState). ---
    private float _uiScale = 1.0f;
    private float _opacity = 1.0f;

    // --- A5 커서 장착 debug 데모. B6(상점/장착 UI)가 나오기 전까지 Key2/3/4로
    // 슬롯별 자리표시자 에셋을 순환한다. null 은 "빈 슬롯". ---
    private static readonly string[] DemoAssetIds = { null, "demo_a", "demo_b", "demo_c" };
    private readonly int[] _demoEquipIndex = new int[3];

    /// <summary>
    /// <c>--selftest</c> / <c>--report=</c> 같은 무인 실행에서는 세이브를 건드리지 않는다.
    /// 실제로 이걸 안 하니 헤드리스 selftest 가 헤드리스 환경의 엉뚱한 창 위치
    /// (예: -88,-88)를 유저의 진짜 세이브 파일에 덮어썼다 - 이 파일 개발 중 실측.
    /// </summary>
    private bool _skipSavePersist;

    // --- 토글 상태 ---
    private bool _passthroughOn = true;
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

        // per_pixel_transparency/allowed 는 project.godot 에서 이미 켰다.
        // 여기서 켜려고 하면 조용히 무시된다.
        _win.Borderless = true;
        _win.AlwaysOnTop = true;
        _win.Transparent = true;
        GetTree().Root.TransparentBg = true;

        ApplyPowerSettings();
        BuildScene();

        // A4 실물. 별도 헬퍼 프로세스로 전역 타건 수를 받는다 (docs/A4-GLOBAL-INPUT.md).
        _input = new HelperInputSource();
        _input.Start();

        // A2 커서 추종 창. 여기서 IsSupported 가 false 로 나오면 기획서 §1.3 의
        // C안이 성립하지 않는다는 뜻이고, 그게 이 스파이크가 먼저 답해야 할 질문이다.
        _cursor = new CursorLayer(this);
        // 투명은 창 생성 시점에 정해지므로 이 인자만 다른 것들보다 먼저 읽는다.
        _cursor.Build(opaque: Array.IndexOf(OS.GetCmdlineUserArgs(), "--cursor-opaque") >= 0);

        // 창 배율/투명도/위치 복원 (§7-1). SetScale 이 안에서 ApplyPassthrough 까지
        // 걸어주므로 별도로 부를 필요가 없다.
        RestoreWindowState();

        var tick = new Timer { WaitTime = 0.5, Autostart = true };
        tick.Timeout += OnTick;
        AddChild(tick);

        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--selftest") >= 0)
        {
            // 세이브 스키마가 기획서 §7-5 의 JSON 과 맞는지 확인하고 끝낸다.
            // 계약 문서와 코드가 갈라지는 것은 눈으로 안 잡히고, 을이 구현을
            // 끝낸 뒤에야 드러난다.
            _skipSavePersist = true;
            GD.Print(SaveSchema.Describe());
            GetTree().Quit(SaveSchema.SelfTest() == null ? 0 : 1);
            return;
        }

        ParseAutoReportArgs();

        GD.Print($"[shell] ready. screens={DisplayServer.GetScreenCount()} cores={_perf.Cores}");
    }

    // ------------------------------------------------------------------ 씬 구성

    /// <summary>
    /// 씬을 코드로 짓는다. .tscn 은 루트 노드 하나뿐이다.
    /// 스파이크 단계에서는 이게 낫다 - 에디터를 안 거쳐도 상태 전부가 한 파일에서 읽힌다.
    /// 게임 본편으로 넘어가면 당연히 에디터에서 씬을 짜야 한다.
    /// </summary>
    private void BuildScene()
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
    /// 창 배율. 옵션 화면(§7-4, A6)이 아직 없어서 지금은 debug 키(<c>[</c>/<c>]</c>)로
    /// 시험한다. 실물 소비자는 A6 옵션 창의 "크기" 슬라이더가 될 것이다.
    ///
    /// 루트 <see cref="Node2D.Scale"/>을 바꿔서 마스코트/외곽선을 같이 키운다.
    /// <see cref="DebugHud"/>는 <c>CanvasLayer</c>라 이 노드의 Transform/Modulate를
    /// 물려받지 않는다 - 배율/투명도를 바꿔도 HUD 글자는 항상 또렷하게 남는다.
    /// 창 크기도 같이 키우는 이유는, 안 키우면 커진 마스코트가 창 밖으로 잘려서
    /// 클릭 영역(passthrough 폴리곤)도 창 밖으로 나가 못 먹는 부분이 생기기 때문이다.
    /// </summary>
    public void SetScale(float s)
    {
        // 상한/하한은 A6 이 옵션 UI를 만들 때 실제 체감으로 다시 정한다. 지금은
        // "창이 사라지거나 화면을 뒤덮는" 극단만 막아 두는 자리 표시자다.
        _uiScale = Mathf.Clamp(s, 0.5f, 2.0f);
        Scale = Vector2.One * _uiScale;
        _win.Size = new Vector2I(
            Mathf.RoundToInt(_baseWindowSize.X * _uiScale),
            Mathf.RoundToInt(_baseWindowSize.Y * _uiScale));

        // 마스코트 크기가 바뀌었으니 클릭 영역도 다시 계산해야 한다.
        ApplyPassthrough(force: true);
    }

    /// <summary>
    /// 창 투명도. 옵션의 "투명도" (§7-4), debug 키 <c>-</c>/<c>=</c>로 시험한다.
    ///
    /// 하한을 0 이 아니라 0.1로 잡은 이유: 옵션 화면(A6)이 아직 없는 상태에서
    /// 완전 투명(0)까지 허용하면 유저가 창을 되찾을 UI 자체가 사라진다. 상주 앱에서
    /// "설정으로 자기 자신을 못 보이게 만들고 되돌릴 방법이 없다"는 실제로 발생하는
    /// 사고 패턴이다.
    /// </summary>
    public void SetOpacity(float a)
    {
        _opacity = Mathf.Clamp(a, 0.1f, 1.0f);
        Modulate = new Color(1f, 1f, 1f, _opacity);
    }

    /// <summary>클릭 통과 On/Off (§7-1). 옵션의 "위치 잠금"이 이것과 연결된다.</summary>
    public void SetClickThrough(bool on)
    {
        _passthroughOn = on;
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
    /// 창 배율/투명도/위치를 세이브에서 복원한다 (§7-1 "위치·크기 저장, 재실행 시 복원").
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

        SetScale(save.Settings.Scale);
        SetOpacity(save.Settings.Opacity);

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
    /// 지금 배율/투명도/위치를 세이브 파일에 반영한다.
    ///
    /// 전체 <see cref="SaveData"/>를 새로 만들지 않고 매번 <see cref="SaveIO.Load"/>로
    /// 읽어서 <c>Settings</c>만 고쳐 쓴다 - 을의 B5가 나무/인벤토리를 채운 뒤에는
    /// 이 파일에 게임 상태도 같이 들어있을 것이고, 셸이 그걸 기본값으로 덮어쓰면 안 된다.
    /// </summary>
    private void PersistWindowState()
    {
        if (_skipSavePersist)
        {
            return;
        }

        SaveData save = SaveIO.Load();
        save.Settings.Scale = _uiScale;
        save.Settings.Opacity = _opacity;
        save.Settings.Pos = new[] { _win.Position.X, _win.Position.Y };
        SaveIO.Save(save);
    }

    // ------------------------------------------------------------------ 클릭 통과

    /// <summary>
    /// 창 픽셀 좌표 기준의 클릭 수신 폴리곤.
    /// 빈 배열을 넘기면 passthrough 가 꺼지고 창 전체가 마우스를 가로챈다(Godot 기본 동작).
    /// </summary>
    private Vector2[] BuildRegion()
    {
        if (!_passthroughOn)
        {
            return Array.Empty<Vector2>();
        }

        Rect2 r = MascotRect().Grow(HitPadding);
        return new[]
        {
            r.Position,
            new Vector2(r.End.X, r.Position.Y),
            r.End,
            new Vector2(r.Position.X, r.End.Y),
        };
    }

    /// <summary>
    /// 마스코트의 창-픽셀 좌표 기준 사각형.
    ///
    /// <see cref="_mascot"/>의 Position/Scale은 이 노드(루트 Node2D)의 로컬 좌표계다.
    /// <see cref="SetScale"/>이 루트에 <see cref="Node2D.Scale"/>을 걸어 두므로,
    /// passthrough 에 넘길 **창 픽셀** 좌표를 얻으려면 <see cref="_uiScale"/>을
    /// 직접 곱해야 한다 - Godot 렌더링은 이 배율을 자동으로 반영하지만, Win32
    /// <c>SetWindowRgn</c>에 넘기는 이 좌표는 그 파이프라인을 안 거친다.
    /// </summary>
    private Rect2 MascotRect()
    {
        Vector2 size = _mascot.Texture.GetSize() * _mascot.Scale * _uiScale;
        Vector2 topLeft = (_mascot.Position * _uiScale)
            - (_mascot.Centered ? size * 0.5f : Vector2.Zero);
        return new Rect2(topLeft, size);
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

        if (_punch > 0.0)
        {
            _punch = Math.Max(0.0, _punch - delta * 4.0);
            _mascot.Scale = Vector2.One * (MascotScale * (1.0f + (float)_punch * 0.18f));
        }

        ApplyPassthrough(force: _updateEveryFrame);
        _cursor.Tick(delta);
        _input.Tick(delta);

        if (_autoReportPath != null)
        {
            TickAutoReport(delta);
        }
    }

    private void OnTick()
    {
        _perf.Sample();
        _hud.SetStats(BuildStats());
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
            PersistWindowState();
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
                _passthroughOn = !_passthroughOn;
                ApplyPassthrough(force: true);
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

            case Key.F11:
                _cursor.SetEnabled(!_cursor.Enabled);
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

            // IShell 실물을 옵션 UI(A6) 없이 시험하기 위한 debug 키.
            // 을이 옵션 화면을 만들면 이 자리를 그 UI가 대신 호출한다.
            case Key.Bracketleft:
                SetScale(_uiScale - 0.1f);
                break;

            case Key.Bracketright:
                SetScale(_uiScale + 0.1f);
                break;

            case Key.Minus:
                SetOpacity(_opacity - 0.1f);
                break;

            case Key.Equal:
                SetOpacity(_opacity + 0.1f);
                break;

            case Key.Escape:
                GetTree().Quit();
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
        Rect2 hit = MascotRect().Grow(HitPadding);

        return string.Join("\n", new[]
        {
            $"cpu   {_perf.CpuPercent,5:F2}%  avg {_perf.AvgCpuPercent,5:F2}%  peak {_perf.PeakCpuPercent,5:F2}%",
            $"mem   priv {_perf.PrivateCommitMb,4} MB   ws {_perf.WorkingSetMb,4} MB"
                + $"   godot {_perf.GodotStaticMb} MB   clr {_perf.ManagedHeapMb} MB",
            $"fps   {Engine.GetFramesPerSecond(),5:F0}  cap {(Engine.MaxFps == 0 ? "none" : Engine.MaxFps.ToString())}  lowpower {OnOff(_lowPower)}",
            $"rend  {RenderingServer.GetCurrentRenderingMethod()} / {RenderingServer.GetCurrentRenderingDriverName()}",
            "",
            $"pass  {OnOff(_passthroughOn)}   update {(_updateEveryFrame ? "every-frame" : "on-change")}   writes {_regionWrites}",
            $"in    total {_input.TotalCount}  cap-drop {_input.DroppedByCap}"
                + $"  decay-drop {_input.DroppedByDecay}  [{_input.Status}]",
            $"      available {OnOff(_input.IsAvailable)}  restarts {_input.Restarts}",
            $"ontop {OnOff(_win.AlwaysOnTop)}   outline {OnOff(_showOutline)}"
                + $"   in L{_clicks} R{_rclicks} W{_wheels} D{_drags}",
            _cursor.StatusLine(),
            "",
            $"win   pos {_win.Position.X},{_win.Position.Y}  size {_win.Size.X}x{_win.Size.Y}"
                + $"  uiscale {_uiScale:F2}  opacity {_opacity:F2}  save {OnOff(SaveIO.Exists())}",
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

        // 무인 측정 세션이다. 스윕 스크립트가 --cursor-* 조건을 바꿔가며 여러 번
        // 재시작하는데, 매번 실제 세이브 파일에 이 세션의 창 위치/배율을 남기면
        // 다음 정상 실행이 그 값을 주워서 시작한다. 측정용 상태는 측정 세션 안에만 있어야 한다.
        _skipSavePersist = true;

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
            $"fps cap        {(Engine.MaxFps == 0 ? "none" : Engine.MaxFps.ToString())}, low power {OnOff(_lowPower)}",
            $"passthrough    {OnOff(_passthroughOn)}, update {(_updateEveryFrame ? "every-frame" : "on-change")},"
                + $" writes {_regionWrites}",
            $"always on top  {OnOff(_win.AlwaysOnTop)}",
            $"screen         #{screen} of {DisplayServer.GetScreenCount()},"
                + $" dpi {DisplayServer.ScreenGetDpi(screen)},"
                + $" scale {DisplayServer.ScreenGetScale(screen):F2},"
                + $" {DisplayServer.ScreenGetRefreshRate(screen):F0}Hz",
            $"window         {_win.Position.X},{_win.Position.Y} {_win.Size.X}x{_win.Size.Y},"
                + $" uiscale {_uiScale:F2}, opacity {_opacity:F2}, save {OnOff(SaveIO.Exists())}",
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
            $"[auto] private commit < 150MB  {Verdict(_perf.PrivateCommitMb < 150)}"
                + $"   ({_perf.PrivateCommitMb} MB)",
            "[eye ] no flicker              ?   <- F3 로 every-frame 과 비교해서 직접 채운다",
            "[eye ] drag ok on every screen ?   <- F7/F8 로 모니터별 확인 후 직접 채운다",
        });
    }

    public override void _ExitTree()
    {
        // 창 배율/투명도/위치를 여기서 한 번 더 남긴다. 드래그 없이 바로 끈 세션도
        // 다음 실행에서 지금 상태(예: F5 로 바꾼 스케일)를 복원하려면 필요하다.
        PersistWindowState();

        // RawInput 등록과 WndProc 후킹을 되돌린다. 상주 앱이라 프로세스가
        // 오래 살고, 남겨두면 다음 실행에서 무엇이 원인인지 알기 어려워진다.
        _input?.Dispose();
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string Verdict(bool pass) => pass ? "PASS" : "FAIL";
}
