# Tasks: Rules Engine (Full)

**Input**: Design documents from `/specs/001-rules-engine/`
**Prerequisites**: [plan.md](./plan.md) (required), [spec.md](./spec.md) (required), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: Included. Plan R-007 decides to add `EcoFlowMonitor.Core.Tests`
(xUnit + FluentAssertions) to cover the pure-logic surface: trigger evaluator,
template expander, webhook retry policy, bounded queue, and audit store. UI
(`App/Views/Automation/*`) is manually verified via the "Test rule now" flow
from US4 — no Avalonia Headless tests in scope.

**Organization**: Tasks are grouped by user story (per spec.md priorities)
so each story is an independently shippable MVP increment.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: `US1`–`US5` — maps to user stories in spec.md
- Include exact file paths in descriptions

## Path Conventions

Paths are relative to the repo root. Existing projects sit under `src/`;
new code follows the structure chart in `plan.md`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project scaffolding needed before any feature work.

- [X] T001 Create new xUnit test project at `src/EcoFlowMonitor.Core.Tests/EcoFlowMonitor.Core.Tests.csproj` targeting `net10.0`, with `PackageReference`s: `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2`, `FluentAssertions 6.12.x`, `Microsoft.Data.Sqlite 10.0.5`, `Microsoft.NET.Test.Sdk 17.11.x`. Add `<ProjectReference>` to `EcoFlowMonitor.Core`.
- [X] T002 Register `EcoFlowMonitor.Core.Tests` in `src/EcoFlowMonitor.sln` with `dotnet sln src/EcoFlowMonitor.sln add src/EcoFlowMonitor.Core.Tests/EcoFlowMonitor.Core.Tests.csproj`.
- [X] T003 [P] Verify `dotnet test src/EcoFlowMonitor.Core.Tests/` runs (empty, 0 tests) on all three OSes; CI `build.yml` already covers Core, no workflow change needed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared infrastructure every user story depends on.

**⚠️ CRITICAL**: No user-story work starts until this phase completes.

- [X] T010 Convert `src/EcoFlowMonitor.Core/Models/TriggerConfig.cs` to a polymorphic base class via `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` with `[JsonDerivedType]` attributes for the four existing variants (`PowerLost`, `PowerRestored`, `BatteryBelow`, `TimeRemainingBelow`). Keep existing JSON shape byte-identical for current `config.json` files (verify via round-trip test added in T041).
- [X] T011 Convert `src/EcoFlowMonitor.Core/Models/ActionConfig.cs` to a polymorphic base class with `[JsonDerivedType]` attributes for the six existing variants (`RunScript`, `Shutdown`, `Hibernate`, `Sleep`, `Notification`, `WriteLog`). Preserve current JSON shape.
- [X] T012 [P] Extend `src/EcoFlowMonitor.Core/Actions/TemplateExpander.cs` to support new variables `{temp_c}`, `{ac_plugged}`, `{charge_state}`, `{device_sn}`, and to emit `<unknown>` for every `null`/missing value (covers FR-009).
- [X] T013 [P] Create `src/EcoFlowMonitor.Core/Models/RuleFiring.cs` (record type per `data-model.md`: `Id, Ts, RuleId, RuleName, DeviceSn, TriggerType, TriggerValueJson, IsTest, Actions`) and `src/EcoFlowMonitor.Core/Models/RuleFiringAction.cs` (record: `Id, FiringId, Ordinal, ActionType, Outcome, DurationMs, ErrorText, DetailJson`).
- [X] T014 [P] Create `src/EcoFlowMonitor.Core/History/IRuleFiringStore.cs` with methods `Task AppendAsync(RuleFiring)`, `Task<IReadOnlyList<RuleFiring>> QueryAsync(string? deviceSn, string? ruleId, DateTime? since, int limit)`, `Task PruneOlderThanAsync(DateTime cutoffUtc)`.
- [X] T015 Create `src/EcoFlowMonitor.Core/History/SqliteRuleFiringStore.cs` implementing `IRuleFiringStore`. Execute the DDL from `specs/001-rules-engine/contracts/rule-firing-audit.sql` on first construction via `CREATE TABLE IF NOT EXISTS`. Reuse the existing `history.db` connection path from `SqliteHistoryStore`. Use `Microsoft.Data.Sqlite`.
- [ ] T016 Register `IRuleFiringStore` → `SqliteRuleFiringStore` as a singleton in `src/EcoFlowMonitor.App/App.axaml.cs` DI setup, alongside `IHistoryStore`.
- [ ] T017 Add concurrency queue to `src/EcoFlowMonitor.Core/Actions/ActionRunner.cs`: private `Channel<QueuedAction>` (`BoundedChannelOptions { Capacity = 256, FullMode = DropOldest }`) + `SemaphoreSlim(8)` per plan R-004. Spawn a reader loop task on first enqueue. Expose `ConfigureConcurrency(int maxConcurrent, int queueCapacity)` for the Settings screen.
- [ ] T018 Extend `ActionRunner` with a `Func<RuleFiringAction, Task>` hook so each action-attempt outcome is written to `IRuleFiringStore` regardless of action type; constitution Principle I forbids silent failures.
- [ ] T019 Extend `src/EcoFlowMonitor.App/Services/MonitorOrchestrator.cs` `OnStateChanged` to: (a) build a `DeviceStateSnapshot` (per data-model.md) once per evaluation, (b) pass it to every triggered rule's actions, (c) create one `RuleFiring` per rule fire with the snapshot serialised into `TriggerValueJson`, and (d) write it to `IRuleFiringStore` at firing start. Per-action rows are written by the ActionRunner hook (T018).
- [ ] T020 Add retention timer to `src/EcoFlowMonitor.App/App.axaml.cs`: on framework-initialization-completed, `PeriodicTimer(TimeSpan.FromHours(24))` calls `IRuleFiringStore.PruneOlderThanAsync(DateTime.UtcNow - TimeSpan.FromDays(_config.General.AuditRetentionDays ?? 30))`. Run once immediately at startup too. Add `AuditRetentionDays` to `AppConfig.General` with default `30`.
- [X] T021 Create `src/EcoFlowMonitor.Core/Platform/IShellExecutor.cs` with the interface + `ShellExecRequest`/`ShellExecResult` records + `ShellKind` enum exactly as specified in `contracts/IShellExecutor.md`. No implementations yet — that's per-story in US2.

**Checkpoint**: Foundation is in place. All new polymorphic types round-trip through JSON, audit rows are emitted on every rule fire, and the shell interface is available for US2 to consume. User stories can now proceed in parallel.

---

## Phase 3: User Story 1 — Webhook on power status (Priority: P1) 🎯 MVP

**Goal**: Deliver a working `Webhook` action with configurable headers, method, body template, timeout, and retry policy, fired from existing `PowerLost`/`PowerRestored` triggers.

**Independent Test** (from spec US1): Add a rule "on PowerLost → POST to `http://localhost:8080/hook`" with `nc -l 8080` as the receiver. Unplug the station (or use "Test rule now"); verify the POST arrives within 5 s with the default JSON body from `contracts/webhook-request.json`.

### Tests for User Story 1 ⚠️

> Write these first; they MUST fail before implementation lands.

- [ ] T030 [P] [US1] `src/EcoFlowMonitor.Core.Tests/WebhookActionTests.cs` — test the happy path: `FakeHttpMessageHandler` returns 200, single attempt, outcome `success`, `detailJson.httpStatus == 200`.
- [ ] T031 [P] [US1] `WebhookActionTests.cs` — test retry exhaustion: handler returns 503 three times, action has `retries=2`, verify three attempts with `retryDelayMs` waits between them and final outcome `failure`.
- [ ] T032 [P] [US1] `WebhookActionTests.cs` — test timeout: handler delays 20 s, action has `timeoutMs=500`, outcome `timeout` with `durationMs ≈ 500`.
- [ ] T033 [P] [US1] `WebhookActionTests.cs` — test 5xx vs 4xx retry policy: 500 retries, 404 does not retry.
- [ ] T034 [P] [US1] `WebhookActionTests.cs` — test header redaction: request with `Authorization: Bearer abc` + custom `X-Foo: bar` writes a `detailJson` whose headers section shows `Authorization: ***redacted***` but leaves `X-Foo: bar`.

### Implementation for User Story 1

- [ ] T035 [P] [US1] Create `src/EcoFlowMonitor.Core/Actions/WebhookActionConfig.cs`: record extending `ActionConfig` with fields `Url, Method, Headers, BodyTemplate, Retries, RetryDelayMs, TimeoutMs` per data-model.md. Add `[JsonDerivedType(typeof(WebhookActionConfig), "Webhook")]` to `ActionConfig`'s polymorphic attributes (from T011).
- [ ] T036 [US1] Create `src/EcoFlowMonitor.Core/Actions/WebhookAction.cs` that implements the runner-callback contract: takes a `WebhookActionConfig`, a `DeviceStateSnapshot`, and a `CancellationToken`; expands the body template (or builds the default JSON body per `contracts/webhook-request.json`); uses a shared `static HttpClient` (see plan R-002) with `SocketsHttpHandler { PooledConnectionLifetime = 5 min, AutomaticDecompression = All }`; applies per-request `CancellationTokenSource.CancelAfter(timeoutMs)`; implements the retry loop per data-model.md "Fire semantics".
- [ ] T037 [US1] Add `Webhook` dispatch branch to `ActionRunner` (`T011`'s switch) that hands off to `WebhookAction`. Each retry attempt writes a separate `RuleFiringAction` row via the T018 hook, with the first attempt as `ordinal=N` and retries appended. Overall rule outcome is `success` if any attempt succeeded.
- [ ] T038 [US1] Implement header-redaction helper in `src/EcoFlowMonitor.Core/Actions/AuditRedactor.cs`: case-insensitive match against `Authorization`, `X-*-Token`, `X-*-Secret`, plus any header name the user flagged "secret"; produces a redacted copy for storage in `detailJson`. Keep the unredacted copy for the outgoing HTTP request only.
- [ ] T039 [US1] URL-secret heuristic in `src/EcoFlowMonitor.Core/Actions/UrlSecretWarning.cs`: static helper returning `bool ContainsSuspectSecretParam(Uri url)` — matches query-param names against `^(token|key|signature|sig|secret|password|auth|access_token)$` case-insensitive. Used by the rule editor (later in US4) to surface the save-time warning from FR-021.
- [ ] T040 [US1] Register the shared `HttpClient` as a singleton in DI (`App.axaml.cs`), constructed with the `SocketsHttpHandler` from T036. `WebhookAction` receives it via constructor.
- [ ] T041 [P] [US1] `src/EcoFlowMonitor.Core.Tests/PolymorphicConfigTests.cs` — round-trip test: serialise a `RuleConfig` containing a `WebhookActionConfig` to JSON and back, assert equality. Also parse a sample `config.json` snippet that contains legacy variants (e.g. `RunScript`) to prove backward compatibility.

**Checkpoint**: US1 complete. A user with a JSON config (no UI yet) can already define + fire a Webhook rule end-to-end. Headers are redacted in the audit log; URL-secret warning helper is ready for the UI phase.

---

## Phase 4: User Story 2 — Cross-platform `RunCommand` (Priority: P1)

**Goal**: Users can author one rule with per-OS command strings and have it execute correctly on whichever OS the app runs on.

**Independent Test** (from spec US2): Create a rule "on BatteryBelow 10% → Run Command" with `commandWindows/macOS/linux` set to a script that writes a file named with `{device}` and `{battery}` substituted. Simulate low battery; verify the correct file is created on the running OS, and that running the same config.json on a different OS produces the file with that OS's expansion. Rules lacking a command for the current OS log `skipped` cleanly.

### Tests for User Story 2 ⚠️

- [ ] T050 [P] [US2] `src/EcoFlowMonitor.Core.Tests/RunCommandActionTests.cs` — per-OS dispatch: inject `OperatingSystem.IsWindows() == true` fake, set all three command fields, verify only `commandWindows` is sent to the fake `IShellExecutor`.
- [ ] T051 [P] [US2] `RunCommandActionTests.cs` — skipped: inject `OperatingSystem.IsLinux() == true`, leave `commandLinux` null, verify outcome `skipped` with `errorText` containing `"no command for linux"`, and fake shell executor was NOT called.
- [ ] T052 [P] [US2] `RunCommandActionTests.cs` — template expansion: `commandMacOS = "./run.sh --pct {battery}"` + snapshot `batteryPct=42.5`, assert the fake executor receives `"./run.sh --pct 42.5"`.
- [ ] T053 [P] [US2] `RunCommandActionTests.cs` — timeout: fake executor returns `TimedOut=true`, outcome `timeout`.
- [ ] T054 [P] [US2] `RunCommandActionTests.cs` — failure: fake executor returns `ExitCode=2, StdErrHead="boom"`, outcome `failure`, `errorText` starts with `"boom"`, `detailJson.exitCode == 2`.

### Implementation for User Story 2

- [ ] T055 [P] [US2] Implement `src/EcoFlowMonitor.Platform.Windows/WindowsShellExecutor.cs` per `contracts/IShellExecutor.md`: spawn `cmd.exe /c "<command>"` (or `pwsh -NoProfile -NonInteractive -Command "<command>"` when `Shell == PowerShell`) via `Process.Start`; `UseShellExecute=false`, `RedirectStandardOutput/Error=true`, `CreateNoWindow=true`. Enforce timeout via `Process.WaitForExitAsync(cts.Token)` → kill on cancel. Cap stdout/stderr capture at 4 KiB each.
- [ ] T056 [P] [US2] Implement `src/EcoFlowMonitor.Platform.macOS/MacShellExecutor.cs`: spawn `/bin/sh -c "<command>"` (or `pwsh -NoProfile -Command` when `Shell == PowerShell` and `pwsh` is on PATH; else throw `PlatformNotSupportedException`). Same process-handling pattern as Windows.
- [ ] T057 [P] [US2] Implement `src/EcoFlowMonitor.Platform.Linux/LinuxShellExecutor.cs`: same as macOS (`/bin/sh -c "<command>"` default, optional `pwsh`).
- [ ] T058 [US2] Add `StubShellExecutor` as a nested class in `src/EcoFlowMonitor.App/Services/PlatformServiceFactory.cs` that throws `PlatformNotSupportedException("IShellExecutor not available on this platform")` from `RunAsync`. Register as the fallback when no OS matches — mirrors the `StubBleAdapter` pattern.
- [ ] T059 [US2] Wire the per-OS `IShellExecutor` through `PlatformServiceFactory.Register` in `src/EcoFlowMonitor.App/Services/PlatformServiceFactory.cs`, following the existing `INotificationService` / `IPowerActionService` pattern (reflection-load from `EcoFlowMonitor.Platform.{OS}` assembly, fallback to `StubShellExecutor`). No new `Assembly.Load` call: reuse the existing per-OS assembly already loaded.
- [ ] T060 [P] [US2] Create `src/EcoFlowMonitor.Core/Actions/RunCommandActionConfig.cs`: record extending `ActionConfig` with `CommandWindows, CommandMacOS, CommandLinux, Shell (enum), WorkingDirectory, TimeoutMs`. Add `[JsonDerivedType(typeof(RunCommandActionConfig), "RunCommand")]` to `ActionConfig`.
- [ ] T061 [US2] Create `src/EcoFlowMonitor.Core/Actions/RunCommandAction.cs`: picks the OS-matching field via `OperatingSystem.IsWindows/IsMacOS/IsLinux`; if null/empty, returns `RuleFiringAction { Outcome = "skipped", ErrorText = $"no command for {os}" }`. Otherwise template-expand, call `IShellExecutor.RunAsync`, map result → `success|failure|timeout` per data-model.md.
- [ ] T062 [US2] Add `RunCommand` dispatch branch to `ActionRunner` that routes to `RunCommandAction`. Single audit row per fire (no retry loop for commands).
- [ ] T063 [P] [US2] Create `src/EcoFlowMonitor.Core/Actions/ElevationRequirements.cs` (pure helper): `static IReadOnlyList<RequiredCapability> Detect(RuleConfig rule, OSPlatform os)` returning flags like `SystemShutdown`, `SystemHibernate`, `RootShellExec`. Matches `Shutdown`/`Hibernate` actions and scans `RunCommandActionConfig` command strings for substrings like `systemctl poweroff`, `systemctl hibernate`, `shutdown -h`. Consumed by the rule editor in US4 (FR-026).

**Checkpoint**: US1 + US2 complete. Webhook + RunCommand rules both work end-to-end on all three OSes, driven from `config.json`. The app remains JSON-edit-only until US4 lands the UI.

---

## Phase 5: User Story 3 — New trigger types (Priority: P2)

**Goal**: Users can fire rules on temperature, AC-plug state, input/output watts, and device offline/online, in addition to the existing four triggers.

**Independent Test** (from spec US3): Spoof a BMS payload with `TempC = 60.0`, verify a rule with `TempAbove 55` fires once, cooldown before next fire. Pull the AC line (set `AcPluggedIn=false`); verify `AcUnplugged` fires. Stop the device monitor thread for 6 minutes; verify `DeviceOffline 5 min` fires.

### Tests for User Story 3 ⚠️

- [ ] T070 [P] [US3] `src/EcoFlowMonitor.Core.Tests/TriggerEvaluatorTests.cs` — `TempAbove 55`: state transitions 50 → 60, rule fires once; 60 → 50 → 60 within cooldown, no second fire; after cooldown expires, 50 → 60 fires again.
- [ ] T071 [P] [US3] `TriggerEvaluatorTests.cs` — `AcPlugged` / `AcUnplugged` as edge triggers: verify exactly one fire per boolean transition, independent of `PowerLost` firing (a simultaneous grid drop + pull fires both, distinctly).
- [ ] T072 [P] [US3] `TriggerEvaluatorTests.cs` — `BatteryAbove 80`: counterpart to existing `BatteryBelow`; fires while `BatteryPct > 80`, throttled by cooldown.
- [ ] T073 [P] [US3] `TriggerEvaluatorTests.cs` — `InputWattsBelow 50` / `OutputWattsAbove 500`: confirm level semantics + cooldown.
- [ ] T074 [P] [US3] `src/EcoFlowMonitor.Core.Tests/DeviceOfflineWatcherTests.cs` — `windowSeconds=30`: advance fake clock, no `DeviceUpdated` events, verify `DeviceOffline` fires at t≈30 s. Resume updates → `DeviceOnline` fires once.

### Implementation for User Story 3

- [ ] T075 [P] [US3] Extend `src/EcoFlowMonitor.Core/Models/TriggerConfig.cs`: add `[JsonDerivedType]` entries and concrete records for each new variant (`BatteryAbove`, `TempAbove`, `TempBelow`, `AcPlugged`, `AcUnplugged`, `InputWattsBelow`, `OutputWattsAbove`, `DeviceOffline`, `DeviceOnline`) with fields from data-model.md. Validation: threshold ranges enforced in the record constructor or via `ValidateTrigger(out error)`.
- [ ] T076 [P] [US3] Extend `src/EcoFlowMonitor.Core/Triggers/TriggerEvaluator.cs` `Evaluate` with switch cases for each new trigger. For level triggers, use the existing cooldown helper (`RecordFired` + `LastFired`). For new edge triggers (`AcPlugged`, `AcUnplugged`), track previous `AcPluggedIn` state on `DeviceState` and fire only on boolean transitions.
- [ ] T077 [US3] Add `LastAcPluggedIn` field to `src/EcoFlowMonitor.Core/State/DeviceState.cs` (nullable bool, updated by `TriggerEvaluator.Evaluate` after each pass). Needed for `AcPlugged`/`AcUnplugged` edge detection.
- [ ] T078 [US3] Create `src/EcoFlowMonitor.Core/Triggers/DeviceOfflineWatcher.cs`: `PeriodicTimer(TimeSpan.FromSeconds(10))` task; tracks per-device `IsOffline` boolean and `WindowStartedAt`. Exposes `event EventHandler<DeviceState>? OfflineCrossed` / `OnlineCrossed`. Subscribes to `MonitorOrchestrator.DeviceUpdated` to reset the window.
- [ ] T079 [US3] Wire `DeviceOfflineWatcher` into `MonitorOrchestrator`: on `OfflineCrossed`, synthesise a `DeviceState` snapshot with `PowerStatus = Unknown` (or whatever makes sense for an offline device) and call the same trigger-evaluation path. Same for `OnlineCrossed`.

**Checkpoint**: US1 + US2 + US3 complete. Users can author any of the 13 trigger types × 7 action types without a UI.

---

## Phase 6: User Story 4 — In-app rule editor (Priority: P2)

**Goal**: Author, edit, enable/disable, duplicate, and "test now" rules from the Avalonia UI, without touching `config.json`.

**Independent Test** (from spec US4): With no prior rules configured, use only the UI to author one rule, hit "Test rule now", and verify the action executes with synthetic trigger context.

**Note**: UI implementation has no xUnit tests (plan R-007). Manual verification via the acceptance scenarios.

### Implementation for User Story 4

- [ ] T090 [US4] Add new `AutomationViewModel` navigation entry to `src/EcoFlowMonitor.App/ViewModels/MainWindowViewModel.cs` (existing Settings/Dashboard/History siblings pattern). Sub-tabs: `Rules`, `History`.
- [ ] T091 [P] [US4] Create `src/EcoFlowMonitor.App/ViewModels/Automation/RulesListViewModel.cs` with `ObservableCollection<RuleRowViewModel>` populated from `AppConfig.Devices[].Rules` + orphaned rules. `[RelayCommand]`s for `AddRule`, `EditRule(RuleRowViewModel)`, `DuplicateRule(...)`, `DeleteRule(...)`, `TestRule(...)`, `ToggleEnabled(...)`.
- [ ] T092 [P] [US4] Create `src/EcoFlowMonitor.App/Views/Automation/RulesListView.axaml` + `.axaml.cs`: `DataGrid` bound to the list; toggle switch for `Enabled`; per-row action buttons (edit/duplicate/delete/test); badge showing `orphaned` / `needs elevation` / `cooldown remaining`.
- [ ] T093 [US4] Create `src/EcoFlowMonitor.App/ViewModels/Automation/RuleEditorViewModel.cs` holding the working copy of a `RuleConfig`. Sub-view-models for the currently-selected trigger and each action. Validation: `CanSave` computed from field-level validation per data-model.md. Surface warning list for: elevation-needed-but-non-elevated (uses `ElevationRequirements` from T063 + `IElevationService.IsElevated`), URL-with-secret-param (uses `UrlSecretWarning` from T039).
- [ ] T094 [P] [US4] Create `src/EcoFlowMonitor.App/Views/Automation/RuleEditorView.axaml` + `.axaml.cs`: form with Name, Device dropdown, Trigger picker (showing fields appropriate to selected type), Actions list (add/remove/reorder), Cooldown override, Enabled toggle, Save/Cancel. Warning banner area at the top for FR-027 elevation instructions (Markdown blobs embedded per plan R-008).
- [ ] T095 [P] [US4] Create `src/EcoFlowMonitor.App/ViewModels/Automation/TriggerEditorViewModel.cs`: maps each `TriggerType` enum value → the set of fields that variant requires. Used by `RuleEditorView` as the dynamic form region under "Trigger".
- [ ] T096 [P] [US4] Create `src/EcoFlowMonitor.App/ViewModels/Automation/ActionEditorViewModel.cs`: maps each `ActionType` → fields. For `Webhook`, show URL/Method/Headers(list)/BodyTemplate/Retries/RetryDelay/Timeout. For `RunCommand`, show three command text boxes (Windows/macOS/Linux), Shell dropdown, WorkingDirectory, Timeout, with tabs/labels making per-OS nature explicit.
- [ ] T097 [US4] Implement `TestRule` command: constructs a `DeviceStateSnapshot` from the latest known state of the rule's device (or plausible defaults if never observed), sets `isTest=true`, feeds it through `ActionRunner.EnqueueForTest(rule, snapshot)` which is a new entry point that bypasses trigger evaluation. Audit rows land with `is_test=1`. Result is shown inline in the UI: per-action outcome badges in a modal.
- [ ] T098 [US4] Orphan-rule handling: on app startup, `MonitorOrchestrator` detects rules whose `DeviceSerialNumber` does not resolve and relocates them into `AppConfig.OrphanedRules` (new top-level list). Rules list shows them under an "Orphaned" group with a "Reassign…" action that picks a device and moves the rule back under `Devices[].Rules`.
- [ ] T099 [US4] Add `AuditRetentionDays` slider and concurrency cap (`MaxConcurrentActions`, default 8, range 1–64) controls to `src/EcoFlowMonitor.App/Views/SettingsView.axaml` → General tab, bound through `SettingsViewModel`. Apply concurrency changes to `ActionRunner` live via `ConfigureConcurrency` (T017).

**Checkpoint**: US1 + US2 + US3 + US4 complete. End-to-end from "open Settings → add rule → save → Test now → see audit row" works without editing JSON.

---

## Phase 7: User Story 5 — Rule history UI (Priority: P3)

**Goal**: A filterable, time-sorted view of every rule firing, with per-action diagnostics.

**Independent Test** (from spec US5): Trigger three rules (one succeeding webhook, one failing webhook, one successful script). Open the History view; verify all three rows appear, sorted newest first, with correct per-action badges. Expand the failing webhook row; verify URL + HTTP status + response-body excerpt are shown.

### Implementation for User Story 5

- [ ] T110 [US5] Create `src/EcoFlowMonitor.App/ViewModels/Automation/RuleHistoryViewModel.cs`: observable collection of `RuleFiring`s from `IRuleFiringStore.QueryAsync`. Filter controls: device (dropdown, multi-select), rule (dropdown), date range. Commands: `Refresh`, `ClearFilters`.
- [ ] T111 [P] [US5] Create `src/EcoFlowMonitor.App/Views/Automation/RuleHistoryView.axaml` + `.axaml.cs`: `DataGrid` of rule firings with expandable detail pane per row. Columns: timestamp, rule name, device, trigger value (compact), action-outcome badges. Expansion shows per-action detail: Webhook → method+URL+status+response-body-excerpt; RunCommand → exit-code + stderr head; others → message/path.
- [ ] T112 [US5] Register `RuleHistoryViewModel` as a singleton in DI (`App.axaml.cs`) so the view reuses one instance across tab switches.
- [ ] T113 [P] [US5] `src/EcoFlowMonitor.Core.Tests/RuleFiringStoreTests.cs` — use in-memory SQLite (`Data Source=:memory:`): insert 100 firings across 2 devices; verify `QueryAsync` filtering by device, rule, and time works; verify `PruneOlderThanAsync` deletes only rows older than cutoff and cascades to `rule_firing_actions`.
- [ ] T114 [US5] Expose "Queue overflow" audit entries in the History view distinctly: when `ActionRunner`'s bounded channel drops oldest actions (T017), insert a synthetic `RuleFiring` row with `triggerType="QueueOverflow"` listing the dropped action IDs — satisfies FR-010a and SC-003's "no silent failures" requirement.

**Checkpoint**: US1 + US2 + US3 + US4 + US5 complete. Feature spec fully delivered.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T120 [P] Run `./scripts/build-macos.sh 0.0.0-rules-engine-smoke` on the dev Mac and exercise the quickstart walkthrough (Phase 1 of quickstart.md) end-to-end. File any surprises as separate issues — do not extend this feature.
- [ ] T121 [P] Run `dotnet test src/EcoFlowMonitor.Core.Tests/` on a clean checkout. Every test from T030–T054, T070–T074, T113 must pass.
- [ ] T122 Update `CLAUDE.md` Platform Abstraction table to include `IShellExecutor` (row for purpose + per-OS implementation file). The `update-agent-context.sh` step already added the language entry; this is a manual content touch-up.
- [ ] T123 [P] Update `README.md` to mention the Automation view in the Features list and link to `specs/001-rules-engine/quickstart.md` as the user-facing walkthrough.
- [ ] T124 [P] Update `docs/ecoflow-cloud-flow.md` and `docs/ecoflow-ble-flow.md` with a short "Rule firing hook" section pointing out where `MonitorOrchestrator.OnStateChanged` feeds the rules engine (for future protocol-layer contributors who touch that path).
- [ ] T125 Run the CI matrix locally via `gh workflow run build.yml` once main is updated, confirm all three OS jobs pass with the new test project.
- [ ] T126 Write a short migration note (2–3 paragraphs) in `specs/001-rules-engine/migration.md` describing how users with existing `config.json` rules are affected: zero action required; new fields default to sensible values; older files continue to parse via the polymorphic base. Part of Principle V.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no deps, start immediately.
- **Phase 2 (Foundational)**: depends on Phase 1. **Blocks all user stories.**
- **Phase 3 (US1 Webhook)**: depends on Phase 2.
- **Phase 4 (US2 RunCommand)**: depends on Phase 2. Can run in parallel with Phase 3 (no shared files beyond `ActionRunner` dispatch switch, which T037 and T062 touch sequentially).
- **Phase 5 (US3 New triggers)**: depends on Phase 2. Parallel-friendly with Phase 3 and Phase 4.
- **Phase 6 (US4 UI)**: depends on Phase 2 + at least one action type from US1 or US2 to demonstrate end-to-end, so start after Phase 3 is green.
- **Phase 7 (US5 History UI)**: depends on Phase 2 (audit store) plus any firings — start after Phase 6 to have a real UI nav entry.
- **Phase 8 (Polish)**: depends on whichever stories you plan to ship.

### Within-phase dependencies (explicit)

- T017 (ActionRunner concurrency) blocks T018 (audit hook), which blocks T037/T062 (per-action dispatch).
- T010/T011 (polymorphic base) block T035/T060/T075 (new variants).
- T014/T015/T016 (`IRuleFiringStore` + impl + DI) blocks T019/T020/T097/T110.
- T021 (`IShellExecutor` interface) blocks T055/T056/T057/T058/T059 (impls + DI).
- T063 (`ElevationRequirements`) blocks T093 (editor warnings).
- T039 (URL-secret helper) blocks T093 (editor warnings).

### Parallel opportunities

- T012, T013, T014 are all independent files → run in parallel.
- T030–T034 (US1 tests) are independent test files → parallel.
- T035, T041 within US1 — independent.
- T050–T054 (US2 tests) parallel.
- T055, T056, T057 (three OS impls) parallel.
- T060, T063 parallel with shell-executor impls.
- T070–T074 (US3 tests) parallel.
- T075 (variants) parallel with T074 (watcher test).
- T091, T094, T095, T096 (UI views + VMs) parallel within US4.
- T120–T126 (Polish) all parallel-friendly.

---

## Parallel Example: User Story 1

```bash
# Tests first, in parallel:
Task T030: "Webhook happy-path test in src/EcoFlowMonitor.Core.Tests/WebhookActionTests.cs"
Task T031: "Webhook retry exhaustion test in src/EcoFlowMonitor.Core.Tests/WebhookActionTests.cs"
Task T032: "Webhook timeout test in src/EcoFlowMonitor.Core.Tests/WebhookActionTests.cs"
Task T033: "Webhook 5xx/4xx retry policy test in src/EcoFlowMonitor.Core.Tests/WebhookActionTests.cs"
Task T034: "Webhook header redaction test in src/EcoFlowMonitor.Core.Tests/WebhookActionTests.cs"

# Independent implementation files in parallel:
Task T035: "WebhookActionConfig record in src/EcoFlowMonitor.Core/Actions/WebhookActionConfig.cs"
Task T041: "Polymorphic config round-trip test in src/EcoFlowMonitor.Core.Tests/PolymorphicConfigTests.cs"

# Sequential within US1:
Task T036: "WebhookAction implementation (needs T035)"
Task T037: "ActionRunner dispatch branch (needs T036 + T017)"
Task T038: "AuditRedactor (needs T037)"
Task T039: "UrlSecretWarning helper (independent)"
Task T040: "HttpClient DI registration (needs T036)"
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 Setup.
2. Phase 2 Foundational (the concurrency queue + polymorphic configs + audit store + `IShellExecutor` interface unlock everything else).
3. Phase 3 US1 — Webhook on PowerLost / PowerRestored.
4. **STOP + VALIDATE**: spec acceptance scenarios 1.1, 1.2, 1.3. Run Phase 8 tasks T120–T121.
5. Ship as a pre-release for users who live in JSON-edit-land.

### Incremental delivery

1. Setup + Foundational → Foundation ready (no user-visible change yet).
2. + US1 Webhook → first shippable MVP.
3. + US2 RunCommand → users with platform-specific scripts unblocked.
4. + US3 New triggers → covers "hot station" / "AC unplugged" automations.
5. + US4 UI → no-JSON-editing audience onboarded.
6. + US5 History → full trust story.
7. Polish → release.

### Parallel team strategy

With two engineers after Foundational:
- Engineer A: US1 (Phase 3), then US4 (Phase 6). Owns Actions, ActionRunner dispatch, rule editor UI.
- Engineer B: US2 (Phase 4) + US3 (Phase 5), then US5 (Phase 7). Owns Platform shell executors, new triggers, history UI.

T017 / T018 / T037 / T062 touch `ActionRunner` sequentially — serialise those through A.

---

## Notes

- [P] tasks = different files, no dependency on another incomplete task.
- Every US-phase task has a `[US#]` label; Phase 1/2/8 tasks do not.
- Tests for a story run first within that story (plan R-007).
- Commit after each task or each coherent group of [P] tasks.
- Every checkpoint is a safe place to stop for the night and keep a consistent build.
- Avoid: silently catching exceptions (Constitution I), adding new `Assembly.Load` / `Type.GetType(string)` paths (Constitution IV), or putting OS-specific code in `Core` (Constitution II).
