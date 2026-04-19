# EcoFlow Cloud Communication Flow (C# Implementation)

Source: `EcoFlowMonitor.Core/Client/EcoFlowClient.cs`, `MqttMonitor.cs`, `Protocol/ProtobufDecoder.cs`, `App/Services/MonitorOrchestrator.cs`, `App/ViewModels/LoginViewModel.cs`.

## Actors
- **App** — Avalonia desktop client (this codebase)
- **EcoFlow REST API** — `https://api.ecoflow.com`
- **EcoFlow MQTT Broker** — TLS broker (host returned by REST)
- **EcoFlow Device** — Delta 3 / Delta 3 Max physical unit, publishing telemetry to its own topic

---

## Phase 1 — User Login (REST)

**Trigger:** User submits email + password in `LoginView` → `LoginViewModel.SignInAsync()`.

1. App → `POST https://api.ecoflow.com/auth/login`
   - Headers: `Accept: application/json`, `lang: en_US`
   - Body (JSON):
     ```
     {
       "email":    "<user email>",
       "password": "<base64(utf8(password))>",
       "scene":    "IOT_APP",
       "userType": "ECOFLOW"
     }
     ```
2. REST → App: JSON `{ data: { token, user: { userId } } }`
3. App stores `token` and `userId` in memory; sets `Authorization: Bearer <token>` on the `HttpClient`.
4. On failure → throw `InvalidOperationException` with HTTP status + server `message`.

---

## Phase 2 — Device Discovery (REST)

1. App → `GET https://api.ecoflow.com/app/user/device/list`
   - Header: `Authorization: Bearer <token>`
2. REST → App: JSON `{ data: [ { sn, deviceName, ... }, ... ] }`
3. App merges each `(sn, deviceName)` into local `AppConfig.Devices`, persists to `config.json`.
4. App navigates to `DashboardViewModel`, which kicks off `MonitorOrchestrator.StartAsync()`.

---

## Phase 3 — MQTT Credential Issuance (REST)

This phase converts a logged-in user identity into a short-lived **per-user MQTT account** (username + password) that the broker accepts. The EcoFlow REST API is the only authority that can issue these — they cannot be derived locally and they are not the user's email/password. They are scoped to a single `userId`, not to a single device, so one issuance can be reused for every device on the account.

**Caller:** `MonitorOrchestrator.ConnectMqttAsync(device, state)` runs once per cloud-mode device on `StartAsync()`. Each invocation creates its **own** `EcoFlowClient` (via `using var client = new EcoFlowClient()`), so today the app actually re-runs Phase 1 + Phase 3 once per device. The credentials are not cached or shared across devices in the current implementation.

### Step-by-step

1. **Precondition check** — `GetMqttCredsAsync()` calls `EnsureLoggedIn()`, which throws `InvalidOperationException("Not logged in. Call LoginAsync first.")` if `_token` is null. The bearer token from Phase 1 must already be sitting on `_http.DefaultRequestHeaders.Authorization`.

2. **Request** — App → REST:
   ```
   GET https://api.ecoflow.com/iot-auth/app/certification?userId=<Uri.EscapeDataString(userId)>
   Headers:
     Accept:        application/json
     lang:          en_US
     Authorization: Bearer <token from Phase 1>
   ```
   - The `userId` query parameter is URL-encoded. It comes from the Phase 1 login response (`data.user.userId`), held in `_userId` on the same `EcoFlowClient` instance.
   - No request body. The bearer token is the only thing tying the call to a specific account; the `userId` query string just tells the server which subject to mint creds for (and the server validates it matches the token).

3. **Response (success)** — REST → App: HTTP 200, JSON:
   ```
   {
     "data": {
       "url":                 "<mqtt host, e.g. mqtt.ecoflow.com>",
       "port":                "8883",
       "certificateAccount":  "app-xxxxxxxxxxxxxxxx",
       "certificatePassword": "<random per-issuance secret>"
     },
     ...
   }
   ```
   - `url` is a hostname, not a full URI. It is fed straight into `MqttClientOptionsBuilder.WithTcpServer(host, port)` in Phase 4.
   - `port` arrives as a JSON **string** in EcoFlow's response. The C# code reads it via `.ToString()` and parses with `int.TryParse`; on missing/unparseable values it falls back to `8883` (TLS).
   - `certificateAccount` / `certificatePassword` are misleadingly named — they are not X.509 material. They are an MQTT **username/password** pair the broker validates on `CONNECT`. They are bound to `userId`, not to any specific device serial number.

4. **Response (failure)** — On non-2xx, the code throws:
   ```
   InvalidOperationException($"MQTT cert request failed ({(int)statusCode}): {json?["message"]}")
   ```
   On 2xx but missing fields, it throws targeted errors per missing key (`"MQTT cert response missing 'data'"`, `"MQTT cert missing 'url'"`, etc.) so the failure pinpoints which contract assumption broke. None of these are retried inside `EcoFlowClient`; the exception propagates up to `MonitorOrchestrator.ConnectMqttAsync`'s catch, which logs a warning and leaves that device unmonitored.

5. **Result construction** — App returns:
   ```
   new MqttCredentials(
     Host:     data["url"],
     Port:     parsedPort,            // 8883 fallback
     Username: data["certificateAccount"],
     Password: data["certificatePassword"]
   )
   ```
   `MqttCredentials` is an immutable positional record (`record MqttCredentials(string Host, int Port, string Username, string Password)`) — it is passed by value into `MqttMonitor`'s constructor and never mutated.

### Token / credential lifecycle

- **Bearer token (Phase 1):** used for exactly two REST calls — `device/list` (Phase 2) and `iot-auth/app/certification` (Phase 3). It is held only in the `EcoFlowClient` instance, never persisted to `config.json`, and the instance is `using`-disposed immediately after Phase 3 completes.
- **MQTT credentials (Phase 3 output):** held only in the `MqttMonitor` instance (`_creds` field) for the lifetime of that monitor. They are not persisted either. If the monitor is restarted (`MonitorOrchestrator.RestartDeviceAsync`), Phase 1 + Phase 3 run again from scratch.
- **Implication:** there is no token refresh logic. If the broker rejects the MQTT credentials mid-session (e.g., they expire server-side), the current Polly retry loop will keep reconnecting with the same stale credentials — a full app restart is needed to re-issue them. This is a known gap, not by design.

### Why the certificate endpoint exists

EcoFlow's broker does not accept user emails or bearer tokens. The `/iot-auth/app/certification` endpoint is the only bridge from the user-facing REST identity (email + password → token) to the MQTT identity (username + password the broker recognises). Without it, the bearer token is useless to the broker and the user's email/password are useless too — Phase 3 is mandatory for any cloud telemetry.

---

## Phase 4 — MQTT Connection Bring-up

`MqttMonitor.StartAsync()` → `ConnectLoopAsync()` (wrapped in a Polly pipeline: exponential retry 2 s → 5 min with jitter, behind a circuit breaker that opens after 3 failures for 30 s to avoid broker rate-limit lockout).

FSM states (`Stateless` library): `Idle → Connecting → Authenticating → Streaming`, with `Retrying` / `Error` branches.

1. App constructs MQTT client options:
   - `ClientId = "ANDROID_<UPPERCASE_GUID>_<userId>"` (mirrors the official Android app)
   - TCP server = `(creds.Host, creds.Port)`
   - Credentials = `(creds.Username, creds.Password)`
   - Protocol = **MQTT 3.1.1** (broker requirement)
   - TLS enabled, **certificate validation bypassed** (`return true` for any cert)
   - `CleanSession = true`
2. App → Broker: MQTT `CONNECT`
3. Broker → App: `CONNACK` → FSM fires `Connected` → state `Authenticating`.
4. App → Broker: `SUBSCRIBE` to `"/app/device/property/<sn>"` (QoS 0, AtMostOnce).
5. Broker → App: `SUBACK`.
6. App → Broker: `PUBLISH` "wake" command on `"/app/<userId>/<sn>/thing/property/get"` (QoS 0):
   ```
   {"from":"HomeAssistant","id":"999954321","version":"1.1",
    "moduleType":0,"operateType":"latestQuotas","params":{}}
   ```
   Without this the device stays silent until the EcoFlow mobile app opens.

---

## Phase 5 — Telemetry Streaming (MQTT)

Device → Broker → App: continuous binary protobuf messages on `/app/device/property/<sn>`.

On first decoded message, FSM fires `Authenticated` → state `Streaming`.

`MqttMonitor.OnMessageReceivedAsync` → `ProtobufDecoder.Dispatch(raw)`:

1. **ParseOuter** decodes the outer protobuf envelope:
   - Field 1 = HeaderMessage (nested protobuf)
   - Header field 1 = `pdata` (payload bytes)
   - Header field 6 = `encType`, field 8 = `cmdFunc`, field 9 = `cmdId`, field 14 = `seq`, field 2 = `src`
   - **If `encType == 1` and `src != 32`:** XOR-decrypt `pdata` byte-wise with key `(seq & 0xFF)`.
2. **Dispatch** routes by `(cmdFunc, cmdId)`:

   | cmdFunc | cmdId | Decoder | Output |
   |---------|-------|---------|--------|
   | 32 | 50 | `DecodeBms` | `BmsData` (battery %, V, A, °C, cell mV/°C, MOS temps, in/out W, remain min, cycles, SoH, energy counters, pack SN) |
   | 32 | 2  | `DecodeEms` | `EmsData` (charge state, fan, max charge SoC, UPS mode, chg/dsg remain min, BMS connected mask, AC line plugged) — wrapped in CMS envelope (v1.0 at field 1, v1.3 at field 2) |
   | 254 | 21 or 22 | `DecodeDisplay` | `DisplayData` (TotalIn/Out W, solar high/low, AC in W, USB A1/A2/C1/C2 W, AC plugged, AC freq) |

3. Decoded fragment merged into `DeviceState` under `_state.SyncLock`:
   - `state.Bms ??= bms; state.Display ??= display; state.Ems ??= ems;`
   - `state.Power = PowerStateMachine.Update(state.Power, state)` (pure function → derives `PowerStatus`: Idle / Charging / PowerLost / Unknown)
   - `state.LastDataReceived = DateTime.Now`
4. `MqttMonitor.StateChanged` event fires on the MQTTnet thread pool with `(state, previousPower)`.

---

## Phase 6 — App-side Fan-out

`MonitorOrchestrator.OnStateChanged`:

1. **Trigger eval** — `TriggerEvaluator.Evaluate(device, state, previousPower)` returns rules to fire (edge: PowerLost / PowerRestored; level w/ 5-min cooldown: BatteryBelow / TimeRemainingBelow).
2. **Action dispatch** — for each fired rule, `ActionRunner.Run(action, ...)` → `RunScript | Shutdown | Hibernate | Sleep | Notification | WriteLog` via platform services.
3. **History** — `IHistoryStore.EnqueueSnapshot(TelemetrySnapshot)` (SQLite).
4. **Event log** — on power-status transition, `IEventStore.EnqueueEvent(PowerEvent)`.
5. **UI event** — re-raise as `DeviceUpdated`. `DashboardViewModel.OnDeviceUpdated` marshals to UI via `Dispatcher.UIThread.Post()` and updates the `DeviceViewModel` observable properties.

---

## Phase 7 — Disconnect / Reconnect

- Broker → App: `DISCONNECT` (or transport drop) → `OnDisconnectedAsync`
- FSM fires `Disconnected` → state `Retrying`
- `_disconnectTcs.TrySetException(...)` causes the Polly `await` to throw
- Polly schedules next attempt (exponential backoff, jitter, max 5 min); FSM enters `Retrying` with the planned delay, then re-enters `Connecting` for the next pipeline iteration.
- Circuit breaker opens for 30 s after 3 consecutive failures (logs "broker rate limit protection active").
- `MqttMonitor.StopAsync()` fires FSM `Stop` → `Idle`, cancels `_cts`, and disconnects the client cleanly.

---

## Endpoint / Topic Reference

| Step | Type | URL / Topic | Direction |
|------|------|-------------|-----------|
| Login | REST POST | `https://api.ecoflow.com/auth/login` | App → API |
| Device list | REST GET | `https://api.ecoflow.com/app/user/device/list` | App → API |
| MQTT cert | REST GET | `https://api.ecoflow.com/iot-auth/app/certification?userId=<id>` | App → API |
| Connect | MQTT 3.1.1 / TLS | `<creds.Host>:<creds.Port>` (default 8883) | App → Broker |
| Subscribe | MQTT SUBSCRIBE | `/app/device/property/<sn>` | App → Broker |
| Wake | MQTT PUBLISH | `/app/<userId>/<sn>/thing/property/get` | App → Broker → Device |
| Telemetry | MQTT PUBLISH (binary protobuf) | `/app/device/property/<sn>` | Device → Broker → App |

## Key Constants
- API host: `https://api.ecoflow.com`
- Default MQTT port: `8883` (TLS)
- MQTT protocol version: `3.1.1`
- Client ID format: `ANDROID_<UPPERCASE_GUID>_<userId>`
- TLS cert validation: **bypassed** (`_ => true`)
- QoS used: `AtMostOnce` (0) for both subscribe and publish
