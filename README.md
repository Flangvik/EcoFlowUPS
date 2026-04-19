# EcoFlowUPS

Cross-platform desktop monitor for EcoFlow power stations. Real-time telemetry via **BLE** (local) or **MQTT** (cloud), configurable automation rules, and persistent history. No developer API key required — authenticates with your regular EcoFlow account.

> **Status:** Confirmed working on EcoFlow Delta 3 (BLE + MQTT). Other EcoFlow devices using the `pd335_sys` protobuf should also work.

---

## Download

Pre-built, self-contained binaries are published on the [Releases page](../../releases/latest). No .NET runtime install required.

| Platform | File |
|---|---|
| Windows x64 | `EcoFlowMonitor-vX.Y.Z-win-x64.zip` |
| Linux x64 | `EcoFlowMonitor-vX.Y.Z-linux-x64.tar.gz` |
| macOS (Apple Silicon) | `EcoFlowMonitor-vX.Y.Z-osx-arm64.zip` |
| macOS (Intel) | `EcoFlowMonitor-vX.Y.Z-osx-x64.zip` |

Binaries are **unsigned**. First-run workarounds:

- **macOS (Gatekeeper):** `xattr -cr /path/to/EcoFlowMonitor.App.app` after unzipping.
- **Windows (SmartScreen):** click **More info → Run anyway** on the warning dialog.
- **Linux:** make sure `bluetoothd` is running and your user is in the `bluetooth` group. Mark the binary executable with `chmod +x EcoFlowMonitor.App` if needed.

---

## Repository layout

```
EcoFlowUPS/
├── src/                  Avalonia desktop app (.NET 10, cross-platform)
│   ├── EcoFlowMonitor.sln
│   ├── EcoFlowMonitor.App/                Avalonia UI, ViewModels, services
│   ├── EcoFlowMonitor.Core/               Protocol, models, MQTT + BLE clients
│   ├── EcoFlowMonitor.Cli/                Diagnostic CLI (raw MQTT dump)
│   ├── EcoFlowMonitor.Platform.Windows/   WinRT BLE, toasts, Task Scheduler
│   ├── EcoFlowMonitor.Platform.macOS/     CoreBluetooth BLE, osascript, LaunchAgent
│   └── EcoFlowMonitor.Platform.Linux/     BlueZ BLE, libnotify, systemd user units
├── poc/                  Python reference implementation (protocol notes)
├── CLAUDE.md             AI-assistant context for this repo
└── README.md             You are here
```

---

## `src/` — Desktop app

.NET 10 + Avalonia UI. Runs on Windows, macOS, and Linux with a shared core and per-platform adapters (tray icon, notifications, BLE backend, power actions, autostart).

**Features:**

- **BLE channel:** ECDH SECP160r1 handshake → AES-128-CBC session → `pd335_sys.DisplayPropertyUpload` protobuf
- **MQTT channel:** Cloud broker at `mqtt.ecoflow.com:8883` using credentials fetched via the REST login flow
- **History:** SQLite-backed time series of every telemetry snapshot and power event, visible in the History view
- **Rules:** triggers (`PowerLost`, `PowerRestored`, `BatteryBelow`, `TimeRemainingBelow`) → actions (shutdown, hibernate, sleep, run script, OS notification, write log line)

**Build:**

```bash
# macOS (Apple Silicon)
dotnet build src/EcoFlowMonitor.App/EcoFlowMonitor.App.csproj -f net10.0-macos -c Debug -r osx-arm64
open src/EcoFlowMonitor.App/bin/Debug/net10.0-macos/osx-arm64/EcoFlowMonitor.App.app

# Windows
dotnet build src/EcoFlowMonitor.sln -c Release

# Linux
dotnet build src/EcoFlowMonitor.App/EcoFlowMonitor.App.csproj -f net10.0 -c Release
```

**Config:** written by the in-app Settings screen. The app does not read `.env` or environment variables.

| OS | Location |
|---|---|
| Windows | `%AppData%\EcoFlowMonitor\config.json` |
| macOS | `~/Library/Application Support/EcoFlowMonitor/config.json` |
| Linux | `~/.config/EcoFlowMonitor/config.json` |

---

## `poc/` — Python reference

Standalone scripts that document the EcoFlow REST API, MQTT protocol, and BLE protocol. Useful for verifying credentials or exploring wire formats before debugging the C# app. See [`poc/README.md`](poc/README.md) for setup (uses its own `poc/config.json`).

---

## Protocol notes

### Cloud (MQTT)

- **Auth:** REST login at `api.ecoflow.com/auth/login` returns a JWT + user ID
- **MQTT creds:** `api.ecoflow.com/iot-auth/app/certification` exchanges the JWT for MQTT username/password
- **Broker:** `mqtt.ecoflow.com:8883` (TLS, self-signed cert)
- **Topic:** `/app/device/property/{serialNumber}`
- **Encoding:** Protobuf binary with a custom outer envelope; inner payload XOR-encrypted when `encType == 1` and `src != 32` (key = `seq & 0xFF`)

### Local (BLE)

- **Advertisement:** Service UUID `0000FFF0-0000-1000-8000-00805F9B34FB`, manufacturer ID `46517`
- **GATT:** Nordic UART — notify `00000003-…`, write `00000002-…`
- **Handshake:** ECDH SECP160r1, session key derived via embedded `keydata.b64` lookup table + MD5
- **Transport:** AES-128-CBC with PKCS7 padding; frame type `0x01` for post-handshake packets
- **Auth:** `MD5(cloud_user_id + device_sn)` as ASCII-hex on `cmdSet=0x35, cmdId=0x86`
- **Data:** `pd335_sys.DisplayPropertyUpload` on `src=0x02, cmdSet=0xFE, cmdId=0x15`; payload XOR-decoded with `seq[0]` before protobuf parse

No official SDK — all of the above is from packet capture and the [`ha-ef-ble`](https://github.com/tonyswe/ha-ef-ble) Home Assistant integration.

---

## Triggers

| Trigger | Description |
|---|---|
| `PowerLost` | AC input dropped to 0 W (edge, fires once) |
| `PowerRestored` | AC input returned after power loss (edge, fires once) |
| `BatteryBelow` | Battery % below threshold (level, 5-minute cooldown) |
| `TimeRemainingBelow` | Estimated minutes remaining below threshold (level, 5-minute cooldown) |

## Actions

| Action | Windows | macOS | Linux |
|---|---|---|---|
| `Shutdown` | `shutdown.exe /s /t 0` | `osascript … shut down` | `systemctl poweroff` |
| `Hibernate` | `shutdown.exe /h` | `pmset sleepnow` | `systemctl hibernate` |
| `Sleep` | `rundll32.exe … SetSuspendState` | `pmset sleepnow` | `systemctl suspend` |
| `RunScript` | `.bat`, `.ps1`, `.exe` | shell / exec | shell / exec |
| `Notification` | Toast | `osascript display notification` | `notify-send` |
| `WriteLog` | timestamped append | timestamped append | timestamped append |

Template variables expanded in notification body and log messages: `{device}`, `{battery}`, `{remain}`, `{status}`, `{in_w}`, `{out_w}`.
