# Codebase Concerns

**Analysis Date:** 2026-03-30

## Tech Debt

**Verbose debug logging in BleTransport (production-grade data spew):**
- Issue: Every BLE notification, frame parse, decryption, and packet decode emits a `Logger.Log()` call with hex dumps of raw bytes. There are 14 log calls in a 172-line file. Each incoming BLE notification triggers 3-5 log lines including `Convert.ToHexString()` of raw data. At a typical 2-4 Hz heartbeat rate this produces hundreds of log lines per minute, all written synchronously via `File.AppendAllText()`.
- Files: `service/src/EcoFlowMonitor.Core/Client/Ble/BleTransport.cs` (lines 75, 102, 108, 125, 131, 136)
- Impact: Log file grows rapidly (megabytes per hour); synchronous file I/O under `_bufferLock` blocks the notification pipeline; no log level filtering means users cannot reduce noise without editing source.
- Fix approach: Introduce log levels (Debug, Info, Warn, Error) to `service/src/EcoFlowMonitor.Core/Logging/Logger.cs`. Guard hex-dump lines behind `Logger.IsDebug`. The Logger is a 28-line static class with no level support at all -- every call writes unconditionally.

**Pervasive verbose logging across all components:**
- Issue: 99 `Logger.Log()` calls across 10 files in `service/src/`. `CoreBluetoothBleAdapter.cs` alone has 30 log calls (720-line file). `BleMonitor.cs` has 19 calls. `MqttMonitor.cs` has 13 calls. None have severity levels; all hit disk via `File.AppendAllText()` under a global lock.
- Files: `service/src/EcoFlowMonitor.Core/Logging/Logger.cs`, all files in `service/src/EcoFlowMonitor.Core/Client/`, `service/src/EcoFlowMonitor.App/Services/CoreBluetoothBleAdapter.cs`
- Impact: Performance degradation from synchronous file writes under global lock. No way to tune verbosity at runtime. Log file has no rotation or size limits.
- Fix approach: Replace `Logger` with a proper logging framework (or at minimum add log levels and async file writes). Add log rotation or max file size.

**Two parallel protobuf decode paths (hand-rolled vs compiled):**
- Issue: MQTT messages are decoded by a hand-rolled protobuf decoder (`ProtobufDecoder.cs`, 408 lines) that manually parses wire-format bytes with `ReadVarint`, `DecodeFields`, etc. BLE messages for Delta 3 are decoded by compiled protobuf via `Google.Protobuf` using `.proto` files (`BleProtoMapper.cs` calls `Pd335Sys.DisplayPropertyUpload.Parser.ParseFrom`). The `BleDispatcher` even falls back to the hand-rolled decoder for non-Delta-3 devices (lines 42-56).
- Files: `service/src/EcoFlowMonitor.Core/Protocol/ProtobufDecoder.cs` (hand-rolled), `service/src/EcoFlowMonitor.Core/Protocol/BleProtoMapper.cs` (compiled), `service/src/EcoFlowMonitor.Core/Protocol/BleDispatcher.cs` (mixes both), `service/src/EcoFlowMonitor.Core/Proto/pd335_sys.proto`, `service/src/EcoFlowMonitor.Core/Proto/pd335_bms_bp.proto`
- Impact: Two decoders must be kept in sync for overlapping field semantics. The hand-rolled decoder lacks zigzag decode for some varint fields (uses `ToSigned64` cast instead of proper zigzag for field 9/TempC). Adding new device types requires deciding which decoder to extend. The `pd303.proto` is excluded from compilation due to duplicate field names (noted in `.csproj` comment line 20).
- Fix approach: Create `.proto` definitions for the MQTT envelope and BMS/Display/EMS messages, replacing the hand-rolled decoder. Use a single compiled-protobuf path for both MQTT and BLE. Fix `pd303.proto` field name conflicts.

**StatCard rewrite was needed due to Avalonia styled property binding failures:**
- Issue: `StatCard` was rewritten to use `OnLoaded` + `OnPropertyChanged` pattern with manual `FindControl<TextBlock>()` instead of standard XAML data binding. This bypasses Avalonia's normal binding system.
- Files: `service/src/EcoFlowMonitor.App/Controls/StatCard.axaml.cs` (lines 44-83)
- Impact: Fragile -- any change to the AXAML template must keep `LabelText`, `ValueText`, `UnitText` names in sync. Other controls may have similar binding issues that manifest as silent display failures.
- Fix approach: Investigate why styled property bindings fail (likely a `TemplatedControl` vs `UserControl` issue) and migrate back to standard bindings.

**Duplicate code between legacy WinForms project and new Avalonia project:**
- Issue: The `service/EcoFlowMonitor/` directory contains a complete legacy WinForms project with its own copies of `ProtobufDecoder.cs`, `DeviceState.cs`, `ConfigManager.cs`, `ActionRunner.cs`, `TriggerEvaluator.cs`, and model classes. These duplicate the newer `service/src/EcoFlowMonitor.Core/` implementations.
- Files: `service/EcoFlowMonitor/Core/ProtobufDecoder.cs` vs `service/src/EcoFlowMonitor.Core/Protocol/ProtobufDecoder.cs`, `service/EcoFlowMonitor/Core/DeviceState.cs` vs `service/src/EcoFlowMonitor.Core/State/DeviceState.cs`, `service/EcoFlowMonitor/Config/ConfigManager.cs` vs `service/src/EcoFlowMonitor.Core/Config/ConfigManager.cs` (plus ~20 more duplicate files)
- Impact: Bug fixes must be applied in two places. The legacy versions may drift from the current implementations.
- Fix approach: Delete or archive the `service/EcoFlowMonitor/` legacy project. It appears to be the original WinForms prototype superseded by the Avalonia cross-platform app.

## Known Bugs

**ECDH handshake dead code and redundant key setting:**
- Symptoms: In `BleMonitor.PerformEcdhHandshakeAsync`, the session key is set twice: once at line 211 with a confusing ternary expression (`modern.Decrypt(new byte[16])[..0].Length == 0` always evaluates to `true` since a zero-length slice always has length 0), and again at line 216 with the correct values. The first call is effectively dead code.
- Files: `service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs` (lines 211-216)
- Trigger: Every Type 7 BLE encryption handshake.
- Workaround: The second `SetSessionKey` call overwrites the first, so behavior is correct. The dead code is confusing but not harmful.

**Type 1 BLE encryption not wired to transport:**
- Symptoms: `SetType1Encryption()` at line 111 creates a `BleCryptoLegacy` instance and assigns it to `_crypto`, but the transport was created with `crypto: null` at line 92. The transport's crypto is never updated via `_transport.SetCrypto()` for Type 1 devices. Only Type 7 calls `_transport.SetCrypto()` (line 219).
- Files: `service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs` (lines 92, 111-118, 219)
- Trigger: Connecting to a legacy device with `BleEncryptionType == 1`.
- Workaround: No workaround -- Type 1 BLE encryption is likely broken for incoming frames (transport will not decrypt them).

## Security Considerations

**Plaintext credentials in config.json:**
- Risk: `AccountConfig` stores `Email` and `Password` in plaintext. `LoginViewModel.SignInAsync()` writes `Password = Password` directly to config. `ConfigManager.Save()` serializes to `config.json` in the user's AppData directory with no encryption.
- Files: `service/src/EcoFlowMonitor.Core/Models/AccountConfig.cs`, `service/src/EcoFlowMonitor.App/ViewModels/LoginViewModel.cs` (line 42), `service/src/EcoFlowMonitor.Core/Config/ConfigManager.cs`
- Current mitigation: File is stored in per-user AppData directory (`Environment.SpecialFolder.ApplicationData`).
- Recommendations: Use platform keychain/credential store (macOS Keychain, Windows Credential Manager, Linux libsecret). At minimum, encrypt the password field with DPAPI on Windows or a user-derived key. Never store the raw password -- store the API token instead, which can be refreshed.

**TLS certificate validation disabled for MQTT:**
- Risk: `MqttMonitor` builds MQTT client options with `WithCertificateValidationHandler(_ => true)`, which accepts any certificate including self-signed or expired ones. This enables MITM attacks on the MQTT connection carrying device credentials and telemetry.
- Files: `service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs` (lines 83-87)
- Current mitigation: None. All MQTT connections blindly trust any certificate.
- Recommendations: Remove the custom validation handler (MQTTnet will use system trust store by default). If EcoFlow's broker uses a non-standard CA, pin that specific certificate or add it to the trust store.

**BLE encryption uses SECP160r1 (deprecated weak curve):**
- Risk: The ECDH key exchange uses SECP160r1 which provides only ~80 bits of security, well below the NIST minimum of 112 bits. The session key derivation uses MD5 which has known collision attacks. These are dictated by the EcoFlow device firmware and cannot be changed client-side.
- Files: `service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs` (line 129), `service/src/EcoFlowMonitor.Core/Protocol/BleCrypto.cs` (lines 73-146)
- Current mitigation: This is the device's protocol -- the client must conform. BLE range limits physical attack surface.
- Recommendations: Document the security limitations. Consider logging a warning on first BLE connection. Do not send additional sensitive data beyond what the protocol requires.

**EcoFlow API password sent as base64 (not encrypted):**
- Risk: `EcoFlowClient.LoginAsync()` sends the password as `Convert.ToBase64String(Encoding.UTF8.GetBytes(password))` -- base64 encoding is not encryption. The HTTPS transport provides the actual security layer.
- Files: `service/src/EcoFlowMonitor.Core/Client/EcoFlowClient.cs` (line 35)
- Current mitigation: Connection uses HTTPS (`ApiHost = "https://api.ecoflow.com"`).
- Recommendations: This is the EcoFlow API's design -- the base64 encoding is required by their auth endpoint. No action needed beyond ensuring HTTPS is always used.

## Reliability Concerns

**MQTT connection can silently stop receiving messages:**
- Problem: The `MqttMonitor.OnMessageReceivedAsync` handler has a bare `catch` at line 236 that swallows all exceptions silently. If decoding consistently fails (e.g., protocol change), no error is ever surfaced. The connection appears healthy (`IsConnected = true`) but no data flows to the UI.
- Files: `service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs` (lines 194-242)
- Cause: The catch-all at line 236 (`catch { }`) exists to prevent decode errors from crashing the monitor, but it also hides connection-level issues.
- Improvement path: Log decode failures (with rate limiting). Add a "last data received" timestamp and surface a warning in the UI if no data arrives for N minutes. Add a watchdog timer that triggers reconnection after prolonged silence.

**No exponential backoff for MQTT reconnection:**
- Problem: Both `ConnectLoopAsync` (line 119) and `OnDisconnectedAsync` (line 185) use a fixed 5-second retry delay. If the EcoFlow broker rate-limits the client (which happens after rapid reconnections), the fixed retry interval can extend the rate-limit lockout.
- Files: `service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs` (lines 103-123, 176-188)
- Cause: Simple retry loop copied from the Python POC.
- Improvement path: Implement exponential backoff with jitter (e.g., 5s, 10s, 20s, 40s, max 5min). Track consecutive failures and surface the retry state in the UI.

**BLE connection depends on CoreBluetooth state machine with no reconnection:**
- Problem: If the BLE connection drops (device moves out of range, Bluetooth restarts), `BleMonitor` has no automatic reconnection. The `ConnectLoopAsync` only retries during the initial connection phase. Once connected and authenticated, a disconnection is not detected or recovered.
- Files: `service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs` (lines 47-66), `service/src/EcoFlowMonitor.Core/Client/Ble/BleTransport.cs`
- Cause: The `BleTransport` has no disconnection event. The `CoreBluetoothBleAdapter` receives `DisconnectedPeripheral` callbacks but these are not propagated back to `BleMonitor`.
- Improvement path: Add a `Disconnected` event to `BleTransport` and `IBleGattConnection`. Have `BleMonitor` subscribe to it and restart the `ConnectLoopAsync` on disconnection.

**Thread safety of DeviceState mutations from multiple monitors:**
- Problem: `DeviceState` is a plain class with no synchronization. `MqttMonitor.OnMessageReceivedAsync` mutates `_state.Bms`, `_state.Display`, `_state.Ems`, `_state.Power`, and `_state.LastUpdated` on ThreadPool threads. `BleMonitor.OnPacketReceived` does the same via `MergeBms`/`MergeDisplay`/`MergeEms`. In Auto mode, both monitors could theoretically run simultaneously during transitions.
- Files: `service/src/EcoFlowMonitor.Core/State/DeviceState.cs`, `service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs` (lines 219-233), `service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs` (lines 296-306)
- Cause: `DeviceState` has public settable properties with no locking.
- Improvement path: Make `DeviceState` immutable or add a lock around all mutations. Alternatively, ensure only one monitor is active per device at a time (which the current code generally does, but does not enforce).

**Bare catch blocks suppress important errors:**
- Problem: 27 bare `catch` or `catch { }` blocks across the codebase. Some are appropriate (platform service fallbacks), but several hide critical failures: `MqttMonitor.OnMessageReceivedAsync` (line 236), `ProtobufDecoder.Dispatch` (line 403), `ConfigManager.Load` (line 29), `BleTransport.ConnectAsync` RFCOMM fallback (line 61).
- Files: See full list from grep above; most critical are in `service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs`, `service/src/EcoFlowMonitor.Core/Protocol/ProtobufDecoder.cs`, `service/src/EcoFlowMonitor.Core/Config/ConfigManager.cs`
- Cause: Defensive coding pattern applied too broadly.
- Improvement path: Replace bare catches with typed exception handlers. Log the exception in error-path catches. Return error results instead of silently returning defaults.

## Performance Bottlenecks

**Synchronous file I/O for every log message:**
- Problem: `Logger.Log()` calls `File.AppendAllText()` for every single log message, under a global `lock`. At 99 log call sites -- many in hot paths like BLE notification handlers and MQTT message handlers -- this serializes all processing through synchronous disk writes.
- Files: `service/src/EcoFlowMonitor.Core/Logging/Logger.cs` (lines 19-27)
- Cause: Simplest possible logging implementation.
- Improvement path: Buffer log messages in memory and flush periodically (e.g., every 100ms or 100 lines). Use `StreamWriter` with buffering instead of `File.AppendAllText` which opens/writes/closes on every call. Consider `Channel<string>` for lock-free producer/consumer.

**MemoryStream buffer allocation in BleTransport:**
- Problem: `ProcessBuffer()` calls `_buffer.ToArray()` on every notification, which allocates a new byte array copy of the entire buffer. With high-frequency BLE notifications (multiple per second), this creates GC pressure.
- Files: `service/src/EcoFlowMonitor.Core/Client/Ble/BleTransport.cs` (lines 90-147)
- Cause: Simplest approach to buffer management.
- Improvement path: Use `_buffer.GetBuffer()` with `_buffer.Position` to avoid allocation. Or switch to `ArrayPool<byte>` based buffering.

## Architecture Concerns

**BLE only works on macOS (CoreBluetooth native interop):**
- Problem: The only real `IBleAdapter` implementation is `CoreBluetoothBleAdapter`, which uses macOS-specific `CoreBluetooth`, `Foundation`, and `NSData` types. Windows and Linux get `StubBleAdapter` which throws `NotSupportedException` on connection attempt.
- Files: `service/src/EcoFlowMonitor.App/Services/CoreBluetoothBleAdapter.cs` (macOS only), `service/src/EcoFlowMonitor.App/Services/PlatformServiceFactory.cs` (lines 32-44, 86-104)
- Impact: BLE mode is unavailable on Windows and Linux. Users on those platforms are limited to Cloud/MQTT mode.
- Fix approach: Implement `IBleAdapter` using platform-specific BLE libraries: `Windows.Devices.Bluetooth` for Windows, `BlueZ` D-Bus bindings for Linux. The `IBleAdapter`/`IBleGattConnection` abstractions are already designed for this.

**Platform service registration uses reflection and string-based type loading:**
- Problem: `PlatformServiceFactory.RegisterWindows/MacOS/Linux` loads platform assemblies by name string and resolves types by full name string. This bypasses compile-time checking and fails silently if assemblies are missing.
- Files: `service/src/EcoFlowMonitor.App/Services/PlatformServiceFactory.cs` (lines 47-76)
- Impact: NullReferenceException at runtime if a platform assembly is missing or type name changes. The `!` null-forgiving operator on `GetType()` return values masks the failure.
- Fix approach: Use conditional compilation (`#if WINDOWS`) or separate platform-specific startup projects instead of reflection. Or add null checks with meaningful error messages.

**CycleConnectionMode saves config but RestartDeviceAsync may fail silently:**
- Problem: `DashboardViewModel.CycleConnectionModeAsync()` saves config via `ConfigManager.Save()` before calling `_orchestrator.RestartDeviceAsync()`. If restart fails, the config is already persisted with the new mode. Next app launch will try the potentially-broken mode.
- Files: `service/src/EcoFlowMonitor.App/ViewModels/DashboardViewModel.cs` (lines 104-113), `service/src/EcoFlowMonitor.App/Services/MonitorOrchestrator.cs` (lines 200-237)
- Impact: User gets stuck on a non-working connection mode.
- Fix approach: Save config only after successful connection, or implement a "last known good" fallback mode.

## Missing Features / TODOs

**Rule wizard not connected from dashboard:**
- Problem: `DashboardViewModel.AddRule()` (line 100) has a `// TODO: open rule wizard for selected device` comment and empty body. The `RuleWizardViewModel` is fully implemented but unreachable from the UI.
- Files: `service/src/EcoFlowMonitor.App/ViewModels/DashboardViewModel.cs` (line 100), `service/src/EcoFlowMonitor.App/ViewModels/RuleWizardViewModel.cs`
- Blocks: Users cannot create automation rules (power-loss alerts, battery-low shutdowns) through the UI.

**Settings page partially functional:**
- Problem: `SettingsViewModel` saves general settings (dark mode, log path, startup) but has no UI for managing individual device connection modes, BLE re-pairing, or per-device rule management. The `SignOut` command clears all devices without confirmation.
- Files: `service/src/EcoFlowMonitor.App/ViewModels/SettingsViewModel.cs`
- Blocks: Device management and rule configuration from settings.

**No Windows or Linux BLE adapter implementation:**
- Problem: Only macOS has a real BLE adapter. `StubBleAdapter` throws on connect for Windows/Linux.
- Files: `service/src/EcoFlowMonitor.App/Services/PlatformServiceFactory.cs` (lines 86-104)
- Blocks: BLE-only devices on non-macOS platforms.

**No automated tests for .NET code:**
- Problem: The Python POC has 12 tests in `poc/tests/test_ecoflow.py`, but the .NET `service/src/` project has zero test projects, zero test files. No `*.Tests.csproj` exists. The `ProtobufDecoder`, `PowerStateMachine`, `TriggerEvaluator`, and `BleCrypto` are all pure logic that would be straightforward to test.
- Files: (none -- no test infrastructure exists)
- Blocks: Confidence in refactoring, especially the protobuf decoder and crypto code.

## Test Coverage Gaps

**Zero unit tests for the .NET codebase:**
- What's not tested: Everything. The protobuf decoder (408 lines of bit manipulation), BLE crypto (AES-CBC key derivation), power state machine, trigger evaluator, template expander, BLE packet parser/builder, config serialization.
- Files: All of `service/src/EcoFlowMonitor.Core/` -- none have corresponding test files.
- Risk: Regressions from any refactoring (especially the planned protobuf decoder unification) will go undetected. The crypto and protocol code is particularly risky to modify without tests.
- Priority: High -- the Python POC tests prove the pattern works; port them to xUnit/NUnit for the .NET code. Start with `ProtobufDecoder`, `PowerStateMachine`, `TriggerEvaluator`, and `BleCrypto`.

## Scaling Limits

**Single-device-at-a-time assumption in monitor architecture:**
- Current capacity: The `MonitorOrchestrator` supports multiple devices in `_monitors` list, but the UI (`DashboardViewModel`) only shows one device's details at a time via `SelectedDevice`.
- Limit: The `RuleLastFired` dictionary on `DeviceState` is a plain `Dictionary<string, DateTime>` with no concurrent-safe access. With many devices firing rules simultaneously, this could produce race conditions.
- Scaling path: Use `ConcurrentDictionary` for `RuleLastFired`. The UI limitation is by design (single-device dashboard view) but the backend supports multiple devices.

## Dependencies at Risk

**BouncyCastle for SECP160r1 (only used for one EC curve):**
- Risk: The BouncyCastle dependency (`BouncyCastle.Cryptography` 2.5.1) is pulled in solely because .NET's built-in `ECDiffieHellman` does not support SECP160r1 (a deprecated curve). This adds ~4MB to the binary for one specific BLE handshake path.
- Impact: Large dependency for narrow use case. If .NET adds SECP160r1 support (unlikely given deprecation), or if EcoFlow updates to SECP256r1, BouncyCastle can be removed.
- Migration plan: No immediate action needed. If binary size is a concern, consider using a lighter EC library or native interop for just the SECP160r1 computation.

---

*Concerns audit: 2026-03-30*
