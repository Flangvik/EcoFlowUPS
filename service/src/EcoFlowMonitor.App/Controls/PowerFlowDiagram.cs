using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Controls;

public class PowerFlowDiagram : Control
{
    public static readonly StyledProperty<int> InputWProperty =
        AvaloniaProperty.Register<PowerFlowDiagram, int>(nameof(InputW));

    public static readonly StyledProperty<int> OutputWProperty =
        AvaloniaProperty.Register<PowerFlowDiagram, int>(nameof(OutputW));

    public static readonly StyledProperty<int> SolarWProperty =
        AvaloniaProperty.Register<PowerFlowDiagram, int>(nameof(SolarW));

    public static readonly StyledProperty<float> BatteryPctProperty =
        AvaloniaProperty.Register<PowerFlowDiagram, float>(nameof(BatteryPct));

    public static readonly StyledProperty<PowerStatus> StatusProperty =
        AvaloniaProperty.Register<PowerFlowDiagram, PowerStatus>(nameof(Status));

    public int InputW
    {
        get => GetValue(InputWProperty);
        set => SetValue(InputWProperty, value);
    }

    public int OutputW
    {
        get => GetValue(OutputWProperty);
        set => SetValue(OutputWProperty, value);
    }

    public int SolarW
    {
        get => GetValue(SolarWProperty);
        set => SetValue(SolarWProperty, value);
    }

    public float BatteryPct
    {
        get => GetValue(BatteryPctProperty);
        set => SetValue(BatteryPctProperty, value);
    }

    public PowerStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private double _animPhase;
    private DispatcherTimer? _timer;

    static PowerFlowDiagram()
    {
        AffectsRender<PowerFlowDiagram>(InputWProperty, OutputWProperty, SolarWProperty, BatteryPctProperty, StatusProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _animPhase = (_animPhase + 0.05) % 1.0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var cardBg = new SolidColorBrush(Color.Parse("#1A1A1A"));
        var cardBorder = new Pen(new SolidColorBrush(Color.Parse("#2A2A2A")), 1);
        var textPrimary = new SolidColorBrush(Color.Parse("#F0F0F0"));
        var textSecondary = new SolidColorBrush(Color.Parse("#888888"));

        // Node dimensions and positions
        double nodeW = 90, nodeH = 52;
        double centerX = w / 2, centerY = h / 2;
        double leftX = 30, rightX = w - nodeW - 30;

        // Source nodes (left side)
        DrawNode(context, leftX, centerY - nodeH - 16, nodeW, nodeH,
            "Grid", $"{InputW} W",
            InputW > 0 ? "#00E676" : "#666666",
            cardBg, cardBorder, textPrimary, textSecondary);

        DrawNode(context, leftX, centerY + 16, nodeW, nodeH,
            "Solar", $"{SolarW} W",
            SolarW > 0 ? "#FFD600" : "#666666",
            cardBg, cardBorder, textPrimary, textSecondary);

        // Center node (device/battery)
        DrawNode(context, centerX - nodeW / 2, centerY - nodeH / 2, nodeW, nodeH,
            "Device", $"{BatteryPct:F0}%",
            "#00D4AA",
            cardBg, cardBorder, textPrimary, textSecondary);

        // Output nodes (right side)
        DrawNode(context, rightX, centerY - nodeH - 16, nodeW, nodeH,
            "AC Out", $"{OutputW} W",
            OutputW > 0 ? "#FF9100" : "#666666",
            cardBg, cardBorder, textPrimary, textSecondary);

        DrawNode(context, rightX, centerY + 16, nodeW, nodeH,
            "USB", "\u2014",
            "#666666",
            cardBg, cardBorder, textPrimary, textSecondary);

        // Flow lines with animated dots
        if (InputW > 0)
        {
            DrawFlowLine(context,
                leftX + nodeW, centerY - nodeH / 2 - 16 + nodeH / 2,
                centerX - nodeW / 2, centerY,
                "#00E676");
        }

        if (SolarW > 0)
        {
            DrawFlowLine(context,
                leftX + nodeW, centerY + 16 + nodeH / 2,
                centerX - nodeW / 2, centerY,
                "#FFD600");
        }

        if (OutputW > 0)
        {
            DrawFlowLine(context,
                centerX + nodeW / 2, centerY,
                rightX, centerY - nodeH / 2 - 16 + nodeH / 2,
                "#FF9100");
        }
    }

    private static void DrawNode(DrawingContext ctx, double x, double y, double w, double h,
        string title, string value, string accentColor,
        IBrush bg, Pen border, IBrush textPrimary, IBrush textSecondary)
    {
        var rect = new Rect(x, y, w, h);
        ctx.DrawRectangle(bg, border, new RoundedRect(rect, 8));

        // Left accent bar
        ctx.DrawRectangle(
            new SolidColorBrush(Color.Parse(accentColor)), null,
            new Rect(x, y + 8, 3, h - 16), 1.5, 1.5);

        var titleFmt = new FormattedText(
            title,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            11,
            textSecondary);
        ctx.DrawText(titleFmt, new Point(x + 12, y + 8));

        var valFmt = new FormattedText(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("JetBrains Mono, Consolas, monospace"),
            14,
            textPrimary);
        ctx.DrawText(valFmt, new Point(x + 12, y + 26));
    }

    private void DrawFlowLine(DrawingContext ctx, double x1, double y1, double x2, double y2, string color)
    {
        var linePen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 2);
        ctx.DrawLine(linePen, new Point(x1, y1), new Point(x2, y2));

        // Animated dots traveling along the line
        var parsedColor = Color.Parse(color);
        var dotBrush = new SolidColorBrush(parsedColor);
        var glowBrush = new SolidColorBrush(Color.FromArgb(40, parsedColor.R, parsedColor.G, parsedColor.B));

        for (int i = 0; i < 3; i++)
        {
            double t = (_animPhase + i * 0.33) % 1.0;
            double dx = x1 + (x2 - x1) * t;
            double dy = y1 + (y2 - y1) * t;

            ctx.DrawEllipse(dotBrush, null, new Point(dx, dy), 3, 3);
            ctx.DrawEllipse(glowBrush, null, new Point(dx, dy), 6, 6);
        }
    }
}
