namespace EcoFlowMonitor.Models;

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
    public int? DesignCapMah { get; set; }
    public int? RemainCapMah { get; set; }
    public int? MaxCellMv { get; set; }
    public int? MinCellMv { get; set; }
    public int[]? CellVolsMv { get; set; }
    public float[]? CellTempsC { get; set; }
    public float[]? MosTempsC { get; set; }
    public long? AccuChgEnergyWh { get; set; }
    public long? AccuDsgEnergyWh { get; set; }
    public string? PackSn { get; set; }
}
