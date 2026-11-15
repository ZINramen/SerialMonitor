using System.Collections.Concurrent;
using System.Diagnostics;
using SerialMonitor.Core.Judging;
using SerialMonitor.Core.Protocol;

namespace SerialMonitor.Core.Device;

public sealed class SessionOptions
{
    /// <summary>한 번 읽기를 기다리는 시간.</summary>
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>이 시간 동안 아무것도 안 오면 응답 없음으로 본다.</summary>
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>포기하기 전까지 다시 찔러보는 횟수.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>재연결을 다시 시도하기까지 기다리는 시간.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>화면이 밀리지 않도록 큐에 쌓아둘 최대 개수.</summary>
    public int MaxQueuedSamples { get; set; } = 20_000;
}

/// <summary>
/// 수신 루프를 도는 세션.
///
/// 설계에서 가장 중요한 두 가지
/// 1. 수신은 전용 스레드에서 돌고, 화면은 건드리지 않는다.
///    받은 값은 큐에 넣기만 하고, 화면 쪽에서 자기 주기로 꺼내 간다.
///    이렇게 해야 초당 수백 개가 들어와도 화면이 멈추지 않는다.
/// 2. 무응답은 예외가 아니라 정상적으로 일어나는 상황으로 다룬다.
///    타임아웃 → 재시도 → 그래도 안 되면 끊고 상태를 알린다.
/// </summary>
public sealed class DeviceSession : IDisposable
{
    private readonly IDeviceLink _link;
    private readonly IReadOnlyDictionary<byte, ChannelLimit> _limits;
    private readonly SessionOptions _options;
    private readonly FrameParser _parser = new();
    private readonly ConcurrentQueue<Sample> _samples = new();
    private readonly SessionStatistics _stats = new();
    private readonly byte[] _readBuffer = new byte[4096];
    private readonly Stopwatch _rateClock = Stopwatch.StartNew();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int? _lastSequence;
    private long _framesAtLastRateCheck;
    private byte _commandSequence;
    private int _queuedCount;

    public DeviceSession(IDeviceLink link, IEnumerable<ChannelLimit> limits, SessionOptions? options = null)
    {
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _limits = limits.ToDictionary(l => l.Channel);
        _options = options ?? new SessionOptions();
    }

    public DeviceState State { get; private set; } = DeviceState.Disconnected;

    public string LinkName => _link.Name;

    public event EventHandler<string>? Log;

    public event EventHandler<DeviceState>? StateChanged;

    public SessionStatistics GetStatistics() => _stats.Snapshot();

    public IReadOnlyDictionary<byte, ChannelLimit> Limits => _limits;

    public void Start()
    {
        if (_loop is { IsCompleted: false })
            return;

        _parser.Reset();
        _lastSequence = null;
        _stats.Reset();
        _framesAtLastRateCheck = 0;
        _rateClock.Restart();

        while (_samples.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _queuedCount, 0);

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // 취소로 끝난 것이므로 넘긴다.
        }

        _loop = null;
        _cts?.Dispose();
        _cts = null;

        try
        {
            _link.Close();
        }
        catch (Exception ex)
        {
            Emit($"연결 종료 중 오류: {ex.Message}");
        }

        SetState(DeviceState.Disconnected);
    }

    /// <summary>쌓인 값을 한꺼번에 꺼내 간다. 화면 갱신 주기에 맞춰 호출한다.</summary>
    public int Drain(List<Sample> destination, int max = 5000)
    {
        int taken = 0;
        while (taken < max && _samples.TryDequeue(out var sample))
        {
            destination.Add(sample);
            Interlocked.Decrement(ref _queuedCount);
            taken++;
        }
        return taken;
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                SetState(DeviceState.Connecting);
                _link.Open();
                SetState(DeviceState.Connected);
                Emit($"연결됨: {_link.Name}");

                SendCommand(CommandCode.StartMeasure);

                await ReceiveLoopAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Faulted);
                Emit($"통신 오류: {ex.Message}");
            }

            if (token.IsCancellationRequested)
                break;

            // 끊어졌으면 잠시 쉬었다가 다시 붙는다.
            try
            {
                _link.Close();
            }
            catch
            {
                // 이미 닫혀 있을 수 있다.
            }

            SetState(DeviceState.Disconnected);
            _stats.Reconnects++;
            Emit($"{_options.ReconnectDelay.TotalSeconds:0.#}초 후 재연결을 시도합니다.");

            try
            {
                await Task.Delay(_options.ReconnectDelay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var lastDataAt = DateTime.UtcNow;
        int retries = 0;

        while (!token.IsCancellationRequested)
        {
            int read = _link.Read(_readBuffer, 0, _readBuffer.Length, _options.ReadTimeout);

            if (read > 0)
            {
                lastDataAt = DateTime.UtcNow;

                if (retries > 0)
                {
                    Emit("응답이 돌아왔습니다.");
                    retries = 0;
                }

                if (State != DeviceState.Connected)
                    SetState(DeviceState.Connected);

                Consume(_readBuffer.AsSpan(0, read));
                UpdateRate();
                continue;
            }

            // 아무것도 안 왔다. 바로 끊지 않고 응답 유예 시간을 본다.
            if (DateTime.UtcNow - lastDataAt < _options.ResponseTimeout)
            {
                await Task.Yield();
                continue;
            }

            _stats.Timeouts++;
            SetState(DeviceState.NotResponding);

            if (retries >= _options.MaxRetries)
            {
                Emit($"재시도 {retries}회 후에도 응답이 없어 연결을 끊습니다.");
                return;
            }

            retries++;
            _stats.Retries++;
            Emit($"응답 없음. 재시도 {retries}/{_options.MaxRetries}");

            // 재시도해도 되는 명령만 다시 보낸다.
            // Ping은 몇 번을 보내든 장비 상태가 달라지지 않는다(멱등).
            // 장비를 움직이거나 설정을 바꾸는 명령이라면 이렇게 재전송하면 안 되고,
            // 일련번호로 중복 수행을 막아야 한다.
            SendCommand(CommandCode.Ping);
            lastDataAt = DateTime.UtcNow;
        }
    }

    private void SendCommand(CommandCode command)
    {
        try
        {
            var frame = new Frame(_commandSequence++, command, Array.Empty<byte>());
            _link.Write(frame.ToBytes());
        }
        catch (Exception ex)
        {
            Emit($"명령 전송 실패({command}): {ex.Message}");
        }
    }

    private void Consume(ReadOnlySpan<byte> data)
    {
        var frames = _parser.Append(data);

        _stats.CrcErrors = _parser.CrcErrorCount;
        _stats.DiscardedBytes = _parser.DiscardedByteCount;

        foreach (var frame in frames)
        {
            _stats.ReceivedFrames++;
            TrackSequence(frame.Sequence);

            if (frame.Command != CommandCode.Measurement)
                continue;

            if (!Measurement.TryParse(frame.Payload, out var measurement))
            {
                Emit($"측정 페이로드 길이가 규격과 다릅니다({frame.Payload.Length}바이트).");
                continue;
            }

            _stats.ReceivedMeasurements++;

            var verdict = _limits.TryGetValue(measurement.Channel, out var limit)
                ? limit.Judge(measurement.Value)
                : Verdict.Unknown;

            if (verdict == Verdict.Ok) _stats.OkCount++;
            else if (verdict == Verdict.Ng) _stats.NgCount++;

            Enqueue(new Sample(
                measurement.Channel,
                frame.Sequence,
                measurement.DeviceTimeMs,
                measurement.Value,
                verdict,
                DateTime.UtcNow));
        }
    }

    private void TrackSequence(byte sequence)
    {
        if (_lastSequence is null)
        {
            _lastSequence = sequence;
            return;
        }

        // 0~255를 돌기 때문에 단순 뺄셈이 아니라 256으로 감싸서 계산한다.
        int gap = (sequence - _lastSequence.Value + 256) % 256;

        if (gap == 0)
        {
            // 같은 번호가 또 왔다. 장비가 재전송했거나 우리가 중복 수신한 것이다.
            _lastSequence = sequence;
            return;
        }

        if (gap > 1)
            _stats.LostFrames += gap - 1;

        _lastSequence = sequence;
    }

    private void Enqueue(Sample sample)
    {
        // 화면이 못 따라가도 메모리가 무한정 늘지 않게 상한을 둔다.
        // 오래된 것부터 버리고, 원본이 필요하면 CSV 기록 쪽을 쓴다.
        if (Interlocked.Increment(ref _queuedCount) > _options.MaxQueuedSamples)
        {
            if (_samples.TryDequeue(out _))
                Interlocked.Decrement(ref _queuedCount);
        }

        _samples.Enqueue(sample);
    }

    private void UpdateRate()
    {
        if (_rateClock.ElapsedMilliseconds < 500)
            return;

        long delta = _stats.ReceivedFrames - _framesAtLastRateCheck;
        _stats.FramesPerSecond = delta * 1000.0 / _rateClock.ElapsedMilliseconds;
        _framesAtLastRateCheck = _stats.ReceivedFrames;
        _rateClock.Restart();
    }

    private void SetState(DeviceState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void Emit(string message) => Log?.Invoke(this, message);

    public void Dispose()
    {
        Stop();
        _link.Dispose();
    }
}
