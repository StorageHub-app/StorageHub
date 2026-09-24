# StorageHub 2.0 roadmap

What is left before 2.0 ships, in order. Each numbered item is one commit (or a few), ticked off when it
lands. The Avalonia port's screens 1–9 are done; this picks up from there.

**Ground rules**

- CodeLogic.Storage is frozen at 4.8.95. It moves bytes; StorageHub keeps its own transfer queue, sync
  engine and SQLite state. A gap in the library is worked around in `src/StorageHub.Storage.CodeLogic`,
  not fixed upstream.
- `eng/cl-storage-acceptance` is run whenever the library version changes.
- Every commit: `dotnet test StorageHub.slnx` on Windows with the lab up (`eng/testlab`), then the Linux
  run in WSL. Changed windows are photographed (`STORAGEHUB_SHOT_DIR`).

## 0. CodeLogic.Storage 4.8.95

- [ ] Test lab on a pullable MinIO image (`pgsty/minio`, pinned), packages at CodeLogic.Storage 4.8.95
      and CodeLogic 4.8.20, lock files regenerated, tests and acceptance checks green.

## 1. Settings that are refused today start working

The profile already stores these; `CodeLogicConnectionProfileConnector.BuildAsync` refuses them.

- [ ] 1.1 Transfer rate and ETA: library `Progress` through `TransferProgress` and IPC to the queue's
      progress column and the status bar (`TransferBytesPerSecond`).
- [ ] 1.2 Speed limits: per-connection bandwidth to `TransferLimits`, the global limit to `MaxTotal*`;
      the "Speed Limits…" command gets its dialog.
- [ ] 1.3 Proxy: `StorageProxyConfig` for FTP, SFTP and S3; a proxy section in the connection editor.
- [ ] 1.4 FTP options: separate connect and read timeouts, filename encoding (code pages), server time
      zone, listing parser, active mode; an "Advanced" expander in the editor.
- [ ] 1.5 SFTP options: keyboard-interactive login, several keys and inline keys (no temp key file),
      jump host, algorithm lists, session limits.
- [ ] 1.6 Conflict choices on transfers: overwrite, skip, if newer, if size differs, rename, resume.
- [ ] 1.7 Resume: real resume for uploads and downloads in place of the `ResumeToken => null` stubs.
- [ ] 1.8 Server checksums (`PreferServer`): verify without re-downloading; the inspector shows where a
      checksum came from; the sync scanner uses them.
- [ ] 1.9 Certificate pins for S3.

## 2. Connection test and trust (was port item 10)

- [ ] 2.1 The Test button runs `TestConnectionAsync` on the unsaved draft and shows each step.
- [ ] 2.2 A refused host key or certificate is shown with Trust / Reject (`TrustOrRolloverAsync`,
      `RejectAsync`); trust-on-first-use for SFTP and FTPS stops being refused; the editor keeps the
      host-key fingerprint in the draft.
- [ ] 2.3 A "Connection details" panel from `GetConnectionDiagnosticsAsync`.

## 3. Directory tree (was port item 11)

- [ ] 3.1 The folder tree beside the file panes.

## 4. New abilities in the file panes

- [ ] 4.1 Properties: permissions, owner, link target, dates; a chmod dialog and "Set modified time".
- [ ] 4.2 Free space in each pane's status bar.
- [ ] 4.3 Connection health (retrying, lost, back) on the connection card and in the activity log.
- [ ] 4.4 Auto-refresh of a folder when it changes (polling for remote connections, off by default).
- [ ] 4.5 Compare folders: a two-pane diff from `CompareAsync`; differences copied through our queue.
- [ ] 4.6 Clean up leftover staging files, as a maintenance action.
- [ ] 4.7 (Optional) FTP raw command console.

## 5. New providers

Each goes through the profile model, SQLite, IPC, the connector, capabilities, the editor (fields,
icon, da/de text), settings and import/export, and a lab container with a conformance run.

- [ ] 5.1 WebDAV (lab: `rclone serve webdav`, plus a TLS variant for pins)
- [ ] 5.2 Azure Blob (Azurite)
- [ ] 5.3 Google Cloud Storage (fake-gcs-server)
- [ ] 5.4 Swift (all-in-one container)

## 6. Release

- [ ] 6.1 A full pass of the real app against the lab, with screenshots of every changed window.
- [ ] 6.2 The Linux .deb installed and checked in WSL.
- [ ] 6.3 Release notes and the 1.x upgrade path.
