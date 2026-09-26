using System;
using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 처음 켰을 때의 안내 (B15, 기획서 §9 "튜토리얼 / 최초 실행 온보딩 (3화면 이내)").
///
/// <b>첫 장에 개인정보 짧은 문구가 그대로 들어간다</b> - 기획서 §7-6 이 "스토어 페이지와 게임 내 최초
/// 실행에 같은 문구" 로 정했다. 문구의 원본은 docs/C2-PRIVACY.md §1 이고, 거기가 바뀌면 여기도 고친다.
///
/// 다 보면(<see cref="Finished"/>) 게임 레이어가 세이브에 <see cref="Version"/> 을 남기고, 다음부터는 안 뜬다.
/// 안내 내용을 크게 바꿔서 기존 유저에게도 다시 보여 줘야 하면(개인정보 문구가 바뀌었을 때 등) <see cref="Version"/>
/// 을 올린다. [?] 버튼으로 언제든 다시 연다.
///
/// 상점·로비 창과 같은 모양(<see cref="ShopWindow"/> 의 배경·색)이고, 창 전체를 덮는다.
/// </summary>
public partial class OnboardingWindow : CanvasLayer
{
    /// <summary>안내의 판(版). 세이브의 <c>onboardingSeen</c> 이 이보다 작으면 켤 때 연다.</summary>
    public const int Version = 1;

    /// <summary>상점(105)·로비(106) 위, 옵션(110) 아래 - 처음 켠 사람이 옵션부터 열어도 옵션이 보여야 한다.</summary>
    private const int LayerIndex = 107;

    /// <summary>마지막 장의 [시작하기] 또는 [건너뛰기]. 게임 레이어가 세이브에 본 것을 남긴다.</summary>
    public event Action Finished;

    /// <summary>한 장. <paramref name="Art"/> 는 그 장 맨 위의 게임 그림(원숭이 / 나무 / 커서 장식).</summary>
    private sealed record Page(string Title, string[] Lines, Func<Texture2D> Art, bool Privacy = false);

    private static readonly Page[] Pages =
    {
        new("PunchMonkey 에 오신 걸 환영합니다", new[]
        {
            "바탕화면에 켜 두는 게임입니다. 평소처럼 일하세요 — 키보드를 치거나 클릭할 때마다 원숭이가 나무를 칩니다.",
        }, PunchingMonkey, Privacy: true),
        new("바나나 따기", new[]
        {
            "바나나는 시간이 지나면 저절로 익습니다.",
            "익은 송이는 열 번 맞으면 떨어지고, 떨어진 만큼 바나나가 쌓입니다.",
            "자리를 비워도 나무는 계속 자랍니다. 돌아와서 치면 됩니다.",
            "많이 칠수록 레벨이 오릅니다 — 누적 타수가 곧 레벨입니다.",
        }, () => GD.Load<Texture2D>("res://assets/entities/tree_full.png")),
        new("꾸미고, 같이 치기", new[]
        {
            "[상점] 바나나로 커서 장식을 삽니다. 원숭이 · 바나나 · 장식 세 칸에 골라 끼웁니다. 나무를 키우는 강화도 여기 있습니다.",
            "[멀티] 스팀 친구와 로비에 모이면 친구의 원숭이와 나무가 바탕화면에 작은 창으로 뜹니다.",
            "[옵션] 크기 · 투명도 · 숨기기 · Windows 시작 시 실행. 트레이 아이콘에서도 열립니다.",
            "원숭이와 나무를 끌면 창이 옮겨집니다. 이 안내는 [?] 로 다시 볼 수 있습니다.",
        }, () => GD.Load<Texture2D>("res://assets/cursor/monkey/monkey_05/icon.png")),
    };

    /// <summary>docs/C2-PRIVACY.md §1 한국어 - 글자 하나 바꾸지 않는다.</summary>
    private const string PrivacyBold = "이 게임은 타이핑과 마우스 클릭의 횟수만 셉니다.";

    private const string PrivacyRest =
        " 어떤 키를 눌렀는지, 어떤 버튼을 눌렀는지, 마우스가 어디에 있는지 읽지 않고, 저장하지 않으며, 외부로 전송하지 않습니다.\n\n"
        + "바나나와 커서 장식을 지키기 위해 게임 서버에 스팀 계정 ID 와 게임 진행 상태(잔액·나무·강화)를 저장합니다."
        + " 자세한 내용은 개인정보 처리 안내를 참고하세요.";

    /// <summary>펀치 시트(8칸 x 298x324)의 팔을 뻗은 칸 - 캡슐 아트와 같은 프레임.</summary>
    private static Texture2D PunchingMonkey() => new AtlasTexture
    {
        Atlas = GD.Load<Texture2D>("res://assets/entities/monkey_punch.png"),
        Region = new Rect2(2 * 298, 0, 298, 324),
    };

    private const float ArtHeight = 120f;

    private int _page;
    private Label _title;
    private VBoxContainer _body;
    private Label _counter;
    private Button _back;
    private Button _next;
    private Button _skip;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Layer = LayerIndex;
        Visible = false;
        BuildUi();
    }

    public void Open()
    {
        _page = 0;
        ShowPage();
        Visible = true;
    }

    public void Close() => Visible = false;

    private void Finish()
    {
        Close();
        Finished?.Invoke();
    }

    private void Move(int step)
    {
        if (_page + step >= Pages.Length)
        {
            Finish();
            return;
        }

        _page = Math.Clamp(_page + step, 0, Pages.Length - 1);
        ShowPage();
    }

    private void ShowPage()
    {
        Page page = Pages[_page];
        _title.Text = page.Title;

        foreach (Node child in _body.GetChildren())
        {
            child.QueueFree();
        }

        _body.AddChild(new TextureRect
        {
            Texture = page.Art(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(0, ArtHeight),
        });

        foreach (string line in page.Lines)
        {
            _body.AddChild(MakeLine(line));
        }

        if (page.Privacy)
        {
            _body.AddChild(MakePrivacyBox());
        }

        bool last = _page == Pages.Length - 1;
        _counter.Text = $"{_page + 1} / {Pages.Length}";
        _back.Disabled = _page == 0;
        _next.Text = last ? "시작하기" : "다음";
        _skip.Visible = !last;
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
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        // 상점·로비와 같은 틀이지만 불투명하게 - 처음 켠 사람이 읽는 글이라 뒤의 HUD 가 비치면 안 된다.
        StyleBoxFlat background = ShopWindow.MakeBackground();
        background.BgColor = background.BgColor with { A = 1f };
        panel.AddThemeStyleboxOverride("panel", background);
        margin.AddChild(panel);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        panel.AddChild(rows);

        var header = new HBoxContainer();
        _title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _title.AddThemeFontSizeOverride("font_size", 16);
        _title.AddThemeColorOverride("font_color", ShopWindow.Gold);
        header.AddChild(_title);
        _counter = new Label();
        _counter.AddThemeColorOverride("font_color", ShopWindow.Dim);
        _counter.AddThemeFontSizeOverride("font_size", 11);
        header.AddChild(_counter);
        rows.AddChild(header);

        rows.AddChild(new HSeparator());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        rows.AddChild(scroll);

        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_body);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        _skip = new Button { Text = "건너뛰기", Flat = true };
        _skip.AddThemeColorOverride("font_color", ShopWindow.Dim);
        _skip.Pressed += Finish;
        buttons.AddChild(_skip);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _back = new Button { Text = "이전", CustomMinimumSize = new Vector2(64, 0) };
        _back.Pressed += () => Move(-1);
        buttons.AddChild(_back);
        _next = new Button { CustomMinimumSize = new Vector2(84, 0) };
        _next.Pressed += () => Move(1);
        buttons.AddChild(_next);
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

        // "[상점]" 처럼 버튼 이름으로 시작하는 줄은 그 버튼 이름을 강조색으로 - 화면 아래 버튼과 이어지게.
        if (text.StartsWith('['))
        {
            label.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.97f));
        }

        return label;
    }

    /// <summary>개인정보 짧은 문구 상자. 첫 문장만 강조색 - 스토어의 굵은 첫 문장과 같은 자리다.</summary>
    private static Control MakePrivacyBox()
    {
        var box = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.16f, 0.12f, 0.9f),
            BorderColor = new Color(ShopWindow.Accent, 0.6f),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(4);
        style.SetContentMarginAll(10);
        box.AddThemeStyleboxOverride("panel", style);

        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Text = $"[color=#{ShopWindow.Accent.ToHtml(false)}]{PrivacyBold}[/color]{PrivacyRest}",
        };
        text.AddThemeFontSizeOverride("normal_font_size", 12);
        box.AddChild(text);
        return box;
    }
}
