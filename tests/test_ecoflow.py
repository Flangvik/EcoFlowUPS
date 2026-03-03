# tests/test_ecoflow.py
import base64
import pytest
from unittest.mock import patch, MagicMock
from ecoflow_status import ecoflow_login, get_device_sn, get_mqtt_credentials, parse_status_payload, PowerState, PowerStatus, update_power_state, build_layout


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


def test_parse_status_payload_extracts_params():
    import json
    fake_payload = json.dumps({"params": {"soc": 85, "wattsInSum": 120}}).encode()
    result = parse_status_payload(fake_payload)
    assert result["soc"] == 85
    assert result["wattsInSum"] == 120


def test_parse_status_payload_falls_back_to_root():
    import json
    # If no "params" key, return the whole dict
    fake_payload = json.dumps({"soc": 50}).encode()
    result = parse_status_payload(fake_payload)
    assert result["soc"] == 50


def test_power_state_charging():
    from ecoflow_status import PowerState, PowerStatus, update_power_state
    state = PowerState()
    new = update_power_state(state, {"total_in_W": 225})
    assert new.status == PowerStatus.CHARGING
    assert new.last_input_W == 225

def test_power_state_idle():
    from ecoflow_status import PowerState, PowerStatus, update_power_state
    state = PowerState()
    new = update_power_state(state, {"total_in_W": 5})
    assert new.status == PowerStatus.IDLE

def test_power_state_lost_transition():
    from ecoflow_status import PowerState, PowerStatus, update_power_state
    state = update_power_state(PowerState(), {"total_in_W": 225})
    assert state.status == PowerStatus.CHARGING
    new = update_power_state(state, {"total_in_W": 0})
    assert new.status == PowerStatus.POWER_LOST
    assert new.last_input_W == 225
    assert new.lost_at is not None

def test_power_state_restored():
    from ecoflow_status import PowerState, PowerStatus, update_power_state
    state = update_power_state(PowerState(), {"total_in_W": 200})
    state = update_power_state(state, {"total_in_W": 0})
    assert state.status == PowerStatus.POWER_LOST
    new = update_power_state(state, {"total_in_W": 180})
    assert new.status == PowerStatus.CHARGING
    assert new.restored_at is not None

def test_power_state_no_input_key_is_idle():
    from ecoflow_status import PowerState, PowerStatus, update_power_state
    state = update_power_state(PowerState(), {})
    assert state.status == PowerStatus.IDLE


def test_build_layout_returns_panel():
    from rich.panel import Panel
    from ecoflow_status import build_layout, PowerState
    panel = build_layout({"battery_pct": 68.5, "remain_min": 2760, "total_in_W": 225, "total_out_W": 1, "temp_C": 1.8, "voltage_V": 53.37, "cycles": 3, "soh_pct": 100}, PowerState(), "Delta 3")
    assert isinstance(panel, Panel)

def test_build_layout_shows_battery():
    import io
    from rich.console import Console
    from ecoflow_status import build_layout, PowerState
    panel = build_layout({"battery_pct": 68.5, "remain_min": 2760, "total_in_W": 225, "total_out_W": 1}, PowerState(), "Delta 3")
    buf = io.StringIO()
    Console(file=buf, width=80, force_terminal=False).print(panel)
    assert "68.5" in buf.getvalue()

def test_build_layout_power_lost_shows_alert():
    import io
    from rich.console import Console
    from ecoflow_status import build_layout, PowerState, PowerStatus, update_power_state
    state = update_power_state(PowerState(), {"total_in_W": 225})
    state = update_power_state(state, {"total_in_W": 0})
    status = {"battery_pct": 68.5, "remain_min": 2760, "total_in_W": 0, "total_out_W": 1}
    panel = build_layout(status, state, "Delta 3")
    buf = io.StringIO()
    Console(file=buf, width=80, force_terminal=False).print(panel)
    out = buf.getvalue()
    assert any(word in out for word in ["POWER", "LOST", "CUT", "INPUT"])
