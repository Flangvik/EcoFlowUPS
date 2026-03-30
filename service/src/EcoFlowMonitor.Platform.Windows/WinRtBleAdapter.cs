using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Windows;

/// <summary>
/// Windows BLE adapter using WinRT BluetoothLEAdvertisementWatcher for scanning.
/// Implements IBleAdapter behind the platform abstraction — BleMonitor and BleTransport
/// consume this via the interface unchanged.
/// </summary>
public class WinRtBleAdapter : IBleAdapter
{
    private BluetoothLEAdvertisementWatcher? _watcher;
    private readonly Dictionary<ulong, BluetoothLEAdvertisementReceivedEventArgs> _advertisementCache = new();

    public event EventHandler<BleAdvertisement>? AdvertisementReceived;

    public async Task StartScanAsync(CancellationToken ct = default)
    {
        // Clear stale entries from previous scan sessions — prevents delivering
        // advertisements for devices no longer in range.
        _advertisementCache.Clear();

        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Start();

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // Expected — BleMonitor cancels the token after ~8 seconds.
        }
        finally
        {
            StopScan();
        }
    }

    public void StopScan()
    {
        if (_watcher?.Status == BluetoothLEAdvertisementWatcherStatus.Started)
        {
            // CRITICAL: unsubscribe the handler BEFORE calling Stop() so no
            // advertisement events are delivered after the watcher is stopped.
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stop();
        }
    }

    private void OnAdvertisementReceived(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        // Cache by Bluetooth address ulong — WinRtGattConnection will look up
        // the address from the decimal-string DeviceId during ConnectAsync.
        _advertisementCache[args.BluetoothAddress] = args;

        ushort mfgId = 0;
        byte[]? mfgData = null;
        if (args.Advertisement.ManufacturerData.Count > 0)
        {
            var section = args.Advertisement.ManufacturerData[0];
            mfgId = section.CompanyId;
            var reader = DataReader.FromBuffer(section.Data);
            mfgData = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(mfgData);
        }

        AdvertisementReceived?.Invoke(this, new BleAdvertisement
        {
            // DeviceId is ulong.ToString() (decimal string). This MUST match what
            // WinRtGattConnection.ConnectAsync expects — it parses back via ulong.Parse().
            DeviceId = args.BluetoothAddress.ToString(),
            Name = args.Advertisement.LocalName ?? "",
            Rssi = args.RawSignalStrengthInDBm,
            ManufacturerId = mfgId,
            ManufacturerData = mfgData
        });
    }

    public IBleGattConnection CreateConnection()
    {
        // Pass the cache so WinRtGattConnection can resolve BluetoothAddress from DeviceId.
        return new WinRtGattConnection(_advertisementCache);
    }
}
