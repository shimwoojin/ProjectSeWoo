using Godot;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 커서에 달리는 것 한 벌 — 장식(<see cref="DecoView"/>) → 바나나 → 원숭이(<see cref="MonkeyRig"/>) (docs/B17-CURSOR-REWORK.md §4-1).
/// <b>원점이 커서 끝이다.</b> 내 커서 창(<c>platform/CursorLayer</c>)과 친구 창(<c>game/multiplayer/RemotePlayerView</c>)이
/// 같은 조립을 쓴다 — 바나나 자리와 원숭이가 잡는 점이 두 곳에서 갈라지지 않게.
/// </summary>
public partial class CursorOrnament : Node2D
{
    /// <summary>바나나 그림(banana.png) 안의 꼭지 윗점과 원숭이가 잡는 점, 픽셀. 모든 변형이 같은 실루엣이다.</summary>
    private static readonly Vector2 BananaStem = new(102, 6), BananaGrip = new(95, 190);

    /// <summary>바나나 높이(px).</summary>
    private const float BananaHeight = 36f;

    /// <summary>
    /// 흔한 화살표 커서의 <b>꼬리 끝</b> - 끝점(0,0) 기준, 100% 배율(32px 커서) px. 꼬리 아래 모서리 (7,20)·(9,19) 의 가운데다.
    /// 친구 칸이 그리는 화살표(<c>RemotePlayerView.MakeArrow</c>)도 이 모양이다.
    /// </summary>
    public static readonly Vector2 ArrowTail = new(8, 19.5f);

    private DecoView _deco;
    private Vector2 _stemFromTip = new(10, 24);
    private Sprite2D _banana;
    private MonkeyRig _rig;
    private readonly string[] _equipped = new string[3];

    /// <summary>커서가 없는 곳(친구 창) — 원숭이가 흔들리지 않고 매달림·반응·졸기만 한다.</summary>
    public bool Still { get; set; }

    public bool IsFrozen => _rig?.IsFrozen ?? false;

    public string RigState => _rig == null ? "-" : $"{_rig.State}{(_rig.IsFrozen ? " frozen" : "")}";

    public string EquippedIn(CursorSlot slot) => _equipped[(int)slot];

    /// <summary>
    /// 바나나 꼭지 윗점의 자리 - 이 노드 좌표(원점 = 커서 끝). <b>화살표 꼬리 끝에 닿게 둔다</b> (2026-09-30): 내 커서는
    /// 화면에 보이는 시스템 화살표 크기에서 잰다(<c>platform/CursorLayer</c>), 친구 칸은 그린 화살표에서 잰다.
    ///
    /// 예전엔 모든 곳이 고정 (10, 24) 였다 (2026-09-26, 150% 모니터 화살표를 비키려고). 그 값은 "내 커서 크기" 를 키우면 틈도
    /// 같이 커져서 바나나가 커서에서 멀어졌다 - 시스템 화살표는 그 옵션으로 안 커진다. 캡슐 아트 원숭이(부르는 곳 없음)만
    /// 그 기본값을 그대로 쓴다.
    /// </summary>
    public Vector2 StemFromTip
    {
        get => _stemFromTip;
        set
        {
            _stemFromTip = value;
            Layout();
        }
    }

    public override void _Ready()
    {
        _deco = new DecoView { Name = "Deco" };
        AddChild(_deco);
        _banana = new Sprite2D { Name = "Banana", Centered = false, Visible = false };
        AddChild(_banana);
        _rig = new MonkeyRig { Name = "Monkey", Visible = false, Still = Still };
        AddChild(_rig);

        Layout();

        // 트리에 들어오기 전에 끼운 것 (친구 창은 만들자마자 끼운다)
        for (int i = 0; i < _equipped.Length; i++)
        {
            Apply((CursorSlot)i);
        }
    }

    /// <summary>바나나를 꼭지가 <see cref="StemFromTip"/> 에 오게, 원숭이를 바나나 잡는 점에 둔다.</summary>
    private void Layout()
    {
        if (_banana == null)
        {
            return;
        }

        float k = BananaHeight / (BananaGrip.Y - BananaStem.Y + 20f);
        _banana.Scale = Vector2.One * k;
        _banana.Position = _stemFromTip - BananaStem * k;
        _rig.Position = _banana.Position + BananaGrip * k;
    }

    /// <summary>
    /// 칸에 아이템을 끼운다. null 이면 비운다. 그림은 규칙으로 찾는다 (<see cref="ItemManifest.AssetDir"/>) — 원숭이는
    /// 스킨, 바나나는 <c>banana.png</c>, 장식은 <c>icon.png</c> + 종류. 없는 그림은 그리지 않는다.
    /// </summary>
    public void Equip(CursorSlot slot, string id)
    {
        if (_equipped[(int)slot] == id && _rig != null)
        {
            return;
        }

        _equipped[(int)slot] = id;
        if (_rig != null)
        {
            Apply(slot);
        }
    }

    private void Apply(CursorSlot slot)
    {
        string id = _equipped[(int)slot];
        switch (slot)
        {
            case CursorSlot.Monkey:
                MonkeySkin skin = MonkeySkin.Load(id);
                _rig.SetSkin(skin);
                _rig.Visible = skin != null;
                break;

            case CursorSlot.Banana:
                string path = id == null ? null : ItemManifest.AssetDir(CursorSlot.Banana, id) + "banana.png";
                Texture2D texture = path != null && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
                _banana.Texture = texture;
                _banana.Visible = texture != null;
                break;

            case CursorSlot.Deco:
                string kind = null;
                foreach (ItemManifest.Entry e in ItemManifest.Items)
                {
                    if (e.Id == id && e.Slot == CursorSlot.Deco)
                    {
                        kind = e.DecoKind;
                    }
                }

                _deco.SetItem(kind == null ? null : id, kind);
                break;
        }
    }

    /// <summary>커서의 화면 좌표와, 이 노드 원점(커서 끝)의 화면 좌표. 친구 창은 부르지 않는다.</summary>
    public void Follow(Vector2 cursorScreen, Vector2 originScreen)
    {
        _rig?.Follow(cursorScreen);
        _deco?.Follow(cursorScreen, originScreen);
    }

    /// <summary>타건·클릭 (횟수만).</summary>
    public void Keystrokes(int count) => _rig?.Keystrokes(count);

    public void Tick(double delta)
    {
        if (_rig == null)
        {
            return;
        }

        _rig.Still = Still;
        _rig.Tick(delta);
        _deco.Frozen = _rig.IsFrozen;
        _deco.Tick(delta);
    }
}
