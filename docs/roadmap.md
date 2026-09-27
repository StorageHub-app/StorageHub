# StorageHub 2.0 roadmap

What is left before 2.0 ships, in order. Each numbered item is one commit (or a few), ticked off when it
lands. The Avalonia port's screens 1–9 are done; this picks up from there.

**Ground rules**

- CodeLogic.Storage is frozen at 4.8.95. It moves bytes; StorageHub keeps its own transfer queue, sync
  engine and SQLite state. A gap in the library is worked around in `src/StorageHub.Storage.CodeLogic`,
  not fixed upstream.
- `eng/cl-storage-acceptance` is run whenever the library version changes.
- Every commit: `dotnet test StorageHub.slnx` on Windows with the lab up (`eng/testlab`), then the Linux
  run in WSL. Changed windows are photographed (`STORAGEHUB_SHOT_DIR`).

## 0. CodeLogic.Storage 4.8.95

- [x] Test lab on a pullable MinIO image (`pgsty/minio`, pinned), packages at CodeLogic.Storage 4.8.95
      and CodeLogic 4.8.20, lock files regenerated, tests and acceptance checks green.

## P. Behaviour parity with 1.4 (first)

A code-against-code sweep on 2026-09-26 compared what 1.4 does in five areas with what 2.0 does. It
found much more than looks: whole startup steps that never run, commands that are drawn but not wired,
and settings that are saved but never read. Section L is how 2.0 looks; this is whether it works as 1.4
did, so it comes first. Each item names the 1.4 source to port from, under
`C:\Projects\StorageHubOld\src\StorageHub.Desktop.WinForms`.

Already done in this pass: the Welcome page is live and its buttons work (e174058); the app opens on
Welcome and Sync tasks with no workspace, and opening a connection with none open makes one (e174058);
panes in a workspace added later load their connections (e174058); This PC's drive list refuses New,
Rename and Delete, which in 2.0 would have deleted a volume's contents permanently (31e8cc9).

### P.1 Startup, shutdown and install (blocks shipping)

P.1.1, P.1.2, P.1.4 and P.1.6 landed together: `Services/DesktopBoot.cs` runs 1.x's sequence behind
`SplashWindow`, `Framework/DesktopFrameworkHost.cs` is ported to Desktop.Core, and
`DesktopAgentStartup` ensures the agent on both platforms (the packaged lifecycle on Windows, the
user's systemd unit on Linux; a build from source uses an agent started by hand). The Check
installation button waits for P.5.6. The language is loaded and applied, but there is still no
setting to choose it (P.5.1).

- [x] P.1.1 Splash while starting, with 1.4's stages (preparing data, framework, environment, settings,
      language, starting the agent, opening), "this can take a few seconds the first time", and a
      failure screen with Retry, Quit, Check installation and Copy details. `StorageHubSplashForm.cs`,
      `DesktopBootContext.cs`; `BootStatus`/`BootStage` are already in Desktop.Core and unused.
- [x] P.1.2 Start the agent at launch and wait for it (`EnsureAgentAsync`, 8 s start / 12 s ready), with
      `DesktopStartupPreflight.DescribeFailure` on the splash. Nothing starts it today: without the
      service the app sits at "Agent: not connected".
- [x] P.1.3 Restart the agent when it drops, and reload the panes, Welcome, Sync tasks and the queue when
      it comes back (`DesktopAgentAvailability.Changed`). `MainForm.cs:3404-3437, 3520-3551`.
- [x] P.1.4 Load the framework and the language: `EnsureCreated`, `EnsureSupportedCultures`,
      `SeedShippedTranslations`, `Ui.UseFramework`. 2.0 is English whatever is configured.
      `Framework/DesktopFrameworkHost.cs`.
- [ ] P.1.5 Installers: a plain WiX MSI on Windows and the .deb on Linux, nothing else (decided
      2026-09-26). Velopack goes: drop `vpk` from `eng/package-windows.ps1` (no Setup.exe, no portable
      ZIP), and the hooks in `DesktopPackageLifecycleHooks` become MSI custom actions -- autostart on
      install and upgrade, stop the agent before upgrade and uninstall, remove the Run entry and the
      drop broker's COM registration on uninstall. `DesktopUpdater` is Velopack-based and needs a
      replacement: find the newer release, download its MSI, verify it, and run it. Update
      `docs/releasing.md` and `eng/test-windows-installer.ps1` to match.
- [x] P.1.6 `--agent-only` (the sign-in autostart) ensures the agent and exits without a window, and
      honours `STORAGEHUB_DISABLE_AUTOSTART`; intercept `--version/--info/--health/--dry-run/
      --generate-configs` before the framework. `Program.cs:15-53`; `DesktopCommandLine` is uncalled.
- [x] P.1.7 Register the Explorer drop broker on launch (`ExplorerDropBrokerInstaller.EnsureRegistered`).
- [ ] P.1.8 Update check on start, close the shell when an update is installing, and the update link in
      the status bar -- on the MSI updater from P.1.5. `MainForm.cs:246-250, 3604-3693`.
- [ ] P.1.9 (Done but the save prompt, which waits for P.3.1.) Stop the agent on exit in "only while StorageHub is open" mode (`DesktopStopsAgent`); ask to
      save each changed workspace before closing; make shutdown finish before the process exits (the
      async `ShutdownRequested` handler is not awaited).
- [ ] P.1.10 First-run "How should StorageHub run?" prompt and `AgentHostModeController` (Windows).
- [x] P.1.11 Unhandled exceptions are reported rather than ending the process silently; logs go to disk
      (only `LogToTrace` today). A settings folder that cannot be read says so instead of opening on
      defaults.

- [ ] P.1.12 A full `dotnet test StorageHub.slnx` stopped a hand-started dev agent listening on the
      default pipe (graceful "Stopping application" at 19:54:16 on 2026-09-26, during the run). Some
      test reaches the real endpoint rather than one of its own; find it and give it its own pipe.

### P.2 The file pane

- [x] P.2.1 Paging: load the next page as the list scrolls, "Load more", and "more available" in the
      footer. A remote page is 40 rows, so any folder over 40 shows 40 today. `BrowserPaneControl.cs:
      1236-1275, 354-370, 2048-2054`.
- [x] P.2.2 Paste or drop into a folder with more than one page loads every page first rather than
      refusing "finish indexing first" (`MainForm.cs:2581`). Today that paste never works.
- [x] P.2.3 The collision check reads the whole folder, not the filtered rows.
- [x] P.2.4 Icons: shell icons per extension on Windows, drive icons on This PC, folder/file glyphs as
      the fallback and on Linux. `WindowsShellIconProvider.cs`, `BrowserPaneControl.cs:1199-1206`.
- [x] P.2.5 Right-click menu on the list, selecting the row under the pointer: New folder, New file,
      Open/Go up, Edit, Rename, Batch rename, Copy, Cut, Paste, Delete, Refresh, Select all, Properties,
      with shortcuts. `BrowserPaneControl.cs:498-574`. The address bar also stopped taking relative
      paths, which both 1.x and 2.0 resolved against the process's working directory.
- [x] P.2.6 An editable address bar (Enter goes there, a bad path says why) and Ctrl+L to focus it.
- [x] P.2.7 A failed switch to another connection clears the old listing rather than showing A's files
      under B's name.
- [x] P.2.8 Loading overlay ("Fetching folder") over the list; a centred empty-folder notice; a warning
      banner for errors that retries when clicked.
- [x] P.2.9 The directory tree (was 3.1). Built from what the pane lists, as 1.x's was.
- [x] P.2.10 Delete: local items to the Recycle Bin; a confirmation listing up to six items with
      "don't show again", skipped when "Warn before deleting" is off (the setting is saved and unread).
      `DeleteItemsConfirmationForm.cs`. Done, and Linux too: the desktop Trash through `gio trash`,
      permanent only where there is no gio -- the reason 2.0 had given for dropping the Recycle Bin.
- [ ] P.2.11 Drag out to Explorer: real paths from This PC, the drop broker from a connection; highlight
      the pane being dragged over. Files dropped from Explorer go through the agent's plan and ask
      Replace/Skip/Cancel on conflicts, as 1.4 did, rather than queueing at once.
      The drag out feeds `PendingDropRegistry` as 1.4's `BrowserPaneControl` did (P.4.6): Begin when
      the drag starts, Discard when it lands on a StorageHub pane, MarkQueued, MarkCancelled or
      MarkFailed from the broker's commit; the queue and the log already show those rows. Until then
      a desktop file queues at once, and one that is already there is refused ("Replacing an
      existing file requires captured source and destination version or entity-tag evidence.")
      rather than asked about; a desktop folder is refused, and leaves a Failed row on the Active
      tab for a minute.
- [x] P.2.12 Panes re-read while transfers run and once they settle (1.4's 5 s timer). Quietly: no cover, the filter, selection and scroll kept. A folder over a page re-reads its first page and brings the rest back on scroll.
- [ ] P.2.13 Filter survives navigation (done); Size right-aligned (done); sort and filter saved with the pane, which comes with workspace saving (P.3.1).
- [x] P.2.14 The staging bar always shows ("Clipboard: empty"), with "Paste to active pane"; the drag-hint
      row above the panes.
      Dragging a pane by its header, which the hint promises and 2.0 could not do, is back too:
      the middle of another pane swaps the two, an edge docks the dragged one there.
- [x] P.2.15 A thin accent strip across each pane in the connection's colour; opening a connection records
      it in Welcome's recent list; "Open in new pane".
      Done, with 1.x's right-click menu on a connection card (Open, Open in new pane, Edit, Delete).
- [x] P.2.16 Connections Home (was L.9).
- [ ] P.2.17 A pane opens on This PC, and a connection opened in it before that listing arrives
      loses its rows to the late This PC listing (found in P.4.6; "Open in new pane" can do it).
      `BrowserPaneModel` drops a listing for a location it has left.

### P.3 The shell

      Done: in the picker beside This PC, and what a second pane opens on.
- [ ] P.3.1 Workspaces: Save, Save As, Open (.shw), Rename, and the tab's `*` (was L.8); Welcome's
      workspace list with Open, Pin, Remove and Copy path; Workspace menu Pinned/Recent and Pin/Unpin.
- [ ] P.3.2 Workspace > Exit; workspace commands dimmed on Welcome and Sync tasks.
- [ ] P.3.3 View > Connections panel (Ctrl+B), Move connections panel, and the panel's width, side and
      visibility remembered.
- [ ] P.3.4 Rebound shortcuts are dispatched and shown; the toolbar is built from the saved layout and
      rebuilt when Settings changes it.
- [ ] P.3.5 Go menu Favorites (`FavoriteConnectionMenu` is in Core, unused).
- [ ] P.3.6 Status bar: the agent cell opens Agent control and carries its detail as a tooltip; the
      transfer speed cell (the queue already has `BytesPerSecond`); short messages for copied, staged,
      imported, exported.
- [ ] P.3.7 A concurrency change waits for running transfers before restarting the agent, as 1.4 did.
- [ ] P.3.8 The window opens centred.

### P.4 Transfers and sync

- [x] P.4.1 Queue selection survives each poll (rows are rebuilt and the selection lost every 0.5-2 s);
      Cancel/Retry/Apply follow the selection at once; several rows can be acted on. Rows are now
      updated in place by transfer id, a selected row the agent moves up is selected again, the
      table no longer scrolls back to the selection on each poll, and each tab keeps its own
      selection (by id, where 1.4 kept it by row). Requests carry the revisions shown at the click.
- [x] P.4.2 Queue context menu: Clear selected, Cancel and clear, Clear all history, with the "warn before
      clearing" confirmation (the setting is saved and unread). The menu opens on the first
      right-click and on the Menu key; the warning's button reads "Clear history", drawn primary
      as 1.4's was.
- [x] P.4.3 Cancel, retry and reconcile say what happened ("Updated 3", "2 conflicts") and refusals are
      shown; Reconcile defaults to Restart for a conflict; MarkFailed and Cancel are offered. The
      counts are said since P.4.1 (for a few seconds, then the toolbar's "N transfer(s)." comes
      back, as 1.4 wrote it after every read). A request the agent refused outright, or answered
      unreadably, was thrown into a fire-and-forget and left "Applying queue action…" up; it now
      says "StorageHub could not complete that request." and goes to the error log, for clearing
      history and for the queue's reads as well (a read the agent keeps refusing is logged once per
      run of failures, not every poll). The drop-down offers all five actions, moves to Restart
      when a conflict comes to be chosen (1.4 moved it back on every poll, so a Review chosen on
      purpose did not last; here it stays, through the polls that move its row too), and is
      dimmed with Apply while nothing chosen waits on a decision, as 1.4's was: a quiet field in
      the scheme's colours, not Fluent's grey block, and 1.4's 170 wide, trimming the long German
      entries. Open: the agent's own reason for a refusal is not shown, since it is English and
      the client drops it; the translated general message is.
- [x] P.4.4 Queue paging. Worse than the sweep thought: the queue asked for 100 rows per page against
      a contract limit of 50, the client refused every request, and the refusal was lost in a
      fire-and-forget -- so the 2.0 queue never listed anything. Found by the new error log; it now
      reads 50 at a time up to 200, and the test fake enforces the contract as the real client does.
      "Next" pages on by 200 as 1.4's did by 25; Refresh, and a transfer just queued, come back to
      the newest (with P.4.1).
- [x] P.4.5 Source and Destination name the connection; Status reads "State: error". 1.4 wrote the
      first eight characters of the connection's id before the path ("3fa2b1c4 · /photos/a.jpg");
      2.0 wrote the path alone, and the error in place of the state. Both sides now read
      "Studio SFTP · /photos/a.jpg", named from the last list of connections the agent answered, and
      read again as a new list comes in, so a rename shows as the Connection Manager closes; a
      connection not on it keeps 1.4's short id. Status reads "Failed: The server refused the
      login.", in a column as wide as Source and Destination, as 1.4's was, with the whole of it on
      the tip. Open: a This PC folder is no saved connection and the summary carries only an id made
      from the folder, so it still shows the short id, as in 1.4.
- [x] P.4.6 Explorer drops waiting to be queued show in Active and Logs and can be cancelled
      (`PendingDropRegistry` is in Core, unused). 1.4's registry held two kinds of row: a drag out
      to Explorer waiting to hear where it landed, and a folder being read for a transfer. The
      second is back: a folder pasted or dropped on a pane stands on the Active tab and in the log
      from the moment it is confirmed, the destination's own reading included ("Gathering folders
      and files... 3 queued, 1 folders"), and Cancel there stops the reading and keeps what it
      queued. 1.4's stop was thrown rather than reported, so its row went on to read "Queued";
      here it reads "Cancelled: Reading the folder was stopped...". Found on the way: plain files
      to or from This PC were refused as "Recursive transfers require saved connections on both
      panes"; they go straight to the queue again, as in 1.4, and so do files from the desktop
      until P.2.11 sends them through the agent's plan. Open, with P.2.11: the drag out has no
      broker yet, so nothing makes a "Waiting for destination" row.
- [x] P.4.7 Sync tasks loads when it is first shown, not only on Refresh. 1.4's control read the
      agent in OnVisibleChanged, so every time the tab came forward, not only the first; the shell
      now does the same when Sync tasks is selected, and going to Run history and back does not
      reload, as it did not in 1.4. Its footer reads 1.4's "Sync tasks will load when this tab is
      opened." (`TasksDeferred`, unused until now) before then, rather than "No sync tasks
      configured". The shells the tests photograph now read an agent with nothing saved rather
      than the pipe, on both sub-tabs, so a dev agent's profiles no longer reach the shots.
- [x] P.4.8 Schedule delete asks "Delete schedule …?" (`DeleteSchedulePrompt`), not the disabled-profile
      text followed by "Schedule deleted". As in 1.4: "Delete schedule" as the title, OK and Cancel
      with Cancel the default, and "Schedule deleted" only in the status once it has been. The
      disabled-profile text went back where 1.4 had it: a disabled profile, or one that no longer
      exists, reads "Name (disabled)" in the schedule's profile picker. Open: the question opens over
      the main window rather than over the schedule manager, as every dialog asked from inside a
      modal window does.
- [ ] P.4.9 Previewing from the editor loads the run and the history reliably on the first visit.
      `SyncRunHistoryTests.ApprovingDispatchesTheRunThatWasReviewed` failed once under a full-suite
      run on 2026-09-26 and passed alone three times; likely the same race.
- [ ] P.4.10 The "previewed while disabled" and "non-atomic" warnings are seen before the editor closes.
- [ ] P.4.11 Maximum deletion accepts 0.01-100 in steps of 0.25, so a saved 0.5 % is not clamped to 1.
- [x] P.4.12 A completed transfer of unknown size draws a full bar, in the success colour, as 1.4's
      did; its text still says the bytes it moved, "0 B" too, rather than "100%" of a size nobody
      knew. A transfer of nothing says "100%", as 1.4's did.
- [ ] P.4.13 Sync tasks lists saved tasks most recently updated first, as 1.4 did
      (`OrderByDescending(UpdatedUtc)`), not by name. 1.4 also read up to 1,000 runs and listed and
      counted them all under Last syncs and "Runs this session"; 2.0 reads 200
      (`SyncTasksController.MaximumLoadedRuns`) and lists 20. Decide the cap against 1.4's.

### P.5 Settings and dialogs

- [ ] P.5.1 Language on the Appearance page, applied at startup and by restarting the shell.
- [ ] P.5.2 SSH terminal settings reach the session (type, keep-alive, font, scrollback, bold); today the
      session gets `preferences: null` and a fixed font.
- [ ] P.5.3 Per-provider connection defaults prefill a new connection (the editor passes `stored: null`).
- [ ] P.5.4 "New workspace layout" (a preset or "Ask every time"); ticking "stop asking" can be undone.
- [ ] P.5.5 "Start with" concurrency, enabled only when adaptive is on; the update toggles depend on one
      another; the update source and installed version under Updates.
- [ ] P.5.6 Installation check window, from Agent control and from the splash.
- [ ] P.5.7 Open an SSH client in its own window from the Connection Manager.

## L. Look parity with 1.4 (before 1.5)

An audit on 2026-09-26 put the Avalonia shots beside `docs/ui-reference/`. The welcome, sync tasks, new
workspace, connection picker and queue still read as 1.4. The pane, the connection editor, settings and
the sidebar don't, and the chrome shared by every screen adds to it. 2.0 is meant to be 1.4 plus
improvements, not a new design, so this comes ahead of the rest of section 1. The new fields from 1.2–1.4
(speed limits, proxy, FTP options) stay; they are filed where 1.4 would have put them.

An item is done when its screen, photographed at 1.25x in both light and dark, reads as the matching
reference shot.

- [x] L.1 Shared chrome (every screen): flat toolbar and queue buttons that dim rather than fill when
      unavailable; column headings in the regular weight, on a band in the palette, with rows as
      dense as 1.4's (Fluent's `TableViewRowPadding` made each row 37 tall); boxed tabs with the accent
      along the top. Text size was a false alarm: body text is 12 px like 1.4's 9 pt Segoe UI, and the
      "1.25x" shell shots lay out a larger window at 1.0 rather than scaling, which makes text look
      small beside a 125% reference.
- [ ] L.8 Workspace tabs: Save, Save As and Open workspace have no handlers in 2.0, and the tab's `*`
      for unsaved changes comes with them. Close is done: the X on a workspace tab and
      Workspace > Close Workspace both close it and release its panes' connections.
- [ ] L.2 The file pane (ref 05). Done apart from the drag-hint row: the chip, the badges, the state
      line, the `FILES` row with its overflow, "Filter:" and the item count. Was: the connection chip with its `STORAGE`/`LOCAL` badges and the
      "● Ready" status line in place of the "Select the profile to connect" drop-down; the labelled
      `FILES` command row (New folder · Copy · Move · Paste · Delete · overflow) in place of the icon
      strip, with Copy/Move/Paste taken out of the bottom-right corner; "Filter:" beside the path box;
      the "N items" footer; the drag-hint row under the tab strip.
- [x] L.3 A new pane opens on This PC with the drives listed, not on an empty `/`. Every pane does:
      1.x opened the second on "Connections Home", a listing of saved connections that 2.0 does not
      have yet (L.9).
- [ ] L.9 Connections Home: a pane pointed at no connection lists the saved ones as rows, as 1.x's
      second pane did, and opening a row opens that connection.
- [x] L.10 The overview's Agent card still said "Starting" after the status bar had moved to
      "Agent: connected". Fixed with the live Welcome page (e174058).
- [ ] L.11 Sync tasks' first sub-tab reads "Tasks", as in ref 03, not "Sync tasks"
      (`Ui.Sync.TasksTitle`, which is the page's headline); it needs a caption of its own, with its
      translations.
- [x] L.4 The connection editor (ref 08). Done, with the Connection Manager's list still beside it:
      the sidebar has no Edit or Delete of its own yet, so the list stays until L.7 gives it them,
      and then the editor becomes 1.x's plain "Edit Connection" dialog. Was: back to the dialog shape. Type and Provider / protocol
      drop-downs in a fixed header with the provider's description under them; General,
      Authentication and TLS / SSH Trust tabs; each description under its control rather than under
      its label; a red asterisk on required fields; icon and colour swatches; the badge preview; the
      "Loaded version N" status in the footer. Proxy, speed limits and FTP Advanced go into the tabs.
- [ ] L.5 Settings pages (ref 02): Shortcuts, Connections & trust (per-provider defaults under a
      `STORAGE` caption, SSH Terminal under `CLIENTS`) and Background agent come back. Performance and
      Confirmations fold back into "Transfers & sync".
- [ ] L.6 Settings rows (ref 02): rows grouped in one card under capitalised captions (`CONCURRENCY`,
      `CONFIRMATIONS`) instead of a card each; units inside number fields ("4 jobs"); buttons ordered
      Apply / Cancel / OK.
- [x] L.7 The connections sidebar (ref 09). Done for the cards and the details panel: coloured
      icon tiles, the provider / folder / health line, tag chips, a selected card with inline edit
      and delete, and a details panel with Open, Test, Edit and Delete. Kept from 2.0: groups made
      by hand rather than 1.x's fixed Storage and Clients sections (c4886f7). Not done: a Favorites
      section, which the hand-made groups replaced; say if it should come back. Was: coloured rounded icon tiles in place of the
      `STORAGE`/`CLIENT` text badges; tag chips under each card; a Favorites section above the
      storage groups; inline edit and delete on the selected card.

## 1. Settings that are refused today start working

The profile already stores these; `CodeLogicConnectionProfileConnector.BuildAsync` refuses them.

- [x] 1.1 Transfer rate and ETA: library `Progress` through `TransferProgress` and IPC to the queue's
      progress column and the status bar (`TransferBytesPerSecond`).
- [x] 1.2 Speed limits: per-connection bandwidth to `TransferLimits`, the global limit to `MaxTotal*`;
      the "Speed Limits…" command gets its dialog.
- [x] 1.3 Proxy: `StorageProxyConfig` for FTP, SFTP and S3; a proxy section in the connection editor.
- [x] 1.4 FTP options: separate connect and read timeouts, filename encoding (code pages), server time
      zone, listing parser, active mode; an "Advanced" expander in the editor.
- [ ] 1.5 SFTP options: keyboard-interactive login, several keys and inline keys (no temp key file),
      jump host, algorithm lists, session limits.
- [ ] 1.6 Conflict choices on transfers: overwrite, skip, if newer, if size differs, rename, resume.
- [ ] 1.7 Resume: real resume for uploads and downloads in place of the `ResumeToken => null` stubs.
- [ ] 1.8 Server checksums (`PreferServer`): verify without re-downloading; the inspector shows where a
      checksum came from; the sync scanner uses them.
- [ ] 1.9 Certificate pins for S3.

## 2. Connection test and trust (was port item 10)

- [ ] 2.1 The Test button runs `TestConnectionAsync` on the unsaved draft and shows each step.
- [ ] 2.2 A refused host key or certificate is shown with Trust / Reject (`TrustOrRolloverAsync`,
      `RejectAsync`); trust-on-first-use for SFTP and FTPS stops being refused; the editor keeps the
      host-key fingerprint in the draft.
- [ ] 2.3 A "Connection details" panel from `GetConnectionDiagnosticsAsync`.

## 3. Directory tree (was port item 11)

- [ ] 3.1 The folder tree beside the file panes.

## 4. New abilities in the file panes

- [ ] 4.1 Properties: permissions, owner, link target, dates; a chmod dialog and "Set modified time".
- [ ] 4.2 Free space in each pane's status bar.
- [ ] 4.3 Connection health (retrying, lost, back) on the connection card and in the activity log.
- [ ] 4.4 Auto-refresh of a folder when it changes (polling for remote connections, off by default).
- [ ] 4.5 Compare folders: a two-pane diff from `CompareAsync`; differences copied through our queue.
- [ ] 4.6 Clean up leftover staging files, as a maintenance action.
- [ ] 4.7 (Optional) FTP raw command console.

## 5. New providers

Each goes through the profile model, SQLite, IPC, the connector, capabilities, the editor (fields,
icon, da/de text), settings and import/export, and a lab container with a conformance run.

- [ ] 5.1 WebDAV (lab: `rclone serve webdav`, plus a TLS variant for pins)
- [ ] 5.2 Azure Blob (Azurite)
- [ ] 5.3 Google Cloud Storage (fake-gcs-server)
- [ ] 5.4 Swift (all-in-one container)

## 6. Release

- [ ] 6.1 A full pass of the real app against the lab, with screenshots of every changed window.
- [ ] 6.2 The Linux .deb installed and checked in WSL.
- [ ] 6.3 Release notes and the 1.x upgrade path.
