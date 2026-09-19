using Godot;

namespace ProjectSeWoo.Shared.Mocks;

/// <summary>
/// <see cref="ISaveStore"/> 목. <b>디스크를 아예 안 만진다.</b>
///
/// 그게 요점이다 - <c>Shell.tscn</c> 없이 <c>game/GameRoot.tscn</c> 만 단독
/// 실행할 때 게임 레이어 실험이 유저의 진짜 세이브를 덮으면 안 된다. 매번
/// 기본값에서 시작하는 것이 오히려 편하기도 하다.
///
/// 오프라인 성장처럼 "저장된 값이 있어야" 시험할 수 있는 것은
/// <see cref="Data"/> 를 직접 채워 넣고 확인한다 - 예를 들어
/// <c>Data.LastQuitUtc = DateTime.UtcNow.AddHours(-3)</c>.
/// </summary>
public sealed class MockSaveStore : ISaveStore
{
    public SaveData Data { get; } = new();

    /// <summary><see cref="MarkDirty"/> 가 몇 번 왔는가. 저장을 부르긴 하는지 확인용.</summary>
    public int DirtyCount { get; private set; }

    /// <summary><see cref="FlushNow"/> 가 몇 번 왔는가.</summary>
    public int FlushCount { get; private set; }

    public void MarkDirty() => DirtyCount++;

    public void FlushNow()
    {
        FlushCount++;
        GD.Print($"[mock-save] flush (dirty {DirtyCount}회) - 디스크에는 안 쓴다");
    }
}
