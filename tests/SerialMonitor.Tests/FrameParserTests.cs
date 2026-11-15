using SerialMonitor.Core.Protocol;
using Xunit;

namespace SerialMonitor.Tests;

/// <summary>
/// 파서 테스트. 통신에서 실제로 겪는 상황을 그대로 재현한다.
/// 장비 없이도 이 테스트만으로 수신 로직이 맞는지 매번 확인할 수 있다.
/// </summary>
public class FrameParserTests
{
    private static byte[] MeasurementFrame(byte sequence, byte channel, double value)
        => new Frame(
            sequence,
            CommandCode.Measurement,
            Measurement.FromValue(channel, 1234, value).ToPayload()).ToBytes();

    [Fact]
    public void 정상_프레임_하나를_읽는다()
    {
        var parser = new FrameParser();

        var frames = parser.Append(MeasurementFrame(7, 2, 25.5));

        var frame = Assert.Single(frames);
        Assert.Equal(7, frame.Sequence);
        Assert.Equal(CommandCode.Measurement, frame.Command);
        Assert.True(Measurement.TryParse(frame.Payload, out var measurement));
        Assert.Equal(2, measurement.Channel);
        Assert.Equal(25.5, measurement.Value, 3);
    }

    [Fact]
    public void 프레임이_반으로_잘려_들어와도_이어붙인다()
    {
        // 시리얼은 한 번 읽을 때 프레임이 반만 와 있는 경우가 흔하다.
        var parser = new FrameParser();
        var bytes = MeasurementFrame(1, 1, 30.0);

        var first = parser.Append(bytes.AsSpan(0, 6));
        Assert.Empty(first); // 아직 덜 왔으니 아무것도 나오면 안 된다.

        var second = parser.Append(bytes.AsSpan(6));
        Assert.Single(second);
    }

    [Fact]
    public void 한_번에_여러_프레임이_붙어_와도_모두_읽는다()
    {
        var parser = new FrameParser();
        var buffer = new List<byte>();
        for (byte i = 0; i < 5; i++)
            buffer.AddRange(MeasurementFrame(i, 1, 20 + i));

        var frames = parser.Append(buffer.ToArray());

        Assert.Equal(5, frames.Count);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, frames.Select(f => f.Sequence));
    }

    [Fact]
    public void 앞에_쓰레기_바이트가_섞여도_동기를_다시_잡는다()
    {
        var parser = new FrameParser();
        var payload = new List<byte> { 0xAA, 0xBB, 0xCC };
        payload.AddRange(MeasurementFrame(9, 3, 11.1));

        var frames = parser.Append(payload.ToArray());

        Assert.Single(frames);
        Assert.Equal(9, frames[0].Sequence);
        Assert.Equal(3, parser.DiscardedByteCount);
    }

    [Fact]
    public void CRC가_틀린_프레임은_버린다()
    {
        var parser = new FrameParser();
        var bytes = MeasurementFrame(4, 1, 22.0);
        bytes[6] ^= 0x01; // 페이로드 한 비트를 뒤집는다.

        var frames = parser.Append(bytes);

        Assert.Empty(frames);
        Assert.Equal(1, parser.CrcErrorCount);
    }

    [Fact]
    public void 깨진_프레임_뒤에_오는_정상_프레임은_살린다()
    {
        // 깨진 프레임을 통째로 버리면 바로 뒤의 정상 프레임까지 날아갈 수 있다.
        var parser = new FrameParser();
        var broken = MeasurementFrame(1, 1, 10.0);
        broken[5] ^= 0x02;

        var buffer = new List<byte>();
        buffer.AddRange(broken);
        buffer.AddRange(MeasurementFrame(2, 1, 12.0));

        var frames = parser.Append(buffer.ToArray());

        Assert.Single(frames);
        Assert.Equal(2, frames[0].Sequence);
        Assert.True(parser.CrcErrorCount >= 1);
    }

    [Fact]
    public void 페이로드에_STX가_들어있어도_정상_처리한다()
    {
        // 0x02는 측정값 안에도 얼마든지 나올 수 있다.
        // 길이 필드와 ETX 위치로 걸러내지 못하면 여기서 깨진다.
        var parser = new FrameParser();
        var measurement = new Measurement(Frame.Stx, 0x02020202, 0x02020202);
        var bytes = new Frame(1, CommandCode.Measurement, measurement.ToPayload()).ToBytes();

        var frames = parser.Append(bytes);

        var frame = Assert.Single(frames);
        Assert.True(Measurement.TryParse(frame.Payload, out var parsed));
        Assert.Equal(measurement, parsed);
    }

    [Fact]
    public void 바이트를_하나씩_넣어도_결과가_같다()
    {
        // 극단적으로 느린 회선을 흉내낸다.
        var parser = new FrameParser();
        var bytes = MeasurementFrame(42, 4, 33.3);

        int produced = 0;
        foreach (var b in bytes)
            produced += parser.Append(new[] { b }).Count;

        Assert.Equal(1, produced);
    }

    [Fact]
    public void 빈_페이로드_프레임도_읽는다()
    {
        var parser = new FrameParser();
        var bytes = new Frame(3, CommandCode.Ack, Array.Empty<byte>()).ToBytes();

        var frames = parser.Append(bytes);

        var frame = Assert.Single(frames);
        Assert.Equal(CommandCode.Ack, frame.Command);
        Assert.Empty(frame.Payload);
    }
}
