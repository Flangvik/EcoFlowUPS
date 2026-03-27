using System.Collections.ObjectModel;
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

    // Connection info
    [ObservableProperty] private string _connectionBadge = "CLOUD";
    [ObservableProperty] private string _activeSource = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ConnectionMode _connectionMode;

    // ── Core stats ──
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private PowerStatus _powerStatus = PowerStatus.Unknown;
    [ObservableProperty] private float _batteryPct;
    [ObservableProperty] private int _totalInW;
    [ObservableProperty] private int _totalOutW;
    [ObservableProperty] private int _solarW;
    [ObservableProperty] private string _remainingTime = "--";
    [ObservableProperty] private string _lastUpdated = "--:--:--";

    // ── Battery / BMS ──
    [ObservableProperty] private float _voltageV;
    [ObservableProperty] private float _currentA;
    [ObservableProperty] private float _tempC;
    [ObservableProperty] private int _cycles;
    [ObservableProperty] private int _sohPct;
    [ObservableProperty] private int _cellSpreadMv;
    [ObservableProperty] private int _designCapMah;
    [ObservableProperty] private int _remainCapMah;
    [ObservableProperty] private long _accuChgWh;
    [ObservableProperty] private long _accuDsgWh;
    [ObservableProperty] private string _packSn = "";

    // ── Cell voltages (up to 16 cells) ──
    public ObservableCollection<CellVoltageItem> CellVoltages { get; } = new();

    // ── Power / Display ──
    [ObservableProperty] private int _acFreqHz;
    [ObservableProperty] private int _usbOutW;
    [ObservableProperty] private bool _acPluggedIn;
    [ObservableProperty] private int _acInW;

    // ── EMS / System ──
    [ObservableProperty] private int _upsMode;
    [ObservableProperty] private int _fanLevel;
    [ObservableProperty] private int _battsConnected;
    [ObservableProperty] private int _battsTotal;
    [ObservableProperty] private int _maxChgSoc;
    [ObservableProperty] private string _chargeTime = "--";

    // Power history for chart
    public List<PowerHistoryPoint> PowerHistory { get; } = new();

    public DeviceViewModel(DeviceConfig device)
    {
        _device = device;
        ConnectionMode = device.ConnectionMode;
        UpdateBadge();
    }

    public void UpdateBadge()
    {
        ConnectionBadge = _device.ConnectionMode switch
        {
            ConnectionMode.Ble => "BLE",
            ConnectionMode.Auto => "AUTO",
            _ => "CLOUD"
        };
    }

    public void SetActiveSource(string source) => ActiveSource = source;

    public void CycleConnectionMode()
    {
        _device.ConnectionMode = _device.ConnectionMode switch
        {
            ConnectionMode.Cloud => _device.HasBle ? ConnectionMode.Auto : ConnectionMode.Cloud,
            ConnectionMode.Auto => ConnectionMode.Ble,
            ConnectionMode.Ble => ConnectionMode.Cloud,
            _ => ConnectionMode.Cloud
        };
        ConnectionMode = _device.ConnectionMode;
        UpdateBadge();
    }

    public void UpdateFromState(DeviceState state)
    {
        IsConnected = state.IsConnected;
        PowerStatus = state.Power.Status;
        LastUpdated = state.LastUpdated.ToString("HH:mm:ss");

        if (state.Bms != null)
        {
            var b = state.Bms;
            BatteryPct = b.BatteryPct ?? BatteryPct;
            VoltageV = b.VoltageV ?? VoltageV;
            CurrentA = b.CurrentA ?? CurrentA;
            TempC = b.TempC ?? TempC;
            Cycles = b.Cycles ?? Cycles;
            SohPct = b.SohPct ?? SohPct;
            CellSpreadMv = (b.MaxCellMv ?? 0) - (b.MinCellMv ?? 0);
            DesignCapMah = b.DesignCapMah ?? DesignCapMah;
            RemainCapMah = b.RemainCapMah ?? RemainCapMah;
            AccuChgWh = b.AccuChgEnergyWh ?? AccuChgWh;
            AccuDsgWh = b.AccuDsgEnergyWh ?? AccuDsgWh;
            if (!string.IsNullOrEmpty(b.PackSn)) PackSn = b.PackSn;

            if (b.RemainMin.HasValue)
            {
                int h = b.RemainMin.Value / 60;
                int m = b.RemainMin.Value % 60;
                RemainingTime = $"{h}h {m:D2}m";
            }

            // Update cell voltages
            if (b.CellVolsMv != null && b.CellVolsMv.Length > 0)
            {
                while (CellVoltages.Count < b.CellVolsMv.Length)
                    CellVoltages.Add(new CellVoltageItem());
                while (CellVoltages.Count > b.CellVolsMv.Length)
                    CellVoltages.RemoveAt(CellVoltages.Count - 1);

                int min = b.CellVolsMv.Min();
                int max = b.CellVolsMv.Max();
                for (int i = 0; i < b.CellVolsMv.Length; i++)
                {
                    CellVoltages[i].Index = i + 1;
                    CellVoltages[i].MilliVolts = b.CellVolsMv[i];
                    CellVoltages[i].IsMin = b.CellVolsMv[i] == min;
                    CellVoltages[i].IsMax = b.CellVolsMv[i] == max;
                }
            }
        }

        if (state.Display != null)
        {
            var d = state.Display;
            TotalInW = d.TotalInW ?? TotalInW;
            TotalOutW = d.TotalOutW ?? TotalOutW;
            SolarW = (d.SolarInHighW ?? 0) + (d.SolarInLowW ?? 0);
            AcFreqHz = d.AcInFreqHz ?? AcFreqHz;
            AcInW = d.AcInW ?? AcInW;
            AcPluggedIn = d.AcPluggedIn ?? AcPluggedIn;
            UsbOutW = (d.UsbA1W ?? 0) + (d.UsbA2W ?? 0) +
                      (d.UsbC1W ?? 0) + (d.UsbC2W ?? 0);
        }

        if (state.Ems != null)
        {
            var e = state.Ems;
            UpsMode = e.UpsMode ?? UpsMode;
            FanLevel = e.FanLevel ?? FanLevel;
            MaxChgSoc = e.MaxChargeSoc ?? MaxChgSoc;
            if (e.BmsConnected != null)
            {
                BattsConnected = e.BmsConnected.Count(v => v != 0);
                BattsTotal = e.BmsConnected.Length;
            }
            if (e.ChgRemainMin.HasValue && e.ChgRemainMin.Value > 0 && e.ChgRemainMin.Value < 100000)
            {
                int h = e.ChgRemainMin.Value / 60;
                int m = e.ChgRemainMin.Value % 60;
                ChargeTime = $"{h}h {m:D2}m";
            }
        }

        PowerHistory.Add(new PowerHistoryPoint(state.LastUpdated, TotalInW, TotalOutW));
        if (PowerHistory.Count > 60)
            PowerHistory.RemoveAt(0);
    }
}

public class CellVoltageItem : ObservableObject
{
    private int _index;
    private int _milliVolts;
    private bool _isMin;
    private bool _isMax;

    public int Index { get => _index; set => SetProperty(ref _index, value); }
    public int MilliVolts { get => _milliVolts; set => SetProperty(ref _milliVolts, value); }
    public bool IsMin { get => _isMin; set => SetProperty(ref _isMin, value); }
    public bool IsMax { get => _isMax; set => SetProperty(ref _isMax, value); }
}

public record PowerHistoryPoint(DateTime Time, int InputW, int OutputW);
