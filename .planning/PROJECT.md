# EcoFlow UPS Monitor

## What This Is

A cross-platform desktop app that monitors EcoFlow Delta 3 / Delta 3 Max battery stations in real-time via BLE and MQTT cloud connections. Displays live telemetry (battery, power, cell voltages, temperatures), detects power events (outage, restore), and automates responses through a configurable rules engine. Built with .NET 10 + Avalonia UI.

## Core Value

Reliable, real-time power monitoring that never silently loses connection — the user always knows their power status and gets alerted when it changes.

## Requirements

### Validated

- ✓ MQTT cloud connection with auto-discovery and live telemetry — existing
- ✓ BLE local connection with ECDH handshake and encrypted data stream — existing (macOS)
- ✓ Auto/Cloud/BLE connection mode toggle per device — existing
- ✓ Live dashboard with battery %, voltage, current, temp, cycles, SoH — existing
- ✓ Per-cell voltage display with min/max highlighting — existing
- ✓ Power state machine (charging, power lost, idle, restored) — existing
- ✓ Capacity, accumulated energy, charge/discharge time display — existing
- ✓ Power flow diagram and power history chart — existing
- ✓ Multi-device support in sidebar — existing
- ✓ Dark theme Avalonia UI with StatCard controls — existing
- ✓ Hand-rolled protobuf decoder for MQTT + compiled protobuf for BLE — existing
- ✓ CLI diagnostic tool for raw MQTT data dump — existing

### Active

- [ ] Windows BLE adapter (WinRT Bluetooth LE)
- [ ] Linux BLE adapter (BlueZ D-Bus)
- [ ] Cross-platform BLE that just works on all 3 OSes
- [ ] Auto-reconnect with exponential backoff for BLE and MQTT
- [ ] Graceful handling of device unreachable / powered off
- [ ] Offline mode with cached last-known state
- [ ] Connection state feedback in UI (scanning, connecting, error, retry)
- [ ] Clean dashboard layout with better visual hierarchy
- [ ] Working settings page (thresholds, connection config, notification prefs)
- [ ] Rules engine: webhooks, shell scripts, push notifications, email on power events
- [ ] Multiple action types per rule (webhook + push + script)
- [ ] In-app historical charts (hourly/daily/weekly) with SQLite persistence
- [ ] Proper error surfacing — no silent failures anywhere
- [ ] Unified protobuf decode path (eliminate dual decoder maintenance)

### Out of Scope

- Mobile app (iOS/Android) — different platform, different project
- Web dashboard / Docker headless mode — v2 consideration
- Prometheus/InfluxDB metrics export — v2 consideration
- EcoFlow device control (set charge limits, toggle AC) — monitoring only for v1
- Support for non-Delta 3 EcoFlow devices — only Delta 3 / Delta 3 Max for v1

## Context

**Existing codebase:** ~8,000 lines across 6 .NET projects. MVVM architecture with Avalonia UI, CommunityToolkit.Mvvm, platform abstraction via interfaces. BLE works on macOS via CoreBluetooth native interop. MQTT works everywhere. Two parallel protobuf decode paths (hand-rolled for MQTT, compiled for BLE) that need unification.

**Known issues from codebase map (.planning/codebase/CONCERNS.md):**
- MQTT broker rate-limits rapid reconnections — no backoff strategy
- BLE connection can silently drop with no reconnect
- Credentials stored in plaintext config.json
- TLS cert validation disabled for MQTT
- StatCard had to be rewritten because Avalonia styled property bindings silently failed
- Verbose debug logging left in BleTransport (frame-level logs every second)
- Thread safety of DeviceState mutations from multiple monitors not guaranteed

**Platform state:** Windows and Linux Platform projects exist with stub implementations for notifications, power actions, startup, and script runner. BLE adapters for these platforms don't exist yet.

**Python POC:** Original prototype in `poc/` with 14 pytest tests covering API, MQTT, protobuf decode, and power state machine. Useful as reference for expected behavior.

## Constraints

- **Tech stack**: .NET 10 + Avalonia UI — already committed, not changing
- **Devices**: EcoFlow Delta 3 and Delta 3 Max only — both use pd335_sys protobuf
- **BLE libraries**: Need platform-native approaches — WinRT on Windows, BlueZ on Linux, CoreBluetooth on macOS
- **No test suite**: Zero C# unit tests currently — need to add alongside refactoring
- **EcoFlow API**: Undocumented, reverse-engineered — can break without notice

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Avalonia UI over MAUI | Cross-platform desktop focus, better Linux support | ✓ Good |
| Platform abstraction via interfaces | Clean separation for OS-specific BLE/notifications | ✓ Good |
| Hand-rolled protobuf decoder | MQTT messages use non-standard envelope, needed custom parsing | ⚠️ Revisit — should unify with compiled protobuf |
| StatCard uses OnLoaded + OnPropertyChanged | Styled property bindings silently failed in Avalonia | ✓ Good (workaround, but works) |
| CommunityToolkit.Mvvm source generators | Reduces boilerplate for observable properties | ✓ Good |
| SQLite for history persistence | Lightweight, no server, cross-platform | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd:transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-03-30 after initialization*
