using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 관전형 멀티 세션 (기획확정-일감분배-260907.md §8-2, §4). 갑 제공 → 을 소비.
///
/// Steam Lobby + P2P 상태 브로드캐스트. **전용 서버 없음.**
/// 경제를 동기화하지 않으므로 치트 방어 서버가 필요 없다 — 이게 멀티를 4주 안에
/// 넣을 수 있게 만드는 유일한 이유다 (§4-1).
///
/// 이 인터페이스에 재화·타이머·강화 수치를 넣자는 요청이 나오면 그건 설계 변경이다.
/// <see cref="PlayerState"/> 의 주석을 먼저 읽을 것.
///
/// <b>2026-09-24 확장 (B12 룸 UI).</b> 룸 화면이 그릴 것(멤버·룸 타수·친구 목록)과
/// 할 것(나가기·초대)이 계약에 없어서 추가했다. 기존 멤버는 그대로다.
/// 룸 랭킹은 누적 타수가 아니라 <b>룸에서 친 타수</b>로 정렬한다
/// (<see cref="RoomMember.RoomKeystrokes"/>). 그 숫자는 세션이 들고 있고 게임은
/// <see cref="AddKeystrokes"/> 로 흘려보내기만 한다 - 재입장 때 이어 세기가
/// 스팀 로비 데이터에 걸려 있어서 게임 레이어가 알 수 없는 영역이기 때문이다.
///
/// <b>메인 스레드 전용이다.</b> 이벤트는 스팀 콜백 펌프(셸의 <c>_Process</c>)에서
/// 나오므로 노드를 바로 만져도 된다.
/// </summary>
public interface INetSession
{
    /// <summary>
    /// 룸을 쓸 수 있는가. 스팀이 안 떠 있으면 false 다 - 그때 생성·참가는
    /// <see cref="RoomJoinError.SteamUnavailable"/> 로 실패하고, 화면은 안내 문구를 띄운다.
    /// 나머지 게임은 이 값과 무관하게 돈다.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>지금 들어가 있는 룸. 룸 밖이면 null.</summary>
    RoomHandle? Current { get; }

    /// <summary>
    /// 룸 멤버. <b>입장 순서</b>이고 나도 들어 있다. 룸 밖이면 빈 목록.
    /// 랭킹 정렬은 호출부가 한다 - 타수가 같으면 이 순서(먼저 온 사람)를 따른다.
    /// </summary>
    IReadOnlyList<RoomMember> Members { get; }

    /// <summary>
    /// 룸을 만든다. 최대 4명 (§4-1). 이미 룸에 있으면 나온 뒤 만든다.
    /// 실패하면 <see cref="RoomJoinException"/>.
    /// </summary>
    Task<RoomHandle> CreateRoom();

    /// <summary>
    /// 룸 UID 로 참가한다. 스팀 친구 초대도 같은 경로로 들어온다.
    /// 룸 코드(<see cref="RoomCode"/>, 대소문자·하이픈 무관)와
    /// <see cref="FriendInfo.RoomUid"/> 를 둘 다 받는다. 이미 룸에 있으면 나온 뒤 들어간다.
    /// 실패하면 <see cref="RoomJoinException"/>.
    /// </summary>
    Task<RoomHandle> JoinRoom(string uid);

    /// <summary>룸에서 나간다. 룸 밖이면 아무 일도 안 한다.</summary>
    void LeaveRoom();

    /// <summary>
    /// 내 타건을 룸 타수에 더한다. <c>IInputSource.OnKeystrokes</c> 가 줄 때마다
    /// 그대로 넘기면 된다 - 룸 밖이면 버린다. 전송 주기(1초)는 세션이 정한다.
    /// </summary>
    void AddKeystrokes(int count);

    /// <summary>
    /// 스팀 친구 목록. 부른 시점의 스냅샷이고 <see cref="FriendStatus"/> 순으로
    /// 정렬돼 있다. 룸 화면이 열려 있을 때만 부른다 - 상주 앱이라 안 보이는
    /// 목록을 갱신할 이유가 없다 (§7-3). 스팀이 없으면 빈 목록.
    /// </summary>
    IReadOnlyList<FriendInfo> GetFriends();

    /// <summary>친구를 지금 룸으로 초대한다 (스팀 초대). 룸 밖이면 아무 일도 안 한다.</summary>
    void InviteFriend(PeerId friend);

    /// <summary>스팀 오버레이의 초대 창을 연다. 룸 밖이면 아무 일도 안 한다.</summary>
    void OpenInviteOverlay();

    /// <summary>
    /// 내 상태를 뿌린다. 200ms 단위로 묶어 unreliable 로 나간다 (§4-1).
    ///
    /// 타건을 개별 전송하지 않는 이유는 대역폭이고, unreliable 인 이유는
    /// 한 구간을 놓쳐도 다음 스냅샷이 덮어쓰기 때문이다. 관전용 지표라
    /// 유실이 치명적이지 않다.
    /// </summary>
    void Broadcast(PlayerState s);

    /// <summary>
    /// 룸 상태가 바뀌었다 - 입장·퇴장, 방장 변경, <b>다른</b> 멤버의 룸 타수 변경.
    /// 내 타수는 <see cref="AddKeystrokes"/> 로 내가 넣은 것이라 알리지 않는다
    /// (100ms 마다 화면을 다시 그리게 된다). 여러 변경이 한 번으로 묶여 올 수 있다.
    /// </summary>
    event Action OnRoomChanged;

    event Action<PeerId, PlayerState> OnPeerState;

    event Action<PeerId> OnPeerJoin;

    event Action<PeerId> OnPeerLeave;
}
