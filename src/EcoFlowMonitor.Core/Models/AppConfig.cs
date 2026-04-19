namespace EcoFlowMonitor.Models;

public class AppConfig
{
    public AccountConfig? Account { get; set; }
    public List<DeviceConfig> Devices { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public string LocalUserId { get; set; } = "";
    public string CloudUserId { get; set; } = "";
    public bool IsConfigured =>
        (Account != null && !string.IsNullOrEmpty(Account.Email))
        || Devices.Any(d => d.HasBle);
}

public class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public string ErrorLogPath { get; set; } = "";
    public bool DarkMode { get; set; } = true;

    // -- Rules-engine settings (feature 001) --

    /// <summary>Audit log retention in days. Default 30.</summary>
    public int AuditRetentionDays { get; set; } = 30;

    /// <summary>Global concurrent-action cap. Default 8. Range 1..64.</summary>
    public int MaxConcurrentActions { get; set; } = 8;

    /// <summary>Bounded queue capacity for pending rule fires. Default 256.</summary>
    public int ActionQueueCapacity { get; set; } = 256;
}
