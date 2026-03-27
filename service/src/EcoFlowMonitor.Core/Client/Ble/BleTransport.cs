using EcoFlowMonitor.Logging;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.Protocol;

namespace EcoFlowMonitor.Client.Ble;

public class BleTransport : IDisposable
{
    private static readonly Guid RfcommServiceUuid = new("00000001-0000-1000-8000-00805f9b34fb");
    private static readonly Guid RfcommNotifyUuid = new("00000003-0000-1000-8000-00805f9b34fb");
    private static readonly Guid RfcommWriteUuid = new("00000002-0000-1000-8000-00805f9b34fb");
    private static readonly Guid NordicServiceUuid = new("6e400001-b5a3-f393-e0a9-e50e24dcca9e");
    private static readonly Guid NordicNotifyUuid = new("6e400003-b5a3-f393-e0a9-e50e24dcca9e");
    private static readonly Guid NordicWriteUuid = new("6e400002-b5a3-f393-e0a9-e50e24dcca9e");

    private readonly BleDeviceInfo _deviceInfo;
    private readonly IBleCryptoSession? _crypto;
    private readonly IBleAdapter _adapter;

    private IBleGattConnection? _connection;
    private Guid _activeServiceUuid;
    private Guid _activeWriteUuid;

    private readonly MemoryStream _buffer = new();
    private readonly object _bufferLock = new();

    public event EventHandler<byte[]>? RawFrameReceived;
    public event EventHandler<BlePacket>? PacketReceived;
    public bool IsConnected => _connection?.IsConnected ?? false;

    public BleTransport(BleDeviceInfo deviceInfo, IBleAdapter adapter, IBleCryptoSession? crypto = null)
    {
        _deviceInfo = deviceInfo;
        _adapter = adapter;
        _crypto = crypto;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        Logger.Log($"BleTransport: connecting to {_deviceInfo.Name} ({_deviceInfo.Address})...");

        _connection = _adapter.CreateConnection();
        _connection.NotificationReceived += OnNotification;
        await _connection.ConnectAsync(_deviceInfo.Address, ct);

        try
        {
            await _connection.SubscribeNotifyAsync(RfcommServiceUuid, RfcommNotifyUuid, ct);
            _activeServiceUuid = RfcommServiceUuid;
            _activeWriteUuid = RfcommWriteUuid;
            Logger.Log("BleTransport: using RFCOMM characteristics");
        }
        catch
        {
            await _connection.SubscribeNotifyAsync(NordicServiceUuid, NordicNotifyUuid, ct);
            _activeServiceUuid = NordicServiceUuid;
            _activeWriteUuid = NordicWriteUuid;
            Logger.Log("BleTransport: using Nordic UART characteristics");
        }

        Logger.Log($"BleTransport: connected to {_deviceInfo.Name}");
    }

    private void OnNotification(object? sender, byte[] data)
    {
        if (data.Length == 0) return;
        try
        {
            lock (_bufferLock)
            {
                _buffer.Write(data, 0, data.Length);
                ProcessBuffer();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"BleTransport: notification error — {ex.Message}");
        }
    }

    private void ProcessBuffer()
    {
        var bufData = _buffer.ToArray();
        int offset = 0;

        while (offset < bufData.Length)
        {
            var remaining = bufData.AsSpan(offset);
            var result = BlePacketParser.TryParseFrame(remaining);
            if (result == null) break;

            var (frameType, encPayload, consumed) = result.Value;
            offset += consumed;

            byte[] decrypted;
            if (_crypto != null && frameType != 0x00)
            {
                try { decrypted = _crypto.Decrypt(encPayload); }
                catch (Exception ex)
                {
                    Logger.Log($"BleTransport: decrypt failed — {ex.Message}");
                    continue;
                }
            }
            else
            {
                decrypted = encPayload;
            }

            RawFrameReceived?.Invoke(this, decrypted);

            var packet = BlePacketParser.ParsePacket(decrypted);
            if (packet != null)
                PacketReceived?.Invoke(this, packet);
        }

        if (offset > 0)
        {
            var remaining = bufData.AsSpan(offset).ToArray();
            _buffer.SetLength(0);
            if (remaining.Length > 0)
                _buffer.Write(remaining, 0, remaining.Length);
        }
    }

    public async Task SendAsync(byte[] data, CancellationToken ct = default)
    {
        if (_connection == null) throw new InvalidOperationException("Not connected");
        await _connection.WriteAsync(_activeServiceUuid, _activeWriteUuid, data, ct);
    }

    public async Task DisconnectAsync()
    {
        if (_connection != null)
        {
            _connection.NotificationReceived -= OnNotification;
            await _connection.DisconnectAsync();
        }
        Logger.Log("BleTransport: disconnected");
    }

    public void Dispose()
    {
        if (_connection != null)
            _connection.NotificationReceived -= OnNotification;
        (_connection as IDisposable)?.Dispose();
        _buffer.Dispose();
    }
}
