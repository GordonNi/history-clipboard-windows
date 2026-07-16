# Technical Specification

## Platform

- Language: C# with nullable reference types enabled.
- Runtime/UI: .NET 10 LTS and WPF.
- Minimum systems: supported Windows 10 x64 and Windows 11 x64 releases.
- Persistence: `Microsoft.Data.Sqlite` plus PNG files.
- Tests: a .NET test project with in-memory adapters and temporary filesystem/database integration tests.

## Solution shape

- `HistoryClipboard.App`: WPF composition root, tray UI, window controller, and Windows adapters.
- `HistoryClipboard.Core`: history model and behavior; no WPF or Win32 dependency.
- `HistoryClipboard.Infrastructure`: SQLite and filesystem persistence adapters; references Core but contains no UI.
- `HistoryClipboard.Tests`: core and integration tests.

Do not create an interface for every class. The initial real seams are clipboard access and history persistence because each has both a production adapter and a test adapter.

## Runtime flow

1. A hidden message window registers with `AddClipboardFormatListener`.
2. `WM_CLIPBOARDUPDATE` triggers the Windows clipboard adapter.
3. The adapter reads Unicode text first, then bitmap image, using bounded retries when another process owns the clipboard.
4. The core history module applies pause, deduplication, ordering, and retention rules.
5. Metadata is committed to SQLite; image bytes are atomically written as PNG files.
6. The view model receives the updated ordered collection.
7. Recopying an item records the applied event/sequence so the listener suppresses the self-generated notification.

History presentation uses one `ClipboardHistory.Query` interface. It trims and case-folds a text query, returns matching text items in history order, and returns complete-history text/image totals in the same result. Images are excluded only from nonblank search results; their total remains visible in the footer.

Card mutations remain in the history module. `TogglePin` returns the updated item so presentation can report the resulting state without duplicating toggle logic; `Delete` returns the removed item so presentation can report its type. Both mutations persist before in-memory state changes, then the window reruns the active query to refresh ordering, matches, and totals.

Capture pause state remains inside `ClipboardCaptureService`. A paused capture returns `Paused` without invoking the clipboard reader, and the retry loop rechecks pause state before every read and delay. Resuming affects future clipboard notifications only; paused content is not backfilled. Clear-all asks for explicit user confirmation, then delegates transaction and PNG cleanup to `ClipboardHistory.Clear` before rerunning the active query.

Retention preferences remain behind one concrete Infrastructure module because JSON and filesystem details are local and no second adapter is needed. Startup loads the saved 1/3/5-day value, falls back to the required 3-day default when the file is absent, malformed, or unsupported, then removes expired unpinned history before showing the window. A UI change saves first and only then invokes cleanup, so a settings write failure never causes history deletion; pinned records remain exempt.

## Data storage

Base directory:

```text
%LOCALAPPDATA%\HistoryClipboard\
```

Database record fields:

```text
id TEXT PRIMARY KEY
type INTEGER NOT NULL
created_at_utc TEXT NOT NULL
is_pinned INTEGER NOT NULL
text_content TEXT NOT NULL
image_path TEXT NULL
```

The database includes a schema-version table. Images use `Images\{id}.png`. Settings are stored separately in `%LOCALAPPDATA%\HistoryClipboard\settings.json`; the current document writes `retention_days` as 1, 3, or 5 plus the selected global hotkey. Writes use a unique same-directory temporary file, disk flush, and atomic replacement. Launch-at-login is deliberately not duplicated in JSON: the current executable's `HKCU` Run registration is its source of truth. The JSON format can later add pause state and sync preferences without mixing those concerns into the history database.

Schema version 1 is created transactionally. An unsupported version stops startup with a clear error instead of attempting an unsafe read. Core mutations write to the repository before changing in-memory state, and multi-record deletions use a transaction. SQLite connection pooling is disabled so the single-user desktop process releases database handles deterministically.

The explicit `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 reference overrides the vulnerable 2.1.11 native dependency currently selected transitively by `Microsoft.Data.Sqlite` 10.0.10.

PNG persistence remains inside the SQLite adapter so callers do not coordinate files and metadata. The adapter validates the PNG signature, writes to a unique temporary file, flushes it to disk, and atomically renames it to `Images\{id}.png` before inserting metadata. Database rows store a relative image path; loaded items expose a validated absolute path that cannot escape the application-owned Images directory.

Image deletion first atomically moves files to application-owned tombstones, deletes all selected metadata in one transaction, and restores the files if the transaction fails. After a successful commit, tombstones are removed; locked tombstones are retried during the next repository startup.

## Windows integration

- The Phase 2 clipboard tracer bullet attaches an `HwndSource` hook to the verification window, registers it with `AddClipboardFormatListener`, handles `WM_CLIPBOARDUPDATE`, and unregisters before releasing the hook.
- Core depends on one `IClipboardContentReader` seam whose values are either Unicode text or PNG bytes. WPF-specific `BitmapSource`, encoder, COM, and STA details do not cross into Core.
- The WPF adapter reads Unicode text first. If no supported text is available, it reads a bitmap with `Clipboard.GetImage` and encodes it in memory with `PngBitmapEncoder` before passing the PNG bytes to the history module.
- A temporary `COMException` is translated to the clipboard seam's unavailable result and retried up to five times with a 50 ms delay.
- Multiple messages received during a retry are coalesced and processed again after the active capture completes, preventing overlapping WPF clipboard reads.
- `ClipboardCopyService` exposes one copy operation plus one listener-side suppression check. It retries temporary write locks, while WPF text/image writing and Win32 sequence lookup remain behind injected adapters.
- The WPF writer stores text as Unicode and loads persisted PNG files completely before calling `Clipboard.SetImage`, so the source stream can be closed immediately.
- After a successful write, `GetClipboardSequenceNumber` records the resulting nonzero sequence. Listener updates with that sequence are suppressed; a different sequence clears the marker and is processed as an external update.
- Tray: a `System.Windows.Forms.NotifyIcon` deep module hosted by the WPF process. It owns a code-drawn overlapping-card icon, left-click toggle, show/exit context menu, deterministic disposal, and an idempotent republish operation.
- The WPF application uses explicit shutdown. Closing or minimizing the history window hides it without disposing the clipboard listener; only the tray Exit command permits window close and shuts down the process.
- Window deactivation schedules a short delayed hide. The delay lets a tray click complete its own toggle first, while modal clear-history confirmation temporarily suppresses auto-hide so the owning window does not disappear underneath the dialog.
- Tray-window placement is isolated behind `WindowPlacementController`. Before any initial, tray, hotkey, or secondary-instance show activates the WPF window, it selects the current foreground window's monitor; if unavailable it falls back to the cursor monitor, then the application window's nearest monitor, then the primary monitor.
- The controller reads the selected monitor's physical-pixel work area and effective DPI. Pure geometry converts the nominal WPF size and 16-DIP margin to target-monitor pixels, bottom-right aligns the popup, and clamps all four edges to the work area. `SetWindowPos` changes only the HistoryClipboard window; it does not move other windows or change display settings.
- The application declares `PerMonitorV2` in its WPF manifest. The mixed WPF/WinForms project suppresses WinForms warning `WFO0003` because the WinForms project property was verified to leave this WPF entry point only system-DPI-aware, while the manifest produces real per-monitor awareness.
- Hotkey: one `GlobalHotkeyController` owns the WPF message hook plus `RegisterHotKey`/`UnregisterHotKey` with `MOD_NOREPEAT`. The settings UI offers `Ctrl+Shift+V`, `Ctrl+Alt+V`, `Ctrl+Shift+H`, and disabled; the selection is persisted in `settings.json`. Registration failure is non-fatal and leaves tray operation available.
- Startup: one `LaunchAtLoginController` owns the current-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` access. The `HistoryClipboard` value is a quoted absolute path to the current executable and is never written unless the user enables the setting. Reads and writes return explicit success/failure results; access failures leave the tray process running, roll the UI back to its last known state, and report the failure in the status rail.
- Single instance: one deep `SingleInstanceController` module owns a session-local named mutex, an auto-reset activation event, the background wait loop, and deterministic handle release. `TryStart` returns `null` to a secondary launch after signaling the event, so that process shuts down before opening history storage or creating tray resources. The primary dispatches each activation request onto the WPF dispatcher and calls the existing `ShowFromTray` path. Kernel-object lifetime provides crash recovery without lock files.
- Primary-instance startup is guarded after `SingleInstanceController.TryStart`
  succeeds. `StartupFailureClassifier` gives unsupported-schema, local-storage,
  and unknown failures stable codes without copying exception messages, paths,
  clipboard content, or stacks into the UI. Unsupported schema recognition uses
  `UnsupportedSchemaVersionException` rather than message matching; it remains a
  `NotSupportedException` for compatibility with existing callers.
- `StartupFailureCoordinator` performs best-effort cleanup in the fixed order
  taskbar recovery, tray icon, window, then single-instance resources; it then
  presents one safe Chinese error and calls WPF shutdown with exit code `1`.
  Cleanup or presentation failures cannot skip remaining cleanup or nonzero
  shutdown. The secondary-instance `null` path remains outside this failure flow
  and exits normally without opening storage or presenting an error.
- Startup failure handling never migrates, repairs, recreates, deletes, or
  overwrites an unsupported existing database. Integration tests compare the
  complete database bytes before and after rejection.
- Clipboard listener and hotkey are unregistered during orderly shutdown.
- Explorer/taskbar recreation is isolated behind `TaskbarRestartRecoveryController`. It registers the shell's `TaskbarCreated` message, hooks the persistent WPF window handle, and invokes the tray module's idempotent republish operation when that message arrives. Shutdown removes the hook before disposing the tray icon, and both layers reject late recovery work so an exited application cannot recreate the icon. Verification sends the registered message directly to an application-owned test window or process; it never restarts or terminates the user's real `explorer.exe`.

## Local-network sync architecture for v0.2

The sync feature is a deep module with a small interface: pair/remove a device, start/stop sync, publish a local clipboard event, and observe connection state. Discovery, encryption, reconnection, framing, and replay protection remain internal.

Message envelope:

```text
protocol_version
event_id
source_device_id
source_sequence
content_type
created_at_utc
content_hash
payload_length
encrypted_payload
```

Design rules:

- Local discovery uses mDNS/Bonjour-compatible service discovery.
- Pairing exchanges long-term public identities through a short-lived code/QR flow.
- Sessions use authenticated ephemeral key exchange and authenticated encryption.
- Private device keys use macOS Keychain and Windows DPAPI-backed secure storage.
- Each client keeps a bounded applied-event cache and never republishes a received event.
- Simultaneous updates use arrival order per authenticated connection; clocks are informational rather than authoritative.
- Protocol version and capability negotiation allow text-only peers to interoperate before image support is enabled.
- The network transport is absent and disabled in v0.1, guaranteeing that the local-only release makes no network requests.

## Packaging

- The current artifact is versioned `0.1.0-preview.2`, self-contained for
  `win-x64`, and explicitly labeled `preview` plus `unsigned` in both assembly
  metadata and filenames.
- `scripts/build-portable-release.ps1` publishes without trimming or
  single-file bundling, fails on missing inputs or invalid content, and creates
  a deterministic-layout ZIP plus SHA-256 file under `artifacts/`.
- The portable package contains startup/runtime files, usage instructions, the
  privacy statement, and the release checklist. It rejects PDBs, local history,
  settings, image-history folders, logs, test results, and temporary/build files.
- `scripts/verify-portable-package.ps1` validates package structure, hash,
  embedded self-contained runtime files, unsigned-preview product identity,
  launch viability, extraction cleanup, and absence of residual processes.
- The Inno Setup definition uses `PrivilegesRequired=lowest`, defaults to
  `%LOCALAPPDATA%\Programs\HistoryClipboard`, accepts x64-compatible systems,
  and checks the application's single-instance mutex before update or removal.
- Installer input reuses the validated self-contained publish and rejects PDB,
  database, settings, image-history, logs, tests, and temporary/intermediate
  content. Setup filename and metadata remain explicitly `preview` and `unsigned`.
- Interactive uninstall defaults to keeping `%LOCALAPPDATA%\HistoryClipboard`
  and offers explicit removal. Silent uninstall never prompts or removes user
  data. The `HKCU` Run value is removed only when it exactly names the installed
  executable.
- The static installer probe works without a compiler. The round-trip probe is
  reserved for a compiled artifact in a disposable profile without existing
  clipboard data.
- Inno Setup is pinned by version, official release URL, size, SHA-256, official
  `.issig` descriptor SHA-256, and Authenticode publisher. Its own documented
  `/PORTABLE=1` mode extracts the compiler beneath project `tools/` without a
  global install, `PATH` mutation, or uninstall registration.
- The installer artifact probe has a read-only hard preflight before Run
  snapshots or installer execution. It refuses when the default program path,
  fixed-AppId or HistoryClipboard-named HKCU/HKLM uninstall entry, or current or
  common Start Menu group indicates an installed, partial, or damaged product.
  No bypass is supported because a same-AppId temporary uninstall can remove an
  existing installation's registration or shortcuts.
- When no installed-product evidence exists, pre-existing
  `%LOCALAPPDATA%\HistoryClipboard` user data is allowed and fingerprinted. The
  probe installs to a temporary `/DIR` without starting HistoryClipboard and
  verifies installed files/version/uninstaller, owned Run cleanup, exact Run
  restoration, unchanged user-data existence and content/metadata fingerprint,
  program-directory removal, and zero residual application processes. The
  stricter application-start round trip remains for a disposable profile with
  no pre-existing clipboard data.
- Produce SHA-256 checksums for release artifacts.
- Code signing is required before declaring v1.0 production-ready; unsigned preview releases must be labeled clearly.
