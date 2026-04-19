# Phase 0 — Research

Resolutions for every non-obvious technical decision the plan leans on.
Each entry records **Decision**, **Rationale**, and **Alternatives
Considered**. The spec itself had zero `[NEEDS CLARIFICATION]` markers;
this document captures the supporting technology choices.

---

## R-001: Polymorphic trigger / action persistence

**Decision:** Use `System.Text.Json`'s polymorphic serialization with
`[JsonPolymorphic]` + `[JsonDerivedType(typeof(...), "type-string")]`
on the `TriggerConfig` and `ActionConfig` abstract base types. Each
concrete variant gets a stable `"type"` discriminator string
(e.g. `"PowerLost"`, `"BatteryBelow"`, `"Webhook"`, `"RunCommand"`).

**Rationale:**
- Ships with the BCL in .NET 10; no extra dependency.
- Preserves round-trippable JSON — a hand-edited `config.json` from
  today's app upgrades transparently because today's values are the
  same discriminator strings we keep.
- Explicit discriminators satisfy Constitution IV: no
  `Assembly.Load` / `Type.GetType(string)` at deserialization time.
- The existing `ConfigManager.Save`/`Load` already uses
  `System.Text.Json` with camelCase naming; no re-plumbing needed.

**Alternatives Considered:**
- **Newtonsoft.Json `$type`**: adds a NuGet dependency and has a
  historically thorny security reputation (type-resolution RCE when
  allowlists are loose).
- **Hand-rolled `switch` on a string `type` field**: works but creates
  a second source of truth (enum in code + string in JSON) — easier to
  desync. Polymorphic attribute keeps the mapping on the type itself.
- **Separate concrete props** (e.g. `webhookActions: [], scriptActions:
  []` on the rule): breaks the ordered-list-of-actions semantics
  required by FR-010 (sequential within a rule).

---

## R-002: HTTP client for the Webhook action

**Decision:** One static `HttpClient` per app lifetime, wrapping a
`SocketsHttpHandler` with `PooledConnectionLifetime = TimeSpan.FromMinutes(5)`
and `AutomaticDecompression = All`. Per-request timeout via
`CancellationTokenSource.CancelAfter(timeout)` rather than
`HttpClient.Timeout` (so each webhook invocation has an isolated
deadline).

**Rationale:**
- One long-lived client avoids socket exhaustion under a burst (e.g.
  20 rules × 3 retries).
- `PooledConnectionLifetime` picks up DNS changes without an app
  restart.
- Per-CTS timeout is the idiomatic pattern for "timeout this one
  request"; `HttpClient.Timeout` is process-wide.
- No Avalonia / OS-specific handler wiring — `SocketsHttpHandler` is
  the cross-platform managed handler that works identically on
  Win/macOS/Linux, satisfying Principle II.

**Alternatives Considered:**
- **`HttpClientFactory`** (via `Microsoft.Extensions.Http`): more
  ceremony than value for a desktop app with one HTTP consumer. Adds
  a package.
- **A fresh `HttpClient` per fire**: simpler but socket-exhaustion
  risk under burst + slower (no keepalive reuse).
- **Native platform HTTP handlers** (`WinHttpHandler`,
  `NSUrlSessionHandler`): would break Principle II (different
  behaviour per OS) for no observable user benefit.

---

## R-003: Cross-platform shell invocation

**Decision:** Define `IShellExecutor` in `Core/Platform/` with a single
method:
```csharp
Task<ShellExecResult> RunAsync(
    string command, TimeSpan timeout, CancellationToken ct);
```
Each platform project implements it with its own shell + argv:
- Windows: `cmd.exe /c "<command>"` (fall back to `pwsh -NoProfile -Command "<command>"` if the command's per-OS field was authored as a PowerShell string — detected by a `shell` tag on the action config, default `cmd`).
- macOS: `/bin/sh -c "<command>"`.
- Linux: `/bin/sh -c "<command>"`.

`ShellExecResult` carries `ExitCode`, `StdOut` (first N KB),
`StdErr` (first N KB), `TimedOut`, `Duration`.

**Rationale:**
- Matches Principle II: OS detail lives in Platform projects, Core
  ships only the interface.
- Single `-c` form keeps quoting and argv semantics uniform across
  Unix; cmd.exe `/c` for Windows command-line (most common user
  expectation).
- Optional PowerShell on Windows because many users' tooling is PS-
  flavoured (`shutdown.exe` is a cmd command; `Stop-Computer` is PS).
  A per-action `shell: "powershell"` opt-in handles both.
- Stdout/stderr capture with a size cap keeps audit rows bounded
  (FR-018) even when a chatty script runs.

**Alternatives Considered:**
- **`Process.Start(new ProcessStartInfo { FileName = "/bin/sh", ... })`
  inlined into `RunCommandAction`**: simpler but pulls process-spawn
  idioms into `Core`, violating Principle II.
- **One "universal" shell** via a bundled `busybox` / `7z` / `pwsh`:
  massive bundled weight; users can't trust an embedded shell they
  didn't pick.
- **No shell, pure argv exec**: loses template substitution in
  pipelines / redirects, which real scripts use (`./foo.sh | tee
  log.txt`).

---

## R-004: Concurrency cap + bounded FIFO queue

**Decision:** `System.Threading.Channels.Channel.CreateBounded<RuleAction>(
new BoundedChannelOptions(capacity: 256) { FullMode =
BoundedChannelFullMode.DropOldest })` combined with a
`SemaphoreSlim(initialCount: 8, maxCount: 64)` as the concurrency
limiter. Reader loop grabs from the channel, awaits the semaphore,
spawns a task that runs the action and releases the semaphore.

**Rationale:**
- `Channel<T>` is the BCL primitive for producer/consumer with bounded
  capacity and first-class back-pressure.
- `DropOldest` matches FR-010a's "drop oldest of the same rule first"
  behaviour — we implement per-rule dedup above the channel (e.g., a
  `ConcurrentDictionary<ruleId, CountsOfPendingActions>` that removes
  the oldest pending entry for a rule whose bucket is saturated before
  accepting a new one).
- `SemaphoreSlim` with adjustable release is the standard
  ThreadCount-style limiter; cap can be reconfigured at runtime by
  releasing/taking permits without disposing the semaphore.
- Both types work identically across all three target OSes
  (Principle II).

**Alternatives Considered:**
- **`Parallel.ForEachAsync`** with `MaxDegreeOfParallelism`: designed
  for enumerable workloads, not long-lived queue-consumer scenarios.
- **Custom `TaskScheduler`**: much more code, no payoff for desktop
  scale.
- **`ActionBlock<T>`** from TPL Dataflow: good fit but pulls in
  `System.Threading.Tasks.Dataflow` — one extra package. Channels
  cover the same need in the BCL.

---

## R-005: SQLite audit table in the existing history.db

**Decision:** Add two tables to `history.db` (already created by
`SqliteHistoryStore`):

```sql
CREATE TABLE IF NOT EXISTS rule_firings (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    ts                 INTEGER NOT NULL,       -- unix seconds UTC
    rule_id            TEXT    NOT NULL,
    rule_name          TEXT    NOT NULL,
    device_sn          TEXT    NOT NULL,
    trigger_type       TEXT    NOT NULL,
    trigger_value_json TEXT    NOT NULL,       -- frozen trigger context
    is_test            INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_rule_firings_ts ON rule_firings(ts DESC);
CREATE INDEX IF NOT EXISTS ix_rule_firings_rule ON rule_firings(rule_id, ts DESC);

CREATE TABLE IF NOT EXISTS rule_firing_actions (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    firing_id      INTEGER NOT NULL REFERENCES rule_firings(id) ON DELETE CASCADE,
    ordinal        INTEGER NOT NULL,           -- position in rule
    action_type    TEXT    NOT NULL,
    outcome        TEXT    NOT NULL,           -- success|failure|skipped|timeout|dropped
    duration_ms    INTEGER NOT NULL,
    error_text     TEXT    NULL,               -- first 512 chars
    detail_json    TEXT    NULL                -- type-specific info (HTTP status, exit code)
);
CREATE INDEX IF NOT EXISTS ix_rule_firing_actions_firing
    ON rule_firing_actions(firing_id);
```

Retention pruning runs from a `Timer` (fire daily) that executes
`DELETE FROM rule_firings WHERE ts < ?` with today − retention-days.
Child rows cascade.

**Rationale:**
- Reuse the existing DB and connection pool — no second SQLite file to
  manage, no new Microsoft.Data.Sqlite wiring.
- Two-table structure keeps row size bounded and lets the UI render
  "summary + expand on click" efficiently.
- `is_test` column separates synthetic test fires (FR-015) from real
  ones at query time.
- `ON DELETE CASCADE` makes retention pruning a single DELETE.

**Alternatives Considered:**
- **One wide table**: each firing row holds JSON array of actions.
  Simpler schema, but action-level filtering in the UI needs a full
  table scan. Two-table wins for audit-log UX.
- **A new `rules.db`**: avoids coupling to history but forces two DB
  connections in the app. No benefit.

---

## R-006: Device-offline detection

**Decision:** New service `DeviceOfflineWatcher` in `Core/Triggers/`
runs on a `PeriodicTimer(TimeSpan.FromSeconds(10))` and, for each
monitored device, compares `DateTime.Now - state.LastDataReceived` to
the rule's configured window. On first crossing past the threshold,
raises a synthetic `DeviceOffline` trigger through the same
`TriggerEvaluator.Evaluate` call path real telemetry uses. When data
resumes, raises `DeviceOnline`. Both are edge triggers.

**Rationale:**
- Keeps trigger dispatch flowing through one code path — the evaluator
  doesn't gain a second entry point.
- `PeriodicTimer` is the BCL-recommended successor to `Timer` for
  async loops and has clean cancellation semantics.
- 10 s poll is small enough that a 5-minute offline threshold resolves
  within 2 % of the configured window.

**Alternatives Considered:**
- **Compute offline on every `DeviceUpdated`**: doesn't work when no
  update is happening (which is the condition we're detecting).
- **Per-device `Timer` reset on each update**: more moving parts; one
  central poller is simpler and cheap.

---

## R-007: Testing approach

**Decision:** Add `EcoFlowMonitor.Core.Tests` project using xUnit 2.9,
`FluentAssertions` 6.x. Test only the pure-logic pieces: trigger
evaluation, template expansion, retry policy, bounded-queue semantics,
audit store. UI and `Orchestrator` glue remains manually verified via
the "Test rule now" button (FR-015) — adding Avalonia Headless testing
is out of scope for this feature.

**Rationale:**
- Covers the mutation-prone areas (threshold math, flap/cooldown,
  retry backoff) without boiling the ocean.
- xUnit is the .NET default; FluentAssertions gives readable test
  failures.
- No existing test project exists (CLAUDE.md) — adding one here also
  unlocks future non-feature tests, a modest net good.

**Alternatives Considered:**
- **NUnit / MSTest**: functionally equivalent; xUnit picked on
  popularity/tooling familiarity.
- **No tests, purely manual**: violates the spirit of Principle I
  (reliability) and Principle V (reproducibility) — the flap/cooldown
  logic alone is not something we want to regression-test by hand.

---

## R-008: Privilege detection UX

**Decision:** Re-use the existing `IElevationService` (per CLAUDE.md)
from each platform project. Add a pure-function helper in Core,
`ElevationRequirements.Detect(RuleConfig rule, OSPlatform os)`, that
returns a list of `RequiredCapability` entries (e.g.
`SystemShutdown`, `SystemHibernate`, `RootShellExec`). The rule editor
composes this detection with `IElevationService.IsElevated` at save
time to decide whether to show the inline warning required by FR-027.
Platform-specific instructions are surfaced as Markdown blobs
embedded in the ViewModel (one per OS), not loaded from disk.

**Rationale:**
- Keeps the detection logic in Core (pure, testable).
- Avoids adding a new platform service — re-uses what's already there.
- Embedded Markdown strings survive trimming trivially (they're
  strings, not types) — no Principle IV concern.

**Alternatives Considered:**
- **Runtime detection** (try the action, observe failure): violates
  FR-027 (detection is at save time).
- **External docs link only**: weaker UX — users setting up automation
  want the instruction inline, not in a web page.
