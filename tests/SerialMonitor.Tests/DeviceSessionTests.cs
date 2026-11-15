using SerialMonitor.Core.Device;
using SerialMonitor.Core.Judging;
using Xunit;

namespace SerialMonitor.Tests;

/// <summary>
/// 시뮬레이터부터 판정까지 전체가 이어져 도는지 확인하는 통합 테스트.
/// 장비 없이도 수신 로직 전체를 매번 같은 조건에서 검증할 수 있다.
/// </summary>
public class DeviceSessionTests
{
    private static ChannelLimit[] Limits() => new[]
    {
        new ChannelLimit(1, "접촉 저항", 10.0, 40.0, "mΩ"),
        new ChannelLimit(2, "접촉 저항", 10.0, 40.0, "mΩ"),
    };

    private static List<Sample> Collect(SimulatorOptions options, TimeSpan duration, out SessionStatistics stats)
    {
        var link = new SimulatedDeviceLink(options);
        using var session = new DeviceSession(link, Limits());

        var collected = new List<Sample>();
        session.Start();

        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            session.Drain(collected);
            Thread.Sleep(20);
        }

        session.Drain(collected);
        stats = session.GetStatistics();
        session.Stop();

        return collected;
    }

    [Fact]
    public void 정상_조건에서는_오류_없이_측정값이_들어온다()
    {
        var options = SimulatorOptions.Clean();
        options.SampleRateHz = 100;
        options.ChannelCount = 2;
        options.RandomSeed = 1234;

        var samples = Collect(options, TimeSpan.FromSeconds(1.5), out var stats);

        Assert.NotEmpty(samples);
        Assert.Equal(0, stats.CrcErrors);
        Assert.Equal(0, stats.LostFrames);
        Assert.Equal(0, stats.DiscardedBytes);
        Assert.All(samples, s => Assert.InRange(s.Channel, (byte)1, (byte)2));
    }

    [Fact]
    public void 고장을_주입하면_유실과_CRC_오류가_집계된다()
    {
        var options = new SimulatorOptions
        {
            SampleRateHz = 100,
            ChannelCount = 2,
            RandomSeed = 4321,
            DropRate = 0.05,
            CorruptRate = 0.05,
            GarbageRate = 0.02,
            StallRate = 0,
            SpikeRate = 0,
        };

        var samples = Collect(options, TimeSpan.FromSeconds(2), out var stats);

        Assert.NotEmpty(samples);
        Assert.True(stats.LostFrames > 0, "유실이 집계되어야 합니다.");
        Assert.True(stats.CrcErrors > 0, "CRC 오류가 집계되어야 합니다.");
        Assert.True(stats.DiscardedBytes > 0, "버려진 바이트가 집계되어야 합니다.");

        // 오류가 섞여도 정상 프레임은 계속 들어와야 한다.
        Assert.True(stats.ReceivedMeasurements > 100,
            $"정상 수신이 너무 적습니다: {stats.ReceivedMeasurements}");
    }

    [Fact]
    public void 기준을_벗어난_값은_NG로_판정된다()
    {
        var options = SimulatorOptions.Clean();
        options.SampleRateHz = 100;
        options.ChannelCount = 2;
        options.RandomSeed = 777;
        options.BaseValue = 90.0;   // 상한 40을 한참 넘는 값
        options.NoiseAmplitude = 0.5;

        var samples = Collect(options, TimeSpan.FromSeconds(1), out var stats);

        Assert.NotEmpty(samples);
        Assert.All(samples, s => Assert.Equal(Verdict.Ng, s.Verdict));
        Assert.Equal(0, stats.OkCount);
        Assert.True(stats.NgCount > 0);
    }

    [Fact]
    public void 무응답이_길어지면_타임아웃과_재시도가_집계된다()
    {
        var options = SimulatorOptions.Clean();
        options.SampleRateHz = 50;
        options.ChannelCount = 1;
        options.RandomSeed = 55;
        options.StallRate = 1.0;                              // 항상 멈춘다
        options.StallDuration = TimeSpan.FromMilliseconds(500);

        var link = new SimulatedDeviceLink(options);
        using var session = new DeviceSession(link, Limits(), new SessionOptions
        {
            ReadTimeout = TimeSpan.FromMilliseconds(50),
            ResponseTimeout = TimeSpan.FromMilliseconds(200),
            MaxRetries = 2,
            ReconnectDelay = TimeSpan.FromMilliseconds(200),
        });

        session.Start();
        Thread.Sleep(2000);
        var stats = session.GetStatistics();
        session.Stop();

        Assert.True(stats.Timeouts > 0, "타임아웃이 집계되어야 합니다.");
        Assert.True(stats.Retries > 0, "재시도가 집계되어야 합니다.");
    }
}
