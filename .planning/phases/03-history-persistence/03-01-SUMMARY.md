---
phase: 03-history-persistence
plan: 01
subsystem: history-persistence
tags: [sqlite, channel, wal, persistence, history]
dependency_graph:
  requires: []
  provides: [IHistoryStore, IEventStore, SqliteHistoryStore, SqliteEventStore]
  affects: [03-02, 03-03]
tech_stack:
  added: [Microsoft.Data.Sqlite 10.0.5]
  patterns: [Channel<T> debounce write queue, WAL mode SQLite, batch transaction flush]
key_files:
  created:
    - service/src/EcoFlowMonitor.Core/History/Resolution.cs
    - service/src/EcoFlowMonitor.Core/History/TelemetrySnapshot.cs
    - service/src/EcoFlowMonitor.Core/History/PowerEvent.cs
    - service/src/EcoFlowMonitor.Core/History/PowerEventItem.cs
    - service/src/EcoFlowMonitor.Core/History/IHistoryStore.cs
    - service/src/EcoFlowMonitor.Core/History/IEventStore.cs
    - service/src/EcoFlowMonitor.Core/History/SqliteHistoryStore.cs
    - service/src/EcoFlowMonitor.Core/History/SqliteEventStore.cs
  modified:
    - service/src/EcoFlowMonitor.Core/EcoFlowMonitor.Core.csproj
decisions:
  - "Used static Logger.Log (project convention) instead of ILogger<T> — Core project has no Microsoft.Extensions.Logging reference and all Core logging uses the static Logger class"
metrics:
  duration_seconds: 356
  completed_date: "2026-03-30"
  tasks_completed: 2
  files_created: 8
  files_modified: 1
---

# Phase 03 Plan 01: SQLite History Persistence Layer Summary

SQLite persistence layer for EcoFlowMonitor with WAL-mode stores, Channel<T> debounce write queues, and domain contracts for telemetry snapshots and power events.

## What Was Built

Two SQLite stores behind clean interfaces, consumed by the rest of Phase 3:

- **`IHistoryStore`** / **`SqliteHistoryStore`**: Writes telemetry snapshots (battery %, power in/out, temp, remain minutes) via a `Channel<TelemetrySnapshot>` (capacity 500, DropOldest). Batch-flushes to SQLite with `INSERT OR IGNORE` in a single transaction per drain. Supports Raw / Hourly / Daily / Weekly query aggregations using `strftime GROUP BY`. Prune API deletes rows older than a retention period.

- **`IEventStore`** / **`SqliteEventStore`**: Writes power events (PowerLost, PowerRestored, BatteryLow, ConnectionChanged) via a `Channel<PowerEvent>` (capacity 200, DropOldest). Batch-flushes `INSERT INTO power_events` in a single transaction. QueryAsync returns `PowerEventItem` list with `TimeLabel`/`DayLabel` display properties.

- **WAL mode**: Every connection open calls `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;` via the shared `ConfigureConnectionAsync` helper. Read queries use `Mode=ReadOnly` connection strings.

- **DTOs**: `TelemetrySnapshot`, `PowerEvent`, `PowerEventItem`, `Resolution` enum — all file-scoped `namespace EcoFlowMonitor.History;`.

## Tasks Completed

| Task | Description | Commit |
|------|-------------|--------|
| 1 | Add Microsoft.Data.Sqlite 10.0.5 + domain contracts (6 files) | 8f42b63 |
| 2 | Implement SqliteHistoryStore and SqliteEventStore | 53cb070 |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Replaced ILogger<T> with static Logger.Log**
- **Found during:** Task 2 - build failure
- **Issue:** Plan specified `ILogger<SqliteHistoryStore>` constructor parameter, but `EcoFlowMonitor.Core` has no reference to `Microsoft.Extensions.Logging.Abstractions` and no transitive path to it. All Core logging uses the static `EcoFlowMonitor.Logging.Logger` class.
- **Fix:** Removed `ILogger<T>` constructor parameter from both stores; replaced `_logger.LogWarning/LogError` calls with `Logger.Log(...)`. Constructors now accept only `string dbPath`.
- **Files modified:** `SqliteHistoryStore.cs`, `SqliteEventStore.cs`
- **Commit:** 53cb070

## Known Stubs

None — all data flows are wired to SQLite. The stores are ready to receive callers from Plans 02 and 03.

## Self-Check: PASSED

Verified files exist:
- FOUND: service/src/EcoFlowMonitor.Core/History/IHistoryStore.cs
- FOUND: service/src/EcoFlowMonitor.Core/History/IEventStore.cs
- FOUND: service/src/EcoFlowMonitor.Core/History/SqliteHistoryStore.cs
- FOUND: service/src/EcoFlowMonitor.Core/History/SqliteEventStore.cs
- FOUND: service/src/EcoFlowMonitor.Core/History/Resolution.cs
- FOUND: service/src/EcoFlowMonitor.Core/History/TelemetrySnapshot.cs
- FOUND: service/src/EcoFlowMonitor.Core/History/PowerEvent.cs
- FOUND: service/src/EcoFlowMonitor.Core/History/PowerEventItem.cs

Verified commits:
- FOUND: 8f42b63
- FOUND: 53cb070

Build: 0 errors, 0 warnings.
