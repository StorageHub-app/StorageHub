# What CodeLogic.Storage needs, so StorageHub can let it do all the work

Written 2026-09-23 against 4.8.93. The decision (`next.md` §0, A2): every byte StorageHub moves should move
through the library, and StorageHub should keep only what is policy — which jobs exist, what the user
approved, what a sync's baseline says. Today that is not possible without giving up guarantees StorageHub
makes, because the library is missing the pieces below. Each item says what is missing, and what StorageHub
does in its place today.

The evidence is in `docs/storage-engines-evaluation.md` (a spike against real files and the Docker lab, and
both engines read in full).

Priority: **P1** blocks moving transfers onto the library; **P2** blocks moving sync; **P3** makes it
complete.

---

## 1. Transfers: guarantees on a single copy or move (P1)

1. **Conditional destination on cross-connection copy and move.** `StorageTransferOptions` has no
   `Condition`. StorageHub only replaces a file when the destination is still the version the user saw
   (`ExpectedETag` / `ExpectedVersionId`), including when the write is staged and then promoted — the
   promote must be conditional too. `StorageUploadOptions.Condition` exists; copy and move need the same.
2. **Pinned source.** Copy and move need `SourceVersionId` and `ExpectedSourceETag`: read exactly the
   version that was planned, and fail with `storage.conflict` if the source changed during the relay
   (re-checked after streaming for sources that have only an ETag).
3. **Exact length.** An `ExpectedSourceLength`: fail, without committing, when the source turns out shorter
   or longer than planned. StorageHub probes one byte past the end today.
4. **Verification built in.** A `Verify` option on copy, move and upload: SHA-256 computed in the same pass
   as the relay, compared against an optional expected digest before commit, then the destination confirmed
   (server digest where it is SHA-256, otherwise re-read). The digest comes back in the result. Today
   nothing in the library verifies a transfer.
5. **Atomic create-new everywhere, or an honest capability.** `Overwrite = false` must be atomic
   (conditional create), or the backend must say it cannot, so the caller can refuse rather than race.
6. **Conditional source delete on move.** The move deletes the source only if it is still the version that
   was copied (version or ETag condition).
7. **Structured outcomes instead of strings.** `storage.partial_failure` carries
   `sourceDeleteError=…;destinationState=complete` as text. A result type is needed: destination committed
   yes/no, source deleted yes/no, staging left behind (path), backup restored yes/no. StorageHub turns
   exactly these into "needs reconciliation".
8. **A transfer report.** Bytes, digest, the destination's new ETag/version, the name actually written
   (after `Rename`), and whether it was skipped and why (`Skip`, `OverwriteIfNewer`, ...).
9. **Staged resume.** `ConflictPolicy.Resume` writes in place, unstaged. StorageHub needs resume of the
   *staging* object: append the missing tail to the staged file, then promote atomically, so the
   destination is never half-written. `AppendAsync` exists on Local, FTP and SFTP; the coordinator needs to
   use it on its own staging item.
10. **A resume token that survives a restart.** The staging path, the bytes committed, and the source
    identity they were read from, exportable after a failure and accepted by a later call, so a job
    persisted by the caller can continue after the process restarts.
11. **Progress everywhere.** `TotalBytes` on downloads (known from `GetInfoAsync`), totals for directory
    transfers (an optional pre-scan), and a report for native server-side copies (at least start and end).
12. **A streaming write.** `OpenWriteAsync(path, options)` returning a stream the caller writes into, with
    commit and abort. StorageHub bridges its push-style writes onto the pull-style `UploadAsync` with a pipe
    today (`CodeLogicStreamingWriteHandle`, 550 lines).
13. **Local staging.** Confirm that Local overwrites are staged and conditional in 4.8.93, so StorageHub's
    own `.storagehub-internal/staging` for Local (`CodeLogicLocalStaging`) can be removed.

## 2. Transfer queue (P1)

14. **A persistence hook.** Jobs live in memory and are captured as closures. The queue needs a job store
    interface (add, claim, transition, checkpoint, list), and jobs described as data — kind, both endpoints,
    every option, priority — so they can be written down and rebuilt. With `StorageHub` supplying a SQLite
    store.
15. **Restart semantics.** On start, jobs that were running become `Interrupted`. One whose record shows no
    provider write can go straight back to the queue; one that may have written needs a decision.
16. **Caller-supplied, idempotent job ids.** Enqueuing the same id twice returns the existing job; the same
    id with a different description is a conflict.
17. **More states.** `Blocked` (with a reason: trust, credential) instead of `Failed` when a host key or
    login is refused; `NeedsReconciliation` when the outcome is uncertain (a write began and the result is
    unknown). Only transient failures retried.
18. **Backoff.** Exponential with jitter, honouring `Retry-After` (`StorageErrorInfo.TryGetRetryAfter`
    exists; the queue uses a fixed delay).
19. **Control.** Pause and resume one job, not only the whole queue; change priority; move up and down;
    remove a queued job. Integer priorities, not three levels.
20. **Leases and fencing** when the store is shared, so two processes never run one job, and a worker that
    lost its lease cannot record an outcome.
21. **Events.** Cancelled and Retrying bus events; progress events actually throttled (the `Throttled`
    wrapper passes every report through); an option to raise events on a synchronization context.
22. **History.** A retention limit, and clearing by state.
23. **Adaptive concurrency (optional).** Start low, add a slot while transfers stay healthy, drop one on
    failure or falling throughput. StorageHub has this (`AdaptiveConcurrencyController`).
24. **`EnqueueDownload` taking `StorageDownloadOptions`** (version, range).

## 3. Sync (P2)

25. **A baseline, so two-way is three-way.** `SyncAsync` diffs two live listings. Without a record of the
    last sync it resurrects deletions and settles an edit on both sides by timestamp (spike S1, S2). A state
    store interface (per path: both sides' identity, size, digest, version, tombstones), and a classifier:
    created, modified, deleted per side; both modified, both created and delete-versus-modify as conflicts.
26. **Conflict policies.** Block (the default), keep both (conflict copies with a deterministic name), and
    newer-wins only when asked for.
27. **Deletion propagation for two-way**, only through the baseline.
28. **Plan, then apply.** A plan object that can be shown, stored and approved, with a digest over plan,
    snapshots and options; apply re-checks every item's identity and refuses a plan that no longer matches.
    `DryRun` returns a plan but cannot be approved and applied as that plan.
29. **Deletion safety.** A maximum count and percentage of deletions; refuse to delete when a side is
    unexpectedly empty (spike S3 deleted all 50 files), when a listing was incomplete, or when copies in the
    same run failed. Mirror runs its deletes even after failed copies today.
30. **Copies through the transfer path.** Sync's copy is a plain upload with overwrite, although the comment
    says "staged, atomic". It should use the conditional, verified, staged copy from section 1.
31. **Filters.** Include and exclude path globs with `**`, applied to both sides and to the baseline; a
    folder deleted as extraneous must not take excluded files with it.
32. **Case collisions.** Per-provider case sensitivity, and collisions refused rather than the last parallel
    copy winning.
33. **Object-store times.** Keep the source's modification time in metadata (as rclone does) and compare
    by it, so a two-way sync with S3 does not rewrite identical files (spike L2).
34. **Mirror bug.** Mirror copies an older source over a newer destination; the comment says only Update
    and Mirror never do, and only Update checks.
35. **Scale.** The plan looks up each action's entry with a linear search inside the parallel loop
    (quadratic); both full listings are held in memory with no cap. Limits and a streaming comparison.
36. **Partial failure.** Per-item retry of transient failures, a continue-or-stop policy, and the report
    kept when the run is cancelled.
37. **Hashing policy.** Server digests where they exist, computed digests bounded (files, bytes, concurrency)
    and run in parallel, not one at a time inside the compare loop.
38. **S3 folders.** Infer directories from key prefixes, so a folder without a marker object is a folder.
39. **Links in sync.** The `LinkHandling` option, as transfers have.

## 4. Connections, errors and runtime (P1 unless marked)

40. **A runtime-only mode.** StorageHub starts the library with configured connections disabled
    (`Program.cs`: "Until CL.Storage ships RuntimeOnly mode") and registers every connection at runtime with
    `persist: false`. A supported mode: no configuration file read or written, no connections but the
    runtime ones.
41. **TLS failure reasons.** `storage.tls_failure` covers the server's certificate refused, ours refused,
    and no common protocol. A detail key (`tlsReason`) and the presented certificate in the error, so a
    refused server certificate can be offered for trust and a refused client certificate reported as a
    credential problem.
42. **Credentials in memory.** `PrivateKeyContent` exists for SFTP. The FTPS and WebDAV client certificate
    (`ClientCertificatePath`) needs the same (`ClientCertificateContent` as bytes, and a password), so
    StorageHub no longer writes a PFX to a temporary file.
43. **Listing tokens across registrations.** StorageHub retires an idle registration after 30 s and
    re-registers with a new id; the listing snapshot, and so a continuation token, is tied to the id. Either
    tokens that survive re-registration of the same settings, or a documented lifetime.
44. **Speed limits on direct streams.** Confirm, and document, that `TransferLimits` and the library-wide
    limits apply to `DownloadAsync` streams and `UploadAsync` used directly, not only to copy and transfer
    helpers.
45. **Watch through the proxy (P3).** `WatchAsync` never uses native notifications for connections from
    `GetStorage()`, because the service proxy does not implement `IStorageWatchService`, so local
    connections poll. The native watcher also has no overflow handler, so events lost to a full buffer
    vanish; it should report an overflow and rescan.
46. **Incremental polling (P3).** Polling lists the whole tree every interval. Per-directory modification
    times (where a provider has them) or a change token would make watching a large remote tree affordable.
47. **Connection pooling across registrations (P3).** A session pool per registration means StorageHub's
    re-registration throws away warm sessions. A pool keyed by connection settings would keep them.

---

## When these land

In order of what it unlocks for StorageHub:

- **1–13** → `TransferExecutor`, `BoundedStreamCopier`, staging and promote, and the streaming write
  bridge are deleted; the queue calls the library for every job. Resume, conflict choices, rate and
  time left, and speed limits come with it.
- **14–24** → the SQLite queue becomes the library's queue with StorageHub's store behind the hook.
- **25–39** → the sync engine's scanner, classifier, planner and executor become library calls;
  StorageHub keeps profiles, schedules, approval and history.
- **40–47** → the start-up workaround, the temporary PFX file and the re-registration costs go.
