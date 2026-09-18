using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The prompts and confirmations the shell raises, and the shared button captions.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
///
/// A property whose name ends in <c>Format</c> takes arguments, and its placeholders are numbered
/// so that a translator can reorder them. Pass them through <see cref="Ui.Format"/>, which is the
/// only formatter the shell uses.
/// </remarks>
[LocalizationSection("dialogs")]
internal sealed class DialogStrings : LocalizationModelBase
{
    // ---------------------------------------------------------------- shared
    public string ButtonOk { get; set; } = "OK";

    public string ButtonCancel { get; set; } = "Cancel";

    public string ButtonApply { get; set; } = "Apply";

    public string ButtonClose { get; set; } = "Close";

    // ------------------------------------------------------------- workspaces
    public string ExitCaption { get; set; } = "Exit StorageHub";

    public string CloseWorkspaceCaption { get; set; } = "Close Workspace";

    /// <summary>{0} = the workspace name.</summary>
    public string SaveWorkspaceChangesPromptFormat { get; set; } = "Save changes to {0}?";

    public string OpenWorkspaceCaption { get; set; } = "Open Workspace";

    /// <summary>{0} = the underlying error message.</summary>
    public string OpenWorkspaceFailedFormat { get; set; } =
        "StorageHub could not open this workspace. {0}";

    /// <summary>{0} = the workspace path.</summary>
    public string WorkspaceMissingPromptFormat { get; set; } =
        "StorageHub could not find this workspace.\n\n{0}\n\nRemove it from the pinned and recent lists?";

    public string SaveWorkspaceCaption { get; set; } = "Save Workspace";

    /// <summary>{0} = the underlying error message.</summary>
    public string SaveWorkspaceFailedFormat { get; set; } =
        "StorageHub could not save this workspace. {0}";

    // --------------------------------------------------------------- about
    public string AboutCaption { get; set; } = "About StorageHub";

    /// <summary>{0} = the application version.</summary>
    public string AboutBodyFormat { get; set; } =
        "StorageHub {0}\nOpen-source secure storage manager\nPowered by CodeLogic and CL.Storage";

    // ------------------------------------------------------------- transfers
    /// <summary>Named in the confirmation below, in capitals, so the consequence is unmissable.</summary>
    public string TransferOperationMove { get; set; } = "MOVE";

    public string TransferOperationCopy { get; set; } = "COPY";

    /// <summary>{0} = the operation, already localized and lowercased by the caller.</summary>
    public string ReviewTransferCaptionFormat { get; set; } = "Review {0}";

    /// <summary>{0} = operation, {1} = what is moving, {2} = source, {3} = destination, {4} = the note below.</summary>
    public string ReviewTransferBodyFormat { get; set; } =
        "You are about to {0} {1}.\n\nFrom: {2}\nTo: {3}\n\n{4}";

    public string TransferOriginalsRemoved { get; set; } =
        "The originals are removed after the transfer completes successfully.";

    public string TransferOriginalsRemain { get; set; } = "The originals will remain in place.";

    /// <summary>{0} = the number of items.</summary>
    public string SelectedItemsFormat { get; set; } = "{0:N0} selected items";

    public string TransferQueueCaption { get; set; } = "Transfer queue";

    // -------------------------------------------------------- explorer import
    public string ImportFromExplorerCaption { get; set; } = "Import from Explorer";

    /// <summary>{0} = the number of files.</summary>
    public string ImportFromExplorerPromptFormat { get; set; } =
        "Import {0:N0} file(s) into this saved connection?";

    public string ImportConflictsCaption { get; set; } = "Import conflicts";

    /// <summary>{0} = the number of conflicting files.</summary>
    public string ImportConflictsPromptFormat { get; set; } =
        "{0:N0} file(s) conflict at the destination.\n\nYes replaces conflicting files. No skips them. Cancel stops the import.";

    // -------------------------------------------------------- object inspector
    public string ObjectInspectorCaption { get; set; } = "Object inspector";

    // -------------------------------------------------------- external editor
    public string ExternalEditorCaption { get; set; } = "External editor";

    /// <summary>{0} = the underlying error message.</summary>
    public string ExternalEditorOpenFailedFormat { get; set; } =
        "StorageHub could not open the external editor. {0}";

    /// <summary>{0} = the configured limit in KiB.</summary>
    public string ExternalEditorTooLargeFormat { get; set; } =
        "The edited file is larger than the configured {0:N0} KiB limit and cannot be uploaded.";

    public string UploadEditedFileCaption { get; set; } = "Upload edited file";

    /// <summary>{0} = the file name.</summary>
    public string UploadEditedFilePromptFormat { get; set; } =
        "'{0}' changed in the external editor. Upload it back to the remote connection?";

    /// <summary>{0} = the underlying error message.</summary>
    public string ExternalEditorProcessFailedFormat { get; set; } =
        "StorageHub could not process the edited file. {0}";

    public string ExternalEditorChooseExecutable { get; set; } =
        "Choose an existing editor executable that is not a symbolic link or reparse point.";

    // ------------------------------------------------------------ connections
    public string FetchHostKeyCaption { get; set; } = "Fetch SSH host key";

    /// <summary>{0} = the endpoint.</summary>
    public string FetchHostKeyPromptFormat { get; set; } =
        "Fetch the SSH host key currently presented by {0}?\n\nFetching contacts the endpoint but does not trust it.";

    public string VerifyHostKeyCaption { get; set; } = "Verify SSH host key";

    /// <summary>{0} = the key algorithm, {1} = the SHA-256 fingerprint.</summary>
    public string VerifyHostKeyPromptFormat { get; set; } =
        "The endpoint presented this host key:\n\nAlgorithm: {0}\nFingerprint: {1}\n\nFetching does not prove the server is genuine. Compare this SHA-256 fingerprint with one obtained through a separate trusted channel. Use it only after that comparison succeeds.";

    public string DeleteVaultSecretCaption { get; set; } = "Delete vault secret";

    public string DeleteVaultSecretPrompt { get; set; } =
        "Permanently delete this vault secret? Any other profile that still references it will stop working. This action cannot be undone.";

    public string RejectServerIdentityCaption { get; set; } = "Reject server identity";

    public string RejectServerIdentityPrompt { get; set; } =
        "Record this exact server identity as rejected? Connections will continue to fail closed unless a different verified identity is trusted.";

    // -------------------------------------------------------------- key store
    public string DeleteStoredKeyCaption { get; set; } = "Delete stored key";

    /// <summary>{0} = the display name of the stored key.</summary>
    public string DeleteStoredKeyPromptFormat { get; set; } =
        "Permanently delete '{0}'? The stored material cannot be recovered.";

    public string DefaultSshPrivateKeyCaption { get; set; } = "Default SSH private key";

    public string DefaultSshPrivateKeyImportFailed { get; set; } =
        "The SSH private key could not be imported into the vault.";

    public string DefaultSshPrivateKeyVaultFailed { get; set; } =
        "StorageHub could not import that private key into the encrypted vault.";

    // -------------------------------------------------------------- schedules
    public string DeleteScheduleCaption { get; set; } = "Delete schedule";

    public string DeleteSchedulePrompt { get; set; } =
        "Delete this schedule? Existing run history is not deleted.";

    // ------------------------------------------------------------------- sync
    public string ApproveSyncCaption { get; set; } = "Approve and dispatch sync";

    public string ApproveSyncPrompt { get; set; } =
        "Approve this exact immutable plan and durably dispatch its apply request? This does not mean provider execution has completed.";

    // --------------------------------------------------------------- settings
    public string SettingsCaption { get; set; } = "Settings";

    public string SettingsSaveFailed { get; set; } =
        "StorageHub could not save settings. Your previous settings are unchanged.";

    public string ExportSettingsCaption { get; set; } = "Export Settings";

    /// <summary>{0} = the underlying error message.</summary>
    public string ExportWriteFailedFormat { get; set; } =
        "StorageHub could not write the export. {0}";

    /// <summary>{0} = the underlying error message.</summary>
    public string ExportReadFailedFormat { get; set; } =
        "StorageHub could not read your connections and tasks. {0}";

    public string BackupLocationCaption { get; set; } = "Backup location";

    // ------------------------------------------------------------- deletions
    public string DeleteConnectionCaption { get; set; } = "Delete saved connection";

    /// <summary>{0} = the connection's display name.</summary>
    public string DeleteConnectionPromptFormat { get; set; } =
        "Delete '{0}'? Queued work will keep its immutable history, but the connection can no longer be opened.";

    // ----------------------------------------------------------------- startup
    public string StartupCheckCaption { get; set; } = "StorageHub startup check";

    public string AgentMissingExecutable { get; set; } =
        "The StorageHub background agent is missing. Repair or reinstall StorageHub, then try again.";

    public string AgentStartupTimedOut { get; set; } =
        "The StorageHub background agent did not become ready in time. Close any stuck StorageHub processes and try again.";

    public string AgentLaunchFailed { get; set; } =
        "The StorageHub background agent could not be started. Check Windows security settings and the StorageHub installation, then try again.";

    public string AgentNotReady { get; set; } =
        "The StorageHub background agent is not ready. Try starting StorageHub again.";
    /// <summary>Shown once when an older settings file has been carried over. {0} is the name the old file now has.</summary>
    public string SettingsMigratedFormat { get; set; } = "Your settings were carried over. The old file was kept as {0}.";

    /// <summary>Shown when a settings file could not be trusted. {0} is the name it was moved to.</summary>
    public string SettingsQuarantinedFormat { get; set; } = "A settings file could not be read and was set aside as {0}. Those settings are back at their defaults.";

    public string SettingsRepaired { get; set; } = "Some settings were outside their limits and have been corrected.";

    // --------------------------------------------------------- review delete
    public string ReviewDeleteCaption { get; set; } = "Review delete";

    public string DeleteReviewAccessibleName { get; set; } = "Delete review";

    /// <summary>{0} = how many items are selected.</summary>
    public string DeleteItemsPromptFormat { get; set; } = "Delete {0:N0} selected item(s)?";

    /// <summary>{0} = one item's name, listed as a bullet in the preview.</summary>
    public string DeletePreviewItemFormat { get; set; } = "• {0}";

    /// <summary>{0} = how many further items the preview does not list.</summary>
    public string DeletePreviewMoreFormat { get; set; } = "• …and {0:N0} more";

    public string DeleteLocalToRecycleBin { get; set; } =
        "Local items will be sent to the Windows Recycle Bin.";

    public string DeleteRemotePermanent { get; set; } =
        "Remote deletion may be permanent and cannot be undone.";

    public string ButtonDelete { get; set; } = "Delete";

    public string DontShowWarningAgain { get; set; } = "Don't show this warning again";

    public string DontShowDeleteWarningAgainAccessibleName { get; set; } =
        "Don't show delete warning again";

    // ----------------------------------------------- unprotected external edit
    public string UnsafeExternalEditCaption { get; set; } = "Unprotected external editing";

    public string UnsafeExternalEditAccessibleName { get; set; } =
        "Unprotected external editing warning";

    /// <summary>{0} = the file name.</summary>
    public string UnsafeExternalEditBodyFormat { get; set; } =
        "StorageHub cannot protect '{0}' with a remote version or ETag check.\n\nIf you continue, the file will be downloaded and uploaded without change protection. A newer remote version could be overwritten.";

    public string WarningAccessibleName { get; set; } = "Warning";

    public string RestoreWarningInSettingsHint { get; set; } =
        "You can restore this warning later in Settings under Editing.";

    public string ButtonContinueAnyway { get; set; } = "Continue anyway";

    // ------------------------------------------------------------ language change
    public string LanguageRestartCaption { get; set; } = "Change language";

    /// <summary>
    /// {0} = the chosen language, named in itself — the same way the settings list names it, so
    /// someone who has landed in a language they cannot read still recognizes their own.
    /// </summary>
    public string LanguageRestartPromptFormat { get; set; } =
        "StorageHub needs to restart to switch to {0}.\n\nRestart now? Transfers and synchronization keep running in the background agent.";

}
