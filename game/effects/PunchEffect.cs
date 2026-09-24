using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 주먹이 줄기에 닿는 순간의 타격 이펙트 (§2-3). 8프레임을 한 번 돌리고 스스로
/// 사라진다 - <see cref="FallingBanana"/> 와 같은 "연출만 하고 사라지는" 노드다.
///
/// 시트(<c>assets/effects/punch_effect.png</c>)는 <c>slice.py</c> 가 프레임마다
/// 무게중심을 칸 가운데에 맞춰 깔았다 - 그래서 노드 위치가 곧 맞은 자리다.
///
/// 빠르게 치면 여러 개가 겹쳐 터진다. 매번 똑같은 그림이 겹치면 도장처럼 보여서
/// 회전·좌우 반전·크기를 조금씩 흔든다.
/// </summary>
public partial class PunchEffect : Node2D
{
    /// <summary>한 프레임 길이(초). 8프레임이면 0.28초 - 펀치 애니메이션(0.17~0.34초)과 비슷하다.</summary>
    private const double FrameSeconds = 0.035;

    public override void _Ready()
    {
        var sprite = GetNode<Sprite2D>("Sprite");
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        Rotation = rng.RandfRange(-0.35f, 0.35f);
        Scale = Vector2.One * rng.RandfRange(0.9f, 1.1f);
        sprite.FlipV = rng.Randf() < 0.5f;

        int frames = sprite.Hframes * sprite.Vframes;
        Tween tween = CreateTween();
        tween.TweenProperty(sprite, "frame", frames - 1, FrameSeconds * (frames - 1))
            .From(0);
        tween.TweenCallback(Callable.From(QueueFree)).SetDelay(FrameSeconds);
    }
}
