using System;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform;

/// <summary>
/// <see cref="ISaveStore"/> 실물 (A3 의 <see cref="SaveIO"/> 위에 얹는다).
///
/// 기동 때 한 번 읽고, 그 뒤로는 메모리의 <see cref="Data"/> 하나만 산다.
/// 디스크 쓰기는 <see cref="MinWriteIntervalSec"/> 간격으로 묶는다.
/// </summary>
public sealed class SaveStore : ISaveStore
{
    /// <summary>
    /// 연속 쓰기 사이의 최소 간격(초).
    ///
    /// 타이핑하는 동안은 100ms 배치마다 누적 타수가 바뀌므로 <see cref="MarkDirty"/>
    /// 가 초당 열 번씩 온다. 그걸 그대로 쓰면 상주 앱이 디스크를 계속 두드린다.
    /// **10초를 잃어도 되는 이유는 오프라인 성장이 그 구멍을 메우기 때문이다** -
    /// 아래 <see cref="Write"/> 의 <c>LastQuitUtc</c> 설명 참고.
    /// </summary>
    private const double MinWriteIntervalSec = 10.0;

    /// <summary>
    /// 무인 실행(<c>--selftest</c>, <c>--report=</c>, 헤드리스)인가.
    ///
    /// true 면 <b>디스크에 한 글자도 안 쓴다.</b> A3 실측에서 헤드리스 selftest 가
    /// 엉뚱한 창 위치를 유저의 진짜 세이브에 덮어쓴 적이 있다 - 이제는 게임 상태까지
    /// 같은 파일에 들어 있어서 같은 실수의 대가가 더 크다.
    /// </summary>
    private readonly bool _readOnly;

    private bool _dirty;
    private double _sinceWrite;

    public SaveStore(bool readOnly)
    {
        _readOnly = readOnly;

        // 읽기는 무인 실행에서도 한다. 측정 실행이 유저의 실제 설정(배율/투명도)
        // 그대로 떠야 숫자가 의미를 갖는다.
        Data = SaveIO.Load();
    }

    public SaveData Data { get; }

    /// <summary>세이브 파일이 원래 있었는가. 첫 실행 판정에 쓴다.</summary>
    public bool HadFile { get; } = SaveIO.Exists();

    public void MarkDirty() => _dirty = true;

    /// <summary>매 프레임 부른다. 밀린 쓰기가 있으면 간격을 보고 흘려보낸다.</summary>
    public void Tick(double delta)
    {
        _sinceWrite += delta;

        if (_dirty && _sinceWrite >= MinWriteIntervalSec)
        {
            Write();
        }
    }

    public void FlushNow()
    {
        if (_dirty)
        {
            Write();
        }
    }

    private void Write()
    {
        _dirty = false;
        _sinceWrite = 0.0;

        if (_readOnly)
        {
            return;
        }

        // **매 쓰기마다 시각을 찍는다.** 필드 이름은 lastQuitUtc 지만 실제 의미는
        // "마지막으로 저장한 시각" 이다. 그렇게 하면 정상 종료와 강제 종료가 같은
        // 경로가 된다 - 크래시로 마지막 10초를 잃어도, 다음 실행의 오프라인 성장이
        // 그 10초를 벽시계 기준으로 그대로 메운다. 상주 앱은 강제 종료가 잦다(§7-5).
        Data.LastQuitUtc = DateTime.UtcNow;
        SaveIO.Save(Data);
    }
}
