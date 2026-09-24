using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="OverlayShell"/> 의 <b>디버그 키</b>. 전부 임시다.
///
/// 두 종류가 섞여 있다:
/// <list type="bullet">
///   <item><b>스파이크 손잡이</b> (F3 매 프레임 갱신, F5 FPS 캡, F6 저전력,
///   F7 모니터 이동, F8 히트 외곽선, F9 리포트, F10 계측 초기화, F12/1 커서
///   폴링·추종 방식). Week 0 의 Go/No-Go 를 눈으로 가르려고 만든 것이고,
///   판정이 끝난 지금도 재측정에 쓰이므로 남긴다.</item>
///   <item><b>UI 대역</b> (F2 위치 잠금, F11 커서 장식, <c>[</c>/<c>]</c> 크기,
///   <c>-</c>/<c>=</c> 투명도, O 옵션 창).
///   <b>이쪽은 주인이 생기면 지운다</b> - 옵션 창(A6)이 이미 F2/F11/크기/투명도의
///   주인이다. <b>2/3/4(커서 슬롯 장착)는 2026-09-21 에 B6 이 가져갔다</b> -
///   예고대로 <c>game/GameRoot</c> 로 옮겼고, 이제 인벤토리를 거치므로 세이브와
///   상점 화면이 같이 따라간다.</item>
/// </list>
///
/// 키가 겹치면 게임 레이어가 먼저 죽는다. Godot 은 <c>_UnhandledKeyInput</c> 을
/// <c>_UnhandledInput</c> <b>보다 먼저</b> 부르고 여기서 <c>SetInputAsHandled</c> 를
/// 걸기 때문에, 여기 잡힌 키는 <c>game/</c> 에 아예 도달하지 않는다 - 게임 쪽
/// 디버그 키(지금은 G)를 고를 때 이 파일의 <c>switch</c> 를 먼저 본다.
/// </summary>
public partial class OverlayShell
{
    /// <summary>H(debug 숨김)가 스스로 돌아오기까지의 시간. 숨은 창은 키를 못 받는다.</summary>
    private const double DebugHideSeconds = 3.0;

    /// <summary>H(debug 숨김)의 남은 시간. 0 이하면 쉬는 중이다.</summary>
    private double _debugHideRemaining;

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.F1:
                _hud.Visible = !_hud.Visible;
                break;

            case Key.F2:
                // A6부터는 진짜 옵션이다 - 옵션 창의 "위치 잠금" 체크박스와 정확히
                // 같은 경로(SetClickThrough)를 부른다.
                SetClickThrough(!_settings.PositionLocked);
                PersistSettings();
                break;

            case Key.F3:
                _updateEveryFrame = !_updateEveryFrame;
                GD.Print($"[shell] passthrough update = {(_updateEveryFrame ? "EVERY FRAME (flicker repro)" : "ON CHANGE")}");
                break;

            case Key.F4:
                _win.AlwaysOnTop = !_win.AlwaysOnTop;
                EnforceTaskbarMinimize();
                break;

            case Key.F5:
                _fpsCapIndex = (_fpsCapIndex + 1) % FpsCaps.Length;
                Engine.MaxFps = FpsCaps[_fpsCapIndex];
                _perf.Reset();
                break;

            case Key.F6:
                _lowPower = !_lowPower;
                ApplyPowerSettings();
                _perf.Reset();
                break;

            case Key.F7:
                MoveToScreen((DisplayServer.WindowGetCurrentScreen() + 1) % DisplayServer.GetScreenCount());
                break;

            case Key.F8:
                _showOutline = !_showOutline;
                _outline.Visible = _showOutline;
                RefreshOutline(_appliedRegion);
                break;

            case Key.F9:
                DisplayServer.ClipboardSet(BuildReport());
                GD.Print("[shell] report copied to clipboard");
                break;

            case Key.F10:
                ResetCounters();
                break;

            // 숨김이 실제로 먹는지 트레이 없이 확인하기 위한 debug 키. 그냥 숨기면
            // 창이 포커스를 잃어 **다시 켤 키를 못 받는다** - 트레이로만 되돌릴 수
            // 있게 된다. 그래서 잠깐 숨겼다 스스로 돌아온다.
            case Key.H:
                _userWantsVisible = false;
                _debugHideRemaining = DebugHideSeconds;
                ApplyVisibility();
                break;

            case Key.F11:
                // 옵션 창의 "커서 장식" 체크박스와 같은 경로.
                _settings.CursorEnabled = !_settings.CursorEnabled;
                ApplyVisibility();
                PersistSettings();
                _perf.Reset();
                break;

            case Key.F12:
                _cursor.CycleInterval();
                _perf.Reset();
                break;

            case Key.Key1:
                _cursor.CycleMode();
                _perf.Reset();
                break;

            // 2/3/4 는 여기 없다. B6 이 가져가 game/GameRoot 가 처리한다 -
            // **여기서 잡으면 SetInputAsHandled 때문에 게임에 아예 도달하지 않는다.**

            // IShell 실물을 옵션 창 없이 빠르게 시험하기 위한 debug 키.
            // 옵션 창의 슬라이더와 정확히 같은 SetScale/SetOpacity를 부른다.
            case Key.Bracketleft:
                SetScale(_settings.Scale - 0.1f);
                break;

            case Key.Bracketright:
                SetScale(_settings.Scale + 0.1f);
                break;

            case Key.Minus:
                SetOpacity(_settings.Opacity - 0.1f);
                break;

            case Key.Equal:
                SetOpacity(_settings.Opacity + 0.1f);
                break;

            case Key.O:
                // A6 옵션 창을 트레이 없이/트레이 지원이 없는 환경에서도 열 수 있게.
                ToggleOptionsWindow();
                break;

            case Key.Escape:
                if (_options.IsOpen)
                {
                    _options.Close();
                }
                else
                {
                    GetTree().Quit();
                }

                break;

            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

}
