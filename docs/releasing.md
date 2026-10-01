# Release engineering

StorageHub publishes an unsigned engineering prerelease for every successful
push to `main`, and an unsigned stable release for every successful push of a
`vMAJOR.MINOR.PATCH` tag. Pull requests build and smoke-test the same installer,
but never receive a write-capable GitHub token and never publish a release.

## Cutting a stable release

A stable release is cut by tagging, so the decision is recorded in git before
anything is published and the tag names the exact commit that ships. How the
number is chosen is in [Versioning and merges](versioning.md).

`main` is protected, so steps 1 and 3 arrive through a pull request like any
other change. Only the tag is pushed directly, because a tag is not a branch.

```powershell
# 1. In a pull request: rename the changelog's Unreleased heading to the version
#    and today's date, open a fresh Unreleased above it, and confirm
#    Directory.Build.props already declares that version:
#    <VersionPrefix>1.4.2</VersionPrefix>
#    Merging it publishes one more candidate.

# 2. Tag the merged commit. This publishes the stable release.
git tag v1.4.2 <the merge commit>
git push origin v1.4.2

# 3. In a second pull request: open the next line of development, which returns
#    main to candidates.
#    Directory.Build.props: <VersionPrefix>1.4.3</VersionPrefix>
```

The tag must match the `VersionPrefix` declared by the commit it points at. A
tag that disagrees fails the build rather than publishing a release whose
assemblies carry a different version from the release that contains them.

Both kinds run the identical gate. They differ only in the version they carry
and how the release is marked: a candidate is `MAJOR.MINOR.PATCH-rc.<run>.g<sha>`
and is published as a prerelease that never becomes Latest, while a stable
release is `MAJOR.MINOR.PATCH` exactly and does become Latest. That distinction
is what an installation excluding release candidates follows.

Stable releases remain unsigned for now; see the signing gate below.

## Release contents

The Windows bundle is built for `win-x64` with the .NET runtime included. The
Desktop and Agent are published as complete directory trees; trimming, native
AOT, ReadyToRun, and single-file bundling remain disabled because StorageHub,
Avalonia, provider SDKs, SQLite native assets, and `CL.Storage` use runtime
discovery and platform-specific files.

The Windows release contains one installer, a plain WiX MSI
(`StorageHub-<version>-win-x64.msi`), with separated symbols when available,
`BUILDINFO.json`, `release-version.txt`, `LICENSE`, `README.md`, and
`SHA256SUMS`. There is no Setup.exe and no portable ZIP. Linux ships the .deb
from `eng/package-linux.sh`.

The MSI is built from `eng/installer/StorageHub.wxs` by
`eng/installer/StorageHub.Installer.wixproj`, which uses the WiX Toolset SDK
from NuGet, so `dotnet build` is all it needs. It is not in `StorageHub.slnx`:
it builds from the folder the packaging script stages and has nothing to add to
a build or a test run of the code.

The MSI installs per user, as 1.4 did, into `%LOCALAPPDATA%\Programs\StorageHub`
with the Agent in `Agent` beside the Desktop, and needs no elevation to
install, upgrade, or remove. It adds a Start menu shortcut and records its
folder under `HKCU\Software\StorageHub\Installer`, which is how the Desktop
tells an installed copy from a build run from source. Its custom actions run
the installed Desktop with `--package-hook` (`DesktopPackageLifecycleHooks`):

- install registers the sign-in entry (`HKCU\...\Run\StorageHub.Agent`,
  `--agent-only`), as a fresh install always has;
- an upgrade first has the copy being replaced stop the agent it started,
  before Windows Installer looks for files in use, and afterwards keeps the
  sign-in entry only if the mode in force wants one;
- uninstall stops the agent, removes the sign-in entry, and removes the
  Explorer drop broker's COM registration.

The removal of the older package inside an upgrade does not run the uninstall
hook, so an upgrade never changes how the agent runs. No hook can fail the
package. The MSI never installs the Agent as a Windows service.

An upgrade is a major upgrade: every release has a new product code under the
fixed upgrade code, and a release candidate carries its release's
`MAJOR.MINOR.PATCH` as its MSI version and upgrades another candidate of the
same release.

Uninstall removes program files, the shortcut, the install record, and
autostart registration, but deliberately preserves `%LOCALAPPDATA%\StorageHub`,
and an upgrade never touches it. Deleting durable state, connection profiles,
trust decisions, schedules, or the encrypted vault requires a separate explicit
user action.

## Updates

Installed builds find new versions where this workflow publishes them: the
GitHub releases of the fixed public repository
`https://github.com/StorageHub-app/StorageHub`, read through GitHub's release
listing. The Desktop offers the newest release newer than the one running that
is not a draft, is a stable release unless release candidates are included,
and carries `StorageHub-<version>-win-x64.msi` (on Linux, the `_amd64.deb`) and
a `SHA256SUMS` naming it, both downloaded from that repository's own release
downloads. Downgrades are never offered.

The MSI is checked before anything runs it: its size must be the one GitHub
lists, and its SHA-256 the one the release's `SHA256SUMS` gives; a download
that fails either is deleted. Releases are not yet Authenticode-signed, so
there is no signature to check; that integrity check is not a substitute for
the production signing gate below.

"Restart and install" closes the shell the way Exit does, asking about each
changed workspace first, and only once it has closed starts
`msiexec /i <msi> /qb /norestart STORAGEHUB_RELAUNCH=1`, logging beside the
download. The package stops the agent, upgrades, and reopens StorageHub. A
close cancelled at the save prompt leaves the update to install when
StorageHub does close.

Automatic checks on start, automatic downloads, release candidate inclusion,
and automatic restart are persisted per user; automatic restart is opt-in.
Builds run from source, or any copy the MSI did not install, never check or
modify an installation.

## Build a bundle locally

Run:

```powershell
& .\eng\package-windows.ps1 `
  -Version '0.1.0-local.1' `
  -OutputRoot artifacts\local-release
```

The packaging script publishes the Desktop (with the Explorer drop broker,
which needs the Visual Studio C++ build tools) and the Agent, stages them,
builds the MSI, reads its tables back to check the upgrade code, version,
per-user scope, custom actions and payload, and validates every expected
output.

The install/upgrade/uninstall smoke script, `eng\test-windows-installer.ps1`,
refuses to run on a normal workstation by default because it changes the
current user's installed programs, sign-in entry and Explorer registration, and
its uninstall stops whichever agent answers this user's pipe. It also refuses
on any account where StorageHub is already installed or registered at sign-in.
Run it only on a disposable Windows runner, Sandbox, or test account. Outside
CI, both `-AllowOutsideCi` and `-ConfirmDisposableRunner` are required before
it will make system changes. Pass `-PreviousBundleRoot` with an earlier
version's bundle to test the upgrade as well.

## Automated publication

The CI workflow uses a least-privilege job chain:

1. build, test, audit NuGet and hash-locked Python dependencies, and run the
   SHA-256-pinned MinIO/S3 plus FTP/FTPS and SFTP fixtures with read-only repository
   access;
2. package the MSI and silently install/test/uninstall it on a disposable
   Windows runner;
3. attest the exact same-run artifacts after a successful `main` push; and
4. create or verify the prerelease for the exact commit without checking out or
   executing repository code in the write-capable publication job.

Push concurrency is keyed by commit SHA, so a newer push cannot cancel an older
successful push before its release is produced. Rerunning one workflow is
idempotent: it verifies the existing exact-commit tag and asset names, then
leaves the already-published prerelease immutable.

Production signing remains a release gate. Signing keys and PFX passwords must
be provided only by a protected signing system or GitHub environment; they must
never be stored in this repository, workflow artifacts, command output, or
ordinary repository secrets exposed to build steps.
