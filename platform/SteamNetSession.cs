using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using ProjectSeWoo.Shared;
using Steamworks;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="INetSession"/> 실물 - Steam Lobby (A9, 기획서 §4). 전용 서버 없음.
///
/// <b>무엇을 로비에 싣는가</b> (전부 문자열 키/값, 스팀 서버가 멤버 전원에게 뿌린다):
/// <list type="table">
///   <item><term>로비 데이터 <c>v</c></term><description>프로토콜 버전. 지금은 1.</description></item>
///   <item><term>로비 데이터 <c>created</c></term><description>룸이 생긴 unix 초. 경과 시간 표시용. 방장이 만들 때 쓴다.</description></item>
///   <item><term>로비 데이터 <c>s:&lt;steamid&gt;</c></term><description>
///   나간 사람의 룸 타수. <b>방장만</b> 쓸 수 있어서 멤버가 나가면(<see cref="LobbyChatUpdate_t"/>)
///   방장이 마지막으로 본 값을 적어 둔다. 다시 들어오면 여기서 이어 센다.
///   방장 자신은 나가기 직전에 직접 적는다.</description></item>
///   <item><term>멤버 데이터 <c>ks</c></term><description>
///   그 멤버의 룸 타수. 본인만 쓸 수 있다. <see cref="PublishIntervalSec"/> 에 한 번, 바뀌었을 때만.</description></item>
/// </list>
///
/// <b>200ms P2P 상태(<see cref="Broadcast"/>)는 아직 없다</b> - A10/B10 에서 붙는다.
/// 랭킹은 1초 단위면 충분하고, 로비 멤버 데이터는 연결 관리 없이 스팀이 알아서 뿌려 준다.
///
/// <b>룸 코드 = 로비 SteamID 하위 32비트</b> (<see cref="RoomCode"/>). 상위 32비트는
/// 유니버스(Public)·계정 종류(Chat)·인스턴스 플래그(Lobby|MMSLobby)라 모든 로비가
/// 같다(<see cref="LobbyHighBits"/>). 그래서 코드만으로 로비 ID 를 되살려 바로
/// <c>JoinLobby</c> 할 수 있고, 코드→로비를 찾아 줄 서버가 필요 없다. 만든 로비가
/// 이 가정과 다르면 로그로 크게 알린다 - 그때도 친구 목록·초대로는 들어올 수 있다.
///
/// <b>로비 종류는 Public 이다.</b> 우리는 로비 목록 검색(<c>RequestLobbyList</c>)을
/// 전혀 하지 않으므로 모르는 사람에게 노출되지 않고, 코드를 아는 사람만 들어온다.
/// FriendsOnly 로 두면 친구가 아닌 사람은 코드가 있어도 못 들어온다.
///
/// <b>메인 스레드 전용.</b> 스팀 콜백은 <see cref="SteamService.Tick"/> 의
/// <c>RunCallbacks</c> 에서, 곧 셸의 <c>_Process</c> 에서 온다.
/// </summary>
public sealed class SteamNetSession : INetSession, IDisposable
{
    private const int MaxMembers = 4;

    /// <summary>내 룸 타수를 멤버 데이터로 올리는 최소 간격. 랭킹이 1초 늦는 것은 괜찮다.</summary>
    private const double PublishIntervalSec = 1.0;

    /// <summary>생성 응답 상한. 스팀이 응답을 안 주면 버튼이 영영 잠긴다.</summary>
    private const double CreateTimeoutSec = 15.0;

    /// <summary>
    /// 참가 응답 상한. <b>한 번도 없던 로비에 <c>JoinLobby</c> 하면 스팀이 아예 응답하지
    /// 않는다</b> (2026-09-24 실측: 닫힌 로비는 <c>DoesntExist</c> 로 바로 오는데
    /// <c>000-0001</c> 은 15초 동안 무응답). 코드 입장은 <see cref="ProbeTimeoutSec"/>
    /// 의 사전 확인이 그 경우를 먼저 걸러 내므로, 이 값은 그게 뚫렸을 때의 안전망이다.
    /// 시간 초과는 "그런 룸 없음" 으로 본다.
    /// </summary>
    private const double JoinTimeoutSec = 8.0;

    /// <summary>
    /// 코드 입장 전 사전 확인(<c>RequestLobbyData</c>)의 응답 상한. 없는 로비면 스팀이
    /// <c>m_bSuccess = 0</c> 으로 <b>약 0.2초</b> 만에 답한다 (2026-09-24 실측, 두 번).
    /// 넘기면 확인 없이 그냥 참가를 시도한다 - 확인이 참가를 막는 일은 없어야 한다.
    /// </summary>
    private const double ProbeTimeoutSec = 4.0;

    /// <summary>
    /// 매치메이킹 로비 SteamID 의 상위 32비트: 유니버스 1(Public) &lt;&lt; 24 |
    /// 계정 종류 8(Chat) &lt;&lt; 20 | 인스턴스 0x60000(Lobby|MMSLobby).
    /// 실물 로비 ID 가 <c>1097...</c> 로 시작하는 그것이다.
    /// </summary>
    private const uint LobbyHighBits = 0x01860000;

    private const string KeyVersion = "v";
    private const string KeyCreated = "created";
    private const string KeySavedPrefix = "s:";
    private const string KeyMemberKeystrokes = "ks";
    private const string ProtocolVersion = "1";

    /// <summary>친구 목록 상한. 친구가 수백 명이면 창을 열 때마다 줄 수백 개를 만든다.</summary>
    private const int MaxFriendsListed = 60;

    private readonly SteamService _steam;

    // 콜백 핸들은 필드로 잡아 둔다 - 지역 변수면 GC 가 걷어가고 그때부터 안 온다
    // (SteamService._statsReceived 주석).
    private Callback<LobbyChatUpdate_t> _chatUpdate;
    private Callback<LobbyDataUpdate_t> _dataUpdate;
    private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
    private Callback<GameRichPresenceJoinRequested_t> _presenceJoinRequested;
    private Callback<PersonaStateChange_t> _personaChanged;
    private CallResult<LobbyCreated_t> _createResult;
    private CallResult<LobbyEnter_t> _enterResult;
    private bool _registered;

    /// <summary>진행 중인 생성·참가. 한 번에 하나만.</summary>
    private TaskCompletionSource<RoomHandle> _pending;
    private double _pendingAge;
    private bool _pendingIsJoin;

    /// <summary>
    /// 사전 확인 중인 로비. 여기 값이 있는 동안은 아직 지금 룸에서 안 나갔다 -
    /// 코드를 잘못 쳤다고 있던 룸에서 튕겨 나가면 안 된다.
    /// </summary>
    private CSteamID _probing = CSteamID.Nil;

    /// <summary>
    /// 스팀이 붙기 전에 들어온 참가 요청 - 실행 인자 <c>+connect_lobby</c> 가 대표다.
    /// 스팀이 붙는 첫 <see cref="Tick"/> 에서 처리한다.
    /// </summary>
    private ulong _deferredJoin;

    private CSteamID _lobby = CSteamID.Nil;
    private DateTime _enteredUtc;
    private long _mine;
    private long _published = -1;
    private double _sincePublish;

    /// <summary>
    /// 다른 멤버의 마지막 룸 타수. 멤버가 나간 뒤에는 멤버 데이터를 더 못 읽으므로,
    /// 방장이 <c>s:&lt;id&gt;</c> 로 보관할 값을 여기서 꺼낸다.
    /// </summary>
    private readonly Dictionary<ulong, long> _lastKeystrokes = new();

    /// <summary>
    /// 친구 목록에서 본 룸 코드 → 실제 로비 ID. 친구 목록의 [참가]는 코드를 되살리지 않고
    /// 이 값으로 들어간다 - <see cref="LobbyHighBits"/> 가정이 틀린 로비에도 들어갈 수 있게.
    /// </summary>
    private readonly Dictionary<uint, CSteamID> _knownLobbies = new();

    /// <summary><see cref="OnRoomChanged"/> 를 다음 틱에 한 번으로 묶어 부른다.</summary>
    private bool _changed;

    public SteamNetSession(SteamService steam)
    {
        _steam = steam;
        _deferredJoin = ParseConnectLobby(OS.GetCmdlineArgs());
    }

    public event Action OnRoomChanged;

    /// <summary>A10(P2P 상태)에서 채운다. 그때까지 부를 일이 없다.</summary>
    public event Action<PeerId, PlayerState> OnPeerState
    {
        add { }
        remove { }
    }

    public event Action<PeerId> OnPeerJoin;

    public event Action<PeerId> OnPeerLeave;

    public bool IsAvailable => _steam.IsInitialized && _registered;

    private bool InLobby => _lobby.IsValid() && _lobby != CSteamID.Nil;

    public RoomHandle? Current
    {
        get
        {
            if (!InLobby)
            {
                return null;
            }

            CSteamID owner = SteamMatchmaking.GetLobbyOwner(_lobby);
            return new RoomHandle(
                CodeOf(_lobby),
                new PeerId(owner.m_SteamID),
                owner == SteamUser.GetSteamID(),
                CreatedUtc());
        }
    }

    public IReadOnlyList<RoomMember> Members
    {
        get
        {
            if (!InLobby)
            {
                return Array.Empty<RoomMember>();
            }

            CSteamID self = SteamUser.GetSteamID();
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(_lobby);
            int count = SteamMatchmaking.GetNumLobbyMembers(_lobby);

            var members = new List<RoomMember>(count);
            for (int i = 0; i < count; i++)
            {
                CSteamID id = SteamMatchmaking.GetLobbyMemberByIndex(_lobby, i);
                bool isSelf = id == self;
                members.Add(new RoomMember(
                    new PeerId(id.m_SteamID),
                    isSelf ? SteamFriends.GetPersonaName() : SteamFriends.GetFriendPersonaName(id),
                    isSelf ? _mine : KeystrokesOf(id),
                    id == owner,
                    isSelf));
            }

            return members;
        }
    }

    // ------------------------------------------------------------------ 생성·참가·나가기

    public Task<RoomHandle> CreateRoom()
    {
        RequireIdle();
        LeaveRoom();

        SteamAPICall_t call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, MaxMembers);
        Task<RoomHandle> task = BeginPending(isJoin: false);
        _createResult.Set(call);
        GD.Print("[net] 로비 생성 요청");
        return task;
    }

    public Task<RoomHandle> JoinRoom(string uid)
    {
        if (!RoomCode.TryParse(uid, out uint code))
        {
            throw new RoomJoinException(RoomJoinError.InvalidCode, $"'{uid}'");
        }

        CSteamID lobby = _knownLobbies.TryGetValue(code, out CSteamID known) ? known : LobbyOf(code);
        return JoinLobby(lobby, precheck: true);
    }

    public void LeaveRoom()
    {
        if (!InLobby)
        {
            return;
        }

        CSteamID lobby = _lobby;

        // 다시 들어왔을 때 이어 세도록 마지막 값을 남긴다. 방장이면 직접 보관하고,
        // 아니면 멤버 데이터로 올려 두면 방장이 나간 것을 보고 보관한다.
        PublishNow();
        if (IsOwner(lobby))
        {
            SteamMatchmaking.SetLobbyData(lobby, SavedKey(SteamUser.GetSteamID()), Format(_mine));
        }

        SteamMatchmaking.LeaveLobby(lobby);
        SteamFriends.ClearRichPresence();

        GD.Print($"[net] 로비 나감 {CodeOf(lobby)} - 룸 타수 {_mine}");

        _lobby = CSteamID.Nil;
        _mine = 0;
        _published = -1;
        _lastKeystrokes.Clear();

        OnRoomChanged?.Invoke();
    }

    public void AddKeystrokes(int count)
    {
        if (InLobby && count > 0)
        {
            _mine += count;
        }
    }

    /// <summary>
    /// 참가. <paramref name="precheck"/> 면 먼저 로비가 있는지 묻고(<c>RequestLobbyData</c>),
    /// 있을 때만 지금 룸을 나와 들어간다. 사람이 친 코드일 때 켠다 - 스팀 초대·친구창
    /// 참가는 로비가 있는 게 확실하니 바로 들어간다.
    /// </summary>
    private Task<RoomHandle> JoinLobby(CSteamID lobby, bool precheck)
    {
        RequireIdle();

        if (InLobby && _lobby == lobby)
        {
            return Task.FromResult(Current.Value);
        }

        Task<RoomHandle> task = BeginPending(isJoin: true);

        // 이미 그 로비의 데이터를 받고 있으면 false 가 온다 - 그때는 확인 없이 들어간다.
        if (precheck && SteamMatchmaking.RequestLobbyData(lobby))
        {
            _probing = lobby;
            GD.Print($"[net] 로비 확인 {CodeOf(lobby)} ({lobby.m_SteamID})");
            return task;
        }

        StartJoin(lobby);
        return task;
    }

    /// <summary>지금 룸을 나와 실제로 <c>JoinLobby</c> 한다. 결과는 <see cref="OnLobbyEntered"/>.</summary>
    private void StartJoin(CSteamID lobby)
    {
        _probing = CSteamID.Nil;
        LeaveRoom();

        SteamAPICall_t call = SteamMatchmaking.JoinLobby(lobby);
        _pendingAge = 0.0;
        _enterResult.Set(call);
        GD.Print($"[net] 로비 참가 요청 {CodeOf(lobby)} ({lobby.m_SteamID})");
    }

    private void OnLobbyCreated(LobbyCreated_t cb, bool ioFailure)
    {
        if (ioFailure || cb.m_eResult != EResult.k_EResultOK)
        {
            FailPending(RoomJoinError.Failed, $"CreateLobby {(ioFailure ? "IO 실패" : cb.m_eResult.ToString())}");
            return;
        }

        var lobby = new CSteamID(cb.m_ulSteamIDLobby);
        if ((uint)(lobby.m_SteamID >> 32) != LobbyHighBits)
        {
            // 코드로는 이 로비를 되살릴 수 없다. 친구 목록·초대로는 여전히 들어온다.
            GD.PushError($"[net] 로비 ID 상위 비트가 예상과 다르다 ({lobby.m_SteamID:X16}, 예상 {LobbyHighBits:X8}xxxxxxxx)"
                + " - 룸 코드 입장이 안 된다. SteamNetSession.LobbyHighBits 를 확인할 것");
        }

        SteamMatchmaking.SetLobbyData(lobby, KeyVersion, ProtocolVersion);
        SteamMatchmaking.SetLobbyData(
            lobby, KeyCreated, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

        Enter(lobby);
    }

    private void OnLobbyEntered(LobbyEnter_t cb, bool ioFailure)
    {
        if (ioFailure)
        {
            FailPending(RoomJoinError.Failed, "JoinLobby IO 실패");
            return;
        }

        var response = (EChatRoomEnterResponse)cb.m_EChatRoomEnterResponse;
        switch (response)
        {
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess:
                Enter(new CSteamID(cb.m_ulSteamIDLobby));
                return;
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist:
                FailPending(RoomJoinError.NotFound, response.ToString());
                return;
            case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull:
                FailPending(RoomJoinError.Full, response.ToString());
                return;
            default:
                FailPending(RoomJoinError.Failed, response.ToString());
                return;
        }
    }

    /// <summary>생성이든 참가든 로비에 들어간 뒤의 공통 처리.</summary>
    private void Enter(CSteamID lobby)
    {
        _lobby = lobby;
        _enteredUtc = DateTime.UtcNow;
        _lastKeystrokes.Clear();

        string version = SteamMatchmaking.GetLobbyData(lobby, KeyVersion);
        if (version != ProtocolVersion)
        {
            GD.PushWarning($"[net] 로비 프로토콜 버전이 다르다 (로비 '{version}', 우리 '{ProtocolVersion}')");
        }

        // 전에 이 룸에 있었으면 그때 값에서 이어 센다 (방장이 보관해 둔 것).
        _mine = ParseLong(SteamMatchmaking.GetLobbyData(lobby, SavedKey(SteamUser.GetSteamID())));
        _published = -1;
        PublishNow();

        int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
        for (int i = 0; i < count; i++)
        {
            RememberKeystrokes(SteamMatchmaking.GetLobbyMemberByIndex(lobby, i));
        }

        // 스팀 친구창의 "게임 참가" 가 이 문자열로 우리를 부른다 - 게임이 꺼져 있으면
        // 실행 인자로, 켜져 있으면 GameRichPresenceJoinRequested_t 로 온다.
        SteamFriends.SetRichPresence("connect", $"+connect_lobby {lobby.m_SteamID}");

        GD.Print($"[net] 로비 입장 {CodeOf(lobby)} ({lobby.m_SteamID}) - 멤버 {count}명, 이어 세기 {_mine}타");

        TaskCompletionSource<RoomHandle> pending = _pending;
        _pending = null;
        pending?.TrySetResult(Current.Value);
        _changed = true;
    }

    // ------------------------------------------------------------------ 친구·초대

    public IReadOnlyList<FriendInfo> GetFriends()
    {
        if (!IsAvailable)
        {
            return Array.Empty<FriendInfo>();
        }

        var appId = new AppId_t(_steam.AppId);
        int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        var list = new List<FriendInfo>(count);

        for (int i = 0; i < count; i++)
        {
            CSteamID id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            string name = SteamFriends.GetFriendPersonaName(id);
            FriendStatus status = SteamFriends.GetFriendPersonaState(id) == EPersonaState.k_EPersonaStateOffline
                ? FriendStatus.Offline
                : FriendStatus.Online;
            string roomUid = null;

            if (SteamFriends.GetFriendGamePlayed(id, out FriendGameInfo_t game) && game.m_gameID.AppID() == appId)
            {
                status = FriendStatus.InGame;

                CSteamID lobby = game.m_steamIDLobby;
                if (lobby.IsValid() && lobby.IsLobby())
                {
                    status = FriendStatus.InRoom;
                    roomUid = CodeOf(lobby);
                    _knownLobbies[(uint)lobby.m_SteamID] = lobby;
                }
            }

            list.Add(new FriendInfo(new PeerId(id.m_SteamID), name, status, roomUid));
        }

        list.Sort((a, b) =>
        {
            int byStatus = a.Status.CompareTo(b.Status);
            return byStatus != 0 ? byStatus : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        if (list.Count > MaxFriendsListed)
        {
            // 정렬이 오프라인을 맨 뒤로 보내므로 잘리는 것은 오프라인부터다.
            list.RemoveRange(MaxFriendsListed, list.Count - MaxFriendsListed);
        }

        return list;
    }

    public void InviteFriend(PeerId friend)
    {
        if (!InLobby)
        {
            return;
        }

        bool sent = SteamMatchmaking.InviteUserToLobby(_lobby, new CSteamID(friend.Value));
        GD.Print($"[net] 초대 {friend} - {(sent ? "보냄" : "실패")}");
    }

    public void OpenInviteOverlay()
    {
        if (!InLobby)
        {
            return;
        }

        // 스팀 오버레이는 스팀으로 실행했을 때만 붙는다. 에디터·VS 실행에서는 안 뜨는 게 정상이다.
        SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
    }

    /// <summary>A10(P2P 200ms 상태)에서 채운다. 지금은 버린다 - 계약상 부르는 쪽에 분기가 없어야 한다.</summary>
    public void Broadcast(PlayerState s)
    {
    }

    // ------------------------------------------------------------------ 틱·콜백

    /// <summary>
    /// 셸이 <see cref="SteamService.Tick"/> 바로 뒤에 매 프레임 부른다. 스팀이 늦게 붙는
    /// 경우(30초 재시도)가 있어서 콜백 등록도 여기서 처음 붙은 것을 본 순간에 한다.
    /// </summary>
    public void Tick(double delta)
    {
        if (!_steam.IsInitialized)
        {
            return;
        }

        if (!_registered)
        {
            Register();
        }

        if (_deferredJoin != 0 && _pending == null)
        {
            ulong lobby = _deferredJoin;
            _deferredJoin = 0;
            JoinFromSteam(new CSteamID(lobby), "실행 인자");
        }

        if (_pending != null && _probing != CSteamID.Nil)
        {
            _pendingAge += delta;
            if (_pendingAge >= ProbeTimeoutSec)
            {
                GD.Print($"[net] 로비 확인 응답 없음 ({ProbeTimeoutSec:F0}초) - 확인 없이 참가 시도");
                StartJoin(_probing);
            }
        }
        else if (_pending != null)
        {
            _pendingAge += delta;
            double limit = _pendingIsJoin ? JoinTimeoutSec : CreateTimeoutSec;
            if (_pendingAge >= limit)
            {
                _createResult.Cancel();
                _enterResult.Cancel();
                FailPending(
                    _pendingIsJoin ? RoomJoinError.NotFound : RoomJoinError.Failed,
                    $"{limit:F0}초 안에 스팀 응답이 없다");
            }
        }

        if (InLobby)
        {
            _sincePublish += delta;
            if (_sincePublish >= PublishIntervalSec)
            {
                PublishNow();
            }
        }

        if (_changed)
        {
            _changed = false;
            OnRoomChanged?.Invoke();
        }
    }

    private void Register()
    {
        _registered = true;
        _chatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        _dataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
        _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(
            cb => JoinFromSteam(cb.m_steamIDLobby, "스팀 초대"));
        _presenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnPresenceJoinRequested);
        _personaChanged = Callback<PersonaStateChange_t>.Create(OnPersonaChanged);
        _createResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
        _enterResult = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
        GD.Print("[net] 스팀 로비 콜백 등록");
    }

    /// <summary>입장·퇴장. 퇴장이면 방장이 그 사람의 룸 타수를 보관한다.</summary>
    private void OnLobbyChatUpdate(LobbyChatUpdate_t cb)
    {
        if (!InLobby || cb.m_ulSteamIDLobby != _lobby.m_SteamID)
        {
            return;
        }

        var who = new CSteamID(cb.m_ulSteamIDUserChanged);
        var change = (EChatMemberStateChange)cb.m_rgfChatMemberStateChange;

        if ((change & EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
        {
            RememberKeystrokes(who);
            OnPeerJoin?.Invoke(new PeerId(who.m_SteamID));
        }
        else
        {
            // 소유권은 이 콜백 전에 이미 넘어와 있다 - 방장이 나간 경우 새 방장이 적는다.
            if (IsOwner(_lobby) && _lastKeystrokes.TryGetValue(who.m_SteamID, out long last))
            {
                SteamMatchmaking.SetLobbyData(_lobby, SavedKey(who), Format(last));
            }

            OnPeerLeave?.Invoke(new PeerId(who.m_SteamID));
        }

        _changed = true;
    }

    /// <summary>로비·멤버 데이터가 바뀌었다. 다른 사람의 룸 타수가 여기로 온다.</summary>
    private void OnLobbyDataUpdate(LobbyDataUpdate_t cb)
    {
        // 코드 입장의 사전 확인 응답. 로비 자체의 데이터(멤버 = 로비)일 때만 본다.
        if (_probing != CSteamID.Nil
            && cb.m_ulSteamIDLobby == _probing.m_SteamID
            && cb.m_ulSteamIDMember == cb.m_ulSteamIDLobby)
        {
            CSteamID lobby = _probing;
            if (cb.m_bSuccess == 0)
            {
                FailPending(RoomJoinError.NotFound, "RequestLobbyData 실패 - 없는 로비");
            }
            else
            {
                StartJoin(lobby);
            }

            return;
        }

        if (!InLobby || cb.m_ulSteamIDLobby != _lobby.m_SteamID)
        {
            return;
        }

        if (cb.m_ulSteamIDMember != cb.m_ulSteamIDLobby)
        {
            RememberKeystrokes(new CSteamID(cb.m_ulSteamIDMember));
        }

        _changed = true;
    }

    private void OnPresenceJoinRequested(GameRichPresenceJoinRequested_t cb)
    {
        ulong lobby = ParseConnectLobby(cb.m_rgchConnect.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (lobby != 0)
        {
            JoinFromSteam(new CSteamID(lobby), "스팀 게임 참가");
        }
    }

    /// <summary>닉네임이 늦게 도착하면 랭킹의 이름을 다시 그려야 한다.</summary>
    private void OnPersonaChanged(PersonaStateChange_t cb)
    {
        if (InLobby && (cb.m_nChangeFlags & EPersonaChange.k_EPersonaChangeName) != 0)
        {
            _changed = true;
        }
    }

    /// <summary>
    /// 게임 밖(스팀 초대 수락·친구창 참가·실행 인자)에서 온 참가. 화면이 기다리고 있지
    /// 않으므로 실패는 로그로만 남는다 - 룸 창을 열면 룸 밖 상태가 보인다.
    /// </summary>
    private async void JoinFromSteam(CSteamID lobby, string via)
    {
        GD.Print($"[net] {via} 로 참가 - {lobby.m_SteamID}");
        try
        {
            await JoinLobby(lobby, precheck: false);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[net] {via} 참가 실패 - {e.Message}");
        }
    }

    // ------------------------------------------------------------------ 내부

    private void PublishNow()
    {
        _sincePublish = 0.0;
        if (!InLobby || _mine == _published)
        {
            return;
        }

        SteamMatchmaking.SetLobbyMemberData(_lobby, KeyMemberKeystrokes, Format(_mine));
        _published = _mine;
    }

    private void RememberKeystrokes(CSteamID member)
    {
        if (member == SteamUser.GetSteamID())
        {
            return;
        }

        string raw = SteamMatchmaking.GetLobbyMemberData(_lobby, member, KeyMemberKeystrokes);
        if (!string.IsNullOrEmpty(raw))
        {
            _lastKeystrokes[member.m_SteamID] = ParseLong(raw);
        }
    }

    private long KeystrokesOf(CSteamID member)
    {
        string raw = SteamMatchmaking.GetLobbyMemberData(_lobby, member, KeyMemberKeystrokes);
        if (!string.IsNullOrEmpty(raw))
        {
            return ParseLong(raw);
        }

        return _lastKeystrokes.TryGetValue(member.m_SteamID, out long last) ? last : 0;
    }

    private DateTime CreatedUtc()
    {
        long seconds = ParseLong(SteamMatchmaking.GetLobbyData(_lobby, KeyCreated));
        return seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : _enteredUtc;
    }

    private static bool IsOwner(CSteamID lobby) =>
        SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID();

    /// <summary>
    /// <b><c>RunContinuationsAsynchronously</c> 를 빼면 안 된다.</b> 결과는 스팀 콜백
    /// (<c>RunCallbacks</c>) 안에서 정해지는데, 기본 옵션이면 <c>await</c> 뒤의 코드
    /// (룸 창 갱신 등)가 그 콜백 한가운데서 인라인으로 돈다. 2026-09-24 프로브에서
    /// 그 뒷부분이 <c>SteamAPI.Shutdown</c> 까지 불렀고, 스팀이 콜백을 마저 정리하다
    /// <c>FreeLastCallback</c> 에서 AccessViolation 으로 죽었다. 이 옵션이면 이어지는
    /// 코드는 콜백이 끝난 뒤 Godot 메인 스레드에서 돈다.
    /// </summary>
    private Task<RoomHandle> BeginPending(bool isJoin)
    {
        _pending = new TaskCompletionSource<RoomHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAge = 0.0;
        _pendingIsJoin = isJoin;
        return _pending.Task;
    }

    private void FailPending(RoomJoinError error, string detail)
    {
        GD.Print($"[net] 실패 {error} - {detail}");
        _probing = CSteamID.Nil;
        TaskCompletionSource<RoomHandle> pending = _pending;
        _pending = null;
        pending?.TrySetException(new RoomJoinException(error, detail));
    }

    private void RequireIdle()
    {
        if (!IsAvailable)
        {
            throw new RoomJoinException(RoomJoinError.SteamUnavailable);
        }

        if (_pending != null)
        {
            throw new RoomJoinException(RoomJoinError.Failed, "이전 요청이 아직 안 끝났다");
        }
    }

    private static string CodeOf(CSteamID lobby) => RoomCode.Format((uint)lobby.m_SteamID);

    private static CSteamID LobbyOf(uint code) => new(((ulong)LobbyHighBits << 32) | code);

    private static string SavedKey(CSteamID member) => KeySavedPrefix + member.m_SteamID.ToString(CultureInfo.InvariantCulture);

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static long ParseLong(string raw) =>
        long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value > 0 ? value : 0;

    /// <summary><c>+connect_lobby &lt;id&gt;</c> 를 찾는다. 없으면 0.</summary>
    private static ulong ParseConnectLobby(string[] args)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id))
            {
                return id;
            }
        }

        return 0;
    }

    public void Dispose()
    {
        if (_steam.IsInitialized && _registered)
        {
            LeaveRoom();
        }

        if (_pending != null)
        {
            FailPending(RoomJoinError.Failed, "종료");
        }

        _chatUpdate?.Dispose();
        _dataUpdate?.Dispose();
        _lobbyJoinRequested?.Dispose();
        _presenceJoinRequested?.Dispose();
        _personaChanged?.Dispose();
        _createResult?.Dispose();
        _enterResult?.Dispose();
        _registered = false;
    }
}
