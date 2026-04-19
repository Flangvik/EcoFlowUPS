-- EcoFlowMonitor rule-firing audit schema
--
-- Lives in the existing history.db alongside the telemetry and power-event
-- tables. Created by SqliteRuleFiringStore on first use; subsequent
-- applications of this DDL are idempotent thanks to IF NOT EXISTS.
--
-- Rows are appended once per rule firing (real or test) and pruned once
-- daily by a background timer (see plan.md R-005).

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS rule_firings (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    ts                 INTEGER NOT NULL,       -- unix seconds UTC
    rule_id            TEXT    NOT NULL,       -- snapshot; survives rule deletion
    rule_name          TEXT    NOT NULL,       -- snapshot
    device_sn          TEXT    NOT NULL,       -- snapshot
    trigger_type       TEXT    NOT NULL,       -- discriminator e.g. PowerLost
    trigger_value_json TEXT    NOT NULL,       -- frozen DeviceStateSnapshot (JSON)
    is_test            INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_rule_firings_ts
    ON rule_firings(ts DESC);

CREATE INDEX IF NOT EXISTS ix_rule_firings_rule
    ON rule_firings(rule_id, ts DESC);

CREATE TABLE IF NOT EXISTS rule_firing_actions (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    firing_id      INTEGER NOT NULL
                   REFERENCES rule_firings(id) ON DELETE CASCADE,
    ordinal        INTEGER NOT NULL,           -- position of action in rule
    action_type    TEXT    NOT NULL,           -- Webhook | RunCommand | Shutdown | ...
    outcome        TEXT    NOT NULL
                   CHECK (outcome IN
                       ('success', 'failure', 'skipped', 'timeout', 'dropped')),
    duration_ms    INTEGER NOT NULL,
    error_text     TEXT    NULL,               -- first 512 chars
    detail_json    TEXT    NULL                -- type-specific JSON payload
);

CREATE INDEX IF NOT EXISTS ix_rule_firing_actions_firing
    ON rule_firing_actions(firing_id, ordinal);

--
-- Retention pruning (executed by the app, not by the DB):
--
--   DELETE FROM rule_firings
--   WHERE ts < :cutoff_unix_seconds;
--
-- Child rows cascade via ON DELETE CASCADE. `:cutoff_unix_seconds` is
-- `strftime('%s', 'now') - retention_days * 86400`, where
-- `retention_days` comes from AppConfig.General (default 30).
