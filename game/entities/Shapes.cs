using Godot;

namespace ProjectSeWoo.Game;

internal static class Shapes
{
    /// <summary>
    /// 부모 좌표계 기준 외접 사각형.
    ///
    /// B4 전까지 엔티티가 전부 <see cref="Polygon2D"/> 자리표시자 도형이라
    /// 점 배열을 그대로 읽는 게 가장 정확하다. 실물 스프라이트로 바뀌면
    /// 텍스처 크기 기준으로 다시 짠다.
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
}
