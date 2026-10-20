namespace OriG.App;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

/// <summary>
/// 电量指示：大号圆环（默认）/ 电池条两种样式。
/// 圆环按参考图做——粗描边、中间大号百分比 + 左右耳标签。
/// </summary>
internal static class BatteryDisplay
{
    private const double RingSize = 118;
    private const double RingThickness = 9;

    // 正常电量跟随主题强调色，警告/过低保持语义色
    private static Brush GoodBrush => ThemeManager.Brush("AccentBrush", "#C8F25A");
    private static readonly Brush WarnBrush = Frozen("#FF9F0A");
    private static readonly Brush LowBrush = Frozen("#FF3B30");

    // 其余颜色跟随当前主题（每次渲染时重新取，主题切换后自动生效）
    private static Brush TrackBrush => ThemeManager.Brush("BatteryTrackBrush", "#E1E4E9");
    private static Brush TextBrush => ThemeManager.Brush("TextBrush", "#14161A");
    private static Brush LabelBrush => ThemeManager.Brush("MutedBrush", "#6B7280");
    private static Brush BarBorderBrush => ThemeManager.Brush("BatteryBarBorderBrush", "#D9DDE3");
    private static Brush BarFillBrush => ThemeManager.Brush("BatteryBarFillBrush", "#B7E9C4");
    private static Brush BarLowFillBrush => ThemeManager.Brush("BatteryBarLowFillBrush", "#FFC9C4");

    public static void Render(Panel host, int left, int right, BatteryStyle style)
    {
        host.Children.Clear();
        host.Children.Add(Build(left, "左", style));
        host.Children.Add(Build(right, "右", style));
    }

    private static FrameworkElement Build(int percent, string label, BatteryStyle style)
    {
        var host = new StackPanel
        {
            Margin = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        if (style == BatteryStyle.Ring)
        {
            host.Children.Add(BuildRing(percent, label));
        }
        else
        {
            host.Children.Add(BuildBar(percent));
            host.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = LabelBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        return host;
    }

    // ---------------------------------------------------------------- 圆环

    private static FrameworkElement BuildRing(int percent, string label)
    {
        var grid = new Grid { Width = RingSize, Height = RingSize };

        grid.Children.Add(new Ellipse
        {
            Width = RingSize - RingThickness,
            Height = RingSize - RingThickness,
            Stroke = TrackBrush,
            StrokeThickness = RingThickness,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (percent > 0)
        {
            grid.Children.Add(new Path
            {
                Data = ArcGeometry(RingSize, RingThickness, percent),
                Stroke = ColorFor(percent),
                StrokeThickness = RingThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Fill = null,
            });
        }

        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        center.Children.Add(new TextBlock
        {
            Text = percent < 0 ? "—" : $"{percent}%",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        center.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = LabelBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        });
        grid.Children.Add(center);

        return grid;
    }

    private static PathGeometry ArcGeometry(double size, double thickness, int percent)
    {
        double radius = (size - thickness) / 2;
        var center = new Point(size / 2, size / 2);
        double sweep = Math.Min(359.9, 360.0 * Math.Clamp(percent, 0, 100) / 100.0);

        var start = new Point(center.X, center.Y - radius);
        double rad = sweep * Math.PI / 180.0;
        var end = new Point(
            center.X + radius * Math.Sin(rad),
            center.Y - radius * Math.Cos(rad));

        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            end, new Size(radius, radius), 0,
            sweep > 180, SweepDirection.Clockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    // ---------------------------------------------------------------- 电池

    private static FrameworkElement BuildBar(int percent)
    {
        const double width = 96;
        const double height = 34;
        const double pad = 4;

        var grid = new Grid
        {
            Width = width + 6,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        grid.Children.Add(new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = BarBorderBrush,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        grid.Children.Add(new Border
        {
            Width = 4,
            Height = 12,
            CornerRadius = new CornerRadius(0, 2, 2, 0),
            Background = BarBorderBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (percent > 0)
        {
            double inner = (width - pad * 2 - 4) * Math.Clamp(percent, 0, 100) / 100.0;
            grid.Children.Add(new Border
            {
                Width = Math.Max(3, inner),
                Height = height - pad * 2,
                CornerRadius = new CornerRadius(6),
                Background = percent <= 20 ? BarLowFillBrush : BarFillBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(pad + 2, 0, 0, 0),
            });
        }

        grid.Children.Add(new TextBlock
        {
            Text = percent < 0 ? "—" : $"{percent}%",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });

        return grid;
    }

    private static Brush ColorFor(int percent) =>
        percent <= 10 ? LowBrush : percent <= 25 ? WarnBrush : GoodBrush;

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
