using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.Converters;

public class StatusColorConverter : IValueConverter
{
    public static readonly StatusColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool connected)
            return connected ? Color.Parse("#00E676") : Color.Parse("#666666");
        return Color.Parse("#666666");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StatusTextConverter : IValueConverter
{
    public static readonly StatusTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            PowerStatus.Charging => "Charging",
            PowerStatus.PowerLost => "Power Lost",
            PowerStatus.Idle => "Idle",
            _ => "Unknown"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StatusBrushConverter : IValueConverter
{
    public static readonly StatusBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            PowerStatus.Charging => new SolidColorBrush(Color.Parse("#00E676")),
            PowerStatus.PowerLost => new SolidColorBrush(Color.Parse("#FF5252")),
            PowerStatus.Idle => new SolidColorBrush(Color.Parse("#666666")),
            _ => new SolidColorBrush(Color.Parse("#555555"))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ConnectedTextConverter : IValueConverter
{
    public static readonly ConnectedTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool connected && connected ? "Connected" : "Disconnected";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class WattColorConverter : IValueConverter
{
    public static readonly WattColorConverter InstanceGreen = new("#00E676");
    public static readonly WattColorConverter InstanceOrange = new("#FF9100");
    public static readonly WattColorConverter InstanceGold = new("#FFD600");

    private readonly string _activeColor;

    private WattColorConverter(string color) => _activeColor = color;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var watts = value is int w ? w : value is double d ? (int)d : 0;
        return watts > 0
            ? new SolidColorBrush(Color.Parse(_activeColor))
            : new SolidColorBrush(Color.Parse("#888888"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class UpsModeConverter : IValueConverter
{
    public static readonly UpsModeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int mode && mode > 0 ? $"UPS {mode}" : "Normal";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class FanLevelConverter : IValueConverter
{
    public static readonly FanLevelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int level && level > 0 ? $"Lvl {level}" : "Off";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
