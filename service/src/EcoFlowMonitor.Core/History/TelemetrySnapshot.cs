namespace EcoFlowMonitor.History;

public record TelemetrySnapshot(
    string DeviceSn,
    long Ts,
    float? BatteryPct,
    int? TotalInW,
    int? TotalOutW,
    string? PowerState,
    int? RemainMin,
    float? TempC,
    string Source);
