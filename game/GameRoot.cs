using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Shared.Mocks;

namespace ProjectSeWoo.Game;

public partial class GameRoot : Node2D, IInteractiveArea, IPlatformConsumer
{
    /// <summary>디버그 키가 한 번에 앞당기는 성장 시간.</summary>
    private const long DebugGrowMs = 60_000;

    /// <summary>
    /// 플랫폼 실물 묶음. 셸이 <see cref="AttachPlatform"/> 으로 넘긴다.
    /// 지금 읽는 곳은 <see cref="IPlatformServices.Input"/> 뿐이지만 나머지도
    /// 부를 자리가 정해져 있다 - <c>Cursor</c> 는 B6(상점/장착), <c>Shell</c> 은
    /// B4(안전 영역 배치), <c>Achievements</c> 는 B3/B7(마일스톤·도감 100%),
    /// <c>Net</c> 은 B10~B12(룸 화면)다.
    /// </summary>
    private IPlatformServices _platform;

    /// <summary>
    /// <b>단독 실행일 때만</b> 채워진다 - 에디터에서 <c>Shell.tscn</c> 없이 이 씬만
    /// 열어 돌리는 경우다. 실물이 왔으면 널로 남고, 널인지 아닌지가 곧
    /// "지금 목으로 도는가"의 답이다. 목에만 있는 <c>Tick</c>/<c>Feed</c> 를
    /// 부를 자격도 여기에 묶여 있다.
    /// </summary>
    private MockPlatformServices _standalone;

    private Tree _tree;
    private Monkey _monkey;
    private Label _bananas;

    // TODO(B5): SaveIO 로 왕복시킨다. 지금은 스키마 기본값(슬롯 3개, 성장 8분)만 읽는다.
    private readonly SaveData _save = new();

    public override void _Ready()
    {
        AddToGroup(SceneGroups.GameRoot);

        _tree = GetNode<Tree>("Tree");
        _monkey = GetNode<Monkey>("Monkey");
        _bananas = GetNode<Label>("Bananas");

        _tree.Configure(_save.Tree);
        UpdateBananaLabel();

        // **여기서 입력을 구독하면 안 된다.** Godot 은 자식의 _Ready 를 부모보다
        // 먼저 부르는데 실물을 만드는 것은 부모(OverlayShell)라, 이 시점에는
        // _platform 이 아직 비어 있다 (shared/Contracts/IPlatformServices.cs 의 ⚠).
        //
        // 프레임 끝에 한 번 확인해서 그래도 비어 있으면 셸이 없는 실행이다 -
        // 그때만 목으로 돈다.
        Callable.From(FallBackToMocks).CallDeferred();
    }

    /// <summary>
    /// <see cref="IPlatformConsumer.AttachPlatform"/>. 실물이 필요한 배선은 전부 여기서 한다.
    /// </summary>
    public void AttachPlatform(IPlatformServices platform)
    {
        if (_platform != null)
        {
            GD.PushWarning("[game] AttachPlatform 이 두 번 왔다 - 먼저 온 것을 유지한다");
            return;
        }

        _platform = platform;
        _platform.Input.OnKeystrokes += OnKeystrokes;
    }

    /// <summary>
    /// 셸이 실물을 안 넘겼으면 목으로 돈다. <see cref="_Ready"/> 가 프레임 끝으로
    /// 미뤄 두고 부른다 - 그때는 부모의 <c>_Ready</c> 까지 전부 끝나 있다.
    /// </summary>
    private void FallBackToMocks()
    {
        if (_platform != null)
        {
            return;
        }

        GD.Print("[game] 플랫폼 미연결 - 목으로 돈다 (Shell.tscn 없이 단독 실행)");
        _standalone = new MockPlatformServices();
        AttachPlatform(_standalone);
    }

    public override void _Process(double delta)
    {
        // 실물의 폴링은 셸이 돌린다. 목일 때만 우리가 굴린다.
        _standalone?.Tick(delta);

        _tree.Tick(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        // 8분을 기다리지 않고 수확까지 확인하려고 둔 debug 키다. 셸이 쓰는 키
        // (F1~F12 / 1~4 / [ ] - = O H / Esc)와 겹치지 않는 자리를 골랐다.
        // 강화 UI(B7)가 생기면 그쪽이 이 자리를 대신한다.
        if (key.Keycode == Key.G)
        {
            _tree.DebugAdvance(DebugGrowMs);
            return;
        }

        // 목은 수동으로 먹여야 타건이 생긴다. **실물일 때는 부르지 않는다** -
        // A4 는 포커스 없이 전역으로 이미 세고 있어서, 여기서 또 먹이면 창에
        // 포커스가 있는 동안만 두 배로 수확된다.
        _standalone?.Feed(1);
    }

    private void OnKeystrokes(int count)
    {
        _monkey.Punch();

        // 수확을 애니메이션 타이밍이 아니라 입력에 직접 건다. §2-3 검토 노트의
        // "키 입력과 애니메이션을 1:1 고정 대응시키지 말 것"이 이 뜻이고,
        // 펀치 연출이 끊기거나 겹쳐도 수확 개수가 흔들리지 않는다.
        int harvested = 0;
        while (harvested < count && _tree.TryHarvest())
        {
            harvested++;
        }

        if (harvested == 0)
        {
            return;
        }

        _save.Bananas += harvested;   // §2-2: 펀치 1회당 열린 바나나 1개
        UpdateBananaLabel();
    }

    private void UpdateBananaLabel()
    {
        _bananas.Text = $"bananas {_save.Bananas}";
    }

    public override void _ExitTree()
    {
        // 실물(HelperInputSource)은 이 노드보다 오래 살 수 있다 - 셸이 들고 있고
        // 셸은 _ExitTree 가 더 늦게 돈다. 구독을 남긴 채 나가지 않는다.
        if (_platform != null)
        {
            _platform.Input.OnKeystrokes -= OnKeystrokes;
        }
    }

    public Rect2 GetClickableBounds() => Transform * _tree.GetBounds().Merge(_monkey.GetBounds());
}
