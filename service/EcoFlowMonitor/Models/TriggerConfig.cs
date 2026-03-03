using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.Models
{
    public class TriggerConfig
    {
        public TriggerType Type { get; set; }
        // Threshold value for BatteryBelow (percent) and TimeRemainingBelow (minutes)
        public int Threshold { get; set; }
    }
}
