# Technology Stack

**Analysis Date:** 2026-03-30

## Languages

**Primary:**
- C# (latest LangVersion) - All .NET projects in `service/src/` and `service/EcoFlowMonitor/`
- AXAML - Avalonia UI markup in `service/src/EcoFlowMonitor.App/Views/` and `service/src/EcoFlowMonitor.App/Themes/`

**Secondary:**
- Python 3 - Reference/prototype implementation in `poc/`
- Protobuf (proto3/proto2) - Protocol schemas in `service/src/EcoFlowMonitor.Core/Proto/`

## Runtime

**Environment:**
- .NET 10.0 (`net10.0`) - All projects in `service/src/` target net10.0
- .NET Framework 4.8 (`net48`) - Legacy WinForms project at `service/EcoFlowMonitor/`
- Python 3.x - PoC scripts (no version pinned)

**Package Manager:**
- NuGet (SDK-style .csproj) - No central package management; versions pinned per project
- pip - `poc/requirements.txt` for Python dependencies
- No lockfiles present (no `packages.lock.json`, no `pip freeze` output)

## Frameworks

**Core:**
- Avalonia 11.2.3 - Cross-platform desktop UI framework (`service/src/EcoFlowMonitor.App/`)
- Avalonia.Themes.Fluent 11.2.3 - Fluent design system theme
- Avalonia.Fonts.Inter 11.2.3 - Inter font family for typography
- CommunityToolkit.Mvvm 8.4.0 - MVVM source generators and base classes (used in both Core and App)

**Testing:**
- pytest 7.4.0 - Python PoC tests only (`poc/tests/`)
- No .NET test project exists

**Build/Dev:**
- Grpc.Tools 2.68.1 - Compiles `.proto` files to C# at build time (`service/src/EcoFlowMonitor.Core/`)
- MSBuild SDK-style projects - Standard `dotnet build` / `dotnet run` workflow
- Conditional TFM multi-targeting: `EcoFlowMonitor.App` targets both `net10.0` and `net10.0-macos`
- Conditional project references: Platform projects loaded based on OS at build time

## Key Dependencies

**Critical:**
- MQTTnet 4.3.7.1207 - MQTT client for EcoFlow cloud broker (`service/src/EcoFlowMonitor.Core/`)
- MQTTnet.Extensions.ManagedClient 4.3.7.1207 - Managed client extensions (referenced but plain `IMqttClient` is used)
- Google.Protobuf 3.28.3 - Protobuf runtime for generated message classes
- BouncyCastle.Cryptography 2.5.1 - SECP160r1 ECDH key exchange for BLE Type 7 encryption
- NSec.Cryptography 24.4.0 - Referenced in Core csproj (available for additional crypto if needed)

**Infrastructure:**
- Microsoft.Extensions.DependencyInjection 8.0.1 - Service container in `EcoFlowMonitor.App`
- LiveChartsCore.SkiaSharpView.Avalonia 2.0.0-rc3.3 - Power history charting in dashboard
- Microsoft.Toolkit.Uwp.Notifications 7.1.3 - Windows toast notifications (Windows platform project only)
- System.Text.Json - JSON serialization for config and REST API (built-in, no extra package)

**Legacy Project Only (net48):**
- MQTTnet 4.3.7 - Slightly older version than net10 project
- Newtonsoft.Json 13.0.3 - JSON handling (replaced by System.Text.Json in the modern stack)

**Python PoC:**
- requests 2.31.0 - REST API calls
- paho-mqtt 1.6.1 - MQTT client
- rich 13.7.0 - Terminal dashboard rendering

## Solution Structure

**Two solution files:**
- `service/EcoFlowMonitor.sln` - Legacy single-project solution (net48 WinForms)
- `service/src/EcoFlowMonitor.sln` - Modern multi-project solution (net10.0 Avalonia)

**Modern solution projects (`service/src/EcoFlowMonitor.sln`):**

| Project | Target | Purpose |
|---------|--------|---------|
| `EcoFlowMonitor.Core` | net10.0 | Protocol, models, client, state, triggers, actions |
| `EcoFlowMonitor.App` | net10.0; net10.0-macos | Avalonia UI application |
| `EcoFlowMonitor.Cli` | net10.0 | CLI diagnostic tool for raw MQTT data dump |
| `EcoFlowMonitor.Platform.Windows` | net10.0-windows | Windows-specific platform services |
| `EcoFlowMonitor.Platform.macOS` | net10.0 | macOS-specific platform services |
| `EcoFlowMonitor.Platform.Linux` | net10.0 | Linux-specific platform services |

**Note:** The Cli project is not included in the solution file but has its own `.csproj`.

## Protobuf Tooling

**Proto files in `service/src/EcoFlowMonitor.Core/Proto/`:**
- `pd335_sys.proto` - Delta 3 system/display messages (proto3, compiled to C#)
- `pd335_bms_bp.proto` - Delta 3 BMS/battery pack messages (proto3, compiled to C#)
- `pd303.proto` - Smart Panel protocol (proto2, excluded from build due to duplicate field name)

**Build integration:** Grpc.Tools compiles `.proto` files at build time via `<Protobuf>` items in `EcoFlowMonitor.Core.csproj`. Set `GrpcServices="None"` (protobuf only, no gRPC service stubs).

**Manual decoder:** `service/src/EcoFlowMonitor.Core/Protocol/ProtobufDecoder.cs` contains a hand-written protobuf parser for MQTT messages (does not use generated classes). The generated protobuf classes are used by `BleProtoMapper.cs` for BLE data decoding.

## Embedded Resources

- `service/src/EcoFlowMonitor.Core/Protocol/keydata.b64` - Base64-encoded 65,280-byte lookup table for BLE Type 7 session key derivation. Compiled as `<EmbeddedResource>`.

## Configuration

**Environment:**
- `.env.example` at repo root documents required credentials: `ECOFLOW_EMAIL`, `ECOFLOW_PASSWORD`, `ECOFLOW_DEVICE_SN`
- `.env` file present in `.gitignore` - never committed
- Runtime config stored as JSON at `%APPDATA%/EcoFlowMonitor/config.json` (or platform equivalent via `Environment.SpecialFolder.ApplicationData`)
- Config managed by `service/src/EcoFlowMonitor.Core/Config/ConfigManager.cs` using `System.Text.Json`

**Build:**
- No `global.json` - uses whatever .NET SDK is installed
- No `Directory.Build.props` - each project defines its own settings
- Nullable reference types enabled (`<Nullable>enable</Nullable>`) on all modern projects
- Implicit usings enabled on all modern projects

## Platform Requirements

**Development:**
- .NET 10.0 SDK (preview/RC as of analysis date)
- macOS 14.0+ for `net10.0-macos` target (set in `SupportedOSPlatformVersion`)
- Xcode version validation disabled (`ValidateXcodeVersion=false`)
- No CI pipeline detected

**Production:**
- **macOS:** Desktop app with tray icon, CoreBluetooth BLE support, native notifications
- **Windows:** Desktop app with tray icon, UWP toast notifications, Windows-specific power actions
- **Linux:** Desktop app with tray icon, stub implementations for notifications/power
- Single-instance enforcement via named `Mutex` in `Program.cs`
- Crash logs written to `%APPDATA%/EcoFlowMonitor/crash.log`

## Platform Abstraction

Platform-specific code is isolated behind interfaces defined in `service/src/EcoFlowMonitor.Core/Platform/`:

| Interface | Purpose | Implementations |
|-----------|---------|-----------------|
| `IBleAdapter` | BLE scanning and connection | `CoreBluetoothBleAdapter` (macOS), `StubBleAdapter` (fallback) |
| `INotificationService` | System notifications | Windows, macOS, Linux implementations |
| `IPowerActionService` | Shutdown/hibernate/sleep | Windows, macOS, Linux implementations |
| `IStartupService` | Auto-start on login | Windows, macOS, Linux implementations |
| `IScriptRunnerService` | Execute scripts | Windows, macOS, Linux implementations |
| `IElevationService` | Admin/root elevation | Windows, macOS, Linux implementations |

Platform services are registered at runtime by `service/src/EcoFlowMonitor.App/Services/PlatformServiceFactory.cs` using reflection-based assembly loading.

---

*Stack analysis: 2026-03-30*
