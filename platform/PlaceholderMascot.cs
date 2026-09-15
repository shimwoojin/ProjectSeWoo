using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="IInteractiveArea"/> 자리표시자. `game/GameRoot`가 없는 동안
/// `OverlayShell`이 자기가 만든 스프라이트 하나(`icon.svg`)로 클릭 영역을
/// 채운다. **이 클래스가 사라지는 날이 게임 레이어(B1) 착수일이다.**
///
/// A2~A7 스파이크에서 `OverlayShell.MascotRect()`가 하던 계산을 그대로
/// 옮겨온 것뿐이다 - 동작은 바뀌지 않는다.
/// </summary>
public sealed class PlaceholderMascot : IInteractiveArea
{
    private readonly Sprite2D _sprite;

    public PlaceholderMascot(Sprite2D sprite)
    {
        _sprite = sprite;
    }

    public Rect2 GetClickableBounds()
    {
        Vector2 size = _sprite.Texture.GetSize() * _sprite.Scale;
        Vector2 topLeft = _sprite.Position - (_sprite.Centered ? size * 0.5f : Vector2.Zero);
        return new Rect2(topLeft, size);
    }
}
