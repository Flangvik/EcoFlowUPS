using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EcoFlowMonitor.Converters;

/// <summary>
/// Colours a single cell-voltage value based on the LFP (LiFePO4) operating
/// envelope used by EcoFlow Delta 3 / Delta 3 Max.
///
/// Why absolute voltage and not min/max-of-pack: a perfectly balanced pack
/// at e.g. 3.31 V / 3.31 V / 3.32 V is healthy. The previous "always paint
/// the minimum red" rule produced a constant false-positive that made the
/// CELL VOLTAGES card useless. Absolute thresholds reflect actual battery
/// state.
///
/// Reference values for LFP (LiFePO4):
///   2.50 V   BMS cutoff (deep-discharge guard)
///   2.80 V   Recommended discharge floor
///   3.00 V   Bottom of the flat plateau — anything below = nearly empty
///   3.20 V   Nominal cell voltage (most of SOC sits here)
///   3.40 V   Top of the flat plateau
///   3.45 V   Typical BMS charge target (≈ 95-100% SOC)
///   3.55 V   Approaching maximum
///   3.65 V   Absolute upper limit (over-voltage on any cell = damage risk)
///
/// Healthy cell-balance spread should sit under ~30 mV; pack-imbalance
/// flagging belongs on the CELL ΔmV stat card, not this per-cell colourer.
/// </summary>
public class CellVoltageColorConverter : IValueConverter
{
    public static readonly CellVoltageColorConverter Instance = new();

    // LFP voltage thresholds (millivolts).
    private const int CriticalLowMv      = 2800;   // < this → BMS-floor territory
    private const int LowMv              = 3000;   // < this → low (near empty)
    private const int ApproachingFullMv  = 3500;   // ≥ this → upper end of charge curve
    private const int CriticalHighMv     = 3600;   // ≥ this → over-voltage risk

    // Palette — matches the rest of the dashboard.
    private static readonly IBrush Critical = new SolidColorBrush(Color.Parse("#FF5252")); // red
    private static readonly IBrush Warning  = new SolidColorBrush(Color.Parse("#FFB300")); // amber
    private static readonly IBrush Notice   = new SolidColorBrush(Color.Parse("#FFD740")); // yellow
    private static readonly IBrush Normal   = new SolidColorBrush(Color.Parse("#F0F0F0")); // default text

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int mv) return Normal;

        if (mv <= 0)                    return Normal;       // unknown / no data yet
        if (mv < CriticalLowMv)         return Critical;     // < 2.80 V — danger
        if (mv < LowMv)                 return Warning;      // 2.80–3.00 V — low
        if (mv >= CriticalHighMv)       return Critical;     // ≥ 3.60 V — over-voltage
        if (mv >= ApproachingFullMv)    return Notice;       // 3.50–3.60 V — near full
        return Normal;                                       // 3.00–3.50 V — healthy operating range
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
