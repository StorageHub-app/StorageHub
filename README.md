<p align="center">
  <img src="assets/branding/storagehub-icon.png" width="144" alt="StorageHub icon">
</p>

# StorageHub

A file manager, transfer client, and synchronization engine for Windows, for
people who move files they cannot afford to lose.

Put local disks and remote storage side by side. Queue a transfer and close the
window — it keeps running. Synchronize two locations and see exactly what is
about to change before anything is touched. Every credential lives in an
encrypted vault, and no server is trusted until you have checked its fingerprint
yourself.

Open source, and built with C# and .NET 10 on the CodeLogic application
lifecycle, adapting the all-in-one `CL.Storage` (`CodeLogic.Storage`) provider
library behind a provider-neutral contract.

> [!IMPORTANT]
> StorageHub 1.3 is the current stable release. Every push to `main` also
> publishes a release candidate, which an installation can opt into or exclude.
> Release binaries are not yet Authenticode-signed, so Windows SmartScreen may
> warn on first run; verify downloads against the published `SHA256SUMS`. As
> with any file-management tool, keep an independent backup of irreplaceable
> data.

## Features

**Browse and manage** — Workspaces of one to four equal-capability panes, in six
layouts from a single pane to a 2×2 grid. Saving a workspace names it after the
file, and pinning it reopens it where you left off. Each pane carries a
one-line connection bar that can be hidden per pane when you want the whole
pane for the listing. Local and remote browsing is asynchronous, with
history, filtering, and bounded paging, so a folder with a hundred thousand
objects does not freeze the window. Create, rename, batch-rename, and delete
items; inspect versions, metadata, and tags read-only. Drag files to and from
File Explorer.

**Connect securely** — Typed profiles for Local/UNC, S3, FTP, FTPS, and SFTP
storage plus SSH clients, organized in a grouped, searchable tree with folders,
favorites, and tags. Give a connection or a folder its own icon and color, and
read a profile as a foldable property grid. A favorite is listed under Favorites
and in its own folder, which Settings can turn off. Credentials live in a
Windows DPAPI current-user vault and never enter profile JSON, logs, or
diagnostics. Keys and certificates live in a
shared store, so rotating one updates every profile that uses it instead of
sending you hunting. Server identity is pinned explicitly: no certificate or
host key is accepted on first contact.

**Transfer durably** — Any-to-any copy and move with source preconditions,
optional SHA-256 verification, and delete-only-after-verified-commit move
semantics. Jobs are queued in SQLite with fenced claims, checkpoints, retries,
and interrupted-owner recovery, and execute in the background agent — closing
the desktop does not discard accepted work.

**Synchronize and schedule** — Three-way classification with conflict
categories and deletion guards, producing immutable SHA-256 plans. Preview is
read-only; applying requires an explicit approval bound to the plan, verified
roots, and live capabilities. Cron schedules honor time zones and DST and
dispatch preview-only runs.

**SSH terminal** — A managed SSH.NET client with no PuTTY dependency. Sessions
run in the agent with vault-backed authentication and verified host keys. A full
VT emulator renders the alternate screen, so `htop`, `vim`, `less`, and `nano`
draw correctly and leave your scrollback intact on exit. Resizing reflows rather
than truncating, and tells the remote its new size. Output survives a dropped
read: the agent holds it until the terminal confirms it arrived, so a timeout
replays instead of losing the bytes. ANSI, 256-color, and true-color with
inverse, underline, and box drawing; the mouse is forwarded to programs that ask
for it, with Shift to select locally. Ctrl+Shift+C and Ctrl+Shift+V copy and
paste, which leaves Ctrl+C free to always interrupt.

**In your language** — English, Danish, and German throughout, including the
menus, the error messages and the screen-reader labels. Pick one in Settings or
follow Windows; StorageHub offers to restart so the change takes effect
everywhere at once.

**Themes and settings** — Light, Dark, and System appearances applied across
every window, including native scrollbars, list headers, and edit borders. Text
fields, dropdowns, number steppers, switches, and buttons are drawn by
StorageHub rather than by Windows, so a field looks and measures the same in
Settings, in a dialog, and on a toolbar, at any display scale. Settings are laid
out as captioned cards of labelled rows: a title and its explanation on the
left, the control on the right. They cover transfers and sync, editing,
appearance, workspaces, the toolbar's contents, rebindable shortcuts,
per-provider connection defaults and trust policy, language, and updates.
Settings, connections, and sync tasks can be exported and imported, optionally
password-protected.

What is implemented is what is described above and covered by the test suite.
Provider coverage is listed under [Provider status](#provider-status).

## Install

Download the latest release from
[GitHub Releases](https://github.com/StorageHub-app/StorageHub/releases):

- `StorageHub-<version>-win-x64-Setup.exe` — recommended one-click, per-user installer;
- `StorageHub-<version>-win-x64.msi` — per-user MSI for managed deployment; and
- `StorageHub-<version>-win-x64-portable.zip` — self-contained portable payload.

The installer does not require elevation and starts the background agent as the
signed-in Windows user. Application data under `%LOCALAPPDATA%\StorageHub` is
preserved across updates and uninstalls.

How the agent is hosted is then chosen in the application — once on first run,
and afterwards in Settings under **Background agent**:

- **When I sign in** — the historical behaviour. The agent starts with Windows
  and outlives the window, and your secrets stay readable only by your account.
  Scheduled work runs only while you are signed in.
- **Only while StorageHub is open** — no logon entry and nothing left running;
  transfers and schedules progress only while the window is open.
- **As a Windows service** — starts with the computer and keeps running with
  nobody signed in, which is what an unattended schedule needs. It requires
  administrator approval once, moves the database to `%ProgramData%\StorageHub`,
  and re-protects every stored secret with the machine key, so administrators of
  the computer can read them. Your existing data is copied, never moved, so
  switching back leaves the original untouched.

Installed builds check the official GitHub release feed at startup and silently
download integrity-checked updates by default. Settings can disable automatic
checks or downloads, exclude release candidates, or opt into silent
install-and-restart. **Help > Check for Updates...** remains available when
automatic checks are off. Portable and developer builds never modify an
installation.

## Provider status

`CL.Storage` supplies native implementations without PuTTY or external transfer
executables. StorageHub exposes those implementations only through capability
checks; a provider is not complete merely because it exists in the library.

| Provider | In `CL.Storage` | StorageHub profile model | End-to-end StorageHub coverage |
| --- | :---: | :---: | --- |
| Local directories and UNC paths | Yes | Yes | Real integration test |
| Amazon S3 and S3-compatible services | Yes | Yes | Hermetic MinIO interoperability and hostile-input test |
| FTP, explicit FTPS, implicit FTPS | Yes | Yes | Hermetic interoperability, pin/downgrade, and client-PFX tests |
| SFTP | Yes, via SSH.NET | Yes | Hermetic password/key interoperability and hostile host-key tests |
| SSH terminal client | Managed SSH.NET client | Yes | Hermetic open/write/read/resize/close test beside SFTP |
| WebDAV | Yes | Not yet | Pending |
| Azure Blob Storage | Yes | Not yet | Pending |
| Google Cloud Storage | Yes | Not yet | Pending |
| OpenStack Swift | Yes | Not yet | Pending |

A provider moves out of *Pending* only once it has a StorageHub profile model,
credential and trust mapping, capability conformance tests, and a hermetic
integration test. Existing in `CL.Storage` is not enough.

## Build from source

### Prerequisites

- Windows and PowerShell
- Visual Studio C++ build tools with the Desktop development workload, required
  for the Explorer drag/drop broker
- [.NET SDK 10.0.401](global.json), or a later 10.0 patch accepted by `global.json`
- Git
- CPython 3.12 when running the local FTP/FTPS or SFTP fixtures; CI pins 3.12.10

The CodeLogic framework and `CL.Storage` provider library are restored from the
centrally pinned `CodeLogic` and `CodeLogic.Storage` NuGet packages.

### Restore, build, and test

```powershell
dotnet restore StorageHub.slnx --locked-mode
dotnet build StorageHub.slnx --configuration Release --no-restore
dotnet test StorageHub.slnx --configuration Release --no-build --no-restore
dotnet list StorageHub.slnx package --vulnerable --include-transitive --no-restore
```

Warnings are errors, package versions are centrally managed, and StorageHub
projects restore from committed lock files. CI runs the same Release build,
test, and vulnerability-audit path on Windows.

Every successful push to `main` publishes a uniquely versioned prerelease after
the Release build, full test suite, dependency audit, packaging, silent install,
agent health, and silent uninstall checks pass. A failed check produces no
release.

### Provider fixtures

```powershell
.\eng\run-provider-smoke.ps1
```

This starts pinned loopback MinIO, FTP/FTPS, and SFTP services, exercises their
real health/read/write behavior, and runs provider-neutral transfers between
each supported remote direction and Local storage. S3 is verified in both
directions. FTP and SFTP outbound create is verified to fail closed, because
those protocols cannot provide StorageHub's required atomic create-if-absent
guarantee.

### Run from source

Start the background agent first:

```powershell
dotnet run --project src\StorageHub.Agent.Windows --configuration Release
```

Then the desktop shell in another terminal:

```powershell
dotnet run --project src\StorageHub.Desktop.WinForms --configuration Release
```

The agent uses `%LOCALAPPDATA%\StorageHub` by default. Set
`STORAGEHUB_DATA_ROOT` before starting it to use an isolated development data
directory. `--run-once` performs startup and a clean shutdown; `--health` runs
the CodeLogic health path.

## Using StorageHub

File commands act on the pane marked **Active**, outlined in the theme accent
color. Defaults include Ctrl+C/Ctrl+X/Ctrl+V for copy/cut/paste, Ctrl+A for
select all, F2 for rename, Delete for delete, Ctrl+Shift+N for a folder,
Ctrl+Alt+N for an empty file, F5 for refresh, Ctrl+L for the address, and F6 for
the next pane. Text fields and SSH terminal input keep their own keyboard
behavior; file shortcuts never operate on SSH panes.

Use **Tools > Settings > Shortcuts** to reassign commands, clear a binding, or
restore the defaults. Conflicting shortcuts must be cleared before reassignment.

## Documentation

- [Architecture](docs/architecture.md)
- [Release engineering](docs/releasing.md)
- [Contributing](CONTRIBUTING.md)

StorageHub is written to keep credentials in a current-user vault, to refuse a
server whose identity has not been confirmed, and to treat every delete,
overwrite, and resume as its own decision. None of that has been independently
audited. Report a suspected vulnerability privately through the repository's
**Security** tab (**Report a vulnerability**) rather than in a public issue, and
never put a credential or private key in an issue, log, or test fixture.

## License

StorageHub is licensed under the [MIT License](LICENSE).
