using System.Globalization;
using Avalonia.Data.Converters;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.Converters;

public class StepConverter : IValueConverter
{
    public static readonly StepConverter IsStep0 = new(0);
    public static readonly StepConverter IsStep1 = new(1);
    public static readonly StepConverter IsStep2 = new(2);
    public static readonly StepConverter IsLastStep = new(-1);

    private readonly int _step;
    private StepConverter(int step) => _step = step;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int current)
        {
            if (_step == -1) return current >= 2;
            return current == _step;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class TriggerMatchConverter : IValueConverter
{
    public static readonly TriggerMatchConverter PowerLost = new(TriggerType.PowerLost);
    public static readonly TriggerMatchConverter PowerRestored = new(TriggerType.PowerRestored);
    public static readonly TriggerMatchConverter BatteryBelow = new(TriggerType.BatteryBelow);
    public static readonly TriggerMatchConverter TimeBelow = new(TriggerType.TimeRemainingBelow);

    private readonly TriggerType _type;
    private TriggerMatchConverter(TriggerType type) => _type = type;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TriggerType t && t == _type;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true) return _type;
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
