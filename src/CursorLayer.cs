using System;
using Godot;

namespace ProjectSeWoo;

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

    /// <summary>
    /// "어떤 점도 포함하지 않는" passthrough 폴리곤 = 창 전체 클릭 통과.
    ///
    /// Godot 의 window_set_mouse_passthrough 는 "이 폴리곤 **안쪽**이 마우스를 받는다"는
    /// 의미이고, 빈 배열은 passthrough 를 끄는 것(창 전체가 마우스를 먹음)이다.
    /// 그래서 전체 통과를 표현하려면 비우는 게 아니라, 창 밖에 있는 축퇴 삼각형을
    /// 준다. 그러면 어느 점을 찍어도 폴리곤 안이 아니므로 전부 통과한다.
    ///
    /// Windows 에서 Godot 은 이걸 WM_NCHITTEST 의 HTTRANSPARENT 로 구현하므로
    /// 렌더링에는 영향이 없다. SetWindowRgn 처럼 창을 잘라내는 방식이 아니다.
    /// </summary>
    private static readonly Vector2[] NoHitRegion =
    {
        new(-4.0f, -4.0f),
        new(-3.0f, -4.0f),
        new(-4.0f, -3.0f),
    };

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
    public void Build()
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
            Transparent = true,
            TransparentBg = true,
            Unfocusable = true,
            Unresizable = true,
            Size = new Vector2I(WindowSize, WindowSize),
            Visible = false,
        };
        _host.AddChild(_win);

        var texture = GD.Load<Texture2D>("res://icon.svg");
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
        GD.Print($"[cursor] window id={id} size={WindowSize} -> OS window OK");
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
        DisplayServer.WindowSetMousePassthrough(NoHitRegion, _win.GetWindowId());
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
        return $"cursor {(Enabled ? "on" : "off")}{(Simulate ? " SIM" : "")},"
            + $" {Mode.ToString().ToLowerInvariant()}, every {interval},"
            + $" moves {_moves}, skip {_skipped}";
    }
}
