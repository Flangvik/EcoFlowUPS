using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Controls;

public class ArcBatteryGauge : Control
{
    public static readonly StyledProperty<float> PercentageProperty =
        AvaloniaProperty.Register<ArcBatteryGauge, float>(nameof(Percentage));

    public static readonly StyledProperty<PowerStatus> StatusProperty =
        AvaloniaProperty.Register<ArcBatteryGauge, PowerStatus>(nameof(Status));

    public float Percentage
    {
        get => GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }

    public PowerStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    static ArcBatteryGauge()
    {
        AffectsRender<ArcBatteryGauge>(PercentageProperty, StatusProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        var size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0) return;

        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        var radius = size / 2 - 12;
        var strokeWidth = 10.0;

        // Background track (dark gray arc, 270 degrees)
        var trackPen = new Pen(new SolidColorBrush(Color.Parse("#2A2A2A")), strokeWidth, lineCap: PenLineCap.Round);
        DrawArc(context, center, radius, 135, 270, trackPen);

        // Filled arc (color based on percentage and status)
        var fillColor = GetArcColor();
        var fillPen = new Pen(new SolidColorBrush(fillColor), strokeWidth, lineCap: PenLineCap.Round);
        var sweepAngle = 270.0 * Math.Clamp(Percentage / 100.0, 0, 1);
        if (sweepAngle > 0)
            DrawArc(context, center, radius, 135, sweepAngle, fillPen);

        // Glow effect -- draw the filled arc again with thicker, semi-transparent stroke
        var glowColor = Color.FromArgb(60, fillColor.R, fillColor.G, fillColor.B);
        var glowPen = new Pen(new SolidColorBrush(glowColor), strokeWidth + 8, lineCap: PenLineCap.Round);
        if (sweepAngle > 0)
            DrawArc(context, center, radius, 135, sweepAngle, glowPen);

        // Center text: percentage
        var pctText = new FormattedText(
            $"{Percentage:F0}%",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
            size * 0.22,
            new SolidColorBrush(Color.Parse("#F0F0F0")));
        context.DrawText(pctText, new Point(center.X - pctText.Width / 2, center.Y - pctText.Height / 2 - 4));

        // Sub text: "battery"
        var subText = new FormattedText(
            "battery",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            size * 0.09,
            new SolidColorBrush(Color.Parse("#888888")));
        context.DrawText(subText, new Point(center.X - subText.Width / 2, center.Y + pctText.Height / 2 - 2));
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double startAngle, double sweepAngle, Pen pen)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var startRad = startAngle * Math.PI / 180;
            var endRad = (startAngle + sweepAngle) * Math.PI / 180;

            var startPoint = new Point(
                center.X + radius * Math.Cos(startRad),
                center.Y + radius * Math.Sin(startRad));
            var endPoint = new Point(
                center.X + radius * Math.Cos(endRad),
                center.Y + radius * Math.Sin(endRad));

            ctx.BeginFigure(startPoint, false);
            ctx.ArcTo(endPoint, new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, pen, geo);
    }

    private Color GetArcColor()
    {
        if (Status == PowerStatus.PowerLost) return Color.Parse("#FF5252");
        if (Percentage > 50) return Color.Parse("#00E676");
        if (Percentage > 20) return Color.Parse("#FFB300");
        return Color.Parse("#FF5252");
    }
}
