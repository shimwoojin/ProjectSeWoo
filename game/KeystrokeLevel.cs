using System;

namespace ProjectSeWoo.Game;

/// <summary>
/// 누적 타수 → 레벨 환산 (기획확정-일감분배-260907.md §6).
///
/// <b>레벨은 기록이지 성장이 아니다.</b> 재화를 주지 않고 나무·원숭이·강화 어디에도
/// 곱해지지 않는다 - §6 이 "자랑용 숫자이고 게임 밸런스에 영향 없음" 이라고 못박은
/// 그대로다. 그래서 이 파일은 순수 함수만 들고 있고 상태가 없다.
///
/// <b>왜 로그인가</b> — 빈 나무를 치는 시간이 게임 시간의 대부분이고(§1-2 "아직 남은
/// 약점"), 그 구간을 메우는 것이 이 숫자의 일이다. 선형이면 첫날에 올라가던 속도가
/// 한 달 뒤에도 같아서 아무 일도 안 일어나는 것처럼 느껴진다. 로그면 초반에는
/// 문장 몇 줄에 한 번 오르고, 뒤로 갈수록 한 칸이 무거워진다.
/// </summary>
internal static class KeystrokeLevel
{
    /// <summary>
    /// Lv.2 에 필요한 타수. 곡선의 시작점이다.
    ///
    /// Lv.2 가 36타다 - 한 문장이다. <b>첫 레벨업이 앉은 자리에서 일어나야</b>
    /// 이 숫자가 살아 있다는 것을 알 수 있다.
    /// </summary>
    private const double Base = 100.0;

    /// <summary>
    /// 레벨당 필요량 배수. 이 값 하나가 곡선 전체의 기울기다.
    ///
    /// 1.35 를 고른 기준은 §6 의 마일스톤 세 개다 — 1만/10만/100만 타가
    /// <b>7~8 레벨씩 고르게 벌어지도록</b> 맞췄다. 마일스톤 사이가 들쭉날쭉하면
    /// 도전과제와 레벨이 서로 다른 이야기를 하게 된다.
    /// </summary>
    private const double Ratio = 1.35;

    /// <summary>
    /// <para>누적 타수의 레벨. 0타는 Lv.1 이다.</para>
    ///
    /// 레벨별 문턱(<see cref="ThresholdFor"/>)과 §6 마일스톤이 걸리는 자리:
    ///
    /// <code>
    ///   Lv.2         36타        Lv.20     29,847타
    ///   Lv.5        233타        Lv.24     99,367타   &lt;- 10만 타 마일스톤 = Lv.24
    ///   Lv.10     1,390타        Lv.31    812,755타   &lt;- 100만 타 마일스톤 = Lv.31
    ///   Lv.16     8,916타   &lt;- 1만 타 마일스톤 = Lv.16
    /// </code>
    ///
    /// 마일스톤 셋이 Lv.16 / 24 / 31 로 7~8 레벨씩 벌어진다.
    ///
    /// W2 밸런스 튜닝(B14)에서 다시 볼 수 있다. <b>다만 A15 에서 도전과제를 스팀에
    /// 등록한 뒤에는 마일스톤 수치를 못 바꾼다</b>(API Name 에 숫자가 들어 있다) —
    /// 그때는 <see cref="Ratio"/> 만 움직이게 된다.
    /// </summary>
    public static int LevelFor(long keystrokes)
    {
        if (keystrokes <= 0)
        {
            return 1;
        }

        return 1 + (int)Math.Floor(Math.Log(1.0 + (keystrokes / Base)) / Math.Log(Ratio));
    }

    /// <summary>
    /// <paramref name="level"/> 에 도달하는 데 필요한 누적 타수.
    /// <see cref="LevelFor"/> 의 역함수라 <c>LevelFor(ThresholdFor(L)) == L</c> 이다.
    /// 다음 레벨까지 얼마나 남았는지 보여줄 때 쓴다.
    /// </summary>
    public static long ThresholdFor(int level)
    {
        if (level <= 1)
        {
            return 0;
        }

        return (long)Math.Ceiling(Base * (Math.Pow(Ratio, level - 1) - 1.0));
    }

    /// <summary>
    /// 지금 레벨 구간에서 얼마나 왔는가 (0~1). HUD 진행 표시용이다.
    /// </summary>
    public static float ProgressInLevel(long keystrokes)
    {
        int level = LevelFor(keystrokes);
        long from = ThresholdFor(level);
        long to = ThresholdFor(level + 1);

        if (to <= from)
        {
            return 0f;
        }

        return Math.Clamp((keystrokes - from) / (float)(to - from), 0f, 1f);
    }
}
