using EcoFlowMonitor.Triggers;

namespace EcoFlowMonitor.Models;

public class TriggerConfig
{
    public TriggerType Type { get; set; }
    public int Threshold { get; set; }
}
