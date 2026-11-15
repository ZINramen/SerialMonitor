namespace SerialMonitor.Core.Protocol;

/// <summary>
/// 바이트 흐름에서 프레임을 끊어내는 파서.
///
/// 시리얼이나 TCP로 들어오는 데이터는 "패킷"이 아니라 그냥 바이트가 계속
/// 흘러들어온다. 한 번 읽었을 때 프레임이 반만 와 있을 수도 있고, 두 개 반이
/// 붙어 올 수도 있다. 그래서 받은 바이트를 버퍼에 쌓아두고, 완전한 프레임이
/// 만들어질 때마다 꺼내는 구조가 필요하다.
///
/// 이 클래스는 상태를 가지므로 스레드 안전하지 않다. 수신 스레드 하나에서만 쓴다.
/// </summary>
public sealed class FrameParser
{
    private byte[] _buffer = new byte[4096];
    private int _length;

    /// <summary>CRC가 맞지 않아 버린 프레임 수.</summary>
    public long CrcErrorCount { get; private set; }

    /// <summary>동기가 깨져서 버린 바이트 수.</summary>
    public long DiscardedByteCount { get; private set; }

    /// <summary>정상적으로 꺼낸 프레임 수.</summary>
    public long FrameCount { get; private set; }

    public void Reset()
    {
        _length = 0;
    }

    /// <summary>
    /// 새로 받은 바이트를 넣고, 그 결과로 완성된 프레임들을 돌려준다.
    /// 완성된 게 없으면 빈 목록.
    /// </summary>
    public IReadOnlyList<Frame> Append(ReadOnlySpan<byte> incoming)
    {
        EnsureCapacity(_length + incoming.Length);
        incoming.CopyTo(_buffer.AsSpan(_length));
        _length += incoming.Length;

        List<Frame>? frames = null;
        int consumed = 0;

        while (true)
        {
            int available = _length - consumed;
            if (available < Frame.OverheadBytes)
                break;

            var span = _buffer.AsSpan(consumed, available);

            // 1) STX 찾기. 앞에 쓰레기가 있으면 버리고 동기를 다시 잡는다.
            if (span[0] != Frame.Stx)
            {
                int stxIndex = span.IndexOf(Frame.Stx);
                if (stxIndex < 0)
                {
                    DiscardedByteCount += available;
                    consumed = _length;
                    break;
                }
                DiscardedByteCount += stxIndex;
                consumed += stxIndex;
                continue;
            }

            // 2) 길이를 읽어 프레임 전체가 도착했는지 본다.
            int payloadLength = span[1];
            int totalLength = payloadLength + Frame.OverheadBytes;
            if (available < totalLength)
                break; // 아직 덜 왔다. 다음 수신까지 기다린다.

            // 3) ETX 자리가 맞는지 확인. 틀리면 이 STX는 페이로드 안의 0x02였을 수 있다.
            if (span[totalLength - 1] != Frame.Etx)
            {
                DiscardedByteCount++;
                consumed++;
                continue;
            }

            // 4) CRC 검사. 범위는 LEN부터 페이로드 끝까지.
            ushort expected = Crc16.Compute(span.Slice(1, 3 + payloadLength));
            int crcOffset = 4 + payloadLength;
            ushort actual = (ushort)(span[crcOffset] | (span[crcOffset + 1] << 8));

            if (expected != actual)
            {
                // 깨진 데이터는 버린다. 단 한 바이트만 버리고 다시 찾는 이유는,
                // 이 STX 자체가 가짜였을 수 있어서 프레임 하나를 통째로 버리면
                // 바로 뒤에 있는 진짜 프레임까지 같이 날아갈 수 있기 때문이다.
                CrcErrorCount++;
                DiscardedByteCount++;
                consumed++;
                continue;
            }

            // 5) 정상 프레임.
            var payload = payloadLength == 0
                ? Array.Empty<byte>()
                : span.Slice(4, payloadLength).ToArray();

            frames ??= new List<Frame>(4);
            frames.Add(new Frame(span[2], (CommandCode)span[3], payload));
            FrameCount++;
            consumed += totalLength;
        }

        if (consumed > 0)
        {
            int remaining = _length - consumed;
            if (remaining > 0)
                _buffer.AsSpan(consumed, remaining).CopyTo(_buffer);
            _length = remaining;
        }

        return (IReadOnlyList<Frame>?)frames ?? Array.Empty<Frame>();
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length)
            return;

        int size = _buffer.Length;
        while (size < required)
            size *= 2;

        Array.Resize(ref _buffer, size);
    }
}
