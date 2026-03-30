using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Linux;

public class BlueZGattConnection : IBleGattConnection
{
    private readonly Dictionary<string, Device> _deviceCache;
    private Device? _device;
    private GattCharacteristicEventHandlerAsync? _notifyHandler;
    private GattCharacteristic? _subscribedCharacteristic;

    public bool IsConnected { get; private set; }
    public event EventHandler<byte[]>? NotificationReceived;

    public BlueZGattConnection(Dictionary<string, Device> deviceCache)
    {
        _deviceCache = deviceCache;
    }

    public async Task ConnectAsync(string deviceId, CancellationToken ct = default)
    {
        _device = _deviceCache.TryGetValue(deviceId, out var cached)
            ? cached
            : throw new InvalidOperationException(
                $"Device {deviceId} not found in scan cache. A scan must precede every connect attempt.");

        _device.Disconnected += OnDisconnectedAsync;

        await _device.ConnectAsync();

        // CRITICAL — wait for transport-level connection
        await _device.WaitForPropertyValueAsync("Connected", value: true, TimeSpan.FromSeconds(15));

        // CRITICAL — wait for GATT service resolution before any GATT operations
        // If this is skipped, GetServiceAsync returns null immediately after ConnectAsync returns.
        await _device.WaitForPropertyValueAsync("ServicesResolved", value: true, TimeSpan.FromSeconds(15));

        IsConnected = true;
    }

    private Task OnDisconnectedAsync(Device device, BlueZEventArgs args)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public async Task SubscribeNotifyAsync(Guid serviceUuid, Guid characteristicUuid, CancellationToken ct = default)
    {
        var svc = await _device!.GetServiceAsync(serviceUuid.ToString())
            ?? throw new InvalidOperationException($"Service {serviceUuid} not found");

        var ch = await svc.GetCharacteristicAsync(characteristicUuid.ToString())
            ?? throw new InvalidOperationException($"Characteristic {characteristicUuid} not found");

        // Unsubscribe previous handler to prevent accumulation (mirrors Windows adapter pattern)
        if (_notifyHandler != null && _subscribedCharacteristic != null)
            _subscribedCharacteristic.Value -= _notifyHandler;

        _notifyHandler = (GattCharacteristic c, GattCharacteristicValueEventArgs e) =>
        {
            NotificationReceived?.Invoke(this, e.Value);
            return Task.CompletedTask;
        };
        _subscribedCharacteristic = ch;
        ch.Value += _notifyHandler;

        await ch.StartNotifyAsync();
    }

    public async Task WriteAsync(Guid serviceUuid, Guid characteristicUuid, byte[] data, CancellationToken ct = default)
    {
        var svc = await _device!.GetServiceAsync(serviceUuid.ToString())
            ?? throw new InvalidOperationException($"Service {serviceUuid} not found");

        var ch = await svc.GetCharacteristicAsync(characteristicUuid.ToString())
            ?? throw new InvalidOperationException($"Characteristic {characteristicUuid} not found");

        // Empty options dict = default (write-with-response)
        await ch.WriteValueAsync(data, new Dictionary<string, object>());
    }

    public async Task DisconnectAsync()
    {
        if (_notifyHandler != null && _subscribedCharacteristic != null)
        {
            _subscribedCharacteristic.Value -= _notifyHandler;
            try { await _subscribedCharacteristic.StopNotifyAsync(); } catch { /* ignore if already disconnected */ }
        }
        _notifyHandler = null;
        _subscribedCharacteristic = null;

        if (_device != null)
            _device.Disconnected -= OnDisconnectedAsync;

        if (_device != null)
        {
            try { await _device.DisconnectAsync(); } catch { /* ignore */ }
        }

        _device = null;
        IsConnected = false;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
