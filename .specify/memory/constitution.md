<!--
SYNC IMPACT REPORT
==================
Version change: (template) → 1.0.0
Bump rationale: Initial ratification — constitution was a pristine placeholder
template; this is the first concrete, signed version. MAJOR in semver terms
because it establishes binding rules where none existed.

Modified principles:
  (none — first version)

Added sections:
  - Core Principles × 5 (Reliability Is the Product, Platform Abstraction at
    the Boundary, Protocol Fragility Is a Given, Reflection-Safe Build Chain,
    Reproducible Cross-Platform Releases)
  - Security & Secrets
  - Development Workflow
  - Governance

Removed sections:
  (none)

Templates requiring updates:
  ✅ .specify/templates/plan-template.md — inspected; generic "Constitution
     Check" slot is compatible with the new principles (no edits required).
  ✅ .specify/templates/spec-template.md — inspected; scope sections do not
     conflict.
  ✅ .specify/templates/tasks-template.md — inspected; task categorisation is
     generic enough to absorb the new principles without changes.
  ✅ .specify/templates/agent-file-template.md — inspected; no principle
     references to update.
  ✅ .specify/templates/checklist-template.md — inspected; no principle
     references to update.
  ⚠ CLAUDE.md — operational detail only; does not contradict the constitution
     but may be cross-referenced from it going forward.
  ⚠ README.md — the Download / build-script section is consistent with the
     Reproducible Releases principle; no edits required.

Follow-up TODOs:
  (none — all placeholders resolved; ratification and amendment dates set to
   today.)
-->

# EcoFlow UPS Monitor Constitution

## Core Principles

### I. Reliability Is the Product

The user trusts this app to know whether their power is OK. A monitor that
silently loses its connection is worse than no monitor at all. Therefore:

- Every I/O channel (MQTT, BLE, REST) MUST expose its connection state through
  a typed status enum surfaced to the UI; no hidden "we're probably connected"
  states.
- Every disconnect or decode failure MUST be logged, and the owning component
  MUST attempt reconnection (with bounded backoff) until explicitly stopped.
- Silent `catch { }` is FORBIDDEN. Swallowed errors MUST at minimum call
  `Logger.Log(...)` with enough context to reconstruct the fault later.
- The single-instance mutex and global exception handlers MUST remain in
  `Program.cs`; removing them is a MAJOR constitutional change.

**Rationale:** The app is positioned as a UPS watchdog. A watchdog that doesn't
bark is a liability.

### II. Platform Abstraction at the Boundary

OS-specific behaviour MUST live behind interfaces in
`src/EcoFlowMonitor.Core/Platform/` (for example `IBleAdapter`,
`INotificationService`, `IPowerActionService`). OS implementations live only in
`src/EcoFlowMonitor.Platform.{Windows,macOS,Linux}/`.

- `EcoFlowMonitor.Core` MUST have zero direct OS dependencies beyond
  `Environment.SpecialFolder` and `OperatingSystem.IsXxx()`. No `using
  CoreBluetooth`, no `using Windows.Devices.*`, no BlueZ D-Bus types in Core.
- Adding a new OS capability MUST start with a Core interface; the App and
  Platform projects adapt to that interface.
- Conditional `ProjectReference`s in `EcoFlowMonitor.App.csproj` MUST remain
  gated on `$([MSBuild]::IsOSPlatform(...))` so each host only pulls in its
  own platform project.

**Rationale:** Three platforms, one codebase, and a CI matrix that builds each
on its native runner — this only works if the platform seams are crisp.

### III. Protocol Fragility Is a Given

EcoFlow exposes no public API. Every protocol this app speaks — REST at
`api.ecoflow.com`, MQTT at `mqtt.ecoflow.com`, the BLE GATT and protobuf
framing — has been reverse-engineered. It can break without notice.

- Protocol decoders (`ProtobufDecoder.Dispatch`, `BleDispatcher.Dispatch`,
  `BlePacketParser`) MUST return `false` / `null` on any parse error and MUST
  NOT throw into the caller's hot path.
- Unknown `cmdFunc` / `cmdId` / `src` combinations MUST be logged at Debug,
  not Error; a new firmware revision is not a bug report.
- New device support MUST be added behind a dispatch branch; the existing
  Delta 3 / Delta 3 Max path MUST NOT be altered to accommodate other devices
  unless the behaviour is demonstrably identical.
- The embedded `keydata.b64` resource MUST remain an `<EmbeddedResource>` and
  MUST NOT be regenerated, rotated, or split out to an external file. BLE
  Type 7 handshake breaks without it.

**Rationale:** Resilience against the next firmware drop is a feature, not an
afterthought.

### IV. Reflection-Safe Build Chain

`PlatformServiceFactory` loads its per-OS siblings via
`Assembly.Load("EcoFlowMonitor.Platform.Xxx")` and resolves concrete types via
`Type.GetType(string)`. `BleKeyData` reads an embedded resource via
reflection. These paths MUST survive every build configuration.

- `PublishTrimmed` MUST NOT be enabled for the App project. On `net10.0-macos`
  where the workload forces it on, `LinkMode=None` MUST be passed to disable
  actual linking.
- AOT (`PublishAot`) MUST NOT be enabled without first proving the
  reflection-loaded types survive.
- ILLink warnings (`IL2026`, `IL2072`) about `GetType` / `Assembly.Load` are
  expected and MUST NOT be suppressed by changing the code to remove the
  reflection. They are the *reminder* that trimming is unsafe here.
- The single-file publish (Windows/Linux) MUST include
  `IncludeNativeLibrariesForSelfExtract=true`; SQLite and SkiaSharp native
  bits break without it.

**Rationale:** Losing a platform service at runtime is a silent failure mode
(the app boots, then BLE/notifications/shutdown actions just don't work). Not
worth the binary-size savings.

### V. Reproducible Cross-Platform Releases

A tagged release (`vX.Y.Z`) MUST produce, through CI with zero manual
post-processing, a self-contained, signed-where-required, ready-to-run binary
for each supported target.

- The GitHub Actions matrix MUST cover `linux-x64`, `win-x64`, and
  `osx-arm64` at minimum. Other RIDs MAY be added but MUST NOT replace these
  three.
- macOS `.app` bundles MUST be ad-hoc re-signed after publish with the
  entitlements in `src/EcoFlowMonitor.App/Entitlements.plist`
  (`disable-library-validation`, `allow-jit`,
  `allow-unsigned-executable-memory`, `allow-dyld-environment-variables`) —
  without this, macOS 15+ kernel kills the process at dyld-load with
  `CODESIGNING / Invalid Page`.
- `scripts/build-{macos.sh,linux.sh,windows.ps1}` MUST stay in lock-step with
  the CI recipe so a `git clone && ./scripts/build-<os>.sh` produces the same
  artifact as CI.
- macOS CI MUST pin Xcode 26.2 (not the default selection) and use `dotnet
  workload restore` — the defaults hit Xcode stub-SDK and workload
  CDN-cache bugs.

**Rationale:** "Works on my laptop" is not a release channel. The pipeline is
the release channel.

## Security & Secrets

- User credentials (EcoFlow email + password, `CloudUserId`, `LocalUserId`)
  MUST be persisted only to the per-OS local config path
  (`%APPDATA%\EcoFlowMonitor\config.json` on Windows,
  `~/Library/Application Support/EcoFlowMonitor/config.json` on macOS,
  `~/.config/EcoFlowMonitor/config.json` on Linux).
- Credentials MUST NEVER be logged, emitted to stderr, included in crash
  dumps, or transmitted to any host other than EcoFlow's own
  (`api.ecoflow.com`, `mqtt.ecoflow.com`) and the device itself.
- Config files MUST NEVER be committed to the repository. `config.json` is
  gitignored at repo root and MUST stay gitignored.
- No third-party analytics, error reporting, or telemetry may be added
  without an explicit opt-in switch in Settings that defaults to OFF.

## Development Workflow

- Every PR to `main` MUST pass the `build.yml` matrix (linux-x64, win-x64,
  osx-arm64) before merge. The `release.yml` pipeline fires only on tags
  matching `v*.*.*` and on manual dispatch.
- Commit messages follow Conventional Commits prefixes already in use in the
  repo: `feat:`, `fix:`, `ci:`, `docs:`, `chore:`, `refactor:`. The
  `Co-Authored-By` trailer for AI-assisted commits is allowed and encouraged.
- The UI is Avalonia MVVM. ViewModels expose observable state; Views bind.
  Direct `Dispatcher.UIThread.Post(...)` calls are reserved for event
  handlers that receive data on background threads — see
  `DashboardViewModel.OnDeviceUpdated` as the canonical example.
- Changes to the on-wire protocol files (`*.proto`, `BlePacketBuilder.cs`,
  `BlePacketParser.cs`, `ProtobufDecoder.cs`, `BleCrypto.cs`) MUST be
  described in the PR body with a captured sample (hex dump or log excerpt)
  motivating the change.

## Governance

This constitution is the single source of truth for non-negotiable rules in
the EcoFlow UPS Monitor project. Operational detail (code style, dependency
versions, per-file conventions) belongs in `CLAUDE.md` and `README.md`, not
here.

Amendment procedure:

1. Proposed changes land as a PR that edits `.specify/memory/constitution.md`
   and bumps the version line at the foot of the file.
2. Version bumps follow semantic versioning:
   - **MAJOR** — a principle is removed, inverted, or redefined in a way that
     invalidates existing code (e.g. dropping Platform Abstraction).
   - **MINOR** — a new principle or section is added, or an existing one is
     materially expanded.
   - **PATCH** — clarifications, typo fixes, rewording, or non-semantic
     refinements.
3. The PR body MUST include a Sync Impact Report listing every template,
   doc, or code area that needs follow-up. Flagged items stay `⚠ pending`
   until closed.
4. Merging the amendment requires at least one reviewer approval.
5. `LAST_AMENDED_DATE` is updated to the merge date; `RATIFICATION_DATE` is
   immutable after the first signed version.

Compliance review:

- Any PR that appears to violate a principle MUST be called out in review
  with a reference to the principle number.
- Violations MAY still be merged if the PR includes a justification in its
  body and a linked issue proposing the relevant constitutional amendment;
  principle drift without an amendment is not permitted.

**Version**: 1.0.0 | **Ratified**: 2026-04-19 | **Last Amended**: 2026-04-19
