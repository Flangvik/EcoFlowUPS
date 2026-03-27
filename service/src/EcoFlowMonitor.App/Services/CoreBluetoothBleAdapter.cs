// This file only compiles under net10.0-macos (requires CoreBluetooth framework bindings).
#if MACCATALYST || MACOS
using CoreBluetooth;
using Foundation;
using EcoFlowMonitor.Logging;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Services;

/// <summary>
/// Native macOS IBleAdapter implementation using CoreBluetooth.
/// Uses CBCentralManager for scanning and CBPeripheral for GATT operations.
/// </summary>
public sealed class CoreBluetoothBleAdapter : IBleAdapter, IDisposable
{
    private readonly CBCentralManager _centralManager;
    private readonly CentralManagerDelegate _centralDelegate;

    /// <summary>
    /// Cache of discovered peripherals keyed by UUID string.
    /// CoreBluetooth requires holding a strong reference to peripherals
    /// to prevent them from being garbage-collected before connection.
    /// </summary>
    private readonly Dictionary<string, CBPeripheral> _discoveredPeripherals = new();
    private readonly object _peripheralLock = new();

    public event EventHandler<BleAdvertisement>? AdvertisementReceived;

    public CoreBluetoothBleAdapter()
    {
        _centralDelegate = new CentralManagerDelegate(this);
        _centralManager = new CBCentralManager(_centralDelegate, null);
        Logger.Log("CoreBluetoothBleAdapter: created CBCentralManager");
    }

    public async Task StartScanAsync(CancellationToken ct = default)
    {
        Logger.Log("CoreBluetoothBleAdapter: StartScanAsync requested");

        // Wait for the central manager to reach PoweredOn state.
        await _centralDelegate.WaitForPoweredOnAsync(ct);

        Logger.Log("CoreBluetoothBleAdapter: CBCentralManager is PoweredOn, starting scan");

        // Clear previous discoveries for a fresh scan.
        lock (_peripheralLock)
        {
            _discoveredPeripherals.Clear();
        }

        // Scan for all peripherals; pass null to discover everything.
        // Request CBCentralManagerScanOptionAllowDuplicatesKey = false (default).
        _centralManager.ScanForPeripherals((CBUUID[]?)null);

        // Keep scanning until cancellation is requested.
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // Expected — caller cancelled the scan.
        }
        finally
        {
            StopScan();
        }
    }

    public void StopScan()
    {
        if (_centralManager.IsScanning)
        {
            _centralManager.StopScan();
            Logger.Log("CoreBluetoothBleAdapter: scan stopped");
        }
    }

    public IBleGattConnection CreateConnection()
    {
        return new CoreBluetoothGattConnection(_centralManager, _centralDelegate, this);
    }

    /// <summary>
    /// Retrieves a previously discovered peripheral by its UUID string.
    /// Returns null if the peripheral was not found in the cache.
    /// </summary>
    internal CBPeripheral? GetDiscoveredPeripheral(string uuid)
    {
        lock (_peripheralLock)
        {
            _discoveredPeripherals.TryGetValue(uuid, out var peripheral);
            return peripheral;
        }
    }

    internal void OnAdvertisementDiscovered(CBPeripheral peripheral, NSDictionary advertisementData, NSNumber rssi)
    {
        var uuid = peripheral.Identifier.ToString();
        var name = peripheral.Name ?? "";

        // Hold a strong reference so the peripheral isn't deallocated.
        lock (_peripheralLock)
        {
            _discoveredPeripherals[uuid] = peripheral;
        }

        // Parse manufacturer-specific data from the advertisement.
        ushort manufacturerId = 0;
        byte[]? manufacturerData = null;

        if (advertisementData.ObjectForKey(CBAdvertisement.DataManufacturerDataKey) is NSData mfgNSData)
        {
            var raw = mfgNSData.ToArray();
            if (raw.Length >= 2)
            {
                // First 2 bytes are the company identifier in little-endian.
                manufacturerId = (ushort)(raw[0] | (raw[1] << 8));
                manufacturerData = raw.Length > 2 ? raw[2..] : Array.Empty<byte>();
            }
        }

        var advertisement = new BleAdvertisement
        {
            DeviceId = uuid,
            Name = name,
            Rssi = rssi.Int32Value,
            ManufacturerId = manufacturerId,
            ManufacturerData = manufacturerData
        };

        Logger.Log($"CoreBluetoothBleAdapter: discovered {name} ({uuid}) RSSI={rssi.Int32Value}");
        AdvertisementReceived?.Invoke(this, advertisement);
    }

    public void Dispose()
    {
        StopScan();
        _centralManager.Dispose();
        Logger.Log("CoreBluetoothBleAdapter: disposed");
    }

    // ──────────────────────────────────────────────────────────────────
    //  CBCentralManagerDelegate
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delegate that receives CBCentralManager callbacks for state changes,
    /// discovery, connection, and disconnection events.
    /// </summary>
    internal sealed class CentralManagerDelegate : CBCentralManagerDelegate
    {
        private readonly CoreBluetoothBleAdapter _adapter;
        private TaskCompletionSource? _poweredOnTcs;
        private readonly object _poweredOnLock = new();

        // Connection callbacks keyed by peripheral UUID.
        private readonly Dictionary<string, TaskCompletionSource<bool>> _connectTcsMap = new();
        private readonly object _connectLock = new();

        // Disconnection callbacks keyed by peripheral UUID.
        private readonly Dictionary<string, TaskCompletionSource> _disconnectTcsMap = new();
        private readonly object _disconnectLock = new();

        public CentralManagerDelegate(CoreBluetoothBleAdapter adapter)
        {
            _adapter = adapter;
        }

        /// <summary>
        /// Waits until the central manager reports CBManagerState.PoweredOn.
        /// Throws OperationCanceledException if ct fires, or InvalidOperationException
        /// if the manager enters an unrecoverable state.
        /// </summary>
        public Task WaitForPoweredOnAsync(CancellationToken ct)
        {
            if (_adapter._centralManager.State == CBManagerState.PoweredOn)
                return Task.CompletedTask;

            lock (_poweredOnLock)
            {
                _poweredOnTcs ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            ct.Register(() =>
            {
                lock (_poweredOnLock)
                {
                    _poweredOnTcs?.TrySetCanceled(ct);
                }
            });

            return _poweredOnTcs.Task;
        }

        public override void UpdatedState(CBCentralManager central)
        {
            Logger.Log($"CoreBluetoothBleAdapter: CBCentralManager state → {central.State}");

            lock (_poweredOnLock)
            {
                if (_poweredOnTcs == null) return;

                switch (central.State)
                {
                    case CBManagerState.PoweredOn:
                        _poweredOnTcs.TrySetResult();
                        break;
                    case CBManagerState.PoweredOff:
                        _poweredOnTcs.TrySetException(
                            new InvalidOperationException("Bluetooth is powered off."));
                        break;
                    case CBManagerState.Unauthorized:
                        _poweredOnTcs.TrySetException(
                            new InvalidOperationException("Bluetooth access is not authorized. Check System Settings > Privacy & Security > Bluetooth."));
                        break;
                    case CBManagerState.Unsupported:
                        _poweredOnTcs.TrySetException(
                            new InvalidOperationException("Bluetooth Low Energy is not supported on this hardware."));
                        break;
                    // Resetting / Unknown — keep waiting.
                }
            }
        }

        public override void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral,
            NSDictionary advertisementData, NSNumber rssi)
        {
            _adapter.OnAdvertisementDiscovered(peripheral, advertisementData, rssi);
        }

        public override void ConnectedPeripheral(CBCentralManager central, CBPeripheral peripheral)
        {
            var uuid = peripheral.Identifier.ToString();
            Logger.Log($"CoreBluetoothBleAdapter: connected to {uuid}");

            lock (_connectLock)
            {
                if (_connectTcsMap.TryGetValue(uuid, out var tcs))
                {
                    tcs.TrySetResult(true);
                    _connectTcsMap.Remove(uuid);
                }
            }
        }

        public override void FailedToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
        {
            var uuid = peripheral.Identifier.ToString();
            Logger.Log($"CoreBluetoothBleAdapter: failed to connect to {uuid} — {error?.LocalizedDescription ?? "unknown error"}");

            lock (_connectLock)
            {
                if (_connectTcsMap.TryGetValue(uuid, out var tcs))
                {
                    tcs.TrySetException(new InvalidOperationException(
                        $"Failed to connect to peripheral {uuid}: {error?.LocalizedDescription ?? "unknown error"}"));
                    _connectTcsMap.Remove(uuid);
                }
            }
        }

        public override void DisconnectedPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
        {
            var uuid = peripheral.Identifier.ToString();
            Logger.Log($"CoreBluetoothBleAdapter: disconnected from {uuid} — {error?.LocalizedDescription ?? "clean"}");

            lock (_disconnectLock)
            {
                if (_disconnectTcsMap.TryGetValue(uuid, out var tcs))
                {
                    tcs.TrySetResult();
                    _disconnectTcsMap.Remove(uuid);
                }
            }
        }

        /// <summary>
        /// Registers a TaskCompletionSource that will be completed when the peripheral connects.
        /// </summary>
        internal void RegisterConnectCallback(string peripheralUuid, TaskCompletionSource<bool> tcs)
        {
            lock (_connectLock)
            {
                _connectTcsMap[peripheralUuid] = tcs;
            }
        }

        /// <summary>
        /// Registers a TaskCompletionSource that will be completed when the peripheral disconnects.
        /// </summary>
        internal void RegisterDisconnectCallback(string peripheralUuid, TaskCompletionSource tcs)
        {
            lock (_disconnectLock)
            {
                _disconnectTcsMap[peripheralUuid] = tcs;
            }
        }
    }
}

// ──────────────────────────────────────────────────────────────────────
//  CoreBluetoothGattConnection — IBleGattConnection over CBPeripheral
// ──────────────────────────────────────────────────────────────────────

/// <summary>
/// BLE GATT connection backed by a native CBPeripheral.
/// Bridges CoreBluetooth delegate callbacks to async Task-based APIs.
/// </summary>
internal sealed class CoreBluetoothGattConnection : IBleGattConnection
{
    private readonly CBCentralManager _centralManager;
    private readonly CoreBluetoothBleAdapter.CentralManagerDelegate _centralDelegate;
    private readonly CoreBluetoothBleAdapter _adapter;

    private CBPeripheral? _peripheral;
    private PeripheralDelegate? _peripheralDelegate;
    private string _deviceId = "";

    public bool IsConnected { get; private set; }
    public event EventHandler<byte[]>? NotificationReceived;

    public CoreBluetoothGattConnection(
        CBCentralManager centralManager,
        CoreBluetoothBleAdapter.CentralManagerDelegate centralDelegate,
        CoreBluetoothBleAdapter adapter)
    {
        _centralManager = centralManager;
        _centralDelegate = centralDelegate;
        _adapter = adapter;
    }

    public async Task ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        Logger.Log($"CoreBluetoothGattConnection: connecting to {deviceId}");
        _deviceId = deviceId;

        // Look up the peripheral from the adapter's discovered-peripheral cache.
        _peripheral = _adapter.GetDiscoveredPeripheral(deviceId);
        if (_peripheral == null)
        {
            throw new InvalidOperationException(
                $"Peripheral {deviceId} not found. Run a scan first so the adapter can discover it.");
        }

        // Attach our peripheral delegate to receive service/characteristic callbacks.
        _peripheralDelegate = new PeripheralDelegate(this);
        _peripheral.Delegate = _peripheralDelegate;

        // Register a TCS for the connection callback on the central manager.
        var connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _centralDelegate.RegisterConnectCallback(deviceId, connectTcs);

        // Initiate connection.
        _centralManager.ConnectPeripheral(_peripheral);

        // Wait with timeout.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            await connectTcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _centralManager.CancelPeripheralConnection(_peripheral);
            throw new TimeoutException($"Connection to {deviceId} timed out after 20 seconds.");
        }

        IsConnected = true;

        // Discover all services up front.
        await DiscoverServicesAsync(ct);

        Logger.Log($"CoreBluetoothGattConnection: fully connected and services discovered for {deviceId}");
    }

    public async Task SubscribeNotifyAsync(Guid serviceUuid, Guid characteristicUuid, CancellationToken ct = default)
    {
        if (_peripheral == null || _peripheralDelegate == null)
            throw new InvalidOperationException("Not connected.");

        var characteristic = FindCharacteristic(serviceUuid, characteristicUuid);

        Logger.Log($"CoreBluetoothGattConnection: subscribing to notifications on {characteristicUuid}");

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _peripheralDelegate.RegisterNotifyCallback(characteristic.UUID.ToString(), tcs);

        _peripheral.SetNotifyValue(true, characteristic);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await tcs.Task.WaitAsync(cts.Token);

        Logger.Log($"CoreBluetoothGattConnection: notifications enabled for {characteristicUuid}");
    }

    public async Task WriteAsync(Guid serviceUuid, Guid characteristicUuid, byte[] data, CancellationToken ct = default)
    {
        if (_peripheral == null || _peripheralDelegate == null)
            throw new InvalidOperationException("Not connected.");

        var characteristic = FindCharacteristic(serviceUuid, characteristicUuid);

        Logger.Log($"CoreBluetoothGattConnection: writing {data.Length} bytes to {characteristicUuid}");

        var tcs = new TaskCompletionSource<NSError?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _peripheralDelegate.RegisterWriteCallback(characteristic.UUID.ToString(), tcs);

        var nsData = NSData.FromArray(data);
        _peripheral.WriteValue(nsData, characteristic, CBCharacteristicWriteType.WithResponse);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var error = await tcs.Task.WaitAsync(cts.Token);
        if (error != null)
        {
            throw new InvalidOperationException(
                $"Write to {characteristicUuid} failed: {error.LocalizedDescription}");
        }

        Logger.Log($"CoreBluetoothGattConnection: write completed for {characteristicUuid}");
    }

    public async Task DisconnectAsync()
    {
        if (_peripheral == null)
        {
            IsConnected = false;
            return;
        }

        Logger.Log($"CoreBluetoothGattConnection: disconnecting from {_deviceId}");

        if (IsConnected)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _centralDelegate.RegisterDisconnectCallback(_deviceId, tcs);

            _centralManager.CancelPeripheralConnection(_peripheral);

            // Wait up to 5 seconds for the disconnect callback.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"CoreBluetoothGattConnection: disconnect timed out for {_deviceId}");
            }
        }

        IsConnected = false;
        _peripheral.Delegate = null;
        _peripheral = null;
        _peripheralDelegate = null;

        Logger.Log($"CoreBluetoothGattConnection: disconnected from {_deviceId}");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Discovers all services on the peripheral, then discovers all
    /// characteristics on each service. Populates the CBPeripheral's
    /// Services and their Characteristics properties.
    /// </summary>
    private async Task DiscoverServicesAsync(CancellationToken ct)
    {
        if (_peripheral == null || _peripheralDelegate == null) return;

        // Discover services.
        var servicesTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _peripheralDelegate.RegisterServiceDiscoveryCallback(servicesTcs);

        _peripheral.DiscoverServices();

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            await servicesTcs.Task.WaitAsync(cts.Token);
        }

        if (_peripheral.Services == null || _peripheral.Services.Length == 0)
        {
            Logger.Log("CoreBluetoothGattConnection: no services found on peripheral");
            return;
        }

        Logger.Log($"CoreBluetoothGattConnection: discovered {_peripheral.Services.Length} services");

        // Discover characteristics on every service.
        foreach (var service in _peripheral.Services)
        {
            var charTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _peripheralDelegate.RegisterCharacteristicDiscoveryCallback(service.UUID.ToString(), charTcs);

            _peripheral.DiscoverCharacteristics(service);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            await charTcs.Task.WaitAsync(cts.Token);

            var count = service.Characteristics?.Length ?? 0;
            Logger.Log($"CoreBluetoothGattConnection: service {service.UUID} → {count} characteristics");
        }
    }

    /// <summary>
    /// Finds a CBCharacteristic matching the given service and characteristic GUIDs
    /// among the already-discovered services on the peripheral.
    /// </summary>
    private CBCharacteristic FindCharacteristic(Guid serviceUuid, Guid characteristicUuid)
    {
        if (_peripheral?.Services == null)
            throw new InvalidOperationException("Services have not been discovered yet.");

        var svcCbuuid = CBUUID.FromString(serviceUuid.ToString());
        var charCbuuid = CBUUID.FromString(characteristicUuid.ToString());

        foreach (var svc in _peripheral.Services)
        {
            if (!svc.UUID.Equals(svcCbuuid))
                continue;

            if (svc.Characteristics == null)
                continue;

            foreach (var ch in svc.Characteristics)
            {
                if (ch.UUID.Equals(charCbuuid))
                    return ch;
            }
        }

        throw new InvalidOperationException(
            $"Characteristic {characteristicUuid} not found in service {serviceUuid}.");
    }

    internal void OnNotificationReceived(byte[] data)
    {
        NotificationReceived?.Invoke(this, data);
    }

    // ──────────────────────────────────────────────────────────────────
    //  CBPeripheralDelegate
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delegate that receives CBPeripheral callbacks for service discovery,
    /// characteristic discovery, value updates, writes, and notify state changes.
    /// </summary>
    private sealed class PeripheralDelegate : CBPeripheralDelegate
    {
        private readonly CoreBluetoothGattConnection _connection;

        // Service discovery.
        private TaskCompletionSource? _serviceDiscoveryTcs;
        private readonly object _serviceDiscoveryLock = new();

        // Characteristic discovery keyed by service UUID string.
        private readonly Dictionary<string, TaskCompletionSource> _charDiscoveryMap = new();
        private readonly object _charDiscoveryLock = new();

        // Notify subscription keyed by characteristic UUID string.
        private readonly Dictionary<string, TaskCompletionSource> _notifyMap = new();
        private readonly object _notifyLock = new();

        // Write acknowledgements keyed by characteristic UUID string.
        private readonly Dictionary<string, TaskCompletionSource<NSError?>> _writeMap = new();
        private readonly object _writeLock = new();

        public PeripheralDelegate(CoreBluetoothGattConnection connection)
        {
            _connection = connection;
        }

        // ── Registration helpers ─────────────────────────────────────

        internal void RegisterServiceDiscoveryCallback(TaskCompletionSource tcs)
        {
            lock (_serviceDiscoveryLock)
            {
                _serviceDiscoveryTcs = tcs;
            }
        }

        internal void RegisterCharacteristicDiscoveryCallback(string serviceUuid, TaskCompletionSource tcs)
        {
            lock (_charDiscoveryLock)
            {
                _charDiscoveryMap[serviceUuid] = tcs;
            }
        }

        internal void RegisterNotifyCallback(string characteristicUuid, TaskCompletionSource tcs)
        {
            lock (_notifyLock)
            {
                _notifyMap[characteristicUuid] = tcs;
            }
        }

        internal void RegisterWriteCallback(string characteristicUuid, TaskCompletionSource<NSError?> tcs)
        {
            lock (_writeLock)
            {
                _writeMap[characteristicUuid] = tcs;
            }
        }

        // ── CBPeripheralDelegate overrides ───────────────────────────

        public override void DiscoveredService(CBPeripheral peripheral, NSError? error)
        {
            if (error != null)
                Logger.Log($"PeripheralDelegate: service discovery error — {error.LocalizedDescription}");

            lock (_serviceDiscoveryLock)
            {
                if (error != null)
                    _serviceDiscoveryTcs?.TrySetException(new InvalidOperationException(
                        $"Service discovery failed: {error.LocalizedDescription}"));
                else
                    _serviceDiscoveryTcs?.TrySetResult();

                _serviceDiscoveryTcs = null;
            }
        }

        public override void DiscoveredCharacteristics(CBPeripheral peripheral, CBService service, NSError? error)
        {
            var key = service.UUID.ToString();

            if (error != null)
                Logger.Log($"PeripheralDelegate: characteristic discovery error on {key} — {error.LocalizedDescription}");

            lock (_charDiscoveryLock)
            {
                if (_charDiscoveryMap.TryGetValue(key, out var tcs))
                {
                    if (error != null)
                        tcs.TrySetException(new InvalidOperationException(
                            $"Characteristic discovery failed on service {key}: {error.LocalizedDescription}"));
                    else
                        tcs.TrySetResult();

                    _charDiscoveryMap.Remove(key);
                }
            }
        }

        public override void UpdatedCharacterteristicValue(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
        {
            if (error != null)
            {
                Logger.Log($"PeripheralDelegate: value update error on {characteristic.UUID} — {error.LocalizedDescription}");
                return;
            }

            var data = characteristic.Value?.ToArray();
            if (data != null)
            {
                Logger.Log($"PeripheralDelegate: notification received on {characteristic.UUID}, {data.Length} bytes");
                _connection.OnNotificationReceived(data);
            }
        }

        public override void UpdatedNotificationState(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
        {
            var key = characteristic.UUID.ToString();

            if (error != null)
                Logger.Log($"PeripheralDelegate: notify state update error on {key} — {error.LocalizedDescription}");
            else
                Logger.Log($"PeripheralDelegate: notify state updated for {key}, isNotifying={characteristic.IsNotifying}");

            lock (_notifyLock)
            {
                if (_notifyMap.TryGetValue(key, out var tcs))
                {
                    if (error != null)
                        tcs.TrySetException(new InvalidOperationException(
                            $"Failed to enable notifications on {key}: {error.LocalizedDescription}"));
                    else
                        tcs.TrySetResult();

                    _notifyMap.Remove(key);
                }
            }
        }

        public override void WroteCharacteristicValue(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
        {
            var key = characteristic.UUID.ToString();

            if (error != null)
                Logger.Log($"PeripheralDelegate: write error on {key} — {error.LocalizedDescription}");
            else
                Logger.Log($"PeripheralDelegate: write acknowledged for {key}");

            lock (_writeLock)
            {
                if (_writeMap.TryGetValue(key, out var tcs))
                {
                    tcs.TrySetResult(error);
                    _writeMap.Remove(key);
                }
            }
        }
    }
}
#endif
