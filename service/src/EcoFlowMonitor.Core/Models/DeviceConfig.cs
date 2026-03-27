namespace EcoFlowMonitor.Models;

/// <summary>
/// Single device config keyed by SerialNumber.
/// A device can have both cloud and BLE capabilities — ConnectionMode controls which is used.
/// </summary>
public class DeviceConfig
{
    /// <summary>Serial number — the universal device identifier across cloud and BLE.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>User-facing name (from cloud API or manual override).</summary>
    public string DisplayName { get; set; } = "EcoFlow Device";

    /// <summary>How to connect: Cloud, Ble, or Auto (try BLE first, fall back to Cloud).</summary>
    public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Cloud;

    /// <summary>Automation rules for this device.</summary>
    public List<RuleConfig> Rules { get; set; } = new();

    // ── BLE capabilities (populated when device is discovered via BLE scan) ──

    /// <summary>BLE address (macOS UUID or platform-specific identifier). Null if never seen via BLE.</summary>
    public string? BleAddress { get; set; }

    /// <summary>BLE encryption type (1=legacy, 7=modern ECDH). 0 if unknown.</summary>
    public int BleEncryptionType { get; set; }

    /// <summary>BLE protocol version (2 or 3). Defaults to 3.</summary>
    public int BleProtocolVersion { get; set; } = 3;

    /// <summary>BLE advertisement name (may differ from cloud DisplayName).</summary>
    public string? BleName { get; set; }

    /// <summary>True if this device has been seen via BLE and has a valid BLE address.</summary>
    public bool HasBle => !string.IsNullOrEmpty(BleAddress);

    /// <summary>True if this device was discovered via cloud API.</summary>
    public bool HasCloud { get; set; } = true;
}
