# Testing

Documented from the actual code as of the current state. This is a rapidly developed project -- testing is minimal and primarily manual.

---

## Automated Test Projects

### C# Service: No Test Projects

The solution (`EcoFlowMonitor.sln`) contains **zero test projects**. There are no xUnit, NUnit, MSTest, or any other C# test assemblies. None of the six projects contain test code.

The solution projects are:
- `EcoFlowMonitor.Core` -- no tests
- `EcoFlowMonitor.App` -- no tests
- `EcoFlowMonitor.Cli` -- no tests (this IS the diagnostic tool)
- `EcoFlowMonitor.Platform.Windows` -- no tests
- `EcoFlowMonitor.Platform.macOS` -- no tests
- `EcoFlowMonitor.Platform.Linux` -- no tests

No test frameworks (`xunit`, `nunit`, `MSTest`) appear in any `.csproj` file.

### Python POC: Has Unit Tests

The Python proof-of-concept (`poc/`) has a test suite at `poc/tests/test_ecoflow.py` using pytest. This tests the **original Python implementation**, not the C# service. These tests cover:

| Test | What it validates |
|---|---|
| `test_login_returns_token_and_user_id` | EcoFlow API login parses token and userId from response |
| `test_get_device_sn_returns_first_device` | Device list API returns first serial number |
| `test_get_device_sn_raises_if_no_devices` | Raises `RuntimeError` when no devices found |
| `test_get_mqtt_credentials` | MQTT credential endpoint parsing (host, port, username, password) |
| `test_parse_status_payload_extracts_params` | JSON payload parsing extracts `params` sub-object |
| `test_parse_status_payload_falls_back_to_root` | Falls back to root object when no `params` key |
| `test_power_state_charging` | Power state machine: input watts > threshold = Charging |
| `test_power_state_idle` | Power state machine: low watts = Idle |
| `test_power_state_lost_transition` | Power state machine: Charging -> 0 watts = PowerLost |
| `test_power_state_restored` | Power state machine: PowerLost -> watts = Charging (restored) |
| `test_power_state_no_input_key_is_idle` | Power state machine: missing data = Idle |
| `test_build_layout_returns_panel` | Rich TUI layout renders without crash |
| `test_build_layout_shows_battery` | Rich TUI output contains battery percentage |
| `test_build_layout_power_lost_shows_alert` | Rich TUI shows alert text during power loss |

These Python tests use `unittest.mock` for HTTP mocking and test the logic that was later ported to C#. The C# `PowerStateMachine`, `EcoFlowClient`, and `ProtobufDecoder` are functional equivalents of the tested Python code but have no C# test coverage.

---

## Test Coverage Assessment

### What Has Zero Automated Test Coverage

Everything in the C# codebase. Specifically:

**Core library (highest value for unit testing):**
- `PowerStateMachine.Update()` -- pure function, easily testable, directly equivalent to tested Python code
- `TriggerEvaluator.Evaluate()` -- pure logic with cooldown tracking
- `ProtobufDecoder.Dispatch()`, `DecodeBms()`, `DecodeDisplay()`, `DecodeEms()` -- byte-level parsing
- `BlePacketParser.TryParseFrame()`, `ParsePacket()` -- binary frame parsing with CRC validation
- `BlePacketBuilder.BuildPacket()`, `BuildAuthPacket()`, `WrapInFrame()` -- packet construction
- `TemplateExpander.Expand()` -- string template expansion
- `ConfigManager.Load()` / `Save()` -- JSON round-trip
- `BleCryptoLegacy` / `BleCryptoModern` -- encryption/decryption (AES-CBC)
- `Crc.ComputeCrc8()`, `Crc.ComputeCrc16()` -- CRC calculation
- `BleDispatcher.Dispatch()` -- packet routing
- `BleProtoMapper.MapDelta3Display()` -- protobuf-to-model mapping

**App layer (would benefit from integration tests):**
- `MonitorOrchestrator` -- device lifecycle management
- `NavigationService` -- view routing
- All view models -- command handlers, state transitions
- All converters -- value transformation logic

**Platform implementations (hard to unit test):**
- `CoreBluetoothBleAdapter` -- macOS-specific BLE
- `WindowsNotificationService`, `MacNotificationService`, etc. -- OS-level side effects

### What Is Implicitly Tested by Usage

The application has been used against real EcoFlow devices (Delta 3 series), which provides some confidence in:
- MQTT connection and subscription flow
- BLE connection, ECDH handshake, and authentication
- Protobuf decoding of real device messages
- Power state machine transitions
- UI rendering and data binding

---

## CLI Diagnostic Tool (EcoFlowMonitor.Cli)

### Purpose

`EcoFlowMonitor.Cli` is a console application that serves as the primary manual testing and protocol exploration tool. Located at `service/src/EcoFlowMonitor.Cli/Program.cs`.

### What It Does

1. Loads the existing `config.json` via `ConfigManager.Load()`
2. Authenticates against the EcoFlow API using stored credentials
3. Obtains MQTT credentials
4. Connects to the EcoFlow MQTT broker
5. Subscribes to device property topics for all configured devices
6. Publishes a wake command to trigger device state push
7. Listens for 30 seconds, capturing all unique message types
8. For each unique `(serialNumber, cmdFunc, cmdId)` combination, dumps:
   - All protobuf fields with their wire types
   - Varint values (unsigned and signed interpretations, hex)
   - Float32 values from 4-byte blobs
   - UTF-8 string attempts from short byte blobs
   - Raw hex for longer byte blobs

### Usage Pattern

Run from the `service/src/EcoFlowMonitor.Cli/` directory after configuring via the GUI app (which creates `config.json`):

```
dotnet run
```

No command-line arguments. It uses the same `ConfigManager` as the main app, so credentials and device list are shared.

### What It Validates

- API authentication still works
- MQTT credentials are valid
- MQTT broker is reachable
- Device topics are active
- Raw protobuf field layout of each message type

### Deduplication

Uses a `HashSet<string>` keyed on `{sn}:{cmdFunc}:{cmdId}` to only dump each unique message type once. This is intentional -- the tool is for protocol exploration, not continuous monitoring.

### Code Style Note

The CLI uses **top-level statements** (no `Main` method, no class wrapper). It is the only project in the solution that does this.

---

## Integration Testing Approach

### Real Device Testing

All testing to date has been manual integration testing against physical EcoFlow devices:

- **Cloud/MQTT path**: Requires a real EcoFlow account with registered devices. The MQTT broker is EcoFlow's production server (`mqtt.ecoflow.com:8883`). There is no mock server or test environment.

- **BLE path**: Requires physical proximity to an EcoFlow device with BLE enabled. The BLE handshake (ECDH Type 7 or Legacy Type 1) cannot be simulated without the actual device firmware.

- **Power state transitions**: Tested by physically plugging/unplugging AC power from the EcoFlow device while the monitor is running.

### No Mock Infrastructure

There are no:
- Mock MQTT brokers
- Recorded MQTT message fixtures for replay
- BLE simulation/emulation
- Fake device state generators
- Test doubles for `EcoFlowClient` (the HTTP client creates `HttpClient` directly, no interface abstraction)

### What Would Be Needed for Offline Testing

To enable automated testing without real devices:

1. **Captured protocol data** -- Record raw MQTT payloads and BLE packets as byte arrays. The CLI tool's output format would be a starting point, but raw bytes would need to be captured separately.

2. **Interface abstraction for EcoFlowClient** -- Currently a concrete class with `HttpClient` created internally. Would need an interface or injectable `HttpClient` for mocking.

3. **BLE adapter stubs** -- `StubBleAdapter` and `StubGattConnection` already exist in `PlatformServiceFactory.cs` but they throw `NotSupportedException` on connect. These could be extended to replay captured data.

---

## Test Data and Fixtures

### Existing

- **Python POC test data**: Inline JSON payloads in `poc/tests/test_ecoflow.py` (mock API responses, status payloads). These are for the Python implementation but document the expected API response shapes.

- **Proto files**: `pd335_sys.proto` and `pd335_bms_bp.proto` in `EcoFlowMonitor.Core/Proto/` define the expected message structures for Delta 3 family BLE messages.

- **Embedded key data**: `Protocol/keydata.b64` is a fixed lookup table used in Type 7 encryption. This is test-relevant as a known constant.

### Not Existing

- No captured raw MQTT payloads (byte arrays)
- No captured BLE packet sequences
- No expected decode results for known inputs
- No config.json fixtures for testing ConfigManager
- No mock API response fixtures for C# code

---

## Summary

| Aspect | Status |
|---|---|
| C# unit tests | None |
| C# integration tests | None |
| C# test framework | Not added to any project |
| Python POC tests | 14 tests in `poc/tests/test_ecoflow.py` (pytest) |
| CI/CD pipeline | None |
| Manual testing tool | `EcoFlowMonitor.Cli` (MQTT raw dump) |
| Real device testing | Primary validation method |
| Test data / fixtures | Python test mocks only; no C# fixtures |
| Code coverage tooling | Not configured |

The project relies entirely on manual testing against real devices and the CLI diagnostic tool for protocol exploration. The Python POC tests validate logic that was ported to C# but the C# implementations themselves have no automated test coverage.
