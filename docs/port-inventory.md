# What StorageHub 1.x does, and where the port has got to

Taken from the source rather than from memory: the command catalog, every screen in
`src/StorageHub.Desktop.WinForms`, and the settings pages the old dialog declares. It exists so
"are we done?" has an answer that is not somebody's recollection.

**The point is parity of behaviour, not of implementation.** A row here is done when the Avalonia
shell does what the old one did, on both platforms, with the logic in `Desktop.Core` where both can
reach it. Several rows are deliberately *not* going to be reproduced; those say so.

---

## The one number that matters

The catalog declares **62 commands**. `UiCommandCatalog.IsAvailable` says **37 of them are wired**
in 1.x. The other **25 have always been inert** — they are drawn, and they do nothing:

> `ViewDirectoryTree` · `ViewTransferQueue` · `ViewSessionLog` · `ViewHiddenFiles` · `ViewTheme` ·
> `GoHome` · `GoHistory` · `GoFavorites` · `ConnectionsQuickConnect` · `ConnectionsReconnect` ·
> `ConnectionsDisconnect` · `ConnectionsTestConnection` · `TransferStartQueue` ·
> `TransferPauseAll` · `TransferResumeAll` · `TransferCancelSelected` · `TransferSpeedLimits` ·
> `SyncComparePanes` · `ToolsSearch` · `ToolsChecksums` · `ToolsLogs` · `ToolsDiagnostics` ·
> `HelpKeyboardShortcuts` · `HelpDocumentation` · `HelpReportIssue`

So 1:1 means **37**, and the menu can keep showing the other 25 exactly as it always has. Worth
knowing before promising any of them: several sound like core features (Hidden files, Directory
tree, Transfer queue toggle, Cancel selected) and are not.

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
| Status bar: agent state, selection, rate, queue depth | **partial** — agent and queue are live, selection and rate are not |
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
| Sort by any column, filter as you type | **todo** — `PagedListingIndex` is in Core and tested |
| Directory tree beside the listing | **todo** — the pane draws one; the `View` command that toggles it is inert |
| Association icons per file type | **todo** — needs `IFileIconProvider`; `WindowsShellIconProvider` is the Windows half |
| Hidden files toggle | **dropped** — inert in 1.x |
| Paging a large listing, with prefetch | **partial** — the controller pages; the pane does not ask for more yet |
| Select all, invert selection | **todo** — commands exist, pane needs them |
| An SSH connection opens a terminal instead of a listing | **partial** — the pane becomes one, hides its listing chrome and is refused as a transfer endpoint; the VT painter is the piece left |

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
| Sidebar: grouped by folder, favourites, per-row menu, search | **partial** — lists and searches on live data; grouping, favourites and the row menu are not there |
| Connection Manager: create, edit, delete, test, 1,784 lines of provider fields | **todo** — the largest screen left after the pane |
| Connection picker in a pane's header | **done** as a plain list; 1.x groups it and filters as you type |
| Per-connection icon and accent colour | **partial** — resolved and drawn; no picker |
| Key store: import, list, delete SSH keys and certificates | **todo** |

### Sync

| What 1.x does | Status |
|---|---|
| Sync profiles: create, edit, preview a run | **todo** — screen is stand-in |
| Schedules: create, edit, enable, delete | **todo** |
| Run history and review, dispatch an approved revision | **todo** — screen is stand-in |
| Compare panes | **dropped** — inert in 1.x |
| Sync tasks overview: enabled, disabled, runs this session | **partial** — the screen is drawn on stand-in data |

### Files

| What 1.x does | Status |
|---|---|
| New folder, new empty file | **todo** — `PaneItemNameRules` is in Core |
| Rename, batch rename | **todo** |
| Delete, with a review dialog listing what goes | **todo** |
| Properties: versions, metadata, tags (Object Inspector) | **todo** |
| Open in an external editor, with an unsafe-edit warning | **todo** — `ExternalEditorController` is still in the shell; it takes `IWin32Window` five times and calls `MessageBox.Show` eight, all of which the dialog vocabulary now answers |

### Workspaces

| What 1.x does | Status |
|---|---|
| Two panes, split, swap, move, close, layout presets | **partial** — one to four panes in all six presets, drawn from `WorkspaceLayoutModel`, splitters write their ratio back; swap and drag-to-dock are not wired to a gesture yet |
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

## The order worth doing the rest in

1. ~~**"This PC"**, so a pane can be a local folder.~~ Done.
2. ~~**Panes and presets**, one to four.~~ Done. What is left of workspaces is saving and opening a
   `.shw`, reconnecting its panes, and the drag gestures for split and swap.
3. **Sort, filter, select-all and invert** in the pane, over `PagedListingIndex`.
4. **File operations** — new, rename, delete, properties — which also lands the dialog screens.
5. **Connection Manager**, the largest screen left.
6. **The terminal painter**, which is what an SSH pane is still missing. `VtTerminalEmulator` and
   `VtKeyEncoder` are in Core with three suites; what is not written is the `Control` that renders a
   screen buffer into a `DrawingContext` and implements `ILogicalScrollable`.
7. **Sync**: profiles, schedules, run review.
8. The rest of Settings, and the update window.
