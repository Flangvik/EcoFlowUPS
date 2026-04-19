# Quickstart — Rules Engine (Full)

Five-minute tour for a user adding their first automation after the
feature ships. These flows map directly onto the acceptance scenarios
in `spec.md`; each also doubles as a manual verification script.

---

## 1. Call a webhook on power loss (User Story 1)

**Goal:** Ping `https://home.example/hooks/ups` with a JSON body
whenever the station switches from mains to battery.

1. Open the app. Left nav → **Automation** → **Add rule**.
2. Fill the form:
   - **Name:** `Notify HA on power loss`.
   - **Device:** (pick your device from the dropdown).
   - **Trigger:** `Power lost`.
   - **Actions → Add → Webhook.**
     - **URL:** `https://home.example/hooks/ups`
     - **Method:** `POST`
     - **Headers** (Add →): `Authorization: Bearer <your-ha-token>`
     - **Body template:** leave empty (use the default JSON body —
       schema in `contracts/webhook-request.json`).
     - **Retries:** `2`. **Retry delay (ms):** `1000`.
3. **Save.** The rule appears in the list, enabled.
4. Click **Test rule now** on the rule row. A synthetic `PowerLost`
   fires with your last-known device state; you see the webhook POST
   land at your server within ~1 s.
5. Unplug the station for real: within ≤5 s (cloud) or ≤2 s (BLE) the
   webhook fires again, this time `isTest: false`.

**What's happening under the hood:**
- `TriggerEvaluator.Evaluate` detects the `PowerLost` edge.
- `ActionRunner.Enqueue` pushes the Webhook action onto the bounded
  channel. Semaphore (cap=8) admits it to a worker.
- Worker calls the shared `HttpClient`, wraps the request in a
  per-attempt `CancellationTokenSource.CancelAfter(timeoutMs)`.
- On failure: retry per `retries`/`retryDelayMs`, each attempt
  written to `rule_firing_actions`.

---

## 2. Run a platform-specific script on low battery (User Story 2)

**Goal:** Run a PowerShell cleanup on Windows, a bash cleanup on
macOS, and a bash cleanup on Linux — one rule, three command fields.

1. **Automation → Add rule.**
2. Form:
   - **Name:** `Graceful shutdown prep`.
   - **Trigger:** `Battery below` → `Threshold:` `10`.
   - **Actions → Add → Run command.**
     - **Shell:** `sh` (default).
     - **Command (Windows):** `powershell -NoProfile -File "C:\Tools\ecoflow-shutdown-prep.ps1" -Device "{device}" -Pct {battery}`
       (Switch **Shell** → `powershell` if you'd rather drop the
       `powershell -File` prefix.)
     - **Command (macOS):** `/usr/local/bin/ecoflow-shutdown-prep.sh --device "{device}" --pct {battery}`
     - **Command (Linux):** `/usr/local/bin/ecoflow-shutdown-prep.sh --device "{device}" --pct {battery}`
     - **Timeout (ms):** `30000`.
3. Save. The editor warns if any destructive built-ins (Shutdown,
   Hibernate) are selected AND the app is running non-elevated.
4. Simulate via **Test rule now**, or let real battery drop.

**Template variables** expand to snapshot values: `{device}`,
`{battery}`, `{remain}`, `{status}`, `{in_w}`, `{out_w}`, plus new
`{temp_c}`, `{ac_plugged}`, `{charge_state}`, `{device_sn}`.

---

## 3. New trigger types (User Story 3)

Quick reference for authoring each new trigger:

| You want… | Trigger | Parameter |
|---|---|---|
| Hot-station alert | `Temperature above` | `thresholdC: 55.0` |
| AC line pulled (distinct from grid power loss) | `AC unplugged` | (none — edge) |
| Low solar input | `Input watts below` | `thresholdW: 50` |
| Device unreachable for 5 min | `Device offline` | `windowSeconds: 300` |
| Device came back | `Device online` | (none — pair with above) |

All level triggers respect a 5-minute cooldown by default; edge
triggers fire once per transition.

---

## 4. Edit, disable, duplicate (User Story 4)

- **Toggle** the `Enabled` switch on a rule row → disabled rules are
  never evaluated.
- **Edit** via the pencil icon → same form as Add, pre-filled. Save
  overwrites in place; `modifiedAt` bumps.
- **Duplicate** via the copy icon → creates `Rule name (copy)` with a
  fresh GUID, same trigger + actions, disabled by default.
- **Delete** via the trash icon → confirmation dialog; audit entries
  for that rule stay in the history.

---

## 5. Audit (User Story 5)

- **Automation → History** shows every firing, newest first.
- Each row: timestamp, rule name, device, trigger value, then one
  badge per action (green success / red failure / grey skipped /
  amber timeout / blue test).
- Expand a row to see per-action detail:
  - Webhook: HTTP method + URL (with sensitive-query warning if
    applicable), status code, response-body excerpt, attempt count.
  - RunCommand: exit code, stderr head, stdout head, which OS
    dispatched.
  - Others: message / script path as appropriate.
- Top-right filter: device + rule + date range.
- **Retention** is set in Settings → General → `Audit retention
  (days)`. Default 30. Pruning runs on app start and once every 24 h.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Rule didn't fire on power loss | Rule is disabled, orphaned, or cooldown hasn't elapsed | Check the Enabled toggle; hover the rule for a badge showing orphan / cooldown remaining. |
| Webhook audit shows repeated 403 | Auth header missing or wrong | Edit rule; re-add `Authorization` header. Headers matching redaction patterns are hidden from the audit row body. |
| RunCommand audit says `skipped: no command for linux` | You authored the rule on Windows only | Edit rule → fill the Linux (and/or macOS) command field. |
| Warning in rule editor: "Requires elevation on Windows" | You picked `Shutdown` or `Hibernate` and the app isn't admin | Follow the inline instructions to run the app as administrator. The rule can still save; it just won't work until the app is elevated. |
| Audit history growing fast | Level trigger is flapping at threshold | Raise the cooldown, tighten the threshold, or widen the hysteresis (e.g. `BatteryBelow 15` + `BatteryAbove 20` instead of a single tight threshold). |

---

## Verifying the build is healthy

```bash
# From a local checkout after implementing this feature:
./scripts/build-macos.sh 0.0.0-rules-engine-smoke     # or build-linux.sh / build-windows.ps1
open 'src/EcoFlowMonitor.App/bin/Release/net10.0-macos/osx-arm64/EcoFlowMonitor.App.app'

# Exercise:
#   1. Automation menu appears in the left nav.
#   2. Add a Webhook rule pointing at http://localhost:8080.
#   3. In another terminal: `nc -l 8080`.
#   4. Click "Test rule now" on the rule.
#   5. nc prints a POST with the JSON body. ✅
```

Unit tests: `dotnet test src/EcoFlowMonitor.Core.Tests/`.
