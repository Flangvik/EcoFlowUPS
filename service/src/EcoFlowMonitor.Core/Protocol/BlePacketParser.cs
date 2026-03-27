using System.Buffers.Binary;
using EcoFlowMonitor.Logging;

namespace EcoFlowMonitor.Protocol;

public static class BlePacketParser
{
    // Wire frame prefix
    private const ushort FramePrefix = 0x5A5A;
    // Application packet prefix
    private const byte PacketPrefix = 0xAA;

    /// <summary>
    /// Try to extract a complete 0x5A5A frame from a buffer.
    /// Returns the frame type and encrypted payload, or null if incomplete.
    /// Also returns bytesConsumed so the caller can trim the buffer.
    /// </summary>
    public static (byte frameType, byte[] encryptedPayload, int bytesConsumed)? TryParseFrame(ReadOnlySpan<byte> buffer)
    {
        // Need at least 8 bytes: 2 prefix + 1 frameType + 1 unknown + 2 length + ... + 2 crc
        if (buffer.Length < 8) return null;

        // Find 0x5A5A prefix
        int start = -1;
        for (int i = 0; i <= buffer.Length - 2; i++)
        {
            if (buffer[i] == 0x5A && buffer[i + 1] == 0x5A)
            {
                start = i;
                break;
            }
        }
        if (start < 0) return null;

        var frame = buffer[start..];
        if (frame.Length < 8) return null;

        byte frameType = (byte)(frame[2] >> 4);
        // frame[3] is always 0x01
        ushort payloadLen = BinaryPrimitives.ReadUInt16LittleEndian(frame[4..6]);

        // Total frame size: 6 header + payloadLen + 2 crc
        int totalLen = 6 + payloadLen + 2;
        if (frame.Length < totalLen) return null; // incomplete

        // Validate CRC16 (everything except last 2 bytes)
        var frameData = frame[..totalLen];
        ushort expectedCrc = BinaryPrimitives.ReadUInt16LittleEndian(frameData[(totalLen - 2)..]);
        ushort actualCrc = Crc.ComputeCrc16(frameData[..(totalLen - 2)]);
        if (expectedCrc != actualCrc)
        {
            Logger.Log($"BlePacketParser: frame CRC16 mismatch expected=0x{expectedCrc:X4} actual=0x{actualCrc:X4}");
            return null;
        }

        // Extract encrypted payload (between header and CRC)
        var encPayload = frameData[6..(6 + payloadLen)].ToArray();
        return (frameType, encPayload, start + totalLen);
    }

    /// <summary>
    /// Parse a decrypted 0xAA application packet.
    /// </summary>
    public static BlePacket? ParsePacket(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[0] != PacketPrefix) return null;

        byte version = data[1];
        int minLen = version == 2 ? 18 : 20;
        if (data.Length < minLen) return null;

        ushort payloadLen = BinaryPrimitives.ReadUInt16LittleEndian(data[2..4]);

        // Validate header CRC8 (first 4 bytes)
        byte expectedCrc8 = data[4];
        byte actualCrc8 = Crc.ComputeCrc8(data[..4]);
        if (expectedCrc8 != actualCrc8)
        {
            Logger.Log($"BlePacketParser: header CRC8 mismatch expected=0x{expectedCrc8:X2} actual=0x{actualCrc8:X2}");
            return null;
        }

        // Validate packet CRC16 (for version 2, 3, 4)
        if (version is 2 or 3 or 4 && data.Length >= minLen + payloadLen)
        {
            int totalPacketLen = minLen + payloadLen;
            // Some packets include CRC16 at end
            if (data.Length >= totalPacketLen + 2)
            {
                ushort expectedCrc16 = BinaryPrimitives.ReadUInt16LittleEndian(data[totalPacketLen..(totalPacketLen + 2)]);
                ushort actualCrc16 = Crc.ComputeCrc16(data[..totalPacketLen]);
                if (expectedCrc16 != actualCrc16)
                {
                    Logger.Log($"BlePacketParser: packet CRC16 mismatch");
                    return null;
                }
            }
        }

        byte productByte = data[5];
        var seq = data[6..10].ToArray();
        // data[10..12] = static zeros
        byte src = data[12];
        byte dst = data[13];

        byte dsrc = 0, ddst = 0;
        int payloadStart;
        byte cmdSet, cmdId;

        if (version == 2)
        {
            cmdSet = data[14];
            cmdId = data[15];
            payloadStart = 16;
        }
        else
        {
            dsrc = data[14];
            ddst = data[15];
            cmdSet = data[16];
            cmdId = data[17];
            payloadStart = 18;
        }

        byte[] payload = Array.Empty<byte>();
        if (payloadLen > 0 && data.Length >= payloadStart + payloadLen)
        {
            payload = data[payloadStart..(payloadStart + payloadLen)].ToArray();
        }

        return new BlePacket
        {
            Version = version,
            ProductByte = productByte,
            Sequence = seq,
            Src = src,
            Dst = dst,
            DeltaSrc = dsrc,
            DeltaDst = ddst,
            CmdSet = cmdSet,
            CmdId = cmdId,
            Payload = payload
        };
    }
}
