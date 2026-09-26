using System;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 강화 3축의 단계별 효과와 가격 (B13, 2026-09-24 — docs/B13-UPGRADES.md).
///
/// <b>서버 사본이 따로 있다</b> — <c>server/src/catalog.ts</c> 의 <c>UPGRADES</c>. 진짜 판정은
/// 서버가 자기 표로 한다(가격을 클라이언트가 보내면 조작된다). 이 표는 목 경제와 강화 UI 가
/// 쓴다. <b>두 표가 갈라지면 조용히 갈라진다</b> — 한쪽을 고치면 다른 쪽도 같이 고칠 것.
///
/// 레벨 L 의 효과는 <c>Effect[L]</c>, L 에서 L+1 로 올리는 가격은 <c>Price[L]</c>.
/// 그래서 <c>Price.Length == Effect.Length - 1</c> 이고, 최대 레벨은 <c>Price.Length</c>.
/// 표보다 큰 레벨(예전 곡선으로 올린 개발 계정)은 마지막 단계로 본다.
/// </summary>
/// <b>가격은 B14(2026-09-26)에서 B13 의 두 배가 됐다</b> - 첫 강화가 반나절 플레이로 본전을 뽑아 "강화 먼저" 가 늘 정답이었다.
/// 두 배면 본전이 16~25시간이 되어 꾸미기와 효율 사이에 고민이 생긴다 (docs/B14-BALANCE.md).
public static class UpgradeTable
{
    /// <summary>가지 늘리기: 슬롯 수. 슬롯 = 오프라인 저장고 크기이기도 하다 (기획서 §2-2).</summary>
    public static readonly int[] SlotCount = { 3, 4, 5, 6 };
    public static readonly long[] SlotPrice = { 120, 400, 1200 };

    /// <summary>빨리 익기: 한 송이가 익는 데 걸리는 시간(ms). 8분에서 1분씩.</summary>
    public static readonly long[] GrowthMs = { 480_000, 420_000, 360_000, 300_000, 240_000 };
    public static readonly long[] CyclePrice = { 80, 240, 700, 1800 };

    /// <summary>황금 바나나: 한 송이가 익을 때 황금일 확률(%).</summary>
    public static readonly int[] GoldenChancePercent = { 0, 5, 10, 15, 20 };
    public static readonly long[] GoldenPrice = { 100, 300, 900, 2400 };

    /// <summary>황금 바나나 한 송이의 값. 보통 바나나는 1.</summary>
    public const int GoldenMultiplier = 5;

    public static int MaxLevel(UpgradeAxis axis) => Prices(axis).Length;

    /// <summary>다음 단계 가격. 최대 레벨이면 null.</summary>
    public static long? NextPrice(UpgradeAxis axis, int level)
    {
        long[] prices = Prices(axis);
        return level >= 0 && level < prices.Length ? prices[level] : null;
    }

    public static int SlotsAt(int level) => SlotCount[Clamp(level, SlotCount.Length)];

    public static long GrowthMsAt(int level) => GrowthMs[Clamp(level, GrowthMs.Length)];

    public static int GoldenChanceAt(int level) => GoldenChancePercent[Clamp(level, GoldenChancePercent.Length)];

    private static long[] Prices(UpgradeAxis axis) => axis switch
    {
        UpgradeAxis.Slots => SlotPrice,
        UpgradeAxis.Cycle => CyclePrice,
        UpgradeAxis.Golden => GoldenPrice,
        _ => Array.Empty<long>(),
    };

    private static int Clamp(int level, int length) => Math.Clamp(level, 0, length - 1);
}
