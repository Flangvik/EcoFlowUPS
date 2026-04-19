using System.Diagnostics;
using EcoFlowMonitor.Models;
using Microsoft.Data.Sqlite;

namespace EcoFlowMonitor.History;

/// <summary>
/// SQLite-backed <see cref="IRuleFiringStore"/>. Shares the history.db file
/// with <see cref="SqliteHistoryStore"/> — a separate connection pair is held
/// to keep writes serialized without blocking telemetry writes.
/// Schema DDL matches <c>contracts/rule-firing-audit.sql</c>.
/// </summary>
public sealed class SqliteRuleFiringStore : IRuleFiringStore
{
    private readonly string _writeConnStr;
    private readonly string _readConnStr;
    private SqliteConnection? _writeConn;

    /// <summary>Serializes concurrent writers. SQLite is fine with one writer.</summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public SqliteRuleFiringStore(string dbPath)
    {
        _writeConnStr = $"Data Source={dbPath};Mode=ReadWriteCreate;Foreign Keys=True";
        _readConnStr  = $"Data Source={dbPath};Mode=ReadOnly;Foreign Keys=True";
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _writeConn = new SqliteConnection(_writeConnStr);
        await ConfigureConnectionAsync(_writeConn).ConfigureAwait(false);
        await CreateSchemaAsync(_writeConn).ConfigureAwait(false);
    }

    public async Task AppendAsync(RuleFiring firing, CancellationToken ct = default)
    {
        if (_writeConn is null)
            throw new InvalidOperationException("Store not started; call StartAsync first.");

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var tx = _writeConn.BeginTransaction();

            long firingId;
            using (var cmd = _writeConn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO rule_firings
    (ts, rule_id, rule_name, device_sn, trigger_type, trigger_value_json, is_test)
VALUES (@ts, @rule_id, @rule_name, @device_sn, @trigger_type, @trigger_value_json, @is_test);
SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@ts",                 firing.Timestamp.ToUnixTimeSeconds());
                cmd.Parameters.AddWithValue("@rule_id",            firing.RuleId);
                cmd.Parameters.AddWithValue("@rule_name",          firing.RuleName);
                cmd.Parameters.AddWithValue("@device_sn",          firing.DeviceSerialNumber);
                cmd.Parameters.AddWithValue("@trigger_type",       firing.TriggerType);
                cmd.Parameters.AddWithValue("@trigger_value_json", firing.TriggerValueJson);
                cmd.Parameters.AddWithValue("@is_test",            firing.IsTest ? 1 : 0);

                firingId = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            }

            using (var cmd = _writeConn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO rule_firing_actions
    (firing_id, ordinal, action_type, outcome, duration_ms, error_text, detail_json)
VALUES (@firing_id, @ordinal, @action_type, @outcome, @duration_ms, @error_text, @detail_json)";
                var pFid = cmd.Parameters.Add("@firing_id",   SqliteType.Integer);
                var pOrd = cmd.Parameters.Add("@ordinal",     SqliteType.Integer);
                var pAt  = cmd.Parameters.Add("@action_type", SqliteType.Text);
                var pOut = cmd.Parameters.Add("@outcome",     SqliteType.Text);
                var pDur = cmd.Parameters.Add("@duration_ms", SqliteType.Integer);
                var pErr = cmd.Parameters.Add("@error_text",  SqliteType.Text);
                var pDet = cmd.Parameters.Add("@detail_json", SqliteType.Text);

                foreach (var act in firing.Actions)
                {
                    pFid.Value = firingId;
                    pOrd.Value = act.Ordinal;
                    pAt.Value  = act.ActionType;
                    pOut.Value = OutcomeToString(act.Outcome);
                    pDur.Value = act.DurationMs;
                    pErr.Value = act.ErrorText is null ? DBNull.Value : Truncate(act.ErrorText, 512);
                    pDet.Value = act.DetailJson is null ? DBNull.Value : (object)act.DetailJson;
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SqliteRuleFiringStore] AppendAsync failed: {ex.Message}");
            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<RuleFiring>> QueryAsync(
        string? deviceSerialNumber = null,
        string? ruleId = null,
        DateTimeOffset? since = null,
        int limit = 500,
        CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_readConnStr);
        await ConfigureConnectionAsync(conn).ConfigureAwait(false);

        // -- Fetch parent rows --
        var firings = new Dictionary<long, (RuleFiring firing, List<RuleFiringAction> acts)>();

        using (var cmd = conn.CreateCommand())
        {
            var sql = @"
SELECT id, ts, rule_id, rule_name, device_sn, trigger_type, trigger_value_json, is_test
FROM rule_firings
WHERE 1=1";
            if (deviceSerialNumber is not null) { sql += " AND device_sn = @sn";  cmd.Parameters.AddWithValue("@sn", deviceSerialNumber); }
            if (ruleId            is not null) { sql += " AND rule_id   = @rid"; cmd.Parameters.AddWithValue("@rid", ruleId); }
            if (since             is not null) { sql += " AND ts        >= @sc"; cmd.Parameters.AddWithValue("@sc", since.Value.ToUnixTimeSeconds()); }
            sql += " ORDER BY ts DESC LIMIT @lim";
            cmd.Parameters.AddWithValue("@lim", limit);
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                long id = reader.GetInt64(0);
                var firing = new RuleFiring(
                    Timestamp:         DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)),
                    RuleId:            reader.GetString(2),
                    RuleName:          reader.GetString(3),
                    DeviceSerialNumber: reader.GetString(4),
                    TriggerType:       reader.GetString(5),
                    TriggerValueJson:  reader.GetString(6),
                    Actions:           Array.Empty<RuleFiringAction>(),
                    IsTest:            reader.GetInt32(7) != 0)
                { Id = id };
                firings[id] = (firing, new List<RuleFiringAction>());
            }
        }

        if (firings.Count == 0) return Array.Empty<RuleFiring>();

        // -- Fetch child rows in a single IN query --
        using (var cmd = conn.CreateCommand())
        {
            // SQLite has no native array param; inline IDs (safe: integers we produced).
            var ids = string.Join(",", firings.Keys);
            cmd.CommandText = $@"
SELECT id, firing_id, ordinal, action_type, outcome, duration_ms, error_text, detail_json
FROM rule_firing_actions
WHERE firing_id IN ({ids})
ORDER BY firing_id, ordinal";
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                long id       = reader.GetInt64(0);
                long fid      = reader.GetInt64(1);
                if (!firings.TryGetValue(fid, out var entry)) continue;
                entry.acts.Add(new RuleFiringAction(
                    Ordinal:    reader.GetInt32(2),
                    ActionType: reader.GetString(3),
                    Outcome:    ParseOutcome(reader.GetString(4)),
                    DurationMs: reader.GetInt32(5),
                    ErrorText:  reader.IsDBNull(6) ? null : reader.GetString(6),
                    DetailJson: reader.IsDBNull(7) ? null : reader.GetString(7))
                {
                    Id       = id,
                    FiringId = fid,
                });
            }
        }

        // -- Compose results in descending-ts order (same as parent query) --
        return firings
            .OrderByDescending(kvp => kvp.Value.firing.Timestamp)
            .Select(kvp => kvp.Value.firing with { Actions = kvp.Value.acts })
            .ToList();
    }

    public async Task<int> PruneOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default)
    {
        if (_writeConn is null) return 0;
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = _writeConn.CreateCommand();
            cmd.CommandText = "DELETE FROM rule_firings WHERE ts < @cutoff";
            cmd.Parameters.AddWithValue("@cutoff", cutoffUtc.ToUnixTimeSeconds());
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_writeConn is not null)
            await _writeConn.DisposeAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }

    // -- Private helpers --

    private static async Task ConfigureConnectionAsync(SqliteConnection conn)
    {
        await conn.OpenAsync().ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000; PRAGMA foreign_keys=ON;";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task CreateSchemaAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS rule_firings (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    ts                 INTEGER NOT NULL,
    rule_id            TEXT    NOT NULL,
    rule_name          TEXT    NOT NULL,
    device_sn          TEXT    NOT NULL,
    trigger_type       TEXT    NOT NULL,
    trigger_value_json TEXT    NOT NULL,
    is_test            INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_rule_firings_ts
    ON rule_firings(ts DESC);
CREATE INDEX IF NOT EXISTS ix_rule_firings_rule
    ON rule_firings(rule_id, ts DESC);

CREATE TABLE IF NOT EXISTS rule_firing_actions (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    firing_id      INTEGER NOT NULL
                   REFERENCES rule_firings(id) ON DELETE CASCADE,
    ordinal        INTEGER NOT NULL,
    action_type    TEXT    NOT NULL,
    outcome        TEXT    NOT NULL
                   CHECK (outcome IN
                       ('success', 'failure', 'skipped', 'timeout', 'dropped')),
    duration_ms    INTEGER NOT NULL,
    error_text     TEXT    NULL,
    detail_json    TEXT    NULL
);
CREATE INDEX IF NOT EXISTS ix_rule_firing_actions_firing
    ON rule_firing_actions(firing_id, ordinal);";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string OutcomeToString(RuleFiringActionOutcome o) => o switch
    {
        RuleFiringActionOutcome.Success => "success",
        RuleFiringActionOutcome.Failure => "failure",
        RuleFiringActionOutcome.Skipped => "skipped",
        RuleFiringActionOutcome.Timeout => "timeout",
        RuleFiringActionOutcome.Dropped => "dropped",
        _ => "failure",
    };

    private static RuleFiringActionOutcome ParseOutcome(string s) => s switch
    {
        "success" => RuleFiringActionOutcome.Success,
        "failure" => RuleFiringActionOutcome.Failure,
        "skipped" => RuleFiringActionOutcome.Skipped,
        "timeout" => RuleFiringActionOutcome.Timeout,
        "dropped" => RuleFiringActionOutcome.Dropped,
        _         => RuleFiringActionOutcome.Failure,
    };

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max);
}
