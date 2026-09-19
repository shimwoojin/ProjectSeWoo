using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="IInputSource"/> 실물. 별도 헬퍼 프로세스가 센 타건 수를 읽어 온다.
/// (기획확정-일감분배-260907.md A4 / WEEK0-GODOT-VALIDATION.md §3 의 C안)
///
/// <b>게임 본체에는 키보드 원본 데이터를 만지는 코드가 한 줄도 없다.</b>
/// 이 클래스가 하는 일은 공유 메모리에서 <c>long</c> 몇 개를 읽는 것뿐이다.
/// §7-2 가 요구한 "코드 레벨에서 증명 가능한 구조" 가 프로세스 경계로 한 번 더
/// 갈라진 셈이고, 검증할 코드는 <c>platform/InputHelper/RawKeyboardCounter.cs</c>
/// 하나로 모인다.
///
/// 왜 별도 프로세스인가는 docs/A4-GLOBAL-INPUT.md §3 — 엔진 프로세스 안에서는
/// 키보드 WM_INPUT 이 오지 않았고, 같은 순간 별도 프로세스는 정상 동작했다.
/// </summary>
public sealed class HelperInputSource : IInputSource, IDisposable
{
    /// <summary>100ms 배치. <see cref="IInputSource"/> 의 계약이다.</summary>
    private const double PollSeconds = 0.1;

    /// <summary>헬퍼가 죽으면 이 간격으로 다시 띄운다.</summary>
    private const double RestartSeconds = 3.0;

    private MemoryMappedFile _map;
    private MemoryMappedViewAccessor _view;
    private Process _helper;

    private double _pollElapsed;
    private double _restartElapsed;
    private long _lastTotal;
    private int _restarts;

    public event Action<int> OnKeystrokes;

    public long TotalCount { get; private set; }

    /// <summary>
    /// 헬퍼가 살아서 심장박동을 찍고 있는가.
    ///
    /// false 면 게임 레이어는 시간 기반 폴백으로 전환한다 (§2-2). 나무는 원래
    /// 시간으로 자라므로 타건이 끊겨도 게임은 성립한다.
    /// </summary>
    public bool IsAvailable { get; private set; }

    public string Status { get; private set; } = "미시작";

    public long DroppedByCap { get; private set; }

    public long DroppedByDecay { get; private set; }

    /// <summary>
    /// <see cref="TotalCount"/> 중 마우스 버튼이 낸 몫 (2026-09-19). 총합에서 빼야
    /// 하는 값이 아니라 그 안에 들어 있는 내역이고, 진단 표시 말고는 쓰지 않는다.
    /// </summary>
    public long MouseCount { get; private set; }

    /// <summary>헬퍼를 다시 띄운 횟수. 0 이 아니면 헬퍼가 불안정하다는 뜻이다.</summary>
    public int Restarts => _restarts;

    // --- 시작 / 종료 ------------------------------------------------------

    public void Start()
    {
        if (OS.GetName() != "Windows")
        {
            Status = "Windows 아님";
            return;
        }

        int pid = System.Environment.ProcessId;

        try
        {
            // **게임이 매핑을 만든다.** 헬퍼가 늦게 뜨거나 재시작해도 이쪽 핸들이
            // 살아 있어야 카운터가 0 으로 튀지 않는다.
            _map = MemoryMappedFile.CreateNew(InputBridge.MapName(pid), InputBridge.Size);
            _view = _map.CreateViewAccessor(0, InputBridge.Size);
        }
        catch (Exception e)
        {
            Status = $"공유 메모리 실패 ({e.GetType().Name})";
            GD.PrintErr($"[input] {Status}");
            return;
        }

        if (!LaunchHelper(pid))
        {
            return;
        }

        Status = "헬퍼 기동";
    }

    private bool LaunchHelper(int pid)
    {
        string exe = ResolveHelperPath();
        if (exe == null)
        {
            Status = "헬퍼 실행 파일 없음";
            GD.PrintErr($"[input] {Status}. 먼저 빌드할 것: "
                + "dotnet build platform/InputHelper/InputHelper.csproj");
            return false;
        }

        try
        {
            _helper = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--pid={pid}",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception e)
        {
            Status = $"헬퍼 기동 실패 ({e.GetType().Name}: {e.Message})";
            GD.PrintErr($"[input] {Status}");
            return false;
        }

        GD.Print($"[input] 헬퍼 기동 pid={_helper.Id} ({exe})");
        return true;
    }

    /// <summary>
    /// 헬퍼 실행 파일을 찾는다.
    ///
    /// 익스포트 빌드에서는 게임 exe 옆에 있고(A12 에서 같이 배포해야 한다),
    /// 개발 중에는 <c>dotnet build</c> 산출물 자리에 있다. 둘 다 뒤진다.
    /// </summary>
    private static string ResolveHelperPath()
    {
        string exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
        string projectDir = ProjectSettings.GlobalizePath("res://");

        string[] candidates =
        {
            // 익스포트 빌드: 게임 exe 옆
            exeDir == null ? null : Path.Combine(exeDir, "InputHelper.exe"),

            // 개발 중: dotnet build 산출물
            Path.Combine(projectDir, "platform", "InputHelper", "bin", "Release", "net8.0", "InputHelper.exe"),
            Path.Combine(projectDir, "platform", "InputHelper", "bin", "Debug", "net8.0", "InputHelper.exe"),
        };

        foreach (string path in candidates)
        {
            if (path != null && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_helper != null)
        {
            try
            {
                // 헬퍼는 부모가 죽으면 스스로 나가지만, 정상 종료 경로에서는
                // 기다리지 않고 바로 정리한다. 상주 앱이 종료될 때 유령 프로세스가
                // 남는 것이 가장 나쁘다.
                if (!_helper.HasExited)
                {
                    _helper.Kill();
                }
            }
            catch (Exception)
            {
                // 이미 죽었거나 접근 불가. 어느 쪽이든 할 일이 없다.
            }

            _helper.Dispose();
            _helper = null;
        }

        _view?.Dispose();
        _view = null;
        _map?.Dispose();
        _map = null;

        IsAvailable = false;
        Status = "종료됨";
    }

    // --- 폴링 -------------------------------------------------------------

    /// <summary>매 프레임 호출한다. 100ms 마다 공유 메모리를 읽는다.</summary>
    public void Tick(double delta)
    {
        if (_view == null)
        {
            return;
        }

        _pollElapsed += delta;
        if (_pollElapsed < PollSeconds)
        {
            return;
        }

        _pollElapsed = 0.0;

        long magic = _view.ReadInt64(InputBridge.OffsetMagic);
        long beat = _view.ReadInt64(InputBridge.OffsetHeartbeat);
        long age = System.Environment.TickCount64 - beat;

        // magic 이 0 이면 헬퍼가 아직 한 번도 안 썼다는 뜻이다. "아직 시작 전" 과
        // "죽었다" 를 구분하려고 둔 표식이다.
        IsAvailable = magic == InputBridge.Magic && age < InputBridge.HeartbeatTimeoutMs;

        if (!IsAvailable)
        {
            Status = magic == 0 ? "헬퍼 대기 중" : $"헬퍼 응답 없음 ({age}ms)";
            MaybeRestart(delta);
            return;
        }

        Status = "헬퍼 정상";
        _restartElapsed = 0.0;

        DroppedByCap = _view.ReadInt64(InputBridge.OffsetDroppedCap);
        DroppedByDecay = _view.ReadInt64(InputBridge.OffsetDroppedDecay);
        MouseCount = _view.ReadInt64(InputBridge.OffsetMouseTotal);

        long total = _view.ReadInt64(InputBridge.OffsetTotal);
        long diff = total - _lastTotal;
        _lastTotal = total;

        if (diff <= 0)
        {
            return;
        }

        TotalCount = total;
        OnKeystrokes?.Invoke((int)diff);
    }

    /// <summary>
    /// 헬퍼가 죽었으면 다시 띄운다.
    ///
    /// 상주 앱은 며칠씩 켜져 있으므로 헬퍼가 한 번 죽으면 그날 하루의 타수가
    /// 통째로 사라진다. 재기동은 선택이 아니라 필수다. 다만 <b>누적값은
    /// 이어붙이지 않는다</b> — 헬퍼의 카운터가 0 부터 다시 시작하므로
    /// <see cref="_lastTotal"/> 도 같이 되돌려서 음수 diff 가 나오지 않게 한다.
    /// </summary>
    private void MaybeRestart(double delta)
    {
        if (_helper != null && !_helper.HasExited)
        {
            return;
        }

        _restartElapsed += delta;
        if (_restartElapsed < RestartSeconds)
        {
            return;
        }

        _restartElapsed = 0.0;
        _lastTotal = 0;
        _restarts++;

        _helper?.Dispose();
        _helper = null;
        LaunchHelper(System.Environment.ProcessId);
    }

    /// <summary>리포트용 한 줄. ASCII 전용.</summary>
    public string StatusLine() =>
        $"input {(IsAvailable ? "on" : "off")} [{Status}],"
        + $" total {TotalCount} (mouse {MouseCount}),"
        + $" dropped cap {DroppedByCap} / decay {DroppedByDecay},"
        + $" restarts {_restarts}";
}
