using System.Collections.Generic;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 친구 칸 줄 (B10). 로비에 나 말고 누가 있으면 창을 늘려(<see cref="IShell.ExtendWindow"/>)
/// 친구마다 <see cref="RemotePlayerView"/> 를 하나씩 둔다. 로비가 비면 창을 원래대로 돌린다.
///
/// 칸 순서는 로비 입장 순서다 - 랭킹 순으로 두면 순위가 바뀔 때마다 친구 나무가 자리를
/// 바꿔서 누가 누군지 놓친다. 순위는 로비 창과 HUD 가 보여 준다.
///
/// 창을 어느 쪽으로 늘릴지는 셸이 정한다(아래에 자리가 없으면 위로, 창을 끌어 놓으면 다시).
/// 이 줄은 답을 받아 자기 자리만 옮긴다 - 아래면 원래 창 맨 아래 빈 띠에 겹쳐서
/// (y = 기본 높이 - <see cref="BottomSlack"/>), 위면 원래 창 바로 위(y = -칸 높이).
/// </summary>
public partial class FriendStrip : Node2D
{
    /// <summary>최대 4명 로비에서 나를 뺀 수 (§4-1).</summary>
    public const int MaxFriends = 3;

    /// <summary>
    /// 로비 타수는 이벤트 없이도 오른다(목) - 1초마다 이름·타수를 다시 맞춘다. 칸을
    /// 새로 만들지는 않으므로 가볍다.
    /// </summary>
    private const double RefreshSec = 1.0;

    /// <summary>
    /// 원래 창 맨 아래의 빈 띠(버튼 밑 y 455~560). 아래로 늘릴 때는 친구 칸을 이만큼
    /// 겹쳐 올리고 창은 그만큼 덜 늘린다 - 안 그러면 버튼과 친구 칸 사이가 100px 넘게 빈다.
    /// 위로 늘릴 때는 원래 창 맨 위에 HUD 가 있어서 겹칠 자리가 없다.
    /// </summary>
    private const int BottomSlack = 100;

    private readonly Dictionary<PeerId, RemotePlayerView> _views = new();
    private INetSession _net;
    private IShell _shell;
    private float _baseWidth;
    private float _baseHeight;
    private int _requestedHeight;
    private double _sinceRefresh;

    public void Bind(INetSession net, IShell shell)
    {
        _net = net;
        _shell = shell;
        _baseWidth = (int)ProjectSettings.GetSetting("display/window/size/viewport_width");
        _baseHeight = (int)ProjectSettings.GetSetting("display/window/size/viewport_height");

        _net.OnRoomChanged += Sync;
        _net.OnPeerState += OnPeerState;
        _shell.WindowExtensionChanged += Place;
        Sync();
    }

    public override void _ExitTree()
    {
        if (_net == null)
        {
            return;
        }

        _net.OnRoomChanged -= Sync;
        _net.OnPeerState -= OnPeerState;
        _shell.WindowExtensionChanged -= Place;
    }

    public override void _Process(double delta)
    {
        if (_views.Count == 0)
        {
            return;
        }

        _sinceRefresh += delta;
        if (_sinceRefresh >= RefreshSec)
        {
            _sinceRefresh = 0.0;
            Sync();
        }
    }

    private void OnPeerState(PeerId peer, PlayerState state)
    {
        if (_views.TryGetValue(peer, out RemotePlayerView view))
        {
            view.ApplyState(state);
        }
    }

    /// <summary>로비 멤버에 칸을 맞춘다. 들어온 사람은 칸을 만들고, 나간 사람은 지운다.</summary>
    private void Sync()
    {
        var friends = new List<RoomMember>(MaxFriends);
        foreach (RoomMember m in _net.Members)
        {
            if (!m.IsSelf && friends.Count < MaxFriends)
            {
                friends.Add(m);
            }
        }

        var keep = new HashSet<PeerId>();
        foreach (RoomMember m in friends)
        {
            keep.Add(m.Id);
        }

        foreach (PeerId gone in new List<PeerId>(_views.Keys))
        {
            if (!keep.Contains(gone))
            {
                _views[gone].QueueFree();
                _views.Remove(gone);
            }
        }

        // 줄을 가운데로 모은다 - 친구가 하나면 칸 하나가 한가운데.
        float left = (_baseWidth - friends.Count * RemotePlayerView.CellWidth) / 2f;
        for (int i = 0; i < friends.Count; i++)
        {
            RoomMember m = friends[i];
            if (!_views.TryGetValue(m.Id, out RemotePlayerView view))
            {
                view = new RemotePlayerView();
                view.Init(m.Id);
                AddChild(view);
                _views[m.Id] = view;
            }

            view.Position = new Vector2(left + i * RemotePlayerView.CellWidth, 0);
            view.SetMember(m.Name, m.RoomKeystrokes);
        }

        Resize(friends.Count > 0 ? (int)RemotePlayerView.CellHeight : 0);
        Visible = friends.Count > 0;
    }

    private void Resize(int height)
    {
        if (height == _requestedHeight)
        {
            return;
        }

        _requestedHeight = height;
        Place(_shell.ExtendWindow(height, BottomSlack));
    }

    /// <summary>
    /// 셸이 정한 방향에 맞춰 자리를 잡는다. 처음 붙일 때, 그리고 유저가 창을 끌어 놓아
    /// 방향이 바뀌었을 때(<see cref="IShell.WindowExtensionChanged"/>) 온다.
    /// </summary>
    private void Place(WindowExtension placement)
    {
        Position = placement == WindowExtension.Above
            ? new Vector2(0, -RemotePlayerView.CellHeight)
            : new Vector2(0, _baseHeight - BottomSlack);
    }
}
