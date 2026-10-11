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

---

## 2.0.0 — 2026-10-11

The first stable StorageHub 2.0, released after a run of release candidates
(`2.0.0-rc.1` by hand, then one for every push to `main`). It replaces 1.4.5 as
the release marked Latest, and the 1.4 source is kept on the `1.x-archive`
branch.

**Duplicate a connection.** A connection's menu and its selected card have a
Duplicate action: it saves a copy named "(copy)", then "(copy 2)" and on, with the same
settings, the same saved passwords and keys and the same pinned host key, so it
signs in without anything being typed again. The copy is not a favourite, and it
is selected when it appears.

**Key store audit.** PEM and PKCS#8 SSH keys can now be used by a connection:
the editor never recorded a key's format, so every profile said OpenSSH and the
connector refused the rest as malformed. The connector now reads the format from
the key itself, a key borrowed from the Key Store brings its format with it, and
the import dialog picks the format from the file. A client certificate with no
password is accepted for FTPS, as it already was for WebDAV. A WebDAV client
certificate is released when its connection stops using it or is deleted, where
it used to keep the Key Store entry undeletable. Enrolling over, or deleting, a
field that holds a Key Store entry no longer rewrites or destroys the shared
entry for every connection using it. Keep-both sync conflicts could overwrite
location B before saving its copy for most file types; the overwrite now runs
last. An Explorer drop committed with an upper-case token no longer expires, a
too-large settings import is refused before it is read, and runtime key files
left by a crashed agent are removed at the next start instead of a day later.

**StorageHub 2.0: a new shell, and one place its data lives.** The desktop is
rewritten on AvaloniaUI and runs on Windows and Linux from the same source. The
WinForms shell is gone -- 35,686 lines of it -- and with it the DPI machinery,
the 250-line theme walker and 5,334 lines of owner-drawn controls, replaced by
styles over stock controls.

MAJOR is the right number because of where data lives, which is what
[Versioning and merges](docs/versioning.md) reserves it for. Every Windows mode
now resolves `%PROGRAMDATA%\StorageHub`, and the agent runs as you rather than
as a machine-wide service. An installation that still has the old service must
remove it: the desktop no longer looks for the pipe it publishes, and the agent
cannot take a data root that LocalSystem owns. `eng/remove-legacy-agent-service.ps1`
does both halves, elevated. There is no migration of the old vault and there
cannot be -- it is protected with the machine's DPAPI key and the new one with
yours, so its entries could not be read across the move even if the files were
kept. A 1.4 installation that ran its agent in your session kept its database
under `%LOCALAPPDATA%\StorageHub`, which 2.0 does not read either; the
installation check points at it, and a 1.4 settings export is refused rather
than imported with its shortcuts bound to the wrong keys. Workspace files are
the same `.shw` 1.4 wrote, and open in both.

**It works as 1.4 did.** Every screen of 1.4 was ported, and then compared with
1.4 code against code and picture against picture until each row of
[the port inventory](docs/port-inventory.md) was done or deliberately dropped:
the Welcome page, workspaces with their folder tree and paging, the transfer
queue, sync tasks, schedules and run review, the connection editor, the key
store, the object inspector, Settings, favourites, shortcuts, the toolbar
layout, the SSH terminal, drag and drop with Explorer, the updater, and Danish
and German. What still differs from 1.4 is named in its row, with the reason.

What is better rather than merely different: the connections panel holds groups
you make and drag between, instead of a fixed Storage/Clients split, with a
badge on each row saying which it is. A workspace holds one to four panes in six
arrangements. Transfers stage and paste, so "the other pane" no longer has to
mean anything. Panes browse this computer through the same object they browse a
bucket with. Twenty-two colour schemes, identical on both platforms.

**Linux.** A `.deb` for Debian and Ubuntu installs into `/opt/storagehub` and
runs the agent per user under `systemd --user`, as the `storagehub-agent` unit,
with no root and no service. Its vault key is a file only your account can
read, which is what DPAPI's current-user scope gives on Windows; the keyring was
passed over because a lingering agent has to read its vault with nobody signed
in. Files dropped in from Nautilus or Dolphin go through the same review as an
Explorer drop. A remote connection's rows cannot be dragged out to a Linux file
manager, which takes only files that already exist.

**Four installers, one checksum file.** Windows ships a plain per-user WiX MSI
in place of Velopack's Setup.exe and portable ZIP: no elevation, the same
install folder as 1.4, a sign-in entry for the agent, and the agent stopped
before an upgrade or removal. Every release carries it for x64 and ARM64 beside
an amd64 and an arm64 `.deb`, each built, installed and smoke-tested by CI on a
runner of its own architecture, all under one `SHA256SUMS` and with GitHub
artifact attestations. They are not code-signed yet. The updater reads the
GitHub releases, picks the package for the machine's own architecture -- so an
x64 copy on an ARM64 PC is offered the ARM64 MSI -- holds it to `SHA256SUMS`
and GitHub's size, and installs it once StorageHub has closed: through
`msiexec` on Windows, and through `apt` under polkit on Linux.

**An SSH key without a passphrase is accepted.** 1.4 refused one. It is kept
encrypted in the vault like any other, the library is handed no passphrase for
it, and importing or choosing one warns that a passphrase is still recommended.

**SFTP reaches more servers.** A connection can go through a jump host, chosen
from your saved SFTP and SSH connections, each with its own sign-in and pinned
host key. A server that only signs in through keyboard-interactive prompts is
answered with the password, as OpenSSH answers it, and one that wants a key and
then the account password takes both. The editor's Advanced section limits the
key exchanges, ciphers, MACs and host key types offered, how many sessions are
opened at once and how often an idle one is kept alive. The private key goes to
the SSH library in memory rather than as a file in the runtime folder.

**Test a connection before saving it.** The editor's Test connection button
tests what is in the fields, saved or not, and lists how far it got: the
settings, the server's identity, the credentials, reaching the server and
listing the starting folder, each passed, failed, not needed or not reached,
with what to check when one fails. A fingerprint typed or fetched into the
editor is trusted for the test, so a new SFTP or FTPS connection can be tested
before it exists, and an SFTP server with no key yet has its key fetched and
shown to be checked first. A test refused on the host key offers the key the
server presents. A pane that fails on a server's key or certificate offers
Review trust there, which opens the connection's trust settings. Nothing is
saved by a test and a saved connection's status is not changed by testing edits
to it.

**See what a connection is talking to.** Live details in the connections panel adds
a Live connection section under the selected connection: how it is secured, the
server's system and software, the host key or certificate it presented and
whether it is trusted, the cipher and key exchange negotiated, the server's
features, how many sessions are open and the last health check.

**Azure Blob Storage, Google Cloud Storage and OpenStack Swift connections.**
An Azure container signs in with a shared key, a SAS token, a connection string
or the computer's own Azure identity. A Google Cloud Storage bucket signs in with
a service-account key file, Application Default Credentials, or anonymously for
a public bucket, and can name a private endpoint or an emulator. A Swift
container signs in with Keystone v3 (user, password, project and optional
region), TempAuth, or a token you already have plus the storage URL, with the
request timeout under Advanced. Every key, password and token is kept in the
encrypted vault, plain HTTP is refused unless you allow it for an emulator or a
test cluster, and the new connections browse, transfer, export and import like
any other.

**Compare two panes and copy the differences, and clean up what interrupted
transfers left behind.** Sync, Compare Panes (Ctrl+D) now opens a Compare
Folders window that lists what only the left or right folder has, what differs
by size, time or, if asked, SHA-256 contents, and what is the same. You can
filter by kind and name, and copy selected differences in either direction as
normal queued transfers that ask the usual conflict question and never overwrite
a file that changed since the comparison. Folders on saved connections and on
this PC can both be compared. A new Tools, Clean Up Leftover Files… lists the
partial and staged copies, write tests and rename stand-ins left in folders
StorageHub transferred to, plus copies of files no longer open in the external
editor, each with its size. It removes only what you select and confirm, and
leaves anything an unfinished transfer, a running sync or an open editor may
still need.

**Free space, connection trouble and folders that refresh themselves.** A
pane's footer ends with the free space behind the folder it shows: for a folder
on this PC from the drive, for SFTP and FTP connections from the server, and
nothing where it is not known. Each connection's card in the connections panel
says when its link to the server is retrying, has been lost or has come back,
and each change is a Connection line in the activity log. A new setting under
Settings, Workspace, "Refresh a folder when it changes", off by default, makes a
pane show changes by itself: a folder on this PC as soon as something in it
changes, a connection's folder on an interval you choose from 15 seconds to 5
minutes. Selection and scroll position stay where they were, and a busy pane
waits until it is done.

**WebDAV connections.** StorageHub now connects to WebDAV servers such as
Nextcloud, ownCloud, SharePoint and NAS boxes. Enter the folder's URL and sign
in with Basic, Digest, NTLM, Negotiate, a bearer token or no sign-in at all. The
server's HTTPS certificate is trusted by the system or pinned the same way as
for FTPS, and a client certificate from the vault can be added. Plain HTTP works
only after you allow it on the Trust tab. A request timeout under Advanced can
be raised for large uploads on slow lines. Reopening a pinned FTPS connection
for editing no longer loses its pin, and imported connections that rely on a
client certificate now arrive disabled when that certificate is not available.

**Properties shows permissions, owner and dates, and can change permissions
and the modified time.** Properties now opens on a folder or a link as well as a
file, with a General tab listing its kind, size, modified, created and
last-accessed times, its permissions as both `rwxr-xr-x` and octal, its owner
and group, and where a link points, each as far as the connection reports it.
On SFTP and FTP connections, and on local folders, "Change permissions…" opens
a dialog with read, write and execute boxes for owner, group and others beside
the octal value, and "Set modified time…" asks for a new time in your own time
zone. Both are dimmed, with a sentence saying why, where the connection cannot
make the change, and a server that refuses one says so in words. The window and
the pane show the new values as soon as the change is made.

**Checksums the server already keeps.** The object inspector has a Checksum
tab: Get checksum shows the file's SHA-256 and says whether the server reported
it (S3, FTP, Azure, Google Cloud Storage and Swift can, without the file being
downloaded) or it was computed from the bytes read, up to 256 MB. Compute from
the file reads it to check what the server says. Sync scans take a server's
SHA-256 instead of reading the file when it has one. A verified copy still
reads back what it wrote, because a server's checksum is its word for the bytes
rather than a check of them.

**A file already at the destination is a choice.** A copy or a move that meets
one asks once, at the first, what to do with it and with the rest: overwrite,
skip, overwrite if the source is newer or if the sizes differ, keep both by
naming the new one "name (1).ext", or resume a shorter file. Settings, under
Transfers & sync, can name the answer instead, and the question can save it
there. A folder copy asks at the first such file however deep it is. Files on
this PC can now be replaced by a download and can replace a file on a
connection, each still checked to be the file that was listed.

**An interrupted transfer continues where it stopped.** A download to this
PC, and an upload to a Local, FTP or SFTP connection, is written to a hidden
`.storagehub-….partial` file and renamed into place only when it is whole, so
the destination never holds half a file. When the connection drops, the agent
restarts or the transfer is paused, the bytes that arrived stay, and the retry
or the next queueing of the same file reads only the rest. The staged part is
used only for the same version of the same source to the same place: a source
that changed since, or staged bytes that no longer match it, start again from
the beginning, and a checksum the transfer was planned with still covers the
whole file. Cancelling a transfer removes what it staged. S3 has no append, so
uploads to it still start again.

**Smaller things that are new.** A pane's FILES bar can be hidden from Pane
actions and is saved with the workspace, now that the list's right-click menu
offers everything the bar does. The SSH terminal passes the mouse to a program
that asks for it, with Shift keeping it for selecting and pasting. The
connection editor fetches an SFTP or SSH server's host key for you to accept
into the fingerprint field, and rejects a fingerprint for a saved connection,
as 1.4 did. A new schedule starts on the time zone Settings names, following
the system by default, and the schedule window says which one is in use; on
Linux a machine on `Etc/UTC` no longer gets `Africa/Abidjan`.

**Every menu entry now does what it says.** 1.4 listed 23 menu commands that
did nothing; 2.0 builds all of them. In View, the folder tree beside a pane,
the transfer queue, the session log and hidden files can each be shown or
hidden, and StorageHub remembers which; Theme switches between light, dark and
following the system. Go > Home takes a pane to the top of its connection or to
your home folder, and Go > History and Go > Favorites list the folders the pane
visited this session and your favourite connections, under the address bar.
Quick Connect finds a saved connection as you type, and Reconnect, Disconnect
and Test Connection act on the pane's connection or the one selected in the
Connections panel. In Transfer, Start Queue (F7) retries everything stopped,
Pause All (F8) pauses the whole queue, a running copy stopping at its next safe
point and keeping what it has so far, Resume All carries on, and Cancel
Selected cancels the rows chosen in the queue. Tools > Search finds files by
name or wildcard under the active pane's folder, on this PC or a connection;
Tools > Checksums shows the SHA-256 of the selected files, the server's own
where it keeps one, compares them with each other or a checksum you paste, and
copies them in `sha256sum` form; Tools > Logs opens the log folder; and Tools >
Diagnostics shows a short report and saves a support bundle with your user
name, computer name and home folder taken out. Help > Keyboard Shortcuts opens
the shortcut settings, Documentation opens the README, and Report Issue opens
the bug form with your version and system filled in. Each entry is greyed out
when there is nothing for it to act on.

**An FTP command console.** Connections, FTP Command Console (also in a pane's
actions menu) opens a console on the active pane's FTP or FTPS server. Type a
command such as SYST, FEAT, STAT or SITE CHMOD 644 file.txt and the server's
reply appears below it, every line of a multi-line reply included, with failure
codes in red; Up and Down bring back earlier commands. Each command runs on a
short connection of its own, so nothing typed there can disturb the panes or the
transfer queue, and for the same reason commands that would open a data
connection, log in or out, change TLS or set something for the session (LIST,
RETR, STOR, USER, PASS, AUTH, CWD, TYPE and the like) are not sent, and the
console says why. The saved password stays in the agent.

**More room for the panes.** The bar above the panes that said "Drag pane headers
to swap or dock panes" beside an empty clipboard is only drawn while something is
staged for a paste; the hint is now the tooltip on a pane's header. The frame
round each pane is two pixels wide instead of three, the active pane still
marked by its colour.

**The connections panel now always shows its groups, even empty ones.** Favorites,
every group you made and Ungrouped are always listed, each saying "No
connections" (or "No favorite connections") when there is nothing in it, and a
search keeps them instead of making them vanish. An empty Ungrouped is also
somewhere to drop a connection to take it out of its group. If you prefer the
quieter panel, turn on Settings > Hide empty groups, next to "Show favourites in
their folders too"; it travels with Export and Import settings.

The 1.4 line's last unreleased fixes, to updating and repairing a
service-hosted agent, stay in `1.x-archive`'s changelog. 2.0 has no service for
them to apply to.

## 1.4.5 — 2026-09-19

**StorageHub can tell you what is wrong with itself.** When the background
agent does not come up, the only symptom is the desktop saying it did not
become ready, which points at the wrong thing: the state that decides it is
spread over a per-user data root, a machine data root, a staged binary tree, a
service registration and a named pipe. Check installation looks at all five and
says which one is at fault, in a sentence rather than a status code.

It also answers the frightening question directly. Switching between the session
and service modes moves the data root, so it reports a populated database left
behind in the mode you are not using: the connections are not gone, they are out
of reach until that mode is selected again. A path this account may not
read is reported as exactly that rather than as missing: the service data root
denies the signed-in user by design, and File.Exists answers false for a path it
cannot open, so the first version of this check told a machine with a healthy,
running service that its database had vanished.

Where it can fix something it offers to, and asks Windows for consent when the
fix needs it rather than sending you away to restart the application as an
administrator: creating a missing data directory, starting a stopped service,
and telling Windows to restart the agent if it stops unexpectedly. The elevated
work is done by the agent in one short-lived pass, which is the same route
switching host modes already takes. That last one
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
