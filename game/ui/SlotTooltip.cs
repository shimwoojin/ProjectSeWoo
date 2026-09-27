using System;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 송이에 마우스를 올리면 뜨는 작은 말풍선 - "언제 열리나" (2026-09-27).
///
/// 성장은 서버 시계라(<see cref="IEconomyService.Slots"/>) 화면의 색·크기만으로는 얼마나 남았는지 알 수
/// 없었다. 수확이 타이핑에 묶여 있어서 "지금 쳐야 하나, 좀 있다 와도 되나" 가 궁금해지는데, 그 답을 늘
/// 띄워 두면 바탕화면이 시끄러워진다 - 그래서 올렸을 때만 보인다.
///
/// <b>황금 여부는 익기 전에 알려 주지 않는다.</b> 나무 그림도 다 익은 순간에야 금색으로 바뀐다
/// (<see cref="TreeSlot.SetProgress"/>) - 열어 보는 재미를 여기서 깨지 않는다.
///
/// 클릭을 받지 않는다(<see cref="Control.MouseFilterEnum.Ignore"/>) - 말풍선이 송이를 덮어도 그 아래
/// 마우스 판정이 흔들리지 않는다.
/// </summary>
public partial class SlotTooltip : PanelContainer
{
    private Label _title;
    private Label _body;

    public SlotTooltip()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        ZIndex = 50;

        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.07f, 0.10f, 0.90f),
            BorderColor = new Color(0.30f, 0.55f, 0.75f, 0.85f),
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(4);
        box.ContentMarginLeft = 8;
        box.ContentMarginRight = 8;
        box.ContentMarginTop = 3;
        box.ContentMarginBottom = 4;
        AddThemeStyleboxOverride("panel", box);

        var rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 0);
        AddChild(rows);

        _title = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _title.AddThemeFontSizeOverride("font_size", 12);
        rows.AddChild(_title);

        _body = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _body.AddThemeFontSizeOverride("font_size", 11);
        _body.AddThemeColorOverride("font_color", ShopWindow.Dim);
        rows.AddChild(_body);
    }

    /// <summary>
    /// 슬롯 상태로 글자를 채운다. 같은 글자면 건드리지 않는다 - 초 단위 카운트다운이라 대부분의 호출은 그대로다.
    /// </summary>
    /// <param name="hits">익은 송이가 지금까지 맞은 횟수 (<see cref="Tree.HitsOf"/>).</param>
    public void ShowSlot(SlotState slot, int hits)
    {
        string title;
        string body;
        Color titleColor;

        if (slot.Ready)
        {
            title = slot.Golden ? "황금 바나나 · 다 익음" : "다 익음";
            titleColor = ShopWindow.Gold;
            body = $"{Tree.HitsToDrop - hits}번 더 치면 떨어짐";
        }
        else
        {
            title = "익는 중";
            titleColor = ShopWindow.Accent;
            body = $"{FormatRemaining(slot.GrowthMs - slot.ElapsedMs)} 뒤 열림";
        }

        if (_title.Text != title)
        {
            _title.Text = title;
            _title.AddThemeColorOverride("font_color", titleColor);
        }

        if (_body.Text != body)
        {
            _body.Text = body;
        }

        Visible = true;
    }

    /// <summary>남은 시간을 짧게. 초는 올림이다 - 0.4초 남았을 때 "0초" 라고 하면 이미 열린 것처럼 읽힌다.</summary>
    internal static string FormatRemaining(long ms)
    {
        long seconds = Math.Max(1, (ms + 999) / 1000);
        if (seconds >= 3600)
        {
            return $"{seconds / 3600}시간 {seconds % 3600 / 60}분";
        }

        if (seconds >= 60)
        {
            return $"{seconds / 60}분 {seconds % 60}초";
        }

        return $"{seconds}초";
    }
}
