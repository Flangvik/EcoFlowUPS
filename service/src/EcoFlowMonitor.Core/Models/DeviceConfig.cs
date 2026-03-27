namespace EcoFlowMonitor.Models;

public class DeviceConfig
{
    public string DisplayName { get; set; } = "EcoFlow Device";
    public string? SerialNumber { get; set; }
    public List<RuleConfig> Rules { get; set; } = new();
    public ConnectionType ConnectionType { get; set; } = ConnectionType.Cloud;
    public string? BleAddress { get; set; }
    public int BleEncryptionType { get; set; }
    public int BleProtocolVersion { get; set; } = 3;
}
