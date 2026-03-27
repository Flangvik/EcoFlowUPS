using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace EcoFlowMonitor.Controls;

public class StepIndicator : Control
{
    public static readonly StyledProperty<int> TotalStepsProperty =
        AvaloniaProperty.Register<StepIndicator, int>(nameof(TotalSteps), 4);

    public static readonly StyledProperty<int> CurrentStepProperty =
        AvaloniaProperty.Register<StepIndicator, int>(nameof(CurrentStep), 0);

    public int TotalSteps
    {
        get => GetValue(TotalStepsProperty);
        set => SetValue(TotalStepsProperty, value);
    }

    public int CurrentStep
    {
        get => GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    static StepIndicator()
    {
        AffectsRender<StepIndicator>(TotalStepsProperty, CurrentStepProperty);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0 || TotalSteps <= 0) return;

        double dotRadius = 10;
        double spacing = (w - TotalSteps * dotRadius * 2) / Math.Max(TotalSteps - 1, 1);
        double y = h / 2;

        var completedBrush = new SolidColorBrush(Color.Parse("#00D4AA"));
        var currentBrush = new SolidColorBrush(Color.Parse("#0088FF"));
        var linePen = new Pen(new SolidColorBrush(Color.Parse("#2A2A2A")), 2);
        var completedLinePen = new Pen(completedBrush, 2);

        for (int i = 0; i < TotalSteps; i++)
        {
            double cx = dotRadius + i * (dotRadius * 2 + spacing);

            // Connector line to previous dot
            if (i > 0)
            {
                double prevCx = dotRadius + (i - 1) * (dotRadius * 2 + spacing);
                var pen = i <= CurrentStep ? completedLinePen : linePen;
                context.DrawLine(pen, new Point(prevCx + dotRadius, y), new Point(cx - dotRadius, y));
            }

            // Dot rendering based on state
            if (i < CurrentStep)
            {
                // Completed step
                context.DrawEllipse(completedBrush, null, new Point(cx, y), dotRadius, dotRadius);

                var check = new FormattedText(
                    "\u2713",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter"),
                    12,
                    Brushes.White);
                context.DrawText(check, new Point(cx - check.Width / 2, y - check.Height / 2));
            }
            else if (i == CurrentStep)
            {
                // Current active step
                context.DrawEllipse(currentBrush, null, new Point(cx, y), dotRadius, dotRadius);
                context.DrawEllipse(null, new Pen(Brushes.White, 2), new Point(cx, y), dotRadius - 2, dotRadius - 2);
            }
            else
            {
                // Future step
                context.DrawEllipse(
                    null,
                    new Pen(new SolidColorBrush(Color.Parse("#555555")), 2),
                    new Point(cx, y),
                    dotRadius, dotRadius);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(availableSize.Width, 28);
    }
}
