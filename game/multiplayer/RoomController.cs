using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Shared.Mocks;

namespace ProjectSeWoo.Game;

/// <summary>
/// 룸 창(<see cref="RoomWindow"/>)과 멀티 세션(<see cref="INetSession"/>) 사이 (B12).
///
/// <see cref="GameRoot"/> 가 이미 850줄이라 룸 쪽 배선은 여기 모은다. 하는 일:
/// <list type="bullet">
///   <item>창의 요청을 세션 호출로 바꾸고, 실패 사유(<see cref="RoomJoinError"/>)를 문구로 바꾼다.</item>
///   <item>창이 열려 있는 동안 1초마다 랭킹·경과 시간을, 5초마다 친구 목록을 다시 그린다.
///   닫혀 있으면 아무것도 안 그린다 (§7-3 저부하).</item>
///   <item>HUD 에 룸 한 줄(<c>룸 2/4 · 1위 · 1,234타</c>)을 1초마다 쓴다.</item>
/// </list>
/// </summary>
public sealed class RoomController : IDisposable
{
    private const double RoomRefreshSec = 1.0;
    private const double FriendsRefreshSec = 5.0;

    /// <summary>[디버그] Shift+M 으로 들어오는 가짜 친구 이름.</summary>
    private static readonly string[] FakeNames = { "침팬지", "보노보", "긴팔원숭이", "개코원숭이", "마모셋" };

    private readonly INetSession _net;
    private readonly RoomWindow _window;
    private readonly StatusHud _hud;
    private readonly Random _random = new();

    private double _sinceRoom;
    private double _sinceFriends;
    private bool _busy;
    private ulong _nextFakeId = 9001;

    public RoomController(INetSession net, RoomWindow window, StatusHud hud)
    {
        _net = net;
        _window = window;
        _hud = hud;

        _net.OnRoomChanged += OnRoomChanged;
        _window.CreateRequested += OnCreateRequested;
        _window.JoinRequested += OnJoinRequested;
        _window.InviteRequested += OnInviteRequested;
        _window.InviteOverlayRequested += OnInviteOverlayRequested;
        _window.LeaveRequested += OnLeaveRequested;

        _window.Bind(_net);
        UpdateHud();
    }

    public void Dispose()
    {
        _net.OnRoomChanged -= OnRoomChanged;
        _window.CreateRequested -= OnCreateRequested;
        _window.JoinRequested -= OnJoinRequested;
        _window.InviteRequested -= OnInviteRequested;
        _window.InviteOverlayRequested -= OnInviteOverlayRequested;
        _window.LeaveRequested -= OnLeaveRequested;
    }

    /// <summary><see cref="GameRoot"/> 의 <c>_Process</c> 가 매 프레임 부른다.</summary>
    public void Tick(double delta)
    {
        _sinceRoom += delta;
        if (_sinceRoom >= RoomRefreshSec)
        {
            _sinceRoom = 0.0;

            // 내 타수는 이벤트 없이 오르므로 여기서 주기적으로 다시 읽는다.
            UpdateHud();
            if (_window.IsOpen)
            {
                _window.RefreshRoom();
            }
        }

        if (!_window.IsOpen)
        {
            // 열 때는 창이 Open() 에서 한 번 다 그린다. 거기서부터 다시 센다.
            _sinceFriends = 0.0;
            return;
        }

        _sinceFriends += delta;
        if (_sinceFriends >= FriendsRefreshSec)
        {
            _sinceFriends = 0.0;
            _window.RefreshFriends();
        }
    }

    // ------------------------------------------------------------------ 창 요청

    private async void OnCreateRequested()
    {
        await RunBusy(async () =>
        {
            RoomHandle room = await _net.CreateRoom();
            _window.ShowMessage($"로비를 만들었어요. 로비 코드 {room.Uid} 를 친구에게 보내 주세요");
        });
    }

    private async void OnJoinRequested(string uid)
    {
        await RunBusy(async () =>
        {
            RoomHandle room = await _net.JoinRoom(uid);
            _window.ClearCodeInput();
            _window.ShowMessage($"로비 {room.Uid} 에 들어왔어요");
        });
    }

    private void OnInviteRequested(PeerId friend)
    {
        _net.InviteFriend(friend);
        _window.ShowMessage("초대를 보냈어요. 친구가 스팀에서 수락하면 들어와요");
    }

    private void OnInviteOverlayRequested() => _net.OpenInviteOverlay();

    private void OnLeaveRequested()
    {
        _net.LeaveRoom();
        _window.ShowMessage("로비에서 나왔어요. 다시 들어가면 타수가 이어져요");
    }

    /// <summary>
    /// 생성·참가 공통. 기다리는 동안 버튼을 잠그고, 실패는 문구로 바꾼다.
    /// 여기서 예외를 놓치면 <c>async void</c> 라 그대로 앱을 죽인다.
    /// </summary>
    private async Task RunBusy(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _window.SetBusy(true);
        _window.ShowMessage("잠시만요...");
        try
        {
            await action();
        }
        catch (RoomJoinException e)
        {
            GD.Print($"[room] 실패 {e.Error} - {e.Message}");
            _window.ShowMessage(MessageFor(e.Error), isError: true);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[room] 예외 - {e.GetType().Name}: {e.Message}");
            _window.ShowMessage(MessageFor(RoomJoinError.Failed), isError: true);
        }
        finally
        {
            _busy = false;
            _window.SetBusy(false);
            _window.Refresh();
            UpdateHud();
        }
    }

    private static string MessageFor(RoomJoinError error) => error switch
    {
        // 형식이 틀린 것과 없는 룸을 가르지 않는다 - 유저가 할 일(번호를 다시 확인)이 같다.
        RoomJoinError.InvalidCode or RoomJoinError.NotFound => "잘못된 로비 코드입니다",
        RoomJoinError.Full => $"로비가 가득 찼어요 (최대 {RoomWindow.MaxMembers}명)",
        RoomJoinError.SteamUnavailable => "스팀이 켜져 있어야 멀티를 쓸 수 있어요",
        _ => "들어가지 못했어요. 잠시 뒤 다시 해 주세요",
    };

    // ------------------------------------------------------------------ 상태 반영

    private void OnRoomChanged()
    {
        if (_window.IsOpen)
        {
            _window.Refresh();
        }

        UpdateHud();
    }

    private void UpdateHud()
    {
        if (_net.Current == null)
        {
            _hud.SetRoom(null);
            return;
        }

        IReadOnlyList<RoomMember> members = _net.Members;
        foreach (RoomMember m in members)
        {
            if (m.IsSelf)
            {
                _hud.SetRoom($"로비 {members.Count}/{RoomWindow.MaxMembers}"
                    + $" · {RoomRanking.RankOf(members, m)}위 · {m.RoomKeystrokes:N0}타");
                return;
            }
        }

        _hud.SetRoom(null);
    }

    // ------------------------------------------------------------------ 디버그

    /// <summary>
    /// [디버그, Shift+M] 목일 때 가짜 친구를 한 명 들이고(4명 미만이면), 가짜 멤버
    /// 전원의 타수를 조금씩 올린다. 실물 세션에서는 아무 일도 안 한다.
    /// </summary>
    public void DebugSimulateActivity()
    {
        if (_net is not MockNetSession mock)
        {
            GD.PushWarning("[room][디버그] 실물 세션에는 가짜 친구가 없다 - 목일 때만 동작한다");
            return;
        }

        if (_net.Current == null)
        {
            GD.Print("[room][디버그] 룸에 먼저 들어가야 한다 (M → 룸 만들기)");
            return;
        }

        if (_net.Members.Count < RoomWindow.MaxMembers)
        {
            var id = new PeerId(_nextFakeId++);
            mock.SimulateJoin(id, FakeNames[(int)(id.Value % (ulong)FakeNames.Length)]);
        }

        foreach (RoomMember m in _net.Members)
        {
            if (!m.IsSelf)
            {
                mock.SimulateKeystrokes(m.Id, _random.Next(20, 120));
            }
        }
    }

    /// <summary>[디버그, Ctrl+M] 목일 때 마지막으로 들어온 가짜 멤버를 내보낸다.</summary>
    public void DebugSimulateLeave()
    {
        if (_net is not MockNetSession mock)
        {
            return;
        }

        IReadOnlyList<RoomMember> members = _net.Members;
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (!members[i].IsSelf)
            {
                mock.SimulateLeave(members[i].Id);
                return;
            }
        }
    }
}
