using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace EcoFlowMonitor.Controls;

public partial class StatCard : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StatCard, string>(nameof(Label), "");

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<StatCard, double>(nameof(Value));

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<StatCard, string>(nameof(Unit), "");

    public static readonly StyledProperty<string> FormatProperty =
        AvaloniaProperty.Register<StatCard, string>(nameof(Format), "F0");

    public static readonly StyledProperty<string?> StringValueProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(StringValue));

    public static readonly StyledProperty<IBrush?> ValueColorProperty =
        AvaloniaProperty.Register<StatCard, IBrush?>(nameof(ValueColor));

    // Computed display properties (read-only styled properties)
    public static readonly StyledProperty<string> DisplayValueProperty =
        AvaloniaProperty.Register<StatCard, string>(nameof(DisplayValue), "0");

    public static readonly StyledProperty<bool> HasUnitProperty =
        AvaloniaProperty.Register<StatCard, bool>(nameof(HasUnit), false);

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public string? StringValue { get => GetValue(StringValueProperty); set => SetValue(StringValueProperty, value); }
    public IBrush? ValueColor { get => GetValue(ValueColorProperty); set => SetValue(ValueColorProperty, value); }
    public string DisplayValue { get => GetValue(DisplayValueProperty); private set => SetValue(DisplayValueProperty, value); }
    public bool HasUnit { get => GetValue(HasUnitProperty); private set => SetValue(HasUnitProperty, value); }

    static StatCard()
    {
        ValueProperty.Changed.AddClassHandler<StatCard>((s, _) => s.Recompute());
        StringValueProperty.Changed.AddClassHandler<StatCard>((s, _) => s.Recompute());
        FormatProperty.Changed.AddClassHandler<StatCard>((s, _) => s.Recompute());
        UnitProperty.Changed.AddClassHandler<StatCard>((s, _) => s.Recompute());
    }

    public StatCard()
    {
        InitializeComponent();
    }

    private void Recompute()
    {
        DisplayValue = StringValue ?? Value.ToString(Format);
        HasUnit = !string.IsNullOrEmpty(Unit);
    }
}
