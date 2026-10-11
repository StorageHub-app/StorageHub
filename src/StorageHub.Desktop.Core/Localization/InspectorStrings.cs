using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The Properties window: an item's attributes, a file's versions, metadata and tags, and the
/// permissions dialog.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
/// </remarks>
[LocalizationSection("inspector")]
internal sealed class InspectorStrings : LocalizationModelBase
{
    public string Current { get; set; } = "Current";

    public string DeleteMarker { get; set; } = "Delete marker";

    public string EntityTag { get; set; } = "Entity tag";

    public string InspectedObjectPath { get; set; } = "Inspected object path";

    public string Latest { get; set; } = "Latest";

    public string LoadMoreVersions { get; set; } = "Load &more versions";

    public string LoadMoreVersions2 { get; set; } = "Load more versions";

    public string LoadTheNextObjectVersionPage { get; set; } = "Load the next object version page";

    public string LoadingTheNextVersionPage { get; set; } = "Loading the next version page…";

    public string LoadingVersionHistoryMetadataAndTags { get; set; } = "Loading version history, metadata, and tags…";

    public string Metadata { get; set; } = "Metadata";

    public string MetadataHasNotBeenLoaded { get; set; } = "Metadata has not been loaded.";

    public string Modified { get; set; } = "Modified";

    public string ModifiedUTC { get; set; } = "Modified (UTC)";

    public string ObjectDetailCategories { get; set; } = "Object detail categories";

    public string ObjectInspectorCommands { get; set; } = "Object inspector commands";

    public string ObjectInspectorStatus { get; set; } = "Object inspector status";

    public string ObjectMetadata { get; set; } = "Object metadata";

    public string ObjectTags { get; set; } = "Object tags";

    public string ObjectVersions { get; set; } = "Object versions";

    public string ReadOnlyObjectVersionsPortableMetadataAnd { get; set; } = "Read-only object versions, portable metadata, and tags from the background agent.";

    public string StorageObjectInspector { get; set; } = "Storage object inspector";

    public string StorageHubCouldNotAuthenticateToTheLocal { get; set; } = "StorageHub could not authenticate to the local background agent.";

    public string TagsHaveNotBeenLoaded { get; set; } = "Tags have not been loaded.";

    public string TheInspectorConnectsToTheBackgroundAgent { get; set; } = "The inspector connects to the background agent when shown.";

    public string TheObjectInspectorCouldNotLoadDetails { get; set; } = "The object inspector could not load details from the background agent.";

    public string TheObjectInspectorRequestTimedOut { get; set; } = "The object inspector request timed out.";

    public string Version { get; set; } = "Version";

    public string VersionID { get; set; } = "Version ID";

    public string VersionHistoryHasNotBeenLoaded { get; set; } = "Version history has not been loaded.";

    public string Versions { get; set; } = "Versions";

    /// <summary>{0} = the connection's id.</summary>
    public string ConnectionIdentityFormat { get; set; } = "Connection {0:D}";

    /// <summary>{0} = how many versions the page holds.</summary>
    public string TabVersionsFormat { get; set; } = "Versions ({0:N0})";

    /// <summary>{0} = how many metadata fields the page holds.</summary>
    public string TabMetadataFormat { get; set; } = "Metadata ({0:N0})";

    /// <summary>{0} = how many tags the page holds.</summary>
    public string TabTagsFormat { get; set; } = "Tags ({0:N0})";

    public string NoVersionsReturned { get; set; } = "No versions were returned.";

    public string NoMetadataReturned { get; set; } = "No metadata fields were returned.";

    public string NoTagsReturned { get; set; } = "No tags were returned.";

    /// <summary>{0} = how many versions were loaded.</summary>
    public string LoadedVersionsFormat { get; set; } = "Loaded {0:N0} version(s).";

    /// <summary>{0} = how many metadata fields were loaded.</summary>
    public string LoadedMetadataFormat { get; set; } = "Loaded {0:N0} metadata field(s).";

    /// <summary>{0} = how many tags were loaded.</summary>
    public string LoadedTagsFormat { get; set; } = "Loaded {0:N0} tag(s).";

    /// <summary>{0} = versions, {1} = metadata fields, {2} = tags.</summary>
    public string LoadedSummaryFormat { get; set; } =
        "Loaded {0:N0} version(s), {1:N0} metadata field(s), and {2:N0} tag(s).";

    /// <summary>{0} = how many of the three sections could not be loaded.</summary>
    public string LoadedWithFailuresFormat { get; set; } = "Object loaded with {0:N0} unavailable detail section(s).";

    public string Size { get; set; } = "Size";

    public string Name { get; set; } = "Name";

    public string Value { get; set; } = "Value";

    public string Tags { get; set; } = "Tags";

    public string Refresh { get; set; } = "Refresh";

    public string Close { get; set; } = "Close";

    public string Yes { get; set; } = "Yes";

    public string No { get; set; } = "No";

    /// <summary>What a cell shows when the provider reported nothing for it.</summary>
    public string NotReported { get; set; } = "—";

    public string TabChecksum { get; set; } = "Checksum";

    public string ChecksumNotLoaded { get; set; } =
        "Ask for the file's SHA-256. The server's own checksum is used when it keeps one; otherwise up to 256 MB of the file is read to compute it.";

    public string GetChecksum { get; set; } = "Get checksum";

    public string ComputeChecksum { get; set; } = "Compute from the file";

    public string ComputeChecksumHint { get; set; } =
        "Reads the file and computes its SHA-256 here, to check what the server reports.";

    public string GettingChecksum { get; set; } = "Getting the checksum...";

    public string ChecksumAlgorithm { get; set; } = "SHA-256";

    public string ChecksumFromServerFormat { get; set; } =
        "Reported by the server for the whole file ({0:N0} bytes); nothing was downloaded. It is the server's word for the bytes, not a check of them.";

    public string ChecksumComputedFormat { get; set; } = "Computed here from the {0:N0} bytes read.";

    public string ObjectChecksum { get; set; } = "Object checksum";

    public string TabGeneral { get; set; } = "General";

    public string GeneralHasNotBeenLoaded { get; set; } = "Properties have not been loaded.";

    /// <summary>{0} = the item's path, possibly shortened from the left.</summary>
    public string PropertiesTitleFormat { get; set; } = "Properties — {0}";

    public string WhatCanBeChangedHere { get; set; } =
        "Versions, metadata and tags are read-only here. Permissions and the modified time can be changed where the connection allows it.";

    public string WhatCanBeChangedNotice { get; set; } = "What can be changed here";

    public string ItemProperties { get; set; } = "Item properties";

    public string Kind { get; set; } = "Kind";

    public string KindFile { get; set; } = "File";

    public string KindFolder { get; set; } = "Folder";

    public string KindLink { get; set; } = "Symbolic link";

    public string KindOther { get; set; } = "Other";

    public string Created { get; set; } = "Created";

    public string Accessed { get; set; } = "Last accessed";

    public string Permissions { get; set; } = "Permissions";

    public string Owner { get; set; } = "Owner";

    public string Group { get; set; } = "Group";

    public string LinkTarget { get; set; } = "Link target";

    /// <summary>{0} = an owner or group name, {1} = its numeric ID.</summary>
    public string PrincipalWithIdFormat { get; set; } = "{0} (ID {1})";

    /// <summary>{0} = this computer's offset from UTC, as UTC+02:00.</summary>
    public string TimesAreLocalFormat { get; set; } = "Times are in this computer's time zone ({0}).";

    public string ChangePermissions { get; set; } = "Change permissions…";

    public string SetModifiedTime { get; set; } = "Set modified time…";

    public string CannotChangePermissions { get; set; } = "This connection cannot change permissions.";

    public string CannotSetModifiedTime { get; set; } = "This connection cannot set the modified time.";

    public string CannotChangeEither { get; set; } =
        "This connection cannot change permissions or set the modified time.";

    public string ChangingPermissions { get; set; } = "Changing permissions…";

    public string SettingModifiedTime { get; set; } = "Setting the modified time…";

    /// <summary>{0} = the new mode, as rwxr-xr-x (0755).</summary>
    public string PermissionsChangedFormat { get; set; } = "Permissions changed to {0}.";

    /// <summary>{0} = the new time, in this computer's time zone.</summary>
    public string ModifiedTimeSetFormat { get; set; } = "Modified time set to {0}.";

    public string LoadingProperties { get; set; } = "Loading properties…";

    public string PropertiesLoaded { get; set; } = "Properties loaded.";

    public string TheAgentCouldNotReadProperties { get; set; } =
        "The background agent could not read this item's properties.";

    public string SetModifiedTimeTitle { get; set; } = "Set modified time";

    /// <summary>{0} = the item's name, {1} = the offset from UTC, {2} = an example time.</summary>
    public string SetModifiedTimeLabelFormat { get; set; } =
        "New modified time for {0} in this computer's time zone ({1}), written as {2}:";

    public string Set { get; set; } = "Set";

    /// <summary>{0} = an example time, as 2026-10-11 14:30:00.</summary>
    public string ModifiedTimeNotUnderstoodFormat { get; set; } =
        "Write the time as year-month-day and 24-hour time, for example {0}.";

    public string ModifiedTimeOutOfRange { get; set; } =
        "The modified time must fall between 1 January 1970 and 7 February 2106.";

    /// <summary>{0} = the item's name.</summary>
    public string PermissionsTitleFormat { get; set; } = "Permissions — {0}";

    /// <summary>{0} = the item's name.</summary>
    public string PermissionsIntroFormat { get; set; } = "Who may read, write and execute {0}:";

    public string Others { get; set; } = "Others";

    public string Read { get; set; } = "Read";

    public string Write { get; set; } = "Write";

    public string Execute { get; set; } = "Execute";

    public string Octal { get; set; } = "Octal";

    public string OctalNotUnderstood { get; set; } = "Write one to four octal digits (0–7), for example 755.";

    public string SpecialBitsAreTheFirstDigit { get; set; } =
        "Set-user-ID, set-group-ID and sticky are the first of four octal digits.";

    public string Apply { get; set; } = "Apply";

    public string PermissionCheckboxes { get; set; } = "Permission checkboxes";
}
