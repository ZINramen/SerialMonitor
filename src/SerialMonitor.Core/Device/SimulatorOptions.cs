namespace SerialMonitor.Core.Device;

/// <summary>
/// 가짜 장비의 동작 설정. 정상 동작만이 아니라 "고장난 상황"을 일부러
/// 만들어낼 수 있어야 예외 처리를 미리 검증할 수 있다.
/// </summary>
public sealed class SimulatorOptions
{
    /// <summary>채널당 초당 측정 횟수.</summary>
    public int SampleRateHz { get; set; } = 20;

    public int ChannelCount { get; set; } = 4;

    /// <summary>프레임을 아예 보내지 않을 확률. 유실(SEQ 끊김)을 만든다.</summary>
    public double DropRate { get; set; } = 0.01;

    /// <summary>바이트 하나를 일부러 망가뜨릴 확률. CRC 오류를 만든다.</summary>
    public double CorruptRate { get; set; } = 0.01;

    /// <summary>프레임 앞에 쓰레기 바이트를 끼워 넣을 확률. 재동기를 시험한다.</summary>
    public double GarbageRate { get; set; } = 0.005;

    /// <summary>잠시 응답을 멈출 확률. 타임아웃과 재시도를 시험한다.</summary>
    public double StallRate { get; set; } = 0.002;

    public TimeSpan StallDuration { get; set; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>측정값의 기준치(접촉 저항 mΩ 기준).</summary>
    public double BaseValue { get; set; } = 25.0;

    /// <summary>평상시 흔들림 폭.</summary>
    public double NoiseAmplitude { get; set; } = 2.0;

    /// <summary>불량에 해당하는 값이 튀어나올 확률.</summary>
    public double SpikeRate { get; set; } = 0.01;

    public int RandomSeed { get; set; } = Environment.TickCount;

    public static SimulatorOptions Clean() => new()
    {
        DropRate = 0,
        CorruptRate = 0,
        GarbageRate = 0,
        StallRate = 0,
        SpikeRate = 0,
    };
}
