---
phase: 02-cross-platform-ble
plan: 01
subsystem: platform-windows
tags: [ble, windows, winrt, gatt, bluetooth]
dependency_graph:
  requires: [EcoFlowMonitor.Core/Platform/IBleAdapter]
  provides: [WinRtBleAdapter, WinRtGattConnection]
  affects: [EcoFlowMonitor.Platform.Windows]
tech_stack:
  added: [Windows.Devices.Bluetooth (WinRT via TFM), Windows.Devices.Bluetooth.Advertisement, Windows.Devices.Bluetooth.GenericAttributeProfile, Windows.Foundation.TypedEventHandler]
  patterns: [WinRT CsWinRT projection via net10.0-windows10.0.19041.0 TFM, EnableWindowsTargeting for cross-compilation on macOS]
key_files:
  created:
    - service/src/EcoFlowMonitor.Platform.Windows/WinRtBleAdapter.cs
    - service/src/EcoFlowMonitor.Platform.Windows/WinRtGattConnection.cs
  modified:
    - service/src/EcoFlowMonitor.Platform.Windows/EcoFlowMonitor.Platform.Windows.csproj
decisions:
  - "EnableWindowsTargeting=true added to csproj for cross-compilation on macOS (required by NETSDK1100)"
  - "using Windows.Foundation added for TypedEventHandler<T1,T2> to resolve namespace ambiguity with EcoFlowMonitor.Platform.Windows"
  - "GattCharacteristic.ValueChanged uses TypedEventHandler not EventHandler — field type is TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>"
metrics:
  duration_minutes: 4
  completed_date: 2026-03-30
  tasks_completed: 2
  files_created: 2
  files_modified: 1
---

# Phase 02 Plan 01: Windows BLE Adapter (WinRT) Summary

Windows BLE adapter implemented via WinRT native APIs — BluetoothLEAdvertisementWatcher scan + BluetoothLEDevice/GATT connection — behind the existing IBleAdapter/IBleGattConnection interfaces with lazy-connect workaround and handler accumulation guard.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Update Windows csproj TFM and scaffold WinRtBleAdapter | 4715191 | EcoFlowMonitor.Platform.Windows.csproj, WinRtBleAdapter.cs |
| 2 | Implement WinRtGattConnection | 4864847 | WinRtGattConnection.cs |

## What Was Built

### WinRtBleAdapter (IBleAdapter)

- `StartScanAsync`: clears `_advertisementCache` on every call, creates `BluetoothLEAdvertisementWatcher` with `Active` scanning mode, awaits cancellation token (BleMonitor cancels after ~8s), then calls `StopScan()` in `finally`.
- `StopScan`: checks watcher status before stopping; unsubscribes `Received` handler before `Stop()` to prevent post-stop event delivery.
- `OnAdvertisementReceived`: caches args by `BluetoothAddress` ulong, extracts manufacturer data via `DataReader.FromBuffer`, raises `AdvertisementReceived` with `DeviceId = args.BluetoothAddress.ToString()` (decimal string format).
- `CreateConnection`: returns `new WinRtGattConnection(_advertisementCache)` — passes cache so connection can resolve address from decimal-string DeviceId.

### WinRtGattConnection (IBleGattConnection)

- `ConnectAsync`: parses `deviceId` as `ulong`, calls `FromBluetoothAddressAsync`, immediately calls `GetGattServicesAsync(BluetoothCacheMode.Uncached)` to force real OS connection (lazy-connect workaround). Holds `_services` as strong reference.
- `SubscribeNotifyAsync`: discovers characteristics with `BluetoothCacheMode.Uncached`; guards against handler accumulation with `_valueChangedHandler != null` check and `ValueChanged -=` before `+=`.
- `WriteAsync`: discovers characteristic with `Uncached`, writes via `DataWriter` with `GattWriteOption.WriteWithResponse`.
- `DisconnectAsync`: unsubscribes `ValueChanged`, unsubscribes `ConnectionStatusChanged`, disposes all `_services` entries, disposes `_device`, sets `IsConnected = false`.
- `DisposeAsync`: delegates to `DisconnectAsync` for `IAsyncDisposable` compliance.

### csproj Changes

- TFM: `net10.0-windows` → `net10.0-windows10.0.19041.0` (enables WinRT BLE APIs via CsWinRT projection)
- Added `<EnableWindowsTargeting>true</EnableWindowsTargeting>` to allow compilation on macOS

## Verification Results

```
dotnet build EcoFlowMonitor.Platform.Windows/EcoFlowMonitor.Platform.Windows.csproj
Build succeeded. 2 Warning(s), 0 Error(s)
```

Warnings are pre-existing `System.Drawing.Common` vulnerability advisory from `Microsoft.Toolkit.Uwp.Notifications` transitive dependency — not caused by this plan.

Full solution build (`dotnet build EcoFlowMonitor.sln`) has 4 errors in `EcoFlowMonitor.Platform.Linux` — these are pre-existing/parallel-agent Linux adapter errors out of scope for this Windows-only plan (see Deferred Issues below).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Config] Added EnableWindowsTargeting=true to csproj**
- **Found during:** Task 1 build verification
- **Issue:** NETSDK1100 error — cross-targeting Windows from macOS requires `EnableWindowsTargeting=true` in the csproj
- **Fix:** Added `<EnableWindowsTargeting>true</EnableWindowsTargeting>` to PropertyGroup
- **Files modified:** EcoFlowMonitor.Platform.Windows.csproj
- **Commit:** 4715191

**2. [Rule 1 - Bug] Fixed Windows.Storage.Streams namespace conflict**
- **Found during:** Task 1 compilation
- **Issue:** `Windows.Storage.Streams.DataReader.FromBuffer()` used as fully-qualified name but the project's namespace `EcoFlowMonitor.Platform.Windows` caused the parser to interpret `Windows` as the project namespace
- **Fix:** Added `using Windows.Storage.Streams;` to WinRtBleAdapter.cs; removed fully-qualified prefix
- **Files modified:** WinRtBleAdapter.cs
- **Commit:** 4715191

**3. [Rule 1 - Bug] Fixed TypedEventHandler type for GattCharacteristic.ValueChanged**
- **Found during:** Task 2 compilation
- **Issue:** Plan specified `EventHandler<GattValueChangedEventArgs>` for the handler field, but WinRT's `GattCharacteristic.ValueChanged` is a `TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>` — these are incompatible types
- **Fix:** Changed field type to `TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>` (requires `using Windows.Foundation;`); updated lambda signature to explicit typed parameters
- **Files modified:** WinRtGattConnection.cs
- **Commit:** 4864847

**4. [Rule 1 - Bug] Fixed Windows.Foundation namespace ambiguity**
- **Found during:** Task 2 compilation after TypedEventHandler fix
- **Issue:** Using `Windows.Foundation.TypedEventHandler<>` as fully-qualified name caused "namespace 'Foundation' does not exist in EcoFlowMonitor.Platform.Windows" — same root cause as issue #2
- **Fix:** Added `using Windows.Foundation;` to WinRtGattConnection.cs; used unqualified `TypedEventHandler<>` type
- **Files modified:** WinRtGattConnection.cs
- **Commit:** 4864847

## Deferred Issues

**Out-of-scope Linux adapter compilation errors (parallel agent work):**
- `EcoFlowMonitor.Platform.Linux/BlueZBleAdapter.cs` has 4 errors involving `IAdapter1.DeviceFound` API mismatch and missing `BlueZGattConnection` type
- These errors are from the parallel agent working on Plan 02-02 (Linux BLE)
- Not caused by this plan's changes; not fixed here
- Will be resolved in Plan 02-02

## Known Stubs

None — both adapter types are fully implemented with all IBleAdapter and IBleGattConnection methods wired to real WinRT API calls. No placeholder returns or hardcoded values.

## Self-Check: PASSED
