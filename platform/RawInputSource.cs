using System;
using System.Runtime.InteropServices;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 전역 타건 수 공급자 실물 (기획확정-일감분배-260907.md A4, §7-2, §6).
///
/// <b>RawInput(WM_INPUT) 방식이다. <c>WH_KEYBOARD_LL</c> 은 쓰지 않는다.</b>
/// 저수준 훅은 키로거와 코드 시그니처가 같아서 백신 오탐과 안티치트 충돌이
/// 거의 확정이고, 게이머 대상 상주 앱에서 그건 치명적이다. RawInput 은 훅이
/// 아니라 등록 방식이라 오탐이 덜하다고 알려져 있다.
///
/// <b>이 클래스는 키 코드를 읽지 않는다.</b> 말이 아니라 코드로 증명되게 짰다 —
/// <see cref="HandleRawInput"/> 의 주석을 볼 것. 스토어와 게임 내에 걸릴
/// 문구(§7-6)가 거짓말이 아니어야 한다. 이건 기능이 아니라 신뢰 문제고,
/// 스팀 리뷰에 "키로거 아니냐" 글 하나면 프로젝트가 죽는다.
/// </summary>
public sealed class RawInputSource : IInputSource, IDisposable
{
    // --- Win32 -----------------------------------------------------------

    private const int WmInput = 0x00FF;

    private const ushort UsagePageGeneric = 0x01;
    private const ushort UsageKeyboard = 0x06;

    /// <summary>포커스가 없어도 입력을 받는다. 상주 앱이므로 이게 핵심이다.</summary>
    private const uint RidevInputSink = 0x00000100;

    private const uint RidevRemove = 0x00000001;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeKeyboard = 1;

    /// <summary>키를 <b>뗄 때</b> 켜지는 플래그. 이게 있으면 세지 않는다.</summary>
    private const ushort RiKeyBreak = 0x01;

    // RAWINPUT 버퍼의 바이트 배치 (x64):
    //
    //   offset  0  RAWINPUTHEADER.dwType        (4)
    //           4  RAWINPUTHEADER.dwSize        (4)
    //           8  RAWINPUTHEADER.hDevice       (8)
    //          16  RAWINPUTHEADER.wParam        (8)
    //          24  RAWKEYBOARD.MakeCode         (2)   <- 읽지 않는다 (스캔 코드)
    //          26  RAWKEYBOARD.Flags            (2)   <- 읽는다 (누름/뗌 구분)
    //          28  RAWKEYBOARD.Reserved         (2)
    //          30  RAWKEYBOARD.VKey             (2)   <- 읽지 않는다 (가상 키 코드)
    //          32  RAWKEYBOARD.Message          (4)
    //          36  RAWKEYBOARD.ExtraInformation (4)
    //
    // 이 클래스가 버퍼에서 읽는 오프셋은 0(타입)과 26(플래그) 둘뿐이다.
    // 24(스캔 코드)와 30(가상 키 코드)은 어느 코드 경로에서도 접근하지 않는다.
    //
    // RAWKEYBOARD 를 통째로 마샬링하는 구조체를 만들지 않은 이유가 이것이다 —
    // 구조체를 만들면 키 코드가 필드로 존재하게 되고 "안 읽는다"가 규율의 문제가
    // 된다. 오프셋 두 개만 읽으면 구조적으로 못 읽는다.
    private const int RawInputHeaderSize = 24;
    private const int TypeOffset = 0;
    private const int FlagsOffset = 26;

    /// <summary>키보드 RAWINPUT 한 건은 40바이트다. 넉넉히 잡아도 이 크기다.</summary>
    private const int BufferSize = 64;

    // 창 스타일. **메시지 전용 창(HWND_MESSAGE)을 쓰면 안 된다** — 그렇게 만들었더니
    // WM_INPUT 이 한 건도 안 왔다(msg 4 = 생성 중 SendMessage 로 직접 온 것들뿐).
    // RIDEV_INPUTSINK 는 실제 최상위 창을 요구한다. 대조군으로 띄운 WinForms 창은
    // 같은 등록으로 잘 받았고, 차이가 이것뿐이었다.
    //
    // 그래서 최상위 창이되 눈에 안 띄게 만든다: 보이지 않고(WS_VISIBLE 없음),
    // 작업표시줄·Alt+Tab 에 안 나오고(TOOLWINDOW), 포커스를 뺏지 않는다(NOACTIVATE).
    private const uint WsPopup = 0x80000000;

    // **WS_VISIBLE 이 필요하다.** 숨긴 창(WS_VISIBLE 없음)으로는 WM_INPUT 이
    // 오지 않았다. 동작하는 대조군(WinForms 창)은 Application.Run 이 창을 실제로
    // 표시한 상태였고, 그 차이 말고는 같았다.
    // 1x1 크기 + TOOLWINDOW + NOACTIVATE 라 화면에는 사실상 안 보이고,
    // 작업표시줄과 Alt+Tab 에도 안 나오며, 포커스를 뺏지 않는다.
    private const uint WsVisible = 0x10000000;

    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;

    private const string ClassName = "ProjectSeWooRawInput";

    /// <summary>클래스가 이미 등록돼 있다는 에러. 같은 프로세스 재시도 시 정상이다.</summary>
    private const int ErrorClassAlreadyExists = 1410;

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

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRegisteredRawInputDevices(
        [Out] RawInputDevice[] devices, ref uint count, uint size);

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);

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

    [DllImport("user32.dll")]
    private static extern bool PeekMessageW(out Msg msg, IntPtr hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Msg msg);

    private const uint PmRemove = 0x0001;

    /// <summary>한 프레임에 처리할 메시지 상한. 폭주해도 프레임을 안 잡아먹게 한다.</summary>
    private const int MaxPumpPerFrame = 256;

    // --- 어뷰징 방어 수치 (§6) -------------------------------------------

    /// <summary>초당 캡. 이걸 넘는 입력은 버린다.</summary>
    public const int PerSecondCap = 10;

    /// <summary>100ms 배치. <see cref="IInputSource"/> 의 계약이다.</summary>
    private const double BatchSeconds = 0.1;

    /// <summary>이 횟수를 넘게 규칙적으로 이어지면 감쇠에 들어간다 (§6 "5타 이후").</summary>
    private const int MetronomeRunLimit = 5;

    /// <summary>간격이 평균의 이 비율 안이면 "규칙적"으로 본다.</summary>
    private const double MetronomeTolerance = 0.12;

    /// <summary>사람의 최속 연타도 이보다는 느리다. 이보다 짧으면 기계다.</summary>
    private const double MinHumanIntervalSec = 0.02;

    // --- 상태 -------------------------------------------------------------

    private IntPtr _hwnd;
    private IntPtr _buffer;

    // 델리게이트를 필드로 잡아둔다. GC 가 수거하면 OS 가 죽은 함수 포인터를
    // 부른다 = 프로세스 크래시. A2 에서 같은 함정을 이미 밟았다.
    private WndProcDelegate _wndProcHook;

    private int _pending;
    private int _thisSecond;
    private double _secondElapsed;
    private double _batchElapsed;
    private double _regCheckElapsed;

    // 진단용 단계별 계수기. "안 세진다" 의 원인이 넷인데 밖에서는 구분이 안 된다:
    // 창이 메시지를 못 받나 / WM_INPUT 이 안 오나 / 키보드가 아니라고 걸러졌나 / 뗌으로 봤나.
    private long _msgSeen;
    private long _inputMsgSeen;
    private long _keyboardSeen;
    private long _keyDownSeen;
    private long _rawReadFail;
    private long _lastType = -1;
    private long _lastSize = -1;

    private double _now;
    private double _lastKeyAt = double.NegativeInfinity;
    private double _intervalAvg;
    private int _metronomeRun;

    public event Action<int> OnKeystrokes;

    public long TotalCount { get; private set; }

    public bool IsAvailable { get; private set; }

    /// <summary>등록 실패 사유. 리포트에 박아서 조용한 실패를 막는다.</summary>
    public string Status { get; private set; } = "미시작";

    /// <summary>캡·감쇠로 버린 입력 수. 방어가 실제로 동작하는지 보는 대조군이다.</summary>
    public long DroppedByCap { get; private set; }

    public long DroppedByDecay { get; private set; }

    /// <summary>등록을 다시 건 횟수. 0 이 아니면 누군가와 경합 중이라는 뜻이다.</summary>
    public long ReRegistered { get; private set; }

    // --- 시작 / 종료 ------------------------------------------------------

    /// <summary>
    /// 보이지 않는 최상위 창을 만들고 거기로 RawInput 을 등록한다.
    ///
    /// <b>엔진의 메인 창을 서브클래싱하지 않는다.</b> 처음에는 그렇게 만들었는데
    /// WM_INPUT 이 한 건도 오지 않았다 — 후킹은 걸렸고(메시지 22건 수신) 등록도
    /// 우리 것이었는데도 그랬다. 같은 방식의 최소 수신기(WinForms 창)는 잘 받는
    /// 것을 대조군으로 확인했으므로, 남의 창에 얹는 구조 자체가 원인이다.
    ///
    /// 전용 창이면 WndProc 이 온전히 우리 것이라 엔진과 경합할 여지가 없다.
    /// 메시지 펌프는 엔진 것을 그대로 쓴다 — <c>PeekMessage</c> 가 스레드의 모든
    /// 창 메시지를 가져오고 <c>DispatchMessage</c> 가 창마다 알맞은 WndProc 으로
    /// 보내주기 때문에 펌프를 따로 돌릴 필요가 없다. 그래서 이 창은 반드시
    /// <b>엔진 메인 스레드에서</b> 만들어야 한다.
    /// </summary>
    public void Start()
    {
        if (OS.GetName() != "Windows")
        {
            Status = "Windows 아님";
            return;
        }

        _buffer = Marshal.AllocHGlobal(BufferSize);
        _wndProcHook = WindowProc;

        IntPtr instance = GetModuleHandleW(null);
        var cls = new WndClass
        {
            WndProc = Marshal.GetFunctionPointerForDelegate(_wndProcHook),
            Instance = instance,
            ClassName = ClassName,
        };

        if (RegisterClassW(ref cls) == 0)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != ErrorClassAlreadyExists)
            {
                Status = $"RegisterClass 실패 (err {err})";
                GD.PrintErr($"[input] {Status}");
                return;
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
            GD.PrintErr($"[input] {Status}");
            return;
        }

        if (!Register())
        {
            Status = $"RegisterRawInputDevices 실패 (err {Marshal.GetLastWin32Error()})";
            GD.PrintErr($"[input] {Status}");
            return;
        }

        IsAvailable = true;
        Status = "RawInput INPUTSINK";
        GD.Print($"[input] {Status} 등록 완료 (수신 창 0x{_hwnd.ToInt64():X})");
    }

    /// <summary>키보드 RawInput 을 우리 창으로, 포커스와 무관하게 등록한다.</summary>
    private bool Register()
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = UsagePageGeneric,
                Usage = UsageKeyboard,
                Flags = RidevInputSink,
                Target = _hwnd,
            },
        };

        return RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    /// <summary>
    /// 등록이 아직 우리 것인지 확인하고, 아니면 다시 건다.
    ///
    /// <b>RawInput 등록은 (UsagePage, Usage) 당 프로세스에 하나뿐이고, 나중에
    /// 부른 쪽이 앞의 것을 교체한다.</b> Godot 이 자기 키보드 등록을 걸면서 우리
    /// INPUTSINK 등록을 덮어쓰는 것을 실측으로 확인했다 — 등록 조회에
    /// <c>flags 0x0 target 0x0</c> 으로 남아 있었다.
    ///
    /// 우리가 다시 걸어도 엔진의 키 입력은 안 깨진다. 엔진은 WM_KEYDOWN 으로도
    /// 키를 받는다. 그래도 <b>엔진 핫키가 계속 듣는지는 손으로 확인해야 한다.</b>
    /// </summary>
    private void EnsureRegistered()
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<RawInputDevice>();
        GetRegisteredRawInputDevices(null, ref count, size);

        bool ours = false;
        if (count > 0)
        {
            var list = new RawInputDevice[count];
            uint got = GetRegisteredRawInputDevices(list, ref count, size);
            if (got != unchecked((uint)-1))
            {
                for (int i = 0; i < got; i++)
                {
                    if (list[i].UsagePage == UsagePageGeneric
                        && list[i].Usage == UsageKeyboard
                        && (list[i].Flags & RidevInputSink) != 0
                        && list[i].Target == _hwnd)
                    {
                        ours = true;
                        break;
                    }
                }
            }
        }

        if (ours)
        {
            return;
        }

        if (Register())
        {
            ReRegistered++;
        }
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            // 등록을 풀 때 Target 은 반드시 IntPtr.Zero 여야 한다. 창 핸들을
            // 넣으면 RIDEV_REMOVE 가 조용히 실패한다.
            var remove = new[]
            {
                new RawInputDevice
                {
                    UsagePage = UsagePageGeneric,
                    Usage = UsageKeyboard,
                    Flags = RidevRemove,
                    Target = IntPtr.Zero,
                },
            };
            RegisterRawInputDevices(remove, 1, (uint)Marshal.SizeOf<RawInputDevice>());

            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            _wndProcHook = null;
        }

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }

        IsAvailable = false;
        Status = "종료됨";
    }

    // --- 메시지 처리 ------------------------------------------------------

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        _msgSeen++;

        if (msg == WmInput)
        {
            _inputMsgSeen++;
            HandleRawInput(lParam);
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// <b>이 메서드가 "키를 읽지 않는다"의 증명 지점이다.</b>
    ///
    /// 버퍼에서 읽는 값은 딱 둘이다:
    ///   - offset 0  : 이게 키보드 입력인가 (마우스·HID 를 걸러내려고)
    ///   - offset 26 : 누른 것인가 뗀 것인가 (안 그러면 한 타가 두 번 세진다)
    ///
    /// 스캔 코드(offset 24)와 가상 키 코드(offset 30)는 어느 경로에서도 읽지
    /// 않는다. 변수에 담지도, 로그에 남기지도, 비교하지도 않는다.
    /// <see cref="IInputSource"/> 에 키를 나타내는 타입이 없는 것과 같은 이유다.
    /// </summary>
    private void HandleRawInput(IntPtr hRawInput)
    {
        uint size = BufferSize;
        uint read = GetRawInputData(hRawInput, RidInput, _buffer, ref size, RawInputHeaderSize);

        if (read == unchecked((uint)-1) || size < FlagsOffset + 2)
        {
            _rawReadFail++;
            _lastSize = size;
            return;
        }

        _lastSize = size;
        uint type = (uint)Marshal.ReadInt32(_buffer, TypeOffset);
        _lastType = type;

        if (type != RimTypeKeyboard)
        {
            return;
        }

        _keyboardSeen++;

        ushort flags = (ushort)Marshal.ReadInt16(_buffer, FlagsOffset);
        if ((flags & RiKeyBreak) != 0)
        {
            // 키를 뗀 것이다. 누른 것만 센다.
            return;
        }

        _keyDownSeen++;
        CountKeyDown();
    }

    /// <summary>
    /// 어뷰징 방어를 적용해서 한 타를 받아들이거나 버린다 (§6).
    ///
    /// <b>기획서 §6 의 "동일 키 연속 입력 시 5타 이후 감쇠" 를 타이밍 기준으로
    /// 바꿨다.</b> 같은 키인지 알려면 키 코드를 읽어야 하고, 그건 §7-2 의
    /// "키 코드를 읽지 않는다" 와 정면으로 충돌한다. §10 이 개인정보 문구를
    /// "절대 자르지 않는 것" 으로 두었으므로 프라이버시 쪽이 우선이다.
    ///
    /// 타이밍 기준이 오히려 더 넓게 막는다. 사람의 타건 간격은 들쭉날쭉하고
    /// 매크로·키 홀드 자동 반복은 메트로놈처럼 규칙적이다. 키 코드를 안 봐도
    /// 갈리고, <b>서로 다른 키를 번갈아 누르는 매크로까지 잡힌다</b> —
    /// 원래 규칙으로는 못 잡던 것이다.
    ///
    /// 자동 반복(키 홀드)도 같은 장치로 처리된다. 윈도우 자동 반복은 고정 주기라
    /// 규칙성 판정에 그대로 걸린다.
    /// </summary>
    private void CountKeyDown()
    {
        double gap = _now - _lastKeyAt;
        _lastKeyAt = _now;

        if (gap < MinHumanIntervalSec)
        {
            // 사람이 낼 수 없는 간격이다. 판정할 것도 없다.
            _metronomeRun++;
        }
        else if (double.IsInfinity(gap) || gap > 2.0)
        {
            // 첫 타이거나 한참 쉬었다. 규칙성 판정을 새로 시작한다.
            _metronomeRun = 0;
            _intervalAvg = double.IsInfinity(gap) ? 0.0 : gap;
        }
        else
        {
            bool regular = _intervalAvg > 0.0
                && Math.Abs(gap - _intervalAvg) <= _intervalAvg * MetronomeTolerance;

            _metronomeRun = regular ? _metronomeRun + 1 : 0;

            // 지수 이동 평균. 사람이 점점 빨라지는 것까지 매크로로 보면 안 된다.
            _intervalAvg = _intervalAvg > 0.0 ? (_intervalAvg * 0.7) + (gap * 0.3) : gap;
        }

        if (_metronomeRun >= MetronomeRunLimit)
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
        _pending++;
    }

    // --- 배치 -------------------------------------------------------------

    /// <summary>
    /// 우리 메시지 창의 메시지를 직접 꺼내 처리한다.
    ///
    /// <b>엔진의 펌프에 기대면 안 된다.</b> Godot 의 메시지 루프는 자기 창으로
    /// 필터링해서 <c>PeekMessage</c> 를 부르기 때문에, 같은 스레드에 있어도
    /// 우리 창 앞으로 <b>큐에 올라온</b> 메시지는 영영 안 꺼내진다.
    ///
    /// 실측으로 확인했다: 전용 메시지 창을 만들었더니 <c>msg 4</c> 만 찍혔다.
    /// 그 4건은 창 생성 중 <c>SendMessage</c> 로 **직접 보내진** 것들이고
    /// (WM_NCCREATE / WM_CREATE 등), 큐를 거치는 WM_INPUT 은 한 건도 없었다.
    /// 이 대비가 원인을 정확히 가리킨다.
    ///
    /// <c>PeekMessage</c> 에 <see cref="_hwnd"/> 를 넘기므로 우리 창 것만
    /// 가져온다. 엔진 메시지를 가로채지 않는다.
    /// </summary>
    private void PumpMessages()
    {
        int guard = 0;
        while (guard++ < MaxPumpPerFrame
            && PeekMessageW(out Msg msg, _hwnd, 0, 0, PmRemove))
        {
            DispatchMessageW(ref msg);
        }
    }

    /// <summary>매 프레임 호출한다. 메시지 펌프, 초당 창, 100ms 배치를 굴린다.</summary>
    public void Tick(double delta)
    {
        _now += delta;

        if (IsAvailable)
        {
            PumpMessages();
        }

        if (IsAvailable)
        {
            // 1초에 한 번 등록이 살아 있는지 본다. 엔진이 언제 자기 등록을 거는지
            // 모르므로 한 번 걸고 마는 것으로는 부족하다. 조회는 syscall 하나다.
            _regCheckElapsed += delta;
            if (_regCheckElapsed >= 1.0)
            {
                _regCheckElapsed = 0.0;
                EnsureRegistered();
            }
        }

        _secondElapsed += delta;
        if (_secondElapsed >= 1.0)
        {
            _secondElapsed = 0.0;
            _thisSecond = 0;
        }

        _batchElapsed += delta;
        if (_batchElapsed < BatchSeconds)
        {
            return;
        }

        _batchElapsed = 0.0;

        if (_pending <= 0)
        {
            return;
        }

        int batch = _pending;
        _pending = 0;
        TotalCount += batch;
        OnKeystrokes?.Invoke(batch);
    }

    /// <summary>리포트용 한 줄. ASCII 전용.</summary>
    public string StatusLine() =>
        $"input {(IsAvailable ? "on" : "off")} [{Status}],"
        + $" total {TotalCount}, dropped cap {DroppedByCap} / decay {DroppedByDecay},"
        + $" re-reg {ReRegistered}";

    /// <summary>진단용. 어느 단계에서 끊기는지 보여준다.</summary>
    public string TraceLine() =>
        $"input trace: msg {_msgSeen}, wm_input {_inputMsgSeen},"
        + $" keyboard {_keyboardSeen}, keydown {_keyDownSeen},"
        + $" readfail {_rawReadFail}, lastType {_lastType}, lastSize {_lastSize}";

    /// <summary>
    /// 지금 이 프로세스에 등록된 RawInput 장치를 OS 에 직접 물어본다.
    /// 등록은 성공했는데 WM_INPUT 이 안 오는 상황을 가르기 위한 것이다.
    /// </summary>
    public string RegistrationLine()
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<RawInputDevice>();

        GetRegisteredRawInputDevices(null, ref count, size);
        if (count == 0)
        {
            return "input reg: (없음)";
        }

        var list = new RawInputDevice[count];
        uint got = GetRegisteredRawInputDevices(list, ref count, size);
        if (got == unchecked((uint)-1))
        {
            return $"input reg: 조회 실패 (err {Marshal.GetLastWin32Error()})";
        }

        var parts = new string[got];
        for (int i = 0; i < got; i++)
        {
            bool mine = list[i].Target == _hwnd;
            parts[i] = $"[page {list[i].UsagePage:X2} usage {list[i].Usage:X2}"
                + $" flags 0x{list[i].Flags:X} target 0x{list[i].Target.ToInt64():X}"
                + $"{(mine ? " <-우리 창" : string.Empty)}]";
        }

        return $"input reg: {got}개 {string.Join(" ", parts)}";
    }
}
