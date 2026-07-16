# Project-private Inno Setup toolchain

## Locked official release

- Product: Inno Setup 7 x64
- Version: `7.0.2`
- Release date: 2026-07-13
- Official release:
  `https://github.com/jrsoftware/issrc/releases/tag/is-7_0_2`
- Installer:
  `https://github.com/jrsoftware/issrc/releases/download/is-7_0_2/innosetup-7.0.2-x64.exe`
- Installer size: `17,020,192` bytes
- Installer SHA-256:
  `5ad54ca3def786f8f4212552e54cc6d8d61329e2d24a1cfee0571d42c2684ff1`
- Official `.issig` descriptor SHA-256:
  `b85f4a9c527ee573d308840e859ff3ca99c8a750acb259d51f111301c7ef71bd`
- Required Authenticode publisher: `Pyrsys B.V.`

The machine-readable source of truth is `tools/inno-setup.lock.json`.

JRSoftware's download page identifies 7.0.2 x64 as an official signed release:
`https://jrsoftware.org/isdl.php`. Its verification guide documents the
Authenticode publisher and explains that the `.issig` file carries the file hash:
`https://jrsoftware.org/isdl-verify.php`.

## Private installation boundary

JRSoftware documents that its own installer accepts `/PORTABLE=1`, including
side-by-side current-user use without an uninstaller or Add/Remove Programs
entry: `https://jrsoftware.org/ishelp/topic_technotes.htm`.

`scripts/bootstrap-inno-setup.ps1` uses that mode and an explicit `/DIR` beneath
`tools/inno-setup/7.0.2-x64`. It verifies the pinned download and descriptor
hashes, descriptor file name/size/hash, valid Authenticode publisher, extracted
compiler product identity/signature, unchanged process/user/machine PATH values,
and unchanged Inno Setup uninstall-key inventory.

Binary downloads and extracted tools are ignored by Git. The lock file,
bootstrap script, and this audit record remain tracked.

## Build and safe verification

```powershell
& scripts\bootstrap-inno-setup.ps1
$iscc = Resolve-Path tools\inno-setup\7.0.2-x64\ISCC.exe
& scripts\build-installer-release.ps1 -IsccPath $iscc
& scripts\verify-installer-artifact.ps1
```

The artifact probe never starts HistoryClipboard. Its hard preflight runs
before Run-value snapshots or any installer invocation and refuses if the
default program path, fixed-AppId or HistoryClipboard-named HKCU/HKLM uninstall
entry, or current/common Start Menu group indicates an installed, partial, or
damaged product. It has no bypass and therefore must run in a configuration
without an installed product, normally a disposable profile.

Pre-existing `%LOCALAPPDATA%\HistoryClipboard` user data alone is allowed and
does not trigger the guard. After the preflight passes, the probe installs to a temp `/DIR`,
checks program files, version, and uninstaller, snapshots/restores the current
user Run value, silently uninstalls, verifies the installer-owned directory was
removed, and compares the existing user-data existence plus per-file hash and
metadata fingerprint before/after.

Do not substitute this for clean Windows 10/11, upgrade, interactive uninstall,
publisher-warning, signing, or disposable-profile application-start gates.
