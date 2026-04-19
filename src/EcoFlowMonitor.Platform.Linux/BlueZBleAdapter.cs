using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Platform.Linux;

public class BlueZBleAdapter : IBleAdapter
{
    // BlueZManager.GetAdapterAsync returns the concrete Adapter type which has DeviceFound.
    private Adapter? _adapter;
    private readonly Dictionary<string, Device> _deviceCache = new();
    private DeviceChangeEventHandlerAsync? _deviceFoundHandler;

    public event EventHandler<BleAdvertisement>? AdvertisementReceived;

    public async Task StartScanAsync(CancellationToken ct = default)
    {
        // Preflight check: MUST be first, before any BlueZ/D-Bus call.
        BlueZPermissionCheck.EnsureBluetoothGroupMembership();

        // Clear advertisement cache on every scan to avoid stale results.
        _deviceCache.Clear();

        _adapter = await BlueZManager.GetAdapterAsync("hci0")
            ?? (await BlueZManager.GetAdaptersAsync()).FirstOrDefault()
            ?? throw new InvalidOperationException("No Bluetooth adapter found. Ensure bluetooth service is running.");

        _deviceFoundHandler = async (Adapter a, DeviceFoundEventArgs e) =>
        {
            try
            {
                var props = await e.Device.GetAllAsync();
                var address = props.Address;
                if (string.IsNullOrEmpty(address)) return;

                _deviceCache[address] = e.Device;

                ushort mfgId = 0;
                byte[]? mfgData = null;
                // ManufacturerData is IDictionary<ushort, object> in Linux.Bluetooth 5.67.1
                if (props.ManufacturerData != null && props.ManufacturerData.Count > 0)
                {
                    var first = props.ManufacturerData.First();
                    mfgId = first.Key;
                    mfgData = first.Value as byte[];
                }

                AdvertisementReceived?.Invoke(this, new BleAdvertisement
                {
                    DeviceId = address,          // "AA:BB:CC:DD:EE:FF" — matches ConnectAsync expectation
                    Name = props.Name ?? "",
                    Rssi = props.RSSI,
                    ManufacturerId = mfgId,
                    ManufacturerData = mfgData
                });
            }
            catch { /* swallow per-device parse errors to keep scan running */ }
        };

        _adapter.DeviceFound += _deviceFoundHandler;

        await _adapter.StartDiscoveryAsync();

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await StopScanAsync();
        }
    }

    public void StopScan() => _ = StopScanAsync();

    private async Task StopScanAsync()
    {
        if (_adapter == null) return;
        if (_deviceFoundHandler != null)
        {
            _adapter.DeviceFound -= _deviceFoundHandler;
            _deviceFoundHandler = null;
        }
        try { await _adapter.StopDiscoveryAsync(); } catch { /* ignore if not scanning */ }
    }

    public IBleGattConnection CreateConnection() => new BlueZGattConnection(_deviceCache);
}
