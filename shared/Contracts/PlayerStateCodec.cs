using System;
using System.Buffers.Binary;
using System.Text;

namespace ProjectSeWoo.Shared;

/// <summary>
/// <see cref="PlayerState"/> 의 전송 형식 (A10). 200ms 마다 로비 멤버에게 P2P 로 간다.
///
/// <b>스팀을 모른다.</b> 바이트와 구조체 사이의 변환만 해서, 실물(<c>SteamNetSession</c>)과
/// 목, <c>--selftest</c> 가 같은 코드를 탄다.
///
/// 형식 (리틀 엔디언, 총 20~120 바이트):
/// <code>
/// [0]     버전 (지금 2 - 2026-09-26 B17: 장식 세 칸의 뜻이 원숭이/바나나/장식으로 바뀌었다. 1 과는 서로 버린다)
/// [1..2]  KeystrokesInWindow (ushort)
/// [3]     HarvestsInWindow (byte)
/// [4..11] TotalKeystrokes (long)
/// [12]    CollectionPercent (byte)
/// [13..]  EquippedMonkey / Banana / Deco - 각각 길이 1바이트 + ASCII. 길이 0xFF 는 null(빈 슬롯)
/// </code>
///
/// <b>받은 것은 믿지 않는다.</b> 상대는 우리 게임이 아닐 수도 있다 - 로비 코드만 알면
/// 누구나 들어오고, 패킷은 얼마든지 조작할 수 있다. 그래서 디코드가 막는 것:
/// <list type="bullet">
///   <item>길이가 모자라거나 남으면 버린다.</item>
///   <item>장식 ID 는 <c>[a-z0-9_]</c> 1~<see cref="MaxIdLength"/> 글자만 받는다. 이 값이
///   그대로 에셋 경로(<c>res://assets/cursor/{분류}/{id}/</c>, <see cref="ItemManifest.AssetDir"/>)에 들어가므로, <c>../</c>
///   같은 경로 조작을 여기서 끊는다. 카탈로그에 있는 ID 인지는 그리는 쪽이 한 번 더 본다.</item>
///   <item>수치 범위(한 창의 타건 수 등)는 연출하는 쪽이 자른다 - 형식은 값을 해석하지 않는다.</item>
/// </list>
///
/// 필드를 늘리면 개인정보 안내(docs/C2-PRIVACY.md §2-5)와 기획서 §7-6 의 전송 목록도 고친다.
/// </summary>
public static class PlayerStateCodec
{
    public const byte Version = 2;

    /// <summary>장식 ID 최대 길이. 지금 카탈로그에서 가장 긴 것이 12글자쯤이다.</summary>
    public const int MaxIdLength = 32;

    /// <summary>한 메시지의 최대 크기. 수신 버퍼를 이만큼만 잡는다.</summary>
    public const int MaxSize = 13 + 3 * (1 + MaxIdLength);

    private const byte NullId = 0xFF;

    public static byte[] Encode(PlayerState s)
    {
        var buffer = new byte[MaxSize];
        buffer[0] = Version;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(1), s.KeystrokesInWindow);
        buffer[3] = s.HarvestsInWindow;
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(4), s.TotalKeystrokes);
        buffer[12] = s.CollectionPercent;

        int at = 13;
        at = WriteId(buffer, at, s.EquippedMonkey);
        at = WriteId(buffer, at, s.EquippedBanana);
        at = WriteId(buffer, at, s.EquippedDeco);

        return buffer.AsSpan(0, at).ToArray();
    }

    /// <summary>형식이 아니면 false. 버전이 다르면 false - 지금은 하위 호환을 두지 않는다.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> data, out PlayerState state)
    {
        state = default;
        if (data.Length < 13 + 3 || data.Length > MaxSize || data[0] != Version)
        {
            return false;
        }

        int at = 13;
        if (!TryReadId(data, ref at, out string monkey)
            || !TryReadId(data, ref at, out string banana)
            || !TryReadId(data, ref at, out string deco)
            || at != data.Length)
        {
            return false;
        }

        state = new PlayerState
        {
            KeystrokesInWindow = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(1)),
            HarvestsInWindow = data[3],
            TotalKeystrokes = Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(data.Slice(4))),
            CollectionPercent = Math.Min(data[12], (byte)100),
            EquippedMonkey = monkey,
            EquippedBanana = banana,
            EquippedDeco = deco,
        };
        return true;
    }

    /// <summary>우리가 보낼 수 있는 ID 인가. 보낼 때도 같은 규칙을 적용해서 못 받을 것을 안 보낸다.</summary>
    public static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return false;
        }

        foreach (char c in id)
        {
            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 인코드→디코드 왕복과 조작된 입력 거부를 확인한다. 통과하면 null, 아니면 실패 사유.
    /// 셸의 <c>--selftest</c> 가 세이브 스키마와 같이 부른다.
    /// </summary>
    public static string SelfTest()
    {
        var sample = new PlayerState
        {
            KeystrokesInWindow = 3,
            HarvestsInWindow = 1,
            TotalKeystrokes = 1_234_567_890_123,
            CollectionPercent = 56,
            EquippedMonkey = "monkey_01",
            EquippedBanana = "banana_01",
            EquippedDeco = null,
        };

        byte[] bytes = Encode(sample);
        if (!TryDecode(bytes, out PlayerState back) || back != sample)
        {
            return $"왕복 불일치 ({bytes.Length}바이트)";
        }

        // 받을 수 없는 ID 는 보낼 때 빈 슬롯이 된다.
        byte[] traversal = Encode(sample with { EquippedMonkey = "../../evil" });
        if (!TryDecode(traversal, out PlayerState cleaned) || cleaned.EquippedMonkey != null)
        {
            return "경로 조작 ID 가 인코드에서 안 걸렀다";
        }

        // 손으로 만든 조작 패킷: ID 자리에 '/' 가 든 것.
        byte[] forged = (byte[])bytes.Clone();
        forged[14] = (byte)'/';
        if (TryDecode(forged, out _))
        {
            return "조작된 ID 를 받아들였다";
        }

        if (TryDecode(bytes.AsSpan(0, bytes.Length - 1), out _))
        {
            return "잘린 패킷을 받아들였다";
        }

        byte[] future = (byte[])bytes.Clone();
        future[0] = Version + 1;
        if (TryDecode(future, out _))
        {
            return "다른 버전을 받아들였다";
        }

        return null;
    }

    private static int WriteId(byte[] buffer, int at, string id)
    {
        if (!IsValidId(id))
        {
            buffer[at] = NullId;
            return at + 1;
        }

        buffer[at] = (byte)id.Length;
        Encoding.ASCII.GetBytes(id, 0, id.Length, buffer, at + 1);
        return at + 1 + id.Length;
    }

    private static bool TryReadId(ReadOnlySpan<byte> data, ref int at, out string id)
    {
        id = null;
        if (at >= data.Length)
        {
            return false;
        }

        byte length = data[at++];
        if (length == NullId)
        {
            return true;
        }

        if (length == 0 || length > MaxIdLength || at + length > data.Length)
        {
            return false;
        }

        string text = Encoding.ASCII.GetString(data.Slice(at, length));
        if (!IsValidId(text))
        {
            return false;
        }

        id = text;
        at += length;
        return true;
    }
}
