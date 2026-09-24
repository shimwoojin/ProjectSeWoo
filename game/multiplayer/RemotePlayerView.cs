using System;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Game;

/// <summary>
/// 친구 한 명의 칸 (B10, 기획서 §4-1 "입장한 유저 각자가 본인의 나무 + 원숭이 + 장착 커서로 렌더링").
/// <see cref="FriendWindows"/> 가 로비 멤버마다 작은 창 하나에 하나씩 둔다.
///
/// <b>내 나무·원숭이를 그대로 줄여 쓴다</b> (<c>Tree.tscn</c>, <c>Monkey.tscn</c>) - 친구 칸만
/// 다른 그림이면 "저게 친구 나무" 로 안 읽힌다.
///
/// <b>친구 나무에는 바나나 상태가 없다.</b> 성장 타이머는 동기화하지 않는다(§4-1, 경제는
/// 전부 로컬). 그래서 빈 나무를 두고, 친구가 치면 흔들리고 따면 바나나가 떨어지는 것으로
/// 보여 준다(B11).
///
/// <b>친구 커서 장식은 커서를 따라가지 않는다.</b> 커서 위치는 보내지 않는다 - 개인정보
/// 안내(docs/C2-PRIVACY.md §2-1)의 "커서 위치를 읽지 않는다" 가 그대로 멀티에도 걸린다.
/// 그래서 칸 안에 작은 화살표를 하나 두고 장식 3종을 거기 붙여서 보여 준다.
/// </summary>
public partial class RemotePlayerView : Node2D
{
    public const float CellWidth = 140f;
    public const float CellHeight = 200f;

    /// <summary>내 나무 대비 크기. 칸(140x200) 위쪽 140px 에 나무가 들어간다.</summary>
    private const float MiniScale = 0.4f;

    /// <summary>
    /// 한 창(200ms)에 칠 수 있는 최대 펀치 수. 입력 헬퍼의 캡(초당 10타)이면 창당 2타라
    /// 넉넉히 잡았다. 조작된 상대가 65535 를 보내도 원숭이가 폭주하지 않는다.
    /// </summary>
    private const int MaxPunchesPerWindow = 3;

    private const string TreeScene = "res://game/entities/Tree.tscn";
    private const string MonkeyScene = "res://game/entities/Monkey.tscn";
    private const string BananaScene = "res://game/effects/FallingBanana.tscn";

    /// <summary>한 창에 딴 바나나를 몇 개까지 떨어뜨릴지. 폭주 방지는 펀치와 같은 이유.</summary>
    private const int MaxBananasPerWindow = 3;

    /// <summary>
    /// 상태가 이만큼 안 오면 "연결 확인 중". 친구는 치지 않아도 1초마다 생존 신호를 보낸다
    /// (<see cref="PlayerStateSender"/>) - 5초면 네 번 빠진 것이다.
    /// </summary>
    private const double StaleSec = 5.0;

    /// <summary>이만큼 안 치면 "쉬는 중". 일하다 잠깐 손 뗀 것까지 쉰다고 하면 시끄럽다.</summary>
    private const double IdleSec = 60.0;

    /// <summary>
    /// 떨어진 바나나가 닿는 높이 - 원숭이 발치. 내 화면(GameRoot.GroundY 404, 나무 262)의
    /// 나무 기준 거리를 줄인 것이다. 필드가 아니라 프로퍼티인 이유: 정적 필드는 선언 순서대로
    /// 초기화되는데 <see cref="TreeAt"/> 이 아래에 있어서, 필드면 (0,0) 기준으로 계산된다.
    /// </summary>
    private static float GroundY => TreeAt.Y + (404f - 262f) * MiniScale;

    // 내 화면 배치(GameRoot.tscn)에서 원숭이는 나무 기준 (-70, +98) 에 있다. 같은 모양을 줄인다.
    private static readonly Vector2 TreeAt = new(CellWidth / 2f + 6f, 76f);
    private static readonly Vector2 MonkeyFromTree = new Vector2(-70f, 98f) * MiniScale;

    // 커서 장식 배치는 platform/CursorLayer 의 SlotOffset/SlotScale 을 줄인 것이다.
    private const float CursorScale = 0.55f;

    /// <summary>이름표 띠의 위쪽. 여기부터 칸 아래 끝까지 이름·레벨·도감 세 줄.</summary>
    private const float LabelsTop = 144f;
    private static readonly Vector2 CursorAt = new(CellWidth - 22f, 116f);
    private static readonly Vector2[] SlotOffset = { new(0, -24), new(12, 16), new(0, 4) };
    private static readonly float[] SlotScale = { 0.42f, 0.30f, 0.34f };
    private static readonly float[] SlotAlpha = { 1.0f, 0.6f, 1.0f };

    private Tree _tree;
    private Monkey _monkey;
    private Label _name;
    private Label _stats;
    private Label _collection;
    private readonly Sprite2D[] _slots = new Sprite2D[3];
    private readonly string[] _shownIds = new string[3];

    private long _totalKeystrokes = -1;

    /// <summary>마지막 상태·타건을 받은 시각(ms, <see cref="Time.GetTicksMsec"/>). 0 = 아직 없음.</summary>
    private ulong _lastStateMs;
    private ulong _lastTypedMs;
    private readonly RandomNumberGenerator _rng = new();
    private long _roomKeystrokes;
    private byte _collectionPercent;

    public PeerId Peer { get; private set; }

    /// <summary>만들고 바로 부른다 - Godot 이 인자 없는 생성자를 요구해서 식별자는 여기서 받는다.</summary>
    public void Init(PeerId peer) => Peer = peer;

    public override void _Ready()
    {
        _tree = GD.Load<PackedScene>(TreeScene).Instantiate<Tree>();
        _tree.Position = TreeAt;
        _tree.Scale = Vector2.One * MiniScale;
        AddChild(_tree);

        _monkey = GD.Load<PackedScene>(MonkeyScene).Instantiate<Monkey>();
        _monkey.Position = TreeAt + MonkeyFromTree;
        _monkey.Scale = Vector2.One * MiniScale;
        AddChild(_monkey);

        AddChild(BuildCursor());

        _name = MakeLabel(new Vector2(0, LabelsTop), 13);
        _stats = MakeLabel(new Vector2(0, 162), 11);
        _collection = MakeLabel(new Vector2(0, 178), 10);
        _collection.AddThemeColorOverride("font_color", new Color(0.80f, 0.84f, 0.90f));
    }

    /// <summary>
    /// 칸 창의 모양 (<see cref="ISatelliteWindow.SetShape"/>) - 위는 나무·원숭이·커서 장식을
    /// 감싸는 사각형, 아래는 이름표 띠(칸 폭 전체)를 이은 T 자. 이 안이 보이고 잡히며,
    /// 나무 양옆 빈 곳은 바탕화면으로 클릭이 통과한다. 아직 트리에 안 붙었으면 null(창 전체).
    /// </summary>
    public Vector2[] GetShape()
    {
        if (_tree == null)
        {
            return null;
        }

        // 커서 장식은 화살표 끝 기준 위로 매달리고(Hang) 아래로 깔린다 - 대략 40x48.
        var cursor = new Rect2(CursorAt + new Vector2(-18, -30), new Vector2(40, 48));
        Rect2 top = _tree.GetBounds().Merge(_monkey.GetBounds()).Merge(cursor);

        float left = Mathf.Clamp(top.Position.X, 0, CellWidth);
        float right = Mathf.Clamp(top.End.X, 0, CellWidth);
        float topY = Mathf.Clamp(top.Position.Y, 0, LabelsTop);

        return new[]
        {
            new Vector2(left, topY),
            new Vector2(right, topY),
            new Vector2(right, LabelsTop),
            new Vector2(CellWidth, LabelsTop),
            new Vector2(CellWidth, CellHeight),
            new Vector2(0, CellHeight),
            new Vector2(0, LabelsTop),
            new Vector2(left, LabelsTop),
        };
    }

    /// <summary>로비 정보(이름·로비 타수). 로비 멤버 목록이 바뀌거나 1초마다 온다.</summary>
    public void SetMember(string name, long roomKeystrokes)
    {
        // 목 셸(셸 없이 GameRoot 만 돌릴 때)의 가짜 창은 씬 트리에 없어서 _Ready 가 안 돈다.
        if (_name == null)
        {
            return;
        }

        if (_name.Text != name)
        {
            _name.Text = name;
        }

        _roomKeystrokes = roomKeystrokes;
        RefreshStats();
    }

    /// <summary>친구가 200ms 마다 보내는 상태 (A10). 장식·레벨을 맞추고 친 만큼 펀치한다.</summary>
    public void ApplyState(PlayerState state)
    {
        if (_name == null)
        {
            return;
        }

        ShowDecoration(CursorSlot.Hang, state.EquippedHang);
        ShowDecoration(CursorSlot.Trail, state.EquippedTrail);
        ShowDecoration(CursorSlot.Base, state.EquippedBase);

        _totalKeystrokes = state.TotalKeystrokes;
        _collectionPercent = state.CollectionPercent;

        ulong now = Time.GetTicksMsec();
        _lastStateMs = now;
        if (state.KeystrokesInWindow > 0 || _lastTypedMs == 0)
        {
            // 처음 받은 상태는 "방금 친 것" 으로 친다 - 들어오자마자 "쉬는 중" 으로 뜨지 않게.
            _lastTypedMs = now;
        }

        RefreshStats();

        int bananas = Math.Min((int)state.HarvestsInWindow, MaxBananasPerWindow);
        if (bananas > 0)
        {
            DropBananas(bananas);
        }

        int punches = Math.Min((int)state.KeystrokesInWindow, MaxPunchesPerWindow);
        for (int i = 0; i < punches; i++)
        {
            // 한 창에 온 타건을 창 길이(200ms) 안에 나눠 친다 - 한꺼번에 치면 한 대로 보인다.
            double delay = i * 0.2 / punches;
            if (delay <= 0)
            {
                Punch();
            }
            else
            {
                GetTree().CreateTimer(delay).Timeout += Punch;
            }
        }
    }

    private void Punch()
    {
        // 타이머가 울리기 전에 친구가 나가서 칸이 지워졌을 수 있다.
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }

        double contact = _monkey.Punch();
        GetTree().CreateTimer(contact).Timeout += () =>
        {
            if (IsInstanceValid(_tree))
            {
                _tree.Shake();
            }
        };
    }

    private void RefreshStats()
    {
        string level = _totalKeystrokes >= 0 ? $"Lv.{KeystrokeLevel.LevelFor(_totalKeystrokes)} · " : string.Empty;
        string stats = $"{level}{_roomKeystrokes:N0}타";
        if (_stats.Text != stats)
        {
            _stats.Text = stats;
        }

        // 셋째 줄은 평소엔 도감 %, 한동안 안 치면 "쉬는 중", 상태가 끊기면 "연결 확인 중".
        // 끊긴 칸은 흐리게 - 친구가 창을 닫았는지 네트워크가 끊겼는지는 이쪽에서 모른다.
        string status;
        float alpha = 1f;
        if (_lastStateMs == 0)
        {
            status = string.Empty;
        }
        else
        {
            double sinceState = (Time.GetTicksMsec() - _lastStateMs) / 1000.0;
            double sinceTyped = (Time.GetTicksMsec() - _lastTypedMs) / 1000.0;
            if (sinceState >= StaleSec)
            {
                status = "연결 확인 중…";
                alpha = 0.5f;
            }
            else if (sinceTyped >= IdleSec)
            {
                status = $"쉬는 중 · {(int)(sinceTyped / 60)}분";
                alpha = 0.8f;
            }
            else
            {
                status = $"도감 {_collectionPercent}%";
            }
        }

        if (_collection.Text != status)
        {
            _collection.Text = status;
        }

        Modulate = new Color(1f, 1f, 1f, alpha);
    }

    /// <summary>
    /// 친구가 바나나를 땄다 (B11, §4-2 "친구가 방금 바나나를 땄다"). 친구 나무에서 바나나가
    /// 떨어지고 "+N 바나나" 가 떠오른다. 친구 창 안의 일이라 메인 창을 가리지 않는다.
    /// </summary>
    private void DropBananas(int count)
    {
        PackedScene scene = GD.Load<PackedScene>(BananaScene);
        for (int i = 0; i < count; i++)
        {
            var banana = scene.Instantiate<FallingBanana>();
            banana.Position = TreeAt + new Vector2(_rng.RandfRange(-20f, 20f), -20f - i * 6f);
            banana.Scale = Vector2.One * MiniScale;
            AddChild(banana);
            banana.Drop(GroundY);
        }

        var pop = new Label
        {
            Text = $"+{count} 바나나",
            // 창 모양(GetShape) 안에서만 그려진다 - 칸 맨 위는 나무 윗부분보다 높아서 잘린다.
            // 그래서 나무 잎 한가운데에서 떠오른다.
            Position = new Vector2(0, 48),
            Size = new Vector2(CellWidth, 18),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        pop.AddThemeFontSizeOverride("font_size", 13);
        pop.AddThemeColorOverride("font_color", new Color(0.98f, 0.82f, 0.30f));
        pop.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        pop.AddThemeConstantOverride("outline_size", 4);
        AddChild(pop);

        Tween tween = pop.CreateTween();
        tween.TweenProperty(pop, "position:y", 30f, 0.9).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(pop, "modulate:a", 0f, 0.9).SetDelay(0.3);
        tween.TweenCallback(Callable.From(pop.QueueFree));
    }

    /// <summary>
    /// 장식 한 칸. <b>카탈로그에 있고 그 슬롯의 것일 때만</b> 그린다 - 전송 형식이 글자는
    /// 걸렀지만(<see cref="PlayerStateCodec"/>), 없는 ID 로 파일을 찾으러 가지 않게 한 번 더 본다.
    /// </summary>
    private void ShowDecoration(CursorSlot slot, string id)
    {
        int index = (int)slot;
        if (_shownIds[index] == id)
        {
            return;
        }

        _shownIds[index] = id;
        Sprite2D sprite = _slots[index];

        ShopCatalog.Item item = id == null ? null : ShopCatalog.Find(id);
        string path = item != null && item.Slot == slot
            ? $"res://assets/cursor/{slot.ToString().ToLowerInvariant()}/{item.Id}.png"
            : null;

        sprite.Texture = path != null && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    private Node2D BuildCursor()
    {
        var cursor = new Node2D { Position = CursorAt, Scale = Vector2.One * CursorScale };

        // 그리는 순서: Trail(맨 아래, 잔상) → Base → 화살표 → Hang(맨 위). CursorLayer 와 같다.
        foreach (CursorSlot slot in new[] { CursorSlot.Trail, CursorSlot.Base })
        {
            cursor.AddChild(MakeSlotSprite(slot));
        }

        cursor.AddChild(MakeArrow());
        cursor.AddChild(MakeSlotSprite(CursorSlot.Hang));
        return cursor;
    }

    private Sprite2D MakeSlotSprite(CursorSlot slot)
    {
        int i = (int)slot;
        var sprite = new Sprite2D
        {
            Position = SlotOffset[i],
            Scale = Vector2.One * SlotScale[i],
            Modulate = new Color(1f, 1f, 1f, SlotAlpha[i]),
        };
        _slots[i] = sprite;
        return sprite;
    }

    /// <summary>
    /// 흔한 화살표 커서. 커서 에셋이 따로 없고, 친구 "커서" 라는 것만 알아보면 된다.
    /// 끝점이 (0,0) 이라 장식 오프셋이 CursorLayer 와 같은 뜻이 된다.
    /// </summary>
    private static Node2D MakeArrow()
    {
        Vector2[] points =
        {
            new(0, 0), new(0, 17), new(4, 13), new(7, 20), new(9, 19), new(6, 12), new(12, 12),
        };

        var arrow = new Polygon2D { Polygon = points, Color = Colors.White };
        var outline = new Line2D { Width = 1.5f, DefaultColor = Colors.Black, Closed = true, Points = points };
        arrow.AddChild(outline);
        return arrow;
    }

    private Label MakeLabel(Vector2 at, int size)
    {
        var label = new Label
        {
            Position = at,
            Size = new Vector2(CellWidth, 16),
            HorizontalAlignment = HorizontalAlignment.Center,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        // StatusHud 와 같은 이유로 외곽선을 넣는다 - 뒤가 유저의 바탕화면이다.
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
        label.AddThemeConstantOverride("outline_size", 4);
        AddChild(label);
        return label;
    }
}
