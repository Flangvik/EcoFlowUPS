# EcoFlow POC — Python Reference Implementation

Standalone scripts that show how the EcoFlow cloud API and MQTT protocol work. Not for production use.

---

## Setup

```bash
pip install -r requirements.txt
cp config.json.example config.json
# edit config.json with your EcoFlow account credentials
```

`config.json` format:

```json
{
  "email": "you@example.com",
  "password": "yourpassword"
}
```

---

## Usage

### One-shot status print

```bash
python ecoflow_status.py
```

Logs in, fetches MQTT credentials, subscribes to your first device, decodes one telemetry message, prints a summary, and exits.

### Live Rich dashboard

```bash
python ecoflow_status.py --live
```

Subscribes continuously and updates a live terminal dashboard (uses the [Rich](https://github.com/Textualize/rich) library).

---

## Telemetry Fields Decoded

| Field | Source | Unit |
|---|---|---|
| Battery % | BMS field 25 (float32) or field 6 (uint) | % |
| Voltage | BMS field 7 | V (raw / 1000) |
| Current | BMS field 8 | A (signed int64 / 1000) |
| Temperature | BMS field 9 | °C (signed int64 / 10) |
| Remaining | BMS field 28 | minutes |
| Cycles | BMS field 14 | count |
| State of Health | BMS field 15 | % |
| Total input | Display field 3 | W (float32 or uint) |
| Total output | Display field 4 | W (float32 or uint) |
| AC input | Display field 54 | W (float32 or uint) |

---

## Protocol Summary

1. **REST login** — `POST https://api.ecoflow.com/auth/login` with `email` and Base64-encoded `password` — returns JWT token + user ID
2. **MQTT credentials** — `GET https://api.ecoflow.com/iot-auth/app/certification?userId={id}` with auth header — returns broker host, port, MQTT username + password
3. **Subscribe** — connect to `mqtt.ecoflow.com:8883` over TLS (cert validation disabled) and subscribe to `/app/device/property/{serialNumber}`
4. **Decode** — each message is a protobuf binary with a custom outer envelope. When `encType == 1` and `src != 32`, inner bytes are XOR-decrypted with `seq & 0xFF`. Inner type is determined by `cmdFunc` + `cmdId`; BMS data is `cmdFunc=2, cmdId=2`; display data is `cmdFunc=20, cmdId=1`

---

## Other Scripts

| Script | Purpose |
|---|---|
| `decode_bms.py` | Shows all known BMS field numbers and their meanings |
| `decode_payloads.py` | Decodes raw hex payloads captured from device traffic — useful as test fixtures |

---

## Tests

```bash
pytest tests/
```

Tests cover power state machine transitions and protobuf decoder correctness using captured hex payloads.
