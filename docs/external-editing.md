# External editing

Configure editors under **Settings > Editing**. The default editor opens files without a matching
file-type rule. Leave the default blank to use the operating system's file association.

To add a rule, enter extensions such as `.txt; .log; .json`, choose the editor executable, and press
**Add or replace**. Rules ignore extension case. Compound extensions work too: `.tar.gz` takes
precedence over `.gz`. Adding an existing extension replaces its editor; Remove deletes its rule.
Apply or OK saves the changes. Rules and executable paths belong to the optional machine-specific
section of a settings export.

Select a remote file and choose **Edit in external editor...** from its context menu or the pane's
file actions. The pane shows a downloading overlay, followed by an opening-editor message. Other
panes remain usable. Once the editor opens, the overlay disappears and StorageHub watches the
private working copy for saved changes.

The default and maximum size is **100 MiB (104,857,600 bytes)**. Settings displays this as 102,400 KiB.
The old 1 MiB default migrates to 100 MiB once; a different existing limit is preserved. After
migration, explicitly choosing 1 MiB keeps that limit. Files that grow past the configured limit
while downloading or while being edited are refused.

Saving a change still asks before uploading it. Uploads retain the original version/ETag conditions
and advance to the identity returned after a successful upload. A remote conflict leaves the local
copy available and reports the failed upload. Providers without conditional editing retain the
existing unsafe-edit warning and its opt-out setting.

Downloads, editor launch, detected saves, upload decisions, completed uploads, stopped monitoring,
and failures appear in the queue's **Logs** tab. These recent entries are held for the desktop
session; the same events are written to the rotating `logs/desktop.log` for later diagnosis. File
contents are never logged.

## Implementation and checks

Large files use a bounded transfer protocol over authenticated IPC. Each chunk is at most 512 KiB;
the normal 8 MiB frame limit is unchanged. The agent keeps at most eight open editing transfers,
checks offsets and declared lengths, and expires abandoned streams after three idle minutes.
Closing or failing an unfinished upload disposes its write handle without committing it.

Coverage includes a 100 MiB download/upload through actual framed serialization, upload preconditions
and updated identities, incomplete and out-of-order uploads, unknown-length size enforcement,
explicit close, rule matching and persistence, configuration migration, pane progress cleanup,
settings controls, activity logging, and existing watcher/conflict behavior.
