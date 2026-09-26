using System;
using System.Runtime.InteropServices;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 커서 장식 레이어. <see cref="ICursorLayer"/> 실물이다
/// (A5, 기획확정-일감분배-260907.md §3-1 / §8-2).
///
/// A2 스파이크(창이 하나 더 뜨는가 / CPU / 클릭 통과)가 전부 Go로 끝났고
/// (docs/A2-CURSOR-SPIKE.md), 이 클래스는 그 위에 3슬롯 장착을 얹은 정식 버전이다.
/// 진단 전용이었던 것들(불투명 사각형 채우기, 렌더 타깃 픽셀 읽기, 클릭 통과
/// 방식 순환)은 A5에서 걷어냈다 — A2가 이미 "Layered가 정답, 나머지 둘은 쓰면
/// 안 된다"고 결론 낸 뒤라, 고를 수 있게 남겨두는 것 자체가 위험이었다.
///
/// 이 게임의 재화 소비처이자 차별화 훅이 "커서 꾸미기"다. 채택된 방식(§1.3 C안):
/// 시스템 커서 본체는 그대로 두고, 커서 좌표를 따라다니는 클릭 통과 투명 창에
/// 장식만 그린다.
/// </summary>
public sealed class CursorLayer : ICursorLayer
{
    /// <summary>장식 창 한 변. 커서 주변 장식이 들어갈 만큼만. 작을수록 컴포지팅이 싸다.</summary>
    private const int WindowSize = 128;

    /// <summary>이동 주기 후보(ms). 0 = 매 프레임.</summary>
    public static readonly int[] IntervalsMs = { 0, 16, 33, 50, 100 };

    // --- 3칸 레이아웃 (B17). 배열 인덱스는 CursorSlot 의 선언 순서(Monkey=0, Banana=1, Deco=2).
    // **임시다** - B17 3단계에서 바나나 고정 자리 + 원숭이 리그로 바뀐다 (docs/B17-CURSOR-REWORK.md §4).
    // 지금은 그림 한 장씩: 바나나는 커서 끝 아래, 원숭이는 그 바나나에 매달리고, 장식은 뒤에 깔린다.
    private static readonly Vector2[] SlotOffset =
    {
        new(10, 34),  // Monkey: 바나나 아래에 매달림
        new(10, 14),  // Banana: 커서 끝 바로 아래
        new(0, 4),    // Deco: 커서 뒤
    };

    private static readonly float[] SlotScale = { 0.42f, 0.26f, 0.34f };

    private static readonly float[] SlotAlpha = { 1.0f, 1.0f, 1.0f };

    /// <summary>원숭이가 맨 위, 장식이 맨 아래, 바나나가 그 사이.</summary>
    private static readonly int[] SlotZIndex = { 2, 1, 0 };

    // --- Win32 클릭 통과 ---------------------------------------------------
    //
    // Godot 의 window_set_mouse_passthrough 는 Windows 에서 SetWindowRgn 으로
    // 구현돼 있다. 즉 창을 **물리적으로 잘라낸다.** 그래서 "창 전체 통과"를 노리고
    // 창 밖의 축퇴 폴리곤을 주면 창이 통째로 잘려서 아무것도 안 보이게 된다.
    // 실제로 이 프로젝트에서 그렇게 만들었고, "클릭이 통과된다"는 측정 결과까지
    // 같이 나와서 성공으로 오판했다 — 거기 창이 아예 없었기 때문이다.
    //
    // 올바른 방법은 WS_EX_TRANSPARENT + WS_EX_LAYERED 를 거는 것이다 (docs/A2-CURSOR-SPIKE.md
    // §2). A2에서 세 가지(TRANSPARENT 단독 / TRANSPARENT+LAYERED / WM_NCHITTEST 후킹)를
    // 실사용으로 비교했고 TRANSPARENT+LAYERED만 다른 프로세스의 클릭을 통과시켰다.
    // 나머지 둘은 지웠다 — 고를 수 있게 남겨두면 언젠가 실수로 고른다.

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
    private const uint LwaAlpha = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);

    /// <summary>
    /// 추종 모드. Lazy 는 §1.3의 폴백안("고빈도 추종 없이 느슨하게 따라옴")을
    /// 미리 만들어 둔 것이다. A2에서 "고무줄 현상"을 옵션으로 노출하기로 했으므로
    /// (2026-09-14 결정, docs/A2-CURSOR-SPIKE.md), 이 셋은 진단용이 아니라
    /// **실제 유저 옵션 후보**다. A6이 옵션 UI를 만들면 이 자리를 대신 호출한다.
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
    private readonly SlotVisual[] _slots = new SlotVisual[3];
    private Texture2D _placeholderTexture;

    /// <summary>슬롯 하나의 화면 요소 + 장착 상태.</summary>
    private sealed class SlotVisual
    {
        public Sprite2D Sprite;
        public string AssetId;
    }

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
    ///
    /// A7(저부하 실측, 커서 창 포함)이 이걸 계속 쓴다 - 지우지 않는다.
    /// </summary>
    public bool Simulate { get; set; }

    /// <summary>
    /// 진단용. 클릭 통과를 걸지 않는다. 그것이 창을 안 보이게 만드는 범인인지 가른다.
    /// A7의 부하 비교(클릭 통과 유/무)에도 쓸 수 있어 남겨둔다.
    /// </summary>
    public bool SkipClickThrough { get; set; }

    /// <summary>클릭 통과가 실제로 걸렸는지. 리포트에 박아서 조용한 실패를 막는다.</summary>
    public string ClickThroughState { get; private set; } = "미적용";

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
    /// 두 번째 OS 창을 만든다. A2 스파이크의 성패가 여기서 갈렸다(Go).
    ///
    /// <c>embed_subwindows=false</c> 가 project.godot 에 있어야 Window 노드가
    /// 게임 화면 안에 그려지는 가짜 창이 아니라 진짜 OS 창이 된다. 그게 없으면
    /// 창이 "떠 있는 것처럼" 보이지만 바탕화면 위로는 못 나간다.
    /// </summary>
    /// <param name="opaque">
    /// 진단용. 창을 투명하지 않게 만든다. A2에서 "서브 윈도우가 아무것도 렌더하지
    /// 않는다"는 증상의 원인이 투명 설정인지 가르는 데 썼다. 투명은 런타임에 못
    /// 바꾸므로 생성 시점에 정해야 하고, 그래서 핫키가 아니라 인자다.
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

        _placeholderTexture = GD.Load<Texture2D>("res://icon.svg");
        if (_placeholderTexture == null)
        {
            GD.PrintErr("[cursor] icon.svg 로드 실패");
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            var sprite = new Sprite2D
            {
                Name = $"Slot_{(CursorSlot)i}",
                Texture = _placeholderTexture,
                Centered = true,
                Scale = Vector2.One * SlotScale[i],
                Position = new Vector2(WindowSize / 2f, WindowSize / 2f) + SlotOffset[i],
                ZIndex = SlotZIndex[i],
                Visible = false,
            };
            _win.AddChild(sprite);
            _slots[i] = new SlotVisual { Sprite = sprite };
        }

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
    /// (INVALID_WINDOW_ID) 이 돌아온다. A2에서 이걸 "메인 창 id 가 아니니 성공"으로
    /// 읽어서 검증이 통과해 버렸었다. 그래서 판정은 창을 켠 뒤로 옮긴다.
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
        GD.Print($"[cursor] window id={id} size={WindowSize}x{WindowSize} -> OS window OK"
            + $" (transparent {_win.Transparent}, bg {_win.TransparentBg})");
    }

    /// <summary>
    /// 창 전체를 클릭 통과로 만든다. OS 창이 생긴 뒤에 불러야 한다.
    ///
    /// TRANSPARENT|LAYERED만 쓴다 - A2가 실사용으로 확인한 유일한 조합이다.
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

        long before = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(before | WsExTransparent | WsExLayered));

        // 반드시 되읽어서 확인한다. "걸었다"와 "걸렸다"는 다르다 - A2의 1차 실패가 그것이었다.
        long after = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        bool ok = (after & (WsExTransparent | WsExLayered)) == (WsExTransparent | WsExLayered);
        ClickThroughState = ok ? "TRANSPARENT|LAYERED" : "적용 실패";

        // LAYERED 를 붙이면 알파를 정해주기 전까지 창이 아예 안 보인다.
        // 255 = 완전 불투명이지만, Godot 이 DWM 합성으로 그리는 per-pixel 알파는
        // 그대로 살아남는다(A2 실측: 장식 주변 모서리에 뒤 배경이 비쳤다).
        if (ok && !SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha))
        {
            GD.PrintErr("[cursor] SetLayeredWindowAttributes 실패");
        }

        GD.Print($"[cursor] click-through {ClickThroughState} (ex 0x{before:X} -> 0x{after:X})");
    }

    // ------------------------------------------------------------------ 장착 (ICursorLayer)

    /// <summary>
    /// 슬롯에 에셋을 끼운다. <paramref name="assetId"/> 가 null 이면 슬롯을 비운다.
    ///
    /// 실제 아트(을의 B9, "1차 에셋: 커서 장식 16종")는 아직 없다. 그때까지는
    /// <see cref="ResolveTexture"/> 가 자리표시자(icon.svg + assetId 해시 색상)를
    /// 돌려준다. B9가 <c>res://assets/cursor/&lt;slot&gt;/&lt;assetId&gt;.png</c> 를
    /// 채우면 그 경로를 먼저 찾으므로, **여기 호출부는 바뀔 필요가 없다.**
    /// </summary>
    public void Equip(CursorSlot slot, string assetId)
    {
        if (!_built)
        {
            return;
        }

        SlotVisual visual = _slots[(int)slot];
        visual.AssetId = assetId;

        if (assetId == null)
        {
            visual.Sprite.Visible = false;
            return;
        }

        visual.Sprite.Texture = ResolveTexture(slot, assetId, out bool isPlaceholder);

        // **자리표시자일 때만 물들인다.** 해시 색은 "실물이 없을 때도 아이템을
        // 구분해 보이게" 하려고 둔 것이라, 실물 아트에 곱하면 그림이 통째로 그
        // 색이 된다 - B9 로 실물 16종이 들어온 뒤 원숭이도 나무 단면도 전부
        // 분홍으로 나왔다. 알파(슬롯별 투명도)는 양쪽 다 그대로 간다.
        float alpha = SlotAlpha[(int)slot];
        visual.Sprite.Modulate = isPlaceholder
            ? TintFor(assetId, alpha)
            : new Color(1f, 1f, 1f, alpha);

        visual.Sprite.Visible = Enabled;

        GD.Print($"[cursor] {slot} = {assetId}");
    }

    /// <summary>
    /// 슬롯+에셋id 를 텍스처로 바꾼다. 실제 파일이 있으면 그걸, 없으면 자리표시자를 쓴다.
    /// </summary>
    /// <param name="isPlaceholder">
    /// 자리표시자로 떨어졌는가. 호출부가 이 값으로 해시 색을 걸지 말지 정한다 -
    /// 실물에 걸면 그림이 통째로 그 색이 된다.
    /// </param>
    private Texture2D ResolveTexture(CursorSlot slot, string assetId, out bool isPlaceholder)
    {
        string realPath = ItemManifest.IconPath(slot, assetId);
        if (ResourceLoader.Exists(realPath))
        {
            isPlaceholder = false;
            return GD.Load<Texture2D>(realPath);
        }

        isPlaceholder = true;
        return _placeholderTexture;
    }

    /// <summary>
    /// assetId 로부터 안정적인 색을 만든다. **`string.GetHashCode()`는 안 쓴다** -
    /// .NET 은 보안을 위해 프로세스마다 다른 해시를 낸다. 같은 아이템이 실행할 때마다
    /// 다른 색으로 보이면 자리표시자로도 못 쓴다. FNV-1a 로 직접 고정한다.
    /// </summary>
    private static Color TintFor(string assetId, float alpha)
    {
        uint hash = 2166136261u;
        foreach (char c in assetId)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        float hue = (hash % 360u) / 360f;
        Color c2 = Color.FromHsv(hue, 0.55f, 1.0f);
        c2.A = alpha;
        return c2;
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
            // "매달려 흔들리는" 연출은 원숭이 담당이다. B17 3단계에서 리그의 진자로 바뀐다.
            _slots[(int)CursorSlot.Monkey].Sprite.Rotation = Mathf.Sin((float)_phase) * 0.18f;
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

            foreach (SlotVisual slot in _slots)
            {
                slot.Sprite.Visible = slot.AssetId != null;
            }

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
        _slots[(int)CursorSlot.Monkey].Sprite.Rotation = 0.0f;
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
        string equip = $"M:{_slots[(int)CursorSlot.Monkey].AssetId ?? "-"}"
            + $" B:{_slots[(int)CursorSlot.Banana].AssetId ?? "-"}"
            + $" D:{_slots[(int)CursorSlot.Deco].AssetId ?? "-"}";

        return $"cursor {(Enabled ? "on" : "off")}{(Simulate ? " SIM" : "")},"
            + $" {Mode.ToString().ToLowerInvariant()}, every {interval},"
            + $" moves {_moves}, skip {_skipped}, clickthru {ClickThroughState}, equip {equip}";
    }
}
