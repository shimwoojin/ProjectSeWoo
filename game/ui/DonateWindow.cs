using System;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 기부 화면 (B20, docs/B20-DONATION.md). 메뉴(<see cref="MenuHub"/>)의 "기부" 탭이다.
///
/// 바나나를 기부함에 넣으면 사라지고 누적 기부량만 남는다. 누적이 칭호(<see cref="DonationTable"/>)가 되어 HUD 와
/// 친구 칸에 보인다. 도감을 다 채운 뒤에도 바나나를 쓸 곳이다.
///
/// <see cref="UpgradeWindow"/> 와 같은 경계다: 읽기는 <see cref="Inventory"/>, 바꾸기는 <see cref="DonateRequested"/>
/// 로 올려보내 <see cref="GameRoot"/> 가 한다.
/// </summary>
public partial class DonateWindow : CanvasLayer
{
    /// <summary>상점·강화와 같은 층. 메뉴가 한 번에 하나만 연다.</summary>
    private const int LayerIndex = 105;

    /// <summary>[전부] 를 한 번 누르면 이만큼(초) 동안 "한 번 더 누르면 전부" 로 기다린다 - 실수로 전 재산을 넣지 않게.</summary>
    private const double ConfirmSeconds = 3.0;

    public event Action Closed;

    public event Action Opened;

    /// <summary>기부할 바나나 수. 전부면 그때의 잔액.</summary>
    public event Action<long> DonateRequested;

    private Inventory _inventory;
    private Label _bananas;
    private Label _notice;
    private Label _total;
    private Label _title;
    private Label _next;
    private ProgressBar _progress;
    private Button _all;
    private readonly Button[] _amountButtons = new Button[DonationTable.Amounts.Length];
    private string _message;
    private double _confirmLeft;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Layer = LayerIndex;
        Visible = false;
        BuildUi();
        SetProcess(false);
    }

    public void Bind(Inventory inventory)
    {
        _inventory = inventory;
        Refresh();
    }

    public void Open()
    {
        _message = null;
        _confirmLeft = 0;
        Refresh();
        Visible = true;
        Opened?.Invoke();
    }

    public void Close()
    {
        Visible = false;
        _confirmLeft = 0;
        SetProcess(false);
        Closed?.Invoke();
    }

    /// <summary>기부 결과(고마움·실패 사유)를 안내 줄에. 다음에 창을 열 때 지워진다.</summary>
    public void ShowMessage(string message)
    {
        _message = message;
        Refresh();
    }

    /// <summary>
    /// [전부] 확인 대기 시간만 센다. 대기 중일 때만 켜진다 - 상주 앱이라 쉬는 창이 매 프레임 돌면 안 된다 (§7-3).
    /// </summary>
    public override void _Process(double delta)
    {
        _confirmLeft -= delta;
        if (_confirmLeft <= 0)
        {
            _confirmLeft = 0;
            SetProcess(false);
            Refresh();
        }
    }

    /// <summary>잔액·누적을 다시 반영한다. 기부 직후와 창을 열 때 부른다 - 매 프레임 부르지 않는다.</summary>
    public void Refresh()
    {
        if (_inventory == null)
        {
            return;
        }

        long bananas = _inventory.Bananas;
        long total = _inventory.Donated;
        bool online = _inventory.Online;

        _bananas.Text = $"바나나 {bananas:N0}";
        _notice.Text = !online ? "인터넷 연결이 필요하다 - 기부는 온라인에서만 된다" : _message ?? string.Empty;
        _notice.Visible = _notice.Text.Length > 0;

        _total.Text = $"누적 기부 {total:N0}";
        string title = DonationTable.TitleFor(total);
        _title.Text = title ?? "아직 칭호가 없다";
        _title.AddThemeColorOverride("font_color", title != null ? ShopWindow.Gold : ShopWindow.Dim);

        if (DonationTable.NextTitle(total) is (long threshold, string nextTitle))
        {
            (long from, _) = PreviousThreshold(total);
            _progress.MinValue = from;
            _progress.MaxValue = threshold;
            _progress.Value = total;
            _progress.Visible = true;
            _next.Text = $"다음 칭호 \"{nextTitle}\" 까지 {threshold - total:N0}";
        }
        else
        {
            _progress.Visible = false;
            _next.Text = "최고 칭호다. 고마워!";
        }

        for (int i = 0; i < _amountButtons.Length; i++)
        {
            long amount = DonationTable.Amounts[i];
            _amountButtons[i].Disabled = !online || bananas < amount;
        }

        long all = Math.Min(bananas, DonationTable.MaxPerRequest);
        _all.Disabled = !online || all <= 0;
        _all.Text = _confirmLeft > 0 ? $"정말 {all:N0} 전부?" : "전부";
    }

    /// <summary>지금 칭호의 문턱(없으면 0) - 진행 막대의 시작점.</summary>
    private static (long Threshold, string Title) PreviousThreshold(long total)
    {
        (long, string) previous = (0, null);
        foreach ((long threshold, string name) in DonationTable.Titles)
        {
            if (total >= threshold)
            {
                previous = (threshold, name);
            }
        }

        return previous;
    }

    private void OnAllPressed()
    {
        long all = Math.Min(_inventory?.Bananas ?? 0, DonationTable.MaxPerRequest);
        if (all <= 0)
        {
            return;
        }

        if (_confirmLeft <= 0)
        {
            _confirmLeft = ConfirmSeconds;
            SetProcess(true);
            Refresh();
            return;
        }

        _confirmLeft = 0;
        SetProcess(false);
        DonateRequested?.Invoke(all);
    }

    // ------------------------------------------------------------------ UI 구성

    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", MenuHub.ContentTop);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        AddChild(margin);
        MenuHub.AnchorToPanel(margin);

        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", ShopWindow.MakeBackground());
        margin.AddChild(panel);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        panel.AddChild(rows);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        var heading = new Label { Text = "기부" };
        heading.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(heading);
        _bananas = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bananas.AddThemeColorOverride("font_color", ShopWindow.Gold);
        header.AddChild(_bananas);
        rows.AddChild(header);

        _notice = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _notice.AddThemeColorOverride("font_color", ShopWindow.Warn);
        _notice.AddThemeFontSizeOverride("font_size", 12);
        rows.AddChild(_notice);

        rows.AddChild(new HSeparator());

        var hint = new Label
        {
            Text = "정글 기부함에 바나나를 넣는다. 넣은 바나나는 돌려받을 수 없다. 누적 기부량은 칭호가 되어 "
                + "내 이름 옆과 멀티에서 친구 화면에 보인다.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", ShopWindow.Dim);
        rows.AddChild(hint);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 18);
        rows.AddChild(_title);

        _total = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _total.AddThemeFontSizeOverride("font_size", 13);
        rows.AddChild(_total);

        _progress = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
        rows.AddChild(_progress);

        _next = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _next.AddThemeFontSizeOverride("font_size", 11);
        _next.AddThemeColorOverride("font_color", ShopWindow.Accent);
        rows.AddChild(_next);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 6);
        rows.AddChild(buttons);

        for (int i = 0; i < DonationTable.Amounts.Length; i++)
        {
            long amount = DonationTable.Amounts[i];
            var button = new Button { Text = $"{amount:N0}", CustomMinimumSize = new Vector2(64, 30) };
            button.Pressed += () => DonateRequested?.Invoke(amount);
            buttons.AddChild(button);
            _amountButtons[i] = button;
        }

        _all = new Button { Text = "전부", CustomMinimumSize = new Vector2(96, 30) };
        _all.Pressed += OnAllPressed;
        buttons.AddChild(_all);

        rows.AddChild(new HSeparator());

        var titles = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        titles.AddThemeFontSizeOverride("font_size", 11);
        titles.AddThemeColorOverride("font_color", ShopWindow.Dim);
        var lines = new System.Text.StringBuilder("칭호\n");
        foreach ((long threshold, string name) in DonationTable.Titles)
        {
            lines.Append($"  {threshold,8:N0}  {name}\n");
        }

        titles.Text = lines.ToString().TrimEnd();
        rows.AddChild(titles);
    }
}
