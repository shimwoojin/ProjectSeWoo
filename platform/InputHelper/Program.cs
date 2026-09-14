using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.InputHelper;

/// <summary>
/// 타건 수만 세는 최소 프로세스 (기획확정-일감분배-260907.md A4 / §7-2 / §6).
///
/// <b>왜 별도 프로세스인가.</b> 엔진 프로세스 안에서는 키보드 RawInput 을 안정적으로
/// 받지 못했다. RawInput 등록은 (UsagePage, Usage) 당 프로세스에 하나뿐이라 엔진과
/// 자리를 다투게 되고, 등록을 되찾아도 키보드 WM_INPUT 이 오지 않았다.
/// 같은 순간 별도 프로세스 수신기는 정상 동작했다. 자세한 경과는
/// docs/A4-GLOBAL-INPUT.md §3.
///
/// <b>이 프로그램은 키 코드를 읽지 않는다.</b> 읽는 것은 RAWINPUT 버퍼의 오프셋
/// 두 개뿐이다 — 타입(키보드인가)과 플래그(누름인가 뗌인가). 스캔 코드와 가상 키
/// 코드는 어느 경로에서도 접근하지 않는다. <see cref="RawKeyboardCounter"/> 참고.
///
/// 하는 일이 이게 전부다. 파일을 읽지 않고, 네트워크를 열지 않고, 창을 보여주지
/// 않는다. 백신·안티치트 검증(§7-2)을 받을 때 <b>작을수록 유리하다.</b>
/// </summary>
internal static class Program
{
    /// <summary>공유 메모리에 쓰고 부모 생존을 확인하는 주기(ms).</summary>
    private const int TickMs = 200;

    private static MemoryMappedViewAccessor _view;
    private static RawKeyboardCounter _counter;
    private static Process _parent;

    private static int Main(string[] args)
    {
        int parentPid = 0;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--pid=", StringComparison.Ordinal)
                && int.TryParse(arg["--pid=".Length..], out int pid))
            {
                parentPid = pid;
            }
        }

        if (parentPid == 0)
        {
            // 부모 없이 혼자 도는 헬퍼는 유령이 된다. 상주 앱에서 이건 허용 못 한다.
            Console.Error.WriteLine("--pid=<게임 PID> 가 필요하다");
            return 2;
        }

        try
        {
            _parent = Process.GetProcessById(parentPid);
        }
        catch (ArgumentException)
        {
            return 3;
        }

        try
        {
            // 게임이 먼저 만들어 둔 매핑을 연다. 헬퍼가 만들지 않는 이유는,
            // 헬퍼가 늦게 뜨거나 재시작해도 게임 쪽 핸들이 그대로 살아 있어야
            // 하기 때문이다.
            using MemoryMappedFile map = MemoryMappedFile.OpenExisting(
                InputBridge.MapName(parentPid));
            using MemoryMappedViewAccessor view = map.CreateViewAccessor(0, InputBridge.Size);
            _view = view;

            _counter = new RawKeyboardCounter();
            if (!_counter.Start(TickMs, OnTick))
            {
                Console.Error.WriteLine($"입력 수신 시작 실패: {_counter.Status}");
                return 4;
            }

            _view.Write(InputBridge.OffsetMagic, InputBridge.Magic);
            _counter.RunMessageLoop();
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"헬퍼 종료: {e.GetType().Name}: {e.Message}");
            return 5;
        }
        finally
        {
            _counter?.Dispose();
        }
    }

    /// <summary>
    /// 200ms 마다: 누적값을 공유 메모리에 쓰고, 부모가 살아 있는지 본다.
    ///
    /// 부모 확인을 여기서 하는 이유는 타이머가 이미 돌고 있어서다. 스레드를
    /// 하나 더 만들 이유가 없고, 상주 앱에서 프로세스와 스레드는 적을수록 좋다.
    /// </summary>
    /// <returns>계속 돌아야 하면 true, 종료해야 하면 false.</returns>
    private static bool OnTick()
    {
        _view.Write(InputBridge.OffsetTotal, _counter.TotalCount);
        _view.Write(InputBridge.OffsetDroppedCap, _counter.DroppedByCap);
        _view.Write(InputBridge.OffsetDroppedDecay, _counter.DroppedByDecay);
        _view.Write(InputBridge.OffsetHeartbeat, Environment.TickCount64);

        _parent.Refresh();
        return !_parent.HasExited;
    }
}
