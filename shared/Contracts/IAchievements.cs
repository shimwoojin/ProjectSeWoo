namespace ProjectSeWoo.Shared;

/// <summary>
/// 스팀 도전과제 (기획확정-일감분배-260907.md §3-3, §6 / A8 스텁 · A15 실제 등록).
/// 갑 제공 → 을 소비.
///
/// **기획서 §8-2 의 인터페이스 4종에 없던 6번째 계약이다.** 그런데도 목(mock)을 끼고
/// 인터페이스로 가르는 이유는 §8-2 의 4종과 정확히 같다 — 해금 조건(도감 100%,
/// 누적 타수 마일스톤)을 아는 것은 게임 레이어(을)인데, 스팀에 실제로 쓰는 것은
/// 플랫폼(갑)이다. 을이 <c>SteamUserStats</c> 를 직접 부르기 시작하면 A15 에서
/// 도전과제 이름이 바뀔 때 game/ 이 같이 흔들리고, 스팀 없이 돌리는 개발 실행에서
/// game/ 이 통째로 죽는다.
///
/// **스팀이 없어도 게임은 돈다.** 이 앱은 상주 오버레이라 스팀을 안 켠 채로 켜져 있는
/// 시간이 더 길다 (§7-1). 그래서 <see cref="IsAvailable"/> 가 false 여도 나머지
/// 메서드는 던지지 않고 조용히 버린다 — 을은 분기 없이 그냥 부르면 된다.
/// <see cref="IInputSource.IsAvailable"/> 과 같은 규칙이다.
/// </summary>
public interface IAchievements
{
    /// <summary>
    /// 스팀에 붙었고 통계를 받아왔는가. false 면 <see cref="Unlock"/> 은 버려지고
    /// <see cref="IsUnlocked"/> 는 항상 false 다. **분기용이 아니라 표시용이다** —
    /// 도전과제 UI 에 "스팀 오프라인" 을 띄우고 싶을 때만 읽으면 된다.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 해금한다. 이미 해금된 것을 다시 불러도 안전하다 (스팀이 무시한다).
    /// 스팀 쪽 저장은 이 호출 안에서 같이 일어난다.
    /// </summary>
    /// <param name="id">스팀 파트너 사이트에 등록한 API Name. <see cref="AchievementIds"/> 를 쓴다.</param>
    void Unlock(string id);

    /// <summary>해금돼 있는가. 세이브가 아니라 스팀 쪽 값이다 — 다른 PC 에서 딴 것도 true 다.</summary>
    bool IsUnlocked(string id);

    /// <summary>
    /// 진행도 토스트를 띄운다 ("10,000 / 100,000 타"). 해금은 하지 않는다.
    ///
    /// 누적 타수 마일스톤(§6)처럼 목표가 큰 항목에만 쓴다. 스팀이 자체적으로
    /// 도배를 막지만(같은 구간을 반복해서 부르면 안 띄운다), 매 타건마다 부르라고
    /// 만든 API 가 아니다 — 구간을 넘을 때만 부른다.
    /// </summary>
    void IndicateProgress(string id, int current, int max);
}

/// <summary>
/// 도전과제 API Name 목록.
///
/// **아직 스팀 파트너 사이트에 등록되지 않았다 (A15 일감).** 여기 있는 것은 기획서가
/// 이미 확정한 두 축(§3-3 도감 100%, §6 누적 타수 마일스톤)을 코드에서 부를 수 있게
/// 이름만 먼저 박아둔 것이다. 문자열을 을 쪽에 직접 쓰게 두면 A15 에서 이름을 고칠 때
/// game/ 을 뒤져야 하므로 여기 한 곳에 모은다 — <see cref="SceneGroups"/> 와 같은 이유다.
///
/// **마일스톤 수치는 잠정이다.** §6 은 "로그 스케일 레벨 환산" 만 확정했고 구체적인
/// 도전과제 구간은 정하지 않았다. 10배씩 세 칸은 B3(레벨 곡선)와 W2 밸런스 튜닝 뒤에
/// 다시 본다. 이름에 숫자가 들어 있으므로 **수치를 바꾸면 API Name 도 같이 바뀐다** —
/// 스팀에 등록한 뒤에는 이름을 못 바꾸니 A15 전에 확정해야 한다.
/// </summary>
public static class AchievementIds
{
    /// <summary>도감 수집률 100% (§3-3). 기획서가 유일하게 명시한 도전과제다.</summary>
    public const string Collection100 = "ACH_COLLECTION_100";

    /// <summary>누적 1만 타 (§6).</summary>
    public const string Keystrokes10K = "ACH_KEYSTROKES_10K";

    /// <summary>누적 10만 타 (§6).</summary>
    public const string Keystrokes100K = "ACH_KEYSTROKES_100K";

    /// <summary>누적 100만 타 (§6).</summary>
    public const string Keystrokes1M = "ACH_KEYSTROKES_1M";

    /// <summary>
    /// 누적 타수 마일스톤을 작은 것부터. <see cref="IAchievements.IndicateProgress"/> 의
    /// 구간 계산과 해금 판정을 을이 표로 돌릴 수 있게 열어둔다.
    /// </summary>
    public static readonly (string Id, int Threshold)[] KeystrokeMilestones =
    {
        (Keystrokes10K, 10_000),
        (Keystrokes100K, 100_000),
        (Keystrokes1M, 1_000_000),
    };
}
