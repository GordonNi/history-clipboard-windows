# Project-private tools

Binary tools are not committed. `inno-setup.lock.json` pins the audited official
Inno Setup release, download location, size, SHA-256, signature descriptor, and
publisher identity used by the release scripts.

Bootstrap the compiler without a global install or `PATH` change:

```powershell
& scripts\bootstrap-inno-setup.ps1
```

The compiler is extracted through the official Inno Setup `/PORTABLE=1` mode to
`tools/inno-setup/<version>-<architecture>/`. Downloads stay under
`tools/.cache/`; both binary directories are ignored by Git.

The bootstrap verifies the pinned installer hash, the official `.issig`
descriptor hash and contents, and the valid Authenticode publisher before it
executes the portable extraction. It also confirms that the current process,
user, and machine `PATH` values and Inno Setup uninstall-key inventory did not
change.
