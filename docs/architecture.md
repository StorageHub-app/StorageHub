# Architecture

StorageHub separates file semantics, provider integration, durable execution,
Windows security, and presentation so that a provider SDK cannot define the
application's safety rules.

## Runtime shape

```text
StorageHub.Desktop (WinForms, custom-painted)
            |
            | versioned current-user named pipe
            v
StorageHub.Agent.Windows (CodeLogic lifecycle)
       |            |             |
       v            v             v
  SQLite/WAL    DPAPI vault   scheduler/engine seams
                                    |
                                    v
                         StorageHub.Storage contract
                                    |
                                    v
                         CL.Storage runtime adapter
                                    |
                   local / S3 / FTP(S) / SFTP / ...
```

The desktop is intended to be disposable presentation state. The agent owns
long-running work and durable state. It exposes status, request-scoped saved
connection discovery/testing, paged storage listing, optimistic profile CRUD,
profile-bound certificate/host-key trust decisions and atomic rollover,
durable transfer queue commands, sync preview/apply management, and preview-only
schedule management. A separate current-user-only channel owns secret enrollment
and rotation. The desktop uses these operations for bounded remote browsing,
Connection Manager, saved-pane file transfers, queue control, sync review, and
schedule editing. A dedicated read-only inspector contract exposes bounded object
version pages, portable metadata, and tags; signed URLs and advanced mutations
remain outside that contract.

## Project boundaries

| Project | Responsibility |
| --- | --- |
| `StorageHub.Contracts` | Stable results and versioned IPC data transfer objects |
| `StorageHub.Domain` | Strong IDs, root-safe addresses, entries, and capabilities |
| `StorageHub.Application` | CodeLogic application lifecycle and validated connection-profile model |
| `StorageHub.Storage` | Provider-neutral asynchronous endpoint/session contract |
| `StorageHub.Storage.CodeLogic` | Vault/trust-aware profile connector, runtime-only `CL.Storage` adapter, and streaming write bridge |
| `StorageHub.Transfers` | Transfer intent, state, durable-store contracts, checkpoints, bounded copy, and verified move behavior |
| `StorageHub.Sync` | Three-way classification, deletion policy, immutable plans, execution approvals, and plan execution |
| `StorageHub.Persistence` | SQLite configuration/migrations and durable profile, trust, scheduler, transfer, sync, execution, and outbox stores |
| `StorageHub.Security` | Opaque secret references, vault contracts/envelopes, trust contracts |
| `StorageHub.Application.Credentials` | Shared key/certificate store model, derived non-sensitive summaries, and reference-counted profile bindings |
| `StorageHub.Infrastructure.Windows` | Windows DPAPI and restricted runtime-secret files |
| `StorageHub.Agent` | Runtime coordination, named-pipe IPC, schedules, and scheduler contracts |
| `StorageHub.Agent.Windows` | CodeLogic console host and database/vault/worker composition, storage browsing, profile/transfer/sync/schedule IPC, and dedicated secret IPC |
| `StorageHub.Desktop.WinForms` | High-DPI WinForms multi-pane shell with a custom-painted element set, owned Light/Dark/System themes, English/Danish/German localization, asynchronous local/remote browsers, saved-pane transfers, queue/sync/schedule management, Connection Manager, and agent-status monitor |
| `StorageHub.Diagnostics` | Allow-list policy for safe diagnostic artifacts |

Core projects target `net10.0`. Windows hosts target Windows-specific TFMs; only
those layers may depend on WinForms or operating-system security APIs.

## Storage contract

Every session is bound to a `ConnectionProfileId` and a root identity. An address
contains that same identity plus a canonical relative path. Validation rejects
absolute paths, parent traversal, mismatched roots, invalid normalization, and
operations against a different profile.

Providers publish effective capabilities. File and directory copy/move are
represented separately because a provider need not support both. Callers must
also preflight range reads, conditional versions, atomic publication, and
resumable upload. An absent capability is a hard unsupported result; adapters
must not silently substitute weaker behavior. CL flags for ACL, lease, and append
are not advertised as usable until StorageHub has corresponding operations.

Expected provider errors cross the boundary as `StorageResult` failures with safe
codes and categories. Cancellation remains cancellation. Unexpected exceptions
are reduced to non-sensitive operation context.

`CodeLogicConnectionProfileConnector` resolves vault references and accepted
trust records immediately before building a provider configuration. It supports
Local, S3 with system trust, acknowledged plaintext FTP, FTPS with system trust
or explicit certificate pins, and SFTP with explicit host-key pins. Credentials
are registered only in memory. PFX/private-key files are materialized in a
current-user-only directory and retained for exactly the runtime connection's
lifetime.

The session root identity is a canonical SHA-256 transcript over the profile ID
and revision, endpoint namespace, authentication mode/principal, opaque vault
reference revisions, and selected trust-record revisions. Credential values are
never included. Rotating credentials or trust invalidates stale pane pages,
queued addresses, and other identity-bound work instead of silently retargeting
it.

FTPS client PFX use requires a separate vault-backed password. SFTP private-key
profiles require a vault-backed passphrase. The connector strictly parses the
OpenSSH, legacy PEM, or PKCS#8 envelope and uses SSH.NET to decrypt and parse the
actual key with that passphrase before creating the runtime backend.

The connector rejects settings that the current provider boundary cannot enforce,
including TOFU capture, S3 certificate pinning, per-connection proxy/bandwidth,
non-UTF-8 FTP names, and incompatible retry/timeout combinations. It never turns
on accept-any certificate or host-key behavior.

`CodeLogicStorageEndpointSession` maps the resulting runtime connection to the
common contract. Uploads use a bounded pipe rather than buffering an entire file.
Local writes stream into a cryptographically random reserved staging object and
publish through a same-volume atomic move; abort, validation failure, and provider
failure clean the staging object. The reserved namespace is rejected and filtered,
and stale owned artifacts are scavenged when a local session is registered.
Disposing the runtime connection removes its backend without persisting provider
configuration through CodeLogic and removes short-lived credential files.

## Transfers and sync

A transfer is described by immutable source/destination addresses and a
verification policy. The executor:

1. validates profiles, roots, capabilities, source metadata, and preconditions;
2. streams through a bounded buffer;
3. commits the destination write;
4. verifies the destination size and, when required and available, its digest;
5. deletes the source for a move only after successful verification.

Checkpoint models bind resume decisions to source identity, size, modification
time, optional portable SHA-256, provider state, and completed parts. The SQLite
transfer store persists complete immutable intents and uses optimistic state
revisions, exclusive fenced claims, renewable leases, monotonic versioned
checkpoints, retry availability, and atomic attempt closure. Its recovery marks
expired in-flight ownership as interrupted while preserving live leases;
migrated legacy in-flight rows without root identity are held for reconciliation.
The Windows agent composes the worker and bounded queue IPC. Manual enqueue
reuses a stable transfer ID after a lost acknowledgement and surfaces unresolved
ambiguity instead of creating an invisible duplicate.

Sync compares both sides with a baseline and classifies creates, modifications,
deletes, equality, and conflicts. Bounded scans can stream SHA-256 for files that
lack both a version ID and ETag; algorithm and byte count are explicit, and
opaque ETags never become hashes. Execution consumes an immutable, canonically
hashed digest-schema-v3 plan. Apply mode verifies the plan digest and, for destructive work,
requires a separate execution-approval digest binding snapshot completeness and
counts, verified roots, live session roots and capabilities, execution mode,
deletion limits, and transfer options. Substitution fails before provider I/O.
Deletes require exact object versions and native conditional versioning;
overwrites additionally require versioned source and destination identities,
complete scans, and native temporary-file/move/atomic-rename support. Providers
that cannot enforce those guarantees return unsupported. Preview mode performs
no endpoint mutations.

The scheduler core polls durable snapshots and relies on the schema-v2 SQLite
store to atomically acquire and renew profile-scoped leases with monotonic fencing
tokens. It bounds global concurrency, prevents overlapping runs for a profile,
samples lease time only after acquiring the cross-process write lock, bounds
renewal calls by the remaining lease, rejects stale completion, records exact
completion retries through the schema-v4 immutable journal, records misfires, and
can retain at most one queued occurrence. The Windows host composes a fenced
runner that records a durable preview outbox event. Schedule management is
intentionally preview-only: a schedule cannot create an execution approval or
request unattended provider mutation.

## Persistence and recovery

The SQLite initializer enables foreign keys, WAL journaling, `synchronous=FULL`,
a bounded busy timeout, cross-process-serialized ordered migrations, and `quick_check`. Database writes go
through a single-writer gate. Schema v1 establishes normalized state for
connections, credential references, trust, transfers/checkpoints, sync plans and
runs, schedules, conflicts, notifications, audits, settings, plugins, and an
outbox. Schema v2 adds scheduler CAS/lease/fencing/queue/outcome state; schema v3
adds immutable transfer intents plus transfer revision/retry/lease metadata;
schema v4 adds immutable lease-keyed scheduler completion records; schemas v5-v7
add sync durability, orchestration, and fenced execution; and schema v8 adds
portable checksum evidence plus digest-schema tracking. Schemas v9-v11 carry
neutral sync policy, bind each copy to whether its destination existed in the
approved snapshot so one plan can mix atomic creates with conditional
overwrites, and record the fail-closed policy for non-atomic FTP/SFTP writes.
Schema v12 lets durable intents address agent-owned root-validated local
endpoints that are not saved profiles. Schemas v13-v14 replace the credential
reference placeholder with the shared key and certificate store, whose bindings
block deletion while a profile still uses an entry, and allow a PKCS#12 bundle
to carry no passphrase while SSH keys still require one.

Connection-profile and trust repositories use optimistic versions. Trust
rollover revokes the old identity and inserts the verified replacement in one
SQLite transaction, while IPC binds the mutation to the exact saved profile
revision and derives the artifact kind, host, and port server-side. Scheduler,
transfer, sync, execution, and outbox repositories implement their durable
concurrency protocols. Transfer, sync-outbox, and scheduler workers are composed
into the agent; schema presence alone is still not treated as feature completion.

Initialization can report recovery-only operation instead of starting mutating
subsystems after a database failure. The Windows host composes real database,
vault, transfer, sync-outbox, scheduler, and IPC health checks under the CodeLogic
lifecycle.

## Desktop presentation

The shell is WinForms, but its inputs are not. `StorageHubFieldChrome` holds one
set of metrics -- padding, corner radius, the width of the zone a trailing glyph
or stepper occupies -- and the painting that draws a field from them, in a
standard and a dense variant. `StorageHubTextField`, `StorageHubChoiceField`,
`StorageHubNumberField`, `StorageHubTimeField`, `StorageHubToggle`,
`StorageHubCheckBox`, and `StorageHubButton` are built on it, so a field is the
same height and sits on the same baseline whether it appears in Settings, in a
dialog, or hosted in a toolbar. Toolbars use the dense variant through
`ToolStripControlHost` wrappers rather than the stock `ToolStripTextBox` and
`ToolStripComboBox`, which cannot be themed.

Every metric is written as a logical unit and converted with
`Control.LogicalToDeviceUnits`. A literal pixel in layout code is a bug on a
scaled display, and most of this project's DPI defects have been exactly that.
`DisplayMetrics` supplies the `Padding` and `Point` conversions WinForms leaves
out; the framework converts only an `int` and a `Size`, and the two shapes it
omits are the ones layout code writes most, which is why the rule went unapplied
for so long outside the custom controls.

Nothing is left to the framework's own scaling. Every form sets
`AutoScaleMode.Dpi` but none assigns `AutoScaleDimensions`, so WinForms
initializes it to the current DPI and the automatic pass is a no-op by
construction. That is deliberate rather than accidental: the automatic pass only
ever scales the tree that exists when a container is first laid out, and this
shell builds most of its surface afterwards — panes per workspace tab, rows per
settings page, cards per connection. Converting at the point of use is the only
form of it that reaches those.

Two consequences are easy to forget. A rasterised glyph has to be drawn at the
size it was rasterised for, so `ToolStrip.ImageScalingSize` is converted too --
the framework never scales it, and left alone it resamples a 125% glyph back down
to its 96-DPI extent. And a metric a control compares against measured text has
to be in device units, because `TextRenderer` measures in device units: a column
width left logical is compared against text a quarter larger than itself at 125%.

Settings pages are composed rather than positioned: `SettingsRow` puts a title
and its description on the left and the control on the right, `SettingsCard`
stacks rows and draws the dividers between them, `SettingsCaption` labels a
group, and `SettingsPagePanel` scrolls the column. A page therefore cannot drift
out of alignment with the others, because none of them carry coordinates.

The toolbar's contents are a saved list of command ids -- ids rather than labels
or indices, so a customised toolbar survives a language change and a command
being renamed -- resolved against the command catalog at build time. A command
the shell does not implement, or one that no longer exists, is dropped instead of
drawn as a button that does nothing.

## Dependency and extension policy

Provider SDKs stay behind `StorageHub.Storage`. StorageHub does not shell out to
PuTTY, WinSCP, FileZilla, rsync, or other transfer executables. `CL.Storage` uses
managed/open-source provider packages such as SSH.NET and FluentFTP.

The `CL.Storage` dependency is consumed through the centrally pinned
`CodeLogic.Storage` NuGet package and committed package lock files.

Provider integration fixtures run outside the application process lifecycle.
The first fixture downloads an exact MinIO release only from the official
archive, verifies its reviewed SHA-256 before execution, binds it to random
loopback ports with ephemeral credentials and data, and removes only the run
directory and process it created. CI applies a shared S3 conformance and
hostile-input suite before packaging; the installer jobs remain independent of
the fixture.

The FTP fixture similarly downloads an exact pyftpdlib source archive and
verifies its reviewed SHA-256, while every binary Python TLS dependency is
version- and hash-locked. It generates a memory-only private CA plus a server
certificate, encrypted server key, client certificate, and encrypted PFX for
each run; starts separate plaintext,
explicit-TLS, implicit-TLS, and mutual-TLS servers on random loopback control
and passive ports; requires encrypted FTPS data channels; and removes only its
own processes and run directory. The shared conformance suite adapts to FTP's
advertised lack of atomic conditional create instead of silently emulating it.

The SFTP fixture installs only version- and hash-locked binary Python packages,
generates encrypted RSA host and client OpenSSH keys for every run, and starts
separate password-only, public-key-only, and rotated-host-key AsyncSSH endpoints
on random loopback ports. The shared bounded conformance suite uses unique-path
overwrite because SFTP does not advertise atomic conditional create. Host-key,
credential, passphrase, key, authentication-mode, address, and mounted-root
substitution cases must fail closed without disclosing fixture secrets.

New providers should first join `CL.Storage`, then receive a StorageHub profile
model, secure credential/trust mapping, capability conformance tests, and a
hermetic integration test. A provider is not complete until all four layers are
present.

Trust boundaries are described where they are enforced: the storage contract
and connector sections above for identity and credentials, the transfers and
sync section for destructive work, and the persistence section for durable
state. There is no separate security document; a boundary that is not in the
code and its tests is not a boundary.
