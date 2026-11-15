using System.Diagnostics;
using SerialMonitor.Core.Protocol;

namespace SerialMonitor.Core.Device;

/// <summary>
/// 가짜 장비. 실제 장비와 똑같은 바이트 흐름을 만들어 보낸다.
///
/// 장비가 없는 상태에서 개발을 시작하려고 만든 것이고, 지금은 그것 말고도
/// 쓸모가 있다. 실제 장비는 "일부러 고장나게" 할 수 없지만 이건 가능하다.
/// 유실·CRC 오류·쓰레기 바이트·무응답을 설정한 확률대로 섞어 보내므로
/// 예외 처리가 제대로 도는지 매번 같은 조건에서 확인할 수 있다.
/// </summary>
public sealed class SimulatedDeviceLink : IDeviceLink
{
    private readonly SimulatorOptions _options;
    private readonly Random _random;
    private readonly Stopwatch _clock = new();
    private readonly object _gate = new();
    private readonly Queue<byte[]> _outbox = new();
    private readonly ManualResetEventSlim _dataReady = new(false);

    private CancellationTokenSource? _cts;
    private Task? _pump;
    private byte[]? _partial;
    private int _partialOffset;
    private byte _sequence;
    private bool _measuring = true;

    public SimulatedDeviceLink(SimulatorOptions? options = null)
    {
        _options = options ?? new SimulatorOptions();
        _random = new Random(_options.RandomSeed);
    }

    public string Name => "시뮬레이터 (가상 장비)";

    public bool IsOpen => _pump is { IsCompleted: false };

    public void Open()
    {
        if (IsOpen)
            return;

        lock (_gate)
        {
            _outbox.Clear();
            _partial = null;
            _partialOffset = 0;
        }

        _clock.Restart();
        _cts = new CancellationTokenSource();
        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    public void Close()
    {
        _cts?.Cancel();
        try
        {
            _pump?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // 취소로 끝난 것이므로 조용히 넘긴다.
        }

        _pump = null;
        _cts?.Dispose();
        _cts = null;
        _clock.Stop();
        _dataReady.Set();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        // PC가 보낸 명령을 해석해 장비처럼 반응한다.
        var parser = new FrameParser();
        foreach (var frame in parser.Append(data))
        {
            switch (frame.Command)
            {
                case CommandCode.Ping:
                    Enqueue(new Frame(frame.Sequence, CommandCode.Ack, Array.Empty<byte>()).ToBytes());
                    break;
                case CommandCode.StartMeasure:
                    _measuring = true;
                    Enqueue(new Frame(frame.Sequence, CommandCode.Ack, Array.Empty<byte>()).ToBytes());
                    break;
                case CommandCode.StopMeasure:
                    _measuring = false;
                    Enqueue(new Frame(frame.Sequence, CommandCode.Ack, Array.Empty<byte>()).ToBytes());
                    break;
            }
        }
    }

    public int Read(byte[] buffer, int offset, int count, TimeSpan timeout)
    {
        if (count <= 0)
            return 0;

        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            lock (_gate)
            {
                if (_partial is null && _outbox.Count > 0)
                {
                    _partial = _outbox.Dequeue();
                    _partialOffset = 0;
                }

                if (_partial is not null)
                {
                    int remaining = _partial.Length - _partialOffset;
                    int take = Math.Min(remaining, count);
                    Array.Copy(_partial, _partialOffset, buffer, offset, take);
                    _partialOffset += take;

                    if (_partialOffset >= _partial.Length)
                    {
                        _partial = null;
                        _partialOffset = 0;
                    }

                    if (_outbox.Count == 0 && _partial is null)
                        _dataReady.Reset();

                    return take;
                }

                _dataReady.Reset();
            }

            var remainingWait = deadline - DateTime.UtcNow;
            if (remainingWait <= TimeSpan.Zero)
                return 0; // 무응답. 위쪽에서 타임아웃으로 처리한다.

            _dataReady.Wait(remainingWait);

            if (!IsOpen)
                return 0;
        }
    }

    private async Task PumpAsync(CancellationToken token)
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _options.SampleRateHz));
        double phase = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(interval, token).ConfigureAwait(false);

                if (!_measuring)
                    continue;

                // 가끔 장비가 멈춘 것처럼 군다.
                if (_random.NextDouble() < _options.StallRate)
                {
                    await Task.Delay(_options.StallDuration, token).ConfigureAwait(false);
                    continue;
                }

                phase += 0.05;

                for (byte channel = 1; channel <= _options.ChannelCount; channel++)
                {
                    byte sequence = _sequence++;

                    // 유실: 번호는 올라갔는데 프레임을 보내지 않는다.
                    // 받는 쪽에서는 SEQ가 건너뛴 것으로 보인다.
                    if (_random.NextDouble() < _options.DropRate)
                        continue;

                    double value = GenerateValue(channel, phase);
                    var measurement = Measurement.FromValue(channel, (uint)_clock.ElapsedMilliseconds, value);
                    var bytes = new Frame(sequence, CommandCode.Measurement, measurement.ToPayload()).ToBytes();

                    // 쓰레기 바이트를 앞에 끼워 넣어 동기가 깨지게 만든다.
                    if (_random.NextDouble() < _options.GarbageRate)
                    {
                        var garbage = new byte[_random.Next(1, 5)];
                        _random.NextBytes(garbage);
                        Enqueue(garbage);
                    }

                    // 바이트 하나를 망가뜨려 CRC가 틀리게 만든다.
                    if (_random.NextDouble() < _options.CorruptRate)
                    {
                        int index = _random.Next(1, bytes.Length - 1);
                        bytes[index] ^= (byte)(1 << _random.Next(8));
                    }

                    Enqueue(bytes);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료.
        }
    }

    private double GenerateValue(byte channel, double phase)
    {
        // 채널마다 조금씩 다른 기준값 + 느린 흔들림 + 잡음.
        double channelOffset = (channel - 1) * 1.5;
        double drift = Math.Sin(phase + channel) * 1.2;
        double noise = (_random.NextDouble() - 0.5) * 2 * _options.NoiseAmplitude;
        double value = _options.BaseValue + channelOffset + drift + noise;

        // 가끔 불량에 해당하는 값이 튀어나오게 한다.
        if (_random.NextDouble() < _options.SpikeRate)
            value += _random.NextDouble() < 0.5 ? 35 : -20;

        return value;
    }

    private void Enqueue(byte[] data)
    {
        lock (_gate)
        {
            _outbox.Enqueue(data);
        }
        _dataReady.Set();
    }

    public void Dispose()
    {
        Close();
        _dataReady.Dispose();
    }
}
