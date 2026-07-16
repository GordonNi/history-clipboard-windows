# Design Specification

## Product character

The Windows application should feel like the same product as the macOS edition: compact, calm, translucent light blue, privacy-first, and immediately understandable. It should still follow Windows input and window-management conventions.

## Tray window

- Nominal size: 390 × 560 device-independent pixels.
- Open above the taskbar notification area on the active monitor and remain inside its work area.
- Choose the active target before showing or activating the popup: foreground-window monitor first, then cursor, nearest application monitor, and primary-monitor fallbacks.
- Keep a 16-DIP bottom/right margin when space permits. Convert the nominal DIP size and margin with the selected monitor's effective DPI, and clamp oversized or edge-case layouts to the physical work area.
- Hide when focus is lost; tray click or the global hotkey toggles visibility.
- Do not show a normal taskbar button.
- Windows 11 may use supported Fluent/translucent effects; Windows 10 uses an opaque light-blue fallback with equivalent contrast.

## Layout

1. Header: title and settings entry.
2. Search field.
3. Virtualized history list.
4. Footer: text/image counts, clear and exit actions, retention selector, pause toggle, and launch-at-login toggle.

Search feedback uses a compact result badge at the end of the search rail. Footer text/image counts always describe the complete stored history rather than the filtered subset, so applying a query does not make inventory totals appear to change.

The current retention control lives in a compact collapsed `历史设置` expander so the 390 × 560 tray surface stays focused on history. Its 1/3/5-day selector uses the existing mist-blue border and explicitly states that pinned records do not expire.

The same expander contains a global-hotkey selector with three supported shortcuts plus a disabled state, and an accessible launch-at-login checkbox scoped to the current Windows user. A hotkey registration conflict or registry access failure is reported in the status rail without closing the application or blocking tray access. When the launch setting cannot be read safely, its checkbox is disabled rather than presenting an unknown registry state as an editable choice.

## History cards

- Identify text/image type, creation time, and pinned state.
- Clicking the main card copies the item and briefly shows a copied confirmation.
- Pin and delete remain separate buttons and must not trigger copy.
- Text shows up to four lines in the collapsed card.
- Images use bounded thumbnails; missing image files show a recoverable placeholder.

Each card begins with a quiet metadata stamp: a `文字`/`图片` capsule, an optional blue `已固定` capsule, and the locally converted `MM-dd HH:mm` creation time. When the active query has no cards, the list surface becomes a directed empty state: `Ctrl + C` guidance for a genuinely empty history, or search-recovery guidance when a query has no matches.

## Accessibility

- All actions are keyboard reachable and expose accessible names.
- Focus state must be visible.
- Text and icons must meet practical contrast requirements in both translucent and fallback modes.
- Motion is brief and nonessential; respect reduced-motion settings where available.

Interactive controls use one consistent two-pixel blue keyboard focus ring. The history list container itself is skipped in the tab order so keyboard navigation lands on meaningful copy, pin, delete, pause, clear, settings, and retention controls.

## Sync UI for v0.2

- Sync lives in settings, not in the main history list.
- Show explicit states: disabled, pairing, connected, reconnecting, and error.
- Show paired device name and last successful connection, never clipboard content in diagnostic text.
- Provide separate text/image toggles and a prominent pause/remove-device action.
