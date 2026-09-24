using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ProjectSeWoo.InputHelper;

/// <summary>
/// RawInput 으로 입력 횟수만 세는 핵심 (기획확정-일감분배-260907.md §7-2 / §6).
///
/// <b>RawInput(WM_INPUT) 방식이다. <c>WH_KEYBOARD_LL</c> 은 쓰지 않는다.</b>
/// 저수준 훅은 키로거와 코드 시그니처가 같아서 백신 오탐과 안티치트 충돌이 거의
/// 확정이고, 게이머 대상 상주 앱에서 그건 치명적이다.
///
/// <b>이 클래스가 "무엇을 눌렀는지 읽지 않는다" 의 증명 지점이다.</b> 프로젝트
/// 전체에서 입력 원본 데이터를 만지는 코드는 여기 하나뿐이고,
/// <see cref="HandleRawInput"/> 하나만 읽으면 검증이 끝난다. 게임 본체에는 이
/// 코드가 아예 없다.
///
/// <b>2026-09-19: 마우스 버튼도 센다.</b> 키보드만 세던 것을 을 요청으로 넓혔다.
/// 휠은 빼는데, 한 번 굴릴 때 이벤트가 여러 번 터져서 횟수가 부풀기 때문이다.
/// 마우스를 넣어도 개인정보 수준은 그대로다 - <see cref="HandleRawInput"/> 의
/// 설명 참고.
///
/// <b>2026-09-24: 키보드는 뗄 때 센다.</b> 누를 때 세면 키를 꾹 누르고 있는
/// 동안 OS 자동 반복이 "누름" 을 33ms 마다 계속 보내서 초당 캡(10)까지 타수가
/// 올랐다. 자동 반복은 누름만 되풀이하고 뗌은 손을 뗄 때 한 번뿐이라, 뗌에서
/// 세면 얼마나 오래 누르든 1타다. 키 코드는 여전히 안 읽는다 - 원래 읽던
/// 플래그 하나를 반대로 볼 뿐이다.
/// </summary>
internal sealed class RawKeyboardCounter : IDisposable
{
    // --- Win32 상수 -------------------------------------------------------

    private const int WmInput = 0x00FF;
    private const int WmTimer = 0x0113;
    private const int WmDestroy = 0x0002;

    private const ushort UsagePageGeneric = 0x01;
    private const ushort UsageKeyboard = 0x06;
    private const ushort UsageMouse = 0x02;

    /// <summary>포커스가 없어도 입력을 받는다. 상주 앱이므로 이게 핵심이다.</summary>
    private const uint RidevInputSink = 0x00000100;

    private const uint RidevRemove = 0x00000001;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const uint RimTypeKeyboard = 1;

    /// <summary>키를 <b>뗄 때</b> 켜지는 플래그. <b>이게 있을 때만 센다</b> (자동 반복은 이 플래그 없이 누름만 되풀이한다).</summary>
    private const ushort RiKeyBreak = 0x01;

    /// <summary>
    /// "버튼이 눌렸다" 에 해당하는 비트를 전부 합친 마스크 —
    /// 좌 0x0001 · 우 0x0004 · 중 0x0010 · 사이드4 0x0040 · 사이드5 0x0100.
    ///
    /// <b>뗌 비트와 휠 비트는 일부러 뺐다.</b> 뗌(0x0002/0x0008/…)을 넣으면 한 번
    /// 클릭이 두 번 세지고, 휠(0x0400)·가로휠(0x0800)은 한 번 굴릴 때 여러 번
    /// 터져서 횟수가 부푼다.
    ///
    /// <b>이 마스크는 통째로만 쓴다.</b> 개별 비트를 꺼내면 "어느 버튼인지" 를 아는
    /// 코드가 되므로, <c>!= 0</c> 비교 한 번으로 끝낸다 - 아래 <see cref="HandleRawInput"/> 참고.
    /// </summary>
    private const ushort AnyButtonDown = 0x0155;

    // 창 스타일. **메시지 전용 창(HWND_MESSAGE)을 쓰면 안 되고, WS_VISIBLE 이
    // 있어야 한다.** 둘 다 실측으로 확인했다 — 메시지 전용 창에는 WM_INPUT 이
    // 아예 안 왔고, 숨긴 최상위 창도 마찬가지였다. 표시된 창에서만 왔다.
    // 1x1 + TOOLWINDOW + NOACTIVATE 라 화면에 사실상 안 보이고, 작업표시줄과
    // Alt+Tab 에도 안 나오며, 포커스를 뺏지 않는다.
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;

    private const string ClassName = "ProjectSeWooInputHelper";
    private const int ErrorClassAlreadyExists = 1410;
    private const nuint TimerId = 1;

    // RAWINPUT 버퍼의 바이트 배치 (x64). 헤더 24바이트는 공통이고 그 뒤가 갈린다:
    //
    //   offset  0  RAWINPUTHEADER.dwType        (4)   <- 읽는다 (키보드인가 마우스인가)
    //           4  RAWINPUTHEADER.dwSize        (4)
    //           8  RAWINPUTHEADER.hDevice       (8)
    //          16  RAWINPUTHEADER.wParam        (8)
    //
    //   [키보드]
    //          24  RAWKEYBOARD.MakeCode         (2)   <- 읽지 않는다 (스캔 코드)
    //          26  RAWKEYBOARD.Flags            (2)   <- 읽는다 (누름/뗌)
    //          28  RAWKEYBOARD.Reserved         (2)
    //          30  RAWKEYBOARD.VKey             (2)   <- 읽지 않는다 (가상 키 코드)
    //          32  RAWKEYBOARD.Message          (4)
    //          36  RAWKEYBOARD.ExtraInformation (4)
    //
    //   [마우스]
    //          24  RAWMOUSE.usFlags             (2)   <- 읽지 않는다
    //          28  RAWMOUSE.usButtonFlags       (2)   <- 읽는다 (버튼이 눌렸는가)
    //          30  RAWMOUSE.usButtonData        (2)   <- 읽지 않는다 (휠 회전량)
    //          32  RAWMOUSE.ulRawButtons        (4)   <- 읽지 않는다
    //          36  RAWMOUSE.lLastX              (4)   <- 읽지 않는다 (이동량)
    //          40  RAWMOUSE.lLastY              (4)   <- 읽지 않는다 (이동량)
    //          44  RAWMOUSE.ulExtraInformation  (4)
    //
    // RAWKEYBOARD/RAWMOUSE 를 통째로 마샬링하는 구조체를 만들지 않은 이유가
    // 이것이다 — 구조체를 만들면 키 코드와 좌표가 필드로 존재하게 되고
    // "안 읽는다" 가 규율의 문제가 된다. 오프셋 세 개만 읽으면 구조적으로 못 읽는다.
    private const int RawInputHeaderSize = 24;
    private const int TypeOffset = 0;
    private const int FlagsOffset = 26;
    private const int ButtonFlagsOffset = 28;
    private const int BufferSize = 64;

    // --- 어뷰징 방어 수치 (§6) -------------------------------------------

    /// <summary>초당 캡. 이걸 넘는 입력은 버린다.</summary>
    private const int PerSecondCap = 10;

    /// <summary>이 횟수를 넘게 규칙적으로 이어지면 감쇠에 들어간다 (§6 "5타 이후").</summary>
    private const int MetronomeRunLimit = 5;

    /// <summary>간격이 평균의 이 비율 안이면 "규칙적"으로 본다.</summary>
    private const double MetronomeTolerance = 0.12;

    /// <summary>사람의 최속 연타도 이보다는 느리다. 이보다 짧으면 기계다.</summary>
    private const double MinHumanIntervalMs = 20.0;

    /// <summary>이만큼 쉬면 규칙성 판정을 새로 시작한다.</summary>
    private const double IdleResetMs = 2_000.0;

    // --- P/Invoke ---------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Style;
        public IntPtr WndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] d, uint n, uint cb);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRegisteredRawInputDevices(
        [Out] RawInputDevice[] d, ref uint n, uint cb);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(
        IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WndClass cls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nuint SetTimer(IntPtr hWnd, nuint id, uint ms, IntPtr proc);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int code);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);

    // --- 상태 -------------------------------------------------------------

    private IntPtr _hwnd;
    private IntPtr _buffer;
    private WndProcDelegate _wndProc;
    private Func<bool> _onTick;

    /// <summary>
    /// 간격 측정용 고해상도 시계. <c>GetTickCount64</c> 는 약 15.6ms 단위로만
    /// 올라서, 33ms·100ms 같은 고정 주기가 31/47ms, 94/109ms 로 들쭉날쭉하게
    /// 재어졌고 규칙성 판정(허용 12%)을 번번이 빠져나갔다 (2026-09-24 실측:
    /// 100ms 매크로 30타 중 23타 통과).
    /// </summary>
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private double _lastSecondStamp;
    private int _thisSecond;

    /// <summary>
    /// 규칙성 판정에 쓰는 상태. <b>키보드와 마우스가 따로 들고 간다.</b>
    ///
    /// 한 덩어리로 합치면 두 스트림이 섞여 간격이 불규칙해지고, 타자를 치는 동안
    /// 같이 도는 오토클리커가 감쇠를 그냥 빠져나간다 - 감쇠를 둔 이유가 바로
    /// 그 경우다. 초당 캡(<see cref="_thisSecond"/>)은 반대로 <b>공유</b>한다.
    /// 총 획득 속도는 안 올라야 §2-2 경제가 그대로이기 때문이다.
    /// </summary>
    private struct Rhythm
    {
        public double LastAtMs;
        public double IntervalAvg;
        public int Run;
    }

    private Rhythm _keyboardRhythm = new() { LastAtMs = double.NegativeInfinity };
    private Rhythm _mouseRhythm = new() { LastAtMs = double.NegativeInfinity };

    public long TotalCount { get; private set; }

    /// <summary><see cref="TotalCount"/> 중 마우스 버튼이 낸 몫. 진단용이다.</summary>
    public long MouseCount { get; private set; }

    public long DroppedByCap { get; private set; }

    public long DroppedByDecay { get; private set; }

    public string Status { get; private set; } = "미시작";

    // --- 시작 / 종료 ------------------------------------------------------

    /// <param name="tickMs">주기 콜백 간격.</param>
    /// <param name="onTick">주기마다 호출. false 를 돌려주면 메시지 루프를 끝낸다.</param>
    public bool Start(int tickMs, Func<bool> onTick)
    {
        _onTick = onTick;
        _buffer = Marshal.AllocHGlobal(BufferSize);

        // 델리게이트를 필드로 잡아둔다. GC 가 수거하면 OS 가 죽은 함수 포인터를
        // 부른다 = 프로세스 크래시.
        _wndProc = WindowProc;

        IntPtr instance = GetModuleHandleW(null);
        var cls = new WndClass
        {
            WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            Instance = instance,
            ClassName = ClassName,
        };

        if (RegisterClassW(ref cls) == 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != ErrorClassAlreadyExists)
            {
                Status = $"RegisterClass 실패 (err {err})";
                return false;
            }
        }

        _hwnd = CreateWindowExW(
            WsExToolWindow | WsExNoActivate,
            ClassName, string.Empty, WsPopup | WsVisible,
            0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Status = $"CreateWindow 실패 (err {Marshal.GetLastWin32Error()})";
            return false;
        }

        if (!Register())
        {
            Status = $"RegisterRawInputDevices 실패 (err {Marshal.GetLastWin32Error()})";
            return false;
        }

        if (SetTimer(_hwnd, TimerId, (uint)tickMs, IntPtr.Zero) == 0)
        {
            Status = $"SetTimer 실패 (err {Marshal.GetLastWin32Error()})";
            return false;
        }

        _lastSecondStamp = _clock.Elapsed.TotalMilliseconds;
        Status = "RawInput INPUTSINK";
        return true;
    }

    /// <summary>
    /// 메시지 루프. <c>GetMessage</c> 는 메시지가 올 때까지 <b>블록</b>한다.
    ///
    /// 폴링 루프(<c>PeekMessage</c> + Sleep)를 쓰지 않는 이유는 상주 앱이기
    /// 때문이다. 아무도 타자를 안 치는 동안 이 프로세스는 깨어나지 않아야 한다.
    /// 주기 작업은 <c>WM_TIMER</c> 가 깨워준다.
    /// </summary>
    public void RunMessageLoop()
    {
        while (GetMessageW(out Msg msg, IntPtr.Zero, 0, 0) > 0)
        {
            DispatchMessageW(ref msg);
        }
    }

    /// <summary>우리가 받는 기기 두 종류. 등록·확인·해제가 같은 목록을 본다.</summary>
    private static readonly ushort[] Usages = { UsageKeyboard, UsageMouse };

    private bool Register()
    {
        var devices = new RawInputDevice[Usages.Length];
        for (int i = 0; i < Usages.Length; i++)
        {
            devices[i] = new RawInputDevice
            {
                UsagePage = UsagePageGeneric,
                Usage = Usages[i],
                Flags = RidevInputSink,
                Target = _hwnd,
            };
        }

        return RegisterRawInputDevices(
            devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    /// <summary>
    /// 등록이 아직 우리 것인지 확인하고 아니면 다시 건다.
    ///
    /// 이 프로세스에는 등록을 건드릴 다른 코드가 없지만, 남겨 둔다.
    /// 엔진 프로세스 안에서 등록이 조용히 교체되는 것을 이미 겪었고,
    /// 그때 원인을 찾는 데 오래 걸렸다. 비용은 1초에 syscall 하나다.
    /// </summary>
    private void EnsureRegistered()
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<RawInputDevice>();
        GetRegisteredRawInputDevices(null, ref count, size);

        if (count > 0)
        {
            var list = new RawInputDevice[count];
            uint got = GetRegisteredRawInputDevices(list, ref count, size);
            if (got != unchecked((uint)-1))
            {
                // **둘 다 있어야 통과다.** 하나만 보고 빠져나가면 마우스 등록이
                // 조용히 빠진 채로 계속 돈다 - 키는 세지는데 클릭만 안 세지는,
                // 원인 찾기 고약한 상태가 된다.
                int found = 0;
                for (int i = 0; i < got; i++)
                {
                    if (list[i].UsagePage == UsagePageGeneric
                        && Array.IndexOf(Usages, list[i].Usage) >= 0
                        && (list[i].Flags & RidevInputSink) != 0
                        && list[i].Target == _hwnd)
                    {
                        found++;
                    }
                }

                if (found >= Usages.Length)
                {
                    return;
                }
            }
        }

        Register();
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            // 등록을 풀 때 Target 은 반드시 IntPtr.Zero 여야 한다.
            // 창 핸들을 넣으면 RIDEV_REMOVE 가 조용히 실패한다.
            var remove = new RawInputDevice[Usages.Length];
            for (int i = 0; i < Usages.Length; i++)
            {
                remove[i] = new RawInputDevice
                {
                    UsagePage = UsagePageGeneric,
                    Usage = Usages[i],
                    Flags = RidevRemove,
                    Target = IntPtr.Zero,
                };
            }

            RegisterRawInputDevices(
                remove, (uint)remove.Length, (uint)Marshal.SizeOf<RawInputDevice>());

            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            _wndProc = null;
        }

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    // --- 메시지 처리 ------------------------------------------------------

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmInput:
                HandleRawInput(lParam);
                break;

            case WmTimer:
                EnsureRegistered();
                if (_onTick != null && !_onTick())
                {
                    PostQuitMessage(0);
                }

                break;

            case WmDestroy:
                PostQuitMessage(0);
                break;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// <b>무엇을 눌렀는지 읽지 않는다는 것이 여기서 증명된다.</b>
    ///
    /// 버퍼에서 읽는 값은 딱 셋이다:
    ///   - offset 0  : 키보드인가 마우스인가 (그 외 HID 를 걸러내려고)
    ///   - offset 26 : (키보드) 누른 것인가 뗀 것인가 — 뗀 것만 센다 (자동 반복 차단)
    ///   - offset 28 : (마우스) 버튼이 눌렸는가 — 뗌·휠을 걸러내려고
    ///
    /// 스캔 코드(24)·가상 키 코드(30)·마우스 좌표(36/40)·휠 회전량(30)은 어느
    /// 경로에서도 읽지 않는다. 변수에 담지도, 로그에 남기지도, 비교하지도 않는다.
    ///
    /// <b>마우스 버튼도 "어느 버튼인지" 는 모른다.</b> <see cref="AnyButtonDown"/>
    /// 마스크와 <c>!= 0</c> 비교 한 번만 하고 개별 비트를 꺼내지 않는다. 키보드에서
    /// 키 코드를 안 읽는 것과 같은 수준이고, 이 성질이 §7-6 개인정보 문구를
    /// 떠받친다.
    /// </summary>
    private void HandleRawInput(IntPtr hRawInput)
    {
        uint size = BufferSize;
        uint read = GetRawInputData(hRawInput, RidInput, _buffer, ref size, RawInputHeaderSize);

        if (read == unchecked((uint)-1))
        {
            return;
        }

        var type = (uint)Marshal.ReadInt32(_buffer, TypeOffset);

        if (type == RimTypeKeyboard)
        {
            if (size < FlagsOffset + 2)
            {
                return;
            }

            ushort flags = (ushort)Marshal.ReadInt16(_buffer, FlagsOffset);
            if ((flags & RiKeyBreak) == 0)
            {
                // 누름이다. 뗌만 센다 - 꾹 누르고 있으면 OS 자동 반복이 누름을
                // 계속 보내는데, 뗌은 손을 뗄 때 한 번뿐이다. 키 코드 없이 "같은
                // 키의 반복" 을 가릴 방법이 이것뿐이다 (Shift 를 누른 채 치는
                // 대문자와 섞여도 갈린다).
                return;
            }

            Count(ref _keyboardRhythm, mouse: false);
            return;
        }

        if (type == RimTypeMouse)
        {
            if (size < ButtonFlagsOffset + 2)
            {
                return;
            }

            // 마우스 이동만 있는 패킷은 버튼 비트가 0 이라 여기서 걸러진다.
            // 이동 패킷이 훨씬 많으므로 이 분기가 사실상 대부분이다.
            ushort buttons = (ushort)Marshal.ReadInt16(_buffer, ButtonFlagsOffset);
            if ((buttons & AnyButtonDown) == 0)
            {
                return;
            }

            Count(ref _mouseRhythm, mouse: true);
        }
    }

    /// <summary>
    /// 어뷰징 방어를 적용해서 한 타를 받아들이거나 버린다 (§6).
    ///
    /// <b>기획서 §6 의 "동일 키 연속 입력 시 5타 이후 감쇠" 를 타이밍 기준으로
    /// 바꿨다 (2026-09-14 확정).</b> 같은 키인지 알려면 키 코드를 읽어야 하고,
    /// 그건 §7-2 의 "키 코드를 읽지 않는다" 와 정면으로 충돌한다. §10 이 개인정보
    /// 문구를 "절대 자르지 않는 것" 으로 두었으므로 프라이버시 쪽이 우선이다.
    ///
    /// 타이밍 기준이 오히려 더 넓게 막는다. 사람의 타건 간격은 들쭉날쭉하고
    /// 매크로·키 홀드 자동 반복은 메트로놈처럼 규칙적이다. 키 코드를 안 봐도
    /// 갈리고, <b>서로 다른 키를 번갈아 누르는 매크로까지 잡힌다</b> —
    /// 원래 규칙으로는 못 잡던 것이다. <b>오토클리커도 같은 장치에 그대로
    /// 걸린다</b> - 마우스가 자기 <see cref="Rhythm"/> 을 따로 들고 가는 이유다.
    ///
    /// <b>키 홀드 자동 반복은 여기가 아니라 <see cref="HandleRawInput"/> 에서
    /// 막는다</b> (뗌에서 센다, 2026-09-24). 원래 이 장치에 걸린다고 적혀
    /// 있었지만 실제로는 타이머 해상도 때문에 걸리지 않았다.
    /// </summary>
    private void Count(ref Rhythm rhythm, bool mouse)
    {
        double now = _clock.Elapsed.TotalMilliseconds;

        if (now - _lastSecondStamp >= 1_000)
        {
            _lastSecondStamp = now;
            _thisSecond = 0;
        }

        double gap = now - rhythm.LastAtMs;
        rhythm.LastAtMs = now;

        if (gap < MinHumanIntervalMs)
        {
            // 사람이 낼 수 없는 간격이다. 판정할 것도 없다.
            rhythm.Run++;
        }
        else if (double.IsInfinity(gap) || gap > IdleResetMs)
        {
            // 첫 입력이거나 한참 쉬었다. 규칙성 판정을 새로 시작한다.
            // 평균은 비워 두고 다음 간격으로 새로 잡는다 - 쉰 시간(2초+)을 평균에
            // 넣으면 100ms 매크로가 그 평균까지 수렴하는 15타 동안 규칙적으로 안
            // 보여서, 쉬었다 시작할 때마다 약 20타가 공짜로 통과했다 (2026-09-24 실측).
            rhythm.Run = 0;
            rhythm.IntervalAvg = 0.0;
        }
        else
        {
            bool regular = rhythm.IntervalAvg > 0.0
                && Math.Abs(gap - rhythm.IntervalAvg) <= rhythm.IntervalAvg * MetronomeTolerance;

            rhythm.Run = regular ? rhythm.Run + 1 : 0;

            // 지수 이동 평균. 사람이 점점 빨라지는 것까지 매크로로 보면 안 된다.
            rhythm.IntervalAvg = rhythm.IntervalAvg > 0.0
                ? (rhythm.IntervalAvg * 0.7) + (gap * 0.3)
                : gap;
        }

        if (rhythm.Run >= MetronomeRunLimit)
        {
            DroppedByDecay++;
            return;
        }

        if (_thisSecond >= PerSecondCap)
        {
            DroppedByCap++;
            return;
        }

        _thisSecond++;
        TotalCount++;

        if (mouse)
        {
            MouseCount++;
        }
    }
}
