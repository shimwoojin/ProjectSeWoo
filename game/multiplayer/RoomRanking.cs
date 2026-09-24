using System;
using System.Collections.Generic;
using System.Linq;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 룸 랭킹 정렬 규칙 (§4-2 "누적 타수 랭킹 — 룸 내 정렬"). 룸 창과 HUD 가 같은
/// 순위를 보여야 하므로 한 곳에 둔다.
///
/// 정렬 키는 <b>룸에서 친 타수</b>다(<see cref="RoomMember.RoomKeystrokes"/>).
/// 누적 타수로 줄을 세우면 먼저 시작한 사람이 영원히 1등이라 "같이 켜 놓고 일하는"
/// 방에서 겨룰 거리가 없다.
/// </summary>
public static class RoomRanking
{
    /// <summary>
    /// 타수 내림차순. 같으면 입장 순서(<see cref="INetSession.Members"/> 의 순서)를
    /// 따른다 - <c>OrderByDescending</c> 는 안정 정렬이다.
    /// </summary>
    public static List<RoomMember> Sort(IReadOnlyList<RoomMember> members) =>
        members.OrderByDescending(m => m.RoomKeystrokes).ToList();

    /// <summary>
    /// 순위. 동점이면 같은 순위다(1, 1, 3). 줄 순서는 입장 순서로 갈리지만,
    /// 먼저 왔다고 더 높은 순위를 주지는 않는다.
    /// </summary>
    public static int RankOf(IReadOnlyList<RoomMember> members, RoomMember member) =>
        1 + members.Count(m => m.RoomKeystrokes > member.RoomKeystrokes);

    /// <summary>룸이 생긴 뒤 지난 시간. 하루를 넘어도 시간으로 센다 (<c>27:04:10</c>).</summary>
    public static string FormatElapsed(DateTime createdUtc)
    {
        TimeSpan elapsed = DateTime.UtcNow - createdUtc;
        if (elapsed < TimeSpan.Zero)
        {
            // 방장 PC 시계가 앞서 있으면 음수가 나온다. 0 으로 보인다.
            elapsed = TimeSpan.Zero;
        }

        return $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }
}
