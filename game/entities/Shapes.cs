using System.Collections.Generic;
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

    /// <summary>
    /// 스프라이트의 보이는 모양을 가로 띠 <paramref name="bands"/> 개로 (부모 좌표계, 2026-09-28). 띠마다 불투명한
    /// 픽셀의 왼쪽 끝~오른쪽 끝 사각형이다 - 잎이 넓고 줄기가 좁은 나무가 계단 모양으로 잡힌다. 빈 띠는 뺀다.
    ///
    /// 시트(<see cref="Sprite2D.Hframes"/>)는 <b>모든 칸을 합친</b> 모양이다 - 펀치로 뻗은 팔도 들어간다.
    /// 텍스처를 CPU 로 읽으므로 <c>_Ready</c> 와 그림이 바뀔 때(B18 나무 단계·원숭이 스킨)만 부른다. 이미지를 못 읽으면 <see cref="Bounds(Sprite2D)"/> 하나.
    /// </summary>
    public static List<Rect2> Silhouette(Sprite2D sprite, int bands)
    {
        var result = new List<Rect2>(bands);
        Image image = sprite.Texture?.GetImage();
        if (image == null || image.IsEmpty())
        {
            result.Add(Bounds(sprite));
            return result;
        }

        if (image.IsCompressed())
        {
            image.Decompress();
        }

        int columns = Mathf.Max(sprite.Hframes, 1);
        int rows = Mathf.Max(sprite.Vframes, 1);
        var frame = new Vector2I(image.GetWidth() / columns, image.GetHeight() / rows);
        Vector2 topLeft = sprite.Offset - (sprite.Centered ? (Vector2)frame / 2f : Vector2.Zero);
        Transform2D toParent = sprite.Transform;

        for (int b = 0; b < bands; b++)
        {
            int y0 = frame.Y * b / bands;
            int y1 = frame.Y * (b + 1) / bands;
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

            for (int f = 0; f < columns * rows; f++)
            {
                var origin = new Vector2I(f % columns * frame.X, f / columns * frame.Y);
                Rect2I used = image.GetRegion(new Rect2I(origin.X, origin.Y + y0, frame.X, y1 - y0)).GetUsedRect();
                if (used.Size.X <= 0 || used.Size.Y <= 0)
                {
                    continue;
                }

                minX = Mathf.Min(minX, used.Position.X);
                maxX = Mathf.Max(maxX, used.End.X);
                minY = Mathf.Min(minY, y0 + used.Position.Y);
                maxY = Mathf.Max(maxY, y0 + used.End.Y);
            }

            if (minX == int.MaxValue)
            {
                continue;
            }

            // 띠 안의 실제 위아래까지 줄인다 - 맨 위·아래 띠가 빈 공간을 덜 먹는다. 가운데 띠는 이웃과 붙어 있다.
            var local = new Rect2(topLeft + new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
            var rect = new Rect2(toParent * local.Position, Vector2.Zero);
            rect = rect.Expand(toParent * new Vector2(local.End.X, local.Position.Y));
            rect = rect.Expand(toParent * new Vector2(local.Position.X, local.End.Y));
            rect = rect.Expand(toParent * local.End);
            result.Add(rect);
        }

        if (result.Count == 0)
        {
            result.Add(Bounds(sprite));
        }

        return result;
    }
}
