using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Shared.Mocks;

/// <summary>
/// <see cref="INetSession"/> 목 구현. 혼자서 룸을 만들고, 가짜 친구를 붙일 수 있다.
///
/// 실물은 W3 의 A9~A11 이다. 멀티는 컷 라인 6번(=마지막)이므로 (§10) 을은
/// 이 목으로 룸 화면(B10~B12)을 끝까지 만들어 둘 수 있어야 한다.
///
/// <b>실물의 규칙을 흉내 낸다</b> - 목에서 되던 것이 실물에서 안 되면 목이 거짓말을 한 것이다.
/// <list type="bullet">
///   <item>룸은 마지막 사람이 나가면 사라진다. 다른 사람이 남아 있으면 남는다.</item>
///   <item>나갔다 다시 들어오면 룸 타수가 이어진다(실물: 방장이 로비 데이터에 보관).</item>
///   <item>방장이 나가면 남은 사람 중 먼저 온 사람이 방장이 된다(스팀의 소유권 이전).</item>
///   <item>4명이 차면 <see cref="RoomJoinError.Full"/>.</item>
/// </list>
///
/// 가짜 친구 5명은 상태가 하나씩 다르다. 그중 둘은 룸에 있는데, 하나는 들어갈 수
/// 있고 하나는 가득 차 있다 - 친구 목록의 [참가]와 "가득 참" 문구를 둘 다 볼 수 있게.
/// </summary>
public sealed class MockNetSession : INetSession
{
    private const int MaxMembers = 4;

    /// <summary>나. 실물의 스팀 ID 자리다.</summary>
    public static readonly PeerId SelfId = new(1);

    private sealed class Seat
    {
        public PeerId Id;
        public string Name;
        public long Keystrokes;
    }

    private sealed class Room
    {
        public uint Code;
        public DateTime CreatedUtc;
        public PeerId Host;

        /// <summary>입장 순서. 나도 들어 있을 수 있다.</summary>
        public readonly List<Seat> Seats = new();

        /// <summary>나간 사람의 룸 타수. 실물의 로비 데이터 <c>s:&lt;steamid&gt;</c> 자리다.</summary>
        public readonly Dictionary<PeerId, long> Saved = new();
    }

    /// <summary>살아 있는 룸 전부. 친구의 룸도 여기 있어서 들어갔다 나와도 남는다.</summary>
    private readonly Dictionary<uint, Room> _rooms = new();

    private readonly List<FriendInfo> _friends = new();
    private readonly Random _random = new();
    private Room _current;

    public MockNetSession()
    {
        SeedFriends();
    }

    public event Action OnRoomChanged;

    public event Action<PeerId, PlayerState> OnPeerState;

    public event Action<PeerId> OnPeerJoin;

    public event Action<PeerId> OnPeerLeave;

    /// <summary>시험용. false 로 두면 "스팀이 없을 때" 화면을 볼 수 있다.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>마지막으로 뿌린 상태. 게임 레이어 테스트에서 확인용.</summary>
    public PlayerState LastBroadcast { get; private set; }

    public RoomHandle? Current => _current == null ? null : HandleOf(_current);

    public IReadOnlyList<RoomMember> Members
    {
        get
        {
            if (_current == null)
            {
                return Array.Empty<RoomMember>();
            }

            var members = new List<RoomMember>(_current.Seats.Count);
            foreach (Seat seat in _current.Seats)
            {
                members.Add(new RoomMember(
                    seat.Id, seat.Name, seat.Keystrokes, seat.Id == _current.Host, seat.Id == SelfId));
            }

            return members;
        }
    }

    public Task<RoomHandle> CreateRoom()
    {
        RequireAvailable();
        LeaveRoom();

        uint code;
        do
        {
            code = (uint)_random.NextInt64(0, (long)uint.MaxValue + 1);
        }
        while (_rooms.ContainsKey(code));

        var room = new Room { Code = code, CreatedUtc = DateTime.UtcNow, Host = SelfId };
        _rooms[code] = room;
        Enter(room);

        GD.Print($"[mock-net] 룸 생성 {RoomCode.Format(code)}");
        return Task.FromResult(HandleOf(room));
    }

    public Task<RoomHandle> JoinRoom(string uid)
    {
        RequireAvailable();

        if (!RoomCode.TryParse(uid, out uint code))
        {
            throw new RoomJoinException(RoomJoinError.InvalidCode, $"'{uid}'");
        }

        if (_current?.Code == code)
        {
            return Task.FromResult(HandleOf(_current));
        }

        if (!_rooms.TryGetValue(code, out Room room))
        {
            throw new RoomJoinException(RoomJoinError.NotFound, RoomCode.Format(code));
        }

        if (room.Seats.Count >= MaxMembers)
        {
            throw new RoomJoinException(RoomJoinError.Full, RoomCode.Format(code));
        }

        LeaveRoom();
        Enter(room);

        GD.Print($"[mock-net] 룸 참가 {RoomCode.Format(code)} - 이어 세기 {SelfSeat().Keystrokes}타");
        return Task.FromResult(HandleOf(room));
    }

    public void LeaveRoom()
    {
        if (_current == null)
        {
            return;
        }

        Room room = _current;
        _current = null;
        RemoveSeat(room, SelfId);

        GD.Print($"[mock-net] 룸 나감 {RoomCode.Format(room.Code)}"
            + (_rooms.ContainsKey(room.Code) ? " (룸은 남음)" : " (마지막이라 룸이 사라짐)"));
        OnRoomChanged?.Invoke();
    }

    public void AddKeystrokes(int count)
    {
        if (_current == null || count <= 0)
        {
            return;
        }

        SelfSeat().Keystrokes += count;
    }

    public IReadOnlyList<FriendInfo> GetFriends()
    {
        if (!IsAvailable)
        {
            return Array.Empty<FriendInfo>();
        }

        // 룸에 있는 친구의 코드는 그 룸이 살아 있을 때만 보인다 - 실물도 친구가
        // 로비에 있을 때만 GetFriendGamePlayed 에 로비 ID 가 실린다.
        var list = new List<FriendInfo>(_friends.Count);
        foreach (FriendInfo f in _friends)
        {
            if (f.Status == FriendStatus.InRoom
                && (!RoomCode.TryParse(f.RoomUid, out uint code) || !_rooms.ContainsKey(code)))
            {
                list.Add(f with { Status = FriendStatus.InGame, RoomUid = null });
                continue;
            }

            list.Add(f);
        }

        list.Sort((a, b) => a.Status.CompareTo(b.Status));
        return list;
    }

    public void InviteFriend(PeerId friend)
    {
        if (_current == null)
        {
            return;
        }

        string name = _friends.Find(f => f.Id == friend).Name ?? friend.ToString();
        GD.Print($"[mock-net] {name} 초대 (목이라 실제로 오지 않는다 - Shift+M 으로 가짜 입장)");
    }

    public void OpenInviteOverlay()
    {
        if (_current != null)
        {
            GD.Print("[mock-net] 스팀 초대 창 (목이라 안 뜬다)");
        }
    }

    public void Broadcast(PlayerState s)
    {
        if (_current == null)
        {
            GD.PushWarning("[mock-net] 룸에 안 들어간 상태로 Broadcast 했다");
            return;
        }

        LastBroadcast = s;
    }

    // ------------------------------------------------------------------ 시험용

    /// <summary>
    /// 가짜 친구를 입장시킨다. 룸 화면 레이아웃을 최대 인원(4명)까지 시험하려면 필요하다.
    /// 전에 나갔던 사람이면 그때의 룸 타수에서 이어진다.
    /// </summary>
    public bool SimulateJoin(PeerId peer, string name = null)
    {
        if (_current == null || _current.Seats.Count >= MaxMembers || SeatOf(_current, peer) != null)
        {
            return false;
        }

        _current.Saved.Remove(peer, out long resumed);
        _current.Seats.Add(new Seat { Id = peer, Name = name ?? $"친구{peer}", Keystrokes = resumed });
        OnPeerJoin?.Invoke(peer);
        OnRoomChanged?.Invoke();
        return true;
    }

    public void SimulateLeave(PeerId peer)
    {
        if (_current == null || peer == SelfId || SeatOf(_current, peer) == null)
        {
            return;
        }

        RemoveSeat(_current, peer);
        OnPeerLeave?.Invoke(peer);
        OnRoomChanged?.Invoke();
    }

    /// <summary>가짜 친구가 타건했다. 랭킹이 뒤집히는 순간을 보려고 쓴다.</summary>
    public void SimulateKeystrokes(PeerId peer, long count)
    {
        Seat seat = _current == null ? null : SeatOf(_current, peer);
        if (seat == null || peer == SelfId)
        {
            return;
        }

        seat.Keystrokes += count;
        OnRoomChanged?.Invoke();
    }

    /// <summary>가짜 친구의 상태를 밀어 넣는다. 타건 리듬·수확 토스트 연출 확인용.</summary>
    public void SimulateState(PeerId peer, PlayerState s) => OnPeerState?.Invoke(peer, s);

    // ------------------------------------------------------------------ 내부

    private void SeedFriends()
    {
        // 들어갈 수 있는 친구 룸 (2/4)
        Room open = SeedRoom(0x0B7A2F31u, TimeSpan.FromMinutes(42),
            (new PeerId(101), "바나나킹", 3_120), (new PeerId(102), "고릴라", 1_870));

        // 가득 찬 친구 룸 (4/4)
        Room full = SeedRoom(0x19C4E07Du, TimeSpan.FromHours(3),
            (new PeerId(103), "망고", 9_410), (new PeerId(201), "망고친구1", 7_002),
            (new PeerId(202), "망고친구2", 6_540), (new PeerId(203), "망고친구3", 880));

        _friends.Add(new FriendInfo(new PeerId(101), "바나나킹", FriendStatus.InRoom, RoomCode.Format(open.Code)));
        _friends.Add(new FriendInfo(new PeerId(103), "망고", FriendStatus.InRoom, RoomCode.Format(full.Code)));
        _friends.Add(new FriendInfo(new PeerId(104), "키보드장인", FriendStatus.InGame, null));
        _friends.Add(new FriendInfo(new PeerId(105), "오랑우탄", FriendStatus.Online, null));
        _friends.Add(new FriendInfo(new PeerId(106), "나무늘보", FriendStatus.Offline, null));
    }

    private Room SeedRoom(uint code, TimeSpan age, params (PeerId Id, string Name, long Keystrokes)[] seats)
    {
        var room = new Room { Code = code, CreatedUtc = DateTime.UtcNow - age, Host = seats[0].Id };
        foreach ((PeerId id, string name, long keystrokes) in seats)
        {
            room.Seats.Add(new Seat { Id = id, Name = name, Keystrokes = keystrokes });
        }

        _rooms[code] = room;
        return room;
    }

    private void Enter(Room room)
    {
        room.Saved.Remove(SelfId, out long resumed);
        room.Seats.Add(new Seat { Id = SelfId, Name = "플레이어", Keystrokes = resumed });
        _current = room;
        OnRoomChanged?.Invoke();
    }

    /// <summary>
    /// 자리를 비운다. 타수는 룸에 맡겨 두고, 방장이었으면 넘기고, 아무도 안 남으면 룸을 없앤다.
    /// </summary>
    private void RemoveSeat(Room room, PeerId peer)
    {
        Seat seat = SeatOf(room, peer);
        if (seat == null)
        {
            return;
        }

        room.Seats.Remove(seat);
        room.Saved[peer] = seat.Keystrokes;

        if (room.Seats.Count == 0)
        {
            _rooms.Remove(room.Code);
            return;
        }

        if (room.Host == peer)
        {
            room.Host = room.Seats[0].Id;
        }
    }

    private Seat SelfSeat() => SeatOf(_current, SelfId);

    private static Seat SeatOf(Room room, PeerId peer) => room.Seats.Find(s => s.Id == peer);

    private static RoomHandle HandleOf(Room room) =>
        new(RoomCode.Format(room.Code), room.Host, room.Host == SelfId, room.CreatedUtc);

    private void RequireAvailable()
    {
        if (!IsAvailable)
        {
            throw new RoomJoinException(RoomJoinError.SteamUnavailable);
        }
    }
}
