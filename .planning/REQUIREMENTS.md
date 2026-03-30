# Requirements: EcoFlow UPS Monitor

**Defined:** 2026-03-30
**Core Value:** Reliable, real-time power monitoring that never silently loses connection

## v1 Requirements

### Connection Resilience

- [x] **CONN-01**: App shows visible connection state per device (scanning, connecting, authenticated, streaming, disconnected, retrying) with retry attempt counter
- [x] **CONN-02**: App displays last-known data with staleness indicator ("Last update: 5m ago") when device is disconnected
- [ ] **CONN-03**: BLE connection works on Windows via WinRT Bluetooth LE APIs
- [ ] **CONN-04**: BLE connection works on Linux via BlueZ D-Bus (Linux.Bluetooth)
- [ ] **CONN-05**: Connection mode toggle (Cloud/BLE/Auto) provides clear feedback during transitions and actually restarts the monitor

### Infrastructure

- [ ] **INFRA-01**: Replace static Logger with Serilog structured logging (file sink with rotation, log levels)
- [x] **INFRA-02**: DeviceState mutations are thread-safe (concurrent BLE + MQTT writes don't corrupt state)
- [x] **INFRA-03**: Bare catch blocks replaced with proper error handling — no silent swallowing of exceptions
- [ ] **INFRA-04**: Connection state machine (Stateless or equivalent) replaces raw while-loop retry in BleMonitor and MqttMonitor

### Data & History

- [ ] **DATA-01**: Telemetry snapshots persist to SQLite (battery %, voltage, power in/out, temp per device, sampled every 10-30s)
- [ ] **DATA-02**: Dashboard shows historical charts with hourly/daily/weekly time range selector
- [ ] **DATA-03**: Event log records timestamped power events (power lost, restored, low battery, connection changes) with persistent storage
- [ ] **DATA-04**: SQLite uses WAL mode and handles concurrent read/write without "database is locked" errors

### Rules & Automation

- [ ] **RULE-01**: User can create rules with a trigger condition (power lost, battery below X%, power restored) and one or more actions
- [ ] **RULE-02**: Webhook action: fires HTTP POST to user-configured URL with JSON payload containing device state
- [ ] **RULE-03**: OS notification action: sends desktop notification via platform notification service
- [ ] **RULE-04**: Shell script action: executes user-configured script/command on trigger
- [ ] **RULE-05**: Rules support cooldown period to prevent notification spam
- [ ] **RULE-06**: Multiple actions per rule (e.g. webhook + notification + script all fire on same trigger)

### Settings & Configuration

- [ ] **SET-01**: Working settings page with editable connection preferences per device
- [ ] **SET-02**: Configurable battery threshold for low-battery alerts (default 20%)
- [ ] **SET-03**: Notification preferences (enable/disable per event type)
- [ ] **SET-04**: Rule management UI (create, edit, delete, enable/disable rules)

### UX Polish

- [ ] **UX-01**: Dashboard layout has clear visual hierarchy — power state prominent, details scannable
- [ ] **UX-02**: Error states are always surfaced — no blank screens, no silent failures
- [ ] **UX-03**: Verbose debug logging removed from production (BleTransport frame-level logs)

## v2 Requirements

### Export & Integration

- **EXP-01**: Prometheus/InfluxDB metrics endpoint for external monitoring stacks
- **EXP-02**: CSV/JSON export of historical data
- **EXP-03**: Docker headless mode (MQTT only, REST API for dashboards)

### Advanced Rules

- **RULE-07**: Compound AND/OR trigger conditions (e.g. "battery below 20% AND on battery for 10+ minutes")
- **RULE-08**: Email action type with SMTP configuration

### Platform

- **PLAT-01**: Auto-reconnect with exponential backoff + jitter for BLE and MQTT (Polly)
- **PLAT-02**: Application installer/packaging for each platform

## Out of Scope

| Feature | Reason |
|---------|--------|
| Mobile app (iOS/Android) | Different platform entirely; EcoFlow official app covers mobile |
| Web dashboard / HTTP server | Scope creep toward a different product; defer to v2 headless mode |
| Device control (set charge limits, toggle AC) | Monitoring only for v1; control introduces API breakage risk |
| Cloud sync / multi-machine dashboard | Requires auth service and backend infrastructure |
| Non-Delta 3 EcoFlow devices | Only Delta 3 / Delta 3 Max for v1; protocol differs per family |
| Audio alarms / sound effects | OS notifications are sufficient; audio requires extensive preference tuning |
| NUT server compatibility | EcoFlow devices aren't NUT-compatible; different integration path |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| CONN-01 | Phase 1 | Complete |
| CONN-02 | Phase 1 | Complete |
| CONN-03 | Phase 2 | Pending |
| CONN-04 | Phase 2 | Pending |
| CONN-05 | Phase 1 | Pending |
| INFRA-01 | Phase 1 | Pending |
| INFRA-02 | Phase 1 | Complete |
| INFRA-03 | Phase 1 | Complete |
| INFRA-04 | Phase 1 | Pending |
| DATA-01 | Phase 3 | Pending |
| DATA-02 | Phase 3 | Pending |
| DATA-03 | Phase 3 | Pending |
| DATA-04 | Phase 3 | Pending |
| RULE-01 | Phase 4 | Pending |
| RULE-02 | Phase 4 | Pending |
| RULE-03 | Phase 4 | Pending |
| RULE-04 | Phase 4 | Pending |
| RULE-05 | Phase 4 | Pending |
| RULE-06 | Phase 4 | Pending |
| SET-01 | Phase 4 | Pending |
| SET-02 | Phase 4 | Pending |
| SET-03 | Phase 4 | Pending |
| SET-04 | Phase 4 | Pending |
| UX-01 | Phase 4 | Pending |
| UX-02 | Phase 1 | Pending |
| UX-03 | Phase 1 | Pending |

---
*Defined: 2026-03-30*
