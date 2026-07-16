# Contributing to HistoryClipboard for Windows

[简体中文](CONTRIBUTING.zh-CN.md) | English

Thank you for helping improve HistoryClipboard. By participating, you agree to
follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before opening an issue

- Search existing issues first.
- Use the bug or feature form when possible.
- Security vulnerabilities must follow [SECURITY.md](SECURITY.md), not a public
  issue.
- Never post real clipboard content, a real `history.db`, `settings.json`,
  cached images, credentials, personal paths, or sensitive screenshots.

## Development setup

This repository targets Windows 10/11 x64, .NET 10, and WPF. Install the SDK
selected by `global.json`, then run:

```powershell
dotnet restore HistoryClipboard.slnx
dotnet format HistoryClipboard.slnx --verify-no-changes
dotnet build HistoryClipboard.slnx --configuration Release --no-restore
dotnet test HistoryClipboard.slnx --configuration Release --no-build
dotnet list HistoryClipboard.slnx package --vulnerable --include-transitive
```

Read the relevant files in `docs/` before changing storage, Windows
integration, packaging, privacy behavior, or the future sync design.

## Pull requests

- Keep each pull request focused and reviewable.
- Add or update tests for behavior changes.
- Preserve the local-only default. Do not add telemetry, content logging,
  advertising, or unencrypted clipboard transport.
- Keep Windows integration behind narrow seams so core behavior remains
  testable.
- Update public documentation for user-visible behavior.
- Run the commands above and report any manual checks performed.
- Do not include build output, release artifacts, local application data, or
  private tool binaries.

Maintainers may ask for changes before merging. A contribution is accepted
under the repository's [MIT License](LICENSE).
