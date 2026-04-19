# EcoFlow BLE Communication Flow (C# Implementation)

Source: `EcoFlowMonitor.Core/Client/Ble/{BleScanner,BleMonitor,BleTransport}.cs`, `Protocol/{BleCrypto,BlePacketBuilder,BlePacketParser,BleDispatcher,BleProtoMapper,ProtobufDecoder}.cs`, `Core/Platform/IBleAdapter.cs`, `App/Services/MonitorOrchestrator.cs`.

## Actors
- **App** — Avalonia desktop client (this codebase)
- **Platform BLE adapter** — `IBleAdapter` (CoreBluetooth on macOS, Win32/WinRT on Windows, BlueZ on Linux). Surfaces a uniform scan + GATT connection API.
- **EcoFlow device** — Delta 3 / Delta 3 Max station, advertising over BLE and serving one of two GATT services (RFCOMM-style or Nordic UART).

Unlike the cloud flow, **no EcoFlow servers are involved**. Everything happens directly between the host PC's Bluetooth radio and the station. The only "identity" piece carried over from the cloud path is the `userId` string, which the device hashes with the serial number to authenticate the session.

---

## Phase 1 — Identity Bootstrap (no network)

**Trigger:** `MonitorOrchestrator.StartBleMonitor(device, state)` for any device whose `ConnectionMode` is `Ble` or `Auto` (with BLE info present).

1. App resolves a `userId` via `MonitorOrchestrator.GetUserId()`:
   - If `_config.CloudUserId` is non-empty (set by a prior REST login), use it.
   - Otherwise fall back to `_config.LocalUserId` — a random GUID generated once via `LoginViewModel.UseBleOnly()` (or `MergeBleScanResult`) and persisted to `config.json`. This makes BLE-only operation possible with no EcoFlow account.
2. If both are empty, `StartBleMonitor` aborts silently — there is no anonymous BLE auth.
3. App constructs a `BleMonitor(deviceConfig, deviceState, userId, IBleAdapter, logger, loggerFactory)`. Scan/connect work runs on a background `Task.Run`; results surface via the `StateChanged` event.

The user identity is never sent in plaintext over the air — only `MD5(userId + serialNumber)` is sent during Phase 5 authentication.

---

## Phase 2 — Advertisement Discovery (BLE Scan)

`BleScanner.StartScanAsync()` is used for the **interactive add-device wizard**. `BleMonitor` runs its own internal short scan inside `ConnectAndAuthAsync` to make sure the platform stack has cached the peripheral handle before connecting (the macOS CoreBluetooth address cache does not survive app restarts).

1. App → Adapter: `IBleAdapter.StartScanAsync(ct)`.
2. Adapter → App: `AdvertisementReceived` events (`BleAdvertisement` carrying `Name`, `DeviceId`, `ManufacturerId`, `ManufacturerData`, `Rssi`).
3. `BleScanner.OnAdvertisementReceived` filters with **either** condition:
   - `Name` starts with `"EF-"` or `"Ecoflow"` (case-insensitive), OR
   - `ManufacturerId == 46517` (EcoFlow's Bluetooth SIG vendor ID).
4. If the advertisement matches **and** carries ≥18 bytes of manufacturer data, the scanner parses:
   ```
   data[0]      → protocolVersion (commonly 3)
   data[1..17]  → 16-byte ASCII serial number (right-trimmed of NULs)
   data[22]     → encryptionType = (data[22] >> 3) & 0x07   // commonly 1 or 7
   ```
   If manufacturer data is absent/short, the device address is used as a placeholder serial number and `protocolVersion=3, encryptionType=7` are assumed.
5. Each unique serial fires `DeviceDiscovered` exactly once (deduped via `_seen` HashSet).
6. The wizard view-model marshals the discovery to the UI; user picks one → `MonitorOrchestrator.MergeBleScanResult(...)` writes the BLE fields (`BleAddress`, `BleEncryptionType`, `BleProtocolVersion`, `BleName`) into the device's `DeviceConfig` and saves `config.json`.

**Key implication:** the encryption type is decided by the **device itself** at advertisement time — the app does not negotiate it. Older Delta units advertise type 1; current Delta 3 / Delta 3 Max advertise type 7.

---

## Phase 3 — GATT Connect & Subscribe

`BleMonitor.ConnectAndAuthAsync(ct)` is wrapped in a Polly `ResiliencePipeline` (exponential retry 2 s → 5 min, jitter, infinite attempts; **no circuit breaker** — BLE has no shared rate-limited resource to protect).

FSM (`Stateless`): `Idle → Scanning → Connecting → Authenticating → Streaming`, with `Retrying` / `Error` branches.

1. **Pre-connect scan** — App spins up an 8-second scan via the same `IBleAdapter` to populate the platform cache. Cancellation/timeout is expected and swallowed.
2. **Fresh transport + crypto every attempt** — `_transport?.Dispose()` is called unconditionally before each retry. A new `BleCryptoModern` (Type 7) is constructed alongside; this guarantees no stale ECDH session key is reused after a disconnect (the comment calls it "Pitfall 4 prevention").
3. App → Adapter: `IBleAdapter.CreateConnection()` returns an `IBleGattConnection`. `BleTransport.ConnectAsync` calls `connection.ConnectAsync(deviceAddress, ct)`.
4. **Service/characteristic discovery (try-then-fallback):**
   - First attempt: subscribe to the RFCOMM-like service:
     ```
     Service: 00000001-0000-1000-8000-00805f9b34fb
     Notify : 00000003-0000-1000-8000-00805f9b34fb
     Write  : 00000002-0000-1000-8000-00805f9b34fb
     ```
   - On any failure, fall back to the Nordic UART service:
     ```
     Service: 6e400001-b5a3-f393-e0a9-e50e24dcca9e
     Notify : 6e400003-b5a3-f393-e0a9-e50e24dcca9e
     Write  : 6e400002-b5a3-f393-e0a9-e50e24dcca9e
     ```
   - Whichever subscribe succeeds first becomes `_activeServiceUuid` / `_activeWriteUuid` for the rest of the session.
5. FSM fires `Connected` → state `Authenticating`. From this point all device-bound traffic uses `_connection.WriteAsync(writeUuid, ...)`; all device-sourced bytes arrive via `IBleGattConnection.NotificationReceived` → `BleTransport.OnNotification`.

Every notification is appended to a single `MemoryStream _buffer` under a lock and re-parsed each time, because BLE notifications can split a logical frame across MTU boundaries.

---

## Phase 4 — Encryption Setup

The path branches on `BleEncryptionType` from the saved config (originally read from the advertisement in Phase 2).

### Type 1 — legacy AES-256-CBC (older Delta units)

Stateless. No on-air handshake. `BleCryptoLegacy(serial)`:

- `key`  = `MD5(serial)` doubled to 32 bytes (concat with itself) → AES-256.
- `iv`   = `MD5(reverse(serial))` → 16 bytes.
- Padding = none (caller right-pads to 16-byte boundary; decryption returns the padded buffer).

### Type 7 — ECDH SECP160r1 → derived AES-128-CBC (Delta 3 / Delta 3 Max)

Multi-step on-air handshake driven by `BleMonitor.PerformEcdhHandshakeAsync()` using BouncyCastle for the EC math. Frames during the handshake are sent as `frameType=0x00` (unencrypted command). The signal that the handshake is in progress is `_handshakeTcs` — a `TaskCompletionSource<byte[]>` set by `BleTransport.RawFrameReceived` (every successfully framed-and-CRC-checked notification fires this, regardless of encryption state).

| Step | Direction | Frame contents |
|------|-----------|----------------|
| 1 | App generates SECP160r1 keypair | (local) |
| 2 | App → Device | `[0x01, 0x00, X(20B), Y(20B)]` wrapped in a `5A5A` frame, frameType=0x00. After a 500 ms delay to let notifications settle. |
| 3 | Device → App | `[status, ecdhType, devicePub(40B for SECP160r1)]`. The C# code reconstructs the EC point by prefixing `0x04` (uncompressed) and decoding via the curve. |
| 4 | App computes shared secret | `sharedPoint = devicePub.Q.Multiply(privateKey.D); sharedSecret = sharedPoint.AffineXCoord` |
| 5 | App sets *initial* AES-128 | `key = sharedSecret[0..16]`, `iv = MD5(sharedSecret)` (`BleCryptoModern.SetInitialKey`) |
| 6 | App → Device | `[0x02]` wrapped in a `5A5A` frame, frameType=0x00 (session-key request) |
| 7 | Device → App | `[0x02, <ciphertext>]`. App decrypts `<ciphertext>` with the *initial* key → 18 bytes plaintext: `srand[0..16] ‖ seed[0..2]`. |
| 8 | App derives final session key | `pos = seed[0]*0x10 + ((seed[1]-1) & 0xFF)*0x100`; concatenate `keydata[pos..pos+8] ‖ keydata[pos+8..pos+16] ‖ srand[0..8] ‖ srand[8..16]` (32 bytes), then `sessionKey = MD5(...)`. |
| 9 | App installs final crypto | `SetSessionKey(sessionKey, MD5(sharedSecret))` then `BleTransport.SetCrypto(modern)`. From here every non-zero `frameType` frame is AES-128-CBC encrypted with PKCS7 padding (decryption falls back to no-padding if PKCS7 fails — some device responses ship without it). |

`keydata` is a fixed 65,280-byte lookup table embedded in the assembly as `Protocol/keydata.b64` (loaded once by `BleKeyData` static ctor). It is the device-side ROM table the EcoFlow firmware uses to gate session key derivation; without it the modern Type 7 channel cannot be opened.

A 10-second `kexCts` timeout protects each TCS await — failure escalates to Polly retry with a fresh keypair.

---

## Phase 5 — Authentication

`BleMonitor.SendAuthAsync(sn, ct)`:

1. **Auth-status probe** — App → Device packet `(src=0x21, dst=0x35, cmdSet=0x35, cmdId=0x89, payload=empty)`, wrapped with `frameType=0x01` and crypto.Encrypt if a session is active. Then a hard `Task.Delay(1000)` to let the device respond / settle.
2. **Auth challenge** — `BlePacketBuilder.BuildAuthPacket(userId, sn, protocolVersion)`:
   - `payload = Encoding.UTF8.GetBytes( Convert.ToHexString( MD5( UTF8(userId + sn) ) ) )` — uppercase hex ASCII (32 chars), matching the `ha-ef-ble` Python reference.
   - Outer packet: `(src=0x21, dst=0x35, cmdSet=0x35, cmdId=0x86)`.
   - Wrapped with `frameType=0x01`; encrypted with the active crypto.
3. App → Device: write `authFrame`. Set `_authTcs = new TaskCompletionSource<bool>()`.
4. Wait up to **10 seconds** for `OnPacketReceived` to see `(cmdSet=0x35, cmdId=0x86)` and resolve the TCS:
   - `success = packet.Payload.Length == 0 || packet.Payload[0] == 0x00`
5. **Tolerant timeout** — if no response arrives within 10 s but cancellation was *not* requested, the code logs a warning and proceeds anyway: "some devices skip explicit response". A returned `false` does throw `InvalidOperationException("BLE authentication rejected")`, which Polly retries.
6. FSM fires `Authenticated` → state `Streaming`.

The MD5 challenge means the device only ever sees a hash; a wrong `userId` simply produces no telemetry. There is no rolling counter or replay protection — the same auth packet for the same `(userId, sn)` always works.

---

## Phase 6 — Telemetry Streaming

The device starts pushing periodic encrypted notifications immediately after auth. No "wake" command equivalent to the MQTT path is needed.

For each notification `BleTransport.OnNotification(data)` runs under `_bufferLock`:

1. Append bytes to `_buffer`.
2. `ProcessBuffer()` calls `BlePacketParser.TryParseFrame` repeatedly:
   - Find the next `0x5A 0x5A` prefix in the buffer.
   - Read frame: `[5A 5A] [frameType<<4] [0x01] [len:u16LE] [payload(len-2 bytes)] [CRC16:u16LE]`.
   - Verify CRC16-Modbus over header+payload; mismatched frames are dropped (logged at Debug).
   - On incomplete data, leave bytes in the buffer for the next notification.
3. Decrypt:
   - `frameType == 0x00` → unencrypted, payload passed through (used during handshake).
   - `frameType != 0x00` and `_crypto != null` → `_crypto.Decrypt(payload)` (AES-128-CBC for Type 7, AES-256-CBC for Type 1). Decrypt failures are logged and the frame skipped.
4. `RawFrameReceived` fires (used by handshake TCS in Phase 4).
5. `BlePacketParser.ParsePacket(decrypted)`:
   - Magic `0xAA`, version (`2` = 16-byte header, `3+` = 18-byte header with extra `dsrc/ddst`).
   - Header CRC8 verified over first 4 bytes.
   - Payload extracted; if `seq[0] != 0` the payload is XOR-decrypted byte-wise with key `seq[0]` (Delta-family quirk on top of the AES layer).
   - Returns `BlePacket { Version, ProductByte, Sequence, Src, Dst, DeltaSrc, DeltaDst, CmdSet, CmdId, Payload }`.
6. `PacketReceived` fires → `BleMonitor.OnPacketReceived(packet)`.
7. `BleDispatcher.Dispatch(packet, …)` routes by `(Src, CmdSet, CmdId)`:

   | Src | CmdSet | CmdId | Decoder | Output |
   |-----|--------|-------|---------|--------|
   | `0x02` | `0xFE` | `0x15` or `0x16` | `BleProtoMapper.MapDelta3Display` | `BmsData` + `DisplayData` + `EmsData` (single Delta 3 protobuf carries everything) |
   | any | `0x35` | any | (auth response — handled by `BleMonitor` directly, not as data) | — |
   | any | `0x01` | `0x52` | (device time-sync request — currently logged & ignored) | — |
   | `0x0B` *or* (`0x03`/`0x20`/`0x32`) | — | — | `ProtobufDecoder.DecodeBms` (legacy MQTT-style) | `BmsData` |
   | `0x02` | `0x20` | — | `ProtobufDecoder.DecodeDisplay` | `DisplayData` |
   | `0x03` | `0x20` | — | `ProtobufDecoder.DecodeEms` | `EmsData` |
   | other | other | other | — (logged "unhandled" at Debug) | — |

8. Decoded fragments are merged into `DeviceState` under `_state.SyncLock` via `MergeBms` / `MergeDisplay` / `MergeEms`. Each merge **only overwrites fields that have actual values**, preserving previous data when the device sends a partial update.
9. `_state.Power = PowerStateMachine.Update(...)` recomputes `PowerStatus`.
10. `StateChanged` event fires (outside the lock) with `(state, previousPower)` on the BLE callback thread.

---

## Phase 7 — App-side Fan-out

Identical to the cloud path: `MonitorOrchestrator.OnStateChanged(entry, e)` runs trigger evaluation, action dispatch, history persistence, event-log writes, and re-raises `DeviceUpdated` with `Source = "BLE"`. The UI marshal happens in `DashboardViewModel.OnDeviceUpdated` via `Dispatcher.UIThread.Post(...)`.

**Rule-firing hook:** feature 001 wires the orchestrator's action-dispatch into the async `ActionRunner` (bounded channel + concurrency cap) and persists every action attempt to the `rule_firings` / `rule_firing_actions` tables in `history.db`. See `docs/ecoflow-cloud-flow.md` § Phase 6 for the details — the path is channel-agnostic (BLE or cloud), which is why it lives in a single place.

---

## Phase 8 — Disconnect / Reconnect

- Adapter → App: GATT disconnect surfaces as the underlying `_connection` becoming `IsConnected = false`. The next `WriteAsync` or buffered notification will throw, falling out of `ConnectAndAuthAsync`.
- The Polly pipeline catches the exception, fires `RetryScheduled` on the FSM (state → `Retrying` with the planned delay), and re-enters `ConnectAndAuthAsync`.
- Because step 2 of the pipeline disposes `_transport` and rebuilds `_crypto`, the **next attempt does a full GATT connect + ECDH handshake again** — there is no fast-path resume.
- `BleMonitor.StopAsync()` fires `Stop` → `Idle`, cancels `_cts`, calls `_transport.DisconnectAsync()`. `Dispose()` cancels and disposes both transport and CTS.

---

## Frame & Packet Reference

### Wire frame (transport layer, 0x5A5A)
```
[0x5A 0x5A] [frameType<<4] [0x01] [len:u16LE] [payload(len-2 bytes)] [CRC16:u16LE]
                                                                     ^^ CRC over header+payload (Modbus)
frameType: 0x00 = unencrypted, 0x01 = encrypted (AES per crypto session)
```

### Application packet (0xAA, after frame decrypt)
```
v3+ layout (18-byte header):
[0xAA] [version] [payloadLen:u16LE] [CRC8(first 4 bytes)] [productByte=0x0D]
[seq(4)] [0x00 0x00] [src] [dst] [dsrc] [ddst] [cmdSet] [cmdId]
[payload(payloadLen)] [CRC16:u16LE]

If seq[0] != 0: payload is additionally XOR'd byte-wise with seq[0].
v2 layout omits dsrc/ddst (16-byte header).
```

### GATT services / characteristics (try in order)

| Variant | Service UUID | Notify UUID | Write UUID |
|---------|--------------|-------------|------------|
| RFCOMM-style | `00000001-0000-1000-8000-00805f9b34fb` | `00000003-...` | `00000002-...` |
| Nordic UART | `6e400001-b5a3-f393-e0a9-e50e24dcca9e` | `6e400003-...` | `6e400002-...` |

### Auth packet
```
src=0x21  dst=0x35  cmdSet=0x35  cmdId=0x86
payload = ASCII( UPPERCASE_HEX( MD5( UTF8(userId + serialNumber) ) ) )   // 32 chars
frameType = 0x01 (encrypted with active crypto session)
```

### Auth-status probe (sent before auth)
```
src=0x21  dst=0x35  cmdSet=0x35  cmdId=0x89  payload=empty
```

### ECDH handshake messages
```
App → Device  pubkey:        [0x01 0x00 X(20) Y(20)]                          frameType=0x00
Device → App  pubkey reply:  [status ecdhType pubkey(40)]                      frameType=0x00
App → Device  session req:   [0x02]                                            frameType=0x00
Device → App  session reply: [0x02 <ciphertext>]  → decrypt → srand(16) seed(2)  frameType=0x00
```

## Key Constants
- BLE frame prefix: `0x5A 0x5A`
- BLE packet prefix: `0xAA`
- Header CRC: CRC8 over first 4 packet bytes
- Frame/packet CRC: CRC16 (little-endian, Modbus polynomial)
- EcoFlow manufacturer ID: `46517`
- Advertisement name filter: `EF-*` or `Ecoflow*` (case-insensitive)
- Pre-connect scan timeout: `8 s`
- ECDH handshake timeout: `10 s` per round
- Auth response timeout: `10 s` (proceed-on-timeout tolerated)
- ECDH curve: SECP160r1 (BouncyCastle `CustomNamedCurves.GetByName("secp160r1")`)
- Type 1 cipher: AES-256-CBC, no padding, key = `MD5(sn) ‖ MD5(sn)`, IV = `MD5(reverse(sn))`
- Type 7 cipher: AES-128-CBC, PKCS7 (decrypt falls back to none), key = derived session key (see Phase 4 step 8)
- Embedded key table: `Protocol/keydata.b64` (65,280 bytes, base64-encoded)
- Polly retry: exponential 2 s → 5 min, jittered, infinite attempts, **no** circuit breaker
- Auth payload format: uppercase MD5 hex of `userId + serialNumber` as UTF-8 ASCII
