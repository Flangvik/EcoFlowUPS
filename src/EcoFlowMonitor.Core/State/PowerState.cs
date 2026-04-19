namespace EcoFlowMonitor.State;

public class PowerState
{
    public PowerStatus Status { get; set; } = PowerStatus.Unknown;
    public int LastInputW { get; set; }
    public DateTime? LostAt { get; set; }
    public DateTime? RestoredAt { get; set; }
}
