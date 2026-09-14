using System;
using Godot;

namespace ProjectSeWoo;

/// <summary>
/// Day 1-2 오버레이 셸 스파이크.
///
/// 검증 대상 (docs/WEEK0-GODOT-VALIDATION.md §2):
///   1. 투명 + 무테 + 항상 위 + 클릭 통과가 동시에 되는가
///   2. 몸통 드래그로 창이 따라오는가
///   3. 멀티모니터 / DPI 스케일링에서 좌표가 어긋나지 않는가
///   4. 유휴 CPU &lt; 1%, 메모리 &lt; 150MB 를 만족하는가
///   5. godot#80098 흰색 깜빡임이 우리 환경에서 재현되는가, 회피책이 통하는가
///
/// 5번이 이 스파이크의 설계 이유다. passthrough 폴리곤을 매 프레임 갱신하는 모드와
/// 상태가 바뀔 때만 갱신하는 모드를 F3로 즉시 전환할 수 있게 해서, 플리커가
/// "Godot이 못 하는 것"인지 "우리가 잘못 부른 것"인지를 눈으로 가른다.
/// </summary>
public partial class OverlayShell : Node2D
{
    /// <summary>클릭 영역을 스프라이트보다 조금 넓게 잡는다. 가장자리 클릭이 새는 걸 막는다.</summary>
    private const int HitPadding = 8;

    private const float MascotScale = 1.5f;

    /// <summary>0 = 무제한. 상주 앱에서 부하와 반응성의 균형점을 찾기 위한 후보들.</summary>
    private static readonly int[] FpsCaps = { 60, 30, 10, 0 };

    private readonly PerfProbe _perf = new();
    private CursorLayer _cursor;

    private Window _win;
    private Sprite2D _mascot;
    private Line2D _outline;
    private DebugHud _hud;

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
    private bool _autoCursorFill;
    private bool _autoCursorNoPass;
    private string _autoCursorClickThru;
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

        // per_pixel_transparency/allowed 는 project.godot 에서 이미 켰다.
        // 여기서 켜려고 하면 조용히 무시된다.
        _win.Borderless = true;
        _win.AlwaysOnTop = true;
        _win.Transparent = true;
        GetTree().Root.TransparentBg = true;

        ApplyPowerSettings();
        BuildScene();

        // A2 커서 추종 창. 여기서 IsSupported 가 false 로 나오면 기획서 §1.3 의
        // C안이 성립하지 않는다는 뜻이고, 그게 이 스파이크가 먼저 답해야 할 질문이다.
        _cursor = new CursorLayer(this);
        // 투명은 창 생성 시점에 정해지므로 이 인자만 다른 것들보다 먼저 읽는다.
        _cursor.Build(opaque: Array.IndexOf(OS.GetCmdlineUserArgs(), "--cursor-opaque") >= 0);
        MoveToScreen(DisplayServer.WindowGetCurrentScreen());
        ApplyPassthrough(force: true);

        var tick = new Timer { WaitTime = 0.5, Autostart = true };
        tick.Timeout += OnTick;
        AddChild(tick);

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

    private Rect2 MascotRect()
    {
        Vector2 size = _mascot.Texture.GetSize() * _mascot.Scale;
        Vector2 topLeft = _mascot.Position - (_mascot.Centered ? size * 0.5f : Vector2.Zero);
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

            case Key.Key2:
                _cursor.ToggleDebugFill();
                break;

            case Key.Key3:
                GD.Print($"[cursor] {_cursor.ProbeRenderTarget()}");
                break;

            case Key.Key4:
                _cursor.CycleClickThrough();
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
            $"ontop {OnOff(_win.AlwaysOnTop)}   outline {OnOff(_showOutline)}"
                + $"   in L{_clicks} R{_rclicks} W{_wheels} D{_drags}",
            _cursor.StatusLine(),
            "",
            $"win   pos {_win.Position.X},{_win.Position.Y}  size {_win.Size.X}x{_win.Size.Y}",
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
            else if (arg == "--cursor-fill")
            {
                _autoCursorFill = true;
            }
            else if (arg == "--cursor-nopass")
            {
                _autoCursorNoPass = true;
            }
            else if (arg.StartsWith("--cursor-clickthru=", StringComparison.Ordinal))
            {
                _autoCursorClickThru = arg["--cursor-clickthru=".Length..];
            }
            else if (arg.StartsWith("--cursor-interval=", StringComparison.Ordinal))
            {
                _autoCursorIntervalMs = arg["--cursor-interval=".Length..];
            }
            else if (arg.StartsWith("--cursor-mode=", StringComparison.Ordinal))
            {
                _autoCursorMode = arg["--cursor-mode=".Length..];
            }
        }

        ApplyAutoCursorArgs();

        if (_autoReportPath == null)
        {
            return;
        }

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

        if (!string.IsNullOrEmpty(_autoCursorClickThru))
        {
            if (Enum.TryParse(_autoCursorClickThru, ignoreCase: true,
                    out CursorLayer.ClickThroughMode ctMode))
            {
                _cursor.ClickThrough = ctMode;
            }
            else
            {
                GD.PrintErr($"[shell] --cursor-clickthru={_autoCursorClickThru} 를 모른다"
                    + " (transparent|layered|hittest)");
            }
        }
        if (_autoCursorFill)
        {
            _cursor.ToggleDebugFill();
        }

        _cursor.SetEnabled(true);
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
            $"window         {_win.Position.X},{_win.Position.Y} {_win.Size.X}x{_win.Size.Y}",
            $"input on body: left {_clicks}, right {_rclicks},"
                + $" wheel {_wheels}, drag-moved {_drags}",
            _cursor.StatusLine(),
            _cursor.PositionLine(),
            _cursor.ProbeRenderTarget(),
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

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string Verdict(bool pass) => pass ? "PASS" : "FAIL";
}
