using Godot;

namespace ProjectSeWoo.Game;

/// <summary>
/// 나무 슬롯 하나의 겉모습. [비어있음] --(성장)--> [바나나 열림] 을 표현만 한다 (§2-1).
/// 타이머는 <see cref="Tree"/> 가 들고 있다 - 상태를 두 군데 두지 않기 위해서다.
/// </summary>
public partial class TreeSlot : Node2D
{
    private static readonly Color Growing = new(0.30f, 0.52f, 0.22f);
    private static readonly Color Ripe = new(0.98f, 0.82f, 0.22f);

    private Polygon2D _fruit;

    public override void _Ready()
    {
        _fruit = GetNode<Polygon2D>("Fruit");
    }

    /// <param name="t">0 = 갓 수확한 빈 슬롯, 1 = 바나나 열림.</param>
    public void SetProgress(float t)
    {
        _fruit.Scale = Vector2.One * Mathf.Lerp(0.35f, 1.0f, t);
        _fruit.Color = Growing.Lerp(Ripe, t * t);
        _fruit.Modulate = new Color(1, 1, 1, Mathf.Lerp(0.5f, 1.0f, t));
    }
}
