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

    /// <summary>
    /// 바나나 높이(px)와, 꼭지를 커서 끝에서 얼마나 떨어뜨려 붙이는가. 화살표 아래로 뺀다 - 예전 (6, 8) 은 150% 배율
    /// 모니터의 화살표(약 28px)가 바나나를 덮었다 (2026-09-26 스크린샷 확대에서 발견). 200% 화살표(약 38px)는 여전히
    /// 꼭지 끝에 조금 걸친다.
    /// </summary>
    private const float BananaHeight = 36f;
    private static readonly Vector2 StemFromTip = new(10, 24);

    private DecoView _deco;
    private Sprite2D _banana;
    private MonkeyRig _rig;
    private readonly string[] _equipped = new string[3];

    /// <summary>커서가 없는 곳(친구 창) — 원숭이가 흔들리지 않고 매달림·반응·졸기만 한다.</summary>
    public bool Still { get; set; }

    public bool IsFrozen => _rig?.IsFrozen ?? false;

    public string RigState => _rig == null ? "-" : $"{_rig.State}{(_rig.IsFrozen ? " frozen" : "")}";

    public string EquippedIn(CursorSlot slot) => _equipped[(int)slot];

    public override void _Ready()
    {
        _deco = new DecoView { Name = "Deco" };
        AddChild(_deco);
        _banana = new Sprite2D { Name = "Banana", Centered = false, Visible = false };
        AddChild(_banana);
        _rig = new MonkeyRig { Name = "Monkey", Visible = false, Still = Still };
        AddChild(_rig);

        float k = BananaHeight / (BananaGrip.Y - BananaStem.Y + 20f);
        _banana.Scale = Vector2.One * k;
        _banana.Position = StemFromTip - BananaStem * k;
        _rig.Position = _banana.Position + BananaGrip * k;

        // 트리에 들어오기 전에 끼운 것 (친구 창은 만들자마자 끼운다)
        for (int i = 0; i < _equipped.Length; i++)
        {
            Apply((CursorSlot)i);
        }
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
