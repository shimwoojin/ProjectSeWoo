using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 펀치하는 원숭이. B1 에서는 팔 하나가 왕복하는 것뿐이고,
/// B2 가 여기를 AnimationPlayer 4종 랜덤 교차로 바꾼다 (§2-3).
/// </summary>
public partial class Monkey : Node2D
{
    private const float RestRotation = -0.25f;
    private const float StrikeRotation = 0.8f;

    private Polygon2D _body;
    private Polygon2D _arm;
    private Tween _swing;

    public override void _Ready()
    {
        _body = GetNode<Polygon2D>("Body");
        _arm = GetNode<Polygon2D>("Arm");
    }

    public void Punch()
    {
        _swing?.Kill();
        _swing = CreateTween();
        _swing.TweenProperty(_arm, "rotation", StrikeRotation, 0.05)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _swing.TweenProperty(_arm, "rotation", RestRotation, 0.12)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    /// <summary>
    /// 팔은 일부러 뺀다. 팔이 들어가면 펀치 중에 클릭 영역이 매 프레임 바뀌고,
    /// 그만큼 <c>WindowSetMousePassthrough</c> 쓰기가 늘어난다 (§7-3).
    /// </summary>
    public Rect2 GetBounds() => Transform * Shapes.Bounds(_body);
}
