using System.Security.Cryptography;
using System.Text;

namespace EcoFlowMonitor.Protocol;

public interface IBleCryptoSession
{
    byte[] Encrypt(byte[] plaintext);
    byte[] Decrypt(byte[] ciphertext);
    /// <summary>True if this session requires a multi-step key exchange handshake.</summary>
    bool RequiresHandshake => false;
}

/// <summary>
/// Type 1 legacy encryption: AES-256-CBC with MD5-derived keys.
/// Key = MD5(serial) doubled to 32 bytes, IV = MD5(reverse(serial)), zero-padded.
/// </summary>
public class BleCryptoLegacy : IBleCryptoSession
{
    private readonly byte[] _key;
    private readonly byte[] _iv;

    public BleCryptoLegacy(string serialNumber)
    {
        var snBytes = Encoding.UTF8.GetBytes(serialNumber);
        var md5Key = MD5.HashData(snBytes);
        _key = new byte[32];
        Array.Copy(md5Key, 0, _key, 0, 16);
        Array.Copy(md5Key, 0, _key, 16, 16);

        var reversedSn = new string(serialNumber.Reverse().ToArray());
        _iv = MD5.HashData(Encoding.UTF8.GetBytes(reversedSn));
    }

    public byte[] Encrypt(byte[] plaintext)
    {
        int paddedLen = (plaintext.Length + 15) / 16 * 16;
        var padded = new byte[paddedLen];
        Array.Copy(plaintext, padded, plaintext.Length);
        return EncryptCbc(_key, _iv, padded);
    }

    public byte[] Decrypt(byte[] ciphertext)
    {
        return DecryptCbc(_key, _iv, ciphertext);
    }

    internal static byte[] EncryptCbc(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(data, iv, PaddingMode.None);
    }

    internal static byte[] DecryptCbc(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(data, iv, PaddingMode.None);
    }
}

/// <summary>
/// Type 7 encryption: ECDH (SECP160r1) key exchange → session key via keydata → AES-CBC with PKCS7.
/// This is a multi-step protocol:
///   1. Generate SECP160r1 keypair, send public key to device
///   2. Receive device public key, compute shared secret
///   3. IV = MD5(shared_secret), initial encryption key = shared_secret[:16]
///   4. Send session key request, receive encrypted seed+srand
///   5. Derive final session key via keydata lookup table + MD5
///   6. Final encryption uses AES-CBC(session_key, iv) with PKCS7 padding
/// </summary>
public class BleCryptoModern : IBleCryptoSession
{
    private byte[] _key = Array.Empty<byte>();
    private byte[] _iv = Array.Empty<byte>();
    private bool _initialized;

    public bool RequiresHandshake => true;

    /// <summary>
    /// Set the encryption key and IV after the ECDH handshake completes.
    /// Called by BleMonitor after the key exchange protocol finishes.
    /// </summary>
    public void SetSessionKey(byte[] key, byte[] iv)
    {
        _key = key; // Use 16-byte key directly for AES-128
        _iv = iv;
        _initialized = true;
    }

    /// <summary>
    /// Set initial encryption from the ECDH shared secret (before session key derivation).
    /// </summary>
    public void SetInitialKey(byte[] sharedSecret)
    {
        // Key: first 16 bytes of shared secret (AES-128)
        _key = new byte[16];
        Array.Copy(sharedSecret, 0, _key, 0, Math.Min(16, sharedSecret.Length));
        // IV: MD5 of full shared secret
        _iv = MD5.HashData(sharedSecret);
        _initialized = true;
    }

    public byte[] Encrypt(byte[] plaintext)
    {
        if (!_initialized) throw new InvalidOperationException("ECDH handshake not complete");
        using var aes = Aes.Create();
        aes.Key = _key;
        return aes.EncryptCbc(plaintext, _iv, PaddingMode.PKCS7);
    }

    public byte[] Decrypt(byte[] ciphertext)
    {
        if (!_initialized) throw new InvalidOperationException("ECDH handshake not complete");
        using var aes = Aes.Create();
        aes.Key = _key;
        try
        {
            return aes.DecryptCbc(ciphertext, _iv, PaddingMode.PKCS7);
        }
        catch (CryptographicException)
        {
            // Some responses don't have proper PKCS7 padding
            return aes.DecryptCbc(ciphertext, _iv, PaddingMode.None);
        }
    }

    /// <summary>
    /// Derive the final session key from seed and srand using the keydata lookup table.
    /// seed = 2 bytes, srand = 16 bytes (from encrypted device response).
    /// Returns 16-byte MD5 hash of (keydata_8bytes[pos] + keydata_8bytes[pos+8] + srand[0:8] + srand[8:16]).
    /// </summary>
    public static byte[] DeriveSessionKey(byte[] seed, byte[] srand)
    {
        int pos = seed[0] * 0x10 + ((seed[1] - 1) & 0xFF) * 0x100;

        var data = new byte[32];
        BleKeyData.CopyBytes(pos, data, 0, 8);
        BleKeyData.CopyBytes(pos + 8, data, 8, 8);
        Array.Copy(srand, 0, data, 16, 8);
        Array.Copy(srand, 8, data, 24, 8);

        return MD5.HashData(data);
    }
}

/// <summary>
/// The embedded key data table used for Type 7 session key derivation.
/// This is a fixed 65,280-byte lookup table extracted from the EcoFlow BLE protocol.
/// Loaded from embedded resource keydata.b64.
/// </summary>
internal static class BleKeyData
{
    private static readonly byte[] _data;

    static BleKeyData()
    {
        var asm = typeof(BleKeyData).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("keydata.b64"))
            ?? throw new InvalidOperationException("keydata.b64 embedded resource not found");

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var b64 = reader.ReadToEnd().Trim();
        _data = Convert.FromBase64String(b64);
    }

    public static void CopyBytes(int pos, byte[] dest, int destOffset, int count)
    {
        if (pos < 0 || pos + count > _data.Length) return;
        Array.Copy(_data, pos, dest, destOffset, count);
    }

    public static byte[] Get8Bytes(int pos)
    {
        var result = new byte[8];
        CopyBytes(pos, result, 0, 8);
        return result;
    }
}
