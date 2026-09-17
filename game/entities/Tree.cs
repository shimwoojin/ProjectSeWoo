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

    private Node2D _slotRoot;
    private Polygon2D _trunk;
    private Polygon2D _canopy;

    private TreeSlot[] _slots;
    private long[] _timers;
    private int[] _drawnStep;
    private long _growthMs = 1;

    /// <summary>
    /// 아직 타이머에 못 넣은 1ms 미만의 잔차.
    ///
    /// <c>(long)(delta * 1000)</c> 로 잘라 버리면 60fps 에서 프레임당 0.67ms 씩
    /// 새서 **성장이 약 4% 느려진다** - 8분 주기 기준 매번 20초쯤 늦는다.
    /// 방치형에서 성장 속도는 경제 그 자체라(§2-2) 눈에 안 보이는 만큼 오래 간다.
    /// </summary>
    private double _msCarry;

    public override void _Ready()
    {
        _slotRoot = GetNode<Node2D>("Slots");
        _trunk = GetNode<Polygon2D>("Trunk");
        _canopy = GetNode<Polygon2D>("Canopy");
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
            Redraw(i);
        }
    }

    public void Tick(double delta)
    {
        _msCarry += delta * 1000.0;
        var ms = (long)_msCarry;
        _msCarry -= ms;

        if (ms <= 0)
        {
            return;
        }

        for (int i = 0; i < _timers.Length; i++)
        {
            if (_timers[i] >= _growthMs)
            {
                continue;
            }

            _timers[i] = Math.Min(_timers[i] + ms, _growthMs);
            Redraw(i);
        }
    }

    /// <summary>열린 바나나가 있으면 하나 수확하고 그 슬롯을 비운다.</summary>
    public bool TryHarvest()
    {
        for (int i = 0; i < _timers.Length; i++)
        {
            if (_timers[i] < _growthMs)
            {
                continue;
            }

            _timers[i] = 0;
            Redraw(i);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 앱이 꺼져 있던 동안 자란 만큼을 한 번에 반영한다 (§2-2 "자리를 비워도 나무는 자란다").
    ///
    /// <b>슬롯마다 따로 상한에 걸린다</b> - 그래서 아무리 오래 비워도 포화(전 슬롯
    /// 열림)까지만 차고, 그 이상은 흘러넘쳐 사라진다. 경제가 시간이 아니라 슬롯 수에
    /// 묶여 있다는 §2-2 의 설계가 여기서 실행된다. <c>lastQuitUtc</c> 를 조작해도
    /// 무한 파밍이 안 되는 이유이기도 하다 (shared/Save/SaveData.cs 주석).
    /// </summary>
    /// <returns>이번 반영으로 새로 열린 바나나 수. 복귀 토스트에 쓸 값이다.</returns>
    public int AdvanceOffline(long ms)
    {
        if (ms <= 0)
        {
            return 0;
        }

        int ripened = 0;
        for (int i = 0; i < _timers.Length; i++)
        {
            bool wasRipe = _timers[i] >= _growthMs;
            _timers[i] = Math.Min(_timers[i] + ms, _growthMs);

            if (!wasRipe && _timers[i] >= _growthMs)
            {
                ripened++;
            }

            Redraw(i);
        }

        return ripened;
    }

    /// <summary>디버그용. 성장을 <paramref name="ms"/> 만큼 앞당긴다.</summary>
    public void DebugAdvance(long ms) => AdvanceOffline(ms);

    /// <summary>
    /// 지금 상태를 세이브 스키마에 써 넣는다 (§7-5).
    ///
    /// 슬롯 수까지 같이 쓰는 것은 강화(B13)로 슬롯이 늘어나면 <c>slots</c> 와
    /// <c>slotTimers</c> 길이가 어긋나면 안 되기 때문이다 - 한 곳에서 같이 쓴다.
    /// </summary>
    public void WriteTo(SaveData.TreeState state)
    {
        state.Slots = _timers.Length;
        state.GrowthMs = _growthMs;
        state.SlotTimers = (long[])_timers.Clone();
    }

    public Rect2 GetBounds() => Transform * Shapes.Bounds(_trunk).Merge(Shapes.Bounds(_canopy));

    private void Redraw(int i)
    {
        float t = (float)_timers[i] / _growthMs;

        int step = Mathf.FloorToInt(t * ProgressSteps);
        if (step == _drawnStep[i])
        {
            return;
        }

        _drawnStep[i] = step;
        _slots[i].SetProgress(t);
    }
}
