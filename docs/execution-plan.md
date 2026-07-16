# Execution Plan

## Phase 0 — Project baseline

- Install and verify .NET 10 SDK.
- Create the WPF app, core library, and test project.
- Establish docs, logs, agent instructions, build rules, and a passing baseline test.
- Gate: `dotnet build` and `dotnet test` both pass from a clean checkout.

## Phase 1 — Local history core

- Status: completed on 2026-07-15.
- Implement model, SQLite persistence, image storage, sorting, deduplication, retention, pin/delete/clear, and tests.
- Gate: behavior works without WPF or the real Windows clipboard.

## Phase 2 — Windows clipboard integration

- Status: implementation complete; event-driven capture, bitmap-to-PNG conversion, copy-back, retries, and clipboard-sequence self-write suppression are working. Manual acceptance remains.
- Add event-driven clipboard monitoring, text/image extraction, retry behavior, and self-write suppression.
- Gate: manual capture matrix passes across representative Windows applications.

## Phase 3 — Tray UI

- Add tray lifecycle, popup window, search, cards, settings controls, and accessibility.
- Status: implementation complete pending the manual acceptance matrix; tray lifecycle, focus-loss hiding, active-monitor/per-monitor-DPI placement, search/count feedback, metadata cards, directed empty states, keyboard focus, pin/delete controls, pause/resume capture, confirmed clear-all cleanup, and persisted 1/3/5-day retention are working.
- Gate: feature parity with the published macOS local-history behavior.

## Phase 4 — Windows lifecycle

- Add global hotkey, launch-at-login, single-instance activation, and taskbar-restart recovery.
- Status: implementation complete pending the full human lifecycle matrix; named-mutex single-instance activation, configurable/disableable global-hotkey registration, current-user launch-at-login, and `TaskbarCreated` tray republishing are implemented. Real probes cover second-launch activation, hotkey window activation, non-fatal hotkey conflicts, the accessible startup toggle, exact `HKCU` Run registration, restoration of any pre-probe Run value, controlled taskbar-recreation messages without restarting Explorer, and stable placement inside the selected monitor work area under a real per-monitor-aware process.
- Gate: restart, conflict, and recovery scenarios pass.

## Phase 5 — Packaging and local release

- Produce portable ZIP, user-level installer, checksums, README, privacy statement, and release checklist.
- Status: release infrastructure is implemented for the scoped
  `0.1.0-preview.2+unsigned` preview. It builds and verifies a self-contained
  `win-x64` portable ZIP, SHA-256 sidecar, and non-administrator per-user setup.
  The public-repository layer includes separate English and Simplified Chinese
  entry points, MIT/community files, privacy-safe issue templates, and
  least-privilege GitHub CI/prerelease workflows.
  Official Inno Setup 7.0.2 x64 is hash/signature/publisher locked and extracted
  through its documented portable mode into project `tools/`, without a global
  install or PATH change. Packaging probes validate product identity, startup,
  PerMonitorV2, startup-failure guards, forbidden-content exclusions, and the
  installer definition. Successful installer artifact and application-start
  round trips remain restricted to a disposable profile with no installed,
  partial, or damaged HistoryClipboard product. Signing, clean-machine matrices,
  and the complete Phase 5 manual gate remain pending.
- Startup hardening is implemented: primary-instance initialization classifies
  unsupported-schema, local-storage, and unknown failures into stable safe
  notices, releases partially owned desktop resources deterministically, and
  exits nonzero. Unsupported-schema rejection is byte-for-byte non-mutating.
- Gate: clean-machine install, upgrade, portable launch, and uninstall tests pass.

## Phase 6 — Cross-device protocol prototype

- Freeze a versioned sync envelope and build in-memory Mac/Windows protocol fixtures.
- Implement explicit pairing identities, encrypted sessions, replay/loop protection, and local discovery.
- Gate: two test peers synchronize encrypted text events without clipboard or UI dependencies.

## Phase 7 — Mac ↔ Windows text sync

- Integrate the protocol into both native clipboard monitors and add connection/settings UI.
- Gate: bidirectional same-LAN copy-to-paste works through disconnects without loops.

## Phase 8 — Encrypted image sync

- Add capability negotiation, image limits, progress/error handling, and cleanup.
- Gate: supported images synchronize reliably without excessive memory or disk usage.

## Deferred roadmap

- Internet relay with end-to-end encryption, ARM64 packages, rich clipboard formats, and automatic updates require separate design approval.
