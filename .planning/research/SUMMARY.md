# Project Research Summary

**Project:** EcoFlow UPS Monitor — Cross-platform BLE Milestone
**Domain:** Cross-platform desktop UPS/battery monitoring (.NET 10, Avalonia, BLE + MQTT)
**Researched:** 2026-03-30
**Confidence:** HIGH

## Executive Summary

EcoFlow UPS Monitor is a desktop-native, BLE-local-capable UPS monitoring application targeting the homelab/prosumer segment that currently has no good tool: NUT is config-hell, PowerChute/CyberPower are Windows-USB only, and the EcoFlow official app is mobile-only and cloud-dependent. The project has a solid MVVM foundation on macOS with working MQTT cloud and BLE transports, but is missing three critical capabilities for a complete product: (1) cross-platform BLE adapters for Windows and Linux, (2) resilient reconnection with visible connection state, and (3) data persistence for historical charts and event logs. Research confirms well-established patterns for all three and the existing `IBleAdapter` / `IDeviceMonitor` abstraction is correctly shaped to receive them.

The recommended implementation order is: fix the logging and reconnection infrastructure first (these are blocking everything else and causing real production bugs today), then add Windows and Linux BLE adapters against the stable interfaces, then layer in SQLite history persistence, and finally extend the rules engine with webhook actions and a functional settings UI. This order is dictated by concrete dependencies: the BLE adapters will immediately use the connection state machine; the history layer needs a stable `StateChanged` stream; and the rules engine extension is lowest risk once the foundation is solid. All recommended packages are either native to the .NET 10 platform or have verified NuGet releases at exact versions.

The key risks are platform-specific: Windows WinRT BLE has three distinct pitfalls around paired-device caching, lazy-connect semantics, and handler accumulation that will cause silent failures if not explicitly coded around; Linux BlueZ has a D-Bus permissions trap that will work for developers but fail silently for end users; and the existing MQTT fixed-delay retry is a confirmed bug that is already causing broker rate-limit lockouts in production. All three are well-understood with concrete mitigations documented in research. None require architectural changes — they are implementation-level concerns within the existing abstraction boundaries.

---

## Key Findings

### Recommended Stack

The stack additions are surgical and well-justified. Windows BLE uses `Microsoft.Windows.SDK.Contracts` (WinRT native, no MSIX packaging required for desktop apps) and Linux BLE uses `Linux.Bluetooth` 5.67.1 (BlueZ D-Bus, the same library that InTheHand uses internally — going direct eliminates an unnecessary abstraction layer). Both fit the existing `IBleAdapter` interface without changes. `Polly` 8.6.6 replaces the hand-rolled retry loops in both `BleMonitor` and `MqttMonitor` with exponential backoff + jitter, which is the direct countermeasure for the confirmed MQTT broker rate-limit bug. EF Core was evaluated and rejected for the history layer in favor of `Microsoft.Data.Sqlite` with raw SQL — the schema is two tables with known SQLite aggregate functions, and EF Core change-tracking overhead on high-frequency time-series rows is a documented performance trap. Logging replaces the synchronous `Logger.cs` static class with `Serilog` + `Microsoft.Extensions.Logging` bridge, which removes the synchronous file I/O bottleneck from the BLE notification pipeline.

**Core technologies:**
- `Microsoft.Windows.SDK.Contracts` 10.0.26100.7705: WinRT BLE types for `WinRtBleAdapter` — native OS API, no abstraction overhead, no packaging required
- `Linux.Bluetooth` 5.67.1: BlueZ D-Bus GATT client for `BlueZBleAdapter` — actively maintained, exposes `Disconnected` event missing from InTheHand wrapper
- `Polly` 8.6.6: `ResiliencePipeline` for both BLE and MQTT reconnect — replaces confirmed-broken fixed-5s retry with exponential backoff + jitter + circuit breaker
- `Microsoft.Data.Sqlite` (inbox in .NET 10) + Dapper: SQLite persistence for telemetry history — no ORM overhead, WAL mode for concurrent read/write
- `Serilog` 4.2+ + `Serilog.Extensions.Logging` 10.0.0: Async-buffered structured logging — removes synchronous disk I/O from BLE notification path
- `Stateless` 5.x: Lightweight state machine for `ConnectionStateMachine` — replaces unstructured retry loops with explicit state transitions visible to UI

### Expected Features

The competitive analysis against NUT, APC PowerChute, CyberPower PowerPanel, Eaton IPM, and EcoFlow app validates the project's roadmap choices. The table-stakes gaps (visible connection status, auto-reconnect, graceful offline state) are what make the app feel broken today. The differentiators (BLE-local, per-cell voltage, historical charts, webhook rules) are what make it better than every existing tool for EcoFlow users.

**Must have (table stakes gaps — currently missing):**
- Visible connection status (connected/reconnecting/error) in sidebar and dashboard header — every competitor shows this prominently
- Exponential backoff with UI retry counter ("Reconnecting in 43s, attempt 4") — builds trust during network disruptions
- Graceful offline/stale state (last-known values + "last seen X ago" badge) — currently shows blank screen on disconnect
- Working settings page with threshold configuration — `SettingsViewModel` exists but is non-functional
- OS notifications wired through rules engine for power lost/restored/battery low — `INotificationService` interface exists but is not connected

**Should have (high-value differentiators):**
- SQLite-backed event log with timestamped rule fire history — no competitor has this
- Historical battery/power charts with hourly/daily resolution — EcoFlow app does this but is cloud-only; desktop local charts are a real gap
- Webhook action type in rules engine — more accessible than NUT's script hooks; integrates with n8n/Home Assistant

**Defer (v2+):**
- Linux BLE adapter (large platform work, separate milestone per FEATURES.md recommendation)
- Windows BLE adapter (same — but research confirms patterns are ready)
- Compound AND/OR trigger conditions — Eaton IPM took years to build this well
- Chart export / data download
- Web dashboard / Prometheus export / SNMP

### Architecture Approach

The existing architecture is correctly shaped for this milestone. `IBleAdapter` / `IBleGattConnection` interfaces in Core are adequate for all three platforms without modification. The recommended additions are: a `ConnectionStateMachine` (using `Stateless`) inside each monitor to replace unstructured retry loops and expose state to UI; an `IHistoryStore` / `SqliteHistoryStore` pair in Core for telemetry persistence; and an `IActionHandler` dispatcher pattern in `ActionRunner` to support extensible rule actions without growing a monolithic switch statement. Data flows in one direction: `StateChanged` event → `MonitorOrchestrator.OnStateChanged()` → history write (debounced 10s) + rules evaluation (async dispatch) + UI event. History reads flow the other direction: `HistoryViewModel` queries `IHistoryStore` directly, bypassing the orchestrator.

**Major components:**
1. `ConnectionStateMachine` (Core) — explicit states (Idle/Scanning/Connecting/Authenticating/Streaming/Disconnected/Retrying); replaces while-true loops; exposes `ConnectionStatus` to UI via `DeviceState`
2. `WinRtBleAdapter` (Platform.Windows) + `BlueZBleAdapter` (Platform.Linux) — platform-native BLE implementing existing `IBleAdapter` interface; registered in `PlatformServiceFactory`
3. `IHistoryStore` / `SqliteHistoryStore` (Core) — WAL-mode SQLite with 10-second debounced write via `Channel<TelemetrySnapshot>`; serves both history charts and event log
4. `IActionHandler` dispatcher in `ActionRunner` (Core) — strategy pattern per `ActionType`; enables `WebhookActionHandler`, `NotificationActionHandler`, `ScriptActionHandler` as independent units
5. `MonitorOrchestrator` (App) — unchanged coordinator; pipes `StateChanged` into history write, rules evaluation, and UI events

### Critical Pitfalls

1. **WinRT lazy-connect model mismatch** — `FromBluetoothAddressAsync` does not open a GATT connection; it defers until an operation is attempted. `IsConnected` returns true before the device is actually connected, causing false UI state and 7-second silent timeouts. Prevention: force connection via `GetGattServicesAsync(BluetoothCacheMode.Uncached)` immediately after device construction; treat success as "connected"; expose `ConnectionStatusChanged` through `IBleGattConnection`.

2. **WinRT ValueChanged handler accumulation on reconnect** — each reconnect adds a new `ValueChanged` event handler without removing the old one. After N reconnects, the decode callback fires N times per packet, silently corrupting `DeviceState`. Prevention: dispose `BluetoothLEDevice` fully between reconnects; track and revoke `EventRegistrationToken` before re-subscribing.

3. **MQTT fixed-delay retry triggering broker rate-limit lockout** — confirmed production bug; fixed 5s retry with no jitter causes broker to lock the client out, extending a 30s network blip into a 5-10 minute outage. Prevention: replace with Polly exponential backoff + jitter (base 2s, max 5min) + MQTT-specific circuit breaker. Fix this before all other reliability work.

4. **BlueZ D-Bus permission failure for non-root users** — `StartDiscoveryAsync` throws `NotPermitted` for users not in the `bluetooth` group, but bare catch blocks swallow the error silently. Prevention: catch `DBusException` specifically in the Linux adapter; surface human-readable diagnostic; document `usermod -aG bluetooth $USER` in install instructions; add preflight check in `InitializeAsync`.

5. **BLE reconnect does not re-run ECDH handshake** — after a BLE disconnect/reconnect, the EcoFlow device issues a new challenge; the old session key is invalid. Packets are received but decrypt to garbage, caught by a bare catch, producing silent stale data. Prevention: always reset `_transport` to `crypto: null` and re-run `PerformEcdhHandshakeAsync` on every new connection; never resume from saved crypto state.

---

## Implications for Roadmap

Based on combined research, the phase structure is dictated by concrete implementation dependencies, not arbitrary grouping. The connection state machine and logging must come first because every subsequent phase either uses or writes through them.

### Phase 1: Tech Debt and Infrastructure Foundation

**Rationale:** The synchronous logger blocks the BLE notification pipeline; the bare catch pattern hides errors across the entire codebase; the `PlatformServiceFactory` reflection loader masks assembly failures. These are not nice-to-haves — they are active obstacles to adding BLE adapters and resilience code. Fixing them first means every subsequent phase works in a clean environment with observable errors.

**Delivers:** Async-buffered structured logging (Serilog), removed bare catches with meaningful error boundaries, null-safe `PlatformServiceFactory` with diagnostic messages, `ConnectionStateMachine` integrated into `BleMonitor` and `MqttMonitor`, `ConnectionStatus` enum surfaced in `DeviceState` and `DeviceViewModel`, Polly `ResiliencePipeline` replacing fixed-delay retry in both monitors.

**Addresses:** Connection status indicator (table stakes), exponential backoff with retry counter (differentiator)

**Avoids:** Synchronous logger blocking BLE pipeline (P14), bare catch hiding MQTT data starvation (P7), reflection factory crashing without diagnostics (P13), MQTT broker rate-limit lockout (P6 — confirmed production bug, must fix here)

### Phase 2: Windows BLE Adapter

**Rationale:** Windows is the largest target platform. The WinRT adapter has the most pitfalls (three critical ones documented) and requires the most careful implementation. macOS is already working. Getting Windows stable validates the `IBleAdapter` interface and the `ConnectionStateMachine` integration before adding Linux complexity.

**Delivers:** `WinRtBleAdapter` implementing `IBleAdapter` using `BluetoothLEAdvertisementWatcher` for scanning and `BluetoothLEDevice` GATT for connection; full reconnect lifecycle with ECDH handshake reset; end-to-end BLE monitoring on Windows.

**Uses:** `Microsoft.Windows.SDK.Contracts` 10.0.26100.7705, `net10.0-windows10.0.18362.0` TFM

**Avoids:** Paired-device Unreachable on first GetGattServicesAsync (P1 — always use Uncached, retry after 500ms), ValueChanged handler accumulation on reconnect (P2 — dispose device fully, track tokens), lazy-connect model mismatch (P3 — force connection via GetGattServicesAsync), stale ECDH session key on reconnect (P12 — always re-run handshake)

**Research flag:** Needs `/gsd:research-phase` if WinRT BLE behavior deviates from documented patterns during implementation. P1, P2, P3 are all well-documented but subtle enough to warrant integration testing before declaring complete.

### Phase 3: Linux BLE Adapter

**Rationale:** Linux adapter is structurally parallel to Windows (same `IBleAdapter` interface) but has different platform-specific pitfalls. Separating it from Windows work allows each to be developed and tested independently per OS. Linux comes after Windows because Windows patterns are better documented and establish confidence in the interface contract.

**Delivers:** `BlueZBleAdapter` implementing `IBleAdapter` using `Linux.Bluetooth` 5.67.1; D-Bus permission preflight check with human-readable diagnostic; `ServicesResolved` wait before service discovery; `Disconnected` event wired to `IBleGattConnection`; ECDH handshake reset on reconnect; move `CoreBluetoothBleAdapter` from `App/Services/` to `Platform.macOS/`.

**Uses:** `Linux.Bluetooth` 5.67.1, `Tmds.DBus` (transitive)

**Avoids:** D-Bus permission failure (P4 — preflight check, specific exception catch), ServicesResolved race (P5 — `WaitForPropertyValueAsync` with 10s timeout), macOS adapter in wrong project (ARCHITECTURE anti-pattern 4)

**Research flag:** Linux distro permission behavior varies (Arch vs. Debian vs. Fedora). Mark for validation on multiple distros during phase planning.

### Phase 4: SQLite History Persistence and Charts

**Rationale:** History depends on a stable `StateChanged` stream — which is established by Phase 1. The schema is simple and independent of the platform BLE work. This phase can be developed on macOS while Windows/Linux BLE is being stabilized, but should not ship before the connection state machine is solid (stale/offline state affects what gets written).

**Delivers:** `IHistoryStore` interface + `TelemetrySnapshot` model in Core; `SqliteHistoryStore` with WAL mode, `busy_timeout`, 10s debounced write via `Channel<TelemetrySnapshot>`; 90-day rolling prune; `HistoryViewModel` + LiveChartsCore chart view with hour/day/week resolution; event log view (read-only list of fired rules).

**Uses:** `Microsoft.Data.Sqlite` (inbox in .NET 10), Dapper for query mapping

**Implements:** `IHistoryStore` / `SqliteHistoryStore` architecture component

**Avoids:** Database locked under concurrent read/write (P8 — WAL mode), `busy_timeout` per-connection not global (P17), writing on every frame (ARCHITECTURE anti-pattern 2 — 10s debounce)

### Phase 5: Rules Engine Extension and Settings UI

**Rationale:** Rules engine extension is lowest risk when history and BLE are stable — it shares the same `StateChanged` pathway. The settings UI is the most complex Avalonia work in the milestone and benefits from having all the systems it configures (connection mode, thresholds, rules) fully working first. Rules UI correctness is easiest to verify when the underlying engine is demonstrably functional.

**Delivers:** `IActionHandler` strategy dispatcher refactoring `ActionRunner`; `WebhookActionHandler` with Polly retry (3 attempts, 30s max); `Channel<RuleAction>` async dispatch decoupling actions from notification pipeline; working settings page with threshold configuration, notification preferences, connection mode toggle, and rule create/edit/delete UI.

**Addresses:** Webhook action type (differentiator), working settings page (table stakes), multiple actions per rule (differentiator), OS notification wiring (table stakes)

**Avoids:** Rules engine blocking notification pipeline (P9 — Channel-based async dispatch), Avalonia UserControl silent binding failures (P10 — TemplatedControl base class, no DataContext=this), `CycleConnectionMode` persisting broken config (P15 — save config after restart success)

**Research flag:** Rules UI (create/edit form with Avalonia data validation) is rated HIGH complexity in FEATURES.md. Recommend `/gsd:research-phase` specifically for the rule wizard form pattern in Avalonia before implementing.

### Phase Ordering Rationale

- Phase 1 must come first: the synchronous logger and bare catches are active blockers that will hide errors in every subsequent phase. The connection state machine must exist before BLE adapters are written.
- Phases 2 and 3 are parallel in capability but sequential in implementation: Windows first because it is better documented, then Linux reuses the validated interface.
- Phase 4 (history) is independent of Phases 2-3 but dependent on Phase 1. It can be developed on macOS while cross-platform BLE is being completed.
- Phase 5 (rules + settings UI) is last because it configures and depends on all other systems being functional. The rules engine extension is also the most risk-free when the rest of the stack is stable.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 2 (Windows BLE):** WinRT GattSession management and LE Privacy (rotating addresses) behavior in production — documented pitfalls exist but implementation details may vary by Windows build
- **Phase 3 (Linux BLE):** D-Bus policy configuration across distros (Arch, Fedora, Ubuntu LTS) and systemd service running as non-root — sparse documentation, distro-dependent
- **Phase 5 (Rules UI):** Avalonia data validation patterns for multi-field forms — Avalonia's validation is less mature than WPF; research specific patterns before building the rule wizard

Phases with standard, well-documented patterns (research-phase optional):
- **Phase 1 (Tech Debt):** Serilog setup, Polly pipeline configuration, and `Stateless` state machine patterns are all extensively documented with official guides
- **Phase 4 (SQLite History):** WAL mode, time-bucket queries, and `Channel<T>` write debouncing are standard patterns with verified sources

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All packages verified on NuGet at exact versions; WinRT and BlueZ APIs confirmed against official Microsoft/BlueZ docs; Polly v8 and EF Core 10 are official releases matching .NET 10 |
| Features | HIGH (table stakes) / MEDIUM (differentiators) | NUT, APC, CyberPower, Eaton docs are authoritative; EcoFlow app feature set inferred from public materials rather than API documentation |
| Architecture | HIGH | Patterns verified against official docs (WinRT, BlueZ, SQLite WAL, Polly); existing codebase interfaces confirmed adequate without modification; Stateless library verified against official GitHub |
| Pitfalls | HIGH (Windows/Linux BLE, MQTT) / MEDIUM (Avalonia UI) | BLE pitfalls confirmed against Nordic DevZone, Microsoft Q&A, Bleak issues — multiple independent sources agree; Avalonia UserControl pitfall confirmed by project's own CONCERNS.md |

**Overall confidence:** HIGH

### Gaps to Address

- **Linux Wayland tray icon (P11):** Avalonia's Wayland support is listed as "private preview" as of early 2026. Tray icon behavior on GNOME Wayland is known to fail silently. Not a blocker for Phase 3 but needs explicit handling before a Linux release is announced — validate on Ubuntu 24.04 specifically.

- **EcoFlow ECDH handshake on reconnect (P12):** The crypto reset on reconnect is documented as necessary, but the exact handshake re-entry point in `BleTransport` needs code-level verification during Phase 1/2 planning. The existing Type 1 encryption bug (CONCERNS.md line 47) should be fixed concurrently.

- **Serilog net10.0 TFM:** Serilog 4.x targets `netstandard2.0`, which works on .NET 10 but does not use .NET 10-specific APIs. `Serilog.Extensions.Logging` 10.0.0 explicitly lists .NET 10. This is acceptable but worth noting as a minor gap in the tech stack confidence.

- **EcoFlow MQTT broker rate-limit specifics:** The rate-limit behavior is confirmed from CONCERNS.md, but the exact threshold (connections/minute before lockout) is undocumented. The Polly circuit breaker parameters (3 failures, 30s break) are reasonable defaults but should be tunable via settings.

- **DeviceState thread safety:** The race condition between BLE and MQTT mutations in Auto mode (P16) is documented but deferred. A `lock` on `MonitorEntry.State` mutations should be added in Phase 1 alongside the connection state machine work to prevent races from being amplified by the new reconnect paths.

---

## Sources

### Primary (HIGH confidence)
- [Windows.Devices.Bluetooth Namespace — Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth?view=winrt-26100)
- [Bluetooth GATT Client — UWP (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client)
- [Bluetooth developer FAQ — desktop app capabilities (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/bluetooth-dev-faq)
- [SuessLabs/Linux.Bluetooth on GitHub](https://github.com/SuessLabs/Linux.Bluetooth)
- [Polly v8 Retry Strategy Documentation](https://www.pollydocs.org/strategies/retry.html)
- [Microsoft.Data.Sqlite overview (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)
- [SQLite WAL mode documentation](https://www.sqlite.org/wal.html)
- [Stateless state machine library (GitHub)](https://github.com/dotnet-state-machine/stateless)
- [NUT Features](https://networkupstools.org/features.html)
- [APC PowerChute Personal Edition](https://www.se.com/us/en/product-range/61934-powerchute-personal-edition/)
- [CyberPower PowerPanel Personal](https://www.cyberpowersystems.com/product/software/power-panel-personal/powerpanel-personal-windows/)
- [Nordic DevZone — WinRT BLE paired device unreachable](https://devzone.nordicsemi.com/f/nordic-q-a/48916/)
- [SQLite concurrent writes and "database is locked"](https://tenthousandmeters.com/blog/sqlite-concurrent-writes-and-database-is-locked-errors/)

### Secondary (MEDIUM confidence)
- [Linux.Bluetooth 5.67.1 on NuGet](https://www.nuget.org/packages/Linux.Bluetooth/) — API surface verified, distro behavior standard
- [Eaton Intelligent Power Manager](https://www.eaton.com/us/en-us/catalog/backup-power-ups-surge-it-power-distribution/eaton-intelligent-power-manager.models.html) — feature comparison
- [Suesslabs.com — .NET and Linux Bluetooth](https://suesslabs.com/csharp/net-and-linux-bluetooth/) — ServicesResolved pattern
- [EMQ — MQTT auto-reconnect best practices](https://www.emqx.com/en/blog/mqtt-client-auto-reconnect-best-practices)
- [Avalonia — Bringing Wayland support](https://avaloniaui.net/blog/bringing-wayland-support-to-avalonia)
- [Avalonia Discussion #17159 — Styled property in UserControl](https://github.com/AvaloniaUI/Avalonia/discussions/17159)
- [Microsoft Q&A — GetGattServicesAsync LE privacy retry](https://learn.microsoft.com/en-us/answers/questions/2280559/)

### Tertiary (LOW confidence)
- [Avalonia Discussion #10594 — BLE discussion](https://github.com/AvaloniaUI/Avalonia/discussions/10594) — confirms DI pattern; no concrete BLE guidance
- [Settings UX Best Practices (Toptal)](https://www.toptal.com/designers/ux/settings-ux) — general UX guidance, not domain-specific

---
*Research completed: 2026-03-30*
*Ready for roadmap: yes*
