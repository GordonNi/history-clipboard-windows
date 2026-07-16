# Security Policy

[简体中文](SECURITY.zh-CN.md) | English

## Supported versions

Security fixes are considered for the newest published preview only. Preview
builds are unsigned and are not a production-readiness claim.

| Version | Supported |
| --- | --- |
| `0.1.0-preview.2` | Yes |
| Older previews | No |

## Report a vulnerability privately

Use GitHub private vulnerability reporting:

<https://github.com/GordonNi/history-clipboard-windows/security/advisories/new>

Do not open a public issue for a suspected vulnerability.

Include the affected version, impact, minimal reproduction steps, and a
sanitized description of the expected and actual behavior. Do **not** attach or
paste:

- real clipboard text or images;
- a real `history.db`, `settings.json`, or `%LOCALAPPDATA%\HistoryClipboard`
  directory;
- screenshots containing passwords, tokens, personal messages, paths, or other
  sensitive information;
- credentials, private keys, or exploit data belonging to another person.

Use synthetic content and a disposable Windows profile whenever possible.
Reports will be acknowledged and triaged privately; response and disclosure
timing depends on severity and the ability to reproduce the issue.

## Security boundaries

Version `0.1` is local-only and has no synchronization transport. Future sync
work must use explicit pairing, authenticated end-to-end encryption, payload
limits, and replay/loop protection before it can ship.
