# What StorageHub 1.x does, and where the port has got to

Taken from the source rather than from memory: the command catalog, every screen in
`src/StorageHub.Desktop.WinForms`, and the settings pages the old dialog declares. It exists so
"are we done?" has an answer that is not somebody's recollection.

**The point is parity of behaviour, not of implementation.** A row here is done when the Avalonia
shell does what the old one did, on both platforms, with the logic in `Desktop.Core` where both can
reach it. Several rows are deliberately *not* going to be reproduced; those say so.

---

## The one number that matters

The catalog declares **62 commands**. `UiCommandCatalog.IsAvailable` admits **37**; the other
**25 have always been inert** in 1.x too -- drawn, and doing nothing:

> `ViewDirectoryTree` · `ViewTransferQueue` · `ViewSessionLog` · `ViewHiddenFiles` · `ViewTheme` ·
> `GoHome` · `GoHistory` · `GoFavorites` · `ConnectionsQuickConnect` · `ConnectionsReconnect` ·
> `ConnectionsDisconnect` · `ConnectionsTestConnection` · `TransferStartQueue` ·
> `TransferPauseAll` · `TransferResumeAll` · `TransferCancelSelected` · `TransferSpeedLimits` ·
> `SyncComparePanes` · `ToolsSearch` · `ToolsChecksums` · `ToolsLogs` · `ToolsDiagnostics` ·
> `HelpKeyboardShortcuts` · `HelpDocumentation` · `HelpReportIssue`

So 1:1 means **37**, and the menu keeps showing the other 25 unavailable, exactly as it always has.

**Of those 37, this shell handles 22.** That is the real number, and until recently this document
quoted 1.x's in its place. It is now visible in the product rather than only here: a command is
offered when it has a handler and dims when it does not, so the count of enabled menu entries is
the count of handlers. `CommandAvailabilityTests` asserts the two are equal, which makes the menu
the port's progress meter.

Worth knowing before promising any of the 25: several sound like core features -- Hidden files,
Directory tree, Transfer queue toggle, Cancel selected -- and are not.

---

## Where the work happens, and where the old app is

2.0 is built on the **`2.0` branch**. `main` stays on 1.4 until the port is finished, and is then
replaced by it -- so nothing that is half-ported is ever what `main` says StorageHub is, and CI's
release jobs, which fire on a push to `main`, stay pointed at something shippable.

| Branch | Commit | What it is |
|---|---|---|
| **`2.0`** | working branch | Where 2.0 is built. The Avalonia shell, no WinForms. |
| `main` | `2db3820` | 1.4, and what the remote still has. Replaced by `2.0` when the port is done. |
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

Roughly 3,400 lines of screens have no counterpart here yet. In descending order, from
`winforms-reference`:

| Lines | File | Block |
|---:|---|---|
| 622 | `SshTerminalForm.cs` | SSH |
| 532 | `SettingsImportForm.cs` | settings |
| 487 | `ConnectionPicker.cs` | grouped, filter-as-you-type |
| 483 | `ExternalEditorController.cs` | mostly portable once its prompt is behind an interface |
| 358 | `SettingsExportForm.cs` | settings |
| 353 | `ToolbarSettingsControl.cs` | settings |
| 329 | `ActivityLogControl.cs` | queue |
| 254 | `AgentControlForm.cs` | lifecycle |
| 233 | `UpdateCheckerForm.cs` | lifecycle |
| 178 | `AboutForm.cs` | dialog |
| 154 | `TransferProgressColumn.cs` | a bar behind the text |

Plus the smaller dialogs and the trapped types listed under the plan's step 4.

The desktop is two projects now: `StorageHub.Desktop`, which draws, and `StorageHub.Desktop.Core`,
which does not and is where both platforms' logic lives. The Windows-only integrations -- registry,
COM registration, the named-pipe lifecycle client -- sit in `Desktop.Core/Windows/` behind
`[SupportedOSPlatform("windows")]` rather than in a project of their own.

---

## Status by area

Legend: **done** · **partial** — works, with named gaps · **todo** · **dropped** — deliberately not
reproduced.

Where it stands across the 59 rows below: **11 done, 13 partial, 31 todo, 4 dropped.** The partials
are the honest ones — each names what is missing rather than claiming the row.

### Shell chrome

| What 1.x does | Status |
|---|---|
| Menu bar, nine menus, all 62 entries, shortcuts shown | **done** |
| Toolbar from `ToolbarLayout`, customisable order and label style | **partial** — renders the default preset; the toolbar editor is a screen (below) |
| Workspace tab strip with per-tab icons | **done** |
| Status bar: agent state, selection, rate, queue depth | **partial** — location, selection, queue depth and agent state are live. The rate is not shown at all: the transfer contract carries no throughput, so the cell could only ever say "0 B/s" |
| Connections panel, dockable left or right, collapsible, remembered width | **partial** — drawn on the left; the two `View` commands that move and hide it are not wired |
| Shortcut dispatch that beats focus, and declines inside a text box or SSH | **done** — `ShellCommandRouter`, tunnelling, sharing `UiCommandCatalog.CanDispatch` |
| Splash while the agent starts | **todo** |
| Light and dark, following the system | **done**, and now 22 schemes |

### Browsing

| What 1.x does | Status |
|---|---|
| Browse a saved connection: list, enter, back, forward, up | **done** |
| "This PC": local drives and folders in a pane | **done** — one `IPaneSource` for both, so a disk and a bucket differ in one object |
| Address bar, typed and focusable (`Go > Focus address`) | **partial** — shows the path, not yet editable |
| Sort by any column, filter as you type | **done** — through `PagedListingIndex`, so a bucket and a disk come out in the same order |
| Directory tree beside the listing | **todo** — the pane draws one; the `View` command that toggles it is inert |
| Association icons per file type | **todo** — needs `IFileIconProvider`; `WindowsShellIconProvider` is the Windows half |
| Hidden files toggle | **dropped** — inert in 1.x |
| Paging a large listing, with prefetch | **partial** — the controller pages; the pane does not ask for more yet |
| Select all, invert selection | **done** — buttons in the pane, and the menu entries now reach the active one |
| An SSH connection opens a terminal instead of a listing | **done** — the pane becomes one, and `TerminalView` paints the emulator's screen, follows the live end only while it is there, scrolls the history through the pane's own scroll bar, selects by drag, double and triple click, and copies with Ctrl+Shift+C. Mouse reporting to the remote program is not ported |

### Transfers

| What 1.x does | Status |
|---|---|
| Copy and move between the two panes | **done**, including the folder-recursion path |
| Queue: active, queued, paused, failed, completed, conflicts | **done** |
| Cancel, retry, reconcile, with revision checks | **done** |
| Progress column drawn as a bar behind the text | **todo** — `TransferProgressColumn` |
| Drag and drop between panes | **todo** |
| Drag out to Explorer, and drop in from it | **partial** — the broker is in `Desktop.Windows`; the shell end is Windows-only by nature |
| Clear transfer history, with a confirmation | **todo** |
| Activity log tab | **todo** — the tab is there and says so |
| Speed limits, pause all, resume all, cancel selected, start queue | **dropped** — all five inert in 1.x |

### Connections

| What 1.x does | Status |
|---|---|
| Sidebar: grouped by folder, favourites, per-row menu, search | **partial** — groups are there and are better than 1.x: made by hand, reordered by dragging, remembered, and seeded from each connection's folder path so an upgrade keeps its organisation. Every row wears a STORAGE or CLIENT badge, which is what became of the fixed Storage/Clients split. Favourites and the per-row menu are not there, and the search box does not filter yet |
| Connection Manager: create, edit, delete, test, 1,784 lines of provider fields | **done** — list, editor, save, test and delete. The fields are not written out: they come from `ConnectionProviderCatalog` and the draft from `ConnectionEditorDraftFactory`, which is what most of those 1,784 lines were doing by hand. A secret field is a read-only reference with Enroll, Delete and, for the two material fields, Key Store beside it, as in 1.x. Host-key trust (fetch from host, reject) is the one part of the editor not here |
| Connection picker in a pane's header | **done** as a plain list; 1.x groups it and filters as you type |
| Per-connection icon and accent colour | **partial** — resolved and drawn; no picker |
| Key store: import, list, delete SSH keys and certificates | **done** — `KeyStoreController` in Core, and three windows: the store, one import dialog where 1.x chained three prompts, and the picker the Connection Manager opens. Proven against the live agent and the lab's SFTP server: a key imported in the store, borrowed by a connection, opens the server. Running it found that a deleted connection kept its key bound for good; the agent now releases bindings on delete |

### Sync

| What 1.x does | Status |
|---|---|
| Sync profiles: create, edit, preview a run | **done** — the fourteen fields, the nine behaviours as a list that says which of them delete things, and a preview that hands its run to the review screen instead of embedding a second copy of it. Why a draft will not save is now said one field at a time: `SyncProfileDraftRules` names the field, and the contract still has the last word so the two can disagree without the screen offering to save something the agent will refuse. Not carried over: the two Browse buttons, which opened `SyncLocationPickerForm` — that screen is not ported, and the roots are typed |
| Schedules: create, edit, enable, delete | **done** — a list beside one schedule's settings, with the recurrence chosen rather than written: `ScheduleRecurrence` turns a frequency and a time into the cron the agent stores and reads one back, so the cron box appears only for the expressions no preset covers, and one written by hand survives being opened and saved. Deleting confirms; disabling, which is reversible from the same screen, does not. A refusal because a run is in progress says that rather than "could not be changed" |
| Run history and review, dispatch an approved revision | **done** — history a page at a time, a run's plan and its conflicts, and an approval carrying the revision and digest the reviewer was shown. The checks that make that safe are in `SyncRunReviewController` with a suite of their own; the plan page is refused outright if it does not belong to the plan on screen. Approving confirms first, and defaults to Cancel. A loaded run re-reads itself — closely while the agent is acting on it, occasionally otherwise, not at all once it has settled. An operation still names its connection by id rather than by name, which needs the connection client this screen does not hold |
| Compare panes | **dropped** — inert in 1.x |
| Sync tasks overview: enabled, disabled, runs this session | **done** — profiles and recent runs from the agent, with the run-to-profile names resolved rather than left as ids |

### Files

| What 1.x does | Status |
|---|---|
| New folder, new empty file | **done** — one `PaneMutationController` for both a bucket and a disk, and the name box refuses what the storage would |
| Rename, batch rename | **partial** — renaming one item is done, including a case-only rename on this computer. Batch rename is a screen of its own and is not built |
| Delete, with a review dialog listing what goes | **done** — it confirms and says how far it got if it stops part way. It deletes rather than recycling: 1.x used a Windows-only Recycle Bin API with no cross-platform equivalent, so the confirmation says so |
| Properties: versions, metadata, tags (Object Inspector) | **done** — `ObjectInspectorWindow` over the controller Core already had. Opened from the pane's toolbar or Edit > Properties for one file on a saved connection; each of the three sections keeps its own failure |
| Open in an external editor, with an unsafe-edit warning | **todo** — `ExternalEditorController` is still in the shell; it takes `IWin32Window` five times and calls `MessageBox.Show` eight, all of which the dialog vocabulary now answers |

### Workspaces

| What 1.x does | Status |
|---|---|
| Two panes, split, swap, move, close, layout presets | **done** — one to four panes, all six presets in a New Workspace chooser, and split / close / swap / move behind each pane's own actions menu. Dragging a pane header to dock is the one gesture still missing |
| Stage a selection, then paste it into another pane | **done** — the rule that survives four panes, and what 1.x did with two |
| Save and open a `.shw` workspace file | **todo** — `WorkspaceModel` and the file store are in Core, tested |
| Pinned and recent workspaces on the Welcome screen | **partial** — the card is drawn, the data is not wired |
| Reconnect remote panes on open | **todo** |

### Settings and lifecycle

| What 1.x does | Status |
|---|---|
| Settings: appearance, workspace, performance, confirmations, updates | **done** |
| Settings: shortcuts editor | **todo** |
| Settings: toolbar editor | **todo** |
| Settings: connection defaults | **todo** |
| Settings: external editing, trust, agent mode | **todo** |
| Export and import settings, section by section | **todo** — the model and the mapper are in Core, tested |
| Background agent control: start, stop, status | **todo** |
| Check for updates, download, install | **partial** — the engine and the presentation are in Core; the window is not ported |
| About | **todo** |

---

## What is deliberately not coming back

- **The 25 inert commands.** They stay in the menu, unavailable, exactly as in 1.x.
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
   per-pane actions menu. What is left of workspaces is saving and opening a `.shw`, reconnecting
   its panes, and dragging a pane header to dock or swap.
3. ~~**Sort, filter, select-all and invert** in the pane.~~ Done. The rows are still copied out of
   the index rather than bound to it, so a very large listing is held twice; collecting that back
   needs `IndexedView` to be an `IList` before a TableView will read it by index.
4. ~~**File operations** — new folder, new file, rename, delete.~~ Done, through one
   `PaneMutationController` in Core. Properties is done too: the Object Inspector, opened from
   the pane's toolbar or from Edit, over the controller Core already had. Batch rename is what
   remains of this group.
5. ~~**Connection Manager**.~~ Done for listing, editing, creating and deleting, and for
   enrolling and borrowing secrets now that the key store is in. Host-key trust from the editor
   is what remains.
6. ~~**The terminal painter**.~~ Done: `TerminalView` renders the screen buffer, drives the pane's
   scroll bar through `ILogicalScrollable` in lines, and selects and copies. Mouse reporting to
   the remote program is what remains of the SSH pane.
7. ~~**Sync**: profiles, schedules, run review.~~ Done, and proven against the live agent and the
   lab's servers.
8. The rest of Settings, and the update window.
