using System;
using Godot;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 트레이 아이콘 + 우클릭 메뉴 (§7-4, WEEK0-GODOT-VALIDATION.md §4).
///
/// Godot 4.3+ 내장 API(<c>DisplayServer.CreateStatusIndicator</c> +
/// <c>NativeMenu</c>)만 쓴다. 플러그인이나 GDExtension이 필요 없다.
/// macOS/Windows만 지원하고 Linux는 안 된다 - <see cref="IsSupported"/>로 확인한다.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private int _indicatorId = -1;
    private Rid _menuRid;

    public bool IsSupported { get; private set; }

    public event Action OnToggleVisibility;
    public event Action OnOpenSettings;
    public event Action OnQuit;

    public void Build(Texture2D icon, string tooltip)
    {
        if (!DisplayServer.HasFeature(DisplayServer.Feature.StatusIndicator))
        {
            GD.Print("[tray] StatusIndicator 미지원 플랫폼 - 트레이 아이콘 없이 진행한다");
            return;
        }

        _indicatorId = DisplayServer.CreateStatusIndicator(
            icon, tooltip, Callable.From<MouseButton, Vector2I>(OnIndicatorActivate));

        if (_indicatorId < 0)
        {
            GD.PrintErr("[tray] CreateStatusIndicator 실패");
            return;
        }

        BuildMenu();
        DisplayServer.StatusIndicatorSetMenu(_indicatorId, _menuRid);
        IsSupported = true;
        GD.Print($"[tray] indicator id={_indicatorId}");
    }

    private void BuildMenu()
    {
        _menuRid = NativeMenu.CreateMenu();
        NativeMenu.AddItem(_menuRid, "보이기/숨기기", Callable.From(() => OnToggleVisibility?.Invoke()));
        NativeMenu.AddItem(_menuRid, "설정...", Callable.From(() => OnOpenSettings?.Invoke()));
        NativeMenu.AddSeparator(_menuRid);
        NativeMenu.AddItem(_menuRid, "종료", Callable.From(() => OnQuit?.Invoke()));
    }

    /// <summary>
    /// 트레이 아이콘 자체를 클릭했을 때. 우클릭은 <see cref="StatusIndicatorSetMenu"/>로
    /// 붙인 메뉴가 OS 차원에서 알아서 띄운다 - 여기서는 좌클릭만 다룬다(보이기/숨기기).
    /// </summary>
    private void OnIndicatorActivate(MouseButton button, Vector2I position)
    {
        if (button == MouseButton.Left)
        {
            OnToggleVisibility?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_indicatorId >= 0)
        {
            DisplayServer.DeleteStatusIndicator(_indicatorId);
            _indicatorId = -1;
        }

        if (_menuRid.IsValid)
        {
            NativeMenu.FreeMenu(_menuRid);
            _menuRid = default;
        }
    }
}
