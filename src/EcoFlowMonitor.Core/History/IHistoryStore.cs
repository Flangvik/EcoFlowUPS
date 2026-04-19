namespace EcoFlowMonitor.History;

public interface IHistoryStore : IAsyncDisposable
{
    void EnqueueSnapshot(TelemetrySnapshot snapshot);
    Task<IReadOnlyList<TelemetrySnapshot>> QueryAsync(
        string deviceSn,
        DateTimeOffset from,
        DateTimeOffset to,
        Resolution resolution,
        CancellationToken ct = default);
    Task PruneAsync(TimeSpan retentionPeriod, CancellationToken ct = default);
    Task StartAsync(CancellationToken ct = default);
}
