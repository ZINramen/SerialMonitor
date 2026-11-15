using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SerialMonitor.Core.Device;
using SerialMonitor.Core.Judging;

namespace SerialMonitor.App;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}

public sealed class StateToBrushConverter : IValueConverter
{
    private static readonly Brush Grey = Freeze(0x5A, 0x63, 0x72);
    private static readonly Brush Green = Freeze(0x3F, 0xC3, 0x80);
    private static readonly Brush Amber = Freeze(0xE8, 0xA3, 0x3D);
    private static readonly Brush Red = Freeze(0xE0, 0x51, 0x51);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            DeviceState.Connected => Green,
            DeviceState.Connecting => Amber,
            DeviceState.NotResponding => Amber,
            DeviceState.Faulted => Red,
            _ => Grey,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    internal static Brush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class VerdictToBrushConverter : IValueConverter
{
    private static readonly Brush Neutral = StateToBrushConverter.Freeze(0xC8, 0xD0, 0xDC);
    private static readonly Brush Ok = StateToBrushConverter.Freeze(0x4D, 0xD6, 0x9C);
    private static readonly Brush Ng = StateToBrushConverter.Freeze(0xFF, 0x6B, 0x6B);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Verdict.Ok => Ok,
            Verdict.Ng => Ng,
            _ => Neutral,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class VerdictToBadgeConverter : IValueConverter
{
    private static readonly Brush Neutral = StateToBrushConverter.Freeze(0x4A, 0x52, 0x60);
    private static readonly Brush Ok = StateToBrushConverter.Freeze(0x2E, 0x8B, 0x61);
    private static readonly Brush Ng = StateToBrushConverter.Freeze(0xC0, 0x3B, 0x3B);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Verdict.Ok => Ok,
            Verdict.Ng => Ng,
            _ => Neutral,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
