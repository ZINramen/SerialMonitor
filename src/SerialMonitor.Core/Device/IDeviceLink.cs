namespace SerialMonitor.Core.Device;

/// <summary>
/// 장비와의 연결 통로. 실제 시리얼 포트일 수도, 가짜 장비일 수도 있다.
///
/// 이 인터페이스가 이 프로젝트의 핵심 설계다.
/// 장비가 손에 없는 상태에서 개발을 시작해야 했기 때문에, 통신 계층을
/// 인터페이스로 끊어두고 가짜 장비를 먼저 붙였다. 실제 장비가 생기면
/// 구현체만 갈아끼우면 되고, 위쪽 코드는 한 줄도 바뀌지 않는다.
/// 테스트에서도 같은 자리에 테스트용 구현을 넣을 수 있다.
/// </summary>
public interface IDeviceLink : IDisposable
{
    string Name { get; }

    bool IsOpen { get; }

    void Open();

    void Close();

    /// <summary>명령 바이트를 장비로 보낸다.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>
    /// 받은 바이트를 버퍼에 채우고 채운 길이를 돌려준다.
    /// 지정한 시간 안에 아무것도 오지 않으면 0을 돌려준다(예외를 던지지 않는다).
    /// </summary>
    int Read(byte[] buffer, int offset, int count, TimeSpan timeout);
}
