namespace EcoFlowMonitor.Models;

public class EmsData
{
    public int? ChgState { get; set; }
    public int? FanLevel { get; set; }
    public int? MaxChargeSoc { get; set; }
    public int? UpsMode { get; set; }
    public int? ChgRemainMin { get; set; }
    public int? DsgRemainMin { get; set; }
    public int[]? BmsConnected { get; set; }
    public int? ChgLinePlugged { get; set; }
}
