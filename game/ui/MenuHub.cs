using System;
using System.Collections.Generic;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 메뉴 - 상점 · 강화 · 멀티 · 설정을 한 곳에서 오가는 탭 줄 (2026-09-27).
///
/// <b>예전엔 나무 아래에 [상점] [멀티] [옵션] [?] 버튼 넷이 나란히 있었다.</b> 바탕화면에 늘 떠 있는
/// 창이라 버튼이 많을수록 거슬리고, 창마다 따로 열고 닫아야 해서 오가기도 불편했다. 이제 화면에는
/// [메뉴] 하나만 두고, 누르면 창 위쪽에 이 탭 줄이 뜬다. 탭을 누르면 그 창으로 바로 넘어간다.
///
/// <b>창을 새로 만들지 않고 있던 창을 탭 줄 아래에 연다.</b> 상점·강화·로비는 게임 창이고 설정은
/// 셸 창(<see cref="IShell.OpenOptions"/>)이라 한 컨테이너에 넣을 수 없다 - 대신 모두 위쪽을
/// <see cref="ContentTop"/> 만큼 비워 두고, 이 줄이 한 번에 하나만 열어 둔다.
///
/// 어떤 경로로 닫히든(탭 줄의 ×, Esc, 설정 창의 [닫기]) 지금 탭의 창이 닫히면 줄도 같이 걷는다
/// (<see cref="OnWindowClosed"/>).
/// </summary>
public partial class MenuHub : CanvasLayer
{
    public enum Tab
    {
        Shop,
        Upgrade,
        Room,
        Options,
    }

    /// <summary>모든 창(설정 110 포함)보다 위 - 어느 탭이 열려 있든 줄이 보여야 한다.</summary>
    private const int LayerIndex = 115;

    private const int BarMargin = 10;
    private const int BarHeight = 34;

    /// <summary>탭 줄 아래 창들이 시작하는 높이. 상점·강화·로비 창의 위쪽 여백이다.</summary>
    public const int ContentTop = BarMargin + BarHeight + 6;

    /// <summary>[안내] - 처음 안내는 세이브에 본 판을 남겨야 해서 게임 레이어가 연다.</summary>
    public event Action HelpRequested;

    private ShopWindow _shop;
    private UpgradeWindow _upgrade;
    private RoomWindow _room;
    private IShell _shell;

    private readonly Dictionary<Tab, Button> _buttons = new();
    private Tab? _current;

    /// <summary>탭을 바꾸거나 줄을 걷는 중. 그 사이 창이 쏘는 Closed 는 우리가 닫은 것이라 무시한다.</summary>
    private bool _switching;

    public bool IsOpen => _current != null;

    /// <summary>마지막으로 연 탭. [메뉴] 버튼이 여기로 다시 연다 - 상점을 보다 닫았으면 상점으로.</summary>
    public Tab LastTab { get; private set; } = Tab.Shop;

    public override void _Ready()
    {
        Layer = LayerIndex;
        Visible = false;
        BuildUi();
    }

    /// <summary>게임 창 셋. <see cref="GameRoot._Ready"/> 에서 부른다.</summary>
    public void Bind(ShopWindow shop, UpgradeWindow upgrade, RoomWindow room)
    {
        _shop = shop;
        _upgrade = upgrade;
        _room = room;

        _shop.Closed += () => OnWindowClosed(Tab.Shop);
        _upgrade.Closed += () => OnWindowClosed(Tab.Upgrade);
        _room.Closed += () => OnWindowClosed(Tab.Room);
    }

    /// <summary>설정 창 주인. 셸이 붙은 뒤(<see cref="GameRoot.AttachPlatform"/>)에야 온다 - 그 전엔 설정 탭이 안 열린다.</summary>
    public void BindShell(IShell shell)
    {
        _shell = shell;
        _shell.OptionsClosed += () => OnWindowClosed(Tab.Options);
    }

    public void Toggle(Tab tab)
    {
        if (_current == tab)
        {
            Close();
        }
        else
        {
            Open(tab);
        }
    }

    /// <summary>
    /// <paramref name="tab"/> 의 창을 열고, 열려 있던 창은 닫는다. <b>새 창을 먼저 연다</b> - 설정 창이 닫힐 때
    /// 셸이 클릭 통과를 다시 계산하는데(<c>OverlayShell.ApplyPassthrough</c>), 그 순간 메뉴가 열려 있어야
    /// 창 전체가 클릭을 받는 상태로 남는다.
    /// </summary>
    public void Open(Tab tab)
    {
        if (_current == tab)
        {
            return;
        }

        if (tab == Tab.Options && _shell == null)
        {
            return;
        }

        _switching = true;
        Tab? previous = _current;
        _current = tab;
        LastTab = tab;
        OpenWindow(tab);
        if (previous != null)
        {
            CloseWindow(previous.Value);
        }

        _switching = false;

        Visible = true;
        foreach ((Tab t, Button button) in _buttons)
        {
            button.SetPressedNoSignal(t == tab);
        }
    }

    /// <summary>줄을 걷고 열려 있던 창도 닫는다.</summary>
    public void Close()
    {
        if (_current == null)
        {
            return;
        }

        Tab previous = _current.Value;
        _current = null;
        Visible = false;

        _switching = true;
        CloseWindow(previous);
        _switching = false;
    }

    /// <summary>Esc. 로비는 팝업부터 닫는다(<see cref="RoomWindow.Back"/>) - 나머지는 메뉴째 닫는다.</summary>
    public void Back()
    {
        if (_current == Tab.Room && _room.IsPopupOpen)
        {
            _room.ClosePopup();
            return;
        }

        Close();
    }

    /// <summary>지금 탭의 창이 스스로 닫혔다(설정 창의 [닫기] 등) - 줄도 걷는다.</summary>
    private void OnWindowClosed(Tab which)
    {
        if (_switching || _current != which)
        {
            return;
        }

        _current = null;
        Visible = false;
    }

    private void OpenWindow(Tab tab)
    {
        switch (tab)
        {
            case Tab.Shop:
                _shop.Open();
                break;
            case Tab.Upgrade:
                _upgrade.Open();
                break;
            case Tab.Room:
                _room.Open();
                break;
            case Tab.Options:
                _shell.OpenOptions(ContentTop);
                break;
        }
    }

    private void CloseWindow(Tab tab)
    {
        switch (tab)
        {
            case Tab.Shop:
                _shop.Close();
                break;
            case Tab.Upgrade:
                _upgrade.Close();
                break;
            case Tab.Room:
                _room.Close();
                break;
            case Tab.Options:
                _shell?.CloseOptions();
                break;
        }
    }

    // ------------------------------------------------------------------ UI 구성

    private void BuildUi()
    {
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", BarMargin);
        margin.AddThemeConstantOverride("margin_right", BarMargin);
        margin.AddThemeConstantOverride("margin_top", BarMargin);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(0, BarHeight) };
        StyleBoxFlat background = ShopWindow.MakeBackground();
        background.SetContentMarginAll(4);
        panel.AddThemeStyleboxOverride("panel", background);
        margin.AddChild(panel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        panel.AddChild(row);

        AddTab(row, Tab.Shop, "상점");
        AddTab(row, Tab.Upgrade, "강화");
        AddTab(row, Tab.Room, "멀티");
        AddTab(row, Tab.Options, "설정");

        var help = new Button { Text = "안내", Flat = true, FocusMode = Control.FocusModeEnum.None };
        help.AddThemeFontSizeOverride("font_size", 12);
        help.AddThemeColorOverride("font_color", ShopWindow.Dim);
        help.Pressed += () =>
        {
            Close();
            HelpRequested?.Invoke();
        };
        row.AddChild(help);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var close = new Button
        {
            Text = "닫기",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "메뉴 닫기 (Esc)",
        };
        close.AddThemeFontSizeOverride("font_size", 12);
        close.Pressed += Close;
        row.AddChild(close);
    }

    private void AddTab(HBoxContainer row, Tab tab, string text)
    {
        // 누른 탭이 눌린 채로 보이게 토글 버튼을 쓴다. 눌림 상태는 Open 이 직접 맞춘다 - 같은 탭을 다시 눌러도
        // 풀리지 않게 신호가 아니라 Pressed 에 건다.
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(52, 0),
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_pressed_color", ShopWindow.Gold);
        button.Pressed += () =>
        {
            Open(tab);
            button.SetPressedNoSignal(true);
        };
        row.AddChild(button);
        _buttons[tab] = button;
    }
}
