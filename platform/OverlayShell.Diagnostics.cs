using System;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="OverlayShell"/> 중 <b>계측과 자체 검사</b> (A7, docs/A7-PERF.md ·
/// docs/DAY1-2-SPIKE.md §4).
///
/// HUD 통계 · F9 리포트 · <c>--report=</c> 무인 측정 · <c>--steam-selftest</c> 가
/// 여기 있다. 스파이크 시절에는 본체와 한 파일이었는데, A1~A8 이 끝나고 보니
/// 셸 본체의 3분의 1이 "창을 띄우는 일"이 아니라 "창이 얼마를 먹는지 재는 일"이었다.
///
/// <b>릴리스에서 빼지 않는다.</b> <c>#if DEBUG</c> 로 감싸고 싶어지는 코드지만
/// A7 의 메모리 게이트는 <c>ExportRelease</c> 빌드를 상대로 재서 닫혔고
/// (docs/A7-PERF.md §4), <c>tools/measure-renderers.ps1</c> 이 재는 것도 릴리스
/// 빌드다. 디버그 빌드에서만 도는 계측은 우리가 실제로 묻는 질문에 답하지 못한다.
/// 여기서 갈라 두는 것은 바이너리가 아니라 <b>읽는 사람의 주의</b>다.
/// </summary>
public partial class OverlayShell
{
    private readonly PerfProbe _perf = new();

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
    private long _regionWrites;
    private int _clicks;
    private int _rclicks;
    private int _wheels;
    private int _drags;
    private double _uptime;

    /// <summary>
    /// 계측을 한 프레임 굴린다. 본체 <c>_Process</c> 가 부르는 유일한 계측 진입점이다.
    /// </summary>
    private void TickDiagnostics(double delta)
    {
        _uptime += delta;

        if (_steamSelftest)
        {
            TickSteamSelftest(delta);
        }

        if (_autoReportPath != null)
        {
            TickAutoReport(delta);
        }
    }

    /// <summary>0.5초 틱. HUD 는 이 주기로만 다시 그린다 - 매 프레임 문자열을 짓지 않는다.</summary>
    private void SampleDiagnostics()
    {
        _perf.Sample();
        _hud.SetStats(BuildStats());
    }

    /// <summary>F10. 측정 구간을 지금부터 다시 시작한다.</summary>
    private void ResetCounters()
    {
        _perf.Reset();
        _regionWrites = 0;
        _clicks = 0;
        _rclicks = 0;
        _wheels = 0;
        _drags = 0;
        _uptime = 0.0;
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
            $"in    total {_input.TotalCount}  mouse {_input.MouseCount}"
                + $"  cap-drop {_input.DroppedByCap}"
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
            // winVisible 은 _win.Visible 이면 안 된다 - Godot 은 메인 창의 Visible 을
            // 못 바꾸므로 그 값은 항상 true 다. 숨김이 안 먹던 A6 버그가 리포트에서
            // 안 보였던 이유가 정확히 이것이고, HUD(BuildStats)는 이미 고쳐져 있었다.
            $"visibility     userWants {OnOff(_userWantsVisible)}, autoHiddenForFullscreen {OnOff(_autoHiddenForFullscreen)},"
                + $" winVisible {OnOff(_shellWindowVisible)}",
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

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string Verdict(bool pass) => pass ? "PASS" : "FAIL";
}
