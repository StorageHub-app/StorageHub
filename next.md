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
| Menu commands with a handler | 25 of the 37 that 1.x had live |
| WinForms screen lines with no counterpart | about 1,900 (was 14,200) |
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
4. **Update checker** — `UpdateCheckerForm` (233). `DesktopUpdatePresentation` is the extracted state machine
   and the shell references none of it.
5. **Activity log** — `ActivityLogControl` (329), beside the queue. Extract the queue as its own view when this
   lands; it is drawn inline in `MainWindow.axaml` today.
6. **Connection picker** — `ConnectionPicker` (487): grouped, filter-as-you-type, in the pane header.
7. **External editing** — `ExternalEditorController` (483): mostly portable once its prompt is behind an
   interface.
8. **Transfer progress column** — a bar behind the text (154).
9. **Small dialogs** — About, icon picker, splash, sync location picker, batch rename, unsafe-edit warning.
10. **Host-key trust from the Connection Manager** — fetch from host, reject. The controller has it; the
    editor does not offer it.
11. **The directory tree beside the listing.**

Still missing from blocks that are otherwise done:

- Mouse reporting from the terminal to the remote program (needs the mouse encoder ported into Core).
- Dragging a remote file out to Explorer (needs the staging broker; `Desktop.Windows` is not in the tree).
- Dragging a pane header to dock.

### 2. Shell frame gaps (small, each removes something that lies)

- Status bar: selection count, rate and queue depth are frozen.
- Connections search box does nothing; `ConnectionPickerFilter` is in Core and unused.
- Connection detail foot is a constant; rows do nothing on click.
- Overview is built once from `ShellStatusSnapshot.Initial`.
- Favourites and the per-row menu in the sidebar; `FavoriteConnectionMenu` is in Core.
- Address bar is not editable.

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
  fails on Ubuntu in a full-solution run and passes in isolation. Seen twice on 2026-09-22, and
  three isolated runs passed in between, so it is contention under parallel load rather than a
  break. It has never failed on Windows. Worth pinning down before release.
