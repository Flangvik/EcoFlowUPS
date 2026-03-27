using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace EcoFlowMonitor.Controls;

public class GlowStatusIndicator : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<GlowStatusIndicator, bool>(nameof(IsActive));

    public static readonly StyledProperty<Color> ActiveColorProperty =
        AvaloniaProperty.Register<GlowStatusIndicator, Color>(nameof(ActiveColor), Color.Parse("#00E676"));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Color ActiveColor
    {
        get => GetValue(ActiveColorProperty);
        set => SetValue(ActiveColorProperty, value);
    }

    private double _pulse;
    private DispatcherTimer? _timer;

    static GlowStatusIndicator()
    {
        AffectsRender<GlowStatusIndicator>(IsActiveProperty, ActiveColorProperty);
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
        _pulse = (_pulse + 0.03) % (Math.PI * 2);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2;

        if (!IsActive)
        {
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#666666")), null, center, radius, radius);
            return;
        }

        // Pulsing glow ring
        var glowAlpha = (byte)(40 + 30 * Math.Sin(_pulse));
        var glowColor = Color.FromArgb(glowAlpha, ActiveColor.R, ActiveColor.G, ActiveColor.B);
        context.DrawEllipse(new SolidColorBrush(glowColor), null, center, radius * 1.8, radius * 1.8);

        // Core dot
        context.DrawEllipse(new SolidColorBrush(ActiveColor), null, center, radius, radius);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(8, 8);
    }
}
