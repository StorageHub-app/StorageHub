using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// What the shell reports when input is rejected or an operation cannot proceed.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
/// </remarks>
[LocalizationSection("validation")]
internal sealed class ValidationStrings : LocalizationModelBase
{
    public string ADestinationFolderOrNonFileItem { get; set; } = "A destination folder or non-file item already uses one of the selected names.";

    public string ADestinationFolderOrNonFileItem2 { get; set; } = "A destination folder or non-file item conflicts with a source file.";

    public string ADestinationIsRequiredForDirectNavigation { get; set; } = "A destination is required for direct navigation.";

    public string AFileCannotBeCopiedOrMoved { get; set; } = "A file cannot be copied or moved onto itself.";

    public string APrivateKeyPassphraseVaultReferenceIs { get; set; } = "A private-key passphrase vault reference is required for SSH multi-factor authentication.";

    public string APrivateKeyPassphraseVaultReferenceIs2 { get; set; } = "A private-key passphrase vault reference is required.";

    public string AGroupNameIsRequired { get; set; } = "A group name is required.";

    public string AProfileNameIsRequired { get; set; } = "A profile name is required.";

    public string ASavedConnectionIDAndVerifiedRoot { get; set; } = "A saved connection ID and verified root identity are required.";

    public string ASavedConnectionPaneMustUseA { get; set; } = "A saved connection pane must use a canonical root-relative path.";

    public string ASelectedItemContainsAnInvalidPath { get; set; } = "A selected item contains an invalid path, kind, length, or provider identity.";

    public string ASelectedItemHasAnInvalidName { get; set; } = "A selected item has an invalid name.";

    public string ASigningRegionIsRequiredForThis { get; set; } = "A signing region is required for this S3-compatible service.";

    public string AUsernameIsRequired { get; set; } = "A username is required.";

    public string AValidLocalAgentPipeNameIs { get; set; } = "A valid local agent pipe name is required.";

    public string AValidatedPaneSnapshotProducedAnInvalid { get; set; } = "A validated pane snapshot produced an invalid transfer request.";

    public string AVaultPasswordReferenceIsRequired { get; set; } = "A vault password reference is required.";

    public string AVerifiedSSHHostKeySHA256 { get; set; } = "A verified SSH host-key SHA-256 fingerprint is required.";

    public string AVerifiedCertificateSHA256PinIs { get; set; } = "A verified certificate SHA-256 pin is required for pinned FTPS.";

    public string AccessDeniedYouDoNotHavePermission { get; set; } = "Access denied. You do not have permission to open this location.";

    public string AlreadyHereLeftAlone { get; set; } = "Already here, left alone";

    public string AlternateWindowsIdentitiesAreNotAvailableUntil { get; set; } = "Alternate Windows identities are not available until a provider-enforced impersonation boundary is implemented.";


    public string AnFTPHostIsRequired { get; set; } = "An FTP host is required.";

    public string AnFTPSHostIsRequired { get; set; } = "An FTPS host is required.";

    public string AnHTTPSS3ServiceEndpointIsRequired { get; set; } = "An HTTPS S3 service endpoint is required.";

    public string AnS3BucketIsRequired { get; set; } = "An S3 bucket is required.";

    public string AnSFTPHostIsRequired { get; set; } = "An SFTP host is required.";

    public string AnSFTPUsernameIsRequired { get; set; } = "An SFTP username is required.";

    public string AnSSHHostIsRequired { get; set; } = "An SSH host is required.";

    public string AnAbsoluteLocalOrUNCRootIs { get; set; } = "An absolute local or UNC root is required.";

    public string AnAccountPasswordVaultReferenceIsRequired { get; set; } = "An account-password vault reference is required for SSH multi-factor authentication.";

    public string AnEncryptedPrivateKeyVaultReferenceIs { get; set; } = "An encrypted private-key vault reference is required for SSH multi-factor authentication.";

    public string AnEncryptedPrivateKeyVaultReferenceIs2 { get; set; } = "An encrypted private-key vault reference is required.";

    public string AtLeastOneUniqueValidAcceptedOr { get; set; } = "At least one unique valid accepted or ambiguous transfer ID is required.";



    public string EnterAFolderPathOrThisPC { get; set; } = "Enter a folder path or 'This PC'.";

    public string EnterAPathRelativeToTheConnection { get; set; } = "Enter a path relative to the connection root.";

    public string EnterAPathRelativeToTheSelected { get; set; } = "Enter a path relative to the selected connection root.";

    public string EnterTheS3ServiceEndpointOnlyWithout { get; set; } = "Enter the S3 service endpoint only, without credentials, query text, fragments, or a bucket path.";


    public string FileFolder { get; set; } = "File folder";

    public string FolderMovesAreNotEnabledYetBecause { get; set; } = "Folder moves are not enabled yet because deleting source directories requires durable child-job dependencies. Copy the folder first.";


    public string LocalDisk { get; set; } = "Local disk";

    public string ManualTransferCurrentlyAcceptsFilesOnlyDirectory { get; set; } = "Manual transfer currently accepts files only; directory recursion is not yet represented safely.";

    public string ManualTransferEnqueueWasCancelledAfterAn { get; set; } = "Manual transfer enqueue was cancelled after an acknowledgement became ambiguous.";

    public string MetadataUnavailable { get; set; } = "Metadata unavailable";

    public string MoreThanOneSelectedFileMapsTo { get; set; } = "More than one selected file maps to the same destination path.";

    public string MovingAFileRequiresItsCapturedVersion { get; set; } = "Moving a file requires its captured version ID or entity tag.";

    public string OneOrMoreConnectionFieldsAreOutside { get; set; } = "One or more connection fields are outside the supported profile bounds.";


    public string ParentTraversalIsNotAllowedInA { get; set; } = "Parent traversal is not allowed in a remote path.";



    public string PlainFTPRequiresExplicitPlaintextTransportAcknowledgemen { get; set; } = "Plain FTP requires explicit plaintext-transport acknowledgement.";



    public string QueueTransfersMoveBetweenThisPCAnd { get; set; } = "Queue transfers move between this PC and a saved connection. Copying between two local folders is not supported yet; use File Explorer for that.";

    public string QueueTransfersRequireTheDestinationPaneTo { get; set; } = "Queue transfers require the destination pane to be a saved connection or an open folder on this PC.";

    public string QueueTransfersRequireTheSourcePaneTo { get; set; } = "Queue transfers require the source pane to be a saved connection or an open folder on this PC.";

    public string ReadOnly { get; set; } = "Read-only";

    public string RecursiveTransfersRequireSavedConnectionsOnBoth { get; set; } = "Recursive transfers require saved connections on both panes.";

    public string RefreshTheQueueUsingTheReturnedTransfer { get; set; } = "Refresh the queue using the returned transfer ID before trying again.";

    public string RefusingToWriteTheExportThroughA { get; set; } = "Refusing to write the export through a reparse point.";

    public string Removable { get; set; } = "Removable";

    public string ReplacingAnExistingFileRequiresCapturedSource { get; set; } = "Replacing an existing file requires captured source and destination version or entity-tag evidence.";

    public string ReplacingAnExistingFileRequiresStableSource { get; set; } = "Replacing an existing file requires stable source and destination identity evidence.";

    public string S3AccessKeyAuthenticationRequiresBothAccess { get; set; } = "S3 access-key authentication requires both access-key and secret-key vault references.";

    public string SSHAgentAuthenticationIsNotAvailableIn { get; set; } = "SSH agent authentication is not available in the current provider adapter.";

    public string SkippedItChangedWhileImporting { get; set; } = "Skipped, it changed while importing";

    public string SkippedItConflictsWithOneAlreadyHere { get; set; } = "Skipped, it conflicts with one already here";

    public string SkippedItIsNotValidHere { get; set; } = "Skipped, it is not valid here";

    public string SkippedItIsRunningNow { get; set; } = "Skipped, it is running now";

    public string SkippedItsConnectionsAreNotHere { get; set; } = "Skipped, its connections are not here";

    public string SkippedItsSyncTaskIsNotHere { get; set; } = "Skipped, its sync task is not here";

    public string SkippedThatNameIsTaken { get; set; } = "Skipped, that name is taken";

    public string SkippedTheAgentWasUnavailable { get; set; } = "Skipped, the agent was unavailable";


    public string StorageHubCouldNotAuthenticateToTheLocal { get; set; } = "StorageHub could not authenticate to the local background agent.";

    public string StorageHubCouldNotBuildTheRecursiveTransfer { get; set; } = "StorageHub could not build the recursive transfer manifest.";

    public string ReadingTheFolderWasStopped { get; set; } =
        "Reading the folder was stopped. The files found before it stopped are still queued.";

    public string TheListingTimeoutMustBeAtMost { get; set; } = "The listing timeout must be at most ten minutes.";

    public string TheRecursiveListingTimedOutFormat { get; set; } =
        "Listing \"{0}\" did not finish in time. The folder is still being read after page {1}; open a smaller folder, or move it in parts.";

    public string StorageHubCouldNotOpenThisLocation { get; set; } = "StorageHub could not open this location.";

    public string StorageHubCouldNotPrepareTheSelectedItems { get; set; } = "StorageHub could not prepare the selected items for Explorer.";

    public string StorageHubCouldNotSaveThatPreferenceSo { get; set; } = "StorageHub could not save that preference, so this warning will appear again next time.";

    public string StorageHubWillNotReadSettingsThroughA { get; set; } = "StorageHub will not read settings through a reparse point.";









    public string ThatFileCouldNotBeOpened { get; set; } = "That file could not be opened.";

    public string ThatFileIsEmpty { get; set; } = "That file is empty.";

    public string ThatFileIsTooLargeToBe { get; set; } = "That file is too large to be a StorageHub settings export.";

    public string ThatFileNoLongerExists { get; set; } = "That file no longer exists.";

    public string ThatFolderNoLongerExistsStorageHubMoved { get; set; } = "That folder no longer exists. StorageHub moved to the nearest available parent.";

    public string TheExplorerDropCommitIsOutsideThe { get; set; } = "The Explorer drop commit is outside the negotiated IPC contract bounds.";

    public string TheExplorerDropRequestIsOutsideThe { get; set; } = "The Explorer drop request is outside the negotiated IPC contract bounds.";

    public string TheS3BucketNameMustBe3 { get; set; } = "The S3 bucket name must be 3-255 characters with no spaces or slashes.";

    public string TheS3ServiceEndpointMustBeA { get; set; } = "The S3 service endpoint must be a valid hostname or absolute HTTPS URL.";

    public string TheS3ServiceEndpointMustUseHTTPS { get; set; } = "The S3 service endpoint must use HTTPS; insecure HTTP endpoints are not enabled.";

    public string TheAddressIsNotAValidFolder { get; set; } = "The address is not a valid folder path.";

    public string TheBackgroundAgentCouldNotEnqueueThe { get; set; } = "The background agent could not enqueue the transfer.";

    public string TheBackgroundAgentDidNotConfirmWhether { get; set; } = "The background agent did not confirm whether the transfer was durably enqueued.";

    public string TheBackgroundAgentDidNotConfirmWhether2 { get; set; } = "The background agent did not confirm whether the transfer was durably enqueued. ";

    public string TheBackgroundAgentDidNotRespondIn { get; set; } = "The background agent did not respond in time.";

    public string TheBackgroundAgentIsNotRunning { get; set; } = "The background agent is not running.";

    public string TheBackgroundAgentIsUnavailable { get; set; } = "The background agent is unavailable.";

    public string TheBackgroundAgentReturnedAnInvalidEnqueue { get; set; } = "The background agent returned an invalid enqueue response.";

    public string TheBackgroundAgentReturnedAnInvalidResponse { get; set; } = "The background agent returned an invalid response.";

    public string TheConnectTimeoutMustBeAtMost { get; set; } = "The connect timeout must be at most 15 seconds.";

    public string TheConnectionRootIdentityChangedWhileThe { get; set; } = "The connection root identity changed while the recursive manifest was being built.";

    public string TheConnectionListRequestIsOutsideThe { get; set; } = "The connection-list request is outside the storage IPC contract bounds.";

    public string TheConnectionTestRequestIsOutsideThe { get; set; } = "The connection-test request is outside the storage IPC contract bounds.";

    public string TheConnectionsHomeContextCannotIdentifyA { get; set; } = "The connections-home context cannot identify a storage path.";

    public string TheCurrentWindowsUserIdentityIsUnavailable { get; set; } = "The current Windows user identity is unavailable.";

    public string TheDestinationEntriesAreDuplicatedOrDo { get; set; } = "The destination entries are duplicated or do not belong to the captured pane location.";

    public string TheDestinationPaneExceedsTheSafeSnapshot { get; set; } = "The destination pane exceeds the safe snapshot limit.";

    public string TheDestinationReturnedAnInvalidRecursiveEntry { get; set; } = "The destination returned an invalid recursive entry.";

    public string TheDestinationReturnedDuplicatedRecursiveEntries { get; set; } = "The destination returned duplicated recursive entries.";

    public string TheEditorReplacedTheTemporaryFileWith { get; set; } = "The editor replaced the temporary file with a link. StorageHub will not upload it.";

    public string TheExportDirectoryIsUnavailable { get; set; } = "The export directory is unavailable.";

    public string TheExportPathMustBeAbsolute { get; set; } = "The export path must be absolute.";

    public string TheExternalEditorTemporaryDirectoryCannotBe { get; set; } = "The external-editor temporary directory cannot be a reparse point.";

    public string TheKeyStoreRequestIsOutsideThe { get; set; } = "The key store request is outside the negotiated IPC contract bounds.";

    public string TheLocalAgentInspectorRequestTimedOut { get; set; } = "The local agent inspector request timed out before it could start.";

    public string TheLocalAgentInspectorRequestTimedOut2 { get; set; } = "The local agent inspector request timed out.";

    public string TheLocalAgentKeyStoreRequestTimed { get; set; } = "The local agent key store request timed out before it could start.";

    public string TheLocalAgentKeyStoreRequestTimed2 { get; set; } = "The local agent key store request timed out.";

    public string TheLocalAgentRejectedTheKeyStore { get; set; } = "The local agent rejected the key store request.";

    public string TheLocalAgentRejectedTheObjectInspector { get; set; } = "The local agent rejected the object inspector request.";

    public string TheLocalAgentRejectedTheScheduleRequest { get; set; } = "The local agent rejected the schedule request.";

    public string TheLocalAgentRejectedTheStorageRequest { get; set; } = "The local agent rejected the storage request.";

    public string TheLocalAgentRejectedTheTransferRequest { get; set; } = "The local agent rejected the transfer request.";

    public string TheLocalAgentRequestTimedOutBefore { get; set; } = "The local agent request timed out before it could start.";

    public string TheLocalAgentRequestTimedOut { get; set; } = "The local agent request timed out.";

    public string TheLocalAgentResponseDidNotMatch { get; set; } = "The local agent response did not match the active request.";

    public string TheLocalAgentReturnedAProfileResponse { get; set; } = "The local agent returned a profile response outside the negotiated bounds.";

    public string TheLocalAgentReturnedASecretResponse { get; set; } = "The local agent returned a secret response outside the negotiated bounds.";

    public string TheLocalAgentReturnedAStorageResponse { get; set; } = "The local agent returned a storage response outside the negotiated bounds.";

    public string TheLocalAgentReturnedATransferResponse { get; set; } = "The local agent returned a transfer response outside the negotiated bounds.";

    public string TheLocalAgentReturnedATrustResponse { get; set; } = "The local agent returned a trust response outside the negotiated bounds.";

    public string TheLocalAgentReturnedAnInvalidResponse { get; set; } = "The local agent returned an invalid response payload.";

    public string TheLocalAgentReturnedAnInvalidTransfer { get; set; } = "The local agent returned an invalid transfer response.";

    public string TheLocalAgentReturnedAnUnexpectedResponse { get; set; } = "The local agent returned an unexpected response type.";

    public string TheLocalAgentReturnedInvalidKeyStore { get; set; } = "The local agent returned invalid key store data.";

    public string TheLocalAgentReturnedInvalidObjectInspector { get; set; } = "The local agent returned invalid object inspector data.";

    public string TheLocalAgentReturnedInvalidScheduleData { get; set; } = "The local agent returned invalid schedule data.";

    public string TheLocalAgentReturnedKeyStoreData { get; set; } = "The local agent returned key store data outside the negotiated bounds.";

    public string TheLocalAgentReturnedObjectInspectorData { get; set; } = "The local agent returned object inspector data outside the negotiated bounds.";

    public string TheLocalAgentReturnedScheduleDataOutside { get; set; } = "The local agent returned schedule data outside the negotiated bounds.";

    public string TheLocalAgentScheduleRequestTimedOut { get; set; } = "The local agent schedule request timed out.";

    public string TheLocalListingContinuationIsNoLonger { get; set; } = "The local listing continuation is no longer available.";

    public string TheNormalizedRemotePathIsTooLong { get; set; } = "The normalized remote path is too long.";

    public string TheObjectInspectorRequestIsOutsideThe { get; set; } = "The object inspector request is outside the negotiated IPC contract bounds.";

    public string ThePaneContextKindIsInvalid { get; set; } = "The pane context kind is invalid.";

    public string ThePaneLocationIsEmptyTooLong { get; set; } = "The pane location is empty, too long, or contains unsupported characters.";

    public string ThePortMustBeBetween1And { get; set; } = "The port must be between 1 and 65,535.";

    public string TheProfileRequestIsOutsideTheConnection { get; set; } = "The profile request is outside the connection-management contract bounds.";

    public string TheProfileRequestTimedOutBeforeIt { get; set; } = "The profile request timed out before it could start.";

    public string TheProfileRequestTimedOut { get; set; } = "The profile request timed out.";

    public string TheProviderRejectedTheSavedUsernameOr { get; set; } = "The provider rejected the saved username or credential.";

    public string TheProviderRepeatedARecursiveListingPage { get; set; } = "The provider repeated a recursive listing page token.";

    public string TheProviderReturnedAnInconsistentNextPage { get; set; } = "The provider returned an inconsistent next page.";

    public string TheProviderReturnedAnInvalidListing { get; set; } = "The provider returned an invalid listing.";

    public string TheRecursiveListingExceededItsBoundedPage { get; set; } = "The recursive listing exceeded its bounded page limit.";

    public string TheRecursiveManifestMappedAFileAnd { get; set; } = "The recursive manifest mapped a file and folder to the same destination path.";

    public string TheRecursiveManifestProducedAnInvalidOr { get; set; } = "The recursive manifest produced an invalid or self-referencing transfer.";

    public string TheRemoteFolderOrSavedConnectionWas { get; set; } = "The remote folder or saved connection was not found.";

    public string TheRemoteLocationCouldNotBeOpened { get; set; } = "The remote location could not be opened.";

    public string TheRemotePathIsTooLongOr { get; set; } = "The remote path is too long or contains unsupported characters.";

    public string TheRemotePathOrConnectionSettingsAre { get; set; } = "The remote path or connection settings are invalid.";

    public string TheRemoteProviderDidNotRespondIn { get; set; } = "The remote provider did not respond in time.";

    public string TheRemoteProviderIsTemporarilyUnavailable { get; set; } = "The remote provider is temporarily unavailable.";

    public string TheRemoteRequestWasCancelled { get; set; } = "The remote request was cancelled.";

    public string TheRequestTimeoutMustBeAtMost { get; set; } = "The request timeout must be at most one minute.";

    public string TheRequestTimeoutMustBeAtMost2 { get; set; } = "The request timeout must be at most two minutes.";

    public string TheSavedConnectionIsNoLongerAvailable { get; set; } = "The saved connection is no longer available.";

    public string TheSavedServerIdentityTrustDecisionIs { get; set; } = "The saved server-identity trust decision is missing or no longer valid.";

    public string TheScheduleRequestIsOutsideTheNegotiated { get; set; } = "The schedule request is outside the negotiated IPC contract bounds.";

    public string TheSecretRequestIsOutsideTheSecret { get; set; } = "The secret request is outside the secret IPC contract bounds.";

    public string TheSecretRequestTimedOutBeforeIt { get; set; } = "The secret request timed out before it could start.";

    public string TheSecretRequestTimedOut { get; set; } = "The secret request timed out.";

    public string TheSelectedFilesDoNotProduceUnique { get; set; } = "The selected files do not produce unique canonical destination paths.";

    public string TheSelectedFolderContainsASymbolicLink { get; set; } = "The selected folder contains a symbolic link or provider item that cannot be transferred safely.";

    public string TheSelectedItemsAreDuplicatedOrDo { get; set; } = "The selected items are duplicated or do not belong to the captured pane location.";

    public string TheSelectedUpdateChangedWhileDownloading { get; set; } = "The selected update changed while downloading.";

    public string TheServerIdentityFingerprintMustBeSHA { get; set; } = "The server identity fingerprint must be SHA-256 hexadecimal or SHA256 base64.";

    public string TheShellExportRequestIsOutsideThe { get; set; } = "The shell export request is outside the negotiated IPC contract bounds.";

    public string TheShellImportCommitIsOutsideThe { get; set; } = "The shell import commit is outside the negotiated IPC contract bounds.";

    public string TheShellImportPlanIsOutsideThe { get; set; } = "The shell import plan is outside the negotiated IPC contract bounds.";

    public string TheSourceReturnedADuplicatedRecursiveEntry { get; set; } = "The source returned a duplicated recursive entry.";

    public string TheSourceReturnedAFileAndFolder { get; set; } = "The source returned a file and folder with the same path.";

    public string TheSourceReturnedAnEntryOutsideThe { get; set; } = "The source returned an entry outside the selected folder.";

    public string TheStorageListRequestIsOutsideThe { get; set; } = "The storage-list request is outside the storage IPC contract bounds.";

    public string TheTransferOperationVerificationPolicyOrPriority { get; set; } = "The transfer operation, verification policy, or priority is invalid.";

    public string TheTransferRequestIsOutsideTheNegotiated { get; set; } = "The transfer request is outside the negotiated IPC contract bounds.";

    public string TheTrustRequestIsOutsideTheConnection { get; set; } = "The trust request is outside the connection-trust contract bounds.";

    public string TheUpdateCandidateDidNotOriginateFrom { get; set; } = "The update candidate did not come from the update feed.";

    public string TheseSettingsAreTooLargeToExport { get; set; } = "These settings are too large to export.";

    public string ThisPCConnectionHomeAndAdHoc { get; set; } = "This PC, connection-home, and ad-hoc panes cannot claim a saved connection identity.";

    public string ThisFileNeedsAPassword { get; set; } = "This file needs a password.";

    public string ThisFolderPathIsTooLongFor { get; set; } = "This folder path is too long for Windows to open.";

    public string ThisInspectorClientDoesNotSupportBounded { get; set; } = "This inspector client does not support bounded editor downloads.";

    public string ThisInspectorClientDoesNotSupportBounded2 { get; set; } = "This inspector client does not support bounded editor uploads.";

    public string ThisInspectorClientDoesNotSupportDeleting { get; set; } = "This inspector client does not support deleting storage items.";

    public string ThisInspectorClientDoesNotSupportDirectory { get; set; } = "This inspector client does not support directory creation.";

    public string ThisInspectorClientDoesNotSupportEmpty { get; set; } = "This inspector client does not support empty-file creation.";

    public string ThisInspectorClientDoesNotSupportExplicit { get; set; } = "This inspector client does not support explicit directory creation.";

    public string ThisInspectorClientDoesNotSupportRenaming { get; set; } = "This inspector client does not support renaming storage items.";

    public string ThisLocationCouldNotBeFoundIt { get; set; } = "This location could not be found. It may have moved or been disconnected.";

    public string ThisProfileClientDoesNotSupportSSH { get; set; } = "This profile client does not support SSH host-key discovery.";

    public string ThisProviderDoesNotSupportTheRequested { get; set; } = "This provider does not support the requested browse operation.";

    public string ThisTransferClientDoesNotSupportExplorer { get; set; } = "This transfer client does not support Explorer drops.";

    public string ThisTransferClientDoesNotSupportClearing { get; set; } = "This transfer client does not support clearing history.";

    public string ThisTransferClientDoesNotSupportShell { get; set; } = "This transfer client does not support shell exports.";

    public string ThisTransferClientDoesNotSupportShell2 { get; set; } = "This transfer client does not support shell imports.";

    public string Unavailable { get; set; } = "Unavailable";

    public string UnknownNavigationKind { get; set; } = "Unknown navigation kind.";

    public string UnknownRemoteNavigationKind { get; set; } = "Unknown remote navigation kind.";

    public string UpdatesAutomaticChecksOff { get; set; } = "Updates: automatic checks off";

    public string UpdatesCheckFailedTryAgainLater { get; set; } = "Updates: check failed; try again later";

    public string UpdatesCheckingGitHub { get; set; } = "Updates: checking…";

    public string UpdatesCouldNotStartTheInstaller { get; set; } = "Updates: could not start the installer";

    public string UpdatesDownloadFailedTryAgainLater { get; set; } = "Updates: download failed; try again later";

    public string UpdatesIdle { get; set; } = "Updates: idle";

    public string UpdatesInstallStorageHubToEnable { get; set; } = "Updates: install StorageHub to enable";


    public string WindowsCouldNotReadThisLocationIt { get; set; } = "Windows could not read this location. It may be disconnected or in use.";

    public string WindowsCouldNotStartTheConfiguredEditor { get; set; } = "Windows could not start the configured editor.";

    // ------------------------------------------------------------- item names
    public string EnterAName { get; set; } = "Enter a name.";

    public string NamesCannotBeginOrEndWithSpaces { get; set; } =
        "Names cannot begin or end with spaces, or end with a period.";

    /// <summary>{0} = the maximum number of characters a name may have.</summary>
    public string NamesCannotExceedCharactersFormat { get; set; } =
        "Names cannot exceed {0:N0} characters.";

    public string TheNameContainsCharactersThatAreNot { get; set; } =
        "The name contains characters that are not portable across storage providers.";

    public string ThatNameIsReservedByWindows { get; set; } = "That name is reserved by Windows.";

    // ----------------------------------------------------------- batch rename
    public string TwoSelectedItemsWouldReceiveTheSame { get; set; } =
        "Two selected items would receive the same name.";

    /// <summary>{0} = the name that is already taken.</summary>
    public string AnItemNamedAlreadyExistsFormat { get; set; } =
        "An item named \u2018{0}\u2019 already exists.";

    public string ATargetNameCollidesWithAnother { get; set; } =
        "A target name collides with another selected item. Rename those items separately.";

    public string EnterTextToFindInTheSelected { get; set; } =
        "Enter text to find in the selected names.";

    public string NoneOfTheSelectedNamesContain { get; set; } =
        "None of the selected names contain that text.";

    // --------------------------------------------------------------- shortcuts
    /// <summary>{0} = the command the shortcut would be assigned to.</summary>
    public string ChooseCtrlAltWithAKeyFormat { get; set; } =
        "Choose Ctrl/Alt with a key, a function key, or Delete for {0}.";

    /// <summary>{0} = the shortcut, {1} = the command that already uses it.</summary>
    public string IsAlreadyAssignedToClearThatFormat { get; set; } =
        "{0} is already assigned to {1}. Clear that assignment first.";
}
