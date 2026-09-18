using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The read-only object inspector: versions, metadata and tags.
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

    public string READONLYVersionPagesMetadataAndTags { get; set; } = "READ ONLY  •  Version pages, metadata, and tags only — no signed links or mutation commands.";

    public string ReadOnlyInspectorSafetyNotice { get; set; } = "Read-only inspector safety notice";

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
}
