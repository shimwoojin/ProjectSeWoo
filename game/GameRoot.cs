using Godot;
using ProjectSeWoo.Shared;
using ProjectSeWoo.Platform.Mocks;

namespace ProjectSeWoo.Game;

public partial class GameRoot : Node2D, IInteractiveArea
{
    private MockInputSource _input;
    private MockCursorLayer _cursor;
    private MockShell _shell;
    private MockNetSession _net;
    private Sprite2D _placeholder;

    public override void _Ready()
    {
        AddToGroup(SceneGroups.GameRoot);

        _placeholder = GetNode<Sprite2D>("Placeholder");

        _input = new MockInputSource();
        _cursor = new MockCursorLayer();
        _shell = new MockShell();
        _net = new MockNetSession();

        _input.OnKeystrokes += OnKeystrokes;

        // TODO(B1~B5): 나무/원숭이 씬 instance, 세이브 로드
    }

    public override void _Process(double delta)
    {
        _input.Tick(delta);
        // TODO(B1): 나무 슬롯 성장 타이머
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 테스트용: 실물 IInputSource(A4)는 포커스 없이 전역 키 입력을 받지만
        // MockInputSource는 수동으로 Feed() 해줘야 한다. 에디터에서 직접
        // 키를 눌러 확인할 수 있도록 임시로 연결. B1 코어 루프가 붙으면
        // 이 메서드는 지워도 된다 (§0, IInputSource 계약 참고).
        if (@event is InputEventKey { Pressed: true, Echo: false })
        {
            _input.Feed(1);
        }
    }

    private void OnKeystrokes(int count)
    {
        GD.Print($"[GameRoot] keystroke feed: {count}");
        // TODO(B1/B2): 원숭이 펀치 트리거, 빈 나무면 헛펀치
    }

    public Rect2 GetClickableBounds()
    {
        Vector2 size = _placeholder.Texture.GetSize() * _placeholder.Scale;
        return new Rect2(_placeholder.Position - size * 0.5f, size);
    }
}
