using SerialMonitor.Core.Judging;

namespace SerialMonitor.Core.Device;

/// <summary>판정까지 끝난 측정값 하나. 화면과 기록이 함께 쓰는 단위.</summary>
public readonly record struct Sample(
    byte Channel,
    byte Sequence,
    uint DeviceTimeMs,
    double Value,
    Verdict Verdict,
    DateTime ReceivedAtUtc);

public enum DeviceState
{
    Disconnected,
    Connecting,
    Connected,
    /// <summary>연결은 살아있지만 응답이 끊긴 상태.</summary>
    NotResponding,
    Faulted,
}
