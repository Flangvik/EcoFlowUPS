using System.Text;
using EcoFlowMonitor.Logging;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;

namespace EcoFlowMonitor.Client.Ble;

public class BleScanner : IDisposable
{
    private const ushort EcoFlowManufacturerId = 46517;

    private readonly IBleAdapter _adapter;
    private readonly HashSet<string> _seen = new();
    private CancellationTokenSource? _cts;

    public event EventHandler<BleDeviceInfo>? DeviceDiscovered;

    public BleScanner(IBleAdapter adapter)
    {
        _adapter = adapter;
        _adapter.AdvertisementReceived += OnAdvertisementReceived;
    }

    public async Task StartScanAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _seen.Clear();
        Logger.Log("BleScanner: starting scan...");

        try
        {
            await _adapter.StartScanAsync(_cts.Token);
            // Keep alive until cancelled
            try { await Task.Delay(Timeout.Infinite, _cts.Token); }
            catch (OperationCanceledException) { }
        }
        catch (Exception ex)
        {
            Logger.Log($"BleScanner: scan error — {ex.Message}");
        }
    }

    private void OnAdvertisementReceived(object? sender, BleAdvertisement e)
    {
        try
        {
            var name = e.Name;
            if (!name.StartsWith("EF-", StringComparison.OrdinalIgnoreCase)) return;
            if (e.ManufacturerId != EcoFlowManufacturerId) return;

            var data = e.ManufacturerData;
            if (data == null || data.Length < 18) return;

            int protoVersion = data[0];
            string serialNumber = Encoding.ASCII.GetString(data, 1, 16).TrimEnd('\0');
            int encryptionType = data.Length > 22 ? (data[22] >> 3) & 0x07 : 1;

            if (string.IsNullOrEmpty(serialNumber)) return;
            if (!_seen.Add(serialNumber)) return;

            var info = new BleDeviceInfo
            {
                Name = name,
                Address = e.DeviceId,
                SerialNumber = serialNumber,
                ProtocolVersion = protoVersion,
                EncryptionType = encryptionType,
                Rssi = e.Rssi
            };

            Logger.Log($"BleScanner: found {name} sn={serialNumber} enc={encryptionType} rssi={e.Rssi}");
            DeviceDiscovered?.Invoke(this, info);
        }
        catch (Exception ex)
        {
            Logger.Log($"BleScanner: parse error — {ex.Message}");
        }
    }

    public void StopScan()
    {
        _cts?.Cancel();
        _adapter.StopScan();
        Logger.Log("BleScanner: scan stopped");
    }

    public void Dispose()
    {
        StopScan();
        _adapter.AdvertisementReceived -= OnAdvertisementReceived;
        _cts?.Dispose();
    }
}
