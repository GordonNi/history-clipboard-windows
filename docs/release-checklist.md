# Local Release Checklist

Current candidate: `0.1.0-preview.2+unsigned` for `win-x64`. This candidate
includes the guarded primary-startup failure path and is now installed in the
current user's default per-user directory after a controlled preview.1-to-preview.2
in-place upgrade. Clean-machine upgrade coverage remains a separate manual gate.

## Identity and scope

- [x] Version and filenames contain `preview` and `unsigned`.
- [x] Runtime identifier is `win-x64` and publish mode is self-contained.
- [x] Installer and portable filenames contain `preview` and `unsigned` and make
      no signature claim.
- [x] English and Simplified Chinese README, contribution, security, support,
      Code of Conduct, issue/PR templates, and prerelease notes are present.
- [x] Public repository links target
      `GordonNi/history-clipboard-windows`; the separate macOS edition remains
      linked as `GordonNi/history-clipboard`.
- [x] CI excludes GUI lifecycle, launch-at-login, registry mutation, and strict
      installer round-trip probes. Release automation uses a clean runner for
      only the headless portable and file-only installer artifact checks.

## Automated gates

- [x] `dotnet format HistoryClipboard.slnx --verify-no-changes` passes.
- [x] Release build and all tests pass with zero warnings and errors.
- [x] Direct and transitive NuGet vulnerability audit reports no vulnerable packages.
- [x] Startup-failure classification and cleanup-order tests pass, including
      nested unsupported-schema priority, safe notices, cleanup/presentation
      failure tolerance, and a nonzero shutdown code.
- [x] Unsupported-schema integration coverage confirms the database file is
      byte-for-byte unchanged after startup rejection.
- [x] All applicable Windows lifecycle probes pass without leaving a process.
- [x] `scripts/verify-window-placement.ps1` confirms per-monitor awareness,
      work-area containment, stable repeated show, and no residual process.
- [x] `scripts/build-portable-release.ps1` succeeds from a clean artifacts directory.
- [x] `scripts/verify-portable-package.ps1` validates structure, checksum,
      self-contained runtime files, startup, cleanup, and no residual process.
- [x] `scripts/verify-installer-definition.ps1` validates lowest-privilege scope,
      x64 inputs, exclusions, Run ownership, and silent data preservation.
- [x] `scripts/bootstrap-inno-setup.ps1` verifies the pinned official hash,
      `.issig` descriptor, Authenticode publisher, private portable extraction,
      unchanged PATH, and unchanged uninstall registration.
- [x] With `ISCC.exe`, `scripts/build-installer-release.ps1` produces the setup
      executable and SHA-256 file.
- [ ] In a configuration with user data allowed but no installed product,
      `scripts/verify-installer-artifact.ps1` passes a file-only temp install and
      uninstall without starting the app or changing existing user data. The
      final setup from this refresh still requires this no-installed-product
      verification; it was deliberately not run against the current installation.
- [x] In a profile with an installed or partial HistoryClipboard product, the
      artifact probe refuses before Run snapshots or installer invocation and
      leaves program files, uninstall registration, shortcuts, Run, PATH, and
      user data unchanged.
- [ ] In a disposable profile, `scripts/verify-installer-roundtrip.ps1` passes
      install, startup, silent uninstall, restoration, and cleanup assertions.

## Package inspection

- [x] ZIP contains the executable, application DLL, dependency/runtime manifests,
      self-contained runtime files, portable instructions, privacy statement,
      and this checklist.
- [x] ZIP contains no PDB, database, settings, image-history directory, logs,
      test output, build-intermediate directory, or temporary file.
- [x] Published ZIP SHA-256 matches its `.sha256` file.

## Manual release gates still required

- [ ] Phase 2 clipboard matrix passes on representative Windows applications.
- [ ] Phase 3 tray UI matrix passes on a targetable desktop.
- [ ] Active-monitor placement passes on real mixed-DPI multi-monitor layouts,
      including a monitor with negative desktop coordinates.
- [ ] Phase 4 sign-in, hotkey conflict, and Explorer restart matrix passes in a
      disposable Windows session.
- [ ] Portable package starts on clean Windows 10 x64 and Windows 11 x64 machines.
- [ ] Per-user installer install, upgrade, and uninstall pass on clean Windows 10
      x64 and Windows 11 x64 without elevation.
- [ ] Interactive uninstall defaults to keeping data, deletes only after Yes,
      and preserves unrelated launch-at-login values.
- [x] Preview.1-to-preview.2 update and exact user-data retention are confirmed
      in the current user profile before the upgraded application is restarted.
      Clean Windows 10/11 upgrade coverage remains open above.
- [ ] In a disposable profile, unsupported-schema and inaccessible-storage
      startup failures show only the documented safe Chinese message/code,
      leave data unchanged, release the instance lock, and exit nonzero.
- [ ] Code signing is completed before any production-ready claim.

This checklist is release infrastructure, not evidence that the unchecked manual
or production gates have passed.
