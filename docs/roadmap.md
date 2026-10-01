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
user's systemd unit on Linux; a build from source uses an agent started by hand). The failure
screen's Check installation button opens the installation check (P.5.6). The language is loaded and
applied, and chosen on the Appearance page (P.5.1).

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
- [x] P.1.5 Installers: a plain WiX MSI on Windows and the .deb on Linux, nothing else (decided
      2026-09-26). Velopack goes: drop `vpk` from `eng/package-windows.ps1` (no Setup.exe, no portable
      ZIP), and the hooks in `DesktopPackageLifecycleHooks` become MSI custom actions -- autostart on
      install and upgrade, stop the agent before upgrade and uninstall, remove the Run entry and the
      drop broker's COM registration on uninstall. `DesktopUpdater` is Velopack-based and needs a
      replacement: find the newer release, download its MSI, verify it, and run it. Update
      `docs/releasing.md` and `eng/test-windows-installer.ps1` to match.
      `eng/installer` is a WiX SDK project (NuGet, `dotnet build`, kept out of the solution) that
      the packaging script builds from its staged folder and then reads back to check. Per-user, as
      1.4 was: `%LOCALAPPDATA%\Programs\StorageHub`, Agent beside the desktop, no elevation, a Start
      menu shortcut, and the folder recorded under `HKCU\Software\StorageHub\Installer`. The custom
      actions run the installed desktop with `--package-hook`: the copy being replaced stops the
      agent before Windows Installer looks for files in use, on upgrade and uninstall; install
      registers the sign-in entry and an upgrade keeps it only for the mode in force; uninstall
      also takes the Run entry and the drop broker's registration. The older package's removal
      inside an upgrade runs no uninstall hook, and the data is never touched. The updater reads
      the GitHub releases, as 1.4's did, offers the newest newer one with
      `StorageHub-<version>-win-x64.msi` and a SHA256SUMS naming it (candidates only when asked
      for, drafts never, downloads only from the repository's own releases), holds the MSI to
      that digest and GitHub's size, and starts `msiexec /i /qb` with `STORAGEHUB_RELAUNCH=1` once
      the shell has closed; the MSI reopens StorageHub. Releases are unsigned, so there is no
      signature to check yet. A copy the MSI did not install is never updated. Candidates now
      compare by number (rc.10 after rc.9). The locked restore in the packaging script also
      stopped naming the runtime, which had broken it against the two-RID lock files. Not run
      here: installing, upgrading and uninstalling for real, which would stop the dev agent on
      this user's pipe; `eng/test-windows-installer.ps1` does that on a disposable machine, with
      `-PreviousBundleRoot` for the upgrade.
- [x] P.1.6 `--agent-only` (the sign-in autostart) ensures the agent and exits without a window, and
      honours `STORAGEHUB_DISABLE_AUTOSTART`; intercept `--version/--info/--health/--dry-run/
      --generate-configs` before the framework. `Program.cs:15-53`; `DesktopCommandLine` is uncalled.
- [x] P.1.7 Register the Explorer drop broker on launch (`ExplorerDropBrokerInstaller.EnsureRegistered`).
- [x] P.1.8 Update check on start, close the shell when an update is installing, and the update link in
      the status bar -- on the MSI updater from P.1.5. `MainForm.cs:246-250, 3604-3693`.
      The check runs once the window is up, by the update settings: none with automatic checks
      off, a download unless downloads are off, and an install only with automatic restart on.
      An update to install closes the shell as Exit does, asking about changed workspaces, and a
      close cancelled there leaves it to install when StorageHub does close. The status bar's last
      cell, after the last line, is 1.4's link: the updater's own words in 1.4's colours (green
      ready or installing, amber available, red failed, muted otherwise), 1.4's tooltip and
      accessible name, and a click opens the update window.
- [x] P.1.9 Stop the agent on exit in "only while StorageHub is open" mode (`DesktopStopsAgent`); ask to
      save each changed workspace before closing; make shutdown finish before the process exits (the
      async `ShutdownRequested` handler is not awaited). The save prompt came with P.3.1: Cancel on
      any workspace keeps the shell open, and closing one changed tab asks the same.
      `ShellShutdown` holds the first close: a second close meanwhile asks nothing and starts nothing,
      the window takes no input once answered (as 1.x's froze), everything is let go at once and
      each on its own so one that fails or hangs cannot keep the rest or the agent stop, and both
      halves are bounded (5 s, then 10 s for the agent) before the window closes for real. Windows
      signing out is not held when nothing is unsaved. The buttons stay Yes/No/Cancel, as 1.x's were.
      The restart after a language change (P.5.1) closes the same way, but leaves the agent running,
      and a restart of the agent for saved settings is finished before the agent is stopped.
- [x] P.1.10 First-run "How should StorageHub run?" prompt and `AgentHostModeController` (Windows).
      Asked once, over the shell, by an installed Windows build (not one run from source); only a
      change of mode is applied, and the Settings page applies through the same controller. Closing
      the prompt keeps the preselected sign-in, and keeping it is only reported if it fails; until
      the MSI registers the sign-in entry (P.1.5) the prompt makes it, quietly, as 1.4's installer
      had. The service choice went with the service. Not asked on Linux: the mode there is the user's
      systemd unit, which the .deb leaves each user to enable and which the agent platform reads
      from a unit file rather than from what systemd has enabled, so neither Settings nor the
      prompt can change it yet.
- [x] P.1.11 Unhandled exceptions are reported rather than ending the process silently; logs go to disk
      (only `LogToTrace` today). A settings folder that cannot be read says so instead of opening on
      defaults.

- [x] P.1.12 A full `dotnet test StorageHub.slnx` stopped a hand-started dev agent listening on the
      default pipe (graceful "Stopping application" at 19:54:16 on 2026-09-26, during the run). Some
      test reaches the real endpoint rather than one of its own; find it and give it its own pipe.
      It was not a test. The desktop and agent CodeLogic logs show every graceful stop of a dev agent
      (19:54:16 and 21:02:50 on 2026-09-26, 17:04:29 and 22:04:30 on 2026-09-27) followed 7-12 ms
      after the agent's "Framework stopped" by the dev desktop's own graceful stop: a dev desktop was
      closed, and on a machine with no sign-in entry that is "only while StorageHub is open", so its
      close (P.1.9, from 13bd18c at 19:35 that day) sent the agent the shutdown request. That close
      and the language restart's fallback now leave an agent started by hand for a build run from
      source alone, where startup and a settings restart already did. The one path a test had to the
      real agent's shutdown is closed too: a preview shell's panes had `ForThisMachine`, so a terminal
      told by the real agent that its protocol was older would have stopped it; previews now get no
      lifecycle, as 1.4's test-hosted shell had none, and the app's own shell gets
      `ThatCanRestartTheAgent`. Left open: a preview shell still makes its transfer queue, listing,
      inspector and terminal clients on the default pipe. None can stop the agent, but a test that
      changed the queue or a listing while a dev agent is up would act on that agent's data; none
      does today.

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
- [x] P.2.11 Drag out to Explorer: real paths from This PC, the drop broker from a connection; highlight
      the pane being dragged over. Files dropped from Explorer go through the agent's plan and ask
      Replace/Skip/Cancel on conflicts, as 1.4 did, rather than queueing at once.
      Done. This PC's rows carry their own paths beside the pane token, so Explorer, Nautilus and
      Dolphin take them as any file drop, copy or move. A connection's rows carry 1.4's marker
      folder (`ExplorerDragOut`, in Desktop.Core): made before the drag, registered with the agent
      while it runs, committed once it ends, with `PendingDropRegistry` fed as 1.4's
      `BrowserPaneControl` fed it: Begin, Discard when a StorageHub pane took the drop, MarkQueued,
      MarkCancelled or MarkFailed from the commit, and "Queued in StorageHub -> folder" in the pane's
      banner. Copy only while the marker rides along, as in 1.4: Explorer moving the marker would
      move it for real, since the copy hook only stands in a copy's way. Without the broker the drag
      carries nothing out and says 1.4's "Drag to File Explorer is unavailable...". The list or tree
      under the pointer is drawn in the selection colour while it would take the drop, a drag from
      another pane included. Files dropped on a connection go to the agent's import review, folders
      and all, and ask OK/Cancel or, with some already there, Yes (replace), No (skip) or Cancel,
      then "Queued N Explorer import file(s)."; dropped on a folder in the tree they go there, as
      in 1.4. The agent now serves that review on Linux as well, so a Nautilus or Dolphin drop is
      asked about the same way. Kept from 2.0: files dropped on a This PC folder, which 1.4 refused,
      are queued as a paste from This PC.
      Not possible on Linux: dragging a connection's rows out. There is no drop broker, and a file
      manager takes file URIs and nothing that could stand in for a file not yet downloaded;
      staging the download first, as 1.4's Explorer export did for Copy, would hold the pointer for
      as long as the download takes. The pane says so once such a drag ends outside StorageHub
      ("...only be dragged out ... on Windows. Copy them to a This PC pane instead.").
      Open: the drag out to Explorer is proven headless (the registry rows, the agent's begin and
      commit, the landed-in-a-pane case) but not yet by hand against a real Explorer window.
- [x] P.2.12 Panes re-read while transfers run and once they settle (1.4's 5 s timer). Quietly: no cover, the filter, selection and scroll kept. A folder over a page re-reads its first page and brings the rest back on scroll.
- [x] P.2.13 Filter survives navigation (done); Size right-aligned (done); sort and filter saved with the pane, which comes with workspace saving (P.3.1).
      Done: each pane's sort column, direction and filter go in the `.shw` and come back applied.
- [x] P.2.14 The staging bar always shows ("Clipboard: empty"), with "Paste to active pane"; the drag-hint
      row above the panes.
      Dragging a pane by its header, which the hint promises and 2.0 could not do, is back too:
      the middle of another pane swaps the two, an edge docks the dragged one there.
- [x] P.2.15 A thin accent strip across each pane in the connection's colour; opening a connection records
      it in Welcome's recent list; "Open in new pane".
      Done, with 1.x's right-click menu on a connection card (Open, Open in new pane, Edit, Delete).
- [x] P.2.16 Connections Home (was L.9).
      Done: in the picker beside This PC, and what a second pane opens on.
- [x] P.2.17 A pane opens on This PC, and a connection opened in it before that listing arrives
      loses its rows to the late This PC listing (found in P.4.6; "Open in new pane" can do it).
      `BrowserPaneModel` drops a listing for a location it has left.
      Done, as 1.4's `_uiNavigationSequence` did it: every navigation moves the pane's count on, and
      a listing, a page or a quiet re-read lands only if it has not moved since it was asked for.
      Only the latest navigation takes the loading cover off, a superseded one no longer says
      "Disconnected" over its replacement, and no page is asked for while the pane is navigating.
      "Open in new pane" (and opening from Welcome into a new workspace) had its own way to fail:
      the connection was opened before the new pane's connection list arrived, and failed as no
      longer saved. It now waits for the list, as 1.4's `RestoreStateAsync` did, and the list no
      longer takes the cover off, clears the status or opens This PC once the pane has moved on.

### P.3 The shell

- [x] P.3.1 Workspaces: Save, Save As, Open (.shw), Rename, and the tab's `*` (was L.8); Welcome's
      workspace list with Open, Pin, Remove and Copy path; Workspace menu Pinned/Recent and Pin/Unpin.
      Save (Ctrl+S), Save As, Open (Ctrl+O) and Rename read and write 1.4's `.shw`, with one member
      of 2.0's own, a pane's `filesBarHidden` (4.8), which 1.4 skips and drops when it saves; the
      `*` shows whenever the workspace differs from its file. A save or a close while a file is
      still opening waits for it, and a This PC folder saved on the other OS opens with that pane on
      This PC. Welcome lists the pinned workspaces, then the recent ones, with 1.4's Open, Pin/Unpin,
      Remove from list (never the file) and Copy path, a double-click or Enter to open, the path as
      the tooltip and a missing file dimmed. The Workspace menu has 1.4's Pin/Unpin Workspace and
      its bold Pinned and Recent headings over the entries, ahead of Exit, drawn again as it opens;
      pinning an unsaved workspace saves it first. A missing file, opened from either, offers to
      leave the lists. Rename is on the menu only, as in 1.4 (no rename on the tab itself).
- [x] P.3.2 Workspace > Exit; workspace commands dimmed on Welcome and Sync tasks.
      Exit closes the window, so it asks about changed workspaces as the X does. Save, Save As,
      Rename, Close and Pin dim on Welcome and Sync tasks, and their shortcuts do nothing there, as
      in 1.4. The pane commands stay lit on every tab, which is what 1.4's Welcome shows (ref 01).
- [x] P.3.3 View > Connections panel (Ctrl+B), Move connections panel, and the panel's width, side and
      visibility remembered.
      Done: Ctrl+B and the View entry show and hide the panel, the entry's icon framed in the menu
      and the toolbar button framed while it shows, as 1.4 drew a check; Move docks it to the other
      side at the same width; the panel's "..." has 1.4's Move to the other side, Refresh and Hide
      panel. Side, width and visibility are saved as they change (a drag or the arrow keys) and
      restored at startup, and a side chosen in Settings moves the panel when Settings closes. The
      width is in device pixels within 1.4's 220 to 640, with 1.4's 560 kept for the workspace;
      the panel is never under 220 on screen, where its header stops fitting, so above 100% scaling
      its narrowest, and above about 136% its default, is wider than 1.4's.
- [x] P.3.4 Rebound shortcuts are dispatched and shown; the toolbar is built from the saved layout and
      rebuilt when Settings changes it.
      Done: the keys come from the saved shortcut map, resolved as 1.4 did (an unusable set falls
      back to the defaults whole), and the menu bar, a pane's right-click menu and its "..." show
      the key as bound now. The toolbar is built from the saved layout, leaving out commands 2.0
      has not wired and the dividers they leave, with 1.4's three label styles; what does not fit
      goes behind a chevron at its end, whose menu runs the same commands, as 1.4's ToolStrip
      did. Both follow when Settings closes and after an import. Rebinding is on Settings'
      Shortcuts page (L.5).
- [x] P.3.5 Go menu Favorites, and favourite connections with it.
      Done: Go ends with 1.4's section, a line and a bold Favorites over the enabled favourites a
      pane can open, by name (`FavoriteConnectionMenu`), or "No favorite connections" with 1.4's hint;
      choosing one opens it in the active pane, making a workspace from Welcome, as double-clicking
      its card does. It follows each listing of the connections panel. A card's right-click menu has
      1.4's Toggle favorite between Open in new pane and Edit, with 1.4's two lines; it writes the
      flag into the profile at the version read, so the agent keeps it across a restart, and
      Welcome's recent connections follow a toggle or a delete, as 1.4's ConnectionsChanged did.
      The details panel marks a favourite with 1.4's filled star before its name and a Favorite yes
      or no, rather than a button: 1.4 kept the toggle on the menu. Go's "No favorite connections"
      shows its hint although dimmed. Fixed on the way: saving from the editor no longer drops the
      favourite flag (1.4 did), nor the enabled state and default paths, which the editor has no
      field for. A collapsed group, Favorites included, stays collapsed across the panel's
      listings and searches, and follows a rename, as 1.4's `_collapsedGroups` kept it; like
      1.4's, it is remembered for the session and not saved, so StorageHub opens with every group
      open. Left open: the Go entries and the starred name have no accessible description yet.
- [x] P.3.6 Status bar: the agent cell opens Agent control and carries its detail as a tooltip; the
      transfer speed cell (the queue already has `BytesPerSecond`); short messages for copied, staged,
      imported, exported.
      Done: a click on the agent cell runs Tools > Background agent; its tooltip is "Open background
      agent controls" until the agent reports, then the agent's own detail, and while it is brought
      back 1.4's reconnecting, restarted or reconnected sentence. The cell follows a failed call at
      once (reconnecting, then not connected), as 1.4's did, rather than waiting for the next poll;
      recovery mode is in the warning colour, as 1.4 drew it, and the queue count includes running
      syncs. The rate cell is the queue's total, "0 B/s" while nothing moves, as 1.4's always said,
      and while the agent is not answering or the queue is on Logs, where nothing reads the rate.
      Staging says "Copied 3 item(s). Choose a destination and paste." (or Cut), Clear says the
      clipboard was cleared, an import or an export says so, and a paste or drop says what it came
      to ("Queued 3 transfer(s).", or that a folder read was stopped), in the first cell, where 1.4
      put them; `ShellPreviewModel.Say` holds a message there for eight seconds, 1.4's longest;
      another pane picked or a row selected does not write over it, as neither redrew 1.4's bar.
      The "not built yet" stand-in goes the same way. A state rather than news, saved concurrency
      waiting for the transfers or being applied, stays until the restart is done
      (`SayUntilResolved`), coming back after a message said over it. That goes past 1.4 on
      purpose: its poll wrote over the wait within eight seconds, and a setting not yet in force
      has to stay readable. An import says "Settings imported" before it, as 1.4 did. A pane with
      nothing chosen reads "No connection" rather than "/". The bar is 1.4's 22 px, one row with a
      thin line after each cell from the rate on, and each cell has 1.4's accessible name.
      A refused paste or drop is 1.4's "Transfer queue" warning rather than a message here (P.4.14).
      Left open: on Logs the queue count holds its last reading, as the queue is not read there. The
      update cell after the last line came with P.1.8. 1.4 also wrote over a
      message when the queue counts changed, which 2.0 leaves to the eight seconds. A created file
      or folder, a rename, a send to the Recycle Bin, a delete and "Edited file uploaded" are said
      on the pane's own status line, where 1.4 said them in the bar's first cell. A new file or
      folder, a rename, a batch rename, a delete or an external edit that fails is 1.4's "Transfer
      queue" warning, the one a refused paste gets (P.4.14), through the same `RefuseAsync`, and is
      not said on that line: the name refused on create, rename or batch rename, nothing or too
      little selected to rename, a batch stopped part way ("Renamed n item(s), then stopped at …"),
      a delete that failed or stopped part way, and an edit asked of something that is not one
      file on a connection, as 1.4's `ShowManualTransferFailure` calls were. A refused rename
      now reads as 1.4's "The item could not be renamed. …" rather than the bare reason. An editor
      that cannot be opened keeps its own "External editor" warning, as in 1.4. One drift left: a
      "Don't show this warning again" that cannot be saved is let go for the session, where 1.4
      warned and did not delete.
- [x] P.3.7 A concurrency change waits for running transfers before restarting the agent, as 1.4 did.
      The shell restarts it once Settings has closed, or once the agent reports no transfers or
      synchronizations running, and says which in the status bar; an import that changes the
      concurrency does the same, where 2.0 had said it restarted the agent and never did. Saving
      used to restart it at once, on every Apply, whatever was running. A save during a restart
      follows it, closing waits for one under way, and a source build with a hand-started agent is
      told to restart StorageHub rather than losing its agent. Open: a change to the total speed
      limits alone still says "Concurrency settings…" (needs new strings), and a restart still
      waiting when StorageHub closes is dropped, as in 1.4.
- [x] P.3.8 The window opens centred.
      Done: on the screen, as 1.4's CenterScreen, and 2.0 restores no position. As 1.4's
      LogicalWindowSize did, a screen smaller than 1500 by 920 gets a smaller window, minimum
      included, so the title bar is never above the top of the screen. It is fitted before it is
      shown, so it does not open large and then jump, and again once its frame is known.
- [x] P.3.9 An empty table's message ("No workspaces yet", "No sync tasks configured") is not a row:
      it does not light up, take a click or the selection, so nothing opens it. Only Welcome and Sync
      tasks have such rows; the queue and the pane show theirs outside the list, as 1.4 did. Welcome's
      workspace menu and double-click, added with P.3.1, act only on a selected row, so neither
      reaches the message.

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
      until P.2.11 sends them through the agent's plan. The drag out's "Waiting for destination"
      row came with P.2.11.
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
- [x] P.4.9 Previewing from the editor loads the run and the history reliably on the first visit.
      On the first visit the view asks for the history as it is attached and the shell asks for
      the run, and Run history dropped whichever came second ("busy means return"): the run was
      never loaded, or the history was marked loaded without being read. Its reads now queue
      behind one another, as 1.4's review did behind its gate. The failing test was a different
      race: its stub still read the run as waiting for approval after dispatching it, and the
      half-second poll that follows a dispatch picked that up when a full-suite run was slow
      enough; the stub now answers as the agent does. Approving sends the revision and digest
      that were confirmed, even if the idle poll read a newer one while the question was open.
- [x] P.4.10 The "previewed while disabled" and "non-atomic" warnings are seen before the editor closes.
      A preview that warns leaves the editor open with the warning in its status, as 1.4's did
      (the disabled one asks for Enabled to be ticked there); a plain one still closes it. The run
      goes to Run history either way, so the warning adds that Close, 1.4's button back in the
      foot, leads to it. Closing the editor stops a preview still running, as 1.4's did, so a plan
      finishing behind a closed editor no longer moves the main window to Run history.
- [x] P.4.11 Maximum deletion accepts 0.01-100 in steps of 0.25, so a saved 0.5 % is not clamped to 1.
      Shown to two places, as 1.4 did. The floor of 1 did not clamp a saved 0.5 on sight, but it
      could be neither typed nor stepped down from, and the complaint said "1 to 100". Maximum
      deletes and the transfer buffer beside it step by one again, as 1.4's did, not by 10 and 1024.
- [x] P.4.12 A completed transfer of unknown size draws a full bar, in the success colour, as 1.4's
      did; its text still says the bytes it moved, "0 B" too, rather than "100%" of a size nobody
      knew. A transfer of nothing says "100%", as 1.4's did.
- [x] P.4.13 Sync tasks lists saved tasks most recently updated first, as 1.4 did
      (`OrderByDescending(UpdatedUtc)`), not by name. 1.4 also read up to 1,000 runs and listed and
      counted them all under Last syncs and "Runs this session"; 2.0 read 200
      (`SyncTasksController.MaximumLoadedRuns`) and listed 20. Done: 1.4's 1,000, every run read
      is listed in the agent's order (the run started last first), and the table, the card and the
      footer's "Showing n durable run(s)" count the same runs.
- [x] P.4.14 A paste or drop that fails is shown in a warning ("Transfer queue"), as 1.4's
      `ShowManualTransferFailure` did; a folder read stopped from the queue is not a failure and is
      only said. Done: every refusal of a paste or drop is that warning and is not said in the
      status bar as well, as 1.4 did not say it there: a destination that could not be read to
      the end, in 1.4's "could not finish indexing"; the destination or the enqueue refusing; the
      agent failing mid-way, as 1.4's "could not enqueue" rather than 2.0's "retrying
      automatically", which it was not; and files dropped from the desktop that cannot be used.
      A failure after some were queued reads as 1.4's did ("3 transfer(s) were durably accepted
      before the next request failed."), and a transfer the agent never confirmed names its ids
      and asks for the queue to be checked for them, as 1.4's did. Staging that fails is the same
      warning, as it was in 1.4. A stopped folder read is still only said, in 1.4's words, with
      how many were queued before it. A failed new file or folder, rename, batch rename, delete or
      external edit is this warning too, as in 1.4 (under P.3.6).
- [x] P.4.15 Run history names a plan operation's connection by its whole id and a connection's top
      folder `<root>`, as 1.4's review did (`SyncRunReviewControl.FormatEndpoint`), not by the
      first eight characters and "Root". The eight were 1.4's transfer queue's (P.4.5), not its
      review's; the history table's run id stays at eight, as 1.4's did. As in 1.4, the whole id
      leaves less of the path in the From and To columns, and the whole cell is on the tip, as
      1.4's grid showed a cut-off cell. The connection's name, which the queue shows since P.4.5
      from the shell's `Sidebar.NameOf`, is left out on purpose: 1.4's review printed the id.

### P.5 Settings and dialogs

- [x] P.5.1 Language on the Appearance page, applied at startup and by restarting the shell. Each
      language is named in itself; saving one that changes the words on screen asks to restart, and
      the restart leaves the agent running. A save that fails says so and keeps the window open, as
      1.4's did. "Same as Windows" became "Same as the system". Left open: the restart itself has
      not been tried in the real app.
- [x] P.5.2 SSH terminal settings reach the session (type, keep-alive, font, scrollback, bold). Read
      from the settings file as each session opens, as 1.4 read them per terminal window; the size
      is in points, as 1.4's was, and a family this computer lacks falls back to a monospace one.
      The remote program is now told the pane's size once the session opens, rather than keeping
      80x24 until the pane is resized. Left open: not yet tried against a real SSH server in the
      running app.
- [x] P.5.3 Per-provider connection defaults prefill a new connection, and every save takes the
      timeouts and retries a provider has no field for from them, as 1.4's editor did. Moving a
      connection, saved or not, to another provider lets go of the old provider's port, TLS mode and
      other defaults nobody changed, so the new provider's apply, as in 1.4; what was typed, such as
      the host, comes along, where 1.4 threw away every field. A saved connection is not refilled
      from the defaults when opened (1.4 did, which could change an empty field on the next save).
- [x] P.5.4 "New workspace layout" (a preset or "Ask every time"); ticking "stop asking" can be undone.
      Under Workspace, kept in step with "Default pane layout" as 1.4 kept them. Two fixes beside
      it: remembering one pane or the grid no longer overwrites the saved orientation, and a
      workspace made to open a connection into follows that orientation, as 1.4's did.
- [x] P.5.5 "Start with" concurrency, enabled only when adaptive is on (raising it raises the maximums,
      which the settings file requires); the update toggles depend on one another; the update source
      and installed version under Updates. Came with L.5 and L.6.
- [x] P.5.6 Installation check window, from Agent control and from the splash. 1.4's checks less
      the service ones; what starts the agent is checked in their place (the packaged agent on
      Windows, the systemd user unit on Linux), and a database an earlier version left under the
      user's own folder is pointed at. Creating the data directory is the one repair left. An agent
      still starting counts as answering; on Linux the summary names systemd rather than a mode,
      since mode detection there does not read what systemd has enabled. Open: 1.4's per-user
      database is found and pointed at, but nothing migrates it into 2.0's root.
- [x] P.5.7 Open an SSH client in its own window from the Connection Manager. Nothing to port, found
      with L.4: 1.4 had no route to it. Its only own-window terminal was "Open client" on Quick
      Connect's toolbar, and Quick Connect was never enabled in 1.4 (it is not in
      `IsAvailableCommand`, and 2.0 matches). 1.1 took "Open client" off the saved-connection editor
      as a second route to the panel's Open, which opens a client connection as a terminal in a
      pane, as 2.0 does. A pop-out terminal, or Quick Connect, would be a feature after parity.
- [x] P.5.8 "Show favourites in their folders too" is applied: the connections panel lists a favourite
      under its folder as well as under Favourites, and re-reads the setting when Settings closes, as
      1.4's `RefreshConnectionSurfaces` did. Done with P.3.5: the panel reads it at startup, and
      again when Settings closes or an import finishes.
- [x] P.5.9 The SSH terminal passes the mouse to a remote program that asks for it, as 1.4's view
      was built to: clicks and the wheel under DECSET 1000, drags under 1002, every move under 1003,
      sent in the SGR form (1006) and not at all to a program that asked only for X10, as 1.4
      encoded them. While a program has the mouse the terminal does not select or scroll back;
      Shift keeps the mouse local, as in 1.4. 1.4 went no further than taking the mouse: its
      window never subscribed to what its view raised, so the program was sent nothing, and 2.0
      sends it. One drift on purpose: 1.4's window pasted on every right-click, program or not,
      which would now be a click for the program and a paste as well, so a right-click is the
      program's while it has the mouse and Shift+right-click pastes. A move within the cell last
      reported is not sent again, a release goes where its press went, each of several held
      buttons included, and a session that has gone gives the mouse back. Left open: not yet
      tried against htop or nvim in the running app. Stock vim likely asks for X10 and keeps
      selecting locally, as under 1.4, because the terminal answers its version query with the
      VT220 attributes, as 1.4's did; `:set ttymouse=sgr` hands it the mouse.

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
- [x] L.8 Workspace tabs: Save, Save As and Open workspace have no handlers in 2.0, and the tab's `*`
      for unsaved changes comes with them. Done with P.3.1: the tab's tooltip is the file's path and
      its accessible name "{name} workspace", as 1.4 set them. Close is done: the X on a workspace tab
      and Workspace > Close Workspace both close it and release its panes' connections.
- [ ] L.2 The file pane (ref 05). Done apart from the drag-hint row: the chip, the badges, the state
      line, the `FILES` row with its overflow, "Filter:" and the item count. Was: the connection chip with its `STORAGE`/`LOCAL` badges and the
      "● Ready" status line in place of the "Select the profile to connect" drop-down; the labelled
      `FILES` command row (New folder · Copy · Move · Paste · Delete · overflow) in place of the icon
      strip, with Copy/Move/Paste taken out of the bottom-right corner; "Filter:" beside the path box;
      the "N items" footer; the drag-hint row under the tab strip.
- [x] L.3 A new pane opens on This PC with the drives listed, not on an empty `/`. Every pane does:
      1.x opened the second on "Connections Home", a listing of saved connections that 2.0 does not
      have yet (L.9).
- [x] L.9 Connections Home: a pane pointed at no connection lists the saved ones as rows, as 1.x's
      second pane did, and opening a row opens that connection. Done as P.2.16.
- [x] L.10 The overview's Agent card still said "Starting" after the status bar had moved to
      "Agent: connected". Fixed with the live Welcome page (e174058).
- [x] L.11 Sync tasks' first sub-tab reads "Tasks", as in ref 03, not "Sync tasks"
      (`Ui.Sync.TasksTitle`, which is the page's headline); it needs a caption of its own, with its
      translations. Done: `Ui.Sync.TasksTab`.
- [x] L.4 The connection editor (ref 08). Done: 1.x's plain "Edit Connection" dialog, the editor with
      no list beside it, opened on a new connection by New Connection (menu, toolbar, the panel's
      New button, Welcome's Connections) and on a connection by Edit (a card, its menu, the details
      panel); the provider's colour along the top, "New unsaved profile" or "Loaded version N" in
      the footer, Escape to cancel, Enter to save, and a save closes it. New starts on S3, as 1.x's
      did. The details panel's "Fix credentials…" and "Review trust…" open it on Authentication or
      TLS / SSH Trust, and all three tabs are always there. A saved SFTP, SSH or pinned FTPS
      connection opens with its pinned fingerprint in its field, and a save pins what is typed
      there (`TrustOrRolloverAsync`), keeping the dialog open if the pin is refused. Listing, Test
      and Delete are the panel's, as in 1.x: the panel's "Connections" entry and the footer's Test
      are gone, and the panel deletes at the version it listed. Settings' "Create a … connection",
      under a provider's defaults, opens it on that provider. Fetch from host and Reject beside
      the fingerprint came with 2.2. Was: back to the dialog shape. Type and Provider / protocol
      drop-downs in a fixed header with the provider's description under them; General,
      Authentication and TLS / SSH Trust tabs; each description under its control rather than under
      its label; a red asterisk on required fields; icon and colour swatches; the badge preview; the
      "Loaded version N" status in the footer. Proxy, speed limits and FTP Advanced go into the tabs.
- [x] L.5 Settings pages (ref 02). Done: 1.4's rail and order, with Shortcuts, Connections & trust
      (host-key discovery and its caveat; each provider's new-connection defaults under a `STORAGE`
      or `CLIENTS` caption, SSH Terminal carrying the terminal preferences, and "Create a … connection")
      and Background agent (sign-in or only while open; Windows only, as in 1.4). Performance and
      Confirmations are back in "Transfers & sync", with the total speed limits under `SPEED LIMITS`;
      favourites-in-folders is back under Appearance (applied, P.5.8), with Language
      in a card of its own (P.5.1), and the new-workspace preset under Workspace (P.5.4). The shortcuts are
      edited and saved here, and dispatched and shown in the menus with P.3.4.
- [x] L.6 Settings rows (ref 02). Done: one card per caption with hairlines between rows, units inside
      number fields with 1.4's stacked arrows and thousands separators, 1.4's text limits, rows dimmed
      rather than hidden when they do not apply, and Apply / Cancel / OK. Left open: 1.4 drew the
      arrows in a column outside the field's border, and 2.0 draws them inside.
- [x] L.7 The connections sidebar (ref 09). Done for the cards and the details panel: coloured
      icon tiles, the provider / folder / health line, tag chips, a selected card with inline edit
      and delete, and a details panel with Open, Test, Edit and Delete. Kept from 2.0: groups made
      by hand rather than 1.x's fixed Storage and Clients sections (c4886f7). Favorites is back
      above them (with P.3.5): the enabled favourites by name, drawn as a group with a star and no
      menu, nothing dropped into it, and a favourite still in its own group unless Settings' "Also
      list favorites under their own folder" is off, as 1.4 did; both copies select together. Was: coloured rounded icon tiles in place of the
      `STORAGE`/`CLIENT` text badges; tag chips under each card; a Favorites section above the
      storage groups; inline edit and delete on the selected card.
- [x] L.12 Row icons in the Welcome and Sync tasks tables (ref 01, 03), as 1.4 drew them: a muted info
      glyph on an empty table's line (a green check on "Nothing needs attention", a muted "…" on
      Sync tasks'), and connection, warning, enabled or run glyphs on real rows. A cell template on
      the Name column keyed on `IsPlaceholder` covers the empty lines.
      Welcome's three tables are done (with P.3.1). Sync tasks' two are done too: a green tick on
      an enabled task, a muted pause on a disabled one, a play glyph on a run, and a muted "…" on
      either table's empty line. In all five, a name too long for its column ends in "…" beside
      its icon, as 1.4's did, rather than being cut off mid-letter.

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
      `RejectAsync`); trust-on-first-use for SFTP and FTPS stops being refused. The editor already
      loads the pinned fingerprint and pins it on save (L.4).
      Done in the editor, which is as far as 1.4 went: 1.4's "Fetch from host…" (an SFTP or SSH
      host key) and "Reject…" (any fingerprint) beside the fingerprint. Fetching asks the agent
      what the endpoint presents and shows the algorithm and SHA-256 in 1.4's "Verify SSH host
      key" question, defaulting to No; a yes puts it in the field, to be pinned on save as a typed
      one is, and nothing is trusted until then. Reject asks 1.4's "Reject server identity",
      records that fingerprint as rejected for the saved connection and clears the field; on one
      not saved yet it says to save first. Settings' host-key discovery is read again: opening the
      TLS / SSH Trust tab on an SFTP or SSH endpoint with no fingerprint fetches its key, asking
      first ("Ask before fetching", the default) or not ("Fetch automatically"), once per endpoint,
      and "Manual — use Fetch from host" leaves it to the button, as 1.4's SettingsTabSelected did.
      Left open, past 1.4: a host key or certificate refused while browsing or testing is still
      only a failure, with no Trust / Reject offered where it happens, and the first connection to
      an SFTP or FTPS server still needs its fingerprint pinned first.
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
- [x] 4.8 Hide a pane's FILES row from Pane actions, as the connection bar can be (1.4 could not).
      Done: "Show files bar" sits under "Show connection bar", is saved with the pane as
      `filesBarHidden`, appended so 1.4 still opens the file, and puts the `*` on the tab when it
      changes; it is dimmed on a terminal, which has no row. The list's right-click menu offers all
      the row and its "..." do, Invert selection added, and dims each entry where the row's button
      is dimmed, as 1.4's Opening did, so a Copy with nothing selected is no longer offered.

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
