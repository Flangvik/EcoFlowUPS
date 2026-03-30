using System.Threading.Channels;
using Microsoft.Data.Sqlite;

namespace EcoFlowMonitor.History;

public sealed class SqliteEventStore : IEventStore
{
    private readonly string _writeConnStr;
    private readonly string _readConnStr;
    private SqliteConnection? _writeConn;
    private Task? _consumerTask;
    private CancellationTokenSource? _cts;

    private readonly Channel<PowerEvent> _eventQueue =
        Channel.CreateBounded<PowerEvent>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

    public SqliteEventStore(string dbPath)
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
        _consumerTask = Task.Run(() => ConsumeEventsAsync(_cts.Token), _cts.Token);
    }

    public void EnqueueEvent(PowerEvent evt)
    {
        if (!_eventQueue.Writer.TryWrite(evt))
            System.Diagnostics.Debug.WriteLine("[SqliteEventStore] Event write queue full; oldest event dropped.");
    }

    public async Task<IReadOnlyList<PowerEventItem>> QueryAsync(
        string deviceSn,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        using var conn = new SqliteConnection(_readConnStr);
        await ConfigureConnectionAsync(conn).ConfigureAwait(false);

        var results = new List<PowerEventItem>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT ts, event_type, detail, source
FROM power_events
WHERE device_sn = @sn AND ts >= @from AND ts <= @to
ORDER BY ts DESC
LIMIT 500";
        cmd.Parameters.AddWithValue("@sn",   deviceSn);
        cmd.Parameters.AddWithValue("@from", from.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("@to",   to.ToUnixTimeSeconds());

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new PowerEventItem(
                Ts:        reader.GetInt64(0),
                EventType: reader.GetString(1),
                Detail:    reader.IsDBNull(2) ? null : reader.GetString(2),
                Source:    reader.IsDBNull(3) ? "Unknown" : reader.GetString(3)
            ));
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        _eventQueue.Writer.Complete();
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

    private async Task ConsumeEventsAsync(CancellationToken ct)
    {
        var batch = new List<PowerEvent>(50);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await _eventQueue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    break;
                while (_eventQueue.Reader.TryRead(out var evt))
                    batch.Add(evt);
                if (batch.Count > 0)
                {
                    await FlushBatchAsync(batch, ct).ConfigureAwait(false);
                    batch.Clear();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SqliteEventStore] Event consumer error: {ex.Message}"); }
        }
    }

    private async Task FlushBatchAsync(List<PowerEvent> batch, CancellationToken ct)
    {
        if (_writeConn is null) return;
        using var tx = _writeConn.BeginTransaction();
        using var cmd = _writeConn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO power_events (device_sn, ts, event_type, detail, source)
VALUES (@sn, @ts, @type, @detail, @src)";
        var pSn     = cmd.Parameters.Add("@sn",     SqliteType.Text);
        var pTs     = cmd.Parameters.Add("@ts",     SqliteType.Integer);
        var pType   = cmd.Parameters.Add("@type",   SqliteType.Text);
        var pDetail = cmd.Parameters.Add("@detail", SqliteType.Text);
        var pSrc    = cmd.Parameters.Add("@src",    SqliteType.Text);

        foreach (var evt in batch)
        {
            pSn.Value     = evt.DeviceSn;
            pTs.Value     = evt.Ts;
            pType.Value   = evt.EventType;
            pDetail.Value = evt.Detail is not null ? (object)evt.Detail : DBNull.Value;
            pSrc.Value    = (object)evt.Source;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
}
