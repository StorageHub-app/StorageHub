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
WinForms, provider SDKs, SQLite native assets, and `CL.Storage` use runtime
discovery and platform-specific files.

The release contains the per-user Velopack Setup executable and MSI, a portable
ZIP, Velopack update metadata/packages, separated symbols when available,
`release-version.txt`, and `SHA256SUMS`. The setup process is intentionally
unelevated and never installs the Agent as a Windows service. Installing one is a
separate, explicit choice made inside the application, because it is not a
packaging detail: it moves the database to `%ProgramData%`, re-protects every
stored secret with the machine DPAPI key instead of the user's, and publishes a
machine-wide pipe whose ACL replaces the per-user isolation that a session agent
gets for free. Setup has no business making that decision silently, and an
unelevated installer could not make it correctly anyway.

The service is offered once on first run and lives in Settings under Background
agent, alongside the two session modes. Whichever is chosen, the Agent that a
service runs is a copy staged into a directory only administrators can write:
StorageHub installs per user, so registering the install directory itself would
let the signed-in user replace a binary that runs as LocalSystem.

Installed builds use Velopack's GitHub source against the fixed public
repository `https://github.com/StorageHub-app/StorageHub`, retain their packaged
channel, reject downgrades, and can silently download and apply the exact
package described by the release feed. Portable and developer builds fail closed without checking or
modifying an installation. Automatic checks/downloads, release candidate
inclusion, and automatic restart are persisted per user; automatic restart is
opt-in. Because Velopack's framework-level apply-on-startup default is
explicitly disabled, a pending download cannot bypass those persisted StorageHub
preferences. Because release candidate packages are not yet Authenticode-signed,
feed/package checksum verification provides integrity but is not a substitute
for the production signing release gate.

Uninstall removes program files and autostart registration but deliberately
preserves `%LOCALAPPDATA%\StorageHub`. Deleting durable state, connection
profiles, trust decisions, schedules, or the encrypted vault requires a separate
explicit user action.

## Build a bundle locally

Run:

```powershell
& .\eng\package-windows.ps1 `
  -Version '0.1.0-local.1' `
  -OutputRoot artifacts\local-release
```

The packaging script restores the repository-pinned `vpk` tool and validates
every expected output. The install/uninstall smoke script refuses to run on a
normal workstation by default because it mutates the current user's installed
program and autostart state and could collide with a real StorageHub session.
Run it only on a disposable Windows runner, Sandbox, or test account.
Outside CI, both `-AllowOutsideCi` and
`-ConfirmDisposableRunner` are required before it will make system changes.

## Automated publication

The CI workflow uses a least-privilege job chain:

1. build, test, audit NuGet and hash-locked Python dependencies, and run the
   SHA-256-pinned MinIO/S3 plus FTP/FTPS and SFTP fixtures with read-only repository
   access;
2. package and silently install/test/uninstall on a disposable Windows runner;
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
