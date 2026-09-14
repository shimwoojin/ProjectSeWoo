using System;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 커서에 동시에 붙일 수 있는 장착 슬롯 (기획확정-일감분배-260907.md §3-1).
/// 슬롯 3개에 각각 하나씩 끼우고, 조합으로 개성이 난다.
/// </summary>
public enum CursorSlot
{
    /// <summary>커서에 매달려 흔들리는 원숭이. 초기 6종.</summary>
    Hang,

    /// <summary>커서를 따라오는 잔상. 바나나 조각, 잎, 반짝임. 초기 6종.</summary>
    Trail,

    /// <summary>커서 뒤에 깔리는 작은 장식. 초기 4종.</summary>
    Base,
}

/// <summary>
/// 멀티 룸의 상대 식별자. Steam ID 를 그대로 담는다.
///
/// <c>ulong</c> 을 그냥 쓰지 않고 감싸는 이유는, 누적 타수도 <c>long</c> 이고
/// 방 번호도 숫자라서 인자 순서를 바꿔 넣어도 컴파일이 통과하기 때문이다.
/// </summary>
public readonly record struct PeerId(ulong Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>
/// 룸 핸들. 생성·참가의 결과물이고, 초대 링크에 쓸 UID 를 들고 있다.
/// </summary>
/// <param name="Uid">유저가 친구에게 불러줄 수 있는 방 코드 (§4-1).</param>
/// <param name="Host">호스트. 공동 나무를 넣게 되면 이쪽이 권위를 갖는다 (§4-3).</param>
/// <param name="IsHost">내가 호스트인가.</param>
public readonly record struct RoomHandle(string Uid, PeerId Host, bool IsHost);

/// <summary>
/// 멀티 룸에 뿌리는 상태 스냅샷 (§4-1). 200ms 단위로 묶어 보낸다.
///
/// **여기 없는 것이 이 설계의 핵심이다.** 재화 잔액, 나무 성장 타이머, 강화 수치는
/// 동기화하지 않는다. 경제가 전부 로컬이라 치트 방어 서버가 필요 없고,
/// 멀티에서 보이는 건 행동과 자랑 지표뿐이다.
///
/// 전송 목록은 개인정보 방침에 그대로 공개된다 (§7-6). 필드를 늘리려면
/// 스토어 문구와 게임 내 안내도 같이 고쳐야 한다.
/// </summary>
public readonly record struct PlayerState
{
    /// <summary>이번 200ms 구간의 타건 수. 펀치 연출 트리거용이고 키 코드는 없다.</summary>
    public ushort KeystrokesInWindow { get; init; }

    /// <summary>이번 구간에 수확한 바나나 개수. 토스트 연출용.</summary>
    public byte HarvestsInWindow { get; init; }

    /// <summary>누적 타수. 룸 내 랭킹의 정렬 키 (§6).</summary>
    public long TotalKeystrokes { get; init; }

    /// <summary>장착 중인 커서 장식. null 이면 그 슬롯은 비어 있다.</summary>
    public string EquippedHang { get; init; }

    public string EquippedTrail { get; init; }

    public string EquippedBase { get; init; }

    /// <summary>도감 수집률 0~100 (§3-3).</summary>
    public byte CollectionPercent { get; init; }
}
