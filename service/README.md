# EcoFlow Monitor — Windows Tray Service

A lightweight Windows system-tray application that monitors one or more EcoFlow devices over MQTT and executes configurable actions when power events occur.

---

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8 (pre-installed on Windows 10 version 1903+ and all Windows 11 builds)
- An EcoFlow account with at least one registered device

---

## Build

```
dotnet build EcoFlowMonitor.sln
```

Or open `EcoFlowMonitor.sln` in Visual Studio 2022 and press **Build > Build Solution** (F6).

The output binary is placed in `EcoFlowMonitor\bin\Debug\net48\EcoFlowMonitor.exe` (or `Release` for release builds).

---

## First Run

1. Launch `EcoFlowMonitor.exe`. A tray icon appears in the notification area.
2. Right-click the tray icon and select **Open Settings**.
3. On the **Devices** tab, click **+ Add** to add a device.
4. Enter your EcoFlow account **Email** and **Password**.
5. Optionally enter the device **Serial Number** — leave blank to auto-detect the first device on the account.
6. Click **Save**.
7. Restart the application. It will log in, fetch MQTT credentials, and begin monitoring.

---

## Tray Icon States

| Icon | Meaning |
|------|---------|
| Green circle | Charging — AC input is present and charging the battery |
| Gray circle | Idle — running on battery, no AC input |
| Red circle | Power lost — AC input dropped (was previously charging) |
| Dark gray circle | Disconnected or unknown — no data received yet |

### Tooltip format

```
EcoFlow Monitor — 87% | 230W in / 45W out
```

---

## Rules

Each device can have any number of rules. A rule fires when its trigger condition is met and executes its actions in order.

### Triggers

| Trigger | Description | Threshold |
|---------|-------------|-----------|
| `PowerLost` | AC input drops to zero after having been present | — |
| `PowerRestored` | AC input returns after a power-lost event | — |
| `BatteryBelow` | Battery percentage falls below threshold | 0–100 % |
| `TimeRemainingBelow` | Estimated runtime falls below threshold | 0–1440 minutes |

### Actions

| Action | Description |
|--------|-------------|
| `RunScript` | Execute a script or executable (`.exe`, `.cmd`, `.bat`, `.ps1`) |
| `Shutdown` | Initiate a Windows shutdown (`shutdown /s /t 30`) |
| `Hibernate` | Hibernate the system |
| `Sleep` | Put the system to sleep |
| `Notification` | Show a Windows balloon tip / toast notification |
| `WriteLog` | Append a timestamped line to a log file |

### Template variables

The following placeholders are expanded in **Notification** body text and **WriteLog** messages:

| Variable | Value |
|----------|-------|
| `{device}` | Device display name |
| `{battery}` | Current battery percentage |
| `{remain}` | Estimated runtime remaining in minutes |
| `{status}` | Power status (Charging / Idle / PowerLost / Unknown) |
| `{in_w}` | Total AC input power in watts |
| `{out_w}` | Total output load in watts |

Example message:

```
Power lost on {device}! Battery at {battery}% ({remain} min remaining).
```

---

## Configuration

Settings are stored at:

```
%AppData%\EcoFlowMonitor\config.json
```

The file is written automatically when you click **Save** in the Settings form. You can edit it by hand if needed — the application reads it fresh on each startup.

---

## Start with Windows

Enable the **Start with Windows** checkbox on the **General** tab. This writes the following registry value:

```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
  EcoFlowMonitor = "C:\path\to\EcoFlowMonitor.exe" --minimized
```

The `--minimized` flag skips any startup window and goes straight to the tray.

To disable, uncheck the box and click **Save**, or delete the registry value manually.

---

## Single Instance

Only one copy of EcoFlow Monitor may run at a time. If you attempt to launch a second instance, a message box informs you that the application is already running in the system tray.

This is enforced via a named Windows Mutex (`EcoFlowMonitor_SingleInstance`).
