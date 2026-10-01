# What StorageHub 1.x does, and where the port has got to

Taken from the source rather than from memory: the command catalog, every screen in
`src/StorageHub.Desktop.WinForms`, and the settings pages the old dialog declares. It exists so
"are we done?" has an answer that is not somebody's recollection.

**The point is parity of behaviour, not of implementation.** A row here is done when the Avalonia
shell does what the old one did, on both platforms, with the logic in `Desktop.Core` where both can
reach it. Several rows are deliberately *not* going to be reproduced; those say so.

---

## The one number that matters

The catalog declares **62 commands**. 1.x's `MainForm.IsAvailableCommand` admitted **37**; the
other **25 were never wired** in 1.x -- declared, and left out of its menu and its toolbar, which
is why 1.x had no Transfer menu at all:

> `ViewDirectoryTree` · `ViewTransferQueue` · `ViewSessionLog` · `ViewHiddenFiles` · `ViewTheme` ·
> `GoHome` · `GoHistory` · `GoFavorites` · `ConnectionsQuickConnect` · `ConnectionsReconnect` ·
> `ConnectionsDisconnect` · `ConnectionsTestConnection` · `TransferStartQueue` ·
> `TransferPauseAll` · `TransferResumeAll` · `TransferCancelSelected` · `TransferSpeedLimits` ·
> `SyncComparePanes` · `ToolsSearch` · `ToolsChecksums` · `ToolsLogs` · `ToolsDiagnostics` ·
> `HelpKeyboardShortcuts` · `HelpDocumentation` · `HelpReportIssue`

So 1:1 means **37**. `UiCommandCatalog.IsAvailable` admits those 37 and one more,
`TransferSpeedLimits`, which 2.0 wires to the total speed limits on Settings' Transfers & sync page
now that there are limits to set (roadmap 1.2); it brings back a Transfer menu holding that one
entry. The other **24** stay out of the menu and the toolbar, as 1.x kept all 25 out.

**All 38 have a handler.** A command is offered when it has a handler and dims when it does not,
so the count of enabled menu entries is the count of handlers. `CommandAvailabilityTests` asserts
the two are equal, which made the menu the port's progress meter while the screens landed; on a
workspace it now reads 38 of 38. What was left after that was behaviour inside screens that
exist, which the rows below name, and is done.

Worth knowing before promising any of the 24: several sound like core features -- Hidden files,
the Directory tree toggle, Transfer queue toggle, Cancel selected -- and are not.

---

## Where the work happens, and where the old app is

2.0 is built on the **`2.0` branch**. `main` stays on 1.4 until the port is finished, and is then
replaced by it -- so nothing that is half-ported is ever what `main` says StorageHub is, and CI's
release jobs, which fire on a push to `main`, stay pointed at something shippable.

| Branch | Commit | What it is |
|---|---|---|
| **`2.0`** | working branch | Where 2.0 is built. The Avalonia shell, no WinForms. |
| `main` | `2db3820` | 1.4, and still what the remote's `main` is. Replaced by `2.0` when the port is done. |
| `1.x` | `2db3820` | The 1.4 product, kept under its own name so `main` can move without losing it. |
| `winforms-reference` | `522514f` | The last tree holding both shells, with the WinForms screens beside the `Desktop.Core` they were extracted into. For when the extracted form is the one worth reading. |

### The old app, on disk

`C:\Projects\StorageHubOld` is a git worktree of `1.x` -- the whole 1.4 tree as real files, with no
2.0 code in it. Read and grep it directly:

```
grep -n "ISyncManagementAgentClient" C:/Projects/StorageHubOld/src/StorageHub.Desktop.WinForms/*.cs
```

A worktree rather than a copy, so it shares this repository's object store, cannot drift from the
branch, and shows up in `git worktree list` instead of being a directory somebody has to remember
the meaning of. It is a checkout, not a second clone: committing to `1.x` is done there, and
nothing here needs to know.

For the post-extraction form of a screen, `winforms-reference` is one command away and needs no
checkout:

```
git show winforms-reference:src/StorageHub.Desktop.WinForms/SyncProfileEditorForm.cs
```

### What is still there to port

No screen. The roughly 4,000 lines this table listed as having no counterpart were ported from
2026-09-20 to 2026-09-22, along with the smaller dialogs -- batch rename, the icon picker, the sync
location picker -- and the splash followed with the parity sweep (P.1.1). Where each went, from
`winforms-reference`:

| Lines | 1.x file | Where it is now |
|---:|---|---|
| 622 | `SshTerminalForm.cs` | the pane's terminal: `TerminalView`, over `SshTerminalSession` in Core |
| 532 | `SettingsImportForm.cs` | `SettingsImportWindow`, 1.x's three steps in one window |
| 487 | `ConnectionPicker.cs` | `ConnectionPickerView`, with the grouping and keys in Core as `ConnectionPickerSession` |
| 483 | `ExternalEditorController.cs` | `ExternalEditController` in Core, its prompts behind `IExternalEditPrompts` |
| 358 | `SettingsExportForm.cs` | `SettingsExportWindow` |
| 353 | `ToolbarSettingsControl.cs` | Settings' Toolbar page, over `ToolbarEditor` in Core |
| 329 | `ActivityLogControl.cs` | `ActivityLogView`, the queue's Logs tab |
| 254 | `AgentControlForm.cs` | `AgentControlWindow`, over the packaged or systemd lifecycle controller |
| 233 | `UpdateCheckerForm.cs` | `UpdateCheckerWindow` |
| 178 | `AboutForm.cs` | `AboutWindow` |
| 154 | `TransferProgressColumn.cs` | a `ProgressBar` behind the text in `TransferQueueView` |

Nothing is left as **partial** or **todo** below: every row is done or deliberately dropped, and
roadmap sections P (behaviour) and L (look) are ticked through. What still differs from 1.x is
named in its row as a drift kept on purpose, with the reason. Look is the roadmap's section L,
and is named here only where a row's screen still has an item open there.

The desktop is two projects now: `StorageHub.Desktop`, which draws, and `StorageHub.Desktop.Core`,
which does not and is where both platforms' logic lives. The Windows-only integrations -- registry,
COM registration, the named-pipe lifecycle client -- sit in `Desktop.Core/Windows/` behind
`[SupportedOSPlatform("windows")]` rather than in a project of their own.

---

## Status by area

Legend: **done** · **partial** — works, with named gaps · **todo** · **dropped** — deliberately not
reproduced.

Where it stands across the 57 rows below: **54 done, 0 partial, 0 todo, 3 dropped.** Counted from
the rows themselves on 2026-10-01; the count above them had drifted to 49, 4 and 1 while rows
moved to done underneath it. A done row that still differs from 1.x says how and why.

### Shell chrome

| What 1.x does | Status |
|---|---|
| Menu bar: the 37 wired entries under their menus, shortcuts shown | **done** — the same 37, each with the key bound now (P.3.4), and the unwired left out as 1.x left them. Transfer > Speed Limits makes a ninth menu where 1.x had eight (roadmap 1.2). The Workspace menu's Pinned and Recent (P.3.1) and the Go menu's Favorites (P.3.5) are there, as in 1.x |
| Toolbar from `ToolbarLayout`, customisable order and label style | **done** — built from the saved layout with 1.x's three label styles, rebuilt when Settings or an import changes it, and what does not fit goes behind a chevron whose menu runs the same commands, as 1.x's ToolStrip did (P.3.4). Settings has the toolbar editor |
| Workspace tab strip with per-tab icons | **done** |
| Status bar: agent state, selection, rate, queue depth | **done** — location, selection, rate, queue depth and agent state are live; the agent cell opens Agent control and follows a failed call at once; staging, clearing, import, export and what a paste or drop came to show in the first cell, held for eight seconds, 1.x's longest (P.3.6). A refused paste or drop is 1.x's "Transfer queue" warning instead (P.4.14), and so is a failed new file or folder, rename, batch rename, delete or external edit, as 1.x showed them (P.3.6). The update link is the last cell, as 1.x's was (P.1.8). A new file or folder, a rename, a batch rename, a delete and an edited file uploaded are said in the first cell, and the counts and the rate are read while the queue shows its Logs tab, as in 1.x (P.3.6). Kept on purpose: a message stays its eight seconds where 1.x's queue poll wrote over it, and a saved setting waiting on a restart stays until it is applied |
| Connections panel, dockable left or right, collapsible, remembered width | **done** — left or right, shown and hidden from the View menu, the toolbar and Ctrl+B, and its side, width and visibility remembered, within 1.x's limits (P.3.3) |
| Shortcut dispatch that beats focus, and declines inside a text box or SSH | **done** — `ShellCommandRouter`, tunnelling, sharing `UiCommandCatalog.CanDispatch` |
| Splash while the agent starts | **done** — `SplashWindow` over `DesktopBoot`, with 1.x's stages from preparing data to starting the agent, which it starts and waits for on both platforms, and a failure screen with Retry, Quit, Check installation and Copy details (P.1.1, P.1.2, P.5.6) |
| Light and dark, following the system | **done**, and now 22 schemes |

### Browsing

| What 1.x does | Status |
|---|---|
| Browse a saved connection: list, enter, back, forward, up | **done** |
| "This PC": local drives and folders in a pane | **done** — one `IPaneSource` for both, so a disk and a bucket differ in one object |
| Address bar, typed and focusable (`Go > Focus address`) | **done** — Enter goes to what is typed, a bad path says why, and Ctrl+L puts the cursor there with all of it selected (P.2.6) |
| Sort by any column, filter as you type | **done** — through `PagedListingIndex`, so a bucket and a disk come out in the same order |
| Directory tree beside the listing | **done** — `PaneTreeModel`, built from what the pane lists, as 1.x's was (P.2.9). The `View` command that would toggle it stays unwired, as in 1.x |
| Association icons per file type | **done** — shell icons per extension on Windows, drive icons on This PC, and folder and file glyphs as the fallback and on Linux (P.2.4) |
| Hidden files toggle | **dropped** — inert in 1.x |
| Paging a large listing, with prefetch | **done** — the next page loads as the list scrolls, with "Load more" and "more available" in the footer (P.2.1); a paste or drop into a folder over a page reads every page first (P.2.2), and a quiet re-read brings the rest back on scroll (P.2.12) |
| Select all, invert selection | **done** — buttons in the pane, and the menu entries now reach the active one |
| An SSH connection opens a terminal instead of a listing | **done** — the pane becomes one, and `TerminalView` paints the emulator's screen, follows the live end only while it is there, scrolls the history through the pane's own scroll bar, selects by drag, double and triple click, copies with Ctrl+Shift+C and pastes on a right-click, with the SSH terminal settings read as each session opens (P.5.2). A remote program that asks for the mouse (DECSET 1000, 1002, 1003, in the SGR form 1006) is sent its clicks, drags, moves and wheel, and the pane stops selecting and scrolling for it, Shift overriding, as 1.x's `TerminalView` did (P.5.9). 1.x never delivered them: its view raised `MouseInputProduced` and nothing subscribed, so its clicks went nowhere; 2.0 sends them. One drift on purpose: 1.x still pasted on a right-click while a program had the mouse, because its window pasted on every right button up, which would now reach the program as a click and paste as well; 2.0 gives the right-click to the program, and Shift+right-click pastes |

### Transfers

| What 1.x does | Status |
|---|---|
| Copy and move between the two panes | **done**, including the folder-recursion path |
| Queue: active, queued, paused, failed, completed, conflicts | **done** |
| Cancel, retry, reconcile, with revision checks | **done** |
| Progress column drawn as a bar behind the text | **done** — a bar behind the percentage, hidden rather than collapsed while the size is unknown, and full in the success colour once a transfer of unknown size completes, as 1.x drew it (P.4.12) |
| Drag and drop between panes | **done** — a press on a selected row that moves becomes a drag; it lands as the same transfer a paste is, copy by default and move with Shift where the rows can be moved, and leaves what is staged alone. The list or the tree being dragged over is drawn in the selection colour for every drop it would take, a drag from another pane included, as 1.x drew it (P.2.11). One drift: on Windows a connection's rows can only be copied, as in 1.x, because the drag also carries the drop broker's marker and Explorer would move that for real |
| Drag out to Explorer, and drop in from it | **done** — This PC's rows drag out as their own paths, a plain file drop. A connection's rows drag out to Explorer through the drop broker registered at launch (P.1.7): the drag carries an empty marker folder, the copy hook reports where it landed, and the agent downloads the selection there, with the drag on the Active tab as "Waiting for destination" until then (P.4.6). Files dropped in from Explorer, Nautilus or Dolphin on a connection go through the agent's plan, folders included, and ask Replace, Skip or Cancel when some are already there, OK or Cancel when none are, as 1.x did; dropped on a folder in the tree, they go there. On Linux a connection's rows cannot be dragged out: there is no broker, and a file manager takes nothing that could stand in for a file not yet downloaded, so the pane says so and Copy to a This PC pane is the way. Dropped on a This PC folder, which 1.x refused, files are queued as a paste from This PC would be (P.2.11) |
| Clear transfer history, with a confirmation | **done** — the queue's right-click menu has Clear selected, Cancel and clear and Clear all history, and "Warn before clearing" asks first (P.4.2) |
| Activity log tab | **done** — the queue's Logs tab, with the rules in Core as `ActivityLog` and `ActivityLogReader`; a folder being read for a transfer is listed there too (P.4.6) |
| Speed limits, pause all, resume all, cancel selected, start queue | **dropped** — all five inert in 1.x. Speed Limits is the one 2.0 wires, to the total limits on Settings' Transfers & sync page, since there are limits to set now (roadmap 1.2) |

### Connections

| What 1.x does | Status |
|---|---|
| Sidebar: grouped by folder, favourites, per-row menu, search | **done** — groups are there and are better than 1.x: made by hand, reordered by dragging, remembered, and seeded from each connection's folder path so an upgrade keeps its organisation. Each card is drawn as 1.x's were, with its coloured icon tile, the provider, folder and health line and its tag chips (L.7). Favorites is back above the groups, a card's right-click menu has 1.x's Open, Open in new pane, Toggle favorite, Edit and Delete (P.2.15, P.3.5), and the search box filters the cards. A collapsed group stays collapsed across listings and searches, for the session, as 1.x kept it (P.3.5) |
| Connection Manager: create, edit, delete, test, 1,784 lines of provider fields | **done** — 1.x's plain Edit Connection dialog for create and edit, with listing, testing and deleting on the connections panel as in 1.x. The fields are not written out: they come from `ConnectionProviderCatalog` and the draft from `ConnectionEditorDraftFactory`, which is what most of those 1,784 lines were doing by hand. A secret field is a read-only reference with Enroll, Delete and, for the two material fields, Key Store beside it, as in 1.x. A pinned host key or certificate is loaded into its field and pinned on save, as in 1.x. Beside the fingerprint are 1.x's Fetch from host (an SFTP or SSH host key), which shows what the endpoint presents for it to be accepted into the field or not, and Reject, which records the fingerprint as rejected and clears it; opening the Trust tab fetches or asks to, as Settings' host key discovery says, as 1.x did (roadmap 2.2) |
| Connection picker in a pane's header | **done** — a button over a picker grouped under This PC, favourites, folders and providers, filtered as it is typed into, with the open connection marked, as 1.x's was; the keyboard rules are in Core as `ConnectionPickerSession` |
| Per-connection icon and accent colour | **done** — chosen in the Edit Connection dialog from 1.x's icon picker and colour swatches (L.4), and a group's icon from the same picker on a right-click on its heading |
| Key store: import, list, delete SSH keys and certificates | **done** — `KeyStoreController` in Core, and three windows: the store, one import dialog where 1.x chained three prompts, and the picker the Connection Manager opens. Proven against the live agent and the lab's SFTP server: a key imported in the store, borrowed by a connection, opens the server. Running it found that a deleted connection kept its key bound for good; the agent now releases bindings on delete. Unlike 1.x, a key with no passphrase is stored, with a warning (roadmap 1.10) |

### Sync

| What 1.x does | Status |
|---|---|
| Sync profiles: create, edit, preview a run | **done** — the fourteen fields, the nine behaviours as a list that says which of them delete things, and a preview that hands its run to the review screen instead of embedding a second copy of it. Why a draft will not save is now said one field at a time: `SyncProfileDraftRules` names the field, and the contract still has the last word so the two can disagree without the screen offering to save something the agent will refuse. The two Browse buttons are back, over `SyncLocationPickerWindow`, with the browsing in Core as `SyncLocationBrowser`. A preview that warns keeps the editor open to show it, and closing the editor stops a preview still running, as in 1.x (P.4.10) |
| Schedules: create, edit, enable, delete | **done** — a list beside one schedule's settings, with the recurrence chosen rather than written: `ScheduleRecurrence` turns a frequency and a time into the cron the agent stores and reads one back, so the cron box appears only for the expressions no preset covers, and one written by hand survives being opened and saved. Deleting confirms; disabling, which is reversible from the same screen, does not. A refusal because a run is in progress says that rather than "could not be changed" |
| Run history and review, dispatch an approved revision | **done** — history a page at a time, a run's plan and its conflicts, and an approval carrying the revision and digest the reviewer was shown. The checks that make that safe are in `SyncRunReviewController` with a suite of their own; the plan page is refused outright if it does not belong to the plan on screen. Approving confirms first, and defaults to Cancel. A loaded run re-reads itself — closely while the agent is acting on it, occasionally otherwise, not at all once it has settled. An operation names its connection by id rather than by name, as 1.x's did: all thirty-six characters of it, and a connection's top folder `<root>`, as 1.x's review printed them, with a From or To cut off by the column on its tip, as 1.x's grid showed it (P.4.15) |
| Compare panes | **dropped** — inert in 1.x |
| Sync tasks overview: enabled, disabled, runs this session | **done** — profiles and recent runs from the agent, read each time the tab comes forward (P.4.7), with the run-to-profile names resolved rather than left as ids. Saved tasks are listed the most recently updated first, and up to 1,000 runs are read, listed and counted, as in 1.x (P.4.13). The first sub-tab reads "Tasks" (L.11), and each row carries 1.x's glyph (L.12) |

### Files

| What 1.x does | Status |
|---|---|
| New folder, new empty file | **done** — one `PaneMutationController` for both a bucket and a disk, and the name box refuses what the storage would |
| Rename, batch rename | **done** — one item, including a case-only rename on this computer, and several at once in `BatchRenameWindow`, which previews the new names first, with the rules in Core as `BatchRenamePlan` |
| Delete, with a review dialog listing what goes | **done** — 1.x's review listing up to six items with "don't show again", skipped when "Warn before deleting" is off, and it says how far it got if it stops part way. Local items go to the Recycle Bin on Windows and the desktop Trash through `gio trash` on Linux, deleted outright only where there is no gio (P.2.10) |
| Properties: versions, metadata, tags (Object Inspector) | **done** — `ObjectInspectorWindow` over the controller Core already had. Opened from the pane's toolbar or Edit > Properties for one file on a saved connection; each of the three sections keeps its own failure |
| Open in an external editor, with an unsafe-edit warning | **done** — `ExternalEditController` in Core behind `IExternalEditPrompts` and `IEditorLauncher`, with a private session folder per platform; opened from the pane's Edit and its right-click menu, with the warning as a window of its own and the editor chosen on Settings' External editing page |

### Workspaces

| What 1.x does | Status |
|---|---|
| Two panes, split, swap, move, close, layout presets | **done** — one to four panes, all six presets in a New Workspace chooser, and split / close / swap / move behind each pane's own actions menu, which also hides the connection bar, as 1.x's did, and the FILES row, which 1.x could not (roadmap 4.8). A pane dragged by its header swaps with the one it is dropped in the middle of, or docks at the edge it is dropped on (P.2.14) |
| Stage a selection, then paste it into another pane | **done** — the rule that survives four panes, and what 1.x did with two |
| Save and open a `.shw` workspace file | **done** — Save, Save As, Open and Rename read and write 1.x's `.shw`, with each pane's sort and filter and one member of 2.0's own, `filesBarHidden`, which 1.x skips and drops if it saves the file again (roadmap 4.8), and a `*` on the tab while it differs from its file; closing asks about each changed one (P.3.1, P.1.9) |
| Pinned and recent workspaces on the Welcome screen | **done** — Welcome lists the pinned and then the recent workspaces with Open, Pin/Unpin, Remove and Copy path, and the Workspace menu has them too (P.3.1) |
| Reconnect remote panes on open | **done** — a pane in an opened `.shw` connects again and goes back to its folder, as 1.x's `RestoreStateAsync` did, waiting for the connection list first; with "Reconnect remote panes automatically" off it is chosen but not opened, and a click on its banner connects. A connection no longer saved leaves that pane on Connections Home saying so (P.3.1) |

### Settings and lifecycle

| What 1.x does | Status |
|---|---|
| Settings: appearance, workspace, performance, confirmations, updates | **done** |
| Settings: shortcuts editor | **done** — the Shortcuts page, and the shell dispatches and shows what it saves (L.5, P.3.4) |
| Settings: toolbar editor | **done** |
| Settings: connection defaults | **done** — each provider's new-connection defaults under a STORAGE or CLIENTS caption, and a new connection starts from them (L.5, P.5.3) |
| Settings: external editing, trust, agent mode | **done** — agent mode is Windows only, as in 1.x (L.5) |
| Export and import settings, section by section | **done** — `SettingsExportWindow` and `SettingsImportWindow` over the Core services, choosing sections from `SettingsSectionCatalog`, with 1.x's three import steps in one window. An import that changes the concurrency waits for running transfers before the agent restarts (P.3.7), and the bar says what was imported or exported (P.3.6) |
| Background agent control: start, stop, status | **done** — `AgentControlWindow` with Start, Stop, Restart and Check installation, over the packaged agent on Windows and the systemd user unit on Linux, opened from Tools and the status bar's agent cell (P.3.6). The installation check is 1.x's less the service checks (P.5.6), and a dropped agent is started again and the panes reloaded, as in 1.x (P.1.3) |
| Check for updates, download, install | **done** — `UpdateCheckerWindow` checks the GitHub releases, downloads the MSI or the .deb, holds it to the release's SHA256SUMS, and installs it through msiexec or pkexec once the shell has closed. The check on start, closing the shell when an update is to install, and the update link in the status bar are 1.x's (P.1.8) |
| Install, upgrade and uninstall: sign-in autostart, the agent stopped first, the Run entry and the drop broker removed | **done** — a plain per-user WiX MSI (`eng/installer`), whose custom actions run 1.x's hooks in `DesktopPackageLifecycleHooks` through `--package-hook`; Velopack is gone from the build, the packaging and the code (P.1.5) |
| About | **done** — `AboutWindow`, from Help |

---

## What is deliberately not coming back

- **The 24 commands 1.x never wired.** They stay out of the menu and the toolbar, as 1.x kept
  them out. Its 25th, Speed Limits, is wired now that there are limits to set (roadmap 1.2).
- **Windows service mode.** One per-user agent on both platforms; the whole mode, its ACLs and the
  trust model are gone.
- **Velopack.** MSI and `.deb`, with StorageHub's own updater.
- **The DPI machinery.** `DisplayMetrics`, `LogicalToDeviceUnits`, `FitSettingsPageContent`,
  `MeasureNavigationWidth` and the 761 conversion call sites. Avalonia lays out in
  device-independent pixels; a number written once is right at every scaling.
- **The theme walker.** 250 lines of recursive re-tinting, replaced by a brush pointing at a colour.
- **The owner-drawn control set.** `StorageHubButton`, `StorageHubTextField`, `StorageHubToggle`,
  `UiTheme`, `UiIcons` and the rest — **5,334 lines** of painting, replaced by styles over stock
  controls and Lucide.

---

## Running the desktop against a live agent

`eng/run-dev-agent.ps1` builds the agent from this working tree and starts it. The desktop needs
nothing configured: it finds the agent by a pipe named from the current account's SID, so an agent
running as you is one it can reach.

Start the agent first. On Windows a desktop built from source has no packaged agent beside it to
start, so its splash stops on the agent being missing, with Retry, rather than launching one; an
agent started by hand is used as it is, and left running when that desktop closes
(`DesktopAgentStartup`, P.1.12).

By default the agent uses its normal data root, `%PROGRAMDATA%\StorageHub` -- the connections made
are the ones you will have. `-DataRoot` keeps a separate database instead.

If the agent exits with "The StorageHub data directory could not be protected for the current
user", that root belongs to another account. It is what a pre-2.0 installation leaves behind:
StorageHub used to run its agent as a machine-wide service under LocalSystem, which listened on
`StorageHub.Agent.v1.machine` and hardened the data root to itself. The desktop no longer looks for
that pipe and the agent cannot take that directory, so both halves have to go:
`eng/remove-legacy-agent-service.ps1`, elevated. It deletes the old database and vault, and there
is no migration -- the old vault is protected with the machine's DPAPI key and the new one with
yours, so the entries could not be read across the move even if the files were kept.

`STORAGEHUB_LIVE_AGENT=1` turns on the suites that need a running agent: `LiveAgentTests`
(something answers) and `LiveConnectionTests` (a connection is made, listed, browsed and removed).
They are skipped otherwise, so CI stays green without one.

## The order worth doing the rest in

1. ~~**"This PC"**, so a pane can be a local folder.~~ Done.
2. ~~**Panes and presets**, one to four.~~ Done, including the New Workspace chooser and the
   per-pane actions menu, and since then saving and opening a `.shw`, reconnecting its panes, and
   dragging a pane header to dock or swap (P.3.1, P.2.14).
3. ~~**Sort, filter, select-all and invert** in the pane.~~ Done. The rows are still copied out of
   the index rather than bound to it, so a very large listing is held twice; collecting that back
   needs `IndexedView` to be an `IList` before a TableView will read it by index.
4. ~~**File operations** — new folder, new file, rename, delete.~~ Done, through one
   `PaneMutationController` in Core. Properties is done too: the Object Inspector, opened from
   the pane's toolbar or from Edit, over the controller Core already had. Batch rename is done
   too, in a window of its own.
5. ~~**Connection Manager**.~~ Done for listing, editing, creating and deleting, for
   enrolling and borrowing secrets now that the key store is in, and for fetching a host key
   from the host and rejecting one from the editor (roadmap 2.2).
6. ~~**The terminal painter**.~~ Done: `TerminalView` renders the screen buffer, drives the pane's
   scroll bar through `ILogicalScrollable` in lines, and selects and copies. A program that asks
   for the mouse is sent it, with Shift keeping it local, as 1.x's view was built to; 1.x itself
   never sent it (P.5.9).
7. ~~**Sync**: profiles, schedules, run review.~~ Done, and proven against the live agent and the
   lab's servers. The run review names a connection by its whole id and the top folder `<root>`,
   as 1.x's did (P.4.15).
8. ~~**The rest of Settings, and the update window.**~~ Done: every Settings page, export and
   import, Agent control and the update window.

The order from here is `docs/roadmap.md`'s, behaviour before look. Of the rows above, nothing is
left: the installers (P.1.5), the update check on start and the status bar's update link (P.1.8),
fetching and rejecting a host key in the editor (2.2), a pane's news said in the status bar and a
failed file operation shown in the "Transfer queue" warning (P.3.6), and the connections panel
keeping a collapsed group (P.3.5) are all done. What follows is the roadmap's numbered sections,
which go past 1.x.
