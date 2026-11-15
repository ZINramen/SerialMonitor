using System.Buffers.Binary;

namespace SerialMonitor.Core.Protocol;

/// <summary>
/// 장비가 보내는 측정값 하나.
///
/// 페이로드 배치 (9바이트):
/// <code>
///  +---------+---------------------+---------------------+
///  | CHANNEL |   DEVICE TIME (ms)  |   VALUE (1/1000)    |
///  | 1 byte  |  uint32 little-end  |  int32 little-end   |
///  +---------+---------------------+---------------------+
/// </code>
///
/// 값을 실수가 아니라 1/1000 단위 정수로 보내는 이유:
/// 장비 쪽 MCU는 부동소수점이 느리거나 없는 경우가 많고, 정수로 보내면
/// 양쪽 해석이 어긋날 일이 없다. 실제 계측 프로토콜에서 흔한 방식이다.
/// </summary>
public readonly record struct Measurement(
    byte Channel,
    uint DeviceTimeMs,
    int RawValue)
{
    public const int PayloadLength = 9;

    /// <summary>사람이 읽는 단위로 환산한 값.</summary>
    public double Value => RawValue / 1000.0;

    public byte[] ToPayload()
    {
        var payload = new byte[PayloadLength];
        payload[0] = Channel;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1, 4), DeviceTimeMs);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5, 4), RawValue);
        return payload;
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out Measurement measurement)
    {
        if (payload.Length != PayloadLength)
        {
            measurement = default;
            return false;
        }

        measurement = new Measurement(
            payload[0],
            BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(5, 4)));
        return true;
    }

    public static Measurement FromValue(byte channel, uint deviceTimeMs, double value)
        => new(channel, deviceTimeMs, (int)Math.Round(value * 1000.0));
}
