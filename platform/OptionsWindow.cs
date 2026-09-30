using System;
using Godot;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 옵션 창 (§7-4 전 항목, A6).
///
/// <c>CanvasLayer</c>다 - <see cref="DebugHud"/>와 같은 이유로, 셸 배율(<c>SetScale</c>)이
/// 바뀌어도 옵션 UI 자체는 안 흔들리고 항상 같은 크기로 보여야 한다
/// (docs/A3-SHELL-MODULE.md §1 "뜻밖의 소득" 참고).
///
/// **이 클래스는 저장을 모른다.** 값이 바뀌면 이벤트만 쏘고, 실제로 SaveIO에
/// 쓰고 IShell/CursorLayer/Autostart에 적용하는 것은 전부 OverlayShell(호출부) 몫이다.
/// 옵션 UI가 세이브 스키마나 다른 서브시스템을 직접 알 필요가 없게 하려는 것이다.
/// </summary>
public partial class OptionsWindow : CanvasLayer
{
    private HSlider _scaleSlider;
    private HSlider _friendScaleSlider;
    private HSlider _cursorScaleSlider;
    private HSlider _friendCursorScaleSlider;
    private HSlider _opacitySlider;
    private CheckBox _cursorEnabled;
    private CheckBox _cursorIndependent;
    private CheckBox _hideOnFullscreen;
    private CheckBox _autostart;
    private PanelContainer _panel;
    private MarginContainer _margin;
    private Button _closeButton;
    private HSeparator _closeRule;

    /// <summary>기본 위쪽 여백. <see cref="SetArea"/> 가 이보다 작게는 안 줄인다.</summary>
    private const int MarginTop = 12;

    /// <summary>
    /// <see cref="SetValues"/>가 컨트롤 값을 초기화하는 동안 켠다. Godot 컨트롤은
    /// 코드로 <c>.Value</c>/<c>.ButtonPressed</c>를 바꿔도 시그널이 그대로 뜬다 -
    /// 이걸 안 막으면 옵션 창을 여는 순간 이벤트가 전부 다시 발사돼서
    /// 세이브를 쓸데없이 다시 쓴다.
    /// </summary>
    private bool _initializing;

    public event Action<float> ScaleChanged;
    public event Action<float> FriendScaleChanged;
    public event Action<float> CursorScaleChanged;
    public event Action<float> FriendCursorScaleChanged;
    public event Action<float> OpacityChanged;
    public event Action<bool> CursorEnabledChanged;
    public event Action<bool> CursorIndependentChanged;
    public event Action<bool> HideOnFullscreenChanged;
    public event Action<bool> AutostartChanged;

    /// <summary>
    /// 닫힐 때(닫기 버튼) 쏜다. 호출부(OverlayShell)가 이걸 듣고 클릭 통과를
    /// 원래 상태로 되돌린다 - 옵션 창이 열린 동안은 창 전체가 클릭을 받아야 했다.
    /// </summary>
    public event Action Closed;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Layer = 110; // DebugHud(100)보다 위. 옵션 창이 항상 HUD 위에 보여야 한다.
        Visible = false;
        BuildUi();
    }

    public void Open() => Visible = true;

    /// <summary>
    /// 패널을 놓을 자리. 게임 메뉴(MenuHub)에서 열면 그 탭 줄이 창 위를 덮고 있어서 줄 아래로 내리고, 창 왼쪽의 메뉴 칸
    /// (<paramref name="width"/>, IShell.SetSidePanel) 안에만 둔다 - 게임 화면을 가리지 않는다 (2026-09-28).
    /// 트레이에서 열면 둘 다 0 이라 창 전체에 기본 여백(<see cref="MarginTop"/>)을 쓴다 (IShell.OpenOptions).
    ///
    /// <b>메뉴 안에서는 상점·강화·로비 창과 같은 모양이 된다</b> (2026-09-27): 칸 아래까지 채우고, 옆·아래 여백을
    /// 그 창들과 같은 10 으로, 자기 [닫기] 는 숨긴다 - 탭 줄에 [닫기] 가 있다. 트레이에서 열면 예전 모양 그대로다.
    /// </summary>
    /// <param name="height">메뉴 칸 높이(창 px). 0 이면 창 아래 끝까지.</param>
    public void SetArea(float topInset, float width, float height)
    {
        bool inMenu = topInset > 0f;
        int side = inMenu ? 10 : MarginTop;
        _margin.AddThemeConstantOverride("margin_top", Math.Max(MarginTop, Mathf.RoundToInt(topInset)));
        _margin.AddThemeConstantOverride("margin_left", side);
        _margin.AddThemeConstantOverride("margin_right", side);
        _margin.AddThemeConstantOverride("margin_bottom", side);

        // 폭이 있으면 창 왼쪽 위에 그 폭·높이로 (메뉴 칸, MenuHub.AnchorToPanel 과 같은 자리). 높이를 고정하는 것은 배율
        // 슬라이더로 창이 길어져도 메뉴까지 길어지지 않게 하려는 것이다 (2026-09-30).
        if (width > 0f)
        {
            _margin.SetAnchorsPreset(height > 0f ? Control.LayoutPreset.TopLeft : Control.LayoutPreset.LeftWide);
            _margin.OffsetLeft = 0;
            _margin.OffsetTop = 0;
            _margin.OffsetRight = width;
            _margin.OffsetBottom = height;
        }
        else
        {
            _margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }

        _panel.SizeFlagsVertical = inMenu ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill;
        _closeButton.Visible = !inMenu;
        _closeRule.Visible = !inMenu;
    }

    public void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }

    /// <summary>
    /// 컨트롤 값을 세이브 값으로 맞춘다. 이벤트는 안 쏜다(<see cref="_initializing"/>).
    /// 옵션 창을 열 때마다 불러서, 다른 경로(예: F11/[/])로 바뀐 값과 어긋나지 않게 한다.
    /// </summary>
    public void SetValues(Shared.SaveData.SettingsState s, bool autostartActual)
    {
        _initializing = true;

        _scaleSlider.Value = s.Scale;
        _friendScaleSlider.Value = s.FriendScale;
        _cursorScaleSlider.Value = s.CursorScale;
        _friendCursorScaleSlider.Value = s.FriendCursorScale;
        _opacitySlider.Value = s.Opacity;
        _cursorEnabled.ButtonPressed = s.CursorEnabled;
        _cursorIndependent.ButtonPressed = s.CursorIndependent;
        _cursorIndependent.Disabled = !s.CursorEnabled;
        _hideOnFullscreen.ButtonPressed = s.HideOnFullscreen;

        // 자동 시작은 세이브 값이 아니라 레지스트리의 실제 값으로 맞춘다 - 유저가
        // Windows "시작 앱" 설정에서 수동으로 껐다면 체크박스도 그 사실을 보여줘야 한다.
        _autostart.ButtonPressed = autostartActual;

        _initializing = false;
    }

    // ------------------------------------------------------------------ UI 구성

    private void BuildUi()
    {
        var margin = _margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", MarginTop);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        // HUD 는 화면 하단에 붙지만(§ DebugHud), 옵션 창은 위쪽에 둔다 - 둘 다
        // 켜져 있어도 겹치지 않게.
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        margin.AddChild(column);

        _panel = new PanelContainer();
        _panel.AddThemeStyleboxOverride("panel", MakeBackground());
        column.AddChild(_panel);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(rows);

        rows.AddChild(MakeTitle("설정"));
        rows.AddChild(new HSeparator());

        // 크기는 넷으로 나뉜다 (2026-09-30). 범위는 OverlayShell 의 Set*Scale clamp 와 같고, 0.25 칸씩 끊는다
        // (OverlayShell.ScaleStep) - 0.05 씩이면 조금만 끌어도 크기가 바뀌어 원하는 값에 멈추기 어려웠다. 칸마다 눈금을 단다.
        rows.AddChild(MakeScaleRow("내 창 크기", 0.5f, 2.0f, out _scaleSlider));
        _scaleSlider.ValueChanged += v => Relay(() => ScaleChanged?.Invoke((float)v));

        rows.AddChild(MakeScaleRow("친구 칸 크기", 0.5f, 2.0f, out _friendScaleSlider));
        _friendScaleSlider.ValueChanged += v => Relay(() => FriendScaleChanged?.Invoke((float)v));

        rows.AddChild(MakeScaleRow("내 커서 크기", 0.5f, 2.0f, out _cursorScaleSlider));
        _cursorScaleSlider.ValueChanged += v => Relay(() => CursorScaleChanged?.Invoke((float)v));

        rows.AddChild(MakeScaleRow("친구 커서 크기 (왼쪽 끝 숨김)", 0f, 1.25f, out _friendCursorScaleSlider));
        _friendCursorScaleSlider.ValueChanged += v => Relay(() => FriendCursorScaleChanged?.Invoke((float)v));

        HBoxContainer opacityRow = MakeSliderRow("투명도", 0.1f, 1.0f, 0.05f, out _opacitySlider);
        AddValueLabel(opacityRow, _opacitySlider, PercentText);
        rows.AddChild(opacityRow);
        _opacitySlider.ValueChanged += v => Relay(() => OpacityChanged?.Invoke((float)v));


        rows.AddChild(MakeCheckRow("커서 장식", out _cursorEnabled));
        _cursorEnabled.Toggled += on =>
        {
            // 커서 자체를 끄면 "숨겨도 유지" 는 물어볼 것이 없어진다. 값은 그대로
            // 두고 조작만 막는다 - 체크를 강제로 풀면 다시 켰을 때 유저가 정해 둔
            // 값이 사라진다.
            _cursorIndependent.Disabled = !on;
            Relay(() => CursorEnabledChanged?.Invoke(on));
        };

        rows.AddChild(MakeCheckRow("숨겨도 커서 장식은 유지", out _cursorIndependent));
        _cursorIndependent.Toggled += on => Relay(() => CursorIndependentChanged?.Invoke(on));

        rows.AddChild(MakeCheckRow("전체화면 앱 위에서 숨김", out _hideOnFullscreen));
        _hideOnFullscreen.Toggled += on => Relay(() => HideOnFullscreenChanged?.Invoke(on));

        rows.AddChild(MakeCheckRow("Windows 시작 시 자동 실행", out _autostart));
        _autostart.Toggled += on => Relay(() => AutostartChanged?.Invoke(on));

        _closeRule = new HSeparator();
        rows.AddChild(_closeRule);

        _closeButton = new Button { Text = "닫기" };
        _closeButton.Pressed += Close;
        rows.AddChild(_closeButton);
    }

    /// <summary><see cref="_initializing"/> 중에는 이벤트를 막는다.</summary>
    private void Relay(Action fire)
    {
        if (!_initializing)
        {
            fire();
        }
    }

    private static Label MakeTitle(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 15);
        label.AddThemeColorOverride("font_color", new Color(0.90f, 0.94f, 0.99f));
        return label;
    }

    private static HBoxContainer MakeSliderRow(string label, float min, float max, float step, out HSlider slider)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var text = new Label { Text = label, CustomMinimumSize = new Vector2(150, 0) };
        text.AddThemeFontSizeOverride("font_size", 12);
        text.AddThemeColorOverride("font_color", new Color(0.85f, 0.88f, 0.93f));
        row.AddChild(text);

        slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            CustomMinimumSize = new Vector2(160, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddChild(slider);

        return row;
    }

    /// <summary>
    /// 크기 슬라이더 - <see cref="OverlayShell.ScaleStep"/> 칸마다 눈금, 오른쪽에 지금 배율("1.0x", "1.25x"). 0 은 숨김이다
    /// (친구 커서).
    /// </summary>
    private static HBoxContainer MakeScaleRow(string label, float min, float max, out HSlider slider)
    {
        HBoxContainer row = MakeSliderRow(label, min, max, OverlayShell.ScaleStep, out slider);
        slider.TickCount = Mathf.RoundToInt((max - min) / OverlayShell.ScaleStep) + 1;
        slider.TicksOnBorders = true;
        AddValueLabel(row, slider, ScaleText);
        return row;
    }

    /// <summary>
    /// 슬라이더 줄 오른쪽 끝에 지금 값을 글자로 단다. <see cref="SetValues"/> 로 값을 맞출 때도 바뀐다 - 이벤트를 막는
    /// <see cref="Relay"/> 밖에서 단다.
    /// </summary>
    private static void AddValueLabel(HBoxContainer row, HSlider slider, Func<double, string> format)
    {
        var value = new Label
        {
            CustomMinimumSize = new Vector2(40, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = format(slider.Value),
        };
        value.AddThemeFontSizeOverride("font_size", 12);
        value.AddThemeColorOverride("font_color", new Color(0.85f, 0.88f, 0.93f));
        row.AddChild(value);
        slider.ValueChanged += v => value.Text = format(v);
    }

    /// <summary>투명도 0.1~1.0 → "10%"~"100%". 0.05 칸이라 5% 단위로 나온다.</summary>
    private static string PercentText(double v) => $"{Math.Round(v * 100):0}%";

    /// <summary>1 → "1.0x", 1.25 → "1.25x", 0.5 → "0.5x", 0 → "숨김".</summary>
    private static string ScaleText(double v) =>
        v <= 0 ? "숨김" : v.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + "x";

    private static HBoxContainer MakeCheckRow(string label, out CheckBox box)
    {
        var row = new HBoxContainer();
        box = new CheckBox { Text = label };
        box.AddThemeFontSizeOverride("font_size", 12);
        box.AddThemeIconOverride("unchecked", CheckIcons.Unchecked);
        box.AddThemeIconOverride("checked", CheckIcons.Checked);
        box.AddThemeIconOverride("unchecked_disabled", CheckIcons.UncheckedDisabled);
        box.AddThemeIconOverride("checked_disabled", CheckIcons.CheckedDisabled);
        row.AddChild(box);
        return row;
    }

    /// <summary>
    /// 체크박스 아이콘. <b>기본 테마의 빈 칸은 어두운 칸에 어두운 테두리라 이 창 배경 위에서 거의 안 보였다</b>
    /// (2026-09-27) - "위치 잠금" 처럼 꺼진 옵션은 체크박스가 있다는 것조차 안 읽혔다. 밝은 테두리의 빈 칸과
    /// 창 테두리 색으로 채운 체크 칸을 코드로 그린다(그림 파일을 따로 두지 않는다 - 16px 두 장이다).
    /// </summary>
    private static class CheckIcons
    {
        private const int Size = 16;
        private static readonly Color Border = new(0.70f, 0.76f, 0.84f);
        private static readonly Color Empty = new(0.10f, 0.13f, 0.18f);
        private static readonly Color Fill = new(0.30f, 0.55f, 0.75f);

        public static readonly Texture2D Unchecked = Draw(check: false, alpha: 1f);
        public static readonly Texture2D Checked = Draw(check: true, alpha: 1f);
        public static readonly Texture2D UncheckedDisabled = Draw(check: false, alpha: 0.4f);
        public static readonly Texture2D CheckedDisabled = Draw(check: true, alpha: 0.4f);

        private static Texture2D Draw(bool check, float alpha)
        {
            Image image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
            for (int y = 1; y < Size - 1; y++)
            {
                for (int x = 1; x < Size - 1; x++)
                {
                    bool edge = x <= 2 || y <= 2 || x >= Size - 3 || y >= Size - 3;
                    Color c = edge ? (check ? Fill.Lightened(0.25f) : Border) : (check ? Fill : Empty);
                    image.SetPixel(x, y, c with { A = alpha });
                }
            }

            if (check)
            {
                // 흰 체크 표시: (4,8) → (7,11) → (12,5), 두께 2px
                Line(image, new Vector2(4, 8), new Vector2(7, 11), alpha);
                Line(image, new Vector2(7, 11), new Vector2(12, 5), alpha);
            }

            return ImageTexture.CreateFromImage(image);
        }

        private static void Line(Image image, Vector2 from, Vector2 to, float alpha)
        {
            int steps = Mathf.CeilToInt(from.DistanceTo(to) * 3f);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = from.Lerp(to, i / (float)steps);
                for (int dy = 0; dy <= 1; dy++)
                {
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        image.SetPixel(Mathf.RoundToInt(p.X) + dx - 1, Mathf.RoundToInt(p.Y) + dy - 1, Colors.White with { A = alpha });
                    }
                }
            }
        }
    }

    private static StyleBoxFlat MakeBackground()
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.07f, 0.10f, 0.92f),
            BorderColor = new Color(0.30f, 0.55f, 0.75f, 0.95f),
        };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(12);
        box.SetCornerRadiusAll(5);
        return box;
    }
}
