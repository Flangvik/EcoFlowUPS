---
phase: 01-infrastructure
plan: 04
subsystem: UI / ViewModels
tags: [connection-state, staleness, state-bar, avalonia, mvvm, ux]
dependency_graph:
  requires:
    - 01-02  # DeviceState connection fields (ConnectionStatus, RetryAttempt, RetryDelay, LastErrorMessage, LastErrorDetail, LastDataReceived)
    - 01-03  # Stateless FSM writing to ConnectionStatus in BleMonitor + MqttMonitor
  provides:
    - ConnectionStateText observable property on DeviceViewModel
    - RetryInfoText observable property on DeviceViewModel
    - IsStale / StalenessText / DataOpacity observable properties
    - ErrorMessage / ErrorDetail / HasError observable properties
    - State badge bar AXAML in DashboardView (between hero and power flow)
    - ShowErrorDetailCommand on DashboardViewModel
  affects:
    - DashboardView.axaml (layout change: state bar insertion, stat card opacity wrapper)
    - DeviceViewModel (8 new observable properties + 3 new methods)
    - DashboardViewModel (1 new relay command)
tech_stack:
  added: []
  patterns:
    - DispatcherTimer for periodic staleness check (10s interval, UI thread guaranteed)
    - ConnectionStatus enum switch expression for display string mapping
    - DataOpacity double binding on Grid wrapper for dimming stat cards
    - IsActive bool binding on GlowStatusIndicator (not Status)
key_files:
  created: []
  modified:
    - service/src/EcoFlowMonitor.App/ViewModels/DeviceViewModel.cs
    - service/src/EcoFlowMonitor.App/Views/DashboardView.axaml
    - service/src/EcoFlowMonitor.App/ViewModels/DashboardViewModel.cs
decisions:
  - GlowStatusIndicator bound to IsActive=IsConnected (bool) not Status=PowerStatus — control has no Status property
  - All four stat card UniformGrids wrapped in a single Grid with DataOpacity for uniform dimming
  - DispatcherTimer started in DeviceViewModel constructor (no explicit stop — ties lifetime to VM)
metrics:
  duration_seconds: 380
  completed_date: "2026-03-30"
  tasks_completed: 3
  files_modified: 3
---

# Phase 1 Plan 04: Connection State Bar UI Summary

**One-liner:** State badge bar with connection FSM display, staleness dimming, and error reveal wired to DeviceState FSM fields via 8 new observable properties and a DispatcherTimer.

## What Was Built

### Task 1: DeviceViewModel observable properties + staleness timer

**8 new observable properties added to DeviceViewModel:**

| Property | Type | Purpose |
|---|---|---|
| `ConnectionStateText` | `string` | Human-readable FSM state ("Connected", "Scanning...", etc.) |
| `RetryInfoText` | `string` | "(attempt 3, next in 8s)" — D-02, empty when not retrying |
| `IsStale` | `bool` | True when no data for 30+ seconds while disconnected |
| `StalenessText` | `string` | "Last update: 2m ago" — D-04 |
| `DataOpacity` | `double` | 1.0 fresh, 0.5 stale — drives stat card dimming D-04 |
| `ErrorMessage` | `string` | Friendly error from DeviceState.LastErrorMessage — D-07 |
| `ErrorDetail` | `string` | Expandable technical detail from DeviceState.LastErrorDetail — D-08 |
| `HasError` | `bool` | True when ConnectionStatus=Error and LastErrorMessage non-empty |

**`UpdateConnectionState(DeviceState)` method** — called at the start of `UpdateFromState()`, maps `ConnectionStatus` enum values to display strings via switch expression and populates retry/error properties.

**`UpdateStaleness()` method** — fired by DispatcherTimer every 10 seconds:
- No-ops when `IsConnected = true` (connected and fresh)
- Reads `_lastKnownDataReceived` (private field updated on each `UpdateFromState()` call)
- D-05: marks `IsStale = true` at 30 seconds offline
- D-04: sets `DataOpacity = 0.5` when stale
- D-06: calls `ClearStaleValues()` after 5 minutes offline

**`ClearStaleValues()` method** — zeros BatteryPct, TotalInW, TotalOutW, SolarW, VoltageV, CurrentA, TempC; resets RemainingTime to "--".

**Staleness timer configuration:**
- Interval: 10 seconds
- Thread: UI thread (DispatcherTimer guarantees)
- Lifecycle: Started in constructor, no explicit stop (lifetime tied to DeviceViewModel)

### Task 2: DashboardView.axaml state badge bar + DashboardViewModel command

**State badge bar** inserted between the hero Grid (`</Grid>` at line ~122) and `<controls:PowerFlowDiagram>` using `Background="{StaticResource SurfaceCard}"` border, `Margin="0,4,0,4"`.

**Bindings in state bar:**
- `GlowStatusIndicator`: `IsActive="{Binding IsConnected}"` — shows green dot when streaming
- `ConnectionStateText`: primary state label
- `RetryInfoText`: visible only when non-empty (`StringConverters.IsNotNullOrEmpty`)
- `StalenessText`: visible only when `IsStale=true`
- Details button: visible only when `HasError=true`, fires `ShowErrorDetailCommand` via RelativeSource

**Opacity wrapper for stat cards:** All four stat card `UniformGrid` sections wrapped in `<Grid Opacity="{Binding DataOpacity}">` (D-04). The state bar is NOT inside the wrapper so it stays full opacity when data is stale.

**`ShowErrorDetailCommand`** on DashboardViewModel — when `SelectedDevice.HasError`, copies `ErrorDetail` to `StatusText` for display. (Phase 4 will add a proper dialog.)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] GlowStatusIndicator has no `Status` property**
- **Found during:** Task 2, AXAML build
- **Issue:** Plan specified `Status="{Binding PowerStatus}"` on `GlowStatusIndicator`, but the control only exposes `IsActive` (bool) and `ActiveColor` (Color). The `Status` binding caused `Avalonia error AVLN2000: Unable to resolve suitable regular or attached property Status`.
- **Fix:** Changed binding to `IsActive="{Binding IsConnected}"` — shows green pulsing dot when device is streaming, gray dot when disconnected. Correct semantics for the state bar.
- **Files modified:** `service/src/EcoFlowMonitor.App/Views/DashboardView.axaml`
- **Commit:** 7d3f2a2

## Task 3: Checkpoint (Auto-approved)

Task 3 was a `checkpoint:human-verify` gate requiring visual confirmation of the state bar in the running app. Auto-advanced per `auto_advance: true` project configuration.

**What to verify when running the app:**
1. State badge bar appears between battery gauge section and power flow diagram
2. State bar shows "Disconnected" or "Scanning..." when no device connected
3. State bar shows "Connected" when device data is streaming
4. Retry state shows "(attempt N, next in Xs)" in muted text
5. After 30s without data: stat cards dim to ~50% opacity, "Last update: Xs ago" appears
6. After 5 minutes offline: stat card numeric values clear to 0 / "--"
7. Error state: "Error" in state bar + "Details" button visible

## Known Stubs

None. All properties are wired to live DeviceState fields. The `ShowErrorDetailCommand` uses `StatusText` as temporary display (Phase 4 adds a dialog) — this is an intentional placeholder documented in the command itself.

## Self-Check: PASSED

- SUMMARY.md: FOUND
- DeviceViewModel.cs: FOUND
- DashboardView.axaml: FOUND
- DashboardViewModel.cs: FOUND
- Commit 5cec359 (Task 1): FOUND
- Commit 7d3f2a2 (Task 2): FOUND
