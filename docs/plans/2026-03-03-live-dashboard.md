# Live Dashboard Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add `--live` to `ecoflow_status.py` that shows a Rich terminal dashboard with real-time MQTT updates and a prominent alert when AC input power is cut off.

**Architecture:** Three additions to the existing single file: a `PowerState` dataclass tracking the three-state machine (CHARGING / IDLE / POWER_LOST), a `build_layout()` renderer that returns a Rich Panel, and a `live_mode()` function that runs a persistent MQTT loop feeding into `Rich.Live`. `main()` gets `argparse` with `--live` to branch.

**Tech Stack:** Python 3.9+, `rich` (new dep), `paho-mqtt`, existing protobuf decoder

---

### Task 1: Add rich dependency

**Files:**
- Modify: `requirements.txt`

**Step 1: Add rich to requirements.txt**

Append to `requirements.txt`:
```
rich==13.7.0
```

**Step 2: Install it**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && pip install rich==13.7.0
```

Expected: Successfully installed rich-13.7.0

**Step 3: Verify import works**

```bash
python3 -c "from rich.live import Live; from rich.panel import Panel; print('ok')"
```

Expected: `ok`

**Step 4: Commit**

```bash
git add requirements.txt && git commit -m "feat: add rich dependency for live dashboard"
```

---

### Task 2: PowerState dataclass and transition logic

**Files:**
- Modify: `ecoflow_status.py` — add after the `DECODERS` dict (around line 175)
- Modify: `tests/test_ecoflow.py`

The power state machine:
- `CHARGING`: `total_in_W >= 10`
- `IDLE`: `0 < total_in_W < 10` (e.g. USB trickle, rounding noise)
- `POWER_LOST`: `total_in_W == 0` (or key absent) **after** having been CHARGING

`update_power_state(current_state, status)` returns the new `PowerState`. It is a pure function (easy to test).

**Step 1: Write failing tests**

Append to `tests/test_ecoflow.py`:

```python
from ecoflow_status import PowerState, PowerStatus, update_power_state
import datetime

def test_power_state_charging():
    state = PowerState()
    new = update_power_state(state, {"total_in_W": 225})
    assert new.status == PowerStatus.CHARGING
    assert new.last_input_W == 225

def test_power_state_idle():
    state = PowerState()
    new = update_power_state(state, {"total_in_W": 5})
    assert new.status == PowerStatus.IDLE

def test_power_state_lost_transition():
    # Start charging
    state = update_power_state(PowerState(), {"total_in_W": 225})
    assert state.status == PowerStatus.CHARGING
    # Drop to zero
    new = update_power_state(state, {"total_in_W": 0})
    assert new.status == PowerStatus.POWER_LOST
    assert new.last_input_W == 225   # remembers what was lost
    assert new.lost_at is not None

def test_power_state_restored():
    # Build up a POWER_LOST state
    state = update_power_state(PowerState(), {"total_in_W": 200})
    state = update_power_state(state, {"total_in_W": 0})
    assert state.status == PowerStatus.POWER_LOST
    # Restore
    new = update_power_state(state, {"total_in_W": 180})
    assert new.status == PowerStatus.CHARGING
    assert new.restored_at is not None

def test_power_state_no_input_key_is_idle():
    # Status dict with no total_in_W key — treat as IDLE (not POWER_LOST)
    state = update_power_state(PowerState(), {})
    assert state.status == PowerStatus.IDLE
```

Update import line: `from ecoflow_status import ecoflow_login, get_device_sn, get_mqtt_credentials, parse_status_payload, PowerState, PowerStatus, update_power_state`

**Step 2: Run tests to verify they fail**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python -m pytest tests/test_ecoflow.py -k "power_state" -v
```

Expected: ImportError on `PowerState`

**Step 3: Implement PowerState in ecoflow_status.py**

Add these imports near the top of `ecoflow_status.py` (after existing imports):

```python
import enum
import dataclasses
from datetime import datetime
from typing import Optional
```

Then add after the `DECODERS` dict:

```python
class PowerStatus(enum.Enum):
    UNKNOWN  = "unknown"
    IDLE     = "idle"
    CHARGING = "charging"
    POWER_LOST = "power_lost"


@dataclasses.dataclass
class PowerState:
    status:      PowerStatus       = PowerStatus.UNKNOWN
    last_input_W: int              = 0
    lost_at:     Optional[datetime] = None
    restored_at: Optional[datetime] = None


def update_power_state(current: PowerState, status: dict) -> PowerState:
    """Pure function: given current state + new status dict, return next state."""
    raw = status.get("total_in_W")

    if raw is None:
        # No reading yet — stay IDLE, don't trigger POWER_LOST
        return dataclasses.replace(current, status=PowerStatus.IDLE)

    watts = int(raw)

    if watts >= 10:
        restored_at = (
            datetime.now()
            if current.status == PowerStatus.POWER_LOST
            else current.restored_at
        )
        return dataclasses.replace(
            current,
            status=PowerStatus.CHARGING,
            last_input_W=watts,
            lost_at=None,
            restored_at=restored_at,
        )

    if watts == 0 and current.status == PowerStatus.CHARGING:
        return dataclasses.replace(
            current,
            status=PowerStatus.POWER_LOST,
            lost_at=datetime.now(),
        )

    if watts == 0 and current.status == PowerStatus.POWER_LOST:
        return current  # stay in POWER_LOST, preserve timestamps

    return dataclasses.replace(current, status=PowerStatus.IDLE)
```

**Step 4: Run tests to verify they pass**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python -m pytest tests/test_ecoflow.py -k "power_state" -v
```

Expected: 5 tests PASSED

**Step 5: Run full suite**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python -m pytest tests/test_ecoflow.py -v
```

Expected: All 11 tests PASSED

**Step 6: Commit**

```bash
git add ecoflow_status.py tests/test_ecoflow.py && git commit -m "feat: PowerState machine for input power tracking"
```

---

### Task 3: build_layout() — Rich panel renderer

**Files:**
- Modify: `ecoflow_status.py` — add after `update_power_state`
- Modify: `tests/test_ecoflow.py`

`build_layout(status, power_state, device_name)` returns a Rich `Panel`. Testable because we just check that the returned object is a `Panel` and that specific strings appear in its renderable.

**Step 1: Write failing tests**

Append to `tests/test_ecoflow.py`:

```python
from rich.panel import Panel
from ecoflow_status import build_layout

def _make_status():
    return {
        "battery_pct": 68.5,
        "remain_min": 2760,
        "total_in_W": 225,
        "total_out_W": 1,
        "temp_C": 1.8,
        "voltage_V": 53.37,
        "current_A": -0.04,
        "cycles": 3,
        "soh_pct": 100,
    }

def test_build_layout_returns_panel():
    panel = build_layout(_make_status(), PowerState(), "Delta 3")
    assert isinstance(panel, Panel)

def test_build_layout_shows_battery():
    from rich.console import Console
    import io
    panel = build_layout(_make_status(), PowerState(), "Delta 3")
    buf = io.StringIO()
    console = Console(file=buf, width=80, force_terminal=False)
    console.print(panel)
    out = buf.getvalue()
    assert "68.5" in out

def test_build_layout_power_lost_shows_alert():
    from rich.console import Console
    import io
    state = update_power_state(PowerState(), {"total_in_W": 225})
    state = update_power_state(state, {"total_in_W": 0})
    status = _make_status()
    status["total_in_W"] = 0
    panel = build_layout(status, state, "Delta 3")
    buf = io.StringIO()
    console = Console(file=buf, width=80, force_terminal=False)
    console.print(panel)
    out = buf.getvalue()
    assert "POWER" in out or "LOST" in out or "CUT" in out
```

Update import line to add `build_layout`.

**Step 2: Run tests to verify they fail**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python -m pytest tests/test_ecoflow.py -k "build_layout" -v
```

Expected: ImportError on `build_layout`

**Step 3: Implement build_layout in ecoflow_status.py**

Add these imports (merge with existing):
```python
from datetime import datetime
from rich.panel   import Panel
from rich.table   import Table
from rich.text    import Text
from rich.columns import Columns
from rich         import box
```

Then add the function after `update_power_state`:

```python
def build_layout(status: dict, power_state: PowerState, device_name: str = "EcoFlow") -> Panel:
    """Render the full dashboard as a Rich Panel."""
    now = datetime.now().strftime("%H:%M:%S")
    lost   = power_state
    pstatus = power_state.status

    # ── Header / alert banner ──────────────────────────────────────────────
    if pstatus == PowerStatus.POWER_LOST:
        lost_ts = lost.lost_at.strftime("%H:%M:%S") if lost.lost_at else "?"
        header = Text(
            f"  ⚡ POWER CUT — INPUT LOST at {lost_ts}  •  was {lost.last_input_W} W  ",
            style="bold white on red",
            justify="center",
        )
        title_style = "bold red"
        title = f"⚡ {device_name}  •  [bold red]POWER LOST[/]"
    elif pstatus == PowerStatus.CHARGING:
        if lost.restored_at:
            restored_ts = lost.restored_at.strftime("%H:%M:%S")
            header = Text(
                f"  ✓ Power restored at {restored_ts}  ",
                style="bold white on green",
                justify="center",
            )
        else:
            header = Text(
                f"  ✓ Charging  ",
                style="bold white on green",
                justify="center",
            )
        title = f"⚡ {device_name}  •  [bold green]Charging[/]"
        title_style = "bold green"
    else:
        header = Text(f"  ⚡ {device_name}  ", style="bold cyan", justify="center")
        title = f"⚡ {device_name}"
        title_style = "bold cyan"

    # ── Battery panel (left column) ────────────────────────────────────────
    batt = status.get("battery_pct", 0)
    batt_color = "green" if batt > 50 else ("yellow" if batt > 20 else "red")
    arrow = "▲" if pstatus == PowerStatus.CHARGING else ("▼" if pstatus == PowerStatus.POWER_LOST else "")
    filled = int(batt / 5)
    bar    = f"[{batt_color}]{'█' * filled}[/][dim]{'░' * (20 - filled)}[/]"

    remain = status.get("remain_min")
    remain_str = f"{remain // 60}h {remain % 60}m" if remain else "—"

    left = Table.grid(padding=(0, 1))
    left.add_column(style="dim")
    left.add_column()
    left.add_row("Battery", f"[{batt_color} bold]{batt:.1f}% {arrow}[/]")
    left.add_row("",        bar)
    left.add_row("Remaining", remain_str)
    left.add_row("Voltage",   f"{status.get('voltage_V', '—')} V")
    left.add_row("Temp",      f"{status.get('temp_C', '—')} °C")
    left.add_row("Current",   f"{status.get('current_A', '—')} A")
    left.add_row("Cycles",    str(status.get("cycles", "—")))
    left.add_row("Health",    f"{status.get('soh_pct', '—')}%")

    # ── Power panel (right column) ─────────────────────────────────────────
    in_w  = status.get("total_in_W",  status.get("input_W",  0))
    out_w = status.get("total_out_W", status.get("output_W", 0))
    in_color  = "green" if in_w  >= 10 else "red"
    out_color = "yellow" if out_w > 0 else "dim"

    right = Table.grid(padding=(0, 1))
    right.add_column(style="dim")
    right.add_column()
    right.add_row("↓ AC In",    f"[{in_color} bold]{in_w} W[/]")
    right.add_row("↑ Out",      f"[{out_color}]{out_w} W[/]")
    right.add_row("", "")
    right.add_row("Updated",   now)
    right.add_row("",          "[dim]Ctrl+C to quit[/]")

    body = Columns([Panel(left, box=box.SIMPLE), Panel(right, box=box.SIMPLE)])

    content = Table.grid()
    content.add_row(header)
    content.add_row(body)

    return Panel(content, title=title, border_style=title_style, box=box.ROUNDED)
```

**Step 4: Run tests to verify they pass**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python -m pytest tests/test_ecoflow.py -k "build_layout" -v
```

Expected: 3 tests PASSED

**Step 5: Run full suite**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python -m pytest tests/test_ecoflow.py -v
```

Expected: All 14 tests PASSED

**Step 6: Commit**

```bash
git add ecoflow_status.py tests/test_ecoflow.py && git commit -m "feat: build_layout Rich renderer with power-lost alert"
```

---

### Task 4: live_mode() — persistent MQTT loop with Rich.Live

**Files:**
- Modify: `ecoflow_status.py` — add `live_mode()` after `fetch_device_status`

No unit test for `live_mode` itself (it requires real MQTT + terminal). Tested manually.

**Step 1: Add live_mode() to ecoflow_status.py**

Add this import:
```python
from rich.live import Live
```

Then add `live_mode` after `fetch_device_status`:

```python
def live_mode(creds: dict, user_id: str, sn: str, device_name: str = "EcoFlow") -> None:
    """Run a persistent MQTT loop with a live-updating Rich dashboard."""
    client_id = f"ANDROID_{str(uuid.uuid4()).upper()}_{user_id}"
    topic     = f"/app/device/property/{sn}"
    status: dict = {}
    power_state  = PowerState()
    alerted      = False

    client = mqtt.Client(client_id=client_id, protocol=mqtt.MQTTv311)
    client.username_pw_set(creds["username"], creds["password"])
    client.tls_set(cert_reqs=ssl.CERT_NONE)
    client.tls_insecure_set(True)

    layout_lock = threading.Lock()
    live_ref: list = []   # mutable container so the closure can write into it

    def on_connect(c, u, f, rc):
        if rc == 0:
            c.subscribe(topic)

    def on_message(c, u, msg):
        nonlocal power_state, alerted
        try:
            pdata, cmd_func, cmd_id, _, _ = _parse_outer(msg.payload)
            decoder = DECODERS.get((cmd_func, cmd_id))
            if not decoder:
                return
            with layout_lock:
                status.update(decoder(pdata))
                prev = power_state
                power_state = update_power_state(power_state, status)
                # Bell on transition to POWER_LOST
                if prev.status != PowerStatus.POWER_LOST and power_state.status == PowerStatus.POWER_LOST:
                    print("\a", end="", flush=True)
                if live_ref:
                    live_ref[0].update(build_layout(status, power_state, device_name))
        except Exception:
            pass

    client.on_connect = on_connect
    client.on_message = on_message
    client.connect(creds["host"], creds["port"])
    client.loop_start()

    try:
        with Live(
            build_layout(status, power_state, device_name),
            refresh_per_second=4,
            screen=True,
        ) as live:
            live_ref.append(live)
            # Block forever — MQTT loop runs in background thread
            threading.Event().wait()
    except KeyboardInterrupt:
        pass
    finally:
        client.loop_stop()
        client.disconnect()
```

**Step 2: Verify it imports cleanly**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python3 -c "from ecoflow_status import live_mode; print('ok')"
```

Expected: `ok`

**Step 3: Commit**

```bash
git add ecoflow_status.py && git commit -m "feat: live_mode persistent MQTT loop with Rich.Live"
```

---

### Task 5: Wire --live into main() and test end-to-end

**Files:**
- Modify: `ecoflow_status.py` — update `main()`

**Step 1: Update main() to add argparse and branch to live_mode**

Replace the existing `def main():` with:

```python
def main():
    import argparse
    parser = argparse.ArgumentParser(description="EcoFlow device status")
    parser.add_argument("--live", action="store_true", help="Live dashboard mode")
    args = parser.parse_args()

    load_dotenv()
    email    = os.getenv("ECOFLOW_EMAIL")
    password = os.getenv("ECOFLOW_PASSWORD")
    sn       = os.getenv("ECOFLOW_DEVICE_SN")

    if not email or not password:
        print("Error: Set ECOFLOW_EMAIL and ECOFLOW_PASSWORD in .env or environment")
        sys.exit(1)

    print("Logging in...")
    token, user_id = ecoflow_login(email, password)
    print(f"Logged in (userId: {user_id})")

    device_name = "EcoFlow"
    if not sn:
        print("Auto-discovering device SN...")
        resp = requests.get(
            f"{API_HOST}/app/user/device/list",
            headers={"lang": "en_US", "authorization": f"Bearer {token}"},
        )
        resp.raise_for_status()
        devices = resp.json().get("data", [])
        if devices:
            sn          = devices[0]["sn"]
            device_name = devices[0].get("deviceName", "EcoFlow")
            print(f"Found device: {device_name} (SN: {sn})")
    else:
        print(f"Using device SN from env: {sn}")

    print("Fetching MQTT credentials...")
    creds = get_mqtt_credentials(token, user_id)

    if args.live:
        live_mode(creds, user_id, sn, device_name)
        return

    print("Listening for telemetry (up to 30s)...")
    status = fetch_device_status(creds, user_id, sn)

    if not status:
        print("No data received — is the device powered on and connected to WiFi?")
        sys.exit(1)

    print()
    print("=== EcoFlow Status ===")
    batt = status.get("battery_pct")
    if batt is not None:
        bar = "█" * int(batt / 5) + "░" * (20 - int(batt / 5))
        print(f"  Battery:    {batt:5.1f}%  [{bar}]")
    if "remain_min" in status:
        h, m = divmod(status["remain_min"], 60)
        print(f"  Remaining:  {h}h {m}m")
    if "input_W" in status:
        print(f"  Input:      {status['input_W']} W")
    if "output_W" in status:
        print(f"  Output:     {status['output_W']} W")
    if "total_in_W" in status:
        print(f"  Total in:   {status['total_in_W']} W")
    if "total_out_W" in status:
        print(f"  Total out:  {status['total_out_W']} W")
    if "temp_C" in status:
        print(f"  Temp:       {status['temp_C']} °C")
    if "voltage_V" in status:
        print(f"  Voltage:    {status['voltage_V']} V")
    if "cycles" in status:
        print(f"  Cycles:     {status['cycles']}")
    if "soh_pct" in status:
        print(f"  Health:     {status['soh_pct']}%")
    print()
    print("Raw JSON:")
    clean = {k: v for k, v in status.items() if not isinstance(v, bytes)}
    print(json.dumps(clean, indent=2))
```

**Step 2: Run one-shot mode still works**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python ecoflow_status.py 2>&1 | head -20
```

Expected: Logs in, shows status, exits cleanly.

**Step 3: Run live mode**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python ecoflow_status.py --live
```

Expected: Full-screen Rich dashboard appears, updates every few seconds. Press Ctrl+C to exit cleanly.

**Step 4: Test power-lost alert manually**

Unplug the AC input from the Delta 3 while `--live` is running. Verify:
- Red banner appears within one MQTT cycle
- Terminal bell fires
- Timestamp of loss is shown

**Step 5: Run full test suite one last time**

```bash
cd /Users/flangvik/Documents/Tools/Ecoflow && python -m pytest tests/test_ecoflow.py -v
```

Expected: All 14 tests PASSED

**Step 6: Commit**

```bash
git add ecoflow_status.py && git commit -m "feat: --live flag wired into main with device name discovery"
```
