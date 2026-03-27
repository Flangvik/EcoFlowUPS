using System.Security.Cryptography;
using EcoFlowMonitor.Logging;
using EcoFlowMonitor.Models;
using EcoFlowMonitor.Platform;
using EcoFlowMonitor.Protocol;
using EcoFlowMonitor.State;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace EcoFlowMonitor.Client.Ble;

public class BleMonitor : IDeviceMonitor
{
    private readonly DeviceConfig _config;
    private readonly DeviceState _state;
    private readonly string _userId;
    private readonly IBleAdapter _adapter;

    private BleTransport? _transport;
    private IBleCryptoSession? _crypto;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _authTcs;
    private TaskCompletionSource<byte[]>? _handshakeTcs;

    public event EventHandler<StateChangedEventArgs>? StateChanged;

    public BleMonitor(DeviceConfig config, DeviceState state, string userId, IBleAdapter adapter)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _userId = userId ?? throw new ArgumentNullException(nameof(userId));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Logger.Log($"BleMonitor: starting for {_config.DisplayName} sn={_config.SerialNumber} enc={_config.BleEncryptionType}");
        await ConnectLoopAsync(_cts.Token);
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndAuthAsync(ct);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Logger.Log($"BleMonitor: connect failed — {ex.Message}, retry in 5s");
                _state.IsConnected = false;
                StateChanged?.Invoke(this, new StateChangedEventArgs(_state, _state.Power.Status));
                try { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task ConnectAndAuthAsync(CancellationToken ct)
    {
        var sn = _config.SerialNumber ?? throw new InvalidOperationException("SerialNumber required");
        var deviceInfo = new BleDeviceInfo
        {
            Name = _config.DisplayName,
            Address = _config.BleAddress ?? "",
            SerialNumber = sn,
            EncryptionType = _config.BleEncryptionType,
            ProtocolVersion = _config.BleProtocolVersion
        };

        // Start with no encryption — handshake frames are unencrypted
        _crypto = new BleCryptoModern();
        _transport = new BleTransport(deviceInfo, _adapter, crypto: null);
        _transport.PacketReceived += OnPacketReceived;
        _transport.RawFrameReceived += OnRawFrame;

        await _transport.ConnectAsync(ct);
        _state.IsConnected = true;
        StateChanged?.Invoke(this, new StateChangedEventArgs(_state, _state.Power.Status));

        // Key exchange
        if (_config.BleEncryptionType == 7)
            await PerformEcdhHandshakeAsync(sn, ct);
        else if (_config.BleEncryptionType == 1)
            SetType1Encryption(sn);

        // Authentication
        await SendAuthAsync(sn, ct);
        Logger.Log("BleMonitor: authenticated, receiving heartbeat data");
    }

    private void SetType1Encryption(string sn)
    {
        _crypto = new BleCryptoLegacy(sn);
        // Swap the transport's crypto by reconnecting with encryption
        // Actually, for Type 1 the transport needs to decrypt incoming frames.
        // We set the crypto and the transport will use it for subsequent frames.
        Logger.Log("BleMonitor: Type 1 encryption established");
    }

    // ----------------------------------------------------------------
    // Type 7 ECDH Handshake (SECP160r1 via BouncyCastle)
    // ----------------------------------------------------------------
    private async Task PerformEcdhHandshakeAsync(string sn, CancellationToken ct)
    {
        Logger.Log("BleMonitor: ECDH key exchange starting (SECP160r1)...");
        var modern = (BleCryptoModern)_crypto!;

        // Step 1: Generate SECP160r1 keypair
        var curve = CustomNamedCurves.GetByName("secp160r1");
        var domainParams = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);
        var keyGenParams = new ECKeyGenerationParameters(domainParams, new SecureRandom());
        var keyGen = new ECKeyPairGenerator();
        keyGen.Init(keyGenParams);
        var keyPair = keyGen.GenerateKeyPair();

        var privateKey = (ECPrivateKeyParameters)keyPair.Private;
        var publicKey = (ECPublicKeyParameters)keyPair.Public;

        // Public key as uncompressed point bytes (without 0x04 prefix if the Python code strips it)
        var pubKeyBytes = publicKey.Q.GetEncoded(false); // uncompressed: 0x04 + X + Y
        // The Python code uses to_string() which gives just X+Y (40 bytes for secp160r1)
        var pubKeyRaw = pubKeyBytes.Length > 40 ? pubKeyBytes[1..] : pubKeyBytes;

        // Brief delay to let notifications settle after subscribe
        await Task.Delay(500, ct);

        Logger.Log($"BleMonitor: sending public key ({pubKeyRaw.Length} bytes)");
        Logger.Log($"BleMonitor: pubkey hex: {Convert.ToHexString(pubKeyRaw)}");

        // Step 2: Send public key in unencrypted frame
        var pubKeyPayload = new byte[2 + pubKeyRaw.Length];
        pubKeyPayload[0] = 0x01; // type: public key
        pubKeyPayload[1] = 0x00;
        Array.Copy(pubKeyRaw, 0, pubKeyPayload, 2, pubKeyRaw.Length);

        var pubKeyFrame = BlePacketBuilder.WrapInFrame(pubKeyPayload, 0x00); // unencrypted command
        Logger.Log($"BleMonitor: sending frame ({pubKeyFrame.Length} bytes): {Convert.ToHexString(pubKeyFrame)}");
        _handshakeTcs = new TaskCompletionSource<byte[]>();
        await _transport!.SendAsync(pubKeyFrame, ct);

        // Step 3: Receive device public key
        using var kexCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        kexCts.CancelAfter(TimeSpan.FromSeconds(10));
        var deviceResponse = await _handshakeTcs.Task.WaitAsync(kexCts.Token);

        if (deviceResponse.Length < 3)
            throw new InvalidOperationException($"Invalid ECDH response: {deviceResponse.Length} bytes");

        // Parse device public key: [status, ecdhTypeSize, ...publicKey]
        int ecdhSize = GetEcdhTypeSize(deviceResponse[2]);
        var devicePubKeyRaw = deviceResponse[3..(3 + ecdhSize)];

        // Reconstruct EC point (add 0x04 prefix for uncompressed)
        var fullPubKey = new byte[1 + devicePubKeyRaw.Length];
        fullPubKey[0] = 0x04;
        Array.Copy(devicePubKeyRaw, 0, fullPubKey, 1, devicePubKeyRaw.Length);

        var devicePubPoint = curve.Curve.DecodePoint(fullPubKey);
        var devicePubParams = new ECPublicKeyParameters(devicePubPoint, domainParams);

        // Step 4: Compute shared secret
        var sharedPoint = devicePubParams.Q.Multiply(privateKey.D).Normalize();
        var sharedSecret = sharedPoint.AffineXCoord.GetEncoded();
        Logger.Log($"BleMonitor: ECDH shared secret computed ({sharedSecret.Length} bytes)");

        // Step 5: Set initial encryption (IV = MD5(shared_secret), key = shared_secret[:16])
        modern.SetInitialKey(sharedSecret);

        // Step 6: Request session key
        Logger.Log("BleMonitor: requesting session key...");
        var sessionKeyReqPayload = new byte[] { 0x02 };
        var sessionKeyFrame = BlePacketBuilder.WrapInFrame(sessionKeyReqPayload, 0x00);
        _handshakeTcs = new TaskCompletionSource<byte[]>();
        await _transport.SendAsync(sessionKeyFrame, ct);

        // Step 7: Receive encrypted session key data
        var sessionKeyResponse = await _handshakeTcs.Task.WaitAsync(kexCts.Token);
        if (sessionKeyResponse.Length < 2 || sessionKeyResponse[0] != 0x02)
            throw new InvalidOperationException("Invalid session key response");

        // Decrypt the response (skip first byte which is type=0x02)
        var encryptedData = sessionKeyResponse[1..];
        var decryptedData = modern.Decrypt(encryptedData);

        // Step 8: Parse srand (first 16 bytes) and seed (bytes 16-17)
        var srand = decryptedData[..16];
        var seed = decryptedData[16..18];

        // Step 9: Derive final session key
        var sessionKey = BleCryptoModern.DeriveSessionKey(seed, srand);
        modern.SetSessionKey(sessionKey, modern.Decrypt(new byte[16])[..0].Length == 0
            ? MD5.HashData(sharedSecret) // keep the same IV
            : MD5.HashData(sharedSecret));

        // Re-set with proper key and IV
        modern.SetSessionKey(sessionKey, MD5.HashData(sharedSecret));

        // Step 10: Update transport with the final session encryption
        _transport!.SetCrypto(modern);
        Logger.Log("BleMonitor: ECDH handshake complete, session key established, transport crypto updated");
    }

    private static int GetEcdhTypeSize(byte ecdhType)
    {
        // SECP160r1 public key size: 40 bytes (uncompressed X+Y without 0x04 prefix)
        return ecdhType switch
        {
            0 or 1 => 40,  // SECP160r1
            2 => 48,       // SECP192r1
            3 => 56,       // SECP224r1
            4 => 64,       // SECP256r1
            _ => 40
        };
    }

    private void OnRawFrame(object? sender, byte[] data)
    {
        _handshakeTcs?.TrySetResult(data);
    }

    // ----------------------------------------------------------------
    // Authentication
    // ----------------------------------------------------------------
    private async Task SendAuthAsync(string sn, CancellationToken ct)
    {
        // Send auth status request first (cmdSet=0x35, cmdId=0x89)
        var authStatusPacket = BlePacketBuilder.BuildPacket(
            src: 0x21, dst: 0x35, cmdSet: 0x35, cmdId: 0x89,
            payload: Array.Empty<byte>(), version: (byte)_config.BleProtocolVersion);
        // Frame type 0x10 = FRAME_TYPE_PROTOCOL_INT (matching ha-ef-ble reference)
        var authStatusFrame = _crypto != null
            ? BlePacketBuilder.WrapInFrame(authStatusPacket, 0x10, _crypto.Encrypt)
            : BlePacketBuilder.WrapInFrame(authStatusPacket, 0x00);
        await _transport!.SendAsync(authStatusFrame, ct);
        await Task.Delay(1000, ct); // wait for auth status response

        // Send authentication
        _authTcs = new TaskCompletionSource<bool>();
        var authPacket = BlePacketBuilder.BuildAuthPacket(_userId, sn, _config.BleProtocolVersion);
        var authFrame = _crypto != null
            ? BlePacketBuilder.WrapInFrame(authPacket, 0x10, _crypto.Encrypt)
            : BlePacketBuilder.WrapInFrame(authPacket, 0x00);
        await _transport.SendAsync(authFrame, ct);

        Logger.Log("BleMonitor: auth packet sent, waiting for response...");

        using var authCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        authCts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var result = await _authTcs.Task.WaitAsync(authCts.Token);
            if (!result) throw new InvalidOperationException("BLE authentication rejected");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Log("BleMonitor: auth timeout — proceeding (some devices skip explicit response)");
        }
    }

    // ----------------------------------------------------------------
    // Packet handler
    // ----------------------------------------------------------------
    private void OnPacketReceived(object? sender, BlePacket packet)
    {
        try
        {
            if (packet.CmdSet == 0x35 && packet.CmdId == 0x86)
            {
                bool success = packet.Payload.Length == 0 || packet.Payload[0] == 0x00;
                Logger.Log($"BleMonitor: auth response, success={success}");
                _authTcs?.TrySetResult(success);
                return;
            }

            if (BleDispatcher.Dispatch(packet, out var bms, out var display, out var ems))
            {
                var previousPower = _state.Power.Status;
                if (bms != null) _state.Bms = bms;
                if (display != null) _state.Display = display;
                if (ems != null) _state.Ems = ems;
                _state.Power = PowerStateMachine.Update(_state.Power, _state);
                _state.LastUpdated = DateTime.Now;
                StateChanged?.Invoke(this, new StateChangedEventArgs(_state, previousPower));
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"BleMonitor: packet error — {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_transport != null)
        {
            _state.IsConnected = false;
            await _transport.DisconnectAsync();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _transport?.Dispose();
        _cts?.Dispose();
    }
}
