using System;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform.Mocks;

/// <summary>
/// <see cref="INetSession"/> 목 구현. 혼자서 룸을 만들고, 가짜 친구를 붙일 수 있다.
///
/// 실물은 W3 의 A9~A11 이다. 멀티는 컷 라인 6번(=마지막)이므로 (§10) 을은
/// 이 목으로 룸 화면(B10~B12)을 끝까지 만들어 둘 수 있어야 한다.
/// </summary>
public sealed class MockNetSession : INetSession
{
    private RoomHandle _room;
    private bool _joined;

    public event Action<PeerId, PlayerState> OnPeerState;

    public event Action<PeerId> OnPeerJoin;

    public event Action<PeerId> OnPeerLeave;

    /// <summary>마지막으로 뿌린 상태. 게임 레이어 테스트에서 확인용.</summary>
    public PlayerState LastBroadcast { get; private set; }

    public Task<RoomHandle> CreateRoom()
    {
        _room = new RoomHandle("MOCK-0001", new PeerId(1), IsHost: true);
        _joined = true;
        return Task.FromResult(_room);
    }

    public Task<RoomHandle> JoinRoom(string uid)
    {
        _room = new RoomHandle(uid, new PeerId(1), IsHost: false);
        _joined = true;
        return Task.FromResult(_room);
    }

    public void Broadcast(PlayerState s)
    {
        if (!_joined)
        {
            GD.PushWarning("[mock-net] 룸에 안 들어간 상태로 Broadcast 했다");
            return;
        }

        LastBroadcast = s;
    }

    /// <summary>
    /// 가짜 친구를 입장시킨다. 룸 화면 레이아웃을 최대 인원(4명)까지 시험하려면 필요하다.
    /// </summary>
    public void SimulateJoin(PeerId peer) => OnPeerJoin?.Invoke(peer);

    public void SimulateLeave(PeerId peer) => OnPeerLeave?.Invoke(peer);

    /// <summary>가짜 친구의 상태를 밀어 넣는다. 타건 리듬·수확 토스트 연출 확인용.</summary>
    public void SimulateState(PeerId peer, PlayerState s) => OnPeerState?.Invoke(peer, s);
}
