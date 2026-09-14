using System;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform.Mocks;

/// <summary>
/// <see cref="IInputSource"/> 목 구현.
///
/// 기획서 §8-2 의 요지는 "갑이 실물을 만드는 동안 을은 목으로 게임 레이어를 돌린다" 이다.
/// 그러려면 목이 **갑 쪽에 있어야 한다** — 을이 직접 만들면 인터페이스 해석이
/// 갈라지고, 실물로 갈아끼울 때 그 차이가 드러난다.
///
/// 전역 후킹을 하지 않는다. 포커스가 있는 동안의 Godot 입력만 센다.
/// 실물(A4)은 RawInput 으로 포커스 없이도 받지만, 게임 레이어 입장에서는
/// "가끔 <see cref="OnKeystrokes"/> 가 온다" 로 똑같다.
/// </summary>
public sealed class MockInputSource : IInputSource
{
    private const double BatchSeconds = 0.1;

    /// <summary>초당 캡 (§6). 실물과 같은 값을 써서 게임 레이어가 같은 상한을 본다.</summary>
    private const int PerSecondCap = 10;

    private double _batchElapsed;
    private double _secondElapsed;
    private int _pending;
    private int _thisSecond;

    public event Action<int> OnKeystrokes;

    public long TotalCount { get; private set; }

    /// <summary>목은 항상 쓸 수 있다. 폴백 경로를 시험하려면 이 값을 바꿔서 켠다.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>키가 눌렸다고 알린다. 게임 씬의 _Input 에서 호출한다.</summary>
    public void Feed(int count = 1)
    {
        if (!IsAvailable)
        {
            return;
        }

        // 캡을 **여기서** 적용한다. 이벤트를 받는 쪽이 캡을 신경 쓰지 않게 하는 것이
        // 이 인터페이스의 계약이다.
        int room = Math.Max(0, PerSecondCap - _thisSecond);
        int taken = Math.Min(count, room);

        _thisSecond += taken;
        _pending += taken;
    }

    /// <summary>매 프레임 호출한다. 100ms 배치와 초당 캡 창을 굴린다.</summary>
    public void Tick(double delta)
    {
        _secondElapsed += delta;
        if (_secondElapsed >= 1.0)
        {
            _secondElapsed = 0.0;
            _thisSecond = 0;
        }

        _batchElapsed += delta;
        if (_batchElapsed < BatchSeconds)
        {
            return;
        }

        _batchElapsed = 0.0;

        if (_pending <= 0)
        {
            return;
        }

        int batch = _pending;
        _pending = 0;
        TotalCount += batch;
        OnKeystrokes?.Invoke(batch);
    }
}
