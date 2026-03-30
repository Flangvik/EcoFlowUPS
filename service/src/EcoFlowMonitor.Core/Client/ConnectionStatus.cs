namespace EcoFlowMonitor.Client;

/// <summary>
/// Represents the lifecycle state of a device monitor connection.
/// Used by Stateless FSM in BleMonitor and MqttMonitor (Plan 03).
/// </summary>
public enum ConnectionStatus
{
    Idle,           // Pre-start or after explicit stop
    Scanning,       // BLE only: advertising filter active, looking for device
    Connecting,     // GATT connect / MQTT TLS handshake in progress
    Authenticating, // BLE: ECDH + auth packet; MQTT: credentials verified
    Streaming,      // Data flowing; LastDataReceived advances per packet
    Retrying,       // Waiting for next Polly retry window (exposes RetryAttempt + RetryDelay)
    Error,          // Non-retriable error requiring user action
    Disconnected    // Clean disconnect or pre-first-connect
}

/// <summary>
/// Triggers that drive ConnectionStatus transitions in the Stateless FSM.
/// </summary>
public enum ConnectionTrigger
{
    Start,          // StartAsync() called
    DeviceFound,    // BLE: advertisement seen matching serial number
    Connected,      // Transport-level connection open (GATT / MQTT TLS)
    Authenticated,  // Auth handshake completed successfully
    DataReceived,   // First valid packet after connect -> transitions to Streaming
    RetryScheduled, // Polly OnRetry callback fires with retry delay (parameterized: TimeSpan)
    ErrorOccurred,  // Non-retriable error (parameterized: string errorMessage)
    Disconnected,   // Transport closed (device out of range, broker disconnect)
    Stop            // StopAsync() called -- transitions to Idle
}
