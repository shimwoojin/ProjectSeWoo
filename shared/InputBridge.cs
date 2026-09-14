using System;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 게임과 입력 헬퍼 프로세스가 공유하는 메모리 규약
/// (기획확정-일감분배-260907.md A4 / WEEK0-GODOT-VALIDATION.md §3 의 C안).
///
/// <b>이 파일은 Godot 을 참조하지 않는다.</b> 헬퍼가 그대로 링크해서 쓰기 때문이다.
/// 규약을 한 곳에만 두려는 것이고, 양쪽에 상수를 복사해 두면 언젠가 한쪽만 바뀐다.
///
/// 파이프가 아니라 공유 메모리를 쓰는 이유:
/// 게임이 필요로 하는 것은 "지금까지 몇 타" 라는 <b>누적값 하나</b>이고, 100ms 마다
/// 읽으면 충분하다. 연결 관리·재접속·블로킹이 없는 쪽이 상주 앱에 맞고, 헬퍼가
/// 죽었다 살아나도 게임 쪽 코드가 바뀔 게 없다.
/// </summary>
public static class InputBridge
{
    /// <summary>
    /// 매핑 이름. 게임 PID 를 붙여서 인스턴스끼리 안 섞이게 한다.
    /// <c>Local\</c> 접두사라 같은 세션 안에서만 보인다.
    /// </summary>
    public static string MapName(int gamePid) => $"Local\\ProjectSeWooInput-{gamePid}";

    /// <summary>공유 블록 크기. 지금은 40바이트면 되지만 여유를 둔다.</summary>
    public const int Size = 64;

    // 레이아웃. 전부 long(8바이트) 정렬이라 x64 에서 읽기/쓰기가 원자적이다.
    // 락이 없어도 찢어진 값을 볼 일이 없다는 뜻이고, 그래서 이 구조를 골랐다.
    public const int OffsetMagic = 0;
    public const int OffsetTotal = 8;
    public const int OffsetDroppedCap = 16;
    public const int OffsetDroppedDecay = 24;
    public const int OffsetHeartbeat = 32;

    /// <summary>
    /// 헬퍼가 제대로 붙었는지 확인하는 표식. 0 이면 아직 아무도 안 썼다는 뜻이고,
    /// 그걸 "헬퍼가 죽었다" 와 구분하려고 둔다.
    /// </summary>
    public const long Magic = 0x5345574F_494E_0001;

    /// <summary>
    /// 심장박동이 이 시간보다 오래되면 헬퍼가 죽은 것으로 본다.
    /// 헬퍼는 200ms 마다 찍으므로 넉넉한 값이다.
    /// </summary>
    public const long HeartbeatTimeoutMs = 2_000;
}
