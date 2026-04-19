using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.History;

/// <summary>
/// Persistent audit log for rule firings. Backed by the <c>rule_firings</c> and
/// <c>rule_firing_actions</c> tables in history.db (see
/// contracts/rule-firing-audit.sql). Lifecycle mirrors
/// <see cref="IHistoryStore"/>: call <see cref="StartAsync"/> on app init,
/// enqueue rows from the monitor pipeline, query for the UI.
/// </summary>
public interface IRuleFiringStore : IAsyncDisposable
{
    /// <summary>Initialize the connection and ensure the schema exists.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Append a single rule-firing row plus all its child action rows.
    /// Implementation is responsible for writing atomically.
    /// </summary>
    Task AppendAsync(RuleFiring firing, CancellationToken ct = default);

    /// <summary>
    /// Fetch firings, newest first, filtered by optional device / rule / time
    /// lower-bound. <paramref name="limit"/> caps the row count returned.
    /// </summary>
    Task<IReadOnlyList<RuleFiring>> QueryAsync(
        string? deviceSerialNumber = null,
        string? ruleId = null,
        DateTimeOffset? since = null,
        int limit = 500,
        CancellationToken ct = default);

    /// <summary>
    /// Delete firings older than <paramref name="cutoffUtc"/>. Child rows
    /// cascade via <c>ON DELETE CASCADE</c> in the schema.
    /// </summary>
    Task<int> PruneOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default);
}
