using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.State;

public class DeviceState
{
    public BmsData? Bms { get; set; }
    public DisplayData? Display { get; set; }
    public EmsData? Ems { get; set; }
    public PowerState Power { get; set; } = new();
    public Dictionary<string, DateTime> RuleLastFired { get; set; } = new();
    public string? DeviceName { get; set; }
    public string? SerialNumber { get; set; }
    public bool IsConnected { get; set; }
    public DateTime LastUpdated { get; set; }
}
