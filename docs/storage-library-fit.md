# Does CL.Storage do what StorageHub needs?

Written 2026-09-24 against CL.Storage `feat/storage-needs` (`d74e2bd`) and StorageHub `2.0` (`896fd23`), from
two scans: every storage call StorageHub makes today or plans to make, and the library's whole public API.

**Short answer: yes — everything StorageHub calls today and everything it plans exists in the library.
StorageHub does not need the library's transfer queue or its sync planner/applier at all.** Those two parts are
about 5,900 of the library's 31,600 lines and were built because `needs.md` asked for them; they are only needed
if StorageHub gave up its own queue and sync engine, which was decided against (`docs/storage-engines-evaluation.md`:
keep ours, the library underneath). Most of the bugs each review round found were in those two parts.

## What StorageHub calls today (the seam)

Four interfaces in `StorageHub.Storage/Abstractions`, implemented in `StorageHub.Storage.CodeLogic`.

| StorageHub needs | Used by | Library call | Status |
|---|---|---|---|
| Health check | connection test, panes | `CheckConnectionHealthAsync` / `GetInfoAsync("")` | ✔ |
| Stat one item | transfers, inspector, shell, editing | `GetInfoAsync` | ✔ |
| List a folder, paged, recursive | panes, sync scanner, shell | `ListAsync` (`Recursive`, `PageSize`, `ContinuationToken`) | ✔ |
| Read, with a byte range | transfers, editing, shell export | `DownloadAsync` (`Offset`, `Length`, `VersionId`) | ✔ |
| Streamed write with a condition, atomic create-new, abort | transfers, editing, new file | `OpenWriteAsync` → `StorageWriteStream` (`Condition`, `Overwrite=false`, `CommitAsync`, `AbortAsync`) | ✔ — replaces `CodeLogicStreamingWriteHandle` (550 lines) |
| Create folder, delete, rename/move and copy on one connection | panes, sync, transfers | `CreateDirectoryAsync`, `DeleteAsync`, `MoveAsync`, `CopyAsync` | ✔ |
| SHA-256 from the bytes | transfer verify, sync scanner | `ComputeChecksumAsync(…, ComputeOnly)` | ✔ |
| Versions, metadata, tags, signed URLs | object inspector | `IStorageVersion/Metadata/Tag/SignedUrlService` | ✔ |
| Local staging that cannot leave a half file | local writes | built into Local uploads | ✔ — replaces `CodeLogicLocalStaging` |
| Register/remove connections at runtime, no config files | agent start-up | `StorageLibraryOptions.RuntimeOnly`, `AddOrUpdateConnectionAsync(persist:false)` | ✔ — replaces the start-up workaround |
| FTPS client certificate without a temp file | FTPS | `ClientCertificateContent` | ✔ — replaces the temporary PFX |

## What StorageHub plans (next.md §0, phases B–E)

| Plan | Library call | Status |
|---|---|---|
| B.1 rate and time left | `Progress` on upload/download (`BytesPerSecond`, `EstimatedRemaining`) | ✔ |
| B.2 speed limits | `TransferLimits`, `MaxTotal…BytesPerSecond` (apply to direct streams) | ✔ |
| B.3 proxy | `StorageProxyConfig` on every provider | ✔ |
| B.4 FTP encoding, time zone, parser, timeouts, SPKI pins | `FtpConnectionConfig` | ✔ |
| B.5 SFTP keyboard-interactive, several keys, jump host, algorithms | `SftpConnectionConfig` | ✔ |
| B.6 skip / if newer / rename on conflict | `StorageUploadOptions.ConflictPolicy` | ✔ |
| B.7 resume | staged resume (`ConflictPolicy.Resume`, resume identity) | ✔ |
| B.8 server checksums | `GetServerChecksumAsync` | ✔ |
| C.1–2 test an unsaved connection, show the presented key/certificate, Trust/Reject | `TestConnectionAsync` → `ServerIdentity` | ✔ (FTP, SFTP, WebDAV) |
| C.3 connection details | `GetConnectionDiagnosticsAsync` | ✔ |
| D.1 permissions, owner, links, times; chmod | `IStorageAttributeService` | ✔ |
| D.2 free space | `GetSpaceAsync` | ✔ |
| D.3 health and retry on the card | `StorageConnection*Event`s | ✔ |
| D.4 refresh on change | `WatchAsync` | ✔ |
| D.5 raw FTP console | `ExecuteCommandAsync` | ✔ (last or never) |
| D.6 stale staging cleanup | `CleanupStaleStagingAsync` | ✔ |
| D.7 compare folders | `CompareAsync` (read-only) | ✔ |
| E WebDAV, Azure, GCS, Swift | providers | ✔ |

**Nothing StorageHub needs is missing.**

## What StorageHub does not need

| Library part | Size | Why not |
|---|---|---|
| `Queue/*` — durable transfer queue, job store, leases | ~2,600 lines | StorageHub keeps `TransferQueueAgentSubsystem` + its SQLite store (persisted, fenced, tested) |
| `StorageSync` plan / apply / baseline store | ~2,000 lines | StorageHub keeps its three-way engine, approval and outbox |
| `StorageLibrary.CopyAsync/MoveAsync` across connections | — | optional: StorageHub's `TransferExecutor` can keep driving read + conditional write; switching is a later choice |
| `DownloadToFileAsync`, directory upload/download, batch helpers | — | StorageHub has its own |

`needs.md` items 14–39 and most of needs-review section F belong to this table: they were written for replacing
StorageHub's engines, and are dropped.

## The open library bugs, sorted by whether StorageHub is affected

Found by the last docs audit; not yet confirmed by running them.

| Bug | Affects StorageHub? |
|---|---|
| A commit that succeeded can be reported as a plain failure when the read-back after it fails | **Yes** — writes with Verify |
| `TestConnectionAsync` puts raw exception text in errors, and joins a live session pool with the same settings | **Yes** — phase C shows these errors |
| The condition-enforcement query answers Atomic for MinIO uploads that are staged | **Yes, minor** — only if StorageHub reads the query |
| Part-file lock marker race on FTP/SFTP | Barely — only two processes resuming one file; StorageHub's agent is one process |
| `DownloadToFileAsync` Resume appends to any shorter local file | No — not used |
| Queue: removing a running job deletes a NeedsReconciliation record; failed claims retry for ever | No — queue not used |
| `ApplySyncAsync` ignores `DryRun` | No — sync applier not used |
| Stopping one library closes another's idle pools | No — one library per process |

## Recommendation

1. **Freeze the library's scope.** No new features for StorageHub; the library already covers everything above.
2. **Fix the three bugs that affect StorageHub** (first three rows), each with a failing test first.
3. For the rest: fix them too if they are small, or mark the library's queue and sync applier as preview in its
   docs, since nothing depends on them yet.
4. Update `next.md` §0 and `needs.md` to the decision actually taken: the library moves the bytes underneath;
   StorageHub keeps its queue and sync engine.
