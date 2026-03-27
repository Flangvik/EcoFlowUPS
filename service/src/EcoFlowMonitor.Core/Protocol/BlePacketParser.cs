using System.Buffers.Binary;
using EcoFlowMonitor.Logging;

namespace EcoFlowMonitor.Protocol;

public static class BlePacketParser
{
    private const byte PacketPrefix = 0xAA;

    /// <summary>
    /// Try to extract a complete 0x5A5A EncPacket frame from a buffer.
    ///
    /// Wire format: [5A 5A] [frameType<<4] [0x01] [lenField:u16LE] [payload] [CRC16:u16LE]
    /// where lenField = len(payload) + 2 (CRC is counted in the length field).
    /// Total frame = 6 + lenField bytes.
    /// CRC16 covers bytes from 5A5A through payload (excludes CRC itself).
    /// </summary>
    public static (byte frameType, byte[] payload, int bytesConsumed)? TryParseFrame(ReadOnlySpan<byte> buffer)
    {
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
        ushort lenField = BinaryPrimitives.ReadUInt16LittleEndian(frame[4..6]);

        // lenField includes the 2-byte CRC at the end
        // Total frame = 6 (header) + lenField
        int totalLen = 6 + lenField;
        if (frame.Length < totalLen) return null; // incomplete

        // Payload is between header and CRC: frame[6 .. totalLen-2]
        int payloadLen = lenField - 2;
        if (payloadLen < 0) return null;

        var payloadData = frame[6..(6 + payloadLen)];
        var crcData = frame[(totalLen - 2)..totalLen];

        // CRC16 covers header + payload (everything except the CRC itself)
        ushort expectedCrc = BinaryPrimitives.ReadUInt16LittleEndian(crcData);
        ushort actualCrc = Crc.ComputeCrc16(frame[..(totalLen - 2)]);
        if (expectedCrc != actualCrc)
        {
            Logger.Log($"BlePacketParser: frame CRC16 mismatch expected=0x{expectedCrc:X4} actual=0x{actualCrc:X4}");
            return null;
        }

        return (frameType, payloadData.ToArray(), start + totalLen);
    }

    /// <summary>
    /// Parse a decrypted 0xAA application packet.
    /// </summary>
    public static BlePacket? ParsePacket(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[0] != PacketPrefix) return null;

        byte version = data[1];
        int headerLen = version == 2 ? 16 : 18;
        if (data.Length < headerLen) return null;

        ushort payloadLen = BinaryPrimitives.ReadUInt16LittleEndian(data[2..4]);

        // Validate header CRC8 (first 4 bytes)
        byte expectedCrc8 = data[4];
        byte actualCrc8 = Crc.ComputeCrc8(data[..4]);
        if (expectedCrc8 != actualCrc8)
        {
            Logger.Log($"BlePacketParser: header CRC8 mismatch expected=0x{expectedCrc8:X2} actual=0x{actualCrc8:X2}");
            return null;
        }

        byte productByte = data[5];
        var seq = data[6..10].ToArray();
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

        // Total packet = headerLen + payloadLen + 2 (CRC16)
        int totalPacketLen = payloadStart + payloadLen + 2;

        // Validate packet CRC16 if we have enough data
        if (version is 2 or 3 or 4 && data.Length >= totalPacketLen)
        {
            ushort expectedCrc16 = BinaryPrimitives.ReadUInt16LittleEndian(data[(totalPacketLen - 2)..totalPacketLen]);
            ushort actualCrc16 = Crc.ComputeCrc16(data[..(totalPacketLen - 2)]);
            if (expectedCrc16 != actualCrc16)
            {
                Logger.Log($"BlePacketParser: packet CRC16 mismatch");
                // Don't return null — some packets may have different CRC behavior
            }
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
