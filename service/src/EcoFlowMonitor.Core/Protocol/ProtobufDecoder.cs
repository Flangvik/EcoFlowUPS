using System.Text;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Protocol;

public static class ProtobufDecoder
{
    // Wire types
    private const int WireTypeVarint = 0;
    private const int WireType64Bit  = 1;
    private const int WireTypeLenDel = 2;
    private const int WireType32Bit  = 5;

    // ---------------------------------------------------------------
    // Varint reader
    // ---------------------------------------------------------------
    public static ulong ReadVarint(byte[] buf, int i, out int newI)
    {
        int shift = 0;
        ulong val = 0;
        while (true)
        {
            if (i >= buf.Length)
                throw new InvalidOperationException("Buffer underrun reading varint");
            byte b = buf[i++];
            val |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                newI = i;
                return val;
            }
            shift += 7;
        }
    }

    // ---------------------------------------------------------------
    // Field decoder
    // Returns Dictionary<fieldNumber, List<object>>
    //   object is ulong  for wire type 0 (varint)
    //   object is byte[] for wire type 2 (length-delimited)
    //   object is byte[] for wire type 5 (32-bit fixed)
    //   wire type 1 (64-bit fixed) is skipped
    // ---------------------------------------------------------------
    public static Dictionary<int, List<object>> DecodeFields(byte[] buf)
    {
        var fields = new Dictionary<int, List<object>>();
        int i = 0;

        while (i < buf.Length)
        {
            ulong tag = ReadVarint(buf, i, out i);
            int field = (int)(tag >> 3);
            int wtype = (int)(tag & 0x7);

            if (!fields.ContainsKey(field))
                fields[field] = new List<object>();

            switch (wtype)
            {
                case WireTypeVarint:
                {
                    ulong val = ReadVarint(buf, i, out i);
                    fields[field].Add(val);
                    break;
                }
                case WireType64Bit:
                {
                    // Skip 8 bytes
                    i += 8;
                    break;
                }
                case WireTypeLenDel:
                {
                    ulong ln = ReadVarint(buf, i, out i);
                    int length = (int)ln;
                    var data = new byte[length];
                    Array.Copy(buf, i, data, 0, length);
                    fields[field].Add(data);
                    i += length;
                    break;
                }
                case WireType32Bit:
                {
                    var data = new byte[4];
                    Array.Copy(buf, i, data, 0, 4);
                    fields[field].Add(data);
                    i += 4;
                    break;
                }
                default:
                    // Unknown wire type — stop parsing
                    return fields;
            }
        }

        return fields;
    }

    // ---------------------------------------------------------------
    // Helpers to extract typed values from the decoded field map
    // ---------------------------------------------------------------

    private static ulong GetUlong(Dictionary<int, List<object>> f, int field, int index = 0)
    {
        if (!f.TryGetValue(field, out var list) || index >= list.Count) return 0UL;
        return list[index] is ulong u ? u : 0UL;
    }

    private static byte[]? GetBytes(Dictionary<int, List<object>> f, int field, int index = 0)
    {
        if (!f.TryGetValue(field, out var list) || index >= list.Count) return null;
        return list[index] as byte[];
    }

    private static bool HasField(Dictionary<int, List<object>> f, int field)
    {
        return f.ContainsKey(field) && f[field].Count > 0;
    }

    // Signed 64-bit from ulong — C# two's complement cast is exact
    private static long ToSigned64(ulong v) => (long)v;

    // Float from 4-byte little-endian blob
    private static float? ToFloat32(byte[]? blob)
    {
        if (blob == null || blob.Length < 4) return null;
        return BitConverter.ToSingle(blob, 0);
    }

    // Decode a packed repeated field of unsigned varints (wire type 2 blob)
    private static int[] ReadPackedUnsigned(byte[] blob)
    {
        if (blob.Length == 0) return [];
        var result = new List<int>();
        int i = 0;
        while (i < blob.Length)
        {
            ulong v = ReadVarint(blob, i, out i);
            result.Add((int)v);
        }
        return result.ToArray();
    }

    // Decode a packed repeated field of zigzag-signed deci-Celsius values (wire type 2 blob)
    // Returns float[] in degrees Celsius (divided by 10 after zigzag decode)
    private static float[] ReadPackedSignedDC(byte[] blob)
    {
        if (blob.Length == 0) return [];
        var result = new List<float>();
        int i = 0;
        while (i < blob.Length)
        {
            ulong v = ReadVarint(blob, i, out i);
            long signed = (long)(v >> 1) ^ -(long)(v & 1);  // zigzag decode
            result.Add((float)(signed / 10.0));
        }
        return result.ToArray();
    }

    // ---------------------------------------------------------------
    // ParseOuter: top-level envelope -> HeaderMessage -> payload
    // ---------------------------------------------------------------
    public static (byte[] pdata, int cmdFunc, int cmdId, int encType, ulong seq) ParseOuter(byte[] raw)
    {
        // Outer has field 1 = header blob
        var outer = DecodeFields(raw);
        byte[] headerBytes = GetBytes(outer, 1) ?? [];

        var h = DecodeFields(headerBytes);

        byte[] pdata   = GetBytes(h, 1)  ?? [];
        int encType    = (int)GetUlong(h, 6);
        int cmdFunc    = (int)GetUlong(h, 8);
        int cmdId      = (int)GetUlong(h, 9);
        ulong seq      = GetUlong(h, 14);
        int src        = (int)GetUlong(h, 2);

        // XOR decrypt when encType==1 and src!=32
        if (encType == 1 && src != 32)
        {
            byte key = (byte)(seq & 0xFF);
            var decrypted = new byte[pdata.Length];
            for (int i = 0; i < pdata.Length; i++)
                decrypted[i] = (byte)(pdata[i] ^ key);
            pdata = decrypted;
        }

        return (pdata, cmdFunc, cmdId, encType, seq);
    }

    // ---------------------------------------------------------------
    // DecodeBms: cmdFunc=32, cmdId=50
    // ---------------------------------------------------------------
    public static BmsData DecodeBms(byte[] pdata)
    {
        var f = DecodeFields(pdata);
        var bms = new BmsData();

        // BatteryPct: prefer field 25 (wire type 2, float32), fallback to field 6 (varint uint)
        byte[]? battBlob = GetBytes(f, 25);
        if (battBlob != null && battBlob.Length >= 4)
        {
            bms.BatteryPct = ToFloat32(battBlob);
        }
        else if (HasField(f, 6))
        {
            bms.BatteryPct = (float)GetUlong(f, 6);
        }

        // VoltageV: field 7 / 1000
        if (HasField(f, 7))
            bms.VoltageV = (float)((double)GetUlong(f, 7) / 1000.0);

        // CurrentA: field 8 as signed int64 / 1000
        if (HasField(f, 8))
            bms.CurrentA = (float)((double)ToSigned64(GetUlong(f, 8)) / 1000.0);

        // TempC: field 9 as signed int64 / 10
        if (HasField(f, 9))
            bms.TempC = (float)((double)ToSigned64(GetUlong(f, 9)) / 10.0);

        // DesignCapMah: field 11
        if (HasField(f, 11))
            bms.DesignCapMah = (int)GetUlong(f, 11);

        // RemainCapMah: field 12
        if (HasField(f, 12))
            bms.RemainCapMah = (int)GetUlong(f, 12);

        // Cycles: field 14
        if (HasField(f, 14))
            bms.Cycles = (int)GetUlong(f, 14);

        // SohPct: field 15
        if (HasField(f, 15))
            bms.SohPct = (int)GetUlong(f, 15);

        // MaxCellMv: field 16
        if (HasField(f, 16))
            bms.MaxCellMv = (int)GetUlong(f, 16);

        // MinCellMv: field 17
        if (HasField(f, 17))
            bms.MinCellMv = (int)GetUlong(f, 17);

        // RemainMin: field 28
        if (HasField(f, 28))
            bms.RemainMin = (int)GetUlong(f, 28);

        // InputW: field 26
        if (HasField(f, 26))
            bms.InputW = (int)GetUlong(f, 26);

        // OutputW: field 27
        if (HasField(f, 27))
            bms.OutputW = (int)GetUlong(f, 27);

        // CellVolsMv: field 33 — packed unsigned varints (mV each)
        byte[]? cellVolBlob = GetBytes(f, 33);
        if (cellVolBlob != null)
            bms.CellVolsMv = ReadPackedUnsigned(cellVolBlob);

        // CellTempsC: field 35 — packed zigzag signed deci-Celsius
        byte[]? cellTempBlob = GetBytes(f, 35);
        if (cellTempBlob != null)
            bms.CellTempsC = ReadPackedSignedDC(cellTempBlob);

        // MosTempsC: field 56 — packed zigzag signed deci-Celsius
        byte[]? mosTempBlob = GetBytes(f, 56);
        if (mosTempBlob != null)
            bms.MosTempsC = ReadPackedSignedDC(mosTempBlob);

        // AccuChgEnergyWh: field 79
        if (HasField(f, 79))
            bms.AccuChgEnergyWh = (long)GetUlong(f, 79);

        // AccuDsgEnergyWh: field 80
        if (HasField(f, 80))
            bms.AccuDsgEnergyWh = (long)GetUlong(f, 80);

        // PackSn: field 81 — UTF-8 string
        byte[]? packSnBlob = GetBytes(f, 81);
        if (packSnBlob != null)
            bms.PackSn = Encoding.UTF8.GetString(packSnBlob);

        return bms;
    }

    // ---------------------------------------------------------------
    // DecodeDisplay: cmdFunc=254, cmdId=21 or 22
    // ---------------------------------------------------------------
    public static DisplayData DecodeDisplay(byte[] pdata)
    {
        var f = DecodeFields(pdata);
        var disp = new DisplayData();

        // Helper: read a float32 field that may be wire type 5 (32-bit fixed) or wire type 0 (uint)
        int? ReadFloatField(int fieldNum)
        {
            byte[]? blob = GetBytes(f, fieldNum);
            if (blob != null && blob.Length >= 4)
            {
                float? fv = ToFloat32(blob);
                return fv.HasValue ? (int?)Math.Round(fv.Value) : null;
            }
            if (HasField(f, fieldNum)) return (int)GetUlong(f, fieldNum);
            return null;
        }

        disp.TotalInW       = ReadFloatField(3);
        disp.TotalOutW      = ReadFloatField(4);
        disp.SolarInHighW   = ReadFloatField(35);
        disp.SolarInLowW    = ReadFloatField(36);
        disp.AcInW          = ReadFloatField(54);

        // USB port watts: unsigned varints
        if (HasField(f, 9))  disp.UsbA1W = (int)GetUlong(f, 9);
        if (HasField(f, 10)) disp.UsbA2W = (int)GetUlong(f, 10);
        if (HasField(f, 11)) disp.UsbC1W = (int)GetUlong(f, 11);
        if (HasField(f, 12)) disp.UsbC2W = (int)GetUlong(f, 12);

        // AC status
        if (HasField(f, 61)) disp.AcPluggedIn = GetUlong(f, 61) != 0;
        if (HasField(f, 62)) disp.AcInFreqHz  = (int)GetUlong(f, 62);

        return disp;
    }

    // ---------------------------------------------------------------
    // DecodeEms: cmdFunc=32, cmdId=2
    // CMS envelope wraps EMS v1.0 at field 1 and EMS v1.3 at field 2
    // ---------------------------------------------------------------
    public static EmsData DecodeEms(byte[] pdata)
    {
        var ems = new EmsData();
        var f = DecodeFields(pdata);

        // EMS v1.0 sub-message
        byte[]? v10 = GetBytes(f, 1);
        if (v10 != null)
        {
            var e1 = DecodeFields(v10);
            if (HasField(e1, 1))  ems.ChgState     = (int)GetUlong(e1, 1);
            if (HasField(e1, 6))  ems.FanLevel      = (int)GetUlong(e1, 6);
            if (HasField(e1, 7))  ems.MaxChargeSoc  = (int)GetUlong(e1, 7);
            if (HasField(e1, 10)) ems.UpsMode       = (int)GetUlong(e1, 10);
            if (HasField(e1, 12)) ems.ChgRemainMin  = (int)GetUlong(e1, 12);
            if (HasField(e1, 13)) ems.DsgRemainMin  = (int)GetUlong(e1, 13);

            byte[]? bmsConnBlob = GetBytes(e1, 16);
            if (bmsConnBlob != null)
                ems.BmsConnected = ReadPackedUnsigned(bmsConnBlob);
        }

        // EMS v1.3 sub-message
        byte[]? v13 = GetBytes(f, 2);
        if (v13 != null)
        {
            var e2 = DecodeFields(v13);
            if (HasField(e2, 3)) ems.ChgLinePlugged = (int)GetUlong(e2, 3);
        }

        return ems;
    }

    // ---------------------------------------------------------------
    // Dispatch: parse raw bytes and route to correct decoder
    // Returns true if at least one of bms/display/ems was populated
    // ---------------------------------------------------------------
    public static bool Dispatch(byte[] raw, out BmsData? bms, out DisplayData? display, out EmsData? ems)
    {
        bms     = null;
        display = null;
        ems     = null;

        try
        {
            var (pdata, cmdFunc, cmdId, encType, seq) = ParseOuter(raw);

            // BMS: cmdFunc=32, cmdId=50
            if (cmdFunc == 32 && cmdId == 50)
            {
                bms = DecodeBms(pdata);
                return true;
            }

            // CMS/EMS: cmdFunc=32, cmdId=2
            if (cmdFunc == 32 && cmdId == 2)
            {
                ems = DecodeEms(pdata);
                return true;
            }

            // Display: cmdFunc=254, cmdId=21 or 22
            if (cmdFunc == 254 && (cmdId == 21 || cmdId == 22))
            {
                display = DecodeDisplay(pdata);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
