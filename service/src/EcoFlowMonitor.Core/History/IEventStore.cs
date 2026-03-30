namespace EcoFlowMonitor.History;

public interface IEventStore : IAsyncDisposable
{
    void EnqueueEvent(PowerEvent evt);
    Task<IReadOnlyList<PowerEventItem>> QueryAsync(
        string deviceSn,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);
    Task StartAsync(CancellationToken ct = default);
}
