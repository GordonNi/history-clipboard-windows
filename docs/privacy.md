# Privacy Statement

HistoryClipboard `0.1.0-preview.2` is a local-only Windows preview.

- Clipboard history, settings, and cached PNG images stay under
  `%LOCALAPPDATA%\HistoryClipboard` for the current Windows user.
- This release contains no telemetry, analytics, advertising, account system,
  cloud synchronization, or network transport.
- It does not record the source application, window title, URL, keystrokes, or
  unrelated files.
- Clearing history removes database records and the PNG files managed by the
  application. Pinned records are also removed when the user confirms clear-all.
- Removing portable program files does not automatically remove local history
  or settings. To erase them, exit the application and delete
  `%LOCALAPPDATA%\HistoryClipboard`.
- Interactive installer removal asks whether to delete that local data and
  defaults to keeping it. Silent installer removal never prompts and always
  preserves it.
- Installer removal deletes the current-user launch-at-login value only when it
  exactly names the executable owned by that installation.

The planned local-network synchronization feature is not present in this
preview and remains disabled until a later explicitly paired, encrypted release.

Public source, support, and security policies are maintained at
`https://github.com/GordonNi/history-clipboard-windows`. Suspected
vulnerabilities must be reported privately through
`https://github.com/GordonNi/history-clipboard-windows/security/advisories/new`;
never publish real clipboard content, databases, or sensitive screenshots.
