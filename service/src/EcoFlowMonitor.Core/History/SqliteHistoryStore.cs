using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace EcoFlowMonitor.History;

public sealed class SqliteHistoryStore : IHistoryStore
{
    private readonly string _writeConnStr;
    private readonly string _readConnStr;
    private SqliteConnection? _writeConn;
    private Task? _consumerTask;
    private CancellationTokenSource? _cts;

    private readonly Channel<TelemetrySnapshot> _writeQueue =
        Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

    public SqliteHistoryStore(string dbPath)
    {
        _writeConnStr = $"Data Source={dbPath};Mode=ReadWriteCreate";
        _readConnStr  = $"Data Source={dbPath};Mode=ReadOnly";
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _writeConn = new SqliteConnection(_writeConnStr);
        await ConfigureConnectionAsync(_writeConn).ConfigureAwait(false);
        await CreateSchemaAsync(_writeConn).ConfigureAwait(false);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _consumerTask = Task.Run(() => ConsumeWritesAsync(_cts.Token), _cts.Token);
    }

    public void EnqueueSnapshot(TelemetrySnapshot snapshot)
    {
        if (!_writeQueue.Writer.TryWrite(snapshot))
            System.Diagnostics.Debug.WriteLine("[SqliteHistoryStore] Write queue full; oldest snapshot dropped.");
    }

    public async Task<IReadOnlyList<TelemetrySnapshot>> QueryAsync(
        string deviceSn,
        DateTimeOffset from,
        DateTimeOffset to,
        Resolution resolution,
        CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_readConnStr);
        await ConfigureConnectionAsync(conn).ConfigureAwait(false);

        var results = new List<TelemetrySnapshot>();

        using var cmd = conn.CreateCommand();
        long fromTs = from.ToUnixTimeSeconds();
        long toTs   = to.ToUnixTimeSeconds();

        if (resolution == Resolution.Raw)
        {
            cmd.CommandText = @"
SELECT device_sn, ts, battery_pct, total_in_w, total_out_w, power_state, remain_min, temp_c, source
FROM telemetry_snapshots
WHERE device_sn = @sn AND ts >= @from AND ts <= @to
ORDER BY ts
LIMIT 500";
            cmd.Parameters.AddWithValue("@sn",   deviceSn);
            cmd.Parameters.AddWithValue("@from", fromTs);
            cmd.Parameters.AddWithValue("@to",   toTs);

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(new TelemetrySnapshot(
                    DeviceSn:    reader.GetString(0),
                    Ts:          reader.GetInt64(1),
                    BatteryPct:  reader.IsDBNull(2) ? null : (float?)reader.GetDouble(2),
                    TotalInW:    reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3),
                    TotalOutW:   reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                    PowerState:  reader.IsDBNull(5) ? null : reader.GetString(5),
                    RemainMin:   reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6),
                    TempC:       reader.IsDBNull(7) ? null : (float?)reader.GetDouble(7),
                    Source:      reader.IsDBNull(8) ? "Unknown" : reader.GetString(8)
                ));
            }
        }
        else
        {
            string bucketFmt = resolution switch
            {
                Resolution.Hourly => "%Y-%m-%d %H:00",
                Resolution.Daily  => "%Y-%m-%d",
                Resolution.Weekly => "%Y-W%W",
                _                 => "%Y-%m-%d"
            };

            cmd.CommandText = $@"
SELECT strftime('{bucketFmt}', ts, 'unixepoch') AS bucket,
       AVG(battery_pct),
       MAX(total_in_w),
       MAX(total_out_w),
       AVG(temp_c)
FROM telemetry_snapshots
WHERE device_sn = @sn AND ts >= @from AND ts <= @to
GROUP BY bucket
ORDER BY bucket";
            cmd.Parameters.AddWithValue("@sn",   deviceSn);
            cmd.Parameters.AddWithValue("@from", fromTs);
            cmd.Parameters.AddWithValue("@to",   toTs);

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(new TelemetrySnapshot(
                    DeviceSn:    deviceSn,
                    Ts:          0,
                    BatteryPct:  reader.IsDBNull(1) ? null : (float?)reader.GetDouble(1),
                    TotalInW:    reader.IsDBNull(2) ? null : (int?)reader.GetInt32(2),
                    TotalOutW:   reader.IsDBNull(3) ? null : (int?)reader.GetInt32(3),
                    PowerState:  null,
                    RemainMin:   null,
                    TempC:       reader.IsDBNull(4) ? null : (float?)reader.GetDouble(4),
                    Source:      "Aggregate"
                ));
            }
        }

        return results;
    }

    public async Task PruneAsync(TimeSpan retentionPeriod, CancellationToken ct = default)
    {
        if (_writeConn is null) return;
        long cutoff = DateTimeOffset.UtcNow.Subtract(retentionPeriod).ToUnixTimeSeconds();
        using var cmd = _writeConn.CreateCommand();
        cmd.CommandText = "DELETE FROM telemetry_snapshots WHERE ts < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _writeQueue.Writer.Complete();
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
        }
        if (_consumerTask is not null)
        {
            try { await _consumerTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (_writeConn is not null)
        {
            await _writeConn.DisposeAsync().ConfigureAwait(false);
        }
    }

    // -- Private helpers --

    private static async Task ConfigureConnectionAsync(SqliteConnection conn)
    {
        await conn.OpenAsync().ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task CreateSchemaAsync(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS telemetry_snapshots (
    id          INTEGER PRIMARY KEY,
    device_sn   TEXT    NOT NULL,
    ts          INTEGER NOT NULL,
    battery_pct REAL,
    total_in_w  INTEGER,
    total_out_w INTEGER,
    power_state TEXT,
    remain_min  INTEGER,
    temp_c      REAL,
    source      TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_telemetry_device_ts
    ON telemetry_snapshots (device_sn, ts DESC);

CREATE TABLE IF NOT EXISTS power_events (
    id          INTEGER PRIMARY KEY,
    device_sn   TEXT    NOT NULL,
    ts          INTEGER NOT NULL,
    event_type  TEXT    NOT NULL,
    detail      TEXT,
    source      TEXT
);
CREATE INDEX IF NOT EXISTS idx_events_device_ts
    ON power_events (device_sn, ts DESC);";
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task ConsumeWritesAsync(CancellationToken ct)
    {
        var batch = new List<TelemetrySnapshot>(50);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await _writeQueue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    break;
                while (_writeQueue.Reader.TryRead(out var snap))
                    batch.Add(snap);
                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, ct).ConfigureAwait(false);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SqliteHistoryStore] Write consumer error: {ex.Message}"); }
        }
    }

    private async Task FlushBatchAsync(List<TelemetrySnapshot> batch, CancellationToken ct)
    {
        if (_writeConn is null) return;
        using var tx = _writeConn.BeginTransaction();
        using var cmd = _writeConn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT OR IGNORE INTO telemetry_snapshots
    (device_sn, ts, battery_pct, total_in_w, total_out_w, power_state, remain_min, temp_c, source)
VALUES (@sn, @ts, @batt, @in, @out, @ps, @rm, @tc, @src)";
        var pSn   = cmd.Parameters.Add("@sn",   SqliteType.Text);
        var pTs   = cmd.Parameters.Add("@ts",   SqliteType.Integer);
        var pBatt = cmd.Parameters.Add("@batt", SqliteType.Real);
        var pIn   = cmd.Parameters.Add("@in",   SqliteType.Integer);
        var pOut  = cmd.Parameters.Add("@out",  SqliteType.Integer);
        var pPs   = cmd.Parameters.Add("@ps",   SqliteType.Text);
        var pRm   = cmd.Parameters.Add("@rm",   SqliteType.Integer);
        var pTc   = cmd.Parameters.Add("@tc",   SqliteType.Real);
        var pSrc  = cmd.Parameters.Add("@src",  SqliteType.Text);

        foreach (var snap in batch)
        {
            pSn.Value   = snap.DeviceSn;
            pTs.Value   = snap.Ts;
            pBatt.Value = snap.BatteryPct.HasValue ? (object)snap.BatteryPct.Value : DBNull.Value;
            pIn.Value   = snap.TotalInW.HasValue   ? (object)snap.TotalInW.Value   : DBNull.Value;
            pOut.Value  = snap.TotalOutW.HasValue  ? (object)snap.TotalOutW.Value  : DBNull.Value;
            pPs.Value   = snap.PowerState is not null ? (object)snap.PowerState    : DBNull.Value;
            pRm.Value   = snap.RemainMin.HasValue  ? (object)snap.RemainMin.Value  : DBNull.Value;
            pTc.Value   = snap.TempC.HasValue      ? (object)snap.TempC.Value      : DBNull.Value;
            pSrc.Value  = (object)snap.Source;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
