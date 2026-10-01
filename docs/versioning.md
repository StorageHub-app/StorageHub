# Versioning and merges

How a StorageHub version number is chosen, and how a change reaches `main`.
[Release engineering](releasing.md) covers the mechanics of cutting a release
once the number is decided.

## The number

StorageHub versions are `MAJOR.MINOR.PATCH`. One number is declared in one place,
`Directory.Build.props`:

```xml
<VersionPrefix>2.0.0</VersionPrefix>
```

Everything else follows from it: assembly versions, the installer, the update
feed, and the tag that publishes a release.

**PATCH** — fixes, performance, internal work, dependency bumps, documentation.
Anything a person could install without being told what to do differently.

**MINOR** — a feature arrives. Something new is visible in the application, or an
existing thing gains an option somebody would go looking for.

**MAJOR** — reserved. StorageHub ships an application rather than a library, so
the trigger is not an API break but a break in what an installation expects:
durable state that an older version could not read, a provider that stops being
supported, or a change in where data lives.

Two rules that are not judgement calls:

- **A version is never lowered.** An installation that has taken a 1.5.0 release
  candidate refuses 1.4.3 as a downgrade. A number raised in anticipation cannot
  be walked back, so it is raised when the feature lands and not before.
- **A tag must equal the `VersionPrefix` of the commit it points at.** CI fails
  the build rather than publish a release whose assemblies disagree with it.

## Candidates and releases

Every successful push to `main` publishes a prerelease,
`MAJOR.MINOR.PATCH-rc.<run>.g<sha>`, which never becomes Latest. Only a pushed
`vMAJOR.MINOR.PATCH` tag publishes a stable release. Both run the identical gate;
they differ in the version they carry and how the release is marked.

So `main` always carries the *next* version, not the last one released. After
1.4.2 was tagged, `main` moved to 1.4.3 and started publishing 1.4.3 candidates.

## Lines of development

`main` is StorageHub 2.0: the Avalonia desktop, on Windows and Linux, declaring
`2.0.0`. It publishes `2.0.0-rc.<run>.g<sha>` candidates until `v2.0.0` is
tagged. The first, `2.0.0-rc.1`, was published by hand before the ARM64 and
Linux CI jobs existed; every candidate since comes from CI. Until 2.0.0 is
tagged, 1.4.5 stays the release marked Latest.

1.4 is preserved on the `1.x-archive` branch, the last commit of the WinForms
line. It is kept for reference and is not built by CI: the workflow only
publishes from `main` and from version tags. A fix that has to reach 1.4 users
after this point would need its own decision about where it is built and how it
is numbered, since a candidate can never carry a lower version than one already
published from `main`.

## The changelog

`CHANGELOG.md` opens with an `## Unreleased` section. **A change that a person
would notice appends to it in the same pull request that makes the change**, in
the changelog's own register: what changed and why it mattered, not which files
moved. A fix nobody would observe — a refactor, a test, a lock file — needs no
entry.

Cutting a release is then renaming that heading to the version and the date, and
opening a fresh `## Unreleased` above it. Nothing has to be reconstructed from
the commit log at the point where it is least convenient.

The entry goes in the release that contains it, so it lands before the tag, not
after.

## Getting a change into `main`

`main` is always releasable, because a push to it publishes a candidate that an
installation can take. Nothing is published unless the whole gate passes on the
pushed commit: Release build, the whole test suite, the dependency audit, the
provider conformance fixtures, and a silent install and removal of every
package on a runner of its own architecture. A failed push produces no release.

A change arrives either as a commit pushed to `main` by the maintainer, or
through a pull request, which runs the same gate without publishing anything:

```powershell
git switch -c short-description-of-the-change
# ... commit ...
git push -u origin HEAD
gh pr create --fill
gh pr merge --auto --squash
```

`main` has no branch protection at present, so nothing but the gate stands
between a push and a published candidate. Run the tests before pushing.

A squash lands one commit per pull request, so the subject line is what the
history reads as. See [Contributing](../CONTRIBUTING.md) for what belongs in it.
