using System;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 내 상태를 200ms 창으로 묶어 로비에 뿌린다 (A10, 기획서 §4-1 "200ms 단위로 묶어 전송").
///
/// 타건은 개별로 보내지 않는다 - 창 하나에 몇 번 쳤는지만 보내고, 받는 쪽이 그 수만큼
/// 친구 원숭이를 펀치시킨다 (B11). 수확도 같다.
///
/// <b>안 바뀌면 덜 보낸다.</b> 치지도 따지도 않았고 장식·도감도 그대로면 200ms 마다 보낼
/// 이유가 없다 - 그때는 <see cref="HeartbeatSec"/> 에 한 번만 보낸다. 받는 쪽은 이것으로
/// "아직 켜져 있다" 를 안다 (자리 비움 표시, B11).
///
/// 무엇을 보내는지는 <see cref="PlayerState"/> 가 전부다 - 개인정보 안내(docs/C2-PRIVACY.md
/// §2-5)의 전송 목록과 같아야 한다.
/// </summary>
public sealed class PlayerStateSender
{
    private const double WindowSec = 0.2;
    private const double HeartbeatSec = 1.0;

    private readonly INetSession _net;

    /// <summary>창 밖의 값들(누적 타수·장착·도감). 게임이 들고 있어서 받아 온다.</summary>
    private readonly Func<PlayerState> _snapshot;

    private int _keystrokes;
    private int _harvests;
    private double _sinceWindow;
    private double _sinceSent;
    private PlayerState _lastSent;
    private string _sentToRoom;

    public PlayerStateSender(INetSession net, Func<PlayerState> snapshot)
    {
        _net = net;
        _snapshot = snapshot;
    }

    public void AddKeystrokes(int count)
    {
        if (_net.Current != null && count > 0)
        {
            _keystrokes += count;
        }
    }

    public void AddHarvests(int count)
    {
        if (_net.Current != null && count > 0)
        {
            _harvests += count;
        }
    }

    public void Tick(double delta)
    {
        if (_net.Current is not { } room)
        {
            _keystrokes = 0;
            _harvests = 0;
            _sentToRoom = null;
            return;
        }

        _sinceWindow += delta;
        _sinceSent += delta;
        if (_sinceWindow < WindowSec)
        {
            return;
        }

        _sinceWindow = 0.0;

        PlayerState state = _snapshot() with
        {
            KeystrokesInWindow = (ushort)Math.Min(_keystrokes, ushort.MaxValue),
            HarvestsInWindow = (byte)Math.Min(_harvests, byte.MaxValue),
        };
        _keystrokes = 0;
        _harvests = 0;

        // 새 로비에 들어왔으면 바로 한 번 보낸다 - 친구 화면에 내 장식이 곧장 떠야 한다.
        bool newRoom = _sentToRoom != room.Uid;
        bool active = state.KeystrokesInWindow > 0 || state.HarvestsInWindow > 0;
        bool looksChanged = state.EquippedHang != _lastSent.EquippedHang
            || state.EquippedTrail != _lastSent.EquippedTrail
            || state.EquippedBase != _lastSent.EquippedBase
            || state.CollectionPercent != _lastSent.CollectionPercent;

        if (!newRoom && !active && !looksChanged && _sinceSent < HeartbeatSec)
        {
            return;
        }

        _net.Broadcast(state);
        _lastSent = state;
        _sentToRoom = room.Uid;
        _sinceSent = 0.0;
    }
}
