# Domain Pitfalls

**Domain:** Cross-platform BLE + MQTT cloud monitoring desktop app (.NET 10 / Avalonia)
**Researched:** 2026-03-30
**Scope:** Windows BLE (WinRT), Linux BLE (BlueZ), connection resilience, SQLite persistence, rules engine, Avalonia cross-platform rendering

---

## Critical Pitfalls

Mistakes that cause rewrites, data loss, or silent misbehavior in production.

---

### Pitfall 1: WinRT BluetoothLEDevice Paired-Device "Unreachable" on First GetGattServicesAsync

**What goes wrong:** On Windows, calling `GetGattServicesAsync()` on a previously-paired BLE device returns `GattCommunicationStatus.Unreachable` on the first attempt, with a 7-second timeout. The device appears offline even when it is powered on and in range. This does not happen with unpaired devices.

**Why it happens:** Windows retains stale cached connection state for paired devices. When LE Privacy is enabled (rotating MAC addresses), Windows initially tries to connect using the device's random address and fails to resolve the Resolvable Private Address (RPA) until the second attempt. The OS cache is consulted before attempting a live connection.

**Consequences:** The BLE adapter implementation for Windows will appear broken during development and in production. A naively-written connect loop will fail on every first attempt, producing misleading "device unreachable" errors in the UI.

**Prevention:**
1. Always call `GetGattServicesAsync(BluetoothCacheMode.Uncached)` rather than the cached overload.
2. After constructing `BluetoothLEDevice`, add a brief delay (500–1000 ms) before the first `GetGattServicesAsync` call to give the OS time to settle.
3. Treat the first `Unreachable` result as a retryable transient error, not a permanent failure. Retry with `BluetoothCacheMode.Uncached` before surfacing an error to the user.
4. Dispose the `BluetoothLEDevice` object fully between attempts — Windows caches the object and reusing the same instance without disposal can perpetuate the unreachable state.

**Detection:** Unit/integration tests that attempt a second connection to a paired device will succeed while the first attempt fails. If connection reliability tests pass on Linux/macOS but fail on Windows specifically for paired devices, this is the cause.

**Phase:** Windows BLE adapter implementation (IBleAdapter for WinRT).

**Sources:** [Nordic DevZone — WinRT BLE paired device unreachable](https://devzone.nordicsemi.com/f/nordic-q-a/48916/bluetooth-le-windows-10-using-winrt-c-code-works-if-device-not-paired-fails-with-unreachable-if-device-is-paired) | [Microsoft Q&A — GetGattServicesAsync LE privacy](https://learn.microsoft.com/en-us/answers/questions/2280559/retry-required-for-getgattservicesasync()-when-con) | [Bleak issue #1340 — GattSessionStatus.CLOSED](https://github.com/hbldh/bleak/issues/1340)

---

### Pitfall 2: WinRT GattCharacteristic ValueChanged Event Handler Accumulation on Reconnect

**What goes wrong:** Each time a characteristic is rediscovered and re-subscribed after a reconnect, a new `ValueChanged` event handler is added without the old one being removed. After N reconnections, the notification callback fires N times per BLE packet. This corrupts `DeviceState` (N identical writes per tick), produces log spam, and eventually causes exceptions in the packet decoder.

**Why it happens:** WinRT GATT objects (`BluetoothLEDevice`, `GattDeviceService`, `GattCharacteristic`) from prior connections are not automatically invalidated. If the implementation holds onto the old characteristic reference and re-adds a handler on reconnect, handlers accumulate.

**Consequences:** Double or triple decode of the same BLE frame. Race conditions in `DeviceState` mutations. GC pressure from accumulated event closures. The symptom (duplicate data) is subtle and may not be caught without careful logging.

**Prevention:**
1. Track subscription tokens. WinRT `ValueChanged` returns an `EventRegistrationToken`; store it and call `characteristic.ValueChanged -= handler` (or use the token revocation overload) before re-subscribing.
2. Dispose `GattDeviceService` and `BluetoothLEDevice` objects before each reconnect attempt. Creating a new `BluetoothLEDevice` from the device ID is the correct reconnect pattern.
3. Write a reconnection test that verifies only one decode fires per incoming BLE notification after three connect/disconnect cycles.

**Detection:** Add a counter to the BLE notification handler. After N reconnects, the counter should increment by exactly 1 per packet. If it increments by N, handler accumulation is occurring.

**Phase:** Windows BLE adapter implementation; also verify against `CoreBluetoothBleAdapter` on macOS.

**Sources:** [Microsoft Learn — GattCharacteristic.ValueChanged](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattcharacteristic.valuechanged) | [Microsoft Q&A — Multiply times ValueChanged](https://learn.microsoft.com/en-us/answers/questions/1191076/bluetooth-le-multiply-times-valuechanged-n)

---

### Pitfall 3: WinRT FromBluetoothAddressAsync vs FromIdAsync — Connection Model Mismatch

**What goes wrong:** Using `BluetoothLEDevice.FromBluetoothAddressAsync(address)` to connect does NOT initiate a GATT connection by itself. The connection is deferred until an actual operation (service discovery, read, write, or `GattSession.MaintainConnection = true`) is performed. This makes connect/disconnect state invisible and leads to false "connected" UI states.

**Why it happens:** The WinRT BLE model is lazy-connect by design. `FromBluetoothAddressAsync` just creates a handle; the OS connects on demand.

**Consequences:** `IBleGattConnection.IsConnected` will return true before the device is actually connected at the GATT layer. If the device is out of range, the failure only surfaces 7 seconds later when an operation is attempted, not at connection time. The current `IBleGattConnection` abstraction assumes an imperative connect model (like CoreBluetooth's `ConnectPeripheral`) that does not map cleanly to WinRT.

**Prevention:**
1. After obtaining `BluetoothLEDevice`, immediately perform `GetGattServicesAsync(BluetoothCacheMode.Uncached)` to force the connection. Treat success of this call as "connected."
2. Alternatively, set `GattSession.MaintainConnection = true` to make the OS maintain the connection persistently — but be aware this requires careful session management to avoid leaking sessions.
3. Expose `ConnectionStatus` from `BluetoothLEDevice.ConnectionStatusChanged` through the `IBleGattConnection` abstraction so the rest of the app can react to OS-level disconnects.

**Detection:** Log the time between `FromBluetoothAddressAsync` returning and the first packet arriving. If there is a 7-second gap on the first notification, lazy-connect is the cause.

**Phase:** Windows BLE adapter design and implementation.

**Sources:** [Microsoft Learn — GATT Client (UWP)](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client) | [Microsoft Learn — BluetoothLEDevice.FromBluetoothAddressAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.frombluetoothaddressasync)

---

### Pitfall 4: Linux BlueZ D-Bus Permission Failure at Runtime (Non-Root User)

**What goes wrong:** On Linux, calling `StartDiscoveryAsync()` or connecting to a BLE peripheral from a non-root process throws a D-Bus `org.bluez.Error.NotPermitted` or `Access Denied` exception. The app crashes or silently falls back to stub behavior. This is invisible during development if the developer ran as root or was in the `bluetooth` group.

**Why it happens:** BlueZ's D-Bus policy (`/etc/dbus-1/system.d/bluetooth.conf`) grants BLE operations only to processes owned by the `bluetooth` system group. Standard user accounts are not in this group by default. Some distributions (Debian-based) include the policy; others (Arch, Fedora, some Ubuntu variants) do not.

**Consequences:** BLE on Linux will work for the developer (who added themselves to the group) but fail silently for users who installed the app normally. The `PlatformServiceFactory` reflection-based registration will mask this: the adapter loads, but `ConnectAsync` throws, and bare catch blocks (27 of them in the current codebase) will swallow the error.

**Prevention:**
1. Document the `bluetooth` group requirement in install instructions: `sudo usermod -aG bluetooth $USER`.
2. Catch `DBusException` specifically in the Linux BLE adapter and surface a human-readable error: "Bluetooth permission denied — add your user to the 'bluetooth' group and log out/in."
3. Never let D-Bus permission errors fall into a bare catch. The current catch-all pattern in `BleTransport.ConnectAsync` (line 61) will hide this entirely.
4. Add a preflight check in `LinuxBleAdapter.InitializeAsync()` that attempts to read adapter state and fails fast with a diagnostic message if permissions are insufficient.

**Detection:** If the Linux BLE adapter silently never connects and no error appears in logs, check group membership with `id | grep bluetooth`. If the group is absent, this is the cause.

**Phase:** Linux BLE adapter implementation; also addressed by the general bare-catch cleanup.

**Sources:** [Raspberry Pi Forums — BLE without root](https://forums.raspberrypi.com/viewtopic.php?t=108581) | [GitHub — BlueZ D-Bus missing policy](https://github.com/raspberrypi/linux/issues/1494) | [Linux.Bluetooth library](https://github.com/SuessLabs/Linux.Bluetooth)

---

### Pitfall 5: BlueZ ServicesResolved Race — GATT Characteristics Unavailable Immediately After Connect

**What goes wrong:** On Linux, after `Connect()` returns successfully, GATT services are not yet available. Immediately calling `GetGattServicesAsync()` returns empty or throws. This is a BlueZ-specific behavior: service resolution is asynchronous and fires separately from the connection event.

**Why it happens:** BlueZ performs service discovery in the background after establishing the link-layer connection. The `ServicesResolved` D-Bus property starts as `false` and transitions to `true` after discovery completes (typically 1–3 seconds). WinRT and CoreBluetooth handle this internally; BlueZ exposes it explicitly.

**Consequences:** The Linux BLE adapter will appear to connect but then produce empty service lists. If the code immediately tries to find the notification characteristic after connect (as `BleMonitor` does), it will fail with a `KeyNotFoundException` or empty enumeration and abort the connection attempt.

**Prevention:**
1. After `Connect()`, poll or subscribe to the `ServicesResolved` D-Bus property and wait for it to become `true` before proceeding to service/characteristic discovery. `Linux.Bluetooth` provides `WaitForPropertyValueAsync("ServicesResolved", true, timeout)` for this.
2. Add a timeout (10 seconds) to this wait and surface a "Service discovery timed out" error rather than hanging indefinitely.

**Detection:** Connection appears to succeed (no exception) but characteristic lookup immediately returns empty. Adding a 3-second delay before service discovery makes it work — this confirms the race condition.

**Phase:** Linux BLE adapter implementation.

**Sources:** [Linux.Bluetooth README](https://github.com/SuessLabs/Linux.Bluetooth) | [Suesslabs.com — .NET and Linux Bluetooth](https://suesslabs.com/csharp/net-and-linux-bluetooth/)

---

### Pitfall 6: MQTT Fixed-Delay Retry Triggering EcoFlow Broker Rate-Limit Lockout

**What goes wrong:** The current `MqttMonitor` uses a fixed 5-second retry delay for both `ConnectLoopAsync` and `OnDisconnectedAsync`. After a network disruption, multiple rapid reconnection attempts trigger EcoFlow's broker rate limiter. The broker starts rejecting connections, and the fixed 5-second interval keeps hammering it, extending the lockout. This is already confirmed in CONCERNS.md.

**Why it happens:** Cloud MQTT brokers implement connection rate limits per client ID or IP to prevent abuse. Fixed-interval retries with no jitter cause a "thundering herd" pattern: every disconnect/reconnect cycle trains the broker to see the client as abusive.

**Consequences:** A 30-second network blip becomes a 5–10 minute outage because the broker locks the client out. The UI shows `IsConnected = false` indefinitely despite the network being back. This is the most user-visible reliability failure in the app today.

**Prevention:**
1. Replace the fixed 5-second delay with exponential backoff + jitter: base 5s, multiplier 2x, max 5 minutes, ±25% random jitter. Example intervals: 5s, 10s, 20s, 43s, 90s, 300s (capped).
2. Track consecutive failure count and expose it in the UI: "Reconnecting in 43s (attempt 4)."
3. On `OnDisconnectedAsync`, check whether the disconnect was clean (broker-initiated) vs. network loss, and use a shorter base delay for clean disconnects (device restart) vs. longer for rate-limit scenarios.
4. Verify EcoFlow's broker does not reject clean reconnects with a `ReasonCode` in MQTT 5 — if available, parse the disconnect reason before deciding backoff strategy.

**Detection:** Log the timestamps of reconnect attempts. If attempts are exactly 5 seconds apart for more than 3 cycles and no connection is established, the broker has likely rate-limited. A sudden reconnect success after a 60+ second gap (when no backoff is implemented) confirms broker-side lockout.

**Phase:** Connection resilience phase (MQTT reconnection). This is a confirmed bug — fix before any other reliability work.

**Sources:** [EMQ — MQTT auto-reconnect best practices](https://www.emqx.com/en/blog/mqtt-client-auto-reconnect-best-practices) | [DrDroid — MQTT client reconnection loop](https://drdroid.io/stack-diagnosis/mqtt-client-reconnection-loop)

---

### Pitfall 7: MQTT Silent Data Starvation — Bare Catch Hides Decode Failures

**What goes wrong:** `MqttMonitor.OnMessageReceivedAsync` has a bare `catch { }` at line 236. If protobuf decode consistently fails (e.g., after an EcoFlow firmware update that changes the message format), the connection appears healthy (`IsConnected = true`) but no data ever reaches `DeviceState`. The UI freezes on the last known values with no indication anything is wrong.

**Why it happens:** The catch block was added defensively to prevent a decode crash from killing the MQTT connection. This is the correct intent, but a bare catch with no logging or dead-man timer means decode failures are invisible.

**Consequences:** The app looks working but silently delivers stale data. For a UPS monitor whose core value is "you always know your power status," this is a catastrophic failure mode — the user thinks they have live data during a power event when they do not.

**Prevention:**
1. Replace bare `catch` with `catch (Exception ex)` that logs the exception (at least once per 60 seconds to avoid log spam via a rate-limited logger).
2. Track `_lastDataReceivedAt` timestamp on every successful decode. Add a watchdog: if `DateTime.UtcNow - _lastDataReceivedAt > TimeSpan.FromMinutes(5)`, surface a "No data received for 5 minutes" warning in the UI connection status.
3. Expose a `DataFreshness` property on `DeviceState` (or the monitor) so the UI can show a staleness indicator.

**Detection:** Set the EcoFlow device to airplane mode (MQTT disconnects) vs. leave connected with a corrupted decode path. The former causes `IsConnected = false`; the latter causes `IsConnected = true` with `LastUpdated` frozen. If the UI cannot distinguish these, the watchdog is missing.

**Phase:** Connection resilience phase. Also the general bare-catch cleanup.

**Sources:** CONCERNS.md — `MqttMonitor.OnMessageReceivedAsync` line 236

---

## Moderate Pitfalls

Mistakes that cause significant friction or bugs that are non-trivial to fix after the fact.

---

### Pitfall 8: SQLite "Database is Locked" from Multiple Threads Without WAL Mode

**What goes wrong:** With default journal mode (DELETE/ROLLBACK), only one connection can write to the SQLite file at a time. If the MQTT monitor (background ThreadPool thread) writes a telemetry row at the same moment the UI reads historical chart data, the write throws `SqliteException: database is locked (5)`. Without `busy_timeout`, this error is immediate and uncaught.

**Why it happens:** SQLite's default locking is file-level exclusive writes. In a desktop app where background monitors write continuously and UI reads on demand, collisions are frequent. The existing `DeviceState` thread-safety problem (CONCERNS.md) compounds this.

**Prevention:**
1. Enable WAL mode for the SQLite database on first open: `PRAGMA journal_mode=WAL;`. This allows concurrent readers while a writer is active.
2. Set `PRAGMA busy_timeout=3000;` on every connection open. Without this, lock contention immediately throws instead of waiting.
3. Use a single `SqliteConnection` for writes (owned by a dedicated writer service) and separate read connections per query. Do not share a single connection across threads.
4. Wrap all writes in `BEGIN IMMEDIATE` transactions to fail fast on lock acquisition rather than mid-transaction.
5. Use `Microsoft.Data.Sqlite` with `Mode=ReadWriteCreate` for the writer and `Mode=ReadOnly` for read-only query connections.

**Detection:** Enable SQLite error logging during development. "Database is locked" errors during UI chart load or during the first minutes of data ingestion indicate WAL mode is not enabled.

**Phase:** SQLite persistence implementation.

**Sources:** [SQLite WAL mode and connection strategies](https://dev.to/software_mvp-factory/sqlite-wal-mode-and-connection-strategies-for-high-throughput-mobile-apps-beyond-the-basics-eh0) | [SQLite concurrent writes and "database is locked"](https://tenthousandmeters.com/blog/sqlite-concurrent-writes-and-database-is-locked-errors/)

---

### Pitfall 9: Rules Engine Actions Blocking the Notification Pipeline

**What goes wrong:** When a power event fires a rule that executes a webhook, shell script, or email, the action runs synchronously on the thread that detected the event (the MQTT/BLE notification handler). A slow webhook (network timeout) or long-running shell script blocks the notification pipeline for the duration, causing BLE/MQTT data to queue up or be dropped.

**Why it happens:** The existing `TriggerEvaluator` and `ActionRunner` are pure logic classes. If actions are awaited directly in the event handler (the likely first implementation), the notification pipeline stalls.

**Consequences:** During a power outage — the exact moment rules matter most — the BLE/MQTT data pipeline is blocked by the outbound webhook call. Battery state updates stop flowing to the UI for the duration of the webhook call. If the webhook server is down, the 30-second timeout blocks monitoring for 30 seconds.

**Prevention:**
1. Fire rule actions on a `Channel<RuleAction>` producer/consumer pattern. The notification handler enqueues the action and returns immediately. A dedicated consumer task processes actions sequentially.
2. Apply a per-action timeout (e.g., 10 seconds for webhooks, 30 seconds for shell scripts) using `CancellationTokenSource.CancelAfter`.
3. Limit concurrent outbound rule actions (e.g., max 3 simultaneous) to prevent a burst of rules from saturating outbound network connections.
4. Never await rule actions in the BLE notification handler or MQTT message handler.

**Detection:** Simulate a slow webhook endpoint (add a 10-second delay) and observe whether BLE heartbeat packets continue flowing to the UI. If the dashboard freezes for 10 seconds, the pipeline is blocked.

**Phase:** Rules engine implementation.

---

### Pitfall 10: Avalonia TemplatedControl vs UserControl — Silent Binding Failures for Custom Controls

**What goes wrong:** Adding custom dependency-style properties to a `UserControl` (e.g., a new `StatCard`-like control) and binding them via XAML produces no data binding at runtime. The property reads its default value permanently. No exception is thrown and no binding error is logged.

**Why it happens:** In Avalonia, `UserControl` is not designed for custom attached or styled properties. Setting `DataContext = this` in the constructor (a common workaround) redirects bindings to the control itself, breaking external ViewModel bindings. The correct base class for reusable controls with custom properties is `TemplatedControl`. This exact issue caused the `StatCard` rewrite (CONCERNS.md line 27).

**Consequences:** New controls built in the rules wizard, settings page, or chart views will silently display wrong data. This has already burned development time once — it will do so again if not explicitly understood.

**Prevention:**
1. Any control with custom `AvaloniaProperty` declarations must inherit from `TemplatedControl`, not `UserControl`.
2. Never set `DataContext = this` in a control constructor. If a control needs self-referencing for template bindings, use `TemplateBinding` in AXAML or `RelativeSource={RelativeSource TemplatedParent}`.
3. Declare default binding modes explicitly on property declarations when bidirectional binding is needed.
4. Write a comment in every new `TemplatedControl` explaining why it is not a `UserControl` — this tribal knowledge is easily lost.

**Detection:** If a control's displayed value never updates after initial render, and the ViewModel property is definitely changing (verified by a debug breakpoint), the binding root is wrong. Check `DataContext` in the Avalonia DevTools (F12 in debug builds).

**Phase:** Any UI control work — settings page, rule wizard, chart controls.

**Sources:** [Avalonia Discussion #17159 — Styled Property in UserControl not working](https://github.com/AvaloniaUI/Avalonia/discussions/17159) | [Medium — Avalonia User vs Templated Control](https://medium.com/@adamciszewski/avalonia-user-vs-templated-control-code-examples-b05301baf3c0)

---

### Pitfall 11: Linux Wayland — System Tray Icon and Window Positioning Silently Fail

**What goes wrong:** On Linux desktops running Wayland (GNOME, KDE in Wayland mode), Avalonia's tray icon does not appear (GNOME has no StatusNotifierItem support by default), and `Window.Left`/`Window.Top` are ignored by the Wayland compositor. Notification toasts may also be absent depending on the desktop environment.

**Why it happens:** Wayland intentionally removes many X11 privileges (window positioning, global keyboard shortcuts, tray icon protocols). Avalonia falls back to XWayland for rendering, which allows most things to work, but tray icons use the `StatusNotifierItem` D-Bus protocol which requires compositor support. GNOME does not support it without a shell extension.

**Consequences:** If the roadmap includes a "minimize to tray" feature or tray-based power alerts, these will work on Windows, macOS, KDE Plasma (Wayland), and X11, but silently fail on GNOME Wayland. A user on Ubuntu 22.04+ (GNOME default) will see no tray icon and receive no tray notifications.

**Prevention:**
1. Do not rely on tray icon as the sole notification mechanism on Linux. Provide fallback to desktop notifications via `libnotify` / `INotificationService` D-Bus.
2. Test specifically on GNOME Wayland (Ubuntu default), not just KDE or X11.
3. Wayland support in Avalonia is listed as "private preview" as of early 2026 — avoid using Wayland-specific APIs; target XWayland compatibility for the initial cross-platform release.
4. Document the GNOME limitation in the settings UI: "Tray icon may not be visible on GNOME without the AppIndicator extension."

**Detection:** Run the app on Ubuntu 24.04 (GNOME Wayland default). If no tray icon appears, this is the cause.

**Phase:** Linux platform integration; UI notification system.

**Sources:** [Avalonia — Bringing Wayland support](https://avaloniaui.net/blog/bringing-wayland-support-to-avalonia) | [Avalonia Discussion #18404 — Wayland support](https://github.com/AvaloniaUI/Avalonia/discussions/18404)

---

### Pitfall 12: BLE Reconnect Does Not Re-Run ECDH Handshake

**What goes wrong:** After a BLE disconnect and reconnect, the `BleMonitor` re-establishes the GATT connection but may attempt to use the old session key from the previous handshake. The EcoFlow device issues a new challenge on every connection, so the old session key is invalid. Packets will be received but decryption will produce garbage bytes, and the protobuf decoder will throw (caught by a bare catch), producing silent data failure.

**Why it happens:** The `BleMonitor` currently has no automatic reconnection at all (CONCERNS.md). When reconnection is implemented, it must start the entire handshake sequence from the beginning — not resume from a saved state. The crypto context (`_crypto` in `BleTransport`) holds state that becomes invalid on disconnect.

**Consequences:** After any BLE drop and reconnect (device moved out of range, then back), the session key is stale. All subsequent packets are silently corrupted. The UI appears connected (`IsConnected = true`) but shows frozen telemetry.

**Prevention:**
1. On reconnect, always reset `_transport` to `crypto: null` and re-run `PerformEcdhHandshakeAsync` from scratch.
2. Also fix the existing Type 1 encryption bug (CONCERNS.md line 47) during the reconnect refactor — a reconnect exposes the same code path.
3. Store session key derivation logic in a dedicated method that is explicitly called after every new connection, not once at startup.

**Detection:** Connect, observe data flowing, physically move the device out of BLE range (or restart Bluetooth on the OS), wait for reconnect, observe whether data resumes. If the handshake is skipped on reconnect, data will be corrupt and silently dropped.

**Phase:** BLE connection resilience.

**Sources:** CONCERNS.md — `BleMonitor.cs` lines 211-219, BleTransport crypto

---

### Pitfall 13: PlatformServiceFactory Reflection Loading Masks Assembly Load Failures

**What goes wrong:** `PlatformServiceFactory.RegisterWindows/MacOS/Linux` loads platform assemblies by name string using reflection. If the assembly is missing (e.g., the Windows platform DLL is absent in a Linux build), the null-forgiving `!` operator on `GetType()` returns causes a `NullReferenceException` at the first service call, not at startup. The error message is unhelpful.

**Why it happens:** The current implementation (CONCERNS.md line 132) bypasses compile-time checking in favor of runtime reflection, which was necessary to avoid platform-specific compile dependencies. But no null checks with diagnostic messages exist.

**Consequences:** On an incorrectly packaged build (a common CI/CD mistake when adding Windows/Linux platform assemblies), users get a cryptic NullReferenceException deep in the service call stack, not "platform assembly missing." This turns packaging mistakes into untraceable production crashes.

**Prevention:**
1. Add explicit null checks after every reflection-based type lookup: `if (type == null) throw new InvalidOperationException($"Platform assembly '{assemblyName}' not found. Ensure the correct platform build is deployed.");`
2. Consider migrating to `#if WINDOWS / #if LINUX` conditional compilation or separate startup projects as the platform assembly list grows.
3. Add a startup self-test that verifies each registered platform service is not a stub, and logs a warning if BLE is stub-only (as it currently is on Windows/Linux).

**Detection:** Delete one of the platform DLLs from a build output and launch the app. If the error message is helpful, the null checks are in place. If it throws `NullReferenceException` with no context, they are not.

**Phase:** Platform assembly integration; improve before adding Windows/Linux BLE assemblies.

**Sources:** CONCERNS.md — `PlatformServiceFactory.cs` lines 47-76

---

## Minor Pitfalls

Issues that are annoying and produce bugs, but are recoverable without architecture changes.

---

### Pitfall 14: Synchronous Logger Blocks BLE Notification Pipeline

**What goes wrong:** `Logger.Log()` calls `File.AppendAllText()` synchronously under a global lock. `BleTransport` alone has 14 such calls per packet. At 2–4 Hz, this is 30–60 synchronous disk writes per minute from within the notification pipeline, blocking `_bufferLock` on every packet.

**Why it happens:** The logger has no level filtering, no buffering, and no async path (CONCERNS.md). It was the simplest possible first implementation.

**Consequences:** BLE packet processing latency increases by the time it takes to open, write, and close a file for each log call. At 4 Hz with 14 log lines per packet, this is ~56 file I/O operations per second blocking the packet parser. Under load (multiple devices), this causes noticeable processing lag.

**Prevention:** Introduce log levels before the Windows/Linux BLE work begins — otherwise BLE adapters will add more log calls into the same broken pattern. Minimum fix: add `Debug`/`Info`/`Warn`/`Error` levels and gate hex-dump calls behind `Logger.IsDebug`. Better fix: replace with a buffered async logger (e.g., `Channel<string>` producer/consumer with periodic flush).

**Phase:** Should be addressed in the logging/tech-debt phase before new BLE adapters are added.

**Sources:** CONCERNS.md — `Logger.cs`, `BleTransport.cs`

---

### Pitfall 15: CycleConnectionMode Persists Config Before Confirming Success

**What goes wrong:** `DashboardViewModel.CycleConnectionModeAsync()` saves the new connection mode to `config.json` before calling `RestartDeviceAsync()`. If the restart fails (e.g., WinRT BLE unavailable), the app persists in the new (broken) mode on next launch.

**Prevention:** Save config only after `RestartDeviceAsync()` returns success. Implement a "last known good" mode: if startup in the configured mode fails after N seconds, revert to the previously working mode and surface a warning.

**Phase:** Connection mode toggle / settings improvements.

**Sources:** CONCERNS.md — `DashboardViewModel.cs` lines 104-113

---

### Pitfall 16: DeviceState Thread Safety — Concurrent BLE and MQTT Mutations in Auto Mode

**What goes wrong:** In Auto mode during a transition, both `BleMonitor` and `MqttMonitor` may briefly run simultaneously, both mutating `DeviceState` on different ThreadPool threads. `DeviceState` has no locking. The current code "generally" prevents this but does not enforce it.

**Prevention:** Use `Interlocked` or a `lock` for `DeviceState` field mutations, or make the state object immutable (replace-on-update). `ConcurrentDictionary` for `RuleLastFired` (already noted in CONCERNS.md) is the minimum fix.

**Phase:** Connection resilience / state management hardening.

**Sources:** CONCERNS.md — `DeviceState.cs`, `MqttMonitor.cs` line 219

---

### Pitfall 17: SQLite `busy_timeout` Must Be Set Per-Connection, Not Once Globally

**What goes wrong:** `busy_timeout` is a per-connection PRAGMA, not a database-level setting. If the SQLite connection is returned to a pool and reused, or a new connection is opened without explicitly setting the PRAGMA, the new connection has the default timeout of 0 (immediate error on lock contention). This produces "database is locked" errors intermittently and only under concurrent load.

**Prevention:** Set `PRAGMA busy_timeout=3000;` in every connection open sequence, not just during initial setup. In `Microsoft.Data.Sqlite`, use a connection string with `Busy Timeout=3000` or execute the PRAGMA in an `Open()` event handler.

**Phase:** SQLite persistence implementation.

**Sources:** [SQLite concurrent writes — busy_timeout](https://tenthousandmeters.com/blog/sqlite-concurrent-writes-and-database-is-locked-errors/)

---

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|---|---|---|
| Windows BLE (WinRT) adapter | Paired-device Unreachable on first GetGattServicesAsync (P1) | Always use Uncached mode; retry after 500ms delay |
| Windows BLE (WinRT) adapter | ValueChanged handler accumulation on reconnect (P2) | Dispose BluetoothLEDevice fully; track subscription tokens |
| Windows BLE (WinRT) adapter | Lazy-connect model mismatch with IBleGattConnection (P3) | Force connection via GetGattServicesAsync; expose ConnectionStatusChanged |
| Linux BLE (BlueZ) adapter | D-Bus permission denied for non-root user (P4) | Fail fast with diagnostic; document bluetooth group requirement |
| Linux BLE (BlueZ) adapter | ServicesResolved race — characteristics unavailable after connect (P5) | Wait for ServicesResolved=true before service discovery |
| BLE reconnection resilience | Stale ECDH session key after reconnect (P12) | Always re-run full handshake on every new connection |
| MQTT reconnection resilience | Fixed-delay retry triggering broker rate-limit (P6) | Exponential backoff + jitter; this is a confirmed bug, fix first |
| MQTT reliability | Silent data starvation from bare catch (P7) | Last-data-received watchdog; rate-limited exception logging |
| SQLite persistence | Database locked under concurrent read/write (P8) | WAL mode + busy_timeout on every connection open |
| SQLite persistence | busy_timeout not set per-connection (P17) | Set PRAGMA in every connection open, not once globally |
| Rules engine | Actions blocking notification pipeline (P9) | Channel-based async dispatch; never await actions in notify handler |
| New Avalonia controls | Silent binding failure on UserControl custom properties (P10) | Use TemplatedControl base class; never set DataContext=this |
| Linux platform integration | Tray icon invisible on GNOME Wayland (P11) | Fallback to D-Bus notifications; document GNOME limitation |
| Platform assembly packaging | Reflection-based factory crashes without diagnostic (P13) | Add null checks with meaningful error messages |
| Logging refactor (pre-BLE work) | Synchronous logger blocking BLE notification pipeline (P14) | Add log levels; async flush; fix before adding new adapters |

---

## Sources

- [Microsoft Learn — GATT Client (UWP)](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client)
- [Microsoft Learn — BluetoothLEDevice Class](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice)
- [Nordic DevZone — WinRT BLE paired device unreachable](https://devzone.nordicsemi.com/f/nordic-q-a/48916/bluetooth-le-windows-10-using-winrt-c-code-works-if-device-not-paired-fails-with-unreachable-if-device-is-paired)
- [Microsoft Q&A — GetGattServicesAsync LE privacy retry](https://learn.microsoft.com/en-us/answers/questions/2280559/retry-required-for-getgattservicesasync()-when-con)
- [Bleak GitHub Issue #1340 — WinRT GattSessionStatus.CLOSED](https://github.com/hbldh/bleak/issues/1340)
- [SuessLabs Linux.Bluetooth](https://github.com/SuessLabs/Linux.Bluetooth)
- [Suesslabs.com — .NET and Linux Bluetooth](https://suesslabs.com/csharp/net-and-linux-bluetooth/)
- [Raspberry Pi Forums — BLE without root](https://forums.raspberrypi.com/viewtopic.php?t=108581)
- [EMQ — MQTT auto-reconnect best practices](https://www.emqx.com/en/blog/mqtt-client-auto-reconnect-best-practices)
- [DrDroid — MQTT client reconnection loop](https://drdroid.io/stack-diagnosis/mqtt-client-reconnection-loop)
- [SQLite WAL mode and connection strategies](https://dev.to/software_mvp-factory/sqlite-wal-mode-and-connection-strategies-for-high-throughput-mobile-apps-beyond-the-basics-eh0)
- [SQLite concurrent writes and "database is locked"](https://tenthousandmeters.com/blog/sqlite-concurrent-writes-and-database-is-locked-errors/)
- [Avalonia — Bringing Wayland support](https://avaloniaui.net/blog/bringing-wayland-support-to-avalonia)
- [Avalonia Discussion #17159 — Styled property in UserControl](https://github.com/AvaloniaUI/Avalonia/discussions/17159)
- [Medium — Avalonia User vs Templated Control](https://medium.com/@adamciszewski/avalonia-user-vs-templated-control-code-examples-b05301baf3c0)
