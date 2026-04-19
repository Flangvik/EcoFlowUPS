# Migration notes — Rules Engine (v1)

## TL;DR

**No action required.** Existing `config.json` files continue to load
unchanged. New optional fields default to sensible values. The audit
tables are created automatically in `history.db` on first launch after
upgrading.

## What changed in `config.json`

All additions are **optional fields**; no existing key was removed or
renamed.

- `Devices[].Rules[].Trigger` gained:
  - `CooldownSeconds` (int?, null = use default 300 s for level triggers,
    0 s for edge triggers).
  - `ThresholdF` (float?) — decimal threshold for `TempAbove` /
    `TempBelow`. Ignored by v0 triggers.
  - `WindowSeconds` (int?) — offline-detection window for
    `DeviceOffline` / `DeviceOnline`. Defaults to 300 s.
- `Devices[].Rules[].Actions[]` gained two optional nested blocks:
  - `Webhook` — populated only when `Type == "Webhook"`. Fields:
    `Url`, `Method` (POST/PUT), `Headers` (dict), `BodyTemplate`,
    `Retries`, `RetryDelayMs`, `TimeoutMs`.
  - `RunCommand` — populated only when `Type == "RunCommand"`. Fields:
    `CommandWindows`, `CommandMacOS`, `CommandLinux`, `Shell` (enum),
    `WorkingDirectory`, `TimeoutMs`.
- `General` gained three settings (all defaults applied if absent):
  - `AuditRetentionDays` = 30
  - `MaxConcurrentActions` = 8
  - `ActionQueueCapacity` = 256

## What changed in `history.db`

Two new tables added on first run by `SqliteRuleFiringStore`:

```
rule_firings         (id, ts, rule_id, rule_name, device_sn,
                      trigger_type, trigger_value_json, is_test)
rule_firing_actions  (id, firing_id, ordinal, action_type, outcome,
                      duration_ms, error_text, detail_json)
```

Both are created with `CREATE TABLE IF NOT EXISTS`. No DDL needed to
apply by hand; simply launch the upgraded app.

## What changed behaviourally

- Actions are now **asynchronous** — they run off the monitor pipeline
  through a bounded channel with a concurrency cap. A runaway script
  or unresponsive webhook can no longer block telemetry ingestion.
- Every rule firing now appends to the audit log — a previously
  silent failure path (swallowed `catch { }` inside the old inline
  `ActionRunner.Run` call-site) now always leaves a row explaining
  what happened.

## Rollback

To roll back to pre-rules-engine behaviour you'd need the previous
binary. Downgrading the app against an already-upgraded `history.db`
is fine — older code ignores the two new tables; they'll just sit
there until the next upgrade or until you drop them manually.

## Known incompatibilities

None. If you find one, file an issue referencing this file and the
problematic `config.json` snippet.
