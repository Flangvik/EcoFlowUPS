# Integrations

Reference for every external service, protocol, and data flow in the EcoFlowUPS codebase.

---

## External Services

### EcoFlow REST API

Base URL: `https://api.ecoflow.com`

All requests carry `Accept: application/json` and `lang: en_US` headers.

| Endpoint | Method | Purpose |
|---|---|---|
| `/auth/login` | POST | Authenticate with email/password, receive bearer token and userId |
| `/app/user/device/list` | GET | List all devices on the account (returns serial numbers and names) |
| `/iot-auth/app/certification?userId={userId}` | GET | Obtain MQTT broker credentials (host, port, username, password) |

The login body sends the password Base64-encoded, with `scene: "IOT_APP"` and `userType: "ECOFLOW"`. The response places the token at `data.token` and the user ID at `data.user.userId`. Subsequent requests use `Authorization: Bearer {token}`.

**Implementation:** `EcoFlowMonitor.Core/Client/EcoFlowClient.cs`

### EcoFlow MQTT Broker

The broker URL and port come from the `/iot-auth/app/certification` response. Default port is `8883` (TLS). The connection uses MQTT 3.1.1 with TLS enabled (certificate validation is bypassed).

Client ID format: `ANDROID_{GUID-UPPERCASE}_{userId}` (mirrors the EcoFlow mobile app's Android client).

**Topics:**

| Topic | Direction | Purpose |
|---|---|---|
| `/app/device/property/{serialNumber}` | Subscribe | Receive protobuf-encoded device telemetry |
| `/app/{userId}/{serialNumber}/thing/property/get` | Publish | Wake command to trigger a full state push |

The wake payload is JSON: `{"from":"HomeAssistant","id":"999954321","version":"1.1","moduleType":0,"operateType":"latestQuotas","params":{}}`. Without publishing this wake command after subscribe, the broker stays silent until the EcoFlow mobile app opens.

**Implementation:** `EcoFlowMonitor.Core/Client/MqttMonitor.cs`

---

## Protocol Details

### Protobuf: Cloud/MQTT Path (Hand-Rolled Decoder)

The MQTT payload is a nested protobuf envelope decoded by a hand-written parser (no `.proto` schema compilation for the MQTT path). This was necessary because the cloud protocol uses a flat/legacy protobuf wire format that does not match the BLE `.proto` files.

**Implementation:** `EcoFlowMonitor.Core/Protocol/ProtobufDecoder.cs`

#### Wire Type Support

| Wire Type | Value | Handling |
|---|---|---|
| Varint | 0 | Decoded as `ulong` |
| 64-bit fixed | 1 | Skipped (8 bytes) |
| Length-delimited | 2 | Decoded as `byte[]` |
| 32-bit fixed | 5 | Decoded as `byte[]` (often float32) |

#### Outer Envelope (ParseOuter)

Every MQTT message has a two-level protobuf envelope:

```
outer.field1 (len-del) = HeaderMessage
  header.field1  (len-del) = pdata (payload bytes)
  header.field2  (varint)  = src
  header.field6  (varint)  = encType
  header.field8  (varint)  = cmdFunc
  header.field9  (varint)  = cmdId
  header.field14 (varint)  = seq
```

**XOR Decryption:** When `encType == 1` and `src != 32`, each payload byte is XORed with `seq & 0xFF`.

#### Message Routing (Dispatch)

The `cmdFunc` / `cmdId` pair determines which decoder runs:

| cmdFunc | cmdId | Decoder | Model |
|---|---|---|---|
| 32 | 50 | `DecodeBms()` | `BmsData` |
| 32 | 2 | `DecodeEms()` | `EmsData` |
| 254 | 21 | `DecodeDisplay()` | `DisplayData` |
| 254 | 22 | `DecodeDisplay()` | `DisplayData` |

### Protobuf: BLE Path (Compiled `.proto` Schemas)

The BLE path uses Google.Protobuf compiled schemas for Delta 3 / PD335 devices. These `.proto` files are compiled by `Grpc.Tools` at build time.

**Proto files in `EcoFlowMonitor.Core/Proto/`:**

| File | Package | Key Messages |
|---|---|---|
| `pd335_sys.proto` | `pd335_sys` | `DisplayPropertyUpload`, `RuntimePropertyUpload`, `ConfigWrite`, `ConfigWriteAck` |
| `pd335_bms_bp.proto` | `pd335_bms_bp` | `BMSHeartBeatReport`, `CMSHeartBeatReport`, `ems_heartbeat_pack_v1p0_t`, `ems_heartbeat_pack_v1p3_t` |
| `pd303.proto` | (excluded) | Excluded from build -- has duplicate field names that break C# codegen |

**Implementation:** `EcoFlowMonitor.Core/Protocol/BleProtoMapper.cs` maps `Pd335Sys.DisplayPropertyUpload` fields to shared `BmsData`, `DisplayData`, `EmsData` models.

### BLE GATT Characteristics

The transport tries two GATT service sets in order:

| Profile | Service UUID | Write UUID | Notify UUID |
|---|---|---|---|
| RFCOMM-style | `00000001-0000-1000-8000-00805f9b34fb` | `00000002-...` | `00000003-...` |
| Nordic UART | `6e400001-b5a3-f393-e0a9-e50e24dcca9e` | `6e400002-...` | `6e400003-...` |

RFCOMM is tried first. On failure, falls back to Nordic UART Service (NUS).

**Implementation:** `EcoFlowMonitor.Core/Client/Ble/BleTransport.cs`

### BLE Wire Frame Format (0x5A5A)

All BLE data is wrapped in `0x5A5A` frames:

```
[5A 5A] [frameType<<4] [0x01] [lenField:u16LE] [payload] [CRC16:u16LE]
```

- `lenField` = `len(payload) + 2` (CRC is counted in the length)
- Total frame = `6 + lenField` bytes
- CRC-16/ARC (MODBUS, poly 0x8005, reflected) covers everything except the trailing CRC itself
- `frameType 0x00` = unencrypted command, `frameType 0x01` = encrypted payload

**Implementation:** `EcoFlowMonitor.Core/Protocol/BlePacketParser.cs` (TryParseFrame)

### BLE Application Packet Format (0xAA)

Inside a decrypted frame, the application packet has this structure:

```
[AA] [version] [payloadLen:u16LE] [CRC8] [productByte] [seq:4b] [00 00]
[src] [dst] [dsrc ddst]? [cmdSet] [cmdId] [payload] [CRC16:u16LE]
```

- Version 2: 16-byte header (no dsrc/ddst fields)
- Version 3+: 18-byte header (includes dsrc/ddst)
- CRC-8/CCITT validates the first 4 bytes (stored at byte 4)
- CRC-16/ARC validates the entire packet minus the trailing 2 bytes
- **XOR decryption:** If `seq[0] != 0`, each payload byte is XORed with `seq[0]`

**CRC Algorithms** (`EcoFlowMonitor.Core/Protocol/Crc.cs`):
- CRC-8: polynomial 0x07, no reflection (CCITT)
- CRC-16: polynomial 0x8005, reflected input/output (ARC/MODBUS)

**Implementation:** `EcoFlowMonitor.Core/Protocol/BlePacketParser.cs` (ParsePacket), `BlePacketBuilder.cs`

### BLE Encryption

Two encryption schemes, selected by `encryptionType` from the BLE advertisement manufacturer data:

#### Type 1 -- Legacy (BleCryptoLegacy)

AES-256-CBC with MD5-derived keys, no PKCS7 padding (zero-padded manually):
- Key = `MD5(serialNumber)` repeated twice to fill 32 bytes
- IV = `MD5(reverse(serialNumber))`

#### Type 7 -- Modern ECDH (BleCryptoModern)

Multi-step key exchange protocol:

1. **Generate keypair:** SECP160r1 elliptic curve via BouncyCastle
2. **Send public key:** Uncompressed X+Y coordinates (40 bytes for secp160r1), sent in an unencrypted `0x5A5A` frame with payload `[0x01, 0x00, ...pubkey]`
3. **Receive device public key:** Parse response -- byte[2] indicates curve type, remaining bytes are the device's public key
4. **Compute shared secret:** ECDH point multiplication, take AffineXCoord
5. **Set initial encryption:** Key = `sharedSecret[:16]` (AES-128), IV = `MD5(sharedSecret)`
6. **Request session key:** Send `[0x02]` in an unencrypted frame
7. **Receive encrypted session key data:** Decrypt with initial key, extract `srand` (16 bytes) and `seed` (2 bytes)
8. **Derive final session key:** Lookup in a 65,280-byte embedded key data table (`keydata.b64`), combine with srand, hash with MD5
9. **Set final encryption:** AES-128-CBC with PKCS7 padding, key = derived session key, IV = `MD5(sharedSecret)`

The `keydata.b64` file is embedded as an assembly resource and loaded at static initialization.

**ECDH curve type mapping** (from device response byte):

| Byte | Curve | Public Key Size |
|---|---|---|
| 0, 1 | SECP160r1 | 40 bytes |
| 2 | SECP192r1 | 48 bytes |
| 3 | SECP224r1 | 56 bytes |
| 4 | SECP256r1 | 64 bytes |

**Session key derivation formula:**
```
pos = seed[0] * 0x10 + ((seed[1] - 1) & 0xFF) * 0x100
data = keydata[pos..pos+8] + keydata[pos+8..pos+16] + srand[0:8] + srand[8:16]
sessionKey = MD5(data)
```

**Implementation:** `EcoFlowMonitor.Core/Protocol/BleCrypto.cs`

---

## Authentication Flows

### Cloud: REST Login to MQTT Credentials

```
1. POST /auth/login  (email, base64(password))
   -> token, userId

2. GET /iot-auth/app/certification?userId={userId}
   -> MQTT host, port, certificateAccount, certificatePassword

3. Connect MQTT (TLS, MQTT 3.1.1)
   Client ID: ANDROID_{GUID}_{userId}
   Credentials: certificateAccount / certificatePassword

4. Subscribe: /app/device/property/{serialNumber}

5. Publish wake: /app/{userId}/{serialNumber}/thing/property/get
   -> Device pushes full state over subscribed topic
```

**Implementation:** `MonitorOrchestrator.ConnectMqttAsync()` orchestrates this via `EcoFlowClient` then `MqttMonitor`.

### BLE: ECDH Handshake + Auth Packet

```
1. BLE Scan
   - Filter by name prefix "EF-" / "Ecoflow" or manufacturer ID 0xB5C5 (46517)
   - Parse manufacturer data: [protoVersion, serialNumber(16b), ..., encryptionType]

2. Connect GATT
   - Discover services
   - Subscribe to notify characteristic (RFCOMM or Nordic UART)

3. Key Exchange (Type 7 only)
   - Steps 1-9 of ECDH protocol above
   - Transport updated with final crypto session

4. Auth Status Request
   - Send packet: src=0x21, dst=0x35, cmdSet=0x35, cmdId=0x89
   - Encrypted with session crypto, frameType=0x01

5. Auth Packet
   - Payload: MD5(userId + serialNumber) as uppercase hex ASCII (32 bytes)
   - Send packet: src=0x21, dst=0x35, cmdSet=0x35, cmdId=0x86
   - Encrypted with session crypto, frameType=0x01

6. Auth Response
   - Device responds with cmdSet=0x35, cmdId=0x86
   - payload[0] == 0x00 means success
   - Some devices skip the explicit response (timeout is acceptable)

7. Device begins streaming heartbeat data
```

**Implementation:** `EcoFlowMonitor.Core/Client/Ble/BleMonitor.cs`

---

## Data Flows

### MQTT Data Flow (Cloud)

```
EcoFlow REST API
  |
  v
LoginAsync() -> token, userId
GetMqttCredsAsync() -> host, port, user, pass
  |
  v
MqttMonitor.StartAsync()
  |  Connect TLS MQTT 3.1.1
  |  Subscribe /app/device/property/{sn}
  |  Publish wake command
  |
  v  (on message received)
Raw bytes (protobuf envelope)
  |
  v
ProtobufDecoder.Dispatch(raw)
  |  ParseOuter() -> pdata, cmdFunc, cmdId, encType, seq
  |  XOR decrypt if encType==1 && src!=32
  |  Route by cmdFunc/cmdId:
  |    (32,50)   -> DecodeBms()     -> BmsData
  |    (32,2)    -> DecodeEms()     -> EmsData
  |    (254,21)  -> DecodeDisplay() -> DisplayData
  |    (254,22)  -> DecodeDisplay() -> DisplayData
  |
  v
DeviceState (Bms, Display, Ems updated)
  |
  v
PowerStateMachine.Update() -> PowerState
  |
  v
StateChanged event -> MonitorOrchestrator
  |  TriggerEvaluator -> ActionRunner
  |
  v
DeviceUpdated event -> UI (DashboardViewModel)
```

### BLE Data Flow

```
BLE Scan (BleScanner)
  |  Filter: name "EF-"/"Ecoflow" or manufacturer ID 46517
  |  Parse manufacturer data for SN, encryptionType, protoVersion
  |
  v
BleMonitor.ConnectAndAuthAsync()
  |  BleTransport.ConnectAsync() -> GATT connect, discover services
  |  Subscribe to notify characteristic
  |
  v  (Type 7 only)
ECDH Handshake (SECP160r1 via BouncyCastle)
  |  Send public key -> receive device key -> shared secret
  |  Session key request -> derive final key via keydata table
  |  Update transport crypto
  |
  v
Auth Packet (MD5(userId+sn) as hex)
  |
  v  (device streams data via notifications)
Raw BLE notifications
  |
  v
BleTransport: accumulate in buffer
  |
  v
BlePacketParser.TryParseFrame()
  |  Find 0x5A5A prefix, validate CRC-16, extract payload
  |
  v
Decrypt (BleCryptoModern or BleCryptoLegacy)
  |  frameType 0x01: decrypt with session AES
  |  frameType 0x00: pass through
  |
  v
BlePacketParser.ParsePacket()
  |  Parse 0xAA header, validate CRC-8 + CRC-16
  |  XOR decrypt payload if seq[0] != 0
  |
  v
BleDispatcher.Dispatch(packet)
  |  Route by (src, cmdSet, cmdId):
  |
  |  Delta 3 family:
  |    src=0x02, cmdSet=0xFE, cmdId=0x15/0x16
  |    -> BleProtoMapper.MapDelta3Display()
  |       Uses compiled Pd335Sys.DisplayPropertyUpload.Parser
  |       -> BmsData + DisplayData + EmsData
  |
  |  Legacy devices (fallback to hand-rolled decoder):
  |    src=0x0B or (src=0x03, cs=0x20, ci=0x32) -> DecodeBms()
  |    src=0x02, cs=0x20                         -> DecodeDisplay()
  |    src=0x03, cs=0x20                         -> DecodeEms()
  |
  v
DeviceState (merge non-null fields only)
  |
  v
PowerStateMachine.Update() -> PowerState
  |
  v
StateChanged event -> MonitorOrchestrator -> UI
```

---

## Protobuf Field Mappings

### BMS (cmdFunc=32, cmdId=50) -- MQTT Hand-Rolled

Source: `ProtobufDecoder.DecodeBms()`, matches `pd335_bms_bp.proto` `BMSHeartBeatReport`.

| Proto Field | Wire | Model Property | Transform |
|---|---|---|---|
| 6 | varint | BatteryPct | Direct cast to float |
| 25 | float32 | BatteryPct | Preferred over field 6 |
| 7 | varint | VoltageV | / 1000.0 |
| 8 | varint | CurrentA | Signed / 1000.0 |
| 9 | varint | TempC | Signed / 10.0 |
| 11 | varint | DesignCapMah | Direct |
| 12 | varint | RemainCapMah | Direct |
| 14 | varint | Cycles | Direct |
| 15 | varint | SohPct | Direct |
| 16 | varint | MaxCellMv | Direct |
| 17 | varint | MinCellMv | Direct |
| 26 | varint | InputW | Direct |
| 27 | varint | OutputW | Direct |
| 28 | varint | RemainMin | Direct |
| 33 | packed varint | CellVolsMv | Each value is mV |
| 35 | packed zigzag | CellTempsC | Zigzag decode, / 10.0 |
| 56 | packed zigzag | MosTempsC | Zigzag decode, / 10.0 |
| 79 | varint | AccuChgEnergyWh | Direct |
| 80 | varint | AccuDsgEnergyWh | Direct |
| 81 | len-del | PackSn | UTF-8 string |

### Display (cmdFunc=254, cmdId=21/22) -- MQTT Hand-Rolled

Source: `ProtobufDecoder.DecodeDisplay()`.

| Proto Field | Wire | Model Property | Transform |
|---|---|---|---|
| 3 | float32/varint | TotalInW | Round to int |
| 4 | float32/varint | TotalOutW | Round to int |
| 9 | varint | UsbA1W | Direct |
| 10 | varint | UsbA2W | Direct |
| 11 | varint | UsbC1W | Direct |
| 12 | varint | UsbC2W | Direct |
| 35 | float32/varint | SolarInHighW | Round to int |
| 36 | float32/varint | SolarInLowW | Round to int |
| 54 | float32/varint | AcInW | Round to int |
| 61 | varint | AcPluggedIn | != 0 |
| 62 | varint | AcInFreqHz | Direct |

Fields 3, 4, 35, 36, 54 may arrive as either wire type 5 (float32) or wire type 0 (varint). The decoder handles both.

### EMS (cmdFunc=32, cmdId=2) -- MQTT Hand-Rolled

Source: `ProtobufDecoder.DecodeEms()`, matches `pd335_bms_bp.proto` `CMSHeartBeatReport`.

CMS envelope: field 1 = EMS v1.0 sub-message, field 2 = EMS v1.3 sub-message.

**EMS v1.0 (field 1):**

| Sub-Field | Model Property | Notes |
|---|---|---|
| 1 | ChgState | Charge/discharge state |
| 6 | FanLevel | Fan speed level |
| 7 | MaxChargeSoc | Max charge SoC limit |
| 10 | UpsMode | UPS mode flag |
| 12 | ChgRemainMin | Charge remaining minutes |
| 13 | DsgRemainMin | Discharge remaining minutes |
| 16 | BmsConnected | Packed array of connected BMS indices |

**EMS v1.3 (field 2):**

| Sub-Field | Model Property | Notes |
|---|---|---|
| 3 | ChgLinePlugged | AC charge line plugged in |

### Delta 3 BLE (DisplayPropertyUpload) -- Compiled Protobuf

Source: `BleProtoMapper.MapDelta3Display()` using `Pd335Sys.DisplayPropertyUpload`.

| Proto Field Name | Field # | Target Model | Target Property | Transform |
|---|---|---|---|---|
| cms_batt_soc | 262 | BmsData | BatteryPct | float direct |
| bms_batt_soc | 242 | BmsData | BatteryPct | Fallback if cms is 0 |
| bms_max_cell_temp | 259 | BmsData | TempC | / 10.0 (sint32 deci-degrees) |
| bms_batt_soh | 243 | BmsData | SohPct | int cast |
| cms_batt_soh | 263 | BmsData | SohPct | Fallback |
| bms_design_cap | 248 | BmsData | DesignCapMah | int cast |
| cms_dsg_rem_time | 268 | BmsData | RemainMin | int cast |
| bms_dsg_rem_time | 254 | BmsData | RemainMin | Fallback |
| pow_get_bms | 158 | BmsData | InputW/OutputW | Positive = input, negative = output |
| pow_in_sum_w | 3 | DisplayData | TotalInW | Round |
| pow_out_sum_w | 4 | DisplayData | TotalOutW | Round |
| pow_get_ac_in | 54 | DisplayData | AcInW | Round |
| plug_in_info_ac_in_flag | 61 | DisplayData | AcPluggedIn | != 0 |
| plug_in_info_ac_in_feq | 62 | DisplayData | AcInFreqHz | Direct |
| ac_out_freq | 211 | DisplayData | AcInFreqHz | Fallback |
| pow_get_pv | 361 | DisplayData | SolarInHighW | Round |
| pow_get_pv2 | 70 | DisplayData | SolarInLowW | Round |
| pow_get_typec1 | 11 | DisplayData | UsbC1W | Abs + Round |
| pow_get_typec2 | 12 | DisplayData | UsbC2W | Abs + Round |
| pow_get_qcusb1 | 9 | DisplayData | UsbA1W | Abs + Round |
| pow_get_qcusb2 | 10 | DisplayData | UsbA2W | Abs + Round |
| cms_max_chg_soc | 270 | EmsData | MaxChargeSoc | int cast |
| cms_chg_dsg_state | 282 | EmsData | ChgState | int cast |
| cms_chg_rem_time | 269 | EmsData | ChgRemainMin | int cast |
| cms_dsg_rem_time | 268 | EmsData | DsgRemainMin | int cast |
| pcs_fan_level | 30 | EmsData | FanLevel | int cast |

---

## BLE Scanning and Advertisement Parsing

EcoFlow devices are identified during BLE scan by:
- **Name prefix:** `EF-` or `Ecoflow` (case-insensitive)
- **Manufacturer ID:** `0xB5C5` (46517 decimal)

Manufacturer data layout (when present, >= 18 bytes):

| Offset | Length | Field |
|---|---|---|
| 0 | 1 | Protocol version (2 or 3) |
| 1 | 16 | Serial number (ASCII, null-padded) |
| 22 | 1 | Bits [5:3] = encryption type |

**Implementation:** `EcoFlowMonitor.Core/Client/Ble/BleScanner.cs`

---

## Connection Mode Selection

`ConnectionMode` enum in `EcoFlowMonitor.Core/Models/ConnectionType.cs`:

| Mode | Behavior |
|---|---|
| `Cloud` | REST login, then MQTT subscribe. Requires `AccountConfig` with email/password. |
| `Ble` | Direct BLE connection. Requires `BleAddress` from a prior scan and a `userId` (cloud or locally generated). |
| `Auto` | Try BLE first if `device.HasBle` is true and a userId is available. Fall back to Cloud if BLE is unavailable or no BLE address exists. |

When `Auto` is selected and BLE is available, the orchestrator prefers BLE (local, lower latency). If a cloud-only device is later discovered via BLE scan, `MergeBleScanResult()` upgrades its mode from `Cloud` to `Auto` and persists the BLE fields.

A locally generated UUID (`LocalUserId`) is used as the authentication userId for BLE-only setups where no cloud login has been performed. For cloud-authenticated sessions, `CloudUserId` is preferred.

**Implementation:** `EcoFlowMonitor.App/Services/MonitorOrchestrator.cs`

---

## Third-Party SDKs and Libraries

| Package | Version | Purpose |
|---|---|---|
| **MQTTnet** | 4.3.7.1207 | MQTT client for cloud broker communication |
| **MQTTnet.Extensions.ManagedClient** | 4.3.7.1207 | (Referenced but not actively used; MqttMonitor uses raw client) |
| **Google.Protobuf** | 3.28.3 | Runtime for compiled .proto message parsing (BLE path) |
| **Grpc.Tools** | 2.68.1 | Build-time protoc compiler for .proto files |
| **BouncyCastle.Cryptography** | 2.5.1 | SECP160r1 ECDH key generation and point multiplication (BLE Type 7) |
| **NSec.Cryptography** | 24.4.0 | (Referenced; main crypto uses System.Security.Cryptography + BouncyCastle) |
| **CommunityToolkit.Mvvm** | 8.4.0 | MVVM base classes (ObservableObject, RelayCommand) |
| **Avalonia** | (App project) | Cross-platform UI framework |
| **CoreBluetooth** | (macOS native) | BLE scanning and GATT on macOS via `CoreBluetoothBleAdapter` |

Platform-specific `IBleAdapter` implementations:
- **macOS:** `CoreBluetoothBleAdapter` using `CBCentralManager` / `CBPeripheral` (native Xamarin/MAUI binding)
- **Windows/Linux:** Interface defined (`IBleAdapter`) but platform adapters live in respective platform projects

---

## Shared Data Models

All data flows (MQTT and BLE) converge on the same model types in `EcoFlowMonitor.Core/Models/`:

| Model | Key Fields |
|---|---|
| `BmsData` | BatteryPct, VoltageV, CurrentA, TempC, Cycles, SohPct, CellVolsMv[], CellTempsC[], InputW, OutputW, AccuChgEnergyWh, PackSn |
| `DisplayData` | TotalInW, TotalOutW, AcInW, SolarInHighW, SolarInLowW, UsbA1W, UsbA2W, UsbC1W, UsbC2W, AcPluggedIn, AcInFreqHz |
| `EmsData` | ChgState, FanLevel, MaxChargeSoc, UpsMode, ChgRemainMin, DsgRemainMin, BmsConnected[], ChgLinePlugged |

These feed into `DeviceState` (which also holds `PowerState` from `PowerStateMachine`) and drive the UI via `StateChanged` / `DeviceUpdated` events.
