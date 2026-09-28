using System;
using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 처음 켰을 때의 안내 (B15, 기획서 §9 "튜토리얼 / 최초 실행 온보딩 (3화면 이내)").
///
/// <b>한 장, 게임이 무엇인지만 (2026-09-28).</b> 예전엔 세 장이었다 - 개인정보 짧은 문구, 바나나 따기, 메뉴 탭
/// 하나하나의 설명. 처음 켠 사람이 읽기엔 길었고, 메뉴 설명은 메뉴를 열면 보이는 것을 되풀이했다. 이제는 "시간이
/// 지나면 바나나가 열리고, 키보드를 쳐서 따고, 바나나로 마우스를 꾸민다" 만 말한다. 개인정보 문구는 스토어 페이지와
/// 개인정보 처리 안내(docs/C2-PRIVACY.md)에 남아 있다.
///
/// 다 보면(<see cref="Finished"/>) 게임 레이어가 세이브에 <see cref="Version"/> 을 남기고, 다음부터는 안 뜬다.
/// 안내 내용을 크게 바꿔서 기존 유저에게도 다시 보여 줘야 하면 <see cref="Version"/> 을 올린다. 메뉴의 [안내] 로
/// 언제든 다시 연다.
///
/// 상점·로비 창과 같은 모양(<see cref="ShopWindow"/> 의 배경·색)이고, 메뉴 칸(<see cref="MenuHub.AnchorToPanel"/>)에
/// 뜬다 - 읽는 동안 옆에 나무와 원숭이가 보인다.
/// </summary>
public partial class OnboardingWindow : CanvasLayer
{
    /// <summary>
    /// 안내의 판(版). 세이브의 <c>onboardingSeen</c> 이 이보다 작으면 켤 때 연다.
    /// 2 (2026-09-27): 화면 버튼 넷이 [메뉴] 하나가 되면서 셋째 장이 바뀌었다 - 이미 본 사람도 한 번 더 본다.
    /// 2026-09-28 한 장으로 줄였지만 판은 그대로다 - 이미 본 사람에게 새로 알려 줄 것이 없다.
    /// </summary>
    public const int Version = 2;

    /// <summary>상점(105)·로비(106) 위, 옵션(110) 아래 - 처음 켠 사람이 옵션부터 열어도 옵션이 보여야 한다.</summary>
    private const int LayerIndex = 107;

    /// <summary>[시작하기]. 게임 레이어가 세이브에 본 것을 남긴다.</summary>
    public event Action Finished;

    private const string Title = "PunchMonkey 에 오신 걸 환영합니다";

    private static readonly string[] Lines =
    {
        "바탕화면에 켜 두고 평소처럼 일하는 게임입니다.",
        "시간이 지나면 나무에 바나나가 열립니다.",
        "키보드를 치면 원숭이가 나무를 쳐서 익은 바나나를 떨어뜨립니다.",
        "모은 바나나로 마우스 커서를 꾸며 보세요.",
    };

    /// <summary>펀치 시트(8칸 x 298x324)의 팔을 뻗은 칸 - 캡슐 아트와 같은 프레임.</summary>
    private static Texture2D PunchingMonkey() => new AtlasTexture
    {
        Atlas = GD.Load<Texture2D>("res://assets/entities/monkey_punch.png"),
        Region = new Rect2(2 * 298, 0, 298, 324),
    };

    private const float ArtHeight = 140f;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Layer = LayerIndex;
        Visible = false;
        BuildUi();
    }

    public void Open() => Visible = true;

    public void Close() => Visible = false;

    private void Finish()
    {
        Close();
        Finished?.Invoke();
    }

    // ------------------------------------------------------------------ UI 구성

    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        AddChild(margin);
        MenuHub.AnchorToPanel(margin);

        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        // 상점·로비와 같은 틀이지만 불투명하게 - 처음 켠 사람이 읽는 글이라 뒤가 비치면 안 된다.
        StyleBoxFlat background = ShopWindow.MakeBackground();
        background.BgColor = background.BgColor with { A = 1f };
        panel.AddThemeStyleboxOverride("panel", background);
        margin.AddChild(panel);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        panel.AddChild(rows);

        var title = new Label { Text = Title };
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", ShopWindow.Gold);
        rows.AddChild(title);

        rows.AddChild(new HSeparator());

        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 10);
        rows.AddChild(body);

        body.AddChild(new TextureRect
        {
            Texture = PunchingMonkey(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(0, ArtHeight),
        });

        foreach (string line in Lines)
        {
            body.AddChild(MakeLine(line));
        }

        var buttons = new HBoxContainer();
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var start = new Button { Text = "시작하기", CustomMinimumSize = new Vector2(84, 0) };
        start.Pressed += Finish;
        buttons.AddChild(start);
        rows.AddChild(buttons);
    }

    private static Label MakeLine(string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        return label;
    }
}
