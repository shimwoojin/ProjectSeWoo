using System;
using Godot;
using Microsoft.Win32;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 시작 프로그램 등록 (§7-4, WEEK0-GODOT-VALIDATION.md §4).
///
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. 관리자 권한이
/// 필요 없고 이 유저 계정에만 적용된다 - 상주 앱의 자동 시작으로는 이거면 충분하고,
/// 서비스/작업 스케줄러 등록 같은 무거운 방식을 쓸 이유가 없다.
/// </summary>
public static class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ProjectSeWoo";

    /// <summary>
    /// 레지스트리 값을 세이브의 <c>autostart</c>와 맞춘다.
    ///
    /// **개발 중(Godot 에디터로 실행) 주의** - <c>OS.GetExecutablePath()</c>가
    /// 이때는 게임 exe가 아니라 Godot 엔진 exe를 가리킨다. 개발 중에 이 옵션을
    /// 켜면 다음 로그인 때 게임이 아니라 에디터가 뜬다. 익스포트 빌드(A12)에서만
    /// 의미가 있는 기능이고, 그때는 <c>GetExecutablePath()</c>가 정확히 게임
    /// exe를 가리킨다.
    /// </summary>
    public static void SetEnabled(bool on)
    {
        if (OS.GetName() != "Windows")
        {
            return;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                GD.PrintErr("[autostart] Run 키를 열지 못했다");
                return;
            }

            if (on)
            {
                string exePath = OS.GetExecutablePath();
                key.SetValue(ValueName, $"\"{exePath}\"");
                GD.Print($"[autostart] 등록: {exePath}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                GD.Print("[autostart] 해제");
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[autostart] 처리 실패 ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>
    /// 레지스트리에 실제로 값이 있는가. 세이브의 <c>autostart</c>와 실제 상태가
    /// 갈릴 수 있다 - 유저가 "시작 앱" 설정 화면에서 수동으로 꺼버릴 수 있기
    /// 때문이다. 옵션 창을 열 때 이 값으로 체크박스를 다시 맞춘다.
    /// </summary>
    public static bool IsEnabled()
    {
        if (OS.GetName() != "Windows")
        {
            return false;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[autostart] 조회 실패 ({e.GetType().Name}: {e.Message})");
            return false;
        }
    }
}
