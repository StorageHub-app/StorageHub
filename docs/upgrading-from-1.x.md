# Upgrading from StorageHub 1.4

This is what happens when a StorageHub 1.4.x installation meets 2.0, what comes
across by itself, what you have to do by hand, and how to go back to 1.4.5 if
you need to. It was written against the 1.4.5 source (tag `v1.4.5`, kept on the
`1.x-archive` branch) and the 2.0 source on `main`.

**In short:** your preferences, keyboard shortcuts and workspace files come
across. Your saved connections, their passwords and keys, the key store,
trusted host keys and certificates, sync tasks, schedules, run history and the
transfer queue do not. 2.0 keeps the agent's data somewhere new, in a new
format for secrets, and it does not read 1.4's. Plan to set your connections up
again.

## Windows (MSI)

### Where 1.4 kept things, and what 2.0 does with them

1.4 had three ways to run its background agent. Two of them, "When I sign in"
and "Only while StorageHub is open", kept the agent's data under your own
profile. The third, the Windows service, kept it under `%PROGRAMDATA%`. The
table names the 1.4 locations for each.

| What | Where 1.4 kept it | What 2.0 does |
| --- | --- | --- |
| Preferences (Settings), toolbar layout, connection defaults, SSH terminal settings, connections panel width and side | `%LOCALAPPDATA%\StorageHub\Desktop\config.json` | **Read in place.** 2.0 uses the same folder and the same file, and every setting 1.4 wrote is still one 2.0 reads. |
| Keyboard shortcuts | `%LOCALAPPDATA%\StorageHub\Desktop\config.shortcuts.json` | **Read in place.** Shortcuts are stored as text such as `Ctrl+Shift+S` in both versions, and 2.0 writes them back in 1.4's spelling. |
| Pinned and recent workspaces (the lists on Welcome and the Workspace menu) | `%LOCALAPPDATA%\StorageHub\Desktop\config.workspaces.json` | **Read in place.** |
| Whether you were asked how the agent should run | `%LOCALAPPDATA%\StorageHub\Desktop\agent-host-mode-asked` | **Read in place.** If 1.4 already asked, 2.0 does not ask again. |
| Workspace files (`.shw`) | Wherever you saved them | **Opened as they are.** 2.0 reads and writes the same format. Panes on This PC come back on their folders. Panes on a saved connection do not, because the connection is not there yet (see [Workspaces](#reconnect-your-workspaces)). |
| Database: saved connections, sync tasks, schedules, run history, transfer queue and history, trusted host keys and certificates, the key store's list of keys | Sign-in and window modes: `%LOCALAPPDATA%\StorageHub\Agent\storagehub.db`. Service: `%PROGRAMDATA%\StorageHub\Agent\storagehub.db` | **Not read.** 2.0's agent uses `%PROGRAMDATA%\StorageHub\Agent\storagehub.db` whatever mode it runs in, and nothing copies 1.4's database there. The 1.4 file is left where it is. |
| Vault: connection passwords, private keys and certificates | `vault` beside the database | **Not read, and cannot be.** 1.4 sealed each entry with Windows DPAPI directly, under your account or, for the service, the machine. 2.0 seals entries with its own key (kept in `Agent\secrets` and protected by DPAPI for your account), and it refuses an entry sealed any other way. This applies to the sign-in and window modes too, not only to the service. |
| Settings export file (Tools > Export Settings) | Wherever you saved it | **Refused.** 1.4 wrote format 1, which stored shortcuts as WinForms key numbers. 2.0 reads only format 2 and says: "This file was written by StorageHub 1.x (format 1). Export your settings again from this version." That refusal covers the whole file, including any connections, sync tasks and schedules in it. |
| Logs | Under the `CodeLogic` folders inside `Desktop` and `Agent` in the data folder | **Left alone.** 2.0 starts its own. The desktop's error log is `%LOCALAPPDATA%\StorageHub\Desktop\logs\desktop.log`. |
| Program files | 1.4's Velopack install | **Not removed.** The 2.0 MSI is a new kind of package: it upgrades earlier 2.0 MSIs, not 1.4. Uninstall 1.4 yourself (see below). |
| The agent's sign-in entry (`HKCU\...\Run\StorageHub.Agent`) | Written by 1.4 | **Overwritten.** 2.0 uses the same entry name, and its installer points the entry at 2.0. |
| Windows service `StorageHubAgent` and its folders `%PROGRAMDATA%\StorageHub` and `%PROGRAMDATA%\StorageHubAgent` | Service mode only | **Not removed, and in the way.** See [If 1.4 ran its agent as a Windows service](#if-14-ran-its-agent-as-a-windows-service). |

2.0's installation check (Tools > Background Agent... > Check installation)
finds a 1.4 database under `%LOCALAPPDATA%\StorageHub\Agent` and lists it as
"Database from an earlier version". It does not import it.

Uninstalling 1.4 does not delete any of these files. 1.4's uninstaller leaves
`%LOCALAPPDATA%\StorageHub` alone, and it leaves the service's data in
`%PROGRAMDATA%` alone too. The same is true of 2.0's uninstaller.

### Before you upgrade

1. **Write down your connections.** For each one, note the provider, host,
   port, user name, starting folder and any options you changed. Find the
   passwords, and the private key and certificate files you imported into the
   key store. 2.0 cannot read any of them from 1.4.
2. **Write down your sync tasks and schedules** if you want them back. They are
   in the same database and do not come across.
3. **Let the transfer queue finish.** Queued and paused transfers stay in 1.4's
   database.
4. **Check how 1.4's agent runs:** Tools > Settings > Background agent. If it
   says it runs as a Windows service, you will need the extra step in
   [If 1.4 ran its agent as a Windows service](#if-14-ran-its-agent-as-a-windows-service).

### Upgrade steps

1. **Uninstall 1.4 first**, from Windows Settings > Apps > Installed apps.
   Your data stays on disk. Do this before installing 2.0, not after, for two
   reasons:
   - 1.4's uninstaller deletes the `StorageHub.Agent` sign-in entry by name and
     removes the Explorer drop handler's registration. 2.0 uses the same
     names, so uninstalling 1.4 after 2.0 would remove 2.0's sign-in entry.
     (2.0 registers the drop handler again each time it starts, but it does not
     recreate the sign-in entry.)
   - 1.4's agent and 2.0's agent use the same pipe names and the same
     per-user lock. While 1.4's agent is running, 2.0 cannot start its own.
     Uninstalling 1.4 stops it.
2. **If 1.4 used the Windows service**, remove it now (next section).
3. **Install 2.0** from `StorageHub-<version>-win-x64.msi`, or `-win-arm64.msi`
   on an ARM PC. No elevation is needed. It installs into
   `%LOCALAPPDATA%\Programs\StorageHub`, adds a Start menu entry, and starts the
   agent when you sign in. The release is not code-signed yet, so SmartScreen
   may warn. Check the file against the release's `SHA256SUMS`.
4. **Start StorageHub** and set your connections up again: Connections > New
   Connection... Import keys and certificates through Connections > Key
   Store... Trust decisions do not come across either, so you check and accept
   each server's host key or certificate again, as on first use.
5. **Recreate sync tasks and schedules** under the Sync tasks tab.
6. **Reconnect your workspaces** (next but one section).

1.4's built-in updater uses Velopack's update feed, which 2.0 releases do not
publish, so 1.4 will not offer to update itself to 2.0. Download the MSI from the
releases page.

If you already installed 2.0 and uninstalled 1.4 afterwards, open
Tools > Settings > Background agent in 2.0 and choose "When I sign in" again.
That puts the sign-in entry back.

### If 1.4 ran its agent as a Windows service

2.0 has no service mode. Its agent runs as you and takes
`%PROGRAMDATA%\StorageHub` as its data folder. The 1.4 service hardened that
folder so that only LocalSystem could use it. Until the service and that folder
are gone, 2.0's agent cannot start. It stops with "The StorageHub data directory
could not be protected for the current user", and the installation check says
the folder belongs to the Windows service of an earlier StorageHub and has to
be removed first.

Even if you used a session mode, the folder may still be there. When 1.4
switched from the service back to a session mode, it copied the data back into
your profile and left the service's copy in place.

The fix is `eng/remove-legacy-agent-service.ps1`, from the StorageHub
repository. It is not included in the release downloads. It needs an
administrator PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\remove-legacy-agent-service.ps1
```

It stops and deletes the `StorageHubAgent` service and stops any
`StorageHub.Agent.Windows` process. Then it takes ownership of
`%PROGRAMDATA%\StorageHub` and `%PROGRAMDATA%\StorageHubAgent` and deletes
them. It asks before each step. **This deletes the service's database and
vault**, which 2.0 could not read anyway. It does not touch
`%LOCALAPPDATA%\StorageHub`. To look at what was there first, run it with
`-KeepData`. That removes only the service, and 2.0 still cannot use the folder
until you delete it. The script finishes by telling you to start
`run-dev-agent.ps1`. That message is for developers. An installed copy just needs
StorageHub started again.

Scheduled syncs no longer run while you are signed out of Windows. That was the
one thing the service did, and it went with it.

### Reconnect your workspaces

A 1.4 workspace remembers each pane's connection by that connection's internal
ID. A connection you set up again in 2.0 gets a new ID. A pane that pointed at
it opens on Connections Home and says "Saved profile '<name>' is unavailable.
Select another profile." Choose the new connection in each such pane, then save
the workspace (Ctrl+S). Panes on This PC open on their folders without any of
this.

## Linux (.deb)

There was no StorageHub 1.4 for Linux, so a `.deb` install always starts fresh.
It installs into `/opt/storagehub`. The agent runs per user under
`systemd --user` as `storagehub-agent`. Its database and vault go in
`~/.local/share/storagehub` (or `$XDG_DATA_HOME/storagehub`), and preferences go
in `~/.local/share/StorageHub/Desktop`. The desktop starts the agent's unit when
it opens. To have the agent start at sign-in, enable it for your account:

```sh
systemctl --user enable --now storagehub-agent
```

For schedules to keep running while you are signed out, also run
`loginctl enable-linger`. Removing the package stops the agent and leaves every
user's data in place.

Workspace files saved by 1.4 on Windows open on Linux. A pane on a Windows
folder such as `C:\` opens on This PC instead.

## What you will notice is different

**A new window, the same layout.** 2.0 is built on Avalonia rather than
WinForms, and runs the same on Windows and Linux. Every 1.4 screen was ported
and compared with 1.4, so menus, panes, the queue, Sync tasks and Settings are
where you left them. There are twenty-two colour schemes, and Danish and German
are still there.

**Changes made on purpose:**

- **No Windows service.** The agent always runs as you. "When I sign in" and
  "Only while StorageHub is open" remain. Scheduled work does not progress while
  you are signed out of Windows.
- **One data folder per Windows machine.** The agent's data is in
  `%PROGRAMDATA%\StorageHub` in every mode (1.4 used your profile unless you
  chose the service). It is protected to the account whose agent created it.
- **Connection groups you make.** The connections panel holds groups you make
  and drag connections between, instead of 1.4's fixed Storage and Clients
  sections. Favorites still sit above them.
- **SSH keys without a passphrase are accepted.** 1.4 refused them. Such a key is
  still kept encrypted in the vault, and StorageHub warns that a passphrase is
  recommended.
- **Every new host key is still shown to you.** Test connection, and opening the
  trust tab, can fetch the server's key for you. Nothing is trusted silently,
  and nothing is trusted until you save the connection.
- **Files dropped on a This PC folder are copied.** 1.4 refused such a drop. 2.0
  queues it as a paste from This PC.
- **Status bar messages stay for eight seconds.** 1.4's queue updates could
  write over them sooner. A saved setting that is waiting for a restart stays
  until it is applied.
- **Right-click in the SSH terminal.** While a program in the terminal (htop,
  for example) has the mouse, a right-click goes to that program. Shift+right-click
  pastes. 1.4 pasted on every right-click.
- **Delete on Linux uses the desktop Trash** (through `gio trash`), the way
  Windows uses the Recycle Bin.
- **"Same as Windows"** in the language list is now **"Same as the system"**.
- **A settings export from 1.4 cannot be imported.** Export again from 2.0 to
  move settings between 2.0 machines.
- **The installer.** Windows gets a plain per-user MSI instead of Velopack's
  Setup.exe, MSI and portable ZIP. The in-app updater downloads the next MSI,
  checks it against the release's `SHA256SUMS`, and installs it once StorageHub
  has closed. On Linux it installs the `.deb` through `apt`, with polkit asking
  for your password.

## Going back to 1.4.5

Nothing in 2.0 writes to 1.4's database or vault, so 1.4.5 comes back exactly as
you left it.

1. Uninstall StorageHub 2.0 from Windows Settings > Apps > Installed apps. This
   stops the agent and removes its sign-in entry and the Explorer drop handler.
   It keeps `%PROGRAMDATA%\StorageHub` (2.0's data) and
   `%LOCALAPPDATA%\StorageHub` (preferences and 1.4's data).
2. Download and install 1.4.5 from the `v1.4.5` release on the releases page.
3. Start it. Your 1.4 connections, secrets, tasks and schedules are where 1.4
   left them, under `%LOCALAPPDATA%\StorageHub\Agent`.

Some things to know before you go back:

- **Connections you made in 2.0 stay in 2.0's data** and do not appear in 1.4.
  A 2.0 settings export cannot be imported into 1.4.
- **Preferences are shared.** 1.4 reads the settings files 2.0 last wrote and
  ignores the settings it does not know. Shortcuts and workspace files work in
  both versions.
- **If you used the removal script**, the service's data has been deleted, and
  1.4.5 cannot get it back.
- **If you plan to return to 2.0, do not choose the Windows service in 1.4.5.**
  1.4's service keeps its data in `%PROGRAMDATA%\StorageHub`, the folder 2.0
  uses. Switching to it copies 1.4's data there and sets aside what it finds, and
  2.0 then cannot start until the service and that folder are removed again.

## Known limitations of the upgrade

- **There is no import of 1.4 connections, secrets, key store, trust decisions,
  sync tasks, schedules or history.** 2.0 neither reads 1.4's database nor
  accepts a 1.4 settings export, and 1.4's vault is in a format 2.0 refuses. Each
  connection has to be set up again by hand.
- **The removal script is not shipped with the installer.** It has to be
  fetched from the repository and run as an administrator.
- **One Windows account per machine.** The agent's data folder,
  `%PROGRAMDATA%\StorageHub`, is protected to the first account whose agent
  creates it. A second Windows account on the same PC cannot use it, and its
  agent fails with the same "could not be protected" error. That account can set
  the `STORAGEHUB_DATA_ROOT` environment variable to a folder of its own. Both
  the agent and the desktop then use that folder, preferences included.
- **The 1.4 program is not removed** by installing 2.0, and the two cannot run
  their agents at the same time for one user.
