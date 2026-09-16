using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace CodexQuotaMonitor.Wpf;

public sealed class QuotaTrendBlock : FrameworkElement
{
    private static readonly TimeSpan WindowSpan = TimeSpan.FromHours(24);

    private IReadOnlyList<TrendSample> _samples = Array.Empty<TrendSample>();

    public QuotaTrendBlock()
    {
        SnapsToDevicePixels = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    public void SetSamples(IReadOnlyList<TrendSample> samples)
    {
        _samples = samples;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = Math.Max(140.0, ActualWidth);
        var height = Math.Max(72.0, ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var primary = new SolidColorBrush(Formatting.ColorFromHex("#F4F6F8"));
        var muted = new SolidColorBrush(Formatting.ColorFromHex("#8F9BA8"));
        var weeklyBrush = new SolidColorBrush(Formatting.ColorFromHex("#58B8FF"));
        var weeklyFill = new SolidColorBrush(Color.FromArgb(26, 88, 184, 255));
        var fiveHourBrush = new SolidColorBrush(Formatting.ColorFromHex("#8B97A5"));

        var labelSize = Math.Clamp(height * 0.13, 8.0, 10.0);
        DrawText(dc, "24H", 2, 1, labelSize, FontWeights.Bold, primary, dpi);
        DrawLegend(dc, width, 1, labelSize, dpi, weeklyBrush, fiveHourBrush);

        var chartLeft = 20.0;
        var chartRight = width - 2;
        var chartTop = 15.0;
        var chartBottom = height - 15.0;
        var chartWidth = chartRight - chartLeft;
        var chartHeight = chartBottom - chartTop;
        if (chartWidth < 10 || chartHeight < 10)
        {
            return;
        }

        DrawGrid(dc, chartLeft, chartRight, chartTop, chartBottom, dpi, muted);

        var now = DateTimeOffset.Now;
        var start = now - WindowSpan;
        var visible = _samples
            .Where(sample => sample.At >= start &&
                             (sample.WeeklyRemaining.HasValue || sample.FiveHourRemaining.HasValue))
            .ToList();
        var weeklyCount = visible.Count(sample => sample.WeeklyRemaining.HasValue);
        var fiveHourCount = visible.Count(sample => sample.FiveHourRemaining.HasValue);

        if (weeklyCount < 2 && fiveHourCount < 2)
        {
            DrawCenteredText(
                dc,
                "collecting…",
                new Rect(chartLeft, chartTop, chartWidth, chartHeight),
                Math.Max(7.0, labelSize - 1.5),
                FontWeights.Normal,
                muted,
                dpi);
        }
        else
        {
            DrawSeries(dc, visible, sample => sample.WeeklyRemaining, weeklyBrush, weeklyFill, 1.6, chartLeft, chartRight, chartTop, chartHeight, start);
            DrawSeries(dc, visible, sample => sample.FiveHourRemaining, fiveHourBrush, null, 1.1, chartLeft, chartRight, chartTop, chartHeight, start);
        }

        DrawAxisTimes(dc, chartLeft, chartRight, chartBottom, start, now, dpi, muted);
    }

    private static void DrawSeries(
        DrawingContext dc,
        IReadOnlyList<TrendSample> samples,
        Func<TrendSample, double?> valueOf,
        Brush strokeBrush,
        Brush? fillBrush,
        double strokeWidth,
        double left,
        double right,
        double top,
        double height,
        DateTimeOffset start)
    {
        var pen = new Pen(strokeBrush, strokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        var segments = new List<IList<Point>>();
        var current = new List<Point>();
        foreach (var sample in samples)
        {
            var value = valueOf(sample);
            if (value is null)
            {
                AddSegment(segments, current);
                current = new List<Point>();
                continue;
            }

            var x = left + Math.Clamp((sample.At - start).TotalHours / 24.0, 0.0, 1.0) * (right - left);
            var y = top + (100.0 - Math.Clamp(value.Value, 0.0, 100.0)) / 100.0 * height;
            current.Add(new Point(x, y));
        }
        AddSegment(segments, current);

        foreach (var segment in segments)
        {
            if (fillBrush is not null)
            {
                var polygon = new List<Point>(segment)
                {
                    new Point(segment[^1].X, top + height),
                    new Point(segment[0].X, top + height)
                };
                dc.DrawGeometry(fillBrush, null, CreatePolygon(polygon));
            }
            for (var i = 1; i < segment.Count; i++)
            {
                dc.DrawLine(pen, segment[i - 1], segment[i]);
            }
        }
    }

    private static void AddSegment(List<IList<Point>> segments, List<Point> current)
    {
        if (current.Count > 1)
        {
            segments.Add(current);
        }
    }

    private static Geometry CreatePolygon(List<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            for (var i = 1; i < points.Count; i++)
            {
                context.LineTo(points[i], true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static void DrawGrid(DrawingContext dc, double left, double right, double top, double bottom, double dpi, Brush labelBrush)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), 1.0);
        var labelSize = 7.0;
        foreach (var level in new[] { 0.0, 50.0, 100.0 })
        {
            var y = top + (100.0 - level) / 100.0 * (bottom - top);
            dc.DrawLine(gridPen, new Point(left, y), new Point(right, y));
            var label = MakeText($"{level:0}", labelSize, FontWeights.Normal, labelBrush, dpi);
            dc.DrawText(label, new Point(1.0, y - label.Height / 2.0));
        }
    }

    private static void DrawAxisTimes(DrawingContext dc, double left, double right, double bottom, DateTimeOffset start, DateTimeOffset now, double dpi, Brush brush)
    {
        var size = 7.0;
        var leftText = MakeText(start.ToString("HH:mm", CultureInfo.CurrentCulture), size, FontWeights.Normal, brush, dpi);
        dc.DrawText(leftText, new Point(left, bottom + 2.0));
        var rightText = MakeText(now.ToString("HH:mm", CultureInfo.CurrentCulture), size, FontWeights.Normal, brush, dpi);
        dc.DrawText(rightText, new Point(right - rightText.WidthIncludingTrailingWhitespace, bottom + 2.0));
    }

    private static void DrawLegend(DrawingContext dc, double width, double y, double labelSize, double dpi, Brush weeklyBrush, Brush fiveHourBrush)
    {
        var size = Math.Max(6.5, labelSize - 2.2);
        const double dot = 2.4;
        const double gap = 7.0;
        var pairs = new (string Label, Brush Brush)[]
        {
            ("5H", fiveHourBrush),
            ("WK", weeklyBrush)
        };
        var probe = new SolidColorBrush(Colors.Transparent);
        var widths = pairs
            .Select(pair => dot * 2 + 2.5 + MakeText(pair.Label, size, FontWeights.SemiBold, probe, dpi).Width)
            .ToArray();
        var total = widths.Sum() + gap * (pairs.Length - 1);
        var x = width - total;
        for (var i = 0; i < pairs.Length; i++)
        {
            dc.DrawEllipse(pairs[i].Brush, null, new Point(x + dot, y + 1.6), dot, dot);
            dc.DrawText(MakeText(pairs[i].Label, size, FontWeights.SemiBold, pairs[i].Brush, dpi), new Point(x + dot * 2 + 2.5, y + 0.4));
            x += widths[i] + gap;
        }
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, FontWeight weight, Brush brush, double dpi)
    {
        dc.DrawText(MakeText(text, size, weight, brush, dpi), new Point(x, y));
    }

    private static void DrawCenteredText(DrawingContext dc, string text, Rect bounds, double size, FontWeight weight, Brush brush, double dpi)
    {
        var formatted = MakeText(text, size, weight, brush, dpi);
        var textBounds = formatted.BuildGeometry(new Point(0, 0)).Bounds;
        var x = bounds.Left + (bounds.Width - textBounds.Width) / 2.0 - textBounds.Left;
        var y = bounds.Top + (bounds.Height - textBounds.Height) / 2.0 - textBounds.Top;
        dc.DrawText(formatted, new Point(x, y));
    }

    private static FormattedText MakeText(string text, double size, FontWeight weight, Brush brush, double dpi)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            dpi);
    }
}