# Phase 1: Infrastructure - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-03-30
**Phase:** 01-infrastructure
**Areas discussed:** Connection state display, Staleness & offline UX, Error surfacing approach, Logging migration scope

---

## Connection State Display

| Option | Description | Selected |
|--------|-------------|----------|
| Enhanced dot + text | Keep dot, add state text with retry info | |
| State badge bar | Full-width bar under device name with state + source + retry | ✓ |
| Sidebar state chips | Colored chips in sidebar device list | |

**User's choice:** State badge bar
**Notes:** More prominent, harder to miss. Replaces the current green/red dot entirely.

### Retry Info Detail

| Option | Description | Selected |
|--------|-------------|----------|
| Attempt + countdown | "Reconnecting (attempt 3, next in 8s)" | ✓ |
| Just state name | "Reconnecting..." — no numbers | |
| Full debug info | "Retry 3/∞ via BLE, backoff 8s, last error: timeout" | |

**User's choice:** Attempt + countdown

### Source Badge

| Option | Description | Selected |
|--------|-------------|----------|
| Keep as-is | Teal text next to connection dot | ✓ |
| Colored icon | Bluetooth/cloud icon instead of text | |
| You decide | Claude picks | |

**User's choice:** Keep as-is

---

## Staleness & Offline UX

| Option | Description | Selected |
|--------|-------------|----------|
| Dim + timestamp badge | Values dim to 50%, "Last update: Xm ago" badge | ✓ |
| Greyed overlay | Semi-transparent grey overlay on entire panel | |
| Values stay normal + badge only | No visual change to values | |

**User's choice:** Dim + timestamp badge

### Stale Timing

| Option | Description | Selected |
|--------|-------------|----------|
| 30 seconds | ~10 missed cycles = definitely disconnected | ✓ |
| 10 seconds | More aggressive, may flicker | |
| 60 seconds | More relaxed, stale data sits longer | |

**User's choice:** 30 seconds

### Extended Offline

| Option | Description | Selected |
|--------|-------------|----------|
| Keep last values dimmed | Dashboard stays populated with dimmed stale data | |
| Clear to defaults | Reset to "--" / "0" after 5+ minutes | ✓ |
| Keep + countdown | Dimmed with running "Offline for: 5m 23s" counter | |

**User's choice:** Clear to defaults

---

## Error Surfacing

| Option | Description | Selected |
|--------|-------------|----------|
| Per-device in state bar | Errors in device's connection state bar | ✓ |
| Global notification bar | Top-of-window bar for errors | |
| Both | Device-specific + global for critical | |

**User's choice:** Per-device in state bar

### Error Detail

| Option | Description | Selected |
|--------|-------------|----------|
| Friendly + expandable | Friendly message with clickable "Details" for technical info | ✓ |
| Always technical | Show actual error message always | |
| Always friendly | No technical details in UI | |

**User's choice:** Friendly + expandable

### Error Log

| Option | Description | Selected |
|--------|-------------|----------|
| In event log | Errors in same log as power events | ✓ |
| Separate error panel | Dedicated error/debug panel | |
| Log file only | Serilog file only, not visible in UI | |

**User's choice:** In event log (single audit trail)

---

## Logging Migration

| Option | Description | Selected |
|--------|-------------|----------|
| Big bang | Replace all Logger.Log() at once with ILogger<T> | ✓ |
| Incremental | Static Logger wraps Serilog, migrate file-by-file | |
| You decide | Claude picks | |

**User's choice:** Big bang

### Log Levels

| Option | Description | Selected |
|--------|-------------|----------|
| Information | Connection events, state changes, rule firings | ✓ |
| Warning | Only problems and notable events | |
| Debug on demand | Information default + settings toggle for Debug | |

**User's choice:** Information

### Log Rotation

| Option | Description | Selected |
|--------|-------------|----------|
| 10MB / 3 files | ~30MB max disk usage | ✓ |
| Daily / 7 days | One file per day | |
| You decide | Claude picks | |

**User's choice:** 10MB / 3 files

---

## Claude's Discretion

- Connection state machine library choice (Stateless vs hand-rolled)
- Thread-safety approach for DeviceState
- Serilog configuration details
- How to remove verbose debug logging while keeping diagnostic capability

## Deferred Ideas

None — discussion stayed within phase scope.
