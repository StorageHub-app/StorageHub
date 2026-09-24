# CL.Storage: the last fixes before release

You are fixing eight small, known bugs in the CL.Storage library. The scope is **frozen**: fix exactly these,
add nothing else — no new features, no refactors, no renames. Background, if you need it:
`C:\Projects\StorageHub\docs\storage-library-fit.md` (why the scope is frozen, and which parts StorageHub uses).

## Where to work

- **Repository:** `C:\Users\claus\Documents\GitHub\CodeLogic.Libs` (the library lives in `CL.Storage/`, tests in
  `tests/Storage.Tests` and `tests/Storage.Integration.Tests`).
- **Branch: `feat/storage-needs`** — work and commit directly on it, in the main checkout
  (`C:\Users\claus\Documents\GitHub\CodeLogic.Libs`). It is at `d74e2bd`; check `git status` is clean first.
- **Do not use** any other branch or worktree:
  - not `main`, `release`, `prerelease`;
  - not the older storage branches `feat/storage-vnext` and `docs/storage-vnext` (already contained in
    `feat/storage-needs`);
  - not other libraries' branches (`feat/db-parity-audit`, `feat/mysql2-schema-sync-modes`, `fix/watch-test-flake`);
  - no extra worktrees or branches: work in the main checkout on `feat/storage-needs` only.
- **Do not push, tag, publish or merge into another branch.** Do not change `version.txt`.
- **Do not edit the StorageHub repository** (`C:\Projects\StorageHub`) except to run its acceptance program.
- **Commits:** `git -c user.name="Claus" -c user.email="claus@hlab-dc.media2a.com" commit`, conventional messages
  like the branch history (`fix(storage): …`), **no `Co-Authored-By` or any other attribution trailer**. One commit
  per fix (or per closely related pair).

## The fixes

Paths are relative to `CL.Storage/src`. Each was found by reading the code and has **not** been confirmed by running
it: first write a test that shows the bug (it must fail on the current code), then fix, then see it pass. If a test
shows a finding is wrong, say so in your report instead of changing code. Tag each test
`// needs-review F<number>` and put new unit tests in a new file `tests/Storage.Tests/FinalFixesTests.cs`.

### These affect StorageHub — do them first

**F1. A commit that succeeded is reported as a plain failure when the read-back after it fails.**
`Registry/StagedWriter.cs:462` `ConfirmPromotedAsync` returns a failed read-back (for example a transient
`GetInfoAsync` error) as-is. Its callers — `Registry/StorageTransferPipeline.cs:228` (staged uploads),
`Abstractions/StorageWriteStream.cs:181` (`CommitAsync`) and `Registry/StorageTransferCoordinator.cs:965` — then
report a failure without `destinationState=complete`, although the content was committed. Fix: a read-back failure
after a successful promote must say the destination is committed (the `destinationState=complete` details key via
the existing helpers in `Errors/StorageErrorInfo.cs`, and `DestinationCommitted = true` in transfer reports), and a
transient read-back error may be retried a few times first. A size or digest mismatch stays a real failure.

**F2. `TestConnectionAsync` puts raw exception messages into its errors, and joins a live session pool.**
`StorageLibrary.cs:651` and `:671` build errors from `error.Message`. Everywhere else the library only reports the
exception type (see `StorageErrors.FromException`), because messages can carry hosts, paths or credentials. Fix: use
the same sanitised form. Also: a test of settings identical to a registered connection's shares that connection's
live pool (`Providers/SharedResources.cs`, the pools keyed by `ProviderSettingsKey`), so a test can use or disturb a
live session; a test must build its own client and dispose it. Also make invalid settings give the same error code
from `TestConnectionAsync` and from `AddOrUpdateConnectionAsync` (today `invalid_content` vs `provider_error`) —
pick `invalid_content` for both and update the docs line that states the code.

**F3. The condition-enforcement query answers Atomic for MinIO uploads that are actually staged.**
`GetConditionEnforcementAsync(StorageConditionKind.MatchVersion)` (`Providers/S3/S3StorageBackend.cs:1511`) answers
from the PUT probe, but `Registry/StorageTransferPipeline.cs:62` `NeedsConditionStaging` decides from the
`ConditionalUpdate` flag, which after the probe needs both PUT and COPY enforcement — so on MinIO an upload with a
version condition is staged and checked just before the commit, while the query says Atomic. Make the query and the
upload path agree (either the query reports what the upload path will really do, or the upload path uses the PUT
probe result it could rely on). Test with a fake S3 that enforces PUT conditions and ignores COPY ones.

### These do not affect StorageHub — fix them too

**F4. `DownloadToFileAsync` with `ConflictPolicy.Resume` appends to any shorter local file.**
`Abstractions/StorageServiceExtensions.cs:121` and `ResumeDownloadAsync` (`:188`): a local file that is merely shorter
is treated as the start of the remote file, and an equal size as complete, with no identity or content check — a
different file of the right length gets the rest of another file appended. Fix: resume only when the local prefix is
proven to be this remote version (for example a sidecar recording the remote ETag/version and size written when the
download started, or re-reading and comparing the prefix) — otherwise download from the start. Equal size is not
"complete" without that proof.

**F5. Removing a running queue job can delete a record that needs a person's decision.**
`Queue/StorageTransferQueue.cs:420` `RemoveAsync` → `RemoveHeldAsync` (`:915`): when the attempt ends
`NeedsReconciliation` (for example a move whose copy committed), the remove still deletes the record, so nobody is
told that both source and destination now exist. Fix: a job that ends `NeedsReconciliation` is kept and the remove
fails with `storage.conflict` saying why; all other outcomes are removed as today.

**F6. Failed claims never count toward the store-failure limit, so such a job can retry forever.**
`Queue/StorageTransferQueue.cs` `StoreFailures` (`:979`, `:1421`, `:1534`, `MaxStoreFailures`): failures while
claiming or starting a job are not counted, so a job whose claim always throws retries without end. Count them the
same way (the doubling back-off and the limit of `MaxStoreFailures`). Also: a store-failure requeue publishes a
Retrying event with code `storage.cancelled` — give it `storage.unavailable`.

**F7. `ApplySyncAsync` ignores `DryRun`.**
`Sync/StorageSync.cs:312` only `SyncAsync` honours `StorageSyncOptions.DryRun`; `ApplySyncAsync` with `DryRun = true`
applies the plan. Fix: `ApplySyncAsync` with `DryRun` returns the report of what it would do (every step `NotRun`,
nothing written, baseline not saved), like `SyncAsync`. Update the docs line in `docs/libs/storage/transfers.md` that
now says only `SyncAsync` honours it.

**F8. Two smaller ones.**
- The part-file lock marker (`Registry/PartLease.cs`) on FTP and SFTP: create-only is only checked before the write
  there, so two writers can both read back their own marker. After writing the marker, read it back **after a short
  delay** or re-check it right before the promote (the promote already re-checks ownership — confirm that closes the
  window, and if so just add a test and a comment; if not, fix it).
- `Providers/SharedResources.cs:164` `FlushIdleAsync` is process-wide: stopping one `StorageLibrary` disposes idle
  pools lingering for another library in the same process. Track which library a lingering pool belongs to and flush
  only that library's (or only pools no other library has used).
- Also remove the stale comments in the FTP and SFTP backends that say a WebDAV MOVE is atomic (WebDAV no longer
  declares `AtomicMove`).

## Docs

For every fix, update `CL.Storage/CHANGELOG.md` (the single "since 4.8.93" section, under Fixed or Changed) and, where
a documented behaviour changes, `docs/libs/storage/*.md` and `CL.Storage/README.md`. Keep it short. Add a table
`CL.Storage/docs/final-fixes.md`: F1–F8 → fixed (commit, test) / not a bug (why) / not fixed (why).

## Done means

All of these, run at the end on the final commit:

1. `dotnet build CL.Storage/CL.Storage.csproj -c Release` — 0 warnings, 0 errors.
2. `dotnet test tests/Storage.Tests -c Release` — all pass.
3. The live suite — all pass, none skipped. Start the servers with
   `docker compose -f tests/Storage.Integration.Tests/docker-compose.yml up -d` and set the variables in
   `tests/Storage.Integration.Tests/README.md` (including `CL_STORAGE_TEST_WEBDAV_MTLS_URL=https://localhost:8444/`),
   then `dotnet test tests/Storage.Integration.Tests -c Release`.
4. StorageHub's acceptance program — all checks PASS, "All accepted.":
   ```powershell
   dotnet pack CL.Storage/CL.Storage.csproj -c Release -p:Version=4.8.94-local.20 -p:PackageVersion=4.8.94-local.20 -o $env:TEMP\clfeed
   $env:CL_STORAGE_FEED = "$env:TEMP\clfeed"
   dotnet run --project C:\Projects\StorageHub\eng\cl-storage-acceptance -p:StorageVersion=4.8.94-local.20
   ```
   (Use a new version number each time you pack; NuGet caches by version.)

Report at the end: the commit list, the `final-fixes.md` table, and the four results above.
