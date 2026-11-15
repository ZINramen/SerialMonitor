namespace SerialMonitor.Core.Protocol;

/// <summary>명령 코드. 장비와 PC가 주고받는 패킷의 종류.</summary>
public enum CommandCode : byte
{
    Ping = 0x01,
    Ack = 0x02,
    Nak = 0x03,
    Measurement = 0x10,
    StartMeasure = 0x20,
    StopMeasure = 0x21,
}

/// <summary>
/// 장비 통신 프레임 한 개.
///
/// 바이트 배치 (총 길이 = PayloadLength + 7):
/// <code>
///  +-----+-----+-----+-----+----------------+---------+-----+
///  | STX | LEN | SEQ | CMD |    PAYLOAD     |  CRC16  | ETX |
///  | 02  |  1  |  1  |  1  |   LEN bytes    | 2 (LE)  | 03  |
///  +-----+-----+-----+-----+----------------+---------+-----+
///                   CRC 계산 범위: LEN ~ PAYLOAD 끝
/// </code>
///
/// 설계 의도
/// - LEN  : TCP·시리얼은 바이트 흐름이라 패킷 경계가 없다. 길이 필드가 있어야 어디서 끊을지 알 수 있다.
/// - SEQ  : 0~255 순환. 빠진 번호로 유실을, 같은 번호로 중복을 잡는다.
/// - CRC  : 중간에 깨진 데이터를 버리기 위한 값.
/// - STX/ETX : 쓰레기 바이트가 섞였을 때 다시 동기를 잡는 기준점.
/// </summary>
public readonly record struct Frame(byte Sequence, CommandCode Command, byte[] Payload)
{
    public const byte Stx = 0x02;
    public const byte Etx = 0x03;

    /// <summary>STX, LEN, SEQ, CMD, CRC(2), ETX = 7바이트</summary>
    public const int OverheadBytes = 7;

    public const int MaxPayloadLength = 255;

    public int TotalLength => Payload.Length + OverheadBytes;

    /// <summary>프레임을 전송용 바이트 배열로 만든다.</summary>
    public byte[] ToBytes()
    {
        if (Payload.Length > MaxPayloadLength)
            throw new ArgumentOutOfRangeException(nameof(Payload), "페이로드는 255바이트를 넘을 수 없습니다.");

        var buffer = new byte[TotalLength];
        buffer[0] = Stx;
        buffer[1] = (byte)Payload.Length;
        buffer[2] = Sequence;
        buffer[3] = (byte)Command;
        Payload.CopyTo(buffer, 4);

        // CRC 범위는 LEN(인덱스 1)부터 페이로드 끝까지.
        int crcRangeLength = 3 + Payload.Length;
        ushort crc = Crc16.Compute(buffer.AsSpan(1, crcRangeLength));

        // 리틀 엔디안으로 기록. 장비와 PC의 바이트 순서가 다르면
        // 바이트는 멀쩡해도 숫자가 전혀 다르게 읽히므로 한쪽으로 고정해 둔다.
        int crcOffset = 4 + Payload.Length;
        buffer[crcOffset] = (byte)(crc & 0xFF);
        buffer[crcOffset + 1] = (byte)(crc >> 8);
        buffer[crcOffset + 2] = Etx;

        return buffer;
    }
}
