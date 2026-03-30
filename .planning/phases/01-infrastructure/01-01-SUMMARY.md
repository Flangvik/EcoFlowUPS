---
phase: 01-infrastructure
plan: 01
subsystem: infra
tags: [serilog, logging, ilogger, dependency-injection, ble, mqtt]

requires:
  - phase: none

provides:
  - Serilog structured logging with 10MB/3-file rolling file rotation
  - ILogger<T> constructor-injected into MqttMonitor, BleMonitor, BleTransport, CoreBluetoothBleAdapter, MonitorOrchestrator
  - BleTransport frame-level hex dumps guarded at LogDebug (invisible at default Information level)
  - Bootstrap logger in Program.cs for pre-DI exception capture
  - Zero Logger.Log() static call sites remaining in service/src/

affects: [01-02, 01-03, 01-04, all phases requiring new monitor classes]

tech-stack:
  added:
    - Serilog 4.3.1
    - Serilog.Sinks.File 7.0.0
    - Serilog.Extensions.Logging 10.0.0
    - Serilog.Extensions.Hosting 10.0.0
    - Stateless 5.20.1
    - Polly 8.6.6
    - Microsoft.Extensions.DependencyInjection upgraded from 8.0.1 to 10.0.0
  patterns:
    - ILogger<T> injected via constructor (never static Logger.Log)
    - ILoggerFactory passed to orchestrator to create child loggers for monitors
    - Log levels: Info for lifecycle events, Debug for hex/byte data, Warning for recoverable errors, Error for exceptions

key-files:
  created: []
  modified:
    - service/src/EcoFlowMonitor.Core/EcoFlowMonitor.Core.csproj
    - service/src/EcoFlowMonitor.App/EcoFlowMonitor.App.csproj
    - service/src/EcoFlowMonitor.App/Program.cs
    - service/src/EcoFlowMonitor.App/App.axaml.cs
    - service/src/EcoFlowMonitor.Core/Logging/Logger.cs
    - service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs
    - service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs
    - service/src/EcoFlowMonitor.Core/Client/Ble/BleTransport.cs
    - service/src/EcoFlowMonitor.App/Services/CoreBluetoothBleAdapter.cs
    - service/src/EcoFlowMonitor.App/Services/MonitorOrchestrator.cs
    - service/src/EcoFlowMonitor.App/Services/PlatformServiceFactory.cs

key-decisions:
  - "Added Serilog.Extensions.Hosting (not in original plan) to provide CreateBootstrapLogger() — Serilog.Extensions.Logging alone does not provide this method"
  - "Upgraded Microsoft.Extensions.DependencyInjection from 8.0.1 to 10.0.0 to resolve transitive package downgrade conflict from Serilog.Extensions.Logging 10.0.0"
  - "ILoggerFactory passed to MonitorOrchestrator to create loggers for transiently-created monitors (BleMonitor, MqttMonitor) rather than injecting the loggers directly"
  - "BleTransport constructor signature changed to require ILogger<BleTransport> — breaking change handled by updating BleMonitor to pass factory-created logger"

patterns-established:
  - "ILogger<T> pattern: all classes that log receive ILogger<T> via constructor, never call static Logger.Log()"
  - "LogDebug for frame-level data: any line with hex dumps, byte arrays, or per-frame data uses LogDebug"
  - "ILoggerFactory delegation: orchestrator classes that create monitors hold ILoggerFactory and call CreateLogger<T>()"

requirements-completed: [INFRA-01, UX-03]

duration: 35min
completed: 2026-03-30
---

# Phase 01 Plan 01: Serilog Structured Logging Migration Summary

**Replaced static Logger singleton with Serilog ILogger<T> injected via DI across all 5 BLE/MQTT classes, with rolling file sinks and LogDebug-guarded hex dumps satisfying UX-03.**

## Performance

- **Duration:** 35 min
- **Started:** 2026-03-30T10:50:00Z
- **Completed:** 2026-03-30T11:25:49Z
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments

- Added Serilog 4.3.1 + Sinks.File + Extensions.Logging + Extensions.Hosting + Stateless + Polly to Core.csproj (one package group for all downstream consumers)
- Wired Serilog bootstrap logger in Program.cs (captures pre-DI exceptions) and full DI-registered logger in App.axaml.cs (10MB/3-file rolling, buffered, structured output template)
- Eliminated all 40+ Logger.Log() static call sites across MqttMonitor, BleMonitor, BleTransport, CoreBluetoothBleAdapter, MonitorOrchestrator, PlatformServiceFactory
- BleTransport frame-level hex dump lines (raw notifications, frame parse, decrypt output) now emit at LogDebug — invisible at default MinimumLevel.Information
- Build succeeds with 0 errors on net10.0 target

## Task Commits

1. **Task 1: Add Serilog packages and wire bootstrap logger** - `0ebfbec` (feat)
2. **Task 2: Replace all Logger.Log() call sites with ILogger<T>** - `d445c2c` (feat)

## Files Created/Modified

- `service/src/EcoFlowMonitor.Core/EcoFlowMonitor.Core.csproj` - Added 6 NuGet packages (Serilog, Sinks.File, Extensions.Logging, Extensions.Hosting, Stateless, Polly)
- `service/src/EcoFlowMonitor.App/EcoFlowMonitor.App.csproj` - Upgraded Microsoft.Extensions.DependencyInjection to 10.0.0
- `service/src/EcoFlowMonitor.App/Program.cs` - Added bootstrap logger with rolling file sink, Log.CloseAndFlush() in finally
- `service/src/EcoFlowMonitor.App/App.axaml.cs` - Configured full Serilog logger, AddSerilog() in DI, removed Logger.Init() call
- `service/src/EcoFlowMonitor.Core/Logging/Logger.cs` - Static class deleted (stub namespace preserved for build compat)
- `service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs` - ILogger<MqttMonitor> constructor injection, MqttNetLogger uses ILogger
- `service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs` - ILogger<BleMonitor> + ILoggerFactory constructor injection
- `service/src/EcoFlowMonitor.Core/Client/Ble/BleTransport.cs` - ILogger<BleTransport> injection, all hex dumps at LogDebug
- `service/src/EcoFlowMonitor.App/Services/CoreBluetoothBleAdapter.cs` - ILogger<CoreBluetoothBleAdapter> injection
- `service/src/EcoFlowMonitor.App/Services/MonitorOrchestrator.cs` - ILoggerFactory injection, creates loggers for monitors
- `service/src/EcoFlowMonitor.App/Services/PlatformServiceFactory.cs` - Removed last Logger.Log() from StubBleAdapter

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Added Serilog.Extensions.Hosting package**
- **Found during:** Task 1 build
- **Issue:** `CreateBootstrapLogger()` is in `Serilog.Extensions.Hosting`, not `Serilog.Extensions.Logging`. The plan specified only `Serilog.Extensions.Logging` but the method does not exist there.
- **Fix:** Added `Serilog.Extensions.Hosting 10.0.0` to Core.csproj alongside the planned packages
- **Files modified:** `service/src/EcoFlowMonitor.Core/EcoFlowMonitor.Core.csproj`
- **Commit:** d445c2c

**2. [Rule 1 - Bug] Upgraded Microsoft.Extensions.DependencyInjection 8.0.1 → 10.0.0**
- **Found during:** Task 1 restore
- **Issue:** `Serilog.Extensions.Logging 10.0.0` depends on `Microsoft.Extensions.Logging 10.0.0` which requires `Microsoft.Extensions.DependencyInjection >= 10.0.0`, causing NU1605 package downgrade error
- **Fix:** Updated the package reference in App.csproj from 8.0.1 to 10.0.0
- **Files modified:** `service/src/EcoFlowMonitor.App/EcoFlowMonitor.App.csproj`
- **Commit:** 0ebfbec

**3. [Rule 3 - Blocking] PlatformServiceFactory StubBleAdapter had Logger.Log() call**
- **Found during:** Task 2 verification
- **Issue:** `StubBleAdapter.StartScanAsync()` called `Logging.Logger.Log()` — this remained after the Logger class was emptied
- **Fix:** Removed the Logger.Log() call (the stub does nothing by design anyway)
- **Files modified:** `service/src/EcoFlowMonitor.App/Services/PlatformServiceFactory.cs`
- **Commit:** d445c2c

## Self-Check: PASSED

Files verified:
- `service/src/EcoFlowMonitor.Core/EcoFlowMonitor.Core.csproj` — contains Serilog references
- `service/src/EcoFlowMonitor.App/Program.cs` — contains CreateBootstrapLogger()
- `service/src/EcoFlowMonitor.App/App.axaml.cs` — contains AddSerilog()
- Zero Logger.Log() call sites confirmed by grep
- Build succeeded with 0 errors on net10.0
