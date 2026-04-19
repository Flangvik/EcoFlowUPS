namespace EcoFlowMonitor.Models;

/// <summary>
/// Outcome of a single action within a rule firing, persisted as an audit row
/// in the <c>rule_firing_actions</c> table.
/// </summary>
public enum RuleFiringActionOutcome
{
    Success,
    Failure,
    Skipped,
    Timeout,
    Dropped,
}

/// <summary>
/// One child row per executed (or attempted) action in a rule firing.
/// Matches the <c>rule_firing_actions</c> table in history.db — see
/// contracts/rule-firing-audit.sql.
/// </summary>
public sealed record RuleFiringAction(
    int                       Ordinal,
    string                    ActionType,      // "Webhook", "RunCommand", "Shutdown", etc.
    RuleFiringActionOutcome   Outcome,
    int                       DurationMs,
    string?                   ErrorText   = null,    // first 512 chars
    string?                   DetailJson  = null)    // type-specific JSON (HTTP status, exit code, ...)
{
    // Id is assigned by SqliteRuleFiringStore on insert.
    public long Id { get; init; }

    /// <summary>Foreign key to <see cref="RuleFiring.Id"/>. Set by the store on insert.</summary>
    public long FiringId { get; init; }
}

/// <summary>
/// One row per time a rule fired (real or test). Matches the
/// <c>rule_firings</c> table in history.db — see
/// contracts/rule-firing-audit.sql.
/// </summary>
public sealed record RuleFiring(
    DateTimeOffset            Timestamp,
    string                    RuleId,
    string                    RuleName,
    string                    DeviceSerialNumber,
    string                    TriggerType,
    string                    TriggerValueJson,     // frozen DeviceStateSnapshot + trigger params
    IReadOnlyList<RuleFiringAction> Actions,
    bool                      IsTest = false)
{
    // Id is assigned by SqliteRuleFiringStore on insert.
    public long Id { get; init; }
}
