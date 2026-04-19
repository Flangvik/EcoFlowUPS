namespace EcoFlowMonitor.History;

public record PowerEventItem(long Ts, string EventType, string? Detail, string Source)
{
    public string TimeLabel => DateTimeOffset.FromUnixTimeSeconds(Ts).LocalDateTime.ToString("HH:mm:ss");
    public string DayLabel  => DateTimeOffset.FromUnixTimeSeconds(Ts).LocalDateTime.ToString("ddd, MMM d");
}
