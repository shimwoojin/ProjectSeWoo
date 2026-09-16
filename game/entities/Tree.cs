using System;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 나무 하나. 슬롯별 성장 타이머를 들고 있고, 펀치 1회에 열린 바나나 1개를 내준다 (§2-2).
/// </summary>
public partial class Tree : Node2D
{
    /// <summary>강화 상한 (§2-2, §5). 슬롯 배치 반경도 이 수를 기준으로 잡았다.</summary>
    private const int MaxSlots = 8;

    private const float SlotRingRadius = 62f;

    /// <summary>
    /// 성장 표시를 몇 단으로 끊어 갱신할지. 8분 주기면 약 7초에 한 번 다시 그린다 -
    /// 매 프레임 색/크기를 건드리면 상주 앱 부하 기준(§7-3)에 맞지 않는다.
    /// </summary>
    private const int ProgressSteps = 64;

    [Export] private PackedScene _slotScene;

    private Node2D _crown;
    private Node2D _slotRoot;
    private Polygon2D _trunk;
    private Polygon2D _canopy;
    private CpuParticles2D _leaves;
    private Tween _shake;

    private TreeSlot[] _slots;
    private long[] _timers;
    private int[] _drawnStep;
    private long _growthMs = 1;

    /// <summary>
    /// 흔들기 전의 클릭 영역. 흔들리는 동안 다시 재지 않는다 - 매 프레임 값이 바뀌면
    /// 그만큼 WindowSetMousePassthrough 쓰기가 늘어난다 (§7-3).
    /// </summary>
    private Rect2 _restBounds;

    public override void _Ready()
    {
        _crown = GetNode<Node2D>("Crown");
        _slotRoot = GetNode<Node2D>("Crown/Slots");
        _canopy = GetNode<Polygon2D>("Crown/Canopy");
        _leaves = GetNode<CpuParticles2D>("Crown/Leaves");
        _trunk = GetNode<Polygon2D>("Trunk");

        Rect2 crownBounds = _crown.Transform * Shapes.Bounds(_canopy);
        _restBounds = Transform * Shapes.Bounds(_trunk).Merge(crownBounds);
    }

    /// <summary>세이브 스키마를 그대로 받는다 (§4-4). 파일 I/O 는 B5 가 붙인다.</summary>
    public void Configure(SaveData.TreeState state)
    {
        _growthMs = Math.Max(state.GrowthMs, 1L);

        int count = Mathf.Clamp(state.Slots, 1, MaxSlots);
        _timers = new long[count];
        _slots = new TreeSlot[count];
        _drawnStep = new int[count];

        for (int i = 0; i < count; i++)
        {
            if (i < state.SlotTimers.Length)
            {
                _timers[i] = state.SlotTimers[i];
            }

            float angle = -Mathf.Pi / 2f + Mathf.Tau * i / count;
            TreeSlot slot = _slotScene.Instantiate<TreeSlot>();
            slot.Position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * SlotRingRadius;
            _slotRoot.AddChild(slot);

            _slots[i] = slot;
            _drawnStep[i] = -1;
            Redraw(i, flashOnRipe: false);
        }
    }

    public void Tick(double delta)
    {
        var ms = (long)(delta * 1000.0);

        for (int i = 0; i < _timers.Length; i++)
        {
            if (_timers[i] >= _growthMs)
            {
                continue;
            }

            _timers[i] = Math.Min(_timers[i] + ms, _growthMs);
            Redraw(i, flashOnRipe: true);
        }
    }

    /// <summary>
    /// 펀치가 닿은 순간의 반응. **열린 바나나가 없어도 반드시 보인다** -
    /// 빈 나무를 쳐도 반응이 있어야 한다는 것이 §2-3 의 P0 요구다.
    /// </summary>
    public void Shake()
    {
        _leaves.Restart();

        _shake?.Kill();
        _crown.Rotation = 0f;
        _shake = CreateTween();
        _shake.TweenProperty(_crown, "rotation", 0.028f, 0.05).SetTrans(Tween.TransitionType.Sine);
        _shake.TweenProperty(_crown, "rotation", -0.018f, 0.08).SetTrans(Tween.TransitionType.Sine);
        _shake.TweenProperty(_crown, "rotation", 0.0f, 0.12).SetTrans(Tween.TransitionType.Sine);
    }

    /// <summary>열린 바나나가 있으면 하나 수확하고 그 슬롯을 비운다.</summary>
    /// <param name="fruitPosition">수확한 자리. 이 나무의 부모 좌표계 기준이다.</param>
    public bool TryHarvest(out Vector2 fruitPosition)
    {
        for (int i = 0; i < _timers.Length; i++)
        {
            if (_timers[i] < _growthMs)
            {
                continue;
            }

            fruitPosition = Transform * (_crown.Transform * _slots[i].Position);
            _timers[i] = 0;
            Redraw(i, flashOnRipe: false);
            return true;
        }

        fruitPosition = Vector2.Zero;
        return false;
    }

    /// <summary>디버그용. 성장을 <paramref name="ms"/> 만큼 앞당긴다.</summary>
    public void DebugAdvance(long ms)
    {
        for (int i = 0; i < _timers.Length; i++)
        {
            _timers[i] = Math.Min(_timers[i] + ms, _growthMs);
            Redraw(i, flashOnRipe: true);
        }
    }

    public Rect2 GetBounds() => _restBounds;

    private void Redraw(int i, bool flashOnRipe)
    {
        float t = (float)_timers[i] / _growthMs;

        int step = Mathf.FloorToInt(t * ProgressSteps);
        if (step == _drawnStep[i])
        {
            return;
        }

        bool justRipened = flashOnRipe && t >= 1f;
        _drawnStep[i] = step;
        _slots[i].SetProgress(t);

        if (justRipened)
        {
            _slots[i].FlashRipe();
        }
    }
}
