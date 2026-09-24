# CL.Storage `feat/storage-needs`: round 4 (verification of the round-3 fixes)

Checked 2026-09-23 at `a8875df` (35 commits on top of `aaf4afd`). The fixer's table is
`CL.Storage/docs/needs-review-round3.md`; round 3 itself follows below, unchanged, for the ids.

## Result

| Check | Result |
|---|---|
| Build, Release | 0 warnings |
| `tests/Storage.Tests` | 756 passed |
| Live suite, every server incl. mutual TLS (Windows) | 153 passed, none skipped |
| Acceptance (`eng/cl-storage-acceptance`) | all 9 round-3 checks PASS; 2 of 4 new checks FAIL (below) |
| StorageHub against it (`4.8.94-local.3`) | builds unchanged; all green except its own order-dependent desktop test |
| The fixer's table | complete: every A, B and D id has a result; all 193 cited tests exist and are tagged; 101 of 112 that compile on the old code fail there, as claimed (exceptions under R4-D) |

**Verified fixed**: nearly all of A1–A29 and B1–B71. Each area was re-read by its own reviewer, and for A1, A4,
A5, A8, A11, A12 and A13 each fix was undone in a scratch worktree and its test failed. `StorageTransferState` is
back to 4.8.93's numbers, every public enum has explicit numbers, and every signature break is in CHANGELOG and
MIGRATION. The queue's store contract can now be implemented in SQLite. The S3 copy rewrite, Swift, the FTP lookup,
the SFTP append and the shared pools are right.

**Not ready yet**: the fixes brought new problems, two of them shown on the lab servers, and a few round-3 items are
only partly fixed.

```
FAIL  R4-1  SFTP (MaxSessions=1) move under ConflictPolicy.Rename: Failed storage.unsupported
FAIL  R4-2  empty "Docs" folder vs new "docs/x.txt": second folder created, next compare storage.conflict
PASS  R4-3  junction + LinkHandling.Follow + Mirror: destination copy kept (R4-B18 not shown)
PASS  R4-4  WebDAV upload onto a folder: refused by the lab server (R4-A8 is server-dependent)
```

## R4-A. Must fix

- **R4-A1 [run R4-1] Same-server renames and moves no longer use the server's rename.** Every
  `ConflictPolicy.Rename` goes to the coordinator (`StorageLibrary.cs:1011-1014`), and WebDAV, no longer
  `AtomicMove`, pins every file move, which WebDAV answers Unsupported (`WebDavStorageBackend.cs:452`), so it relays.
  A same-server rename becomes copy + promote + delete; on FTP, SFTP and WebDAV the whole file goes through the
  client; with `MaxSessions = 1` it fails outright. StorageHub's KeepBoth rename and every WebDAV rename are hit.
  Choose the free name first, then rename natively, as 4.8.93 did.
- **R4-A2 [run R4-2] A19 is only partly fixed: an empty folder has no spelling.** `FolderSpellings`
  (`StorageCompare.cs:345-357`) records only the parents of items, never a directory item itself, so an empty
  `Docs/` (or one holding only excluded items) is missing and a new `docs/x` is written beside it: the original
  failure. Also, when the source file is absent, `path` takes the baseline's spelling (`StorageSync.cs:476`), so
  CopyToSource writes into an old folder spelling. Add each directory's own path; use `entry.RelativePath`.
- **R4-A3 A9 is still open where the provider enforces the condition.** `PromoteCoreAsync` returns
  `Touched: true` for every provider failure, a 412 included (`StagedWriter.cs:394`), so `RecoverReplacementAsync`
  (`Coordinator.cs:1147-1152`) finds the destination missing and copies the backup back. Shown by a reviewer's
  probe: destination deleted concurrently, the conditional move answers 412, the deleted file comes back with
  `BackupRestored = true`. This is exactly the S3/Azure/GCS path. A condition refusal is "not touched".
- **R4-A4 A failed confirm after commit deletes the only copy of the previous version** (`Coordinator.cs:915`). If
  the mismatch comes from a broken promote rather than a concurrent writer, old and new content are both gone and
  `BackupLeftBehind` is null. Keep the backup and report it; that still honours "never roll back".
- **R4-A5 A "non-recursive" folder delete on FTP and WebDAV is a check followed by a recursive delete.** FTP lists
  without hidden files (`FtpStorageBackend.cs:482-485`) and then calls FluentFTP `DeleteDirectory`, which removes
  the contents; WebDAV checks at depth 1 (`:366-372`) and then sends `DELETE`, which is always depth-infinity. A
  file that arrives in between, or a hidden file the FTP listing skipped, is destroyed. Callers: directory-move
  source cleanup (`StorageLibrary.cs:1487-1491`, the new merge included), sync folder deletes
  (`StorageSync.cs:1195-1200`), rollback of created folders (`Coordinator.cs:1278-1285`). Use a raw `RMD` on FTP;
  on WebDAV never `DELETE` a collection for a non-recursive delete, or lock it first.
- **R4-A6 The S3 probe finds `Rejected` and then ignores it.** The headers are still sent
  (`S3StorageBackend.cs:1197`, `:1325`, `:1419`; `SendsConditions` at `:1437` looks only at the setting). On a server
  that answers 400/501 to them, every create-only copy fails, and every single-file move's source delete fails,
  ending NeedsReconciliation with the source left; before round 3 that delete simply worked. An inconclusive probe
  is never cached, so every conditional operation probes again (2 PUTs, 2 COPYs, 3 DELETEs). On versioned or
  object-lock buckets the probe leaves versions and delete markers, and it fires notifications: document it.
- **R4-A7 Under S3 `Auto`, the capability flags still say ConditionalCreate/Update/Delete whatever the probe
  found** (`S3StorageBackend.cs:145`), and the per-connection enforcement query is internal
  (`StorageServiceProxy.cs:285`, `StorageConditionKind`). Sync, `DeleteIfUnchangedAsync` and StorageHub read the
  flags as atomic; on MinIO a delete is really check-then-delete. Publish trimmed flags after a conclusive probe, or
  make the enforcement query public. `transfers.md:137` ("only while it is still the version copied") needs the
  check-before window stated for Local, FTP, SFTP, WebDAV, Swift and MinIO.
- **R4-A8 A WebDAV upload with Overwrite onto an existing folder sends `MOVE` with `Overwrite: T`**
  (`WebDavStorageBackend.cs:235`); RFC 4918 deletes the destination collection first. The round-3 guard is only in
  `ServerTransferAsync` (`:478`). The lab server refuses (R4-4 passes), but a compliant server such as Apache
  mod_dav deletes the folder. Read the destination first and refuse a collection, as Local, FTP and SFTP do.
- **R4-A9 Resume across processes can mix data on Local, FTP and SFTP.** The one-writer lock is per process
  (`StagedWriter.cs:122`) and the size check after writing (`:309-315`) races: another process appending (`APPE`,
  an open handle) while this one renames puts its bytes into the committed file. Two queue workers are enough.
  Create a lock marker beside the part file exclusively, or use a part file per attempt. (The in-process lease is
  also released at the end of `WriteAsync`, before the promote: a false failure only.)
- **R4-A10 The client-certificate diagnosis still turns ordinary drops into non-retryable errors.**
  `TlsConnectionWatch.Dispose` records a refusal when fewer than 64 bytes arrived after our certificate reply
  (`TlsDiagnosis.cs:311-322`). On TLS 1.3 with a server that *requests* a certificate, SocketsHttpHandler's spare
  connections read 0 bytes and are later scavenged; each records a refusal, and `Enrich` accepts any refusal after
  the attempt began, so a long upload that drops meanwhile becomes `client_certificate_rejected`. The recorder is
  still backend-wide. Behind an HTTP proxy tunnel the watch is not found and detection is silently lost.
- **R4-A11 B9 must be fixed, not documented.** An upload resumes with only `SourceIdentity` set to a path
  (`StorageTransferPipeline.cs:146` requires identity *or* time): an edited file of the same length resumes onto the
  old prefix unless `Verify` is on. Silent corruption. Refuse a resume without `SourceLastModified` unless the
  identity is marked a content version.

## R4-B. Should fix

**Transfers**

1. A11's leftovers are still lost outside single-file transfers: `CopyDirectoryAsync` never merges each file's
   `BackupLeftBehind`/`StagingLeftBehind` (`Coordinator.cs:372-377`, `:443-453`), and `PromoteAsync` drops
   `LeftBehind` (`StagedWriter.cs:342-353`), so staged uploads and `StorageWriteStream` never report them.
2. Directory transfers still roll back files they committed, check-then-act (`RestoreIfStillOursAsync` copies back
   with `Overwrite=true`, `:1338-1341`; `DeleteIfStillOursAsync` deletes unconditionally, `:1323`; an unknown
   identity counts as ours, `:1307`), while `transfers.md:48` says a committed destination is never rolled back.
   Pass the committed ETag as a condition where possible, and reword the docs.
3. Azure waits for a pending copy with the caller's token (`Azure:528`): a cancel leaves a failed copy over the old
   content, and recovery then deletes the backup. A move deletes the source's snapshots silently
   (`IncludeSnapshots`, `:458`).
4. S3: a cancel during `CompleteMultipartAsync` (`:1328`) or a single CopyObject reports a committed destination as
   cancelled (a move then keeps its source). Lost-completion recognition HEADs without a version (`:1361`).
   Complete with `CancellationToken.None`.
5. A cancel or exception after commit on the relay path returns before events are published, and reports
   `SourceDeleted = false` when some sources were already deleted (`StorageLibrary.cs:1275-1294`). The event
   summary says `Files = 1` for a directory (`:1225`).
6. `IStorageRestoringReplace` is declared but never checked: the coordinator drops the backup for any destination
   without ServerSideCopy (`Coordinator.cs:815`), so a third-party backend without a restoring replace loses the old
   file if its promote fails.
7. Report accuracy:
   - directory transfers never report `ConditionEnforcement`;
   - a native directory move reports `Files = 0`, `Bytes = 0`;
   - on the WebDAV relay fallback `PhaseChanged(Committing)` has already fired (`:1118`), so a crash leaves the queue
     job Interrupted;
   - the cancel path drops `StagingLeftBehind`, `BackupRestored`, `ResumeToken` and the copy event (`:1275-1283`).
8. The `StorageWriteStream.CommitAsync` doc still says nothing is left at the destination on failure (`:130`).

**Queue**

9. Pause, cancel and remove now wait for the attempt to stop (`Queue.cs:762`). With the default token they never
   return while a provider ignores cancellation. Bound the wait or return once requested, and document it.
10. A failed or refused remove releases its hold without a Pump (`:801-810`; `ReloadAsync` likewise): a queued job
    can stay unstarted, and `WaitForIdleAsync` never returns.
11. The newer-schema check is bypassed: `StepAsideAsync` (`:1312`) and the outcome paths (`:1205`, `:1234`) adopt a
    fresh record without `IsReadable`, so in a mixed-version fleet an older worker runs a newer job.
12. A store save that always throws (serialization, a constraint) retries the job for ever, about every second,
    because store failures do not use up retries (`:1417-1425`).
13. An unexpected throw between `:1195` and `:1240` leaves taken requests unanswered and the hold raised: the job is
    held for good, and callers without a token hang. Answer and settle in `finally`.
14. Handlers without an `EventContext` run inline on the attempt thread (ProgressChanged under Throttled's lock). A
    handler that synchronously waits on `CancelAsync` for that job deadlocks.
15. The store contract:
    - revisions restart at 1 when an id is removed and added again, so a process holding the old revision-1 copy can
      save over or remove the new job (`JobStore.cs:10-12`, `:107`); continue revisions as fencing tokens now do;
    - say that a JSON store must skip rows it cannot read (`FromJson` throws on newer rows);
    - say that the columns win over the lease copies inside the JSON.
16. Partly fixed:
    - B36: refresh, lapsed-lease recovery, settle work and attempts that outlive the timeout still use the store
      after `DisposeAsync` returns.
    - B38: moves among three or more equal orders return Conflict; a swap is still two saves.
    - B43: a queue still opening when the library stops is never disposed, and the synchronous `Dispose()` blocks
      up to 30 s.
    - A16: each new attempt resets the phase to NotStarted (`:1114`), so a retried directory copy that had committed
      files can be re-queued as untouched after a crash.
    - Also: a refresh ghost (`:433-438`); a null `jobId` still throws.

**Sync and compare**

17. One-way plans ignore `DestinationExclusions` (`StorageSync.cs:365-369`). A destination item left out (a hidden
    `desktop.ini`, a skipped link) makes the create-only copy Stale every run and withholds every Mirror delete for
    good; through a destination folder link it writes into the link's target.
18. `LinkHandling.Follow` with Local directory links. Local no longer descends into them
    (`LocalStorageBackend.cs:606-611`), and Follow only swaps in the target's own item (`StorageCompare.cs:687-696`),
    so by the code a followed folder lists empty and Mirror or two-way would delete its copies; a followed file link
    is Stale every run and withholds deletes. Not shown with a junction (R4-3); needs a test with a real symlink.
19. A23 on `ListAsync`: the hidden filter runs per page on S3, Azure, GCS and Swift, so `.git/b` on page 2 is listed.
    Test every path segment.
20. Sync decides deletes and overwrites with the ±2 s tolerance (`planned.Matches(current, tolerance)` at
    `StorageSync.cs:1202`, `:1125`, `:981`): on FTP/SFTP/WebDAV without ETags a same-size edit inside it is deleted or
    overwritten (FTP LIST has minute resolution). Use zero at apply, as `CopyThenDeleteAsync` does.
21. Plan binding:
    - the plan is bound to connection ids (`:161-163`), so any re-registration between plan and apply refuses it
      (StorageHub re-registers today; its own change, below);
    - `ApplyWithConflicts` and `HashConcurrency` are in the fingerprint, so "see conflicts, then apply the rest"
      needs a new plan;
    - the baseline itself is not bound to connections.
22. Smaller:
    - a folder delete is Stale for ever when a crashed transfer left staging inside it (`:1197-1199`);
    - exceptions still escape planning (a regex timeout, a throwing provider enumerator);
    - a step that really failed while stopping is reported NotRun;
    - the baseline keeps every entry below a newly excluded tree indefinitely (B45);
    - A25's read-back failure records a plan identity without ETag or time.

**Providers and TLS**

23. B63: the Local ETag is labelled weak, but `StagedWriter.SameETag` strips `W/` and compares it as strong
    (`:471`). It is still used for resume and "unchanged while streaming".
24. B64: under `Auto`, uploads are trusted Atomic without a probe, which is the old problem for Ceph/Wasabi/B2-class
    servers.
25. The SSE-C checksum branch (`S3:1644`) cannot be reached on real S3: a HEAD without the key returns 400.
26. The Windows `MachineKeySet` fallback (`ClientCertificates.cs:36`) triggers on any CryptographicException, a wrong
    password included, and uses a machine-wide container.
27. The case probe uses .NET invariant mapping on the first lettered entry (Georgian and similar names misjudge NTFS),
    and runs once in the constructor. Restrict it to ASCII names.
28. Multipart copies use the 16 MiB upload part size (a 6 GiB copy is about 380 part calls). The WebDAV move onto an
    existing collection has a small check-then-MOVE window.

## R4-D. Docs, tests and release

- Name the new enum values callers will receive: `StorageSyncActionKind` 4–7 (DeleteFromSource,
  CreateDirectoryAtSource, RenameAtDestination, Conflict) and `StorageDiffReason.Undecidable` (32); and the
  `EnqueueDownloadAsync` `options` parameter.
- Document the check-before windows (R4-A7), MinIO and Swift staged uploads and sync create-new dropping from atomic
  to checked-before (uploads do not report it), and that SFTP relays need two sessions (`transfers.md:80-83` says
  one).
- Table claims:
  - `Sides_keeping_different_checksums_download_only_one_side` (B60),
    `A_connection_failure_while_hashing_still_fails_the_comparison` (B57), `Every_public_enum_has_distinct_values`
    (A14) and two `Rename_candidates` cases pass on the old code. Each has a partner test that fails, so reword the
    header rather than add tests.
  - B3 should cite `A_tampered_resume_token_is_not_followed`.
  - A4's coordinator guard has no test of its own (`Validate` refuses first).
  - `SyncTests.cs:195` still waits `Task.Delay(300)` for the watcher.
- Tests that assert too little: A1/B13 error details; A8's directory case (Outcome, `BackupLeftBehind`); A2 only the
  relay branch; A28 only the flag function; B3 none.
- Other compose images still float (`quay.io/minio/minio`, `bytemark/webdav`, `atmoz/sftp:alpine`).
- `README.md:20` and `index.md:40`: `await CodeLogic.ConfigureAsync()` resolves to the namespace (it was already in
  4.8.93).
- The proposal (`storagehub-needs-proposal.md`) is complete, with these weak spots:
  - #1: how `SaveAsync(StorageSyncCommit)` versions the store interface;
  - #2: what `CanEnforceAtomically` says before the probe has run;
  - #7: costed S, but must cover Block and NeedsReconciliation and how the hook interacts with retries;
  - #8: what "root identity" is per provider;
  - #13: split it;
  - #14: decide between `CommitAsync` and a new method;
  - #16: give the cap numbers.

## For StorageHub (no library work)

- Use stable connection ids, because plans and stored jobs bind to them.
- Use a global fencing counter in its store.
- Keep the store open after `DisposeAsync` returns.
- Always pass a timeout token to pause, cancel and remove.
- Give the service and the desktop different WorkerIds.

---

# Round 3: complete review

Reviewed 2026-09-23 at `aaf4afd` against the published 4.8.93 (`6343f11`). This replaces rounds 1 and 2: what
they found is either fixed (listed at the end) or repeated here. Paths are relative to `CL.Storage/src`.

## How this round was done, so the next one is short

- **Every changed file read in full by exactly one reviewer** (queue; sync; compare/watch; staged writer and
  coordinator; library, pipeline and models; providers; docs, API and tests), plus two reviews across files:
  every operation traced through every provider, and the library checked against what StorageHub needs to
  delete its own code (section F).
- **The public API diffed by reflection** (4.8.93 against HEAD) and every doc sample compiled.
- **Unit and live suites run**: 547 unit, 132 live on Windows with every server including mutual TLS; all pass.
- **StorageHub built and tested against it**: builds unchanged, all green (one order-dependent desktop test
  is StorageHub's own).
- **Acceptance checks**: the findings that can be shown on the library's own servers are now a program,
  `eng/cl-storage-acceptance` in StorageHub. It prints PASS/FAIL per finding id. **Run it after the fixes**;
  every check must pass. Today: 7 of 9 fail.

```
FAIL  R2-1  parent folder spelled differently: second "Docs" folder beside "docs", next compare fails
FAIL  R2-2  S3 create-only copy: ETag "…-2", server MD5 gone
FAIL  R2-3  StorageTransferState: Completed=3 (was 2), Failed=4 (was 3), Cancelled=5 (was 4)
FAIL  R3-5  Exclude "**/node_modules": the folder's files were copied
FAIL  R3-6  IncludeHidden=false: .git/config was copied
PASS  R3-7  Windows Hidden attribute + Mirror to S3: remote copy kept (claim not confirmed)
FAIL  R3-8  SFTP folder moved onto an existing folder: the existing folder's file is gone, outcome Completed
FAIL  R3-9  Swift upload with a wrong If-Match: succeeded and overwrote the file
```

Markers: **[run]** shown by running it (acceptance check or a reviewer's scratch program); **[known]** raised
in an earlier round and still present.

---

## A. Must fix before publishing (data loss, wrong results, stuck states)

### Transfers

- **A1 [run] A directory move deletes source files it never copied.** With nothing skipped, the move ends in
  one recursive delete of the source (`StorageLibrary.cs:1119-1127`, `:1237-1264`). A file added to the source
  during the copy ends up on neither side, and the report says Completed, `SourceDeleted=true`. The partial
  path deletes each copied file without checking it is unchanged. Fix: always delete per file with the
  identity read at listing time, then remove emptied folders without recursion.
- **A2 A native same-connection move on S3, Azure, GCS and Swift deletes the source unconditionally.** Copy
  (not pinned to the version read), then `DeleteAsync(Recursive=true)` with no condition
  (`Providers/S3/S3StorageBackend.cs:471-481`; Azure `:428-438`, GCS `:527-537`, Swift `:438-448`); it never
  goes through `DeleteMovedSourceAsync`. A writer updating the source in between loses its version. Also
  sync's `RenameAtDestination` (`Sync/StorageSync.cs:764`). Fix: pin the copy to the ETag/generation read
  (`x-amz-copy-source-if-match`, Azure source conditions, GCS `IfSourceGenerationMatch`) and delete with
  the same condition, or route object-store moves through the relay's conditional delete.
- **A3 [run R3-8] A directory moved onto an existing directory destroys it on FTP, SFTP and WebDAV.** The
  existing destination is renamed to a backup and deleted recursively (FTP `:858-914`, `:990-991`; SFTP
  `:821-858`, `:920-925`); WebDAV sends `Overwrite: T`. Local refuses the same move; a relay merges. Fix:
  refuse when a directory destination exists, or merge.
- **A4 [run] A resume token turns on overwrite.** `overwrite = options.Overwrite || resumable`
  (`Registry/StorageTransferCoordinator.cs:505`, `:569`). Replaying a token with `Overwrite=false` after
  someone else created the destination replaced their file. The queue replays stored tokens with the job's
  options, so it would do exactly this. Fix: only `ConflictPolicy.Resume` implies overwrite; reject a token
  with `Overwrite=false` in `Validate`.
- **A5 Two transfers of the same source to the same destination corrupt the staged file.** The resumable part
  file has a fixed name (`Registry/StagedWriter.cs:67-71`) and both append into it (`:178`, `:373-381`); each
  checks only its own byte count, and the size check after promote is ignored unless `Verify` is on
  (`Coordinator.cs:806-808`). Two queued jobs for one file are enough. Fix: a lock/lease on the part file or
  a part file per attempt, and always fail on a size mismatch after promote.
- **A6 A failed size read of the part file duplicates content.** Any `GetInfoAsync` failure on the part file
  gives offset 0 (`StagedWriter.cs:375-377`), and the whole source is appended after the existing prefix.
  Only NotFound may mean 0.
- **A7 A fully staged resume from S3 or Azure fails on every retry.** Opening at `offset == size` becomes a
  range the server answers with 416 (`StagedWriter.cs:155`; S3 `:329-332`), and the part file is kept for the
  next attempt, which does the same. Skip the open when nothing remains.
- **A8 A failed confirm after promote rolls back over another writer.** A Verify mismatch after commit makes
  `RollbackAsync` restore the backup with `Overwrite=true` or delete the "created" file
  (`Coordinator.cs:806-808`, `:1017-1021`); if the mismatch came from a concurrent write, that write is lost,
  and the report still says `DestinationCommitted=true`. After commit, report; never roll back.
- **A9 [known] A destination deleted concurrently is brought back** by the backup restore
  (`Coordinator.cs:986-998`). A condition failure inside the promote should only drop the backup.
- **A10 [known, run R2-2] S3 create-only copies are multipart, for every size.** Parts are not pinned (a source
  replaced mid-copy mixes versions; a *longer* replacement is silently truncated to the old size); only
  ContentType and user metadata survive (Content-Encoding, Cache-Control, Content-Disposition, tags, storage
  class, SSE settings are dropped — a gzip object loses its encoding); the ETag becomes `…-N`, so the MD5
  checksum is gone and sync falls back to reading content; parts are copied one at a time
  (`S3StorageBackend.cs:458-461`, `:1045-1100`). This hits every create-new promote, backup copy and
  same-bucket staging copy. Fix: pin every part (`CopySourceIfMatch` or the version), carry the properties
  and tags, and keep a single `CopyObject` + `If-None-Match` below 5 GiB where the server enforces it.
- **A11 [known, widened] A commit that succeeded is reported as not committed** when anything after it fails:
  a cancel between S3's copy and delete; FTP/SFTP returning PartialFailure because deleting their *own*
  backup failed (FTP `:996-1000`, SFTP `:928-933`); S3 failing to delete staging (`:476-480`). The report
  says `DestinationCommitted=false`, the library's backup is dropped, the backend's `.cl-storage-backup-*` is
  never reported, and a token may be issued for a part file that is gone. Same in `StagedUploadAsync`,
  `StorageWriteStream.CommitAsync` and sync. Fix: such errors carry `destinationState=complete`, and the
  promote maps them to success plus a reported leftover.
- **A12 A cancelled `WriteAsync` on `StorageWriteStream` duplicates bytes on retry.** The bytes are already in
  the pipe when the backpressure wait is cancelled; `_written` is not advanced and the stream stays open
  (`Abstractions/StorageWriteStream.cs:81-82`). Fault the stream on any write exception.
- **A13 A native copy or move that succeeded can be reported Cancelled** if the cancel lands during the
  `GetInfoAsync` after it (`StorageLibrary.cs:1066-1090`, catch `:1143-1151`). Build the committed report
  first; read with `CancellationToken.None`.

### Queue

- **A14 [known, run R2-3] `StorageTransferState` renumbered.** `Paused` inserted at 2 moves Completed, Failed
  and Cancelled from 2/3/4 to 3/4/5 (`Queue/StorageTransferJobs.cs:27-47`); a stored 2 now reads as Paused.
  Not in CHANGELOG or MIGRATION. Put `Paused` at the end, with explicit numbers, as was done for
  `StorageSyncActionKind`. Give the new enums (Outcome, ConflictKind, ConflictPolicy, …) explicit numbers too.
- **A15 A cancel or pause after a move committed loses NeedsReconciliation.** `Decide` checks the caller's
  stop before the report's outcome (`Queue/StorageTransferQueue.cs:993-998`; the error is rewritten at
  `:850`). A move cancelled while deleting its source ends Cancelled, is pruned, and nobody is told the
  source and destination both exist; pause and resume re-runs the whole move.
- **A16 A crash after commit re-queues finished work.** Upload and download jobs only ever record
  Transferring (`Queue.cs:941-956`), and a directory copy's per-file phases move the recorded phase back from
  Committing to Transferring (`Coordinator.cs:256`, `:612-613`). Recovery (`Queue.cs:442`) then treats written
  destinations as untouched and requeues them: a false failure under Fail, a duplicate under Rename.
- **A17 [known, widened] Removes are unconditional and race.** `RemoveAsync`, `ClearAsync` and pruning delete by
  local view with no revision or lease (`Queue.cs:352`, `:1127`; store `:56`), so they can delete a job
  another process is running. `RemoveAsync` on a Queued job races `Pump` (the job can start after Remove
  returned Success; `JobRemoved` fires twice). Removing a running job ignores a store failure (`:878-884`),
  so the record stays Running and runs again after the lease lapses. Pruning races `RetryAsync`.
- **A18 Control requests made during a claim or the final save are acknowledged and then dropped.** Cancel,
  pause or remove sets flags that `StepAsideAsync` and the next `Pump` reset (`Queue.cs:646-652`,
  `:905-917`); after `Decide` (`:857`) a cancel returns Success and a transient retry still goes ahead.

### Sync and compare

- **A19 [known, run R2-1] A parent folder spelled differently on each side.** Only the item itself keeps its
  per-side spelling (`Sync/StorageCompare.cs:195-215`, `Sync/StorageSync.cs:699`). New files and folders under
  `Docs/` go to a case-sensitive destination as a second `Docs` beside `docs`, and the next compare fails with
  `caseCollision` for good. Also: two-way copies of destination-only files into a case-sensitive source;
  KeepBoth conflict renames; entries sorted over mixed spellings, so a tree built from them has orphans.
  Fix: a folder-key → spelling map per side, applied to every OnlyIn*, CreateDirectory and conflict path.
- **A20 A delete that could not read the file is reported as done, and the deletion is later undone.**
  `Current()` turns every `GetInfo` failure (timeout, 503, permission) into "absent", and `DeleteAsync` then
  returns Success (`StorageSync.cs:876-877`, `:893-897`): the step is Applied, the baseline entry removed,
  the file still there, and the next run copies it back. The same swallowing turns a transient source read
  into Stale with no retry. Only NotFound is absent.
- **A21 On a case-insensitive pair, a withheld, stale, failed or not-run delete drops its baseline entry.**
  The delete uses the destination's spelling, the baseline key is the source's (`StorageSync.cs:329`, `:373`,
  `:921-927`); the lookup misses and the entry goes. A blocked DeleteVersusModify conflict is dropped the same
  way. Next run the file is Created and copied back: the deletion is undone, the conflict silently resolved.
- **A22 [run R3-5] Excluding a folder does not exclude its contents.** `Exclude = ["**/node_modules"]` matches
  only the folder entry (`StorageCompare.cs:448`); its files are synced. The excluded set is unbounded in
  memory. Prune below excluded folders, as .gitignore does, or document the difference loudly.
- **A23 [run R3-6] `IncludeHidden = false` does not hide the contents of hidden folders.** `.git/config` is
  synced (`Providers/StorageListFilter.cs:25` tests only the item's own name), and hidden items are not
  recorded as excluded, so Mirror plans a non-recursive delete of a folder that still holds hidden files and
  fails every run.
- **A24 Windows reparse points are treated as links and skipped, and Mirror then deletes their copies.** Any
  file with the ReparsePoint attribute is a Link (`Providers/Local/LocalStorageBackend.cs:589-593`) — OneDrive
  Files-On-Demand placeholders, dedup files, AppExecLinks. Skipped by default, they become OnlyInDestination
  and a Mirror deletes the copies (`StorageSync.cs:247-255`: file deletes never check the source's excluded
  set). Not run (needs OneDrive); follows from the code. Treat only real symlinks/junctions as links, and
  never delete a destination path that was excluded on the source.
- **A25 [known, widened] A successful copy can lose its baseline entry**, when the read after promote fails,
  the size differs, or a cancel lands between promote and `SetTimestampsAsync` (`StorageSync.cs:855-866`,
  `:685-689`): the next run sees a BothCreated/Modified-both conflict (with KeepBoth, a conflict copy of
  identical content). A same-size write by someone else in that window is recorded as ours. Confirm against
  the staged content, record the planned identity when unsure, and use `CancellationToken.None` after commit.

### Providers and TLS

- **A26 [run R3-9] Swift declares ConditionalUpdate and ConditionalDelete, and ignores If-Match on PUT.** The
  pipeline therefore skips its own check (`Registry/StorageTransferPipeline.cs:54-56`) and an upload with a
  wrong ETag overwrites (`Swift:260-262`); the delete test passes only because of a client-side pre-check.
  Drop both flags so staging checks before commit.
- **A27 [known, widened] Ordinary network drops become non-retryable credential failures.** `TlsDiagnosis.Enrich`
  (`Providers/TlsDiagnosis.cs:109-118`) turns any `connection_lost` in an attempt where a certificate request
  was recorded into `client_certificate_rejected`. The recorder is per backend and time-based: with parallel
  transfers one new connection makes every concurrent drop a credential error, so a server restart blocks
  every running job; servers that *request* but do not require a certificate (nginx `optional`, IIS accept)
  trigger it on every handshake. Separately, `IsPlatformTlsFailure` (`:64-73`, `ProviderErrorMapper.cs:64`)
  classifies mid-stream errors — OpenSSL "unexpected eof", "bad record mac", SEC_E_DECRYPT_FAILURE,
  SEC_E_MESSAGE_ALTERED — as `handshake_failed`, also non-transient; SSPI Negotiate errors would be labelled
  `client_certificate_rejected`. Fix: record only when the server actually asked (`acceptableIssuers` or a
  remote certificate), per connection; count only handshake-phase failures; keep `connection_lost` transient
  with `tlsReason` as a hint.
- **A28 Client certificates fail on macOS.** `EphemeralKeySet` is not supported there
  (`Providers/ClientCertificates.cs:172`), so any FTP/WebDAV registration with a certificate throws
  `PlatformNotSupportedException`; the XML doc, README:152 and CHANGELOG:34 promise the key stays in memory
  on macOS.

### Release

- **A29 Sync cancellation silently changed from an exception to a successful result.** `ApplySyncAsync` and
  `SyncAsync` now return `Success` with `Cancelled = true` (`StorageSync.cs:165-184`); 4.8.93 threw. The
  CHANGELOG (`:123`) says sync "still throws", as do `MIGRATION.md:110`, `README.md:858`,
  `docs/libs/storage/errors-events.md:9` and `index.md:13` (which also contradict `CopyAsync`/`MoveAsync`).
  A caller relying on the exception now treats a cancelled sync as done.

---

## B. Should fix

### Transfers and writes

1. Moving a single link with `LinkHandling.Skip` deletes the link and reports Completed: the skip is not
   counted (`Coordinator.cs:433`, `StorageLibrary.cs:1103`).
2. [run] A resume into a folder the transfer created ends as NeedsReconciliation: the rollback cannot remove
   the folder holding the kept part file (`Coordinator.cs:100-110`, `:1123-1128`). Keep those folders.
3. Token cleanup deletes any `.cl-storage-part-*` in the folder, recursively, without checking the token was
   for this destination — it can delete another running job's part file (`Coordinator.cs:672-676`,
   `StagedWriter.cs:327`).
4. A bogus resume token can be issued for a random `.cl-storage-transfer-` object when a staging delete
   failed on a source without identity (`Coordinator.cs:709`, `StagedWriter.cs:115`).
5. `ExpectedSha256` without `Verify` skips the check after promote, though the docs say it implies Verify
   (`Coordinator.cs:807`).
6. Exceptions other than cancellation leak staging and backup and skip rollback (`Coordinator.cs:94`,
   `StagedWriter.cs:110`): a throwing `PhaseChanged`/`IProgress`, provider, open delegate, or
   `SourceTooLongException`.
7. Resumable part files are discarded, with no token, on recoverable failures: backup allocation/copy,
   promote, a transient re-read of the source (reported as Conflict), a transient or cancelled confirm
   (treated as wrong content), a short source, a failed prefix download (`Coordinator.cs:726-731`, `:747`,
   `:761`, `:782`; `StagedWriter.cs:207`, `:221-223`, `:401-402`).
8. Staged uploads and `OpenWriteAsync` never enforce a `Condition` atomically — check, then plain move —
   even on S3/Azure/GCS where a plain upload's condition is atomic, and the enforcement level is not returned
   (`StorageTransferPipeline.cs:87-88`, `:184`; `StagedWriter.cs:245-261`; `StorageWriteStream.cs:117-127`).
   Pass the condition to the provider's move/copy, or document the downgrade and return the level.
   (`CommitAsync` also checks it twice.)
9. The `SourceIdentity` doc suggests a local path; with only that set, an edited file of the same length
   resumes onto the old prefix unless `Verify` (`Models/StorageOptions.cs:55-61`, pipeline `:137`, `:167`).
   Require a time or content version, or say the identity must change with the content.
10. Conditional policies (IfNewer/IfSizeDiffers) decide against the latest version when a version is pinned
    (`StorageLibrary.cs:985-997`).
11. `Rename`: a directory at the target fails instead of taking a new name; a name taken between check and
    promote fails instead of trying the next; `name (1).txt` becomes `name (1) (1).txt`; `file.` becomes the
    Windows-invalid `file (1).` (`Registry/StorageConflictResolver.cs:54-55`, `:140-151`).
12. Upload validation is looser than transfer validation: `Condition` with Rename/Skip/Fail is accepted (with
    Rename it always conflicts); empty `SourceVersionId`/`ExpectedSourceETag` pass (`StorageOptions.cs:96-111`).
13. A cancelled directory transfer whose rollback failed is reported as a clean `Cancelled`
    (`Coordinator.cs:~92`); report NeedsReconciliation.
14. A staged-upload failure replaces the provider's details with the staging info, dropping `retryAfterMs`,
    `httpStatus` and `tlsReason` (`StorageTransferPipeline.cs:178`). Append instead.
15. The "already complete" check hashes the local source through the speed-limited stream: a 10 GB file
    already uploaded takes as long as uploading it, and progress runs to 100% twice (pipeline `:143`,
    `:205-213`).
16. Upload speed limits do not apply to direct appends on resume (`StagedWriter.AppendAsync`).
17. FTP and SFTP pay for a backup through the client on every overwrite — a download and re-upload — although
    their promote makes its own rename backup; with `MaxSessions = 1` the nested download+upload waits out the
    30 s acquire timeout and fails, as does every same-connection relay (`Coordinator.cs:738-769`; FTP
    `:520-528`, SFTP `:482-490`). Skip or rename-back up there; document that 2 sessions are needed.
18. WebDAV treats 207 Multi-Status on COPY/MOVE as success (`WebDav:672`): a partly failed collection MOVE
    reports Completed with `SourceDeleted=true`. `AtomicMove` overclaims for collections.
19. On versioned Swift containers every sync copy and cross-connection move fails: items carry `VersionId`,
    but versioned downloads and version conditions are Unsupported (Swift `:245`, `:291-293`, `:382`, `:841`).
20. The native fast path ignores `MetadataPreservation` and `LinkHandling` (`StorageLibrary.cs:1023`):
    `Discard` still copies metadata; Local `Directory.Move` carries links though the default is Reject.
21. An S3 `CompleteMultipartUpload` whose response is lost and retried gets 412/NoSuchUpload and reports
    failure over our own committed object (`S3StorageBackend.cs:1077-1085`). HEAD and compare the multipart
    ETag.
22. SFTP append now opens and seeks to the end client-side instead of using the server's append flag
    (`SftpStorageBackend.cs:653-658`): concurrent appenders overwrite each other.
23. The S3 backend's own `CopyAsync` silently ignores `SourceVersionId`, `ExpectedSourceETag` and
    `DestinationCondition` when called directly (`S3StorageBackend.cs:430-470`); reject them.
24. `StorageWriteStream`: a synchronous `Dispose()` after commit never disposes `_cancel` (a registration leak
    on the caller's token); `DisposeAsync` during `CommitAsync` disposes it under the commit; disposal
    rethrows upload faults; the storage error is wrapped in a private exception type the caller cannot read
    (`:80`, `:107`, `:148-149`, `:163-183`, `:199`).
25. The docs say `CopyAsync`/`MoveAsync` no longer throw, but a pre-cancelled token throws
    (`StorageLibrary.cs:939`), an unknown connection throws `KeyNotFoundException`, and the proxy's
    `ToResult()` turns a cancel into a failure while backends throw — so sync's conflict rename is Failed, not
    NotRun, on cancel.
26. No event is published when a move needs reconciliation (the destination committed, source kept), so
    watchers and caches miss the new file (`StorageLibrary.cs:1123-1136`).

### Queue

27. A Queued job leased by another worker gets no wake-up and keeps the queue busy forever: `leaseEnds` only
    covers Running jobs, and `UpdateIdle` counts it (`Queue.cs:631-633`, `:671-673`, `:1171`). This is the
    window `Two_queues_on_one_store_run_each_job_once` depends on, so that test can flake.
28. StepAside spins on the store when a claim is refused and the record has not changed (`Queue.cs:909`).
29. [known] One failed final save turns a successful copy into Interrupted instead of saving again while the
    lease holds (`Queue.cs:861-873`; `ReviewQueueTests:109-113` asserts Interrupted).
30. A store call that commits and then throws leaves the local copy a revision behind: the outcome save is
    refused and the job ends Interrupted after a store hiccup (`Queue.cs:818-824`, `:861`). Phase saves should
    use `CancellationToken.None`; after a throw, re-read and adopt if the fence is still ours.
31. Pruning: the "next pass tries again" comment is wrong (the entry left `_jobs`); pruning only runs after an
    attempt, never after a cancel or on load (`Queue.cs:1123-1128`).
32. `RefreshAsync` never forgets jobs gone from the store; a stale snapshot brings removed jobs back as ghosts
    (`Queue.cs:381-388`).
33. `EnqueueAsync` replaces an existing entry (`_jobs[id] = new Entry`), which can orphan a running attempt
    whose `Release` then removes the new entry (`Queue.cs:204-209`).
34. Methods returning `Result` throw `OperationCanceledException` after the store may have committed; a store
    read failure on enqueue is reported as Conflict (`Queue.cs:174-198`, `:537-541`).
35. `EventContext.Post` is not guarded: a throwing context (after UI shutdown) makes `Finish` run twice,
    `_running` is decremented twice and the concurrency limit is exceeded from then on (`Queue.cs:1185`,
    `:889-894`); the same throw from `Throttled.Report` fails the transfer.
36. Dispose: after the hard-coded 30 s, `_shutdown` is disposed under live attempts (ObjectDisposedException
    at `:806`); background refresh, recovery, StepAside and wake-up tasks keep using the store; entry CTSs are
    never disposed; an overlapping `WaitForIdleAsync` never returns (`Queue.cs:403-418`). Control methods,
    `RemoveAsync`, `RefreshAsync` and `ClearAsync` keep writing to the store after dispose.
37. `StoreRefreshInterval` is not validated; a negative value throws out of `OpenAsync` and leaks the queue.
38. Two processes produce duplicate `Order` values (per-process counters, `Adopt` does not advance them);
    MoveUp/MoveDown on equal values do nothing and report Success. [known] A swap is two saves.
39. A job-store failure uses up the transfer's `RetriesLeft` (`Queue.cs:1003-1007`, `:841`).
40. `SameWorkAs` compares JSON strings: `TransferOptions = null` and `new StorageTransferOptions()` differ,
    and connection ids compare case-sensitively while `Connections` ignores case (`StorageTransferJobs.cs:142`).
    `Validate` accepts a source connection on an upload and options that do not apply to the kind.
41. [known] The store contract does not say whose clock decides lease expiry (the queue uses local `UtcNow`,
    `:463`, `:1099`), that fencing tokens must not restart when an id is removed and re-added (the in-memory
    store restarts at 0), and offers no lease release without a save. The record's `SchemaVersion` is never
    checked on load, and there is no record `ToJson`/`FromJson`, so each store serializes checkpoints itself.
42. The aggregate progress of copies and moves always reports `IsCompleted = false` (`Coordinator.cs:182`),
    so the "last report always arrives" promise does not hold for them; a late report resets a finished job's
    progress (`Queue.cs:1209`, `:1212`).
43. Stopping the library does not stop queues opened from it; running jobs then fail non-transiently and end
    Failed rather than Interrupted.

### Sync and compare

44. Any exception from a provider, `StagedWriter`, or `SaveBaselineAsync` escapes `ApplySyncAsync` and loses
    the report and the baseline after copies were made (`StorageSync.cs:669-696`, `:163-173`, `:916-931`).
45. The baseline keeps only what was seen this run: paths newly excluded, under a file/folder clash, or hidden
    lose their entries, so when the filter is lifted a deletion made meanwhile is copied back
    (`StorageSync.cs:916-931`). Carry previous entries forward for them.
46. [known] Sync promote passes `condition: null` (`StorageSync.cs:843`).
47. A planned delete does not check the type: a directory replaced by a file after planning is deleted
    unchecked, and a planned file delete removes an empty directory now at that path (`StorageSync.cs:878-890`).
48. The plan digest leaves out what shapes the apply: `TimeTolerance` (widening it at apply accepts changed
    items), `ApplyWithConflicts`, and which connections the plan was made for (a plan approved for one pair
    applies to another with the same roots). The plan JSON has no schema version.
49. Deletion safety: `MaxDeletePercent = NaN` passes validation and disables the limit; the warning counts
    folder steps while the limit counts files; `"0.#"` can print "50% exceeds 50%"; safety is not re-checked
    at apply (`StorageSync.cs:545-589`, `StorageSyncModels.cs:224`).
50. A folder delete dropped because excluded items keep it loses its baseline entry and is re-created next run
    on the side that deleted it (`StorageSync.cs:465-467`).
51. A missing destination root gets no CreateDirectory step, so parallel copies race to create parents; on
    SFTP/FTP one can fail and withhold every delete.
52. `SyncAsync` with blocked conflicts returns a bare failure, so the caller cannot see which files; NewerWins
    with one pair of equal times refuses the whole run.
53. A source without ETag or version (SFTP, FTP) is not re-checked after streaming; a torn copy is recorded
    under the identity read before (`StorageSync.cs:821`).
54. `StorageSyncOptions.Verify` says "confirmed on the destination", but sync never calls
    `ConfirmPromotedAsync`; `StorageSyncIdentity.Sha256` is never filled.
55. [known] Stopped steps show as Stale instead of NotRun; the apply lock is per process; `Gates` is never
    pruned; `Plan.Agreed` lists every file inside the plan and its digest.
56. A hashing budget that runs out makes a pair `Undecidable`, which one-way sync copies even when size and
    time agree; an unknown size passes the budget free; the budget is taken per side, so one side's download
    is wasted when the other is refused (`StorageCompare.cs:300-301`; `StorageSync.cs:260-268`).
57. One unreadable file (locked `.pst`, deleted between listing and hashing, permission) fails the whole
    compare, while the remaining pairs keep hashing (`StorageCompare.cs:298`, `:346`).
58. `LinkHandling.Follow` does nothing on Local: `GetInfoAsync` returns the link, so it is excluded
    (`StorageCompare.cs:411-412`). Skipped directory links still have their contents compared and synced into
    a real folder (`LocalStorageBackend.cs:562-571`).
59. Watch: a folder vanishing mid-poll gives mass Deleted then mass Created events (`StorageWatch.cs:183`,
    `:212`); a failed first snapshot becomes an empty baseline, so the first good poll reports everything
    Created (`:133`, `:147`); exceptions thrown when native watching starts (a folder not yet there, a replaced
    connection) bypass the polling fallback (`:110`); polling failures are invisible and retried forever.
60. Checksum choice: one algorithm (MD5) for both sides forces downloads where a side has only SHA-256/CRC32C
    or multipart ETags; SSE-C ETags are treated as MD5, giving false differences (`S3StorageBackend.cs:1186`).
61. `cl-mtime` without an offset is parsed as local time; [known] ignored when the source clock runs ahead of
    the server's, so Update re-copies such files every run.

### Providers, pools and connections

62. FTP `FindAsync` lists the whole parent folder (`LIST -a`) on every miss, and the staging-name probe always
    misses: every staged upload lists the destination folder (quadratic in a 50k-file folder); servers that
    reject `-a` make staged uploads fail (`FtpStorageBackend.cs:779-789`, `:936`).
63. The Local synthetic ETag (write time + length, `LocalStorageBackend.cs:612`) collides: coarse timestamps
    (FAT 2 s, exFAT, HFS+ 1 s, SMB/NFS), tools that keep times (`cp -p`, `rsync -t`, Explorer, restores), and
    FAT's local-time DST shift. Conditions pass that should fail, sync's "unchanged while streaming" check
    passes, and resume treats a changed file as the same source. It is formatted as a strong ETag. Mark it weak,
    add file id/change time, and do not use it as a resume identity.
64. [known] S3 claims atomic conditions for every S3-compatible server; only AWS and MinIO are proven (and
    `DeleteObject`/`PutObject` If-Match are not live-tested). Add a per-connection setting or a probe.
65. [known] Windows client-certificate key lands in a key container (crash leaves it; fails without a loaded
    profile); a PFX without a private key is accepted silently.
66. `SharedResources`: after a natural linger timeout the CTS is disposed but `holder.Linger` still points at
    it, so a racing `Acquire` throws after `Users++` and the holder leaks forever (`:67`, `:92-94`, `:121`);
    [known] the flush is process-wide and skipped if a backend fails to dispose; the pool captures the first
    registration's config object (later mutation diverges from the key); the TLS recorder and `SessionOpened`
    are shared across registrations; the certificate is loaded inside the global lock.
67. `CaseInsensitivePaths` is decided by operating system only: wrong for case-sensitive APFS and Windows
    per-folder case sensitivity, missing for case-insensitive mounts on Linux (`LocalStorageBackend.cs:39`).
68. `ImplicitDirectories`: a file `a/b` next to keys `a/b/...` suppresses the folder; output is not globally
    sorted across pages; S3 Express directory buckets list keys unsorted (`ImplicitDirectories.cs:36`).
69. Out-of-root items are dropped silently: WebDAV hrefs in the server's spelling make a root requested as
    `docs` against `Docs` list as empty (`StorageCompare.cs:392`, `:429`).
70. `StorageErrors.Cancelled` has `ErrorType.Unavailable`, so type-based callers treat a cancel as an outage.
71. Runtime-only mode stores the caller's `StorageConfig` object itself, so the caller's later changes change
    the library's settings.

---

## C. Low and nits

- Transfers: `BackupRestored` is always null for a single file (`Coordinator.cs:103`, `:838`); `Recreate`
  ignores the conflict policy and a single link points at a source-connection path (`:471-474`); the
  directory-into-itself check compares backend instances, so overlapping roots on two connections copy without
  end (`:53`); `EnsureDirectoryAsync` can claim someone else's new folder and roll it back (`:940-943`); a
  delete-marker latest fails a pinned read NotFound (`:138`); progress on resume leaves out resumed bytes and
  followed links overrun FilesCompleted; a source stream leaks if hashing throws (`StagedWriter.cs:409-416`);
  the relay's upload error hides the source's (`:536-541`); comment at `Coordinator.cs:380` says the
  opposite of the code; skipped links not in `SkippedFiles`; a directory reports `DestinationCommitted=true`
  with nothing written; `OpenWriteAsync` progress shows the staging name; the one-byte probe can throw out of
  `WriteAsync`; native-path reports: bytes from the earlier HEAD, no digest, `Files=0` for a directory move,
  Atomic claimed from `ConditionalCreate` alone; `ToResult()` throws on a hand-built report without `Error`;
  `OpenWriteAsync` with Skip returns a Conflict failure; `StorageItem` equality now includes `Sha256`.
- Queue: a Retry-After or lease over ~49 days makes `Task.Delay` throw; pausing a Paused job returns Conflict;
  null ids/states to `ClearAsync` throw and it takes no token; no bus event for Interrupted; Pump/UpdateIdle/
  Prune scan every job under the lock; the retry event can use another worker's record (`:895`); `stored!`
  relies on token linkage (`:832`); a removed job used to be Cancelled — now `JobRemoved`, but doc for
  Interrupted ignores `RequeueInterruptedWhenSafe=false`.
- Sync: `RelativePath` XML says "source spelling"; `|| Kind == Conflict` at `:921` is dead; "N other step(s)
  failed" counts Stale; retries restart large files from zero; `BaselineSaved=false` gives no reason; KeepBoth
  names `a.tar.gz` as `a.tar (conflict x).gz`; quadratic directory-delete scans (`:459-465`, `:249`).
- Compare/watch: native watch channel unbounded; two filesystem calls per event on the watcher thread; a rename
  to an internal name loses the disappearance and a rename from one reports an internal OldPath; incremental
  polling misses same-minute changes on FTP and anything below depth 1 until the full rescan; stale-child
  removal O(N × dirs); `ToUpperInvariant` folding differs from stores; NFC/NFD names not unified; one
  collision blocks the whole sync; a HEAD per size-equal file on object stores (100k files = 100k HEADs),
  not covered by the docs; `PathFilter` regexes have no timeout; the proxy doc about the lease during watch
  start is inaccurate.
- Providers: `ServerIdentityRecorder.Set` is not atomic; `Acquire` replaces a holder of another type without
  disposing it; a `cl1:` token to a non-recursive listing is sent to the server; a cancel during
  `InitiateMultipartUpload` can orphan an upload (recommend a lifecycle abort rule); an S3 replace with a
  condition could be atomic via If-Match but is only checked; the unsalted settings hash includes passwords —
  fine in memory, must never be logged; sync conflict names change after upgrade because Local ETags are no
  longer null; uploads from non-seekable streams bypass `Enrich`.

---

## D. Docs, API and release

1. **`StorageTransferState` renumbering** — A14.
2. **Sync cancellation** — A29.
3. S3 create-only copies of any size are multipart — listed only as ">5 GiB" (A10).
4. MIGRATION misses: the `StorageTransferJob` shape (constructor and `Deconstruct` gone, get-only properties,
   `EnqueuedAt`/`FinishedAt` moved to `job.Record`); the `StorageSyncReport` shape (constructor, `Deconstruct`,
   get-only `Actions`/`Unchanged`, `Actions` now planned steps including conflicts); and, though the CHANGELOG
   has them: `AutomaticRetries` 3, disposing the queue leaves jobs queued, tokens keyed by settings, same-size
   no longer complete, SyncId mismatch refused and applies serialized, the file-versus-folder rule.
5. Missing from both: `MaxFinishedJobs = 1000` pruning; sync `MaxItems` (a plan fails above 1M); per-step
   `ItemRetries`; `Blocked` excluded from `FailedJobs`/`RetryFailedAsync`; `partial_failure` →
   NeedsReconciliation; `CompareAsync` failing on a case collision (only under Added); S3/Azure/Swift recursive
   listings now returning inferred folders (changes item counts).
6. Entries about types 4.8.93 never shipped (`StorageWriteStream.Dispose`, `SourceVersionId needs Versioning`,
   `RemoveAsync`/`JobRemoved`, `SetPriorityAsync`, `StorageTransferOutcome` switches, the whole "Transfer queue
   stores" Before/Now table) mislead readers coming from 4.8.93: move them to Added.
7. **Versioning.** `version.txt` stays `4.8`: this breaking release publishes as `4.8.<run>`, and with
   `AssemblyVersion` pinned at `4.8.0.0` a binary built against 4.8.93 loads it and fails at runtime with
   `MissingMethodException` (`CopyAsync`) instead of failing to bind. Bump to 4.9, or say it prominently.
8. `README.md:786` does not compile (`CheckConnectionHealthAsync` returns `Result<HealthStatus>`).
9. The S3 `Atomic` claim for create-new in `transfers.md`/README needs "AWS and MinIO".
10. A listing over 250,000 items is not cached, so its token does not survive re-registration — undocumented.
11. macOS client certificates (A28): doc says in memory.

## E. Tests

**Tests that pass whatever the code does**
- `ReviewTransferTests` pinned move: the fake delete always conflicts and deletes are never asserted.
- `A_condition_refused…`: no concurrent-delete case, `BackupRestored` unchecked.
- `Resume_uploads_only_the_missing_tail`: bytes read not counted, so a full re-upload passes.
- `A_tampered_resume_token…` asserts the foreign part file is deleted — it locks in B3.
- `A_store_that_throws…` asserts Interrupted (B29).
- `Restarting_with_the_same_worker_id` still passes with recovery removed from `LoadAsync`.
- `Concurrency_never_exceeds` never reaches the global limit; `Higher_priorities` uses `Distinct()`, hiding a
  double start; `Finished_history_is_capped` checks neither the store nor `JobRemoved`; `Adaptive_concurrency`
  never checks halving; the NeedsReconciliation test never runs the retried job.
- Live `SftpFact` tests that `return` when S3 is absent pass green (`NeedsPhase2LiveTests.cs:117`,
  `NeedsPhase4LiveTests.cs:14`, `:50`); use a Skip.
- Timing: `TransferQueueTests.cs:300` (`Delay(300)` then "nothing ran"), `StorageHubNeedsPhase1Tests.cs:92`,
  `StorageHubNeedsPhase5Tests.cs:76` (folder mtime granularity), `ReviewCancellationTests.cs:109` never asserts
  "quickly".
- The case-spelling sync test runs on Windows where Local ignores case, so it is only a real test on Linux CI.

**Missing** — every item in A and B needs a test that fails today. Beyond those: renewal that throws; shutdown
during a successful copy; foreign lease takeover; `RequeueInterruptedWhenSafe=false`; directory kinds through
the queue; bus events; record schema on load; S3 copy properties/tags/SSE, abort on part failure, the
10,000-part math, overwrite over 5 GiB; ordinary TLS drop, concurrent requests, optional client certificates,
Linux EOF, macOS; FTP mutual TLS through a shared pool; PFX without key; pool linger race; FTP against a
LIST-only server; Local ETag at coarse precision; baseline after Failed/Stale/NotRun/Withheld/cancel; two-way
type clash; KeepBoth with a stale rename; filter change between runs; live Windows Local ↔ SFTP/S3 with
case-insensitive compare.

**CI**: the Windows job runs the live suite with no servers, so everything skips — the SChannel TLS
classification and the Windows key-container path never run in CI. `caddy:2-alpine` is a floating tag.

---

## F. What StorageHub still needs before it can delete its own code

Not bugs: guarantees StorageHub makes today that the library does not yet offer. **Can go now:**
`BoundedStreamCopier`, the one-byte probe, in-pass hashing, `CodeLogicLocalStaging`, the temporary PFX file,
the RuntimeOnly workaround. **Nearly:** `CodeLogicStreamingWriteHandle`. **Not yet:** `TransferExecutor`, the
SQLite queue engine, the sync engine — for the reasons below, in order of importance.

1. **Sync: the caller commits the baseline.** The library saves the baseline itself after apply — even after a
   cancelled or partly failed run — with no run id or fence, so it cannot join StorageHub's one SQLite
   transaction (baseline + run completion + lease fence), and StorageHub cannot run its verification scan
   first. Wanted: `BaselineCommit = Manual` returning `report.ProposedBaseline`; a public
   `ComputeBaseline(plan, report, previous)`; a store `SaveAsync(StorageSyncCommit)` carrying run id, plan
   digest and caller context; no automatic save after a failed or cancelled run.
2. **Server-enforced conditional replace.** Pass `DestinationCondition` into backend move/copy (S3 complete with
   If-Match, Azure/GCS copy preconditions), a direct verified single-PUT path for object stores, and
   `RequireAtomicCondition` (fail before any bytes move) or `Capabilities.CanEnforceAtomically(kind)`.
   StorageHub refuses non-atomic overwrites unless the user allows them.
3. **Conditions without ETags**: `ExpectedLength`, `ExpectedLastModified`, `ExpectedSha256` on
   `StorageMutationCondition`, so FTP and SFTP can say "replace only what the user saw" (today `SameETag`
   needs both non-null, so a condition there can never pass).
4. **Machine-readable failure reasons**: `StorageTransferReport.FailureReason` (SourceChanged, SourceTruncated,
   SourceGrew, DigestMismatch, DestinationChanged, DestinationUnconfirmed) and `IsTransient`, instead of
   `storage.conflict` with a Details string. StorageHub's retry and state mapping depend on it.
5. **Verification policy** (Size, StrongHashWhenAvailable, StrongHashRequired) instead of a bool; a length
   mismatch after commit must fail even with Verify off.
6. **Queue reconcile commands**: `MarkCompletedAsync`, `MarkFailedAsync`, `RestartAsync` (discard token and
   staging), `ReviewAsync` — and `long? expectedRevision` on every control call (the desktop's IPC contract
   promises it).
7. **Queue classification hook** `Classify(Error, report)` → Retry / Fail / Block(reason) /
   NeedsReconciliation: StorageHub sends conflict and integrity failures to NeedsReconciliation and its own
   `storage.credential.*`/`storage.trust.*` to Blocked. And a **connection resolver**
   `EnsureConnectionAsync(connectionId)` before each attempt (StorageHub registers lazily with vault
   credentials; a locked key store must block the job, not fail it).
8. **Approval binding for sync plans**: `plan.Binding` over an options fingerprint, each connection's kind, root
   identity and capabilities, plus an `ExternalBinding` from the caller, recomputed at apply against the live
   services (FixedTimeEquals). Today the digest covers only the plan JSON, and a new property in a later
   library version invalidates stored approvals.
9. **Strict identity and proven baselines**: `RequireStrongIdentity` with an Indeterminate state that becomes a
   conflict, `StorageItem.ETagIsSynthetic`, `Sha256` filled from Verify or a server digest, and
   `RequireProvenBaseline` leaving unproven entries out (`content_unproven`).
10. **Per-kind conflict policy**: DeleteVersusModify always Block (KeepBoth and NewerWins settle it today);
    `PropagateDeletes = false` should give a conflict, not a copy-back.
11. **Deletion safety parity**: the baseline as the denominator, an option to refuse the whole plan, a baseline
    required for mirror deletes, a root-identity check, limits re-checked at apply and in the digest.
12. **Step journal and fence** for sync: `OnStepStarting`/`OnStepCompleted` (with veto) for outbox
    reconciliation, and a store `TryAcquireAsync` before the first mutation.
13. **Queue durability details**: persist staging path and token when an attempt enters Transferring and
    periodically, and a CleanupPending outcome; an error sanitizer (`Func<Error, …>`; provider text must never
    reach the database); caller `Tags` on the spec, covered by idempotency; priority as part of idempotency
    optionally; `PruneStates` (Failed is not terminal for StorageHub); paged `QueryAsync`/`CountByStateAsync`
    or a documented "bring your own read model" with `LoadAsync` limited to live jobs.
14. **`StorageWriteStream` outcome**: `CommitAsync` returns a report (committed, staging left, enforcement,
    digest); `AbortAsync` returns a Result.
15. **Resumable conditional overwrite** (Resume together with `DestinationCondition`); **staging hygiene**
    `CleanupInternalAsync(olderThan, keepTokens)`; `SourceDeleteEnforcement` in the move report.
16. **Scale caps** for sync (directories, pages, per-file hash cap, pattern limits), `FilterCaseSensitive`, and
    a pluggable concurrency controller (StorageHub's also watches throughput).

**StorageHub's own changes when it adopts the library** (no library work): use stable connection ids (stored
jobs persist them; StorageHub registers `storagehub-{profile}-{Guid}` today); different WorkerIds for the
service and the desktop; `RequeueInterruptedWhenSafe = false`; set `LingerSeconds`; check the record's
`SchemaVersion` in its own row mapping; staging now lives in `.cl-storage-*` siblings, so the reserved-path
rules follow.

---

## Fixed since round 1 (for the record)

Pinned sources read by their own identity; resume refuses a different source; restart with the same WorkerId;
revisioned store with store-owned leases; guarded store calls; success decided before shutdown (untested);
baseline from planned and written versions; two-way directories; stop-on-error returns a report; Mirror deletes
file by file; an older source never replaces a newer destination; KeepBoth numbered fallback; SyncId checked at
apply; `StorageSyncActionKind` numbers restored; TLS reasons on Windows (proven live) with attempt-scoped
attribution; client certificates loaded once and disposed; watcher errors fall back to polling; idle pools
closed on stop; Local uploads honour conditions; uploads and `OpenWriteAsync` return their digest; staged writes
clean up on cancellation; token lifetime and `LingerSeconds` documented.
