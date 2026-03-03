using System.Collections.Generic;

namespace EcoFlowMonitor.Models
{
    public class DeviceConfig
    {
        public string DisplayName { get; set; } = "EcoFlow Device";
        public string Email { get; set; }
        public string Password { get; set; }
        // Leave empty to auto-discover first device
        public string SerialNumber { get; set; }
        public List<RuleConfig> Rules { get; set; } = new List<RuleConfig>();
    }
}
