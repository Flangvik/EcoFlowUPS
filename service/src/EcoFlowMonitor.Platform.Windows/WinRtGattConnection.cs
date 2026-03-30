using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Storage.Streams;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Windows;

/// <summary>
/// Windows GATT connection using WinRT BluetoothLEDevice.
/// Implements IBleGattConnection with the lazy-connect workaround (BluetoothCacheMode.Uncached)
/// and ValueChanged handler accumulation guard (-= before +=).
/// </summary>
public class WinRtGattConnection : IBleGattConnection
{
    private readonly Dictionary<ulong, BluetoothLEAdvertisementReceivedEventArgs> _advertisementCache;
    private BluetoothLEDevice? _device;
    private IReadOnlyList<GattDeviceService>? _services;
    private GattCharacteristic? _subscribedCharacteristic;
    private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>? _valueChangedHandler;

    public bool IsConnected { get; private set; }
    public event EventHandler<byte[]>? NotificationReceived;

    public WinRtGattConnection(
        Dictionary<ulong, BluetoothLEAdvertisementReceivedEventArgs> advertisementCache)
    {
        _advertisementCache = advertisementCache;
    }

    /// <summary>
    /// Connect to the device identified by deviceId (ulong decimal string, as emitted by
    /// WinRtBleAdapter.OnAdvertisementReceived).
    /// </summary>
    public async Task ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        // DeviceId is stored as ulong.ToString() (decimal) in WinRtBleAdapter.
        ulong address = ulong.Parse(deviceId);

        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        if (_device == null)
            throw new InvalidOperationException(
                $"Device {deviceId} not found in WinRT cache. A scan must precede every connect attempt.");

        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        // CRITICAL — force real OS connection.
        // FromBluetoothAddressAsync alone is lazy; the device object exists but no BLE
        // connection is established yet. Calling GetGattServicesAsync(Uncached) immediately
        // triggers the actual OS connection and populates the service list.
        // BluetoothCacheMode.Cached can return stale data and produce Unreachable errors
        // after a device power cycle — always use Uncached on first connect.
        var servicesResult = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        if (servicesResult.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException(
                $"GATT service discovery failed: {servicesResult.Status}");

        // Hold a strong reference — disposing a GattDeviceService disconnects from that service.
        _services = servicesResult.Services;
        IsConnected = true;
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            IsConnected = false;
    }

    /// <summary>
    /// Subscribe to GATT characteristic notifications.
    /// Unsubscribes any existing handler first to prevent accumulation on reconnect.
    /// </summary>
    public async Task SubscribeNotifyAsync(
        Guid serviceUuid,
        Guid characteristicUuid,
        CancellationToken ct = default)
    {
        var svc = _services?.FirstOrDefault(s => s.Uuid == serviceUuid)
            ?? throw new InvalidOperationException($"Service {serviceUuid} not found");

        // Uncached for the same reason as ConnectAsync: avoid stale cached characteristic list.
        var charsResult = await svc.GetCharacteristicsForUuidAsync(
            characteristicUuid, BluetoothCacheMode.Uncached);
        if (charsResult.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException(
                $"Characteristic discovery failed for {characteristicUuid}: {charsResult.Status}");

        var ch = charsResult.Characteristics.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Characteristic {characteristicUuid} not found in service {serviceUuid}");

        // CRITICAL — prevent handler accumulation on reconnect.
        // Without the -= guard, each reconnect adds another subscription and the notification
        // callback fires N times per packet after N reconnects.
        if (_valueChangedHandler != null)
            _subscribedCharacteristic!.ValueChanged -= _valueChangedHandler;

        _valueChangedHandler = (GattCharacteristic sender, GattValueChangedEventArgs args) =>
        {
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            byte[] data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);
            NotificationReceived?.Invoke(this, data);
        };
        _subscribedCharacteristic = ch;
        ch.ValueChanged += _valueChangedHandler;

        await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);
    }

    /// <summary>
    /// Write data to a GATT characteristic with response confirmation.
    /// </summary>
    public async Task WriteAsync(
        Guid serviceUuid,
        Guid characteristicUuid,
        byte[] data,
        CancellationToken ct = default)
    {
        var svc = _services?.FirstOrDefault(s => s.Uuid == serviceUuid)
            ?? throw new InvalidOperationException($"Service {serviceUuid} not found");

        var charsResult = await svc.GetCharacteristicsForUuidAsync(
            characteristicUuid, BluetoothCacheMode.Uncached);
        if (charsResult.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException(
                $"Characteristic discovery failed for {characteristicUuid}: {charsResult.Status}");

        var ch = charsResult.Characteristics.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Characteristic {characteristicUuid} not found in service {serviceUuid}");

        var writer = new DataWriter();
        writer.WriteBytes(data);
        var status = await ch.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithResponse);
        if (status != GattCommunicationStatus.Success)
            throw new InvalidOperationException($"GATT write failed: {status}");
    }

    /// <summary>
    /// Fully disconnect: unsubscribe all event handlers, dispose all GATT service references,
    /// and dispose the device object. Disposing GattDeviceService/BluetoothLEDevice releases
    /// the OS-level BLE connection.
    /// </summary>
    public Task DisconnectAsync()
    {
        // 1. Unsubscribe notification handler first — prevents callbacks after cleanup.
        if (_valueChangedHandler != null && _subscribedCharacteristic != null)
            _subscribedCharacteristic.ValueChanged -= _valueChangedHandler;
        _valueChangedHandler = null;
        _subscribedCharacteristic = null;

        // 2. Unsubscribe connection status handler.
        if (_device != null)
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;

        // 3. Dispose all held service references — each Dispose() releases the corresponding
        //    OS GATT service connection.
        if (_services != null)
            foreach (var svc in _services)
                svc.Dispose();
        _services = null;

        // 4. Dispose the device object itself.
        _device?.Dispose();
        _device = null;

        IsConnected = false;
        return Task.CompletedTask;
    }

    /// <summary>IAsyncDisposable — delegates to DisconnectAsync for resource cleanup.</summary>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
