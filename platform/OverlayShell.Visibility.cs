using System;
using System.Runtime.InteropServices;
using Godot;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="OverlayShell"/> 중 <b>창을 보이고 숨기는 부분과 그걸 조작하는 UI</b>
/// (A6, docs/A6-TRAY-OPTIONS.md).
///
/// 표시/숨김 · 트레이 아이콘 · 옵션 창 · 전체화면 위 자동 숨김이 한 덩어리인
/// 이유는 넷이 전부 <see cref="ApplyVisibility"/> 하나로 수렴하기 때문이다 -
/// 트레이 메뉴도, 옵션 체크박스도, 전체화면 감시도 결국 "지금 보여야 하는가"를
/// 다시 계산하게 만드는 입력일 뿐이다. 이 셋을 서로 다른 파일로 흩으면 A6에서
/// 실제로 났던 상태 꼬임(트레이로 숨겼는데 전체화면이 끝나자 다시 나타남)을
/// 막아 주는 "계산 지점이 하나뿐"이라는 성질이 눈에 안 보이게 된다.
///
/// 본체(<c>OverlayShell.cs</c>)는 창 mechanics만 든다 - docs/SCENE-ARCHITECTURE.md §2.
/// </summary>
public partial class OverlayShell
{
    // --- A6: 표시 여부는 "유저가 원하는가"와 "전체화면 앱이 떠서 자동으로 숨겼는가"
    // 둘의 조합이다 (ApplyVisibility). 커서 장식도 같은 조합을 따르되 옵션
    // (CursorEnabled)까지 하나 더 곱해진다. ---
    private bool _userWantsVisible = true;
    private bool _autoHiddenForFullscreen;

    /// <summary>
    /// 셸 창이 지금 실제로 보이는가. <see cref="_userWantsVisible"/> 등이 "원하는 것"이라면
    /// 이쪽은 <see cref="SetShellWindowVisible"/>가 OS 에 물어서 되읽은 "실제"다.
    /// </summary>
    private bool _shellWindowVisible = true;

    /// <summary>창 숨김 미지원 플랫폼 경고를 한 번만 찍기 위한 표식. 0.5초 틱마다 도배하면 안 된다.</summary>
    private bool _hideUnsupportedLogged;

    private TrayIcon _tray;
    private OptionsWindow _options;
    private FullscreenWatcher _fullscreenWatcher;

    /// <summary>
    /// 실제 표시 여부를 계산해서 창/커서에 적용하는 유일한 지점.
    ///
    /// "보이는가"는 서로 독립적인 두 이유의 조합이다 - 유저가 트레이에서 숨겼는가
    /// (<see cref="_userWantsVisible"/>), 전체화면 앱이 떠서 자동으로 숨겼는가
    /// (<see cref="_autoHiddenForFullscreen"/>). 둘 중 하나라도 "숨겨라"면 숨긴다.
    /// 이 메서드 하나로만 창/커서 표시를 바꾸면, "트레이로 숨겼는데 전체화면이
    /// 끝나자 다시 나타났다" 같은 상태 꼬임이 구조적으로 안 생긴다.
    ///
    /// 커서 장식은 그 위에 옵션 두 개를 더 곱한다 -
    /// <see cref="SaveData.SettingsState.CursorEnabled"/>(아예 쓸 것인가)와
    /// <see cref="SaveData.SettingsState.CursorIndependent"/>(셸이 숨어도 남길 것인가).
    /// 뒤쪽은 숨김 **이유**를 구분하지 않는다. 위 한 줄로 합쳐 둔 것이 상태 꼬임
    /// 방지책이라, 이유별 예외를 만들면 그 이점이 사라진다.
    /// </summary>
    private void ApplyVisibility()
    {
        bool visible = _userWantsVisible && !_autoHiddenForFullscreen;
        SetShellWindowVisible(visible);

        // 친구 칸 창도 메인 창을 따라간다 (B10). 친구만 바탕화면에 남아 있으면 이상하다.
        foreach (SatelliteWindow satellite in _satellites)
        {
            satellite.SetVisible(visible);
        }
        _cursor.SetEnabled(_settings.CursorEnabled && (visible || _settings.CursorIndependent));
    }

    // --- 셸 창 숨기기 -------------------------------------------------------
    //
    // **Godot 은 메인 창의 Visible 을 못 바꾼다.** scene/main/window.cpp 의
    // set_visible 이 "Can't change visibility of main window" 로 막는다. A6 이
    // 그걸 모르고 _win.Visible 에 그대로 썼고, 그래서 트레이 "숨기기"와 전체화면
    // 자동 숨김이 **에러만 찍고 아무 일도 안 했다**(2026-09-16 실행 로그). 커서
    // 레이어는 서브 창이라 똑같은 코드가 멀쩡히 동작했고, 그래서 더 늦게 드러났다.
    //
    // 대신 OS 에 직접 건다. 최소화(WindowSetMode(Minimized))는 안 쓴다 - 작업
    // 표시줄 항목이 생겼다 사라지고 복원 애니메이션이 붙는다. 상주 오버레이가
    // 할 동작이 아니다. ShowWindow 는 ex-style(클릭 통과)도 passthrough 영역도
    // 건드리지 않아서 복원한 뒤 다시 걸어 줄 것이 없다.

    private const int SwHide = 0;

    /// <summary>보이되 포커스는 뺏지 않는다. 오버레이가 남의 창에서 포커스를 가져가면 안 된다.</summary>
    private const int SwShowNoActivate = 4;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// 셸 창을 실제로 숨기고/보인다. 부르는 곳은 <see cref="ApplyVisibility"/> 하나뿐이다.
    /// </summary>
    private void SetShellWindowVisible(bool visible)
    {
        if (_shellWindowVisible == visible)
        {
            return;
        }

        if (OS.GetName() != "Windows")
        {
            // 트레이도 FullscreenWatcher 도 Windows 전용이라 여기 올 일은 거의 없다.
            // 그래도 조용히 넘기지 않는다 - "숨겼다고 생각했는데 안 숨었다" 가 정확히
            // 방금 고친 버그였다.
            if (!_hideUnsupportedLogged)
            {
                _hideUnsupportedLogged = true;
                GD.PrintErr($"[shell] 창 숨김 미지원 플랫폼 ({OS.GetName()}) - 계속 보인다");
            }

            return;
        }

        long handle = DisplayServer.WindowGetNativeHandle(
            DisplayServer.HandleType.WindowHandle, _win.GetWindowId());

        if (handle == 0)
        {
            GD.PrintErr("[shell] HWND 를 못 얻었다. 창을 숨길 수 없다");
            return;
        }

        var hwnd = new IntPtr(handle);
        ShowWindow(hwnd, visible ? SwShowNoActivate : SwHide);

        // A2 의 교훈 그대로 되읽어서 확인한다 - "걸었다" 와 "걸렸다" 는 다르다.
        // 이번 버그도 아무도 결과를 안 물어봐서 통과한 것이다.
        bool actual = IsWindowVisible(hwnd);
        _shellWindowVisible = actual;

        if (actual != visible)
        {
            GD.PrintErr($"[shell] 창 숨김 실패: 요청 {visible}, 실제 {actual}");
        }
    }

    // ------------------------------------------------------------------ A6: 트레이 / 옵션 창 / 자동 숨김

    /// <summary>
    /// WEEK0-GODOT-VALIDATION.md §4 "트레이 아이콘 + 메뉴(보이기/숨기기/설정/종료)".
    /// Godot 4.3+ 내장 API만 쓴다(<see cref="TrayIcon"/>). macOS/Windows만 지원한다.
    /// </summary>
    private void SetupTray()
    {
        _tray = new TrayIcon();
        _tray.OnToggleVisibility += () =>
        {
            _userWantsVisible = !_userWantsVisible;
            ApplyVisibility();
        };
        _tray.OnOpenSettings += OpenOptionsWindow;
        _tray.OnQuit += () => GetTree().Quit();

        var icon = GD.Load<Texture2D>("res://icon.svg");
        _tray.Build(icon, "ProjectSeWoo");

        if (!_tray.IsSupported)
        {
            GD.Print("[shell] 트레이 아이콘 미지원 - 창을 닫으면 트레이로 숨는 대신 그대로 숨는다");
        }
    }

    /// <summary>
    /// 창 닫기 요청(Alt+F4 등)을 종료가 아니라 숨기기로 바꾼다
    /// (WEEK0-GODOT-VALIDATION.md §4). 트레이가 없는 환경(미지원 플랫폼)에서는
    /// 되찾을 방법이 없어지므로, 그때는 그냥 종료한다.
    /// </summary>
    private void OnCloseRequested()
    {
        if (_tray is { IsSupported: true })
        {
            _userWantsVisible = false;
            ApplyVisibility();
        }
        else
        {
            GetTree().Quit();
        }
    }

    /// <summary>
    /// OptionsWindow는 값이 바뀌면 이벤트만 쏜다 - 실제로 적용하고 저장하는 건 여기서 한다
    /// (docs/A6-TRAY-OPTIONS.md §1 "옵션 UI는 저장을 모른다").
    /// </summary>
    private void WireOptionsEvents()
    {
        _options.ScaleChanged += v => { SetScale(v); PersistSettings(); };
        _options.OpacityChanged += v => { SetOpacity(v); PersistSettings(); };
        _options.PositionLockedChanged += v => { SetClickThrough(v); PersistSettings(); };
        _options.SoundChanged += v => { _settings.Sound = v; PersistSettings(); };
        _options.NotificationsChanged += v => { _settings.Notifications = v; PersistSettings(); };
        _options.CursorEnabledChanged += v => { _settings.CursorEnabled = v; ApplyVisibility(); PersistSettings(); };
        _options.CursorIndependentChanged += v => { _settings.CursorIndependent = v; ApplyVisibility(); PersistSettings(); };
        _options.HideOnFullscreenChanged += v => { _settings.HideOnFullscreen = v; PersistSettings(); };
        _options.AutostartChanged += v =>
        {
            _settings.Autostart = v;
            if (!_unattended)
            {
                Autostart.SetEnabled(v);
            }

            PersistSettings();
        };

        // 옵션 창이 열린 동안은 창 전체가 클릭을 받아야 한다 - 안 그러면 패널이
        // 마스코트 클릭 영역 밖으로 나가는 순간 슬라이더/체크박스를 못 누른다.
        // 닫히면 위치 잠금 값대로 되돌린다.
        _options.Closed += () => ApplyPassthrough(force: true);
    }

    private void OpenOptionsWindow()
    {
        _options.SetValues(_settings, _unattended ? _settings.Autostart : Autostart.IsEnabled());
        _options.Open();
        DisplayServer.WindowSetMousePassthrough(Array.Empty<Vector2>());
    }

    private void ToggleOptionsWindow()
    {
        if (_options.IsOpen)
        {
            _options.Close();
        }
        else
        {
            OpenOptionsWindow();
        }
    }

    /// <summary>
    /// 전체화면으로 실행 중인 다른 앱 위에서 자동으로 숨긴다 (§7-1, §7-4).
    /// 0.5초 틱(<see cref="OnTick"/>)마다 확인한다 - 매 프레임 P/Invoke 를 부를
    /// 이유가 없다. 휴리스틱의 한계는 docs/A6-TRAY-OPTIONS.md §3 참고 - 아직
    /// 실제 전체화면 게임으로는 검증하지 못했다.
    /// </summary>
    private void CheckFullscreen()
    {
        bool shouldHide = _settings.HideOnFullscreen && _fullscreenWatcher.IsOtherAppFullscreen();
        if (shouldHide == _autoHiddenForFullscreen)
        {
            return;
        }

        _autoHiddenForFullscreen = shouldHide;
        ApplyVisibility();
    }
}
