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
///
/// <b>피벗은 꼭지(스프라이트 위쪽 끝)다</b> - 씬의 <c>Fruit.offset</c>. 슬롯 위치가
/// 곧 가지에 붙는 점이라, 커질 때 가지에서 아래로 늘어진다. 가운데 피벗이면
/// 사방으로 부풀어서 가지에서 떨어져 보였다.
/// </summary>
public partial class TreeSlot : Node2D
{
    // SelfModulate 는 노란 텍스처에 곱해진다 - 초록을 곱하면 풋바나나 색이 된다.
    private static readonly Color Unripe = new(0.30f, 0.50f, 0.22f);
    private static readonly Color Green = new(0.45f, 0.72f, 0.30f);
    private static readonly Color Turning = new(0.80f, 0.95f, 0.50f);
    private static readonly Color Ripe = new(1.0f, 1.0f, 1.0f);

    /// <summary>
    /// 익은 황금 바나나 (B13). SelfModulate 는 노란 텍스처에 곱해지므로 초록·파랑을 눌러 진한
    /// 호박색을 만든다. **밝기를 1 넘게 올리면 다시 노랗게 날아가서 보통 바나나와 구분이 안
    /// 됐다**(첫 시안, 2026-09-24 캡처) - 밝기는 거의 그대로 두고 옆에 반짝임 아이콘을 붙여 가른다.
    /// 상시 애니메이션은 넣지 않는다 - 저부하 재그리기 정책(§7-3).
    /// </summary>
    private static readonly Color GoldTint = new(1.0f, 0.62f, 0.10f);
    private static readonly Color GoldGlow = new(1.08f, 1.04f, 1.0f);

    /// <summary>황금 송이 옆에 붙이는 반짝임. 잔상 장식(spark_01) 그림을 그대로 쓴다.</summary>
    private const string SparkleTexture = "res://assets/cursor/trail/spark_01.png";

    private Sprite2D _sparkle;

    /// <summary>황금 송이는 익었을 때 이만큼 크게 그린다 - 한눈에 달라 보이게.</summary>
    private const float GoldScale = 1.15f;

    /// <summary>
    /// 색 구간 경계이자 "톡" 펄스가 나는 지점. 크기는 초반에 거의 다 자라므로
    /// 후반의 변화는 색이 맡는다 - 경계를 넘을 때 펄스를 줘서 단계가 바뀐 것을
    /// 눈치채게 한다. 성장은 몇 분에 걸쳐 일어나서 연속 변화만으로는 안 보인다.
    /// </summary>
    private const float GreenAt = 0.4f;

    private const float TurningAt = 0.75f;

    private Sprite2D _fruit;
    private Tween _flash;

    /// <summary>
    /// 씬에 박힌 기본 배율. 성장·반짝임이 여기에 곱해진다 - 덮어쓰면 바나나가
    /// 텍스처 원본 크기(223px)로 튀어나온다.
    /// </summary>
    private Vector2 _baseScale;

    /// <summary>반짝임이 끝나면 돌아갈 크기·밝기. 황금 송이는 보통보다 크고 밝다.</summary>
    private Vector2 _restScale;
    private Color _restModulate = Colors.White;

    /// <summary>지난 <see cref="SetProgress"/> 의 단계. -1 = 아직 안 그림 (첫 그리기엔 펄스 없음).</summary>
    private int _stage = -1;

    public override void _Ready()
    {
        _fruit = GetNode<Sprite2D>("Fruit");
        _baseScale = _fruit.Scale;
        _restScale = _baseScale;

        // 송이 오른쪽 위. 열매 아래로 늘어지는 피벗(꼭지)이라 FruitCenter 기준으로 잡는다.
        _sparkle = new Sprite2D
        {
            Texture = GD.Load<Texture2D>(SparkleTexture),
            Scale = Vector2.One * 0.42f,
            Position = FruitCenter + new Vector2(22f, -24f),
            Visible = false,
            ZIndex = 1,
        };
        AddChild(_sparkle);
    }

    /// <summary>다 자란 열매의 중심 (슬롯 로컬). 수확 때 떨어지는 바나나의 출발점.</summary>
    public Vector2 FruitCenter => _fruit.Position + _fruit.Offset * _baseScale;

    /// <param name="t">0 = 갓 수확한 빈 슬롯, 1 = 바나나 열림.</param>
    /// <param name="golden">황금 송이인가. <b>익었을 때만</b> 금빛으로 그린다 - 자라는 동안은 보통
    /// 바나나와 같아서, 익는 순간 황금으로 드러난다.</param>
    public void SetProgress(float t, bool golden = false)
    {
        _flash?.Kill();

        // 초반에 빠르게 커지고(ease-out) 뒤는 색이 익는다. 알파는 거의 건드리지 않는다 -
        // 예전엔 0.5→1 로 페이드해서 "반투명한 게 옅어지기만" 하는 것처럼 보였다.
        float grow = 1f - (1f - t) * (1f - t);
        bool showGold = golden && t >= 1f;
        Vector2 scale = _baseScale * Mathf.Lerp(0.2f, 1.0f, grow) * (showGold ? GoldScale : 1f);

        Color tint = t < GreenAt
            ? Unripe.Lerp(Green, t / GreenAt)
            : t < TurningAt
                ? Green.Lerp(Turning, (t - GreenAt) / (TurningAt - GreenAt))
                : Turning.Lerp(Ripe, (t - TurningAt) / (1f - TurningAt));
        _fruit.SelfModulate = showGold ? GoldTint : tint with { A = t < 0.05f ? 0.85f : 1f };
        _fruit.Modulate = showGold ? GoldGlow : Colors.White;
        _sparkle.Visible = showGold;

        // 송이들이 서로 겹쳐 있어서 뒤쪽 슬롯의 황금 송이는 앞 송이에 가려졌다 - 맨 앞으로 올린다.
        ZIndex = showGold ? 1 : 0;
        _restModulate = _fruit.Modulate;
        _restScale = scale;

        int stage = t < GreenAt ? 0 : t < TurningAt ? 1 : 2;
        bool advanced = _stage >= 0 && stage > _stage;
        _stage = stage;

        if (advanced)
        {
            Pulse(scale);
        }
        else
        {
            _fruit.Scale = scale;
        }
    }

    /// <summary>바나나가 열린 순간의 반짝임 1회 (§2-3).</summary>
    public void FlashRipe()
    {
        _flash?.Kill();
        _flash = CreateTween();
        _flash.TweenProperty(_fruit, "scale", _restScale * 1.25f, 0.09)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _flash.Parallel()
            .TweenProperty(_fruit, "modulate", new Color(2.2f, 2.2f, 2.2f), 0.09);
        _flash.TweenProperty(_fruit, "scale", _restScale, 0.22)
            .SetTrans(Tween.TransitionType.Quad);
        _flash.Parallel()
            .TweenProperty(_fruit, "modulate", _restModulate, 0.22);
    }

    /// <summary>성장 단계가 바뀐 순간의 작은 "톡". 반짝임 없이 크기만 튄다.</summary>
    private void Pulse(Vector2 target)
    {
        _flash = CreateTween();
        _flash.TweenProperty(_fruit, "scale", target * 1.12f, 0.07)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _flash.TweenProperty(_fruit, "scale", target, 0.11)
            .SetTrans(Tween.TransitionType.Quad);
    }
}
