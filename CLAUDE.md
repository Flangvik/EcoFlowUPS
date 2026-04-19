<!-- GSD:project-start source:PROJECT.md -->
## Project

**EcoFlow UPS Monitor**

A cross-platform desktop app that monitors EcoFlow Delta 3 / Delta 3 Max battery stations in real-time via BLE and MQTT cloud connections. Displays live telemetry (battery, power, cell voltages, temperatures), detects power events (outage, restore), and automates responses through a configurable rules engine. Built with .NET 10 + Avalonia UI.

**Core Value:** Reliable, real-time power monitoring that never silently loses connection — the user always knows their power status and gets alerted when it changes.

### Constraints

- **Tech stack**: .NET 10 + Avalonia UI — already committed, not changing
- **Devices**: EcoFlow Delta 3 and Delta 3 Max only — both use pd335_sys protobuf
- **BLE libraries**: Need platform-native approaches — WinRT on Windows, BlueZ on Linux, CoreBluetooth on macOS
- **No test suite**: Zero C# unit tests currently — need to add alongside refactoring
- **EcoFlow API**: Undocumented, reverse-engineered — can break without notice
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Languages
- C# (latest LangVersion) - All .NET projects in `src/`
- AXAML - Avalonia UI markup in `src/EcoFlowMonitor.App/Views/` and `src/EcoFlowMonitor.App/Themes/`
- Python 3 - Reference/prototype implementation in `poc/`
- Protobuf (proto3/proto2) - Protocol schemas in `src/EcoFlowMonitor.Core/Proto/`
## Runtime
- .NET 10.0 (`net10.0`) - All projects in `src/` target net10.0
- Python 3.x - PoC scripts (no version pinned)
- NuGet (SDK-style .csproj) - No central package management; versions pinned per project
- pip - `poc/requirements.txt` for Python dependencies
- No lockfiles present (no `packages.lock.json`, no `pip freeze` output)
## Frameworks
- Avalonia 11.2.3 - Cross-platform desktop UI framework (`src/EcoFlowMonitor.App/`)
- Avalonia.Themes.Fluent 11.2.3 - Fluent design system theme
- Avalonia.Fonts.Inter 11.2.3 - Inter font family for typography
- CommunityToolkit.Mvvm 8.4.0 - MVVM source generators and base classes (used in both Core and App)
- pytest 7.4.0 - Python PoC tests only (`poc/tests/`)
- No .NET test project exists
- Grpc.Tools 2.68.1 - Compiles `.proto` files to C# at build time (`src/EcoFlowMonitor.Core/`)
- MSBuild SDK-style projects - Standard `dotnet build` / `dotnet run` workflow
- Conditional TFM multi-targeting: `EcoFlowMonitor.App` targets both `net10.0` and `net10.0-macos`
- Conditional project references: Platform projects loaded based on OS at build time
## Key Dependencies
- MQTTnet 4.3.7.1207 - MQTT client for EcoFlow cloud broker (`src/EcoFlowMonitor.Core/`)
- MQTTnet.Extensions.ManagedClient 4.3.7.1207 - Managed client extensions (referenced but plain `IMqttClient` is used)
- Google.Protobuf 3.28.3 - Protobuf runtime for generated message classes
- BouncyCastle.Cryptography 2.5.1 - SECP160r1 ECDH key exchange for BLE Type 7 encryption
- NSec.Cryptography 24.4.0 - Referenced in Core csproj (available for additional crypto if needed)
- Microsoft.Extensions.DependencyInjection 8.0.1 - Service container in `EcoFlowMonitor.App`
- LiveChartsCore.SkiaSharpView.Avalonia 2.0.0-rc3.3 - Power history charting in dashboard
- Microsoft.Toolkit.Uwp.Notifications 7.1.3 - Windows toast notifications (Windows platform project only)
- System.Text.Json - JSON serialization for config and REST API (built-in, no extra package)
- requests 2.31.0 - REST API calls
- paho-mqtt 1.6.1 - MQTT client
- rich 13.7.0 - Terminal dashboard rendering
## Solution Structure
- `src/EcoFlowMonitor.sln` - Multi-project solution (net10.0 Avalonia, cross-platform)
| Project | Target | Purpose |
|---------|--------|---------|
| `EcoFlowMonitor.Core` | net10.0 | Protocol, models, client, state, triggers, actions |
| `EcoFlowMonitor.App` | net10.0; net10.0-macos | Avalonia UI application |
| `EcoFlowMonitor.Cli` | net10.0 | CLI diagnostic tool for raw MQTT data dump |
| `EcoFlowMonitor.Platform.Windows` | net10.0-windows | Windows-specific platform services |
| `EcoFlowMonitor.Platform.macOS` | net10.0 | macOS-specific platform services |
| `EcoFlowMonitor.Platform.Linux` | net10.0 | Linux-specific platform services |
## Protobuf Tooling
- `pd335_sys.proto` - Delta 3 system/display messages (proto3, compiled to C#)
- `pd335_bms_bp.proto` - Delta 3 BMS/battery pack messages (proto3, compiled to C#)
- `pd303.proto` - Smart Panel protocol (proto2, excluded from build due to duplicate field name)
## Embedded Resources
- `src/EcoFlowMonitor.Core/Protocol/keydata.b64` - Base64-encoded 65,280-byte lookup table for BLE Type 7 session key derivation. Compiled as `<EmbeddedResource>`.
## Configuration
- Runtime config stored as JSON at `%APPDATA%/EcoFlowMonitor/config.json` on Windows, `~/Library/Application Support/EcoFlowMonitor/config.json` on macOS, `~/.config/EcoFlowMonitor/config.json` on Linux (resolved via `Environment.SpecialFolder.ApplicationData`)
- Python POC uses a separate `poc/config.json` (template at `poc/config.json.example`); the C# app does not read env vars or `.env` files
- Config managed by `src/EcoFlowMonitor.Core/Config/ConfigManager.cs` using `System.Text.Json`
- No `global.json` - uses whatever .NET SDK is installed
- No `Directory.Build.props` - each project defines its own settings
- Nullable reference types enabled (`<Nullable>enable</Nullable>`) on all modern projects
- Implicit usings enabled on all modern projects
## Platform Requirements
- .NET 10.0 SDK (preview/RC as of analysis date)
- macOS 14.0+ for `net10.0-macos` target (set in `SupportedOSPlatformVersion`)
- Xcode version validation disabled (`ValidateXcodeVersion=false`)
- No CI pipeline detected
- **macOS:** Desktop app with tray icon, CoreBluetooth BLE support, native notifications
- **Windows:** Desktop app with tray icon, UWP toast notifications, Windows-specific power actions
- **Linux:** Desktop app with tray icon, stub implementations for notifications/power
- Single-instance enforcement via named `Mutex` in `Program.cs`
- Crash logs written to `%APPDATA%/EcoFlowMonitor/crash.log`
## Platform Abstraction
| Interface | Purpose | Implementations |
|-----------|---------|-----------------|
| `IBleAdapter` | BLE scanning and connection | `CoreBluetoothBleAdapter` (macOS), `StubBleAdapter` (fallback) |
| `INotificationService` | System notifications | Windows, macOS, Linux implementations |
| `IPowerActionService` | Shutdown/hibernate/sleep | Windows, macOS, Linux implementations |
| `IStartupService` | Auto-start on login | Windows, macOS, Linux implementations |
| `IScriptRunnerService` | Execute scripts | Windows, macOS, Linux implementations |
| `IElevationService` | Admin/root elevation | Windows, macOS, Linux implementations |
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## Project Structure
| Project | Role |
|---|---|
| `EcoFlowMonitor.Core` | Shared library: models, protocol, state, config, logging, platform interfaces |
| `EcoFlowMonitor.App` | Avalonia desktop app: views, view models, controls, converters, services |
| `EcoFlowMonitor.Cli` | CLI diagnostic tool: top-level statements, raw MQTT dump |
| `EcoFlowMonitor.Platform.Windows` | Windows-specific implementations of platform interfaces |
| `EcoFlowMonitor.Platform.macOS` | macOS-specific implementations (CoreBluetooth BLE, osascript notifications) |
| `EcoFlowMonitor.Platform.Linux` | Linux-specific implementations |
## Coding Style
### Naming
- **PascalCase** for types, methods, properties, events, and public members.
- **camelCase with `_` prefix** for private fields: `_config`, `_monitors`, `_cts`.
- **ALL_CAPS** only for true constants in protocol code: `WireTypeVarint`, `PacketPrefix`, `EcoFlowManufacturerId`.
- **No `I` prefix abuse** -- interfaces genuinely start with `I` (e.g., `IBleAdapter`, `IDeviceMonitor`, `INotificationService`).
- **File names match type names** exactly: `BleMonitor.cs`, `PowerStateMachine.cs`, `StatCard.axaml.cs`.
### Casing in AXAML
- Static resource keys use PascalCase: `BackgroundPrimary`, `SurfaceCard`, `TextMuted`, `AccentPrimary`.
- Style class names use PascalCase: `Classes="Ghost"`, `Classes="CaptionUpper"`, `Classes="MonoMedium"`.
- Named controls use PascalCase: `x:Name="LabelText"`, `x:Name="ValueText"`.
### Formatting
- File-scoped namespaces everywhere: `namespace EcoFlowMonitor.Models;` (never block-scoped).
- Braces on new lines for type declarations and methods; single-line bodies use expression-bodied members or inline format.
- Properties on one line when using `[ObservableProperty]`: `[ObservableProperty] private string _email = "";`
- Multiple `[ObservableProperty]` fields stacked without blank lines between them.
- Regions not used. Logical sections delimited by comment banners: `// -- Battery / BMS --` or `// ------ Section ------`.
## C# Patterns
### Records
- `public record MqttCredentials(string Host, int Port, string Username, string Password);` -- positional record for simple DTOs.
- `public record PowerHistoryPoint(DateTime Time, int InputW, int OutputW);` -- data point for charts.
- `private record MonitorEntry(DeviceConfig Device, DeviceState State, IDeviceMonitor Monitor);` -- internal grouping in `MonitorOrchestrator`.
### Classes vs Records
- **Model classes** (`BmsData`, `DisplayData`, `EmsData`, `DeviceConfig`, `AppConfig`) are mutable classes with nullable properties and default initializers. These are deserialized from JSON config or populated incrementally from protocol messages.
- **State classes** (`DeviceState`, `PowerState`) are mutable -- fields updated as data arrives.
- **Enums** are simple, no `[Flags]`: `PowerStatus`, `ConnectionMode`, `TriggerType`, `ActionType`.
### Nullable Reference Types
- Nullable properties for data that may not be present yet: `public float? BatteryPct { get; set; }`, `public BmsData? Bms { get; set; }`.
- Null-coalescing assignment in merge helpers: `state.Bms ??= new BmsData();`.
- Null-conditional throughout: `_config.Account?.Email ?? ""`.
- `!` postfix operator used intentionally on values proven non-null by context: `Path.GetDirectoryName(_path)!`, `client.UserId!`.
### Static Classes
- `Logger` -- static singleton with `Log(string)`.
- `ConfigManager` -- static `Load()` / `Save()`.
- `PowerStateMachine` -- static `Update()` method, never mutates input.
- `TriggerEvaluator` -- static `Evaluate()` and `RecordFired()`.
- `ProtobufDecoder` -- static parsing and dispatch.
- `BlePacketBuilder`, `BlePacketParser`, `BleDispatcher`, `BleProtoMapper` -- all static.
- `TemplateExpander`, `LogAction`, `Crc`, `BleKeyData` -- all static.
## MVVM Patterns (CommunityToolkit.Mvvm)
### ViewModelBase
### [ObservableProperty]
### Partial Methods for Change Notification
### [RelayCommand]
### Manual ObservableObject Usage
### Computed Properties
## Avalonia Patterns
### StyledProperty Registration
### Custom Control Approach
### DataContext and DataTemplate Navigation
### Converter Pattern
### AXAML Patterns
- `x:DataType` is used on views for compile-time binding validation: `x:DataType="vm:DashboardViewModel"`.
- Typed data templates use `x:DataType`: `<DataTemplate x:DataType="vm:DeviceViewModel">`.
- Static resources referenced by key: `Background="{StaticResource BackgroundPrimary}"`.
- Built-in converters used directly: `ObjectConverters.IsNotNull`, `StringConverters.IsNotNullOrEmpty`.
- `RelativeSource` binding for reaching parent DataContext: `Command="{Binding DataContext.CycleConnectionModeCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"`.
- Inline styles within controls using `<ListBox.Styles>` blocks.
### Design System
- Background colors: `#0D0D0D` (primary), `#141414` (secondary)
- Surface colors: `#1A1A1A` (cards), `#222222` (hover), `#2A2A2A` (borders)
- Text colors: `#F0F0F0` (primary), `#888888` (secondary), `#555555` (muted)
- Accent: `#00D4AA` (teal primary)
- Status colors: green (`#00E676`), amber (`#FFB300`), red (`#FF5252`), gray (`#666666`)
- Separate `Typography.axaml` and `Controls.axaml` for text classes and button styles.
### UI Thread Marshaling
- `DashboardViewModel.OnDeviceUpdated` (events from `MonitorOrchestrator`)
- `BleScanViewModel.OnDeviceDiscovered` (events from `BleScanner`)
- `BleScanViewModel.ToggleScanAsync` completion callback
## Dependency Injection
### Platform Service Registration
- Loads platform assemblies at runtime: `Assembly.Load("EcoFlowMonitor.Platform.Windows")`
- Resolves types by string name: `asm.GetType("EcoFlowMonitor.Platform.Windows.WindowsNotificationService")`
- Falls back to no-op implementations for unsupported platforms
- BLE adapter uses `typeof(PlatformServiceFactory).Assembly.GetType()` for the CoreBluetooth adapter on macOS
## Logging Approach
### Logger.Log Static Pattern
- Thread-safe via `lock` on a static object.
- Appends to a single flat file with `[HH:mm:ss.fff]` timestamps.
- Silent failure -- `catch { }` on write errors.
- No log levels (everything is one level).
- No structured logging or log rotation.
### Logging Conventions
## Error Handling Patterns
### Catch-and-Swallow
### Catch-and-Log
### Catch-and-Surface
### Guard Clauses
### Global Exception Handlers
- `AppDomain.CurrentDomain.UnhandledException` -- writes to `crash.log`
- `TaskScheduler.UnobservedTaskException` -- writes to `crash.log`, calls `SetObserved()`
### ConfigManager Defensive Loading
## Async Patterns
### Task.Run for Background Work
### CancellationToken Usage
- `CancellationTokenSource.CreateLinkedTokenSource(ct)` used to chain cancellation.
- `CancelAfter(TimeSpan)` for timeouts on BLE operations.
- `WaitAsync(token)` on `TaskCompletionSource` for awaiting BLE handshake responses.
- Scan operations use `CancellationTokenSource` with `TimeSpan.FromSeconds(15)` timeout.
### ConfigureAwait
### Reconnect Loops
### TaskCompletionSource
- `_authTcs = new TaskCompletionSource<bool>()` -- waits for auth response packet.
- `_handshakeTcs = new TaskCompletionSource<byte[]>()` -- waits for ECDH key exchange response.
## Configuration Patterns
### JSON Config
### No Environment Variables in Service
## Protocol Conventions
### Protobuf Handling
### Data Merging
### Wire Format Constants
- BLE frame prefix: `0x5A5A`
- BLE packet prefix: `0xAA`
- BLE GATT UUIDs: RFCOMM (`00000001-...`) and Nordic UART (`6e400001-...`)
- MQTT topic patterns: `/app/device/property/{sn}`, `/app/{userId}/{sn}/thing/property/get`
- EcoFlow manufacturer ID: `46517`
## Event-Driven Architecture
- `StateChangedEventArgs(DeviceState, PowerStatus)` -- from monitors
- `DeviceStateEventArgs(DeviceState, string source)` -- from orchestrator to UI
## Single Instance Enforcement
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## Pattern Overview
- Clean separation into Core (domain logic), App (UI + orchestration), CLI (diagnostic), and Platform (OS-specific) projects
- CommunityToolkit.Mvvm source generators for observable properties and relay commands
- Event-driven data flow: device monitors emit `StateChanged` events on background threads; UI marshals to Avalonia's Dispatcher
- Platform services abstracted behind interfaces in Core, implemented per-OS in Platform projects, resolved at runtime via `PlatformServiceFactory`
- Two communication channels (MQTT cloud and BLE local) unified behind the `IDeviceMonitor` interface
- Protobuf-based wire protocol decoded by hand (custom decoder) and via generated code (Google.Protobuf for Delta 3)
## Projects & Dependencies
```
```
- **Core** has zero upward dependencies -- all other projects depend on it.
- **Platform.{OS}** projects each reference Core and implement its interfaces.
- **App** references Core directly and conditionally references the appropriate Platform project via MSBuild conditions (`$([MSBuild]::IsOSPlatform(...))`).
- **Cli** references only Core and is a standalone diagnostic tool.
## Layers
- Purpose: Domain logic, protocol decoding, device communication, state management, configuration, and platform interface definitions.
- Location: `src/EcoFlowMonitor.Core/`
- Contains: Models, State, Protocol, Client (MQTT + BLE), Config, Triggers, Actions, Platform interfaces, Logging
- Depends on: MQTTnet, BouncyCastle, Google.Protobuf, CommunityToolkit.Mvvm, NSec.Cryptography
- Used by: App, Cli, Platform.{OS}
- Purpose: OS-specific implementations of platform service interfaces
- Location: `src/EcoFlowMonitor.Platform.{Windows,macOS,Linux}/`
- Contains: Notification, power actions (shutdown/sleep), startup registration, script runner, elevation check
- Depends on: Core (for interface definitions), OS-specific APIs
- Used by: App (loaded at runtime via reflection in `PlatformServiceFactory`)
- Purpose: Avalonia UI application with MVVM ViewModels, Views, custom controls, and the `MonitorOrchestrator` service that ties everything together.
- Location: `src/EcoFlowMonitor.App/`
- Contains: ViewModels, Views (AXAML), Controls, Converters, Services (orchestrator, navigation, platform factory, CoreBluetooth adapter), Themes
- Depends on: Core, Platform.{OS} (conditional), Avalonia, LiveChartsCore, Microsoft.Extensions.DependencyInjection
- Used by: End user (executable)
- Purpose: Diagnostic tool that connects to MQTT and dumps raw protobuf field data for reverse-engineering new device protocols.
- Location: `src/EcoFlowMonitor.Cli/`
- Contains: Single `Program.cs` (top-level statements)
- Depends on: Core
- Used by: Developer for protocol analysis
## Data Flow
- `DeviceState` is a mutable container holding `BmsData`, `DisplayData`, `EmsData`, `PowerState`, and metadata
- Each `DeviceState` instance is owned by a single `MonitorEntry` in the orchestrator -- one per device
- `PowerStateMachine` is a pure static function: `Update(PowerState current, DeviceState ds) -> PowerState` -- never mutates the input
- Observable state for the UI lives in `DeviceViewModel`, which is a CommunityToolkit.Mvvm `ObservableObject` with `[ObservableProperty]` attributes
- `DeviceViewModel.UpdateFromState(DeviceState)` maps domain state to observable properties with null-coalescing (keeps previous value if new data is null)
## Key Abstractions
- Purpose: Unified interface for any device communication channel (MQTT or BLE)
- Location: `src/EcoFlowMonitor.Core/Client/IDeviceMonitor.cs`
- Implementations: `MqttMonitor` (`src/EcoFlowMonitor.Core/Client/MqttMonitor.cs`), `BleMonitor` (`src/EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs`)
- Pattern: Event-based (`StateChanged` event); `StartAsync()`/`StopAsync()` lifecycle; `IDisposable`
- Purpose: Platform-agnostic BLE scanning and GATT operations
- Location: `src/EcoFlowMonitor.Core/Platform/IBleAdapter.cs`
- Implementations: `CoreBluetoothBleAdapter` (macOS, `src/EcoFlowMonitor.App/Services/CoreBluetoothBleAdapter.cs`), `StubBleAdapter` (fallback, in `PlatformServiceFactory.cs`)
- Pattern: Factory (`IBleAdapter.CreateConnection()` returns `IBleGattConnection`); async event-based scanning
- Purpose: Encrypt/decrypt BLE transport frames
- Location: `src/EcoFlowMonitor.Core/Protocol/BleCrypto.cs`
- Implementations: `BleCryptoLegacy` (Type 1, AES-256-CBC with MD5-derived keys), `BleCryptoModern` (Type 7, ECDH SECP160r1 -> AES-128-CBC with session key)
- `INotificationService` - OS notifications (`src/EcoFlowMonitor.Core/Platform/INotificationService.cs`)
- `IPowerActionService` - Shutdown/hibernate/sleep (`src/EcoFlowMonitor.Core/Platform/IPowerActionService.cs`)
- `IStartupService` - Auto-start on login (`src/EcoFlowMonitor.Core/Platform/IStartupService.cs`)
- `IScriptRunnerService` - Execute scripts (`src/EcoFlowMonitor.Core/Platform/IScriptRunnerService.cs`)
- `IElevationService` - Check/request admin privileges (`src/EcoFlowMonitor.Core/Platform/IElevationService.cs`)
- All defined in `src/EcoFlowMonitor.Core/Platform/`
- Purpose: Central coordinator that manages all device monitors, evaluates trigger rules, and dispatches actions
- Location: `src/EcoFlowMonitor.App/Services/MonitorOrchestrator.cs`
- Pattern: Owns a `List<MonitorEntry>` (record of `DeviceConfig + DeviceState + IDeviceMonitor`); provides `DeviceUpdated` event for the UI layer
- Purpose: Simple view-model navigation for single-window app
- Location: `src/EcoFlowMonitor.App/Services/NavigationService.cs`
- Pattern: `CurrentView` observable property; `MainWindow.axaml` uses `DataTemplate`s to resolve ViewModel types to View types
## Entry Points
- Location: `src/EcoFlowMonitor.App/Program.cs`
- Triggers: User launches the application
- Responsibilities: Single-instance mutex, global exception handlers, Avalonia app builder -> `App.OnFrameworkInitializationCompleted()` sets up DI container, creates `MainWindow`, tray icon
- Location: `src/EcoFlowMonitor.Cli/Program.cs`
- Triggers: Developer runs from terminal
- Responsibilities: Connects to MQTT using saved credentials, subscribes to all device topics, dumps unique protobuf field structures to console for 30 seconds
- Location: `src/EcoFlowMonitor.App/App.axaml.cs` (`OnFrameworkInitializationCompleted`)
- Registers: `AppConfig` (singleton), platform services via `PlatformServiceFactory.Register()`, `MonitorOrchestrator`, `NavigationService`, all ViewModels, `BleScanner`
- Container exposed via `App.Services` static property (used by ViewModels to resolve dependencies)
## Threading Model
- `MqttMonitor` callbacks (`OnMessageReceivedAsync`, `OnConnectedAsync`, `OnDisconnectedAsync`) fire on MQTTnet's thread pool threads
- `BleMonitor`'s `BleTransport.OnNotification()` fires on the CoreBluetooth callback thread
- `MonitorOrchestrator.StartAsync()` launches each monitor via `Task.Run()` (fire-and-forget)
- `ConnectLoopAsync()` patterns in both monitors run indefinite reconnect loops on background threads
- `DashboardViewModel.OnDeviceUpdated()` wraps all state updates in `Avalonia.Threading.Dispatcher.UIThread.Post()`
- `BleScanViewModel.OnDeviceDiscovered()` also marshals device list additions via `Dispatcher.UIThread.Post()`
- `StateChanged` and `DeviceUpdated` events intentionally fire on background threads -- callers are responsible for marshaling
- `DeviceState` is a mutable object accessed from both monitor background threads and the UI thread (via orchestrator event handler). No explicit synchronization -- relies on `Dispatcher.UIThread.Post()` to serialize UI reads
- `BleTransport._buffer` is protected by `_bufferLock` for concurrent notification handling
- `CoreBluetoothBleAdapter._discoveredPeripherals` is protected by `_peripheralLock`
- `Logger.Log()` uses a `lock(_lock)` for thread-safe file writes
## Event System
- Source: `MqttMonitor`, `BleMonitor`
- Args: `StateChangedEventArgs` containing `DeviceState` and `PowerStatus PreviousPower`
- Fires on: Background thread (MQTT thread pool or BLE callback thread)
- Handler: `MonitorOrchestrator.OnStateChanged()` -- evaluates triggers, runs actions, re-raises as `DeviceUpdated`
- Source: `MonitorOrchestrator`
- Args: `DeviceStateEventArgs` containing `DeviceState` and `string Source` ("Cloud", "BLE", or status messages)
- Fires on: Background thread (same thread as `StateChanged`)
- Handler: `DashboardViewModel.OnDeviceUpdated()` -- marshals to UI thread, updates `DeviceViewModel`
- Source: `BleScanner`
- Args: `BleDeviceInfo`
- Fires on: CoreBluetooth callback thread
- Handler: `BleScanViewModel.OnDeviceDiscovered()` -- marshals to UI thread, adds to `ObservableCollection`
- Source: `NavigationService.CurrentView` setter
- Handler: `MainWindowViewModel` constructor subscribes to update `CurrentPage`, which `MainWindow.axaml` binds to `ContentControl.Content`
## Trigger/Action System
- Each `DeviceConfig` has a `List<RuleConfig>`, each rule has one `TriggerConfig` and multiple `ActionConfig` items
- Stored in `config.json` at `%APPDATA%/EcoFlowMonitor/config.json` (or equivalent on macOS/Linux)
- `TriggerEvaluator.Evaluate()` is called on every `StateChanged` event in `MonitorOrchestrator.OnStateChanged()`
- Edge triggers: `PowerLost` (transition into PowerLost), `PowerRestored` (transition from PowerLost to Charging)
- Level triggers with 5-minute cooldown: `BatteryBelow(threshold)`, `TimeRemainingBelow(threshold)`
- Cooldown tracked per-rule in `DeviceState.RuleLastFired` dictionary
- `ActionRunner.Run()` dispatches by `ActionType`: `RunScript`, `Shutdown`, `Hibernate`, `Sleep`, `Notification`, `WriteLog`
- `TemplateExpander.Expand()` replaces `{device}`, `{battery}`, `{remain}`, `{status}`, `{in_w}`, `{out_w}` in action text fields
- Actions delegate to platform services (`INotificationService`, `IPowerActionService`, `IScriptRunnerService`)
## Configuration System
- Location: `src/EcoFlowMonitor.Core/Config/ConfigManager.cs`
- Static class with `Load()` / `Save()` methods
- Stores `AppConfig` as JSON in `%APPDATA%/EcoFlowMonitor/config.json`
- Uses `System.Text.Json` with camelCase naming and pretty-printing
- `Account`: email + password (for EcoFlow cloud API)
- `Devices`: list of `DeviceConfig` (serial number, display name, connection mode, BLE parameters, rules)
- `General`: `StartWithWindows`, `ErrorLogPath`, `DarkMode`
- `LocalUserId`: generated GUID for BLE-only users
- `CloudUserId`: from EcoFlow API login response
## Error Handling
- Monitor connect loops: catch all exceptions, log, wait 5s, retry indefinitely
- `MqttMonitor.OnMessageReceivedAsync()`: bare `catch { }` swallows decode errors to prevent monitor crash
- `MonitorOrchestrator.OnStateChanged()`: individual action execution wrapped in try/catch per action
- `ProtobufDecoder.Dispatch()` / `BleDispatcher.Dispatch()`: return `false` on any exception
- `Program.cs`: `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` log to `crash.log`
## Cross-Cutting Concerns
- `Logger` static class (`src/EcoFlowMonitor.Core/Logging/Logger.cs`)
- Writes timestamped lines to `%APPDATA%/EcoFlowMonitor/debug.log` (or custom path from config)
- Thread-safe via `lock(_lock)` on file writes
- Used extensively throughout Core and App layers
- Minimal explicit validation -- primarily null checks via `ArgumentNullException` in constructors
- Config loading defaults to `new AppConfig()` on any error
- Cloud: EcoFlow REST API login -> Bearer token -> MQTT credentials
- BLE: MD5(userId + serialNumber) sent as hex payload via BLE auth packet
- ECDH handshake (Type 7): SECP160r1 key exchange -> session key derivation via embedded lookup table (`keydata.b64`)
- `Microsoft.Extensions.DependencyInjection` configured in `App.axaml.cs`
- Container exposed as `App.Services` static property
- ViewModels resolved from DI container: singletons (`MainWindowViewModel`) and transients (`LoginViewModel`, `DashboardViewModel`, `SettingsViewModel`, `BleScanViewModel`)
- Platform services registered via `PlatformServiceFactory.Register()` using runtime OS detection and reflection-based assembly loading
<!-- GSD:architecture-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd:quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd:debug` for investigation and bug fixing
- `/gsd:execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->



<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd:profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
