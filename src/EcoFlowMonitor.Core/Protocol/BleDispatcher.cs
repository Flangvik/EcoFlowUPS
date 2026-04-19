using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Protocol;

/// <summary>
/// Routes BLE packets by (src, cmdSet, cmdId) to the appropriate protobuf decoder.
/// Delta 3 family sends all data via DisplayPropertyUpload (src=0x02, cmdSet=0xFE, cmdId=0x15).
/// Other devices use different routing — extend here as devices are added.
/// </summary>
public static class BleDispatcher
{
    public static bool Dispatch(BlePacket packet, out BmsData? bms, out DisplayData? display, out EmsData? ems)
    {
        bms = null;
        display = null;
        ems = null;

        if (packet.Payload.Length == 0) return false;

        try
        {
            // Delta 3 family: src=0x02, cmdSet=0xFE, cmdId=0x15 → DisplayPropertyUpload
            // This is the primary data message containing battery, power, and system state
            if (packet.Src == 0x02 && packet.CmdSet == 0xFE && (packet.CmdId == 0x15 || packet.CmdId == 0x16))
            {
                return BleProtoMapper.MapDelta3Display(packet.Payload, out bms, out display, out ems);
            }

            // Auth responses (not data — handled separately by BleMonitor)
            if (packet.CmdSet == 0x35)
                return false;

            // Time sync request from device (cmdSet=0x01, cmdId=0x52)
            if (packet.CmdSet == 0x01 && packet.CmdId == 0x52)
            {
                System.Diagnostics.Debug.WriteLine("BleDispatcher: device requested time sync");
                return false;
            }

            // Fallback: try legacy MQTT-style decoding for other device types
            if (packet.Src == 0x0B || (packet.Src == 0x03 && packet.CmdSet == 0x20 && packet.CmdId == 0x32))
            {
                bms = ProtobufDecoder.DecodeBms(packet.Payload);
                return bms != null;
            }
            if (packet.Src == 0x02 && packet.CmdSet == 0x20)
            {
                display = ProtobufDecoder.DecodeDisplay(packet.Payload);
                return display != null;
            }
            if (packet.Src == 0x03 && packet.CmdSet == 0x20)
            {
                ems = ProtobufDecoder.DecodeEms(packet.Payload);
                return ems != null;
            }

            System.Diagnostics.Debug.WriteLine($"BleDispatcher: unhandled src=0x{packet.Src:X2} cs=0x{packet.CmdSet:X2} ci=0x{packet.CmdId:X2} len={packet.Payload.Length}");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BleDispatcher: error — {ex.Message}");
            return false;
        }
    }
}
