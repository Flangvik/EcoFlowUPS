using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace EcoFlowMonitor.Protocol;

public static class BlePacketBuilder
{
    /// <summary>
    /// Build an authentication packet.
    /// Payload: MD5(userId + serialNumber) as uppercase hex ASCII.
    /// </summary>
    public static byte[] BuildAuthPacket(string userId, string serialNumber, int protocolVersion = 3)
    {
        // MD5(userId + serialNumber) -> uppercase hex string -> UTF8 bytes
        var input = Encoding.UTF8.GetBytes(userId + serialNumber);
        var hash = MD5.HashData(input);
        var hexPayload = Encoding.UTF8.GetBytes(Convert.ToHexString(hash).ToLowerInvariant());

        return BuildPacket(
            src: 0x21,
            dst: 0x35,
            cmdSet: 0x35,
            cmdId: 0x86,
            payload: hexPayload,
            version: (byte)protocolVersion
        );
    }

    /// <summary>
    /// Build a raw 0xAA application packet with CRC.
    /// </summary>
    public static byte[] BuildPacket(byte src, byte dst, byte cmdSet, byte cmdId,
        byte[] payload, byte dsrc = 1, byte ddst = 1, byte version = 3, byte[]? seq = null)
    {
        seq ??= new byte[4];

        using var ms = new MemoryStream();

        // Header: prefix + version + payload length
        ms.WriteByte(0xAA);
        ms.WriteByte(version);
        var lenBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(lenBytes, (ushort)payload.Length);
        ms.Write(lenBytes);

        // Header CRC8
        var header = ms.ToArray();
        ms.WriteByte(Crc.ComputeCrc8(header));

        // Product byte + sequence + static zeros
        ms.WriteByte(0x0D);
        ms.Write(seq);
        ms.Write(new byte[] { 0x00, 0x00 });

        // Addresses
        ms.WriteByte(src);
        ms.WriteByte(dst);

        if (version >= 3)
        {
            ms.WriteByte(dsrc);
            ms.WriteByte(ddst);
        }

        ms.WriteByte(cmdSet);
        ms.WriteByte(cmdId);

        // Payload
        ms.Write(payload);

        // CRC16 over everything
        var packetData = ms.ToArray();
        var crc16Bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(crc16Bytes, Crc.ComputeCrc16(packetData));
        ms.Write(crc16Bytes);

        return ms.ToArray();
    }

    /// <summary>
    /// Wrap an application packet in a 0x5A5A wire frame.
    /// frameType: 0x00 = unencrypted command, 0x01 = encrypted protocol
    /// </summary>
    public static byte[] WrapInFrame(byte[] packetData, byte frameType, Func<byte[], byte[]>? encrypt = null)
    {
        var payload = encrypt != null ? encrypt(packetData) : packetData;

        using var ms = new MemoryStream();
        ms.WriteByte(0x5A);
        ms.WriteByte(0x5A);
        ms.WriteByte((byte)(frameType << 4));
        ms.WriteByte(0x01);

        // Length field includes payload + 2 bytes for trailing CRC16
        var lenBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(lenBytes, (ushort)(payload.Length + 2));
        ms.Write(lenBytes);
        ms.Write(payload);

        var frameData = ms.ToArray();
        var crc16Bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(crc16Bytes, Crc.ComputeCrc16(frameData));
        ms.Write(crc16Bytes);

        return ms.ToArray();
    }
}
