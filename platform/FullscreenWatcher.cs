using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ProjectSeWoo.Platform;

/// <summary>
/// "전체화면으로 실행 중인 다른 앱 위에서는 셸을 자동으로 숨긴다" (§7-1, §7-4).
///
/// 배타적 풀스크린 위에는 애초에 못 뜬다는 것은 docs/DAY1-2-SPIKE.md §3-4가 이미
/// 기능 요구사항으로 반영했다. 이 클래스가 잡는 것은 그 나머지 - **보더리스
/// 풀스크린** 게임/앱이다. 이건 그냥 "모니터를 꽉 채운 일반 창"이라 우리 오버레이가
/// 그 위에 그대로 뜬다. 그게 유저가 원하는 그림이 아닐 때가 많다(방송 화면,
/// 전체화면 영상, 전체화면 게임 등).
///
/// Windows에 "지금 포그라운드가 전체화면인가"를 직접 물어보는 API는 없다.
/// 다들 쓰는 휴리스틱을 그대로 쓴다: **포그라운드 창의 사각형이 그 모니터 전체를
/// 덮는가.** 작업표시줄까지 포함한 모니터 전체 영역과 비교한다 - 일반적인
/// 최대화 창은 작업표시줄 영역을 피해서 뜨므로(작업 영역 기준) 여기 안 걸린다.
/// </summary>
public sealed class FullscreenWatcher
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder buffer, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// 데스크톱/작업표시줄 자체가 모니터를 덮는 창으로 잡히는 오탐을 막는다.
    /// 이 클래스 명단은 실측으로 채워지는 게 맞고, 아직 못 채운 건 있을 수 있다 -
    /// §5 "남은 것" 참고.
    /// </summary>
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd",
    };

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    /// <summary>
    /// 지금 포그라운드에 있는 창이 (우리 프로세스가 아닌) 다른 프로세스의
    /// 보더리스 풀스크린 창인가.
    /// </summary>
    public bool IsOtherAppFullscreen()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == _ownProcessId)
        {
            // 우리 자신(셸/커서 창)이 포그라운드인 경우. 우리 창이 우리를 숨기게
            // 만들면 안 된다.
            return false;
        }

        var classBuffer = new StringBuilder(256);
        GetClassNameW(fg, classBuffer, classBuffer.Capacity);
        if (IgnoredClasses.Contains(classBuffer.ToString()))
        {
            return false;
        }

        if (!GetWindowRect(fg, out Rect win))
        {
            return false;
        }

        IntPtr monitor = MonitorFromWindow(fg, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = default(MonitorInfo);
        info.Size = (uint)Marshal.SizeOf<MonitorInfo>();
        if (!GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        // 작업표시줄까지 포함한 모니터 전체(Monitor)와 비교한다. 일반 최대화 창은
        // 작업 영역(WorkArea) 기준이라 여기 안 걸린다 - 그게 이 휴리스틱의 핵심이다.
        return win.Left <= info.Monitor.Left
            && win.Top <= info.Monitor.Top
            && win.Right >= info.Monitor.Right
            && win.Bottom >= info.Monitor.Bottom;
    }
}
