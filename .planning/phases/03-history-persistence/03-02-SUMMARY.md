---
phase: 03-history-persistence
plan: 02
subsystem: history-persistence
tags: [sqlite, di, orchestrator, telemetry, power-events]
dependency_graph:
  requires: [03-01]
  provides: [MonitorOrchestrator history wiring, IHistoryStore DI registration, IEventStore DI registration]
  affects: [03-03]
tech_stack:
  added: []
  patterns: [IHistoryStore/IEventStore singletons via DI, async void lifecycle method, fire-and-forget Channel writes]
key_files:
  created: []
  modified:
    - service/src/EcoFlowMonitor.App/App.axaml.cs
    - service/src/EcoFlowMonitor.App/Services/MonitorOrchestrator.cs
    - service/src/EcoFlowMonitor.Core/History/SqliteHistoryStore.cs
    - service/src/EcoFlowMonitor.Core/History/SqliteEventStore.cs
decisions:
  - "DeriveEventType maps actual PowerStatus enum values (Charging/Idle/PowerLost) not the plan-specified OnBattery/Connected which do not exist"
  - "async void OnFrameworkInitializationCompleted allowed by Avalonia lifecycle contract — await historyStore/eventStore.StartAsync before window creation"
  - "Logger.Log replaced with Debug.WriteLine in SqliteHistoryStore/SqliteEventStore — Logger class was removed in Serilog migration"
metrics:
  duration_seconds: 540
  completed_date: "2026-03-30"
  tasks_completed: 2
  files_created: 0
  files_modified: 4
---

# Phase 03 Plan 02: History Store Wiring Summary

Wire SqliteHistoryStore and SqliteEventStore into the running application: registered as DI singletons, started before monitoring begins, and called non-blocking on every state change and power transition.

## What Was Built

Two files modified — no new files created:

- **`App.axaml.cs`**: DI registrations for `IHistoryStore` (SqliteHistoryStore) and `IEventStore` (SqliteEventStore) as singletons, both keyed to `ApplicationData/EcoFlowMonitor/history.db`. `OnFrameworkInitializationCompleted` changed to `async void` to `await` both `StartAsync()` calls before the main window is created.

- **`MonitorOrchestrator.cs`**: Constructor extended with `IHistoryStore historyStore` and `IEventStore eventStore` parameters (DI resolves automatically). `OnStateChanged` now: (1) hoists `var source` to top of method; (2) builds and enqueues a `TelemetrySnapshot` on every tick; (3) conditionally enqueues a `PowerEvent` when power status transitions. New `DeriveEventType` helper maps `(PowerStatus prev, PowerStatus cur)` transitions to event name strings. No `await` on either enqueue — writes are fire-and-forget via the Channel<T> queues inside the stores.

## Tasks Completed

| Task | Description | Commit |
|------|-------------|--------|
| 1 | Register IHistoryStore/IEventStore in DI, start before monitoring | 0b35375 |
| 2 | Wire EnqueueSnapshot and EnqueueEvent in MonitorOrchestrator | 7f3f554 |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Logger.Log calls in SqliteHistoryStore and SqliteEventStore**
- **Found during:** Task 1 — build failed with CS0103 "The name 'Logger' does not exist"
- **Issue:** Plan 01 wrote `Logger.Log(...)` calls in the history stores, but `Logger` was removed in the Serilog migration (Phase 01 infrastructure). The `Logger.cs` file in Core now only contains a namespace stub comment.
- **Fix:** Replaced `using EcoFlowMonitor.Logging;` with `using System.Diagnostics;` and replaced all `Logger.Log(...)` calls with `Debug.WriteLine(...)` in both `SqliteHistoryStore.cs` and `SqliteEventStore.cs`.
- **Files modified:** `SqliteHistoryStore.cs`, `SqliteEventStore.cs`
- **Commit:** 0b35375

**2. [Rule 1 - Bug] DeriveEventType used non-existent PowerStatus enum values**
- **Found during:** Task 2 — plan specified `PowerStatus.OnBattery` and `PowerStatus.Connected` which do not exist
- **Issue:** The plan's `DeriveEventType` switch used `PowerStatus.OnBattery` and `PowerStatus.Connected` but the actual `PowerStatus` enum only has: `Unknown`, `Idle`, `Charging`, `PowerLost`.
- **Fix:** Rewrote `DeriveEventType` to map actual enum values: `(Charging|Idle → PowerLost)` → "PowerLost"; `(PowerLost → Charging|Idle)` → "PowerRestored".
- **Files modified:** `MonitorOrchestrator.cs`
- **Commit:** 7f3f554

## Known Stubs

None — all telemetry and event writes are wired to real SQLite stores. Every `StateChanged` tick now produces a `TelemetrySnapshot` record in the database.

## Self-Check: PASSED

Verified files exist:
- FOUND: service/src/EcoFlowMonitor.App/App.axaml.cs
- FOUND: service/src/EcoFlowMonitor.App/Services/MonitorOrchestrator.cs

Verified commits:
- FOUND: 0b35375
- FOUND: 7f3f554

Build: 0 errors, 39 pre-existing warnings (IL2026/IL2072 from reflection-based platform service loading, CS0067 unused events).
