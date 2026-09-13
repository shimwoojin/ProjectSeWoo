using System;
using System.Diagnostics;
using Godot;

namespace ProjectSeWoo;

/// <summary>
/// 프로세스 CPU 점유율과 메모리를 직접 샘플링한다.
///
/// Day 1-2 합격 기준이 "유휴 CPU &lt; 1%, 메모리 &lt; 150MB"인데,
/// 작업관리자를 곁눈질하면서 측정하면 정확도도 기록성도 떨어진다.
/// 그래서 앱 안에서 읽고, F9로 그대로 복사해 붙일 수 있게 한다.
///
/// CPU% 계산은 작업관리자와 같은 방식이다:
///   (프로세스가 쓴 CPU 시간) / (흐른 실제 시간 × 논리 코어 수) × 100
/// </summary>
public sealed class PerfProbe
{
    private readonly Process _proc = Process.GetCurrentProcess();
    // Godot.Environment(월드 환경)와 System.Environment 이름이 겹친다.
    // Godot C#에서 자주 밟는 함정이라 정규화해서 쓴다.
    private readonly int _cores = System.Environment.ProcessorCount;

    private TimeSpan _lastCpuTime;
    private long _lastStamp;

    /// <summary>직전 샘플 구간의 CPU 점유율(%).</summary>
    public double CpuPercent { get; private set; }

    /// <summary>기동 이후 최댓값. 드래그 같은 순간 부하를 잡기 위한 것.</summary>
    public double PeakCpuPercent { get; private set; }

    /// <summary>측정 시작 이후 평균. 유휴 부하 판정은 이 값으로 한다.</summary>
    public double AvgCpuPercent => _samples > 0 ? _cpuSum / _samples : 0.0;

    private double _cpuSum;
    private int _samples;

    /// <summary>
    /// 전체 작업 집합. D3D12 런타임 / GPU 드라이버 / .NET 런타임처럼 다른 프로세스와
    /// 공유되는 DLL 페이지까지 포함하므로, 작업관리자의 "메모리" 열보다 크게 나온다.
    /// 이 값을 150MB 기준에 그대로 대면 우리 앱이 실제로 점유하는 양을 과대평가한다.
    /// </summary>
    public long WorkingSetMb => _proc.WorkingSet64 / (1024 * 1024);

    /// <summary>
    /// 개인 커밋. 작업관리자의 "커밋 크기" 열과 같은 값이고, 공유 페이지가 빠진다.
    /// 상주 앱이 실제로 차지하는 양에 더 가까우므로 150MB 판정은 이 값으로 한다.
    /// </summary>
    public long PrivateCommitMb => _proc.PrivateMemorySize64 / (1024 * 1024);

    /// <summary>Godot이 직접 할당한 메모리. .NET 힙은 포함되지 않는다.</summary>
    public long GodotStaticMb => (long)(OS.GetStaticMemoryUsage() / (1024 * 1024));

    /// <summary>.NET GC 힙. C# 전환 비용이 얼마인지 보려고 따로 뽑는다.</summary>
    public long ManagedHeapMb => GC.GetTotalMemory(false) / (1024 * 1024);

    public int Cores => _cores;

    public PerfProbe()
    {
        _lastCpuTime = _proc.TotalProcessorTime;
        _lastStamp = Stopwatch.GetTimestamp();
    }

    /// <summary>주기적으로(0.5~1초) 호출한다. 더 자주 부르면 값이 튄다.</summary>
    public void Sample()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = (now - _lastStamp) / (double)Stopwatch.Frequency;
        if (elapsed < 0.05)
        {
            return;
        }

        _proc.Refresh();
        TimeSpan cpuNow = _proc.TotalProcessorTime;
        double used = (cpuNow - _lastCpuTime).TotalSeconds;

        _lastCpuTime = cpuNow;
        _lastStamp = now;

        CpuPercent = Math.Max(0.0, used / (elapsed * _cores) * 100.0);
        _cpuSum += CpuPercent;
        _samples++;

        if (CpuPercent > PeakCpuPercent)
        {
            PeakCpuPercent = CpuPercent;
        }
    }

    /// <summary>측정 구간을 다시 시작한다. 설정을 바꾸고 재측정할 때 쓴다.</summary>
    public void Reset()
    {
        PeakCpuPercent = 0.0;
        _cpuSum = 0.0;
        _samples = 0;
    }
}
