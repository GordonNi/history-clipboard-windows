# Portable Preview Usage

## Release identity

- Version: `0.1.0-preview.2`
- Platform: Windows 10/11 x64
- Package type: self-contained portable ZIP
- Release status: preview and unsigned

Official prerelease assets are published at
`https://github.com/GordonNi/history-clipboard-windows/releases`.

Because this preview is unsigned, Windows may show a reputation or publisher
warning. Verify the SHA-256 file before running it. Do not treat this build as a
signed production release.

## Start

1. Extract the complete ZIP to a writable folder. Do not run the executable
   from inside the ZIP viewer.
2. Run `HistoryClipboard.App.exe`.
3. Use the tray icon or the default `Ctrl+Shift+V` shortcut to show the history
   window.
4. Exit from the tray menu before deleting or replacing the program folder.

The .NET runtime is included; installing a separate .NET runtime is not
required. Clipboard history and settings are intentionally not stored beside
the executable. They remain in `%LOCALAPPDATA%\HistoryClipboard`, so moving or
deleting the program folder does not silently move or erase user data.

## Verify SHA-256

From PowerShell in the folder containing the ZIP and `.sha256` file:

```powershell
$zip = "HistoryClipboard-0.1.0-preview.2-unsigned-win-x64-portable.zip"
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Get-Content -LiteralPath "$zip.sha256"
```

The two 64-character hash values must match exactly.

Do not disable Microsoft Defender SmartScreen or other Windows security
features to run this unsigned preview. If the checksum does not match, delete
the download and report the release problem.

## Update or remove

Exit the running tray process, extract a newer package into a new folder, and
start the new executable. To remove the portable application, exit it and delete
its extracted folder. To also remove local history and settings, delete
`%LOCALAPPDATA%\HistoryClipboard` after confirming no data needs to be kept.
