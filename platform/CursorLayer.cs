using System;
using System.Runtime.InteropServices;
using Godot;

namespace ProjectSeWoo.Platform;

/// <summary>
/// A2 커서 추종 창 스파이크 (기획확정-일감분배-260907.md §1.3).
///
/// 이 게임의 재화 소비처이자 차별화 훅이 "커서 꾸미기"인데, 그게 기술적으로
/// 되는지가 아직 검증되지 않았다. 기획서가 셋 중 채택한 방식은 C안이다:
///
///   A. SetSystemCursor 로 시스템 커서 교체    -> 불가. 전역이라 크래시하면 유저 커서가 안 돌아온다
///   B. ShowCursor(FALSE) + 직접 그리기        -> 불가. 다른 앱/전체화면 위에서 깨진다
///   C. 커서 좌표를 따라다니는 클릭 통과 투명 창 -> 채택. 실패해도 유저 환경이 안 망가진다
///
/// C안의 전제는 **두 번째 OS 창**이 투명 + 클릭 통과 + 항상 위로 뜨는 것이다.
/// 그 전제가 깨지면 설계를 "데스크톱 크기 창 하나에 나무와 커서 장식을 같이 그리기"로
/// 갈아야 하고, 늦게 발견할수록 비싸다. 그래서 이 클래스가 가장 먼저 답하는 질문은
/// **"창이 하나 더 떠지는가"** 이고, 나머지 계측은 그 다음이다.
///
/// 실측 대상 (§1.3):
///   1. 이동 주기 vs CPU (16 / 33 / 50 / 100ms). 목표는 유휴 1% 이하
///   2. 고무줄 현상 - 스프링 보간으로 오히려 "매달린" 느낌을 살릴 수 있는가
///   3. 멀티모니터 경계를 넘어갈 때
///   4. 전체화면 게임 / 브라우저 / 작업표시줄 위에서의 동작
///   5. 클릭 통과가 확실한가 (드래그·선택·우클릭 전부)
///
/// 1번이 이 스파이크의 핵심 수치다. 커서를 따라가는 비용은 좌표를 읽는 데서
/// 나오지 않는다(MouseGetPosition 은 싸다). **창을 실제로 옮기는 OS 호출**에서 나온다.
/// 그래서 계측 변수는 폴링 주기가 아니라 <see cref="_moves"/>, 즉 창을 몇 번 옮겼는가다.
/// </summary>
public sealed class CursorLayer
{
    /// <summary>장식 창 한 변. 커서 주변 장식이 들어갈 만큼만. 작을수록 컴포지팅이 싸다.</summary>
    private const int WindowSize = 128;

    /// <summary>이동 주기 후보(ms). 0 = 매 프레임.</summary>
    public static readonly int[] IntervalsMs = { 0, 16, 33, 50, 100 };

    // --- Win32 클릭 통과 ---------------------------------------------------
    //
    // Godot 의 window_set_mouse_passthrough 는 Windows 에서 SetWindowRgn 으로
    // 구현돼 있다. 즉 창을 **물리적으로 잘라낸다.** 그래서 "창 전체 통과"를 노리고
    // 창 밖의 축퇴 폴리곤을 주면 창이 통째로 잘려서 아무것도 안 보이게 된다.
    // 실제로 이 프로젝트에서 그렇게 만들었고, "클릭이 통과된다"는 측정 결과까지
    // 같이 나와서 성공으로 오판했다 — 거기 창이 아예 없었기 때문이다.
    //
    // 올바른 방법은 WS_EX_TRANSPARENT 를 직접 거는 것이다. 이건 히트테스트만
    // 바꾸고 렌더링은 건드리지 않는다.

    private const int GwlExStyle = -20;
    private const int GwlpWndProc = -4;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
    private const uint WmNcHitTest = 0x0084;
    private const uint LwaAlpha = 0x00000002;

    /// <summary>WM_NCHITTEST 응답. "이 창은 마우스에 없는 셈 쳐라".</summary>
    private static readonly IntPtr HtTransparent = new(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr w, IntPtr l);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>클릭 통과 구현 방식. 어느 것이 실제로 먹는지는 재 봐야 안다.</summary>
    public enum ClickThroughMode
    {
        /// <summary>WS_EX_TRANSPARENT 만. **다른 프로세스의 클릭을 막는다. 쓰면 안 된다.**</summary>
        Transparent,

        /// <summary>
        /// WS_EX_TRANSPARENT | WS_EX_LAYERED. **채택.** 오버레이의 교과서 조합이고,
        /// 실사용에서 다른 앱의 클릭이 정상 통과하는 유일한 방식이었다.
        /// </summary>
        Layered,

        /// <summary>
        /// WndProc 을 가로채 WM_NCHITTEST 에 HTTRANSPARENT 를 돌려준다.
        /// **같은 프로세스 창에는 통하지만 프로세스 경계를 못 넘는다.** 쓰면 안 된다.
        /// 자동 계측에서 이것 때문에 오판했다 - 표적이 우리 셸 창이었다.
        /// </summary>
        HitTest,
    }

    /// <summary>
    /// 추종 모드. Lazy 는 §1.3의 폴백안("고빈도 추종 없이 느슨하게 따라옴")을
    /// 미리 만들어 둔 것이다. Direct 가 부하나 고무줄로 탈락해도 축을 접지 않아도 되게.
    /// </summary>
    public enum FollowMode
    {
        /// <summary>커서 좌표에 그대로 붙는다. 지연이 가장 적고 창 이동이 가장 잦다.</summary>
        Direct,

        /// <summary>스프링 보간. 뒤따라오는 지연이 "매달린" 연출이 되는지 보는 것.</summary>
        Spring,

        /// <summary>느슨하게. 폴백안 검증용.</summary>
        Lazy,
    }

    private readonly Node _host;

    private Window _win;
    private Sprite2D _deco;
    private ColorRect _debugFill;

    private Vector2 _pos;
    private double _sinceMove;
    private double _phase;
    private bool _built;
    private double _simTime;
    private Vector2 _simCenter;
    private float _simRadius;

    // --- 계측 ---
    private long _moves;
    private long _skipped;

    public bool Enabled { get; private set; }

    /// <summary>창이 실제 OS 창으로 떴는가. 이게 false 면 C안 자체가 성립하지 않는다.</summary>
    public bool IsSupported { get; private set; }

    public string FailureReason { get; private set; } = "";

    public FollowMode Mode { get; private set; } = FollowMode.Spring;

    /// <summary>
    /// 실제 커서 대신 합성 경로를 따라간다.
    ///
    /// "이동 주기 vs CPU"를 무인으로 재려면 이게 필요하다. 사람이 마우스를 안 흔들면
    /// 창이 안 움직이고, 창이 안 움직이면 이 기능의 부하는 0으로 측정된다.
    /// 즉 손을 놓고 재는 순간 측정 자체가 무의미해진다.
    ///
    /// 합성 경로는 그 반대로 **최악 조건**이다. 커서가 한순간도 쉬지 않는다.
    /// 실사용보다 비싼 값이 나오지만, 재현 가능하고 조건 간 비교가 성립한다.
    /// 체감·지연·멀티모니터는 이걸로 못 보므로 사람이 직접 봐야 한다.
    /// </summary>
    public bool Simulate { get; set; }

    /// <summary>
    /// 창을 불투명하게 띄운다. 진단 전용.
    ///
    /// "커서에 아무것도 안 보인다"는 증상은 원인이 둘인데 화면상으로는 똑같이 보인다:
    ///   (a) 창은 그려지는데 스프라이트가 안 보인다 (알파/좌표/텍스처 문제)
    ///   (b) 창 자체가 아무것도 렌더하지 않는다 (서브 윈도우 투명 미지원)
    /// 불투명 사각형을 강제로 깔면 둘이 갈린다. 사각형이 보이면 (a), 안 보이면 (b)다.
    /// </summary>
    public bool DebugFill { get; private set; }

    /// <summary>진단용. 클릭 통과를 걸지 않는다. 그것이 창을 안 보이게 만드는 범인인지 가른다.</summary>
    public bool SkipClickThrough { get; set; }

    /// <summary>클릭 통과가 실제로 걸렸는지. 리포트에 박아서 조용한 실패를 막는다.</summary>
    public string ClickThroughState { get; private set; } = "미적용";

    /// <summary>
    /// 어느 방식으로 클릭 통과를 걸 것인가.
    ///
    /// <see cref="ClickThroughMode.Layered"/> 가 정답이다. 실사용으로 확인했다 —
    /// 나머지 둘은 메모장·브라우저 같은 **다른 프로세스**의 클릭을 막는다.
    /// </summary>
    public ClickThroughMode ClickThrough { get; set; } = ClickThroughMode.Layered;

    // WndProc 후킹용. 델리게이트를 필드로 잡아두지 않으면 GC 가 수거해서
    // OS 가 죽은 함수 포인터를 부른다 = 프로세스 크래시.
    private WndProcDelegate _wndProcHook;
    private IntPtr _originalWndProc;

    public int IntervalIndex { get; private set; } = 1;

    public int IntervalMs => IntervalsMs[IntervalIndex];

    public long Moves => _moves;

    /// <summary>이동 주기 때문에 건너뛴 갱신 횟수. 주기가 실제로 먹고 있는지의 대조군.</summary>
    public long Skipped => _skipped;

    public CursorLayer(Node host)
    {
        _host = host;
    }

    // ------------------------------------------------------------------ 생성

    /// <summary>
    /// 두 번째 OS 창을 만든다. 이 스파이크의 성패가 여기서 갈린다.
    ///
    /// <c>embed_subwindows=false</c> 가 project.godot 에 있어야 Window 노드가
    /// 게임 화면 안에 그려지는 가짜 창이 아니라 진짜 OS 창이 된다. 그게 없으면
    /// 창이 "떠 있는 것처럼" 보이지만 바탕화면 위로는 못 나간다.
    /// </summary>
    /// <param name="opaque">
    /// 진단용. 창을 투명하지 않게 만든다. "서브 윈도우가 아무것도 렌더하지 않는다"는
    /// 증상의 원인이 투명 설정인지 가르기 위한 것이다. 투명은 런타임에 못 바꾸므로
    /// 생성 시점에 정해야 하고, 그래서 핫키가 아니라 인자다.
    /// </param>
    public void Build(bool opaque = false)
    {
        if (DisplayServer.GetName() == "headless")
        {
            FailureReason = "headless";
            return;
        }

        _win = new Window
        {
            Name = "CursorWindow",
            Borderless = true,
            AlwaysOnTop = true,
            Transparent = !opaque,
            TransparentBg = !opaque,
            Unfocusable = true,
            Unresizable = true,
            Size = new Vector2I(WindowSize, WindowSize),
            Visible = false,
        };
        _host.AddChild(_win);

        // 창 전체를 덮는 진단용 사각형. 스프라이트보다 먼저 넣어서 뒤에 깔리게 한다.
        _debugFill = new ColorRect
        {
            Name = "DebugFill",
            Color = new Color(1.0f, 0.0f, 0.8f, 1.0f),
            Size = new Vector2(WindowSize, WindowSize),
            Visible = false,
        };
        _win.AddChild(_debugFill);

        var texture = GD.Load<Texture2D>("res://icon.svg");
        if (texture == null)
        {
            GD.PrintErr("[cursor] icon.svg 로드 실패");
        }

        _deco = new Sprite2D
        {
            Name = "Deco",
            Texture = texture,
            Centered = true,
            Scale = Vector2.One * 0.5f,
            Position = new Vector2(WindowSize / 2f, WindowSize / 2f),
        };
        _win.AddChild(_deco);

        _pos = DisplayServer.MouseGetPosition();

        Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        _simCenter = usable.Position + (Vector2)usable.Size * 0.5f;
        _simRadius = Math.Min(usable.Size.X, usable.Size.Y) * 0.3f;

        _built = true;
    }

    /// <summary>
    /// OS 창이 실제로 만들어졌는지 확인한다.
    ///
    /// **Build() 시점에는 판정할 수 없다.** Godot 은 Window 노드가 보이게 될 때
    /// 비로소 OS 창을 만들기 때문에, 숨긴 상태에서 GetWindowId() 를 부르면 -1
    /// (INVALID_WINDOW_ID) 이 돌아온다. 처음에 이걸 "메인 창 id 가 아니니 성공"으로
    /// 읽어서 검증이 통과해 버렸다. 그래서 판정은 창을 켠 뒤로 옮긴다.
    /// </summary>
    private void VerifyWindow()
    {
        int id = _win.GetWindowId();

        if (id == DisplayServer.InvalidWindowId)
        {
            FailureReason = "window not created";
            IsSupported = false;
            return;
        }

        // 임베드된 가짜 창은 별도 OS 창을 안 만들고 메인 창 안에 그려진다.
        if (id == DisplayServer.MainWindowId)
        {
            FailureReason = "embedded (embed_subwindows=false 확인 필요)";
            IsSupported = false;
            return;
        }

        IsSupported = true;
        GD.Print($"[cursor] window id={id} size={WindowSize} -> OS window OK"
            + $" (tex {_deco.Texture?.GetSize()}, transparent {_win.Transparent},"
            + $" bg {_win.TransparentBg})");
    }

    /// <summary>
    /// 창 전체를 클릭 통과로 만든다. OS 창이 생긴 뒤에 불러야 한다.
    ///
    /// **여기서 <c>Window.Flags.MousePassthrough</c> 를 쓰면 안 된다.** 이 프로젝트에서
    /// 실측한 결과, 그 플래그는 조용히 아무것도 하지 않았다(exstyle 에 WS_EX_TRANSPARENT 가
    /// 안 붙고, WindowFromPoint 가 이 창을 계속 집었다). 화면상으로는 멀쩡해 보여서
    /// 눈으로는 절대 안 잡히는 종류의 실패다 — 유저가 장식 위를 클릭해 봐야 드러난다.
    /// tools/inspect-windows.ps1 로 히트테스트해서 잡았다.
    ///
    /// 셸 창이 이미 쓰고 있는 폴리곤 API 는 동작하는 것이 확인됐으므로 그쪽을 쓴다.
    /// </summary>
    private void ApplyClickThrough()
    {
        if (SkipClickThrough)
        {
            GD.Print("[cursor] click-through 생략 (진단 모드)");
            return;
        }

        if (OS.GetName() != "Windows")
        {
            ClickThroughState = "미지원 플랫폼";
            return;
        }

        long handle = DisplayServer.WindowGetNativeHandle(
            DisplayServer.HandleType.WindowHandle, _win.GetWindowId());

        if (handle == 0)
        {
            ClickThroughState = "HWND 없음";
            GD.PrintErr("[cursor] HWND 를 못 얻었다. 클릭 통과를 걸 수 없다");
            return;
        }

        var hwnd = new IntPtr(handle);

        switch (ClickThrough)
        {
            case ClickThroughMode.Transparent:
                ApplyExStyle(hwnd, WsExTransparent, "WS_EX_TRANSPARENT");
                break;

            case ClickThroughMode.Layered:
                ApplyExStyle(hwnd, WsExTransparent | WsExLayered, "TRANSPARENT|LAYERED");

                // LAYERED 를 붙이면 알파를 정해주기 전까지 창이 아예 안 보인다.
                // 255 = 완전 불투명이지만, Godot 이 DWM 합성으로 그리는 per-pixel 알파는
                // 그대로 살아남는다(실측: 장식 주변 모서리에 뒤 배경이 비쳤다).
                if (!SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha))
                {
                    GD.PrintErr("[cursor] SetLayeredWindowAttributes 실패");
                }

                break;

            case ClickThroughMode.HitTest:
                HookWndProc(hwnd);
                break;
        }
    }

    private void ApplyExStyle(IntPtr hwnd, long bits, string label)
    {
        long before = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(before | bits));

        // 반드시 되읽어서 확인한다. Window.Flags.MousePassthrough 가 조용히 아무것도
        // 하지 않는 것을 이미 밟았다. 다만 "걸렸다"가 "동작한다"는 아니다 -
        // WS_EX_TRANSPARENT 는 되읽기도 통과했는데 실제 클릭은 막지 못했다.
        long after = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        bool ok = (after & bits) == bits;

        ClickThroughState = ok ? label : $"{label} 실패";
        GD.Print($"[cursor] click-through {label} {(ok ? "적용" : "실패")}"
            + $" (ex 0x{before:X} -> 0x{after:X})");
    }

    /// <summary>
    /// 창의 WndProc 을 가로채서 WM_NCHITTEST 에 HTTRANSPARENT 를 돌려준다.
    ///
    /// exstyle 방식과 달리 이건 Windows 의 히트테스트 경로에 직접 답하는 것이라,
    /// DirectComposition 창(Godot 은 투명 창에 WS_EX_NOREDIRECTIONBITMAP 을 쓴다)에서도
    /// 렌더링을 건드리지 않는다.
    /// </summary>
    private void HookWndProc(IntPtr hwnd)
    {
        if (_originalWndProc != IntPtr.Zero)
        {
            return;
        }

        _wndProcHook = HookProc;
        IntPtr ptr = Marshal.GetFunctionPointerForDelegate(_wndProcHook);
        _originalWndProc = SetWindowLongPtr(hwnd, GwlpWndProc, ptr);

        bool ok = _originalWndProc != IntPtr.Zero;
        ClickThroughState = ok ? "WM_NCHITTEST" : "WndProc 후킹 실패";
        GD.Print($"[cursor] click-through WM_NCHITTEST {(ok ? "적용" : "실패")}");
    }

    /// <summary>
    /// 걸어둔 클릭 통과를 되돌린다. 방식을 바꿔가며 비교하려면 이게 있어야 한다 -
    /// 안 그러면 이전 방식이 남아서 무엇이 효과를 냈는지 알 수 없다.
    /// </summary>
    private void ClearClickThrough()
    {
        if (!IsSupported || OS.GetName() != "Windows")
        {
            return;
        }

        long handle = DisplayServer.WindowGetNativeHandle(
            DisplayServer.HandleType.WindowHandle, _win.GetWindowId());
        if (handle == 0)
        {
            return;
        }

        var hwnd = new IntPtr(handle);

        if (_originalWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr(hwnd, GwlpWndProc, _originalWndProc);
            _originalWndProc = IntPtr.Zero;
            _wndProcHook = null;
        }

        long ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(ex & ~(WsExTransparent | WsExLayered)));

        ClickThroughState = "미적용";
    }

    /// <summary>
    /// 클릭 통과 방식을 순환한다. 재시작 없이 비교하기 위한 것이다.
    ///
    /// 이 항목은 자동 계측으로 여러 번 오판했다. 특히 **같은 프로세스 창을 표적으로 쓴
    /// 실험은 믿을 수 없다** - Godot 은 입력을 앱 단위로 처리해서, 장식 창에 떨어진
    /// 클릭이 엔진 내부 경로로 셸 씬까지 도달할 수 있다. 상주 앱에서 중요한 것은
    /// **다른 프로세스의 창**이 입력을 받는가이고, 그건 사람이 메모장을 클릭해 보는 게
    /// 제일 빠르고 확실하다.
    /// </summary>
    public void CycleClickThrough()
    {
        ClearClickThrough();
        ClickThrough = (ClickThroughMode)(((int)ClickThrough + 1) % 3);

        if (Enabled && IsSupported)
        {
            ApplyClickThrough();
        }

        GD.Print($"[cursor] click-through 방식 -> {ClickThrough} ({ClickThroughState})");
    }

    private IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmNcHitTest)
        {
            return HtTransparent;
        }

        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    // ------------------------------------------------------------------ 루프

    public void Tick(double delta)
    {
        if (!_built || !IsSupported || !Enabled)
        {
            return;
        }

        Vector2 target = Simulate ? SimulatedTarget(delta) : DisplayServer.MouseGetPosition();

        // 모드별로 "어디에 있고 싶은가"를 정한다. 창을 옮기는 것은 그 다음이다.
        switch (Mode)
        {
            case FollowMode.Direct:
                _pos = target;
                break;

            case FollowMode.Spring:
                // 프레임레이트에 안 흔들리는 지수 감쇠. FPS 캡을 바꿔가며 재는 스파이크라
                // delta 를 그냥 곱하는 방식은 쓸 수 없다.
                _pos = _pos.Lerp(target, 1.0f - Mathf.Exp(-14.0f * (float)delta));
                // 매달린 느낌은 지연만으로는 안 난다. 속도에 따라 흔들려야 한다.
                _phase += delta * 6.0;
                break;

            case FollowMode.Lazy:
                // §1.3 폴백: 커서에 붙는 게 아니라 근처에 상주하며 느슨하게 따라온다.
                if (_pos.DistanceTo(target) > 90.0f)
                {
                    _pos = _pos.Lerp(target, 1.0f - Mathf.Exp(-3.0f * (float)delta));
                }
                break;
        }

        _sinceMove += delta * 1000.0;
        if (IntervalMs > 0 && _sinceMove < IntervalMs)
        {
            _skipped++;
            return;
        }

        _sinceMove = 0.0;
        MoveWindow();
    }

    /// <summary>
    /// 리사주 곡선. 원이 아니라 이 곡선을 쓰는 이유는, 원은 x/y 변화량이 일정해서
    /// 매 틱 같은 거리를 움직이는 반면 리사주는 빠른 구간과 느린 구간이 섞이기 때문이다.
    /// "커서가 멈춰 있으면 창을 안 옮긴다"는 최적화가 실제로 먹는지도 같이 드러난다.
    /// </summary>
    private Vector2 SimulatedTarget(double delta)
    {
        _simTime += delta;
        return _simCenter + new Vector2(
            Mathf.Sin((float)_simTime * 1.7f) * _simRadius,
            Mathf.Sin((float)_simTime * 2.3f) * _simRadius);
    }

    /// <summary>
    /// 창을 옮기는 유일한 지점. 부하의 출처가 여기로 모여 있어야 계측이 말이 된다.
    /// </summary>
    private void MoveWindow()
    {
        var half = new Vector2I(WindowSize / 2, WindowSize / 2);
        var next = new Vector2I(Mathf.RoundToInt(_pos.X), Mathf.RoundToInt(_pos.Y)) - half;

        if (_win.Position == next)
        {
            // 커서가 멈춰 있으면 OS 를 건드릴 이유가 없다. 상주 앱에서 유휴 부하의
            // 대부분이 "안 바뀐 값을 계속 쓰는 것"에서 나온다.
            _skipped++;
            return;
        }

        _win.Position = next;
        _moves++;

        if (Mode == FollowMode.Spring)
        {
            _deco.Rotation = Mathf.Sin((float)_phase) * 0.18f;
        }
    }

    // ------------------------------------------------------------------ 토글

    public void SetEnabled(bool on)
    {
        if (!_built)
        {
            return;
        }

        _win.Visible = on;

        if (on)
        {
            // 창은 보이게 된 다음에야 OS 창이 된다. 판정도 그 다음이다.
            VerifyWindow();
            if (!IsSupported)
            {
                _win.Visible = false;
                Enabled = false;
                GD.PrintErr($"[cursor] OS window 실패: {FailureReason}");
                return;
            }

            ApplyClickThrough();

            _pos = DisplayServer.MouseGetPosition();
            MoveWindow();
        }

        Enabled = on;
    }

    public void CycleInterval()
    {
        IntervalIndex = (IntervalIndex + 1) % IntervalsMs.Length;
        ResetCounters();
    }

    /// <summary>
    /// Godot 이 이 창의 렌더 타깃에 실제로 무엇을 그렸는지 읽는다.
    ///
    /// "화면에 아무것도 안 보인다"의 원인이 둘인데 밖에서는 구분이 안 된다:
    ///   (a) Godot 이 애초에 안 그렸다        -> 렌더 타깃이 비어 있다
    ///   (b) 그렸는데 화면에 못 올렸다        -> 렌더 타깃에는 내용이 있다 (합성/표시 문제)
    /// 엔진 안에서 읽으면 이게 갈린다. 밖에서 화면을 캡처하는 것만으로는 못 가른다.
    /// </summary>
    public string ProbeRenderTarget()
    {
        if (!_built || !IsSupported)
        {
            return "render target: n/a";
        }

        try
        {
            Image img = _win.GetTexture()?.GetImage();
            if (img == null)
            {
                return "render target: null (텍스처 없음)";
            }

            int w = img.GetWidth();
            int h = img.GetHeight();
            Color mid = img.GetPixel(w / 2, h / 2);
            Color corner = img.GetPixel(2, 2);

            return $"render target: {w}x{h} center=({mid.R:F2},{mid.G:F2},{mid.B:F2},{mid.A:F2})"
                + $" corner=({corner.R:F2},{corner.G:F2},{corner.B:F2},{corner.A:F2})";
        }
        catch (Exception e)
        {
            return $"render target: 읽기 실패 ({e.GetType().Name}: {e.Message})";
        }
    }

    /// <summary>진단용 불투명 사각형 토글. 자세한 이유는 <see cref="DebugFill"/>.</summary>
    public void ToggleDebugFill()
    {
        if (!_built)
        {
            return;
        }

        DebugFill = !DebugFill;
        _debugFill.Visible = DebugFill;
        GD.Print($"[cursor] debug fill {(DebugFill ? "ON (분홍 사각형이 보여야 한다)" : "off")}");
    }

    public void CycleMode()
    {
        Mode = (FollowMode)(((int)Mode + 1) % 3);
        _deco.Rotation = 0.0f;
        ResetCounters();
    }

    public void ResetCounters()
    {
        _moves = 0;
        _skipped = 0;
    }

    /// <summary>
    /// Godot 이 읽은 커서 좌표와 장식 창이 실제로 놓인 좌표를 같이 찍는다.
    ///
    /// 멀티모니터에서 이 둘이 어긋나면 장식이 커서가 아닌 엉뚱한 곳에 그려진다.
    /// 창이 "안 보인다"의 원인이 될 수 있고, 밖에서 창 위치만 봐서는 못 가른다.
    /// </summary>
    public string PositionLine()
    {
        if (!_built)
        {
            return "cursor pos: n/a";
        }

        Vector2I mouse = DisplayServer.MouseGetPosition();
        Vector2I win = _win.Position;
        Vector2I center = win + new Vector2I(WindowSize / 2, WindowSize / 2);
        Vector2I off = center - mouse;

        return $"cursor pos: mouse {mouse.X},{mouse.Y}  deco-center {center.X},{center.Y}"
            + $"  offset {off.X},{off.Y}  screen #{DisplayServer.WindowGetCurrentScreen(_win.GetWindowId())}";
    }

    /// <summary>HUD / 리포트용 한 줄. ASCII 전용 - 기본 테마 폰트에 한글 글리프가 없다.</summary>
    public string StatusLine()
    {
        if (!_built)
        {
            return $"cursor NOT BUILT ({FailureReason})";
        }

        if (Enabled && !IsSupported)
        {
            return $"cursor UNSUPPORTED ({FailureReason})";
        }

        string interval = IntervalMs == 0 ? "frame" : $"{IntervalMs}ms";
        return $"cursor {(Enabled ? "on" : "off")}{(Simulate ? " SIM" : "")}{(DebugFill ? " FILL" : "")},"
            + $" {Mode.ToString().ToLowerInvariant()}, every {interval},"
            + $" moves {_moves}, skip {_skipped}, clickthru {ClickThroughState}";
    }
}
