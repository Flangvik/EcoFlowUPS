using EcoFlowMonitor.Models;
using Google.Protobuf;

namespace EcoFlowMonitor.Protocol;

/// <summary>
/// Maps device-specific BLE protobuf messages to the shared model types.
/// </summary>
public static class BleProtoMapper
{
    /// <summary>
    /// Map Delta 3 DisplayPropertyUpload to shared models.
    /// </summary>
    public static bool MapDelta3Display(byte[] payload, out BmsData? bms, out DisplayData? display, out EmsData? ems)
    {
        bms = null;
        display = null;
        ems = null;

        try
        {
            var m = Pd335Sys.DisplayPropertyUpload.Parser.ParseFrom(payload);

            // ── Battery / BMS ──
            bms = new BmsData();

            if (m.CmsBattSoc != 0) bms.BatteryPct = (float)m.CmsBattSoc;
            else if (m.BmsBattSoc != 0) bms.BatteryPct = (float)m.BmsBattSoc;

            if (m.HasBmsMaxCellTemp) bms.TempC = m.BmsMaxCellTemp / 10f; // sint32 in deci-degrees
            if (m.BmsBattSoh != 0) bms.SohPct = (int)m.BmsBattSoh;
            else if (m.CmsBattSoh != 0) bms.SohPct = (int)m.CmsBattSoh;
            if (m.BmsDesignCap != 0) bms.DesignCapMah = (int)m.BmsDesignCap;
            if (m.CmsDsgRemTime != 0) bms.RemainMin = (int)m.CmsDsgRemTime;
            else if (m.BmsDsgRemTime != 0) bms.RemainMin = (int)m.BmsDsgRemTime;

            if (m.PowGetBms != 0)
            {
                var w = (int)Math.Round(m.PowGetBms);
                if (w > 0) bms.InputW = w;
                else if (w < 0) bms.OutputW = -w;
            }

            // ── Display / Power ──
            display = new DisplayData();

            if (m.PowInSumW != 0) display.TotalInW = (int)Math.Round(m.PowInSumW);
            if (m.PowOutSumW != 0) display.TotalOutW = (int)Math.Round(m.PowOutSumW);
            if (m.PowGetAcIn != 0) display.AcInW = (int)Math.Round(m.PowGetAcIn);
            if (m.PlugInInfoAcInFlag != 0) display.AcPluggedIn = true;
            if (m.PlugInInfoAcInFeq != 0) display.AcInFreqHz = (int)m.PlugInInfoAcInFeq;
            else if (m.AcOutFreq != 0) display.AcInFreqHz = (int)m.AcOutFreq;
            if (m.PowGetPv != 0) display.SolarInHighW = (int)Math.Round(m.PowGetPv);
            if (m.PowGetPv2 != 0) display.SolarInLowW = (int)Math.Round(m.PowGetPv2);
            if (m.PowGetTypec1 != 0) display.UsbC1W = (int)Math.Abs(Math.Round(m.PowGetTypec1));
            if (m.PowGetTypec2 != 0) display.UsbC2W = (int)Math.Abs(Math.Round(m.PowGetTypec2));
            if (m.PowGetQcusb1 != 0) display.UsbA1W = (int)Math.Abs(Math.Round(m.PowGetQcusb1));
            if (m.PowGetQcusb2 != 0) display.UsbA2W = (int)Math.Abs(Math.Round(m.PowGetQcusb2));

            // ── EMS / System ──
            ems = new EmsData();

            if (m.CmsMaxChgSoc != 0) ems.MaxChargeSoc = (int)m.CmsMaxChgSoc;
            if (m.CmsChgDsgState != 0) ems.ChgState = (int)m.CmsChgDsgState;
            if (m.CmsChgRemTime != 0) ems.ChgRemainMin = (int)m.CmsChgRemTime;
            if (m.CmsDsgRemTime != 0) ems.DsgRemainMin = (int)m.CmsDsgRemTime;
            if (m.PcsFanLevel != 0) ems.FanLevel = (int)m.PcsFanLevel;

            return true;
        }
        catch (InvalidProtocolBufferException ex)
        {
            System.Diagnostics.Debug.WriteLine($"BleProtoMapper: protobuf error — {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BleProtoMapper: error — {ex.Message}");
            return false;
        }
    }
}
