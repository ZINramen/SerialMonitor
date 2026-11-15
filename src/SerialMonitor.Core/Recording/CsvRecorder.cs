using System.Globalization;
using System.Text;
using SerialMonitor.Core.Device;

namespace SerialMonitor.Core.Recording;

/// <summary>
/// 받은 값을 전부 CSV로 남긴다.
///
/// 화면은 사람이 볼 수 있을 만큼만 간추려 그리지만, 기록은 하나도 빠뜨리지 않는다.
/// 나중에 "그때 왜 불량이 났나"를 확인하려면 원본이 남아 있어야 한다.
/// </summary>
public sealed class CsvRecorder : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private int _sinceFlush;

    public CsvRecorder(string path)
    {
        FilePath = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // UTF-8 BOM을 붙여야 엑셀에서 한글이 깨지지 않는다.
        _writer = new StreamWriter(path, append: false, new UTF8Encoding(true));
        _writer.WriteLine("수신시각(UTC),채널,일련번호,장비시간(ms),측정값,판정");
    }

    public string FilePath { get; }

    public long WrittenRows { get; private set; }

    public void Write(Sample sample)
    {
        lock (_gate)
        {
            _writer.Write(sample.ReceivedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(sample.Channel);
            _writer.Write(',');
            _writer.Write(sample.Sequence);
            _writer.Write(',');
            _writer.Write(sample.DeviceTimeMs);
            _writer.Write(',');
            _writer.Write(sample.Value.ToString("0.###", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.WriteLine(sample.Verdict);

            WrittenRows++;

            // 매번 flush하면 디스크 때문에 느려지고, 아예 안 하면 비정상 종료 시 날아간다.
            if (++_sinceFlush >= 200)
            {
                _writer.Flush();
                _sinceFlush = 0;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
