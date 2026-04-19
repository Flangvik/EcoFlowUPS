# Feature Specification: Rules Engine (Full)

**Feature Branch**: `001-rules-engine`
**Created**: 2026-04-19
**Status**: Draft
**Input**: User description: "Implement a full one rules engine that allowes commands, webhook, notifications, scripts, etc to be execute or ran when differnt events from a given ecoflow device happens. Eg run a script when power is close to empty, x% or x minutes from going out, etc etc. needs to have cross platform support in mind."

## Clarifications

### Session 2026-04-19

- Q: Webhook retry policy on transient failure → A: User-configurable per
  action: `retries` count and `retryDelayMs` fields on the Webhook action
  config; default is 0 retries (fire-and-forget).
- Q: Secret redaction scope in the audit log → A: Headers only. URL
  (including path and query string) and body template are stored verbatim.
  Users are responsible for keeping secrets in headers, not in URLs or
  body text.
- Q: Privilege model for destructive commands → A: App runs non-elevated;
  actions use whatever privileges the current process has. When a rule
  uses a known-elevation-requiring action (e.g. `Shutdown`, `Hibernate`,
  `RunCommand` with a command in a curated "requires admin" list) **and**
  the app is currently running without elevation, the rule editor MUST
  surface an inline warning with OS-specific instructions for how to
  re-launch / auto-start the app elevated (Windows: "Run as
  administrator" + Task Scheduler elevated-autostart recipe; macOS:
  `sudo launchctl load -w` for a LaunchDaemon; Linux: systemd --user vs
  system unit + polkit hint). The app never prompts for elevation at
  runtime.
- Q: Max concurrent in-flight actions across all rules → A: Global cap of
  8 concurrent action executions with a bounded FIFO queue for overflow;
  value is configurable in Settings (min 1, max 64). Rules still observe
  the existing per-rule serial-within-rule rule.

## Context

The app today already has a minimal rules system: four triggers (`PowerLost`,
`PowerRestored`, `BatteryBelow`, `TimeRemainingBelow`) and six actions
(`RunScript`, `Shutdown`, `Hibernate`, `Sleep`, `Notification`, `WriteLog`),
stored per-device in `config.json`, fired from `MonitorOrchestrator` and
dispatched by `ActionRunner`. Rule authoring today is *JSON-only* — no UI —
and the action set is deliberately narrow.

This feature expands that into a **full rules engine**: more event types to
fire on, more action types (notably HTTP webhooks and template-expanded
shell/PowerShell commands), an in-app rule editor, and an audit trail of
what fired when. "Full" in this context means the user can express every
automation they reasonably want against a power station — tell home
automation when power drops, run a platform-specific script when battery
is low, notify a Slack channel when charge is restored — without editing a
JSON file by hand.

The feature MUST work on Windows, macOS, and Linux with the same rule
semantics.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Call a webhook when power status changes (Priority: P1)

A user running the app on a home-server box wants their home-automation
system to know whenever the EcoFlow is on mains vs battery. They add a rule
on each of "PowerLost" and "PowerRestored" that hits a webhook URL they own,
posting a JSON body containing the device name, the new status, battery
percent, and estimated remaining minutes.

**Why this priority**: Webhooks are the single highest-leverage addition —
one rule can fan out to every other tool the user owns (Home Assistant, IFTTT,
Slack, ntfy, custom dashboards). Without this, rules are limited to things
that run *on the same machine as the app*, which is a fraction of what users
need.

**Independent Test**: Point the webhook at a local catcher
(`nc -l 8080`, `webhook.site`, etc.), unplug the station or pull the
AC-plugged bit in the simulator, and verify the POST arrives within a few
seconds with the expected JSON body. No other rule features need to work for
this to deliver value.

**Acceptance Scenarios**:

1. **Given** a rule "on PowerLost → POST to https://home.example/hooks/ups"
   exists and is enabled,
   **When** the device transitions from Charging (or Idle) into PowerLost,
   **Then** the configured URL receives exactly one POST with a JSON body
   containing at minimum `{device, status, batteryPct, remainMin,
   totalInW, totalOutW, timestamp}` within 5 seconds of the state change.
2. **Given** the webhook target is unreachable or returns a 5xx,
   **When** the rule fires,
   **Then** the failure is recorded in the event log with the HTTP error
   and the app continues operating normally (no crash, no backlog stall).
3. **Given** a rule has custom headers configured (e.g.
   `Authorization: Bearer …`),
   **When** the rule fires,
   **Then** those headers are sent verbatim with the request.

### User Story 2 — Run a platform-specific script when battery is low (Priority: P1)

A user on Windows wants `shutdown-and-email-me.ps1` to run when the station
drops below 10%; the same rule, authored once, should run
`shutdown-and-email-me.sh` when they move the app to their Mac or Linux box.
The script path and interpreter differ per OS; the *rule* is the same.

**Why this priority**: Running a user-provided script on threshold is the
second most-asked-for automation and is the one the product's current
"RunScript" action *almost* does — it just can't express per-OS paths cleanly
and doesn't expand template variables inside script arguments. Closing that
gap makes the existing action genuinely useful.

**Independent Test**: Configure a rule "on BatteryBelow 10% → run
`./test.sh --device {device} --pct {battery}`" (Linux/macOS) or the `.ps1`
equivalent on Windows, point a test script at a tempfile write, simulate
battery drop below 10%, and verify the file content contains the expanded
device name and numeric percentage.

**Acceptance Scenarios**:

1. **Given** a "Run Command" rule with per-OS command strings set for
   Windows / macOS / Linux,
   **When** the rule fires on any OS,
   **Then** the command matching the running OS is executed through the
   native shell (cmd/PowerShell on Windows, sh on macOS/Linux) with all
   template variables (`{device}`, `{battery}`, `{remain}`, `{status}`,
   `{in_w}`, `{out_w}`) expanded to their current values before execution.
2. **Given** the command exits non-zero,
   **When** the rule fires,
   **Then** the non-zero exit code plus the first N lines of stderr are
   recorded in the event log and the rule is considered "fired with
   warning" rather than "failed".
3. **Given** a Run Command rule was authored on Windows (only
   `commandWindows` populated) and the user opens the same `config.json`
   on Linux,
   **When** the rule fires on Linux,
   **Then** it does NOT execute anything, logs a clear "no command
   configured for linux" warning, and does not count as a misfire.

### User Story 3 — Add new triggers beyond battery/power edges (Priority: P2)

A user wants to know when the station's internal temperature exceeds 55 °C
(suggesting a fan problem), when the AC line is unplugged from the station
(distinct from AC power loss from the grid), and when the station goes
offline from the app's point of view for more than 5 minutes (MQTT + BLE
both silent).

**Why this priority**: These triggers serve a narrower audience than the
"classic four" but are cheap to add once the trigger registration
mechanism is generalized for story 1/2, and they convert the app from a
"UPS watchdog" into a "station health monitor".

**Independent Test**: Spoof a BMS payload with `TempC = 60.0`, confirm a rule
targeting `TempAbove 55` fires exactly once, then confirm that cooling back
to 50 °C and reheating fires the rule a second time (after the cooldown).

**Acceptance Scenarios**:

1. **Given** a rule "on TempAbove 55°C → notification 'Station hot'",
   **When** the reported BMS temperature crosses from ≤55 to >55,
   **Then** the notification fires exactly once per crossing, with the
   configured cooldown between repeats.
2. **Given** a rule "on AcUnplugged → webhook",
   **When** `DisplayData.AcPluggedIn` transitions from `true` to `false`,
   **Then** the webhook fires, distinctly from a `PowerLost` event (which
   may also fire if input watts hit zero).
3. **Given** a rule "on DeviceOffline 5min → notification",
   **When** no data has been received from the device for the configured
   window on either the cloud or BLE channel,
   **Then** the notification fires once, and a matching `DeviceOnline`
   event is available for a matching restore rule.

### User Story 4 — Author, edit, test, and disable rules inside the app (Priority: P2)

A user who has never opened `config.json` can add a webhook rule entirely
from the app UI: pick a device, pick a trigger type, set the parameters,
pick one or more actions, save. They can enable/disable individual rules
with a toggle, rename them, duplicate them, and run a "Test rule now"
action that synthesises the trigger context and invokes the actions so
they know the webhook reaches their server before a real power event ever
happens.

**Why this priority**: Without an editor, the feature is power-user-only.
Users on desktop won't hand-edit JSON. However, stories 1–3 already deliver
value to JSON-editing users, so UI is one step behind on priority.

**Independent Test**: With no prior rules configured, use only the UI to
author a single "PowerLost → log a line" rule, hit the "Test rule now"
button, and verify the log line appears without any power event actually
happening.

**Acceptance Scenarios**:

1. **Given** a user on the Automation settings screen,
   **When** they click "Add rule", fill in name/device/trigger/actions, and
   click Save,
   **Then** the rule appears in the list, is persisted to `config.json`,
   and is live for the next matching event without restarting the app.
2. **Given** a user toggles a rule's "Enabled" switch off,
   **When** the matching event subsequently fires,
   **Then** none of the rule's actions run, and the rule is clearly shown
   as "disabled" in the UI.
3. **Given** a user selects "Test rule now",
   **When** they confirm in the dialog,
   **Then** the rule's actions run exactly once with a synthetic trigger
   context (plausible values for all template variables) and the result is
   shown inline (success/failure per action), marked as a test so it does
   not pollute the real audit trail.

### User Story 5 — Audit what fired and what didn't (Priority: P3)

A user who just had an outage wants to see, for the last 24 hours, every
rule that fired, the trigger values that fired it, and whether each action
succeeded or failed. When a webhook fails, they want enough detail to
diagnose (HTTP status + first line of response body). They do NOT want the
audit trail to grow unbounded on disk.

**Why this priority**: Essential for trust but not blocking first use. A
user can verify rules via US1's webhook catcher for weeks before they miss
a proper audit view.

**Independent Test**: Trigger three rules (one succeeding webhook, one
failing webhook, one successful script) and verify a History screen shows
all three entries with correct timestamps, per-action success/failure, and
readable trigger context. Verify entries older than 30 days get pruned.

**Acceptance Scenarios**:

1. **Given** a rule has fired 12 times in the past week with varying
   outcomes,
   **When** the user opens the "Rule History" view,
   **Then** they see 12 entries sorted by time descending, each listing
   rule name, trigger values at fire time, and one line per action with
   success/failure/duration.
2. **Given** a webhook action returned HTTP 403 with body "forbidden",
   **When** the user expands that history entry,
   **Then** they can see the request URL (with secrets redacted), the
   response status, and the first N characters of the response body.
3. **Given** rule history has accumulated for more than the retention
   window (default 30 days),
   **When** a periodic cleanup runs (at most daily),
   **Then** entries older than the window are removed and the cleanup
   itself is logged.

### Edge Cases

- **Flapping input**: If a value oscillates around the threshold (49%, 51%,
  49%, 51%, …), the rule MUST respect its cooldown and not fire on every
  crossing. Edge triggers (PowerLost / PowerRestored) MUST fire once per
  transition, not repeatedly while the state holds.
- **Rule fires during another rule's action**: A long-running script from
  rule A MUST NOT block rule B from firing on a subsequent event. Actions
  run concurrently relative to other rules but are serialised within a
  single rule.
- **Webhook slow-to-respond**: A user-supplied URL that hangs MUST NOT
  freeze the monitoring pipeline. Each webhook invocation has a bounded
  timeout and runs off the monitor thread.
- **Clock skew between PC and device**: Triggers that depend on
  "time remaining below X minutes" use the device-reported estimate, not
  wall-clock math, so local clock skew does not cause false fires.
- **Very short outage**: A power loss that lasts 3 seconds MUST fire
  `PowerLost` and, 3 seconds later, `PowerRestored`, as two separate
  events with the correct order. No coalescing.
- **Config file hand-edited while the app is running**: When the user
  edits `config.json` externally, the app detects the change on next save
  and merges OR prefers one side deterministically — it MUST NOT silently
  discard either set of changes.
- **Platform-specific command on wrong platform**: A rule with only a
  Windows command run on Linux must log and skip cleanly (see US2.3), not
  spawn the shell with garbage.
- **Template variable missing**: If `{remain}` is referenced in a command
  but no BMS data has arrived yet, the variable expands to a clear
  placeholder (`<unknown>`) rather than empty string or literal `{remain}`.
- **Device removed**: Rules scoped to a device that gets deleted are
  automatically disabled and marked as "orphaned" — not silently deleted,
  so the user can reassign them.
- **Action takes longer than the next trigger**: Rule actions MUST carry
  their own timeout independent of monitor cadence; a hung webhook
  doesn't delay the next `PowerLost` detection.

## Requirements *(mandatory)*

### Functional Requirements

#### Triggers

- **FR-001**: System MUST support all existing trigger types
  (`PowerLost`, `PowerRestored`, `BatteryBelow`, `TimeRemainingBelow`)
  with identical semantics to today (edge vs level + cooldown).
- **FR-002**: System MUST add new trigger types covering at minimum:
  - `BatteryAbove` (level, with cooldown) — counterpart to BatteryBelow.
  - `TempAbove` / `TempBelow` — on BMS temperature in °C.
  - `AcPlugged` / `AcUnplugged` — on AC-line-to-station transitions.
  - `InputWattsBelow` / `OutputWattsAbove` — for solar or load triggers.
  - `DeviceOffline` / `DeviceOnline` — no data received for N minutes vs
    first data after being offline.
- **FR-003**: Every trigger MUST carry a user-configurable cooldown
  (default 5 minutes for level triggers, none for edge triggers).
- **FR-004**: System MUST evaluate triggers every time device state
  changes, not on a fixed tick, so latency from "event happened" to "rule
  action starts" is bounded by the channel's natural cadence (typically
  ≤5 s for MQTT, ≤2 s for BLE).

#### Actions

- **FR-005**: System MUST retain the existing action types (`RunScript`,
  `Shutdown`, `Hibernate`, `Sleep`, `Notification`, `WriteLog`) with no
  regression in behaviour.
- **FR-006**: System MUST add a `Webhook` action that performs an HTTP
  POST to a user-configured URL with a JSON body containing the device
  state snapshot at fire time and an optional user-provided body
  template (with template variables expanded).
- **FR-007**: The `Webhook` action MUST support user-configured HTTP
  headers (arbitrary key/value pairs, e.g. `Authorization`,
  `X-Webhook-Token`), HTTP method (POST default, PUT optional), a
  per-action timeout (default 10 s, configurable up to 60 s), and a
  per-action retry policy expressed as two fields: `retries` (integer,
  default 0) and `retryDelayMs` (integer, default 1000). When
  `retries > 0`, a failed attempt (timeout, network error, HTTP 5xx,
  HTTP 429) is retried up to that many additional times, waiting
  `retryDelayMs` between attempts. Each attempt records its own outcome
  in the audit log; the rule is marked "succeeded" if any attempt
  succeeds, "failed" only after the final attempt fails.
- **FR-008**: System MUST add a `RunCommand` action that executes an
  arbitrary command via the native shell, with template variables
  expanded in both the command string and its arguments. Per-OS command
  strings (Windows / macOS / Linux) MAY be provided on a single action;
  the OS-matching string is chosen at fire time. If the matching string
  is missing, the action skips with a clear log message and the rule
  continues to run its remaining actions.
- **FR-009**: Template expansion in any action payload MUST support the
  current variables (`{device}`, `{battery}`, `{remain}`, `{status}`,
  `{in_w}`, `{out_w}`) plus additions for the new triggers:
  `{temp_c}`, `{ac_plugged}`, `{charge_state}`, `{device_sn}`.
  Unknown variables MUST expand to `<unknown>` and the action MUST still
  run.
- **FR-010**: Every action MUST complete or time out within a bounded
  window; no action may block the monitor pipeline. Actions of a single
  rule run sequentially; actions of different rules run independently,
  subject to the global concurrency cap (FR-010a).
- **FR-010a**: The system MUST cap the number of concurrently executing
  actions across all rules at a user-configurable limit (default 8,
  range 1–64, set in Settings). Actions submitted while the cap is
  saturated enqueue in a bounded FIFO queue and start as soon as a slot
  frees up. The queue MUST bound its own size (default 256 pending
  actions); overflow drops the oldest pending actions of the *same*
  rule first (deduplication), then records a warning in the audit log
  listing any truly dropped actions.

#### Rule Management

- **FR-011**: Users MUST be able to enable/disable individual rules
  without deleting them.
- **FR-012**: Users MUST be able to author new rules, edit existing
  rules (name, device, trigger, actions, cooldown, enabled), and delete
  rules, all from within the app without touching `config.json`.
- **FR-013**: The rule editor MUST validate saved rules (all required
  fields present, numeric thresholds sensible, webhook URLs well-formed)
  before allowing Save, and MUST surface the failing field inline.
- **FR-014**: Users MUST be able to duplicate a rule.
- **FR-015**: Users MUST be able to "Test rule now" — invoke the actions
  once with a plausible synthetic trigger context, marked as a test in
  the audit log.
- **FR-016**: Rules MUST be persisted in the existing `config.json`
  under the existing `Devices[].Rules` structure; hand-edited JSON MUST
  remain round-trippable through the UI.
- **FR-017**: Rules scoped to a deleted device MUST be retained and
  marked "orphaned" so the user can reassign them.

#### Audit & Observability

- **FR-018**: Every rule firing MUST append an entry to a persistent
  audit log containing: timestamp, rule name, device, trigger type &
  value, per-action outcome (success/failure/skipped/timeout), action
  duration, and error summary if applicable.
- **FR-019**: Users MUST be able to view the audit log in the app,
  sorted by time descending, filterable by device and rule.
- **FR-020**: Audit entries older than a configurable retention window
  (default 30 days) MUST be pruned automatically at most once per day.
- **FR-021**: Webhook audit entries MUST redact configured secret
  headers (anything named `Authorization`, `X-*-Token`, `X-*-Secret`,
  or user-flagged "secret") from stored history. URLs (including path
  and query string) and body templates are stored verbatim; the rule
  editor MUST surface a one-line warning ("Prefer headers for tokens")
  if the user types a Webhook URL whose query-string appears to contain
  a secret (parameter names matching `token|key|signature|sig|secret|
  password|auth|access_token`, case-insensitive).

#### Cross-Platform

- **FR-022**: All action types MUST behave identically on Windows,
  macOS, and Linux where the underlying capability exists. When a
  capability genuinely does not exist on a platform (e.g. a
  platform-specific command variant is missing), the action MUST skip
  with a clear log message, not fail silently and not crash the app.
- **FR-023**: The `Webhook` action MUST NOT depend on any OS-specific
  HTTP client configuration; it uses the same runtime HTTP stack across
  all three OSes.
- **FR-024**: Platform-specific `Shutdown`, `Hibernate`, and `Sleep`
  actions MUST continue to dispatch through the existing
  `IPowerActionService` abstraction so a new target OS is added by
  implementing one interface, not by patching the rules engine.

#### Privilege Awareness

- **FR-025**: The app MUST run unprivileged by default. Rule actions
  execute with the privileges the app process has; the app MUST NOT
  prompt for elevation at rule-fire time.
- **FR-026**: The rule editor MUST determine, at rule save time, whether
  any selected action is known to require elevation on the current OS
  (e.g. `Shutdown` and `Hibernate` on Windows; curated "known requires
  root" substrings such as `systemctl poweroff`, `systemctl hibernate`,
  `shutdown -h` in `RunCommand` commands on Linux/macOS). The existing
  `IElevationService` abstraction determines whether the running app is
  currently elevated.
- **FR-027**: When FR-026 detects the "rule needs elevation AND app is
  not elevated" combination, the editor MUST display an inline,
  non-blocking warning containing: (a) which action is affected,
  (b) why elevation is required on the current OS, and (c)
  platform-specific instructions for re-launching or auto-starting the
  app elevated (Windows: right-click → Run as administrator, plus a
  link to the Task Scheduler recipe; macOS: LaunchDaemon instructions;
  Linux: system-level systemd unit or polkit rule). The user MAY save
  the rule anyway.
- **FR-028**: At rule-fire time, if an action fails with an OS-level
  "operation not permitted" / "access denied" / UAC-declined error,
  the audit entry MUST capture that specific OS error text so the user
  can correlate it with the editor warning from FR-027.

### Key Entities

- **Rule**: A user-authored automation. Attributes: stable ID, name,
  owning device reference, enabled flag, trigger, ordered list of
  actions, rule-level cooldown override (optional), created/modified
  timestamps.
- **Trigger**: Specifies *when* a rule fires. Attributes: type
  (enum), type-specific parameters (threshold value, comparison,
  duration window), per-trigger cooldown. Trigger types split into
  "edge" (fires once per transition) and "level" (fires while condition
  holds, throttled by cooldown).
- **Action**: Specifies *what* happens when the rule fires.
  Attributes: type (enum), type-specific parameters (URL, headers, body
  template, command strings per OS, script path, notification text,
  log line template, action-level timeout).
- **Rule Firing Event**: One record per fire. Attributes: timestamp,
  rule reference, trigger context snapshot (all variable values at fire
  time), per-action result list (outcome, duration, error summary),
  "test" flag when fired from the "Test rule now" UI.
- **Device State Snapshot (used as trigger context)**: The frozen set
  of device fields that were current at the moment the trigger
  evaluated, captured so actions that run later (webhook retry, slow
  script) see consistent values.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user who has never edited `config.json` can author a
  working webhook rule end-to-end in under 2 minutes (pick device,
  pick trigger, paste URL, save, verify with Test) on any of the
  three supported OSes.
- **SC-002**: Rule fire latency — from the device state change being
  reflected in the app to the first action starting — is under 5
  seconds at the 95th percentile on the MQTT channel and under 2
  seconds on the BLE channel.
- **SC-003**: The audit trail is complete — 100 % of rule firings over
  a 24-hour period appear in the audit log with correct per-action
  outcomes; no silent failures.
- **SC-004**: A flapping signal that crosses a level threshold 60 times
  in 60 minutes fires the corresponding rule no more than 12 times
  (≤ one fire per 5-minute cooldown window).
- **SC-005**: A webhook action pointing at an unresponsive host times
  out at or under its configured timeout (default 10 s) and never
  delays the monitor pipeline for more than that same timeout.
- **SC-006**: Rules authored on one OS and synced (via `config.json`)
  to another run correctly on the second OS, producing identical
  behaviour where the capability exists, and a clear "skipped on
  $PLATFORM" log entry where it does not.
- **SC-007**: 90 % of users attempting a basic "run my script on
  low-battery" automation succeed on the first try without needing
  to read documentation beyond the Settings screen help text.
- **SC-008**: The audit log on a machine that has been running for 90
  days without cleanup never exceeds the configured retention window
  in size — no runaway growth.
- **SC-009**: With 20 rules all firing on the same event and each action
  taking up to 10 s, all actions complete (executed or cleanly queued)
  within 30 s under default settings (cap = 8), and CPU / memory
  footprint stays within 2× baseline during the burst.

## Assumptions

- The existing `MonitorOrchestrator → TriggerEvaluator → ActionRunner`
  pipeline is extended, not replaced. New trigger and action types
  plug into the existing dispatch logic.
- Rules remain **per-device**, not global. A rule that should apply to
  every device is authored once per device, or (stretch) has an
  optional `applyToAllDevices` flag that the UI may offer later. First
  cut: per-device only.
- Rule storage stays in `config.json` under `Devices[].Rules`. No new
  database is introduced for rules themselves. The audit log lives in
  the existing SQLite history store (`history.db`) as a new table.
- Webhook auth is header-based (user supplies `Authorization` or
  custom header values). HMAC-signed payloads are **out of scope for
  v1**; they can be added later without a breaking change.
- Email (SMTP) and MQTT-publish-to-user-broker actions are **out of
  scope for v1**. The user mentioned "etc"; we interpret that as
  extensibility rather than an immediate requirement.
- Rule import/export, rule templates, and sharing rules between
  machines are **out of scope for v1**.
- The Test action uses realistic synthetic values (last known state,
  or a plausible placeholder if the device has never reported) and is
  clearly tagged in the audit log; it does not attempt to coerce the
  device into the triggering state.
- Template variable syntax stays `{name}` (single braces). No new
  expression language, no `{{jinja}}`-style conditionals.
- Action ordering within a rule is preserved from the config; actions
  run sequentially in that order and a failure in action N does not
  skip actions N+1 (they still run, but their audit entry can reflect
  that a prior action failed if the user wants to chain behaviour in
  future).
- Destructive actions (`Shutdown`, `Hibernate`, `RunCommand`,
  `RunScript`) are assumed acceptable because the user explicitly
  created the rule; no second-confirmation dialog fires at runtime.
  The rule editor MAY surface a one-time "I understand this will shut
  down my computer" acknowledgement on save for those action types.
- The app is assumed to be running when rules should fire. Rules do
  not persist "unfired" events across app restarts — if the app is
  closed when power is lost, no rule fires for that loss; rules only
  respond to live state changes the monitor observes.
