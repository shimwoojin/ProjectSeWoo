using System.Text;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 룸 코드 - 32비트 값을 사람이 불러줄 수 있는 7글자로 바꾼다 (§4-1 "룸 UID 직접 입력").
/// 예: <c>K7Q-2XMD</c>.
///
/// <b>무엇을 담는가.</b> 실물(A9)은 스팀 로비 SteamID 의 하위 32비트(AccountID)를
/// 담는다. 나머지 비트(유니버스·계정 종류·로비 플래그)는 모든 로비가 같으므로
/// 코드만으로 로비 ID 를 되살릴 수 있고, 그래서 코드→로비를 찾을 서버가 없어도 된다.
/// 이 클래스는 스팀을 모른다 - 숫자와 글자 사이의 변환만 하고, 목도 같은 형식을 쓴다.
///
/// <b>Crockford Base32</b> 를 쓰는 이유: 헷갈리는 글자(I·L·O·U)가 알파벳에 없다.
/// 불러주다 틀리기 쉬운 I/L 은 1 로, O 는 0 으로 읽어 준다. 대소문자·하이픈·공백은 무시한다.
/// 32비트는 5비트씩 7글자(35비트)에 들어가고, 맨 앞 글자는 0~7 만 나온다.
/// </summary>
public static class RoomCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int Length = 7;

    /// <summary>화면 표시용. <c>XXX-XXXX</c> 로 끊는다 - 7글자를 한 번에 읽기보다 덜 틀린다.</summary>
    public static string Format(uint value)
    {
        var chars = new char[Length];
        ulong rest = value;
        for (int i = Length - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(rest & 31)];
            rest >>= 5;
        }

        return new string(chars, 0, 3) + "-" + new string(chars, 3, Length - 3);
    }

    /// <summary>
    /// 사람이 친 코드를 값으로 되돌린다. 형식이 아니면 false.
    /// <see cref="Format"/> 의 결과와 그걸 소문자·하이픈 없이 친 것 모두 받는다.
    /// </summary>
    public static bool TryParse(string text, out uint value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = new StringBuilder(Length);
        foreach (char raw in text)
        {
            if (raw is '-' or ' ' or '\t')
            {
                continue;
            }

            char c = char.ToUpperInvariant(raw);
            c = c switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => c,
            };
            cleaned.Append(c);
        }

        if (cleaned.Length != Length)
        {
            return false;
        }

        ulong acc = 0;
        for (int i = 0; i < Length; i++)
        {
            int digit = Alphabet.IndexOf(cleaned[i]);
            if (digit < 0)
            {
                return false;
            }

            acc = (acc << 5) | (uint)digit;
        }

        if (acc > uint.MaxValue)
        {
            return false;
        }

        value = (uint)acc;
        return true;
    }

    /// <summary>입력을 표준 표기(<see cref="Format"/>)로 맞춘다. 형식이 아니면 null.</summary>
    public static string Normalize(string text) =>
        TryParse(text, out uint value) ? Format(value) : null;
}
