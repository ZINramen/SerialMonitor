namespace SerialMonitor.Core.Judging;

public enum Verdict
{
    Unknown,
    Ok,
    Ng,
}

/// <summary>채널별 합격 기준. 규격서에 적힌 상·하한이 여기에 들어간다.</summary>
public sealed class ChannelLimit
{
    public ChannelLimit(byte channel, string name, double lowerLimit, double upperLimit, string unit)
    {
        if (lowerLimit > upperLimit)
            throw new ArgumentException("하한이 상한보다 클 수 없습니다.");

        Channel = channel;
        Name = name;
        LowerLimit = lowerLimit;
        UpperLimit = upperLimit;
        Unit = unit;
    }

    public byte Channel { get; }
    public string Name { get; }
    public double LowerLimit { get; }
    public double UpperLimit { get; }
    public string Unit { get; }

    public Verdict Judge(double value)
        => value >= LowerLimit && value <= UpperLimit ? Verdict.Ok : Verdict.Ng;
}
