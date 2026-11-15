using System.Windows.Media;
using SerialMonitor.App.Controls;
using SerialMonitor.Core.Device;
using SerialMonitor.Core.Judging;

namespace SerialMonitor.App.ViewModels;

public sealed class ChannelViewModel : ObservableObject
{
    private static readonly Brush[] Palette =
    {
        new SolidColorBrush(Color.FromRgb(0x4D, 0xA6, 0xFF)),
        new SolidColorBrush(Color.FromRgb(0x4D, 0xD6, 0x9C)),
        new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x4D)),
        new SolidColorBrush(Color.FromRgb(0xC9, 0x8C, 0xFF)),
    };

    private double _lastValue;
    private Verdict _verdict = Verdict.Unknown;
    private long _okCount;
    private long _ngCount;
    private int _revision;

    public ChannelViewModel(ChannelLimit limit, int trendCapacity = 600)
    {
        Limit = limit;
        Series = new TrendSeries(trendCapacity);
        LineBrush = Palette[(limit.Channel - 1) % Palette.Length];
    }

    public ChannelLimit Limit { get; }

    public TrendSeries Series { get; }

    public Brush LineBrush { get; }

    public string Title => $"CH{Limit.Channel} · {Limit.Name}";

    public string LimitText => $"{Limit.LowerLimit:0.#} ~ {Limit.UpperLimit:0.#} {Limit.Unit}";

    public double LowerLimit => Limit.LowerLimit;

    public double UpperLimit => Limit.UpperLimit;

    public double LastValue
    {
        get => _lastValue;
        private set => Set(ref _lastValue, value);
    }

    public string LastValueText => $"{_lastValue:0.00} {Limit.Unit}";

    public Verdict Verdict
    {
        get => _verdict;
        private set
        {
            if (Set(ref _verdict, value))
                Raise(nameof(VerdictText));
        }
    }

    public string VerdictText => _verdict switch
    {
        Verdict.Ok => "OK",
        Verdict.Ng => "NG",
        _ => "—",
    };

    public long OkCount
    {
        get => _okCount;
        private set => Set(ref _okCount, value);
    }

    public long NgCount
    {
        get => _ngCount;
        private set => Set(ref _ngCount, value);
    }

    public string CountText => $"OK {_okCount:N0} / NG {_ngCount:N0}";

    /// <summary>그래프가 이 값을 보고 다시 그린다.</summary>
    public int Revision
    {
        get => _revision;
        private set => Set(ref _revision, value);
    }

    /// <summary>수신 스레드가 아니라 화면 갱신 주기에서만 불린다.</summary>
    public void Apply(Sample sample)
    {
        Series.Add(sample.Value);
        LastValue = sample.Value;
        Verdict = sample.Verdict;

        if (sample.Verdict == Verdict.Ok) OkCount++;
        else if (sample.Verdict == Verdict.Ng) NgCount++;
    }

    /// <summary>이번 주기에 들어온 값들을 모두 반영한 뒤 한 번만 호출한다.</summary>
    public void CommitFrame()
    {
        Raise(nameof(LastValueText));
        Raise(nameof(CountText));
        Revision++;
    }

    public void Reset()
    {
        Series.Clear();
        OkCount = 0;
        NgCount = 0;
        LastValue = 0;
        Verdict = Verdict.Unknown;
        CommitFrame();
    }
}
