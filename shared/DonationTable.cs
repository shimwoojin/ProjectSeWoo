using System;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 기부 (B20, 2026-09-28 — docs/B20-DONATION.md). 바나나를 기부함에 넣으면 사라지고, <b>누적 기부량</b>만 남아
/// 칭호가 된다. 칭호는 HUD 내 이름 옆과 멀티 친구 칸에 보인다(자랑). 도전과제·스팀 통계도 누적 기부량으로 판다.
///
/// 왜 만들었나: 도감을 다 채우면 바나나를 쓸 곳이 없다. 앞으로 바나나를 돈으로 살 수 있게 하면 더 그렇다 -
/// 끝이 없는 사용처가 있어야 바나나가 계속 의미가 있다. 사라지는 쪽(싱크)이라 장식 시세·경제를 흔들지 않는다.
///
/// <b>누적 기부량의 진실은 서버다</b> (<c>players.donated_total</c>, <see cref="IEconomyService.DonatedTotal"/>) - 잔액과
/// 같은 원장이고, 도전과제가 이 값을 믿으므로 클라이언트가 지어낼 수 없어야 한다. 이 표는 칭호 이름과 문턱뿐이라
/// 서버 사본이 없다.
/// </summary>
public static class DonationTable
{
    /// <summary>칭호 (문턱, 이름). 작은 것부터. 문턱 미만이면 칭호 없음.</summary>
    public static readonly (long Threshold, string Title)[] Titles =
    {
        (100, "새싹 후원자"),
        (1_000, "바나나 후원자"),
        (10_000, "정글 후원자"),
        (100_000, "정글의 은인"),
    };

    /// <summary>기부 창의 버튼 금액. "전부" 는 창이 따로 둔다.</summary>
    public static readonly long[] Amounts = { 10, 100, 1_000 };

    /// <summary>한 번에 기부할 수 있는 최대 - 서버(<c>economy.ts MAX_DONATION</c>)도 같은 값으로 막는다.</summary>
    public const long MaxPerRequest = 1_000_000_000;

    /// <summary>누적 <paramref name="total"/> 의 칭호. 없으면 null.</summary>
    public static string TitleFor(long total)
    {
        string title = null;
        foreach ((long threshold, string name) in Titles)
        {
            if (total >= threshold)
            {
                title = name;
            }
        }

        return title;
    }

    /// <summary>다음 칭호와 그 문턱. 마지막 칭호까지 왔으면 null.</summary>
    public static (long Threshold, string Title)? NextTitle(long total)
    {
        foreach ((long threshold, string name) in Titles)
        {
            if (total < threshold)
            {
                return (threshold, name);
            }
        }

        return null;
    }

    /// <summary>--selftest: 문턱이 커지는 순서이고 이름이 비어 있지 않은가. 통과하면 null.</summary>
    public static string SelfTest()
    {
        for (int i = 0; i < Titles.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(Titles[i].Title) || (i > 0 && Titles[i].Threshold <= Titles[i - 1].Threshold))
            {
                return $"칭호 표 {i} 번째가 순서·이름 규칙에 안 맞는다";
            }
        }

        return TitleFor(99) == null && TitleFor(100) == Titles[0].Title && NextTitle(long.MaxValue) == null
            ? null
            : "칭호 문턱 계산이 틀렸다";
    }
}
