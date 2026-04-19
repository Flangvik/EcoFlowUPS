using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EcoFlowMonitor.Converters;

public class CellVoltageColorConverter : IMultiValueConverter
{
    public static readonly CellVoltageColorConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isMin = values.Count > 0 && values[0] is true;
        bool isMax = values.Count > 1 && values[1] is true;

        if (isMin) return new SolidColorBrush(Color.Parse("#FF5252"));   // red = lowest
        if (isMax) return new SolidColorBrush(Color.Parse("#00E676"));   // green = highest
        return new SolidColorBrush(Color.Parse("#F0F0F0"));              // normal
    }
}
