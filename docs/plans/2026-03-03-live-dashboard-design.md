# EcoFlow Live Dashboard Design

## Goal
Add `--live` flag to `ecoflow_status.py` that launches a Rich-powered terminal dashboard with real-time MQTT updates and a prominent alert when AC input power is cut off.

## Architecture
Single-file addition to `ecoflow_status.py`. A persistent MQTT loop feeds updates into a shared `status` dict. Rich's `Live` context manager re-renders a layout on every update. A power state machine tracks transitions and drives the alert banner.

## Power State Machine
Three states: `CHARGING` (total_in_W >= 10), `IDLE` (0 < total_in_W < 10), `POWER_LOST` (total_in_W == 0 or absent after having been charging).

On `CHARGING → POWER_LOST`:
- Show full-width red banner with timestamp and last known wattage
- Ring terminal bell (`\a`)

On `POWER_LOST → CHARGING`:
- Show green "Restored" flash with timestamp

## Layout (Rich)
```
┌─────────────────────────────────────────────────────┐
│           ⚡ EcoFlow Delta 3  •  Charging  ✓        │  ← normal
├──────────────────────┬──────────────────────────────┤
│  Battery  68.5% ▲    │  ↓ AC In     225 W  (green)  │
│  ████████████░░░░░░  │  ↑ Out         1 W           │
│  Remaining: 46h 0m   │  Temp  1.8°C  Volt 53.37V   │
└──────────────────────┴──────────────────────────────┘

┌─────────────────────────────────────────────────────┐
│  ████  POWER CUT — INPUT LOST at 14:23:45  ████     │  ← alert state
│  Was charging at 225W                               │
├──────────────────────┬──────────────────────────────┤
│  Battery  68.5% ▼    │  ↓ AC In       0 W  (red)   │
└──────────────────────┴──────────────────────────────┘
```

## Components
- `PowerState` dataclass: current state enum, last_input_W, lost_at timestamp, restored_at timestamp
- `build_layout(status, power_state)` → Rich `Panel`: renders full dashboard
- `live_mode(creds, user_id, sn)`: sets up `Rich.Live`, persistent MQTT loop, calls `build_layout` on each message
- `main()`: add `argparse` with `--live` flag; branch to `live_mode` or existing one-shot

## Dependencies
Add `rich` to `requirements.txt`.
