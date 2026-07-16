# Per-user Installer Preview

## Release identity

- Version: `0.1.0-preview.2`
- Platform: Windows 10/11 x64
- Package type: non-administrator per-user Inno Setup installer
- Default directory: `%LOCALAPPDATA%\Programs\HistoryClipboard`
- Release status: preview and unsigned

Official prerelease assets are published at
`https://github.com/GordonNi/history-clipboard-windows/releases`.

The compiler is version-locked and bootstrapped only into this project's ignored
`tools/` directory. It is not globally installed and no `PATH` value is changed.
See `docs/inno-setup-toolchain.md` for the audited source and hashes. Bootstrap
and build with an explicit compiler path:

```powershell
& scripts\bootstrap-inno-setup.ps1
& scripts\verify-installer-definition.ps1 `
    -IsccPath (Resolve-Path tools\inno-setup\7.0.2-x64\ISCC.exe)
& scripts\build-installer-release.ps1 `
    -IsccPath (Resolve-Path tools\inno-setup\7.0.2-x64\ISCC.exe)
```

The build reuses the self-contained `win-x64` portable publish, rejects debug
and user data, and writes the unsigned preview setup executable plus SHA-256
under `artifacts/`.

## Install and remove

The installer runs without administrator elevation and creates current-user
Start menu entries for the application and uninstaller. It refuses to update or
remove the application while its single-instance mutex is present; exit the tray
application first.

Interactive uninstall asks whether to also remove
`%LOCALAPPDATA%\HistoryClipboard`; the default is **No**. `/SILENT` and
`/VERYSILENT` uninstall never show this prompt and always preserve local data.
The current-user launch-at-login Run value is removed only when it exactly names
the executable owned by that installation.

## Verification boundary

The artifact probe is safe only in a configuration with no installed
HistoryClipboard product. Before taking a Run snapshot or performing any
temporary install, it refuses when it finds the default program executable or
directory, the fixed-AppId or HistoryClipboard-named uninstall registration in
HKCU/HKLM, or a current-user/common Start Menu HistoryClipboard group. This
includes partial or damaged installations; there is no bypass parameter.

An existing `%LOCALAPPDATA%\HistoryClipboard` data directory is allowed. When
the installed-product preflight passes, the probe does not start
HistoryClipboard or create/delete that data directory. It fingerprints any
existing data before and after a temporary file-only install/uninstall,
restores the Run value exactly, and verifies zero residual application
processes:

```powershell
& scripts\verify-installer-artifact.ps1
```

Run the stricter application-start round trip only in a disposable user profile
with no existing `%LOCALAPPDATA%\HistoryClipboard`:

```powershell
& scripts\verify-installer-roundtrip.ps1
```

The strict probe uses a temporary `/DIR`, starts the installed application, silently
uninstalls, verifies program removal, owned Run cleanup, silent data
preservation, exact Run restoration, temporary-data cleanup, and zero remaining
processes. It refuses to run against real clipboard history.

Clean Windows 10/11 install, upgrade, interactive uninstall, and publisher
warning checks remain manual gates. This preview is not signed or production-ready.
Do not disable Windows security features to bypass an unknown-publisher or
SmartScreen warning.
