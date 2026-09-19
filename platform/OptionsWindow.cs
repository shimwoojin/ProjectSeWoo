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
    private HSlider _opacitySlider;
    private CheckBox _positionLocked;
    private CheckBox _sound;
    private CheckBox _notifications;
    private CheckBox _cursorEnabled;
    private CheckBox _cursorIndependent;
    private CheckBox _hideOnFullscreen;
    private CheckBox _keystrokeCounting;
    private CheckBox _autostart;
    private PanelContainer _panel;

    /// <summary>
    /// <see cref="SetValues"/>가 컨트롤 값을 초기화하는 동안 켠다. Godot 컨트롤은
    /// 코드로 <c>.Value</c>/<c>.ButtonPressed</c>를 바꿔도 시그널이 그대로 뜬다 -
    /// 이걸 안 막으면 옵션 창을 여는 순간 아홉 개 이벤트가 전부 다시 발사돼서
    /// 세이브를 쓸데없이 다시 쓴다.
    /// </summary>
    private bool _initializing;

    public event Action<float> ScaleChanged;
    public event Action<float> OpacityChanged;
    public event Action<bool> PositionLockedChanged;
    public event Action<bool> SoundChanged;
    public event Action<bool> NotificationsChanged;
    public event Action<bool> CursorEnabledChanged;
    public event Action<bool> CursorIndependentChanged;
    public event Action<bool> HideOnFullscreenChanged;
    public event Action<bool> KeystrokeCountingChanged;
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
        _opacitySlider.Value = s.Opacity;
        _positionLocked.ButtonPressed = s.PositionLocked;
        _sound.ButtonPressed = s.Sound;
        _notifications.ButtonPressed = s.Notifications;
        _cursorEnabled.ButtonPressed = s.CursorEnabled;
        _cursorIndependent.ButtonPressed = s.CursorIndependent;
        _cursorIndependent.Disabled = !s.CursorEnabled;
        _hideOnFullscreen.ButtonPressed = s.HideOnFullscreen;
        _keystrokeCounting.ButtonPressed = s.KeystrokeCounting;

        // 자동 시작은 세이브 값이 아니라 레지스트리의 실제 값으로 맞춘다 - 유저가
        // Windows "시작 앱" 설정에서 수동으로 껐다면 체크박스도 그 사실을 보여줘야 한다.
        _autostart.ButtonPressed = autostartActual;

        _initializing = false;
    }

    // ------------------------------------------------------------------ UI 구성

    private void BuildUi()
    {
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
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

        rows.AddChild(MakeSliderRow("크기", 0.5f, 2.0f, 0.05f, out _scaleSlider));
        _scaleSlider.ValueChanged += v => Relay(() => ScaleChanged?.Invoke((float)v));

        rows.AddChild(MakeSliderRow("투명도", 0.1f, 1.0f, 0.05f, out _opacitySlider));
        _opacitySlider.ValueChanged += v => Relay(() => OpacityChanged?.Invoke((float)v));

        rows.AddChild(MakeCheckRow("위치 잠금", out _positionLocked));
        _positionLocked.Toggled += on => Relay(() => PositionLockedChanged?.Invoke(on));

        rows.AddChild(MakeCheckRow("사운드", out _sound));
        _sound.Toggled += on => Relay(() => SoundChanged?.Invoke(on));

        rows.AddChild(MakeCheckRow("알림", out _notifications));
        _notifications.Toggled += on => Relay(() => NotificationsChanged?.Invoke(on));

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

        rows.AddChild(MakeCheckRow("타건 수 집계", out _keystrokeCounting));
        _keystrokeCounting.Toggled += on => Relay(() => KeystrokeCountingChanged?.Invoke(on));

        rows.AddChild(MakeCheckRow("Windows 시작 시 자동 실행", out _autostart));
        _autostart.Toggled += on => Relay(() => AutostartChanged?.Invoke(on));

        rows.AddChild(new HSeparator());

        var closeBtn = new Button { Text = "닫기" };
        closeBtn.Pressed += Close;
        rows.AddChild(closeBtn);
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

    private static HBoxContainer MakeCheckRow(string label, out CheckBox box)
    {
        var row = new HBoxContainer();
        box = new CheckBox { Text = label };
        box.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(box);
        return row;
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
