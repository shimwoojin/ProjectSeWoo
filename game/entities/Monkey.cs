using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 펀치하는 원숭이. 4종을 랜덤 교차하되 같은 것이 연달아 나오지 않게 한다 -
/// 검토 §3 의 "키 입력과 애니메이션을 1:1 고정 대응시키지 말 것" 이 이 뜻이다 (§2-3).
///
/// <b>B4: 자리표시자 <c>Polygon2D</c> 두 개(Arm/Body)를 스프라이트 시트 한 장으로
/// 바꿨다.</b> 펀치는 이제 팔의 회전이 아니라 <see cref="Sprite2D.Frame"/> 을 돌린다.
/// 시트는 <c>tools/assetgen/slice.py</c> 가 8칸 균등 그리드로 다시 조판한 것이고,
/// 칸 수는 <c>assets/entities/monkey_punch.frames.json</c> 에 같이 적혀 있다.
///
/// 프레임 뜻 (원본 시트 순서):
/// <code>
///   0 대기   1 왼손 준비   2 왼손 뻗음(닿음)   3 가드
///   4 오른손 들기         5 오른손 준비        6 오른손 뻗음(닿음)   7 대기
/// </code>
/// </summary>
public partial class Monkey : Node2D
{
    /// <summary>
    /// Monkey.tscn 의 애니메이션 이름과, 주먹이 최대로 뻗는 키프레임 시각(초).
    /// 나무가 흔들릴 타이밍이 이 값이다 - 애니메이션 길이가 4종 다 달라서
    /// 하나로 못 잡는다. **tscn 의 키프레임을 고치면 이 표도 같이 고친다.**
    ///
    /// 값은 tscn 에서 프레임 2 또는 6(뻗은 프레임)이 나타나는 시각이다.
    /// </summary>
    private static readonly (string Name, double Contact)[] Variants =
    {
        ("punch_a", 0.09),
        ("punch_b", 0.18),
        ("punch_c", 0.03),
        ("punch_d", 0.16),
    };

    private readonly RandomNumberGenerator _rng = new();

    private AnimationPlayer _punches;
    private Rect2 _restBounds;
    private int _lastVariant = -1;

    public override void _Ready()
    {
        _punches = GetNode<AnimationPlayer>("Punches");
        _rng.Randomize();

        // 쉴 때(프레임 0) 한 번만 잰다. 펀치 중에 다시 재면 클릭 영역이 매 프레임
        // 바뀌고, 그만큼 WindowSetMousePassthrough 쓰기가 늘어난다 (§7-3).
        // Shapes.Bounds(Sprite2D) 가 시트 전체가 아니라 **한 칸**을 기준으로 잰다 -
        // 통째로 재면 영역이 8칸 폭만큼 부풀어 나무 너머까지 먹는다.
        _restBounds = Transform * Shapes.Bounds(GetNode<Sprite2D>("Sprite"));
    }

    /// <returns>주먹이 나무에 닿기까지의 시간(초).</returns>
    public double Punch()
    {
        int pick = _rng.RandiRange(0, Variants.Length - 1);
        if (pick == _lastVariant)
        {
            pick = (pick + 1) % Variants.Length;
        }

        _lastVariant = pick;
        _punches.Play(Variants[pick].Name);
        return Variants[pick].Contact;
    }

    public Rect2 GetBounds() => _restBounds;
}
