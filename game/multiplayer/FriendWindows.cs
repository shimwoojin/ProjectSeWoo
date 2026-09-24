using System.Collections.Generic;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 친구 칸 창들 (B10). 로비에 나 말고 누가 있으면 친구마다 작은 창(<see cref="ISatelliteWindow"/>)
/// 을 하나씩 띄우고 그 안에 <see cref="RemotePlayerView"/> 를 둔다. 친구가 나가면 닫는다.
///
/// <b>친구 칸은 끌어서 바탕화면 아무 데나 둘 수 있다</b> (봉고캣처럼). 놓은 자리는 친구마다
/// 기억한다 - 세이브의 <c>multi.friendPositions</c>, 키는 친구의 스팀 계정 ID. 다음에 그
/// 친구와 같은 로비에 들어가면 그 자리에 뜬다. 처음 보는 친구는 내 원숭이 바로 아래에
/// 줄지어 뜨고(자리가 없으면 위), 순서는 로비 입장 순이다.
///
/// 창을 띄우고 끄는 OS 쪽 일(항상 위, 투명, 클릭 통과, 끌기, 옵션 배율·투명도·숨기기)은
/// 셸이 한다(<see cref="IShell.OpenSatellite"/>). 여기는 누구의 칸을 어디에 둘지만 정한다.
/// </summary>
public partial class FriendWindows : Node
{
    /// <summary>최대 4명 로비에서 나를 뺀 수 (§4-1).</summary>
    public const int MaxFriends = 3;

    /// <summary>
    /// 로비 타수는 이벤트 없이도 오른다(목) - 1초마다 이름·타수를 다시 맞춘다. 창을
    /// 새로 만들지는 않으므로 가볍다.
    /// </summary>
    private const double RefreshSec = 1.0;

    /// <summary>
    /// 처음 뜨는 자리 - 메인 창의 맨 아래 빈 띠(버튼 밑 y 460~560)에 걸치게 내 원숭이
    /// 바로 아래. 거기 자리가 없으면(화면 아래 끝) 메인 창 바로 위.
    /// </summary>
    private const float BelowY = 460f;

    private sealed record Friend(ISatelliteWindow Window, RemotePlayerView View);

    private readonly Dictionary<PeerId, Friend> _friends = new();
    private INetSession _net;
    private IShell _shell;
    private ISaveStore _store;
    private float _baseWidth;
    private double _sinceRefresh;

    /// <summary>창 노드 이름용 번호. 자리 번호로 이름을 지으면 나갔다 온 친구 창이 남의 이름과 겹친다.</summary>
    private int _opened;

    public void Bind(INetSession net, IShell shell, ISaveStore store)
    {
        _net = net;
        _shell = shell;
        _store = store;
        _baseWidth = (int)ProjectSettings.GetSetting("display/window/size/viewport_width");

        _net.OnRoomChanged += Sync;
        _net.OnPeerState += OnPeerState;
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
    }

    public override void _Process(double delta)
    {
        if (_friends.Count == 0)
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
        if (_friends.TryGetValue(peer, out Friend friend))
        {
            friend.View.ApplyState(state);
        }
    }

    /// <summary>로비 멤버에 창을 맞춘다. 들어온 친구는 창을 띄우고, 나간 친구는 닫는다.</summary>
    private void Sync()
    {
        var members = new List<RoomMember>(MaxFriends);
        foreach (RoomMember m in _net.Members)
        {
            if (!m.IsSelf && members.Count < MaxFriends)
            {
                members.Add(m);
            }
        }

        var keep = new HashSet<PeerId>();
        foreach (RoomMember m in members)
        {
            keep.Add(m.Id);
        }

        foreach (PeerId gone in new List<PeerId>(_friends.Keys))
        {
            if (!keep.Contains(gone))
            {
                _friends[gone].Window.Close();
                _friends.Remove(gone);
            }
        }

        for (int slot = 0; slot < members.Count; slot++)
        {
            RoomMember m = members[slot];
            if (!_friends.TryGetValue(m.Id, out Friend friend))
            {
                friend = Open(m.Id, slot);
                _friends[m.Id] = friend;
            }

            friend.View.SetMember(m.Name, m.RoomKeystrokes);
        }
    }

    private Friend Open(PeerId peer, int slot)
    {
        // 처음 보는 친구는 줄지어 - 3칸이 메인 창 폭(420)에 딱 맞는다.
        float x = (_baseWidth - MaxFriends * RemotePlayerView.CellWidth) / 2f + slot * RemotePlayerView.CellWidth;
        var size = new Vector2I((int)RemotePlayerView.CellWidth, (int)RemotePlayerView.CellHeight);

        ISatelliteWindow window = _shell.OpenSatellite(
            $"Friend{_opened++}",
            size,
            SavedPosition(peer),
            new Vector2(x, BelowY),
            new Vector2(x, -RemotePlayerView.CellHeight));

        var view = new RemotePlayerView();
        view.Init(peer);
        window.Content.AddChild(view);
        window.SetShape(view.GetShape());
        window.Moved += at => Remember(peer, at);

        return new Friend(window, view);
    }

    private Vector2I? SavedPosition(PeerId peer) =>
        _store.Data.Multi.FriendPositions.TryGetValue(Key(peer), out int[] xy) && xy is { Length: 2 }
            ? new Vector2I(xy[0], xy[1])
            : null;

    private void Remember(PeerId peer, Vector2I at)
    {
        _store.Data.Multi.FriendPositions[Key(peer)] = new[] { at.X, at.Y };
        _store.MarkDirty();
    }

    private static string Key(PeerId peer) => peer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
