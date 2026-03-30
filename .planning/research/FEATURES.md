# Feature Landscape: UPS/Battery Monitoring Apps

**Domain:** Cross-platform desktop UPS/battery monitoring with local+cloud connectivity
**Researched:** 2026-03-30
**Confidence:** HIGH (NUT, PowerChute, CyberPower docs); MEDIUM (EcoFlow app, UX patterns)

---

## Research Basis

Surveyed: NUT (Network UPS Tools), APC PowerChute Personal/Business/Serial editions,
CyberPower PowerPanel Personal, Home Assistant NUT integration, Eaton Intelligent Power
Manager, EcoFlow official app. Cross-referenced against the project's own Active requirements
list to distinguish what the ecosystem validates as necessary vs. what is unique to this project.

---

## Table Stakes

Features users expect from any UPS/battery monitoring tool. Missing = product feels broken.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Live battery % + estimated runtime | Core safety information — every product shows this | Low | Already exists in EcoFlowUPS |
| Power state indication (on AC / on battery / charging) | The one thing UPS software must show | Low | PowerStateMachine exists; UI indicator needs polish |
| Visible connection status (connected / disconnected / reconnecting) | Users panic without it — NUT, APC, CyberPower all show this prominently | Medium | **Currently missing** — silent failures are the #1 complaint in codebase concerns |
| Auto-reconnect after disconnect | Every serious tool does this; no manual reconnect buttons | Medium | `ConnectLoopAsync` exists but no backoff, no feedback |
| OS notification on power lost / power restored | Every tool from NUT to PowerChute to EcoFlow app sends this | Low | `INotificationService` interface exists; wiring to rules needed |
| OS notification on low battery threshold | APC, CyberPower, NUT all support configurable %, typically 20% default | Low | `BatteryBelow` trigger exists; just needs notification action wired |
| Settings page with threshold configuration | Users need to tune without editing JSON | Medium | SettingsViewModel exists but is non-functional |
| Event log / notification history | Even simple UPS tools (APC eventlog.dat, NUT upsmon logs) keep a record | Medium | **Currently absent** — no audit trail of when events fired |
| Graceful handling of device offline | Must show last-known state with staleness indicator, not a blank screen | Medium | **Currently absent** — offline mode with cached state is in Active requirements |
| Input/output voltage + load % display | All major tools show these alongside battery % | Low | Voltage exists; load % should be surfaced |

---

## Differentiators

Features that set a product apart. Not universally expected, but add real user value. These are
the areas where EcoFlowUPS can be genuinely better than NUT/PowerChute for EcoFlow users.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Per-cell voltage display with min/max highlight | No generic UPS tool does cell-level visibility — EcoFlow BMS exposes it | Low | Already implemented; rare differentiator |
| BLE local connection (no cloud dependency) | PowerChute/NUT are USB/serial; this is network-independent monitoring | High | macOS done; Windows/Linux are the gap |
| Connection mode toggle (Auto/Cloud/BLE) per device | No comparable tool offers dual-path with user control | Medium | Exists; needs better UX feedback during mode transitions |
| Historical charts with configurable time range (hour/day/week) | EcoFlow app does day/week/month; NUT/PowerChute have primitive logs only; good charts are a differentiator for desktop | High | SQLite + LiveChartsCore in plan; not yet built |
| Multi-device sidebar | NUT supports multi-device but the UI is server/client config hell | Medium | Already exists |
| Webhook action on power event | NUT supports custom upsmon scripts; PowerChute supports command files; webhooks integrate with n8n/Home Assistant/Zapier — far more accessible | Medium | `ActionType` exists but webhook type not yet implemented |
| Multiple actions per rule (webhook + notify + script) | APC/CyberPower support ONE action per event; stacking is rare | Low | Architecture already supports it; just needs UI |
| Rules engine with AND-style trigger conditions | Eaton IPM has AND/OR rule logic; consumer tools don't — power users want "battery below 20% AND on battery for 10+ minutes" | High | Currently: single-trigger only; compound triggers are a v2 feature |
| SoH (State of Health) + cycle count display | Battery degradation tracking — not shown by any competitor; EcoFlow BMS exposes it | Low | Already implemented |
| Exponential backoff with visible retry counter | Industry tools either silently retry forever or fail loudly; showing "Reconnecting (attempt 3/∞, next in 8s)" builds trust | Low | No backoff currently; easy to add, high UX payoff |

---

## Anti-Features

Features to deliberately NOT build for this milestone. Either out of scope, counter-productive,
or that would distort the architecture without adding proportionate value.

| Anti-Feature | Why Avoid | What to Do Instead |
|--------------|-----------|-------------------|
| Web dashboard / HTTP server | Scope creep toward a different product (headless server tool); adds auth surface area | Defer to v2; Docker/headless mode is already in Out of Scope |
| Prometheus / InfluxDB / SNMP export | NUT already does this well; duplicating it adds protocol complexity | Link to NUT for users who need SNMP/Prometheus |
| Device control (set charge limits, toggle AC outlets) | Monitoring-only is a clear, defensible scope; control introduces failure modes and EcoFlow API breakage risk | Document in README as intentional omission |
| Compound AND/OR rules editor (visual) | Eaton IPM took years to build this well; a crude version is worse than no version | Support single-condition rules with good cooldown; defer compound rules |
| Cloud sync / multi-machine dashboard | Requires auth service, backend infra, and a second product surface | Out of scope for v1 |
| Alert sound effects / audio alarms | Annoying without extensive preference tuning; OS notifications are sufficient | Use OS notification system only |
| Mobile companion app | Completely different platform and deployment story | EcoFlow official app already covers mobile |
| CSV/PDF report generation | Requested by enterprise UPS tools; not the use case here | Historical charts with data export is a v2 consideration |
| Network discovery of remote UPS (NUT server) | EcoFlow devices are not NUT-compatible; would require a separate integration path | Not applicable to EcoFlow BLE/MQTT architecture |

---

## Feature Dependencies

```
SQLite persistence
  -> Historical charts (hour/day/week views)
  -> Event log (notification history)

Connection status indicator (UI)
  -> Exponential backoff with retry counter
    -> Graceful offline mode (last-known state + staleness badge)

OS notification action (working)
  -> Low battery threshold notification
  -> Power lost / restored notification
  -> Rules engine with notification action type

Working settings page
  -> Threshold configuration (battery low %, runtime low)
  -> Connection mode toggle UI
  -> Notification preference toggles
  -> Rule editor (create/edit/delete rules)

Rule editor (settings page)
  -> Webhook action type
  -> Shell script action type
  -> Multiple actions per rule
```

---

## MVP Recommendation

For this milestone (resilience, better UX, rules engine, history), prioritize in order:

**Must ship (table stakes gaps):**
1. Connection status indicator — visible in sidebar and dashboard header at all times
2. Exponential backoff with UI feedback (attempt count, next-retry countdown)
3. Graceful offline/stale state (last-known values + "last seen X ago" badge)
4. Working settings page — thresholds, notification prefs, connection config
5. OS notification action wired through rules engine for power lost/restored/battery low

**Should ship (high-value differentiators):**
6. Event log — timestamped history of every rule that fired (SQLite-backed)
7. Historical charts — battery % over time, hourly/daily views; SQLite persistence
8. Webhook action type in rules engine

**Defer (later milestone):**
- Linux BLE adapter (large platform work, separate milestone)
- Windows BLE adapter (same)
- Compound AND/OR trigger conditions
- Chart export / data download

---

## Competitive Positioning Notes

**NUT** is the gold standard for headless/server UPS management. Its weakness is UX: configuration
is INI files, the web UI is a CGI script, and there is no historical charting built in. Every
Home Assistant NUT user has to write their own Lovelace cards.

**APC PowerChute Personal** and **CyberPower PowerPanel Personal** are Windows-first USB tools.
They nail the basics (status, battery %, auto-shutdown) but have no historical visualization,
no webhook actions, and minimal rules flexibility.

**Eaton IPM** is enterprise: AND/OR rules, centralized fleet management, reporting — but it costs
money and is overkill for a single Delta 3.

**EcoFlow official app** does the best job of historical charts and energy analysis, but it is
mobile-only, cloud-dependent, and closed. A desktop app with BLE-local monitoring and a rules
engine fills a real gap that the official app does not cover.

**This project's niche:** Desktop-native, BLE-local-capable, visual, with a real rules engine and
historical charts. The user who cares about this is a homelab/prosumer who wants NUT-class
reliability with EcoFlow-app-class visualization, without cloud dependency.

---

## Phase-Specific Feature Flags

| Feature Area | Risk Level | Flag |
|-------------|------------|------|
| Connection resilience (backoff, state machine) | Low | Standard patterns; straightforward to implement |
| Offline/stale state caching | Low | Cache `DeviceState` on disk; display with staleness timestamp |
| Settings page (Avalonia forms) | Medium | Avalonia data validation is less mature than WPF; test input binding carefully |
| SQLite persistence + migrations | Medium | Use EF Core or Dapper; add migration path from day 1 or regret it |
| Historical charts (LiveChartsCore) | Medium | LiveChartsCore is already a dependency; aggregation queries may need tuning for large datasets |
| Rules engine UI (create/edit rules) | High | Most complex UI work in this milestone; list + detail form pattern; needs validation |
| Webhook action (HTTPS POST) | Low | Simple `HttpClient` call with JSON body; template expansion already exists |
| Event log view | Low | Read-only list from SQLite; trivial once persistence is in place |

---

## Sources

- [NUT Features](https://networkupstools.org/features.html) — HIGH confidence
- [NUT Wikipedia](https://en.wikipedia.org/wiki/Network_UPS_Tools) — HIGH confidence
- [APC PowerChute Personal Edition](https://www.se.com/us/en/product-range/61934-powerchute-personal-edition/) — HIGH confidence
- [CyberPower PowerPanel Personal Windows](https://www.cyberpowersystems.com/product/software/power-panel-personal/powerpanel-personal-windows/) — HIGH confidence
- [Home Assistant NUT Integration](https://www.home-assistant.io/integrations/nut/) — HIGH confidence
- [Eaton Intelligent Power Manager](https://www.eaton.com/us/en-us/catalog/backup-power-ups-surge-it-power-distribution/eaton-intelligent-power-manager.models.html) — MEDIUM confidence
- [EcoFlow App Features](https://www.ecoflow.com/us/app) — MEDIUM confidence
- [UPS Monitor Battery Backup Notifications](https://mcguirev10.com/2023/05/14/ups-monitor-battery-backup-event-notifications.html) — MEDIUM confidence (single author, but well-documented implementation)
- [Alert Fatigue Best Practices](https://www.datadoghq.com/blog/best-practices-to-prevent-alert-fatigue/) — MEDIUM confidence
- [Settings UX Best Practices](https://www.toptal.com/designers/ux/settings-ux) — LOW confidence (UX blog, not domain-specific)
