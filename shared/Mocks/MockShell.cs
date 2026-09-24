using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Shared.Mocks;

/// <summary>
/// <see cref="IShell"/> 목 구현. 값을 기억만 하고 창은 건드리지 않는다.
///
/// 실물은 <c>platform/OverlayShell.cs</c>다 (A3 완료, 2026-09-15).
/// </summary>
public sealed class MockShell : IShell
{
    /// <summary>시험용 - <see cref="ToggleOptions"/> 를 몇 번 불렀나.</summary>
    public int OptionsToggleCount { get; private set; }

    public void ToggleOptions()
    {
        OptionsToggleCount++;
        GD.Print("[mock-shell] 옵션 창 토글 (목이라 창은 없다)");
    }

    public float Scale { get; private set; } = 1.0f;

    public float Opacity { get; private set; } = 1.0f;

    public bool ClickThrough { get; private set; } = true;

    /// <summary>
    /// 목이 돌려줄 배치 가능 영역. 기본값은 현재 화면이지만, **멀티모니터 좌표를
    /// 시험하려면 음수 원점을 넣어 본다** — 실제로 이 프로젝트의 개발 PC 는
    /// 모니터가 3대고 하나는 X 가 음수다. 게임 레이어가 (0,0) 을 가정하면 거기서 깨진다.
    /// </summary>
    public Rect2I SafeArea { get; set; } = new(Vector2I.Zero, new Vector2I(1920, 1080));

    public void SetScale(float s) => Scale = s;

    public void SetOpacity(float a) => Opacity = a;

    public void SetClickThrough(bool on) => ClickThrough = on;

    public Rect2I GetSafeArea() => SafeArea;

    /// <summary>마지막으로 요청된 확장 높이. 목은 창이 없어서 기록만 하고 늘 아래로 답한다.</summary>
    public int ExtraHeight { get; private set; }

    public WindowExtension ExtendWindow(int height, int belowOverlap = 0)
    {
        ExtraHeight = System.Math.Max(0, height);
        return ExtraHeight == 0 ? WindowExtension.None : WindowExtension.Below;
    }

    /// <summary>목은 창을 끌 일이 없어서 방향이 바뀌지 않는다.</summary>
    public event System.Action<WindowExtension> WindowExtensionChanged
    {
        add { }
        remove { }
    }
}
