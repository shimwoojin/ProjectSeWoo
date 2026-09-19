using Godot;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 측정값과 조작 키를 창 안에 띄우는 HUD.
///
/// 표시 문자열은 전부 ASCII다. Godot 기본 테마 폰트에는 한글 글리프가 없어서
/// 한글을 넣으면 두부(□)로 나온다. 게임 본편에서 한글 UI를 쓰려면 폰트를
/// 별도로 번들해야 한다는 뜻이고, 이것도 Week 0에 확인해 둘 항목이다.
/// </summary>
public partial class DebugHud : CanvasLayer
{
    private Label _stats;
    private Label _keys;
    private PanelContainer _panel;

    public override void _Ready()
    {
        Layer = 100;

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(column);

        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", MakeBackground());
        column.AddChild(_panel);

        var rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(rows);

        _stats = MakeLabel(12, new Color(0.88f, 0.92f, 0.97f));
        rows.AddChild(_stats);

        var rule = new HSeparator();
        rule.AddThemeConstantOverride("separation", 1);
        rows.AddChild(rule);

        _keys = MakeLabel(11, new Color(0.55f, 0.63f, 0.73f));
        _keys.Text = string.Join("\n", new[]
        {
            "drag body = move window",
            "F1 hud    F2 position lock  F3 update mode",
            "F4 on-top F5 fps cap       F6 low power",
            "F7 next screen             F8 hit outline",
            "F9 copy report  F10 reset  ESC quit/close options",
            "F11 cursor on/off  F12 cursor interval  1 cursor follow mode",
            "2/3/4 cycle equip hang/trail/base (demo)  O options window",
            "[ ] shell scale   - = shell opacity",
            "H hide shell for 3s (auto-returns)",
        });
        rows.AddChild(_keys);
    }

    public void SetStats(string text)
    {
        if (_stats != null)
        {
            _stats.Text = text;
        }
    }

    private static Label MakeLabel(int size, Color color)
    {
        var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static StyleBoxFlat MakeBackground()
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.07f, 0.10f, 0.88f),
            BorderColor = new Color(0.17f, 0.42f, 0.64f, 0.95f),
        };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(12);
        box.SetCornerRadiusAll(5);
        return box;
    }
}
