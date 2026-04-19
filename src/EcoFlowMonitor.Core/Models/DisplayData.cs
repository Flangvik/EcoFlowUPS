namespace EcoFlowMonitor.Models;

public class DisplayData
{
    public int? TotalInW { get; set; }
    public int? TotalOutW { get; set; }
    public int? AcInW { get; set; }
    public int? SolarInHighW { get; set; }
    public int? SolarInLowW { get; set; }
    public int? UsbA1W { get; set; }
    public int? UsbA2W { get; set; }
    public int? UsbC1W { get; set; }
    public int? UsbC2W { get; set; }
    public bool? AcPluggedIn { get; set; }
    public int? AcInFreqHz { get; set; }
}
