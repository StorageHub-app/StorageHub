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
release candidate; only a pushed tag publishes a stable release. How a number is
chosen is written down in [Versioning and merges](docs/versioning.md).

---

## Unreleased

**StorageHub can tell you what is wrong with itself.** When the background
agent does not come up, the only symptom is the desktop saying it did not
become ready, which points at the wrong thing: the state that decides it is
spread over a per-user data root, a machine data root, a staged binary tree, a
service registration and a named pipe. Check installation looks at all five and
says which one is at fault, in a sentence rather than a status code.

It also answers the frightening question directly. Switching between the session
and service modes moves the data root, so it reports a populated database left
behind in the mode you are not using: the connections are not gone, they are out
of reach until that mode is selected again.

Where it can fix something it offers to, and says when that needs an
administrator: creating a missing data directory, starting a stopped service,
and telling Windows to restart the agent if it stops unexpectedly. That last one
was simply never configured, so a single crash left the machine with no agent
until somebody noticed and started it by hand. Looking changes nothing; only a
repair you ask for by name does.

It is offered from the background agent screen, and from the startup failure
itself -- when the agent does not come up the main window never opens, so every
other route to it is behind a door that will not open.

## 1.4.4 — 2026-09-19

**The shell is the same shape at every display scaling.** The fonts scaled and
the layout did not, so the two drifted apart by exactly the scaling factor. That
is why StorageHub, which was built at 125%, came apart at 100% and looked
squeezed back at 125%: the numbers had been tuned by eye against text that was
already a quarter larger than the boxes holding it.

Every form set `AutoScaleMode.Dpi` but none ever assigned
`AutoScaleDimensions`, and WinForms initializes that to the *current* DPI, which
makes the scale factor exactly 1 and the automatic pass a no-op. Nothing was
scaling the layout at all. Meanwhile a 9pt font resolves its points against the
real display, so text grew 25% inside boxes that did not. Around 550 literal
pixels across 44 files are now logical units converted at the point of use, which
is the rule `docs/architecture.md` has stated all along and the custom controls
already followed.

The most visible of them: the Settings content column was a fixed 720 pixels, so
a description that fit on one line at 100% wrapped onto two at 125%; the pane
header reserved a fixed 220 pixels for a connection name that had grown; and
every toolbar resampled its icons back down — the glyphs were rasterised at the
right size for the display and then squashed into a `ImageScalingSize` nobody had
scaled, which is why they looked soft and misshapen. Sixteen more icons were
never rasterised for the display in the first place, because the scale argument
defaulted to 1 and was easy to forget; it is derived from the control the icon
belongs to now, so it cannot be.

**A box that holds text is measured from that text.** Converting a pixel to a
logical unit only makes it track the display; it still does not track the font,
and the two disagree whenever a translation runs long or the system font
changes. Seventy-five boxes across twenty-seven files now take their height from
the line they contain plus named breathing room, which is arithmetically the
same at 125% -- the scaling this was all designed at -- and correct everywhere
else. Doing it turned up boxes that were already too small for their own text
before any of this: an 18px band around a 20px bold line in the connection
detail pane, and a 30px band around a 14pt title in the object inspector. Five
more pixel literals had never been converted at all. Every data grid
kept its column header at a flat 23px and its rows at 22, neither of which moves
with the font, so at 150% a header clipped its own titles along the bottom edge;
they measure their contents now.

Two things were being measured before there was anything to measure. Icons were
rasterised against the *system* dpi rather than the display the window is
actually on, so on a mixed-scaling desktop every glyph was drawn for the primary
monitor wherever the window went; they are redrawn now when a control learns
where it is and again if it moves. The sync task band was measured in a
constructor, before WinForms had rescaled the fonts, and its split was assigned
to a SplitContainer that is 150px tall until it is laid out -- so the run
history card was left showing a heading above a half-drawn column header.

**The desktop can actually reach a service-hosted agent.** It never could, which
means the Windows service was unusable even once it started: the connection that
decides whether the agent is there was opened current-user-only, and a service's
pipe belongs to LocalSystem. Windows refuses that connection outright, so the
desktop reported "the background agent did not become ready in time" while a
perfectly healthy service sat there answering everyone else. Two clients were
built by hand and neither named the access mode, so both silently took the
current-user default that is right for a session agent and wrong for a service —
the startup and shutdown client, and every SSH terminal. Naming it is now
mandatory rather than defaulted, so the next client cannot quietly get it wrong.

**Signing in no longer beats the service to it.** With the agent hosted as a
Windows service, StorageHub asked once whether the agent was reachable and gave
up if it was not — a race it lost at almost every sign-in, because Windows
starts the auto-start service alongside the session that starts StorageHub, and
the service answers the service control manager immediately and then spends
several seconds opening its database before its pipe exists. The result was
"the StorageHub background agent did not become ready in time" on a boot where
nothing was wrong and the service was running perfectly. It waits now, the way
it already waited for an agent it started itself. Nothing was waiting for this
before because the service could never start at all.

**The Windows service can start.** It never could. The elevated install copied
the agent into `%ProgramData%\StorageHub\bin`, inside the very data root the
service resolves, and the agent refuses to run with its data root and its
application directory overlapping — so every start exited immediately and
Windows reported only "the StorageHub Agent service terminated unexpectedly".
The binaries are staged beside the data root now, in `%ProgramData%\StorageHubAgent\bin`,
and the copy left in the old location is removed when the service is
re-registered. The layout is checked against the guard the agent actually
enforces at startup, which is what nobody was doing: both halves had tests, and
neither test put the two real paths together.

**Switching host modes no longer arrives with an empty installation.** The
database runs in write-ahead logging mode, so everything written since the last
checkpoint lives in the `-wal` sidecar rather than in the `.db` file — and the
migration copied the `.db` file alone. The result opened cleanly, passed its
integrity check, contained no connections, keys, schedules or history, and
reported that it had copied the database. It is copied through SQLite's online
backup now, which reads through the log and writes one consistent, fully
checkpointed file.

**Switching back brings the installation with it.** Only the way out was ever
migrated. Choosing a session mode again removed the service and left everything
done under it in `%ProgramData%`, silently presenting whatever stale copy the
per-user location still held. Migration is one job in two directions now: the
service is stopped, the installation is copied home and its secrets re-protected
with your key, and only then is the registration removed. Anything already at
the destination is moved into a dated folder beside it rather than overwritten,
and the source is never modified, so a switch stays reversible. Switching back
also restores the logon entry again, which it had stopped doing — "when I sign
in" quietly became "only while StorageHub is open".

**The service's data stays readable by administrators.** It was locked to
LocalSystem alone on first start. That buys no secrecy — the vault is sealed
with the machine key, which any administrator can use, and StorageHub says so
before installing the service — while it did block the elevated switch back,
which runs as the signed-in user and has to read what it is bringing home.

**Choosing the service can no longer leave the machine with no agent at all.**
The registration survived a failed start, and a session agent refuses to run
beside an installed service, so a service that could not start took the whole
application down with it — the desktop opened, and nothing worked. A service
this install created is now removed again if it will not start, so declining or
failing the switch leaves you exactly where you were: running in your own
session. A service that already existed is still left alone.

---

## 1.4.3 — 2026-09-19

The transfer queue stopped keeping things to itself.

**A folder being copied into now fills as it copies.** Nothing reloaded a pane
while transfers ran, or when they finished, so a copy of three thousand files
left the destination showing what was there before it started until somebody
pressed refresh. The panes are re-read every five seconds while work is
outstanding and once more when it drains. A small copy used to miss even that:
the reload waited to catch the queue busy on a poll, and a handful of files can
be queued and finished between two of them, so it is armed when the transfers are
accepted instead.

**A pane no longer keeps an error the agent has recovered from.** "The background
agent is not available" stayed on screen after the agent came back, because every
other surface reloads itself on recovery and the panes were missed when that was
built.

**Conflicts can be cleared.** Applying the reconciliation action to a conflicted
transfer reported success and changed nothing visible: the action list defaults to
Review, the default only moved off it for one of the two states the conflicts tab
shows, and Review on the other simply moved the transfer to the first — still a
conflict, still on that tab. The default now moves for anything reconcilable, and
the right-click menu offers to cancel a conflict and clear it in one step, since
only a settled transfer can be cleared and nothing said so.

**Dragging inside StorageHub no longer reports a cancellation.** A drag left a
second queue row reading "Cancelled: dropped onto a StorageHub pane" — a marker
for a drag out to Explorer that was retired as cancelled when the drop landed on
a pane instead. It says a gesture failed when it succeeded, so it is discarded
rather than settled.

---

## 1.4.2 — 2026-09-19

Dropping a folder used to look like nothing had happened.

A folder dropped on a pane was read to the end before anything reached the
transfer queue: the whole tree walked, every destination folder created, and only
then the files queued in one go. Over SFTP on a large folder that is minutes with
nothing on screen to say the drop was even accepted. The queue now carries a row
for the reading itself from the moment the drop lands, counting what it has
found, and files appear beneath it as they are found. Cancelling that row stops
the walk; what it already queued stays queued.

A drop is no longer all or nothing, which is the trade for showing it. A problem
found on the last page used to mean nothing had been queued; now the files before
it are queued and may already be moving, and the result says how many got
through.

**Reading a folder got faster.** The storage library re-listed and re-sorted an
entire directory on every page of a listing, so paging ten thousand entries meant
walking the tree two hundred and fifty times — over SFTP, where recursive listing
is emulated by walking it, behind a fresh connection each time. It now pages from
one cached listing per pass and reuses its session. StorageHub was rebuilding the
whole connection for every call as well, down to decrypting the credentials and
registering the connection again, which meant the library saw a different
connection on each page and could reuse nothing. A profile's connection is now
shared and kept for half a minute after its last use.

**A failed transfer says what failed.** "StorageHub could not build the recursive
transfer manifest" covered six unrelated causes, and the underlying error was
discarded rather than logged, so a report of it could not be followed up either.
A listing that runs out of time now says which folder it stalled in and on which
page, and listings get a budget of their own rather than sharing the fifteen
seconds meant for a status call.

---

## 1.4.1 — 2026-09-19

The connection sidebar, tidied.

A folder’s cards stopped short of the pane whenever the list was long enough to
scroll, because the scrollbar was subtracted from their width twice — once by
Windows and once by StorageHub. Expanding a folder moved everything left;
collapsing it put it back.

Folder titles were buttons, so each one drew a filled, outlined box inside the
group’s own frame and centred its label. They are a line of text now, with a
background only under the pointer, and the frame around a group is a hairline
rather than an outline.

A row’s edit and delete icons were measured in raw pixels while everything
beside them grew with the display scaling, so at 125% they were small, thin and
pressed against the card’s edge.

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
