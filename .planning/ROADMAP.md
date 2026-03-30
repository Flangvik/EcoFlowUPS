# Roadmap: EcoFlow UPS Monitor

## Overview

Starting from a working macOS-only prototype, this milestone hardens the foundation (logging, error handling, connection state machine), extends BLE to Windows and Linux, adds SQLite-backed history with live charts, and completes the product with a functional rules engine and settings UI. Each phase delivers a verifiable capability; later phases depend only on what came before.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Infrastructure** - Replace the logging stack, add thread safety, wire the connection state machine, and remove silent failures throughout (completed 2026-03-30)
- [ ] **Phase 2: Cross-Platform BLE** - Add Windows (WinRT) and Linux (BlueZ) BLE adapters against the existing IBleAdapter interface
- [ ] **Phase 3: History & Persistence** - Persist telemetry to SQLite and surface it as in-app historical charts and an event log
- [ ] **Phase 4: Rules, Settings & Polish** - Complete the rules engine with webhook/script/notification actions, wire the settings page, and finish UX polish

## Phase Details

### Phase 1: Infrastructure
**Goal**: The app is observable, resilient, and error-free in production — no silent failures, structured logs on disk, and a connection state machine that surfaces state to the UI
**Depends on**: Nothing (first phase)
**Requirements**: INFRA-01, INFRA-02, INFRA-03, INFRA-04, CONN-01, CONN-02, CONN-05, UX-02, UX-03
**Success Criteria** (what must be TRUE):
  1. Every connection state change (scanning, connecting, authenticated, streaming, disconnected, retrying) is visible per device in the sidebar with a retry attempt counter
  2. When a device disconnects, the dashboard shows the last-known telemetry values with a "Last update: Xm ago" staleness badge — no blank screen
  3. Switching connection mode (Cloud/BLE/Auto) restarts the monitor and shows clear feedback during the transition
  4. Error states are surfaced in the UI — no blank screens or silent failures anywhere; structured log files are written to disk with rotation
  5. Verbose debug frame-level logs are absent from the production build; DeviceState mutations from concurrent BLE and MQTT threads do not corrupt state
**Plans**: 4 plans

Plans:
- [x] 01-01-PLAN.md — Serilog migration (big-bang Logger.Log replacement, bootstrap logger, ILogger<T> injection)
- [x] 01-02-PLAN.md — DeviceState contracts: ConnectionStatus enums, thread-safety lock, bare catch fixes
- [x] 01-03-PLAN.md — Connection FSM + Polly: Stateless state machine in both monitors, CONN-05 bug fix
- [x] 01-04-PLAN.md — State badge bar UI: DeviceViewModel props, staleness timer, DashboardView insertion

### Phase 2: Cross-Platform BLE
**Goal**: BLE monitoring works on Windows and Linux using platform-native adapters, with the same resilient reconnect lifecycle already established in Phase 1
**Depends on**: Phase 1
**Requirements**: CONN-03, CONN-04
**Success Criteria** (what must be TRUE):
  1. User can connect to an EcoFlow Delta 3 via BLE on Windows 10/11 and receive live telemetry — ECDH handshake completes and data streams correctly
  2. User can connect via BLE on Linux and receive live telemetry; if the user is not in the bluetooth group, a human-readable diagnostic error is shown rather than a silent failure
  3. BLE reconnects on both platforms re-run the full ECDH handshake — stale crypto state never produces silently corrupted data
**Plans**: TBD
**UI hint**: yes

### Phase 3: History & Persistence
**Goal**: Users can review their device's power history in-app with hourly/daily/weekly charts; all power events are recorded persistently
**Depends on**: Phase 1
**Requirements**: DATA-01, DATA-02, DATA-03, DATA-04
**Success Criteria** (what must be TRUE):
  1. User can open a history view and see battery %, voltage, and power charts with a time range selector (hourly / daily / weekly)
  2. User can view a timestamped event log of power lost, restored, low battery, and connection change events that persists across app restarts
  3. The app never shows a "database is locked" error — concurrent read/write from UI and telemetry writer work without contention
**Plans**: 3 plans

Plans:
- [x] 03-01-PLAN.md — SQLite persistence layer: IHistoryStore/IEventStore contracts, SqliteHistoryStore/SqliteEventStore with WAL + Channel<T> debounce
- [ ] 03-02-PLAN.md — MonitorOrchestrator integration: EnqueueSnapshot on every tick, EnqueueEvent on power transitions, DI registration
- [ ] 03-03-PLAN.md — HistoryView UI: HistoryViewModel with time range selector, LiveChartsCore 2.0.0 charts, event log, dashboard navigation wiring

### Phase 4: Rules, Settings & Polish
**Goal**: Users can configure rules that fire real actions on power events, manage all app settings through a working settings page, and read the dashboard at a glance
**Depends on**: Phase 1
**Requirements**: RULE-01, RULE-02, RULE-03, RULE-04, RULE-05, RULE-06, SET-01, SET-02, SET-03, SET-04, UX-01
**Success Criteria** (what must be TRUE):
  1. User can create a rule with a trigger (power lost, battery below X%, power restored) and one or more actions (webhook, OS notification, shell script) and it fires reliably on the next matching event
  2. A rule with multiple actions (e.g. webhook + notification + script) fires all actions on the same trigger without one blocking another
  3. User can configure connection preferences, battery alert threshold, and notification preferences from the settings page and the changes take effect without restarting the app
  4. User can create, edit, delete, and enable/disable rules from the rule management UI
  5. The dashboard presents power state prominently at the top with secondary details scannable below — visual hierarchy is clear at a glance
**Plans**: TBD
**UI hint**: yes

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Infrastructure | 4/4 | Complete   | 2026-03-30 |
| 2. Cross-Platform BLE | 0/TBD | Not started | - |
| 3. History & Persistence | 0/3 | Not started | - |
| 4. Rules, Settings & Polish | 0/TBD | Not started | - |
