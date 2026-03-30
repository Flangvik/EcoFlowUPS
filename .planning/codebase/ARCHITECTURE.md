# Architecture

**Analysis Date:** 2026-03-30

## Pattern Overview

**Overall:** MVVM (Model-View-ViewModel) with a layered multi-project architecture and platform abstraction via interfaces.

**Key Characteristics:**
- Clean separation into Core (domain logic), App (UI + orchestration), CLI (diagnostic), and Platform (OS-specific) projects
- CommunityToolkit.Mvvm source generators for observable properties and relay commands
- Event-driven data flow: device monitors emit `StateChanged` events on background threads; UI marshals to Avalonia's Dispatcher
- Platform services abstracted behind interfaces in Core, implemented per-OS in Platform projects, resolved at runtime via `PlatformServiceFactory`
- Two communication channels (MQTT cloud and BLE local) unified behind the `IDeviceMonitor` interface
- Protobuf-based wire protocol decoded by hand (custom decoder) and via generated code (Google.Protobuf for Delta 3)

## Projects & Dependencies

```
EcoFlowMonitor.Core          (class library, net10.0)
  |
  +-- EcoFlowMonitor.Platform.Windows  (class library, net10.0-windows)
  +-- EcoFlowMonitor.Platform.macOS    (class library, net10.0)
  +-- EcoFlowMonitor.Platform.Linux    (class library, net10.0)
  |
  +-- EcoFlowMonitor.App              (executable, net10.0 + net10.0-macos)
  |     references: Core + conditional Platform.{OS}
  |
  +-- EcoFlowMonitor.Cli              (executable, net10.0)
        references: Core only
```

- **Core** has zero upward dependencies -- all other projects depend on it.
- **Platform.{OS}** projects each reference Core and implement its interfaces.
- **App** references Core directly and conditionally references the appropriate Platform project via MSBuild conditions (`$([MSBuild]::IsOSPlatform(...))`).
- **Cli** references only Core and is a standalone diagnostic tool.

## Layers

**Core Layer (`EcoFlowMonitor.Core`):**
- Purpose: Domain logic, protocol decoding, device communication, state management, configuration, and platform interface definitions.
- Location: `service/src/EcoFlowMonitor.Core/`
- Contains: Models, State, Protocol, Client (MQTT + BLE), Config, Triggers, Actions, Platform interfaces, Logging
- Depends on: MQTTnet, BouncyCastle, Google.Protobuf, CommunityToolkit.Mvvm, NSec.Cryptography
- Used by: App, Cli, Platform.{OS}

**Platform Layer (`EcoFlowMonitor.Platform.{Windows,macOS,Linux}`):**
- Purpose: OS-specific implementations of platform service interfaces
- Location: `service/src/EcoFlowMonitor.Platform.{Windows,macOS,Linux}/`
- Contains: Notification, power actions (shutdown/sleep), startup registration, script runner, elevation check
- Depends on: Core (for interface definitions), OS-specific APIs
- Used by: App (loaded at runtime via reflection in `PlatformServiceFactory`)

**App Layer (`EcoFlowMonitor.App`):**
- Purpose: Avalonia UI application with MVVM ViewModels, Views, custom controls, and the `MonitorOrchestrator` service that ties everything together.
- Location: `service/src/EcoFlowMonitor.App/`
- Contains: ViewModels, Views (AXAML), Controls, Converters, Services (orchestrator, navigation, platform factory, CoreBluetooth adapter), Themes
- Depends on: Core, Platform.{OS} (conditional), Avalonia, LiveChartsCore, Microsoft.Extensions.DependencyInjection
- Used by: End user (executable)

**CLI Layer (`EcoFlowMonitor.Cli`):**
- Purpose: Diagnostic tool that connects to MQTT and dumps raw protobuf field data for reverse-engineering new device protocols.
- Location: `service/src/EcoFlowMonitor.Cli/`
- Contains: Single `Program.cs` (top-level statements)
- Depends on: Core
- Used by: Developer for protocol analysis

## Data Flow

**Cloud MQTT Flow:**

1. `LoginViewModel` calls `EcoFlowClient.LoginAsync()` to authenticate with `https://api.ecoflow.com`
2. `EcoFlowClient.GetAllDevicesAsync()` retrieves the device list; `GetMqttCredsAsync()` retrieves MQTT broker credentials
3. `MonitorOrchestrator.StartAsync()` creates a `MqttMonitor` per device with those credentials
4. `MqttMonitor.StartAsync()` opens an MQTT TLS connection, subscribes to `/app/device/property/{sn}`, and publishes a wake command to `/app/{userId}/{sn}/thing/property/get`
5. Incoming MQTT payloads go through `ProtobufDecoder.Dispatch()` which routes by `(cmdFunc, cmdId)`:
   - `(32, 50)` -> `DecodeBms()` -> `BmsData`
   - `(254, 21|22)` -> `DecodeDisplay()` -> `DisplayData`
   - `(32, 2)` -> `DecodeEms()` -> `EmsData`
6. Decoded data is merged into `DeviceState`; `PowerStateMachine.Update()` derives the new `PowerState`
7. `MqttMonitor` raises `StateChanged` event with the updated `DeviceState` and previous power status
8. `MonitorOrchestrator.OnStateChanged()` evaluates trigger rules via `TriggerEvaluator`, runs matched actions via `ActionRunner`, then raises `DeviceUpdated` event
9. `DashboardViewModel.OnDeviceUpdated()` receives the event and uses `Avalonia.Threading.Dispatcher.UIThread.Post()` to marshal onto the UI thread
10. `DeviceViewModel.UpdateFromState()` copies state into observable properties, which triggers AXAML bindings to refresh the UI

**BLE Flow:**

1. `BleScanViewModel` uses `BleScanner` (wrapping `IBleAdapter`) to discover EcoFlow devices via BLE advertisements
2. User selects a device; `MonitorOrchestrator.MergeBleScanResult()` adds/updates the `DeviceConfig`, then `StartBleForDevice()` creates a `BleMonitor`
3. `BleMonitor.StartAsync()` performs: BLE scan -> `BleTransport.ConnectAsync()` -> ECDH handshake (Type 7) or legacy key setup (Type 1) -> authentication packet
4. `BleTransport` receives BLE notifications, buffers them, and calls `BlePacketParser.TryParseFrame()` to extract 0x5A5A wire frames
5. Decrypted frames are parsed into `BlePacket` objects by `BlePacketParser.ParsePacket()`
6. `BleDispatcher.Dispatch()` routes packets by `(src, cmdSet, cmdId)`:
   - Delta 3: `(0x02, 0xFE, 0x15|0x16)` -> `BleProtoMapper.MapDelta3Display()` (uses generated protobuf `Pd335Sys.DisplayPropertyUpload`)
   - Legacy fallback: reuses `ProtobufDecoder.DecodeBms/Display/Ems()`
7. From here, identical to MQTT flow: merge into `DeviceState` -> `PowerStateMachine` -> `StateChanged` -> orchestrator -> UI

**State Management:**
- `DeviceState` is a mutable container holding `BmsData`, `DisplayData`, `EmsData`, `PowerState`, and metadata
- Each `DeviceState` instance is owned by a single `MonitorEntry` in the orchestrator -- one per device
- `PowerStateMachine` is a pure static function: `Update(PowerState current, DeviceState ds) -> PowerState` -- never mutates the input
- Observable state for the UI lives in `DeviceViewModel`, which is a CommunityToolkit.Mvvm `ObservableObject` with `[ObservableProperty]` attributes
- `DeviceViewModel.UpdateFromState(DeviceState)` maps domain state to observable properties with null-coalescing (keeps previous value if new data is null)

## Key Abstractions

**IDeviceMonitor:**
- Purpose: Unified interface for any device communication channel (MQTT or BLE)
- Location: `service/src/EcoFlowMonitor.Core/Client/IDeviceMonitor.cs`
- Implementations: `MqttMonitor` (`service/src/EcoFlowMonitor.Core/Client/MqttMonitor.cs`), `BleMonitor` (`service/src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs`)
- Pattern: Event-based (`StateChanged` event); `StartAsync()`/`StopAsync()` lifecycle; `IDisposable`

**IBleAdapter / IBleGattConnection:**
- Purpose: Platform-agnostic BLE scanning and GATT operations
- Location: `service/src/EcoFlowMonitor.Core/Platform/IBleAdapter.cs`
- Implementations: `CoreBluetoothBleAdapter` (macOS, `service/src/EcoFlowMonitor.App/Services/CoreBluetoothBleAdapter.cs`), `StubBleAdapter` (fallback, in `PlatformServiceFactory.cs`)
- Pattern: Factory (`IBleAdapter.CreateConnection()` returns `IBleGattConnection`); async event-based scanning

**IBleCryptoSession:**
- Purpose: Encrypt/decrypt BLE transport frames
- Location: `service/src/EcoFlowMonitor.Core/Protocol/BleCrypto.cs`
- Implementations: `BleCryptoLegacy` (Type 1, AES-256-CBC with MD5-derived keys), `BleCryptoModern` (Type 7, ECDH SECP160r1 -> AES-128-CBC with session key)

**Platform Service Interfaces:**
- `INotificationService` - OS notifications (`service/src/EcoFlowMonitor.Core/Platform/INotificationService.cs`)
- `IPowerActionService` - Shutdown/hibernate/sleep (`service/src/EcoFlowMonitor.Core/Platform/IPowerActionService.cs`)
- `IStartupService` - Auto-start on login (`service/src/EcoFlowMonitor.Core/Platform/IStartupService.cs`)
- `IScriptRunnerService` - Execute scripts (`service/src/EcoFlowMonitor.Core/Platform/IScriptRunnerService.cs`)
- `IElevationService` - Check/request admin privileges (`service/src/EcoFlowMonitor.Core/Platform/IElevationService.cs`)
- All defined in `service/src/EcoFlowMonitor.Core/Platform/`

**MonitorOrchestrator:**
- Purpose: Central coordinator that manages all device monitors, evaluates trigger rules, and dispatches actions
- Location: `service/src/EcoFlowMonitor.App/Services/MonitorOrchestrator.cs`
- Pattern: Owns a `List<MonitorEntry>` (record of `DeviceConfig + DeviceState + IDeviceMonitor`); provides `DeviceUpdated` event for the UI layer

**NavigationService:**
- Purpose: Simple view-model navigation for single-window app
- Location: `service/src/EcoFlowMonitor.App/Services/NavigationService.cs`
- Pattern: `CurrentView` observable property; `MainWindow.axaml` uses `DataTemplate`s to resolve ViewModel types to View types

## Entry Points

**GUI Application:**
- Location: `service/src/EcoFlowMonitor.App/Program.cs`
- Triggers: User launches the application
- Responsibilities: Single-instance mutex, global exception handlers, Avalonia app builder -> `App.OnFrameworkInitializationCompleted()` sets up DI container, creates `MainWindow`, tray icon

**CLI Diagnostic:**
- Location: `service/src/EcoFlowMonitor.Cli/Program.cs`
- Triggers: Developer runs from terminal
- Responsibilities: Connects to MQTT using saved credentials, subscribes to all device topics, dumps unique protobuf field structures to console for 30 seconds

**App Initialization (DI container setup):**
- Location: `service/src/EcoFlowMonitor.App/App.axaml.cs` (`OnFrameworkInitializationCompleted`)
- Registers: `AppConfig` (singleton), platform services via `PlatformServiceFactory.Register()`, `MonitorOrchestrator`, `NavigationService`, all ViewModels, `BleScanner`
- Container exposed via `App.Services` static property (used by ViewModels to resolve dependencies)

## Threading Model

**Background Threads:**
- `MqttMonitor` callbacks (`OnMessageReceivedAsync`, `OnConnectedAsync`, `OnDisconnectedAsync`) fire on MQTTnet's thread pool threads
- `BleMonitor`'s `BleTransport.OnNotification()` fires on the CoreBluetooth callback thread
- `MonitorOrchestrator.StartAsync()` launches each monitor via `Task.Run()` (fire-and-forget)
- `ConnectLoopAsync()` patterns in both monitors run indefinite reconnect loops on background threads

**UI Thread Marshaling:**
- `DashboardViewModel.OnDeviceUpdated()` wraps all state updates in `Avalonia.Threading.Dispatcher.UIThread.Post()`
- `BleScanViewModel.OnDeviceDiscovered()` also marshals device list additions via `Dispatcher.UIThread.Post()`
- `StateChanged` and `DeviceUpdated` events intentionally fire on background threads -- callers are responsible for marshaling

**Thread Safety Concerns:**
- `DeviceState` is a mutable object accessed from both monitor background threads and the UI thread (via orchestrator event handler). No explicit synchronization -- relies on `Dispatcher.UIThread.Post()` to serialize UI reads
- `BleTransport._buffer` is protected by `_bufferLock` for concurrent notification handling
- `CoreBluetoothBleAdapter._discoveredPeripherals` is protected by `_peripheralLock`
- `Logger.Log()` uses a `lock(_lock)` for thread-safe file writes

## Event System

**StateChanged (IDeviceMonitor -> MonitorOrchestrator):**
- Source: `MqttMonitor`, `BleMonitor`
- Args: `StateChangedEventArgs` containing `DeviceState` and `PowerStatus PreviousPower`
- Fires on: Background thread (MQTT thread pool or BLE callback thread)
- Handler: `MonitorOrchestrator.OnStateChanged()` -- evaluates triggers, runs actions, re-raises as `DeviceUpdated`

**DeviceUpdated (MonitorOrchestrator -> DashboardViewModel):**
- Source: `MonitorOrchestrator`
- Args: `DeviceStateEventArgs` containing `DeviceState` and `string Source` ("Cloud", "BLE", or status messages)
- Fires on: Background thread (same thread as `StateChanged`)
- Handler: `DashboardViewModel.OnDeviceUpdated()` -- marshals to UI thread, updates `DeviceViewModel`

**DeviceDiscovered (BleScanner -> BleScanViewModel):**
- Source: `BleScanner`
- Args: `BleDeviceInfo`
- Fires on: CoreBluetooth callback thread
- Handler: `BleScanViewModel.OnDeviceDiscovered()` -- marshals to UI thread, adds to `ObservableCollection`

**NavigationService.PropertyChanged:**
- Source: `NavigationService.CurrentView` setter
- Handler: `MainWindowViewModel` constructor subscribes to update `CurrentPage`, which `MainWindow.axaml` binds to `ContentControl.Content`

## Trigger/Action System

**Rule Configuration:**
- Each `DeviceConfig` has a `List<RuleConfig>`, each rule has one `TriggerConfig` and multiple `ActionConfig` items
- Stored in `config.json` at `%APPDATA%/EcoFlowMonitor/config.json` (or equivalent on macOS/Linux)

**Trigger Evaluation:**
- `TriggerEvaluator.Evaluate()` is called on every `StateChanged` event in `MonitorOrchestrator.OnStateChanged()`
- Edge triggers: `PowerLost` (transition into PowerLost), `PowerRestored` (transition from PowerLost to Charging)
- Level triggers with 5-minute cooldown: `BatteryBelow(threshold)`, `TimeRemainingBelow(threshold)`
- Cooldown tracked per-rule in `DeviceState.RuleLastFired` dictionary

**Action Execution:**
- `ActionRunner.Run()` dispatches by `ActionType`: `RunScript`, `Shutdown`, `Hibernate`, `Sleep`, `Notification`, `WriteLog`
- `TemplateExpander.Expand()` replaces `{device}`, `{battery}`, `{remain}`, `{status}`, `{in_w}`, `{out_w}` in action text fields
- Actions delegate to platform services (`INotificationService`, `IPowerActionService`, `IScriptRunnerService`)

## Configuration System

**ConfigManager:**
- Location: `service/src/EcoFlowMonitor.Core/Config/ConfigManager.cs`
- Static class with `Load()` / `Save()` methods
- Stores `AppConfig` as JSON in `%APPDATA%/EcoFlowMonitor/config.json`
- Uses `System.Text.Json` with camelCase naming and pretty-printing

**AppConfig Structure:**
- `Account`: email + password (for EcoFlow cloud API)
- `Devices`: list of `DeviceConfig` (serial number, display name, connection mode, BLE parameters, rules)
- `General`: `StartWithWindows`, `ErrorLogPath`, `DarkMode`
- `LocalUserId`: generated GUID for BLE-only users
- `CloudUserId`: from EcoFlow API login response

## Error Handling

**Strategy:** Defensive with catch-and-log. Critical paths (MQTT/BLE monitors) swallow exceptions and retry.

**Patterns:**
- Monitor connect loops: catch all exceptions, log, wait 5s, retry indefinitely
- `MqttMonitor.OnMessageReceivedAsync()`: bare `catch { }` swallows decode errors to prevent monitor crash
- `MonitorOrchestrator.OnStateChanged()`: individual action execution wrapped in try/catch per action
- `ProtobufDecoder.Dispatch()` / `BleDispatcher.Dispatch()`: return `false` on any exception
- `Program.cs`: `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` log to `crash.log`

## Cross-Cutting Concerns

**Logging:**
- `Logger` static class (`service/src/EcoFlowMonitor.Core/Logging/Logger.cs`)
- Writes timestamped lines to `%APPDATA%/EcoFlowMonitor/debug.log` (or custom path from config)
- Thread-safe via `lock(_lock)` on file writes
- Used extensively throughout Core and App layers

**Validation:**
- Minimal explicit validation -- primarily null checks via `ArgumentNullException` in constructors
- Config loading defaults to `new AppConfig()` on any error

**Authentication:**
- Cloud: EcoFlow REST API login -> Bearer token -> MQTT credentials
- BLE: MD5(userId + serialNumber) sent as hex payload via BLE auth packet
- ECDH handshake (Type 7): SECP160r1 key exchange -> session key derivation via embedded lookup table (`keydata.b64`)

**Dependency Injection:**
- `Microsoft.Extensions.DependencyInjection` configured in `App.axaml.cs`
- Container exposed as `App.Services` static property
- ViewModels resolved from DI container: singletons (`MainWindowViewModel`) and transients (`LoginViewModel`, `DashboardViewModel`, `SettingsViewModel`, `BleScanViewModel`)
- Platform services registered via `PlatformServiceFactory.Register()` using runtime OS detection and reflection-based assembly loading

---

*Architecture analysis: 2026-03-30*
