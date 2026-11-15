namespace SerialMonitor.Core.Device;

/// <summary>
/// 통신 품질 집계. 현장에서는 재현이 어려워서, 무슨 일이 몇 번 있었는지
/// 숫자로 남겨두지 않으면 원인을 못 찾는다.
/// </summary>
public sealed class SessionStatistics
{
    public long ReceivedFrames { get; internal set; }
    public long ReceivedMeasurements { get; internal set; }
    public long CrcErrors { get; internal set; }
    public long DiscardedBytes { get; internal set; }

    /// <summary>SEQ 번호가 건너뛴 만큼 센 유실 수.</summary>
    public long LostFrames { get; internal set; }

    public long Timeouts { get; internal set; }
    public long Retries { get; internal set; }
    public long Reconnects { get; internal set; }

    public long OkCount { get; internal set; }
    public long NgCount { get; internal set; }

    public double FramesPerSecond { get; internal set; }

    public long TotalJudged => OkCount + NgCount;

    public double NgRate => TotalJudged == 0 ? 0 : (double)NgCount / TotalJudged;

    /// <summary>받았어야 할 것 대비 유실 비율.</summary>
    public double LossRate
    {
        get
        {
            long expected = ReceivedFrames + LostFrames;
            return expected == 0 ? 0 : (double)LostFrames / expected;
        }
    }

    public SessionStatistics Snapshot() => (SessionStatistics)MemberwiseClone();

    public void Reset()
    {
        ReceivedFrames = 0;
        ReceivedMeasurements = 0;
        CrcErrors = 0;
        DiscardedBytes = 0;
        LostFrames = 0;
        Timeouts = 0;
        Retries = 0;
        Reconnects = 0;
        OkCount = 0;
        NgCount = 0;
        FramesPerSecond = 0;
    }
}
