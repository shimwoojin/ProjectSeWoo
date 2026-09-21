using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 나무 슬롯 하나의 겉모습. [비어있음] --(성장)--> [바나나 열림] 을 표현만 한다 (§2-1).
/// 타이머는 <see cref="Tree"/> 가 들고 있다 - 상태를 두 군데 두지 않기 위해서다.
///
/// <b>B4: <c>Polygon2D</c> 를 실물 바나나 스프라이트로 바꿨다.</b> 익는 정도는
/// <see cref="CanvasItem.SelfModulate"/> 로, 열린 순간의 반짝임은
/// <see cref="CanvasItem.Modulate"/> 로 건다 - **두 채널을 나눠 쓰는 것이 핵심이다.**
/// 하나로 합치면 반짝이는 동안 익은 정도의 색이 덮여서, 반짝임이 끝나면 색이
/// 튄다. <c>Polygon2D</c> 일 때 <c>Color</c> 와 <c>Modulate</c> 가 따로였던 것을
/// 그대로 옮긴 것이다.
/// </summary>
public partial class TreeSlot : Node2D
{
    private static readonly Color Growing = new(0.42f, 0.62f, 0.30f);
    private static readonly Color Ripe = new(1.0f, 1.0f, 1.0f);

    private Sprite2D _fruit;
    private Tween _flash;

    /// <summary>
    /// 씬에 박힌 기본 배율. 성장·반짝임이 여기에 곱해진다 - 덮어쓰면 바나나가
    /// 텍스처 원본 크기(223px)로 튀어나온다.
    /// </summary>
    private Vector2 _baseScale;

    public override void _Ready()
    {
        _fruit = GetNode<Sprite2D>("Fruit");
        _baseScale = _fruit.Scale;
    }

    /// <param name="t">0 = 갓 수확한 빈 슬롯, 1 = 바나나 열림.</param>
    public void SetProgress(float t)
    {
        _flash?.Kill();

        _fruit.Scale = _baseScale * Mathf.Lerp(0.35f, 1.0f, t);
        _fruit.SelfModulate = Growing.Lerp(Ripe, t * t)
            with { A = Mathf.Lerp(0.5f, 1.0f, t) };
        _fruit.Modulate = Colors.White;
    }

    /// <summary>바나나가 열린 순간의 반짝임 1회 (§2-3).</summary>
    public void FlashRipe()
    {
        _flash?.Kill();
        _flash = CreateTween();
        _flash.TweenProperty(_fruit, "scale", _baseScale * 1.45f, 0.09)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _flash.Parallel()
            .TweenProperty(_fruit, "modulate", new Color(2.2f, 2.2f, 2.2f), 0.09);
        _flash.TweenProperty(_fruit, "scale", _baseScale, 0.22)
            .SetTrans(Tween.TransitionType.Quad);
        _flash.Parallel()
            .TweenProperty(_fruit, "modulate", Colors.White, 0.22);
    }
}
