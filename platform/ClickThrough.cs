using System;
using System.Runtime.InteropServices;
using Godot;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 창의 클릭 통과 - <c>WS_EX_TRANSPARENT | WS_EX_LAYERED</c> (docs/A2-CURSOR-SPIKE.md §2). 커서 장식 창은 늘 통과,
/// 메인 창·친구 칸은 <b>커서가 보이는 모양 위일 때만 통과를 끈다</b> (2026-09-30).
///
/// <b>왜 창 모양(<c>WindowSetMousePassthrough</c>)을 안 쓰는가.</b> Godot 의 그 함수는 Windows 에서 <c>SetWindowRgn</c>
/// 이라 클릭뿐 아니라 <b>그리기까지</b> 그 모양으로 잘라낸다. 그래서 HUD 글자·떨어지는 바나나를 보이게 하려고 클릭
/// 영역에 일부러 넣어 왔고, 스팀 오버레이가 우리 창에 그리는 업적 알림은 모양 밖이 잘려 조각만 보였다. 이 스타일은
/// 창을 자르지 않고 클릭만 다른 프로세스로 넘긴다 - 픽셀 단위 투명 그림은 그대로다(A2 실측).
///
/// <b>TRANSPARENT 만으로는 안 된다</b> - LAYERED 가 같이 있어야 다른 프로세스로 넘어간다(A2 3차 실패). LAYERED 를 붙이면
/// 알파를 정해 주기 전까지 창이 안 보이므로 <see cref="Prepare"/> 가 255 를 준다.
/// </summary>
public static class ClickThrough
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
    private const uint LwaAlpha = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);

    /// <summary>Godot 창의 HWND. 윈도우가 아니거나 OS 창이 아직 없으면(처음 보이기 전) <see cref="IntPtr.Zero"/>.</summary>
    public static IntPtr Hwnd(int windowId)
    {
        if (OS.GetName() != "Windows" || windowId == DisplayServer.InvalidWindowId)
        {
            return IntPtr.Zero;
        }

        return new IntPtr(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowId));
    }

    /// <summary>
    /// LAYERED 를 붙이고 알파 255 를 준다. <b>반드시 되읽어서 확인한다</b> - "걸었다" 와 "걸렸다" 는 다르다(A2 의 1차 실패).
    /// 이미 LAYERED 면 스타일은 그대로 두고 알파만 다시 준다.
    /// </summary>
    public static bool Prepare(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        long before = Style(hwnd);
        if ((before & WsExLayered) == 0)
        {
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(before | WsExLayered));
        }

        if ((Style(hwnd) & WsExLayered) == 0)
        {
            return false;
        }

        // 255 = 완전 불투명이지만 Godot 이 DWM 합성으로 그리는 픽셀 단위 알파는 살아남는다(A2 실측).
        if (!SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha))
        {
            GD.PrintErr("[click] SetLayeredWindowAttributes 실패");
        }

        return true;
    }

    /// <summary>
    /// 클릭을 뒤 창으로 넘길지. TRANSPARENT 만 켜고 끈다(LAYERED 는 <see cref="Prepare"/> 가 붙여 둔 그대로).
    /// 지금 실제 스타일을 읽어서 다를 때만 쓴다 - Godot 이 스타일을 다시 써서 풀렸어도 다음 호출이 맞춘다.
    /// </summary>
    /// <returns>실제로 스타일을 바꿨으면 true.</returns>
    public static bool SetPassThrough(IntPtr hwnd, bool on)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        long style = Style(hwnd);
        long wanted = on ? style | WsExTransparent | WsExLayered : style & ~WsExTransparent;
        if (wanted == style)
        {
            return false;
        }

        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(wanted));
        return true;
    }

    /// <summary>지금 통과 중인가 (진단용).</summary>
    public static bool IsPassThrough(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && (Style(hwnd) & (WsExTransparent | WsExLayered)) == (WsExTransparent | WsExLayered);

    /// <summary>스타일 값 (진단용 16진 표기).</summary>
    public static long Style(IntPtr hwnd) => GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
}
