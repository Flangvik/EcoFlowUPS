# Phase 1: Infrastructure - Research

**Researched:** 2026-03-30
**Domain:** .NET 10 / Avalonia — Serilog structured logging, Stateless FSM, Polly retry, thread-safe DeviceState, connection-state UI bar
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Connection State Display
- **D-01:** Replace the current green/red dot with a **full-width state badge bar** under the device name showing: state text + source indicator + retry info. More prominent, harder to miss.
- **D-02:** Show retry detail as **attempt count + countdown**: "Reconnecting (attempt 3, next in 8s)". Shows both progress and timing.
- **D-03:** Keep the "via BLE" / "via Cloud" teal text source indicator as-is — unobtrusive and works.

#### Staleness and Offline UX
- **D-04:** When a device disconnects, **dim all values to 50% opacity** and show a "Last update: Xm ago" badge on the state bar. Values stay visible but clearly aged.
- **D-05:** Data becomes "stale" after **30 seconds** of no updates.
- **D-06:** After 5+ minutes offline, **clear values to defaults** ("--" / "0").

#### Error Surfacing
- **D-07:** Errors show **per-device in the connection state bar**: "Error: BLE auth failed". Localized to the affected device, no global notification bar.
- **D-08:** Error detail is **friendly with expandable technical info**: "BLE connection failed" + clickable "Details" revealing the exception.
- **D-09:** Errors accumulate in the **same event log as power events** — single audit trail.

#### Logging Migration
- **D-10:** **Big bang replacement** of all Logger.Log() calls with ILogger<T> via DI. One clean commit.
- **D-11:** Default log level: **Information** — connection events, state changes, rule firings.
- **D-12:** Log rotation: **10MB / 3 files** (~30MB max disk usage).

### Claude's Discretion
- Connection state machine implementation (Stateless library vs hand-rolled enum FSM)
- Thread-safety approach for DeviceState (lock, immutable snapshots, or concurrent collections)
- Specific Serilog sink configuration and structured field naming
- How to remove verbose BleTransport frame-level logging without losing diagnostic capability

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| INFRA-01 | Replace static Logger with Serilog structured logging (file sink with rotation, log levels) | Full Serilog + MEL bridge pattern documented; big-bang replacement strategy verified against codebase call sites |
| INFRA-02 | DeviceState mutations are thread-safe (concurrent BLE + MQTT writes don't corrupt state) | Lock-per-mutation pattern recommended; `ConcurrentDictionary` for RuleLastFired; immutable snapshot approach also documented |
| INFRA-03 | Bare catch blocks replaced with proper error handling — no silent swallowing of exceptions | 27 bare catch sites inventoried; typed exception handling patterns specified per category |
| INFRA-04 | Connection state machine (Stateless or equivalent) replaces raw while-loop retry in BleMonitor and MqttMonitor | Stateless 5.20.1 verified; state enum + transition map; integration into existing IDeviceMonitor pattern |
| CONN-01 | App shows visible connection state per device with retry attempt counter | `ConnectionStatus` enum added to DeviceState; state badge bar in DashboardView.axaml; DeviceViewModel observable props |
| CONN-02 | App displays last-known data with staleness indicator when device is disconnected | `LastUpdated` timestamp already in DeviceState; 30s stale threshold; opacity binding; "--" clear at 5m |
| CONN-05 | Connection mode toggle provides clear feedback during transitions and actually restarts the monitor | CycleConnectionMode bug fix (save after success); state machine transitions surface feedback via `ConnectionStatus` |
| UX-02 | Error states are always surfaced — no blank screens, no silent failures | Error state in FSM + per-device state bar; replace bare catch with typed catch + ILogger |
| UX-03 | Verbose debug logging removed from production (BleTransport frame-level logs) | Guard hex-dump log calls behind `LogEventLevel.Debug`; default level = Information |
</phase_requirements>

---

## Summary

Phase 1 is a hardening and observability pass — no new features, only replacing broken infrastructure with production-grade alternatives. The three primary work streams are: (1) replace the static `Logger` class with Serilog + `ILogger<T>` injection, (2) add a `ConnectionStateMachine` inside each monitor using the Stateless library to replace the bare `while(true)` retry loops and expose state to the UI, and (3) make `DeviceState` thread-safe and surface all connection/error states through a new full-width state badge bar in the dashboard.

The codebase is well-understood from the prior analysis. `DeviceState` is 16 lines and needs only two additions: a `ConnectionStatus` enum property and a `lock` around mutation sites (or replacement of `RuleLastFired` with `ConcurrentDictionary`). The monitor classes each own their retry loop and are the correct home for a `Stateless.StateMachine<ConnectionStatus, ConnectionTrigger>` instance. The `DeviceViewModel` already has `ConnectionBadge`, `ActiveSource`, and `StatusText` observable properties — the state bar extends this rather than replacing it.

The MQTT broker rate-limit bug (fixed 5-second retry, confirmed production blocker) is addressed by adding Polly `ResiliencePipeline` with exponential backoff + jitter as the retry mechanism inside the state machine. The ECDH session key must be fully re-negotiated on every BLE reconnect — this is a hard constraint from the EcoFlow BLE protocol.

**Primary recommendation:** Use Stateless 5.20.1 for the connection FSM (Claude's discretion), lock-based mutation for `DeviceState` thread safety (simplest correct approach), and Serilog 4.3.1 with `Serilog.Sinks.File` 7.0.0 for the logging replacement.

---

## Project Constraints (from CLAUDE.md)

These directives are mandatory for all planning and implementation:

- **Tech stack locked:** .NET 10 + Avalonia UI — not changing
- **Devices:** EcoFlow Delta 3 and Delta 3 Max only — both use pd335_sys protobuf
- **BLE libraries:** Platform-native only — WinRT on Windows, BlueZ on Linux, CoreBluetooth on macOS (Phase 1 is macOS only; Windows/Linux BLE is Phase 2)
- **No test suite:** Zero C# unit tests currently — test infrastructure is a Wave 0 gap; CONCERNS.md priority
- **No environment variables in C# service:** All config via JSON at AppData
- **GSD workflow enforcement:** All file edits must go through a GSD command
- **Coding conventions:** File-scoped namespaces, PascalCase types, `_camelCase` private fields, no regions, `[ObservableProperty]` source generators, `Dispatcher.UIThread.Post()` for all UI marshaling
- **DI pattern:** `App.Services.GetRequiredService<T>()` — static accessor, not constructor injection in views
- **AXAML design system:** Use existing `BackgroundPrimary`, `SurfaceCard`, `AccentPrimary`, `TextMuted` resources; status colors `#00E676` green, `#FFB300` amber, `#FF5252` red, `#666666` gray
- **EcoFlow API is undocumented / reverse-engineered:** Protocol changes without notice; never assume payload format is stable

---

## Standard Stack

### Core — New Packages (Phase 1)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Serilog` | 4.3.1 | Log event pipeline, structured logging | Current stable; ships Feb 2026; netstandard2.0 compatible with net10 |
| `Serilog.Sinks.File` | 7.0.0 | Rolling file sink with size limits, retention | Current stable; replaces synchronous `File.AppendAllText` with buffered async writes |
| `Serilog.Extensions.Logging` | 10.0.0 | Bridge: routes `ILogger<T>` calls into Serilog | Versioned to match .NET 10 MEL; confirmed on NuGet |
| `Stateless` | 5.20.1 | Lightweight FSM — entry/exit actions, parameterized triggers, no dependencies | Current stable; explicit net10.0 TFM; ARCHITECTURE.md chose this |
| `Polly` | 8.6.6 | `ResiliencePipeline` with exponential backoff + jitter for reconnect | Current stable (updated Mar 2026); confirmed in STACK.md decision |

### Already Present (No New Packages)

| Library | Version | Used In | Notes |
|---------|---------|---------|-------|
| `Microsoft.Extensions.DependencyInjection` | 8.0.1 | App | DI container; upgrade to 10.x not required for this phase |
| `Microsoft.Extensions.Logging.Abstractions` | (inbox net10) | Core | `ILogger<T>` interface; comes with the SDK |
| `CommunityToolkit.Mvvm` | 8.4.0 | Core + App | `[ObservableProperty]` already used everywhere |
| `MQTTnet` | 4.3.7.1207 | Core | Not changing; Polly wraps around its connect call |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Stateless | Hand-rolled `enum ConnectionStatus` + `switch` | Hand-rolled is simpler but no entry/exit action hooks, no parameterized triggers for retry delay passthrough — would need custom event wiring anyway |
| Stateless | `Automatonymous` / `MassTransit.StateMachine` | These are saga-oriented, heavyweight — wrong scope |
| `lock` on DeviceState mutations | Immutable snapshot (replace-on-update) | Immutable is cleaner but requires changing 15+ mutation sites to produce new instances; lock on mutable class is a minimal diff |
| `lock` on DeviceState mutations | `Interlocked` per field | Cannot atomically update multiple fields (Bms + Display + Ems + LastUpdated in one operation) |
| Serilog.Sinks.File | NLog file target | Equivalent capability; Serilog has tighter MEL bridge; already in STACK.md decision |

**Installation (additions to `EcoFlowMonitor.Core.csproj`):**
```xml
<PackageReference Include="Serilog" Version="4.3.1" />
<PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
<PackageReference Include="Serilog.Extensions.Logging" Version="10.0.0" />
<PackageReference Include="Stateless" Version="5.20.1" />
<PackageReference Include="Polly" Version="8.6.6" />
```

**Version verification:** Confirmed against NuGet.org search results as of 2026-03-30:
- Serilog 4.3.1 published 2026-02-10
- Serilog.Sinks.File 7.0.0 (latest stable; 8.0.0 prerelease exists but use stable)
- Serilog.Extensions.Logging 10.0.0
- Stateless 5.20.1 (net10.0 TFM confirmed)
- Polly 8.6.6 published 2026-03-04

---

## Architecture Patterns

### Recommended Project Structure Changes

```
service/src/EcoFlowMonitor.Core/
├── Client/
│   ├── ConnectionStatus.cs        # NEW: enum + ConnectionTrigger enum
│   ├── IConnectionStateReporter.cs # NEW: interface to decouple FSM from UI
│   ├── MqttMonitor.cs              # MODIFY: add Stateless FSM + Polly pipeline
│   └── Ble/
│       └── BleMonitor.cs           # MODIFY: add Stateless FSM + Polly pipeline
├── State/
│   └── DeviceState.cs              # MODIFY: add ConnectionStatus, lock object, ConcurrentDictionary
└── Logging/
    └── Logger.cs                   # DELETE: replaced by ILogger<T> injection

service/src/EcoFlowMonitor.App/
├── App.axaml.cs                    # MODIFY: register Serilog provider in DI
├── Program.cs                      # MODIFY: configure Serilog bootstrap logger
├── ViewModels/
│   └── DeviceViewModel.cs          # MODIFY: add StateBarText, RetryInfo, IsStale, Opacity props
└── Views/
    └── DashboardView.axaml         # MODIFY: insert state badge bar between device name and stat cards
```

### Pattern 1: Connection State Machine (Stateless)

**What:** Each monitor (`BleMonitor`, `MqttMonitor`) owns one `StateMachine<ConnectionStatus, ConnectionTrigger>`. The state machine replaces the `while(true)` retry loop and all boolean `IsConnected` flag mutations.

**When to use:** Whenever connection state must be tracked, retry logic must be observable, and error states must propagate to the UI without coupling monitor logic to the ViewModel layer.

**State enum:**
```csharp
// Source: ARCHITECTURE.md + CONTEXT.md decisions
// File: service/src/EcoFlowMonitor.Core/Client/ConnectionStatus.cs
public enum ConnectionStatus
{
    Idle,
    Scanning,       // BLE only: advertising filter active
    Connecting,     // GATT connect / MQTT TLS handshake in progress
    Authenticating, // BLE: ECDH + auth packet; MQTT: credentials accepted
    Streaming,      // Data flowing; StateChanged fires per decode
    Retrying,       // Waiting for next attempt window (exposes RetryAttempt + RetryDelay)
    Error,          // Terminal error requiring user action
    Disconnected    // Clean disconnect or pre-first-connect
}

public enum ConnectionTrigger
{
    Start,
    DeviceFound,     // BLE: advertisement seen
    Connected,       // Transport open
    Authenticated,   // Auth success
    DataReceived,    // First packet after connect (transitions to Streaming)
    RetryScheduled,  // Polly OnRetry callback fires this with delay duration
    ErrorOccurred,   // Non-retriable error (auth failure, permission denied)
    Disconnected,    // Transport closed
    Stop             // StopAsync() called
}
```

**State machine wiring (BleMonitor example):**
```csharp
// Source: Stateless 5.20.1 API — https://github.com/dotnet-state-machine/stateless
_machine = new StateMachine<ConnectionStatus, ConnectionTrigger>(ConnectionStatus.Idle);

var retryTrigger = _machine.SetTriggerParameters<TimeSpan>(ConnectionTrigger.RetryScheduled);
var errorTrigger = _machine.SetTriggerParameters<string>(ConnectionTrigger.ErrorOccurred);

_machine.Configure(ConnectionStatus.Idle)
    .Permit(ConnectionTrigger.Start, ConnectionStatus.Scanning);

_machine.Configure(ConnectionStatus.Scanning)
    .OnEntry(() => NotifyStateChanged())
    .Permit(ConnectionTrigger.DeviceFound, ConnectionStatus.Connecting)
    .Permit(ConnectionTrigger.Stop, ConnectionStatus.Idle);

_machine.Configure(ConnectionStatus.Connecting)
    .OnEntry(() => NotifyStateChanged())
    .Permit(ConnectionTrigger.Connected, ConnectionStatus.Authenticating)
    .Permit(ConnectionTrigger.RetryScheduled, ConnectionStatus.Retrying)
    .Permit(ConnectionTrigger.Stop, ConnectionStatus.Idle);

_machine.Configure(ConnectionStatus.Retrying)
    .OnEntryFrom(retryTrigger, delay =>
    {
        _retryDelay = delay;
        NotifyStateChanged(); // surfaces countdown via DeviceState.RetryDelay
    })
    .Permit(ConnectionTrigger.Connected, ConnectionStatus.Connecting)
    .Permit(ConnectionTrigger.Stop, ConnectionStatus.Idle);

_machine.Configure(ConnectionStatus.Streaming)
    .OnEntry(() => NotifyStateChanged())
    .Permit(ConnectionTrigger.Disconnected, ConnectionStatus.Retrying)
    .Permit(ConnectionTrigger.ErrorOccurred, ConnectionStatus.Error)
    .Permit(ConnectionTrigger.Stop, ConnectionStatus.Idle);

_machine.Configure(ConnectionStatus.Error)
    .OnEntryFrom(errorTrigger, msg =>
    {
        _lastError = msg;
        NotifyStateChanged();
    })
    .Permit(ConnectionTrigger.Start, ConnectionStatus.Connecting); // manual retry
```

**NotifyStateChanged:** On every state entry, copy `_machine.State`, `_retryAttempt`, `_retryDelay`, and `_lastError` into `DeviceState`, then fire `StateChanged`. This propagates state to the orchestrator and then the UI without the FSM knowing about ViewModels.

### Pattern 2: Polly Reconnect Pipeline

**What:** Wrap the connect attempt inside a `ResiliencePipeline`. The `OnRetry` callback fires the `RetryScheduled` trigger on the state machine.

**When to use:** Replacing the existing `await Task.Delay(5000)` in `ConnectLoopAsync` and `OnDisconnectedAsync`.

```csharp
// Source: Polly 8.6.6 docs — https://www.pollydocs.org/strategies/retry.html
_connectPipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = int.MaxValue,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromMinutes(5),
        UseJitter = true,
        OnRetry = args =>
        {
            _retryAttempt = args.AttemptNumber + 1;
            _machine.Fire(retryTrigger, args.RetryDelay);
            return ValueTask.CompletedTask;
        }
    })
    .Build();

// Usage in ConnectAndAuthAsync:
await _connectPipeline.ExecuteAsync(async ct =>
{
    _machine.Fire(ConnectionTrigger.Connecting);
    await ConnectTransportAsync(ct);   // throws on failure -> Polly retries
    _machine.Fire(ConnectionTrigger.Connected);
    await AuthenticateAsync(ct);
    _machine.Fire(ConnectionTrigger.Authenticated);
}, _cts.Token);
```

**MQTT circuit breaker:** Add a `AddCircuitBreaker` stage before the retry for `MqttMonitor` only, with 3-failure threshold and 30-second open duration, to stop hammering the EcoFlow broker rate limiter.

### Pattern 3: Serilog Configuration (Big Bang)

**What:** Remove `Logger.cs`, configure Serilog as the MEL provider, inject `ILogger<T>` everywhere.

**When to use:** Single commit replaces all 99 `Logger.Log()` call sites.

```csharp
// Source: Serilog.Extensions.Logging 10.0.0 — https://github.com/serilog/serilog-extensions-logging
// In App.axaml.cs, OnFrameworkInitializationCompleted():
var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "EcoFlowMonitor", "logs", "app-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File(
        logPath,
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 10 * 1024 * 1024,   // 10 MB — D-12
        retainedFileCountLimit: 3,               // 3 files — D-12
        rollOnFileSizeLimit: true,
        buffered: true,                          // async-buffered writes
        outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

services.AddLogging(lb => lb.AddSerilog(dispose: true));
```

**Constructor injection pattern (replacing Logger.Log calls):**
```csharp
// Before (static):
Logger.Log($"MqttMonitor: connect failed — {ex.Message}");

// After (ILogger<T>):
public class MqttMonitor : IDeviceMonitor
{
    private readonly ILogger<MqttMonitor> _logger;

    public MqttMonitor(DeviceConfig config, DeviceState state,
                       MqttCredentials creds, string userId,
                       ILogger<MqttMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // ...
    }

    // Information level — always written (D-11)
    _logger.LogInformation("Connect failed: {Message}, retry in {Delay}s",
        ex.Message, delay.TotalSeconds);

    // Debug level — only written when level overridden for diagnostics (UX-03)
    _logger.LogDebug("Raw notification {Bytes} bytes: {Hex}",
        data.Length, Convert.ToHexString(data));
}
```

**BleTransport frame-level logs:** Change all 14 `Logger.Log()` hex-dump calls in `BleTransport.cs` to `_logger.LogDebug(...)`. With default `MinimumLevel.Information`, these are a no-op at runtime (UX-03). A developer can override to `Debug` via `LoggerConfiguration.MinimumLevel.Override("EcoFlowMonitor.Client.Ble.BleTransport", LogEventLevel.Debug)`.

### Pattern 4: DeviceState Thread Safety

**What:** Add a `private readonly object _lock = new()` to `DeviceState`. Wrap all property mutation calls in `BleMonitor.OnPacketReceived` and `MqttMonitor.OnMessageReceivedAsync` with `lock (state) { ... }`.

**When to use:** The minimal correct approach — no refactoring of callers, preserves mutable class semantics.

```csharp
// MODIFY: DeviceState.cs
public class DeviceState
{
    // Existing fields unchanged...

    // NEW additions:
    public ConnectionStatus ConnectionStatus { get; set; } = ConnectionStatus.Idle;
    public int RetryAttempt { get; set; }
    public TimeSpan RetryDelay { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? LastErrorDetail { get; set; }   // expandable technical info — D-08
    public DateTime? LastDataReceived { get; set; } // staleness watchdog

    // Thread-safety lock — all mutation sites must acquire this
    public readonly object SyncLock = new();

    // Change RuleLastFired to concurrent-safe
    public ConcurrentDictionary<string, DateTime> RuleLastFired { get; set; } = new();
}
```

**Mutation pattern:**
```csharp
// In MqttMonitor.OnMessageReceivedAsync and BleMonitor.OnPacketReceived:
lock (_state.SyncLock)
{
    if (bms != null) _state.Bms = bms;
    if (display != null) _state.Display = display;
    if (ems != null) _state.Ems = ems;
    _state.Power = PowerStateMachine.Update(_state.Power, _state);
    _state.LastUpdated = DateTime.Now;
    _state.LastDataReceived = DateTime.Now;
}
StateChanged?.Invoke(this, new StateChangedEventArgs(_state, previousPower));
```

**DeviceViewModel reads:** `UpdateFromState()` is called on the UI thread (inside `Dispatcher.UIThread.Post()`). The UI thread read does not need to acquire the lock provided the lock is released before `StateChanged` is raised — which the pattern above ensures.

### Pattern 5: State Badge Bar (UI)

**What:** Full-width bar inserted in `DashboardView.axaml` between the device name row and the stat cards. Bound to new `DeviceViewModel` properties.

**New DeviceViewModel properties to add:**
```csharp
// Mapping from ConnectionStatus + DeviceState into display strings/values
[ObservableProperty] private string _connectionStateText = "Disconnected";
[ObservableProperty] private string _retryInfoText = "";          // "attempt 3, next in 8s" — D-02
[ObservableProperty] private bool _isStale;                       // true if LastDataReceived > 30s
[ObservableProperty] private string _stalenessText = "";          // "Last update: 2m ago" — D-04
[ObservableProperty] private double _dataOpacity = 1.0;          // 1.0 = fresh, 0.5 = stale — D-04
[ObservableProperty] private string _errorMessage = "";          // "BLE connection failed" — D-07
[ObservableProperty] private string _errorDetail = "";           // expandable — D-08
[ObservableProperty] private bool _hasError;
```

**State bar AXAML insertion point:** Between the hero `Grid` (line 84 of `DashboardView.axaml`) and the `PowerFlowDiagram` control (line 124). The bar sits inside `StackPanel DataContext="{Binding SelectedDevice}"`:

```xml
<!-- STATE BAR — full width, between hero and power flow diagram -->
<Border Background="{StaticResource SurfaceCard}"
        CornerRadius="6" Padding="12,8">
    <Grid ColumnDefinitions="*,Auto">
        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="8">
            <controls:GlowStatusIndicator
                Status="{Binding PowerStatus}"
                Width="8" Height="8" VerticalAlignment="Center" />
            <TextBlock Text="{Binding ConnectionStateText}"
                       Classes="BodySmall" VerticalAlignment="Center" />
            <TextBlock Text="{Binding RetryInfoText}" Classes="BodySmall"
                       Foreground="{StaticResource TextMuted}"
                       IsVisible="{Binding RetryInfoText,
                           Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
            <TextBlock Text="{Binding StalenessText}" Classes="BodySmall"
                       Foreground="{StaticResource TextMuted}"
                       IsVisible="{Binding IsStale}" />
        </StackPanel>
        <!-- Error expandable detail — D-08 -->
        <Button Grid.Column="1" Classes="Ghost" Content="Details"
                Padding="6,2" FontSize="10"
                IsVisible="{Binding HasError}"
                Command="{Binding DataContext.ShowErrorDetailCommand,
                    RelativeSource={RelativeSource AncestorType=UserControl}}" />
    </Grid>
</Border>
```

**Opacity binding for stat cards (D-04):** Wrap each `UniformGrid` of stat cards with `Opacity="{Binding DataOpacity}"`.

### Anti-Patterns to Avoid

- **Raising StateChanged while holding SyncLock:** Lock only covers field mutations; release before raising the event to avoid deadlocks if a handler tries to re-acquire the lock.
- **Firing state machine triggers from multiple threads without synchronization:** `Stateless.StateMachine` is not thread-safe. All trigger fires must occur on the same thread (the monitor's background task) or be protected by the monitor's own lock.
- **Saving config before confirming restart success (existing bug):** `DashboardViewModel.CycleConnectionModeAsync` saves config before `RestartDeviceAsync` returns. Fix: save only after success. This is a CONN-05 fix.
- **Using Logger.Log in any new code:** After INFRA-01 is complete, zero new `Logger.Log()` calls. All logging via `ILogger<T>`.
- **Setting DataContext = this in new controls:** Use `TemplatedControl` base class for any new control with custom `AvaloniaProperty` declarations (known Avalonia pitfall from CONCERNS.md / PITFALLS.md P10).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Connection retry with exponential backoff + jitter | Custom `while(true)` with `Math.Pow()` delay | `Polly.ResiliencePipeline` | DecorrelatedJitterBackoffV2 is a known-correct algorithm; MQTT rate-limit bug is caused by the current hand-rolled fixed delay |
| State machine transitions with entry/exit hooks | `switch (state)` inside the connect loop | `Stateless.StateMachine<TState, TTrigger>` | Entry/exit actions, parameterized triggers, guard clauses — hand-rolling these adds ~150 lines of fragile code |
| Structured log file rotation | Custom `StreamWriter` with size check | `Serilog.Sinks.File` with `rollingInterval` + `fileSizeLimitBytes` | The current `File.AppendAllText` has no rotation at all and blocks notification pipeline |
| Concurrent dictionary for rule cooldowns | `lock` + plain `Dictionary<string, DateTime>` | `ConcurrentDictionary<string, DateTime>` | Already the recommended fix in CONCERNS.md; `lock` on a dictionary inside a `lock` on DeviceState risks nested locking |

**Key insight:** The current codebase has three independent "should use a library" problems that all interact — the logging bottleneck blocks BLE notifications, the retry loop hammers the broker, and the missing state machine makes connection state invisible. These are all infrastructure problems with well-established library solutions.

---

## Common Pitfalls

### Pitfall 1: MQTT Fixed Retry Triggering Broker Rate-Limit (CONFIRMED BUG)
**What goes wrong:** Fixed 5-second delay in `ConnectLoopAsync` and `OnDisconnectedAsync` causes rapid hammering after a network blip. EcoFlow broker rate-limits the client. The fixed interval extends the lockout.
**Why it happens:** `await Task.Delay(5000, ct)` — no jitter, no backoff, no circuit breaker.
**How to avoid:** Polly `ResiliencePipeline` with `BackoffType = DelayBackoffType.Exponential`, `UseJitter = true`, `MaxDelay = TimeSpan.FromMinutes(5)`. Add circuit breaker for MQTT: 3-failure threshold, 30-second break.
**Warning signs:** Log timestamps showing reconnect attempts exactly 5 seconds apart for more than 3 cycles with no successful connection.

### Pitfall 2: StateChanged Raised While Holding SyncLock
**What goes wrong:** If `StateChanged?.Invoke(...)` is called while `lock (_state.SyncLock)` is held, any handler that tries to read `_state` with the same lock (or a nested lock) will deadlock.
**Why it happens:** It's tempting to put the `StateChanged.Invoke` inside the mutation block to guarantee the event fires with the latest state.
**How to avoid:** Always release the lock before raising the event. Snapshot the values that need to be passed to `StateChangedEventArgs` inside the lock, then invoke outside.
**Warning signs:** App freezes with no exception — deadlock between monitor background thread and orchestrator handler.

### Pitfall 3: Stateless State Machine Not Thread-Safe
**What goes wrong:** `StateMachine.Fire(trigger)` from two different threads simultaneously causes undefined state transitions.
**Why it happens:** `BleTransport.OnNotification()` fires on the CoreBluetooth callback thread; the monitor's `ConnectAndAuthAsync` runs on a `Task.Run()` thread. Both might fire triggers.
**How to avoid:** All `_machine.Fire(...)` calls within a single monitor must be serialized — use the monitor's own `_cts`-scoped task context, or add a dedicated `lock (_machineLock)`.
**Warning signs:** `InvalidOperationException: No valid leaving transitions are permitted from state X` appearing intermittently in logs.

### Pitfall 4: ECDH Session Key Not Reset on BLE Reconnect
**What goes wrong:** After BLE disconnect/reconnect, `BleMonitor` may attempt to use the old session key. The EcoFlow device issues a new challenge on every connection. Packets arrive but decrypt to garbage; the protobuf decoder throws (caught by a bare catch) and data silently freezes.
**Why it happens:** `_transport` and `_crypto` survive the reconnect loop iteration; the ECDH handshake is only run during initial connect.
**How to avoid:** On every `ConnectAndAuthAsync` call, construct a fresh `BleTransport(deviceInfo, _adapter, crypto: null)` and a fresh `BleCryptoModern()`. Never reuse instances across reconnects.
**Warning signs:** BLE shows `Streaming` status and `IsConnected = true` but `LastDataReceived` stops advancing after a reconnect.

### Pitfall 5: ILogger<T> Not Available in Core (Before DI Wiring)
**What goes wrong:** `MqttMonitor` and `BleMonitor` are created by `MonitorOrchestrator`, which is a DI singleton. If `ILogger<T>` is not registered before the first monitor is created, `GetRequiredService<MqttMonitor>()` throws.
**Why it happens:** `services.AddLogging(lb => lb.AddSerilog(...))` must be called before `services.AddSingleton<MonitorOrchestrator>()` in `App.axaml.cs`.
**How to avoid:** Register logging first in `OnFrameworkInitializationCompleted()`. Use Serilog's `Log.Logger` as a bootstrap logger in `Program.cs` for pre-DI exceptions.
**Warning signs:** `InvalidOperationException: Unable to resolve service for type ILogger<MqttMonitor>` on startup.

### Pitfall 6: CycleConnectionMode Saves Config Before Confirming Success (EXISTING BUG)
**What goes wrong:** `DashboardViewModel.CycleConnectionModeAsync()` calls `ConfigManager.Save()` before `RestartDeviceAsync()` returns. If restart fails, the broken mode is persisted for next launch.
**Why it happens:** Config saved optimistically for responsiveness; failure path not handled.
**How to avoid:** Await `RestartDeviceAsync()` first; save config only if it returns without exception. If it throws, revert `_device.ConnectionMode` to the previous value.
**Warning signs:** After toggling to BLE mode (unsupported on non-macOS), next app launch tries BLE and fails immediately.

---

## Code Examples

### Serilog Bootstrap in Program.cs
```csharp
// Source: Serilog 4.3.1 docs — https://serilog.net/
// Runs before DI is configured; catches pre-startup exceptions
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File(Logger.DefaultPath.Replace("debug.log", "bootstrap.log"))
    .CreateBootstrapLogger();

// In case of startup crash, this writes before DI is ready
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
```

### Adding ConnectionStatus to StateChangedEventArgs
```csharp
// Source: existing pattern in MqttMonitor.cs — extend, not replace
public class StateChangedEventArgs : EventArgs
{
    public DeviceState State { get; }
    public PowerStatus PreviousPower { get; }
    public ConnectionStatus ConnectionStatus { get; }   // NEW

    public StateChangedEventArgs(DeviceState state, PowerStatus previousPower)
    {
        State = state;
        PreviousPower = previousPower;
        ConnectionStatus = state.ConnectionStatus;      // copied from state
    }
}
```

### Staleness Check in DeviceViewModel
```csharp
// Called from UpdateFromState(), runs on UI thread
private void UpdateStaleness(DeviceState state)
{
    var age = state.LastDataReceived.HasValue
        ? DateTime.Now - state.LastDataReceived.Value
        : TimeSpan.MaxValue;

    // D-05: stale after 30 seconds
    IsStale = age > TimeSpan.FromSeconds(30);
    DataOpacity = IsStale ? 0.5 : 1.0;

    if (IsStale && state.LastDataReceived.HasValue)
    {
        int mins = (int)age.TotalMinutes;
        StalenessText = mins < 1 ? "Last update: <1m ago"
                                 : $"Last update: {mins}m ago";
    }
    else
    {
        StalenessText = "";
    }

    // D-06: clear values after 5+ minutes
    if (age > TimeSpan.FromMinutes(5))
    {
        BatteryPct = 0;
        TotalInW = 0;
        TotalOutW = 0;
        RemainingTime = "--";
        // ... other fields to "--" or 0
    }
}
```

### Polly Circuit Breaker for MQTT
```csharp
// Source: Polly 8.6.6 — https://www.pollydocs.org/strategies/circuit-breaker
// MQTT only — prevents broker rate-limit lockout
_connectPipeline = new ResiliencePipelineBuilder()
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 1.0,
        MinimumThroughput = 3,
        SamplingDuration = TimeSpan.FromSeconds(15),
        BreakDuration = TimeSpan.FromSeconds(30),
        OnOpened = args =>
        {
            _logger.LogWarning("MQTT circuit open — pausing reconnect for 30s");
            return ValueTask.CompletedTask;
        }
    })
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = int.MaxValue,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromMinutes(5),
        UseJitter = true,
        OnRetry = args =>
        {
            _retryAttempt = args.AttemptNumber + 1;
            _machine.Fire(retryTrigger, args.RetryDelay);
            return ValueTask.CompletedTask;
        }
    })
    .Build();
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `File.AppendAllText` per log call | `Serilog.Sinks.File` with async buffering | Serilog 1.0 (2013); pattern is stable | Eliminates synchronous I/O blocking BLE notification pipeline |
| `while(true) { catch; await Task.Delay(5s); }` | `Polly.ResiliencePipeline` with jitter | Polly 8 (2023) | No more broker rate-limit lockout |
| `bool IsConnected` flag | `ConnectionStatus` enum with full FSM | Standard since Stateless 1.0 (2009); v5 (2019) | UI can display retry progress, error messages, countdown timers |
| Lock-free mutable state (race condition) | `lock(SyncLock)` on mutation sites | Pattern is language-basic | Prevents data corruption in Auto mode (INFRA-02) |

**Deprecated/outdated in this codebase:**
- `EcoFlowMonitor.Logging.Logger` static class: Replaced entirely by `ILogger<T>` + Serilog. Delete the file.
- `Logger.Init()` calls in `Program.cs` and `App.axaml.cs`: Replaced by `Log.Logger = new LoggerConfiguration()...`.
- `IsConnected` boolean in `DeviceState`: Superseded by `ConnectionStatus` enum (keep for compatibility during transition, then deprecate).

---

## Open Questions

1. **Microsoft.Extensions.DependencyInjection version upgrade**
   - What we know: App currently uses `8.0.1`. Core uses MEL abstractions (inbox .NET 10).
   - What's unclear: Whether upgrading to `10.0.x` is required for `Serilog.Extensions.Logging 10.0.0` compatibility, or whether `8.0.1` works.
   - Recommendation: Attempt without upgrading first. If build errors appear on `AddSerilog()`, upgrade to `10.0.x`. Low risk.

2. **Stateless thread-safety scope**
   - What we know: Stateless `StateMachine` is documented as not thread-safe.
   - What's unclear: Whether BLE notification callbacks and the connect loop ever fire triggers concurrently within a single monitor in practice on macOS CoreBluetooth (single-threaded callback queue?).
   - Recommendation: Add a `lock (_machineLock)` guard on all `.Fire()` calls regardless — cheap and correct.

3. **Event log (D-09): error accumulation UI**
   - What we know: D-09 says errors accumulate in the same event log as power events.
   - What's unclear: The event log itself doesn't exist yet (DATA-03 is Phase 3). For Phase 1, does D-09 mean Serilog file log, or a new in-memory `ObservableCollection<LogEntry>` in the dashboard?
   - Recommendation: For Phase 1, surface per-device errors only in the state bar (D-07/D-08). The unified event log for D-09 is implemented in Phase 3. Document this scope boundary explicitly.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| .NET 10 SDK | Build all projects | Already in use | net10.0 (confirmed from csproj) | None needed |
| NuGet (nuget.org) | Package restore | Available | — | — |
| Serilog 4.3.1 | INFRA-01 | Not yet installed | — | — |
| Serilog.Sinks.File 7.0.0 | INFRA-01 | Not yet installed | — | — |
| Serilog.Extensions.Logging 10.0.0 | INFRA-01 | Not yet installed | — | — |
| Stateless 5.20.1 | INFRA-04 | Not yet installed | — | — |
| Polly 8.6.6 | INFRA-04 | Not yet installed | — | — |

**Missing dependencies with no fallback:**
- All 5 new packages above must be added before implementation begins. Standard `dotnet add package` workflow.

**Missing dependencies with fallback:**
- None.

---

## Sources

### Primary (HIGH confidence)
- `service/src/EcoFlowMonitor.Core/Logging/Logger.cs` — Current static logger (28 lines, no levels, synchronous AppendAllText)
- `service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs` — Fixed 5s retry loop confirmed at lines 119, 185
- `service/src/EcoFlowMonitor.Core/State/DeviceState.cs` — 16-line mutable class, no synchronization
- `service/src/EcoFlowMonitor.App/ViewModels/DeviceViewModel.cs` — Existing observable properties to extend
- `service/src/EcoFlowMonitor.App/Views/DashboardView.axaml` — Hero section insertion point at line 84-121
- `.planning/codebase/ARCHITECTURE.md` — Threading model, event chain, DI configuration
- `.planning/codebase/CONVENTIONS.md` — All coding style rules enforced by this phase
- `.planning/codebase/CONCERNS.md` — 27 bare catch sites, 99 Logger.Log sites, thread-safety analysis
- `.planning/research/STACK.md` — Package version recommendations (cross-checked against NuGet as of 2026-03-30)
- `.planning/research/ARCHITECTURE.md` — Connection state machine design, Polly patterns
- `.planning/research/PITFALLS.md` — P6 (MQTT rate-limit), P12 (ECDH re-run), P14 (logger blocking), P16 (DeviceState races)

### Secondary (MEDIUM confidence)
- [NuGet Gallery: Serilog 4.3.1](https://www.nuget.org/packages/serilog/) — Published 2026-02-10; latest stable
- [NuGet Gallery: Serilog.Sinks.File 7.0.0](https://www.nuget.org/packages/serilog.sinks.file/) — Latest stable (8.0.0-nblumhardt pre-release exists; use 7.0.0)
- [NuGet Gallery: Serilog.Extensions.Logging 10.0.0](https://www.nuget.org/packages/Serilog.Extensions.Logging) — Confirmed at NuGet
- [NuGet Gallery: Stateless 5.20.1](https://www.nuget.org/packages/Stateless/) — Explicit net10.0 TFM; no dependencies
- [NuGet Gallery: Polly 8.6.6](https://www.nuget.org/packages/Polly) — Published 2026-03-04; latest stable
- [Polly v8 retry strategy docs](https://www.pollydocs.org/strategies/retry.html) — ResiliencePipeline API verified
- [Stateless GitHub — dotnet-state-machine/stateless](https://github.com/dotnet-state-machine/stateless) — Entry/exit actions, parameterized triggers API confirmed

### Tertiary (LOW confidence)
- None — all critical claims verified against source files or NuGet.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — versions verified against NuGet.org as of 2026-03-30; all packages have net10.0 or netstandard2.0 compatibility
- Architecture: HIGH — patterns derived from existing codebase structure; no external assumptions required
- Pitfalls: HIGH — P1 (MQTT rate-limit) confirmed as production bug in CONCERNS.md and STATE.md; P4 (ECDH reset) confirmed in PITFALLS.md and CONCERNS.md

**Research date:** 2026-03-30
**Valid until:** 2026-06-30 (Serilog/Polly/Stateless are stable libraries; EcoFlow protocol is the volatile part, not in scope here)
