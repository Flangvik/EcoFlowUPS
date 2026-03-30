# Phase 1: Infrastructure - Context

**Gathered:** 2026-03-30
**Status:** Ready for planning

<domain>
## Phase Boundary

Replace the logging stack, add thread-safe state management, wire a connection state machine for BLE and MQTT, and surface all connection states and errors in the UI. No new features — hardening and observability only.

</domain>

<decisions>
## Implementation Decisions

### Connection State Display
- **D-01:** Replace the current green/red dot with a **full-width state badge bar** under the device name showing: state text + source indicator + retry info. More prominent, harder to miss.
- **D-02:** Show retry detail as **attempt count + countdown**: "Reconnecting (attempt 3, next in 8s)". Shows both progress and timing.
- **D-03:** Keep the "via BLE" / "via Cloud" teal text source indicator as-is — unobtrusive and works.

### Staleness & Offline UX
- **D-04:** When a device disconnects, **dim all values to 50% opacity** and show a "Last update: Xm ago" badge on the state bar. Values stay visible but clearly aged.
- **D-05:** Data becomes "stale" after **30 seconds** of no updates (BLE sends every 1-2s, MQTT every 2-3s — 30s means ~10 missed cycles).
- **D-06:** After 5+ minutes offline, **clear values to defaults** ("--" / "0") — makes it obvious there's no current data.

### Error Surfacing
- **D-07:** Errors show **per-device in the connection state bar**: "Error: BLE auth failed". Localized to the affected device, no global notification bar.
- **D-08:** Error detail is **friendly with expandable technical info**: "BLE connection failed" with a clickable "Details" that reveals the actual exception message.
- **D-09:** Errors accumulate in the **same event log as power events** — single audit trail for everything notable.

### Logging Migration
- **D-10:** **Big bang replacement** of all Logger.Log() calls with ILogger<T> via dependency injection. One clean commit, no mixed logging.
- **D-11:** Default log level: **Information** — connection events, state changes, rule firings. No frame-level noise.
- **D-12:** Log rotation: **10MB / 3 files** (~30MB max disk usage).

### Claude's Discretion
- Connection state machine implementation (Stateless library vs hand-rolled enum FSM)
- Thread-safety approach for DeviceState (lock, immutable snapshots, or concurrent collections)
- Specific Serilog sink configuration and structured field naming
- How to remove verbose BleTransport frame-level logging without losing diagnostic capability

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Codebase Maps
- `.planning/codebase/ARCHITECTURE.md` — Current MVVM architecture, data flow, threading model
- `.planning/codebase/CONVENTIONS.md` — Coding style, MVVM patterns, error handling patterns
- `.planning/codebase/CONCERNS.md` — Known tech debt, security issues, reliability gaps
- `.planning/codebase/INTEGRATIONS.md` — MQTT and BLE protocol details, data flow

### Research
- `.planning/research/STACK.md` — Serilog, Polly, Stateless library recommendations with versions
- `.planning/research/ARCHITECTURE.md` — Connection state machine design, Polly retry patterns
- `.planning/research/PITFALLS.md` — MQTT rate-limit bug, BLE reconnect ECDH requirement, bare catch risks

### Key Source Files
- `service/src/EcoFlowMonitor.Core/Logging/Logger.cs` — Current static logger to replace
- `service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs` — BLE connection loop with retry
- `service/src/EcoFlowMonitor.Core/Client/Ble/BleTransport.cs` — Verbose frame logging to remove
- `service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs` — MQTT connection loop with retry
- `service/src/EcoFlowMonitor.Core/State/DeviceState.cs` — Mutable state, not thread-safe
- `service/src/EcoFlowMonitor.App/ViewModels/DeviceViewModel.cs` — Connection badge, status text
- `service/src/EcoFlowMonitor.App/Views/DashboardView.axaml` — State bar insertion point

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `DeviceViewModel` already has `ConnectionBadge`, `ActiveSource`, `StatusText` properties — can be extended for state bar
- `StatCard` control (rewritten with OnLoaded + OnPropertyChanged) is the proven pattern for data display
- `MonitorOrchestrator` already manages `MonitorEntry` per device with `StateChanged` events
- `PowerStateMachine` exists as a pattern for state transitions — can inform connection state machine design

### Established Patterns
- UI updates must be marshaled via `Dispatcher.UIThread.Post()` (proven fix from StatCard debugging)
- `[ObservableProperty]` source generators for all VM properties
- Platform abstraction via interfaces in Core, implementations in Platform.{OS}

### Integration Points
- `BleMonitor.OnPacketReceived` and `MqttMonitor` message handler — where connection state transitions originate
- `DashboardViewModel.OnDeviceUpdated` — where state reaches the UI (already marshaled to UI thread)
- `DashboardView.axaml` hero section — where state bar should be inserted (between device name and stat cards)

</code_context>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. User wants "it just works" reliability with clear visual feedback.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 01-infrastructure*
*Context gathered: 2026-03-30*
