using System.Collections.Generic;

namespace EcoFlowMonitor.Models
{
    public class AppConfig
    {
        public AccountConfig      Account  { get; set; }
        public List<DeviceConfig> Devices  { get; set; } = new List<DeviceConfig>();
        public GeneralSettings    General  { get; set; } = new GeneralSettings();

        public bool IsConfigured => Account != null && !string.IsNullOrEmpty(Account.Email);
    }

    public class GeneralSettings
    {
        public bool   StartWithWindows { get; set; } = false;
        public string ErrorLogPath     { get; set; } = "";
        public bool   DarkMode         { get; set; } = true;
    }
}
