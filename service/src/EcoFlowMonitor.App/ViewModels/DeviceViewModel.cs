using CommunityToolkit.Mvvm.ComponentModel;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.State;

namespace EcoFlowMonitor.ViewModels;

public partial class DeviceViewModel : ViewModelBase
{
    private readonly DeviceConfig _device;

    public string DisplayName => _device.DisplayName;
    public string? SerialNumber => _device.SerialNumber;
    public DeviceConfig Config => _device;
    public string ConnectionBadge => _device.ConnectionMode switch
    {
        ConnectionMode.Ble => "BLE",
        ConnectionMode.Auto => _device.HasBle ? "AUTO" : "CLOUD",
        _ => "CLOUD"
    };

    // Live state
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private PowerStatus _powerStatus = PowerStatus.Unknown;
    [ObservableProperty] private float _batteryPct;
    [ObservableProperty] private int _totalInW;
    [ObservableProperty] private int _totalOutW;
    [ObservableProperty] private int _solarW;
    [ObservableProperty] private string _remainingTime = "--";
    [ObservableProperty] private float _voltageV;
    [ObservableProperty] private float _tempC;
    [ObservableProperty] private int _cycles;
    [ObservableProperty] private int _sohPct;
    [ObservableProperty] private int _acFreqHz;
    [ObservableProperty] private int _usbOutW;
    [ObservableProperty] private int _upsMode;
    [ObservableProperty] private int _fanLevel;
    [ObservableProperty] private int _cellSpreadMv;
    [ObservableProperty] private int _battsConnected;
    [ObservableProperty] private int _battsTotal;
    [ObservableProperty] private string _lastUpdated = "--:--:--";

    // Power history for chart
    public List<PowerHistoryPoint> PowerHistory { get; } = new();

    public DeviceViewModel(DeviceConfig device)
    {
        _device = device;
    }

    public void UpdateFromState(DeviceState state)
    {
        IsConnected = state.IsConnected;
        PowerStatus = state.Power.Status;
        LastUpdated = state.LastUpdated.ToString("HH:mm:ss");

        if (state.Bms != null)
        {
            BatteryPct = state.Bms.BatteryPct ?? 0;
            VoltageV = state.Bms.VoltageV ?? 0;
            TempC = state.Bms.TempC ?? 0;
            Cycles = state.Bms.Cycles ?? 0;
            SohPct = state.Bms.SohPct ?? 0;
            CellSpreadMv = (state.Bms.MaxCellMv ?? 0) - (state.Bms.MinCellMv ?? 0);

            if (state.Bms.RemainMin.HasValue)
            {
                int h = state.Bms.RemainMin.Value / 60;
                int m = state.Bms.RemainMin.Value % 60;
                RemainingTime = $"{h}h {m:D2}m";
            }
        }

        if (state.Display != null)
        {
            TotalInW = state.Display.TotalInW ?? 0;
            TotalOutW = state.Display.TotalOutW ?? 0;
            SolarW = (state.Display.SolarInHighW ?? 0) + (state.Display.SolarInLowW ?? 0);
            AcFreqHz = state.Display.AcInFreqHz ?? 0;
            UsbOutW = (state.Display.UsbA1W ?? 0) + (state.Display.UsbA2W ?? 0) +
                      (state.Display.UsbC1W ?? 0) + (state.Display.UsbC2W ?? 0);
        }

        if (state.Ems != null)
        {
            UpsMode = state.Ems.UpsMode ?? 0;
            FanLevel = state.Ems.FanLevel ?? 0;
            if (state.Ems.BmsConnected != null)
            {
                BattsConnected = state.Ems.BmsConnected.Count(v => v != 0);
                BattsTotal = state.Ems.BmsConnected.Length;
            }
        }

        // Add to power history (keep last 60 points)
        PowerHistory.Add(new PowerHistoryPoint(state.LastUpdated, TotalInW, TotalOutW));
        if (PowerHistory.Count > 60)
            PowerHistory.RemoveAt(0);
    }
}

public record PowerHistoryPoint(DateTime Time, int InputW, int OutputW);
