using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// Exporting and importing StorageHub settings.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
/// </remarks>
[LocalizationSection("settingstransfer")]
internal sealed class SettingsTransferStrings : LocalizationModelBase
{
    public string ALostPasswordCannotBeRecovered { get; set; } = "A lost password cannot be recovered.";

    public string AgentRestartNotice { get; set; } = "Agent restart notice";

    public string AppearanceTransferAndSyncConcurrencyEditingLimits { get; set; } = "Appearance, transfer and sync concurrency, editing limits, update checks, and workspace defaults.";

    public string Cancel { get; set; } = "Cancel";

    public string ChooseAtLeastOneThingToExport { get; set; } = "Choose at least one thing to export.";

    public string ChooseWhatToInclude { get; set; } = "Choose what to include";

    public string ChooseWhichSettingsToWriteToA { get; set; } = "Choose which settings to write to a file.";

    public string CollectingSettings { get; set; } = "Collecting settings...";

    public string Confirm { get; set; } = "Confirm";

    public string ConfirmExportPassword { get; set; } = "Confirm export password";

    public string ConnectionDefaults { get; set; } = "Connection defaults";

    public string ConnectionProfiles { get; set; } = "Connection profiles";

    public string Connections { get; set; } = "Connections";

    public string ConnectionsAndTasksThatAlreadyExistHere { get; set; } = "Connections and tasks that already exist here:";

    public string DesktopPreferences { get; set; } = "Desktop preferences";

    public string EachEntryIsDerivedFromItsKey { get; set; } = "Each entry is derived from its key material, which stays on this computer.";

    public string EditorPathPinnedAndRecentWorkspacesAnd { get; set; } = "Editor path, pinned and recent workspaces, and the private key chosen in connection ";

    public string EnterTheFileSPassword { get; set; } = "Enter the file's password:";

    public string EveryRebindableCommandShortcut { get; set; } = "Every rebindable command shortcut.";

    public string ExportSettings { get; set; } = "Export Settings";

    public string ExportPassword { get; set; } = "Export password";

    public string ExportSettings2 { get; set; } = "Export settings";

    public string ExportStatus { get; set; } = "Export status";

    public string Export { get; set; } = "Export...";

    public string FilePassword { get; set; } = "File password";

    public string FileSummary { get; set; } = "File summary";

    public string HostTrustDecisions { get; set; } = "Host trust decisions";

    public string Import { get; set; } = "Import";

    public string ImportSettings { get; set; } = "Import Settings";

    public string ImportResult { get; set; } = "Import result";

    public string ImportSettings2 { get; set; } = "Import settings";

    public string Imported { get; set; } = "Imported:";

    public string KeepBothImportingAsACopy { get; set; } = "Keep both, importing as a copy";

    public string KeyStoreEntries { get; set; } = "Key store entries";

    public string KeyboardShortcuts { get; set; } = "Keyboard shortcuts";

    public string LeaveWhatIsAlreadyHereAlone { get; set; } = "Leave what is already here alone";

    public string NeverIncluded { get; set; } = "Never included";

    public string NotImported { get; set; } = "Not imported:";

    public string NotInThisFile { get; set; } = "Not in this file.";

    public string NothingWasImported { get; set; } = "Nothing was imported.";

    public string Password { get; set; } = "Password";

    public string ProtectTheExportWithAPassword { get; set; } = "Protect the export with a password";

    public string ProtectThisFileWithAPassword { get; set; } = "Protect this file with a password";

    public string ReplaceWhatIsAlreadyHere { get; set; } = "Replace what is already here";

    public string ReplacesAppearanceConcurrencyEditingAndUpdatePreferences { get; set; } = "Replaces appearance, concurrency, editing and update preferences.";

    public string ReplacesTheEditorPathAndThePinned { get; set; } = "Replaces the editor path and the pinned and recent workspaces.";

    public string ReviewASettingsFileAndChooseWhat { get; set; } = "Review a settings file and choose what to apply.";

    public string SavedConnectionsWithoutTheirCredentialsWhichNever { get; set; } = "Saved connections without their credentials, which never leave this computer.";

    public string SavedPasswordsAndPrivateKeys { get; set; } = "Saved passwords and private keys";

    public string Schedules { get; set; } = "Schedules";

    public string SelectedFile { get; set; } = "Selected file";

    public string SettingsFileType { get; set; } = "StorageHub settings";

    public string ShowTheBackupFile { get; set; } = "Show the backup file";

    public string ShowTheBackupOfYourPreviousSettings { get; set; } = "Show the backup of your previous settings";

    public string SyncTaskDefinitionsIncludingTheirFiltersAnd { get; set; } = "Sync task definitions, including their filters and safety limits.";

    public string SyncTasks { get; set; } = "Sync tasks";

    public string ThatFileCouldNotBeOpened { get; set; } = "That file could not be opened.";

    public string ThatFileCouldNotBeRead { get; set; } = "That file could not be read.";

    public string ThePerProviderTimeoutsPortsAndOptions { get; set; } = "The per-provider timeouts, ports, and options new connections start from.";

    public string TheTwoPasswordsDoNotMatch { get; set; } = "The two passwords do not match.";

    public string TheseConnectionsNeedTheirCredentialsEnteredBefore { get; set; } = "These connections need their credentials entered before they will work:";

    public string TheseRecordWhatYouVerifiedOnThis { get; set; } = "These record what you verified on this computer, so they are not copied elsewhere.";

    public string TheyAreHeldForThisWindowsAccount { get; set; } = "They are held for this Windows account only and cannot be read back, even by StorageHub.";

    public string ThisComputerOnly { get; set; } = "This computer only";

    public string ThisFileIsNotPasswordProtected { get; set; } = "This file is not password protected.";

    public string ThisFileIsProtectedWithAPassword { get; set; } = "This file is protected with a password.";

    public string TransferConcurrencyChangedTheBackgroundAgentRestarts { get; set; } = "Transfer concurrency changed. The background agent restarts to pick it up.";

    public string TransferConcurrencyChangesTakeEffectAfterThe { get; set; } = "Transfer concurrency changes take effect after the background agent restarts.";

    public string UnknownSettingsSection { get; set; } = "Unknown settings section.";

    public string WhatToDoAboutConnectionsAndTasks { get; set; } = "What to do about connections and tasks that are already here";

    public string WhenEachSyncTaskRuns { get; set; } = "When each sync task runs.";

    public string WithoutAPasswordTheFileIsReadable { get; set; } = "Without a password the file is readable JSON you can review before sharing. ";

    public string WrittenOnAnotherComputer { get; set; } = "Written on another computer.";

    public string WrittenOnThisComputer { get; set; } = "Written on this computer.";
}
