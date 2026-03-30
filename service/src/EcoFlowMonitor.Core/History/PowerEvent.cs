namespace EcoFlowMonitor.History;

public record PowerEvent(
    string DeviceSn,
    long Ts,
    string EventType,
    string? Detail,
    string Source);
