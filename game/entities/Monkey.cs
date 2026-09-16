using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 펀치하는 원숭이. 4종을 랜덤 교차하되 같은 것이 연달아 나오지 않게 한다 -
/// 검토 §3 의 "키 입력과 애니메이션을 1:1 고정 대응시키지 말 것" 이 이 뜻이다 (§2-3).
/// </summary>
public partial class Monkey : Node2D
{
    /// <summary>
    /// Monkey.tscn 의 애니메이션 이름과, 팔이 최대로 뻗는 키프레임 시각(초).
    /// 나무가 흔들릴 타이밍이 이 값이다 - 애니메이션 길이가 4종 다 달라서
    /// 하나로 못 잡는다. **tscn 의 키프레임을 고치면 이 표도 같이 고친다.**
    /// </summary>
    private static readonly (string Name, double Contact)[] Variants =
    {
        ("punch_a", 0.05),
        ("punch_b", 0.10),
        ("punch_c", 0.04),
        ("punch_d", 0.12),
    };

    private readonly RandomNumberGenerator _rng = new();

    private AnimationPlayer _punches;
    private Rect2 _restBounds;
    private int _lastVariant = -1;

    public override void _Ready()
    {
        _punches = GetNode<AnimationPlayer>("Punches");
        _rng.Randomize();

        // 팔은 일부러 뺀다. 팔이 들어가면 펀치 중에 클릭 영역이 매 프레임 바뀌고,
        // 그만큼 WindowSetMousePassthrough 쓰기가 늘어난다 (§7-3).
        _restBounds = Transform * Shapes.Bounds(GetNode<Polygon2D>("Body"));
    }

    /// <returns>팔이 나무에 닿기까지의 시간(초).</returns>
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
