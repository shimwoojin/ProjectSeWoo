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
///
/// 금액 버튼은 바로 기부하지 않고 확인 칸을 먼저 띄운다 - 기부 전·후 바나나와 누적을 보여 주고 [기부] 를 한 번 더
/// 눌러야 넣는다. 넣은 바나나는 돌려받을 수 없어서, 궁금해서 눌러 본 것으로 잃지 않게 (2026-10-01).
/// </summary>
public partial class DonateWindow : CanvasLayer
{
    /// <summary>상점·강화와 같은 층. 메뉴가 한 번에 하나만 연다.</summary>
    private const int LayerIndex = 105;

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
    private HBoxContainer _amountRow;
    private VBoxContainer _confirm;
    private Label _confirmBananas;
    private Label _confirmTotal;
    private Label _confirmTitle;
    private Button _confirmYes;
    private string _message;

    /// <summary>확인 칸에 올려 둔 기부량. 0 이면 확인 칸이 닫혀 있다.</summary>
    private long _pending;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Layer = LayerIndex;
        Visible = false;
        BuildUi();
    }

    public void Bind(Inventory inventory)
    {
        _inventory = inventory;
        Refresh();
    }

    public void Open()
    {
        _message = null;
        _pending = 0;
        Refresh();
        Visible = true;
        Opened?.Invoke();
    }

    public void Close()
    {
        Visible = false;
        _pending = 0;
        Closed?.Invoke();
    }

    /// <summary>기부 결과(고마움·실패 사유)를 안내 줄에. 다음에 창을 열 때 지워진다.</summary>
    public void ShowMessage(string message)
    {
        _message = message;
        Refresh();
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
        _notice.Text = !online ? "인터넷 연결이 필요합니다 - 기부는 온라인에서만 할 수 있습니다" : _message ?? string.Empty;
        _notice.Visible = _notice.Text.Length > 0;

        _total.Text = $"누적 기부 {total:N0}";
        string title = DonationTable.TitleFor(total);
        _title.Text = title ?? "아직 칭호가 없습니다";
        _title.AddThemeColorOverride("font_color", title != null ? ShopWindow.Gold : ShopWindow.Dim);

        if (DonationTable.NextTitle(total) is (long threshold, string nextTitle))
        {
            (long from, _) = PreviousThreshold(total);
            _progress.MinValue = from;
            _progress.MaxValue = threshold;
            _progress.Value = total;
            _progress.Visible = true;
            _next.Text = $"다음 칭호 \"{nextTitle}\"까지 {threshold - total:N0}";
        }
        else
        {
            _progress.Visible = false;
            _next.Text = "최고 칭호입니다. 감사합니다!";
        }

        for (int i = 0; i < _amountButtons.Length; i++)
        {
            long amount = DonationTable.Amounts[i];
            _amountButtons[i].Disabled = !online || bananas < amount;
        }

        long all = Math.Min(bananas, DonationTable.MaxPerRequest);
        _all.Disabled = !online || all <= 0;

        // 확인 중에 잔액이 줄었거나(다른 곳에서 씀) 연결이 끊기면 그 확인은 더 맞지 않는다 - 닫는다.
        if (_pending > 0 && (!online || _pending > bananas))
        {
            _pending = 0;
        }

        _amountRow.Visible = _pending <= 0;
        _confirm.Visible = _pending > 0;
        if (_pending > 0)
        {
            long afterTotal = total + _pending;
            string titleNow = DonationTable.TitleFor(total);
            string titleAfter = DonationTable.TitleFor(afterTotal);
            _confirmBananas.Text = $"바나나 {bananas:N0} → {bananas - _pending:N0}";
            _confirmTotal.Text = $"누적 기부 {total:N0} → {afterTotal:N0}";
            _confirmTitle.Text = titleAfter != null && titleAfter != titleNow ? $"새 칭호 \"{titleAfter}\"" : string.Empty;
            _confirmTitle.Visible = _confirmTitle.Text.Length > 0;
            _confirmYes.Text = $"{_pending:N0} 기부";
        }
    }

    /// <summary>금액 버튼·[전부]: 바로 넣지 않고 확인 칸을 띄운다.</summary>
    private void AskConfirm(long amount)
    {
        if (amount <= 0 || amount > (_inventory?.Bananas ?? 0))
        {
            return;
        }

        _pending = amount;
        _message = null;
        Refresh();
    }

    private void OnConfirmPressed()
    {
        long amount = _pending;
        _pending = 0;
        Refresh();
        if (amount > 0)
        {
            DonateRequested?.Invoke(amount);
        }
    }

    private void OnCancelPressed()
    {
        _pending = 0;
        Refresh();
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
            Text = "정글 기부함에 바나나를 넣습니다. 누적 기부량은 칭호가 되어 이름 옆에 표시됩니다.",
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

        _amountRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _amountRow.AddThemeConstantOverride("separation", 6);
        rows.AddChild(_amountRow);

        for (int i = 0; i < DonationTable.Amounts.Length; i++)
        {
            long amount = DonationTable.Amounts[i];
            var button = new Button { Text = $"{amount:N0}", CustomMinimumSize = new Vector2(64, 30) };
            button.Pressed += () => AskConfirm(amount);
            _amountRow.AddChild(button);
            _amountButtons[i] = button;
        }

        _all = new Button { Text = "전부", CustomMinimumSize = new Vector2(96, 30) };
        _all.Pressed += () => AskConfirm(Math.Min(_inventory?.Bananas ?? 0, DonationTable.MaxPerRequest));
        _amountRow.AddChild(_all);

        // 확인 칸 - 금액 버튼 줄 자리에 대신 뜬다.
        _confirm = new VBoxContainer { Visible = false };
        _confirm.AddThemeConstantOverride("separation", 4);
        rows.AddChild(_confirm);

        var ask = new Label { Text = "정말 기부할까요? 돌려받을 수 없습니다", HorizontalAlignment = HorizontalAlignment.Center };
        ask.AddThemeFontSizeOverride("font_size", 12);
        ask.AddThemeColorOverride("font_color", ShopWindow.Warn);
        _confirm.AddChild(ask);

        _confirmBananas = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _confirmBananas.AddThemeFontSizeOverride("font_size", 13);
        _confirmBananas.AddThemeColorOverride("font_color", ShopWindow.Gold);
        _confirm.AddChild(_confirmBananas);

        _confirmTotal = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _confirmTotal.AddThemeFontSizeOverride("font_size", 12);
        _confirm.AddChild(_confirmTotal);

        _confirmTitle = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _confirmTitle.AddThemeFontSizeOverride("font_size", 12);
        _confirmTitle.AddThemeColorOverride("font_color", ShopWindow.Accent);
        _confirm.AddChild(_confirmTitle);

        var answers = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        answers.AddThemeConstantOverride("separation", 6);
        _confirm.AddChild(answers);

        _confirmYes = new Button { CustomMinimumSize = new Vector2(96, 30) };
        _confirmYes.Pressed += OnConfirmPressed;
        answers.AddChild(_confirmYes);

        var cancel = new Button { Text = "취소", CustomMinimumSize = new Vector2(64, 30) };
        cancel.Pressed += OnCancelPressed;
        answers.AddChild(cancel);

        rows.AddChild(new HSeparator());

        rows.AddChild(DimLabel("칭호"));

        // 글꼴이 고정폭이 아니라 공백으로는 줄이 안 맞는다 - 문턱(오른쪽 정렬)·이름(왼쪽 정렬) 두 칸 표로.
        var indent = new MarginContainer();
        indent.AddThemeConstantOverride("margin_left", 12);
        rows.AddChild(indent);

        var titles = new GridContainer { Columns = 2 };
        titles.AddThemeConstantOverride("h_separation", 12);
        titles.AddThemeConstantOverride("v_separation", 0);
        indent.AddChild(titles);
        foreach ((long threshold, string name) in DonationTable.Titles)
        {
            Label amount = DimLabel($"{threshold:N0}");
            amount.HorizontalAlignment = HorizontalAlignment.Right;
            titles.AddChild(amount);
            titles.AddChild(DimLabel(name));
        }
    }

    private static Label DimLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", ShopWindow.Dim);
        return label;
    }
}
