# Technology Stack

**Project:** EcoFlow UPS Monitor — Cross-platform BLE Milestone
**Researched:** 2026-03-30
**Scope:** Windows BLE (WinRT), Linux BLE (BlueZ), resilient reconnection, SQLite history, logging replacement

---

## Recommended Stack

### Windows BLE

| Technology | Version | Purpose | Confidence |
|------------|---------|---------|------------|
| `Microsoft.Windows.SDK.Contracts` | 10.0.26100.7705 | WinRT type projections for `Windows.Devices.Bluetooth` | HIGH |
| `Windows.Devices.Bluetooth.GenericAttributeProfile` (inbox) | — | GATT scan, connect, characteristic read/notify | HIGH |

**Why this and not InTheHand.BluetoothLE for Windows:**
`Windows.Devices.Bluetooth` is the native OS API — it is what every other library wraps on Windows. The project's existing macOS adapter uses `CoreBluetooth` directly; the Windows adapter should use the native API in the same pattern rather than adding an abstraction layer on top of an abstraction layer. `Microsoft.Windows.SDK.Contracts` (not a CsWinRT-generated package, just the type projection bindings) makes the full WinRT API surface available to a `net10.0-windows10.0.18362.0` or higher TFM target. No MSIX packaging or `Package.appxmanifest` capability declarations are required — Microsoft explicitly states that capability enforcement does not apply to desktop (non-UWP) apps.

**Critical WinRT BLE nuance:** Creating a `BluetoothLEDevice` object does not open a connection. To hold the connection alive you must either set `GattSession.MaintainConnection = true` or keep a live characteristic subscription. Disposing the object triggers disconnect. This must be reflected in the `IBleGattConnection` implementation.

**What to add to `EcoFlowMonitor.Platform.Windows.csproj`:**
```xml
<TargetFramework>net10.0-windows10.0.18362.0</TargetFramework>
<PackageReference Include="Microsoft.Windows.SDK.Contracts" Version="10.0.26100.7705" />
```

---

### Linux BLE

| Technology | Version | Purpose | Confidence |
|------------|---------|---------|------------|
| `Linux.Bluetooth` | 5.67.1 | BlueZ D-Bus GATT client (scan, connect, notify, disconnect event) | HIGH |
| `Tmds.DBus` | 0.20.0+ | D-Bus transport (pulled transitively by Linux.Bluetooth) | HIGH |

**Why Linux.Bluetooth and not InTheHand.BluetoothLE for Linux:**
`InTheHand.BluetoothLE` uses `Linux.Bluetooth` as its Linux layer internally. Going direct eliminates the indirection and gives full access to BlueZ-specific events (the `Disconnected` event is essential for reconnect — see CONCERNS.md). `Linux.Bluetooth` 5.67.1 is the current stable release (September 2024), targets `.NET Standard 2.0` (compatible with `net10.0`), and its only dependency is `Tmds.DBus >= 0.20.0`. It has been validated against BlueZ v5.50+.

**Key capability alignment with `IBleAdapter`:**
- `adapter.StartDiscoveryAsync()` / `StopDiscoveryAsync()` → maps to existing scan abstraction
- `device.ConnectAsync()` → maps to `IBleGattConnection.ConnectAsync()`
- `characteristic.Value += handler` → maps to `IBleGattConnection.SubscribeAsync()`
- `device.Disconnected` event → **this is the missing piece in the macOS adapter** — wire this up to `IBleGattConnection.Disconnected` on Linux from day one

**What to add to `EcoFlowMonitor.Platform.Linux.csproj`:**
```xml
<PackageReference Include="Linux.Bluetooth" Version="5.67.1" />
```

---

### Cross-Platform BLE — Do NOT Use

| Option | Why Not |
|--------|---------|
| `InTheHand.BluetoothLE` (as unified adapter) | Wraps platform-native libs; adds abstraction without benefit since this project already has `IBleAdapter`. Its `.NET 10.0` (`net10.0`) target does not explicitly list Windows or Linux — only specific `-windows` and platform-specific targets. More importantly, adding it alongside existing `CoreBluetoothBleAdapter` causes dependency confusion and diverges from the native pattern already established. |
| `HashtagChris.DotNetBlueZ` / `Plugin.BlueZ` | `Plugin.BlueZ` is officially deprecated; its maintainer migrated to `Linux.Bluetooth`. Do not use. |
| bleak (Python) | Already have a Python POC in `poc/` as reference — do not port tooling back to Python for production. |

---

### Resilient Reconnection

| Technology | Version | Purpose | Confidence |
|------------|---------|---------|------------|
| `Polly` | 8.6.6 | Exponential backoff + jitter retry for BLE and MQTT reconnect loops | HIGH |

**Why Polly and not a hand-rolled loop:**
Both `ConnectLoopAsync` (MQTT, fixed 5s retry) and `BleMonitor` (no reconnect at all) need to be replaced. Polly v8's `ResiliencePipeline` is protocol-agnostic — it wraps any `async` operation, not just HTTP. The `MaxRetryAttempts = int.MaxValue` + `BackoffType = DelayBackoffType.Exponential` + `UseJitter = true` pattern is exactly the right fit for a long-running background reconnect loop. `MaxDelay` caps runaway wait times.

The MQTT broker rate-limit issue identified in CONCERNS.md is directly caused by the fixed 5s retry. Polly's DecorrelatedJitterBackoffV2 (enabled by `UseJitter = true`) spreads retry pressure across time, which is precisely the countermeasure for broker rate-limiting.

**Pattern for both transports:**
```csharp
var reconnectPipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = int.MaxValue,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromMinutes(5),
        OnRetry = args =>
        {
            // Surface connection state to UI via IConnectionStateReporter
            return ValueTask.CompletedTask;
        }
    })
    .Build();
```

Both `BleMonitor` and `MqttMonitor` keep their own `ResiliencePipeline` instance. The `OnRetry` callback is where UI connection state (`Scanning`, `Connecting`, `Retrying`, `Error`) gets updated — this satisfies the "Connection state feedback in UI" requirement.

**What to add to `EcoFlowMonitor.Core.csproj`:**
```xml
<PackageReference Include="Polly" Version="8.6.6" />
```

---

### SQLite History Persistence

| Technology | Version | Purpose | Confidence |
|------------|---------|---------|------------|
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.5 | SQLite persistence for telemetry history | HIGH |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.5 | EF Core migrations (dev-only) | HIGH |

**Why EF Core and not sqlite-net-pcl or raw Microsoft.Data.Sqlite:**

- **EF Core 10.0.5** is the native match for `net10.0`. It ships the same minor version as the runtime. The SQLite provider is 29 KB — this is not a heavy dependency.
- **History schema evolution matters.** Telemetry schemas change (new fields, different aggregation intervals). EF Core migrations give a versioned, testable upgrade path. Raw ADO.NET or `sqlite-net-pcl` requires hand-writing migration scripts.
- **LINQ queries for history charts.** The hourly/daily/weekly chart feature requires time-bucketing queries. EF Core with LINQ keeps these readable and refactorable. Raw SQL with `Microsoft.Data.Sqlite` is viable but produces harder-to-maintain string queries.
- **sqlite-net-pcl** (1.9.172, last stable March 2024) is excellent for simple key-value or single-table persistence. It has no migrations, no query composition. For a simple settings table it would be fine — for a multi-interval telemetry store it becomes friction.

**What NOT to do:** Do not enable EF Core change-tracking for every telemetry row insert. Use `context.Database.ExecuteSqlRaw` or batched `AddRange` + `SaveChanges` for bulk inserts. Change tracking on high-frequency time-series rows is the primary EF Core performance trap.

**WAL mode is mandatory.** The UI reads history while the monitor writes it. Without WAL mode, readers block writers. Enable it once at database open:
```csharp
connection.Execute("PRAGMA journal_mode=WAL;");
```
Or via `optionsBuilder.UseSqlite("...", opts => opts.CommandTimeout(30))` with a custom `DbConnection` that sets WAL on open.

**Schema approach:** One table per telemetry grain (1-minute raw samples, hourly aggregates). Raw samples age out after 30 days via a periodic `DELETE WHERE timestamp < now - 30d`. Keep aggregates indefinitely.

**What to add to the new `EcoFlowMonitor.History` project (or Core):**
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.5" />
```

---

### Logging Replacement

| Technology | Version | Purpose | Confidence |
|------------|---------|---------|------------|
| `Microsoft.Extensions.Logging` | (inbox in .NET 10) | Structured log API — already wired via DI container | HIGH |
| `Serilog` | 4.x (latest 4.2+) | Log sink router with file + console sinks | MEDIUM |
| `Serilog.Sinks.File` | 6.x | Rolling file sink with size limits and retention | MEDIUM |
| `Serilog.Extensions.Logging` | 10.0.0 | Bridge: routes `ILogger<T>` calls into Serilog | HIGH |

**Why replace the custom Logger with Serilog:**
The current `Logger.cs` is a 28-line static class with synchronous `File.AppendAllText` under a global lock — no severity levels, no rotation, no async writes. CONCERNS.md identifies this as the root of the performance bottleneck (serialized disk I/O blocking BLE notification handlers) and the source of uncontrollable log growth.

`Microsoft.Extensions.Logging` is already used by the DI container in `EcoFlowMonitor.App`. The change is: remove the `Logger` static class, inject `ILogger<T>` everywhere, configure Serilog as the provider in `Program.cs`. `Serilog.Sinks.File` has rolling and retention built in, writes async-buffered by default, and respects minimum level filtering at runtime.

**Why not NLog or log4net:** Serilog is the current ecosystem standard for .NET structured logging and has first-class `Microsoft.Extensions.Logging` integration. NLog and log4net are viable but require more configuration for the same outcome.

**Note on confidence:** MEDIUM confidence for Serilog specifically because the actual version compatibility with `net10.0` was verified via NuGet (Serilog.Extensions.Logging 10.0.0 listed explicitly), but the author did not deep-verify whether 4.x Serilog itself has a `net10.0` TFM. `netstandard2.0` compatibility means it will work — it just will not use .NET 10-specific APIs. This is acceptable.

---

## Alternatives Considered and Rejected

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Windows BLE | `Microsoft.Windows.SDK.Contracts` + WinRT | `InTheHand.BluetoothLE` | Extra abstraction layer over the same WinRT APIs; project already abstracts via `IBleAdapter` |
| Linux BLE | `Linux.Bluetooth` 5.67.1 | `InTheHand.BluetoothLE` (Linux path) | InTheHand uses Linux.Bluetooth internally; direct is simpler |
| Linux BLE | `Linux.Bluetooth` 5.67.1 | `Plugin.BlueZ` | Officially deprecated, maintainer migrated to Linux.Bluetooth |
| Reconnect | Polly 8.6.6 | Hand-rolled loop | Already in codebase and demonstrably wrong (fixed 5s, no jitter, no cap) |
| SQLite ORM | EF Core 10.0.5 | `sqlite-net-pcl` | No migrations, no LINQ composition — inadequate for multi-grain telemetry |
| SQLite ORM | EF Core 10.0.5 | Raw `Microsoft.Data.Sqlite` ADO.NET | More boilerplate for migrations and time-bucket queries |
| Logging | Serilog + MEL bridge | Custom `Logger.cs` | Static class, synchronous file I/O, no levels — already identified as performance bottleneck |
| Logging | Serilog + MEL bridge | NLog | Equivalent capability; Serilog has better MEL integration and is more commonly used in current .NET ecosystem |

---

## Full Dependency Delta

These are the new packages to add; they do not replace existing packages except for the `Logger` class (which is deleted in source).

```xml
<!-- EcoFlowMonitor.Platform.Windows.csproj (new / updated TFM) -->
<TargetFramework>net10.0-windows10.0.18362.0</TargetFramework>
<PackageReference Include="Microsoft.Windows.SDK.Contracts" Version="10.0.26100.7705" />

<!-- EcoFlowMonitor.Platform.Linux.csproj -->
<PackageReference Include="Linux.Bluetooth" Version="5.67.1" />

<!-- EcoFlowMonitor.Core.csproj -->
<PackageReference Include="Polly" Version="8.6.6" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.5" />
<PackageReference Include="Serilog" Version="4.2.0" />
<PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
<PackageReference Include="Serilog.Extensions.Logging" Version="10.0.0" />

<!-- EcoFlowMonitor.App.csproj (dev tooling, not shipped) -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.5">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

---

## Fit with Existing Architecture

The `IBleAdapter` / `IBleGattConnection` interfaces already exist. Each new platform adapter implements them:

| Platform | New Adapter Class | Package Used |
|----------|-------------------|--------------|
| Windows | `WinRTBleAdapter` (new) | `Windows.Devices.Bluetooth` via `Microsoft.Windows.SDK.Contracts` |
| Linux | `BlueZBleAdapter` (new) | `Linux.Bluetooth` 5.67.1 |
| macOS | `CoreBluetoothBleAdapter` (existing) | CoreBluetooth via `net10.0-macos` |

`PlatformServiceFactory.cs` currently uses reflection to load platform assemblies. The recommended fix (noted in CONCERNS.md) is to switch to `#if` conditional compilation. For BLE adapters, the cleanest approach for the milestone is to add the new adapters to their respective platform projects and register them in each platform's startup, consistent with how notifications and power actions are already registered.

Polly's `ResiliencePipeline` lives in `EcoFlowMonitor.Core` — neither `BleMonitor` nor `MqttMonitor` should reference platform projects. The reconnect policy is pure transport logic.

EF Core history persistence should live in a new `EcoFlowMonitor.Storage` project (or as a namespace within `Core`) to keep data access separate from protocol and state logic.

---

## Sources

- [Windows.Devices.Bluetooth Namespace — Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth?view=winrt-26100)
- [Bluetooth GATT Client — UWP applications (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client)
- [Bluetooth developer FAQ — UWP applications (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/bluetooth-dev-faq) — confirms no manifest capability required for desktop apps
- [Microsoft.Windows.SDK.Contracts on NuGet](https://www.nuget.org/packages/Microsoft.Windows.SDK.Contracts) — version 10.0.26100.7705
- [SuessLabs/Linux.Bluetooth on GitHub](https://github.com/SuessLabs/Linux.Bluetooth)
- [Linux.Bluetooth 5.67.1 on NuGet](https://www.nuget.org/packages/Linux.Bluetooth/)
- [Polly Retry Strategy Documentation](https://www.pollydocs.org/strategies/retry.html)
- [Polly 8.6.6 on NuGet](https://www.nuget.org/packages/polly/)
- [Microsoft.EntityFrameworkCore.Sqlite 10.0.5 on NuGet](https://www.nuget.org/packages/microsoft.entityframeworkcore.sqlite)
- [Overview — Microsoft.Data.Sqlite (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)
- [sqlite-net-pcl 1.9.172 on NuGet](https://www.nuget.org/packages/sqlite-net-pcl/)
- [Serilog.Extensions.Logging 10.0.0 on NuGet](https://www.nuget.org/packages/Serilog.Extensions.Logging)
- [InTheHand.BluetoothLE 4.0.44 on NuGet](https://www.nuget.org/packages/InTheHand.BluetoothLE)
- [32feet.NET on GitHub](https://github.com/inthehand/32feet)
