using EcoFlowMonitor.Logging;
using EcoFlowMonitor.Models;
using Google.Protobuf;

namespace EcoFlowMonitor.Protocol;

/// <summary>
/// Maps device-specific BLE protobuf messages to the shared model types
/// used by the rest of the app (dashboard, triggers, actions).
///
/// Each device family has its own protobuf schema. Add new MapXxx methods
/// for new device families and register them in BleDispatcher.
/// </summary>
public static class BleProtoMapper
{
    /// <summary>
    /// Map Delta 3 DisplayPropertyUpload to shared BmsData + DisplayData + EmsData.
    /// The Delta 3 sends ALL status data in a single DisplayPropertyUpload message,
    /// so we extract battery, power, and system fields from the same message.
    /// </summary>
    public static bool MapDelta3Display(byte[] payload, out BmsData? bms, out DisplayData? display, out EmsData? ems)
    {
        bms = null;
        display = null;
        ems = null;

        try
        {
            var msg = Pd335Sys.DisplayPropertyUpload.Parser.ParseFrom(payload);

            // Battery / BMS data
            bms = new BmsData();
            if (msg.CmsBattSoc != 0) bms.BatteryPct = (float)msg.CmsBattSoc;
            else if (msg.BmsBattSoc != 0) bms.BatteryPct = (float)msg.BmsBattSoc;

            if (msg.BmsMaxCellTemp != 0) bms.TempC = (float)msg.BmsMaxCellTemp;
            if (msg.BmsBattSoh != 0) bms.SohPct = (int)msg.BmsBattSoh;
            if (msg.CmsDsgRemTime != 0) bms.RemainMin = (int)msg.CmsDsgRemTime;
            if (msg.BmsDesignCap != 0) bms.DesignCapMah = (int)msg.BmsDesignCap;

            // Display / Power data
            display = new DisplayData();
            if (msg.PowInSumW != 0) display.TotalInW = (int)Math.Round(msg.PowInSumW);
            if (msg.PowOutSumW != 0) display.TotalOutW = (int)Math.Round(msg.PowOutSumW);
            if (msg.PowGetAcIn != 0) display.AcInW = (int)Math.Round(msg.PowGetAcIn);
            if (msg.PowGetPv != 0) display.SolarInHighW = (int)Math.Round(msg.PowGetPv);
            if (msg.PowGetTypec1 != 0) display.UsbC1W = (int)Math.Abs(Math.Round(msg.PowGetTypec1));
            if (msg.PowGetTypec2 != 0) display.UsbC2W = (int)Math.Abs(Math.Round(msg.PowGetTypec2));
            if (msg.PowGetQcusb1 != 0) display.UsbA1W = (int)Math.Abs(Math.Round(msg.PowGetQcusb1));
            if (msg.PowGetQcusb2 != 0) display.UsbA2W = (int)Math.Abs(Math.Round(msg.PowGetQcusb2));
            if (msg.PowGetAcOut != 0) display.TotalOutW ??= (int)Math.Abs(Math.Round(msg.PowGetAcOut));

            // EMS / System data
            ems = new EmsData();
            if (msg.CmsMaxChgSoc != 0) ems.MaxChargeSoc = (int)msg.CmsMaxChgSoc;
            if (msg.CmsChgDsgState != 0) ems.ChgState = (int)msg.CmsChgDsgState;
            if (msg.CmsChgRemTime != 0) ems.ChgRemainMin = (int)msg.CmsChgRemTime;
            if (msg.CmsDsgRemTime != 0) ems.DsgRemainMin = (int)msg.CmsDsgRemTime;

            return true;
        }
        catch (InvalidProtocolBufferException ex)
        {
            Logger.Log($"BleProtoMapper: protobuf parse error — {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"BleProtoMapper: mapping error — {ex.Message}");
            return false;
        }
    }
}
