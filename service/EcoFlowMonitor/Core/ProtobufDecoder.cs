using System;
using System.Collections.Generic;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Core
{
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

        private static byte[] GetBytes(Dictionary<int, List<object>> f, int field, int index = 0)
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
        private static float? ToFloat32(byte[] blob)
        {
            if (blob == null || blob.Length < 4) return null;
            return BitConverter.ToSingle(blob, 0);
        }

        // ---------------------------------------------------------------
        // ParseOuter: top-level envelope -> HeaderMessage -> payload
        // ---------------------------------------------------------------
        public static (byte[] pdata, int cmdFunc, int cmdId, int encType, ulong seq) ParseOuter(byte[] raw)
        {
            // Outer has field 1 = header blob
            var outer = DecodeFields(raw);
            byte[] headerBytes = GetBytes(outer, 1) ?? new byte[0];

            var h = DecodeFields(headerBytes);

            byte[] pdata   = GetBytes(h, 1)  ?? new byte[0];
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
            byte[] battBlob = GetBytes(f, 25);
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

            // RemainMin: field 28
            if (HasField(f, 28))
                bms.RemainMin = (int)GetUlong(f, 28);

            // Cycles: field 14
            if (HasField(f, 14))
                bms.Cycles = (int)GetUlong(f, 14);

            // SohPct: field 15
            if (HasField(f, 15))
                bms.SohPct = (int)GetUlong(f, 15);

            // InputW: field 26
            if (HasField(f, 26))
                bms.InputW = (int)GetUlong(f, 26);

            // OutputW: field 27
            if (HasField(f, 27))
                bms.OutputW = (int)GetUlong(f, 27);

            return bms;
        }

        // ---------------------------------------------------------------
        // DecodeDisplay: cmdFunc=254, cmdId=21 or 22
        // ---------------------------------------------------------------
        public static DisplayData DecodeDisplay(byte[] pdata)
        {
            var f = DecodeFields(pdata);
            var disp = new DisplayData();

            // TotalInW: field 3 — may be wire type 5 (float32) or wire type 0 (uint)
            byte[] inBlob = GetBytes(f, 3);
            if (inBlob != null && inBlob.Length >= 4)
            {
                float? fval = ToFloat32(inBlob);
                if (fval.HasValue) disp.TotalInW = (int)Math.Round(fval.Value);
            }
            else if (HasField(f, 3))
            {
                disp.TotalInW = (int)GetUlong(f, 3);
            }

            // TotalOutW: field 4
            byte[] outBlob = GetBytes(f, 4);
            if (outBlob != null && outBlob.Length >= 4)
            {
                float? fval = ToFloat32(outBlob);
                if (fval.HasValue) disp.TotalOutW = (int)Math.Round(fval.Value);
            }
            else if (HasField(f, 4))
            {
                disp.TotalOutW = (int)GetUlong(f, 4);
            }

            // AcInW: field 54
            byte[] acBlob = GetBytes(f, 54);
            if (acBlob != null && acBlob.Length >= 4)
            {
                float? fval = ToFloat32(acBlob);
                if (fval.HasValue) disp.AcInW = (int)Math.Round(fval.Value);
            }
            else if (HasField(f, 54))
            {
                disp.AcInW = (int)GetUlong(f, 54);
            }

            return disp;
        }

        // ---------------------------------------------------------------
        // Dispatch: parse raw bytes and route to correct decoder
        // Returns true if at least one of bms/display was populated
        // ---------------------------------------------------------------
        public static bool Dispatch(byte[] raw, out BmsData bms, out DisplayData display)
        {
            bms     = null;
            display = null;

            try
            {
                var (pdata, cmdFunc, cmdId, encType, seq) = ParseOuter(raw);

                // BMS: cmdFunc=32, cmdId=50
                if (cmdFunc == 32 && cmdId == 50)
                {
                    bms = DecodeBms(pdata);
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
}
