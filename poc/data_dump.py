# -*- coding: utf-8 -*-
"""
data_dump.py - Full live decode of every MQTT message from the EcoFlow device.

Run for ~60 s and pipe / redirect to a file to review:
    python data_dump.py | tee dump.txt

Reads credentials from poc/config.json  (email / password / device_sn).
"""
import json, base64, ssl, uuid, struct, sys, time
from pathlib import Path
import requests
import paho.mqtt.client as mqtt

# ── Config ──────────────────────────────────────────────────────────────────
CFG_PATH = Path(__file__).parent / "config.json"
with open(CFG_PATH) as f:
    cfg = json.load(f)

EMAIL = cfg["email"]
PASS  = cfg["password"]
SN    = cfg["device_sn"]
API   = "https://api.ecoflow.com"

# ── Protobuf helpers ─────────────────────────────────────────────────────────
def read_varint(buf, i):
    shift = 0; val = 0
    while True:
        b = buf[i]; i += 1
        val |= (b & 0x7F) << shift
        if not (b & 0x80): return val, i
        shift += 7

def zigzag(n):
    return (n >> 1) ^ -(n & 1)

def decode_fields(buf):
    """Return {field_num: [values]}  where each value is int | bytes."""
    out = {}; i = 0
    while i < len(buf):
        try:
            tag, i = read_varint(buf, i)
        except Exception:
            break
        field = tag >> 3; wtype = tag & 7
        if field not in out: out[field] = []
        if wtype == 0:
            v, i = read_varint(buf, i); out[field].append(v)
        elif wtype == 1:
            i += 8  # skip 64-bit fixed
        elif wtype == 2:
            ln, i = read_varint(buf, i)
            out[field].append(buf[i:i+ln]); i += ln
        elif wtype == 5:
            out[field].append(buf[i:i+4]); i += 4
        else:
            break  # unknown wire type
    return out

def parse_outer(raw):
    """Return (pdata, cmd_func, cmd_id)."""
    outer = decode_fields(raw)
    hdr_bytes = outer.get(1, [b""])[0]
    h = decode_fields(hdr_bytes)
    pdata   = h.get(1,  [b""])[0]
    enc     = int(h.get(6,  [0])[0])
    src     = int(h.get(2,  [0])[0])
    seq     = int(h.get(14, [0])[0])
    cmd_func = int(h.get(8, [0])[0])
    cmd_id   = int(h.get(9, [0])[0])
    if enc == 1 and src != 32:
        key = seq & 0xFF
        pdata = bytes(b ^ key for b in pdata)
    return pdata, cmd_func, cmd_id

# ── Field schemas ────────────────────────────────────────────────────────────
#  Each entry: field_num -> (label, type)
#  type: 'u' unsigned, 's' signed (zigzag), 'f' float32 blob,
#        'str' utf-8 blob, 'mV' milli-volt (/1000→V), 'mA' (/1000→A),
#        'dC' deci-Celsius (/10→°C), 'rvaru' repeated-packed unsigned,
#        'rvars' repeated-packed signed deci-Celsius

BMS_SCHEMA = {
    1:  ("num",                  "u"),
    2:  ("type",                 "u"),
    3:  ("cell_id",              "u"),
    4:  ("err_code",             "u"),
    5:  ("sys_ver",              "u"),
    6:  ("soc_%",                "u"),
    7:  ("voltage",              "mV"),
    8:  ("current",              "mA"),
    9:  ("temp",                 "dC"),
    10: ("open_bms_flag",        "u"),
    11: ("design_cap_mAh",       "u"),
    12: ("remain_cap_mAh",       "u"),
    13: ("full_cap_mAh",         "u"),
    14: ("cycles",               "u"),
    15: ("soh_%",                "u"),
    16: ("max_cell_mV",          "u"),
    17: ("min_cell_mV",          "u"),
    18: ("max_cell_temp",        "dC"),
    19: ("min_cell_temp",        "dC"),
    20: ("max_mos_temp",         "dC"),
    21: ("min_mos_temp",         "dC"),
    22: ("bms_fault",            "u"),
    23: ("bq_sys_stat_reg",      "u"),
    24: ("tag_chg_amp_mA",       "u"),
    25: ("f32_show_soc",         "f"),
    26: ("input_watts",          "u"),
    27: ("output_watts",         "u"),
    28: ("remain_time_min",      "u"),
    29: ("mos_state",            "u"),
    30: ("balance_state",        "u"),
    31: ("max_vol_diff_mV",      "u"),
    32: ("cell_series_num",      "u"),
    33: ("cell_vol[]_mV",        "rvaru"),
    34: ("cell_ntc_num",         "u"),
    35: ("cell_temp[]",          "rvars"),
    36: ("hw_ver",               "str"),
    37: ("heartbeat_ver",        "u"),
    38: ("ecloud_ocv",           "u"),
    39: ("bms_sn",               "str"),
    40: ("product_type",         "u"),
    41: ("product_detail",       "u"),
    42: ("act_soc",              "f"),
    43: ("diff_soc",             "f"),
    44: ("target_soc",           "f"),
    45: ("sys_loader_ver",       "u"),
    46: ("sys_state",            "u"),
    47: ("chg_dsg_state",        "u"),
    48: ("all_err_code",         "u"),
    49: ("all_bms_fault",        "u"),
    50: ("accu_chg_cap_Ah",      "u"),
    51: ("accu_dsg_cap_Ah",      "u"),
    52: ("real_soh",             "f"),
    53: ("calendar_soh",         "f"),
    54: ("cycle_soh",            "f"),
    55: ("mos_ntc_num",          "u"),
    56: ("mos_temp[]",           "rvars"),
    57: ("env_ntc_num",          "u"),
    58: ("env_temp[]",           "rvars"),
    63: ("max_env_temp",         "dC"),
    64: ("min_env_temp",         "dC"),
    69: ("balance_cmd",          "u"),
    71: ("afe_sys_status",       "u"),
    72: ("mcu_pin_in_status",    "u"),
    73: ("mcu_pin_out_status",   "u"),
    74: ("bms_alarm_state1",     "u"),
    75: ("bms_alarm_state2",     "u"),
    76: ("bms_protect_state1",   "u"),
    77: ("bms_protect_state2",   "u"),
    78: ("bms_fault_state",      "u"),
    79: ("accu_chg_energy_Wh",   "u"),
    80: ("accu_dsg_energy_Wh",   "u"),
    81: ("pack_sn",              "str"),
    82: ("water_in_flag",        "u"),
}

DISPLAY_SCHEMA = {
    1:  ("errcode",              "u"),
    3:  ("total_in_W",           "f"),
    4:  ("total_out_W",          "f"),
    5:  ("lcd_light",            "u"),
    6:  ("energy_backup_state",  "u"),
    9:  ("usb_a1_W",             "u"),
    10: ("usb_a2_W",             "u"),
    11: ("usbc1_W",              "u"),
    12: ("usbc2_W",              "u"),
    17: ("dev_standby_min",      "u"),
    18: ("screen_off_min",       "u"),
    19: ("ac_standby_min",       "u"),
    20: ("dc_standby_min",       "u"),
    30: ("pcs_fan_level",        "u"),
    35: ("solar_in_high_W",      "f"),
    36: ("solar_in_low_W",       "f"),
    47: ("flow_ac_in",           "u"),
    48: ("flow_ac_hv_out",       "u"),
    49: ("flow_ac_lv_out",       "u"),
    52: ("llc_W",                "f"),
    53: ("ac_W",                 "f"),
    54: ("ac_in_W",              "f"),
    55: ("ac_hv_out_W",          "f"),
    56: ("ac_lv_out_W",          "f"),
    61: ("ac_plugged_in",        "u"),
    62: ("ac_in_freq_Hz",        "u"),
}

EMS_V1P0_SCHEMA = {
    1:  ("chg_state",            "u"),
    2:  ("chg_cmd",              "u"),
    3:  ("dsg_cmd",              "u"),
    4:  ("chg_vol",              "u"),
    5:  ("chg_amp",              "u"),
    6:  ("fan_level",            "u"),
    7:  ("max_charge_soc",       "u"),
    8:  ("bms_model",            "u"),
    9:  ("lcd_show_soc",         "u"),
    10: ("ups_mode",             "u"),
    11: ("bms_warning_state",    "u"),
    12: ("chg_remain_min",       "u"),
    13: ("dsg_remain_min",       "u"),
    14: ("ems_normal_flag",      "u"),
    15: ("f32_lcd_soc",          "f"),
    16: ("bms_connected[]",      "rvaru"),
    17: ("max_available_num",    "u"),
    18: ("open_bms_idx",         "u"),
}

EMS_V1P3_SCHEMA = {
    1: ("chg_disable_cond",      "u"),
    2: ("dsg_disable_cond",      "u"),
    3: ("chg_line_plugged",      "u"),
    4: ("sys_chg_dsg_state",     "u"),
    5: ("ems_heartbeat_ver",     "u"),
}

CMS_SCHEMA = {
    1: ("ems_v1p0",  "sub", EMS_V1P0_SCHEMA),
    2: ("ems_v1p3",  "sub", EMS_V1P3_SCHEMA),
}

def decode_repeated_varints(data, signed=False):
    vals = []; i = 0
    while i < len(data):
        v, i = read_varint(data, i)
        if signed: v = zigzag(v)
        vals.append(v)
    return vals

def decode_with_schema(pdata, schema, indent=2):
    pad = " " * indent
    fields = decode_fields(pdata)
    for fnum in sorted(fields.keys()):
        values = fields[fnum]
        info = schema.get(fnum)
        if info is None:
            label = f"field[{fnum}]"
            for v in values:
                if isinstance(v, bytes):
                    print(f"{pad}{label} = bytes[{len(v)}]: {v.hex()}")
                else:
                    print(f"{pad}{label} = {v}")
            continue

        if len(info) == 3 and info[1] == "sub":
            label, _, sub_schema = info
            for v in values:
                if isinstance(v, bytes):
                    print(f"{pad}{label}:")
                    decode_with_schema(v, sub_schema, indent + 2)
            continue

        label, typ = info[0], info[1]
        for v in values:
            if isinstance(v, bytes):
                if typ == "str":
                    print(f"{pad}{label} = \"{v.decode('utf-8', errors='replace')}\"")
                elif typ == "f" and len(v) == 4:
                    print(f"{pad}{label} = {struct.unpack('<f', v)[0]:.4f}")
                elif typ == "rvaru":
                    vals = decode_repeated_varints(v, signed=False)
                    print(f"{pad}{label} = {vals}")
                elif typ == "rvars":
                    vals = decode_repeated_varints(v, signed=True)
                    print(f"{pad}{label} = {[x*0.1 for x in vals]} °C (raw: {vals})")
                else:
                    try:
                        fval = struct.unpack('<f', v)[0]
                        print(f"{pad}{label} = {fval:.2f}  [fixed32]")
                    except Exception:
                        print(f"{pad}{label} = bytes[{len(v)}]: {v.hex()}")
            else:  # varint
                v = int(v)
                if typ == "s":
                    v = zigzag(v)
                if typ == "dC":
                    dc = zigzag(v) if v > 2**62 else v   # treat as signed
                    print(f"{pad}{label} = {dc/10:.1f} °C  (raw={v})")
                elif typ == "mV":
                    print(f"{pad}{label} = {v/1000:.3f} V  (raw={v} mV)")
                elif typ == "mA":
                    sv = zigzag(v) if v > 2**62 else v
                    print(f"{pad}{label} = {sv/1000:.3f} A  (raw={sv} mA)")
                else:
                    print(f"{pad}{label} = {v}")

# ── EcoFlow login + MQTT creds ────────────────────────────────────────────────
print("Logging in...", flush=True)
r = requests.post(f"{API}/auth/login",
    headers={"lang": "en_US", "content-type": "application/json"},
    json={"email": EMAIL, "password": base64.b64encode(PASS.encode()).decode(),
          "scene": "IOT_APP", "userType": "ECOFLOW"})
jr = r.json()
if jr.get("code") != "0":
    sys.exit(f"Login failed: {jr}")
data   = jr["data"]
token  = data["token"]
userId = data["user"]["userId"]
print(f"userId={userId}", flush=True)

hdrs   = {"lang": "en_US", "authorization": f"Bearer {token}"}
cr     = requests.get(f"{API}/iot-auth/app/certification", headers=hdrs, params={"userId": userId})
creds  = cr.json()["data"]
print(f"MQTT host={creds['url']}:{creds['port']}", flush=True)

# ── MQTT ──────────────────────────────────────────────────────────────────────
TOPIC      = f"/app/device/property/{SN}"
WAKE_TOPIC = f"/app/{userId}/{SN}/thing/property/get"
WAKE_PAYLOAD = json.dumps({
    "from": "HomeAssistant", "id": "999954321", "version": "1.1",
    "moduleType": 0, "operateType": "latestQuotas", "params": {}
})

msg_count = [0]
seen_cmd_ids = {}   # (cmdFunc, cmdId) -> count

SEP  = "-" * 70
SEP2 = "=" * 70

def on_connect(c, u, f, rc):
    print(f"\n{SEP2}\nCONNECTED  rc={rc}\n{SEP2}", flush=True)
    c.subscribe(TOPIC, qos=0)
    c.publish(WAKE_TOPIC, WAKE_PAYLOAD, qos=0)
    print(f"Subscribed + wake published\n", flush=True)

def on_message(c, u, msg):
    raw = msg.payload
    msg_count[0] += 1
    n = msg_count[0]

    try:
        pdata, cmd_func, cmd_id = parse_outer(raw)
    except Exception as e:
        print(f"\n[MSG #{n}]  parse_outer failed: {e}  hex={raw[:20].hex()}", flush=True)
        return

    key = (cmd_func, cmd_id)
    seen_cmd_ids[key] = seen_cmd_ids.get(key, 0) + 1

    print(f"\n{SEP}", flush=True)
    print(f"[MSG #{n}]  cmdFunc={cmd_func}  cmdId={cmd_id}  bytes={len(raw)}  pdata_bytes={len(pdata)}", flush=True)

    if cmd_func == 32 and cmd_id == 50:
        print("  -- BMS HeartBeat --")
        decode_with_schema(pdata, BMS_SCHEMA)
    elif cmd_func == 254 and cmd_id in (21, 22):
        print(f"  -- Display PropertyUpload (cmdId={cmd_id}) --")
        decode_with_schema(pdata, DISPLAY_SCHEMA)
    elif cmd_func == 32 and cmd_id == 2:
        print("  -- CMS/EMS HeartBeat --")
        decode_with_schema(pdata, CMS_SCHEMA)
    else:
        print(f"  -- UNKNOWN (cmdFunc={cmd_func} cmdId={cmd_id}) - raw pdata --")
        decode_with_schema(pdata, {})

    sys.stdout.flush()

client_id = f"ANDROID_{str(uuid.uuid4()).upper()}_{userId}"
client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION1, client_id=client_id, protocol=mqtt.MQTTv311)
client.username_pw_set(creds["certificateAccount"], creds["certificatePassword"])
client.tls_set(cert_reqs=ssl.CERT_NONE)
client.tls_insecure_set(True)
client.on_connect = on_connect
client.on_message = on_message
client.connect(creds["url"], int(creds["port"]))

DURATION = 60
print(f"Capturing for {DURATION}s  (device={SN})...", flush=True)
client.loop_start()
time.sleep(DURATION)
client.loop_stop()
client.disconnect()

print(f"\n{SEP2}")
print(f"DONE.  Total messages: {msg_count[0]}")
print("Message types seen:")
for (cf, ci), cnt in sorted(seen_cmd_ids.items()):
    name = {(32, 50): "BMS", (254, 21): "Display", (254, 22): "Display",
            (32, 2): "CMS/EMS"}.get((cf, ci), "UNKNOWN")
    print(f"  cmdFunc={cf:3d}  cmdId={ci:3d}  count={cnt:4d}  [{name}]")
print(SEP2)
