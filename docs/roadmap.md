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

- [x] Test lab on a pullable MinIO image (`pgsty/minio`, pinned), packages at CodeLogic.Storage 4.8.95
      and CodeLogic 4.8.20, lock files regenerated, tests and acceptance checks green.

## L. Look parity with 1.4 (before 1.5)

An audit on 2026-09-26 put the Avalonia shots beside `docs/ui-reference/`. The welcome, sync tasks, new
workspace, connection picker and queue still read as 1.4. The pane, the connection editor, settings and
the sidebar don't, and the chrome shared by every screen adds to it. 2.0 is meant to be 1.4 plus
improvements, not a new design, so this comes ahead of the rest of section 1. The new fields from 1.2–1.4
(speed limits, proxy, FTP options) stay; they are filed where 1.4 would have put them.

An item is done when its screen, photographed at 1.25x in both light and dark, reads as the matching
reference shot.

- [x] L.1 Shared chrome (every screen): flat toolbar and queue buttons that dim rather than fill when
      unavailable; column headings in the regular weight, on a band in the palette, with rows as
      dense as 1.4's (Fluent's `TableViewRowPadding` made each row 37 tall); boxed tabs with the accent
      along the top. Text size was a false alarm: body text is 12 px like 1.4's 9 pt Segoe UI, and the
      "1.25x" shell shots lay out a larger window at 1.0 rather than scaling, which makes text look
      small beside a 125% reference.
- [ ] L.8 Workspace tabs: Save, Save As and Open workspace have no handlers in 2.0, and the tab's `*`
      for unsaved changes comes with them. Close is done: the X on a workspace tab and
      Workspace > Close Workspace both close it and release its panes' connections.
- [ ] L.2 The file pane (ref 05). Done apart from the drag-hint row: the chip, the badges, the state
      line, the `FILES` row with its overflow, "Filter:" and the item count. Was: the connection chip with its `STORAGE`/`LOCAL` badges and the
      "● Ready" status line in place of the "Select the profile to connect" drop-down; the labelled
      `FILES` command row (New folder · Copy · Move · Paste · Delete · overflow) in place of the icon
      strip, with Copy/Move/Paste taken out of the bottom-right corner; "Filter:" beside the path box;
      the "N items" footer; the drag-hint row under the tab strip.
- [x] L.3 A new pane opens on This PC with the drives listed, not on an empty `/`. Every pane does:
      1.x opened the second on "Connections Home", a listing of saved connections that 2.0 does not
      have yet (L.9).
- [ ] L.9 Connections Home: a pane pointed at no connection lists the saved ones as rows, as 1.x's
      second pane did, and opening a row opens that connection.
- [ ] L.10 The overview's Agent card still says "Starting" after the status bar has moved to
      "Agent: connected". Seen in the running app on 2026-09-26.
- [ ] L.4 The connection editor (ref 08): back to the dialog shape. Type and Provider / protocol
      drop-downs in a fixed header with the provider's description under them; General,
      Authentication and TLS / SSH Trust tabs; each description under its control rather than under
      its label; a red asterisk on required fields; icon and colour swatches; the badge preview; the
      "Loaded version N" status in the footer. Proxy, speed limits and FTP Advanced go into the tabs.
- [ ] L.5 Settings pages (ref 02): Shortcuts, Connections & trust (per-provider defaults under a
      `STORAGE` caption, SSH Terminal under `CLIENTS`) and Background agent come back. Performance and
      Confirmations fold back into "Transfers & sync".
- [ ] L.6 Settings rows (ref 02): rows grouped in one card under capitalised captions (`CONCURRENCY`,
      `CONFIRMATIONS`) instead of a card each; units inside number fields ("4 jobs"); buttons ordered
      Apply / Cancel / OK.
- [ ] L.7 The connections sidebar (ref 09): coloured rounded icon tiles in place of the
      `STORAGE`/`CLIENT` text badges; tag chips under each card; a Favorites section above the
      storage groups; inline edit and delete on the selected card.

## 1. Settings that are refused today start working

The profile already stores these; `CodeLogicConnectionProfileConnector.BuildAsync` refuses them.

- [x] 1.1 Transfer rate and ETA: library `Progress` through `TransferProgress` and IPC to the queue's
      progress column and the status bar (`TransferBytesPerSecond`).
- [x] 1.2 Speed limits: per-connection bandwidth to `TransferLimits`, the global limit to `MaxTotal*`;
      the "Speed Limits…" command gets its dialog.
- [x] 1.3 Proxy: `StorageProxyConfig` for FTP, SFTP and S3; a proxy section in the connection editor.
- [x] 1.4 FTP options: separate connect and read timeouts, filename encoding (code pages), server time
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
