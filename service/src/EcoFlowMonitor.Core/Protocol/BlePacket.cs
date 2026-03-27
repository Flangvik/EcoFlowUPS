namespace EcoFlowMonitor.Protocol;

public class BlePacket
{
    public byte Version { get; init; }
    public byte ProductByte { get; init; }
    public byte[] Sequence { get; init; } = new byte[4];
    public byte Src { get; init; }
    public byte Dst { get; init; }
    public byte DeltaSrc { get; init; }
    public byte DeltaDst { get; init; }
    public byte CmdSet { get; init; }
    public byte CmdId { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();
}
