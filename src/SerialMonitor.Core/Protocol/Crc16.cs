namespace SerialMonitor.Core.Protocol;

/// <summary>
/// CRC-16/CCITT-FALSE. 산업 장비 프로토콜에서 가장 흔하게 쓰이는 체크값.
/// 다항식 0x1021, 초기값 0xFFFF, 반전 없음.
/// </summary>
/// <remarks>
/// 체크섬(단순 합)보다 검출력이 좋다. 특히 연속된 비트가 몰려서 깨지는
/// 버스트 오류를 잡아낸다. 시리얼은 노이즈로 비트가 튀는 일이 잦아서
/// 체크섬보다 CRC를 쓰는 편이 안전하다.
/// </remarks>
public static class Crc16
{
    private const ushort Polynomial = 0x1021;
    private const ushort InitialValue = 0xFFFF;

    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)(i << 8);
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ Polynomial)
                    : (ushort)(crc << 1);
            }
            table[i] = crc;
        }
        return table;
    }

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = InitialValue;
        foreach (byte b in data)
        {
            crc = (ushort)((crc << 8) ^ Table[(crc >> 8) ^ b]);
        }
        return crc;
    }
}
