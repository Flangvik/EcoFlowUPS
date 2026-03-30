# Architecture Patterns

**Project:** EcoFlow UPS Monitor
**Dimension:** Cross-platform BLE + cloud connections with SQLite history and rules automation
**Researched:** 2026-03-30
**Overall confidence:** HIGH (patterns verified against official docs and active libraries)

---

## Context: What This Document Addresses

The existing codebase has a solid MVVM foundation, but the next milestone introduces four new structural concerns that each require explicit architectural decisions:

1. **Platform-specific BLE adapters** — macOS works; Windows (WinRT) and Linux (BlueZ) are stubs
2. **Connection state machines** — both BLE and MQTT currently use infinite retry loops with no state visibility
3. **SQLite persistence layer** — not yet present; needed for historical charts
4. **Rules engine** — partially present (`TriggerEvaluator` + `ActionRunner`); needs webhook, push notification, and email action types

All four must integrate cleanly with the existing `IDeviceMonitor` / `MonitorOrchestrator` / `StateChanged` event chain.

---

## Recommended Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  EcoFlowMonitor.App                                             │
│  ┌──────────────────┐  ┌──────────────────────────────────────┐│
│  │   ViewModels      │  │  MonitorOrchestrator                 ││
│  │  DashboardVM      │  │  ┌──────────────┐ ┌──────────────┐  ││
│  │  HistoryVM (new)  │  │  │ MonitorEntry  │ │ RulesEngine  ││  ││
│  │  SettingsVM       │  │  │ (per device)  │ │ (enhanced)   ││  ││
│  └──────────┬────────┘  │  └──────┬───────┘ └──────┬───────┘  ││
│             │           └─────────┼─────────────────┼──────────┘│
│             │                     │                 │            │
│  Dispatcher.UIThread.Post()       │                 │            │
└─────────────┼─────────────────────┼─────────────────┼────────────┘
              │                     │                 │
              │            ┌────────▼──────┐   ┌──────▼───────────┐
              │            │ IDeviceMonitor│   │ IHistoryStore    │
              │            │ ┌──────────┐  │   │ (new, Core)      │
              │            │ │Connection│  │   └──────┬───────────┘
              │            │ │StateMachine  │          │
              │            │ └──────────┘  │   ┌──────▼───────────┐
              │            │               │   │ SqliteHistoryStore│
              │            │  BleMonitor   │   │ (new, Core)      │
              │            │  MqttMonitor  │   └──────────────────┘
              │            └───────┬───────┘
              │                    │
              │         ┌──────────▼──────────────────────┐
              │         │  IBleAdapter (Core interface)   │
              │         │  ┌───────────┐ ┌────────────┐  │
              │         │  │CoreBluetooth│ │WinRtBle   │  │
              │         │  │BleAdapter  │ │Adapter     │  │
              │         │  │(macOS,App) │ │(Win,Plat.) │  │
              │         │  └───────────┘ └────────────┘  │
              │         │  ┌────────────┐                 │
              │         │  │BlueZBle    │                 │
              │         │  │Adapter     │                 │
              │         │  │(Linux,Plat.)                 │
              │         │  └────────────┘                 │
              │         └────────────────────────────────┘
              │
      ┌───────▼────────────────────────────────────────────┐
      │  Avalonia UI (AXAML bindings → ObservableProperty) │
      └────────────────────────────────────────────────────┘
```

---

## Component Boundaries

| Component | Responsibility | Lives In | Communicates With |
|-----------|----------------|----------|-------------------|
| `ConnectionStateMachine` | Tracks scan/connect/auth/stream/retry states, fires `StateChanged` on transitions | `Core/Client/` | `BleMonitor`, `MqttMonitor` |
| `IBleAdapter` / `IBleGattConnection` | Platform-agnostic BLE scan + GATT write/notify | `Core/Platform/` (interface) | `BleMonitor` |
| `WinRtBleAdapter` | WinRT `BluetoothLEAdvertisementWatcher` + `BluetoothLEDevice` | `Platform.Windows` | Implements `IBleAdapter` |
| `BlueZBleAdapter` | BlueZ D-Bus via `Linux.Bluetooth` (`BlueZManager`, `Device`) | `Platform.Linux` | Implements `IBleAdapter` |
| `CoreBluetoothBleAdapter` | macOS `CBCentralManager` (existing) | `App/Services/` | Implements `IBleAdapter` |
| `IHistoryStore` | Persist and query `TelemetrySnapshot` records | `Core/History/` (interface) | `MonitorOrchestrator` (write), `HistoryViewModel` (read) |
| `SqliteHistoryStore` | `Microsoft.Data.Sqlite` implementation of `IHistoryStore` | `Core/History/` | Implements `IHistoryStore` |
| `RulesEngine` | Evaluate triggers, dispatch actions (webhook, email, push, script, power) | `Core/Rules/` | `MonitorOrchestrator`, platform services, `HttpClient` |
| `MonitorOrchestrator` | Create/destroy monitors per device, pipe `StateChanged` into history write + rules evaluation + UI event | `App/Services/` | All of the above |

---

## Connection State Machine Design

### Rationale

The current pattern is an infinite `while(true)` loop in each monitor with a bare `catch / await Task.Delay(5s)`. This has three problems: no state is visible to the UI, no backoff strategy, and no circuit-breaker to stop hammering a rate-limited MQTT broker.

### States

```
Idle
  │  StartAsync() called
  ▼
Scanning          (BLE only: advertising filter active)
  │  Device found / address known
  ▼
Connecting        (GATT connect, or MQTT TLS handshake)
  │  Transport open
  ▼
Authenticating    (BLE: ECDH + auth packet; MQTT: credentials accepted)
  │  Auth success
  ▼
Streaming         (data flowing; StateChanged fires on each decode)
  │  Disconnect / error
  ▼
Disconnected
  │  Retry scheduled (exponential backoff)
  ▼
Retrying          (waiting for next attempt window)
  │  Window elapsed
  └─► Connecting  (back to connecting, not scanning — address already known)

  Any state + StopAsync() called → Idle
  Any state + device powered off for N minutes → Suspended (no retry until woken)
```

### Implementation: Use Stateless

Use the `stateless` library (NuGet: `Stateless`, currently v5.x, MIT). It is a lightweight, zero-dependency state machine that supports:
- Entry/exit actions per state (log transitions, update UI-visible `ConnectionStatus` property)
- Guard clauses on triggers (only retry if `_retryCount < MaxRetries`, or use Polly to cap)
- Parameterized triggers (pass the disconnect reason through the `Disconnected` transition)
- External state storage (store current state in `MonitorEntry` so it survives orchestrator restarts)

The state machine does NOT replace the `IDeviceMonitor` interface. `BleMonitor` and `MqttMonitor` each own one `ConnectionStateMachine` instance internally.

### Connection Status as UI-Observable Property

Add a `ConnectionStatus` enum (`Idle | Scanning | Connecting | Authenticating | Streaming | Disconnected | Retrying | Error`) to `DeviceState`. On every state machine transition, raise `StateChanged` with the updated `DeviceState`. `DeviceViewModel` maps this to a color indicator and status label.

### Retry Strategy

Use Polly v8 `ResiliencePipeline` around the connect attempt (not around the whole monitor loop):

```csharp
// Configure in BleMonitor / MqttMonitor constructor
_connectPipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = int.MaxValue,          // retry forever
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(2),           // initial: 2s
        MaxDelay = TimeSpan.FromMinutes(5),        // cap at 5 min
        UseJitter = true,                          // DecorrelatedJitterBackoffV2
        ShouldHandle = new PredicateBuilder()
            .Handle<BleConnectionException>()
            .Handle<MqttCommunicationException>(),
        OnRetry = args =>
        {
            _stateMachine.Fire(Trigger.RetryScheduled, args.RetryDelay);
            return ValueTask.CompletedTask;
        }
    })
    .Build();
```

This replaces the current hardcoded `await Task.Delay(5000)` in both monitors. The state machine's `Retrying` state entry action sets a UI countdown; `Connecting` entry fires the actual connect attempt inside the Polly pipeline.

**MQTT-specific:** Add a circuit breaker stage before the retry, with a break duration of 30 seconds after 3 consecutive failures, to stop hammering the EcoFlow MQTT broker's rate limiter.

---

## BLE Adapter Abstraction Pattern

### Existing Interface (`IBleAdapter`)

The `IBleAdapter` / `IBleGattConnection` interface pair already exists in `Core/Platform/`. The macOS implementation lives in `App/Services/CoreBluetoothBleAdapter.cs`. The pattern is correct. The only work is filling in the two stub implementations.

### Interface Contract (confirmed adequate for all 3 platforms)

```csharp
// Already in Core/Platform/IBleAdapter.cs
public interface IBleAdapter
{
    event EventHandler<BleDeviceInfo> DeviceDiscovered;
    Task StartScanAsync(CancellationToken ct);
    Task StopScanAsync();
    Task<IBleGattConnection> ConnectAsync(string deviceAddress, CancellationToken ct);
    bool IsAvailable { get; }
}

public interface IBleGattConnection : IAsyncDisposable
{
    event EventHandler<byte[]> NotificationReceived;
    Task WriteAsync(Guid serviceUuid, Guid characteristicUuid, byte[] data, CancellationToken ct);
    Task SubscribeAsync(Guid serviceUuid, Guid characteristicUuid, CancellationToken ct);
    Task DisconnectAsync();
    bool IsConnected { get; }
}
```

No changes needed to the interface. Both new adapters implement these two interfaces.

### Windows: `WinRtBleAdapter`

**Library:** `Windows.Devices.Bluetooth` (WinRT, inbox on Windows 10+). Available to desktop .NET apps without a package manifest — capabilities are not enforced for non-packaged desktop apps. Reference the WinRT types via the `net10.0-windows10.0.19041.0` TFM already used in `Platform.Windows`.

**Scanning:** Use `BluetoothLEAdvertisementWatcher`, not `DeviceWatcher`. `DeviceWatcher` only returns previously-paired or cached devices. `BluetoothLEAdvertisementWatcher` fires on live advertisements, matching the behavior of `CBCentralManager` on macOS.

```csharp
// WinRtBleAdapter scanning skeleton
var watcher = new BluetoothLEAdvertisementWatcher
{
    ScanningMode = BluetoothLEScanningMode.Active
};
watcher.Received += (_, args) =>
{
    var mfrData = args.Advertisement.ManufacturerSpecificData
        .FirstOrDefault(d => d.CompanyId == 0xB5C5);
    if (mfrData is null && !args.Advertisement.LocalName.StartsWith("EF-")) return;
    var info = ParseAdvertisement(args);
    DeviceDiscovered?.Invoke(this, info);
};
watcher.Start();
```

**GATT connection:** `BluetoothLEDevice.FromBluetoothAddressAsync(address)` → `GetGattServicesForUuidAsync(serviceUuid, BluetoothCacheMode.Uncached)` → subscribe `ConnectionStatusChanged` event to detect drops.

**Known pitfall:** On Windows, if the device is paired via the OS Bluetooth settings, `FromBluetoothAddressAsync` may return a stale cached object. Always use `BluetoothCacheMode.Uncached` on service/characteristic discovery.

**Confidence:** HIGH. WinRT BLE APIs are stable since Windows 10 1703 and fully accessible from non-packaged desktop apps on .NET.

### Linux: `BlueZBleAdapter`

**Library:** `Linux.Bluetooth` 5.x (NuGet: `Linux.Bluetooth`). Wraps BlueZ D-Bus via `Tmds.DBus`. Stable and actively maintained (last release verifiable on NuGet).

```csharp
// BlueZBleAdapter scanning skeleton
var adapter = await BlueZManager.GetAdapterAsync("hci0");
adapter.DeviceFound += async (_, device) =>
{
    var props = await device.GetAllAsync();
    if (!IsEcoFlowDevice(props)) return;
    DeviceDiscovered?.Invoke(this, MapToDeviceInfo(props));
};
await adapter.StartDiscoveryAsync();
```

**GATT connection:** `device.ConnectAsync()` → `device.WaitForPropertyValueAsync("ServicesResolved", true, timeout)` → `device.GetServiceAsync(uuid)` → `characteristic.Value += handler`.

**Pitfall:** BlueZ requires the `bluetoothd` daemon running with sufficient permissions. The user must be in the `bluetooth` group or the app must run with elevated privileges. Surface this as a startup check via `IElevationService`.

**Confidence:** MEDIUM. `Linux.Bluetooth` API surface verified. D-Bus permission requirements are standard BlueZ behavior but depend on distro configuration.

### macOS: `CoreBluetoothBleAdapter` (existing)

No structural changes. Move from `App/Services/` to `Platform.macOS/` to be consistent with Windows and Linux adapters. This is a housekeeping change, not a functional one.

### Registration

`PlatformServiceFactory.Register()` already handles runtime OS detection and reflection-based assembly loading. Add `WinRtBleAdapter` and `BlueZBleAdapter` registrations to the Windows and Linux platform assemblies respectively.

---

## SQLite Data Layer Architecture

### Interface

```csharp
// New: Core/History/IHistoryStore.cs
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

public enum Resolution { Raw, Hourly, Daily }
```

### Schema

```sql
CREATE TABLE IF NOT EXISTS telemetry_snapshots (
    id          INTEGER PRIMARY KEY,
    device_sn   TEXT    NOT NULL,
    ts          INTEGER NOT NULL,   -- Unix epoch seconds (INTEGER, not TEXT)
    battery_pct REAL,
    total_in_w  INTEGER,
    total_out_w INTEGER,
    power_state TEXT,               -- PowerState enum name
    remain_min  INTEGER,
    source      TEXT                -- "Cloud" or "BLE"
);

CREATE INDEX IF NOT EXISTS idx_telemetry_device_ts
    ON telemetry_snapshots (device_sn, ts DESC);
```

Timestamps as `INTEGER` epoch seconds: SQLite's native date functions work directly on this type, and range queries use the index efficiently. Avoid TEXT timestamps (no index benefit for range scans).

Do not store the full `DeviceState` graph. Persist only the fields needed for historical charts: battery %, power in/out, power state, remain minutes. Cell voltages and temperatures can be added as a separate `cell_voltages` table if needed in a later phase.

### Write Pattern

Write on every `StateChanged` event, but batch with a 10-second debounce per device. A 1Hz telemetry stream from BLE would produce 86,400 raw rows/day/device. The debounce collapses that to ~8,640 rows/day, which remains fast and small but still produces 1-minute resolution charts.

```csharp
// In MonitorOrchestrator.OnStateChanged(), after raising DeviceUpdated:
_historyDebouncer.Post(entry.State);  // Channel<TelemetrySnapshot>, 10s consumer
```

The debouncer is a `Channel<TelemetrySnapshot>` with a background consumer that batches writes into a single transaction.

### WAL Mode

Enable WAL at connection open time:

```csharp
using var conn = new SqliteConnection(_connectionString);
await conn.OpenAsync();
await conn.ExecuteAsync("PRAGMA journal_mode=WAL;");
await conn.ExecuteAsync("PRAGMA synchronous=NORMAL;");
```

WAL enables concurrent readers (chart queries) without blocking the writer (telemetry consumer). `synchronous=NORMAL` is safe with WAL and reduces fsync overhead.

### Query Pattern (Resolution Downsampling)

For hourly/daily views, use SQLite's `strftime` to group by time bucket:

```sql
-- Hourly average over 7 days
SELECT
    strftime('%Y-%m-%d %H:00', ts, 'unixepoch') AS hour,
    AVG(battery_pct) AS avg_battery,
    MAX(total_in_w)  AS peak_in_w,
    MAX(total_out_w) AS peak_out_w
FROM telemetry_snapshots
WHERE device_sn = @sn
  AND ts >= @from
  AND ts <= @to
GROUP BY hour
ORDER BY hour;
```

The `Resolution` enum parameter in `IHistoryStore.QueryAsync` selects between raw rows, hourly aggregate, and daily aggregate queries. All three are simple SQL with no ORM needed.

### Library Choice

Use `Microsoft.Data.Sqlite` (NuGet: `Microsoft.Data.Sqlite`, version aligned with .NET 10). Do not add Entity Framework Core — the schema is simple and a raw ADO.NET approach avoids migration overhead for a 2-table schema. Use Dapper for mapping if query results become complex.

### Retention

Daily `PruneAsync` call (scheduled from `MonitorOrchestrator` startup): delete rows older than configurable retention period (default 90 days). This keeps the database bounded.

---

## Rules Engine Pattern

### Current State

`TriggerEvaluator` + `ActionRunner` handle edge triggers (`PowerLost`, `PowerRestored`) and level triggers (`BatteryBelow`, `TimeRemainingBelow`) with a 5-minute cooldown. `ActionRunner` handles `RunScript`, `Shutdown`, `Hibernate`, `Sleep`, `Notification`, `WriteLog`.

### What Needs to Change

Three new action types are required: `Webhook` (HTTP POST), `PushNotification` (third-party push service), and `Email`. The trigger evaluation logic itself is adequate. The dispatch pipeline needs extension.

### Pattern: Strategy per ActionType

Keep the existing `ActionType` enum and `ActionConfig` model. Add three values: `Webhook`, `PushNotification`, `Email`. Each action type is handled by a dedicated `IActionHandler` implementation.

```csharp
// New: Core/Rules/IActionHandler.cs
public interface IActionHandler
{
    ActionType Type { get; }
    Task ExecuteAsync(ActionConfig config, DeviceState state, CancellationToken ct);
}
```

`ActionRunner` becomes a dispatcher that resolves the correct handler from a registered `IReadOnlyDictionary<ActionType, IActionHandler>`:

```csharp
public class ActionRunner
{
    private readonly IReadOnlyDictionary<ActionType, IActionHandler> _handlers;

    public async Task RunAsync(ActionConfig action, DeviceState state, CancellationToken ct)
    {
        if (_handlers.TryGetValue(action.Type, out var handler))
            await handler.ExecuteAsync(action, state, ct);
        else
            Logger.Log($"[Rules] No handler for action type {action.Type}");
    }
}
```

**Handler implementations:**
- `WebhookActionHandler` — `HttpClient.PostAsync(url, JsonContent)` with Polly retry (3 attempts, exponential, max 30s). Template-expand `{device}`, `{battery}` etc. in the URL and body.
- `NotificationActionHandler` — existing, delegates to `INotificationService`
- `ScriptActionHandler` — existing, delegates to `IScriptRunnerService`
- `PowerActionHandler` — existing, delegates to `IPowerActionService`
- `EmailActionHandler` — `SmtpClient` or an SMTP-over-HTTP relay (defer to later sub-phase; mark as `TODO` stub initially)

### Trigger Evaluation: No Changes

`TriggerEvaluator.Evaluate()` is already correct. It is called on every `StateChanged` from `MonitorOrchestrator.OnStateChanged()`. Keep this call site. The only change is that `ActionRunner.Run()` becomes `ActionRunner.RunAsync()` to support async HTTP calls, and the call site in `OnStateChanged` awaits it (or fires-and-forgets with a logged exception boundary).

### Rule Configuration Storage

Rules remain in `config.json` as part of `DeviceConfig.Rules`. No migration to a database is needed for the milestone. The SQLite store is for telemetry only.

---

## Data Flow Direction (Updated)

```
StateChanged event (BleMonitor / MqttMonitor)
  │
  ▼
MonitorOrchestrator.OnStateChanged()
  ├── ConnectionStateMachine.Fire(Trigger.DataReceived)   [update state]
  ├── IHistoryStore.WriteSnapshotAsync(snapshot)          [telemetry write]
  ├── TriggerEvaluator.Evaluate() → ActionRunner.RunAsync() [rules]
  └── DeviceUpdated event raised
        │
        ▼
      DashboardViewModel.OnDeviceUpdated()
        └── Dispatcher.UIThread.Post() → DeviceViewModel.UpdateFromState()
                                      → HistoryViewModel.NotifyNewData()
```

History reads flow in the opposite direction: `HistoryViewModel` queries `IHistoryStore` directly (not through the orchestrator) when the user navigates to the history page or changes the time range selector.

---

## Build Order Implications

The four components have the following dependency ordering for implementation:

**Phase 1 — Foundation (no new dependencies, unblocks everything else)**
1. `ConnectionStateMachine` in Core (add `Stateless` NuGet)
2. Integrate into `BleMonitor` and `MqttMonitor` (replaces raw retry loops)
3. Surface `ConnectionStatus` in `DeviceState` and `DeviceViewModel`

Rationale: BLE adapters on Windows/Linux will immediately use the state machine. History writes depend on a stable `StateChanged` event stream. Can be validated on macOS before cross-platform BLE is complete.

**Phase 2 — Platform BLE Adapters (parallel, no interdependency)**
4. `WinRtBleAdapter` in `Platform.Windows`
5. `BlueZBleAdapter` in `Platform.Linux`
6. Move `CoreBluetoothBleAdapter` from `App/Services/` to `Platform.macOS/`

Rationale: Each adapter only depends on the `IBleAdapter` interface already in Core. They can be developed and tested independently per-OS.

**Phase 3 — History Layer**
7. `IHistoryStore` interface + `TelemetrySnapshot` model in Core
8. `SqliteHistoryStore` implementation in Core
9. Write integration in `MonitorOrchestrator.OnStateChanged()`
10. `HistoryViewModel` + chart view in App

Rationale: Schema is simple and stable once `DeviceState` fields are frozen by Phase 1. History layer has no dependency on Platform BLE adapters.

**Phase 4 — Rules Engine Extension**
11. `IActionHandler` interface in Core
12. `WebhookActionHandler` (with Polly retry) in Core
13. Refactor `ActionRunner` to use handler dispatch
14. UI for adding/editing rules in `SettingsViewModel`

Rationale: Action handler refactor is lowest risk if history layer and BLE adapters are already stable, as the rules engine shares the same `StateChanged` event pathway.

---

## Anti-Patterns to Avoid

### Anti-Pattern 1: Retry Loops Without State

**What:** The current `while(true) { try { ... } catch { await Task.Delay(5s); } }` pattern.
**Why bad:** UI cannot display connection state, MQTT broker gets hammered on every connection failure, no circuit breaker stops cascading retries.
**Instead:** `ConnectionStateMachine` + Polly `ResiliencePipeline` with `MaxDelay` and circuit breaker for MQTT.

### Anti-Pattern 2: Writing to SQLite on Every Frame

**What:** Calling `IHistoryStore.WriteSnapshotAsync` on every `StateChanged` event directly.
**Why bad:** BLE streams at ~1Hz; that is 86,400 writes/day/device. Each write holds a brief lock; chart reads compete for the same connection.
**Instead:** 10-second debounce via `Channel<TelemetrySnapshot>` with a batching consumer. One transaction per batch.

### Anti-Pattern 3: Blocking the UI Thread in Rules

**What:** Making `OnStateChanged` synchronously run action handlers that involve HTTP calls or shell scripts.
**Why bad:** HTTP POST to a slow webhook blocks `StateChanged` processing, delaying UI updates.
**Instead:** `ActionRunner.RunAsync()` is awaited inside a `Task.Run()` fire-and-forget boundary in the orchestrator, with a per-action exception catch so one failing action does not block others.

### Anti-Pattern 4: Putting BLE Adapter Logic in App Project

**What:** `CoreBluetoothBleAdapter` currently lives in `App/Services/`.
**Why bad:** Inconsistent with Windows and Linux adapters which will be in Platform projects; forces App to have direct CoreBluetooth references that should be platform-isolated.
**Instead:** Move to `Platform.macOS/`. The interface lives in Core; all implementations live in Platform projects.

### Anti-Pattern 5: Platform Detection at Runtime in Core

**What:** Calling `RuntimeInformation.IsOSPlatform()` inside Core to branch BLE behavior.
**Why bad:** Core should have zero OS knowledge. Platform detection logic belongs in `PlatformServiceFactory`.
**Instead:** All platform branching stays in `PlatformServiceFactory.Register()` in the App layer.

---

## Scalability Considerations

This is a local desktop app monitoring 1–5 devices. Scalability means "works reliably for years" not "handles 10K users."

| Concern | Current | After This Milestone |
|---------|---------|----------------------|
| BLE reconnect storms | Silent drop, never reconnects | State machine + exponential backoff, 5min cap |
| MQTT broker rate-limit | No backoff, gets blocked | Circuit breaker + jitter |
| SQLite file growth | N/A (no history) | 90-day rolling prune, ~50MB/device/year at 10s sampling |
| Thread safety of DeviceState | Races not prevented | Unchanged — `Dispatcher.UIThread.Post()` serializes UI reads; orchestrator should add a `lock` on `MonitorEntry.State` mutations |
| Multiple rules firing simultaneously | Already fire-and-forget per action | Make truly parallel with `Task.WhenAll` per rule's actions |

---

## Sources

- [Stateless state machine library](https://github.com/dotnet-state-machine/stateless) — HIGH confidence (official GitHub, actively maintained)
- [Polly v8 retry strategy](https://www.pollydocs.org/strategies/retry.html) — HIGH confidence (official Polly docs)
- [BluetoothLEAdvertisementWatcher (WinRT)](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice?view=winrt-26100) — HIGH confidence (official Microsoft docs)
- [Linux.Bluetooth NuGet / SuessLabs](https://github.com/SuessLabs/Linux.Bluetooth) — MEDIUM confidence (active library, API surface verified, distro permission behavior is standard BlueZ)
- [Microsoft.Data.Sqlite overview](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/) — HIGH confidence (official Microsoft docs)
- [SQLite WAL mode](https://www.sqlite.org/wal.html) — HIGH confidence (official SQLite documentation)
- [Avalonia BLE discussion](https://github.com/AvaloniaUI/Avalonia/discussions/10594) — LOW confidence (no concrete guidance; confirms DI interface pattern is the community approach)

---

*Architecture analysis: 2026-03-30*
