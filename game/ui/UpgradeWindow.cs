using System;
using System.Collections.Generic;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 강화 화면 (B13). 메뉴(<see cref="MenuHub"/>)의 "강화" 탭이다.
///
/// <b>2026-09-27 전에는 상점 창의 탭 하나였다.</b> 상점은 커서 장식을 사는 곳이고 강화는 나무를
/// 키우는 것이라 성격이 다른데, 탭 다섯 개(원숭이·바나나·장식·강화·도감) 사이에 끼어 있어서 잘
/// 안 보였다. 메뉴로 모으면서 따로 뺐다 - 내용은 그대로다.
///
/// <see cref="ShopWindow"/> 와 같은 경계다: 세이브도 가격 규칙도 모르고, 읽기는 <see cref="Inventory"/>
/// 로, 바꾸기는 <see cref="UpgradeRequested"/> 이벤트로 올려보내 <see cref="GameRoot"/> 가 한다.
/// </summary>
public partial class UpgradeWindow : CanvasLayer
{
    /// <summary>상점(105)과 같은 층. 둘은 메뉴가 한 번에 하나만 연다.</summary>
    private const int LayerIndex = 105;

    public event Action Closed;

    /// <summary>창이 열렸다. 게임 레이어가 서버 연결을 한 번 더 확인하는 계기로 쓴다 (상점과 같다).</summary>
    public event Action Opened;

    public event Action<UpgradeAxis> UpgradeRequested;

    private Inventory _inventory;
    private Label _bananas;
    private Label _notice;
    private string _message;

    /// <summary>한 줄: 이름+단계 / 지금 → 다음 효과 / 가격 / 버튼.</summary>
    private sealed record UpgradeRow(Label Title, Label Effect, Label Price, Button Action);

    private readonly Dictionary<UpgradeAxis, UpgradeRow> _rows = new();

    /// <summary>놓는 순서. 싼 것부터 - 첫 강화는 "빨리 익기".</summary>
    private static readonly UpgradeAxis[] Order = { UpgradeAxis.Cycle, UpgradeAxis.Golden, UpgradeAxis.Slots };

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
        Refresh();
        Visible = true;
        Opened?.Invoke();
    }

    public void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }

    /// <summary>강화 실패 사유를 안내 줄에 띄운다. 다음에 창을 열 때 지워진다.</summary>
    public void ShowMessage(string message)
    {
        _message = message;
        Refresh();
    }

    /// <summary>잔액·단계를 다시 반영한다. 강화 직후와 창을 열 때 부른다 - 매 프레임 부르지 않는다 (§7-3).</summary>
    public void Refresh()
    {
        if (_inventory == null)
        {
            return;
        }

        _bananas.Text = $"바나나 {_inventory.Bananas:N0}";

        bool online = _inventory.Online;
        _notice.Text = !online ? "인터넷 연결이 필요하다 - 강화는 온라인에서만 된다" : _message ?? string.Empty;
        _notice.Visible = _notice.Text.Length > 0;

        foreach ((UpgradeAxis axis, UpgradeRow row) in _rows)
        {
            int level = _inventory.UpgradeLevel(axis);
            int max = UpgradeTable.MaxLevel(axis);
            long? price = UpgradeTable.NextPrice(axis, level);

            row.Title.Text = $"{AxisName(axis)}  {Math.Min(level, max)}/{max}단계";

            if (price == null)
            {
                row.Effect.Text = $"{Effect(axis, level)} (최대)";
                row.Price.Text = string.Empty;
                row.Action.Text = "최대";
                row.Action.Disabled = true;
                continue;
            }

            bool affordable = _inventory.Bananas >= price.Value;
            row.Effect.Text = $"{Effect(axis, level)} → {Effect(axis, level + 1)}";
            row.Price.Text = $"{price.Value:N0}";
            row.Price.AddThemeColorOverride("font_color", affordable ? ShopWindow.Gold : ShopWindow.Dim);
            row.Action.Text = "강화";
            row.Action.Disabled = !affordable || !online;
        }
    }

    private static string AxisName(UpgradeAxis axis) => axis switch
    {
        UpgradeAxis.Slots => "가지 늘리기",
        UpgradeAxis.Cycle => "빨리 익기",
        UpgradeAxis.Golden => "황금 바나나",
        _ => axis.ToString(),
    };

    /// <summary>단계 <paramref name="level"/> 의 효과를 한 마디로.</summary>
    private static string Effect(UpgradeAxis axis, int level) => axis switch
    {
        UpgradeAxis.Slots => $"송이 {UpgradeTable.SlotsAt(level)}개",
        UpgradeAxis.Cycle => $"{UpgradeTable.GrowthMsAt(level) / 60_000}분마다 익음",
        UpgradeAxis.Golden => $"황금 확률 {UpgradeTable.GoldenChanceAt(level)}%",
        _ => string.Empty,
    };

    // ------------------------------------------------------------------ UI 구성

    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", MenuHub.ContentTop);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", ShopWindow.MakeBackground());
        margin.AddChild(panel);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        panel.AddChild(rows);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        var title = new Label { Text = "강화" };
        title.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(title);
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
            Text = $"나무를 키운다. 장식과 같은 바나나를 쓴다. 황금 바나나는 따면 {UpgradeTable.GoldenMultiplier}개.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", ShopWindow.Dim);
        rows.AddChild(hint);

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 12);
        rows.AddChild(list);

        foreach (UpgradeAxis axis in Order)
        {
            list.AddChild(MakeRow(axis));
        }
    }

    private Control MakeRow(UpgradeAxis axis)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);

        var title = new Label();
        title.AddThemeFontSizeOverride("font_size", 13);
        text.AddChild(title);

        var effect = new Label();
        effect.AddThemeFontSizeOverride("font_size", 11);
        effect.AddThemeColorOverride("font_color", ShopWindow.Accent);
        text.AddChild(effect);

        row.AddChild(text);

        var price = new Label { VerticalAlignment = VerticalAlignment.Center };
        price.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(price);

        var action = new Button { CustomMinimumSize = new Vector2(56, 0) };
        action.Pressed += () => UpgradeRequested?.Invoke(axis);
        row.AddChild(action);

        _rows[axis] = new UpgradeRow(title, effect, price, action);
        return row;
    }
}
