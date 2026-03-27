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
        // Key must be 16 bytes for AES-128 or padded to 32 for AES-256
        if (key.Length == 16)
        {
            _key = new byte[32];
            Array.Copy(key, 0, _key, 0, 16);
            Array.Copy(key, 0, _key, 16, 16);
        }
        else
        {
            _key = key;
        }
        _iv = iv;
        _initialized = true;
    }

    /// <summary>
    /// Set initial encryption from the ECDH shared secret (before session key derivation).
    /// </summary>
    public void SetInitialKey(byte[] sharedSecret)
    {
        // Key: first 16 bytes of shared secret, doubled to 32
        _key = new byte[32];
        Array.Copy(sharedSecret, 0, _key, 0, Math.Min(16, sharedSecret.Length));
        Array.Copy(sharedSecret, 0, _key, 16, Math.Min(16, sharedSecret.Length));
        // IV: MD5 of full shared secret
        _iv = MD5.HashData(sharedSecret);
        _initialized = true;
    }

    public byte[] Encrypt(byte[] plaintext)
    {
        if (!_initialized) throw new InvalidOperationException("ECDH handshake not complete");
        // PKCS7 padding
        int paddedLen = (plaintext.Length + 15) / 16 * 16;
        if (paddedLen == plaintext.Length) paddedLen += 16; // PKCS7 always adds padding
        var padded = new byte[paddedLen];
        Array.Copy(plaintext, padded, plaintext.Length);
        byte padByte = (byte)(paddedLen - plaintext.Length);
        for (int i = plaintext.Length; i < paddedLen; i++)
            padded[i] = padByte;
        return BleCryptoLegacy.EncryptCbc(_key, _iv, padded);
    }

    public byte[] Decrypt(byte[] ciphertext)
    {
        if (!_initialized) throw new InvalidOperationException("ECDH handshake not complete");
        var decrypted = BleCryptoLegacy.DecryptCbc(_key, _iv, ciphertext);
        // Remove PKCS7 padding
        if (decrypted.Length > 0)
        {
            byte padLen = decrypted[^1];
            if (padLen > 0 && padLen <= 16 && decrypted.Length >= padLen)
            {
                bool validPad = true;
                for (int i = decrypted.Length - padLen; i < decrypted.Length; i++)
                    if (decrypted[i] != padLen) { validPad = false; break; }
                if (validPad)
                    return decrypted[..^padLen];
            }
        }
        return decrypted;
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
/// This is a fixed lookup table extracted from the EcoFlow BLE protocol.
/// </summary>
internal static class BleKeyData
{
    private static readonly byte[] _data;

    static BleKeyData()
    {
        _data = Convert.FromBase64String(
            "0YJKCksI0pageNcGpGEn8S0fXpRXO3sHSJsLZI6p1Lg+uTfXMFXlvPDpkp419O5K/RFgTrYM8PZDEhnKy+vsbYqPb9FdNVNi" +
            "hmQKawepROIEI8F+7uGkVvzY9VaYjzbwXk15OAob0dkDDsB6S5BlmVGIhhre9x8x+fBKUlY2WaZSUt1AdA2qd2Gi2h9Fu7urQa" +
            "tNMjqDelInqnyoli0IREZ/sgfM4p+xLGFjXdJp4kNgw6axmKltqheKcBDm+qLo/4L4kpmy/3oEOteP9b27+WoP/c7jJ80wD/uN" +
            "u9CqbJpeqyobJs99ae30OuNioQrnyJ1AiF7uY1SbK+lTJ4WkUjYDFvUimzGdY3hP9ipu4mdGldyvMPDO+tRDGhjPkJNho4ugMD" +
            "OlU7YEnh0j2nq1imSJ2e+cvJyYRUoEmS5kp1IGH0ez6LxOY+RPfFJyobDzwkULqIWndI2Pa2nnEQnOaCGgYYA80Ut2vlDV7IQ8" +
            "jxmbZy4cVCkBW0jh4Th7zDQJWn3XtZy7hcmfDw1wERJJofNQIWMxeYsn0tp9q65boGxlmNAiTaDV/0sE9MvCjhNVaLiySvitz" +
            "U31/+DGwElUFkE81Sav6Ihp27EGnasoFCN0AnYEe7M6N44SO8y336N5pHm7ctDAMusoPw6wTly5qgBuxWvwOnwbJRRhKEHvoICk" +
            "uYPXUIE4CoKHFSZBqAEyHXVt0uot+6Zgd4JYbUnzuJeCZmmvoKZL2QCMqBriZXU80ViH8MgyoUOCGhtTfaTK5ZoDXSNkxPujV5" +
            "647GBUEUKzYcG+d0Wn/CveaDkCZdcq5JayGJOSjzgHFH6LBQLixTLzhMqeYdA3bgaCvuGhRG7RDQUjSDwjSe9ZjM+syD8Xiu8" +
            "xjE4yPuX+uRfNeynG+9imJ7uDvB8v8oFhCQrWGts+fE4KSKS4ww3VWngnnLOOmx3UyjitcMMJUGiSlyctw5JsFhgBw8OE5Qpc" +
            "9GzUsy6EjGAz18vkLRBpWzsJtvDAPdv7SHu9CeS0UWfcFGmSXHdWMv7RjqxI1EJ/WKhtBPMKxDCDZs0Gb9szSwUZgT+MNlZvW" +
            "nRNKrIFjXoJewprOTtPX+9k1y35GYQQGdPWxXxkkYUSQiIj4W2edJNlspWniBnXfMi3BHhdyDQWVVjF3zk05WmQ9CZq1hyn8F" +
            "SrBdlEU2dgvZ2bcacgG86z+jQcvmJkc2mBFP2ZkmoM5qODrcXk17tQWMygItSDdfbfgwy5db7TR4ypDu0Lgn7rGj0xRCKuFoor" +
            "JJ9RDhb8D6uKviXoSJM2EqsvKiPy6W2DAugWjvmH873eqd4vUrxTNjj4EV0uQ1xuKcf0OqMkV3CsmHIV/v6Pws7CHey1db8uh" +
            "zlFWjomCBH5IyRh/QgCc840SJE5KlBAQjXLmtpggIeQP0bq4XVq7ns1OoILNpV3KQiXt+U0rD3g3WYrfazMnh0Ud4MezltWJQ" +
            "g8I2FykheZt/xMc2iqfmB3CPbwsFgZPQQOzd+CkzQA08PaakFyV0gsKI1e/AMLDPMKsriv1yrZog4cD/OgBDLTvSmM/SncH/eE" +
            "LwjKQM5GMGOO0X5V/kX8vLFBNPKeaVwqrMhNz/HOecyIFDXRr1hb5k/jA2dwgkFZ8jpXZWGbgQqS/M9/ZwBu+va4B8tWE2wpQ" +
            "gJea5qJiTrSThjpJFmdOUgXpWaoJG2oMXtWPQntXyKLJCJ35pKRasGrHlc+XnChAN2IIV6AjVgUMZHdiuxpWzRVUcvMSpjFgrH" +
            "XGL23HoYY81f+H/1b8UyMQhYR2ovXpgXYuP0x/nhRs3XNhtAWuT/Swd/sLcQfOTCtuRWqL9s/YYMlb8K/vUEajInoAxEtH7P0" +
            "2vU/65fBKNgiD36Id9WNrdtYzY990");
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
