using Godot;

namespace ProjectSeWoo.Game;

internal static class Shapes
{
    /// <summary>
    /// 부모 좌표계 기준 외접 사각형.
    ///
    /// 이 값이 그대로 클릭 통과 영역이 된다 (§7-3). 그래서 **쉴 때 한 번만 재고
    /// 연출 중에는 다시 재지 않는다** - 매 프레임 값이 바뀌면 그만큼
    /// <c>WindowSetMousePassthrough</c> 쓰기가 늘어난다.
    /// </summary>
    public static Rect2 Bounds(Polygon2D poly)
    {
        Vector2[] points = poly.Polygon;
        Transform2D toParent = poly.Transform;

        var rect = new Rect2(toParent * (poly.Offset + points[0]), Vector2.Zero);
        for (int i = 1; i < points.Length; i++)
        {
            rect = rect.Expand(toParent * (poly.Offset + points[i]));
        }

        return rect;
    }

    /// <summary>
    /// 스프라이트판 (B4). 텍스처의 <b>한 프레임</b> 크기를 기준으로 잡는다 -
    /// <see cref="Sprite2D.Hframes"/> 를 쓰는 시트를 통째로 재면 클릭 영역이
    /// 애니메이션 8칸 폭만큼 부풀어 나무 너머까지 먹는다.
    ///
    /// 네 모서리를 각각 변환해서 감싼다. 회전이 걸린 스프라이트에서 크기만
    /// 변환하면 축에 정렬된 사각형이 안 나온다.
    /// </summary>
    public static Rect2 Bounds(Sprite2D sprite)
    {
        if (sprite.Texture is null)
        {
            return new Rect2(sprite.Position, Vector2.Zero);
        }

        Vector2 frame = sprite.Texture.GetSize()
            / new Vector2(Mathf.Max(sprite.Hframes, 1), Mathf.Max(sprite.Vframes, 1));

        Vector2 topLeft = sprite.Offset - (sprite.Centered ? frame / 2f : Vector2.Zero);
        Transform2D toParent = sprite.Transform;

        var rect = new Rect2(toParent * topLeft, Vector2.Zero);
        rect = rect.Expand(toParent * (topLeft + new Vector2(frame.X, 0f)));
        rect = rect.Expand(toParent * (topLeft + new Vector2(0f, frame.Y)));
        rect = rect.Expand(toParent * (topLeft + frame));

        return rect;
    }
}
