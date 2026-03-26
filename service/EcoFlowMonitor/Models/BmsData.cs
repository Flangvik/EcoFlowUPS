namespace EcoFlowMonitor.Models
{
    public class BmsData
    {
        // Core
        public float? BatteryPct { get; set; }
        public float? VoltageV { get; set; }
        public float? CurrentA { get; set; }
        public float? TempC { get; set; }
        public int? RemainMin { get; set; }
        public int? Cycles { get; set; }
        public int? SohPct { get; set; }
        public int? InputW { get; set; }
        public int? OutputW { get; set; }

        // Capacity
        public int? DesignCapMah { get; set; }
        public int? RemainCapMah { get; set; }

        // Cell stats (min/max from device-reported values)
        public int? MaxCellMv { get; set; }
        public int? MinCellMv { get; set; }

        // Packed repeated fields
        public int[] CellVolsMv { get; set; }
        public float[] CellTempsC { get; set; }
        public float[] MosTempsC { get; set; }

        // Lifetime energy counters
        public long? AccuChgEnergyWh { get; set; }
        public long? AccuDsgEnergyWh { get; set; }

        // Pack identity
        public string PackSn { get; set; }
    }
}
