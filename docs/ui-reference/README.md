# The WinForms shell, as it looked

Screenshots of `StorageHub.Desktop.WinForms` running, captured during the Avalonia port to serve as
the target the new shell is measured against.

They exist because "keep the same look" is not a property the compiler or the test suite can check.
The palette has a test, the metrics have a test, and the scaling has a test — but whether a screen
*reads* like the app it replaces is settled by looking. These are what to look at.

Each entry lists what the screen obliges the port to reproduce. Where a detail exists for a reason
that is not obvious from the picture, the reason is written down, because that is the part a
screenshot cannot carry.

## 01 — Welcome / overview (`01-welcome-overview.png`)

The first thing anyone sees. Title and subtitle, four actions (New workspace primary, Connections,
Sync tasks, Refresh), then four metric cards — Agent, Active transfers, Queued, Needs attention —
each with an icon, a large value and a caption, and each with a coloured border keyed to what it
reports. Below: a **Workspaces** section over a Name/Location/State table, then **Recent
connections** and **Needs attention** side by side.

Empty states are written out, not blank: "No workspaces yet", "Save a workspace to pin it here",
"No saved connections yet", "Nothing needs attention".

Source: `OverviewDashboardControl`.

## 02 — Settings (`02-settings-transfers-sync.png`)

A tree on the left, a page on the right, and the shape the whole settings surface follows.

The tree mixes flat entries (Transfers & sync, Editing, Appearance, Workspace, Shortcuts) with an
expandable node (Connections & trust) whose children are grouped under captions — `STORAGE` over
Local/UNC, S3, FTP, FTPS, SFTP, and `CLIENTS` over SSH Terminal — then more flat entries below
(Toolbar, Background agent, Updates). The selected row carries a left accent bar.

The page is a title, a subtitle, then capitalised section captions (`CONCURRENCY`, `CONFIRMATIONS`)
over cards. Every row is the same shape: a bold label and a muted description on the left, the
control hard right — a toggle, or a number field with its unit inside it ("1 jobs") and a spinner.

Apply / Cancel / OK sit bottom right.

This page is why the port keeps settings **data-driven**: the rows come from
`ConnectionFieldDescriptor`s and `SettingsSectionCatalog`, so hand-writing them as markup would be a
regression. `FitSettingsPageContent` and `MeasureNavigationWidth` are manual-layout compensation and
are deleted rather than ported.

Source: `SettingsForm`, `SettingsPagePanel`/`Card`/`Row`/`Caption`.

## 03 — Sync tasks (`03-sync-tasks.png`)

A workspace tab with **sub-tabs of its own** — Tasks | Run history and review — which the Avalonia
shell must nest inside a workspace tab rather than flatten.

Title, subtitle, four actions (New sync profile primary, Schedules, Run history and review,
Refresh), three metric cards (Enabled tasks, Disabled tasks, Runs this session), then two captioned
sections with icons — **Saved sync tasks** over Name/Behavior/State/Updated, and **Last syncs** over
Name/State/Updated — and a footer line: "Updated 12:26. Showing 0 durable run(s)."

Source: `SyncTasksOverviewControl`.

## 04 — Sync run history and review (`04-sync-run-history.png`)

The densest screen. A Run ID field with Load run (primary), Refresh history and a disabled Next page;
a right-aligned status in green. Below, a split: run history on the left
(Updated/Run/Phase/Dispatch/Conflicts), and on the right an empty-state heading with its own two
buttons (Refresh status, Approve & dispatch), its own Plan | Conflicts tabs, a table of
#/Action/From location/To location/Expected bytes/Approval, and "Load next operations" bottom right.

Note the disabled buttons. Several controls here are deliberately unavailable until a run is loaded,
and that is state the view model has to carry — it is not decoration.

Source: `SyncRunsControl`, `SyncRunReviewControl`.

## 05 — A workspace with panes (`05-workspace-panes.png`)

The actual file manager, and the screen the port leaves for last.

The tab reads "Workspace 1 \*" with a close button — the asterisk marks unsaved changes. Under the
tab strip is a drag-and-drop hint row: "Drag pane headers to swap or dock panes | Empty | Paste to
active pane | Clear".

Each pane has a header ("Pane 1 (Active)", with "Pane actions" on the right), then a connection chip
carrying badges (`STORAGE`, `LOCAL`) and a status line ("● Ready"), then a `FILES` command row (New
folder, Copy, Move, Paste, Delete, overflow), then navigation (back / forward / up, a path box,
refresh, and a Filter box), then a tree beside a list with columns Name↑ / Size / Type / Modified /
Status, and an item count in the footer.

The active pane is bordered in the accent colour. That border is the only thing saying which pane a
command will act on, so it is load-bearing rather than decorative.

Source: `BrowserPaneControl` (3,644 lines), `WorkspaceControl`.

## 06 — New workspace dialog (`06-new-workspace-dialog.png`)

Six layout presets as cards in a 3×2 grid, each with a small diagram of the split and a two-line
label: 1 pane Single; 2 panes Side by side; 2 panes Top and bottom; 3 panes Large left, two stacked;
3 panes Large top, two beside; 4 panes 2×2 grid. A checkbox underneath: "Use this for new workspaces
and stop asking".

The diagrams are drawn, not images, so they follow the palette.

## 07 — Connection picker (`07-connection-picker.png`)

The popup a pane opens to choose what it is showing. A search box, a caption (`ON THIS DEVICE`), then
rows carrying a selection dot, a name, a muted subtitle and badges — `SYSTEM · LOCAL` on both, and a
green `ACTIVE` on the one already open. The selected row is filled with the accent.

This is `StorageHubChoiceField`'s job in the old shell — roughly 900 lines with its own item
collection, popup, keyboard navigation and accessible object. It becomes a `ComboBox` with a
`ControlTheme`, which is the single largest deletion in the port and improves accessibility rather
than costing it.

## 08 — Connection editor (`08-connection-editor.png`)

The dialog behind every saved connection, and a third row shape again.

A fixed header first: **Type** and **Provider / protocol** as two drop-downs, with a line under them
describing whatever provider is chosen ("Local disks, mapped drives, and Windows network shares").
Changing the provider rebuilds everything below it. Then tabs — General, Authentication,
TLS / SSH Trust — and inside, a section heading with its own hint (**Endpoint**, `C:\Data or \\server\share`).

The rows differ from the settings page: the label sits in a left column, the control fills the rest,
and the muted description runs *underneath the control* rather than beside the label. A required
field marks itself with a red asterisk after the label.

Two rows are not fields at all. **Icon and colour** pairs a "Choose icon..." button with a grid of
colour swatches, and **Connection badge** is a live preview — `LOCAL · provider color #4C8BF5` —
showing how the connection will read in the sidebar and in a pane header.

The footer carries a status on the left ("Loaded version 1", in green) against Cancel and Save
profile on the right. That version is optimistic-concurrency state, not decoration: saving a profile
someone else has since changed has to be refused rather than silently win.

Source: `ConnectionManagerForm`, `ConnectionDetailView`/`Rows`, `ConnectionAppearanceField`. The
field descriptors it builds rows from are `ConnectionEditorDraftFactory`, already in Desktop.Core.

## 09 — The connections sidebar, in full (`09-connections-sidebar.png`)

What the sidebar looks like with connections in it, which is considerably more than a list.

Connections are **grouped twice over**. A section header with an icon and a rule (`★ Favorites`,
`☁ Storage`) holds collapsible groups, each with its own header carrying a chevron, a name and a
count — `▾ Favorites · 2`, `▾ Cloud · 1`, `▾ Servers · 1`, `▾ Studio · 2`. The same connection
appears under both Favorites and its storage group, so the list is a projection, not a partition.

A card is a coloured rounded icon tile, a name, a muted line combining provider, folder and health
("Local / UNC · Studio · Not tested"), and tag chips underneath. The selected card takes an accent
border and reveals inline edit and delete icons on its right.

The detail pane along the bottom is a real panel, not a caption. It carries a provider badge and the
connection's name, then collapsible sections of key/value rows — Server over Provider, Bucket,
Region, Service, Path; Authentication below it — and a row of actions: Open, Test, Edit, and Delete
styled as the destructive one.

That grouped, variable-height projection is the risk the plan flagged: in the WinForms shell the
grouping is computed *during paint*, by `MeasureConnectionItem` returning a different height for the
first item in a group and `IsFirstConnectionInGroup`/`ConnectionGroupSortKey` deciding membership as
each row is drawn. The DataTemplate is the easy half; the grouping is a data-model problem wearing a
paint problem's clothes.

Source: `ConnectionSidebarControl` (983 lines), `ConnectionsPanelControl`, `ConnectionCardFactory`,
`ConnectionDetailView`.

