using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 수확한 바나나가 떨어져 착지하고 튀는 연출 (§2-3). 연출만 하고 스스로 사라진다 -
/// 재화는 이미 <see cref="GameRoot"/> 가 입력 시점에 더했다.
/// </summary>
public partial class FallingBanana : Node2D
{
    public void Drop(float groundY)
    {
        float fall = Mathf.Max(groundY - Position.Y, 1f);
        double duration = Mathf.Min(0.25f + fall / 900f, 0.8f);

        Tween tween = CreateTween();
        tween.TweenProperty(this, "position:y", groundY, duration)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "scale", new Vector2(1.25f, 0.75f), 0.06)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "scale", Vector2.One, 0.1)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(this, "modulate:a", 0.0f, 0.18)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
