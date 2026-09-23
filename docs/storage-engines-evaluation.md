# The library's transfer queue and sync, against ours

Written 2026-09-23 for the CodeLogic.Storage 4.8.93 upgrade (`next.md` §0, A2). The question: 4.8.93 ships a
transfer queue (`StorageLibrary.CreateTransferQueue`) and a directory sync (`StorageLibrary.SyncAsync`), and
StorageHub has its own of both — about 9,000 lines for transfers and 17,000 for sync and scheduling. Should
ours be replaced?

**Recommendation: keep both of ours. Take from the library what sits underneath them, not the engines.**
The library's versions are well made for what they are — an in-memory queue and a stateless folder diff for
a program that runs to completion — but StorageHub's promises are the ones they do not make: a transfer
survives the agent restarting, a sync never brings back a file somebody deleted, and a change on both sides is
a question for the user rather than a coin toss. The spike below shows each of those failing with the
library, on real files.

## How this was decided

- Both engines read in full, with file and line for every claim (kept out of this page; the claims below are
  the ones that decided it).
- A spike program run against the library 4.8.93 package directly: local folder pairs for the sync semantics,
  and the Docker lab (`eng/testlab`) for SFTP into MinIO.

## What the spike showed

| # | Situation | The library did | Ours does |
|---|---|---|---|
| S1 | A file deleted on B after a two-way sync | copied it back from A: **the deletion is undone** | a deletion; propagated if the profile says so, otherwise a conflict that blocks approval |
| S2 | Both sides edited since the last sync | copied the newer over the older: **A's edit is gone, nothing reported** | a BothModified conflict; Block or KeepBoth |
| S3 | Source emptied (a drive not mounted, a wrong root), then Mirror with deletes | **deleted all 50 files** on the destination | refused: an unexpectedly empty side blocks deletions, as does more than 100 or 10% |
| S5 | Same size, different content, times 1 s apart | nothing: the files count as the same | compared by version, then SHA-256, then length; undecidable is a conflict |
| Q1 | The queue disposed during an upload (an agent restart) | destination clean (staging removed), **job gone**: nothing to resume or retry | job persisted; comes back Interrupted for the user to restart or reconcile |
| L1 | SFTP → MinIO, 200 files, Update, run twice | 200 copied in 1.0 s; second run copied nothing | the same, with SHA-256 verification of each copy |
| L2 | The same pair, two-way, three runs | first run **rewrote 130 identical files back onto SFTP** (S3 keeps the upload time, so they looked newer); then settled | the baseline knows they were just copied |

S4 (a dry run) behaved correctly: a plan, nothing written.

## Transfer queue

| | Library | StorageHub |
|---|---|---|
| Where jobs live | memory; lost on dispose, no hook to save or rebuild them | SQLite, with leases and fencing |
| After a crash | gone | Interrupted, then Restart or Reconcile by the user |
| States | Queued, Running, Completed, Failed, Cancelled | 17, including BlockedTrust, BlockedCredential, NeedsReconciliation |
| Overwrite safety | overwrite by default; staging on Local/FTP/SFTP/WebDAV | only with the destination's version or ETag in hand; staged, then a conditional promote |
| Verification | none | SHA-256 during the copy, then the destination re-hashed |
| Move | source deleted after the destination commits | the same, and the delete is conditional on the source's version |
| Retry | fixed delay, transient codes | exponential backoff, transient codes; trust and credential failures block instead |
| Concurrency | global and per connection | global, per connection, and adaptive |
| Pause, priorities | pause; three priorities fixed at enqueue | **no pause**; integer priority fixed at enqueue |
| Progress | bytes, rate, time left, current file (throttled in the stream) | bytes only; **the rate is never computed** |
| Resume | `ConflictPolicy.Resume`, written in place **without** staging | modelled (checkpoints), **never used**: every attempt starts at byte 0 |
| Directory jobs | one job, all-or-nothing, sequential | one job per file, planned on the desktop |

The library's queue would be a step down in everything except progress, pause and resume — and its resume
gives up the staging that makes our overwrites safe.

## Sync

| | Library `SyncAsync` | StorageHub |
|---|---|---|
| State | none; two live listings diffed | a per-path baseline in SQLite, generation-numbered |
| Two-way deletions | never; deleted files are resurrected (S1) | propagated, or a conflict |
| Both changed | newer time wins silently (S2) | a conflict |
| Mirror safety | dry run only; deletes run even after copies failed (S3) | limits, empty-side detection, checked at preview and again at apply |
| Preview and approval | dry run | a plan with a SHA-256 challenge, approved before anything is written |
| Scheduling | none | cron with time zone, misfire grace, overlap policy, safe-automatic mode |
| Filters | one name glob; excluded files are still deleted inside a deleted folder | include and exclude globs, applied to the baseline too |
| Case collisions | two entries, the last copy wins | refused, the whole scan |
| Object-store times | TwoWay rewrites identical files once (L2) | not affected |
| Copy safety | plain upload with overwrite; no verification | through the transfer executor: staged, verified |

## What to take from the library instead

These replace the queue-related steps of phase B in `next.md` §0. They go under our engines rather than
beside them.

1. **Rate and time left (B.1).** Our transfers stream through our own copier, not the library's calls, so
   its `Progress` does not reach them. Compute the rate from the bytes the worker already counts (a moving
   window), carry it and the time left over the queue IPC, and fill the status bar that shows 0 today.
2. **Speed limits (B.2).** The library enforces `TransferLimits` in its upload and download pipeline, which
   our sessions do call underneath. To be proven against the lab before the refusal is removed.
3. **Conflict choices (B.6).** Skip, If newer, If size differs and Rename are decisions the desktop can
   make when it plans a transfer — it already reads the destination tree for conflicts. Overwrite keeps the
   identity rule.
4. **Resume (B.7), our way.** Resume the *staging* object with the library's `AppendAsync` (Local, FTP,
   SFTP), then promote as now; the destination is still never written in place. Checkpoints start being
   used, and a retried or restarted job continues from its last checkpoint.
5. **Server checksums (B.8)** for the sync scanner on S3, where hashing today means downloading.
6. **Compare folders (D.7)** is the one place the library's `CompareAsync` fits as it is: a view, nothing
   written, where a stateless diff is exactly right. Its limits (case-sensitive keys, S3 folders without
   markers, times on object stores) to be shown, not hidden.

## Gaps in ours that the comparison turned up

Not reasons to switch; things to do.

- **No pause.** The Paused state is modelled and nothing sets it; there is no IPC for it. The library has
  one.
- **A restart leaves running jobs Interrupted** for the user to act on, even when nothing was written. A job
  whose checkpoint shows no provider write could be put back in the queue by itself.
- **No way to resolve a sync run that needs reconciliation.** The state machine allows it; no IPC command or
  screen offers it.
- **A sync stops at the first failed operation** and never retries an item.
- **One-way sync may re-copy unchanged files every run** when the provider gives a version but no digest:
  two files are "known equal" only by matching SHA-256. To confirm with a test before fixing.

## To report upstream (CL.Storage)

- `WatchAsync` never uses native notifications through `GetStorage()`: the service proxy does not
  implement `IStorageWatchService`.
- Mirror copies an older source over a newer destination, although the comment says it never does; only
  Update checks.
- Mirror's deletes run even when copies failed; a folder deleted as extraneous takes files the name filter
  excluded with it.
- Sync's copy is described as a "staged, atomic upload" but is a plain upload with overwrite.
- Two-way against an object store rewrites identical files once, because the store keeps the upload time.
- Case-only name differences are two entries; into a case-insensitive side the last parallel copy wins.
- `SyncAsync` finds each action's entry with a linear search inside the parallel loop: quadratic in files.
- The queue's `Throttled` progress wrapper does not throttle; the native watcher has no overflow handler.
