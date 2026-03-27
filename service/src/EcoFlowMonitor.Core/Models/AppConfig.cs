namespace EcoFlowMonitor.Models;

public class AppConfig
{
    public AccountConfig? Account { get; set; }
    public List<DeviceConfig> Devices { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public string LocalUserId { get; set; } = "";
    public bool IsConfigured =>
        (Account != null && !string.IsNullOrEmpty(Account.Email))
        || Devices.Any(d => d.ConnectionType == ConnectionType.Ble);
}

public class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public string ErrorLogPath { get; set; } = "";
    public bool DarkMode { get; set; } = true;
}
