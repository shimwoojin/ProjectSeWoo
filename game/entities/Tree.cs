using System;
using System.Collections.Generic;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 나무 하나. 슬롯의 겉모습만 그린다 - 언제 자라고 언제 열리는지의 진실은
/// <see cref="IEconomyService.Slots"/>(서버/목)에 있다 (docs/ECONOMY-SERVER.md).
///
/// <b>2026-09-23 이전에는 이 클래스가 성장 타이머를 직접 들고 매 프레임
/// 흘렸다.</b> 바나나로 산 커서 장식을 스팀 인벤토리로 옮기고 커뮤니티 마켓
/// 거래를 노리면서, 잔액·성장 타이머의 진실이 로컬(그래서 손으로 고칠 수
/// 있는 곳)에 있으면 안 되게 됐다 - 그 결정이 이 클래스에서는 "성장을 계산하지
/// 않고 서버가 계산한 값을 그리기만 한다"로 나타난다.
/// </summary>
public partial class Tree : Node2D
{
    /// <summary>강화 상한 (§2-2, §5). <see cref="SlotAnchors"/> 도 이 수만큼 있다.</summary>
    private const int MaxSlots = 8;

    /// <summary>
    /// 슬롯 i 의 바나나 꼭지가 붙는 점 - <b><c>tree_empty.png</c> 텍스처 픽셀 좌표</b>다
    /// (그림을 열어 놓고 바로 고칠 수 있게). 채워지는 순서대로 적었다.
    ///
    /// 예전엔 납작한 타원 호(82×30)에 슬롯 수만큼 고르게 깔았는데, 바깥 슬롯은 잎
    /// 끝에, 가운데 슬롯은 줄기 중간에 걸려 엉뚱해 보였다. 실제 바나나는
    /// <c>tree_full.png</c> 처럼 **잎이 모이는 왕관 바로 아래, 줄기 양옆**에 매달린다.
    /// 인덱스로 자리가 고정되니, 강화로 슬롯이 늘어도 이미 열린 바나나는 안 움직인다.
    /// </summary>
    private static readonly Vector2[] SlotAnchors =
    {
        new(160, 268), // ① 줄기 왼쪽 - tree_full 의 왼쪽 송이 자리
        new(375, 268), // ② 줄기 오른쪽
        new(267, 205), // ③ 왕관 가운데 (줄기 꼭대기 앞)
        new(75, 240),  // ④ 바깥 왼쪽 잎 아래
        new(460, 240), // ⑤ 바깥 오른쪽 잎 아래
        new(190, 165), // ⑥ 왕관 안쪽 왼쪽
        new(345, 165), // ⑦ 왕관 안쪽 오른쪽
        new(267, 110), // ⑧ 왕관 위쪽 가운데
    };

    /// <summary>
    /// 성장 표시를 몇 단으로 끊어 갱신할지. 8분 주기면 약 7초에 한 번 다시 그린다 -
    /// 매 프레임 색/크기를 건드리면 상주 앱 부하 기준(§7-3)에 맞지 않는다.
    /// </summary>
    private const int ProgressSteps = 64;

    /// <summary>
    /// 다 익은 바나나 한 송이를 떨어뜨리는 데 필요한 타격 수. 모자란 타격마다
    /// 익은 순간의 반짝임(<see cref="TreeSlot.FlashRipe"/>)을 다시 터뜨려 "맞고
    /// 있다"는 걸 보여 준다.
    ///
    /// <b>클라이언트에서만 센다.</b> 서버는 슬롯이 익었는지만 보고 수확을 받는다 -
    /// 이 값은 "언제 수확을 요청하는가"를 늦출 뿐 재화 규칙을 바꾸지 않는다.
    /// 세이브에도 없어서 껐다 켜면 0부터 다시 센다.
    /// </summary>
    public const int HitsToDrop = 10;

    [Export] private PackedScene _slotScene;

    private Node2D _sway;
    private Node2D _slotRoot;
    private Sprite2D _body;
    /// <summary>잎 묶음을 동시에 몇 개까지 띄울지 (<see cref="DropLeaves"/>). 한 묶음이 1장(LeafParticle.tscn 의 amount)이라 최대 4장.</summary>
    private const int MaxLeafBursts = 4;

    /// <summary>복제해서 쏘는 틀. 이 노드 자체는 쏘지 않는다.</summary>
    private CpuParticles2D _leaves;

    /// <summary>지금 떨어지고 있는 잎 묶음 수.</summary>
    private int _leafBursts;

    private Tween _shake;

    private TreeSlot[] _slots = Array.Empty<TreeSlot>();
    private int[] _drawnStep = Array.Empty<int>();

    /// <summary>지난 <see cref="SyncSlots"/> 호출에서 각 슬롯이 익어 있었는가.
    /// "방금 익었다"(플래시 연출감)를 판정하려면 직전 상태와 비교해야 한다 -
    /// 예전에는 <c>Tick</c>이 매 프레임 값을 올리면서 그 경계를 직접 알았지만,
    /// 이제는 스냅샷만 받으므로 직접 기억해 둔다.</summary>
    private bool[] _wasReady = Array.Empty<bool>();

    /// <summary>마지막으로 그린 황금 여부. 진행 단계가 같아도 이게 바뀌면 다시 그린다 (B13).</summary>
    private bool[] _drawnGolden = Array.Empty<bool>();

    /// <summary>익은 슬롯마다 지금까지 맞은 횟수 (<see cref="HitsToDrop"/>). 안 익은 슬롯은 0.</summary>
    private int[] _hits = Array.Empty<int>();

    /// <summary>
    /// 흔들기 전의 클릭 영역. 흔들리는 동안 다시 재지 않는다 - 매 프레임 값이 바뀌면
    /// 그만큼 WindowSetMousePassthrough 쓰기가 늘어난다 (§7-3).
    /// </summary>
    private Rect2 _restBounds;

    public override void _Ready()
    {
        _sway = GetNode<Node2D>("Sway");
        _slotRoot = GetNode<Node2D>("Sway/Slots");
        _body = GetNode<Sprite2D>("Sway/Body");
        _leaves = GetNode<CpuParticles2D>("Sway/Leaves");

        // 나무가 한 장이라 밑동과 잎을 따로 잴 것이 없어졌다 (B4).
        _restBounds = Transform * (_sway.Transform * Shapes.Bounds(_body));
    }

    /// <summary>
    /// 서버(또는 목)가 계산한 슬롯 상태로 시각을 맞춘다. 매 프레임 불러도 되는
    /// 순수 반영이다 - 실제 다시 그리기는 성장 단(<see cref="ProgressSteps"/>)이
    /// 바뀔 때만 일어난다(<see cref="Redraw"/> 내부).
    ///
    /// <b>슬롯 개수가 바뀌면 자식 노드를 다시 짠다.</b> 예전에는 <c>Configure</c>
    /// 한 번으로 슬롯 수가 고정이었지만, 이제 그 진실이 서버에 있어서(강화로)
    /// 언제든 바뀔 수 있다 - <see cref="Rebuild"/>.
    /// </summary>
    public void SyncSlots(IReadOnlyList<SlotState> slots)
    {
        if (slots.Count != _slots.Length)
        {
            Rebuild(slots.Count);
        }

        for (int i = 0; i < slots.Count; i++)
        {
            SlotState s = slots[i];
            bool ready = s.Ready;
            float t = s.GrowthMs <= 0 ? 0f : Mathf.Clamp((float)s.ElapsedMs / s.GrowthMs, 0f, 1f);

            Redraw(i, t, s.Golden, flashOnRipe: ready && !_wasReady[i]);
            _wasReady[i] = ready;

            if (!ready)
            {
                _hits[i] = 0;
            }
        }
    }

    /// <summary>
    /// 슬롯 자식 노드를 <paramref name="count"/> 개로 다시 짠다. 씬 트리 변경은
    /// 슬롯 수가 실제로 바뀔 때만 일어나므로(<see cref="SyncSlots"/>가 먼저
    /// 걸러낸다), 강화 없이 매 프레임 도는 동안은 호출되지 않는다.
    /// </summary>
    private void Rebuild(int count)
    {
        count = Mathf.Clamp(count, 1, MaxSlots);

        foreach (TreeSlot slot in _slots)
        {
            slot.QueueFree();
        }

        _slots = new TreeSlot[count];
        _drawnStep = new int[count];
        _wasReady = new bool[count];
        _drawnGolden = new bool[count];
        _hits = new int[count];

        for (int i = 0; i < count; i++)
        {
            TreeSlot slot = _slotScene.Instantiate<TreeSlot>();
            slot.Position = AnchorToSlotRoot(SlotAnchors[i]);
            _slotRoot.AddChild(slot);

            _slots[i] = slot;
            _drawnStep[i] = -1;
        }
    }

    /// <summary>
    /// 펀치가 닿은 순간의 반응. **열린 바나나가 없어도 반드시 보인다** -
    /// 빈 나무를 쳐도 반응이 있어야 한다는 것이 §2-3 의 P0 요구다.
    /// </summary>
    public void Shake()
    {
        DropLeaves();

        _shake?.Kill();
        _sway.Rotation = 0f;
        _shake = CreateTween();
        _shake.TweenProperty(_sway, "rotation", 0.028f, 0.05).SetTrans(Tween.TransitionType.Sine);
        _shake.TweenProperty(_sway, "rotation", -0.018f, 0.08).SetTrans(Tween.TransitionType.Sine);
        _shake.TweenProperty(_sway, "rotation", 0.0f, 0.12).SetTrans(Tween.TransitionType.Sine);
    }

    /// <summary>
    /// 잎 한 묶음을 떨어뜨린다. <c>Sway/Leaves</c> 는 틀로만 쓰고 매번 복제본을 쏜다.
    ///
    /// <b>예전엔 그 노드 하나를 <c>Restart()</c> 했다</b> - 그러면 떨어지던 잎이 전부
    /// 지워지고 위에서 새로 생겨서, 빠르게 치면 잎이 끝까지 못 떨어지고 계속 되감겼다.
    /// 복제본은 <c>local_coords</c> 가 꺼져 있어 나무가 흔들려도 끌려가지 않고,
    /// one-shot 이 끝나면(<c>Finished</c>) 스스로 사라진다.
    ///
    /// 동시에 <see cref="MaxLeafBursts"/> 묶음까지만 띄운다. 넘으면 **떨어지던 잎을
    /// 지우지 않고 이번 것을 건너뛴다** - 연타 중엔 어차피 화면에 잎이 차 있다.
    /// </summary>
    private void DropLeaves()
    {
        if (_leafBursts >= MaxLeafBursts)
        {
            return;
        }

        var burst = (CpuParticles2D)_leaves.Duplicate();
        _sway.AddChild(burst);
        _leafBursts++;
        burst.Finished += () =>
        {
            _leafBursts--;
            burst.QueueFree();
        };
        burst.Emitting = true;
    }

    /// <summary>
    /// 슬롯 <paramref name="index"/> 의 화면 좌표 (낙하 연출의 시작점). 이 나무의
    /// 부모 좌표계 기준이다 - 호출부(<see cref="GameRoot"/>)가 떨어지는 바나나를
    /// 여기서 인스턴스한다.
    /// </summary>
    public Vector2 PositionOf(int index) =>
        Transform * (_sway.Transform * (_slotRoot.Position + _slots[index].Position
                                        + _slots[index].FruitCenter));

    /// <summary>
    /// 텍스처 픽셀 좌표 → <c>Sway/Slots</c> 로컬. <c>Body</c> 는 가운데 정렬
    /// 스프라이트라 텍스처 중심이 원점이고, 배율·위치는 씬 값을 그대로 따른다.
    /// </summary>
    private Vector2 AnchorToSlotRoot(Vector2 texturePx) =>
        _body.Transform * (texturePx - _body.Texture.GetSize() / 2f) - _slotRoot.Position;

    public Rect2 GetBounds() => _restBounds;

    /// <summary>
    /// 익은 슬롯 <paramref name="index"/> 를 한 대 친다. <see cref="HitsToDrop"/> 번째
    /// 타격이면 카운트를 비우고 true - 호출부가 수확을 요청하고 떨어뜨린다.
    /// 연출은 여기서 걸지 않는다(<see cref="FlashHit"/>) - 펀치가 닿는 시점에
    /// 맞추는 것은 호출부의 몫이다.
    /// </summary>
    public bool Hit(int index)
    {
        if (++_hits[index] < HitsToDrop)
        {
            return false;
        }

        _hits[index] = 0;
        return true;
    }

    /// <summary>
    /// 맞은 바나나의 반짝임. 타이머로 늦게 불리므로 그 사이 슬롯이 다시 지어졌을
    /// 수 있다(<see cref="Rebuild"/>) - 범위를 벗어나면 조용히 넘긴다.
    /// </summary>
    public void FlashHit(int index)
    {
        if (index < _slots.Length && _wasReady[index])
        {
            _slots[index].FlashRipe();
        }
    }

    private void Redraw(int i, float t, bool golden, bool flashOnRipe)
    {
        int step = Mathf.FloorToInt(t * ProgressSteps);
        if (step == _drawnStep[i] && golden == _drawnGolden[i])
        {
            return;
        }

        _drawnStep[i] = step;
        _drawnGolden[i] = golden;
        _slots[i].SetProgress(t, golden);

        if (flashOnRipe)
        {
            _slots[i].FlashRipe();
        }
    }
}
