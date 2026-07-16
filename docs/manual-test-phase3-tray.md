# Phase 3 Tray Lifecycle Manual Test

## Active monitor and mixed-DPI placement

1. Arrange two monitors so one is left of or above the primary monitor, producing negative desktop coordinates.
2. Test representative scaling pairs such as 100%/125%, 100%/150%, and 125%/200% without changing scaling while HistoryClipboard is running.
3. From an application on each monitor, show the hidden history window with the configured global hotkey. Confirm it opens on that application's monitor, stays fully inside the work area, and preserves a small bottom/right margin when space permits.
4. Hide the window, place the pointer over the other monitor's notification area, and open it from the tray. Confirm the popup selects the tray interaction monitor when the shell is the active source.
5. Hide the window and launch a secondary `HistoryClipboard.App.exe` from a terminal on each monitor. Confirm the existing window is shown on the terminal's monitor and no second tray process remains.
6. Repeat each show path at least three times. Confirm placement is stable, the 390 x 560 DIP visual size remains practical at each scale, and no edge crosses the taskbar or work-area boundary.
7. Move the taskbar to another edge and repeat the work-area containment checks. Do not move other application windows or change system display settings as part of automated verification.

Pure geometry and a controlled single-machine window probe are automated:

```powershell
& scripts\verify-window-placement.ps1
```

Expected output includes:

```text
DPI_AWARENESS=per-monitor
WINDOW_PLACEMENT=inside-selected-work-area
REPEATED_SHOW=stable
APP_PROCESSES_RUNNING=none
```

The probe does not reposition other windows or alter monitor settings. A real multi-monitor mixed-scaling pass is still required because a single-monitor machine cannot cover target-monitor selection across physical displays.

## Start the application

From PowerShell in the project directory:

```powershell
dotnet run `
  --project src\HistoryClipboard.App\HistoryClipboard.App.csproj `
  --configuration Release `
  --no-build
```

## Tray matrix

1. Confirm the blue overlapping-card icon appears in the Windows notification area and the application has no normal taskbar button.
2. Left-click the tray icon. The history window should toggle between visible and hidden.
3. When shown, the window should appear near the lower-right corner of the
   selected active monitor's work area and remain fully inside it.
4. Click another application. After a brief delay, the history window should hide while the tray process keeps running.
5. Show the window again and left-click the tray icon. It should hide rather than immediately reopening.
6. Click the window close button. The window should hide, while the tray icon and application process remain running.
7. While the window is hidden, copy new text or take a screenshot. Open the window from the tray; the new item should be present.
8. Right-click the tray icon and choose `显示历史记录`. The window should become visible and active.
9. Right-click the tray icon and choose `退出`. The window, tray icon, and application process should all close.

Focus-loss hiding and close-to-tray process behavior also have an automated probe:

```powershell
& scripts\verify-tray-lifecycle.ps1
```

## Search and count matrix

1. With an empty search, confirm every history card is visible and the footer totals match the stored text and image items.
2. Enter a lowercase fragment from a text item that contains uppercase letters. Matching text cards should remain in their existing order and the search badge should show the match count.
3. Add spaces before or after the query. Results should remain unchanged because surrounding whitespace is ignored.
4. While searching, image cards should not appear because images are not searchable in v0.1. The footer's total image count must remain unchanged.
5. Clear the search field. All text and image cards should return immediately.
6. Copy new text or an image. The footer totals should update; if a search is active, the visible result should still follow that query.

## Card action matrix

1. Confirm every card shows a correct `文字` or `图片` type capsule and a creation time in local `MM-dd HH:mm` format.
2. Confirm pinned cards additionally show `已固定`; unpinned cards must not show that capsule.
3. Click the main content area of a text or image card. It should copy exactly as before.
4. Click `固定` on an older card. The button should change to `取消固定`, the status should confirm the action, and the card should move ahead of unpinned cards.
5. Click `取消固定`. The card should return to normal newest-first ordering within the unpinned group.
6. Enter a search and pin a matching text card. The query should remain active and the visible matching cards should retain pinned-first ordering.
7. Click `删除` on a text card. Only that card should disappear and the complete-history text count should decrease by one.
8. Click `删除` on an image card. The card and persisted PNG should be removed and the complete-history image count should decrease by one, including after restart.
9. With the application closed, temporarily move one persisted PNG out of the `Images` folder, then restart. Its card should show `图片文件不可用` and offer deletion instead of displaying a blank region; restore or delete the test record afterward.
10. Confirm that clicking `固定`, `取消固定`, or `删除` does not trigger the card's copy action.

## Empty state and keyboard matrix

1. With no stored history, confirm the list shows `还没有历史记录` and explains that copied text or screenshots will appear there.
2. Add history, then search for a phrase that does not match. Confirm the state changes to `没有匹配结果` and suggests changing or clearing the query.
3. Clear the query. Stored cards should return and the empty state should disappear.
4. Press `Tab` repeatedly. Focus should land on meaningful controls rather than the list container, and each focused control should show a visible two-pixel blue ring.
5. Use `Enter` or `Space` to copy, pin/unpin, delete, pause/resume, expand settings, and change retention. Each action should match its mouse behavior.
6. Open `清空全部`. The owner window must remain visible behind the confirmation dialog; choosing `否` should return focus to it.

## Pause and clear matrix

1. Click `暂停捕获`. The button should change to `恢复捕获` and the status should explain that existing history remains usable.
2. While paused, copy new text and take a screenshot. Neither should be added, while search, copy-back, pin, and delete should continue working.
3. Click `恢复捕获`, then copy new content. The new update should be captured; content copied only during the paused interval should not be backfilled.
4. Click `清空全部`, then choose `否`. Every card, file, and count should remain unchanged.
5. Click `清空全部` again and confirm. Pinned and unpinned text/image cards should all disappear, result and inventory counts should become zero, and the current search text should remain in the search field.
6. Restart the application. Cleared database records and image files must not return.

## Retention settings matrix

1. On a first run without `%LOCALAPPDATA%\HistoryClipboard\settings.json`, expand `历史设置` and confirm the selector shows `3 天`.
2. Select `1 天`, `3 天`, and `5 天` in turn. The status should confirm the saved value; when nothing has expired it should say that no records were removed.
3. Prepare an unpinned record older than the newly selected limit, then shorten the limit. The record should disappear immediately and its stored PNG should also be removed when applicable.
4. Repeat with an old pinned record. It must remain visible because pinned records do not expire.
5. Exit from the tray and restart. The selected value should persist, and expired unpinned records should be cleaned before the window appears.
6. Optional recovery check: replace `settings.json` with malformed JSON while the application is closed. The next start should safely use `3 天` and keep running.

## Current validation boundary

- Active-monitor placement and PerMonitorV2 DPI handling are implemented; the
  real mixed-scaling, negative-coordinate monitor matrix remains a manual gate.
- Launch-at-login, the configurable global hotkey, single-instance activation,
  and taskbar-restart recovery are implemented and covered by the Phase 4 checklist.
- Tray, card, pause, clear, retention, and accessibility behavior still require
  a representative human desktop pass before production release.
