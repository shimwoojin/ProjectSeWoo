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

    /// <summary>창이 열렸다. 게임 레이어가 서버 연결을 한 번 더 확인하는 계기로 쓴다.</summary>
    public event Action Opened;

    /// <summary>id 가 null 이면 그 슬롯을 비워 달라는 뜻이다.</summary>
    public event Action<CursorSlot, string> EquipRequested;

    private Inventory _inventory;
    private Label _bananas;
    private Label _collection;

    /// <summary>
    /// 헤더 아래 안내 한 줄. 오프라인이면 그 사실을, 아니면 마지막 구매 실패 사유를
    /// 보여 주고, 둘 다 없으면 숨는다 (A14). 전에는 오프라인 구매가 아무 반응 없이
    /// 실패해서 버튼이 고장 난 것처럼 보였다.
    /// </summary>
    private Label _notice;
    private string _purchaseMessage;

    private const string OfflineNotice = "인터넷 연결이 필요하다 - 구매는 온라인에서만 된다";

    // 도감 탭 (B7)
    private Label _collectionTotal;
    private ProgressBar _collectionBar;
    private readonly Dictionary<CursorSlot, Label> _slotHeads = new();
    private readonly Dictionary<string, TextureRect> _collectionCells =
        new(StringComparer.Ordinal);

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
        _purchaseMessage = null;
        Refresh();
        Visible = true;
        Opened?.Invoke();
    }

    /// <summary>구매 실패 사유를 안내 줄에 띄운다. 다음에 창을 열 때 지워진다.</summary>
    public void ShowPurchaseMessage(string message)
    {
        _purchaseMessage = message;
        Refresh();
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

        bool online = _inventory.Online;
        _notice.Text = !online ? OfflineNotice : _purchaseMessage ?? string.Empty;
        _notice.Visible = _notice.Text.Length > 0;
        // 퍼센트는 **내림**이다. HUD(StatusHud.SetCollection)와 같은 식이어야
        // 한 화면에 15/16 이 93% 와 94% 로 동시에 보이는 일이 없다.
        _collection.Text = $"수집 {_inventory.OwnedCount}/{ShopCatalog.All.Length}"
            + $" ({_inventory.OwnedCount * 100 / ShopCatalog.All.Length}%)";

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
                // 오프라인이면 살 수 있는 값이어도 잠근다 - 이유는 안내 줄이 말한다.
                row.Action.Disabled = !affordable || !online;
            }
        }

        RefreshCollection();
    }

    // ------------------------------------------------------------------ UI 구성

    // 색과 배경은 룸 창(game/multiplayer/RoomWindow)도 쓴다 - 창 두 개가 같은
    // 게임의 것으로 보여야 한다.
    internal static readonly Color Accent = new(0.55f, 0.85f, 0.55f);
    internal static readonly Color Gold = new(0.98f, 0.82f, 0.30f);
    internal static readonly Color Dim = new(0.62f, 0.66f, 0.72f);
    private static readonly Color Warn = new(1.00f, 0.62f, 0.45f);

    /// <summary>아직 안 가진 도감 칸. 알파는 그대로 두고 색만 죽인다.</summary>
    private static readonly Color Silhouette = new(0.10f, 0.12f, 0.16f, 0.85f);

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

        _notice = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _notice.AddThemeColorOverride("font_color", Warn);
        _notice.AddThemeFontSizeOverride("font_size", 12);
        rows.AddChild(_notice);

        rows.AddChild(new HSeparator());

        var tabs = new TabContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            TabsPosition = TabContainer.TabPosition.Top,
        };
        rows.AddChild(tabs);

        AddSlotTab(tabs, CursorSlot.Hang);
        AddSlotTab(tabs, CursorSlot.Trail);
        AddSlotTab(tabs, CursorSlot.Base);
        AddCollectionTab(tabs);
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

    private void AddSlotTab(TabContainer tabs, CursorSlot slot)
    {
        var scroll = new ScrollContainer
        {
            Name = ShopCatalog.SlotName(slot),
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

    /// <summary>
    /// 도감 (§3-3, B7). <b>상점 목록과 일부러 다르게 그린다</b> - 목록은 "무엇을
    /// 살까" 를 위한 것이고, 도감은 "얼마나 모았나" 를 위한 것이다. 같은 정보를
    /// 같은 모양으로 두 번 보여 주면 탭을 하나 더 둘 이유가 없다.
    ///
    /// 안 가진 것은 <b>실루엣</b>으로 둔다. 무엇이 남았는지는 보이되 그림은 안
    /// 보여 주는 쪽이 모으고 싶게 만든다 - 기획서가 "유저가 다음 목표를 눈으로 볼
    /// 수 있어야" 라고 한 것과 같은 결이다.
    /// </summary>
    private void AddCollectionTab(TabContainer tabs)
    {
        var scroll = new ScrollContainer
        {
            Name = "도감",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        tabs.AddChild(scroll);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(column);

        _collectionTotal = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _collectionTotal.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(_collectionTotal);

        _collectionBar = new ProgressBar
        {
            MaxValue = ShopCatalog.All.Length,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 8),
        };
        column.AddChild(_collectionBar);
        column.AddChild(new HSeparator());

        foreach (CursorSlot slot in Enum.GetValues<CursorSlot>())
        {
            var head = new Label();
            head.AddThemeFontSizeOverride("font_size", 12);
            head.AddThemeColorOverride("font_color", Dim);
            column.AddChild(head);
            _slotHeads[slot] = head;

            var grid = new GridContainer { Columns = 6 };
            grid.AddThemeConstantOverride("h_separation", 4);
            grid.AddThemeConstantOverride("v_separation", 4);
            column.AddChild(grid);

            foreach (ShopCatalog.Item item in ShopCatalog.ForSlot(slot))
            {
                var cell = (TextureRect)MakeThumb(item, ThumbSize);
                cell.TooltipText = item.Name;
                grid.AddChild(cell);
                _collectionCells[item.Id] = cell;
            }
        }
    }

    /// <summary>도감 칸의 소유 여부를 다시 칠한다.</summary>
    private void RefreshCollection()
    {
        int owned = _inventory.OwnedCount;
        _collectionTotal.Text = $"{owned} / {ShopCatalog.All.Length}"
            + $"  ({owned * 100 / ShopCatalog.All.Length}%)";
        _collectionTotal.AddThemeColorOverride(
            "font_color", _inventory.IsComplete ? Accent : Colors.White);
        _collectionBar.Value = owned;

        foreach (CursorSlot slot in Enum.GetValues<CursorSlot>())
        {
            if (_slotHeads.TryGetValue(slot, out Label head))
            {
                head.Text = $"{ShopCatalog.SlotName(slot)}"
                    + $"  {_inventory.OwnedInSlot(slot)}/{ShopCatalog.CountInSlot(slot)}";
            }
        }

        foreach (ShopCatalog.Item item in ShopCatalog.All)
        {
            if (!_collectionCells.TryGetValue(item.Id, out TextureRect cell))
            {
                continue;
            }

            // 실루엣: 알파는 살리고 색만 죽인다. Modulate 를 곱하는 것이라
            // 투명한 배경은 그대로 투명하게 남는다.
            cell.Modulate = _inventory.Owns(item.Id) ? Colors.White : Silhouette;
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
    private static Control MakeThumb(ShopCatalog.Item item, int size = ThumbSize)
    {
        var thumb = new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size),
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

    internal static StyleBoxFlat MakeBackground()
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
