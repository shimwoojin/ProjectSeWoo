using System;
using System.Collections.Generic;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 상점 + 인벤토리 + 장착 화면 (§3-2, B6).
///
/// <c>CanvasLayer</c> 다 - <see cref="ProjectSeWoo.Platform.OptionsWindow"/> 와 같은
/// 이유로, 셸 배율(<c>SetScale</c>)이 바뀌어도 UI 자체는 안 흔들리고 항상 같은
/// 크기로 보여야 한다.
///
/// <b>이 클래스는 세이브도 구매 규칙도 모른다.</b> 읽기는 <see cref="Inventory"/>
/// 를 통해서 하고, 바꾸는 것은 이벤트로 올려보내 <see cref="GameRoot"/> 가 한다 -
/// 옵션 창이 세운 것과 같은 경계다. 그래서 여기에는 "얼마를 깎는다" 가 없다.
///
/// <b>상점과 장착이 한 화면이다.</b> 기획서는 둘을 나눠 적었지만(§3-2 "구매 즉시
/// 인벤토리에 들어가고, 장착 화면에서 슬롯에 끼운다") 창이 420x560 이라 화면을
/// 더 쪼개면 한 번에 보이는 상품이 서너 개로 줄어든다. **전 상품 상시 노출**이
/// §3-2 의 요구라 그쪽을 살렸다 - 대신 버튼 하나가 상태에 따라 구매/장착/해제로
/// 바뀐다.
/// </summary>
public partial class ShopWindow : CanvasLayer
{
    /// <summary>
    /// 옵션 창(110)보다 아래, DebugHud(100)보다 위. 옵션은 셸 것이라 언제든
    /// 게임 UI 를 덮을 수 있어야 한다.
    /// </summary>
    private const int LayerIndex = 105;

    private const int ThumbSize = 44;

    /// <summary>닫기 버튼. 호출부가 클릭 통과를 되돌린다.</summary>
    public event Action Closed;

    public event Action<ShopCatalog.Item> BuyRequested;

    /// <summary>id 가 null 이면 그 슬롯을 비워 달라는 뜻이다.</summary>
    public event Action<CursorSlot, string> EquipRequested;

    private Inventory _inventory;
    private Label _bananas;
    private Label _collection;

    /// <summary>상품 id → 그 줄의 컨트롤들. <see cref="Refresh"/> 가 여기만 훑는다.</summary>
    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);

    private sealed record Row(Label State, Button Action);

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
        Refresh();
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>
    /// 보유량·소유·장착 상태를 화면에 다시 반영한다. 구매/장착 직후와 창을 열 때
    /// 부른다 - <b>매 프레임 부르지 않는다.</b> 상주 앱이라 안 바뀐 라벨을 계속
    /// 다시 그릴 이유가 없다 (§7-3).
    /// </summary>
    public void Refresh()
    {
        if (_inventory == null)
        {
            return;
        }

        _bananas.Text = $"바나나 {_inventory.Bananas:N0}";
        _collection.Text = $"수집 {_inventory.OwnedCount}/{ShopCatalog.All.Length}"
            + $" ({_inventory.CollectionRate * 100f:0}%)";

        foreach (ShopCatalog.Item item in ShopCatalog.All)
        {
            if (!_rows.TryGetValue(item.Id, out Row row))
            {
                continue;
            }

            bool owned = _inventory.Owns(item.Id);
            bool equipped = _inventory.EquippedIn(item.Slot) == item.Id;

            if (equipped)
            {
                row.State.Text = "장착 중";
                row.State.AddThemeColorOverride("font_color", Accent);
                row.Action.Text = "해제";
                row.Action.Disabled = false;
            }
            else if (owned)
            {
                row.State.Text = "보유";
                row.State.AddThemeColorOverride("font_color", Dim);
                row.Action.Text = "장착";
                row.Action.Disabled = false;
            }
            else
            {
                bool affordable = _inventory.Bananas >= item.Price;
                row.State.Text = $"{item.Price:N0}";
                row.State.AddThemeColorOverride("font_color", affordable ? Gold : Dim);
                row.Action.Text = "구매";

                // **못 사는 것도 보여 준다.** 버튼만 잠근다 - §3-2 가 "유저가 다음
                // 목표를 눈으로 볼 수 있어야 커브가 작동한다" 고 한 부분이다.
                row.Action.Disabled = !affordable;
            }
        }
    }

    // ------------------------------------------------------------------ UI 구성

    private static readonly Color Accent = new(0.55f, 0.85f, 0.55f);
    private static readonly Color Gold = new(0.98f, 0.82f, 0.30f);
    private static readonly Color Dim = new(0.62f, 0.66f, 0.72f);

    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        AddChild(margin);

        // 앵커와 오프셋을 같이 세운다. SetAnchorsPreset 만 부르면 오프셋이 그대로
        // 남아, 창 크기가 바뀌었을 때 패널이 따라가지 않는다.
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var panel = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", MakeBackground());
        margin.AddChild(panel);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        panel.AddChild(rows);

        rows.AddChild(MakeHeader());
        rows.AddChild(new HSeparator());

        var tabs = new TabContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            TabsPosition = TabContainer.TabPosition.Top,
        };
        rows.AddChild(tabs);

        AddSlotTab(tabs, CursorSlot.Hang, "매달림");
        AddSlotTab(tabs, CursorSlot.Trail, "잔상");
        AddSlotTab(tabs, CursorSlot.Base, "바닥");
    }

    private Control MakeHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);

        var title = new Label { Text = "상점" };
        title.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(title);

        _bananas = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bananas.AddThemeColorOverride("font_color", Gold);
        header.AddChild(_bananas);

        // 도감 수집률 (§3-3). B7 이 잘리면 "인벤토리 화면에 수집률 숫자만 표시"
        // 가 컷 라인의 대체안이라(§10-3), 그 숫자를 여기 둔다.
        _collection = new Label();
        _collection.AddThemeColorOverride("font_color", Dim);
        _collection.AddThemeFontSizeOverride("font_size", 11);
        header.AddChild(_collection);

        var close = new Button { Text = "닫기" };
        close.Pressed += Close;
        header.AddChild(close);

        return header;
    }

    private void AddSlotTab(TabContainer tabs, CursorSlot slot, string title)
    {
        var scroll = new ScrollContainer
        {
            Name = title,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        tabs.AddChild(scroll);

        var list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);

        // 슬롯을 비우는 줄. 상품이 아니라 조작이라 표(ShopCatalog)에 넣지 않는다.
        var clear = new Button { Text = "이 슬롯 비우기" };
        clear.Pressed += () => EquipRequested?.Invoke(slot, null);
        list.AddChild(clear);

        foreach (ShopCatalog.Item item in ShopCatalog.ForSlot(slot))
        {
            list.AddChild(MakeItemRow(item));
        }
    }

    private Control MakeItemRow(ShopCatalog.Item item)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        row.AddChild(MakeThumb(item));

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 0);

        var name = new Label { Text = item.Name };
        name.AddThemeFontSizeOverride("font_size", 13);
        text.AddChild(name);

        var state = new Label();
        state.AddThemeFontSizeOverride("font_size", 11);
        text.AddChild(state);

        row.AddChild(text);

        var action = new Button { CustomMinimumSize = new Vector2(56, 0) };
        action.Pressed += () => OnRowPressed(item);
        row.AddChild(action);

        _rows[item.Id] = new Row(state, action);
        return row;
    }

    /// <summary>
    /// 버튼 하나가 상태에 따라 구매/장착/해제로 바뀐다. **무엇을 할지 여기서 정하고
    /// 실제로 하지는 않는다** - 판단 근거(소유·장착)는 화면이 이미 들고 있으므로
    /// 여기서 고르고, 세이브를 고치는 것은 호출부다.
    /// </summary>
    private void OnRowPressed(ShopCatalog.Item item)
    {
        if (_inventory == null)
        {
            return;
        }

        if (!_inventory.Owns(item.Id))
        {
            BuyRequested?.Invoke(item);
            return;
        }

        bool equipped = _inventory.EquippedIn(item.Slot) == item.Id;
        EquipRequested?.Invoke(item.Slot, equipped ? null : item.Id);
    }

    /// <summary>
    /// 상품 미리보기. **실물 에셋을 그대로 쓴다** - 커서에 붙을 그림과 상점에
    /// 보이는 그림이 다르면 산 뒤에 "이게 아닌데" 가 된다.
    ///
    /// 경로 규약은 A5 가 정한 것과 같다(platform/CursorLayer.cs). 파일이 없으면
    /// 빈 칸으로 두되 줄 높이는 유지한다 - 줄마다 높이가 들쭉날쭉하면 목록이
    /// 읽히지 않는다.
    /// </summary>
    private static Control MakeThumb(ShopCatalog.Item item)
    {
        var thumb = new TextureRect
        {
            CustomMinimumSize = new Vector2(ThumbSize, ThumbSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };

        string path = $"res://assets/cursor/{item.Slot.ToString().ToLowerInvariant()}/{item.Id}.png";
        if (ResourceLoader.Exists(path))
        {
            thumb.Texture = GD.Load<Texture2D>(path);
        }

        return thumb;
    }

    private static StyleBoxFlat MakeBackground()
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.07f, 0.10f, 0.94f),
            BorderColor = new Color(0.30f, 0.55f, 0.75f, 0.95f),
        };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(10);
        box.SetCornerRadiusAll(5);
        return box;
    }
}
