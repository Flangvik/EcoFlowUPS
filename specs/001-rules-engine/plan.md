# Implementation Plan: Rules Engine (Full)

**Branch**: `001-rules-engine` | **Date**: 2026-04-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-rules-engine/spec.md`

## Summary

Extend the existing per-device rules system (`RuleConfig` + `TriggerConfig`
+ `ActionConfig` + `TriggerEvaluator` + `ActionRunner`) with: new trigger
types (`BatteryAbove`, `TempAbove/Below`, `AcPlugged/Unplugged`,
`InputWattsBelow`, `OutputWattsAbove`, `DeviceOffline/Online`), two new
action types (`Webhook` with configurable retries + headers + timeout, and
`RunCommand` with per-OS command strings), an in-app rule editor with
enable/disable/duplicate/test-now, a new audit log in the existing
`history.db`, a global concurrency cap (default 8) with a bounded FIFO
queue, and privilege-aware warnings in the rule editor when destructive
actions meet a non-elevated process.

Approach keeps the existing pipeline shape: `DeviceUpdated` →
`TriggerEvaluator.Evaluate` → `ActionRunner.Run`. New trigger types plug
into the evaluator; new action types plug into the runner. A new
`IShellExecutor` interface in `Core/Platform/` lets `RunCommand` dispatch
through the per-OS pattern the constitution (Principle II) already
requires.

## Technical Context

**Language/Version**: C# (latest LangVersion), .NET 10
**Primary Dependencies**:
  - Existing: Avalonia 11.2.3, CommunityToolkit.Mvvm 8.4.0, MQTTnet
    4.3.7.1207, Microsoft.Data.Sqlite 10.0.5, Polly 8.6.6, Stateless
    5.20.1, Serilog 4.3.1
  - New: `System.Net.Http` (BCL) for the Webhook action's HTTP client;
    no new NuGet packages. Bounded concurrency via
    `System.Threading.Channels` (also BCL).
**Storage**:
  - Rules: existing `config.json` (System.Text.Json, per-device
    `Rules` list). No schema break — new trigger/action types are added
    as tagged-union variants.
  - Audit log: new `rule_firings` table in existing `history.db`
    (Microsoft.Data.Sqlite). Columns: `id, ts, rule_id, device_sn,
    trigger_type, trigger_value_json, actions_json, is_test`. Separate
    `rule_firing_actions` child table: `id, firing_id, ordinal,
    action_type, outcome, duration_ms, error_text`.
**Testing**: None currently. Plan adds a new `EcoFlowMonitor.Core.Tests`
  xUnit project with `FluentAssertions`; unit-test the trigger
  evaluator, template expander, retry policy, and queue semantics.
  Integration tests optional; manual verification via the
  "Test rule now" UI path satisfies most acceptance scenarios.
**Target Platform**: Windows 10.0.19041+, macOS 14+ (Sonoma), Linux
  with BlueZ (already the app's support matrix).
**Project Type**: Cross-platform Avalonia desktop application (extending
  the existing solution; no new top-level project besides the tests).
**Performance Goals**:
  - Rule fire latency: <5 s p95 on MQTT, <2 s p95 on BLE (SC-002).
  - 20-rule burst: all actions executed/queued within 30 s at cap=8
    (SC-009).
  - Webhook timeout: ≤10 s default, max 60 s (SC-005).
**Constraints**:
  - `LinkMode=None` stays on for `net10.0-macos` (Constitution IV).
  - No new reflection-loaded types (Constitution IV); polymorphic
    JSON serialization uses `System.Text.Json` polymorphic attributes
    with explicit type discriminators — no `GetType(string)`.
  - App runs non-elevated; actions requiring elevation MUST skip
    cleanly, not crash, not prompt (FR-025..028).
  - No unbounded fork: concurrency cap enforced (FR-010a).
  - Silent `catch { }` remains FORBIDDEN (Constitution I) — every
    failed action gets an audit row.
**Scale/Scope**:
  - Typical user: 1–3 devices, 5–20 rules, 0–5 fires/hour under
    normal conditions.
  - Audit log: bounded growth via 30-day retention; worst-case daily
    churn (level rule flapping at cooldown floor) ≤ 288 fires/day/rule
    → ~5000 rows/device/month.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Principle I — Reliability Is the Product**
- ✅ Every new I/O path (webhook) records outcome in the audit log;
  no `catch { }`.
- ✅ Webhook failures surface in the audit log with HTTP status and
  response-body excerpt; retries recorded per attempt.
- ✅ Queue overflow writes a warning audit row listing dropped actions
  — no silent loss.
- ✅ Monitor pipeline remains unblocked (FR-010, SC-005).

**Principle II — Platform Abstraction at the Boundary**
- ✅ `RunCommand` dispatches through a new `IShellExecutor` interface
  defined in `EcoFlowMonitor.Core/Platform/`. Windows / macOS / Linux
  platform projects each implement it.
- ✅ `Shutdown` / `Hibernate` / `Sleep` continue to route through
  `IPowerActionService` (FR-024).
- ✅ Webhook uses `System.Net.Http.HttpClient` (BCL) — works
  identically across all three OSes; no OS-specific HTTP handler.
- ✅ No `using CoreBluetooth`, `using Windows.Devices.*`, or BlueZ
  types enter `Core`.

**Principle III — Protocol Fragility Is a Given**
- N/A. Rules engine does not touch wire protocol decoders.

**Principle IV — Reflection-Safe Build Chain**
- ✅ Polymorphic trigger/action deserialization uses
  `System.Text.Json`'s `[JsonDerivedType(...)]` polymorphism with
  explicit discriminators — no `Assembly.Load` / `Type.GetType(string)`
  additions.
- ✅ `LinkMode=None` remains unchanged.
- ✅ No new ILLink warnings introduced.

**Principle V — Reproducible Cross-Platform Releases**
- ✅ No build pipeline changes. The existing
  `scripts/build-{macos.sh,linux.sh,windows.ps1}` + CI matrix continue
  to produce the same artifacts.
- ✅ `Entitlements.plist` on macOS already permits JIT / unsigned
  executable memory; no change needed for the rules engine.

**Security & Secrets** (Constitution supporting section)
- ✅ Secret headers redacted in audit log (FR-021). URL/body verbatim
  by user choice (clarification Q2); editor surfaces a warning for
  URL-embedded secrets.
- ✅ No credentials transmitted anywhere beyond the user's own
  configured webhook destination.
- ✅ No new telemetry or analytics.

**Gate verdict:** PASS. The `IShellExecutor` addition is explicitly
aligned with Principle II; all other principles remain satisfied.
Proceed to Phase 0.

### Post-Phase-1 re-check

After generating `research.md`, `data-model.md`, `contracts/`, and
`quickstart.md`, the design was re-evaluated against every principle:

- **I (Reliability):** Confirmed — R-002 (one shared HttpClient with
  per-request CTS timeout), R-004 (bounded channel with logged
  overflow), audit schema in R-005 captures every attempt. No
  swallowed errors anywhere in the planned code paths.
- **II (Platform Abstraction):** Confirmed — `IShellExecutor.md`
  contract places the sole piece of OS-aware code behind an interface;
  `Core/` additions contain no `System.Diagnostics.Process` usage.
- **III (Protocol Fragility):** N/A (no wire-protocol changes).
- **IV (Reflection-Safe):** Confirmed — R-001 picks
  `[JsonPolymorphic]` + `[JsonDerivedType]`, which are trim-friendly
  and compatible with a later move to `JsonSerializerContext` for
  AOT if ever needed. No new `Assembly.Load` / `Type.GetType(string)`
  calls planned.
- **V (Reproducible Releases):** Confirmed — no changes to
  `EcoFlowMonitor.App.csproj`, `Directory.Build.props`, CI workflows,
  or `scripts/build-*`. The new test project is additive and its
  existence does not affect release artifacts.
- **Security & Secrets:** Confirmed — redaction rules live in the
  audit writer (see R-005); webhook URL/body stored verbatim per
  clarification Q2; no network egress to any host other than the
  user's chosen webhook target.

Post-design gate verdict: **PASS**. Ready for `/speckit.tasks`.

## Project Structure

### Documentation (this feature)

```text
specs/001-rules-engine/
├── plan.md                         # This file (/speckit.plan output)
├── spec.md                         # Feature spec (/speckit.specify output)
├── research.md                     # Phase 0 output (this command)
├── data-model.md                   # Phase 1 output (this command)
├── quickstart.md                   # Phase 1 output (this command)
├── contracts/                      # Phase 1 output (this command)
│   ├── webhook-request.json        # Default webhook body schema
│   ├── rule-firing-audit.sql       # SQLite schema for the audit table
│   └── IShellExecutor.md           # Interface contract description
├── checklists/
│   └── requirements.md             # Spec quality checklist (/speckit.specify)
└── tasks.md                        # Phase 2 output (/speckit.tasks, not here)
```

### Source Code (repository root)

```text
src/
├── EcoFlowMonitor.Core/
│   ├── Actions/                    # Existing; extended
│   │   ├── ActionRunner.cs         # EXTEND: new dispatch cases for
│   │   │                             #  Webhook + RunCommand;
│   │   │                             #  concurrency cap + FIFO queue
│   │   ├── ActionConfig.cs         # EXTEND: polymorphic base +
│   │   │                             #  WebhookActionConfig +
│   │   │                             #  RunCommandActionConfig
│   │   ├── TemplateExpander.cs     # EXTEND: {temp_c},
│   │   │                             #  {ac_plugged}, {charge_state},
│   │   │                             #  {device_sn}; <unknown> fallback
│   │   ├── WebhookAction.cs        # NEW
│   │   └── RunCommandAction.cs     # NEW
│   ├── Triggers/                   # Existing; extended
│   │   ├── TriggerEvaluator.cs     # EXTEND: new trigger-type cases
│   │   ├── TriggerConfig.cs        # EXTEND: polymorphic base +
│   │   │                             #  variants for each new type
│   │   └── DeviceOfflineWatcher.cs # NEW: tracks last-data time per
│   │                                 #  device, fires DeviceOffline
│   │                                 #  after window
│   ├── Platform/
│   │   └── IShellExecutor.cs       # NEW: cross-platform shell exec
│   ├── History/                    # Existing (SQLite history store)
│   │   ├── IRuleFiringStore.cs     # NEW
│   │   └── SqliteRuleFiringStore.cs # NEW
│   └── Models/
│       └── RuleFiring.cs           # NEW: audit-row record type
│
├── EcoFlowMonitor.Platform.Windows/
│   └── WindowsShellExecutor.cs     # NEW: cmd.exe or pwsh.exe
│
├── EcoFlowMonitor.Platform.macOS/
│   └── MacShellExecutor.cs         # NEW: /bin/sh
│
├── EcoFlowMonitor.Platform.Linux/
│   └── LinuxShellExecutor.cs       # NEW: /bin/sh
│
├── EcoFlowMonitor.App/
│   ├── ViewModels/
│   │   ├── Automation/             # NEW folder
│   │   │   ├── RulesListViewModel.cs
│   │   │   ├── RuleEditorViewModel.cs
│   │   │   ├── TriggerEditorViewModel.cs
│   │   │   ├── ActionEditorViewModel.cs
│   │   │   └── RuleHistoryViewModel.cs
│   │   └── SettingsViewModel.cs    # EXTEND: nav entry to Automation
│   ├── Views/
│   │   └── Automation/             # NEW folder
│   │       ├── RulesListView.axaml
│   │       ├── RuleEditorView.axaml
│   │       └── RuleHistoryView.axaml
│   └── Services/
│       └── MonitorOrchestrator.cs  # EXTEND: inject IRuleFiringStore,
│                                     #  push audit rows on fire;
│                                     #  add DeviceOfflineWatcher
│
└── EcoFlowMonitor.Core.Tests/      # NEW project (xUnit)
    ├── EcoFlowMonitor.Core.Tests.csproj
    ├── TriggerEvaluatorTests.cs
    ├── TemplateExpanderTests.cs
    ├── WebhookActionTests.cs       # uses HttpMessageHandler mock
    ├── RunCommandActionTests.cs    # uses fake IShellExecutor
    ├── ActionRunnerQueueTests.cs
    └── RuleFiringStoreTests.cs     # uses in-memory SQLite
```

**Structure Decision:** Extend the existing multi-project solution in
`src/`. New types live alongside their siblings (`Actions/`, `Triggers/`,
`Platform/`, `History/`). One new test project
(`EcoFlowMonitor.Core.Tests`) is introduced — the constitution allows it
(no rule forbids tests), and the `Core` project is the only one that
benefits from unit coverage because it contains the rules logic with no
OS dependencies. No new top-level projects beyond that.

## Complexity Tracking

None — Constitution Check passes without exceptions. No simpler
alternative would preserve cross-platform parity for `RunCommand`.
