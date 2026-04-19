namespace EcoFlowMonitor.Platform;

/// <summary>
/// Platform-abstracted BLE advertisement data from a scan result.
/// </summary>
public class BleAdvertisement
{
    public string DeviceId { get; init; } = "";
    public string Name { get; init; } = "";
    public int Rssi { get; init; }
    public byte[]? ManufacturerData { get; init; }
    public ushort ManufacturerId { get; init; }
}

/// <summary>
/// Platform-abstracted BLE GATT connection for reading/writing characteristics.
/// </summary>
public interface IBleGattConnection : IAsyncDisposable
{
    bool IsConnected { get; }
    event EventHandler<byte[]>? NotificationReceived;
    Task ConnectAsync(string deviceId, CancellationToken ct = default);
    Task SubscribeNotifyAsync(Guid serviceUuid, Guid characteristicUuid, CancellationToken ct = default);
    Task WriteAsync(Guid serviceUuid, Guid characteristicUuid, byte[] data, CancellationToken ct = default);
    Task DisconnectAsync();
}

/// <summary>
/// Platform-abstracted BLE adapter for scanning and connecting.
/// Implementations live in Platform.Windows/macOS/Linux projects.
/// </summary>
public interface IBleAdapter
{
    event EventHandler<BleAdvertisement>? AdvertisementReceived;
    Task StartScanAsync(CancellationToken ct = default);
    void StopScan();
    IBleGattConnection CreateConnection();
}
