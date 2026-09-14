using System;
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
/// </summary>
public interface INetSession
{
    /// <summary>룸을 만든다. 최대 4명 (§4-1).</summary>
    Task<RoomHandle> CreateRoom();

    /// <summary>룸 UID 로 참가한다. 스팀 친구 초대도 같은 경로로 들어온다.</summary>
    Task<RoomHandle> JoinRoom(string uid);

    /// <summary>
    /// 내 상태를 뿌린다. 200ms 단위로 묶어 unreliable 로 나간다 (§4-1).
    ///
    /// 타건을 개별 전송하지 않는 이유는 대역폭이고, unreliable 인 이유는
    /// 한 구간을 놓쳐도 다음 스냅샷이 덮어쓰기 때문이다. 관전용 지표라
    /// 유실이 치명적이지 않다.
    /// </summary>
    void Broadcast(PlayerState s);

    event Action<PeerId, PlayerState> OnPeerState;

    event Action<PeerId> OnPeerJoin;

    event Action<PeerId> OnPeerLeave;
}
