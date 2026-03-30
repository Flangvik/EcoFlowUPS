---
phase: 02-cross-platform-ble
plan: 02
subsystem: ble
tags: [linux, bluez, dbus, linux.bluetooth, ble, iblebadapter, iblegattconnection]

# Dependency graph
requires:
  - phase: 02-cross-platform-ble
    provides: IBleAdapter and IBleGattConnection interfaces defined in EcoFlowMonitor.Core

provides:
  - BlueZBleAdapter implementing IBleAdapter using Linux.Bluetooth 5.67.1 (BlueZ D-Bus)
  - BlueZGattConnection implementing IBleGattConnection with ServicesResolved wait
  - BlueZPermissionCheck with bluetooth group preflight check and human-readable error
  - Linux.Bluetooth 5.67.1 package reference in EcoFlowMonitor.Platform.Linux

affects: [03-reconnect, 04-ui-polish, platform-linux-ble-usage]

# Tech tracking
tech-stack:
  added: [Linux.Bluetooth 5.67.1, Tmds.DBus (transitive)]
  patterns:
    - Concrete Adapter type (not IAdapter1) required for DeviceFound event subscription
    - DeviceChangeEventHandlerAsync delegate type for DeviceFound event
    - ServicesResolved wait pattern before any GATT operation
    - MAC address string as DeviceId on Linux (AA:BB:CC:DD:EE:FF format)
    - String UUID (not Guid) for GetServiceAsync/GetCharacteristicAsync extension methods
    - GattCharacteristic (concrete) for Value event subscription

key-files:
  created:
    - service/src/EcoFlowMonitor.Platform.Linux/BlueZPermissionCheck.cs
    - service/src/EcoFlowMonitor.Platform.Linux/BlueZBleAdapter.cs
    - service/src/EcoFlowMonitor.Platform.Linux/BlueZGattConnection.cs
  modified:
    - service/src/EcoFlowMonitor.Platform.Linux/EcoFlowMonitor.Platform.Linux.csproj

key-decisions:
  - "Use concrete Adapter type (not IAdapter1) because DeviceFound event is only on the concrete class in Linux.Bluetooth 5.67.1"
  - "ManufacturerData values are object not byte[] in Device1Properties — cast with 'as byte[]'"
  - "GetServiceAsync/GetCharacteristicAsync take string UUID (not Guid) — convert with .ToString()"
  - "BlueZManager.GetAdapterAsync takes adapter name without '/org/bluez/' prefix in this version"
  - "OnDisconnectedAsync returns Task.CompletedTask to satisfy DeviceEventHandlerAsync delegate signature"

patterns-established:
  - "Pattern: Always call BlueZPermissionCheck.EnsureBluetoothGroupMembership() first in StartScanAsync before any D-Bus call"
  - "Pattern: Wait for ServicesResolved=true after ConnectAsync before any GATT operation"
  - "Pattern: Cache Device objects by MAC address string in BlueZBleAdapter for use by BlueZGattConnection"
  - "Pattern: Unsubscribe previous GattCharacteristic.Value handler before re-subscribing to prevent accumulation"

requirements-completed: [CONN-04]

# Metrics
duration: 5min
completed: 2026-03-30
---

# Phase 02 Plan 02: Linux BLE Adapter Summary

**Linux BLE adapter via BlueZ D-Bus using Linux.Bluetooth 5.67.1: bluetooth group preflight check, MAC-keyed device cache, ServicesResolved wait before GATT operations**

## Performance

- **Duration:** 5 min
- **Started:** 2026-03-30T12:29:26Z
- **Completed:** 2026-03-30T12:34:38Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments

- BlueZPermissionCheck provides a static preflight check for bluetooth group membership with a human-readable fix instruction (`sudo usermod -aG bluetooth $USER`)
- BlueZBleAdapter implements IBleAdapter using Linux.Bluetooth 5.67.1's concrete Adapter class; scans via DeviceFound event, caches Device objects by MAC address, raises BleAdvertisement events
- BlueZGattConnection implements IBleGattConnection with full connect/subscribe/write/disconnect lifecycle including the critical ServicesResolved wait

## Task Commits

Each task was committed atomically:

1. **Task 1: Add Linux.Bluetooth package and implement BlueZPermissionCheck + BlueZBleAdapter** - `976fa1c` (feat)
2. **Task 2: Implement BlueZGattConnection** - `95c5c12` (feat)

**Plan metadata:** (docs commit follows)

## Files Created/Modified

- `service/src/EcoFlowMonitor.Platform.Linux/EcoFlowMonitor.Platform.Linux.csproj` - Added Linux.Bluetooth 5.67.1 PackageReference
- `service/src/EcoFlowMonitor.Platform.Linux/BlueZPermissionCheck.cs` - Static preflight check; runs `id -nG`, verifies bluetooth group, skips on non-Linux
- `service/src/EcoFlowMonitor.Platform.Linux/BlueZBleAdapter.cs` - IBleAdapter implementation; uses concrete Adapter type for DeviceFound; caches IDevice1/Device by MAC string
- `service/src/EcoFlowMonitor.Platform.Linux/BlueZGattConnection.cs` - IBleGattConnection implementation; awaits Connected + ServicesResolved; string UUID conversion for GATT calls

## Decisions Made

- Used concrete `Adapter` type (not `IAdapter1`) because `DeviceFound` event is only available on the concrete class in Linux.Bluetooth 5.67.1. `IAdapter1` only has the D-Bus method surface.
- `ManufacturerData` values in `Device1Properties` are typed as `object` (not `byte[]`) — requires `as byte[]` cast at runtime.
- `GetServiceAsync`/`GetCharacteristicAsync` extension methods take `string` UUID, not `Guid` — Guid converted via `.ToString()`.
- `BlueZManager.GetAdapterAsync("hci0")` uses the adapter name without the `/org/bluez/` prefix (the library normalizes it internally).
- `OnDisconnectedAsync` returns `Task.CompletedTask` to satisfy the `DeviceEventHandlerAsync` async delegate signature.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Corrected Linux.Bluetooth 5.67.1 API — concrete types required**
- **Found during:** Task 1 (initial build)
- **Issue:** Plan used `IAdapter1.DeviceFound` (does not exist), `IDictionary<ushort, byte[]>` for ManufacturerData (actual value type is `object`), and `Func<IAdapter1, DeviceFoundEventArgs, Task>` for handler (actual delegate is `DeviceChangeEventHandlerAsync` taking `Adapter sender`)
- **Fix:** Inspected Linux.Bluetooth 5.67.1 assembly via reflection; corrected all types to match actual API: `Adapter` (concrete), `DeviceChangeEventHandlerAsync`, `object` cast for ManufacturerData values
- **Files modified:** service/src/EcoFlowMonitor.Platform.Linux/BlueZBleAdapter.cs
- **Verification:** `dotnet build EcoFlowMonitor.Platform.Linux.csproj` exits 0
- **Committed in:** 976fa1c (Task 1 commit)

**2. [Rule 1 - Bug] String UUID required for GATT extension methods**
- **Found during:** Task 2 (design review before writing)
- **Issue:** Plan called `GetServiceAsync(serviceUuid)` with `Guid`, but extension method signature is `GetServiceAsync(IDevice1, string)`
- **Fix:** Added `.ToString()` conversion when calling `GetServiceAsync` and `GetCharacteristicAsync`
- **Files modified:** service/src/EcoFlowMonitor.Platform.Linux/BlueZGattConnection.cs
- **Verification:** `dotnet build EcoFlowMonitor.sln` exits 0
- **Committed in:** 95c5c12 (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 Rule 1 - Bug)
**Impact on plan:** Both fixes required for compilation. The Linux.Bluetooth 5.67.1 API differs from what the plan's pseudo-code implied. No scope creep — all fixes are within the plan's intended design.

## Issues Encountered

- Linux.Bluetooth 5.67.1 uses concrete `Adapter`/`Device`/`GattCharacteristic` classes for events rather than their `IAdapter1`/`IDevice1`/`IGattCharacteristic1` interfaces. The D-Bus interfaces only expose the method surface (GetAllAsync, etc.); events live on the concrete classes. This required reflection-based API discovery to resolve correctly.

## User Setup Required

None — no external service configuration required. On Linux, user must be in the `bluetooth` group (enforced by BlueZPermissionCheck at runtime with clear fix instructions).

## Next Phase Readiness

- Linux BLE adapter complete; EcoFlowMonitor.Platform.Linux now implements IBleAdapter and IBleGattConnection alongside the macOS CoreBluetooth adapter
- PlatformServiceFactory can register BlueZBleAdapter for Linux using the same reflection-based loading pattern
- Full solution builds cleanly (0 errors) on macOS confirming Linux.Bluetooth 5.67.1 resolves cross-platform
- Remaining BLE work: auto-reconnect with exponential backoff (Phase 3)

---
*Phase: 02-cross-platform-ble*
*Completed: 2026-03-30*
