# Code audit — 6 November 2026

This pass combines solution-wide compilation and tests with targeted source review of menu styling,
Explorer exports, transfer copying and progress, sync filtering and scanning, IPC framing, secret
storage, SQLite access and backups, search, and desktop shutdown. It is not an exhaustive line-by-line
review of all 589 C# and AXAML source files, a penetration test, or live-provider acceptance testing.
Existing terminal changes in the working tree were preserved.

## Confirmed findings and fixes

| Priority | Finding | Resolution |
| --- | --- | --- |
| High | Explorer exports checked progress before failed/cancelled transfer state. A surviving checkpoint or live counter could hide failure indefinitely. | Check failure states first. Regression coverage exercises all five states previously masked by available progress. |
| Medium | Both Explorer folder export paths could loop forever when a provider returned empty pages with repeated continuation tokens. Entry limits did not help on empty pages. | Reject repeated tokens per directory and bound total pages per traversal using the existing export entry budget. Tests exercise immediate and queued export paths. |
| Medium | Recursive sync patterns required a slash even when the recursive directory prefix should match zero directories. `**/*.txt` missed root-level files; `**/cache/**` missed root-level cache contents. | Allow the recursive prefix to match zero directories. Tests cover root and nested includes/excludes and preserve single-star directory boundaries. |
| Low | Popup menus reused dialog field padding and did not consistently set typography. Pane actions appeared unnecessarily large. | Shared 12-DIP text, 8-by-3-DIP padding, and a 26-DIP minimum row height across dropdowns, context menus, flyouts, and submenus. Popup hosts explicitly use the UI font. Menu widths still accommodate their labels and shortcuts. Confirmation dialogs were unchanged. |
| Low | The browser filter used the obsolete Avalonia `Watermark` property. | Use `PlaceholderText`, removing the build warning. |
| Low | A packaging test assumed test binaries were inside the checkout and failed with external build artifacts. | Repository lookup falls back to the caller's source directory. Verified with artifacts outside the checkout. |

## Validation

The solution's 15 test suites were exercised, including headless desktop layout tests. Menu regression
coverage checks actual rows in light and dark appearances, including checked and disabled entries,
and compares flyout, context-menu, and menu-bar dropdown metrics.

Final solution result: **3,363 passed, 0 failed, 62 skipped**. The skipped tests require platforms or
environments unavailable in this run. `git diff --check` also passed.

Build artifacts were placed outside the checkout because the running desktop held its usual executable
open. The application was not stopped. The final verification command was:

```powershell
dotnet test StorageHub.slnx --artifacts-path "$env:TEMP/StorageHub-audit-build" --no-restore --verbosity quiet --logger trx --results-directory "$env:TEMP/StorageHub-audit-results"
```

## Coverage limits and follow-up

- Run the platform-dependent suites on Linux as well; this pass ran on Windows, and skipped tests
  are not evidence of passing behavior on another platform.
- Exercise real SFTP, FTP, cloud storage, Explorer drops, and interrupted transfers with the existing
  acceptance labs. Unit tests use controlled providers and do not validate every server's behavior.
- Review menus on a real display at the user's DPI and with long translated labels. Automated layout
  tests validate the shared metrics, but this pass did not visually inspect the running application's menus.
- Existing sync profiles using recursive globs now include root-level matches. Review the next sync
  preview against the intended include/exclude rules.
