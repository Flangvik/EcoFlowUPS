using System.Collections.Generic;

namespace EcoFlowMonitor.Models
{
    public class DeviceConfig
    {
        public string          DisplayName  { get; set; } = "EcoFlow Device";
        public string          SerialNumber { get; set; }
        public List<RuleConfig> Rules       { get; set; } = new List<RuleConfig>();
    }
}
