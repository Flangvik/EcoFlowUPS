using System.Globalization;
using Avalonia.Data.Converters;

namespace EcoFlowMonitor.Converters;

public class StringEqualityConverter : IValueConverter
{
    public static readonly StringEqualityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter?.ToString() : Avalonia.Data.BindingOperations.DoNothing;
}
