# Phase 2: Cross-Platform BLE - Research

**Researched:** 2026-03-30
**Domain:** WinRT BLE (Windows), BlueZ D-Bus (Linux), platform adapter pattern
**Confidence:** HIGH

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| CONN-03 | BLE connection works on Windows via WinRT Bluetooth LE APIs | WinRT APIs fully documented; `BluetoothLEAdvertisementWatcher` for scan, `BluetoothLEDevice` + `GattDeviceService` for GATT; TFM `net10.0-windows10.0.19041.0` unlocks all APIs with no extra NuGet package |
| CONN-04 | BLE connection works on Linux via BlueZ D-Bus (Linux.Bluetooth) | `Linux.Bluetooth` 5.67.1 NuGet wraps BlueZ D-Bus; targets netstandard2.0 so it resolves on net10.0; known permissions pitfall (bluetooth group) must be preflight-checked and surfaced as human-readable error |

</phase_requirements>

---

## Summary

Phase 2 adds two platform-native BLE adapters behind the already-defined `IBleAdapter` / `IBleGattConnection` interface from Phase 1 (or already present as the existing stub). The macOS `CoreBluetoothBleAdapter` is the reference implementation. Both new adapters must replicate its exact async lifecycle: scan-to-discover, create-connection, connect, discover-services, subscribe-notify, write, and disconnect. The BleMonitor FSM and BleTransport are unchanged — they consume `IBleAdapter` and do not need to know which platform is underneath.

**Windows** uses `Windows.Devices.Bluetooth` and `Windows.Devices.Bluetooth.GenericAttributeProfile` from the Windows SDK, available for free at TFM `net10.0-windows10.0.19041.0` — no additional NuGet package is required. Scanning uses `BluetoothLEAdvertisementWatcher` to receive advertisement packets (including manufacturer data), cache device addresses, then call `BluetoothLEDevice.FromBluetoothAddressAsync` to obtain a handle. GATT operations (`GetGattServicesAsync`, `GetCharacteristicsAsync`, `WriteClientCharacteristicConfigurationDescriptorAsync`, `ValueChanged`) follow the standard UWP GATT client pattern. The critical pitfall is that `BluetoothLEDevice` creation is lazy: the OS does not actually connect until a GATT operation fires. Use `GattSession.MaintainConnection = true` or trigger `GetGattServicesAsync()` immediately after obtaining the device object. A second pitfall: `ValueChanged` handlers accumulate on reconnect if you subscribe without first unsubscribing the old handler — this causes duplicate notifications.

**Linux** uses `Linux.Bluetooth` 5.67.1 (NuGet), which wraps BlueZ v5.50+ D-Bus APIs via `Tmds.DBus`. The adapter calls `BlueZManager.GetAdapterAsync()`, subscribes to `DeviceFound`, starts discovery, then on device found calls `device.ConnectAsync()` and waits on `WaitForPropertyValueAsync("ServicesResolved")`. GATT characteristic access goes through `IGattService1` / `IGattCharacteristic1`. The Linux-specific gotcha is that if the user is not in the `bluetooth` group, the D-Bus call fails with a permission error that is not always descriptive. A preflight check (`id -nG | grep -qw bluetooth` or equivalent using `System.Environment.GetEnvironmentVariable("USER")` + reading `/etc/group`) must run before attempting scan, and must surface a readable message: "BLE requires membership in the 'bluetooth' group. Run: sudo usermod -aG bluetooth $USER && log out/in."

Both adapters share the ECDH reset requirement from the existing `BleMonitor.ConnectLoopAsync`: every reconnect constructs a fresh `BleTransport` and `BleCryptoModern` — never reuse the previous session state. This is already enforced in BleMonitor; the adapters themselves need to correctly call `DisconnectAsync()` and clean up GATT subscription handlers when a connection drops, so that the next reconnect starts clean.

**Primary recommendation:** Use TFM `net10.0-windows10.0.19041.0` on the Windows project (no extra NuGet), and `Linux.Bluetooth` 5.67.1 on the Linux project. Mirror the CoreBluetooth adapter structure exactly — scan cache, TCS-based async bridge, peripheral delegate pattern — but using platform-native events.

---

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Windows.Devices.Bluetooth (built-in WinRT) | Windows SDK 10.0.19041+ | BLE scanning, GATT client on Windows | First-party Microsoft API; available on all Win10/11 machines; no extra NuGet needed when TFM includes Windows SDK version |
| Windows.Devices.Bluetooth.GenericAttributeProfile (built-in WinRT) | Same | GATT service/characteristic operations | Part of same Windows SDK |
| Windows.Devices.Bluetooth.Advertisement (built-in WinRT) | Same | `BluetoothLEAdvertisementWatcher` for scanning | Part of same Windows SDK |
| Linux.Bluetooth | 5.67.1 | BlueZ D-Bus wrapper for BLE on Linux | Successor to Plugin.BlueZ; netstandard2.0 target; wraps BlueZ D-Bus with high-level C# events |
| Tmds.DBus | 0.20.0+ (transitive via Linux.Bluetooth) | D-Bus IPC on Linux | Linux.Bluetooth's required dependency; pulled in automatically |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 (already in Windows project) | Windows toast notifications | Already present; no BLE usage but same project |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Linux.Bluetooth 5.67.1 | hashtagchris/DotNet-BlueZ | DotNet-BlueZ is archived/unmaintained; Linux.Bluetooth is its active successor |
| Built-in WinRT | 32feet.NET InTheHand.BluetoothLE | 32feet adds abstraction across platforms; overkill here because we already have IBleAdapter and only need the Windows backend |
| Linux.Bluetooth 5.67.1 | Linux.Bluetooth 6.0.0-pre2 | Pre-release; validated only on net6-8; stick to stable 5.67.1 |

**Installation (Windows project — change TFM only, no NuGet add):**
```bash
# Change csproj TargetFramework from net10.0-windows to net10.0-windows10.0.19041.0
# No additional dotnet add package needed for WinRT BLE APIs
```

**Installation (Linux project):**
```bash
dotnet add EcoFlowMonitor.Platform.Linux/EcoFlowMonitor.Platform.Linux.csproj package Linux.Bluetooth --version 5.67.1
```

**Version verification (Linux.Bluetooth):**
```bash
dotnet list EcoFlowMonitor.Platform.Linux/EcoFlowMonitor.Platform.Linux.csproj package
# Expected: Linux.Bluetooth 5.67.1
```

---

## Architecture Patterns

### Recommended Project Structure

```
EcoFlowMonitor.Platform.Windows/
├── WinRtBleAdapter.cs          # IBleAdapter: BluetoothLEAdvertisementWatcher scan, address cache
├── WinRtGattConnection.cs      # IBleGattConnection: BluetoothLEDevice, GattDeviceService, GattCharacteristic
└── EcoFlowMonitor.Platform.Windows.csproj  # TFM: net10.0-windows10.0.19041.0

EcoFlowMonitor.Platform.Linux/
├── BlueZBleAdapter.cs          # IBleAdapter: Linux.Bluetooth IAdapter1, DeviceFound event
├── BlueZGattConnection.cs      # IBleGattConnection: Device.ConnectAsync, IGattService1, IGattCharacteristic1
├── BlueZPermissionCheck.cs     # Static preflight: check bluetooth group membership
└── EcoFlowMonitor.Platform.Linux.csproj    # TFM: net10.0 + Linux.Bluetooth 5.67.1
```

### Pattern 1: Windows — BluetoothLEAdvertisementWatcher Scan Then Connect

**What:** Use `BluetoothLEAdvertisementWatcher` to receive advertisement packets during scan, extract `BluetoothAddress` (ulong) and manufacturer data, cache per address, stop watcher, then call `BluetoothLEDevice.FromBluetoothAddressAsync(address)` in `ConnectAsync`.

**When to use:** Every time `StartScanAsync` is called, and on every reconnect (cache is cleared between scans to match CoreBluetooth behavior).

```csharp
// Source: Microsoft Learn - Bluetooth GATT Client (official docs)
// https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client

// SCAN
private BluetoothLEAdvertisementWatcher? _watcher;
private readonly Dictionary<ulong, BluetoothLEAdvertisementReceivedEventArgs> _advertisementCache = new();

public async Task StartScanAsync(CancellationToken ct = default)
{
    _advertisementCache.Clear();
    _watcher = new BluetoothLEAdvertisementWatcher
    {
        ScanningMode = BluetoothLEScanningMode.Active
    };
    _watcher.Received += OnAdvertisementReceived;
    _watcher.Start();

    try { await Task.Delay(Timeout.Infinite, ct); }
    catch (OperationCanceledException) { }
    finally { StopScan(); }
}

public void StopScan()
{
    if (_watcher?.Status == BluetoothLEAdvertisementWatcherStatus.Started)
    {
        _watcher.Received -= OnAdvertisementReceived;  // CRITICAL: unsubscribe before stopping
        _watcher.Stop();
    }
}

private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
    BluetoothLEAdvertisementReceivedEventArgs args)
{
    // Cache by address; raise BleAdvertisement event
    _advertisementCache[args.BluetoothAddress] = args;

    ushort mfgId = 0;
    byte[]? mfgData = null;
    if (args.Advertisement.ManufacturerData.Count > 0)
    {
        var section = args.Advertisement.ManufacturerData[0];
        mfgId = section.CompanyId;
        var reader = Windows.Storage.Streams.DataReader.FromBuffer(section.Data);
        mfgData = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(mfgData);
    }

    AdvertisementReceived?.Invoke(this, new BleAdvertisement
    {
        DeviceId = args.BluetoothAddress.ToString(),
        Name = args.Advertisement.LocalName ?? "",
        Rssi = args.RawSignalStrengthInDBm,
        ManufacturerId = mfgId,
        ManufacturerData = mfgData
    });
}
```

### Pattern 2: Windows — GATT Connect and Subscribe (Lazy Connect Workaround)

**What:** WinRT does NOT connect on `FromBluetoothAddressAsync` alone. Immediately call `GetGattServicesAsync(BluetoothCacheMode.Uncached)` to trigger the actual OS connection. Hold strong references to `BluetoothLEDevice` and all `GattDeviceService` objects — disposing them disconnects.

```csharp
// Source: Microsoft Learn - Bluetooth GATT Client
// https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client
public async Task ConnectAsync(string deviceId, CancellationToken ct = default)
{
    ulong address = ulong.Parse(deviceId);
    _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
    if (_device == null)
        throw new InvalidOperationException($"Device {deviceId} not found in system cache. Run a scan first.");

    _device.ConnectionStatusChanged += OnConnectionStatusChanged;

    // IMPORTANT: GetGattServicesAsync with Uncached triggers actual OS connection
    var servicesResult = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
    if (servicesResult.Status != GattCommunicationStatus.Success)
        throw new InvalidOperationException($"GATT service discovery failed: {servicesResult.Status}");

    _services = servicesResult.Services;  // hold strong reference
    IsConnected = true;
}

// Subscribe to notifications — must unsubscribe existing handler first on reconnect
public async Task SubscribeNotifyAsync(Guid serviceUuid, Guid charUuid, CancellationToken ct = default)
{
    var characteristic = FindCharacteristic(serviceUuid, charUuid);

    // CRITICAL: Always remove existing handler to prevent accumulation on reconnect
    characteristic.ValueChanged -= OnValueChanged;
    characteristic.ValueChanged += OnValueChanged;

    await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
        GattClientCharacteristicConfigurationDescriptorValue.Notify);
}
```

### Pattern 3: Linux — BlueZ Adapter, Device Connect, GATT Subscribe

**What:** Use `Linux.Bluetooth` `IAdapter1` for scanning. After `DeviceFound`, connect with `device.ConnectAsync()` then wait for `ServicesResolved = true`. Subscribe via `IGattCharacteristic1.Value` event. Unsubscribe on disconnect.

```csharp
// Source: Linux.Bluetooth GitHub (SuessLabs/Linux.Bluetooth)
// https://github.com/SuessLabs/Linux.Bluetooth
public async Task StartScanAsync(CancellationToken ct = default)
{
    _adapter = await BlueZManager.GetAdapterAsync("/org/bluez/hci0");
    _adapter.DeviceFound += OnDeviceFoundAsync;
    await _adapter.StartDiscoveryAsync();

    try { await Task.Delay(Timeout.Infinite, ct); }
    catch (OperationCanceledException) { }
    finally
    {
        await _adapter.StopDiscoveryAsync();
        _adapter.DeviceFound -= OnDeviceFoundAsync;
    }
}

public async Task ConnectAsync(string deviceId, CancellationToken ct = default)
{
    // deviceId is "XX:XX:XX:XX:XX:XX" MAC address on Linux
    var devices = await _adapter.GetDevicesAsync();
    _device = devices.FirstOrDefault(d => /* match by address */ true)
        ?? throw new InvalidOperationException($"Device {deviceId} not found. Run scan first.");

    _device.Disconnected += OnDisconnectedAsync;

    await _device.ConnectAsync();
    await _device.WaitForPropertyValueAsync("Connected", value: true, TimeSpan.FromSeconds(15));
    await _device.WaitForPropertyValueAsync("ServicesResolved", value: true, TimeSpan.FromSeconds(15));
    IsConnected = true;
}
```

### Pattern 4: Linux — bluetooth Group Preflight Check

**What:** Before attempting any BLE scan on Linux, verify the current user is in the `bluetooth` group. If not, throw an `InvalidOperationException` with a human-readable message rather than letting the D-Bus call fail silently or produce a cryptic error.

```csharp
// Source: Linux user/group management (standard POSIX)
public static class BlueZPermissionCheck
{
    public static void EnsureBluetoothGroupMembership()
    {
        // Read current user's supplemental groups via /proc/self/status or `id` subprocess
        var groupsOutput = ExecuteProcess("id", "-nG");
        var groups = groupsOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!groups.Contains("bluetooth"))
        {
            throw new InvalidOperationException(
                "BLE on Linux requires membership in the 'bluetooth' group.\n" +
                "Fix: sudo usermod -aG bluetooth $USER\n" +
                "Then log out and log back in, or run: newgrp bluetooth");
        }
    }
}
```

### Anti-Patterns to Avoid

- **Reusing BluetoothLEDevice across reconnects (Windows):** Disposing and re-creating is required. The old device object holds OS state that prevents clean reconnect.
- **Not unsubscribing ValueChanged before re-subscribing (Windows):** Causes duplicate notification delivery on every reconnect cycle. Always `-=` before `+=`.
- **Using `BluetoothCacheMode.Cached` for service discovery (Windows):** Returns stale data from the OS cache; can cause "Unreachable" failures after a device power cycle. Always use `Uncached` on first connect.
- **Skipping ServicesResolved wait on Linux:** `ConnectAsync()` returns before GATT services are populated. If you query services immediately, you get an empty list. Always `WaitForPropertyValueAsync("ServicesResolved", true, ...)`.
- **Not clearing the advertisement cache between scan sessions:** Can deliver stale `BleAdvertisement` events for devices no longer in range. Clear on every `StartScanAsync`.
- **Assuming `DeviceId` is the same format on all platforms:** macOS uses UUID strings, Windows uses `ulong` address, Linux uses `XX:XX:XX:XX:XX:XX` MAC strings. The `BleAdvertisement.DeviceId` field must carry the correct format per platform; `BleTransport` passes `DeviceId` directly to `IBleGattConnection.ConnectAsync`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| D-Bus protocol for BlueZ | Custom D-Bus binding | Linux.Bluetooth 5.67.1 | D-Bus message framing, type marshalling, and BlueZ interface versioning are 1000+ lines of non-obvious code |
| WinRT async projection | Manual COM interop | Built-in WinRT projection via net10.0-windows10.0.19041.0 TFM | The TFM automatically generates C# projections via CsWinRT; manual COM is error-prone and unnecessary |
| Bluetooth group check | Custom /etc/group parser | `id -nG` subprocess or `System.Security.Principal.WindowsIdentity` equivalent | Reliable, single-line check; parsing /etc/group directly misses dynamic group changes |

**Key insight:** Both platforms have mature, official APIs. The work is the adapter shim (implementing `IBleAdapter` / `IBleGattConnection`), not the Bluetooth layer itself.

---

## Common Pitfalls

### Pitfall 1: WinRT Lazy Connection (Windows)

**What goes wrong:** `BluetoothLEDevice.FromBluetoothAddressAsync(address)` returns a non-null object but the device is not yet connected. Subsequent GATT calls return `DeviceUnreachable` or time out after 7 seconds.

**Why it happens:** WinRT BLE uses a lazy connect model. Creating the device object does not open a connection. The OS connects when the first GATT operation fires.

**How to avoid:** Immediately call `GetGattServicesAsync(BluetoothCacheMode.Uncached)` right after obtaining the `BluetoothLEDevice` object. This forces a real connection attempt. Alternatively set `GattSession.MaintainConnection = true` before any operation.

**Warning signs:** `GetGattServicesAsync` returns `GattCommunicationStatus.Unreachable` or hangs for exactly 7 seconds.

---

### Pitfall 2: ValueChanged Handler Accumulation (Windows)

**What goes wrong:** After each reconnect, an additional `ValueChanged` handler is added. After N reconnects, each notification fires N times. This causes duplicate BleTransport frames, corrupted packet reassembly, and inflated ECDH handshake responses.

**Why it happens:** `characteristic.ValueChanged += handler` accumulates delegates; removing the `GattDeviceService` reference disposes the characteristic object but if you re-discover the same characteristic and re-subscribe without unsubscribing the old handler first, you accumulate.

**How to avoid:** Always track the subscribed characteristic reference. On reconnect: first unsubscribe (`characteristic.ValueChanged -= OnValueChanged`), then Dispose the old `GattDeviceService`, then re-discover, then re-subscribe.

**Warning signs:** BleTransport reports `ProcessBuffer` errors or double-fires `PacketReceived`; notification count doubles with each reconnect.

---

### Pitfall 3: BluetoothLEDevice.FromBluetoothAddressAsync Returns Null (Windows)

**What goes wrong:** `FromBluetoothAddressAsync` returns `null`. This happens when the device has not been seen in the Windows BLE advertisement cache and is not paired.

**Why it happens:** WinRT requires that the device be either paired or present in the OS advertisement cache (populated by `BluetoothLEAdvertisementWatcher`). Calling `FromBluetoothAddressAsync` without a prior scan does not trigger cache population.

**How to avoid:** Always run `BluetoothLEAdvertisementWatcher` scan (which is what `WinRtBleAdapter.StartScanAsync` does) before calling `ConnectAsync`. BleMonitor already does an 8-second scan before connect. The Windows adapter must cache the `BluetoothAddress` (ulong) from each advertisement and provide it on lookup.

**Warning signs:** `FromBluetoothAddressAsync` returns `null`; connection fails immediately with NullReferenceException.

---

### Pitfall 4: ServicesResolved Not Awaited (Linux)

**What goes wrong:** `device.ConnectAsync()` completes but `GetServiceAsync(uuid)` returns null or throws. Data stream never starts.

**Why it happens:** BlueZ D-Bus resolves services asynchronously after connection. `ConnectAsync()` signals the connection is established at the transport level, but service discovery runs separately in BlueZ and fires the `ServicesResolved` property change event afterward.

**How to avoid:** Always `await device.WaitForPropertyValueAsync("ServicesResolved", true, timeout)` before any GATT operation.

**Warning signs:** `GetServiceAsync` returns `null` immediately after `ConnectAsync`.

---

### Pitfall 5: bluetooth Group Permission (Linux)

**What goes wrong:** `BlueZManager.GetAdaptersAsync()` throws a D-Bus `DBusException` with a message like "org.freedesktop.DBus.Error.AccessDenied" or `bluetooth.service` fails to respond. The error is not human-readable; users see a stack trace.

**Why it happens:** By default, accessing the BlueZ D-Bus interface requires the calling user to be in the `bluetooth` group (on most distros: Ubuntu, Debian, Fedora, Arch). Without group membership, D-Bus policy blocks the call.

**How to avoid:** Call `BlueZPermissionCheck.EnsureBluetoothGroupMembership()` at the top of `StartScanAsync`. Catch the group-check failure and surface a readable instruction before attempting any D-Bus call.

**Warning signs:** `DBusException` from `GetAdaptersAsync` on first run; user has not previously used Bluetooth on this machine.

---

### Pitfall 6: ECDH Handshake Must Reset on Reconnect (Both Platforms)

**What goes wrong:** After a disconnect/reconnect cycle, the BleMonitor reuses the existing `BleCryptoModern` session key. The device has reset its crypto state. Frames decrypt as garbage. No error is raised — the app silently processes corrupted protobuf data.

**Why it happens:** EcoFlow BLE devices reset their crypto session on disconnect. The session key derived during the previous ECDH handshake is no longer valid.

**How to avoid:** This is already enforced in `BleMonitor.ConnectLoopAsync` — `_transport` is set to null and recreated on each iteration, and a new `BleCryptoModern` is constructed. Platform adapters must NOT persist GATT connection handles across reconnects. `DisconnectAsync` must fully clean up the connection so the next `CreateConnection()` call starts fresh.

**Warning signs:** Data appears to decode (no exceptions) but values are nonsensical after a reconnect.

---

### Pitfall 7: DeviceId Format Mismatch Between Platforms

**What goes wrong:** `BleAdvertisement.DeviceId` is populated from the advertisement received during scan, then passed verbatim to `IBleGattConnection.ConnectAsync`. If the adapter stores a UUID string (macOS pattern) but the connection expects a MAC address or ulong, the connect fails with "device not found."

**Why it happens:** Each platform uses a different identifier scheme: macOS = UUID string, Windows = ulong printed as string, Linux = "AA:BB:CC:DD:EE:FF".

**How to avoid:** Each platform adapter must use a consistent internal DeviceId format: store the same format in the advertisement cache that it expects in `ConnectAsync`. Windows adapter uses `args.BluetoothAddress.ToString()` (ulong as decimal string) in both places. Linux adapter uses the MAC address string from BlueZ in both places. This is platform-internal and `BleTransport` passes it through opaquely — no cross-platform comparison happens.

---

## Code Examples

### Windows — Full Scan + Connect Skeleton

```csharp
// Source: Microsoft Learn GATT Client docs
// https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client
// TFM required: net10.0-windows10.0.19041.0

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using EcoFlowMonitor.Platform;

// Scan: collect advertisement -> cache BluetoothAddress
var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
watcher.Received += (_, args) => {
    // args.BluetoothAddress (ulong), args.Advertisement.LocalName, args.RawSignalStrengthInDBm
    // args.Advertisement.ManufacturerData[i].CompanyId, .Data (IBuffer)
};
watcher.Start();
// ... await scan duration or cancellation ...
watcher.Stop();

// Connect: FromBluetoothAddressAsync -> trigger connection via Uncached service discovery
ulong address = ulong.Parse(deviceId);
BluetoothLEDevice device = await BluetoothLEDevice.FromBluetoothAddressAsync(address)
    ?? throw new InvalidOperationException("Device not in cache. Rescan.");

device.ConnectionStatusChanged += (d, _) => {
    if (d.ConnectionStatus == BluetoothConnectionStatus.Disconnected) { /* trigger FSM */ }
};

var servicesResult = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
// servicesResult.Status == GattCommunicationStatus.Success to proceed

// Subscribe to notifications
GattDeviceService svc = servicesResult.Services.First(s => s.Uuid == serviceUuid);
var charsResult = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
GattCharacteristic ch = charsResult.Characteristics.First(c => c.Uuid == charUuid);

ch.ValueChanged -= previousHandler; // CRITICAL: remove before add to prevent accumulation
ch.ValueChanged += (_, args) => {
    var reader = Windows.Storage.Streams.DataReader.FromBuffer(args.CharacteristicValue);
    byte[] data = new byte[reader.UnconsumedBufferLength];
    reader.ReadBytes(data);
    NotificationReceived?.Invoke(this, data);
};
await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
    GattClientCharacteristicConfigurationDescriptorValue.Notify);

// Write
var writer = new Windows.Storage.Streams.DataWriter();
writer.WriteBytes(payload);
GattCommunicationStatus writeStatus = await ch.WriteValueAsync(writer.DetachBuffer(),
    GattWriteOption.WriteWithResponse);
```

### Linux — Full Scan + Connect Skeleton

```csharp
// Source: Linux.Bluetooth GitHub (SuessLabs)
// https://github.com/SuessLabs/Linux.Bluetooth
// Package: Linux.Bluetooth 5.67.1

using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;

// Preflight: ensure bluetooth group membership
// (implement using `id -nG` subprocess or /etc/group parse)

// Scan
IAdapter1 adapter = await BlueZManager.GetAdapterAsync("/org/bluez/hci0");
// fallback: (await BlueZManager.GetAdaptersAsync()).FirstOrDefault()

adapter.DeviceFound += async (IAdapter1 a, DeviceFoundEventArgs e) => {
    // e.Device: IDevice1 — get properties via await e.Device.GetAllAsync()
    // props.Address, props.Name, props.RSSI, props.ManufacturerData
};

await adapter.StartDiscoveryAsync();
// ... await scan duration or cancellation ...
await adapter.StopDiscoveryAsync();

// Connect
IDevice1 device = await adapter.GetDeviceAsync(macAddress)
    ?? throw new InvalidOperationException($"Device {macAddress} not found. Rescan.");

device.Disconnected += async (IDevice1 d, BlueZEventArgs e) => { /* trigger FSM reconnect */ };

await device.ConnectAsync();
await device.WaitForPropertyValueAsync("Connected", value: true, TimeSpan.FromSeconds(15));
await device.WaitForPropertyValueAsync("ServicesResolved", value: true, TimeSpan.FromSeconds(15));

// GATT
IGattService1 svc = await device.GetServiceAsync(serviceUuid);
IGattCharacteristic1 ch = await svc.GetCharacteristicAsync(charUuid);

// Subscribe
ch.Value += async (IGattCharacteristic1 c, GattCharacteristicValueEventArgs e) => {
    NotificationReceived?.Invoke(this, e.Value);
};
await ch.StartNotifyAsync();

// Write
await ch.WriteValueAsync(payload, new Dictionary<string, object>());

// Disconnect
await device.DisconnectAsync();
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Plugin.BlueZ (NuGet) | Linux.Bluetooth 5.67.1 (successor) | 2022-2023 | Plugin.BlueZ going read-only; migrate to Linux.Bluetooth for continued support |
| DeviceWatcher enumeration on Windows | BluetoothLEAdvertisementWatcher | Always the recommended scan approach | DeviceWatcher enumerates paired/cached devices only; Advertisement watcher discovers unpaired devices needed for EcoFlow |
| Windows SDK Contracts NuGet package | TFM-based WinRT projection (net10.0-windows10.0.19041.0) | .NET 5+ | No extra NuGet; implicit CsWinRT projection via TFM suffix |

**Deprecated/outdated:**

- `Plugin.BlueZ`: Will be marked read-only; use `Linux.Bluetooth` instead.
- `Microsoft.Windows.SDK.Contracts` NuGet: Replaced by TFM-embedded Windows SDK version (e.g., `net10.0-windows10.0.19041.0`).
- `hashtagchris/DotNet-BlueZ`: Archived; superseded by `Linux.Bluetooth`.

---

## Environment Availability

The adapters target remote platforms (Windows 10/11, Linux with BlueZ). This is development work on macOS; the adapters cannot be integration-tested locally. The phase produces implementations that build cross-platform and are validated on the target OS.

| Dependency | Required By | Available (dev machine) | Notes |
|------------|-------------|-------------------------|-------|
| .NET 10.0 SDK | Build | 10.0.105 (confirmed) | Present |
| Windows 10/11 with BT adapter | WinRT adapter test | N/A (macOS dev) | Requires Windows test machine |
| Linux with BlueZ >= 5.50 | BlueZ adapter test | N/A (macOS dev) | Requires Linux test machine |
| EcoFlow Delta 3 / Delta 3 Max | End-to-end BLE test | Present (existing device) | Same device used for macOS testing |

**Missing dependencies with no fallback:**

- Windows test machine — required to validate CONN-03. The adapter can be built and unit-tested on macOS but cannot be integration-tested.
- Linux test machine with BlueZ — required to validate CONN-04. Verification must happen on target OS.

---

## Validation Architecture

No automated integration tests are possible without the target hardware and OS. The phase introduces two new platform projects — build verification and manual testing are the primary gates.

### Test Framework

| Property | Value |
|----------|-------|
| Framework | None (no .NET test project exists) |
| Config file | None — see Wave 0 below |
| Quick run command | `dotnet build service/src/EcoFlowMonitor.sln` |
| Full suite command | Manual test on Windows/Linux target + `dotnet build` |

### Phase Requirements -> Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CONN-03 | BLE connects on Windows, ECDH completes, data streams | Manual (Windows target) | `dotnet build` (compilation gate) | N/A |
| CONN-04 | BLE connects on Linux, readable error if not in bluetooth group | Manual (Linux target) | `dotnet build` (compilation gate) | N/A |

### Sampling Rate

- **Per task commit:** `dotnet build service/src/EcoFlowMonitor.sln` — ensures no compile errors on macOS (both platform projects compile under their respective TFMs; build will only include the active platform)
- **Per wave merge:** Build on dev machine + manual BLE test on appropriate target OS
- **Phase gate:** CONN-03 verified on Windows, CONN-04 verified on Linux (including bluetooth group error path), before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] No .NET test project exists — integration tests are manual-only for this phase (platform-native BLE not mockable at unit-test level without significant infrastructure investment)
- [ ] Build of Windows platform project (`EcoFlowMonitor.Platform.Windows.csproj`) requires TFM update from `net10.0-windows` to `net10.0-windows10.0.19041.0` before WinRT BLE types are available

---

## Open Questions

1. **Device address format on Windows for EcoFlow Delta 3**
   - What we know: `BluetoothLEAdvertisementWatcher` provides a `ulong` address per advertisement; `BleAdvertisement.DeviceId` is a string.
   - What's unclear: Whether EcoFlow Delta 3 uses a static address (which would allow reliable address-based reconnect without re-scan) or a random resolvable private address.
   - Recommendation: Default to re-scan on every reconnect (8-second window, same as existing macOS behavior) and look up address from cache — this avoids the static vs. random address ambiguity.

2. **Linux adapter: GetDeviceAsync by address vs. scan cache**
   - What we know: `Linux.Bluetooth` provides `GetDevicesAsync()` which returns already-known devices; `DeviceFound` fires during active discovery.
   - What's unclear: Whether `GetDeviceAsync(macAddress)` is available as a direct lookup in v5.67.1, or whether we must filter `GetDevicesAsync()`.
   - Recommendation: Use `(await adapter.GetDevicesAsync()).FirstOrDefault(d => d.Address == macAddress)` as a fallback after discovery, matching the CoreBluetooth pattern.

3. **PlatformServiceFactory registration for BLE adapters**
   - What we know: `PlatformServiceFactory.RegisterWindows` uses reflection to load types from the Windows platform assembly; BLE is currently registered separately in the same method using `OperatingSystem.IsMacOS()`.
   - What's unclear: Whether the Windows and Linux BLE adapter types should be loaded via reflection (like other platform services) or directly referenced.
   - Recommendation: Register them directly in the `RegisterWindows` / `RegisterLinux` helpers using reflection (same as notification service etc.), so the pattern is consistent. The BLE adapter registration block in `PlatformServiceFactory` that currently falls back to `StubBleAdapter` for non-macOS must be updated to load from the platform assembly.

---

## Project Constraints (from CLAUDE.md)

The following directives from CLAUDE.md constrain this phase:

- **Tech stack locked:** .NET 10 + Avalonia UI — not changing.
- **BLE libraries locked:** WinRT on Windows, BlueZ on Linux, CoreBluetooth on macOS — no cross-platform BLE abstraction layer (e.g., 32feet.NET) permitted.
- **Devices in scope:** EcoFlow Delta 3 and Delta 3 Max only.
- **No test suite:** Zero C# unit tests currently — tests may be added alongside implementation but are not required to exist before implementation begins.
- **File-scoped namespaces:** All C# files must use `namespace EcoFlowMonitor.Platform.Windows;` / `namespace EcoFlowMonitor.Platform.Linux;` (never block-scoped).
- **PascalCase for types/methods/public members; `_camelCase` for private fields.**
- **No regions:** Use comment banners for section separation (e.g., `// -- Scan --`).
- **Static classes for protocol helpers** — `BlueZPermissionCheck` should be static.
- **EcoFlow API is undocumented/reverse-engineered** — no assumptions about protocol stability; ECDH handshake must reset on every reconnect.
- **GSD workflow enforcement:** All edits must go through `/gsd:execute-phase` — no direct repo edits outside the workflow.

---

## Sources

### Primary (HIGH confidence)

- [Microsoft Learn — Bluetooth GATT Client](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/gatt-client) — WinRT API flow: scan, connect, service discovery, notify subscribe, write; lazy-connect note; ValueChanged; DataWriter/DataReader patterns
- [Microsoft Learn — BluetoothLEAdvertisementWatcher](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementwatcher?view=winrt-26100) — advertisement scan API, ScanningMode, Received event, ManufacturerData access
- [Microsoft Learn — BluetoothLEDevice.FromBluetoothAddressAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothledevice.frombluetoothaddressasync?view=winrt-26100) — returns null if device not in cache; lazy-connect behavior documented
- [NuGet — Linux.Bluetooth 5.67.1](https://www.nuget.org/packages/Linux.Bluetooth/) — version, netstandard2.0 target, Tmds.DBus dependency, net10.0 compatibility
- [GitHub — SuessLabs/Linux.Bluetooth](https://github.com/SuessLabs/Linux.Bluetooth) — DeviceFound event, ConnectAsync, WaitForPropertyValueAsync, characteristic Value event, StartNotifyAsync

### Secondary (MEDIUM confidence)

- [Microsoft Q&A — Bluetooth LE Multiply times ValueChanged notification](https://learn.microsoft.com/en-us/answers/questions/1191076/bluetooth-low-energy-multiply-times-valuechanged-n) — confirmed handler accumulation pitfall on reconnect; community-verified
- [Suess Labs — .NET and Linux Bluetooth](https://suesslabs.com/csharp/net-and-linux-bluetooth/) — setup guide; D-Bus approach; Plugin.BlueZ deprecation

### Tertiary (LOW confidence)

- D-Bus bluetooth group requirement: observed behavior across Arch/Ubuntu/Debian bug reports — not explicitly documented in Linux.Bluetooth; inferred from D-Bus access control behavior.

---

## Metadata

**Confidence breakdown:**

- Standard stack: HIGH — WinRT is first-party Microsoft; Linux.Bluetooth is the documented successor to Plugin.BlueZ; both verified against current NuGet/docs
- Architecture: HIGH — CoreBluetooth macOS adapter is the reference implementation; both new adapters mirror its pattern with platform-specific events
- Pitfalls: HIGH (Windows ValueChanged, lazy connect, null return) — confirmed by official Microsoft docs and Q&A; MEDIUM (Linux group check) — inferred from D-Bus policy behavior

**Research date:** 2026-03-30
**Valid until:** 2026-06-30 (stable APIs; Linux.Bluetooth may release 6.x stable; recheck before upgrading)
