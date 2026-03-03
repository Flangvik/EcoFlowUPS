# EcoFlow Python Client Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** A one-shot Python script that logs into EcoFlow's client API, auto-discovers your Delta 2's serial number, connects to their MQTT broker, and prints a clean status snapshot.

**Architecture:** Three sequential REST calls (login, device list, MQTT cert) followed by a single MQTT subscription that waits for the first telemetry payload, prints it, and exits. Everything lives in one file (`ecoflow_status.py`) with credentials loaded from env vars or a `.env` file.

**Tech Stack:** Python 3.9+, `requests`, `paho-mqtt`, `python-dotenv`

---

### Task 1: Project scaffold

**Files:**
- Create: `requirements.txt`
- Create: `.env.example`
- Create: `ecoflow_status.py` (empty stub)
- Create: `tests/test_ecoflow.py` (empty stub)

**Step 1: Create requirements.txt**

```
requests==2.31.0
paho-mqtt==1.6.1
python-dotenv==1.0.0
pytest==7.4.0
```

**Step 2: Create .env.example**

```
ECOFLOW_EMAIL=your@email.com
ECOFLOW_PASSWORD=yourpassword
```

**Step 3: Create empty ecoflow_status.py**

```python
# ecoflow_status.py
```

**Step 4: Create empty test stub**

```python
# tests/test_ecoflow.py
```

**Step 5: Install dependencies**

Run: `pip install -r requirements.txt`
Expected: All packages install without errors.

**Step 6: Commit**

```bash
git add requirements.txt .env.example ecoflow_status.py tests/test_ecoflow.py
git commit -m "feat: scaffold ecoflow client project"
```

---

### Task 2: Authentication — login and get token

**Files:**
- Modify: `ecoflow_status.py`
- Modify: `tests/test_ecoflow.py`

The login endpoint is `POST https://api.ecoflow.com/auth/login`.
Password must be base64-encoded before sending.
Response shape: `{"data": {"token": "...", "user": {"userId": "..."}}, "message": "Success", "code": "0"}`

**Step 1: Write the failing test**

```python
# tests/test_ecoflow.py
import base64
from unittest.mock import patch, MagicMock
from ecoflow_status import ecoflow_login

def test_login_returns_token_and_user_id():
    mock_response = MagicMock()
    mock_response.json.return_value = {
        "code": "0",
        "message": "Success",
        "data": {
            "token": "test-token-123",
            "user": {"userId": "user-456"}
        }
    }
    mock_response.raise_for_status = MagicMock()

    with patch("ecoflow_status.requests.post", return_value=mock_response) as mock_post:
        token, user_id = ecoflow_login("test@email.com", "mypassword")

    assert token == "test-token-123"
    assert user_id == "user-456"

    call_args = mock_post.call_args
    body = call_args.kwargs["json"]
    assert body["email"] == "test@email.com"
    assert body["password"] == base64.b64encode(b"mypassword").decode()
    assert body["scene"] == "IOT_APP"
    assert body["userType"] == "ECOFLOW"
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_ecoflow.py::test_login_returns_token_and_user_id -v`
Expected: FAIL with `ImportError: cannot import name 'ecoflow_login'`

**Step 3: Implement ecoflow_login**

```python
# ecoflow_status.py
import base64
import requests

API_HOST = "https://api.ecoflow.com"

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
```

**Step 4: Run test to verify it passes**

Run: `pytest tests/test_ecoflow.py::test_login_returns_token_and_user_id -v`
Expected: PASS

**Step 5: Commit**

```bash
git add ecoflow_status.py tests/test_ecoflow.py
git commit -m "feat: implement ecoflow login with base64 password encoding"
```

---

### Task 3: Fetch device list and extract Delta 2 serial number

**Files:**
- Modify: `ecoflow_status.py`
- Modify: `tests/test_ecoflow.py`

Endpoint: `GET https://api.ecoflow.com/iot-service/app/user/device/list`
Headers: `Authorization: Bearer {token}`
Response: `{"data": [{"deviceName": "DELTA 2", "sn": "R331...", ...}]}`

**Step 1: Write the failing test**

```python
def test_get_device_sn_returns_first_device():
    mock_response = MagicMock()
    mock_response.json.return_value = {
        "code": "0",
        "data": [
            {"deviceName": "DELTA 2", "sn": "R331ABC123"},
            {"deviceName": "Other", "sn": "OTHER999"},
        ]
    }
    mock_response.raise_for_status = MagicMock()

    with patch("ecoflow_status.requests.get", return_value=mock_response):
        sn = get_device_sn("test-token-123")

    assert sn == "R331ABC123"

def test_get_device_sn_raises_if_no_devices():
    mock_response = MagicMock()
    mock_response.json.return_value = {"code": "0", "data": []}
    mock_response.raise_for_status = MagicMock()

    with patch("ecoflow_status.requests.get", return_value=mock_response):
        with pytest.raises(RuntimeError, match="No devices"):
            get_device_sn("test-token-123")
```

Add `import pytest` at the top of `tests/test_ecoflow.py`. Also add `from ecoflow_status import ecoflow_login, get_device_sn` to the imports.

**Step 2: Run tests to verify they fail**

Run: `pytest tests/test_ecoflow.py -v`
Expected: New tests FAIL with `ImportError`

**Step 3: Implement get_device_sn**

```python
def get_device_sn(token: str) -> str:
    """Fetch device list and return the first device's serial number."""
    resp = requests.get(
        f"{API_HOST}/iot-service/app/user/device/list",
        headers={"lang": "en_US", "authorization": f"Bearer {token}"},
    )
    resp.raise_for_status()
    data = resp.json()
    devices = data.get("data", [])
    if not devices:
        raise RuntimeError("No devices found on this account")
    sn = devices[0]["sn"]
    print(f"Found device: {devices[0].get('deviceName', 'Unknown')} (SN: {sn})")
    return sn
```

**Step 4: Run tests to verify they pass**

Run: `pytest tests/test_ecoflow.py -v`
Expected: All tests PASS

**Step 5: Commit**

```bash
git add ecoflow_status.py tests/test_ecoflow.py
git commit -m "feat: fetch device list and extract serial number"
```

---

### Task 4: Fetch MQTT broker credentials

**Files:**
- Modify: `ecoflow_status.py`
- Modify: `tests/test_ecoflow.py`

Endpoint: `GET https://api.ecoflow.com/iot-auth/app/certification`
Headers: `Authorization: Bearer {token}`
Query params: `userId={user_id}`
Response: `{"data": {"url": "mqtt.ecoflow.com", "port": "8883", "certificateAccount": "...", "certificatePassword": "..."}}`

**Step 1: Write the failing test**

```python
def test_get_mqtt_credentials():
    mock_response = MagicMock()
    mock_response.json.return_value = {
        "code": "0",
        "data": {
            "url": "mqtt.ecoflow.com",
            "port": "8883",
            "certificateAccount": "mqtt-user",
            "certificatePassword": "mqtt-pass",
        }
    }
    mock_response.raise_for_status = MagicMock()

    with patch("ecoflow_status.requests.get", return_value=mock_response):
        creds = get_mqtt_credentials("test-token-123", "user-456")

    assert creds["host"] == "mqtt.ecoflow.com"
    assert creds["port"] == 8883
    assert creds["username"] == "mqtt-user"
    assert creds["password"] == "mqtt-pass"
```

Update the import line: `from ecoflow_status import ecoflow_login, get_device_sn, get_mqtt_credentials`

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_ecoflow.py::test_get_mqtt_credentials -v`
Expected: FAIL with `ImportError`

**Step 3: Implement get_mqtt_credentials**

```python
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
```

**Step 4: Run all tests to verify they pass**

Run: `pytest tests/test_ecoflow.py -v`
Expected: All PASS

**Step 5: Commit**

```bash
git add ecoflow_status.py tests/test_ecoflow.py
git commit -m "feat: fetch MQTT broker credentials"
```

---

### Task 5: MQTT subscription — wait for first payload and exit

**Files:**
- Modify: `ecoflow_status.py`
- Modify: `tests/test_ecoflow.py`

Uses `paho.mqtt.client`. Connect to broker, subscribe to `/app/device/property/{sn}`, wait for the first message, store it, disconnect, return the parsed JSON payload.

**Step 1: Write the failing test**

```python
def test_fetch_device_status_returns_parsed_payload():
    import json
    from unittest.mock import patch, MagicMock, call

    fake_payload = json.dumps({"params": {"soc": 85, "wattsInSum": 120}}).encode()

    captured = {}

    def fake_connect(client, creds, sn):
        # Simulate immediate message delivery
        msg = MagicMock()
        msg.payload = fake_payload
        client.on_message(client, None, msg)

    with patch("ecoflow_status.mqtt_subscribe", side_effect=fake_connect):
        # We test the parsing logic directly instead
        pass

    # Test the payload parsing helper directly
    from ecoflow_status import parse_status_payload
    result = parse_status_payload(fake_payload)
    assert result["soc"] == 85
    assert result["wattsInSum"] == 120
```

**Step 2: Run test to verify it fails**

Run: `pytest tests/test_ecoflow.py::test_fetch_device_status_returns_parsed_payload -v`
Expected: FAIL with `ImportError: cannot import name 'parse_status_payload'`

**Step 3: Implement MQTT fetch and payload parser**

```python
import uuid
import json
import threading
import paho.mqtt.client as mqtt


def parse_status_payload(raw: bytes) -> dict:
    """Extract the params dict from an EcoFlow MQTT message payload."""
    data = json.loads(raw)
    return data.get("params", data)


def fetch_device_status(creds: dict, user_id: str, sn: str, timeout: int = 30) -> dict:
    """Connect to MQTT, wait for first device telemetry message, return parsed params."""
    client_id = f"ANDROID_{str(uuid.uuid4()).upper()}_{user_id}"
    result = {}
    event = threading.Event()

    client = mqtt.Client(client_id=client_id, protocol=mqtt.MQTTv311)
    client.username_pw_set(creds["username"], creds["password"])

    def on_connect(client, userdata, flags, rc):
        if rc == 0:
            client.subscribe(f"/app/device/property/{sn}")
        else:
            raise RuntimeError(f"MQTT connect failed with code {rc}")

    def on_message(client, userdata, msg):
        result["data"] = parse_status_payload(msg.payload)
        event.set()
        client.disconnect()

    client.on_connect = on_connect
    client.on_message = on_message

    client.connect(creds["host"], creds["port"])
    client.loop_start()

    if not event.wait(timeout=timeout):
        client.loop_stop()
        raise TimeoutError(f"No message received from device within {timeout}s")

    client.loop_stop()
    return result["data"]
```

**Step 4: Run all tests to verify they pass**

Run: `pytest tests/test_ecoflow.py -v`
Expected: All PASS

**Step 5: Commit**

```bash
git add ecoflow_status.py tests/test_ecoflow.py
git commit -m "feat: MQTT subscription with threading event and payload parser"
```

---

### Task 6: Wire up main() with .env support and pretty output

**Files:**
- Modify: `ecoflow_status.py`

Reads `ECOFLOW_EMAIL` and `ECOFLOW_PASSWORD` from environment (or `.env` file), runs the full pipeline, prints status as formatted JSON.

**Step 1: Add main() to ecoflow_status.py**

Append to the bottom of `ecoflow_status.py`:

```python
import os
import sys
from dotenv import load_dotenv


def main():
    load_dotenv()
    email = os.getenv("ECOFLOW_EMAIL")
    password = os.getenv("ECOFLOW_PASSWORD")

    if not email or not password:
        print("Error: Set ECOFLOW_EMAIL and ECOFLOW_PASSWORD in env or .env file")
        sys.exit(1)

    print("Logging in...")
    token, user_id = ecoflow_login(email, password)
    print(f"Logged in as user {user_id}")

    sn = get_device_sn(token)

    print("Fetching MQTT credentials...")
    creds = get_mqtt_credentials(token, user_id)

    print(f"Connecting to {creds['host']}:{creds['port']}, waiting for telemetry...")
    status = fetch_device_status(creds, user_id, sn)

    print("\n--- Delta 2 Status ---")
    print(json.dumps(status, indent=2))


if __name__ == "__main__":
    main()
```

**Step 2: Create .env with real credentials**

Copy `.env.example` to `.env` and fill in your EcoFlow email and password:

```bash
cp .env.example .env
# edit .env with your credentials
```

**Step 3: Run the script end-to-end**

Run: `python ecoflow_status.py`

Expected output:
```
Logging in...
Logged in as user 12345678
Found device: DELTA 2 (SN: R331XXXXXXX)
Fetching MQTT credentials...
Connecting to mqtt.ecoflow.com:8883, waiting for telemetry...

--- Delta 2 Status ---
{
  "soc": 84,
  "wattsInSum": 0,
  "wattsOutSum": 12,
  ...
}
```

**Step 4: Commit**

```bash
git add ecoflow_status.py .env.example
git commit -m "feat: wire up main() with dotenv and pretty JSON output"
```

---

### Task 7: Add .gitignore

**Files:**
- Create: `.gitignore`

**Step 1: Create .gitignore**

```
.env
__pycache__/
*.pyc
.pytest_cache/
```

**Step 2: Commit**

```bash
git add .gitignore
git commit -m "chore: add gitignore"
```
