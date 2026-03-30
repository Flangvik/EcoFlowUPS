# Phase 3: History & Persistence - Research

**Researched:** 2026-03-30
**Domain:** SQLite persistence, LiveChartsCore Avalonia charting, MVVM history view
**Confidence:** HIGH

---

## Summary

Phase 3 adds the SQLite persistence layer and a history view with charting. The codebase already has the
chart library (`LiveChartsCore.SkiaSharpView.Avalonia 2.0.0-rc3.3`) installed, an existing `PowerHistoryChart`
control that renders an in-memory series, and a `PowerHistory` list on `DeviceViewModel` that stores the last 60
in-memory points (lost on restart). The work is three things: (1) persist snapshots to SQLite, (2) persist power
events to SQLite, (3) replace the in-memory chart with a `HistoryView` that queries SQLite with a time range
selector and an event log list.

The locked decision from earlier research is **raw `Microsoft.Data.Sqlite` + Dapper-style manual SQL** rather than
EF Core — no migration tooling for a 2-table schema. WAL mode is mandatory. The `Channel<TelemetrySnapshot>`
debounce write pattern is required to prevent per-frame writes. All SQLite work lives in `EcoFlowMonitor.Core`
(new `History/` namespace); the view lives in `EcoFlowMonitor.App`.

**Primary recommendation:** Add `Microsoft.Data.Sqlite 10.0.5` to Core. Implement `IHistoryStore` with a single
`SqliteHistoryStore`. Debounce writes via `Channel<T>`. Enable WAL + busy_timeout on every connection open. Use
the existing `LiveChartsCore` for charts — the stable 2.0.0 is available and compatible with Avalonia 11.2.3.

---

## Project Constraints (from CLAUDE.md)

- Tech stack locked: .NET 10 + Avalonia UI — no alternative UI frameworks
- Devices: EcoFlow Delta 3 / Delta 3 Max only
- No .NET test suite exists; add tests alongside new code
- File-scoped namespaces everywhere (`namespace EcoFlowMonitor.History;`)
- PascalCase types/methods/properties; `_camelCase` private fields
- Custom controls with properties MUST use `UserControl` + `StyledProperty` pattern (not `TemplatedControl`)
  — the project's `StatCard` demonstrates this correctly with `OnLoaded` + `OnPropertyChanged`
- `[ObservableProperty]` fields stacked without blank lines; expression-bodied members where possible
- Static resource keys PascalCase; named controls PascalCase (`x:Name="Chart"`)
- `Dispatcher.UIThread.Post()` for all ViewModel updates from background threads
- No `DataContext = this` in control constructors
- `x:DataType` on all views for compile-time binding validation

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| DATA-01 | Telemetry snapshots persist to SQLite (battery %, voltage, power in/out, temp per device, sampled every 10-30s) | `IHistoryStore.WriteSnapshotAsync` called from `MonitorOrchestrator.OnStateChanged`; 10s debounce via `Channel<TelemetrySnapshot>` |
| DATA-02 | Dashboard shows historical charts with hourly/daily/weekly time range selector | `HistoryViewModel` + `HistoryView.axaml` using `LiveChartsCore.SkiaSharpView.Avalonia 2.0.0` (upgrade from rc3.3); `IHistoryStore.QueryAsync(Resolution)` maps to SQL `GROUP BY strftime(...)` |
| DATA-03 | Event log records timestamped power events (power lost, restored, low battery, connection changes) with persistent storage | `IEventStore.AppendAsync(PowerEvent)` called from `MonitorOrchestrator.OnStateChanged`; `power_events` table; `EventLogViewModel` shows `ObservableCollection<PowerEventItem>` |
| DATA-04 | SQLite uses WAL mode and handles concurrent read/write without "database is locked" errors | `PRAGMA journal_mode=WAL` + `PRAGMA busy_timeout=3000` on every connection open; single writer `Channel` consumer; read connections use `Mode=ReadOnly` |
</phase_requirements>

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.Data.Sqlite` | 10.0.5 | SQLite ADO.NET driver for .NET 10 | Official Microsoft package, version-matched to runtime, no ORM overhead for 2-table schema |
| `LiveChartsCore.SkiaSharpView.Avalonia` | 2.0.0 | Avalonia charting (upgrade from rc3.3) | Already installed in project; 2.0.0 stable released, compatible with Avalonia 11.x |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Threading.Channels` | (inbox .NET 10) | `Channel<TelemetrySnapshot>` write debounce queue | Always — built-in, no NuGet needed |
| `Microsoft.Data.Sqlite.Core` | — | Do NOT use — use `Microsoft.Data.Sqlite` which bundles the native SQLite binary | Never add this separately |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `Microsoft.Data.Sqlite` | `EF Core 10.0.5 + Sqlite provider` | EF Core adds migration tooling needed for evolving schemas; overkill for a fixed 2-table schema; prior research locked decision to raw ADO.NET |
| `Microsoft.Data.Sqlite` | `sqlite-net-pcl` | No migrations, no LINQ composition — locked out in prior research |
| `LiveChartsCore 2.0.0` | `ScottPlot.Avalonia` | ScottPlot is viable but not installed; switching chart libraries is higher risk than upgrading the existing one |

**Installation (additions to `EcoFlowMonitor.Core.csproj`):**
```bash
dotnet add service/src/EcoFlowMonitor.Core/EcoFlowMonitor.Core.csproj package Microsoft.Data.Sqlite --version 10.0.5
```

**LiveChartsCore upgrade (in `EcoFlowMonitor.App.csproj` — already present):**
```xml
<!-- Change from rc3.3 to stable 2.0.0 -->
<PackageReference Include="LiveChartsCore.SkiaSharpView.Avalonia" Version="2.0.0" />
```

**Version verification:**
- `Microsoft.Data.Sqlite 10.0.5` — confirmed via NuGet API 2026-03-30
- `LiveChartsCore.SkiaSharpView.Avalonia 2.0.0` — confirmed via NuGet API 2026-03-30; depends on Avalonia >= 11.0.0 (project is on 11.2.3 — compatible)

---

## Architecture Patterns

### Recommended Project Structure

New files in `EcoFlowMonitor.Core`:
```
EcoFlowMonitor.Core/
  History/
    IHistoryStore.cs          # interface: telemetry snapshots
    IEventStore.cs            # interface: power events
    SqliteHistoryStore.cs     # Microsoft.Data.Sqlite implementation
    SqliteEventStore.cs       # Microsoft.Data.Sqlite implementation
    TelemetrySnapshot.cs      # record DTO
    PowerEvent.cs             # record DTO
    Resolution.cs             # enum: Raw | Hourly | Daily | Weekly
```

New files in `EcoFlowMonitor.App`:
```
EcoFlowMonitor.App/
  ViewModels/
    HistoryViewModel.cs       # time range selector, chart series, event log
  Views/
    HistoryView.axaml         # CartesianChart + time range buttons + event log
    HistoryView.axaml.cs
```

Navigation registration:
```
App.axaml.cs                  # register HistoryViewModel as transient; add DI for IHistoryStore/IEventStore
MainWindow.axaml              # add DataTemplate for HistoryViewModel -> HistoryView
DashboardView.axaml           # add "History" button that calls DashboardViewModel.OpenHistoryCommand
```

### Pattern 1: IHistoryStore Interface

```csharp
// Source: .planning/research/ARCHITECTURE.md (verified against project patterns)
// File: service/src/EcoFlowMonitor.Core/History/IHistoryStore.cs
namespace EcoFlowMonitor.History;

public interface IHistoryStore
{
    Task WriteSnapshotAsync(TelemetrySnapshot snapshot, CancellationToken ct = default);
    Task<IReadOnlyList<TelemetrySnapshot>> QueryAsync(
        string deviceSn,
        DateTimeOffset from,
        DateTimeOffset to,
        Resolution resolution,
        CancellationToken ct = default);
    Task PruneAsync(TimeSpan retentionPeriod, CancellationToken ct = default);
}

public enum Resolution { Raw, Hourly, Daily, Weekly }

public record TelemetrySnapshot(
    string DeviceSn,
    long Ts,          // Unix epoch seconds
    float? BatteryPct,
    int? TotalInW,
    int? TotalOutW,
    string? PowerState,
    int? RemainMin,
    float? TempC,
    string Source);   // "Cloud" or "BLE"
```

### Pattern 2: IEventStore Interface

```csharp
// File: service/src/EcoFlowMonitor.Core/History/IEventStore.cs
namespace EcoFlowMonitor.History;

public interface IEventStore
{
    Task AppendAsync(PowerEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<PowerEvent>> QueryAsync(
        string deviceSn,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);
}

public record PowerEvent(
    string DeviceSn,
    long Ts,          // Unix epoch seconds
    string EventType, // "PowerLost" | "PowerRestored" | "BatteryLow" | "ConnectionChanged"
    string? Detail,   // e.g. "Battery 18%" or "Retrying (attempt 3)"
    string Source);
```

### Pattern 3: SQLite Schema

```sql
-- telemetry_snapshots: raw samples, pruned after 90 days
CREATE TABLE IF NOT EXISTS telemetry_snapshots (
    id          INTEGER PRIMARY KEY,
    device_sn   TEXT    NOT NULL,
    ts          INTEGER NOT NULL,   -- Unix epoch seconds
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

-- power_events: all power events, never pruned
CREATE TABLE IF NOT EXISTS power_events (
    id          INTEGER PRIMARY KEY,
    device_sn   TEXT    NOT NULL,
    ts          INTEGER NOT NULL,   -- Unix epoch seconds
    event_type  TEXT    NOT NULL,   -- "PowerLost", "PowerRestored", etc.
    detail      TEXT,
    source      TEXT
);
CREATE INDEX IF NOT EXISTS idx_events_device_ts
    ON power_events (device_sn, ts DESC);
```

### Pattern 4: Write Debounce via Channel<T>

```csharp
// In SqliteHistoryStore constructor — starts a background consumer task
// Source: .planning/research/ARCHITECTURE.md Channel<T> debounce pattern
private readonly Channel<TelemetrySnapshot> _writeQueue =
    Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(500)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleWriter = false,
        SingleReader = true
    });

// Consumer: batches writes every 10 seconds or when 50 items accumulate
private async Task ConsumeWritesAsync(CancellationToken ct)
{
    var batch = new List<TelemetrySnapshot>(50);
    while (!ct.IsCancellationRequested)
    {
        // Drain available items, wait up to 10s for first
        if (await _writeQueue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_writeQueue.Reader.TryRead(out var snap))
                batch.Add(snap);
        }
        if (batch.Count == 0) continue;

        await FlushBatchAsync(batch, ct).ConfigureAwait(false);
        batch.Clear();
    }
}
```

### Pattern 5: WAL Mode — Mandatory Connection Setup

```csharp
// Source: .planning/research/PITFALLS.md Pitfall 8 + Pitfall 17
// Must execute on EVERY connection open, not just the first time
private static async Task ConfigureConnectionAsync(SqliteConnection conn)
{
    await conn.OpenAsync().ConfigureAwait(false);
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;";
    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
}

// Writer connection string (owned by SqliteHistoryStore singleton)
// "Data Source=history.db;Mode=ReadWriteCreate"

// Reader connection string (new connection per query)
// "Data Source=history.db;Mode=ReadOnly"
```

### Pattern 6: Hourly/Daily/Weekly SQL Downsampling

```sql
-- Hourly aggregate (last 24h or 7 days)
SELECT
    strftime('%Y-%m-%d %H:00', ts, 'unixepoch') AS bucket,
    AVG(battery_pct) AS avg_battery,
    MAX(total_in_w)  AS peak_in_w,
    MAX(total_out_w) AS peak_out_w,
    AVG(temp_c)      AS avg_temp_c
FROM telemetry_snapshots
WHERE device_sn = @sn AND ts >= @from AND ts <= @to
GROUP BY bucket ORDER BY bucket;

-- Daily aggregate (last 30 days)
SELECT
    strftime('%Y-%m-%d', ts, 'unixepoch') AS bucket,
    AVG(battery_pct) AS avg_battery,
    MAX(total_in_w)  AS peak_in_w,
    MAX(total_out_w) AS peak_out_w,
    AVG(temp_c)      AS avg_temp_c
FROM telemetry_snapshots
WHERE device_sn = @sn AND ts >= @from AND ts <= @to
GROUP BY bucket ORDER BY bucket;
```

`Resolution` maps to: `Raw` = no GROUP BY (last 500 rows), `Hourly` = `strftime('%Y-%m-%d %H:00', ...)`, `Daily` = `strftime('%Y-%m-%d', ...)`, `Weekly` = `strftime('%Y-W%W', ...)`.

### Pattern 7: HistoryViewModel (MVVM)

```csharp
// File: service/src/EcoFlowMonitor.App/ViewModels/HistoryViewModel.cs
public partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryStore _history;
    private readonly IEventStore _events;

    [ObservableProperty] private Resolution _selectedResolution = Resolution.Hourly;
    [ObservableProperty] private bool _isLoading;
    public ObservableCollection<ISeries> BatterySeries { get; } = new();
    public ObservableCollection<ISeries> PowerSeries   { get; } = new();
    public ObservableCollection<Axis>    XAxes         { get; } = new();
    public ObservableCollection<PowerEventItem> EventLog { get; } = new();

    // Called when user changes time range; queries IHistoryStore on a background thread,
    // then Dispatcher.UIThread.Post() to update series
    [RelayCommand]
    private async Task LoadHistoryAsync() { ... }
}
```

### Pattern 8: LiveChartsCore Series Update

The existing `PowerHistoryChart` uses `Chart.Series = new ISeries[] { ... }` with static arrays.
For `HistoryViewModel` the series must be reactive. Use `ObservableCollection<ISeries>` or update
`LineSeries<double>.Values` directly rather than replacing the entire `Series` property to avoid
the animation flicker that occurs when the whole series array is swapped.

```csharp
// Source: LiveChartsCore 2.0.0 official docs pattern
// Update values in place rather than replacing the series collection
var batterySeries = new LineSeries<double>
{
    Values = new ObservableCollection<double>(),
    Name = "Battery %",
    Stroke = new SolidColorPaint(SKColor.Parse("#00D4AA")) { StrokeThickness = 2 },
    Fill = new SolidColorPaint(SKColor.Parse("#1A00D4AA")),
    GeometrySize = 0,
    LineSmoothness = 0.5
};

// On data load, clear and repopulate:
((ObservableCollection<double>)batterySeries.Values!).Clear();
foreach (var pt in queryResult)
    ((ObservableCollection<double>)batterySeries.Values!).Add(pt.AvgBattery ?? 0);
```

### Pattern 9: Time Range Selector UI

Use a `StackPanel` of `RadioButton`s styled as a segmented control — the simplest approach that
requires no new dependency. Avalonia's `RadioButton` supports visual grouping via `GroupName`.
The `HistoryViewModel.SelectedResolution` property drives both the SQL query and the X-axis
label format.

```axaml
<!-- In HistoryView.axaml — time range selector -->
<StackPanel Orientation="Horizontal" Spacing="4">
    <RadioButton Content="1H" GroupName="TimeRange"
                 IsChecked="{Binding SelectedResolution,
                     Converter={x:Static converters:ResolutionConverter.IsHourly}}" />
    <RadioButton Content="24H" GroupName="TimeRange" ... />
    <RadioButton Content="7D"  GroupName="TimeRange" ... />
    <RadioButton Content="30D" GroupName="TimeRange" ... />
</StackPanel>
```

Alternatively, use `SegmentedControl` from `Avalonia.Controls` — but this is only available
in Avalonia 11.1+ and requires more AXAML. Plain `RadioButton`s styled with the project's
existing `Controls.axaml` button styles are simpler and consistent with the existing design system.

### Pattern 10: Event Log UI

Use `ItemsControl` bound to `ObservableCollection<PowerEventItem>`. Group by day using a
`GroupingItemsControl` or simple day-separator items injected into the collection.
The simplest viable approach: add a `bool IsDaySeparator` and `string DayLabel` to
`PowerEventItem` and use a `DataTemplate`-based selector. No external library needed.

```csharp
public record PowerEventItem(long Ts, string EventType, string? Detail, string Source)
{
    public string TimeLabel => DateTimeOffset.FromUnixTimeSeconds(Ts).LocalDateTime.ToString("HH:mm:ss");
    public string DayLabel  => DateTimeOffset.FromUnixTimeSeconds(Ts).LocalDateTime.ToString("ddd, MMM d");
    public bool IsDaySeparator => false; // overridden for injected separator rows
}
```

### Pattern 11: MonitorOrchestrator Integration

`OnStateChanged` is the write hook for both telemetry and events. The write to history is
non-blocking (enqueue to Channel):

```csharp
// In MonitorOrchestrator.OnStateChanged() — after existing trigger evaluation:
var snapshot = new TelemetrySnapshot(
    DeviceSn: entry.Device.SerialNumber!,
    Ts: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    BatteryPct: e.State.Bms?.BatteryPct,
    TotalInW: e.State.Display?.TotalInW,
    TotalOutW: e.State.Display?.TotalOutW,
    PowerState: e.State.Power.Status.ToString(),
    RemainMin: e.State.Bms?.RemainMin,
    TempC: e.State.Bms?.TempC,
    Source: entry.Monitor is BleMonitor ? "BLE" : "Cloud");
_historyStore.EnqueueSnapshot(snapshot);   // non-blocking, posts to Channel

// Event log: only on state transitions (not every tick)
if (e.PreviousPower != e.State.Power.Status)
{
    var evt = new PowerEvent(
        DeviceSn: entry.Device.SerialNumber!,
        Ts: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        EventType: DeriveEventType(e.PreviousPower, e.State.Power.Status),
        Detail: $"Battery {e.State.Bms?.BatteryPct:F0}%",
        Source: source);
    _eventStore.EnqueueEvent(evt);   // non-blocking
}
```

### Anti-Patterns to Avoid

- **Writing to SQLite on every StateChanged tick:** BLE streams at ~1Hz — 86,400 writes/day/device without debounce. Use the `Channel<T>` consumer.
- **Sharing a single SqliteConnection across threads:** Open a fresh read connection per query; keep one dedicated write connection on the consumer thread.
- **Not setting `busy_timeout` on new connections:** It resets to 0 per connection; always set it explicitly.
- **Setting `DataContext = this` in chart control constructor:** Project convention requires `UserControl` + `StyledProperty` (see `StatCard.axaml.cs`). Use `OnLoaded` + `OnPropertyChanged`.
- **Replacing entire `Chart.Series` array on every data refresh:** This triggers full animation resets. Update `Values` in-place on existing series objects.
- **Awaiting event store writes from the BLE notification thread:** Always enqueue to `Channel` and return; never await on the notification callback thread.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Time-bucketing queries | Custom C# averaging loop over raw rows | SQLite `strftime` + `GROUP BY` | Native SQL aggregation; no extra memory allocation for 86K rows |
| Concurrent read/write isolation | Custom mutex around SQLite calls | WAL mode + read-only connections | WAL is SQLite's built-in concurrent-reader solution; mutex re-introduces serialization |
| Write debounce | `System.Timers.Timer` + lock list | `Channel<T>` bounded channel | Back-pressure built-in; `DropOldest` mode prevents unbounded memory growth on disconnect |
| Chart library | Custom SkiaSharp drawing in `PowerHistoryChart` | Upgrade existing `LiveChartsCore 2.0.0` | Existing control already works; interactive tooltips, animations, axis labels for free |
| Event log grouping | Custom collection with day headers | Flat `ObservableCollection` with separator records | Simpler state management; no need for CollectionViewSource (WPF pattern, not idiomatic Avalonia) |

**Key insight:** The chart and write-debounce problems are already solved by libraries present in the project (LiveChartsCore) or inbox .NET 10 primitives (Channel<T>). The only new dependency is `Microsoft.Data.Sqlite`.

---

## Common Pitfalls

### Pitfall 1: "database is locked" — WAL Not Set Per-Connection

**What goes wrong:** `PRAGMA journal_mode=WAL` set on the initial write connection only. New read connections opened by `HistoryViewModel.QueryAsync` use default journal mode (DELETE), which cannot read while a WAL write is in progress. Intermittent `SqliteException: database is locked (5)` under load.

**Why it happens:** `PRAGMA` is a per-connection setting in SQLite. There is no persistent global mode stored in the database file that new connections inherit automatically (beyond the `-wal` file presence).

**How to avoid:** Execute `PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000;` in every `SqliteConnection.OpenAsync()` call — write connection AND every read connection. Wrap in a helper: `ConfigureConnectionAsync(conn)`.

**Warning signs:** `SqliteException (5)` in logs during UI chart loads within 30 seconds of app start.

---

### Pitfall 2: Per-Frame Write Volume

**What goes wrong:** Calling `IHistoryStore.WriteSnapshotAsync` directly in `MonitorOrchestrator.OnStateChanged` without debounce. BLE at 1Hz produces 86,400 rows/day/device. Each row-insert holds a brief write lock; chart reads that arrive during a write see `SQLITE_BUSY`.

**Why it happens:** `StateChanged` fires on every decoded telemetry packet, not on a timer.

**How to avoid:** Enqueue to `Channel<TelemetrySnapshot>` in `OnStateChanged` (non-blocking, O(1)). The channel consumer batches and flushes every 10 seconds. Use `BoundedChannelOptions.FullMode = DropOldest` to prevent unbounded growth if the consumer falls behind.

**Warning signs:** CPU spike in `MonitorOrchestrator.OnStateChanged` on every BLE tick. Database file growing faster than ~50 MB/device/year.

---

### Pitfall 3: ObservableCollection Mutation From Background Thread

**What goes wrong:** `HistoryViewModel.EventLog.Add()` called from the `MonitorOrchestrator` event (background thread) without `Dispatcher.UIThread.Post()`. Avalonia raises `InvalidOperationException: Collection was modified` or produces rendering artifacts.

**Why it happens:** `ObservableCollection<T>` fires `CollectionChanged` on the calling thread. Avalonia's `ItemsControl` must receive this notification on the UI thread.

**How to avoid:** Subscribe to `MonitorOrchestrator.DeviceUpdated` in `HistoryViewModel`, then marshal via `Dispatcher.UIThread.Post(() => EventLog.Add(item))`. Consistent with existing `DashboardViewModel.OnDeviceUpdated` pattern.

**Warning signs:** Random `InvalidOperationException` in collection change handlers in Avalonia's internal renderer.

---

### Pitfall 4: LiveChartsCore rc3.3 to 2.0.0 Upgrade

**What goes wrong:** The upgrade from `2.0.0-rc3.3` to `2.0.0` stable introduced source generators in rc6 (creating "Xaml" prefixed type variants). The existing `PowerHistoryChart.axaml` uses `<lvc:CartesianChart>` directly — this name is stable across all 2.x versions.

**Why it happens:** Source generators add aliases, but the original type names remain. `CartesianChart`, `LineSeries<T>`, `Axis`, `SolidColorPaint` API surface is unchanged from rc3 to 2.0.0 stable.

**How to avoid:** Upgrade the package reference in `EcoFlowMonitor.App.csproj` and rebuild. No AXAML changes required. The existing `PowerHistoryChart` will continue to work. Verify the build compiles clean after the version bump before adding new chart code.

**Warning signs:** Build error referencing source-generated types (only relevant if you use the new `Xaml`-prefixed controls introduced in rc6).

---

### Pitfall 5: Chart Series Replaced Instead of Updated

**What goes wrong:** Setting `Chart.Series = new ISeries[] { ... }` on every time-range change causes the chart to re-run the entry animation from zero. Jitter is visible on every range toggle.

**Why it happens:** Assigning a new array to `Series` triggers a full series teardown and recreation in LiveChartsCore.

**How to avoid:** Create series objects once in `HistoryViewModel`; on range change, update `Values` in place (clear + repopulate the `ObservableCollection<double>` backing the series). The chart detects the `CollectionChanged` and animates only the diff.

**Warning signs:** Chart flashes white/blank for 200ms on every time range button press.

---

### Pitfall 6: Storing DateTime as TEXT in SQLite

**What goes wrong:** Storing timestamps as `"2026-03-30T12:00:00Z"` TEXT prevents efficient range queries. SQLite must do full-table string comparison rather than integer index range scan.

**Why it happens:** `System.Text.Json` / serialization defaults to ISO 8601 strings; developers copy that pattern into SQL.

**How to avoid:** Store as `INTEGER` (Unix epoch seconds via `DateTimeOffset.ToUnixTimeSeconds()`). Use `strftime` for grouping. The index `(device_sn, ts DESC)` makes range queries fast.

**Warning signs:** Slow chart loads (>500ms) for 7-day hourly queries even with an index.

---

### Pitfall 7: History View Navigation Integration

**What goes wrong:** `HistoryViewModel` added to DI but not registered in `MainWindow.axaml` `DataTemplates`, or not added to `App.axaml.cs` service registrations. Navigation to `HistoryView` silently shows a blank `ContentControl`.

**Why it happens:** The navigation pattern (`NavigationService.NavigateTo(vm)` + `ContentControl` + `DataTemplate` resolution) requires all three to be present: DI registration, `DataTemplate` in `MainWindow.axaml`, and the View class.

**How to avoid:** Check all three locations: `App.axaml.cs` (`services.AddTransient<HistoryViewModel>()`), `MainWindow.axaml` (`<DataTemplate DataType="vm:HistoryViewModel">`), and the existence of `HistoryView.axaml`.

---

## Code Examples

### Database Initialization (WAL + Schema)

```csharp
// Source: .planning/research/ARCHITECTURE.md + .planning/research/PITFALLS.md
// SqliteHistoryStore.cs — called once on startup
private async Task InitializeAsync(string dbPath)
{
    var dir = Path.GetDirectoryName(dbPath)!;
    Directory.CreateDirectory(dir);

    using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate");
    await conn.OpenAsync().ConfigureAwait(false);
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        PRAGMA journal_mode=WAL;
        PRAGMA synchronous=NORMAL;
        PRAGMA busy_timeout=3000;

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
            ON power_events (device_sn, ts DESC);
        """;
    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
}
```

### Batch Write (Telemetry)

```csharp
// SqliteHistoryStore.FlushBatchAsync
private async Task FlushBatchAsync(IReadOnlyList<TelemetrySnapshot> batch, CancellationToken ct)
{
    using var conn = new SqliteConnection(_writeConnStr);
    await conn.OpenAsync(ct).ConfigureAwait(false);
    // WAL must be re-applied per connection
    using var pragmaCmd = conn.CreateCommand();
    pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000;";
    await pragmaCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

    using var tx = conn.BeginTransaction();
    using var insertCmd = conn.CreateCommand();
    insertCmd.CommandText = """
        INSERT OR IGNORE INTO telemetry_snapshots
            (device_sn, ts, battery_pct, total_in_w, total_out_w, power_state, remain_min, temp_c, source)
        VALUES
            (@sn, @ts, @batt, @in, @out, @ps, @rem, @temp, @src)
        """;
    // Bind parameters once, update values in loop
    var pSn   = insertCmd.Parameters.Add("@sn",   SqliteType.Text);
    var pTs   = insertCmd.Parameters.Add("@ts",   SqliteType.Integer);
    var pBatt = insertCmd.Parameters.Add("@batt", SqliteType.Real);
    var pIn   = insertCmd.Parameters.Add("@in",   SqliteType.Integer);
    var pOut  = insertCmd.Parameters.Add("@out",  SqliteType.Integer);
    var pPs   = insertCmd.Parameters.Add("@ps",   SqliteType.Text);
    var pRem  = insertCmd.Parameters.Add("@rem",  SqliteType.Integer);
    var pTemp = insertCmd.Parameters.Add("@temp", SqliteType.Real);
    var pSrc  = insertCmd.Parameters.Add("@src",  SqliteType.Text);

    foreach (var s in batch)
    {
        pSn.Value   = s.DeviceSn;
        pTs.Value   = s.Ts;
        pBatt.Value = s.BatteryPct.HasValue ? s.BatteryPct.Value : DBNull.Value;
        pIn.Value   = s.TotalInW.HasValue ? s.TotalInW.Value : DBNull.Value;
        pOut.Value  = s.TotalOutW.HasValue ? s.TotalOutW.Value : DBNull.Value;
        pPs.Value   = s.PowerState ?? (object)DBNull.Value;
        pRem.Value  = s.RemainMin.HasValue ? s.RemainMin.Value : DBNull.Value;
        pTemp.Value = s.TempC.HasValue ? s.TempC.Value : DBNull.Value;
        pSrc.Value  = s.Source ?? (object)DBNull.Value;
        await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
    tx.Commit();
}
```

### Hourly Query with Downsampling

```csharp
// SqliteHistoryStore.QueryAsync — Resolution.Hourly case
private const string HourlyQuery = """
    SELECT
        strftime('%Y-%m-%d %H:00', ts, 'unixepoch') AS bucket,
        AVG(battery_pct) AS avg_battery,
        MAX(total_in_w)  AS peak_in_w,
        MAX(total_out_w) AS peak_out_w,
        AVG(temp_c)      AS avg_temp_c
    FROM telemetry_snapshots
    WHERE device_sn = @sn AND ts >= @from AND ts <= @to
    GROUP BY bucket
    ORDER BY bucket
    """;

using var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
await conn.OpenAsync(ct).ConfigureAwait(false);
// Apply WAL + busy_timeout to read connections too
using var pragmaCmd = conn.CreateCommand();
pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000;";
await pragmaCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
```

### HistoryViewModel LoadHistory

```csharp
// HistoryViewModel.LoadHistoryAsync
[RelayCommand]
private async Task LoadHistoryAsync()
{
    IsLoading = true;
    try
    {
        var (from, to) = GetTimeRange(SelectedResolution);
        var snapshots = await _history.QueryAsync(
            _deviceSn, from, to, SelectedResolution);

        var events = await _events.QueryAsync(
            _deviceSn, from, to);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateBatterySeries(snapshots);
            UpdatePowerSeries(snapshots);
            UpdateXAxis(snapshots, SelectedResolution);
            UpdateEventLog(events);
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "History load failed");
    }
    finally
    {
        IsLoading = false;
    }
}

private static (DateTimeOffset from, DateTimeOffset to) GetTimeRange(Resolution r) =>
    r switch
    {
        Resolution.Raw     => (DateTimeOffset.UtcNow.AddHours(-1),  DateTimeOffset.UtcNow),
        Resolution.Hourly  => (DateTimeOffset.UtcNow.AddDays(-1),   DateTimeOffset.UtcNow),
        Resolution.Daily   => (DateTimeOffset.UtcNow.AddDays(-30),  DateTimeOffset.UtcNow),
        Resolution.Weekly  => (DateTimeOffset.UtcNow.AddDays(-90),  DateTimeOffset.UtcNow),
        _                  => (DateTimeOffset.UtcNow.AddDays(-1),   DateTimeOffset.UtcNow)
    };
```

### DI Registration (App.axaml.cs addition)

```csharp
// In App.axaml.cs OnFrameworkInitializationCompleted, after existing registrations:
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "EcoFlowMonitor", "history.db");

var historyStore = new SqliteHistoryStore(dbPath, loggerFactory);
await historyStore.InitializeAsync();   // or sync wrapper

services.AddSingleton<IHistoryStore>(historyStore);
services.AddSingleton<IEventStore>(historyStore);  // SqliteHistoryStore implements both
services.AddTransient<HistoryViewModel>();
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| In-memory `List<PowerHistoryPoint>` (max 60 entries) in `DeviceViewModel` | SQLite-backed `IHistoryStore` with 90-day retention | Phase 3 | History survives app restarts; hourly/daily/weekly charts possible |
| `LiveChartsCore 2.0.0-rc3.3` (pre-release) | `LiveChartsCore 2.0.0` stable | 2025 | Stable API, no pre-release risk; source generators available (optional) |
| No event log | `power_events` SQLite table + `EventLog` UI | Phase 3 | Users can see timestamped history of outages, restores, connection changes |

**Deprecated/outdated:**
- `DeviceViewModel.PowerHistory` list — still present in code; will be replaced by `IHistoryStore` queries in Phase 3. The list is used by `PowerHistoryChart` on the dashboard. Decision: keep the in-memory list for the dashboard's live mini-chart (last 60 points, no persistence needed); `HistoryViewModel` uses SQLite for the full history view.
- `PowerHistoryChart.OnDataContextChanged` pulling from `DeviceViewModel.PowerHistory` — this pattern is fine for the live dashboard chart; `HistoryViewModel` is a separate view with its own chart data.

---

## Open Questions

1. **Database file location on Linux**
   - What we know: `Environment.GetFolderPath(SpecialFolder.ApplicationData)` returns `~/.config` on Linux (confirmed .NET behavior); this is used for `config.json` already
   - What's unclear: Whether `.config/EcoFlowMonitor/history.db` is the right Linux convention vs. `~/.local/share`
   - Recommendation: Use the same directory as `config.json` for consistency; Phase 3 should not introduce a separate XDG compliance concern

2. **LiveChartsCore 2.0.0 upgrade risk**
   - What we know: API surface (CartesianChart, LineSeries, Axis, SolidColorPaint) is stable from rc3 to 2.0.0; source generators are additive (opt-in)
   - What's unclear: Whether rc3.3 → 2.0.0 requires any changes to `LiveCharts.Configure(...)` call (if one exists in App startup)
   - Recommendation: Grep for `LiveCharts.Configure` or `LiveChartsSdk.Init` calls before upgrading; if absent (likely), the upgrade is a one-line version bump

3. **Pruning strategy — who schedules it**
   - What we know: `SqliteHistoryStore.PruneAsync` deletes rows older than 90 days
   - What's unclear: Where to call it — `MonitorOrchestrator.StartAsync` once at startup is the simplest; alternatively a `DispatcherTimer` daily call
   - Recommendation: Call once at `MonitorOrchestrator.StartAsync` to keep it simple; 90-day prune on startup costs <50ms for a bounded dataset

4. **HistoryViewModel device selection**
   - What we know: `DashboardViewModel.SelectedDevice` identifies the current device; `HistoryViewModel` needs a `DeviceSn` to query
   - What's unclear: Whether `HistoryViewModel` should receive `DeviceSn` at construction (transient, passed by `DashboardViewModel.OpenHistoryCommand`) or observe `SelectedDevice` live
   - Recommendation: Pass `DeviceSn` as constructor parameter when navigating; simpler state management than observing selection changes from another ViewModel

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10.0 SDK | All compilation | Yes | 10.0.105 | — |
| `Microsoft.Data.Sqlite 10.0.5` | SQLite persistence | Not yet installed in Core | To be added | — |
| `LiveChartsCore.SkiaSharpView.Avalonia 2.0.0` | Charts | Installed as rc3.3 (upgrade needed) | rc3.3 currently | Stay on rc3.3 if upgrade blocked |
| SQLite native binary | `Microsoft.Data.Sqlite` bundles it | Bundled by package | — | — |

**Missing dependencies with no fallback:**
- `Microsoft.Data.Sqlite 10.0.5` must be added to `EcoFlowMonitor.Core.csproj` — no persistence otherwise

**Missing dependencies with fallback:**
- LiveChartsCore 2.0.0 upgrade: if upgrade breaks the build, remain on rc3.3; the existing `PowerHistoryChart` API is identical on both versions

---

## Sources

### Primary (HIGH confidence)

- NuGet API — `microsoft.data.sqlite` latest stable = 10.0.5 (verified 2026-03-30)
- NuGet API — `livechartscore.skiasharpview.avalonia` 2.0.0 stable deps: Avalonia >= 11.0.0 (verified 2026-03-30)
- `.planning/research/ARCHITECTURE.md` — IHistoryStore interface, schema, WAL pattern, Channel<T> debounce (HIGH — prior verified research)
- `.planning/research/PITFALLS.md` — Pitfall 8 (database locked), Pitfall 17 (busy_timeout per-connection) (HIGH)
- `.planning/research/STACK.md` — Decision: raw Microsoft.Data.Sqlite over EF Core (HIGH — locked decision)
- `service/src/EcoFlowMonitor.App/EcoFlowMonitor.App.csproj` — confirms `LiveChartsCore.SkiaSharpView.Avalonia 2.0.0-rc3.3` installed (HIGH — read from source)
- `service/src/EcoFlowMonitor.App/Controls/PowerHistoryChart.axaml.cs` — confirmed working API surface: `CartesianChart`, `LineSeries<double>`, `Axis`, `SolidColorPaint` (HIGH — read from source)
- `service/src/EcoFlowMonitor.App/Services/MonitorOrchestrator.cs` — confirmed `OnStateChanged` is the correct write hook (HIGH — read from source)
- `service/src/EcoFlowMonitor.App/ViewModels/DeviceViewModel.cs` — confirmed `PowerHistory` list structure; established migration path (HIGH — read from source)
- SQLite official docs — WAL mode, strftime, busy_timeout behavior (HIGH)

### Secondary (MEDIUM confidence)

- GitHub LiveCharts2 releases — rc6 added source generators; CartesianChart name stable across 2.x (MEDIUM — scraped release notes, API confirmed by existing project usage)
- LiveChartsCore 2.0.0 nuspec — `Avalonia >= 11.0.0` dep verified (MEDIUM — NuGet API response)

### Tertiary (LOW confidence)

- None

---

## Metadata

**Confidence breakdown:**
- Standard stack (Microsoft.Data.Sqlite + LiveChartsCore upgrade): HIGH — versions verified from NuGet API
- Architecture (IHistoryStore, Channel<T>, WAL pattern): HIGH — from prior verified research docs
- Pitfalls (WAL per-connection, per-frame writes, ObservableCollection thread): HIGH — directly from project's own PITFALLS.md + prior codebase analysis
- LiveChartsCore upgrade risk: MEDIUM — API surface verified by existing code; rc3→2.0.0 behavior change from release notes only

**Research date:** 2026-03-30
**Valid until:** 2026-06-30 (stable versions; LiveChartsCore may release patch)
