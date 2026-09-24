using System;
using System.Runtime.InteropServices;
using Godot;

namespace ProjectSeWoo.Platform;

public partial class OverlayShell
{
    // --- 작업 표시줄 클릭으로 내려가지 않게 (2026-09-24) ---------------------
    //
    // 활성 창의 작업 표시줄 버튼을 누르면 Windows 는 그 창을 최소화한다. 다만 창에
    // **최소화 버튼 스타일(WS_MINIMIZEBOX)이 있을 때만**이다 - 최소화 버튼이 없는
    // 대화 상자는 눌러도 안 내려가는 것과 같은 규칙이다. Godot 은 테두리 없는 창도
    // 작업 표시줄에서 내릴 수 있게 이 비트를 붙여 두는데, 상주 오버레이가 항상 위로
    // 떠 있는 동안에는 그게 "눌렀더니 사라졌다" 가 된다. 그래서 항상 위일 때만 뗀다.
    //
    // **Godot 이 이 비트를 되살릴 수 있다.** 창 속성(AlwaysOnTop 등)을 바꿀 때마다
    // 스타일을 다시 계산하기 때문이다. 그래서 한 번 떼고 끝내지 않고 0.5초 틱마다
    // 확인한다(비용은 GetWindowLongPtr 한 번). 그 틈에 내려갔으면 되돌린다.

    private const int GwlStyle = -16;
    private const long WsMinimizeBox = 0x00020000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private void EnforceTaskbarMinimize()
    {
        if (_win == null || OS.GetName() != "Windows")
        {
            return;
        }

        long handle = DisplayServer.WindowGetNativeHandle(
            DisplayServer.HandleType.WindowHandle, _win.GetWindowId());
        if (handle == 0)
        {
            return;
        }

        var hwnd = new IntPtr(handle);
        bool allowMinimize = !_win.AlwaysOnTop;
        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        bool hasBox = (style & WsMinimizeBox) != 0;

        if (hasBox != allowMinimize)
        {
            long next = allowMinimize ? style | WsMinimizeBox : style & ~WsMinimizeBox;
            SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(next));
        }

        // 비트를 뗐는데도 내려갔으면(Godot 이 되살린 틈, Win+M 같은 다른 경로) 되돌린다.
        // 트레이 "숨기기"·전체화면 자동 숨김은 최소화가 아니라 ShowWindow(SW_HIDE) 라
        // 여기 걸리지 않는다 (OverlayShell.Visibility.cs).
        if (!allowMinimize && _win.Mode == Window.ModeEnum.Minimized)
        {
            _win.Mode = Window.ModeEnum.Windowed;
        }
    }
}
