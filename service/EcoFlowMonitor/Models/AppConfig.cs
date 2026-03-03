using System.Collections.Generic;

namespace EcoFlowMonitor.Models
{
    public class AppConfig
    {
        public List<DeviceConfig> Devices { get; set; } = new List<DeviceConfig>();
        public GeneralSettings General { get; set; } = new GeneralSettings();
    }

    public class GeneralSettings
    {
        public bool StartWithWindows { get; set; } = false;
        public string ErrorLogPath { get; set; } = "";
        public bool DarkMode { get; set; } = true;
    }
}
