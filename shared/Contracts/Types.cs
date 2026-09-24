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
/// 플랫폼(갑)과 게임(을) 씬이 타입 의존 없이 서로를 찾을 때 쓰는 Godot 그룹 이름.
/// 문자열을 양쪽에 따로 박아 두면 언젠가 한쪽만 바뀌므로 여기 한 곳에 둔다.
/// </summary>
public static class SceneGroups
{
    /// <summary>
    /// `game/GameRoot`가 <c>_Ready()</c>에서 <c>AddToGroup(SceneGroups.GameRoot)</c>로
    /// 자기 자신을 등록한다. <c>OverlayShell</c>이 <c>GetFirstNodeInGroup</c>으로
    /// 찾아서 <see cref="IInteractiveArea"/>로 캐스팅해 쓴다 - platform/이 game/의
    /// 구체 타입을 컴파일 타임에 참조하지 않는다.
    /// </summary>
    public const string GameRoot = "game_root";
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
///
/// <b>스냅샷이다.</b> 방장이 나가면 스팀이 소유권을 다른 멤버에게 넘기므로
/// <see cref="Host"/>/<see cref="IsHost"/> 는 바뀔 수 있다 - 지금 값은
/// <see cref="INetSession.Current"/> 를 다시 읽거나 <see cref="RoomMember.IsHost"/> 를 본다.
/// </summary>
/// <param name="Uid">유저가 친구에게 불러줄 수 있는 방 코드 (§4-1, <see cref="RoomCode"/>).</param>
/// <param name="Host">호스트. 공동 나무를 넣게 되면 이쪽이 권위를 갖는다 (§4-3).</param>
/// <param name="IsHost">내가 호스트인가.</param>
/// <param name="CreatedUtc">
/// 룸이 생긴 시각. 룸 랭킹은 이때부터 센다 - 늦게 들어온 사람은 들어온 때부터.
/// </param>
public readonly record struct RoomHandle(string Uid, PeerId Host, bool IsHost, DateTime CreatedUtc);

/// <summary>
/// 룸 멤버 한 명. <see cref="INetSession.Members"/> 가 입장 순서대로 돌려준다.
/// </summary>
/// <param name="Name">스팀 닉네임.</param>
/// <param name="RoomKeystrokes">
/// <b>이 룸에서</b> 친 타수 - 룸 랭킹의 정렬 키다. 누적 타수(<c>SaveData.TotalKeystrokes</c>)
/// 가 아니다: 들어온 순간 0 에서 시작하고, 나갔다 다시 들어오면 나갈 때 값에서 이어진다.
/// 룸이 없어지면(마지막 사람이 나가면) 같이 사라진다.
/// </param>
/// <param name="IsHost">지금 방장인가.</param>
/// <param name="IsSelf">나인가.</param>
public readonly record struct RoomMember(PeerId Id, string Name, long RoomKeystrokes, bool IsHost, bool IsSelf);

/// <summary>친구 목록에서 보이는 상태. 목록 정렬 순서이기도 하다 (위가 먼저).</summary>
public enum FriendStatus
{
    /// <summary>우리 게임의 룸에 있다. <see cref="FriendInfo.RoomUid"/> 로 바로 참가할 수 있다.</summary>
    InRoom,

    /// <summary>우리 게임을 켜 놓았지만 룸에는 없다.</summary>
    InGame,

    Online,

    Offline,
}

/// <summary>스팀 친구 한 명.</summary>
/// <param name="RoomUid">
/// 친구가 들어가 있는 룸의 코드. <see cref="FriendStatus.InRoom"/> 일 때만 있고
/// 나머지는 null 이다. 그대로 <see cref="INetSession.JoinRoom"/> 에 넘긴다.
/// </param>
public readonly record struct FriendInfo(PeerId Id, string Name, FriendStatus Status, string RoomUid);

/// <summary><see cref="RoomJoinException"/> 의 사유. 화면 문구가 이걸로 갈린다.</summary>
public enum RoomJoinError
{
    /// <summary>룸 코드 형식이 아니다 (<see cref="RoomCode.TryParse"/> 실패).</summary>
    InvalidCode,

    /// <summary>그런 룸이 없다. 코드가 틀렸거나 모두 나가서 룸이 사라졌다.</summary>
    NotFound,

    /// <summary>4명이 다 찼다 (§4-1).</summary>
    Full,

    /// <summary>스팀이 안 붙어 있다 (<see cref="INetSession.IsAvailable"/> false).</summary>
    SteamUnavailable,

    /// <summary>그 밖의 실패. 원인은 로그에 남긴다.</summary>
    Failed,
}

/// <summary>룸 생성·참가 실패. <see cref="INetSession.CreateRoom"/>/<see cref="INetSession.JoinRoom"/> 가 던진다.</summary>
public sealed class RoomJoinException : Exception
{
    public RoomJoinException(RoomJoinError error, string detail = null)
        : base(detail ?? error.ToString())
    {
        Error = error;
    }

    public RoomJoinError Error { get; }
}

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

    /// <summary>
    /// 누적 타수 (§6). 친구 칸의 레벨 표시용이다 - <b>랭킹 키가 아니다.</b> 로비 랭킹은
    /// 로비에 들어온 뒤 친 타수(<see cref="RoomMember.RoomKeystrokes"/>)로 정렬한다.
    /// </summary>
    public long TotalKeystrokes { get; init; }

    /// <summary>장착 중인 커서 장식. null 이면 그 슬롯은 비어 있다.</summary>
    public string EquippedHang { get; init; }

    public string EquippedTrail { get; init; }

    public string EquippedBase { get; init; }

    /// <summary>도감 수집률 0~100 (§3-3).</summary>
    public byte CollectionPercent { get; init; }
}
