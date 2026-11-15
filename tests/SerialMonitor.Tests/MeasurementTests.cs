using SerialMonitor.Core.Judging;
using SerialMonitor.Core.Protocol;
using Xunit;

namespace SerialMonitor.Tests;

public class MeasurementTests
{
    [Theory]
    [InlineData(1, 0u, 0.0)]
    [InlineData(4, 65_000u, 25.123)]
    [InlineData(2, uint.MaxValue, -12.5)]
    public void 직렬화한_뒤_다시_읽으면_같은_값이_나온다(byte channel, uint time, double value)
    {
        var original = Measurement.FromValue(channel, time, value);

        Assert.True(Measurement.TryParse(original.ToPayload(), out var restored));

        Assert.Equal(original, restored);
        Assert.Equal(value, restored.Value, 3);
    }

    [Fact]
    public void 페이로드_길이가_다르면_거부한다()
    {
        Assert.False(Measurement.TryParse(new byte[5], out _));
        Assert.False(Measurement.TryParse(new byte[12], out _));
    }

    [Fact]
    public void 값은_리틀_엔디안으로_기록된다()
    {
        // 장비와 PC의 바이트 순서가 어긋나면 바이트는 멀쩡해도
        // 숫자가 전혀 다르게 읽힌다. 규격을 한쪽으로 고정했는지 확인한다.
        var measurement = new Measurement(1, 0x04030201, 0x08070605);

        var payload = measurement.ToPayload();

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, payload[1..5]);
        Assert.Equal(new byte[] { 0x05, 0x06, 0x07, 0x08 }, payload[5..9]);
    }
}

public class ChannelLimitTests
{
    private static readonly ChannelLimit Limit = new(1, "접촉 저항", 10.0, 40.0, "mΩ");

    [Theory]
    [InlineData(10.0, Verdict.Ok)]   // 하한 경계는 합격
    [InlineData(40.0, Verdict.Ok)]   // 상한 경계는 합격
    [InlineData(25.0, Verdict.Ok)]
    [InlineData(9.999, Verdict.Ng)]
    [InlineData(40.001, Verdict.Ng)]
    public void 상하한_경계를_포함해_판정한다(double value, Verdict expected)
    {
        Assert.Equal(expected, Limit.Judge(value));
    }

    [Fact]
    public void 하한이_상한보다_크면_만들_수_없다()
    {
        Assert.Throws<ArgumentException>(() => new ChannelLimit(1, "잘못된 기준", 50, 10, "mΩ"));
    }
}
