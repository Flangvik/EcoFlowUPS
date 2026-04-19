using System.Text;
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
        System.Diagnostics.Debug.WriteLine("BleScanner: starting scan...");

        try
        {
            await _adapter.StartScanAsync(_cts.Token);
            // Keep alive until cancelled
            try { await Task.Delay(Timeout.Infinite, _cts.Token); }
            catch (OperationCanceledException) { }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BleScanner: scan error — {ex.Message}");
        }
    }

    private void OnAdvertisementReceived(object? sender, BleAdvertisement e)
    {
        try
        {
            var name = e.Name;
            bool nameMatch = name.StartsWith("EF-", StringComparison.OrdinalIgnoreCase)
                          || name.StartsWith("Ecoflow", StringComparison.OrdinalIgnoreCase);
            bool mfgMatch = e.ManufacturerId == EcoFlowManufacturerId;

            if (!nameMatch && !mfgMatch) return;

            var data = e.ManufacturerData;
            string serialNumber;
            int protoVersion = 3;
            int encryptionType = 7;

            if (data != null && data.Length >= 18)
            {
                protoVersion = data[0];
                serialNumber = Encoding.ASCII.GetString(data, 1, 16).TrimEnd('\0');
                encryptionType = data.Length > 22 ? (data[22] >> 3) & 0x07 : 7;
            }
            else
            {
                // No manufacturer data — use device address as identifier
                serialNumber = e.DeviceId;
            }

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

            System.Diagnostics.Debug.WriteLine($"BleScanner: found {name} sn={serialNumber} enc={encryptionType} rssi={e.Rssi}");
            DeviceDiscovered?.Invoke(this, info);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BleScanner: parse error — {ex.Message}");
        }
    }

    public void StopScan()
    {
        _cts?.Cancel();
        _adapter.StopScan();
        System.Diagnostics.Debug.WriteLine("BleScanner: scan stopped");
    }

    public void Dispose()
    {
        StopScan();
        _adapter.AdvertisementReceived -= OnAdvertisementReceived;
        _cts?.Dispose();
    }
}
