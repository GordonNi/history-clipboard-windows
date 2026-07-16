# Phase 2 Text and Image Capture Manual Test

## Start the application

From PowerShell in the project directory:

```powershell
dotnet run `
  --project src\HistoryClipboard.App\HistoryClipboard.App.csproj `
  --configuration Release `
  --no-build
```

The tray application should start and the history window should report clipboard capture status.

## Capture matrix

1. Copy a unique line from Notepad. It should appear at the top of the list and the status should report a newly captured text item.
2. Copy the same line again. The list count should not change and the status should report that the consecutive duplicate was ignored.
3. Copy different Unicode text containing Chinese and emoji. The exact text should appear at the top.
4. Copy an image from a browser or Paint, or take a screenshot with `Win+Shift+S`. A thumbnail should appear at the top and the status should report a newly captured image.
5. If an application exposes both supported Unicode text and a bitmap in the same clipboard update, the text representation should be captured first.
6. Close and restart the application. Previously captured text and image items should reload from SQLite and the image files.
7. Click a text history card, then paste into Notepad. The exact text should paste, the status should report a successful copy, and the history count should not increase.
8. Click an image history card, then paste into Paint. The image should paste, the status should report a successful copy, and the history count should not increase.
9. After either copy-back check, copy new content in another application. The new external clipboard update must still be captured normally.

## Current validation boundary

- Clipboard behavior is implemented; this representative real-application matrix
  remains a manual release gate.
- Tray lifecycle and the configurable global hotkey are implemented and have
  their own Phase 3 and Phase 4 checklists.
- Local data is stored under `%LOCALAPPDATA%\HistoryClipboard\` and no network requests are made.
