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

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public string? StringValue { get => GetValue(StringValueProperty); set => SetValue(StringValueProperty, value); }
    public IBrush? ValueColor { get => GetValue(ValueColorProperty); set => SetValue(ValueColorProperty, value); }

    private TextBlock? _labelTb;
    private TextBlock? _valueTb;
    private TextBlock? _unitTb;
    private bool _loaded;

    public StatCard()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _labelTb = this.FindControl<TextBlock>("LabelText");
        _valueTb = this.FindControl<TextBlock>("ValueText");
        _unitTb = this.FindControl<TextBlock>("UnitText");
        _loaded = true;
        UpdateDisplay();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_loaded && (change.Property == ValueProperty || change.Property == StringValueProperty ||
                        change.Property == FormatProperty || change.Property == LabelProperty ||
                        change.Property == UnitProperty || change.Property == ValueColorProperty))
        {
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (_labelTb != null)
            _labelTb.Text = Label;

        if (_valueTb != null)
        {
            _valueTb.Text = StringValue ?? Value.ToString(Format);
            if (ValueColor != null)
                _valueTb.Foreground = ValueColor;
        }

        if (_unitTb != null)
        {
            _unitTb.Text = Unit;
            _unitTb.IsVisible = !string.IsNullOrEmpty(Unit);
        }
    }
}
