using CommunityToolkit.Mvvm.ComponentModel;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.ViewModels.Automation;

/// <summary>
/// One row in the composite-condition editor. Mirrors the per-trigger-type
/// input UX of the single-trigger editor. Binds flat properties so XAML can
/// drive different NumericUpDowns per type without nested nulls.
/// </summary>
public partial class ConditionRowViewModel : ViewModelBase
{
    /// <summary>Exposed on the row itself so the ComboBox can bind to the row's
    /// own DataContext — avoids a <c>$parent[ItemsControl]</c> lookup race
    /// against SelectedItem binding (same pattern as <c>ActionRowViewModel</c>).</summary>
    public static TriggerType[] AllTriggerTypes { get; } = Enum.GetValues<TriggerType>();

    [ObservableProperty] private TriggerType _triggerType = TriggerType.BatteryBelow;

    [ObservableProperty] private int _triggerPercent          = 20;   // BatteryBelow/Above
    [ObservableProperty] private int _triggerMinutes          = 10;   // TimeRemainingBelow
    [ObservableProperty] private int _triggerWatts            = 50;   // InputWattsBelow / OutputWattsAbove
    [ObservableProperty] private decimal _triggerTempC        = 45m;  // TempAbove/Below
    [ObservableProperty] private int _triggerOfflineSeconds   = 300;  // DeviceOffline window

    // -- Per-type UI gates --------------------------------------------------

    public bool IsEdgeOnly        => TriggerType is TriggerType.PowerLost or TriggerType.PowerRestored
                                                 or TriggerType.AcPlugged or TriggerType.AcUnplugged
                                                 or TriggerType.DeviceOnline;

    public bool IsPercentTrigger  => TriggerType is TriggerType.BatteryBelow or TriggerType.BatteryAbove;
    public bool IsMinutesTrigger  => TriggerType is TriggerType.TimeRemainingBelow;
    public bool IsWattsTrigger    => TriggerType is TriggerType.InputWattsBelow or TriggerType.OutputWattsAbove;
    public bool IsTempTrigger     => TriggerType is TriggerType.TempAbove or TriggerType.TempBelow;
    public bool IsOfflineTrigger  => TriggerType is TriggerType.DeviceOffline;

    public string ThresholdLabel => TriggerType switch
    {
        TriggerType.BatteryBelow       => "Battery below",
        TriggerType.BatteryAbove       => "Battery above",
        TriggerType.TimeRemainingBelow => "Remaining runtime below",
        TriggerType.InputWattsBelow    => "Total input below",
        TriggerType.OutputWattsAbove   => "Total output exceeds",
        TriggerType.TempAbove          => "Temperature above",
        TriggerType.TempBelow          => "Temperature below",
        TriggerType.DeviceOffline      => "Offline for at least",
        _                              => "",
    };

    partial void OnTriggerTypeChanged(TriggerType value)
    {
        OnPropertyChanged(nameof(IsEdgeOnly));
        OnPropertyChanged(nameof(IsPercentTrigger));
        OnPropertyChanged(nameof(IsMinutesTrigger));
        OnPropertyChanged(nameof(IsWattsTrigger));
        OnPropertyChanged(nameof(IsTempTrigger));
        OnPropertyChanged(nameof(IsOfflineTrigger));
        OnPropertyChanged(nameof(ThresholdLabel));
    }

    public ConditionConfig ToConfig()
    {
        var c = new ConditionConfig { Type = TriggerType };
        switch (TriggerType)
        {
            case TriggerType.BatteryBelow:
            case TriggerType.BatteryAbove:
                c.Threshold = TriggerPercent;
                break;
            case TriggerType.TimeRemainingBelow:
                c.Threshold = TriggerMinutes;
                break;
            case TriggerType.InputWattsBelow:
            case TriggerType.OutputWattsAbove:
                c.Threshold = TriggerWatts;
                break;
            case TriggerType.TempAbove:
            case TriggerType.TempBelow:
                c.ThresholdF = (float)TriggerTempC;
                break;
            case TriggerType.DeviceOffline:
                c.WindowSeconds = TriggerOfflineSeconds;
                break;
            // edge triggers carry no threshold
        }
        return c;
    }

    public static ConditionRowViewModel FromConfig(ConditionConfig c)
    {
        var row = new ConditionRowViewModel { TriggerType = c.Type };
        switch (c.Type)
        {
            case TriggerType.BatteryBelow:
            case TriggerType.BatteryAbove:
                row.TriggerPercent = Math.Clamp(c.Threshold, 0, 100);
                break;
            case TriggerType.TimeRemainingBelow:
                row.TriggerMinutes = Math.Clamp(c.Threshold, 1, 1440);
                break;
            case TriggerType.InputWattsBelow:
            case TriggerType.OutputWattsAbove:
                row.TriggerWatts = Math.Clamp(c.Threshold, 0, 10000);
                break;
            case TriggerType.TempAbove:
            case TriggerType.TempBelow:
                row.TriggerTempC = c.ThresholdF.HasValue
                    ? (decimal)c.ThresholdF.Value
                    : (decimal)Math.Clamp(c.Threshold, -40, 120);
                break;
            case TriggerType.DeviceOffline:
                row.TriggerOfflineSeconds = Math.Clamp(c.WindowSeconds ?? 300, 30, 86400);
                break;
        }
        return row;
    }
}
