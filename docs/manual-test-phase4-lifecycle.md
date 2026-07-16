# Phase 4 Windows Lifecycle Manual Test

## Single-instance activation

1. Start the Release application and confirm one tray icon and one `HistoryClipboard.App` process exist.
2. Hide the history window by clicking elsewhere or closing the window without exiting the tray process.
3. Launch the same executable again. The hidden window should become visible and active.
4. Confirm the second launch exits quickly: Task Manager should still show exactly one `HistoryClipboard.App` process and the notification area should still show one tray icon.
5. Repeat the second launch while the window is already visible. The existing window should remain the only application window and come to the foreground.
6. Exit through the tray menu, then launch again. A new primary process should start normally, proving the previous instance released its kernel handles.

The real two-process behavior has an automated probe:

```powershell
& scripts\verify-single-instance.ps1
```

Expected output includes:

```text
SECONDARY_LAUNCH=exited
PRIMARY_ACTIVATION=window-visible
APP_PROCESS_COUNT=1
APP_PROCESSES_RUNNING=none
```

## Global hotkey

1. Start with no `settings.json` hotkey value and confirm `Ctrl+Shift+V` shows or hides the history window.
2. In `历史设置`, change the shortcut to `Ctrl+Alt+V`, then `Ctrl+Shift+H`; confirm only the selected shortcut toggles the window.
3. Exit and restart. Confirm the selected shortcut persists and still toggles the existing window.
4. Select `禁用`, then confirm the former shortcut no longer toggles the window while tray show/hide remains available.
5. Re-enable a shortcut. If another application already owns it, confirm the status rail reports the conflict and HistoryClipboard continues running in the tray.

The registration, activation, and non-fatal conflict paths have an automated probe:

```powershell
& scripts\verify-global-hotkey.ps1
```

Expected output includes:

```text
GLOBAL_HOTKEY=registered
HOTKEY_ACTIVATION=window-visible
HOTKEY_CONFLICT=process-still-running
APP_PROCESSES_RUNNING=none
```

## Launch at login

1. Start the Release application and expand `历史设置`.
2. Confirm `登录后自动启动` is keyboard reachable and exposes a meaningful accessible name.
3. Enable it, then inspect `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The `HistoryClipboard` value should be the quoted absolute path of the running executable.
4. Exit and start the application again. Confirm the checkbox remains enabled because the registry entry still matches the current executable.
5. Disable it and confirm only the `HistoryClipboard` Run value is removed.
6. Deny or otherwise make the Run key unavailable in a controlled test account. Confirm the status rail clearly reports the read/write failure and the tray application remains usable.
7. Sign out and back in only after the registry checks pass. Confirm HistoryClipboard starts for the current user without elevation and still enforces one running instance.

The real registry and UI Automation behavior has a reversible probe:

```powershell
& scripts\verify-launch-at-login.ps1
```

The probe refuses to run while a user-owned application instance is open. It snapshots the original Run value, including its registry type, exercises enable and disable through the accessible settings checkbox, and restores the original snapshot in `finally` before comparing it exactly.

Expected output includes:

```text
LAUNCH_AT_LOGIN=enabled
ACCESSIBLE_CONTROL=available
LAUNCH_AT_LOGIN=disabled
ORIGINAL_RUN_VALUE=restored
APP_PROCESSES_RUNNING=none
```

## Explorer/taskbar restart recovery

1. Start the Release application and confirm its tray icon is available.
2. In a disposable Windows desktop session, restart Windows Explorer from Task Manager. Do not perform this manual step while file operations or shell-dependent work are active.
3. Confirm the HistoryClipboard tray icon returns without relaunching the application and still opens or hides the existing history window.
4. Confirm Task Manager still shows exactly one `HistoryClipboard.App` process and clipboard capture remains active.
5. Exit through the tray menu. Restart Explorer again and confirm HistoryClipboard does not recreate a tray icon after its process has exited.

The message handling and repeated-recovery path have a controlled probe that does not restart or terminate `explorer.exe`:

```powershell
& scripts\verify-taskbar-recovery.ps1
```

The probe refuses to run while a user-owned application instance is open. It starts its own Release process, sends the registered `TaskbarCreated` message twice directly to the application window, verifies synchronous republishing completes without terminating the process, and cleans up only the process it started. The automated controller tests separately prove that disposal removes the hook and blocks a queued recovery callback.

Expected output includes:

```text
TASKBAR_CREATED=message-delivered
TRAY_RECOVERY=republish-completed
RECOVERY_IDEMPOTENCE=process-still-running
APP_PROCESSES_RUNNING=none
```

## Current limits

- The controlled probe validates the real registered-message path without disrupting Explorer; a human Explorer-restart pass is still required before declaring the Phase 4 gate fully accepted.
- The automated probe must start with no existing `HistoryClipboard.App` process; it refuses to terminate a user-owned running instance.
