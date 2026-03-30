---
phase: 01-infrastructure
plan: "03"
subsystem: connection-fsm
tags: [stateless, polly, fsm, mqtt, ble, resilience, conn-05]
dependency_graph:
  requires: [01-02]
  provides: [connection-state-machine, polly-retry, conn05-fix]
  affects: [DeviceState, BleMonitor, MqttMonitor, DashboardViewModel]
tech_stack:
  added: [Stateless 5.20.1 FSM, Polly 8.6.6 ResiliencePipeline, Polly CircuitBreaker]
  patterns: [state-machine, exponential-backoff, circuit-breaker, disconnect-tcs-signaling]
key_files:
  created: []
  modified:
    - service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs
    - service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs
    - service/src/EcoFlowMonitor.App/ViewModels/DashboardViewModel.cs
decisions:
  - "BLE Polly: exponential backoff without circuit breaker (BLE does not rate-limit)"
  - "MQTT Polly: circuit breaker (3 failures / 30s break) before retry to prevent EcoFlow broker lockout"
  - "OnDisconnectedAsync signals Polly via TCS.TrySetException() so Polly retries rather than treating as cancellation"
  - "ILogger<DashboardViewModel> added to constructor (Plan 01 already wired Serilog; DI resolves it automatically)"
  - "Task.Delay(Infinite, token) pattern replaced with TaskCompletionSource<bool> (_disconnectTcs) for cleaner Polly integration"
metrics:
  duration_seconds: 427
  completed_date: "2026-03-30"
  tasks_completed: 2
  files_modified: 3
---

# Phase 01 Plan 03: Connection FSM + Polly Resilience Summary

Replaced raw while-loop retry patterns in BleMonitor and MqttMonitor with Stateless 5.20.1 state machines backed by Polly 8.6.6 ResiliencePipeline. Fixed CONN-05 bug in DashboardViewModel where config was saved before confirming restart success.

## FSM State Diagram

### BleMonitor (8 states)

```
Idle
  --[Start]--> Scanning
    --[DeviceFound]--> Connecting
      --[Connected]--> Authenticating
        --[Authenticated]--> Streaming
          --[Disconnected]--> Retrying
Connecting/Authenticating --[RetryScheduled]--> Retrying
  --[Start (Polly next attempt)]--> Connecting
Any state --[Stop]--> Idle
Any state --[ErrorOccurred]--> Error
  --[Start (UI manual retry)]--> Connecting
Disconnected --[Start]--> Scanning
```

### MqttMonitor (7 states, no Scanning)

```
Idle
  --[Start]--> Connecting
    --[Connected]--> Authenticating
      --[Authenticated (first message received)]--> Streaming
        --[Disconnected]--> Retrying
Connecting/Authenticating --[RetryScheduled]--> Retrying
  --[Start (Polly next attempt)]--> Connecting
Any state --[Stop]--> Idle
Any state --[ErrorOccurred]--> Error
  --[Start (UI manual retry)]--> Connecting
Disconnected --[Start]--> Connecting
```

## Polly Pipeline Configuration

### BleMonitor
- No circuit breaker (BLE does not have broker rate-limiting)
- Retry: exponential backoff, initial delay 2s, max delay 5 min, jitter enabled, infinite attempts
- OnRetry: fires `RetryScheduled` parameterized trigger with `args.RetryDelay`

### MqttMonitor
- Circuit breaker BEFORE retry: `FailureRatio=1.0`, `MinimumThroughput=3`, `BreakDuration=30s`, `SamplingDuration=30s`
- Retry: same as BLE (exponential backoff, jitter, infinite)
- Circuit breaker `OnOpened` logs warning at LogLevel.Warning for operator visibility

## Disconnect-to-Retry Signaling (MqttMonitor)

`OnDisconnectedAsync` cannot simply cancel the CTS (that would make Polly see `OperationCanceledException` and exit cleanly). Instead:

1. `ConnectLoopAsync` creates `_disconnectTcs = new TaskCompletionSource<bool>()` per attempt
2. After `ConnectAsync` succeeds, awaits `_disconnectTcs.Task`
3. `OnDisconnectedAsync` calls `_disconnectTcs.TrySetException(new InvalidOperationException("MQTT disconnected: {reason}"))`
4. Polly sees a regular exception — triggers retry (not cancellation)
5. CTS cancellation (from `StopAsync`) registers via `token.Register(() => _disconnectTcs.TrySetCanceled())` — this makes Polly see `OperationCanceledException` and exit cleanly

## CONN-05 Fix

**Bug:** `CycleConnectionModeAsync` in DashboardViewModel saved config before confirming restart succeeded.

**Fix:**
```csharp
var previousMode = SelectedDevice.Config.ConnectionMode; // snapshot for rollback
SelectedDevice.CycleConnectionMode();
SelectedDevice.StatusText = $"Switching to {SelectedDevice.ConnectionBadge}...";
try
{
    await _orchestrator.RestartDeviceAsync(SelectedDevice.Config);
    ConfigManager.Save(_config); // only save if restart succeeded (CONN-05)
}
catch (Exception ex)
{
    SelectedDevice.Config.ConnectionMode = previousMode;
    SelectedDevice.UpdateBadge();
    SelectedDevice.StatusText = $"Switch failed: {ex.Message}";
    _logger.LogWarning(ex, "CycleConnectionMode failed, reverted to {Mode}", previousMode);
}
```

`ILogger<DashboardViewModel>` added to constructor — resolved automatically by DI since `AddLogging(...)` was wired in Plan 01.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] DeviceViewModel.UpdateConnectionBadge() does not exist**
- **Found during:** Task 2
- **Issue:** Plan referenced `SelectedDevice.UpdateConnectionBadge()` but DeviceViewModel only exposes `UpdateBadge()`
- **Fix:** Changed call to `SelectedDevice.UpdateBadge()` to match existing API
- **Files modified:** `DashboardViewModel.cs`
- **Commit:** 4130359

**2. [Rule 3 - Blocking] Worktree branch missing Plan 01-01 and 01-02 changes**
- **Found during:** Plan start
- **Issue:** Worktree branch `worktree-agent-a8bc7093` was at `21647bb` (pre-planning commits); Plan 01-02 changes (ConnectionStatus.cs, DeviceState extensions) were on `main` at `7ae0f6e`
- **Fix:** `git merge main` (fast-forward) — brought all Plan 01-01/01-02 changes into worktree
- **Files modified:** 47 files (merge brought in .planning/, CLAUDE.md, all source updates)
- **Commit:** fast-forward merge (no separate commit)

## Known Stubs

None — all state transitions are wired to real code paths.

## Self-Check: PASSED

- BleMonitor.cs: FOUND
- MqttMonitor.cs: FOUND
- DashboardViewModel.cs: FOUND
- 01-03-SUMMARY.md: FOUND
- Task 1 commit ac6d48b: FOUND
- Task 2 commit 4130359: FOUND
