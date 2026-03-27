using EcoFlowMonitor.Logging;
using EcoFlowMonitor.Models;

namespace EcoFlowMonitor.Protocol;

/// <summary>
/// Routes BLE packets by (src, cmdSet, cmdId) to the appropriate protobuf decoder.
/// The inner protobuf payloads use the same field layouts as MQTT messages,
/// so we reuse ProtobufDecoder.DecodeBms/DecodeDisplay/DecodeEms.
/// </summary>
public static class BleDispatcher
{
    // BLE source addresses
    private const byte SrcPd = 0x02;     // Power Delivery
    private const byte SrcEms = 0x03;    // Energy Management System
    private const byte SrcInverter = 0x04;
    private const byte SrcMppt = 0x05;   // Solar charger
    private const byte SrcBms = 0x0B;    // Battery Management System

    // Common heartbeat cmdSet
    private const byte CmdSetHeartbeat = 0x02;
    private const byte CmdSetData = 0x20;

    public static bool Dispatch(BlePacket packet, out BmsData? bms, out DisplayData? display, out EmsData? ems)
    {
        bms = null;
        display = null;
        ems = null;

        if (packet.Payload.Length == 0) return false;

        try
        {
            // BMS heartbeat -- battery data
            if (packet.Src == SrcBms ||
                (packet.Src == SrcEms && packet.CmdSet == CmdSetData && packet.CmdId == 0x32))
            {
                bms = ProtobufDecoder.DecodeBms(packet.Payload);
                return bms != null;
            }

            // PD heartbeat -- power display data (input/output/USB/AC)
            if (packet.Src == SrcPd && (packet.CmdSet == CmdSetHeartbeat || packet.CmdSet == CmdSetData))
            {
                display = ProtobufDecoder.DecodeDisplay(packet.Payload);
                return display != null;
            }

            // EMS heartbeat -- energy management (charge state, UPS mode, fan)
            if (packet.Src == SrcEms && (packet.CmdSet == CmdSetHeartbeat || packet.CmdSet == CmdSetData) && packet.CmdId != 0x32)
            {
                ems = ProtobufDecoder.DecodeEms(packet.Payload);
                return ems != null;
            }

            Logger.Log($"BleDispatcher: unhandled packet src=0x{packet.Src:X2} cmdSet=0x{packet.CmdSet:X2} cmdId=0x{packet.CmdId:X2} len={packet.Payload.Length}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"BleDispatcher: decode error -- {ex.Message}");
            return false;
        }
    }
}
