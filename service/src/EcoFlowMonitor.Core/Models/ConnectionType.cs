namespace EcoFlowMonitor.Models;

/// <summary>
/// How to connect to this device. Auto tries BLE first, falls back to Cloud.
/// </summary>
public enum ConnectionMode { Cloud, Ble, Auto }
