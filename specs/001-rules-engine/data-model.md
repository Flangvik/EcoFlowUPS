# Phase 1 — Data Model

Describes the entities, their fields, relationships, lifecycle, and
validation rules. Aligned with the spec's **Key Entities** section and
the clarifications captured in `spec.md`.

---

## Entity: Rule

A user-authored automation scoped to a single device.

| Field | Type | Notes |
|---|---|---|
| `id` | string (GUID) | Immutable, stable across edits. |
| `name` | string | User-visible label. 1–80 chars, unique per device. |
| `deviceSerialNumber` | string | FK to `DeviceConfig.SerialNumber`. `null` while orphaned. |
| `enabled` | bool | Default `true`. |
| `trigger` | `TriggerConfig` (polymorphic) | Exactly one. |
| `actions` | `ActionConfig[]` (polymorphic, ordered) | 1+ entries. Sequential within rule. |
| `cooldownSeconds` | int? | Optional per-rule override; null means "use trigger default". Default trigger cooldown = 300 s for level triggers, 0 s for edge triggers. |
| `createdAt` | timestamp | Set on first save. |
| `modifiedAt` | timestamp | Set on every save. |
| `orphaned` | bool | Computed: `true` when `deviceSerialNumber` does not resolve to any `DeviceConfig`. |

**Lifecycle:**
```
Draft (in editor) → Saved (enabled=true/false) → [edit] → Saved
                                              → [delete] → gone
                                              → [device removed] → Orphaned (enabled forced false)
                                              → [user reassigns] → Saved
```

**Validation:**
- `name` required, not whitespace, ≤80 chars.
- `trigger` required.
- `actions` required, at least 1, at most 20.
- If any action requires elevation on the current OS AND
  `IElevationService.IsElevated == false`, the editor surfaces a warning
  (FR-026, FR-027) but does NOT block save.

**Storage:** `config.json` → `Devices[].Rules` (existing location; no
schema break). Orphaned rules live under a new top-level
`OrphanedRules` list so they survive device deletion.

---

## Entity: TriggerConfig (polymorphic base)

Specifies *when* a rule fires.

**Common fields (all variants):**
| Field | Type | Notes |
|---|---|---|
| `type` | discriminator string | `"PowerLost"`, `"PowerRestored"`, `"BatteryBelow"`, etc. |
| `cooldownSeconds` | int? | Optional trigger-level cooldown override. |

**Edge variants** (fire once per transition, no threshold):
- `PowerLost`
- `PowerRestored`
- `AcPlugged`
- `AcUnplugged`
- `DeviceOffline` (fires when `Now - LastDataReceived > window`)
- `DeviceOnline` (fires on first data after `DeviceOffline`)

**Level variants** (fire while condition holds, throttled by cooldown):
| Variant | Extra fields | Semantics |
|---|---|---|
| `BatteryBelow` | `thresholdPct: int (0–100)` | Fires while `BatteryPct < threshold`. |
| `BatteryAbove` | `thresholdPct: int (0–100)` | Fires while `BatteryPct > threshold`. |
| `TimeRemainingBelow` | `thresholdMinutes: int (1–1440)` | Fires while `RemainMin < threshold` AND device is discharging. |
| `TempAbove` | `thresholdC: decimal (–40..120)` | Fires while `TempC > threshold`. |
| `TempBelow` | `thresholdC: decimal (–40..120)` | Fires while `TempC < threshold`. |
| `InputWattsBelow` | `thresholdW: int (0..3600)` | Fires while `TotalInW < threshold`. |
| `OutputWattsAbove` | `thresholdW: int (0..3600)` | Fires while `TotalOutW > threshold`. |

**Composite / windowed:**
| Variant | Extra fields | Semantics |
|---|---|---|
| `DeviceOffline` | `windowSeconds: int (30..86400)` (default 300) | Fires once after the window elapses with no updates on any channel. |

**Validation:**
- `type` must match a known discriminator.
- `cooldownSeconds`, when set, must be 0..86400.
- Threshold values must be within the ranges listed above.

**Persistence:** Polymorphic JSON with explicit `type` field.

---

## Entity: ActionConfig (polymorphic base)

Specifies *what* happens when a rule fires.

**Common fields (all variants):**
| Field | Type | Notes |
|---|---|---|
| `type` | discriminator string | `"WriteLog"`, `"Notification"`, `"Shutdown"`, …, `"Webhook"`, `"RunCommand"`. |
| `timeoutMs` | int? | Per-action timeout. Default 10000 for Webhook, 30000 for RunCommand, 5000 for others. Max 60000. |

**Variants (existing, carried forward):**
| Variant | Fields | Notes |
|---|---|---|
| `WriteLog` | `message: string` (template) | Writes to `Logger.Log`. |
| `Notification` | `title: string` (template), `body: string` (template) | Dispatched via `INotificationService`. |
| `Shutdown` / `Hibernate` / `Sleep` | (none) | Via `IPowerActionService`. Requires elevation on Windows and Linux. |
| `RunScript` | `scriptPath: string`, `arguments: string` (template) | Legacy; new rules SHOULD prefer `RunCommand`. Kept for backward compat. |

**Variants (new in this feature):**

### `Webhook`
| Field | Type | Notes |
|---|---|---|
| `url` | string (URI) | Required. Validated as a valid absolute http/https URI. |
| `method` | enum: `POST` \| `PUT` | Default `POST`. |
| `headers` | `Dictionary<string,string>` | User-supplied. Headers matching the redaction patterns (see spec FR-021) are redacted from audit. |
| `bodyTemplate` | string | Optional. If omitted, a default JSON body is used (see `contracts/webhook-request.json`). Template variables are expanded before sending. |
| `retries` | int | Default 0. Range 0–5. |
| `retryDelayMs` | int | Default 1000. Range 100–60000. |
| `timeoutMs` | int | Default 10000. Range 1000–60000. |

**Fire semantics:**
1. Expand `bodyTemplate` (or build the default body).
2. For attempt in 0..retries:
   - Issue the HTTP request with `CancellationTokenSource.CancelAfter(timeoutMs)`.
   - On success (2xx): stop; record one audit entry per attempt plus overall outcome `success`.
   - On retriable failure (timeout, network error, 5xx, 429):
     record attempt; if attempts remain, wait `retryDelayMs`, loop.
   - On non-retriable failure (4xx other than 429): stop; record `failure`.

### `RunCommand`
| Field | Type | Notes |
|---|---|---|
| `commandWindows` | string? | Command string, used on Windows if present. Template variables expanded. |
| `commandMacOS` | string? | Command string, used on macOS. |
| `commandLinux` | string? | Command string, used on Linux. |
| `shell` | enum: `sh` \| `cmd` \| `powershell` | Default: `sh` on macOS/Linux, `cmd` on Windows. `powershell` (= `pwsh.exe`) available only on Windows. |
| `workingDirectory` | string? | Optional. Template-expanded. |
| `timeoutMs` | int | Default 30000. Range 1000–60000. |

**Fire semantics:**
1. Pick the `command*` field matching the current OS.
2. If that field is `null` or empty: record `skipped` with reason
   `"no command for $OS"` and return.
3. Expand template variables in the chosen command and
   `workingDirectory`.
4. Invoke `IShellExecutor.RunAsync(command, TimeSpan.FromMilliseconds(timeoutMs), ct)`.
5. Record outcome:
   - `exitCode == 0` → `success`.
   - `exitCode != 0 && !timedOut` → `failure` (with exit code and
     stderr head).
   - `timedOut == true` → `timeout`.

**Validation:**
- `Webhook.url`: must be absolute, scheme `http` or `https`.
- `Webhook.headers`: header names match `^[A-Za-z0-9!#$%&'*+\-.^_`|~]+$`.
- `RunCommand`: at least one of `commandWindows`, `commandMacOS`,
  `commandLinux` must be non-empty.

---

## Entity: RuleFiring (audit row)

One entry per time a rule fires (test or real). Persisted in SQLite.

| Field | Type | Notes |
|---|---|---|
| `id` | long | Auto. |
| `ts` | long | Unix seconds, UTC. |
| `ruleId` | string | Snapshot — survives rule deletion. |
| `ruleName` | string | Snapshot at fire time. |
| `deviceSerialNumber` | string | Snapshot. |
| `triggerType` | string | Discriminator. |
| `triggerValueJson` | string (JSON) | Snapshot of trigger context at fire time — all template variables. |
| `isTest` | bool | `true` if fired from "Test rule now". |
| `actions` | `RuleFiringAction[]` (child rows) | Ordered by `ordinal`. |

## Entity: RuleFiringAction (audit child row)

| Field | Type | Notes |
|---|---|---|
| `id` | long | Auto. |
| `firingId` | long | FK. |
| `ordinal` | int | Position of this action in the rule. |
| `actionType` | string | Discriminator. |
| `outcome` | enum: `success`, `failure`, `skipped`, `timeout`, `dropped` | See semantics under each action above. |
| `durationMs` | int | Wall-clock duration of the attempt (sum of retries for webhook). |
| `errorText` | string? | First 512 chars of error (HTTP body excerpt, shell stderr, exception message). |
| `detailJson` | string? | Type-specific detail: Webhook → `{attempts, httpStatus, responseBodyExcerpt}`; RunCommand → `{exitCode, stderrHead, stdoutHead}`. |

**Retention:** `DELETE FROM rule_firings WHERE ts < ?` where `?` is
`now − retentionDays * 86400`. Default `retentionDays = 30`,
configurable in Settings. Pruning runs once on app start + at most
once every 24 hours thereafter.

**Relationships:**

```
DeviceConfig 1 ── * Rule
Rule 1 ── 1 TriggerConfig
Rule 1 ── 1..* ActionConfig   (ordered)
Rule 1 ── * RuleFiring         (audit; not FK-linked — rule may be deleted)
RuleFiring 1 ── * RuleFiringAction
```

---

## Entity: DeviceStateSnapshot (in-memory only)

Captured by `TriggerEvaluator` at the moment a trigger fires. Passed to
every action in the same firing so late-running actions see consistent
values. Not persisted as a row (its JSON serialization is stored inside
`RuleFiring.triggerValueJson`).

| Field | Type | Source |
|---|---|---|
| `deviceName` | string | `DeviceConfig.DisplayName` |
| `deviceSn` | string | `DeviceConfig.SerialNumber` |
| `batteryPct` | float? | `DeviceState.Bms.BatteryPct` |
| `remainMin` | int? | `DeviceState.Bms.RemainMin` |
| `tempC` | float? | `DeviceState.Bms.TempC` |
| `totalInW` | int? | `DeviceState.Display.TotalInW` |
| `totalOutW` | int? | `DeviceState.Display.TotalOutW` |
| `acPluggedIn` | bool? | `DeviceState.Display.AcPluggedIn` |
| `chargeState` | int? | `DeviceState.Ems.ChgState` |
| `powerStatus` | enum | `DeviceState.Power.Status` |
| `timestamp` | DateTime | `DateTime.UtcNow` at evaluation. |

Template variables expand from this snapshot, with `<unknown>` for any
`null` value.
