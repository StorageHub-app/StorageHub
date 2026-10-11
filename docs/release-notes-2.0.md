# StorageHub 2.0.0 release notes

StorageHub 2.0 is the 1.4 you know, rebuilt to run on Windows and Linux from
the same source, with the settings 1.4 could save but not use now working, and a
set of things it could not do at all. The window is new (Avalonia instead of
WinForms). The layout, menus and workflow are 1.4's: every screen was ported and
compared with 1.4 until it matched or was changed on purpose.

**Upgrading from 1.4?** Your preferences, shortcuts and workspace files come
across. Saved connections, their passwords and keys, sync tasks and schedules do
not, and have to be set up again. Read
[Upgrading from StorageHub 1.4](upgrading-from-1.x.md) before you install.

## Highlights

### New connection types

- **WebDAV.** Connect to Nextcloud, ownCloud, SharePoint, NAS boxes and other
  WebDAV servers by entering the folder's URL. Sign in with Basic, Digest, NTLM,
  Negotiate, a bearer token or no sign-in at all. HTTPS certificates are trusted
  by the system or pinned the same way as for FTPS. A client certificate from
  the key store can be added. Plain HTTP has to be allowed on the Trust tab. A
  longer request timeout for big uploads on slow lines is under Advanced.
- **Azure Blob Storage.** Add a container and sign in with a shared key, a SAS
  token, a connection string or the computer's own Azure identity.
- **Google Cloud Storage.** Add a bucket and sign in with a service-account key
  file, Application Default Credentials, or anonymously for a public bucket. A
  private endpoint or an emulator can be named instead of Google's.
- **OpenStack Swift.** Add a container and sign in with Keystone v3 (user,
  password, project and optional region), TempAuth, or a token you already have
  together with the storage URL.
- Every key, password and token for these is kept in the encrypted vault, and
  plain HTTP is refused unless you allow it for an emulator or a test cluster.

### Connection test and trust

- **Test before you save.** Test connection in the connection editor tests
  what is in the fields, saved or not. It shows how far it got: the settings,
  the server's identity, the credentials, reaching the server and listing the
  starting folder. Each step is marked passed, failed, not needed or not
  reached, with what to check when one fails. Testing never saves anything and
  never changes a saved connection's status.
- **Host keys and certificates, checked by you.** For an SFTP or SSH server with
  no key yet, the editor fetches the server's key and shows it to you to check.
  A test refused on the host key offers the key the server presents instead.
  "Fetch from host…" and "Reject…" sit beside the fingerprint, as in 1.4. A pane
  that fails on a server's key or certificate offers "Review trust…", which
  opens that connection's trust settings. Nothing is trusted until you accept
  it and save.
- **Live connection details.** The connections panel can show what a connection
  is talking to: how it is secured, the server's system and software, the host
  key or certificate and whether it is trusted, the cipher and key exchange in
  use, the server's features, open sessions and the last health check.
- **Connection health.** A connection's card says when its link is retrying,
  has been lost or has come back. Each change is also a Connection line in the
  activity log.
- **SFTP reaches more servers.** You can connect through a jump host chosen from
  your saved SFTP and SSH connections. Servers that sign in only through
  keyboard-interactive prompts are answered with the password, and servers that
  want a key and then a password get both. Advanced settings limit the key
  exchanges, ciphers, MACs and host key types offered, the number of sessions
  and the keep-alive. The private key is handed over in memory and never
  written to a temporary file.
- **SSH keys without a passphrase.** 1.4 refused them. They are now accepted and
  kept encrypted in the vault, with a warning that a passphrase is still
  recommended.

### Transfers and sync

- **Settings 1.4 saved but could not use now work.** These are per-connection
  and total speed limits (Speed Limits… and Settings, Transfers & sync), proxies
  for FTP, SFTP and S3, and FTP's separate connect and read timeouts, filename
  encoding, server time zone, listing parser and active mode.
- **Real speed and time remaining** in the queue and the status bar.
- **A file already at the destination is your choice.** The first time a copy
  or move meets one, at any depth of a folder copy, StorageHub asks what to do:
  overwrite, skip, overwrite if newer, overwrite if the size differs, keep both
  as "name (1).ext", or resume a shorter file. It applies your answer to the
  rest. You can set a fixed answer in Settings, Transfers & sync. Files on this
  PC can now be replaced by a download, and can replace a file on a connection.
- **Interrupted transfers carry on.** Downloads to this PC, and uploads to
  Local, FTP and SFTP connections, are written to a hidden partial file and
  renamed into place only when whole. After a dropped connection, an agent
  restart or a pause, the retry reads only what is missing. This only happens
  for the same version of the same file to the same place. Cancelling removes
  the partial file.
- **Compare two panes.** Sync, Compare Panes (Ctrl+D) lists what only one side
  has and what differs by size, time or, if you ask, SHA-256 contents. You can
  filter the list. Selected differences can be copied either way as normal
  queued transfers.
- **Clean up leftovers.** Tools, Clean Up Leftover Files… lists the partial
  and staged copies, write tests and stand-ins left in folders StorageHub wrote
  to, and old external-editor copies, with their sizes. It removes only what
  you select, and it leaves anything still in use.
- **Server checksums.** Where the server already keeps a file's SHA-256 (S3,
  FTP, Azure, Google Cloud Storage and Swift can), sync
  scans use it instead of reading the whole file. A verified copy still reads
  back what it wrote.
- **A time zone for new schedules.** Settings, Transfers & sync, "Time zone for
  new schedules", follows the system by default. The schedule window says which
  zone is in use.

### File panes

- **Free space** behind the folder shown, at the end of each pane's footer, for
  folders on this PC and for SFTP and FTP connections.
- **Folders that refresh themselves**, if you turn it on: Settings, Workspace,
  "Refresh a folder when it changes". A folder on this PC refreshes as soon as
  something changes. A connection's folder refreshes on an interval you choose,
  from 15 seconds to 5 minutes. Selection and scroll position stay put.
- **The FILES bar can be hidden** from Pane actions and is saved with the
  workspace. The right-click menu offers everything the bar does, plus Invert
  selection.
- **Drag and drop with the file manager on Linux.** Files dropped in from
  Nautilus or Dolphin are reviewed the same way as an Explorer drop.
- **Files dropped on a This PC folder** are copied there. 1.4 refused such drops.
- **The SSH terminal passes the mouse** to programs that ask for it, such as
  htop. Shift keeps the mouse for selecting and pasting.

### Properties

- **Properties opens on folders and links as well as files** on a saved
  connection. A new General tab
  shows kind, size, modified, created and last-accessed times, permissions as
  `rwxr-xr-x` and octal, owner and group, and where a link points, as far as the
  connection reports them.
- **Change permissions… and Set modified time…** work on SFTP and FTP
  connections and on Local connections. On Windows, a Local connection supports
  only the modified time. Each command is dimmed, with a sentence saying why,
  where the connection cannot make the change.
- **A Checksum tab.** Get checksum shows the SHA-256 and says whether the server
  reported it or StorageHub computed it from the file (up to 256 MB). Compute
  from the file reads the file to check what the server says.

### Settings

- **Twenty-two colour schemes**, which look the same on both platforms.
- **New options:** total speed limits, existing-file handling, the time zone for
  new schedules and folder auto-refresh. English, Danish and German are
  available, as in 1.4.
- **Settings export moves to format 2.** It covers preferences, shortcuts,
  connection defaults, connections (with credentials as vault references, never
  secrets), connection groups, sync tasks and schedules.

### Platform and installers

- **Linux.** A `.deb` for Debian and Ubuntu, for amd64 and arm64. It installs
  into `/opt/storagehub` and runs the agent per user under `systemd --user`,
  with no root and no service. The vault key is a file only your account can
  read.
- **Windows on ARM.** Every release has an ARM64 MSI beside the x64 one. The
  updater picks the package for the machine, so an x64 copy on an ARM PC is
  offered the ARM64 MSI.
- **A plain per-user MSI on Windows.** It replaces Velopack's Setup.exe,
  MSI and portable ZIP. It needs no elevation and installs into
  `%LOCALAPPDATA%\Programs\StorageHub`. It registers the agent at sign-in and
  stops the agent before an upgrade or removal.
- **The updater** reads the GitHub releases, holds every download to the
  release's `SHA256SUMS` and GitHub's size, and installs once StorageHub has
  closed. It uses `msiexec` on Windows, and `apt` with a polkit password prompt
  on Linux.
- **Every release is built and smoke-tested** on a machine of its own
  architecture, and carries GitHub artifact attestations.

## Changed from 1.4

- **No Windows service mode.** The agent always runs as you, at sign-in or only
  while StorageHub is open. On Windows, scheduled syncs do not run while you are
  signed out. On Linux, `loginctl enable-linger` keeps them running.
- **The agent's data has moved.** It is now in `%PROGRAMDATA%\StorageHub` on
  Windows, whichever way the agent runs, and in `~/.local/share/storagehub` on
  Linux. 1.4's database and vault are not read. See
  [Upgrading from StorageHub 1.4](upgrading-from-1.x.md).
- **1.4 settings exports cannot be imported**, because their shortcuts were
  stored as WinForms key numbers. Export again from 2.0.
- **Connection groups are yours.** The fixed Storage and Clients sections are
  gone. You make groups and drag connections between them, with Favorites
  above.
- **Right-click in the SSH terminal** goes to a program that has the mouse.
  Shift+right-click pastes.
- **Status bar messages stay for eight seconds**, and a setting waiting for a
  restart stays until it is applied.
- **Delete on Linux** sends files to the desktop Trash.
- **"Same as Windows"** in the language list is now "Same as the system".

## Known limitations

- **Releases are not code-signed yet.** Windows SmartScreen may warn. Check
  downloads against `SHA256SUMS`.
- **There is no import from 1.4** of connections, secrets, the key store, trust
  decisions, sync tasks or schedules.
- **One Windows account per PC.** On Windows the agent's data folder is
  protected to the first account that uses it. Another account needs
  `STORAGEHUB_DATA_ROOT` set to a folder of its own.
- **Linux:**
  - A connection's files cannot be dragged out to a Linux file manager. Copy
    them to a This PC pane instead.
  - Whether the agent starts at sign-in is set with `systemctl`, not in
    Settings.
- **Resume:**
  - Uploads to S3 always start again from the beginning.
  - A partial file left by another program is not continued.
- **Existing files:** the answer is chosen once per copy, not file by file.
- **SFTP:**
  - Only one private key per connection.
  - Link targets show as "—".
- **Properties:**
  - Works on saved connections, not on This PC.
  - Changing owner or group is not offered.
  - Permissions cannot be changed on a whole folder tree.
- **Compare Panes** cannot copy between two folders that are both on this PC.
- **Clean Up Leftover Files** does not include downloaded updates.
- **Connection health:** S3 connections never show "retrying".
- **WebDAV:**
  - No Windows integrated sign-in.
  - No custom headers.
  - No public-key pins.
- **S3:** certificate pins are not available yet.

## More

- [Upgrading from StorageHub 1.4](upgrading-from-1.x.md)
- [Changelog](../CHANGELOG.md)
