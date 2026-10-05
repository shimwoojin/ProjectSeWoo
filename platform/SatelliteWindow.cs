using System;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="ISatelliteWindow"/> 실물 - 친구 칸 하나를 담는 작은 OS 창 (B10).
///
/// 창 속성은 커서 창(<see cref="CursorLayer"/>)과 같다 - 테두리 없음, 항상 위, 투명,
/// 포커스 안 뺏음. 다른 점은 <b>일부만 클릭을 받는다</b>는 것: 커서 창은 전부 통과시키지만
/// 친구 칸은 원숭이·나무를 잡아 끌 수 있어야 해서, 메인 창과 같이 <b>커서가 모양 위일 때만 통과를 끈다</b>
/// (<see cref="ClickThrough"/>, 2026-09-30). 예전엔 창 모양(<c>WindowSetMousePassthrough</c>)이라 모양 밖이 그려지지도
/// 않아서 이름표 띠까지 모양에 넣어야 했다.
///
/// 끌기는 셸의 메인 창 끌기와 같은 방식이다 - 매 틱 마우스 버튼과 화면 좌표를 보고,
/// 끄는 동안은 창 전체가 마우스를 받게 한다(영역을 벗어나는 순간 끊기지 않게).
/// 틱은 셸이 돌린다(<see cref="OverlayShell"/>).
/// </summary>
public sealed class SatelliteWindow : ISatelliteWindow
{
    private readonly Window _win;
    private readonly Vector2I _contentSize;
    private readonly Action<SatelliteWindow> _onClosed;

    /// <summary>창 모양(콘텐츠 좌표). null 이면 창 전체.</summary>
    private Vector2[] _shape;
    private float _scale = 1f;
    private bool _dragging;
    private bool _wasPressed;
    private Vector2I _dragOffset;
    private Vector2I _dragStart;
    private bool _closed;
    private IntPtr _hwnd;
    private bool _clickThroughFailed;

    public SatelliteWindow(Node host, string name, Vector2I contentSize, Vector2I position, float scale, Action<SatelliteWindow> onClosed)
    {
        _contentSize = contentSize;
        _onClosed = onClosed;

        _win = new Window
        {
            Name = name,
            Borderless = true,
            AlwaysOnTop = true,
            Transparent = true,
            TransparentBg = true,
            Unfocusable = true,
            Unresizable = true,
            Visible = false,
        };

        Content = new Node2D { Name = "Content" };
        _win.AddChild(Content);
        host.AddChild(_win);

        SetScale(scale);
        _win.Position = position;
    }

    public event Action<Vector2I> Moved;

    public Node2D Content { get; }

    public Vector2I ScreenPosition => _win.Position;

    public void SetShape(Vector2[] outline) => _shape = outline is { Length: >= 3 } ? outline : null;

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _win.QueueFree();
        _onClosed?.Invoke(this);
    }

    // ------------------------------------------------------------------ 셸이 부른다

    /// <summary>옵션 "친구 칸 크기" (메인 창과 따로, 2026-09-30). 창 크기와 내용을 같이 키운다.</summary>
    public void SetScale(float scale)
    {
        _scale = scale;
        Content.Scale = Vector2.One * scale;
        _win.Size = new Vector2I(
            Mathf.RoundToInt(_contentSize.X * scale),
            Mathf.RoundToInt(_contentSize.Y * scale));
    }

    /// <summary>옵션 투명도 (메인 창과 같은 값).</summary>
    public void SetOpacity(float opacity) => Content.Modulate = new Color(1f, 1f, 1f, opacity);

    /// <summary>
    /// 보이기/숨기기 - 트레이 "숨기기" 와 전체화면 자동 숨김을 메인 창과 같이 따른다.
    /// 서브 창은 메인 창과 달리 <c>Visible</c> 이 그대로 먹는다 (OverlayShell.Visibility 주석).
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (_win.Visible == visible)
        {
            return;
        }

        _win.Visible = visible;
    }

    /// <summary>매 프레임. 끌기와, 커서가 모양 위인지에 따라 클릭 통과를 켜고 끈다.</summary>
    public void Tick()
    {
        if (_closed || !_win.Visible)
        {
            return;
        }

        bool pressed = Input.IsMouseButtonPressed(MouseButton.Left);
        Vector2I mouse = DisplayServer.MouseGetPosition();
        bool onShape = IsOnShape(mouse);

        if (_dragging)
        {
            if (pressed)
            {
                _win.Position = mouse - _dragOffset;
            }
            else
            {
                EndDrag();
            }
        }
        else if (pressed && !_wasPressed && onShape)
        {
            // 누른 순간이 잡는 영역 안일 때만 - 다른 데서 누른 채 들어온 것은 끌기가 아니다.
            _dragging = true;
            _dragOffset = mouse - _win.Position;
            _dragStart = _win.Position;
        }

        _wasPressed = pressed;

        // 끄는 동안은 창 전체가 받는다 - 커서가 모양을 벗어나도 끌기가 안 끊기게.
        if (EnsureHwnd())
        {
            ClickThrough.SetPassThrough(_hwnd, !(_dragging || onShape));
        }
    }

    private void EndDrag()
    {
        _dragging = false;

        if (_win.Position != _dragStart)
        {
            Moved?.Invoke(_win.Position);
        }
    }

    /// <summary>
    /// HWND 를 잡고 LAYERED 를 붙인다. OS 창은 처음 보일 때 생기므로 틱에서 늦게 잡는다. 못 잡으면 창 전체가 클릭을 받는
    /// 채로 둔다(경고 한 번).
    /// </summary>
    private bool EnsureHwnd()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return true;
        }

        if (_clickThroughFailed || _win.GetWindowId() == DisplayServer.InvalidWindowId)
        {
            return false;
        }

        IntPtr hwnd = ClickThrough.Hwnd(_win.GetWindowId());
        if (hwnd == IntPtr.Zero || !ClickThrough.Prepare(hwnd))
        {
            _clickThroughFailed = true;
            GD.PushWarning($"[satellite] {_win.Name} 에 클릭 통과를 걸 수 없다 - 창 전체가 클릭을 받는다");
            return false;
        }

        _hwnd = hwnd;
        return true;
    }

    /// <summary>화면 좌표의 점이 창 모양 안인가. 모양이 없으면 창 전체.</summary>
    private bool IsOnShape(Vector2I screen)
    {
        Vector2 local = (Vector2)(screen - _win.Position) / _scale;
        return _shape == null
            ? new Rect2(Vector2.Zero, _contentSize).HasPoint(local)
            : Geometry2D.IsPointInPolygon(local, _shape);
    }
}
