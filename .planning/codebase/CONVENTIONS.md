# Codebase Conventions

Documented from the actual code as of the current state. This is a rapidly developed project -- patterns reflect practical choices, not aspirational standards.

---

## Project Structure

Six projects in the solution (`EcoFlowMonitor.sln`):

| Project | Role |
|---|---|
| `EcoFlowMonitor.Core` | Shared library: models, protocol, state, config, logging, platform interfaces |
| `EcoFlowMonitor.App` | Avalonia desktop app: views, view models, controls, converters, services |
| `EcoFlowMonitor.Cli` | CLI diagnostic tool: top-level statements, raw MQTT dump |
| `EcoFlowMonitor.Platform.Windows` | Windows-specific implementations of platform interfaces |
| `EcoFlowMonitor.Platform.macOS` | macOS-specific implementations (CoreBluetooth BLE, osascript notifications) |
| `EcoFlowMonitor.Platform.Linux` | Linux-specific implementations |

All projects target `net10.0`. The App also targets `net10.0-macos` for CoreBluetooth access. Nullable reference types are enabled everywhere. ImplicitUsings are enabled. The Core and App projects both use `RootNamespace: EcoFlowMonitor`.

---

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

---

## C# Patterns

### Records

Used sparingly for immutable value types:
- `public record MqttCredentials(string Host, int Port, string Username, string Password);` -- positional record for simple DTOs.
- `public record PowerHistoryPoint(DateTime Time, int InputW, int OutputW);` -- data point for charts.
- `private record MonitorEntry(DeviceConfig Device, DeviceState State, IDeviceMonitor Monitor);` -- internal grouping in `MonitorOrchestrator`.

### Classes vs Records

- **Model classes** (`BmsData`, `DisplayData`, `EmsData`, `DeviceConfig`, `AppConfig`) are mutable classes with nullable properties and default initializers. These are deserialized from JSON config or populated incrementally from protocol messages.
- **State classes** (`DeviceState`, `PowerState`) are mutable -- fields updated as data arrives.
- **Enums** are simple, no `[Flags]`: `PowerStatus`, `ConnectionMode`, `TriggerType`, `ActionType`.

### Nullable Reference Types

Enabled project-wide. Applied consistently:
- Nullable properties for data that may not be present yet: `public float? BatteryPct { get; set; }`, `public BmsData? Bms { get; set; }`.
- Null-coalescing assignment in merge helpers: `state.Bms ??= new BmsData();`.
- Null-conditional throughout: `_config.Account?.Email ?? ""`.
- `!` postfix operator used intentionally on values proven non-null by context: `Path.GetDirectoryName(_path)!`, `client.UserId!`.

### Static Classes

Heavy use for stateless utility logic:
- `Logger` -- static singleton with `Log(string)`.
- `ConfigManager` -- static `Load()` / `Save()`.
- `PowerStateMachine` -- static `Update()` method, never mutates input.
- `TriggerEvaluator` -- static `Evaluate()` and `RecordFired()`.
- `ProtobufDecoder` -- static parsing and dispatch.
- `BlePacketBuilder`, `BlePacketParser`, `BleDispatcher`, `BleProtoMapper` -- all static.
- `TemplateExpander`, `LogAction`, `Crc`, `BleKeyData` -- all static.

---

## MVVM Patterns (CommunityToolkit.Mvvm)

### ViewModelBase

```csharp
public abstract class ViewModelBase : ObservableObject { }
```

All view models inherit from `ViewModelBase`. No shared state or common functionality beyond `ObservableObject`.

### [ObservableProperty]

Source-generated observable properties are the standard pattern. Private backing fields with `_camelCase` naming:

```csharp
[ObservableProperty] private string _email = "";
[ObservableProperty] private bool _isSigningIn;
[ObservableProperty] private DeviceViewModel? _selectedDevice;
```

The generator creates public `Email`, `IsSigningIn`, `SelectedDevice` properties with `PropertyChanged` notification.

### Partial Methods for Change Notification

Used for dependent property updates:

```csharp
partial void OnErrorMessageChanged(string? value)
{
    OnPropertyChanged(nameof(HasError));
}

partial void OnSelectedTriggerChanged(TriggerType value)
{
    ShowThreshold = value == TriggerType.BatteryBelow || value == TriggerType.TimeRemainingBelow;
    TotalSteps = ShowThreshold ? 4 : 3;
    OnPropertyChanged(nameof(IsLastStep));
    OnPropertyChanged(nameof(NextButtonText));
}
```

### [RelayCommand]

Used for all UI-bound commands. Async commands use `Task` return type:

```csharp
[RelayCommand]
private async Task SignInAsync() { ... }

[RelayCommand]
private void OpenSettings() { ... }

[RelayCommand]
private void RemoveAction(ActionConfig? action) { ... }
```

### Manual ObservableObject Usage

`CellVoltageItem` uses manual `SetProperty` instead of `[ObservableProperty]` because it directly extends `ObservableObject` (not `partial class` with the generator):

```csharp
public class CellVoltageItem : ObservableObject
{
    private int _index;
    public int Index { get => _index; set => SetProperty(ref _index, value); }
}
```

`NavigationService` also extends `ObservableObject` directly for the `CurrentView` property.

### Computed Properties

Non-observable computed properties that depend on other observable properties, with manual `OnPropertyChanged` calls:

```csharp
public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
public bool IsFirstStep => CurrentStep == 0;
public bool IsLastStep => CurrentStep >= TotalSteps - 1;
```

---

## Avalonia Patterns

### StyledProperty Registration

Custom controls use `AvaloniaProperty.Register<TOwner, TValue>` with CLR property wrappers:

```csharp
public static readonly StyledProperty<float> PercentageProperty =
    AvaloniaProperty.Register<ArcBatteryGauge, float>(nameof(Percentage));

public float Percentage
{
    get => GetValue(PercentageProperty);
    set => SetValue(PercentageProperty, value);
}
```

For render-affecting properties, `AffectsRender` is called in the static constructor:

```csharp
static ArcBatteryGauge()
{
    AffectsRender<ArcBatteryGauge>(PercentageProperty, StatusProperty);
}
```

### Custom Control Approach

Two patterns are used:

1. **Code-only controls** extending `Control` with custom `Render()` override: `ArcBatteryGauge`, `PowerFlowDiagram`, `GlowStatusIndicator`, `StepIndicator`. These do all drawing via `DrawingContext`.

2. **AXAML UserControls** with code-behind: `StatCard`, `PowerHistoryChart`. These use `OnLoaded` + `OnPropertyChanged` to update child elements found via `FindControl<T>`.

### DataContext and DataTemplate Navigation

The `MainWindow` hosts a `ContentControl` bound to `CurrentPage`. View resolution uses `DataTemplate` declarations in AXAML:

```xml
<Window.DataTemplates>
    <DataTemplate DataType="vm:LoginViewModel">
        <views:LoginView />
    </DataTemplate>
    <DataTemplate DataType="vm:DashboardViewModel">
        <views:DashboardView />
    </DataTemplate>
</Window.DataTemplates>

<ContentControl Content="{Binding CurrentPage}" />
```

Nested DataContext switching is used in the Dashboard view -- `StackPanel DataContext="{Binding SelectedDevice}"` changes the binding context for all children.

### Converter Pattern

Converters use static singleton instances for AXAML binding:

```csharp
public class StatusColorConverter : IValueConverter
{
    public static readonly StatusColorConverter Instance = new();
    // ...
}
```

Referenced in AXAML as:
```xml
Converter="{x:Static converters:StatusColorConverter.Instance}"
```

Some converters are parameterized via constructor:
```csharp
public static readonly WattColorConverter InstanceGreen = new("#00E676");
public static readonly WattColorConverter InstanceOrange = new("#FF9100");
```

Multi-value converters implement `IMultiValueConverter`:
```csharp
public class CellVoltageColorConverter : IMultiValueConverter { ... }
```

### AXAML Patterns

- `x:DataType` is used on views for compile-time binding validation: `x:DataType="vm:DashboardViewModel"`.
- Typed data templates use `x:DataType`: `<DataTemplate x:DataType="vm:DeviceViewModel">`.
- Static resources referenced by key: `Background="{StaticResource BackgroundPrimary}"`.
- Built-in converters used directly: `ObjectConverters.IsNotNull`, `StringConverters.IsNotNullOrEmpty`.
- `RelativeSource` binding for reaching parent DataContext: `Command="{Binding DataContext.CycleConnectionModeCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"`.
- Inline styles within controls using `<ListBox.Styles>` blocks.

### Design System

A dark theme design system defined in `Themes/DesignSystem.axaml` with:
- Background colors: `#0D0D0D` (primary), `#141414` (secondary)
- Surface colors: `#1A1A1A` (cards), `#222222` (hover), `#2A2A2A` (borders)
- Text colors: `#F0F0F0` (primary), `#888888` (secondary), `#555555` (muted)
- Accent: `#00D4AA` (teal primary)
- Status colors: green (`#00E676`), amber (`#FFB300`), red (`#FF5252`), gray (`#666666`)
- Separate `Typography.axaml` and `Controls.axaml` for text classes and button styles.

Font stack: Inter (primary), JetBrains Mono/Consolas (monospace for data values).

### UI Thread Marshaling

All cross-thread UI updates use `Avalonia.Threading.Dispatcher.UIThread.Post(() => { ... })`. This is called from:
- `DashboardViewModel.OnDeviceUpdated` (events from `MonitorOrchestrator`)
- `BleScanViewModel.OnDeviceDiscovered` (events from `BleScanner`)
- `BleScanViewModel.ToggleScanAsync` completion callback

---

## Dependency Injection

Microsoft.Extensions.DependencyInjection is configured in `App.OnFrameworkInitializationCompleted()`:

```csharp
var services = new ServiceCollection();
services.AddSingleton(config);                    // AppConfig instance
PlatformServiceFactory.Register(services);        // Platform abstractions
services.AddSingleton<MonitorOrchestrator>();
services.AddSingleton<NavigationService>();
services.AddSingleton<MainWindowViewModel>();
services.AddTransient<LoginViewModel>();           // Transient -- recreated on navigation
services.AddTransient<DashboardViewModel>();
services.AddTransient<SettingsViewModel>();
services.AddSingleton<BleScanner>();
services.AddTransient<BleScanViewModel>();
```

Service resolution uses the static `App.Services` property with `GetRequiredService<T>()`.

### Platform Service Registration

`PlatformServiceFactory` uses runtime OS detection and reflection-based assembly loading:
- Loads platform assemblies at runtime: `Assembly.Load("EcoFlowMonitor.Platform.Windows")`
- Resolves types by string name: `asm.GetType("EcoFlowMonitor.Platform.Windows.WindowsNotificationService")`
- Falls back to no-op implementations for unsupported platforms
- BLE adapter uses `typeof(PlatformServiceFactory).Assembly.GetType()` for the CoreBluetooth adapter on macOS

---

## Logging Approach

### Logger.Log Static Pattern

A single static `Logger` class in `EcoFlowMonitor.Logging`:

```csharp
Logger.Init();                          // Optional path, defaults to AppData/EcoFlowMonitor/debug.log
Logger.Log("descriptive message");      // Thread-safe, timestamped, file-append
```

Characteristics:
- Thread-safe via `lock` on a static object.
- Appends to a single flat file with `[HH:mm:ss.fff]` timestamps.
- Silent failure -- `catch { }` on write errors.
- No log levels (everything is one level).
- No structured logging or log rotation.

### Logging Conventions

Log messages follow a consistent prefix pattern: `ClassName: description`:

```
MqttMonitor: connecting to ...
BleMonitor: ECDH key exchange starting...
BleTransport: raw notification 20 bytes: ...
BleDispatcher: unhandled src=0x02 ...
MonitorOrchestrator: starting BLE monitor for ...
BleScanner: found EF-Delta3 sn=R331... enc=7 rssi=-65
```

Hex dumps use `Convert.ToHexString()` for protocol data. Internal MQTTnet traces are piped through a custom `MqttNetLogger` that filters to Warning+ level.

---

## Error Handling Patterns

### Catch-and-Swallow

The dominant pattern throughout the codebase. Used where errors should not crash the application:

```csharp
catch { }                               // Logger.Log write failures
catch { }                               // BLE scan timeout (expected)
catch { }                               // Protocol decode errors in MqttMonitor
```

### Catch-and-Log

Used in service-layer code:

```csharp
catch (Exception ex)
{
    Logger.Log($"MonitorOrchestrator: MQTT connect failed for {device.DisplayName}: {ex.Message}");
}
```

### Catch-and-Surface

Used in view models to show errors to the user:

```csharp
catch (Exception ex)
{
    ErrorMessage = ex.Message.Contains("401") ...
        ? "Invalid email or password"
        : $"Connection failed: {ex.Message}";
}
```

### Guard Clauses

`ArgumentNullException` thrown in constructors for required dependencies:

```csharp
_config = config ?? throw new ArgumentNullException(nameof(config));
_state  = state  ?? throw new ArgumentNullException(nameof(state));
```

### Global Exception Handlers

`Program.cs` registers:
- `AppDomain.CurrentDomain.UnhandledException` -- writes to `crash.log`
- `TaskScheduler.UnobservedTaskException` -- writes to `crash.log`, calls `SetObserved()`

Both write to `%APPDATA%/EcoFlowMonitor/crash.log`.

### ConfigManager Defensive Loading

Returns a default `AppConfig()` on any load failure:

```csharp
catch { return new AppConfig(); }
```

---

## Async Patterns

### Task.Run for Background Work

Fire-and-forget pattern with `_ = Task.Run(...)` used heavily:

```csharp
_ = Task.Run(() => ConnectMqttAsync(device, state));
_ = Task.Run(async () => { await monitor.StartAsync(); });
```

This is the standard approach for starting long-running monitors that should not block the UI.

### CancellationToken Usage

- `CancellationTokenSource.CreateLinkedTokenSource(ct)` used to chain cancellation.
- `CancelAfter(TimeSpan)` for timeouts on BLE operations.
- `WaitAsync(token)` on `TaskCompletionSource` for awaiting BLE handshake responses.
- Scan operations use `CancellationTokenSource` with `TimeSpan.FromSeconds(15)` timeout.

### ConfigureAwait

`ConfigureAwait(false)` used consistently in the Core library (non-UI code):

```csharp
await _client.ConnectAsync(_options, ct).ConfigureAwait(false);
await Task.Delay(5000, ct).ConfigureAwait(false);
```

Not used in view model code (runs on UI context).

### Reconnect Loops

Both `MqttMonitor` and `BleMonitor` implement retry loops with delay:

```csharp
while (!ct.IsCancellationRequested)
{
    try { await ConnectAndAuthAsync(ct); return; }
    catch (OperationCanceledException) { return; }
    catch (Exception ex)
    {
        Logger.Log($"BleMonitor: connect failed -- {ex.Message}, retry in 5s");
        await Task.Delay(5000, ct);
    }
}
```

### TaskCompletionSource

Used for bridging event-driven BLE responses to async/await:
- `_authTcs = new TaskCompletionSource<bool>()` -- waits for auth response packet.
- `_handshakeTcs = new TaskCompletionSource<byte[]>()` -- waits for ECDH key exchange response.

---

## Configuration Patterns

### JSON Config

`ConfigManager` is a static class that reads/writes `%APPDATA%/EcoFlowMonitor/config.json`:

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
```

The config file uses camelCase JSON keys. The `AppConfig` class hierarchy:

```
AppConfig
  +-- Account: AccountConfig? (email, password)
  +-- Devices: List<DeviceConfig>
  |     +-- SerialNumber, DisplayName, ConnectionMode
  |     +-- BLE fields (BleAddress, BleEncryptionType, BleProtocolVersion)
  |     +-- Rules: List<RuleConfig>
  |           +-- Trigger: TriggerConfig (Type, Threshold)
  |           +-- Actions: List<ActionConfig> (Type, ScriptPath, etc.)
  +-- General: GeneralSettings (StartWithWindows, ErrorLogPath, DarkMode)
  +-- LocalUserId, CloudUserId
  +-- IsConfigured (computed)
```

Config is loaded once at startup, passed as a singleton through DI, mutated in-place, and saved via `ConfigManager.Save(_config)` after changes.

### No Environment Variables in Service

The C# service does not read `.env` files or environment variables. All configuration is persisted in the JSON config file. The `.env.example` at root is for the Python POC only.

---

## Protocol Conventions

### Protobuf Handling

Two approaches coexist:

1. **Hand-rolled protobuf decoder** (`ProtobufDecoder`) -- parses raw wire format manually for MQTT cloud messages. Returns `Dictionary<int, List<object>>` of field numbers to values. This handles the outer envelope, XOR decryption, and field extraction.

2. **Google.Protobuf generated code** -- used for BLE messages where `.proto` files exist (`pd335_sys.proto`, `pd335_bms_bp.proto`). `BleProtoMapper` uses the generated `Parser.ParseFrom()`.

### Data Merging

BLE data arrives incrementally -- not every message contains every field. The `BleMonitor` uses explicit null-guarded merge helpers:

```csharp
if (src.BatteryPct.HasValue) dst.BatteryPct = src.BatteryPct;
if (src.VoltageV.HasValue) dst.VoltageV = src.VoltageV;
```

MQTT data uses full replacement (`_state.Bms = bms`).

### Wire Format Constants

Protocol magic bytes and routing are hardcoded:
- BLE frame prefix: `0x5A5A`
- BLE packet prefix: `0xAA`
- BLE GATT UUIDs: RFCOMM (`00000001-...`) and Nordic UART (`6e400001-...`)
- MQTT topic patterns: `/app/device/property/{sn}`, `/app/{userId}/{sn}/thing/property/get`
- EcoFlow manufacturer ID: `46517`

---

## Event-Driven Architecture

The data flow follows an event chain:

```
BLE/MQTT Transport -> IDeviceMonitor.StateChanged -> MonitorOrchestrator.OnStateChanged
  -> TriggerEvaluator.Evaluate -> ActionRunner.Run
  -> MonitorOrchestrator.DeviceUpdated -> DashboardViewModel.OnDeviceUpdated
    -> Dispatcher.UIThread.Post -> DeviceViewModel.UpdateFromState
```

Events use standard .NET `EventHandler<TEventArgs>` pattern. Custom event args classes:
- `StateChangedEventArgs(DeviceState, PowerStatus)` -- from monitors
- `DeviceStateEventArgs(DeviceState, string source)` -- from orchestrator to UI

---

## Single Instance Enforcement

`Program.cs` uses a named `Mutex` to prevent multiple instances:

```csharp
_mutex = new Mutex(true, "EcoFlowMonitor_SingleInstance", out bool createdNew);
if (!createdNew) return;
```
