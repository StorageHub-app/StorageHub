# Changelog

What changed in each release, in the order it happened. Dates are when the work
landed on `main`; a version is cut by tagging the commit that carries it, so a
release contains everything since the previous tag.

StorageHub's git history was rewritten when the project moved to its own
organization, and the releases published before that move were withdrawn. This
file was reconstructed from the development history that preceded it, so the
entries before 1.4.0 describe work whose commits are no longer public. They are
grouped by what changed rather than listed commit by commit.

Versions follow `MAJOR.MINOR.PATCH`. Every push to `main` also publishes a
release candidate; only a pushed tag publishes a stable release.

---

## 1.4.0 — 2026-09-18

The release where the shell stopped looking like a WinForms application, and
where the desktop learned to say one sensible thing when the agent goes away.

**The shell draws its own controls.** Text fields, dropdowns, number steppers,
switches, checkboxes and buttons are painted by StorageHub rather than by
Windows, from one set of metrics, so a field is the same height and sits on the
same baseline in Settings, in a dialog, and on a toolbar, at any display scale.
Settings pages are composed of captioned cards of labelled rows instead of
positioned by hand, and the toolbar's contents can be rearranged and are stored
as command ids that survive a language change.

**Three ways to run the background agent**, chosen on first run and changeable in
Settings: only while StorageHub is open, from sign-in to sign-out, or as a
Windows service that starts with the computer and runs with nobody signed in.
The service states its consequences before it is installed — it moves the
database to `%ProgramData%` and re-protects secrets with the machine key — and
copies your data rather than moving it.

**One answer when the agent goes away.** A failed call is classified in one
place, which says so in StorageHub's own words rather than printing "Pipe is
broken", marks the state reconnecting, probes once for a burst of failures, and
tells every surface to reload when the agent answers again. The status bar can
no longer read "connected" while the window says otherwise.

**Fixes worth naming.** Dropdown menus opened in the system's colours rather
than the application's, because the runtime was never told its colour mode when
the appearance was left on System — which also left an active pane's title
painted in near-black. Several windows, the New Workspace chooser among them,
were drawn in the system palette entirely, and the clear-history confirmation
managed white text on white. Every rounded control on a settings card had a dark
block outside each corner, where it filled the gap with the page's colour
instead of the card's. A Danish or German shell started with New Workspace, Open
Workspace and Exit disabled. The settings page headed "Transfers & sync" was
painted "Transfers  sync". The transfer queue's reconciliation setting never
applied, and three provider editors quietly stopped responding after a control
change.

**Documentation.** The project moved to its own organization, so the updater,
the issue templates and the build metadata point at its new home. The security
model, development status and roadmap pages are gone: three documents making
claims in more detail than the repository could be checked against. What
survived moved to where it can be verified, and this changelog was written.

---

## 1.3.0 — 2026-09-18

**The SSH terminal became a terminal.** A table-driven VT parser with the
alternate screen, so `htop`, `vim`, `less` and `nano` draw correctly and leave
your scrollback intact on exit. Resizing reflows rather than truncating, and
tells the remote its new size. Colour, cursor addressing, box drawing, and the
mouse forwarded to programs that ask for it, with Shift to select locally.

**Output stopped being lost.** The agent holds a session's output until the
terminal confirms it arrived, so a cancelled or failed read replays instead of
discarding bytes, and a dropped chunk is reported rather than silently missing.

**Keys that were being dropped** — page up and down, the function keys, Alt
combinations, Insert — are encoded and sent. `Ctrl+C` always interrupts;
`Ctrl+Shift+C` and `Ctrl+Shift+V` copy and paste.

**Panes lost a row of chrome.** The connection bar is one line instead of three,
with no Manage button duplicating what four other surfaces already offer, and it
can be hidden per pane and is remembered in the saved workspace.

---

## 1.2.2 — 2026-09-18

Stopped the shell freezing and blinking while a window was resized. Dragging an
edge repainted far more than it needed to, on a thread that had better things to
do.

## 1.2.1 — 2026-09-17

The folder tree got its own icons, and opening the connection manager from a
pane now opens it on the connection that pane is showing rather than on the
first one in the list.

## 1.2.0 — 2026-09-17

Release plumbing, mostly: a tab strip stopped being identified by its translated
name — which worked in English and nowhere else — and the publish job was given
room for a slow upload day.

---

## 1.1.0 — 2026-09-17

The release that made StorageHub usable in more than one language and left
nothing looking unfinished.

**Danish and German throughout**, including the menus, the error messages, the
validation text and the screen-reader labels. A language picker in Settings, and
a restart offered so the change reaches every window at once.

**A key and certificate store.** Certificates and SSH keys live in one place
instead of being pasted into profiles, so rotating one updates every profile
that uses it. Key material that is not protected is refused with a message that
says why.

**Settings, connections and sync tasks export and import**, optionally password
protected, with a backup written before an import changes anything.

**Saved connections moved into a panel** beside the workspaces, grouped and
searchable, with favorites and a detail view. The pane's connection drop-down
became a searchable picker.

**The background agent is visible**: its state, its active work, and start, stop
and restart in the application rather than in Task Manager. The manual update
check became a real window instead of a chain of message boxes.

**Dark mode finished.** Native scrollbars, list headers, spin buttons and edit
borders follow the theme, and every command has an icon. Windows repaint
themselves when the appearance changes rather than waiting to be reopened.

Pinned and recent workspaces are remembered. The pane chooser offers every
arrangement rather than a pane count, and can remember the answer.

---

## 1.0.0 — 2026-09-11

The first stable release. What the engineering preview had been building toward,
with the gaps closed and the release process able to publish it.

**Workspaces** of one to four equal-capability panes in six layouts, saved,
pinned, and named after their file, with history, filtering and bounded paging
so a folder of a hundred thousand objects does not freeze the window.

**File management in every pane**: create, rename, batch rename, delete, and
copy or move between any two panes, local or remote, with Explorer drag and
drop.

**Configurable keyboard shortcuts** and an active pane that file commands
actually act on.

**Durable transfers.** Any-to-any copy and move with source preconditions,
optional SHA-256 verification, and deletion only after a verified commit. Jobs
are queued in SQLite with fenced claims, checkpoints and retries, and execute in
the background agent — closing the desktop does not discard accepted work.

**Synchronization** with three-way classification, conflict categories and
deletion guards, producing immutable plans. Preview is read-only; applying
requires an explicit approval bound to the plan. Cron schedules honour time
zones and DST.

**Providers**: local disks and UNC paths, Amazon S3 and S3-compatible stores,
FTP, explicit and implicit FTPS, and SFTP — each with a profile model, credential
and trust mapping, capability conformance tests and a hermetic integration test.
Credentials live in a Windows DPAPI vault and never enter profile files, logs or
diagnostics. No certificate or host key is accepted on first contact.

**Packaging**: a per-user installer and MSI, a portable archive, checksums,
provenance attestation, and an integrity-checked updater that can be told to
check, download, or do neither.

---

## 0.1.0 — engineering preview, 2026-08-02 to 2026-09-04

The foundations, built in the order safety depended on them: contracts and
domain types, the provider-neutral storage boundary, the DPAPI vault and trust
store, the SQLite database with ordered migrations and a single-writer gate, the
transfer state machine and its durable queue, the sync engine, the scheduler
with fenced leases, and the current-user named-pipe protocol the desktop talks
to the agent over.

Alongside them, the things that make those foundations checkable: hermetic MinIO,
FTP/FTPS and SFTP fixtures pinned by hash, a shared conformance suite run against
each, and a CI pipeline that builds, tests, audits dependencies, packages, and
installs and uninstalls the result on a disposable runner before publishing
anything.

Preview builds were never offered as releases, and the databases they wrote are
migrated rather than read as-is.
