# HistoryClipboard Windows Requirements

## Product goal

Build a lightweight Windows 10/11 clipboard-history tray application that matches the published macOS HistoryClipboard behavior and later supports secure Mac-to-Windows clipboard synchronization.

## Version 0.1: Windows local history

### Required behavior

- Run as a single-instance tray application without a normal taskbar window.
- Record newly copied Unicode text and bitmap images.
- Prefer text when one clipboard update exposes both supported text and image formats, matching the macOS app.
- Show records with pinned items first and newest items first within each group.
- Copy a selected record back to the clipboard without creating a duplicate history event.
- Search text records case-insensitively; image records are not searchable in v0.1.
- Pin, unpin, and delete individual text or image records.
- Clear all history only after confirmation, including stored image files.
- Retain unpinned records for 1, 3, or 5 days; pinned records do not expire.
- Pause and resume recording.
- Optionally launch when the current user signs in.
- Toggle the history window from the tray and from a configurable global hotkey.
- Store all history locally and make no network requests in v0.1.
- If startup cannot safely open local history, stop with a stable error code and
  an actionable Chinese message that does not expose exception text, paths,
  clipboard content, or a stack trace.
- Treat a newer or otherwise unsupported database schema as read-only evidence:
  do not migrate, rebuild, delete, overwrite, or otherwise repair it automatically.

### Default decisions

- Default retention: 3 days.
- Default hotkey: `Ctrl+Shift+V`; it can be changed or disabled.
- Supported architecture for the first release: Windows x64.
- Distribution: self-contained portable ZIP and non-administrator per-user installer.

## Version 0.2: Mac and Windows clipboard sync

### Required behavior

- Pair a Mac and Windows device through an explicit one-time code or QR flow.
- Synchronize newly copied text over the same trusted local network.
- Add encrypted image synchronization only after text synchronization is stable.
- Write received content to the destination clipboard so the user can paste normally.
- Support both Mac-to-Windows and Windows-to-Mac directions.
- Never inject an automatic paste keystroke into the active application.
- Prevent sync loops through a unique event ID, source-device ID, and applied-event cache.
- Allow users to pause sync, limit content types, remove a paired device, and inspect connection state.
- Use authenticated end-to-end encryption; unencrypted clipboard payloads must never cross the network.
- Keep sync disabled until the user explicitly pairs and enables it.
- Enforce payload limits, initially 1 MiB for text and 10 MiB for images.

### Out of scope for v0.2

- Internet/cloud relay, user accounts, automatic paste injection, complete history database replication, files, HTML/RTF, and mobile clients.

## Privacy requirements

- No telemetry, analytics, advertising, or content logging.
- Do not record source application, window title, URL, or keyboard activity.
- Secrets used for device pairing must be stored with platform-provided secure storage.
- Clearing history must delete database records and associated image files.
- Uninstall must offer removal of local history, settings, and pairing material.

## Acceptance summary

- Local clipboard updates appear reliably without idle polling.
- The application remains usable when the hotkey is unavailable or the clipboard is temporarily locked.
- Idle CPU usage is negligible and image-heavy history remains responsive.
- Every phase passes automated tests and its documented manual acceptance checklist.
- A failed primary-instance startup releases partially created window, tray,
  taskbar-recovery, and single-instance resources before exiting nonzero; a
  secondary-instance activation remains a normal zero-error exit without a dialog.
