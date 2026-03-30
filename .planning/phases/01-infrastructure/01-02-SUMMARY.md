---
phase: 01-infrastructure
plan: "02"
subsystem: core-thread-safety
tags: [thread-safety, connection-state, error-handling, logging]
dependency_graph:
  requires: [01-01]
  provides: [ConnectionStatus enum, DeviceState.SyncLock, locked mutation sites, CONN-01 contract, CONN-02 contract]
  affects: [MqttMonitor, BleMonitor, ProtobufDecoder, ConfigManager, BleScanner, BleDispatcher, BleProtoMapper, BlePacketParser]
tech_stack:
  added: []
  patterns:
    - lock(_state.SyncLock) wrapping all DeviceState mutations in monitor hot paths
    - StateChanged raised OUTSIDE lock to prevent deadlock
    - ConcurrentDictionary<string,DateTime> for RuleLastFired
    - System.Diagnostics.Debug.WriteLine as non-silent catch in static classes
key_files:
  created:
    - service/src/EcoFlowMonitor.Core/Client/ConnectionStatus.cs
  modified:
    - service/src/EcoFlowMonitor.Core/State/DeviceState.cs
    - service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs
    - service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs
    - service/src/EcoFlowMonitor.Core/Protocol/ProtobufDecoder.cs
    - service/src/EcoFlowMonitor.Core/Config/ConfigManager.cs
    - service/src/EcoFlowMonitor.Core/Client/Ble/BleScanner.cs
    - service/src/EcoFlowMonitor.Core/Protocol/BleDispatcher.cs
    - service/src/EcoFlowMonitor.Core/Protocol/BleProtoMapper.cs
    - service/src/EcoFlowMonitor.Core/Protocol/BlePacketParser.cs
decisions:
  - "BleMonitor migrated from static Logger to ILogger<BleMonitor> + ILoggerFactory due to Plan 01 Logger removal (Rule 3 deviation)"
  - "Static BLE classes (BleScanner, BleDispatcher, BleProtoMapper, BlePacketParser) use Debug.WriteLine as interim — full ILogger migration is Phase 4 concern per CONTEXT.md"
  - "StateChanged raised outside SyncLock to prevent deadlock if event handler reads DeviceState"
  - "ConnectionTrigger enum has Disconnected member (same name as ConnectionStatus.Disconnected — no conflict, different enum types)"
metrics:
  duration_seconds: 602
  completed_date: "2026-03-30"
  tasks_completed: 2
  files_modified: 9
  files_created: 1
---

# Phase 1 Plan 02: Contract Layer and Thread Safety Summary

Thread-safe DeviceState mutation contract established for the connection FSM (Plan 03). ConnectionStatus/ConnectionTrigger enums created, SyncLock added to DeviceState, all monitor hot paths locked, and bare catch blocks in critical paths replaced with annotated catches.

## Tasks Completed

### Task 1: Create ConnectionStatus enums and extend DeviceState

**ConnectionStatus enum (8 states):**
- `Idle` — Pre-start or after explicit stop
- `Scanning` — BLE only: advertising filter active
- `Connecting` — GATT connect / MQTT TLS handshake in progress
- `Authenticating` — BLE ECDH + auth; MQTT credentials verified
- `Streaming` — Data flowing; LastDataReceived advances per packet
- `Retrying` — Waiting for next Polly retry window
- `Error` — Non-retriable error requiring user action
- `Disconnected` — Clean disconnect or pre-first-connect

**ConnectionTrigger enum (9 triggers):** Start, DeviceFound, Connected, Authenticated, DataReceived, RetryScheduled, ErrorOccurred, Disconnected, Stop

**DeviceState extensions:**
- `public readonly object SyncLock = new()` — thread safety anchor
- `ConcurrentDictionary<string, DateTime> RuleLastFired` — was plain Dictionary
- `ConnectionStatus ConnectionStatus` — FSM state (default: Idle)
- `int RetryAttempt`, `TimeSpan RetryDelay` — Polly retry context
- `string? LastErrorMessage`, `string? LastErrorDetail` — error surfacing (D-07, D-08)
- `DateTime? LastDataReceived` — staleness watchdog (D-05, D-06)

**Commit:** de6b0bf

### Task 2: Add mutation locks in monitors and fix bare catch blocks

**MqttMonitor.cs:**
- `OnMessageReceivedAsync`: previousPower snapshot before lock, all Bms/Display/Ems/Power/LastUpdated/LastDataReceived mutations inside `lock(_state.SyncLock)`
- StateChanged raised outside lock
- Bare `catch {}` replaced with `catch (Exception ex)` using `_logger.LogWarning`

**BleMonitor.cs:**
- `OnPacketReceived`: same lock pattern as MqttMonitor, LastDataReceived set in lock
- `ConnectLoopAsync` catch: `lock(_state.SyncLock)` wraps `IsConnected = false` and `ConnectionStatus = ConnectionStatus.Retrying`
- Migrated from static `Logger.Log()` to `ILogger<BleMonitor>` + `ILoggerFactory` (Rule 3 deviation — Plan 01 removed static Logger class)

**ProtobufDecoder.cs:** bare `catch { return false; }` in Dispatch() replaced with `catch (Exception ex)` + `Debug.WriteLine`

**ConfigManager.cs:** bare `catch { return new AppConfig(); }` in Load() replaced with `catch (Exception ex)` + `Debug.WriteLine`

**Commit:** f19a4e4

## SyncLock Usage Pattern Established

```csharp
// Pattern: snapshot before lock, mutate inside, raise event outside
var previousPower = _state.Power.Status;
lock (_state.SyncLock)
{
    if (bms != null) _state.Bms = bms;
    // ... other mutations ...
    _state.LastDataReceived = DateTime.Now;
}
// StateChanged raised OUTSIDE the lock -- prevents deadlock if handler reads _state
StateChanged?.Invoke(this, new StateChangedEventArgs(_state, previousPower));
```

## Bare Catch Sites Fixed

| File | Location | Before | After |
|------|----------|--------|-------|
| MqttMonitor.cs | OnMessageReceivedAsync | `catch { }` swallow | `catch (Exception ex)` + `_logger.LogWarning` |
| ProtobufDecoder.cs | Dispatch() | `catch { return false; }` | `catch (Exception ex)` + `Debug.WriteLine` |
| ConfigManager.cs | Load() | `catch { return new AppConfig(); }` | `catch (Exception ex)` + `Debug.WriteLine` |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] BleMonitor migrated from static Logger to ILogger**
- **Found during:** Task 2
- **Issue:** Plan 01 (parallel agent) replaced Logger.cs with a stub (`// Logger.cs removed — replaced by Serilog ILogger<T>`). BleMonitor and all static BLE classes used `Logger.Log()` which no longer compiled.
- **Fix:** BleMonitor migrated to `ILogger<BleMonitor>` with `ILoggerFactory` injected. Static classes (BleScanner, BleDispatcher, BleProtoMapper, BlePacketParser) use `System.Diagnostics.Debug.WriteLine` as interim — consistent with the plan's approach for ProtobufDecoder. Full static class ILogger migration is explicitly deferred to Phase 4 per CONTEXT.md.
- **Files modified:** BleMonitor.cs (ILogger injection), BleScanner.cs, BleDispatcher.cs, BleProtoMapper.cs, BlePacketParser.cs (Debug.WriteLine replacement)
- **Commit:** f19a4e4

**Impact on callers:** BleMonitor constructor now requires `ILogger<BleMonitor>` and `ILoggerFactory`. MonitorOrchestrator.cs was already updated by Plan 01 to pass these parameters.

## Known Stubs

None — all new fields in DeviceState are correctly typed with appropriate defaults. ConnectionStatus starts at `ConnectionStatus.Idle` which is the correct initial state. No placeholder values flow to UI rendering from this plan's changes.

## Self-Check: PASSED

- ConnectionStatus.cs: FOUND
- DeviceState.cs: FOUND
- SUMMARY.md: FOUND
- Commit de6b0bf: FOUND (Task 1)
- Commit f19a4e4: FOUND (Task 2)
- Core builds: 0 errors, 0 warnings
