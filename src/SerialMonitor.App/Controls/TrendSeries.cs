namespace SerialMonitor.App.Controls;

/// <summary>
/// 그래프에 그릴 최근 값들을 담는 고정 크기 버퍼.
///
/// 들어오는 값을 전부 들고 있으면 메모리가 계속 늘어난다. 화면에는 최근
/// 구간만 보이면 되므로 정해진 개수만 남기고 오래된 것부터 덮어쓴다.
/// 원본 전체는 CSV 쪽에 따로 남는다.
/// </summary>
public sealed class TrendSeries
{
    private readonly double[] _values;
    private int _head;

    public TrendSeries(int capacity)
    {
        if (capacity <= 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _values = new double[capacity];
    }

    public int Capacity => _values.Length;

    public int Count { get; private set; }

    public double Minimum { get; private set; } = double.NaN;

    public double Maximum { get; private set; } = double.NaN;

    public void Add(double value)
    {
        _values[_head] = value;
        _head = (_head + 1) % _values.Length;

        if (Count < _values.Length)
            Count++;

        RecomputeRange();
    }

    /// <summary>오래된 것부터 순서대로 꺼낸다.</summary>
    public double this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            int start = Count == _values.Length ? _head : 0;
            return _values[(start + index) % _values.Length];
        }
    }

    public void Clear()
    {
        _head = 0;
        Count = 0;
        Minimum = double.NaN;
        Maximum = double.NaN;
    }

    private void RecomputeRange()
    {
        double min = double.MaxValue;
        double max = double.MinValue;

        for (int i = 0; i < Count; i++)
        {
            double v = this[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        Minimum = Count == 0 ? double.NaN : min;
        Maximum = Count == 0 ? double.NaN : max;
    }
}
