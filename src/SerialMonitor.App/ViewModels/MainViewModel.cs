using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using SerialMonitor.Core.Device;
using SerialMonitor.Core.Judging;
using SerialMonitor.Core.Recording;

namespace SerialMonitor.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// 화면 갱신 주기. 초당 25번.
    /// 수신은 이보다 훨씬 빠르지만 화면을 그만큼 그릴 이유가 없다.
    /// 수신 주기와 화면 주기를 떼어놓는 것이 이 프로그램의 핵심이다.
    /// </summary>
    private static readonly TimeSpan UiInterval = TimeSpan.FromMilliseconds(40);

    private readonly DispatcherTimer _uiTimer;
    private readonly List<Sample> _drainBuffer = new(8192);
    private readonly Dictionary<byte, ChannelViewModel> _channelMap = new();

    private DeviceSession? _session;
    private CsvRecorder? _recorder;

    private bool _useSimulator = true;
    private string _selectedPort = string.Empty;
    private int _selectedBaudRate = 115200;
    private bool _injectFaults = true;
    private bool _recordCsv;
    private string _statusText = "연결 안 됨";
    private string _recordingText = "기록 안 함";
    private DeviceState _state = DeviceState.Disconnected;
    private SessionStatistics _statistics = new();

    public MainViewModel()
    {
        // 규격서에 적힌 합격 기준에 해당하는 부분.
        var limits = new[]
        {
            new ChannelLimit(1, "접촉 저항", 10.0, 40.0, "mΩ"),
            new ChannelLimit(2, "접촉 저항", 10.0, 40.0, "mΩ"),
            new ChannelLimit(3, "접촉 저항", 12.0, 42.0, "mΩ"),
            new ChannelLimit(4, "접촉 저항", 12.0, 42.0, "mΩ"),
        };

        foreach (var limit in limits)
        {
            var vm = new ChannelViewModel(limit);
            Channels.Add(vm);
            _channelMap[limit.Channel] = vm;
        }

        RefreshPorts();

        ConnectCommand = new RelayCommand(Connect, () => _session is null);
        DisconnectCommand = new RelayCommand(Disconnect, () => _session is not null);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        ClearLogCommand = new RelayCommand(() => LogLines.Clear());

        _uiTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = UiInterval };
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();
    }

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    public ObservableCollection<string> AvailablePorts { get; } = new();

    public ObservableCollection<string> LogLines { get; } = new();

    public int[] BaudRates { get; } = { 9600, 19200, 38400, 57600, 115200, 230400 };

    public RelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public bool UseSimulator
    {
        get => _useSimulator;
        set
        {
            if (Set(ref _useSimulator, value))
                Raise(nameof(IsSerialMode));
        }
    }

    public bool IsSerialMode => !_useSimulator;

    public string SelectedPort
    {
        get => _selectedPort;
        set => Set(ref _selectedPort, value);
    }

    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => Set(ref _selectedBaudRate, value);
    }

    /// <summary>켜면 시뮬레이터가 유실·CRC 오류·무응답을 섞어 보낸다.</summary>
    public bool InjectFaults
    {
        get => _injectFaults;
        set => Set(ref _injectFaults, value);
    }

    public bool RecordCsv
    {
        get => _recordCsv;
        set => Set(ref _recordCsv, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string RecordingText
    {
        get => _recordingText;
        private set => Set(ref _recordingText, value);
    }

    public DeviceState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public SessionStatistics Statistics
    {
        get => _statistics;
        private set => Set(ref _statistics, value);
    }

    public bool IsConnected => _session is not null;

    private void RefreshPorts()
    {
        AvailablePorts.Clear();

        try
        {
            foreach (var name in SerialDeviceLink.GetAvailablePortNames().OrderBy(n => n))
                AvailablePorts.Add(name);
        }
        catch (Exception ex)
        {
            AppendLog($"포트 목록을 읽지 못했습니다: {ex.Message}");
        }

        if (AvailablePorts.Count == 0)
        {
            AppendLog("사용 가능한 시리얼 포트가 없습니다. 시뮬레이터로 진행하세요.");
            SelectedPort = string.Empty;
        }
        else if (!AvailablePorts.Contains(SelectedPort))
        {
            SelectedPort = AvailablePorts[0];
        }
    }

    private void Connect()
    {
        if (_session is not null)
            return;

        IDeviceLink link;

        try
        {
            if (UseSimulator)
            {
                var options = InjectFaults ? new SimulatorOptions() : SimulatorOptions.Clean();
                link = new SimulatedDeviceLink(options);
                AppendLog(InjectFaults
                    ? "시뮬레이터 연결 (유실·CRC 오류·무응답 주입 켜짐)"
                    : "시뮬레이터 연결 (정상 동작만)");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(SelectedPort))
                {
                    AppendLog("포트를 선택하세요.");
                    return;
                }
                link = new SerialDeviceLink(SelectedPort, SelectedBaudRate);
                AppendLog($"시리얼 연결 시도: {SelectedPort} @ {SelectedBaudRate}bps");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"연결을 만들지 못했습니다: {ex.Message}");
            return;
        }

        foreach (var channel in Channels)
            channel.Reset();

        if (RecordCsv)
            StartRecording();

        var session = new DeviceSession(link, _channelMap.Values.Select(c => c.Limit));
        session.Log += OnSessionLog;
        session.StateChanged += OnSessionStateChanged;
        session.Start();

        _session = session;
        UpdateCommandStates();
    }

    private void Disconnect()
    {
        if (_session is null)
            return;

        _session.Log -= OnSessionLog;
        _session.StateChanged -= OnSessionStateChanged;
        _session.Dispose();
        _session = null;

        StopRecording();

        State = DeviceState.Disconnected;
        StatusText = "연결 안 됨";
        AppendLog("연결을 끊었습니다.");
        UpdateCommandStates();
    }

    private void StartRecording()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "SerialMonitor");
            var path = Path.Combine(folder, $"measure_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            _recorder = new CsvRecorder(path);
            RecordingText = $"기록 중 · {Path.GetFileName(path)}";
            AppendLog($"CSV 기록 시작: {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"CSV 기록을 시작하지 못했습니다: {ex.Message}");
            _recorder = null;
            RecordingText = "기록 안 함";
        }
    }

    private void StopRecording()
    {
        if (_recorder is null)
            return;

        AppendLog($"CSV 기록 종료: {_recorder.WrittenRows:N0}행 · {_recorder.FilePath}");
        _recorder.Dispose();
        _recorder = null;
        RecordingText = "기록 안 함";
    }

    /// <summary>
    /// 화면 갱신. 이 메서드만 UI 스레드에서 돈다.
    /// 수신 스레드가 쌓아둔 값을 한꺼번에 꺼내 반영하고, 그린다.
    /// </summary>
    private void OnUiTick(object? sender, EventArgs e)
    {
        var session = _session;
        if (session is null)
            return;

        _drainBuffer.Clear();
        session.Drain(_drainBuffer);

        if (_drainBuffer.Count > 0)
        {
            var recorder = _recorder;

            foreach (var sample in _drainBuffer)
            {
                if (_channelMap.TryGetValue(sample.Channel, out var channel))
                    channel.Apply(sample);

                recorder?.Write(sample);
            }

            foreach (var channel in Channels)
                channel.CommitFrame();
        }

        Statistics = session.GetStatistics();
        Raise(nameof(Statistics));
    }

    private void OnSessionLog(object? sender, string message)
    {
        // 수신 스레드에서 올라오므로 UI 스레드로 넘겨야 한다.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => AppendLog(message));
    }

    private void OnSessionStateChanged(object? sender, DeviceState state)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            State = state;
            StatusText = state switch
            {
                DeviceState.Disconnected => "연결 안 됨",
                DeviceState.Connecting => "연결 중",
                DeviceState.Connected => $"연결됨 · {_session?.LinkName}",
                DeviceState.NotResponding => "응답 없음 (재시도 중)",
                DeviceState.Faulted => "오류",
                _ => state.ToString(),
            };
        });
    }

    private void AppendLog(string message)
    {
        LogLines.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {message}");

        // 로그가 무한정 쌓이면 메모리와 렌더링 양쪽에서 문제가 된다.
        while (LogLines.Count > 300)
            LogLines.RemoveAt(LogLines.Count - 1);
    }

    private void UpdateCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        Raise(nameof(IsConnected));
    }

    public void Dispose()
    {
        _uiTimer.Stop();
        Disconnect();
    }
}
