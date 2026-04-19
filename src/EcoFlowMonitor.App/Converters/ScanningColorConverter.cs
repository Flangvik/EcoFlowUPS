using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EcoFlowMonitor.Converters;

public class ScanningColorConverter : IValueConverter
{
    public static readonly ScanningColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Color.Parse("#00E676") : Color.Parse("#666666");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
