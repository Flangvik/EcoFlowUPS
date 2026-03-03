# ecoflow_status.py
import argparse
import base64
import dataclasses
import enum
import json
import os
import ssl
import struct
import sys
import threading
import uuid
from datetime import datetime
from typing import Optional

from pathlib import Path

import paho.mqtt.client as mqtt
import requests

from rich.panel   import Panel
from rich.table   import Table
from rich.text    import Text
from rich.columns import Columns
from rich.live    import Live
from rich         import box

API_HOST = "https://api.ecoflow.com"


# ---------------------------------------------------------------------------
# REST helpers
# ---------------------------------------------------------------------------

def ecoflow_login(email: str, password: str) -> tuple[str, str]:
    """Login to EcoFlow client API. Returns (token, user_id)."""
    encoded_password = base64.b64encode(password.encode()).decode()
    resp = requests.post(
        f"{API_HOST}/auth/login",
        headers={"lang": "en_US", "content-type": "application/json"},
        json={
            "email": email,
            "password": encoded_password,
            "scene": "IOT_APP",
            "userType": "ECOFLOW",
        },
    )
    resp.raise_for_status()
    data = resp.json()
    if data.get("code") != "0":
        raise RuntimeError(f"Login failed: {data.get('message')}")
    return data["data"]["token"], data["data"]["user"]["userId"]


def get_device_sn(token: str) -> str:
    """Fetch device list and return the first device's serial number."""
    resp = requests.get(
        f"{API_HOST}/app/user/device/list",
        headers={"lang": "en_US", "authorization": f"Bearer {token}"},
    )
    resp.raise_for_status()
    data = resp.json()
    devices = data.get("data", [])
    if not devices:
        raise RuntimeError("No devices found on this account")
    sn = devices[0]["sn"]
    name = devices[0].get("deviceName", "Unknown")
    print(f"Found device: {name} (SN: {sn})")
    return sn


def get_mqtt_credentials(token: str, user_id: str) -> dict:
    """Fetch MQTT broker credentials from EcoFlow cert endpoint."""
    resp = requests.get(
        f"{API_HOST}/iot-auth/app/certification",
        headers={"lang": "en_US", "authorization": f"Bearer {token}"},
        params={"userId": user_id},
    )
    resp.raise_for_status()
    data = resp.json()
    if data.get("code") != "0":
        raise RuntimeError(f"Failed to get MQTT credentials: {data.get('message')}")
    d = data["data"]
    return {
        "host": d["url"],
        "port": int(d["port"]),
        "username": d["certificateAccount"],
        "password": d["certificatePassword"],
    }


# ---------------------------------------------------------------------------
# Protobuf decoder (no external library needed)
# ---------------------------------------------------------------------------

def _read_varint(buf: bytes, i: int) -> tuple[int, int]:
    shift = 0
    val = 0
    while True:
        b = buf[i]; i += 1
        val |= (b & 0x7F) << shift
        if not (b & 0x80):
            return val, i
        shift += 7


def _zigzag(n: int) -> int:
    return (n >> 1) ^ -(n & 1)


def _decode_fields(buf: bytes) -> dict:
    """Raw protobuf decode: returns {field_number: [values...]}."""
    fields: dict = {}
    i = 0
    while i < len(buf):
        tag, i = _read_varint(buf, i)
        field = tag >> 3
        wtype = tag & 0x7
        if wtype == 0:
            val, i = _read_varint(buf, i)
            fields.setdefault(field, []).append(val)
        elif wtype == 1:
            i += 8
        elif wtype == 2:
            ln, i = _read_varint(buf, i)
            fields.setdefault(field, []).append(buf[i:i + ln])
            i += ln
        elif wtype == 5:
            fields.setdefault(field, []).append(buf[i:i + 4])
            i += 4
        else:
            break
    return fields


def _parse_outer(raw: bytes) -> tuple[bytes, int, int, int, int]:
    """Parse the EcoFlow HeaderMessage wrapper.

    Returns (pdata, cmd_func, cmd_id, enc_type, seq).
    """
    f = _decode_fields(raw)
    # Outer is a repeated Header at field 1; take first entry
    header_bytes = f.get(1, [b""])[0]
    h = _decode_fields(header_bytes)
    pdata   = h.get(1,  [b""])[0]   # bytes
    enc_type = h.get(6,  [0])[0]
    cmd_func = h.get(8,  [0])[0]
    cmd_id   = h.get(9,  [0])[0]
    seq      = h.get(14, [0])[0]
    src      = h.get(2,  [0])[0]
    if enc_type == 1 and src != 32:
        key   = seq & 0xFF
        pdata = bytes(b ^ key for b in pdata)
    return pdata, cmd_func, cmd_id, enc_type, seq


def _decode_bms(pdata: bytes) -> dict:
    """Decode BMSHeartBeatReport (cmdFunc=32, cmdId=50)."""
    f = _decode_fields(pdata)
    result = {}

    def u(field):
        vals = f.get(field)
        return vals[0] if vals else None

    def s(field):
        v = u(field)
        if v is None:
            return None
        # protobuf int32/int64 negative values are two's complement, not zigzag
        return v if v < 2**63 else v - 2**64

    def flt(field):
        vals = f.get(field)
        if vals and isinstance(vals[0], bytes) and len(vals[0]) == 4:
            return round(struct.unpack("<f", vals[0])[0], 2)
        return None

    def txt(field):
        vals = f.get(field)
        if vals and isinstance(vals[0], bytes):
            return vals[0].decode("utf-8", errors="replace").rstrip("\x00")
        return None

    soc = flt(25) or u(6)
    if soc is not None:
        result["battery_pct"] = round(float(soc), 1)

    vol = u(7)
    if vol is not None:
        result["voltage_V"] = round(vol / 1000, 2)

    amp = s(8)
    if amp is not None:
        result["current_A"] = round(amp / 1000, 2)

    temp = s(9)
    if temp is not None:
        result["temp_C"] = round(temp / 10, 1)

    remain = u(28)
    if remain is not None:
        result["remain_min"] = remain

    result["output_W"] = u(27)
    result["input_W"]  = u(26)
    result["cycles"]   = u(14)
    result["soh_pct"]  = u(15)

    cap_remain = u(12)
    cap_full   = u(13)
    if cap_remain and cap_full:
        result["remain_Wh"] = round(cap_remain * (vol or 50000) / 1000 / 1000, 0)

    result["hw_ver"]   = txt(36)
    result["pack_sn"]  = txt(81)

    return {k: v for k, v in result.items() if v is not None}


def _decode_display(pdata: bytes) -> dict:
    """Decode DisplayPropertyUpload (cmdFunc=254, cmdId=21)."""
    f = _decode_fields(pdata)

    def watts(field):
        vals = f.get(field)
        if not vals:
            return None
        v = vals[0]
        if isinstance(v, bytes) and len(v) == 4:
            return round(struct.unpack("<f", v)[0])
        return int(v)

    result = {}
    pw_in  = watts(3)
    pw_out = watts(4)
    if pw_in  is not None: result["total_in_W"]  = pw_in
    if pw_out is not None: result["total_out_W"] = pw_out
    ac_in  = watts(54)
    if ac_in  is not None: result["ac_in_W"]  = ac_in
    return result


DECODERS = {
    (32,  50): _decode_bms,
    (254, 21): _decode_display,
    (254, 22): _decode_display,
}


# ---------------------------------------------------------------------------
# Power state machine
# ---------------------------------------------------------------------------

class PowerStatus(enum.Enum):
    UNKNOWN    = "unknown"
    IDLE       = "idle"
    CHARGING   = "charging"
    POWER_LOST = "power_lost"


@dataclasses.dataclass
class PowerState:
    status:       PowerStatus        = PowerStatus.UNKNOWN
    last_input_W: int                = 0
    lost_at:      Optional[datetime] = None
    restored_at:  Optional[datetime] = None


def update_power_state(current: PowerState, status: dict) -> PowerState:
    """Pure function: given current state + new status dict, return next state."""
    raw = status.get("total_in_W")

    if raw is None:
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
        return current

    return dataclasses.replace(current, status=PowerStatus.IDLE)


def build_layout(status: dict, power_state: PowerState, device_name: str = "EcoFlow") -> Panel:
    """Render the full dashboard as a Rich Panel."""
    now     = datetime.now().strftime("%H:%M:%S")
    pstatus = power_state.status

    # Header / alert banner
    if pstatus == PowerStatus.POWER_LOST:
        lost_ts = power_state.lost_at.strftime("%H:%M:%S") if power_state.lost_at else "?"
        header  = Text(
            f"  ⚡ POWER CUT — INPUT LOST at {lost_ts}  •  was {power_state.last_input_W} W  ",
            style="bold white on red", justify="center",
        )
        title = f"⚡ {device_name}  •  [bold red]POWER LOST[/]"
        border_style = "red"
    elif pstatus == PowerStatus.CHARGING:
        if power_state.restored_at:
            ts     = power_state.restored_at.strftime("%H:%M:%S")
            header = Text(f"  ✓ Power restored at {ts}  ", style="bold white on green", justify="center")
        else:
            header = Text(f"  ✓ Charging  ", style="bold white on green", justify="center")
        title        = f"⚡ {device_name}  •  [bold green]Charging[/]"
        border_style = "green"
    else:
        header       = Text(f"  ⚡ {device_name}  ", style="bold cyan", justify="center")
        title        = f"⚡ {device_name}"
        border_style = "cyan"

    # Battery column
    batt  = float(status.get("battery_pct", 0))
    color = "green" if batt > 50 else ("yellow" if batt > 20 else "red")
    arrow = "▲" if pstatus == PowerStatus.CHARGING else ("▼" if pstatus == PowerStatus.POWER_LOST else "")
    bar   = f"[{color}]{'█' * int(batt / 5)}[/][dim]{'░' * (20 - int(batt / 5))}[/]"

    remain = status.get("remain_min")
    remain_str = f"{remain // 60}h {remain % 60}m" if remain else "—"

    left = Table.grid(padding=(0, 1))
    left.add_column(style="dim")
    left.add_column()
    left.add_row("Battery",   f"[{color} bold]{batt:.1f}% {arrow}[/]")
    left.add_row("",          bar)
    left.add_row("Remaining", remain_str)
    left.add_row("Voltage",   f"{status.get('voltage_V', '—')} V")
    left.add_row("Temp",      f"{status.get('temp_C', '—')} °C")
    left.add_row("Current",   f"{status.get('current_A', '—')} A")
    left.add_row("Cycles",    str(status.get("cycles", "—")))
    left.add_row("Health",    f"{status.get('soh_pct', '—')}%")

    # Power column
    in_w  = int(status.get("total_in_W",  status.get("input_W",  0)))
    out_w = int(status.get("total_out_W", status.get("output_W", 0)))
    in_c  = "green" if in_w >= 10 else "red"
    out_c = "yellow" if out_w > 0 else "dim"

    right = Table.grid(padding=(0, 1))
    right.add_column(style="dim")
    right.add_column()
    right.add_row("↓ AC In",  f"[{in_c} bold]{in_w} W[/]")
    right.add_row("↑ Out",    f"[{out_c}]{out_w} W[/]")
    right.add_row("", "")
    right.add_row("Updated",  now)
    right.add_row("",         "[dim]Ctrl+C to quit[/]")

    body    = Columns([Panel(left, box=box.SIMPLE), Panel(right, box=box.SIMPLE)])
    content = Table.grid()
    content.add_row(header)
    content.add_row(body)

    return Panel(content, title=title, border_style=border_style, box=box.ROUNDED)


def parse_status_payload(raw: bytes) -> dict:
    """Extract the params dict from an EcoFlow MQTT message payload."""
    data = json.loads(raw)
    return data.get("params", data)


# ---------------------------------------------------------------------------
# MQTT subscriber
# ---------------------------------------------------------------------------

def fetch_device_status(creds: dict, user_id: str, sn: str, timeout: int = 30) -> dict:
    """Subscribe to device topic, collect protobuf messages for up to timeout seconds,
    decode and merge all fields, return a clean status dict."""
    client_id = f"ANDROID_{str(uuid.uuid4()).upper()}_{user_id}"
    topic     = f"/app/device/property/{sn}"
    merged: dict = {}
    deadline  = threading.Event()

    client = mqtt.Client(client_id=client_id, protocol=mqtt.MQTTv311)
    client.username_pw_set(creds["username"], creds["password"])
    client.tls_set(cert_reqs=ssl.CERT_NONE)
    client.tls_insecure_set(True)

    def on_connect(c, u, f, rc):
        if rc == 0:
            c.subscribe(topic)

    def on_message(c, u, msg):
        try:
            pdata, cmd_func, cmd_id, _, _ = _parse_outer(msg.payload)
            decoder = DECODERS.get((cmd_func, cmd_id))
            if decoder:
                merged.update(decoder(pdata))
                # Stop as soon as we have the BMS heartbeat (most complete)
                if (cmd_func, cmd_id) == (32, 50) and "battery_pct" in merged:
                    deadline.set()
        except Exception:
            pass

    client.on_connect = on_connect
    client.on_message = on_message
    client.connect(creds["host"], creds["port"])
    client.loop_start()
    deadline.wait(timeout=timeout)
    client.loop_stop()
    client.disconnect()
    return merged


def live_mode(creds: dict, user_id: str, sn: str, device_name: str = "EcoFlow") -> None:
    """Run a persistent MQTT loop with a live-updating Rich dashboard."""
    client_id = f"ANDROID_{str(uuid.uuid4()).upper()}_{user_id}"
    topic     = f"/app/device/property/{sn}"
    status: dict = {}
    power_state  = PowerState()
    lock         = threading.Lock()
    live_handle: Optional[Live] = None

    client = mqtt.Client(client_id=client_id, protocol=mqtt.MQTTv311)
    client.username_pw_set(creds["username"], creds["password"])
    client.tls_set(cert_reqs=ssl.CERT_NONE)
    client.tls_insecure_set(True)

    def on_connect(c, u, f, rc):
        if rc == 0:
            c.subscribe(topic)

    def on_message(c, u, msg):
        nonlocal power_state, live_handle
        try:
            pdata, cmd_func, cmd_id, _, _ = _parse_outer(msg.payload)
            decoder = DECODERS.get((cmd_func, cmd_id))
            if not decoder:
                return
            with lock:
                status.update(decoder(pdata))
                prev = power_state
                power_state = update_power_state(power_state, status)
                if prev.status != PowerStatus.POWER_LOST and power_state.status == PowerStatus.POWER_LOST:
                    print("\a", end="", flush=True)
                if live_handle is not None:
                    live_handle.update(build_layout(status, power_state, device_name))
        except Exception as exc:
            with lock:
                status["_error"] = str(exc)

    client.on_connect = on_connect
    client.on_message = on_message
    try:
        client.connect(creds["host"], creds["port"])
    except Exception as exc:
        raise RuntimeError(
            f"Failed to connect to MQTT broker {creds['host']}:{creds['port']}: {exc}"
        ) from exc
    client.loop_start()

    try:
        with lock:
            initial_layout = build_layout(status, power_state, device_name)
        with Live(initial_layout, refresh_per_second=4, screen=True) as live:
            with lock:
                live_handle = live
            threading.Event().wait()
    except KeyboardInterrupt:
        pass
    finally:
        client.loop_stop()
        client.disconnect()


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="EcoFlow device status")
    parser.add_argument("--live", action="store_true", help="Live dashboard mode")
    args = parser.parse_args()

    config_path = Path(__file__).parent / "config.json"
    if not config_path.exists():
        print(f"Error: config.json not found at {config_path}")
        print("Copy config.json.example to config.json and fill in your credentials.")
        sys.exit(1)
    with config_path.open() as f:
        cfg = json.load(f)

    email    = cfg.get("email")
    password = cfg.get("password")
    sn       = cfg.get("device_sn")

    if not email or not password:
        print("Error: config.json must contain 'email' and 'password'")
        sys.exit(1)

    print("Logging in...")
    try:
        token, user_id = ecoflow_login(email, password)
    except (RuntimeError, requests.HTTPError) as exc:
        print(f"Error: {exc}")
        sys.exit(1)
    print(f"Logged in (userId: {user_id})")

    device_name = "EcoFlow"
    if not sn:
        print("Auto-discovering device SN...")
        try:
            resp = requests.get(
                f"{API_HOST}/app/user/device/list",
                headers={"lang": "en_US", "authorization": f"Bearer {token}"},
            )
            resp.raise_for_status()
        except requests.HTTPError as exc:
            print(f"Error: Device discovery failed: {exc}")
            sys.exit(1)
        devices = resp.json().get("data", [])
        if not devices:
            print("Error: No devices found on this account")
            sys.exit(1)
        sn          = devices[0]["sn"]
        device_name = devices[0].get("deviceName", "EcoFlow")
        print(f"Found device: {device_name} (SN: {sn})")
    else:
        print(f"Using device SN from env: {sn}")

    print("Fetching MQTT credentials...")
    try:
        creds = get_mqtt_credentials(token, user_id)
    except (RuntimeError, requests.HTTPError) as exc:
        print(f"Error: {exc}")
        sys.exit(1)

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


if __name__ == "__main__":
    main()
