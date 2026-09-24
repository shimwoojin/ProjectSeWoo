using System;
using System.Collections.Generic;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 멀티 룸 화면 (B12 룸 UI + B11 의 타수 랭킹, §4).
///
/// <see cref="ShopWindow"/> 와 같은 경계다 - <b>읽기는 <see cref="INetSession"/> 에서
/// 직접 하고, 바꾸는 것은 이벤트로 올려보낸다.</b> 생성·참가는 비동기에 실패 사유가
/// 여러 갈래라 <see cref="RoomController"/> 가 받아서 문구로 바꾼다.
///
/// 화면이 두 벌이다:
/// <list type="bullet">
///   <item><b>룸 밖</b> - 룸 만들기, 코드로 입장, 스팀 친구 목록(룸에 있는 친구는 [참가]).</item>
///   <item><b>룸 안</b> - 룸 코드와 복사, 경과 시간·인원, 룸 타수 랭킹, 친구 초대, 나가기.</item>
/// </list>
///
/// <b>랭킹 줄은 4개를 미리 만들어 두고 글자만 바꾼다.</b> 내 타수는 이벤트 없이
/// 오르므로(<see cref="INetSession.OnRoomChanged"/> 주석) 창이 열려 있는 동안 1초마다
/// 다시 그리는데, 그때마다 노드를 새로 만들 이유가 없다. 친구 목록은 길이가
/// 정해져 있지 않아서 다시 만든다 - 대신 5초에 한 번이다.
/// </summary>
public partial class RoomWindow : CanvasLayer
{
    /// <summary>상점(105) 위, 옵션 창(110) 아래.</summary>
    private const int LayerIndex = 106;

    /// <summary>룸 최대 인원 (§4-1). 랭킹 줄 개수이기도 하다.</summary>
    public const int MaxMembers = 4;

    private static readonly Color Online = new(0.55f, 0.75f, 0.95f);
    private static readonly Color Error = new(0.95f, 0.50f, 0.45f);

    /// <summary>닫기 버튼. 호출부가 클릭 통과를 되돌린다.</summary>
    public event Action Closed;

    public event Action CreateRequested;

    /// <summary>코드 입력칸의 글자 그대로거나 친구의 <see cref="FriendInfo.RoomUid"/>.</summary>
    public event Action<string> JoinRequested;

    public event Action<PeerId> InviteRequested;

    public event Action InviteOverlayRequested;

    public event Action LeaveRequested;

    private INetSession _net;
    private bool _busy;

    // 공통
    private Label _status;
    private Label _message;

    // 오류 팝업. 아래 한 줄 문구는 작아서 오류를 놓친다 - 오류만 창 가운데에 띄운다.
    private Control _popup;
    private Label _popupText;
    private Button _popupOk;

    // 룸 밖
    private Control _outside;
    private Label _unavailable;
    private Button _create;
    private LineEdit _codeInput;
    private Button _join;
    private Button _pasteJoin;
    private VBoxContainer _joinFriends;

    // 룸 안
    private Control _inside;
    private Label _code;
    private Label _info;
    private readonly RankRow[] _rankRows = new RankRow[MaxMembers];
    private VBoxContainer _inviteFriends;
    private Button _inviteOverlay;
    private Button _leave;

    /// <summary>이 룸에서 이미 초대를 보낸 친구. 룸이 바뀌면 비운다.</summary>
    private readonly HashSet<PeerId> _invited = new();
    private string _invitedRoom;

    private sealed record RankRow(Control Root, Label Rank, Label Name, Label Count);

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Layer = LayerIndex;
        Visible = false;
        BuildUi();
    }

    public void Bind(INetSession net)
    {
        _net = net;
        Refresh();
    }

    public void Open()
    {
        Refresh();
        Visible = true;
    }

    public void Close()
    {
        // 코드 입력칸이 포커스를 쥔 채 숨으면, 창이 닫혀 있는데도 키 입력을 먹는다.
        _codeInput.ReleaseFocus();
        ClosePopup();
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

    /// <summary>창 전체를 다시 그린다. 열 때, 룸이 바뀔 때, 생성·참가가 끝났을 때.</summary>
    public void Refresh()
    {
        if (_net == null)
        {
            return;
        }

        RoomHandle? room = _net.Current;
        _outside.Visible = room == null;
        _inside.Visible = room != null;

        if (room is { } r && r.Uid != _invitedRoom)
        {
            _invited.Clear();
            _invitedRoom = r.Uid;
        }

        RefreshControls();
        RefreshRoom();
        RefreshFriends();
    }

    /// <summary>
    /// 룸 코드·경과 시간·랭킹만 다시 쓴다. 노드를 안 만들므로 1초마다 불러도 된다.
    /// </summary>
    public void RefreshRoom()
    {
        if (_net?.Current is not { } room)
        {
            return;
        }

        IReadOnlyList<RoomMember> members = _net.Members;
        List<RoomMember> ranked = RoomRanking.Sort(members);

        _code.Text = room.Uid;
        _info.Text = $"경과 {RoomRanking.FormatElapsed(room.CreatedUtc)}"
            + $"  ·  {members.Count}/{MaxMembers}명"
            + (room.IsHost ? "  ·  내가 방장" : string.Empty);

        for (int i = 0; i < MaxMembers; i++)
        {
            RankRow row = _rankRows[i];
            if (i >= ranked.Count)
            {
                row.Root.Visible = false;
                continue;
            }

            RoomMember m = ranked[i];
            row.Root.Visible = true;
            row.Rank.Text = $"{RoomRanking.RankOf(ranked, m)}위";
            row.Name.Text = m.Name + (m.IsSelf ? " (나)" : string.Empty) + (m.IsHost ? "  · 방장" : string.Empty);
            row.Count.Text = $"{m.RoomKeystrokes:N0}타";

            Color color = m.IsSelf ? ShopWindow.Accent : Colors.White;
            row.Name.AddThemeColorOverride("font_color", color);
            row.Count.AddThemeColorOverride("font_color", m.IsSelf ? ShopWindow.Accent : ShopWindow.Gold);
        }
    }

    /// <summary>친구 목록을 다시 만든다. 룸 밖이면 [참가], 룸 안이면 [초대] 목록이다.</summary>
    public void RefreshFriends()
    {
        if (_net == null)
        {
            return;
        }

        IReadOnlyList<FriendInfo> friends = _net.GetFriends();
        if (_net.Current is { } room)
        {
            FillInviteList(friends, room);
        }
        else
        {
            FillJoinList(friends);
        }
    }

    /// <summary>
    /// 문구를 띄운다. 안내는 창 아래 한 줄로, <b>오류는 창 가운데 팝업으로</b> 띄운다 -
    /// 아래 한 줄은 작아서 "왜 안 들어가지지?" 를 놓친다. 빈 문자열이면 아래 줄을 지운다.
    /// </summary>
    public void ShowMessage(string text, bool isError = false)
    {
        if (isError)
        {
            _message.Text = string.Empty;
            _popupText.Text = text ?? string.Empty;
            _popup.Visible = true;
            _popupOk.GrabFocus();
            return;
        }

        _message.Text = text ?? string.Empty;
    }

    public bool IsPopupOpen => _popup.Visible;

    public void ClosePopup() => _popup.Visible = false;

    /// <summary>Esc. 팝업이 떠 있으면 팝업만 닫고, 아니면 창을 닫는다.</summary>
    public void Back()
    {
        if (IsPopupOpen)
        {
            ClosePopup();
            return;
        }

        Close();
    }

    /// <summary>생성·참가 응답을 기다리는 동안 버튼을 잠근다 - 연타로 룸이 두 개 생기지 않게.</summary>
    public void SetBusy(bool busy)
    {
        _busy = busy;
        RefreshControls();
    }

    /// <summary>코드 입력칸을 비운다. 입장에 성공했을 때 부른다.</summary>
    public void ClearCodeInput() => _codeInput.Text = string.Empty;

    // ------------------------------------------------------------------ 갱신

    private void RefreshControls()
    {
        bool available = _net?.IsAvailable == true;
        bool enabled = available && !_busy;

        _unavailable.Visible = !available;
        _status.Text = _net?.Current == null ? string.Empty : "룸에 있음";

        _create.Disabled = !enabled;
        _join.Disabled = !enabled;
        _pasteJoin.Disabled = !enabled;
        _codeInput.Editable = enabled;
        _leave.Disabled = _busy;
        _inviteOverlay.Disabled = !enabled;
    }

    private void FillJoinList(IReadOnlyList<FriendInfo> friends)
    {
        ClearChildren(_joinFriends);

        if (friends.Count == 0)
        {
            _joinFriends.AddChild(MakeDimLabel(_net.IsAvailable ? "스팀 친구가 없어요" : "스팀에 연결되면 친구가 보여요"));
            return;
        }

        foreach (FriendInfo f in friends)
        {
            Button action = null;
            if (f.Status == FriendStatus.InRoom && f.RoomUid != null)
            {
                string uid = f.RoomUid;
                action = new Button { Text = "참가", Disabled = _busy || !_net.IsAvailable };
                action.Pressed += () => JoinRequested?.Invoke(uid);
            }

            _joinFriends.AddChild(MakeFriendRow(f, action));
        }
    }

    private void FillInviteList(IReadOnlyList<FriendInfo> friends, RoomHandle room)
    {
        ClearChildren(_inviteFriends);

        var here = new HashSet<PeerId>();
        foreach (RoomMember m in _net.Members)
        {
            here.Add(m.Id);
        }

        int shown = 0;
        foreach (FriendInfo f in friends)
        {
            // 이미 이 방에 있는 사람, 오프라인인 사람은 초대 대상이 아니다.
            if (here.Contains(f.Id) || f.Status == FriendStatus.Offline || f.RoomUid == room.Uid)
            {
                continue;
            }

            PeerId id = f.Id;
            bool sent = _invited.Contains(id);
            var action = new Button { Text = sent ? "보냄" : "초대", Disabled = sent };
            action.Pressed += () =>
            {
                _invited.Add(id);
                action.Text = "보냄";
                action.Disabled = true;
                InviteRequested?.Invoke(id);
            };

            _inviteFriends.AddChild(MakeFriendRow(f, action));
            shown++;
        }

        if (shown == 0)
        {
            _inviteFriends.AddChild(MakeDimLabel("초대할 수 있는 친구가 없어요. 로비 코드를 복사해 보내 주세요"));
        }
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
        panel.AddThemeStyleboxOverride("panel", ShopWindow.MakeBackground());
        margin.AddChild(panel);

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        panel.AddChild(rows);

        rows.AddChild(MakeHeader());
        rows.AddChild(new HSeparator());

        _outside = BuildOutside();
        rows.AddChild(_outside);

        _inside = BuildInside();
        rows.AddChild(_inside);

        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _message.AddThemeFontSizeOverride("font_size", 12);
        _message.AddThemeColorOverride("font_color", ShopWindow.Dim);
        rows.AddChild(_message);

        // 팝업은 맨 마지막 자식이어야 창 위에 그려진다.
        _popup = BuildPopup();
        AddChild(_popup);
    }

    /// <summary>
    /// 오류 팝업. 창 전체를 어둡게 덮어 뒤의 버튼을 못 누르게 하고, 가운데에 문구와
    /// [확인]을 둔다. [확인]·Esc·Enter 로 닫힌다.
    /// </summary>
    private Control BuildPopup()
    {
        var dim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.AddChild(center);

        var box = ShopWindow.MakeBackground();
        box.BorderColor = Error;
        box.SetBorderWidthAll(2);
        box.SetContentMarginAll(18);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
        panel.AddThemeStyleboxOverride("panel", box);
        center.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);

        _popupText = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(244, 0),
        };
        _popupText.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_popupText);

        _popupOk = new Button
        {
            Text = "확인",
            CustomMinimumSize = new Vector2(96, 30),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        _popupOk.Pressed += ClosePopup;
        column.AddChild(_popupOk);

        return dim;
    }

    private Control MakeHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);

        var title = new Label { Text = "멀티 룸" };
        title.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(title);

        _status = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _status.AddThemeColorOverride("font_color", ShopWindow.Accent);
        _status.AddThemeFontSizeOverride("font_size", 11);
        header.AddChild(_status);

        var close = new Button { Text = "닫기" };
        close.Pressed += Close;
        header.AddChild(close);

        return header;
    }

    private Control BuildOutside()
    {
        var box = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);

        _unavailable = new Label
        {
            Text = "스팀이 켜져 있어야 멀티를 쓸 수 있어요",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _unavailable.AddThemeColorOverride("font_color", Error);
        box.AddChild(_unavailable);

        _create = new Button { Text = "룸 만들기", CustomMinimumSize = new Vector2(0, 32) };
        _create.Pressed += () => CreateRequested?.Invoke();
        box.AddChild(_create);

        var codeRow = new HBoxContainer();
        codeRow.AddThemeConstantOverride("separation", 6);
        _codeInput = new LineEdit
        {
            PlaceholderText = "로비 코드 (예: K7Q-2XMD)",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = 16,
        };
        _codeInput.TextSubmitted += text => JoinRequested?.Invoke(text);
        codeRow.AddChild(_codeInput);

        _join = new Button { Text = "입장", CustomMinimumSize = new Vector2(56, 0) };
        _join.Pressed += () => JoinRequested?.Invoke(_codeInput.Text);
        codeRow.AddChild(_join);
        box.AddChild(codeRow);

        // 입력칸에 포커스가 안 가는 환경(오버레이 창)을 위한 길이기도 하다 -
        // 친구가 보낸 코드를 복사해 두고 이것만 누르면 된다.
        _pasteJoin = new Button { Text = "복사한 로비 코드로 입장" };
        _pasteJoin.Pressed += () =>
        {
            string pasted = DisplayServer.ClipboardGet().Trim();
            _codeInput.Text = pasted;
            JoinRequested?.Invoke(pasted);
        };
        box.AddChild(_pasteJoin);

        box.AddChild(new HSeparator());
        box.AddChild(MakeSectionLabel("스팀 친구"));
        box.AddChild(MakeScroll(out _joinFriends));

        return box;
    }

    private Control BuildInside()
    {
        var box = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);

        var codeRow = new HBoxContainer();
        codeRow.AddThemeConstantOverride("separation", 8);

        var codeTitle = new Label { Text = "로비 코드" };
        codeTitle.AddThemeColorOverride("font_color", ShopWindow.Dim);
        codeRow.AddChild(codeTitle);

        _code = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _code.AddThemeFontSizeOverride("font_size", 20);
        _code.AddThemeColorOverride("font_color", ShopWindow.Gold);
        codeRow.AddChild(_code);

        var copy = new Button { Text = "복사" };
        copy.Pressed += CopyCode;
        codeRow.AddChild(copy);
        box.AddChild(codeRow);

        _info = new Label();
        _info.AddThemeFontSizeOverride("font_size", 12);
        _info.AddThemeColorOverride("font_color", ShopWindow.Dim);
        box.AddChild(_info);

        box.AddChild(new HSeparator());
        box.AddChild(MakeSectionLabel("룸 타수 랭킹 (들어온 뒤로 친 타수)"));

        for (int i = 0; i < MaxMembers; i++)
        {
            _rankRows[i] = MakeRankRow();
            box.AddChild(_rankRows[i].Root);
        }

        box.AddChild(new HSeparator());

        var inviteHead = new HBoxContainer();
        var inviteTitle = MakeSectionLabel("친구 초대");
        inviteTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        inviteHead.AddChild(inviteTitle);
        _inviteOverlay = new Button { Text = "스팀으로 초대" };
        _inviteOverlay.Pressed += () => InviteOverlayRequested?.Invoke();
        inviteHead.AddChild(_inviteOverlay);
        box.AddChild(inviteHead);

        box.AddChild(MakeScroll(out _inviteFriends));

        _leave = new Button { Text = "룸 나가기" };
        _leave.Pressed += () => LeaveRequested?.Invoke();
        box.AddChild(_leave);

        return box;
    }

    private void CopyCode()
    {
        if (_net?.Current is not { } room)
        {
            return;
        }

        DisplayServer.ClipboardSet(room.Uid);
        ShowMessage($"로비 코드 {room.Uid} 를 복사했어요. 친구에게 보내 주세요");
    }

    private static RankRow MakeRankRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var rank = new Label { CustomMinimumSize = new Vector2(34, 0) };
        rank.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(rank);

        var name = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        name.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(name);

        var count = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        count.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(count);

        return new RankRow(row, rank, name, count);
    }

    private static Control MakeFriendRow(FriendInfo f, Button action)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        Color color = f.Status switch
        {
            FriendStatus.InRoom => ShopWindow.Accent,
            FriendStatus.InGame => ShopWindow.Gold,
            FriendStatus.Online => Online,
            _ => ShopWindow.Dim,
        };

        var dot = new Label { Text = "●" };
        dot.AddThemeColorOverride("font_color", color);
        dot.AddThemeFontSizeOverride("font_size", 10);
        row.AddChild(dot);

        var name = new Label
        {
            Text = f.Name,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        name.AddThemeFontSizeOverride("font_size", 13);
        if (f.Status == FriendStatus.Offline)
        {
            name.AddThemeColorOverride("font_color", ShopWindow.Dim);
        }

        row.AddChild(name);

        var state = new Label
        {
            Text = f.Status switch
            {
                FriendStatus.InRoom => "룸에 있음",
                FriendStatus.InGame => "게임 중",
                FriendStatus.Online => "온라인",
                _ => "오프라인",
            },
        };
        state.AddThemeFontSizeOverride("font_size", 11);
        state.AddThemeColorOverride("font_color", color);
        row.AddChild(state);

        if (action != null)
        {
            action.CustomMinimumSize = new Vector2(52, 0);
            row.AddChild(action);
        }

        return row;
    }

    private static ScrollContainer MakeScroll(out VBoxContainer list)
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };

        list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        return scroll;
    }

    private static Label MakeSectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", ShopWindow.Dim);
        return label;
    }

    private static Label MakeDimLabel(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", ShopWindow.Dim);
        return label;
    }

    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }
}
