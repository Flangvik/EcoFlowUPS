namespace EcoFlowMonitor.Models
{
    public class BmsData
    {
        public float? BatteryPct { get; set; }
        public float? VoltageV { get; set; }
        public float? CurrentA { get; set; }
        public float? TempC { get; set; }
        public int? RemainMin { get; set; }
        public int? Cycles { get; set; }
        public int? SohPct { get; set; }
        public int? InputW { get; set; }
        public int? OutputW { get; set; }
    }
}
