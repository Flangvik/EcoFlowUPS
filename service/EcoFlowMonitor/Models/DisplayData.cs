namespace EcoFlowMonitor.Models
{
    public class DisplayData
    {
        // Power totals
        public int? TotalInW { get; set; }
        public int? TotalOutW { get; set; }
        public int? AcInW { get; set; }

        // Solar
        public int? SolarInHighW { get; set; }
        public int? SolarInLowW { get; set; }

        // USB ports
        public int? UsbA1W { get; set; }
        public int? UsbA2W { get; set; }
        public int? UsbC1W { get; set; }
        public int? UsbC2W { get; set; }

        // AC status
        public bool? AcPluggedIn { get; set; }
        public int? AcInFreqHz { get; set; }
    }
}
