# EcoFlowUPS

Unofficial EcoFlow power station monitor — no developer API key required.

Connects to EcoFlow's cloud MQTT broker using your regular account credentials, decodes protobuf telemetry, and fires configurable actions (scripts, shutdown, notifications, log entries) based on power events and battery thresholds.

> **Status:** Confirmed working on EcoFlow Delta 3. Any EcoFlow device with MQTT telemetry should work.

---

## Components

### `poc/` — Python Reference Implementation

Standalone scripts that document the EcoFlow API and MQTT protocol. Use these to explore the protocol or verify your credentials before configuring the Windows service.

See [`poc/README.md`](poc/README.md) for setup and usage.

### `service/` — Windows Tray Monitor

C# .NET Framework 4.8 WinForms application that runs in the system tray and monitors one or more EcoFlow devices. Fires user-configured rules when power events or battery thresholds occur.

See [`service/README.md`](service/README.md) for setup and usage.

---

## Protocol Notes

- **Auth:** REST login at `api.ecoflow.com/auth/login` returns a JWT token and user ID
- **MQTT credentials:** Separate REST call to `api.ecoflow.com/iot-auth/app/certification` exchanges the JWT for MQTT username/password
- **Broker:** `mqtt.ecoflow.com:8883` (TLS, certificate validation disabled — self-signed cert)
- **Topic:** `/app/device/property/{serialNumber}` — device publishes telemetry here
- **Encoding:** Protobuf binary with a custom outer envelope; inner fields are XOR-encrypted when `encType == 1` and `src != 32`, key = `seq & 0xFF`
- **No official SDK:** This is based on packet capture and reverse engineering

---

## Supported Triggers

| Trigger | Description |
|---|---|
| `PowerLost` | AC input dropped to 0 W (edge, fires once) |
| `PowerRestored` | AC input returned after power loss (edge, fires once) |
| `BatteryBelow` | Battery percentage below threshold (level, with 5-min cooldown) |
| `TimeRemainingBelow` | Estimated minutes remaining below threshold (level, with 5-min cooldown) |

## Supported Actions

| Action | Description |
|---|---|
| `RunScript` | Execute a `.bat`, `.ps1`, or `.exe` |
| `Shutdown` | `shutdown.exe /s /t 0` |
| `Hibernate` | `shutdown.exe /h` |
| `Sleep` | `rundll32.exe powrprof.dll,SetSuspendState 0,1,0` |
| `Notification` | Windows toast / balloon tip |
| `WriteLog` | Append timestamped entry to a log file |

Template variables available in notification body and log messages: `{device}`, `{battery}`, `{remain}`, `{status}`, `{in_w}`, `{out_w}`.
