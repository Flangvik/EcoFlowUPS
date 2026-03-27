namespace EcoFlowMonitor.Protocol;

public static class Crc
{
    // CRC-8/CCITT - used for BLE packet header validation (first 4 bytes -> byte 4)
    private static readonly byte[] Crc8Table = BuildCrc8Table();

    private static byte[] BuildCrc8Table()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            byte crc = (byte)i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x07) : (byte)(crc << 1);
            table[i] = crc;
        }
        return table;
    }

    public static byte ComputeCrc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var b in data)
            crc = Crc8Table[crc ^ b];
        return crc;
    }

    // CRC-16/ARC (MODBUS) - reflected input/output, poly 0x8005
    // Used for BLE packet and frame integrity
    private static readonly ushort[] Crc16Table = BuildCrc16Table();

    private static ushort[] BuildCrc16Table()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            table[i] = crc;
        }
        return table;
    }

    public static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data)
            crc = (ushort)((crc >> 8) ^ Crc16Table[(crc ^ b) & 0xFF]);
        return crc;
    }
}
