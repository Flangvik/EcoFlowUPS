namespace EcoFlowMonitor.Models;

public class BleDeviceInfo
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public int ProtocolVersion { get; set; }
    public int EncryptionType { get; set; }
    public int Rssi { get; set; }
}
