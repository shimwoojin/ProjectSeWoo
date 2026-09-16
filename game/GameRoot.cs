using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Platform.Mocks;

namespace ProjectSeWoo.Game;

public partial class GameRoot : Node2D, IInteractiveArea
{
    /// <summary>디버그 키가 한 번에 앞당기는 성장 시간.</summary>
    private const long DebugGrowMs = 60_000;

    // 목 4종. 지금 읽는 곳은 _input 뿐이지만, 나머지도 부를 자리가 정해져 있어
    // 같이 들고 간다 - _cursor 는 B6(상점/장착), _shell 은 B4(안전 영역 배치),
    // _net 은 B10~B12(룸 화면)에서 쓴다. A1-CONTRACTS.md §3 의 목록과 같다.
    private MockInputSource _input;
    private MockCursorLayer _cursor;
    private MockShell _shell;
    private MockNetSession _net;

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

        _input = new MockInputSource();
        _cursor = new MockCursorLayer();
        _shell = new MockShell();
        _net = new MockNetSession();

        _input.OnKeystrokes += OnKeystrokes;

        _tree.Configure(_save.Tree);
        UpdateBananaLabel();
    }

    public override void _Process(double delta)
    {
        _input.Tick(delta);
        _tree.Tick(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        // 8분을 기다리지 않고 수확까지 확인하려고 둔 debug 키다. 셸이 쓰는 키
        // (F1~F12 / 1~4 / [ ] - = O / Esc)와 겹치지 않는 자리를 골랐다.
        // 강화 UI(B7)가 생기면 그쪽이 이 자리를 대신한다 - platform 쪽 Key2~4
        // (커서 장착 시연)와 같은 성격의 임시 키다.
        if (key.Keycode == Key.G)
        {
            _tree.DebugAdvance(DebugGrowMs);
            return;
        }

        // 테스트용: 실물 IInputSource(A4)는 포커스 없이 전역으로 받지만 목은
        // 수동으로 Feed() 해줘야 한다. 실물로 갈아끼우면 이 줄은 지운다.
        _input.Feed(1);
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

    public Rect2 GetClickableBounds() => Transform * _tree.GetBounds().Merge(_monkey.GetBounds());
}
