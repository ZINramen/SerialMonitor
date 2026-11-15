using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SerialMonitor.App.Controls;

/// <summary>
/// 추이 그래프. 외부 차트 라이브러리를 쓰지 않고 직접 그린다.
///
/// 값이 들어올 때마다 다시 그리지 않고, 화면 갱신 주기에 맞춰 Revision이
/// 올라갈 때만 다시 그린다. 수신은 초당 수십~수백 번이지만 사람 눈은
/// 그만큼 구분하지 못하므로, 그리는 횟수를 줄이는 쪽이 훨씬 중요하다.
/// </summary>
public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(TrendSeries), typeof(TrendChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>이 값이 바뀔 때만 다시 그린다.</summary>
    public static readonly DependencyProperty RevisionProperty =
        DependencyProperty.Register(nameof(Revision), typeof(int), typeof(TrendChart),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowerLimitProperty =
        DependencyProperty.Register(nameof(LowerLimit), typeof(double), typeof(TrendChart),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UpperLimitProperty =
        DependencyProperty.Register(nameof(UpperLimit), typeof(double), typeof(TrendChart),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(TrendChart),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public TrendSeries? Series
    {
        get => (TrendSeries?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public int Revision
    {
        get => (int)GetValue(RevisionProperty);
        set => SetValue(RevisionProperty, value);
    }

    public double LowerLimit
    {
        get => (double)GetValue(LowerLimitProperty);
        set => SetValue(LowerLimitProperty, value);
    }

    public double UpperLimit
    {
        get => (double)GetValue(UpperLimitProperty);
        set => SetValue(UpperLimitProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20));
    private static readonly Pen GridPen = CreatePen(Color.FromRgb(0x2A, 0x31, 0x3D), 1);
    private static readonly Pen LimitPen = CreatePen(Color.FromRgb(0xC0, 0x4B, 0x4B), 1, dashed: true);
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6));

    private static Pen CreatePen(Color color, double thickness, bool dashed = false)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        if (dashed)
            pen.DashStyle = new DashStyle(new double[] { 4, 4 }, 0);
        pen.Freeze();
        return pen;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));

        var series = Series;
        if (series is null || series.Count < 2)
        {
            DrawText(dc, "데이터 없음", 8, 6);
            return;
        }

        // 세로 범위는 기준선이 항상 보이도록 측정값과 상·하한을 모두 포함시킨다.
        double min = series.Minimum;
        double max = series.Maximum;

        if (!double.IsNaN(LowerLimit)) min = Math.Min(min, LowerLimit);
        if (!double.IsNaN(UpperLimit)) max = Math.Max(max, UpperLimit);

        double span = max - min;
        if (span < 1e-6) span = 1;
        double margin = span * 0.12;
        min -= margin;
        max += margin;
        span = max - min;

        double MapY(double value) => height - (value - min) / span * height;

        // 가로 격자
        for (int i = 1; i < 4; i++)
        {
            double y = height * i / 4.0;
            dc.DrawLine(GridPen, new Point(0, y), new Point(width, y));
        }

        // 상·하한 선
        if (!double.IsNaN(UpperLimit))
        {
            double y = MapY(UpperLimit);
            dc.DrawLine(LimitPen, new Point(0, y), new Point(width, y));
            DrawText(dc, $"상한 {UpperLimit:0.#}", 4, y - 16);
        }

        if (!double.IsNaN(LowerLimit))
        {
            double y = MapY(LowerLimit);
            dc.DrawLine(LimitPen, new Point(0, y), new Point(width, y));
            DrawText(dc, $"하한 {LowerLimit:0.#}", 4, y + 2);
        }

        // 추이선. 점이 가로 픽셀 수보다 많으면 픽셀당 하나로 줄여서 그린다.
        int count = series.Count;
        int step = Math.Max(1, (int)Math.Ceiling(count / Math.Max(1.0, width)));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            bool started = false;
            for (int i = 0; i < count; i += step)
            {
                double x = (double)i / (count - 1) * width;
                var point = new Point(x, MapY(series[i]));

                if (!started)
                {
                    ctx.BeginFigure(point, false, false);
                    started = true;
                }
                else
                {
                    ctx.LineTo(point, true, false);
                }
            }
        }
        geometry.Freeze();

        var linePen = new Pen(LineBrush, 1.6);
        linePen.Freeze();
        dc.DrawGeometry(null, linePen, geometry);
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10,
            LabelBrush,
            96);
        dc.DrawText(formatted, new Point(x, y));
    }
}
