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
- **Automation:** full rules engine, cross-platform. In-app editor on the dashboard — **+ Add Rule** / **Rule History** buttons at the top-right of each device card; per-rule **Test**, **Edit**, **Delete** controls on the list below. Triggers + actions are summarised in the tables further down.
- **Audit log:** every rule firing and per-action outcome persisted in `history.db`, viewable via the **Rule History** button. 30-day retention by default; configurable.
- **Concurrency cap:** actions dispatch through a bounded channel with a user-configurable limit (default 8, range 1–64) so runaway scripts or hung webhooks can't block telemetry ingestion.

**Build a local release:**

Helper scripts in [`scripts/`](scripts/) produce a self-contained, ready-to-ship binary for each OS. They mirror exactly what CI does.

```bash
# macOS (host arch auto-detected; produces signed .app + zip in dist/)
./scripts/build-macos.sh 1.0.0

# Linux (produces single-file ELF + tar.gz in dist/)
./scripts/build-linux.sh 1.0.0

# Windows (PowerShell — produces single-file .exe + zip in dist/)
./scripts/build-windows.ps1 -Version 1.0.0
```

Pass just `./scripts/build-macos.sh` (no args) for a `0.0.0-local` dev build.

**Quick iteration (no packaging):**

```bash
# macOS (Apple Silicon) — Debug build, launch in place
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

Edge triggers fire once per state transition; level triggers fire while the condition holds, throttled by a per-rule cooldown (default 300 s). All cooldowns are editable per rule.

| Trigger | Kind | Parameter | Description |
|---|---|---|---|
| `PowerLost` | edge | — | Station transitions into PowerLost (AC disappeared). |
| `PowerRestored` | edge | — | Station transitions out of PowerLost back to Charging. |
| `AcPlugged` | edge | — | AC cable plugged into the station (false → true on `AcPluggedIn`). |
| `AcUnplugged` | edge | — | AC cable unplugged from the station. |
| `DeviceOnline` | edge | — | First telemetry after a `DeviceOffline` gap resolves. |
| `BatteryBelow` | level | `%` (0–100) | Battery percentage below threshold. |
| `BatteryAbove` | level | `%` (0–100) | Battery percentage above threshold. |
| `TimeRemainingBelow` | level | `min` (1–1440) | Device-reported minutes-to-empty below threshold. |
| `TempAbove` | level | `°C` (decimal) | BMS temperature above threshold. |
| `TempBelow` | level | `°C` (decimal) | BMS temperature below threshold. |
| `InputWattsBelow` | level | `W` | Total input watts below threshold (handy for solar drop-off). |
| `OutputWattsAbove` | level | `W` | Total output watts above threshold (heavy-load alert). |
| `DeviceOffline` | edge | `sec` (30–86400) | No telemetry on any channel for the configured window (default 300 s). |

## Actions

Actions dispatch through a bounded concurrency queue (default 8 in-flight). Each attempt lands in the audit log with its outcome — success / failure / skipped / timeout / dropped.

| Action | Windows | macOS | Linux |
|---|---|---|---|
| `Shutdown` | `shutdown.exe /s /t 0` | `osascript … shut down` | `systemctl poweroff` |
| `Hibernate` | `shutdown.exe /h` | `pmset sleepnow` | `systemctl hibernate` |
| `Sleep` | `rundll32.exe … SetSuspendState` | `pmset sleepnow` | `systemctl suspend` |
| `RunScript` | `.bat`, `.ps1`, `.exe` | shell / exec | shell / exec |
| `RunCommand` | cmd.exe / `pwsh` (per-action shell picker) | `/bin/sh -c "…"` | `/bin/sh -c "…"` |
| `Webhook` | HTTP POST/PUT with user headers + optional body template; per-action timeout + configurable retries (0–5) | same | same |
| `Notification` | Toast | `osascript display notification` | `notify-send` |
| `WriteLog` | timestamped append | timestamped append | timestamped append |

**`RunCommand`** takes per-OS command strings (`commandWindows` / `commandMacOS` / `commandLinux`) so one rule runs the right thing on whichever host the app is launched on. If the field for the current OS is empty, the action is marked `skipped` (no failure, no crash).

**`Webhook`**: `Authorization` and `X-*-Token` / `X-*-Secret` / `X-*-Key` headers are redacted in the audit log; URL and body template are stored verbatim (keep secrets in headers, not in URLs).

**Template variables** — expanded in any templated field (notification title/body, log message, webhook body, RunCommand arguments). Unknown / missing values expand to `<unknown>` (`?` for legacy variables, preserved for backward compatibility).

| Variable | Source |
|---|---|
| `{device}` | Device display name |
| `{device_sn}` | Device serial number |
| `{battery}` | Battery percent (1 decimal, InvariantCulture) |
| `{remain}` | Estimated runtime remaining (`Xh Ym`) |
| `{status}` | `Charging` / `Idle` / `PowerLost` / `Unknown` |
| `{in_w}` | Total input watts |
| `{out_w}` | Total output watts |
| `{temp_c}` | BMS temperature °C (1 decimal, InvariantCulture) |
| `{ac_plugged}` | `true` / `false` — AC plugged-in state |
| `{charge_state}` | Raw EMS charge-state integer |
