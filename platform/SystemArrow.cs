using System;
using System.IO;
using Godot;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 지금 Windows 가 그리는 화살표 커서의 <b>꼬리 끝</b>이 커서 끝(핫스팟)에서 몇 px 인가 - 커서 원숭이의 바나나 꼭지를
/// 거기 붙인다 (<see cref="CursorLayer"/>, 2026-09-30).
///
/// <b>화살표 모양을 가정하지 않고 실제 커서 파일을 잰다.</b> 처음엔 흔한 화살표의 꼬리 끝(8, 19.5)에 크기 배율만 곱했는데,
/// 접근성 "마우스 포인터 크기" 를 최대로 두면 Windows 가 만든 화살표(<c>arrow_eoa.cur</c>, 256x256)는 칸의 60% 만 쓰고
/// 모양도 달라서 10~15px 어긋났다. 그래서 레지스트리 <c>HKCU\Control Panel\Cursors\Arrow</c> 가 가리키는 .cur 파일을 읽고,
/// 불투명한 픽셀의 맨 아래 줄 근처(<see cref="TailBand"/>)의 가운데를 꼬리 끝으로 친다.
///
/// 못 읽으면(애니메이션 .ani, 32비트가 아닌 그림, 경로 없음) null - 부르는 쪽이 흔한 화살표 모양으로 대신한다.
/// </summary>
public static class SystemArrow
{
    private const string CursorsKey = @"Control Panel\Cursors";

    /// <summary>맨 아래 불투명 줄에서 화살표 높이의 이 비율 안의 줄들을 꼬리 끝으로 본다. 넓히면 머리 아래 모서리가 섞인다.</summary>
    private const float TailBand = 0.03f;

    /// <summary>접근성 "마우스 포인터 크기" (96 DPI 기준 px). 없거나 못 읽으면 기본 32.</summary>
    public static int BaseSize()
    {
        object value = ReadCursorsValue("CursorBaseSize");
        return value is int size && size >= 32 ? size : 32;
    }

    /// <summary>
    /// 바뀌었는지 보는 열쇠 - 포인터 크기, 화살표 파일 경로, 그 파일의 수정 시각. 크기를 바꾸면 Windows 가 같은 이름의
    /// <c>arrow_eoa.cur</c> 를 다시 쓰므로 시각까지 본다.
    /// </summary>
    public static string Signature()
    {
        string path = ArrowPath();
        long written = 0;
        try
        {
            if (path != null && File.Exists(path))
            {
                written = File.GetLastWriteTimeUtc(path).Ticks;
            }
        }
        catch (Exception)
        {
            // 못 보면 크기·경로만으로 판단한다.
        }

        return $"{BaseSize()}|{path}|{written}";
    }

    /// <summary>
    /// 꼬리 끝 - 핫스팟 기준, <paramref name="drawnSize"/> px 로 그려질 때의 px. 커서 파일에서 그 크기에 가장 가까운(같거나
    /// 큰 것 중 가장 작은) 그림을 재서 비례로 맞춘다. 못 재면 null.
    /// </summary>
    public static Vector2? MeasureTail(int drawnSize)
    {
        string path = ArrowPath();
        if (path == null || !path.EndsWith(".cur", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return MeasureCur(File.ReadAllBytes(path), drawnSize);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[cursor] 화살표 커서 파일을 못 읽었다 ({path}): {e.Message}");
            return null;
        }
    }

    private static string ArrowPath()
    {
        string raw = ReadCursorsValue("Arrow") as string;
        return string.IsNullOrWhiteSpace(raw) ? null : System.Environment.ExpandEnvironmentVariables(raw);
    }

    private static object ReadCursorsValue(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(CursorsKey);
            return key?.GetValue(name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// .cur = ICONDIR(6) + 항목(16)×n + 그림. 항목: 폭·높이(0 = 256), 핫스팟 x·y, 크기, 위치. 그림은 PNG 이거나
    /// BITMAPINFOHEADER 로 시작하는 DIB(높이가 AND 마스크까지 두 배, 아래 줄부터).
    /// </summary>
    private static Vector2? MeasureCur(byte[] b, int drawnSize)
    {
        if (b.Length < 6 || BitConverter.ToUInt16(b, 2) != 2)
        {
            return null;
        }

        int count = BitConverter.ToUInt16(b, 4);
        int best = -1, bestSize = 0;
        for (int i = 0; i < count; i++)
        {
            int size = b[6 + 16 * i] == 0 ? 256 : b[6 + 16 * i];
            bool better = best < 0
                || (size >= drawnSize && (bestSize < drawnSize || size < bestSize))
                || (size < drawnSize && bestSize < drawnSize && size > bestSize);
            if (better)
            {
                best = i;
                bestSize = size;
            }
        }

        if (best < 0)
        {
            return null;
        }

        int entry = 6 + 16 * best;
        var hot = new Vector2(BitConverter.ToUInt16(b, entry + 4), BitConverter.ToUInt16(b, entry + 6));
        int offset = (int)BitConverter.ToUInt32(b, entry + 12);
        int length = (int)BitConverter.ToUInt32(b, entry + 8);

        if (!TryLoadAlpha(b, offset, length, out byte[] alpha, out int width, out int height))
        {
            return null;
        }

        Vector2? tail = FindTail(alpha, width, height);
        if (tail == null)
        {
            return null;
        }

        return (tail.Value - hot) * (drawnSize / (float)width);
    }

    /// <summary>그림 하나의 알파만 위 줄부터 꺼낸다. 픽셀 하나씩 Godot 을 부르지 않는다 - 256x256 이면 눈에 띄게 멈춘다.</summary>
    private static bool TryLoadAlpha(byte[] b, int offset, int length, out byte[] alpha, out int width, out int height)
    {
        alpha = null;
        width = height = 0;

        if (b[offset] == 0x89)
        {
            var png = new Image();
            byte[] data = new byte[length];
            Array.Copy(b, offset, data, 0, length);
            if (png.LoadPngFromBuffer(data) != Error.Ok)
            {
                return false;
            }

            png.Convert(Image.Format.Rgba8);
            width = png.GetWidth();
            height = png.GetHeight();
            byte[] rgba = png.GetData();
            alpha = new byte[width * height];
            for (int i = 0; i < alpha.Length; i++)
            {
                alpha[i] = rgba[i * 4 + 3];
            }

            return true;
        }

        // DIB: BITMAPINFOHEADER 다음이 BGRA, 아래 줄부터. 높이는 AND 마스크까지 두 배로 적혀 있다.
        int header = (int)BitConverter.ToUInt32(b, offset);
        width = BitConverter.ToInt32(b, offset + 4);
        height = BitConverter.ToInt32(b, offset + 8) / 2;
        int bpp = BitConverter.ToUInt16(b, offset + 14);
        if (bpp != 32 || width <= 0 || height <= 0 || offset + header + width * height * 4 > b.Length)
        {
            return false;
        }

        alpha = new byte[width * height];
        int pixels = offset + header;
        for (int row = 0; row < height; row++)
        {
            int src = pixels + row * width * 4;
            int dst = (height - 1 - row) * width;
            for (int x = 0; x < width; x++)
            {
                alpha[dst + x] = b[src + x * 4 + 3];
            }
        }

        return true;
    }

    /// <summary>불투명한 픽셀의 맨 아래 줄 근처(<see cref="TailBand"/>)의 가로 가운데와 맨 아래 줄.</summary>
    private static Vector2? FindTail(byte[] alpha, int width, int height)
    {
        int top = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            if (RowHasInk(alpha, width, y))
            {
                if (top < 0)
                {
                    top = y;
                }

                bottom = y;
            }
        }

        if (bottom < 0)
        {
            return null;
        }

        int from = bottom - Mathf.RoundToInt((bottom - top) * TailBand);
        float sumX = 0;
        int n = 0;
        for (int y = from; y <= bottom; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (alpha[y * width + x] > 128)
                {
                    sumX += x;
                    n++;
                }
            }
        }

        return new Vector2(sumX / n, bottom);
    }

    private static bool RowHasInk(byte[] alpha, int width, int y)
    {
        for (int x = 0; x < width; x++)
        {
            if (alpha[y * width + x] > 128)
            {
                return true;
            }
        }

        return false;
    }
}
