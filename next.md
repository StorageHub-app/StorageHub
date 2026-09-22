# StorageHub 2.0 — where it stands, and what is next

Written 2026-09-21, on branch `2.0` at `1b41b3d`. The port of the WinForms shell to Avalonia, cross-platform, with
one `Desktop.Core` doing the logic for both platforms. `docs/port-inventory.md` has the row-by-row status;
this is the short version and the order of the work left.

## Status

**The four blocks that decided the schedule are all in.**

| Block | State | Proven against |
|---|---|---|
| Sync: profile editor, schedule manager, run history and review | done | live agent, lab SFTP, lab S3 |
| Key store: import, rename, delete, picker in the Connection Manager | done | live agent, lab SFTP |
| Object inspector: versions, metadata, tags, Properties from the pane | done | live agent, lab S3 (MinIO) |
| SSH terminal: painter, scrollback, selection, copy, paste | done | live agent, lab sshd |
| Drag and drop: pane to pane, and files in from the desktop | done | unit tests only; a real drag cannot be driven headlessly |

The Connection Manager's secret fields are no longer plain text boxes: enrol a typed secret, enrol a file, delete,
and borrow a key from the key store.

**Numbers**

| | |
|---|---|
| Menu commands with a handler | 26 of the 37 that 1.x had live |
| WinForms screen lines with no counterpart | about 270 (was 14,200) |
| Test projects | 15, all green on Windows and Ubuntu |
| Live tests against the running agent and the Docker lab | 8, all green |

**Bugs found only by running the desktop against real servers, all fixed:**

- Unix socket transport threw `SocketException`, which nothing expected.
- Schedules outside UTC could not be saved: a non-zero offset was written into a `_utc` column.
- An SFTP sync ended `NeedsReconciliation` because the `ConditionalCreate` refusal came after approval;
  it now comes at preview, with a sentence naming the setting.
- Deleting a connection left its key bound, so the key could never be deleted.
- No pinned host could open a shell: the terminal compared a base64 fingerprint against a hex one.
- The `.deb` had never been run; six defects in `package-linux.sh` fixed.
- Choosing a settings category did nothing: the window wired the navigation list to the page from
  `DataContextChanged`, which runs before the visual tree exists. Every category showed Appearance.
- The toolbar editor offered commands the shell has not wired, which render as nothing on the bar.
- `tools.background-agent` was listed as available and had no handler: the menu entry did nothing.
- `help.check-for-updates` likewise.
- Three of the update window's headlines, its Close button and its failure message were English
  literals rather than strings, so that window was part-untranslated in Danish and German.
- The queue's column headers were English literals once it moved to Avalonia, and the activity log
  printed a run's dispatch state as its enum name.
- On the Logs tab the queue toolbar kept reporting the agent unavailable beside a log it had just read.
- A connection card's state and summary ("Not tested", "saved profile") were English literals in the
  panel, the Connection Manager and the pane, although translated strings for them already existed.
- 1.x's external editor wrote a remote file named "../../x" outside its session directory, shared one
  agent client between concurrent uploads, and dropped a save made during an upload.
- Tone colours were only styled on status lines, so "caption danger" and "heading warning" never showed
  their colour; the agent window's recovery heading and the Key Store import's refusal were affected.
- A terminal could not restart a stale agent: the lifecycle controller was built but never given to panes.
- Group icons were saved in the `FolderIcons` preference and never drawn.
- Help > About and Edit > Batch rename were listed as available and had no handler.
- The settings check's report was thrown away in all ten places that ran it, so a damaged settings file was
  set aside and its settings reset without a word.
- The sync location picker's status lines and its "selected" messages were English literals.

**Running things right now:** the dev agent on `%LOCALAPPDATA%\StorageHub.SyncLive` and the lab containers.
`docker compose --project-directory ./eng/testlab down --volumes` stops the lab.

## What needs to be done, in order

### 1. The remaining screens (about 3,400 lines)

Grouped by what they need, not by size. Each is a Core controller first, then a window, as the four blocks were.

1. ~~**Settings import and export**~~ — done at `3dcf2e3`. Both screens are a view model over the Core
   services, which were unchanged; `SettingsImportStep` moved into Core. 22 new tests, and both screens
   photographed in light and dark.
2. ~~**Toolbar settings**~~ — done at `05b7f1b`. Rules extracted to `ToolbarEditor` in Core; the page is the
   second settings page whose editor is a screen. 31 new tests, photographed in light and dark.
3. ~~**Agent control**~~ — done at `d990f21`. Two implementations, not one: `PackagedAgentLifecycleController`
   over the desktop-owned process on Windows, and `SystemdAgentLifecycleController` over `systemctl --user` on
   Linux. Both behind `IProcessRunner` so they test anywhere. 26 new tests, photographed in light and dark.
4. ~~**Update checker**~~ — done at `0220fd4`. One button for check, download and install, over the
   `DesktopUpdatePresentation` state machine that was already extracted. 15 new tests, photographed in light
   and dark.
5. ~~**Activity log**~~ — done at `ebaaec3`, in the queue's Logs tab. Rules in Core as `ActivityLog` and
   `ActivityLogReader`; rows reconciled by key. The queue is its own `TransferQueueView` now. 25 new tests,
   photographed in light and dark.
6. ~~**Connection picker**~~ — done at `896c64b`. The pane header's ComboBox is a button over a grouped,
   searchable picker; the group rule and keyboard behaviour moved into Core as `ConnectionPickerSession`.
   25 new tests, photographed in light and dark and in the shell.
7. ~~**External editing**~~ — done at `41be07d`. `ExternalEditController` in Core behind
   `IExternalEditPrompts` and `IEditorLauncher`; private sessions per platform (ACL on Windows, 0700 under
   XDG_RUNTIME_DIR on Linux); a Settings Editing page with the first path row. 36 new tests, including the
   real file watcher, photographed in light and dark.
8. ~~**Transfer progress column**~~ — done at `16a254a`. A bar behind the percentage, hidden rather than
   collapsed when the size is unknown so the column does not jump.
9. ~~**Small dialogs**~~ — done. About at `7b9f7c7`; batch rename at `33c7300`, with the rules in Core as
   `BatchRenamePlan`; the icon picker at `b743d53`, for a connection and for a group; the sync location
   picker at `68bcd58`, with the browsing in Core as `SyncLocationBrowser` and the editor's Browse buttons
   back. The splash was not ported: 2.0 opens without waiting on anything, so there is nothing for it to
   narrate. Its other job, reporting what the settings check found, is done by the shell at `711e7f0`.
   The `BootStage`/`BootStatus`/`DesktopBootException` types in Core are now unused.
10. **Host-key trust from the Connection Manager** — fetch from host, reject. The controller has it; the
    editor does not offer it.
11. **The directory tree beside the listing.**

Still missing from blocks that are otherwise done:

- Mouse reporting from the terminal to the remote program (needs the mouse encoder ported into Core).
- Dragging a remote file out to Explorer (needs the staging broker; `Desktop.Windows` is not in the tree).
- Dragging a pane header to dock.

### 2. Shell frame gaps (small, each removes something that lies)

- Status bar: selection count, rate and queue depth are frozen.
- Connection detail foot is a constant; rows do nothing on click.
- Overview is built once from `ShellStatusSnapshot.Initial`.
- Favourites and the per-row menu in the sidebar; `FavoriteConnectionMenu` is in Core.
- Address bar is not editable.
- A pane lists only the first page of a folder; there is no "load more". Batch rename checks new names
  against what is listed, so in a very large folder it can miss a collision the provider then refuses.

### 3. Packaging

- **Windows: WiX MSI.** No WiX in the repo yet. `MajorUpgrade` with `AllowSameVersionUpgrades`, fixed
  `UpgradeCode`, per-machine. Replace the Velopack pack block in `package-windows.ps1`; rewrite
  `test-windows-installer.ps1` for MSI, keeping its real checks.
- **Retire Velopack.** The `PackageReference` in `Desktop.Core.csproj` is dead; the autostart path becomes
  `Environment.ProcessPath`; Danish and German strings still name Velopack.
- **Linux `.deb`** is built and installs on Ubuntu 24.04; it still has to be verified on Debian.

### 4. CI, docs, release

- Add `build-test-linux` on `ubuntu-24.04`, then `package-linux` and `attest-linux`. `package-linux.sh` is never
  invoked by CI today.
- `docs/releasing.md` describes Velopack and is wrong in almost every paragraph; `docs/architecture.md` has
  nine WinForms references; `README.md` one.
- Bump `VersionPrefix` 1.4.6 → 2.0.0, write the CHANGELOG entry calling out the data-root move.
- Regenerate the 37 `packages.lock.json` files in one commit when Velopack goes and WiX arrives.
- Push `2.0`, `1.x` and `winforms-reference` to the remote; only a `main` push triggers release jobs.

### 5. Quality gates before release

- **Sign the update feed.** A SHA-256 over HTTPS to one host is the only integrity control, and the payload is
  handed to `msiexec` or `pkexec` elevated. Detached signature over the manifest, verified with an embedded key.
- **Accessibility.** A headless test asserting every interactive element has an automation name.
- **Persisted widths** were saved in device pixels; in Avalonia the same number is DIPs. Bump the config schema.
- **Clipboard** is async and window-scoped; file lists are `text/uri-list`; X11 data vanishes on exit.
- **Bind the listing instead of copying it**; needs `IndexedView` to implement `IList`.

## How to verify, every time

- `dotnet test StorageHub.slnx` on Windows. Stop the dev agent first, or the Agent.Host build cannot copy its
  output.
- The same on Ubuntu through WSL: `~/StorageHub` pulls from the Windows tree as origin.
- `eng/run-dev-agent.ps1 -DataRoot "$env:LOCALAPPDATA\StorageHub.SyncLive"`, dot-source
  `eng/testlab/.fixtures/env.ps1`, set `STORAGEHUB_LIVE_AGENT=1`, and run the `Live*` tests. Give the agent a
  few seconds after a restart before running them.
- Photograph every new screen with `STORAGEHUB_SHOT_DIR` and look at it. Nine of the layout defects so far were
  caught that way and none by an assertion.
- `systemctl --user` writes refusals to standard error and nothing to standard output, and exits 5 for
  an unknown unit — checked against systemd in WSL, and what `ProcessRunner` relies on to carry
  "Unit storagehub-agent.service not found." through to the screen instead of a generic message.
- The Avalonia XAML compiler writes `Avalonia error AVLN2000`, not `: error`; a grep for the latter reports a
  clean build that fails at runtime.
- `SqliteConnectionProfileRepositoryTests.Update_uses_optimistic_concurrency_and_returns_the_new_version`
  fails intermittently on Ubuntu in a full-solution run and passes in isolation. Seen twice on
  2026-09-22, with three isolated runs and a later full run passing in between, so it is contention
  under parallel load rather than a break. It has never failed on Windows. Worth pinning down
  before release.
