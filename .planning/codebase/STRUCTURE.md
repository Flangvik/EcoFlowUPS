# Codebase Structure

## Directory Tree

```
EcoFlowUPS/
├── .env.example                    # Template for environment variables
├── .gitignore
├── .planning/
│   └── codebase/
│       ├── ARCHITECTURE.md         # High-level architecture decisions
│       ├── CONCERNS.md             # Known issues and technical debt
│       ├── STACK.md                # Technology stack overview
│       └── STRUCTURE.md            # This file
├── poc/                            # Python proof-of-concept scripts
│   ├── config.json.example         # Example config for PoC scripts
│   ├── data_dump.py                # Raw MQTT data dumper
│   ├── decode_bms.py               # BMS protobuf decoder prototype
│   ├── decode_payloads.py          # Generic payload decoder
│   ├── ecoflow_status.py           # Status monitoring script
│   ├── README.md                   # PoC documentation
│   ├── requirements.txt            # Python deps: requests, paho-mqtt, pytest, rich
│   └── tests/
│       ├── __init__.py
│       └── test_ecoflow.py         # PoC unit tests
├── README.md                       # Project README
└── service/                        # .NET solution root
    ├── EcoFlowMonitor.sln          # LEGACY solution (single WinForms project, superseded)
    ├── EcoFlowMonitor/             # LEGACY WinForms project (not in active solution)
    │   ├── Actions/                # Old action implementations
    │   ├── Config/                 # Old config manager
    │   ├── Core/                   # Old core logic
    │   ├── Models/                 # Old data models
    │   ├── Triggers/               # Old trigger system
    │   └── UI/                     # WinForms UI (LoginForm, MainForm, etc.)
    ├── README.md                   # Service-level documentation
    └── src/                        # ACTIVE .NET 10 solution
        ├── EcoFlowMonitor.sln      # Active solution (5 projects)
        ├── EcoFlowMonitor.Core/            # Shared library (no UI)
        ├── EcoFlowMonitor.App/             # Avalonia UI application
        ├── EcoFlowMonitor.Cli/             # CLI diagnostic tool
        ├── EcoFlowMonitor.Platform.Windows/# Windows platform services
        ├── EcoFlowMonitor.Platform.macOS/  # macOS platform services
        └── EcoFlowMonitor.Platform.Linux/  # Linux platform services
```

---

## Active Solution: `service/src/EcoFlowMonitor.sln`

### Project Dependency Graph

```
EcoFlowMonitor.App ──────────> EcoFlowMonitor.Core
     │
     ├─ (Windows) ──────────> EcoFlowMonitor.Platform.Windows ──> EcoFlowMonitor.Core
     ├─ (macOS)   ──────────> EcoFlowMonitor.Platform.macOS   ──> EcoFlowMonitor.Core
     └─ (Linux)   ──────────> EcoFlowMonitor.Platform.Linux   ──> EcoFlowMonitor.Core

EcoFlowMonitor.Cli ─────────> EcoFlowMonitor.Core
```

Platform references are conditional -- each Platform project is only referenced when building on its matching OS via MSBuild conditions in the App csproj.

---

## EcoFlowMonitor.Core

**Target:** `net10.0` (class library)
**Namespace root:** `EcoFlowMonitor`
**Role:** All protocol logic, data models, state management, triggers, and actions. Zero UI dependencies.

### NuGet Dependencies

| Package | Purpose |
|---|---|
| MQTTnet 4.3.7 | MQTT client for EcoFlow cloud |
| MQTTnet.Extensions.ManagedClient 4.3.7 | Managed MQTT reconnection |
| CommunityToolkit.Mvvm 8.4.0 | MVVM source generators |
| NSec.Cryptography 24.4.0 | Cryptographic primitives |
| BouncyCastle.Cryptography 2.5.1 | ECDH SECP160r1 for BLE Type 7 encryption |
| Google.Protobuf 3.28.3 | Protobuf runtime for compiled .proto files |
| Grpc.Tools 2.68.1 | Protobuf compiler (build-time only) |

### Folder Layout

```
EcoFlowMonitor.Core/
├── Actions/
│   ├── ActionRunner.cs          # Dispatches actions by ActionType enum
│   ├── ActionType.cs            # Enum: RunScript, Shutdown, Hibernate, Sleep, Notification, WriteLog
│   ├── LogAction.cs             # Appends timestamped message to a log file
│   └── TemplateExpander.cs      # Expands {device}, {battery}, {remain}, {status}, {in_w}, {out_w}
├── Client/
│   ├── Ble/
│   │   ├── BleMonitor.cs        # IDeviceMonitor over BLE; ECDH handshake, auth, data merge
│   │   ├── BleScanner.cs        # Scans for EcoFlow BLE advertisements (manufacturer ID 46517)
│   │   └── BleTransport.cs      # GATT read/write; RFCOMM or Nordic UART; frame buffering
│   ├── EcoFlowClient.cs         # REST client: login, device list, MQTT credential fetch
│   ├── IDeviceMonitor.cs        # Interface: StateChanged event, StartAsync, StopAsync
│   ├── MqttCredentials.cs       # Record: Host, Port, Username, Password
│   └── MqttMonitor.cs           # IDeviceMonitor over MQTT; connect loop, wake command, dispatch
├── Config/
│   └── ConfigManager.cs         # JSON read/write to %AppData%/EcoFlowMonitor/config.json
├── Logging/
│   └── Logger.cs                # Static file logger to %AppData%/EcoFlowMonitor/debug.log
├── Models/
│   ├── AccountConfig.cs         # Email + password
│   ├── ActionConfig.cs          # Action type, script path, notification template, log path
│   ├── AppConfig.cs             # Root config: Account, Devices[], GeneralSettings, user IDs
│   ├── BleDeviceInfo.cs         # BLE scan result: name, address, SN, protocol ver, encryption type
│   ├── BmsData.cs               # Battery data: pct, voltage, current, temp, cells, cycles, SOH, energy
│   ├── ConnectionType.cs        # Enum: Cloud, Ble, Auto
│   ├── DeviceConfig.cs          # Per-device config: SN, display name, mode, rules, BLE fields
│   ├── DisplayData.cs           # Power data: total in/out, AC, solar, USB ports, AC status
│   ├── EmsData.cs               # Energy management: charge state, fan, UPS mode, BMS connections
│   ├── RuleConfig.cs            # Rule: ID, name, enabled, trigger, actions list
│   └── TriggerConfig.cs         # Trigger type + threshold
├── Platform/
│   ├── IBleAdapter.cs           # BLE scanning + GATT connection interfaces (+ BleAdvertisement class)
│   ├── IElevationService.cs     # Admin/root privilege check
│   ├── INotificationService.cs  # OS notification
│   ├── IPowerActionService.cs   # Shutdown, hibernate, sleep
│   ├── IScriptRunnerService.cs  # Execute external script
│   └── IStartupService.cs       # Launch-at-login management
├── Proto/
│   ├── pd303.proto              # Smart Home Panel proto (excluded from codegen -- duplicate field)
│   ├── pd335_bms_bp.proto       # Delta 3 BMS heartbeat + EMS heartbeat protos (compiled)
│   └── pd335_sys.proto          # Delta 3 system proto: DisplayPropertyUpload, ConfigWrite (compiled)
├── Protocol/
│   ├── BleCrypto.cs             # IBleCryptoSession + BleCryptoLegacy (Type 1) + BleCryptoModern (Type 7)
│   ├── BleDispatcher.cs         # Routes BLE packets by (src, cmdSet, cmdId) to decoders
│   ├── BlePacket.cs             # Parsed BLE packet: version, src/dst, cmdSet/cmdId, payload
│   ├── BlePacketBuilder.cs      # Builds 0xAA packets + 0x5A5A wire frames; auth packet helper
│   ├── BlePacketParser.cs       # Parses 0x5A5A frames and 0xAA application packets; CRC validation
│   ├── BleProtoMapper.cs        # Maps Delta 3 DisplayPropertyUpload protobuf to BmsData/DisplayData/EmsData
│   ├── Crc.cs                   # CRC-8/CCITT and CRC-16/ARC (MODBUS) lookup tables
│   ├── keydata.b64              # Embedded 65 KB lookup table for Type 7 session key derivation
│   └── ProtobufDecoder.cs       # Hand-rolled protobuf decoder for MQTT payloads; ParseOuter + Dispatch
├── State/
│   ├── DeviceState.cs           # Runtime state: BMS, Display, EMS, Power, connectivity, rule cooldowns
│   ├── PowerState.cs            # PowerStatus, last input watts, lost/restored timestamps
│   ├── PowerStateMachine.cs     # Pure function: (PowerState, DeviceState) -> PowerState
│   └── PowerStatus.cs           # Enum: Unknown, Idle, Charging, PowerLost
└── Triggers/
    ├── TriggerEvaluator.cs      # Evaluates rules with edge triggers and 5-min cooldowns
    └── TriggerType.cs           # Enum: PowerLost, PowerRestored, BatteryBelow, TimeRemainingBelow
```

### Key Design Notes -- Core

- **Two decoders exist for MQTT vs BLE.** `ProtobufDecoder` is a hand-rolled wire-format parser used for cloud MQTT messages (envelope -> header -> payload routing by cmdFunc/cmdId). `BleProtoMapper` uses the compiled protobuf `DisplayPropertyUpload` message for BLE Delta 3 data. `BleDispatcher` routes BLE packets and falls back to `ProtobufDecoder` for older device types.
- **IDeviceMonitor** is the abstraction for both `MqttMonitor` (cloud) and `BleMonitor` (local BLE). Both raise `StateChanged` with the same `DeviceState` model.
- **Platform interfaces** (`IBleAdapter`, `INotificationService`, etc.) live in Core under `Platform/`. Implementations live in the Platform.* projects.
- **PowerStateMachine** is a pure function with no side effects; it derives the next `PowerState` from current state and device readings.
- **BLE encryption** supports two schemes: Type 1 (legacy AES-256-CBC derived from serial number) and Type 7 (ECDH SECP160r1 key exchange with session key derivation from an embedded lookup table).

---

## EcoFlowMonitor.App

**Targets:** `net10.0` (desktop) and `net10.0-macos` (native CoreBluetooth)
**Namespace root:** `EcoFlowMonitor`
**Role:** Avalonia 11 cross-platform desktop GUI with MVVM architecture.

### NuGet Dependencies

| Package | Purpose |
|---|---|
| Avalonia 11.2.3 | UI framework |
| Avalonia.Desktop 11.2.3 | Desktop windowing |
| Avalonia.Themes.Fluent 11.2.3 | Fluent design theme |
| Avalonia.Fonts.Inter 11.2.3 | Inter font family |
| CommunityToolkit.Mvvm 8.4.0 | ObservableObject, RelayCommand, source generators |
| LiveChartsCore.SkiaSharpView.Avalonia 2.0.0-rc3.3 | Power history chart |
| Microsoft.Extensions.DependencyInjection 8.0.1 | Service container |

### Folder Layout

```
EcoFlowMonitor.App/
├── App.axaml                    # Application root: dark theme, style includes
├── App.axaml.cs                 # DI container setup, tray icon, navigation bootstrap
├── Program.cs                   # Entry point: single-instance mutex, crash logging, Avalonia builder
├── Controls/
│   ├── ArcBatteryGauge.cs       # Custom Avalonia Control: 270-degree arc with glow, color by %/status
│   ├── GlowStatusIndicator.cs   # Animated pulsing dot (connected/disconnected)
│   ├── PowerFlowDiagram.cs      # Animated node-based diagram: Grid/Solar -> Device -> AC/USB
│   ├── PowerHistoryChart.axaml  # LiveCharts wrapper for input/output power over time
│   ├── PowerHistoryChart.axaml.cs
│   ├── StatCard.axaml           # Reusable stat display: label, value, unit, color
│   ├── StatCard.axaml.cs
│   └── StepIndicator.cs         # Wizard step dots with completed/current/future states
├── Converters/
│   ├── CellVoltageColorConverter.cs   # Multi-value: IsMin/IsMax -> red/green/normal
│   ├── ScanningColorConverter.cs      # bool -> green/gray for BLE scan indicator
│   ├── StatusColorConverter.cs        # PowerStatus/bool -> colors/text (7 converters in one file)
│   └── WizardConverters.cs            # Step index and TriggerType matching converters
├── Services/
│   ├── CoreBluetoothBleAdapter.cs     # macOS-only: native CoreBluetooth IBleAdapter (excluded from non-macOS builds)
│   ├── MonitorOrchestrator.cs         # Creates/manages MqttMonitor and BleMonitor per device; merges BLE scans
│   ├── NavigationService.cs           # Simple observable CurrentView property
│   └── PlatformServiceFactory.cs      # Registers platform services via reflection; includes no-op + stub fallbacks
├── Themes/
│   ├── Controls.axaml           # Card, Accent/Ghost/Danger button, Badge, Sidebar styles
│   ├── DesignSystem.axaml       # Color palette, spacing tokens, corner radii
│   └── Typography.axaml         # Named text styles: DisplayLarge, HeadingSmall, MonoMedium, etc.
├── ViewModels/
│   ├── BleScanViewModel.cs      # BLE device discovery, connect, navigate to dashboard
│   ├── DashboardViewModel.cs    # Device list, monitoring start, refresh, connection mode cycling
│   ├── DeviceViewModel.cs       # Per-device observable state: battery, power, cells, EMS, history
│   ├── LoginViewModel.cs        # EcoFlow cloud login or BLE-only bypass
│   ├── MainWindowViewModel.cs   # Navigation shell: routes to login or dashboard based on config
│   ├── RuleWizardViewModel.cs   # Multi-step rule creation: name, trigger, threshold, actions
│   ├── SettingsViewModel.cs     # General settings: startup, dark mode, log path, sign out
│   └── ViewModelBase.cs         # Abstract base: extends CommunityToolkit ObservableObject
└── Views/
    ├── BleScanView.axaml(.cs)   # BLE scan UI
    ├── DashboardView.axaml(.cs) # Main dashboard with device list, stats, charts
    ├── LoginView.axaml(.cs)     # Cloud login form
    ├── MainWindow.axaml(.cs)    # Shell window: ContentControl bound to CurrentPage
    ├── RuleWizardView.axaml(.cs)# Step-by-step rule wizard
    └── SettingsView.axaml(.cs)  # Settings panel
```

### Key Design Notes -- App

- **DI is configured in `App.axaml.cs`.** `PlatformServiceFactory.Register()` loads platform assemblies by reflection based on OS detection. The `BleScanner` is a singleton; ViewModels are registered as transient (except `MainWindowViewModel` which is singleton).
- **Navigation** uses a simple `NavigationService` with an observable `CurrentView` property. `MainWindowViewModel` subscribes to changes and sets `CurrentPage`. Views are matched to ViewModels via Avalonia `DataTemplate` declarations in `MainWindow.axaml`.
- **`MonitorOrchestrator`** is the central coordination service. It creates `MqttMonitor` or `BleMonitor` instances per device, evaluates triggers on every `StateChanged`, runs actions, and exposes `DeviceUpdated` for the UI.
- **`DeviceViewModel`** is the richest ViewModel; it contains 30+ observable properties covering battery, power, EMS, cell voltages, and power history. `UpdateFromState()` maps `DeviceState` to all observable properties.
- **`CoreBluetoothBleAdapter`** is conditionally compiled only for `net10.0-macos` TFM. It wraps `CBCentralManager` and `CBPeripheral` delegate callbacks into async Task-based APIs matching `IBleAdapter`/`IBleGattConnection`.

---

## EcoFlowMonitor.Cli

**Target:** `net10.0`
**Role:** Diagnostic tool that connects to MQTT, subscribes to all configured devices, and dumps raw protobuf field maps for each unique (cmdFunc, cmdId) message type.

Single file: `Program.cs` (top-level statements). References only `EcoFlowMonitor.Core`.

---

## Platform Projects

Each platform project targets its OS-specific TFM, references `EcoFlowMonitor.Core`, and implements the 5 platform service interfaces.

### EcoFlowMonitor.Platform.Windows

**Target:** `net10.0-windows`
**NuGet:** `Microsoft.Toolkit.Uwp.Notifications` (toast notifications)

| File | Implements |
|---|---|
| `WindowsNotificationService.cs` | `INotificationService` (UWP toast) |
| `WindowsPowerActionService.cs` | `IPowerActionService` (Win32 API) |
| `WindowsStartupService.cs` | `IStartupService` (registry) |
| `WindowsScriptRunnerService.cs` | `IScriptRunnerService` (cmd.exe) |
| `WindowsElevationService.cs` | `IElevationService` (UAC) |

### EcoFlowMonitor.Platform.macOS

**Target:** `net10.0`

| File | Implements |
|---|---|
| `MacNotificationService.cs` | `INotificationService` (osascript) |
| `MacPowerActionService.cs` | `IPowerActionService` (osascript/pmset) |
| `MacStartupService.cs` | `IStartupService` (launchd plist) |
| `MacScriptRunnerService.cs` | `IScriptRunnerService` (bash/zsh) |
| `MacElevationService.cs` | `IElevationService` (admin check) |

### EcoFlowMonitor.Platform.Linux

**Target:** `net10.0`

| File | Implements |
|---|---|
| `LinuxNotificationService.cs` | `INotificationService` (notify-send) |
| `LinuxPowerActionService.cs` | `IPowerActionService` (systemctl) |
| `LinuxStartupService.cs` | `IStartupService` (.desktop autostart) |
| `LinuxScriptRunnerService.cs` | `IScriptRunnerService` (bash) |
| `LinuxElevationService.cs` | `IElevationService` (uid check) |

---

## Legacy WinForms Project: `service/EcoFlowMonitor/`

The original single-project WinForms application. Superseded by the `service/src/` multi-project Avalonia solution. It has its own `EcoFlowMonitor.sln` in `service/` pointing only to itself. Not part of the active build. Retained for reference.

---

## Naming Conventions

### Projects
- `EcoFlowMonitor.Core` -- shared logic
- `EcoFlowMonitor.App` -- UI host
- `EcoFlowMonitor.Cli` -- command-line tool
- `EcoFlowMonitor.Platform.{OS}` -- platform implementations

### Namespaces
- Root namespace is `EcoFlowMonitor` for Core and App (set via `<RootNamespace>`)
- Platform projects use `EcoFlowMonitor.Platform.{Windows|macOS|Linux}`
- Sub-namespaces mirror folder structure: `EcoFlowMonitor.Client.Ble`, `EcoFlowMonitor.Protocol`, `EcoFlowMonitor.State`, etc.

### Classes
- Interfaces: `I{Concept}` (e.g., `IBleAdapter`, `IDeviceMonitor`, `IPowerActionService`)
- Platform services: `{Platform}{Concept}Service` (e.g., `MacNotificationService`, `LinuxPowerActionService`)
- ViewModels: `{Feature}ViewModel` (e.g., `DashboardViewModel`, `BleScanViewModel`)
- Views: `{Feature}View` (e.g., `DashboardView`, `LoginView`)
- Controls: descriptive name (e.g., `ArcBatteryGauge`, `PowerFlowDiagram`, `StatCard`)
- Converters: `{What}Converter` (e.g., `StatusColorConverter`, `CellVoltageColorConverter`)
- Data models: simple nouns (e.g., `BmsData`, `DisplayData`, `DeviceConfig`)
- Enums: PascalCase values (e.g., `PowerLost`, `BatteryBelow`, `Shutdown`)

### Files
- One primary class per file, file name matches class name
- AXAML views paired with `.axaml.cs` code-behind
- Proto files named `pd{device_id}_{subsystem}.proto`
- Embedded resources use descriptive names (`keydata.b64`)

---

## File Organization Patterns

1. **Separation by layer:** Core has zero UI references. App depends on Core. Platform projects depend only on Core.
2. **Interface-driven platform abstraction:** All OS-specific behavior is behind interfaces in `Core/Platform/`. Implementations are loaded by reflection in `PlatformServiceFactory`.
3. **Feature folders within each project:** Code is grouped by domain concern (Actions, Client, Config, Models, Protocol, State, Triggers) rather than by technical role.
4. **MVVM with manual navigation:** Views are matched to ViewModels via DataTemplates. Navigation is a simple property swap, not a framework.
5. **Two protocol stacks:** MQTT (cloud) and BLE (local) share the same `DeviceState` model but have separate parsers, transport layers, and monitor implementations behind `IDeviceMonitor`.

---

## Where to Add New Code

### New communication protocol (e.g., LAN/WiFi direct)
1. Create `Core/Client/{Protocol}/{Protocol}Monitor.cs` implementing `IDeviceMonitor`
2. Add any transport helpers in the same folder
3. If it needs a platform adapter, add interface in `Core/Platform/` and implementations in each Platform project
4. Wire it into `MonitorOrchestrator.StartAsync()` with the appropriate `ConnectionMode` case
5. Add enum value to `Core/Models/ConnectionType.cs`

### New platform service (e.g., IBatteryMonitorService)
1. Define interface in `Core/Platform/I{ServiceName}.cs`
2. Implement in each Platform project: `Windows{ServiceName}.cs`, `Mac{ServiceName}.cs`, `Linux{ServiceName}.cs`
3. Add no-op fallback in `App/Services/PlatformServiceFactory.cs`
4. Register in the platform-specific `Register{OS}()` method in `PlatformServiceFactory`
5. Inject via constructor in whatever service or ViewModel needs it

### New trigger type
1. Add value to `Core/Triggers/TriggerType.cs`
2. Add evaluation logic as a new `case` in `TriggerEvaluator.ShouldFire()`
3. Add corresponding `TriggerMatchConverter` entry in `App/Converters/WizardConverters.cs`
4. Update `RuleWizardView.axaml` and `RuleWizardViewModel.cs` to expose the new trigger option

### New action type
1. Add value to `Core/Actions/ActionType.cs`
2. If it needs a platform service, create the interface + implementations (see "New platform service")
3. Inject the service into `ActionRunner` constructor and add a `case` in `ActionRunner.Run()`
4. If the action has template-expandable fields, add them to `TemplateExpander.Expand()`
5. Add fields to `Core/Models/ActionConfig.cs`
6. Update `RuleWizardViewModel` to expose the new action in the wizard

### New data model fields (e.g., new BMS telemetry)
1. Add nullable properties to the relevant model in `Core/Models/` (`BmsData.cs`, `DisplayData.cs`, `EmsData.cs`)
2. Map the protobuf field in `ProtobufDecoder` (MQTT) and/or `BleProtoMapper` (BLE)
3. For BLE Delta 3 data, the field is likely already in `pd335_sys.proto` -- check `DisplayPropertyUpload`
4. Add merge logic in `BleMonitor.MergeBms/MergeDisplay/MergeEms()` for BLE path
5. Add observable property in `DeviceViewModel` and map it in `UpdateFromState()`
6. Display it in `DashboardView.axaml` (typically inside a `StatCard` control)

### New custom control
1. Create in `App/Controls/` as either:
   - Pure code control (extend `Control`, override `Render`) -- e.g., `ArcBatteryGauge.cs`
   - AXAML UserControl (`.axaml` + `.axaml.cs`) -- e.g., `StatCard`
2. Use `StyledProperty` for bindable properties
3. Reference design system resources from `Themes/DesignSystem.axaml`

### New view/page
1. Create `App/ViewModels/{Feature}ViewModel.cs` extending `ViewModelBase`
2. Create `App/Views/{Feature}View.axaml` + `{Feature}View.axaml.cs`
3. Register the ViewModel in `App.axaml.cs` DI container
4. Add `DataTemplate` mapping in `MainWindow.axaml`
5. Navigate to it via `NavigationService.NavigateTo(viewModel)`

### New value converter
1. Create in `App/Converters/{Name}Converter.cs` implementing `IValueConverter` or `IMultiValueConverter`
2. Expose a `public static readonly` instance for XAML usage
3. Reference as `{x:Static converters:NameConverter.Instance}` in AXAML

### New device type support
1. If it uses a different protobuf schema:
   - Add `.proto` file in `Core/Proto/`
   - Add `<Protobuf>` item in Core csproj
   - Create mapper in `Core/Protocol/{Device}ProtoMapper.cs`
2. If it uses BLE, add routing rules in `BleDispatcher.Dispatch()` for the new (src, cmdSet, cmdId) tuples
3. If it uses MQTT, add routing in `ProtobufDecoder.Dispatch()` for new (cmdFunc, cmdId) values
4. The shared models (`BmsData`, `DisplayData`, `EmsData`) should accommodate most EcoFlow devices; extend only if needed

### Proto file changes
1. Edit or add `.proto` files in `Core/Proto/`
2. Register in `Core/EcoFlowMonitor.Core.csproj` under the `<Protobuf>` item group with `GrpcServices="None"`
3. Grpc.Tools auto-compiles at build time; generated C# goes into `obj/`
4. Note: `pd303.proto` is excluded because it has a duplicate field name that breaks C# codegen
