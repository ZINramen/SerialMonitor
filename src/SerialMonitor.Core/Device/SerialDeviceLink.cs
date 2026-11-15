using System.IO.Ports;

namespace SerialMonitor.Core.Device;

/// <summary>실제 시리얼 포트(RS-232 / USB-시리얼 변환기 / RS-422 컨버터) 연결.</summary>
public sealed class SerialDeviceLink : IDeviceLink
{
    private readonly SerialPort _port;

    public SerialDeviceLink(string portName, int baudRate)
    {
        // 통신 속도·패리티·데이터 비트·정지 비트는 장비 규격서에 적힌 값과
        // 정확히 같아야 한다. 하나라도 다르면 선은 멀쩡해도 글자가 깨져서 들어온다.
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 200,
            WriteTimeout = 500,
        };
        Name = $"{portName} @ {baudRate}bps";
    }

    public string Name { get; }

    public bool IsOpen => _port.IsOpen;

    public static string[] GetAvailablePortNames() => SerialPort.GetPortNames();

    public void Open()
    {
        if (!_port.IsOpen)
            _port.Open();
    }

    public void Close()
    {
        if (_port.IsOpen)
            _port.Close();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        _port.Write(data.ToArray(), 0, data.Length);
    }

    public int Read(byte[] buffer, int offset, int count, TimeSpan timeout)
    {
        _port.ReadTimeout = Math.Max(1, (int)timeout.TotalMilliseconds);
        try
        {
            return _port.Read(buffer, offset, count);
        }
        catch (TimeoutException)
        {
            // 시리얼에서 무응답은 정상적으로 일어나는 일이다.
            // 예외로 올리지 않고 0을 돌려준 뒤 위쪽에서 타임아웃 정책을 적용한다.
            return 0;
        }
    }

    public void Dispose()
    {
        Close();
        _port.Dispose();
    }
}
